using MapNav.Ecs;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

// Character_AttackController의 자동 조준(오토에임) 분리분. 필드는 본체와 공유한다(partial).
// GameObject 적(IHitTarget)과 ECS 잡몹을 함께 훑어 최근접 적을 찾는다. EntityQuery 라이프사이클은
// EnsureAutoAimQuery가 월드 캐시로 관리하고, Dispose는 본체 OnDestroy에서 함께 처리한다.
public partial class Character_AttackController
{
    private readonly Collider[] _autoAimOverlapBuffer = new Collider[128];
    private EntityManager _em;
    private EntityQuery   _autoAimQuery;
    private World         _cachedWorld;

    private Vector3 FindAutoAimDirection(SO_Attack_Data data)
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

        int hitCount = Physics.OverlapSphereNonAlloc(myPos, range, _autoAimOverlapBuffer);
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _autoAimOverlapBuffer[i];
            if (!col.TryGetComponent(out IHitTarget target) || !target.IsHittable) continue;
            if (!IsHostileHitTarget(col)) continue;
            Vector3 diff = col.ClosestPoint(myPos) - myPos;
            diff.y = 0f;
            if (diff.sqrMagnitude < 0.0001f) continue;
            if (Vector3.Dot(diff.normalized, forward) < -0.5f) continue;
            float dist = diff.sqrMagnitude;
            if (dist >= bestDist) continue;
            bestDist = dist;
            best = diff.normalized;
        }

        if (EnsureAutoAimQuery())
        {
            NativeArray<LocalTransform> transforms = _autoAimQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            NativeArray<NavAgentDeath> deaths = _autoAimQuery.ToComponentDataArray<NavAgentDeath>(Allocator.Temp);
            NativeArray<NavAgentFaction> factions = _autoAimQuery.ToComponentDataArray<NavAgentFaction>(Allocator.Temp);
            NativeArray<NavAgentSettings> settings = _autoAimQuery.ToComponentDataArray<NavAgentSettings>(Allocator.Temp);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (deaths[i].Dying != 0) continue;
                if (factions[i].Faction == AttackerFaction) continue;
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

    // 번개형 장판의 타겟 위치. 전방(조준 반영) 사거리 내 최근접 적 발밑을 우선, 없으면 전방 끝점.
    private Vector3 ResolveAimTargetPosition(NavFaction faction, float maxRange)
    {
        Vector3 myPos = transform.position;
        float searchRange = maxRange > 0f ? maxRange : 9999f;
        float bestDistSq = float.MaxValue;
        Vector3 best = Vector3.zero;
        bool found = false;

        // GameObject 적 (장수·파괴물 등)
        int hitCount = Physics.OverlapSphereNonAlloc(myPos, searchRange, _autoAimOverlapBuffer);
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _autoAimOverlapBuffer[i];
            if (!col.TryGetComponent(out IHitTarget target) || !target.IsHittable) continue;
            if (!IsHostileHitTarget(col)) continue;
            Vector3 p = col.transform.position;
            Vector3 diff = p - myPos; diff.y = 0f;
            float d = diff.sqrMagnitude;
            if (d >= bestDistSq) continue;
            bestDistSq = d; best = p; found = true;
        }

        // ECS 잡몹
        if (EnsureAutoAimQuery())
        {
            NativeArray<LocalTransform> transforms = _autoAimQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            NativeArray<NavAgentDeath> deaths = _autoAimQuery.ToComponentDataArray<NavAgentDeath>(Allocator.Temp);
            NativeArray<NavAgentFaction> factions = _autoAimQuery.ToComponentDataArray<NavAgentFaction>(Allocator.Temp);
            float rangeSq = searchRange * searchRange;
            for (int i = 0; i < transforms.Length; i++)
            {
                if (deaths[i].Dying != 0) continue;
                if (factions[i].Faction == faction) continue;
                var f = transforms[i].Position;
                Vector3 pos = new Vector3(f.x, f.y, f.z);
                Vector3 diff = pos - myPos; diff.y = 0f;
                float d = diff.sqrMagnitude;
                if (d > rangeSq || d >= bestDistSq) continue;
                bestDistSq = d; best = pos; found = true;
            }
            transforms.Dispose();
            deaths.Dispose();
            factions.Dispose();
        }

        if (found) return best;

        Vector3 dir = transform.forward; dir.y = 0f;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        return myPos + dir * (maxRange > 0f ? maxRange : 0f);
    }
}
