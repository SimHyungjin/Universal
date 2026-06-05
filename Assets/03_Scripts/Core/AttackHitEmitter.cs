using System.Collections.Generic;
using MapNav.Ecs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// 공격 히트 판정의 단일 구현. 공격자 transform에 묶이지 않고 임의의 pose(origin+forward)에서
// GameObject(IHitTarget, 장수·파괴물 등)와 ECS 잡몹(NavAgent)을 한 번에 판정한다.
// 근접(Character_AttackController), 발사체(Projectile_Hitbox), 장판(Field_Hitbox)이 공유한다.
//
// 한 적은 GameObject(콜라이더) 또는 ECS(엔티티) 중 하나로만 존재하므로(잡몹=ECS, 장수=콜라이더)
// 두 경로가 같은 scope를 써도 중복 타격이 발생하지 않는다.
public sealed class AttackHitEmitter
{
    // hit VFX는 캐릭터의 가슴/배 높이에서 떠야 자연스럽다. 발 위치 기준으로 +0.5m 보정.
    private const float HitVfxHeightOffset = 0.5f;

    // 피격 강제 어그로 지속 시간(초). 감지 반경 밖에서 맞은 잡몹도 이 시간 동안 공격자를 추적한다.
    private const float ForcedAggroDuration = 5f;

    private readonly Collider[] _overlapBuffer = new Collider[128];

    private EntityManager _em;
    private EntityQuery   _hitQuery;
    private World         _cachedWorld;

    public bool Emit(
        Vector3 origin, Vector3 forward,
        AttackHitboxData hitbox, AttackShapeData shape,
        in AttackHitInfo hit, HitType hitType, float finalDamage,
        NavFaction attackerFaction, Entity attackerEntity,
        AttackHitRegistry registry, int scope, bool hitSameTargetOnce,
        SO_Attack_Data feedbackData)
    {
        bool didHit = EmitToHitTargets(origin, forward, hitbox, shape, hit, finalDamage,
            attackerFaction, registry, scope, hitSameTargetOnce, feedbackData);
        didHit |= EmitToEcs(origin, forward, hitbox, shape, hit, hitType, finalDamage,
            attackerFaction, attackerEntity, registry, scope, hitSameTargetOnce, feedbackData);
        return didHit;
    }

    // ── GameObject 경로 (장수·파괴물 등 IHitTarget) ──────────────────────────────
    private bool EmitToHitTargets(
        Vector3 origin, Vector3 forward,
        AttackHitboxData hitbox, AttackShapeData shape,
        in AttackHitInfo hit, float finalDamage,
        NavFaction attackerFaction,
        AttackHitRegistry registry, int scope, bool hitSameTargetOnce,
        SO_Attack_Data feedbackData)
    {
        Vector3 center = AttackShapeUtility.GetQueryCenter(origin, forward, hitbox, shape);
        float queryRadius = AttackShapeUtility.GetQueryRadius(hitbox, shape);
        bool didHit = false;

        int hitCount = Physics.OverlapSphereNonAlloc(center, queryRadius, _overlapBuffer);
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _overlapBuffer[i];
            if (!col.TryGetComponent(out IHitTarget target)) continue;
            if (!target.IsHittable) continue; // 사망 연출 중/무적 → 시체 타격 연출·게이지 방지
            if (!IsHostile(col, attackerFaction)) continue;
            Vector3 targetPoint = col.ClosestPoint(center);
            // 공중에 뜬 피격자(launched)는 수직 허용범위를 벗어나도 맞도록 수직 차이를 제거하고 평면으로 판정한다.
            if (target.IsAirborneHittable)
                targetPoint.y = origin.y + hitbox.yOffset;
            if (!AttackShapeUtility.Contains(origin, forward, targetPoint, hitbox, shape))
                continue;
            if (registry != null && !registry.TryRegister(col.GetInstanceID(), scope, hitSameTargetOnce))
                continue;

            target.ReceiveHit(origin, forward, hit, finalDamage);
            SpawnHitFeedback(feedbackData, targetPoint);
            didHit = true;
        }

        return didHit;
    }

    // 공격자와 다른 진영만 타격한다. Vitals 없는 IHitTarget(파괴물 등)은 항상 허용.
    private static bool IsHostile(Collider col, NavFaction attackerFaction)
        => !col.TryGetComponent(out Character_Vitals targetVitals) || targetVitals.Faction != attackerFaction;

    // ── ECS 경로 (잡몹 NavAgent) ────────────────────────────────────────────────
    private bool EmitToEcs(
        Vector3 origin, Vector3 forward,
        AttackHitboxData hitbox, AttackShapeData shape,
        in AttackHitInfo hit, HitType hitType, float finalDamage,
        NavFaction attackerFaction, Entity attackerEntity,
        AttackHitRegistry registry, int scope, bool hitSameTargetOnce,
        SO_Attack_Data feedbackData)
    {
        if (!EnsureHitQuery()) return false;

        NativeArray<Entity> entities = _hitQuery.ToEntityArray(Allocator.Temp);
        NativeArray<LocalTransform> transforms = _hitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        NativeArray<NavAgentSettings> settings = _hitQuery.ToComponentDataArray<NavAgentSettings>(Allocator.Temp);

        float3 center = AttackShapeUtility.GetQueryCenter(origin, forward, hitbox, shape);
        float queryRadius = AttackShapeUtility.GetQueryRadius(hitbox, shape);
        float3 myPos = origin;
        float suspendDuration = hit.Launch.suspendDuration;

        bool didHit = false;
        for (int i = 0; i < entities.Length; i++)
        {
            // 잡몹 y가 이제 실제로 뜨므로 시뮬 좌표를 그대로 쓴다(비주얼 오프셋 보정 불필요).
            float3 unitPos = transforms[i].Position;

            // 공중에 뜬 잡몹(launch 중)은 hitbox 수직 허용범위를 벗어나도 맞도록 판정용 y를 공격 중심 높이로
            // 맞춘다(캐릭터 IsAirborneHittable과 동일 개념). repeat 재타격이 유지돼야 체공이 연장된다.
            // 실제 unitPos는 넉백 방향·회전·VFX 계산에 그대로 쓴다.
            float3 judgePos = unitPos;
            if (_em.HasComponent<NavAgentLaunch>(entities[i]) &&
                _em.GetComponentData<NavAgentLaunch>(entities[i]).Airborne != 0)
                judgePos.y = center.y;

            float targetRadius = math.max(0f, settings[i].AgentRadius);
            float expandedQueryRadius = queryRadius + targetRadius;
            if (math.distancesq(center, judgePos) > expandedQueryRadius * expandedQueryRadius) continue;
            if (!AttackShapeUtility.Contains(origin, forward, judgePos, targetRadius, hitbox, shape))
                continue;
            if (registry != null && !registry.TryRegister(GetEntityHitKey(entities[i]), scope, hitSameTargetOnce))
                continue;

            // 사망 연출 중인 적은 더 이상 타격 대상이 아니다.
            if (_em.HasComponent<NavAgentDeath>(entities[i]) &&
                _em.GetComponentData<NavAgentDeath>(entities[i]).Dying != 0)
                continue;

            // 공격자와 다른 진영의 잡몹만 타격한다(플레이어=Ally→Enemy 잡몹, 적 장수=Enemy→Ally 잡몹).
            if (_em.HasComponent<NavAgentFaction>(entities[i]) &&
                _em.GetComponentData<NavAgentFaction>(entities[i]).Faction == attackerFaction)
                continue;

            float3 radialDir = math.normalizesafe(
                new float3(unitPos.x - myPos.x, 0f, unitPos.z - myPos.z),
                new float3(0f, 0f, 1f));
            float3 forwardDir = math.normalizesafe(
                new float3(forward.x, 0f, forward.z),
                new float3(0f, 0f, 1f));
            float3 dir = hit.Knockback.type == KnockbackType.Directional ? forwardDir : radialDir;
            float3 lookDir = math.normalizesafe(
                new float3(myPos.x - unitPos.x, 0f, myPos.z - unitPos.z),
                new float3(forward.x, 0f, forward.z));

            LocalTransform unitTransform = transforms[i];
            unitTransform.Rotation = quaternion.LookRotationSafe(lookDir, math.up());

            bool superArmorBlocked = IsSuperArmorBlocked(entities[i], hit.SuperArmorBreak);
            if (!superArmorBlocked)
            {
                _em.SetComponentData(entities[i], unitTransform);

                if (!TryPreserveAirborneHit(entities[i], hit, hitType, suspendDuration))
                {
                    AttackKnockbackData knockback = hit.Knockback;
                    int prevVersion = _em.GetComponentData<NavAgentKnockback>(entities[i]).HitVersion;
                    _em.SetComponentData(entities[i], new NavAgentKnockback
                    {
                        Velocity        = dir * knockback.force,
                        MotionLockTimer = hit.Down.duration,
                        WakeupTimer     = 0f,
                        Friction        = knockback.friction,
                        InitialSpeed    = knockback.force,
                        HitType         = (int)hitType,
                        SuperArmorBreak = hit.SuperArmorBreak,
                        HitVersion      = prevVersion + 1,
                        IsHeavy         = (byte)(hit.Down.enabled ? 1 : 0)
                    });

                    if (_em.HasComponent<NavAgentLaunch>(entities[i]))
                    {
                        NavAgentLaunch current = _em.GetComponentData<NavAgentLaunch>(entities[i]);
                        if (hit.Launch.enabled && hit.Launch.height > 0f)
                        {
                            // 초기 상승속도 부여 + 공중 진입. 이미 공중이면(repeat 재타격) 지면 기준을 유지하고
                            // 속도만 재부여해 체공을 연장한다(캐릭터 EnterOrRefreshLaunch와 동일 규칙).
                            bool isAirborne = current.Airborne != 0;
                            float groundY = isAirborne ? current.GroundY : unitPos.y;
                            float initialVelocity = LaunchPhysics.InitialVelocity(hit.Launch.height, LaunchPhysics.Gravity);
                            _em.SetComponentData(entities[i], new NavAgentLaunch
                            {
                                Height           = hit.Launch.height,
                                SuspendDuration  = suspendDuration,
                                VerticalVelocity = LaunchPhysics.RefreshVelocityForLaunchHit(current.VerticalVelocity, initialVelocity, isAirborne),
                                GroundY          = groundY,
                                SuspendTimer     = suspendDuration,
                                Airborne         = 1
                            });
                        }
                        else if (suspendDuration > 0f && current.Airborne != 0)
                        {
                            current.SuspendTimer = suspendDuration;
                            _em.SetComponentData(entities[i], current);
                        }
                    }
                }
            }

            if (_em.HasComponent<NavAgentHealth>(entities[i]))
            {
                NavAgentHealth health = _em.GetComponentData<NavAgentHealth>(entities[i]);
                float taken = CombatFormula.ReduceIncomingDamage(settings[i].Defense, finalDamage);
                health.Current -= taken;
                _em.SetComponentData(entities[i], health);
            }

            SpawnHitFeedback(feedbackData, unitPos);
            ApplyForcedAggro(entities[i], attackerEntity);
            didHit = true;
        }

        entities.Dispose();
        transforms.Dispose();
        settings.Dispose();

        return didHit;
    }

    private bool IsSuperArmorBlocked(Entity entity, float superArmorBreak)
    {
        if (!_em.HasComponent<NavAgentAttack>(entity) || !_em.HasComponent<NavAgentAttackProfile>(entity))
            return false;

        NavAgentAttack attack = _em.GetComponentData<NavAgentAttack>(entity);
        if (attack.Phase == NavAttackPhase.Idle)
            return false;

        NavAgentAttackProfile profile = _em.GetComponentData<NavAgentAttackProfile>(entity);
        return profile.SuperArmor > superArmorBreak;
    }

    private bool TryPreserveAirborneHit(Entity entity, in AttackHitInfo hit, HitType hitType, float suspendDuration)
    {
        if (!_em.HasComponent<NavAgentLaunch>(entity))
            return false;

        NavAgentLaunch launch = _em.GetComponentData<NavAgentLaunch>(entity);
        if (launch.Airborne == 0 || (hit.Launch.enabled && hit.Launch.height > 0f))
            return false;

        NavAgentKnockback knockback = _em.GetComponentData<NavAgentKnockback>(entity);
        knockback.HitType = (int)hitType;
        knockback.SuperArmorBreak = hit.SuperArmorBreak;
        knockback.HitVersion++;

        if (hit.Down.enabled)
        {
            knockback.MotionLockTimer = math.max(knockback.MotionLockTimer, hit.Down.duration);
            knockback.WakeupTimer = 0f;
            knockback.IsHeavy = 1;
        }

        _em.SetComponentData(entity, knockback);

        if (suspendDuration > 0f)
        {
            launch.SuspendTimer = math.max(launch.SuspendTimer, suspendDuration);
            _em.SetComponentData(entity, launch);
        }

        return true;
    }

    private static int GetEntityHitKey(Entity entity)
    {
        unchecked
        {
            return (entity.Index * 397) ^ entity.Version;
        }
    }

    // 피격한 잡몹에 강제 어그로를 건다. NavTargetingSystem이 ForcedTimer 동안 공격자를 거리 무시하고 우선 타겟으로 삼는다.
    private void ApplyForcedAggro(Entity target, Entity attacker)
    {
        if (attacker == Entity.Null || !_em.HasComponent<NavAgentCombatTarget>(target))
            return;

        NavAgentCombatTarget combat = _em.GetComponentData<NavAgentCombatTarget>(target);
        combat.ForcedEntity = attacker;
        combat.ForcedTimer = ForcedAggroDuration;
        _em.SetComponentData(target, combat);
    }

    private bool EnsureHitQuery()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;
        if (world == _cachedWorld) return true;

        if (_cachedWorld != null && _cachedWorld.IsCreated)
            _hitQuery.Dispose();

        _cachedWorld = world;
        _em = world.EntityManager;
        _hitQuery = _em.CreateEntityQuery(
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<NavAgentSettings>(),
            ComponentType.ReadWrite<NavAgentKnockback>());
        return true;
    }

    private static void SpawnHitFeedback(SO_Attack_Data data, Vector3 position)
    {
        position.y += HitVfxHeightOffset;
        // 발사체/장판은 호출 측 destroyCancellationToken이 없으므로 풀 토큰(Scene)에 묶이는 None을 쓴다.
        CombatFeedback.PlayHitFeedback(data, position, default);
    }

    public void Dispose()
    {
        if (_cachedWorld != null && _cachedWorld.IsCreated)
            _hitQuery.Dispose();
        _cachedWorld = null;
    }
}

// 한 공격 인스턴스 내에서 동일 대상 중복 타격을 막는 레지스트리.
// key는 콜라이더 InstanceID 또는 엔티티 파생 해시, scope는 메인/추가 히트박스 구분용.
public sealed class AttackHitRegistry
{
    private readonly HashSet<int> _keys = new();

    public void Clear()
    {
        _keys.Clear();
    }

    public bool TryRegister(int key, bool hitSameTargetOnce)
        => TryRegister(key, 0, hitSameTargetOnce);

    public bool TryRegister(int key, int scope, bool hitSameTargetOnce)
    {
        unchecked
        {
            return !hitSameTargetOnce || _keys.Add((key * 397) ^ scope);
        }
    }
}
