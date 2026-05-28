using System;
using Cysharp.Threading.Tasks;
using MapNav.Ecs;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

[DisallowMultipleComponent]
public class Player_Attackcontroller : LoopMonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool drawAttackGizmosAlways;
    [SerializeField] private int gizmoAttackIndex;
    [SerializeField] private Color attackGizmoColor = new Color(1f, 0.2f, 0.2f, 0.35f);

    public int  ComboCount  => _comboCount;
    public bool IsAttacking => _attackTimer > 0f;
    public bool IsInCombo   => _attackTimer > 0f || _comboTimer > 0f;
    public bool SuspendsAtApex => IsAttacking && _currentData != null && _currentData.Jump.suspendAtApex
                                  && _currentData.Lunge.moveType != AttackMoveType.Slam;
    public bool IsSlamDescending => _slamDescending && _currentData != null && _currentData.Lunge.moveType == AttackMoveType.Slam;
    public float SlamDescentSpeed => _currentData != null ? _currentData.Lunge.slamDescentSpeed : 0f;
    public bool BlocksMovement => _attackTimer > 0f || (_comboTimer > 0f && _lockMovementDuringComboWindow);
    public bool IsSuperArmoredAgainst(float superArmorBreak)
        => IsAttacking && _currentData != null && _currentData.SuperArmor > superArmorBreak;

    private Player_Animator       _playerAnimator;
    private Player_Movecontroller _moveController;
    private Player_Vfx            _vfx;
    private Player_ActionHandler  _actionHandler;
    private SO_PlayerStats        _playerStats;
    private SO_AttackData[] _attacks;
    private float _comboWindow = 0.35f;
    private bool _lockMovementDuringComboWindow = true;
    private const float ExplicitLookInputSqrThreshold = 0.04f;
    private int   _comboCount;
    private float _attackTimer;
    private float _comboTimer;
    private bool  _nextQueued;
    private Vector3       _pendingLookDirection;
    private SO_AttackData _currentData;
    private bool          _hitboxFired;
    private float         _nextHitboxElapsed;
    private bool          _attackMoveVfxPlaying;
    private bool          _slamLandingFired;
    private bool          _slamDescending;

    // Skills (loadout). 4개 슬롯 — [[project_skill_loadout]]. 자원 시스템은 미구현이라 cost는 일단 무시.
    private SO_SkillData[] _skills;
    private readonly float[] _skillCooldowns = new float[SkillInput.SlotCount];
    private SO_AttackData[] _skillSequence;
    private int             _skillSequenceIndex;
    private bool  _skillSequenceAnyHit;
    private readonly Collider[] _hitboxOverlapBuffer = new Collider[128];
    private readonly Collider[] _autoAimOverlapBuffer = new Collider[128];
    private readonly AttackHitRegistry _attackHitRegistry = new();

    private IHitboxProcessor _hitboxProcessor;

    private EntityManager _em;
    private EntityQuery   _autoAimQuery;
    private World         _cachedWorld;

    private void Awake()
    {
        _playerAnimator = GetComponent<Player_Animator>();
        _moveController = GetComponent<Player_Movecontroller>();
        _vfx = GetComponent<Player_Vfx>();
        _actionHandler = GetComponent<Player_ActionHandler>();
        _hitboxProcessor = GetComponent<IHitboxProcessor>();
    }

    public void SetPlayerStats(SO_PlayerStats stats)
    {
        _playerStats = stats;
    }

    public void SetBasicAttackCombo(SO_AttackComboData combo)
    {
        if (combo == null)
        {
            _attacks = null;
            _comboWindow = 0.35f;
            _lockMovementDuringComboWindow = true;
            return;
        }

        _attacks = combo.Attacks;
        _comboWindow = combo.ComboWindow;
        _lockMovementDuringComboWindow = combo.LockMovementDuringComboWindow;
    }

    public void SetSkills(SO_SkillData[] skills)
    {
        _skills = skills;
        for (int i = 0; i < _skillCooldowns.Length; i++)
            _skillCooldowns[i] = 0f;
    }

    public SO_SkillData GetSkillData(int slot)
    {
        if (slot < 0 || _skills == null || slot >= _skills.Length) return null;
        return _skills[slot];
    }

    public float GetSkillCooldown(int slot)
        => slot >= 0 && slot < _skillCooldowns.Length ? _skillCooldowns[slot] : 0f;

    public bool TryTriggerSkill(int slot)
    {
        if (slot < 0 || slot >= _skillCooldowns.Length) return false;
        if (_skills == null || slot >= _skills.Length) return false;
        SO_SkillData skill = _skills[slot];
        if (skill == null) return false;
        if (_skillCooldowns[slot] > 0f) return false;
        if (skill.Category == SkillCategory.Ultimate && (_actionHandler == null || !_actionHandler.TryConsumeGauge(skill.ResourceCost)))
            return false;

        SO_AttackData[] sequence = skill.AttackSequence;
        if (sequence == null || sequence.Length == 0 || sequence[0] == null) return false;

        // 진행 중 공격/콤보가 있으면 끊고 스킬 진입. CancelAttack이 _skillSequence를 비우므로 그 뒤에 설정.
        if (_attackTimer > 0f)
            CancelAttack();

        _skillCooldowns[slot] = skill.Cooldown;
        _skillSequence = sequence;
        _skillSequenceIndex = 0;
        StartAttackData(sequence[0]);
        return true;
    }

    public bool RequestAttack()
    {
        if (_attackTimer > 0f)
        {
            _nextQueued = true;
            return true;
        }

        if (_comboCount > 0 && _comboTimer <= 0f)
            ResetCombo();

        _nextQueued = false;
        StartAttack();
        return IsAttacking;
    }

    protected override void OnGameUpdate(float gdt)
    {
        base.OnGameUpdate(gdt);

        TickSkillCooldowns(gdt);
        PollSkillInput();

        if (_attackTimer > 0f)
        {
            if (!_slamDescending)
                _attackTimer -= gdt;

            TickAttackHitbox(_currentData);

            if (_attackTimer <= 0f) OnAttackEnd();
            return;
        }

        if (_nextQueued)
        {
            _nextQueued = false;
            StartAttack();
            return;
        }

        if (_comboTimer > 0f)
        {
            _comboTimer -= gdt;
            if (_comboTimer <= 0f)
                ResetCombo();
        }
    }

    private void TickSkillCooldowns(float gdt)
    {
        for (int i = 0; i < _skillCooldowns.Length; i++)
        {
            if (_skillCooldowns[i] > 0f)
                _skillCooldowns[i] = Mathf.Max(0f, _skillCooldowns[i] - gdt);
        }
    }

    private void PollSkillInput()
    {
        // 스킬 시퀀스 진행 중에는 다른 스킬을 받지 않는다 (자기 자신 끊김 방지).
        if (_skillSequence != null) return;
        if (_skills == null) return;

        for (int i = 0; i < _skillCooldowns.Length; i++)
        {
            if (!InputProvider.ConsumeSkill(i)) continue;
            if (TryTriggerSkill(i)) return;
        }
    }

    private void StartAttack()
    {
        SO_AttackData attack = GetData(_comboCount);
        if (attack == null)
        {
            ResetCombo();
            return;
        }
        StartAttackData(attack);
    }

    private void StartAttackData(SO_AttackData attack)
    {
        Vector3 inputAim = _pendingLookDirection;
        bool hasExplicitInput = inputAim.sqrMagnitude > ExplicitLookInputSqrThreshold;
        Vector3 autoAim = hasExplicitInput ? Vector3.zero : FindAutoAimDirection(attack);
        Vector3 lookDir = hasExplicitInput ? inputAim : autoAim;

        if (lookDir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(lookDir);

        App.AlignThirdPersonCameraToTargetYaw(attack.Duration);

        _pendingLookDirection = Vector3.zero;
        _currentData = attack;
        _comboTimer = 0f;
        _attackTimer = _currentData.Duration;
        _hitboxFired = false;
        _nextHitboxElapsed = 0f;
        _skillSequenceAnyHit = false;
        _slamLandingFired = false;
        _attackHitRegistry.Clear();

        _playerAnimator?.PlayAttack(_currentData.Animation);
        if (_currentData.Lunge.moveType == AttackMoveType.Slam)
        {
            _slamDescending = true;
            _moveController?.StartLunge(transform.forward, _currentData.Lunge);
        }
        else
        {
            _moveController?.StartLunge(transform.forward, _currentData.Lunge);
        }
        if (_currentData.Jump.enabled)
            _moveController?.Jump(_currentData.Jump.height);
        if (ShouldPlayDashVfx(_currentData.Lunge))
        {
            _vfx?.PlayDashStart(transform.forward);
            _attackMoveVfxPlaying = true;
        }

        AttackFeedbackData fb = _currentData.Feedback;
        if (!string.IsNullOrEmpty(fb.castVfxAddress))
        {
            Vector3 castPos = transform.position + transform.rotation * fb.castVfxOffset;
            CombatFeedback.SpawnVfxAtPosition(fb.castVfxAddress, castPos, destroyCancellationToken);
        }
        _vfx?.PlaySwingTrails(fb.swingTrailIds);
        App.PlaySfx(fb.swingSfx, transform.position);

        if (_currentData.SlowMo.enabled)
            TriggerSlowMo(_currentData.SlowMo).Forget();
    }

    private void OnAttackEnd()
    {
        _playerAnimator?.ExitAttack();
        StopAttackMoveVfx(true);
        if (_currentData != null)
            _vfx?.StopSwingTrails(_currentData.Feedback.swingTrailIds);

        // 스킬 시퀀스 진행 중이면 다음 attack을 자동 발사. 시퀀스가 끝나면 일반 콤보 상태로 복귀.
        if (_skillSequence != null)
        {
            if (!_skillSequenceAnyHit)
            {
                _skillSequence = null;
                _skillSequenceIndex = 0;
                ResetCombo();
                return;
            }

            _skillSequenceIndex++;
            if (_skillSequenceIndex < _skillSequence.Length && _skillSequence[_skillSequenceIndex] != null)
            {
                StartAttackData(_skillSequence[_skillSequenceIndex]);
                return;
            }
            _skillSequence = null;
            _skillSequenceIndex = 0;
            ResetCombo();
            return;
        }

        _comboCount++;
        if (_attacks == null || _comboCount >= _attacks.Length)
        {
            ResetCombo();
            return;
        }
        _comboTimer = _comboWindow;
    }

    private void ResetCombo()
    {
        _comboCount = 0;
        _comboTimer = 0f;
        _nextQueued = false;
        _pendingLookDirection = Vector3.zero;
        _hitboxFired = false;
        _nextHitboxElapsed = 0f;
        _skillSequenceAnyHit = false;
        _slamLandingFired = false;
        _slamDescending = false;
        _attackHitRegistry.Clear();
        StopAttackMoveVfx(false);
        _vfx?.StopAllSwingTrails();
        _playerAnimator?.ReleaseLocomotion();
    }

    public void CancelAttack()
    {
        _attackTimer = 0f;
        _playerAnimator?.ExitAttack();
        if (_currentData != null)
            _vfx?.StopSwingTrails(_currentData.Feedback.swingTrailIds);
        _skillSequence = null;
        _skillSequenceIndex = 0;
        ResetCombo();
    }

    private void TickAttackHitbox(SO_AttackData data)
    {
        if (data.Lunge.moveType == AttackMoveType.Slam)
        {
            if (!_slamLandingFired && (_moveController == null || _moveController.IsGrounded))
            {
                _slamLandingFired = true;
                _slamDescending = false;
                FireHitbox(data);
            }
            return;
        }

        AttackHitboxData hitbox = data.Hitbox;
        float elapsed = data.Duration - Mathf.Max(0f, _attackTimer);
        float startElapsed = hitbox.repeatDuringAttack ? 0f : data.Duration * hitbox.timing;
        if (!_hitboxFired)
        {
            if (elapsed < startElapsed)
                return;

            _hitboxFired = true;
            _nextHitboxElapsed = elapsed + Mathf.Max(0.01f, hitbox.repeatInterval);
            FireHitbox(data);
            return;
        }

        if (!hitbox.repeatDuringAttack)
            return;

        float repeatInterval = Mathf.Max(0.01f, hitbox.repeatInterval);
        while (_nextHitboxElapsed <= elapsed)
        {
            FireHitbox(data);
            _nextHitboxElapsed += repeatInterval;
        }
    }

    private void FireHitbox(SO_AttackData data)
    {
        float finalDamage = _playerStats != null
            ? CombatFormula.ScaleAttackDamage(_playerStats.AttackPower, data.Damage)
            : data.Damage;

        string timingVfx = data.Feedback.timingVfxAddress;
        if (!string.IsNullOrEmpty(timingVfx))
        {
            Vector3 center = AttackShapeUtility.GetQueryCenter(transform.position, transform.forward, data.Hitbox, data.Shape);
            center += transform.rotation * data.Feedback.timingVfxOffset;
            CombatFeedback.SpawnVfxAtPosition(timingVfx, center, destroyCancellationToken);
        }

        bool didHit = FireHitInstance(data, data.Hitbox, data.Shape, finalDamage);

        AttackExtraHit[] extras = data.AdditionalHits;
        if (extras != null)
        {
            for (int i = 0; i < extras.Length; i++)
                didHit |= FireHitInstance(data, extras[i].hitbox, extras[i].shape, finalDamage);
        }

        float targetSuspendDuration = GetTargetSuspendDuration(data);
        if (_hitboxProcessor != null && _hitboxProcessor.Process(data, transform, _attackHitRegistry, finalDamage, targetSuspendDuration))
            didHit = true;

        _skillSequenceAnyHit |= didHit;
        if (!didHit) return;

        if (data.LifeSteal.enabled)
        {
            float heal = finalDamage * data.LifeSteal.ratio;
            if (data.LifeSteal.maxPerHit > 0f)
                heal = Mathf.Min(heal, data.LifeSteal.maxPerHit);
            _actionHandler?.Heal(heal);
        }

        _actionHandler?.AddGauge(finalDamage * (_playerStats != null ? _playerStats.GaugeGainPerDamage : 0f));

        if (data.Lunge.stopOnHit)
        {
            _moveController?.StopLunge();
            StopAttackMoveVfx(true);
        }

        TriggerHitstop(data.Hitstop).Forget();
    }

    private static float GetTargetSuspendDuration(SO_AttackData data)
        => data != null ? data.Launch.suspendDuration : 0f;

    private bool FireHitInstance(SO_AttackData data, AttackHitboxData hitbox, AttackShapeData shape, float finalDamage)
    {
        Vector3 center = AttackShapeUtility.GetQueryCenter(transform.position, transform.forward, hitbox, shape);
        float queryRadius = AttackShapeUtility.GetQueryRadius(hitbox, shape);
        bool didHit = false;

        int hitCount = Physics.OverlapSphereNonAlloc(center, queryRadius, _hitboxOverlapBuffer);
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _hitboxOverlapBuffer[i];
            if (!col.TryGetComponent(out IHitTarget target)) continue;
            Vector3 targetPoint = col.ClosestPoint(center);
            if (!AttackShapeUtility.Contains(transform.position, transform.forward, targetPoint, hitbox, shape))
                continue;
            if (!_attackHitRegistry.TryRegister(col.GetInstanceID(), 1, hitbox.hitSameTargetOnce))
                continue;

            target.ReceiveHit(transform.position, transform.forward, data, finalDamage);
            SpawnHitFeedback(data, targetPoint);
            didHit = true;
        }

        return didHit;
    }

    public void UpdateLookDirection(Vector3 worldInput)
    {
        _pendingLookDirection = worldInput;
    }

    private Vector3 FindAutoAimDirection(SO_AttackData data)
    {
        float range = data.Hitbox.offset + AttackShapeUtility.GetPlanarReach(data.Shape);
        Vector3 best = Vector3.zero;
        float bestDist = range * range;
        Vector3 myPos = transform.position;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        // GameObject 타겟
        int hitCount = Physics.OverlapSphereNonAlloc(myPos, range, _autoAimOverlapBuffer);
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _autoAimOverlapBuffer[i];
            if (!col.TryGetComponent(out IHitTarget _)) continue;
            Vector3 diff = col.ClosestPoint(myPos) - myPos;
            diff.y = 0f;
            if (diff.sqrMagnitude < 0.0001f) continue;
            if (Vector3.Dot(diff.normalized, forward) < -0.5f) continue;
            float dist = diff.sqrMagnitude;
            if (dist >= bestDist) continue;
            bestDist = dist;
            best = diff.normalized;
        }

        // ECS 타겟
        if (EnsureAutoAimQuery())
        {
            NativeArray<LocalTransform> transforms = _autoAimQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            NativeArray<NavAgentDeath> deaths = _autoAimQuery.ToComponentDataArray<NavAgentDeath>(Allocator.Temp);
            NativeArray<NavAgentFaction> factions = _autoAimQuery.ToComponentDataArray<NavAgentFaction>(Allocator.Temp);
            NativeArray<NavAgentSettings> settings = _autoAimQuery.ToComponentDataArray<NavAgentSettings>(Allocator.Temp);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (deaths[i].Dying != 0) continue;
                if (factions[i].Faction != NavFaction.Enemy) continue;
                var f = transforms[i].Position;
                float targetRadius = Mathf.Max(0f, settings[i].AgentRadius);
                Vector3 pos = new Vector3(f.x, f.y, f.z);
                Vector3 diff = pos - myPos;
                diff.y = 0f;
                if (diff.sqrMagnitude < 0.0001f) continue;
                if (Vector3.Dot(diff.normalized, forward) < -0.5f) continue;
                float distToBody = Mathf.Max(0f, diff.magnitude - targetRadius);
                float dist = distToBody * distToBody;
                if (dist >= bestDist) continue;
                bestDist = dist;
                best = diff.normalized;
            }
            transforms.Dispose();
            deaths.Dispose();
            factions.Dispose();
            settings.Dispose();
        }

        return best;
    }

    private bool EnsureAutoAimQuery()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;
        if (world == _cachedWorld) return true;

        if (_cachedWorld != null && _cachedWorld.IsCreated)
            _autoAimQuery.Dispose();

        _cachedWorld = world;
        _em = world.EntityManager;
        _autoAimQuery = _em.CreateEntityQuery(
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<NavAgentKnockback>(),
            ComponentType.ReadOnly<NavAgentDeath>(),
            ComponentType.ReadOnly<NavAgentFaction>(),
            ComponentType.ReadOnly<NavAgentSettings>());
        return true;
    }

    private SO_AttackData GetData(int index)
        => _attacks != null && _attacks.Length > 0
            ? _attacks[Mathf.Clamp(index, 0, _attacks.Length - 1)]
            : null;

    // hit VFX는 캐릭터의 가슴/배 높이에서 떠야 자연스럽다. 발 위치 기준으로 +0.5m 보정.
    private const float HitVfxHeightOffset = 0.5f;

    private void SpawnHitFeedback(SO_AttackData data, Vector3 position)
    {
        position.y += HitVfxHeightOffset;
        CombatFeedback.PlayHitFeedback(data, position, destroyCancellationToken);
    }

    private void StopAttackMoveVfx(bool playEnd)
    {
        if (!_attackMoveVfxPlaying)
            return;

        _attackMoveVfxPlaying = false;
        if (playEnd)
            _vfx?.PlayDashEnd(transform.forward);
        else
            _vfx?.StopDash();
    }

    private static bool ShouldPlayDashVfx(AttackLungeData lunge)
        => lunge.moveType == AttackMoveType.Dash || lunge.moveType == AttackMoveType.RushTrack;

    private async UniTaskVoid TriggerHitstop(AttackHitstopData hitstop)
    {
        if (hitstop.duration <= 0f) return;

        Main.Loop.SetGameSpeed(hitstop.timeScale);
        await UniTask.Delay(
            TimeSpan.FromSeconds(hitstop.duration),
            ignoreTimeScale: true,
            cancellationToken: destroyCancellationToken);
        Main.Loop.SetGameSpeed(1f);
    }

    private async UniTaskVoid TriggerSlowMo(AttackSlowMoData slowMo)
    {
        if (slowMo.duration <= 0f) return;

        Main.Loop.SetTimeScales(Mathf.Clamp01(slowMo.worldScale), 1f);
        await UniTask.Delay(
            TimeSpan.FromSeconds(slowMo.duration),
            ignoreTimeScale: true,
            cancellationToken: destroyCancellationToken);
        if (Main.Loop != null)
            Main.Loop.SetTimeScales(1f, 1f);
    }

    private void OnDestroy()
    {
        if (_cachedWorld != null && _cachedWorld.IsCreated)
            _autoAimQuery.Dispose();
    }

    private void OnDrawGizmosSelected()
    {
        SO_AttackData gizmoData = Application.isPlaying && _currentData != null && IsAttacking
            ? _currentData
            : drawAttackGizmosAlways
                ? GetGizmoData()
                : null;
        if (gizmoData == null) return;

        AttackHitboxData hitbox = gizmoData.Hitbox;
        Color solid = attackGizmoColor;
        solid.a = Mathf.Clamp01(solid.a);
        Gizmos.color = solid;
        DrawAttackShapeGizmo(transform.position, transform.forward, hitbox, gizmoData.Shape, true);
        Color wire = solid;
        wire.a = 0.9f;
        Gizmos.color = wire;
        DrawAttackShapeGizmo(transform.position, transform.forward, hitbox, gizmoData.Shape, false);
    }

    private SO_AttackData GetGizmoData()
    {
        if (_attacks == null || _attacks.Length == 0)
            return null;

        return _attacks[Mathf.Clamp(gizmoAttackIndex, 0, _attacks.Length - 1)];
    }

    private static void DrawAttackShapeGizmo(Vector3 origin, Vector3 forward, AttackHitboxData hitbox, AttackShapeData shape, bool solid)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        switch (shape.type)
        {
            case AttackShape.Cone:
                DrawConeGizmo(origin, forward, hitbox, shape, solid);
                break;
            case AttackShape.Box:
                DrawBoxGizmo(origin, forward, hitbox, shape, solid);
                break;
            default:
                Vector3 center = origin + forward * hitbox.offset + Vector3.up * hitbox.yOffset;
                DrawCylinderGizmo(center, Mathf.Max(0f, shape.radius), hitbox.verticalTolerance, solid);
                break;
        }
    }

    private static void DrawConeGizmo(Vector3 origin, Vector3 forward, AttackHitboxData hitbox, AttackShapeData shape, bool solid)
    {
        Vector3 apex = origin + forward * hitbox.offset + Vector3.up * hitbox.yOffset;
        float length = Mathf.Max(shape.radius, shape.length);
        float halfAngle = Mathf.Clamp(shape.angle, 1f, 360f) * 0.5f;
        float verticalTolerance = Mathf.Max(0f, hitbox.verticalTolerance);
        int segments = 24;

        if (solid)
        {
#if UNITY_EDITOR
            Vector3 solidUp = Vector3.up * verticalTolerance;
            Vector3 fromDir = Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward;
            UnityEditor.Handles.color = Gizmos.color;
            UnityEditor.Handles.DrawSolidArc(apex + solidUp, Vector3.up, fromDir, shape.angle, length);
            UnityEditor.Handles.DrawSolidArc(apex - solidUp, Vector3.up, fromDir, shape.angle, length);
#endif
            return;
        }

        Vector3 up = Vector3.up * verticalTolerance;
        DrawConeSlice(apex + up, forward, length, halfAngle, segments);
        DrawConeSlice(apex - up, forward, length, halfAngle, segments);

        Vector3 leftTop = apex + up + Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward * length;
        Vector3 rightTop = apex + up + Quaternion.AngleAxis(halfAngle, Vector3.up) * forward * length;
        Vector3 leftBottom = leftTop - up * 2f;
        Vector3 rightBottom = rightTop - up * 2f;
        Gizmos.DrawLine(apex + up, apex - up);
        Gizmos.DrawLine(leftTop, leftBottom);
        Gizmos.DrawLine(rightTop, rightBottom);
    }

    private static void DrawConeSlice(Vector3 apex, Vector3 forward, float length, float halfAngle, int segments)
    {
        Vector3 previous = apex + Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward * length;
        Gizmos.DrawLine(apex, previous);
        for (int i = 1; i <= segments; i++)
        {
            float t = Mathf.Lerp(-halfAngle, halfAngle, i / (float)segments);
            Vector3 next = apex + Quaternion.AngleAxis(t, Vector3.up) * forward * length;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
        Gizmos.DrawLine(apex, previous);
    }

    private static void DrawBoxGizmo(Vector3 origin, Vector3 forward, AttackHitboxData hitbox, AttackShapeData shape, bool solid)
    {
        float length = Mathf.Max(shape.radius * 2f, shape.length);
        float width = Mathf.Max(shape.radius * 2f, shape.width);
        float height = Mathf.Max(0.05f, Mathf.Max(0f, hitbox.verticalTolerance) * 2f);
        Vector3 center = origin + forward * (hitbox.offset + length * 0.5f) + Vector3.up * hitbox.yOffset;
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, rotation, Vector3.one);
        Vector3 size = new Vector3(width, height, length);
        if (solid) Gizmos.DrawCube(Vector3.zero, size);
        else Gizmos.DrawWireCube(Vector3.zero, size);
        Gizmos.matrix = previousMatrix;
    }

    private static void DrawCylinderGizmo(Vector3 center, float radius, float verticalTolerance, bool solid)
    {
        float halfHeight = Mathf.Max(0f, verticalTolerance);
        if (solid)
        {
#if UNITY_EDITOR
            Vector3 solidUp = Vector3.up * halfHeight;
            UnityEditor.Handles.color = Gizmos.color;
            UnityEditor.Handles.DrawSolidDisc(center + solidUp, Vector3.up, radius);
            UnityEditor.Handles.DrawSolidDisc(center,           Vector3.up, radius);
            UnityEditor.Handles.DrawSolidDisc(center - solidUp, Vector3.up, radius);
#endif
            return;
        }

        Vector3 up = Vector3.up * halfHeight;
        DrawCircle(center, radius);
        DrawCircle(center + up, radius);
        DrawCircle(center - up, radius);

        Vector3[] anchors =
        {
            Vector3.forward,
            Vector3.back,
            Vector3.right,
            Vector3.left
        };

        for (int i = 0; i < anchors.Length; i++)
        {
            Vector3 offset = anchors[i] * radius;
            Gizmos.DrawLine(center + up + offset, center - up + offset);
        }
    }

    private static void DrawCircle(Vector3 center, float radius)
    {
        const int segments = 32;
        if (radius <= 0f)
            return;

        Vector3 previous = center + Vector3.forward * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
