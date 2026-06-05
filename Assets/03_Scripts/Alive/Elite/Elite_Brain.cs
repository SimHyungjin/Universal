using MapNav.Ecs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// 장수(엘리트)를 "AI가 조종하는 플레이어블 캐릭터"로 굴리는 두뇌.
// 직접 이동/애니메이션/공격을 구동하지 않고, Elite_AICommandSource에 입력만 흘려보낸다.
// 이동은 MapNavMonoAgent로 경로/방향만 계산(steer-only)하고, 실제 이동·점프·공격·피격은
// Character_ActionHandler/MoveController/AttackController가 처리한다(플레이어와 동일 스택).
[DisallowMultipleComponent]
[RequireComponent(typeof(Elite_Embodiment))]
public sealed class Elite_Brain : LoopMonoBehaviour
{
    private Elite_State _state;
    private SO_Elite_Data _data;
    private SO_Elite_Brain _brain;
    private Character_ActionHandler _actionHandler;
    private Character_AttackController _attackController;
    private Character_ActionHandler _player;
    private Character_Vitals _playerVitals;
    private Elite_AICommandSource _aiCommands;
    private MapNavMonoAgent _navAgent;

    private float _attackCooldownTimer;
    private float _targetRefreshTimer;
    private float _skillThinkTimer;
    private float _comboPressTimer;
    private float _dashThinkTimer;
    private float _strafeTimer;
    private float _targetResolveTimer;
    private bool _hasCachedTarget;
    private Vector3 _cachedTargetPos;
    private int _strafeSign = 1;

    // 최근접 적 잡몹(ECS) 조회용 캐시. AttackHitEmitter.EnsureHitQuery 패턴과 동일.
    private World _cachedWorld;
    private EntityManager _em;
    private EntityQuery _mobQuery;

    public void Bind(Elite_State state)
    {
        _state = state;
        _data = state != null ? state.Data : null;
        _brain = _data != null ? _data.Brain : null;

        _actionHandler = GetComponent<Character_ActionHandler>();
        _attackController = GetComponent<Character_AttackController>();
        _navAgent = GetComponent<MapNavMonoAgent>();

        _aiCommands = GetComponent<Elite_AICommandSource>();
        if (_aiCommands == null)
            _aiCommands = gameObject.AddComponent<Elite_AICommandSource>();
        _actionHandler?.SetCommandSource(_aiCommands);

        // 공격력은 장수 전용 스탯으로 오버라이드(SetPlayerStats 이후에 호출돼야 적용).
        // 전투 스탯(공격력/이동속도)은 캐릭터에서, AI/nav 파라미터(반경/정지거리)는 Elite Stats에서.
        SO_Character_Stats charStats = _data != null && _data.Character != null ? _data.Character.StatsData : null;
        if (_attackController != null && charStats != null)
            _attackController.SetAttackPower(charStats.AttackPower);

        // nav는 경로/방향만 계산하고 transform은 안 건드린다(실제 이동=Character_MoveController).
        if (_navAgent != null)
        {
            _navAgent.SetDrivesTransform(false);
            float navMoveSpeed = charStats != null ? charStats.MoveSpeed : 3.5f;
            float agentRadius = _brain != null ? _brain.AgentRadius : 0.35f;
            float stopDistance = _brain != null ? _brain.StopDistance : 0.08f;
            _navAgent.ConfigureAgent(navMoveSpeed, agentRadius, stopDistance);
        }
    }

    private void Awake()
    {
        _actionHandler = GetComponent<Character_ActionHandler>();
        _navAgent = GetComponent<MapNavMonoAgent>();
        // Bind 이전 첫 Update에서 nav가 transform(높이 스냅)을 건드리지 않도록 즉시 steer-only로 둔다.
        _navAgent?.SetDrivesTransform(false);
    }

    protected override void OnGameUpdate(float gdt)
    {
        base.OnGameUpdate(gdt);

        if (_state == null || !_state.IsAlive || _actionHandler == null || _aiCommands == null)
            return;

        _attackCooldownTimer = Mathf.Max(0f, _attackCooldownTimer - gdt);
        _skillThinkTimer = Mathf.Max(0f, _skillThinkTimer - gdt);
        _comboPressTimer = Mathf.Max(0f, _comboPressTimer - gdt);
        _dashThinkTimer = Mathf.Max(0f, _dashThinkTimer - gdt);
        TickStrafe(gdt);

        TickPlayableCommandAI(gdt);
    }

    // 플레이어와 같은 입력 스택 위에서 보스다운 판단만 얹는다.
    // 거리 유지 → 횡이동 압박 → 콤보 지속 → 스킬/대시 섞기 순서로 "조종"만 한다.
    private void TickPlayableCommandAI(float dt)
    {
        // 매크로가 "이 섹터를 떠나라"고 신호하면(PendingExitSector) 전투를 멈추고 게이트까지 걸어가 통과한다.
        if (_state.PendingExitSector != null)
        {
            TickGateExit(dt);
            return;
        }

        // 자기 진영과 다른 최근접 적대 대상(플레이어/적 장수/적 잡몹)을 노린다.
        if (!TryGetCachedHostileTarget(dt, out Vector3 targetPos))
        {
            StopChase();
            return;
        }

        Vector3 toTarget = targetPos - transform.position;
        toTarget.y = 0f;
        float sqrDistance = toTarget.sqrMagnitude;

        Vector3 straightDir = sqrDistance > 0.0001f ? toTarget.normalized : transform.forward;
        _aiCommands.SetLookWorld(straightDir);

        float attackRange = GetAttackRange();
        float distance = Mathf.Sqrt(sqrDistance);
        float aggression = GetAggression01();
        float preferredRange = attackRange * Mathf.Lerp(GetPreferredRangeRatio(), 0.85f, aggression);
        float closeRange = attackRange * Mathf.Lerp(GetCloseRangeRatio(), 0.35f, aggression);

        if (HoldCommandsDuringSkillSequence())
            return;

        if (_attackController != null && _attackController.IsInCombo)
        {
            StopMovement();
            PressComboAttack();
            return;
        }

        if (TryUseSkill(sqrDistance, attackRange, straightDir))
            return;

        if (TryEmergencyReposition(distance, closeRange, straightDir, aggression))
            return;

        if (sqrDistance <= attackRange * attackRange)
        {
            // 사거리 안: 멈춘 뒤 한 박자씩 때린다. 저체력 페이즈일수록 박자가 짧아진다.
            StopMovement();
            if (_attackCooldownTimer <= 0f)
            {
                _aiCommands.PressAttack();
                _attackCooldownTimer = GetPhaseAttackCooldown(aggression);
                _comboPressTimer = GetComboPressInterval();
            }
            return;
        }

        if (distance <= preferredRange)
        {
            StalkTarget(straightDir, distance, preferredRange, aggression);
            return;
        }

        // 추격: nav로 경로를 만들고 그 방향을 MoveWorld로 흘린다. nav가 transform을 직접 움직이진 않는다.
        ChaseTarget(targetPos, straightDir, dt);
    }

    private bool HoldCommandsDuringSkillSequence()
    {
        if (_attackController == null || !_attackController.IsSkillSequenceActive)
            return false;

        StopMovement();
        _aiCommands.ClearOneShotInputs();
        return true;
    }

    // 자기 진영과 다른 최근접 적대 대상을 찾는다. 후보: 플레이어 + 실체화된 적 장수 + 적 잡몹(ECS).
    // 실제 타격은 AttackHitEmitter(진영 필터 정상)가 처리하므로 여기선 위치만 정한다.
    private bool TryGetCachedHostileTarget(float dt, out Vector3 targetPos)
    {
        _targetResolveTimer -= dt;
        if (_hasCachedTarget && _targetResolveTimer > 0f)
        {
            targetPos = _cachedTargetPos;
            return true;
        }

        _targetResolveTimer = GetTargetResolveInterval();
        if (TryResolveNearestHostile(_state.Faction, out targetPos))
        {
            _cachedTargetPos = targetPos;
            _hasCachedTarget = true;
            return true;
        }

        _hasCachedTarget = false;
        targetPos = default;
        return false;
    }

    private bool TryResolveNearestHostile(NavFaction myFaction, out Vector3 targetPos)
    {
        Vector3 self = transform.position;
        float aggroSqr = GetAggroRangeSqr();
        float bestInsideSqr = float.MaxValue;
        float bestAnySqr = float.MaxValue;
        Vector3 bestInsidePos = default;
        Vector3 bestAnyPos = default;
        bool foundInside = false;
        bool foundAny = false;
        targetPos = default;

        // 1) 플레이어 — 진영이 다를 때만(아군 장수는 플레이어를 무시).
        Character_ActionHandler player = ResolvePlayer();
        if (player != null && player.State != Character_ActionState.Dead
            && _playerVitals != null && _playerVitals.Faction != myFaction)
            ConsiderCandidate(player.transform.position, self, aggroSqr,
                ref bestInsideSqr, ref bestInsidePos, ref foundInside,
                ref bestAnySqr, ref bestAnyPos, ref foundAny);

        // 2) 실체화된 적 장수.
        System.Collections.Generic.IReadOnlyList<Elite_State> elites =
            Elite_Manager.Instance != null ? Elite_Manager.Instance.Elites : null;
        if (elites != null)
        {
            for (int i = 0; i < elites.Count; i++)
            {
                Elite_State e = elites[i];
                if (e == null || e == _state || !e.IsAlive) continue;
                if (e.Embodiment == null || e.Faction == myFaction) continue;
                ConsiderCandidate(e.Embodiment.transform.position, self, aggroSqr,
                    ref bestInsideSqr, ref bestInsidePos, ref foundInside,
                    ref bestAnySqr, ref bestAnyPos, ref foundAny);
            }
        }

        // 3) 적 잡몹(ECS).
        ConsiderNearestHostileMob(myFaction, self, aggroSqr,
            ref bestInsideSqr, ref bestInsidePos, ref foundInside,
            ref bestAnySqr, ref bestAnyPos, ref foundAny);

        if (foundInside)
        {
            targetPos = bestInsidePos;
            return true;
        }

        if (!foundAny)
            return false;

        targetPos = bestAnyPos;
        return true;
    }

    private static void ConsiderCandidate(
        Vector3 candidate,
        Vector3 self,
        float aggroSqr,
        ref float bestInsideSqr,
        ref Vector3 bestInsidePos,
        ref bool foundInside,
        ref float bestAnySqr,
        ref Vector3 bestAnyPos,
        ref bool foundAny)
    {
        Vector3 d = candidate - self;
        d.y = 0f;
        float sq = d.sqrMagnitude;

        if (sq < bestAnySqr)
        {
            bestAnySqr = sq;
            bestAnyPos = candidate;
            foundAny = true;
        }

        if (sq > aggroSqr || sq >= bestInsideSqr)
            return;

        bestInsideSqr = sq;
        bestInsidePos = candidate;
        foundInside = true;
    }

    private void ConsiderNearestHostileMob(
        NavFaction myFaction,
        Vector3 self,
        float aggroSqr,
        ref float bestInsideSqr,
        ref Vector3 bestInsidePos,
        ref bool foundInside,
        ref float bestAnySqr,
        ref Vector3 bestAnyPos,
        ref bool foundAny)
    {
        if (!EnsureMobQuery())
            return;

        NativeArray<LocalTransform> transforms = _mobQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        NativeArray<NavAgentFaction> factions = _mobQuery.ToComponentDataArray<NavAgentFaction>(Allocator.Temp);
        NativeArray<NavAgentDeath> deaths = _mobQuery.ToComponentDataArray<NavAgentDeath>(Allocator.Temp);

        float3 s = self;
        for (int i = 0; i < transforms.Length; i++)
        {
            if (factions[i].Faction == myFaction) continue;
            if (deaths[i].Dying != 0) continue;

            float3 d = transforms[i].Position - s;
            d.y = 0f;
            float sq = math.lengthsq(d);
            if (sq < bestAnySqr)
            {
                bestAnySqr = sq;
                bestAnyPos = transforms[i].Position;
                foundAny = true;
            }

            if (sq > aggroSqr || sq >= bestInsideSqr)
                continue;

            bestInsideSqr = sq;
            bestInsidePos = transforms[i].Position;
            foundInside = true;
        }

        transforms.Dispose();
        factions.Dispose();
        deaths.Dispose();
    }

    // PendingExitSector로 가는 게이트까지 nav로 걸어가고, 다다르면 Elite_Manager가 통과·매크로 인계한다.
    private const float GateExitArriveRange = 1.6f;

    private void TickGateExit(float dt)
    {
        SectorGate gate = FindGateTo(_state.CurrentSector, _state.PendingExitSector);
        if (gate == null)
        {
            // 목적지로의 게이트가 없으면 이탈 취소(매크로가 다음 틱에 다시 판단).
            _state.CancelEmbodiedGateExit();
            _navAgent?.ClearPath();
            _aiCommands.SetMoveWorld(Vector3.zero);
            return;
        }

        Vector3 gatePos = Elite_WorldSimulator.ResolveGateDeparturePosition(
            _state.CurrentSector,
            _state.PendingExitSector,
            _state);
        Vector3 to = gatePos - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        Vector3 dir = dist > 0.0001f ? to / dist : transform.forward;
        _aiCommands.SetLookWorld(dir);

        if (dist <= GateExitArriveRange)
        {
            _navAgent?.ClearPath();
            _aiCommands.SetMoveWorld(Vector3.zero);
            // 게이트에 다다름 → 통과 대쉬 시작(transform Lerp). 대쉬가 끝나면 매크로로 인계된다.
            Elite_Manager.Instance?.BeginGateExitDash(_state, this, gate);
            return;
        }

        if (_navAgent != null)
        {
            _targetRefreshTimer -= dt;
            if (_targetRefreshTimer <= 0f)
            {
                _targetRefreshTimer = _brain != null ? Mathf.Max(0.05f, _brain.TargetRefreshInterval) : 0.2f;
                _navAgent.SetTarget(gatePos);
            }

            Vector3 navDir = _navAgent.DesiredDirection;
            if (_navAgent.HasPath && navDir.sqrMagnitude > 0.0001f)
            {
                _aiCommands.SetMoveWorld(navDir);
                return;
            }
        }

        _aiCommands.SetMoveWorld(dir);
    }

    private static SectorGate FindGateTo(Sector from, Sector to)
    {
        if (from?.Gates == null || to == null)
            return null;

        SectorGate[] gates = from.Gates;
        for (int i = 0; i < gates.Length; i++)
        {
            SectorGate g = gates[i];
            if (g != null && g.ConnectedGate != null && g.ConnectedGate.Sector == to)
                return g;
        }
        return null;
    }

    private void ChaseTarget(Vector3 targetPos, Vector3 straightDir, float dt)
    {
        _targetRefreshTimer -= dt;
        if (_navAgent != null)
        {
            if (_targetRefreshTimer <= 0f)
            {
                _targetRefreshTimer = _brain != null ? Mathf.Max(0.05f, _brain.TargetRefreshInterval) : 0.2f;
                _navAgent.SetTarget(targetPos);
            }

            Vector3 navDir = _navAgent.DesiredDirection;
            if (_navAgent.HasPath && navDir.sqrMagnitude > 0.0001f)
            {
                _aiCommands.SetMoveWorld(navDir);
                return;
            }
        }

        // nav 경로가 아직 없거나 실패(벽 너머 등)면 직선으로 폴백.
        _aiCommands.SetMoveWorld(straightDir);
    }

    private void StalkTarget(Vector3 toTargetDir, float distance, float preferredRange, float aggression)
    {
        _navAgent?.ClearPath();
        Vector3 tangent = GetTangent(toTargetDir) * _strafeSign;
        float approachBias = Mathf.InverseLerp(preferredRange, GetAttackRange(), distance);
        float forwardWeight = Mathf.Lerp(0.15f, 0.45f, aggression) + approachBias * 0.35f;
        Vector3 move = tangent * Mathf.Lerp(0.75f, 0.45f, aggression) + toTargetDir * forwardWeight;
        _aiCommands.SetMoveWorld(move.sqrMagnitude > 0.0001f ? move.normalized : Vector3.zero);
    }

    private bool TryEmergencyReposition(float distance, float closeRange, Vector3 toTargetDir, float aggression)
    {
        if (distance > closeRange || _actionHandler.State != Character_ActionState.Normal)
            return false;

        Vector3 escape = (-toTargetDir + GetTangent(toTargetDir) * (_strafeSign * 0.65f)).normalized;
        _aiCommands.SetMoveWorld(escape);

        if (_dashThinkTimer <= 0f && aggression > 0.2f)
        {
            _aiCommands.PressDash();
            _dashThinkTimer = Mathf.Lerp(1.4f, 0.65f, aggression);
        }
        return true;
    }

    private bool TryUseSkill(float sqrDistance, float attackRange, Vector3 toTargetDir)
    {
        if (_skillThinkTimer > 0f || _attackController == null || !_actionHandler.CanUseSkill)
            return false;

        _skillThinkTimer = GetSkillThinkInterval();
        int slot = SelectSkillSlot(sqrDistance, attackRange);
        if (slot < 0)
            return false;

        _navAgent?.ClearPath();
        _aiCommands.SetMoveWorld(Vector3.zero);
        _aiCommands.SetLookWorld(toTargetDir);
        _aiCommands.PressSkill(slot);
        _attackCooldownTimer = Mathf.Max(_attackCooldownTimer, 0.25f);
        return true;
    }

    private int SelectSkillSlot(float sqrDistance, float attackRange)
    {
        float aggression = GetAggression01();
        int activeFallback = -1;
        float bestActiveRange = 0f;

        for (int i = 0; i < SkillInput.SlotCount; i++)
        {
            SO_Skill_Data skill = _attackController.GetSkillData(i);
            if (skill == null || _attackController.GetSkillCooldown(i) > 0f)
                continue;

            float skillRange = GetSkillRange(skill, attackRange);
            bool inSkillRange = sqrDistance <= skillRange * skillRange;
            if (skill.IsUltimate)
            {
                if (ShouldUseUltimate(skill, inSkillRange, aggression))
                    return i;
            }
        }

        for (int i = 0; i < SkillInput.SlotCount; i++)
        {
            SO_Skill_Data skill = _attackController.GetSkillData(i);
            if (skill == null || skill.IsUltimate || _attackController.GetSkillCooldown(i) > 0f)
                continue;

            float skillRange = GetSkillRange(skill, attackRange);
            bool inSkillRange = sqrDistance <= skillRange * skillRange;

            bool gapCloser = IsGapCloser(skill, attackRange);
            bool wantsGapCloser = gapCloser && sqrDistance > attackRange * attackRange && inSkillRange;
            bool wantsPressureSkill = inSkillRange && sqrDistance <= Mathf.Max(attackRange, skillRange * 0.85f) * Mathf.Max(attackRange, skillRange * 0.85f);
            if (wantsGapCloser || wantsPressureSkill)
                return i;

            if (inSkillRange && skillRange > bestActiveRange)
            {
                bestActiveRange = skillRange;
                activeFallback = i;
            }
        }

        return aggression >= 0.65f ? activeFallback : -1;
    }

    private bool ShouldUseUltimate(SO_Skill_Data skill, bool inSkillRange, float aggression)
    {
        if (!inSkillRange || _actionHandler.Gauge < skill.ResourceCost)
            return false;

        float healthRatio = GetHealthRatio();
        return healthRatio <= GetLowHealthAggressionThreshold() || aggression >= 0.65f;
    }

    private static bool IsGapCloser(SO_Skill_Data skill, float attackRange)
    {
        SO_Attack_Data[] sequence = skill.AttackSequence;
        if (sequence == null) return false;
        for (int i = 0; i < sequence.Length; i++)
        {
            SO_Attack_Data attack = sequence[i];
            if (attack == null) continue;
            AttackMoveType moveType = attack.Lunge.moveType;
            if ((moveType == AttackMoveType.Dash || moveType == AttackMoveType.RushTrack || moveType == AttackMoveType.Lunge)
                && attack.Lunge.distance > attackRange * 0.75f)
                return true;
        }
        return false;
    }

    private float GetSkillRange(SO_Skill_Data skill, float fallbackRange)
    {
        SO_Attack_Data[] sequence = skill.AttackSequence;
        if (sequence == null || sequence.Length == 0 || sequence[0] == null)
            return fallbackRange;

        SO_Attack_Data attack = sequence[0];
        float range = GetAttackRange(attack);
        AttackLungeData lunge = attack.Lunge;
        if (lunge.moveType == AttackMoveType.Dash || lunge.moveType == AttackMoveType.RushTrack || lunge.moveType == AttackMoveType.Lunge)
            range += Mathf.Max(0f, lunge.distance) * 0.8f;
        if (attack.Projectile.enabled)
            range = Mathf.Max(range, attack.Projectile.maxDistance);
        if (attack.Field.enabled)
            range = Mathf.Max(range, attack.Field.forwardOffset + AttackShapeUtility.GetPlanarReach(attack.Shape));
        return Mathf.Max(fallbackRange, range);
    }

    private void PressComboAttack()
    {
        if (_comboPressTimer > 0f)
            return;

        _aiCommands.PressAttack();
        _comboPressTimer = GetComboPressInterval();
    }

    private void TickStrafe(float dt)
    {
        _strafeTimer -= dt;
        if (_strafeTimer > 0f)
            return;

        _strafeTimer = GetStrafeInterval();
        _strafeSign *= -1;
    }

    private float GetAggression01()
    {
        float healthRatio = GetHealthRatio();
        float low = GetLowHealthAggressionThreshold();
        float critical = GetCriticalHealthAggressionThreshold();
        if (healthRatio <= critical)
            return 1f;
        if (healthRatio <= low)
            return 0.65f;
        return 0.25f;
    }

    private float GetHealthRatio()
    {
        if (_actionHandler == null || _actionHandler.MaxHealth <= 0f)
            return 1f;
        return Mathf.Clamp01(_actionHandler.Health / _actionHandler.MaxHealth);
    }

    private float GetPhaseAttackCooldown(float aggression)
    {
        float baseCooldown = _brain != null ? Mathf.Max(0f, _brain.AttackCooldown) : 1.2f;
        return Mathf.Max(0.08f, baseCooldown * Mathf.Lerp(1f, 0.55f, aggression));
    }

    private float GetAggroRangeSqr()
    {
        float range = _brain != null ? _brain.AggroRange : 18f;
        if (range <= 0f)
            return float.MaxValue;
        return range * range;
    }

    private float GetTargetResolveInterval()
        => _brain != null ? Mathf.Max(0.1f, _brain.TargetRefreshInterval) : 0.2f;

    private float GetPreferredRangeRatio()
        => _brain != null ? Mathf.Max(0.1f, _brain.PreferredRangeRatio) : 1.15f;

    private float GetCloseRangeRatio()
        => _brain != null ? Mathf.Max(0.05f, _brain.CloseRangeRatio) : 0.45f;

    private float GetSkillThinkInterval()
        => _brain != null ? Mathf.Max(0.05f, _brain.SkillThinkInterval) : 0.35f;

    private float GetComboPressInterval()
        => _brain != null ? Mathf.Max(0.05f, _brain.ComboPressInterval) : 0.14f;

    private float GetStrafeInterval()
        => _brain != null ? Mathf.Max(0.1f, _brain.StrafeInterval) : 1.2f;

    private float GetLowHealthAggressionThreshold()
        => _brain != null ? Mathf.Clamp01(_brain.LowHealthAggressionThreshold) : 0.55f;

    private float GetCriticalHealthAggressionThreshold()
        => _brain != null ? Mathf.Clamp01(_brain.CriticalHealthAggressionThreshold) : 0.25f;

    private static Vector3 GetTangent(Vector3 direction)
        => new(direction.z, 0f, -direction.x);

    private bool EnsureMobQuery()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;
        if (world == _cachedWorld) return true;

        _cachedWorld = world;
        _em = world.EntityManager;
        _mobQuery = _em.CreateEntityQuery(
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<NavAgentFaction>(),
            ComponentType.ReadOnly<NavAgentDeath>());
        return true;
    }

    private void OnDestroy()
    {
        if (_cachedWorld != null && _cachedWorld.IsCreated)
            _mobQuery.Dispose();
    }

    // 이동만 멈춘다(시선은 호출부가 관리). 다음 추격 시 즉시 리패스하도록 타이머도 리셋.
    private void StopMovement()
    {
        _aiCommands.SetMoveWorld(Vector3.zero);
        _navAgent?.ClearPath();
        _targetRefreshTimer = 0f;
    }

    // 비전투(사망/어그로 밖): 이동 정지 + 시선도 idle(현재 forward)로.
    private void StopChase()
    {
        _hasCachedTarget = false;
        StopMovement();
        _aiCommands.SetLookWorld(transform.forward);
    }

    private Character_ActionHandler ResolvePlayer()
    {
        if (_player != null && _player.isActiveAndEnabled)
            return _player;

        Player_Actor player = FindAnyObjectByType<Player_Actor>();
        _player = player != null ? player.GetComponent<Character_ActionHandler>() : null;
        _playerVitals = player != null ? player.GetComponent<Character_Vitals>() : null;
        return _player;
    }

    private float GetAttackRange()
        => GetAttackRange(GetBasicAttackData());

    private float GetAttackRange(SO_Attack_Data attack)
    {
        float reach = attack != null
            ? attack.Hitbox.offset + AttackShapeUtility.GetPlanarReach(attack.Shape)
            : 1.5f;
        float padding = _brain != null ? _brain.AttackRangePadding : 0.25f;
        return Mathf.Max(0.1f, reach + padding);
    }

    private SO_Attack_Data GetBasicAttackData()
    {
        SO_Character_Loadout loadout = _data != null && _data.Character != null
            ? _data.Character.DefaultLoadout
            : null;
        SO_Attack_ComboData combo = loadout != null ? loadout.EquippedAttackCombo : null;
        SO_Attack_Data[] attacks = combo != null ? combo.Attacks : null;
        return attacks != null && attacks.Length > 0 ? attacks[0] : null;
    }
}
