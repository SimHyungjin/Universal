using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(MapNavigationPathBuildSystem))]
public partial struct MapNavigationEcsTargetResolveSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton(out MapNavigationBlobComponent navigation) || !navigation.Blob.IsCreated)
            return;

        MapNavigationBlobDataContext context = new(
            navigation.Blob,
            MapNavigationEcsConversion.ToMatrix4x4(navigation.LocalToWorldMatrix),
            MapNavigationEcsConversion.ToMatrix4x4(navigation.WorldToLocalMatrix));

        foreach ((
            RefRW<MapNavEcsTarget> target,
            RefRO<MapNavEcsAgent> agent,
            RefRW<MapNavEcsPathRequest> request,
            RefRW<MapNavEcsPathStatus> status,
            RefRW<MapNavEcsMotionState> motion,
            RefRO<LocalTransform> transform)
            in SystemAPI.Query<
                RefRW<MapNavEcsTarget>,
                RefRO<MapNavEcsAgent>,
                RefRW<MapNavEcsPathRequest>,
                RefRW<MapNavEcsPathStatus>,
                RefRW<MapNavEcsMotionState>,
                RefRO<LocalTransform>>())
        {
            if (target.ValueRO.Dirty == 0)
                continue;

            Vector3 actualStartPosition = transform.ValueRO.Position;
            Vector3 startPosition = actualStartPosition;
            Vector3 targetPosition = target.ValueRO.Position;
            float repathDistance = math.max(0f, agent.ValueRO.TargetRepathDistance);
            if ((request.ValueRO.Pending != 0 || status.ValueRO.Waiting != 0)
                && math.lengthsq(target.ValueRO.Position - target.ValueRO.AcceptedPosition) <= repathDistance * repathDistance)
            {
                target.ValueRW.Dirty = 0;
                continue;
            }

            if (status.ValueRO.HasPath != 0
                && math.lengthsq(target.ValueRO.Position - target.ValueRO.AcceptedPosition) <= repathDistance * repathDistance)
            {
                target.ValueRW.Dirty = 0;
                continue;
            }

            float boundaryTolerance = math.max(0f, agent.ValueRO.BoundaryTolerance);
            if (!TryResolveOrProjectSpace(context, startPosition, boundaryTolerance, out startPosition, out MapNavigationPathSpace startSpace)
                || !TryResolveOrProjectSpace(context, targetPosition, boundaryTolerance, out targetPosition, out MapNavigationPathSpace targetSpace))
            {
                target.ValueRW.Dirty = 0;
                request.ValueRW.Pending = 0;
                status.ValueRW.HasPath = 0;
                status.ValueRW.Waiting = 0;
                status.ValueRW.Failed = 1;
                status.ValueRW.UsedCrossLayerTransition = 0;
                status.ValueRW.PathKind = 0;
                motion.ValueRW.IsMoving = 0;
                motion.ValueRW.CurrentSpeed = 0f;
                motion.ValueRW.Velocity = float3.zero;
                continue;
            }

            request.ValueRW.Pending = 1;
            request.ValueRW.StartPosition = startPosition;
            request.ValueRW.ActualStartPosition = actualStartPosition;
            request.ValueRW.TargetPosition = targetPosition;
            request.ValueRW.StartSpace = startSpace;
            request.ValueRW.TargetSpace = targetSpace;
            target.ValueRW.Dirty = 0;
            target.ValueRW.AcceptedPosition = targetPosition;
            status.ValueRW.Waiting = 1;
            status.ValueRW.Failed = 0;
        }
    }

    private static bool TryResolveOrProjectSpace(
        MapNavigationBlobDataContext context,
        Vector3 worldPosition,
        float boundaryTolerance,
        out Vector3 resolvedWorldPosition,
        out MapNavigationPathSpace space)
    {
        resolvedWorldPosition = worldPosition;
        if (TryResolveSpace(context, resolvedWorldPosition, boundaryTolerance, out space))
            return true;

        if (!MapNavigationQuery.TryProjectToClosestNavigationSpace(
                context,
                worldPosition,
                out Vector3 projected,
                out _,
                out _))
        {
            space = default;
            return false;
        }

        resolvedWorldPosition = projected;
        return TryResolveSpace(context, resolvedWorldPosition, boundaryTolerance, out space);
    }

    private static bool TryResolveSpace(MapNavigationBlobDataContext context, Vector3 worldPosition, float boundaryTolerance, out MapNavigationPathSpace space)
    {
        if (MapNavigationQuery.TryGetNavigationHeight(
                context,
                worldPosition,
                boundaryTolerance,
                -1,
                -1,
                out _,
                out _,
                out int transitionId,
                out int regionId))
        {
            if (transitionId >= 0)
            {
                space = MapNavigationPathSpace.Transition(transitionId);
                return true;
            }

            if (regionId >= 0)
            {
                space = MapNavigationPathSpace.Region(regionId);
                return true;
            }
        }

        space = default;
        return false;
    }
}
