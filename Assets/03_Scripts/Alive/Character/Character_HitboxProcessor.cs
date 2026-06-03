using MapNav.Ecs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[DisallowMultipleComponent]
public class Character_HitboxProcessor : MonoBehaviour, IHitboxProcessor
{
    private EntityManager _em;
    private EntityQuery   _hitQuery;
    private World         _cachedWorld;
    private Character_Vitals _vitals;

    // 공격자(이 컴포넌트가 붙은 캐릭터)의 진영. 잡몹은 이와 다른 진영만 타격한다.
    private NavFaction AttackerFaction
    {
        get
        {
            if (_vitals == null) _vitals = GetComponent<Character_Vitals>();
            return _vitals != null ? _vitals.Faction : NavFaction.Ally;
        }
    }

    // ProcessInstance에 주입되는 전투 파라미터. SO_Attack_Data 또는 AttackExtraHitResult에서 생성.
    private readonly struct HitContext
    {
        public readonly HitType HitType;
        public readonly AttackKnockbackData Knockback;
        public readonly AttackLaunchData Launch;
        public readonly AttackDownData Down;
        public readonly float SuperArmorBreak;
        public readonly float SuspendDuration;
        public readonly bool HitSameTargetOnce;

        public HitContext(SO_Attack_Data data, float suspendDuration)
        {
            HitType            = data.HitType;
            Knockback          = data.Knockback;
            Launch             = data.Launch;
            Down               = data.Down;
            SuperArmorBreak    = data.SuperArmorBreak;
            SuspendDuration    = suspendDuration;
            HitSameTargetOnce  = data.Repeat.hitSameTargetOnce;
        }

        public HitContext(AttackExtraHitResult r, float superArmorBreak, float suspendDuration, bool hitSameTargetOnce)
        {
            HitType            = r.hitType;
            Knockback          = r.knockback;
            Launch             = r.launch;
            Down               = r.down;
            SuperArmorBreak    = superArmorBreak;
            SuspendDuration    = suspendDuration;
            HitSameTargetOnce  = hitSameTargetOnce;
        }
    }

    public bool Process(SO_Attack_Data data, Transform attacker, AttackHitRegistry hitRegistry, float finalDamage, float targetSuspendDuration)
    {
        if (!EnsureHitQuery()) return false;

        NativeArray<Entity> entities = _hitQuery.ToEntityArray(Allocator.Temp);
        NativeArray<LocalTransform> transforms = _hitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        NativeArray<NavAgentSettings> settings = _hitQuery.ToComponentDataArray<NavAgentSettings>(Allocator.Temp);

        HitContext ctx = new HitContext(data, targetSuspendDuration);
        bool didHit = ProcessInstance(ctx, data.Hitbox, data.Shape, attacker, hitRegistry, 2, finalDamage,
            data, entities, transforms, settings);

        entities.Dispose();
        transforms.Dispose();
        settings.Dispose();

        return didHit;
    }

    public bool ProcessExtra(SO_Attack_Data data, AttackExtraHit extra, int extraIndex, Transform attacker, AttackHitRegistry hitRegistry, float finalDamage, float targetSuspendDuration)
    {
        if (!EnsureHitQuery()) return false;

        NativeArray<Entity> entities = _hitQuery.ToEntityArray(Allocator.Temp);
        NativeArray<LocalTransform> transforms = _hitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        NativeArray<NavAgentSettings> settings = _hitQuery.ToComponentDataArray<NavAgentSettings>(Allocator.Temp);

        HitContext ctx = new HitContext(extra.hitResult, data.SuperArmorBreak, targetSuspendDuration, extra.repeat.hitSameTargetOnce);

        // extraIndex + 10을 scope으로 사용해 메인 hitbox(scope=2)와 레지스트리 충돌 방지
        int scope = extraIndex + 10;
        bool didHit = ProcessInstance(ctx, extra.hitbox, extra.shape, attacker, hitRegistry, scope, finalDamage,
            data, entities, transforms, settings);

        entities.Dispose();
        transforms.Dispose();
        settings.Dispose();

        return didHit;
    }

    private bool ProcessInstance(
        in HitContext ctx,
        AttackHitboxData hitbox,
        AttackShapeData shape,
        Transform attacker,
        AttackHitRegistry hitRegistry,
        int registryScope,
        float finalDamage,
        SO_Attack_Data feedbackData,
        NativeArray<Entity> entities,
        NativeArray<LocalTransform> transforms,
        NativeArray<NavAgentSettings> settings)
    {
        float3 center = AttackShapeUtility.GetQueryCenter(attacker.position, attacker.forward, hitbox, shape);
        float queryRadius = AttackShapeUtility.GetQueryRadius(hitbox, shape);
        float3 myPos = attacker.position;
        NavFaction attackerFaction = AttackerFaction;

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
            if (!AttackShapeUtility.Contains(attacker.position, attacker.forward, judgePos, targetRadius, hitbox, shape))
                continue;
            if (hitRegistry != null && !hitRegistry.TryRegister(GetEntityHitKey(entities[i]), registryScope, ctx.HitSameTargetOnce))
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
                new float3(attacker.forward.x, 0f, attacker.forward.z),
                new float3(0f, 0f, 1f));
            float3 dir = ctx.Knockback.type == KnockbackType.Directional ? forwardDir : radialDir;
            float3 lookDir = math.normalizesafe(
                new float3(myPos.x - unitPos.x, 0f, myPos.z - unitPos.z),
                new float3(attacker.forward.x, 0f, attacker.forward.z));

            LocalTransform unitTransform = transforms[i];
            unitTransform.Rotation = quaternion.LookRotationSafe(lookDir, math.up());

            bool superArmorBlocked = IsSuperArmorBlocked(entities[i], ctx.SuperArmorBreak);
            if (!superArmorBlocked)
            {
                _em.SetComponentData(entities[i], unitTransform);

                if (!TryPreserveAirborneHit(entities[i], ctx))
                {
                AttackKnockbackData knockback = ctx.Knockback;
                int prevVersion = _em.GetComponentData<NavAgentKnockback>(entities[i]).HitVersion;
                _em.SetComponentData(entities[i], new NavAgentKnockback
                {
                    Velocity        = dir * knockback.force,
                    MotionLockTimer = ctx.Down.duration,
                    WakeupTimer     = 0f,
                    Friction        = knockback.friction,
                    InitialSpeed    = knockback.force,
                    HitType         = (int)ctx.HitType,
                    SuperArmorBreak = ctx.SuperArmorBreak,
                    HitVersion      = prevVersion + 1,
                    IsHeavy         = (byte)(ctx.Down.enabled ? 1 : 0)
                });

                if (_em.HasComponent<NavAgentLaunch>(entities[i]))
                {
                    NavAgentLaunch current = _em.GetComponentData<NavAgentLaunch>(entities[i]);
                    if (ctx.Launch.enabled && ctx.Launch.height > 0f)
                    {
                        // 초기 상승속도 부여 + 공중 진입. 이미 공중이면(repeat 재타격) 지면 기준을 유지하고
                        // 속도만 재부여해 체공을 연장한다(캐릭터 EnterOrRefreshLaunch와 동일 규칙).
                        bool isAirborne = current.Airborne != 0;
                        float groundY = isAirborne ? current.GroundY : unitPos.y;
                        float initialVelocity = LaunchPhysics.InitialVelocity(ctx.Launch.height, LaunchPhysics.Gravity);
                        _em.SetComponentData(entities[i], new NavAgentLaunch
                        {
                            Height           = ctx.Launch.height,
                            SuspendDuration  = ctx.SuspendDuration,
                            VerticalVelocity = LaunchPhysics.RefreshVelocityForLaunchHit(current.VerticalVelocity, initialVelocity, isAirborne),
                            GroundY          = groundY,
                            SuspendTimer     = ctx.SuspendDuration,
                            Airborne         = 1
                        });
                    }
                    else if (ctx.SuspendDuration > 0f && current.Airborne != 0)
                    {
                        current.SuspendTimer = ctx.SuspendDuration;
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
            didHit = true;
        }

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

    private bool TryPreserveAirborneHit(Entity entity, in HitContext ctx)
    {
        if (!_em.HasComponent<NavAgentLaunch>(entity))
            return false;

        NavAgentLaunch launch = _em.GetComponentData<NavAgentLaunch>(entity);
        if (launch.Airborne == 0 || (ctx.Launch.enabled && ctx.Launch.height > 0f))
            return false;

        if (ctx.Down.enabled)
        {
            NavAgentKnockback knockback = _em.GetComponentData<NavAgentKnockback>(entity);
            knockback.MotionLockTimer = math.max(knockback.MotionLockTimer, ctx.Down.duration);
            knockback.WakeupTimer = 0f;
            knockback.HitType = (int)ctx.HitType;
            knockback.SuperArmorBreak = ctx.SuperArmorBreak;
            knockback.HitVersion++;
            knockback.IsHeavy = 1;
            _em.SetComponentData(entity, knockback);
        }

        if (ctx.SuspendDuration > 0f)
        {
            launch.SuspendTimer = math.max(launch.SuspendTimer, ctx.SuspendDuration);
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

    private void OnDestroy()
    {
        if (_cachedWorld != null && _cachedWorld.IsCreated)
            _hitQuery.Dispose();
    }

    // hit VFX는 캐릭터의 가슴/배 높이에서 떠야 자연스럽다. 발 위치 기준으로 +0.5m 보정.
    private const float HitVfxHeightOffset = 0.5f;

    private void SpawnHitFeedback(SO_Attack_Data data, Vector3 position)
    {
        position.y += HitVfxHeightOffset;
        CombatFeedback.PlayHitFeedback(data, position, destroyCancellationToken);
    }
}
