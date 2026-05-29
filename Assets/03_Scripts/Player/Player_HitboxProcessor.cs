using MapNav.Ecs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[DisallowMultipleComponent]
public class Player_HitboxProcessor : MonoBehaviour, IHitboxProcessor
{
    private EntityManager _em;
    private EntityQuery   _hitQuery;
    private World         _cachedWorld;

    // ProcessInstance에 주입되는 전투 파라미터. SO_AttackData 또는 AttackExtraHitResult에서 생성.
    private readonly struct HitContext
    {
        public readonly HitType HitType;
        public readonly AttackKnockbackData Knockback;
        public readonly AttackLaunchData Launch;
        public readonly AttackDownData Down;
        public readonly float SuperArmorBreak;
        public readonly float SuspendDuration;
        public readonly bool HitSameTargetOnce;

        public HitContext(SO_AttackData data, float suspendDuration)
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

    public bool Process(SO_AttackData data, Transform attacker, AttackHitRegistry hitRegistry, float finalDamage, float targetSuspendDuration)
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

    public bool ProcessExtra(SO_AttackData data, AttackExtraHit extra, int extraIndex, Transform attacker, AttackHitRegistry hitRegistry, float finalDamage, float targetSuspendDuration)
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
        SO_AttackData feedbackData,
        NativeArray<Entity> entities,
        NativeArray<LocalTransform> transforms,
        NativeArray<NavAgentSettings> settings)
    {
        float3 center = AttackShapeUtility.GetQueryCenter(attacker.position, attacker.forward, hitbox, shape);
        float queryRadius = AttackShapeUtility.GetQueryRadius(hitbox, shape);
        float3 myPos = attacker.position;

        bool didHit = false;
        for (int i = 0; i < entities.Length; i++)
        {
            float3 unitPos = transforms[i].Position;
            if (_em.HasComponent<NavAgentLaunch>(entities[i]))
                unitPos.y += _em.GetComponentData<NavAgentLaunch>(entities[i]).VisualYOffset;

            float targetRadius = math.max(0f, settings[i].AgentRadius);
            float expandedQueryRadius = queryRadius + targetRadius;
            if (math.distancesq(center, unitPos) > expandedQueryRadius * expandedQueryRadius) continue;
            if (!AttackShapeUtility.Contains(attacker.position, attacker.forward, unitPos, targetRadius, hitbox, shape))
                continue;
            if (hitRegistry != null && !hitRegistry.TryRegister(GetEntityHitKey(entities[i]), registryScope, ctx.HitSameTargetOnce))
                continue;

            // 사망 연출 중인 적은 더 이상 타격 대상이 아니다.
            if (_em.HasComponent<NavAgentDeath>(entities[i]) &&
                _em.GetComponentData<NavAgentDeath>(entities[i]).Dying != 0)
                continue;

            // 플레이어는 적군 진영만 타격한다.
            if (_em.HasComponent<NavAgentFaction>(entities[i]) &&
                _em.GetComponentData<NavAgentFaction>(entities[i]).Faction != NavFaction.Enemy)
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

                AttackKnockbackData knockback = ctx.Knockback;
                int prevVersion = _em.GetComponentData<NavAgentKnockback>(entities[i]).HitVersion;
                _em.SetComponentData(entities[i], new NavAgentKnockback
                {
                    Velocity        = dir * knockback.force,
                    Timer           = knockback.duration,
                    MotionLockTimer = ctx.Down.duration,
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
                    if (ctx.Launch.enabled)
                    {
                        _em.SetComponentData(entities[i], new NavAgentLaunch
                        {
                            Height          = ctx.Launch.height,
                            Duration        = ctx.Launch.duration,
                            SuspendDuration = ctx.SuspendDuration,
                            SuspendAtApex   = (byte)(ctx.SuspendDuration > 0f ? 1 : 0),
                            Elapsed         = 0f,
                            FreezeTimer     = 0f
                        });
                    }
                    else if (ctx.SuspendDuration > 0f && current.Height > 0f)
                    {
                        current.FreezeTimer = ctx.SuspendDuration;
                        _em.SetComponentData(entities[i], current);
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

    private void SpawnHitFeedback(SO_AttackData data, Vector3 position)
    {
        position.y += HitVfxHeightOffset;
        CombatFeedback.PlayHitFeedback(data, position, destroyCancellationToken);
    }
}
