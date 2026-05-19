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

    public bool Process(SO_AttackData data, Transform attacker)
    {
        if (!EnsureHitQuery()) return false;

        AttackHitboxData hitbox = data.Hitbox;
        float3 center = (attacker.position
            + attacker.forward * hitbox.offset
            + Vector3.up * hitbox.height);
        float radiusSq = hitbox.radius * hitbox.radius;
        float3 myPos = attacker.position;

        NativeArray<Entity> entities = _hitQuery.ToEntityArray(Allocator.Temp);
        NativeArray<LocalTransform> transforms = _hitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

        bool didHit = false;
        for (int i = 0; i < entities.Length; i++)
        {
            float3 unitPos = transforms[i].Position;
            if (math.distancesq(center, unitPos) > radiusSq) continue;

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
            _em.SetComponentData(entities[i], unitTransform);

            AttackKnockbackData knockback = data.Knockback;
            int prevVersion = _em.GetComponentData<NavAgentKnockback>(entities[i]).HitVersion;
            _em.SetComponentData(entities[i], new NavAgentKnockback
            {
                Velocity        = dir * knockback.force,
                Timer           = knockback.duration,
                MotionLockTimer = data.Hitstun.duration,
                Friction        = knockback.friction,
                InitialSpeed    = knockback.force,
                HitType         = (int)data.HitType,
                SuperArmorBreak = data.SuperArmorBreak,
                HitVersion      = prevVersion + 1
            });

            SpawnHitFeedback(data, unitPos);
            didHit = true;
        }

        entities.Dispose();
        transforms.Dispose();

        return didHit;
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
            ComponentType.ReadWrite<NavAgentKnockback>());
        return true;
    }

    private void OnDestroy()
    {
        if (_cachedWorld != null && _cachedWorld.IsCreated)
            _hitQuery.Dispose();
    }

    private void SpawnHitFeedback(SO_AttackData data, Vector3 position)
        => CombatFeedback.PlayHitFeedback(data, position, destroyCancellationToken);
}
