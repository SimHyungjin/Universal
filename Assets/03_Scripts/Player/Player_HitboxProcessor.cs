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

    public bool Process(SO_AttackData data, Transform attacker, AttackHitRegistry hitRegistry, float finalDamage, float targetSuspendDuration)
    {
        if (!EnsureHitQuery()) return false;

        NativeArray<Entity> entities = _hitQuery.ToEntityArray(Allocator.Temp);
        NativeArray<LocalTransform> transforms = _hitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        NativeArray<NavAgentSettings> settings = _hitQuery.ToComponentDataArray<NavAgentSettings>(Allocator.Temp);

        bool didHit = ProcessInstance(data, data.Hitbox, data.Shape, attacker, hitRegistry, finalDamage, targetSuspendDuration,
            entities, transforms, settings);

        AttackExtraHit[] extras = data.AdditionalHits;
        if (extras != null)
        {
            for (int i = 0; i < extras.Length; i++)
                didHit |= ProcessInstance(data, extras[i].hitbox, extras[i].shape, attacker, hitRegistry, finalDamage, targetSuspendDuration,
                    entities, transforms, settings);
        }

        entities.Dispose();
        transforms.Dispose();
        settings.Dispose();

        return didHit;
    }

    private bool ProcessInstance(
        SO_AttackData data,
        AttackHitboxData hitbox,
        AttackShapeData shape,
        Transform attacker,
        AttackHitRegistry hitRegistry,
        float finalDamage,
        float targetSuspendDuration,
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
            if (hitRegistry != null && !hitRegistry.TryRegister(GetEntityHitKey(entities[i]), 2, hitbox.hitSameTargetOnce))
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
            float3 dir = data.Knockback.type == KnockbackType.Directional ? forwardDir : radialDir;
            float3 lookDir = math.normalizesafe(
                new float3(myPos.x - unitPos.x, 0f, myPos.z - unitPos.z),
                new float3(attacker.forward.x, 0f, attacker.forward.z));

            LocalTransform unitTransform = transforms[i];
            unitTransform.Rotation = quaternion.LookRotationSafe(lookDir, math.up());

            bool superArmorBlocked = IsSuperArmorBlocked(entities[i], data.SuperArmorBreak);
            if (!superArmorBlocked)
            {
                _em.SetComponentData(entities[i], unitTransform);

                AttackKnockbackData knockback = data.Knockback;
                int prevVersion = _em.GetComponentData<NavAgentKnockback>(entities[i]).HitVersion;
                _em.SetComponentData(entities[i], new NavAgentKnockback
                {
                    Velocity        = dir * knockback.force,
                    Timer           = knockback.duration,
                    MotionLockTimer = data.Down.duration,
                    Friction        = knockback.friction,
                    InitialSpeed    = knockback.force,
                    HitType         = (int)data.HitType,
                    SuperArmorBreak = data.SuperArmorBreak,
                    HitVersion      = prevVersion + 1,
                    IsHeavy         = (byte)(data.Down.enabled ? 1 : 0)
                });

                if (_em.HasComponent<NavAgentLaunch>(entities[i]))
                {
                    NavAgentLaunch current = _em.GetComponentData<NavAgentLaunch>(entities[i]);
                    if (data.Launch.enabled)
                    {
                        _em.SetComponentData(entities[i], new NavAgentLaunch
                        {
                            Height          = data.Launch.height,
                            Duration        = data.Launch.duration,
                            SuspendDuration = targetSuspendDuration,
                            SuspendAtApex   = (byte)(targetSuspendDuration > 0f ? 1 : 0),
                            Elapsed         = 0f,
                            FreezeTimer     = 0f
                        });
                    }
                    else if (targetSuspendDuration > 0f && current.Height > 0f)
                    {
                        current.FreezeTimer = targetSuspendDuration;
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

            SpawnHitFeedback(data, unitPos);
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
