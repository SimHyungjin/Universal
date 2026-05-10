using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(MapNavigationPathBuildSystem))]
public partial struct MapNavigationEcsMovementSystem : ISystem
{
    private struct EcsHeightSample
    {
        public byte IsValid;
        public float3 Position;
        public float Height;
    }

    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        bool hasNavigation = SystemAPI.TryGetSingleton(out MapNavigationBlobComponent navigation) && navigation.Blob.IsCreated;
        MapNavigationBlobDataContext navigationContext = hasNavigation
            ? new MapNavigationBlobDataContext(
                navigation.Blob,
                MapNavigationEcsConversion.ToMatrix4x4(navigation.LocalToWorldMatrix),
                MapNavigationEcsConversion.ToMatrix4x4(navigation.WorldToLocalMatrix))
            : default;

        foreach ((
            RefRO<MapNavEcsAgent> agent,
            RefRW<MapNavEcsMotionState> motion,
            RefRW<MapNavEcsTarget> targetState,
            RefRW<MapNavEcsPathRequest> request,
            RefRW<MapNavEcsPathStatus> status,
            RefRW<LocalTransform> transform,
            DynamicBuffer<MapNavEcsWaypoint> waypoints)
            in SystemAPI.Query<
                RefRO<MapNavEcsAgent>,
                RefRW<MapNavEcsMotionState>,
                RefRW<MapNavEcsTarget>,
                RefRW<MapNavEcsPathRequest>,
                RefRW<MapNavEcsPathStatus>,
                RefRW<LocalTransform>,
                DynamicBuffer<MapNavEcsWaypoint>>())
        {
            float boundaryTolerance = math.max(0f, agent.ValueRO.BoundaryTolerance);
            EcsHeightSample heightSample = default;

            motion.ValueRW.RepathCooldownRemaining = math.max(0f, motion.ValueRO.RepathCooldownRemaining - deltaTime);

            do
            {
                if (status.ValueRO.HasPath == 0 || waypoints.Length == 0)
                {
                    Stop(ref motion.ValueRW);
                    break;
                }

                float3 current = transform.ValueRO.Position;
                AdvancePastStaleWaypoints(ref motion.ValueRW, agent.ValueRO, current, waypoints);
                if (waypoints.Length == 0)
                {
                    status.ValueRW.HasPath = 0;
                    Stop(ref motion.ValueRW);
                    break;
                }

                int index = math.clamp(motion.ValueRO.WaypointIndex, 0, waypoints.Length - 1);
                float3 target = waypoints[index].Position;
                float3 steeringTarget = GetSteeringTarget(agent.ValueRO, current, index, waypoints);
                float3 planarDelta = target - current;
                planarDelta.y = 0f;

                float reachDistance = waypoints[index].Required != 0
                    ? math.max(0.001f, agent.ValueRO.StopDistance)
                    : math.max(agent.ValueRO.StopDistance, agent.ValueRO.WaypointAdvanceDistance);
                if (math.lengthsq(planarDelta) <= reachDistance * reachDistance)
                {
                    index++;
                    if (index >= waypoints.Length)
                    {
                        status.ValueRW.HasPath = 0;
                        motion.ValueRW.WaypointIndex = 0;
                        motion.ValueRW.LastWaypointAnchor = current;
                        Stop(ref motion.ValueRW);
                        break;
                    }

                    motion.ValueRW.WaypointIndex = index;
                    motion.ValueRW.LastWaypointAnchor = current;
                    target = waypoints[index].Position;
                    steeringTarget = GetSteeringTarget(agent.ValueRO, current, index, waypoints);
                    planarDelta = target - current;
                    planarDelta.y = 0f;
                }

                float distance = math.length(planarDelta);
                if (distance <= 0.0001f)
                {
                    Stop(ref motion.ValueRW);
                    break;
                }

                UpdateStuckState(
                    ref motion.ValueRW,
                    ref targetState.ValueRW,
                    ref request.ValueRW,
                    ref status.ValueRW,
                    agent.ValueRO,
                    distance,
                    deltaTime);

                if (targetState.ValueRO.Dirty != 0)
                {
                    status.ValueRW.HasPath = 0;
                    waypoints.Clear();
                    Stop(ref motion.ValueRW);
                    break;
                }

                float moveDistance = math.min(agent.ValueRO.MoveSpeed * deltaTime, distance);
                float3 steering = steeringTarget - current;
                steering.y = 0f;
                if (math.lengthsq(steering) < 0.0001f || math.dot(steering, planarDelta) < 0f)
                    steering = planarDelta;

                float3 direction = math.normalizesafe(steering, planarDelta / distance);
                float3 step = direction * moveDistance;
                if (moveDistance > distance)
                    step = planarDelta;

                float3 nextPosition = current + step;
                if (!CanMoveAgainstObstacles(navigationContext, hasNavigation, current, nextPosition, boundaryTolerance, agent.ValueRO.AgentRadius))
                {
                    if (TryReplanAroundObstacle(
                            navigationContext,
                            hasNavigation,
                            waypoints,
                            current,
                            boundaryTolerance,
                            agent.ValueRO.AgentRadius,
                            agent.ValueRO.StopDistance))
                    {
                        motion.ValueRW.WaypointIndex = 0;
                        motion.ValueRW.LastWaypointAnchor = current;
                        motion.ValueRW.LastDistanceToWaypoint = 0f;
                        break;
                    }

                    AccumulateBlockedRepath(
                        ref motion.ValueRW,
                        ref targetState.ValueRW,
                        ref request.ValueRW,
                        ref status.ValueRW,
                        agent.ValueRO,
                        deltaTime);
                    break;
                }

                if (agent.ValueRO.ConstrainToNavigationSpaces != 0
                    && !CanMoveToConstrainedPosition(navigationContext, hasNavigation, current, nextPosition, boundaryTolerance, ref heightSample))
                {
                    AccumulateBlockedRepath(
                        ref motion.ValueRW,
                        ref targetState.ValueRW,
                        ref request.ValueRW,
                        ref status.ValueRW,
                        agent.ValueRO,
                        deltaTime);
                    break;
                }

                transform.ValueRW.Position = nextPosition;

                if (math.lengthsq(direction) > 0.0001f)
                    transform.ValueRW.Rotation = quaternion.LookRotationSafe(direction, math.up());

                motion.ValueRW.IsMoving = 1;
                motion.ValueRW.CurrentSpeed = deltaTime > 0f ? moveDistance / deltaTime : 0f;
                motion.ValueRW.Velocity = deltaTime > 0f ? step / deltaTime : float3.zero;
            }
            while (false);

            transform.ValueRW.Position = ApplyHeightSnap(
                transform.ValueRO.Position,
                navigationContext,
                hasNavigation,
                agent.ValueRO,
                boundaryTolerance,
                ref heightSample);
        }
    }

    private static void UpdateStuckState(
        ref MapNavEcsMotionState motion,
        ref MapNavEcsTarget target,
        ref MapNavEcsPathRequest request,
        ref MapNavEcsPathStatus status,
        MapNavEcsAgent agent,
        float distanceToWaypoint,
        float deltaTime)
    {
        if (agent.StuckRepathDelay <= 0f || request.Pending != 0 || status.Waiting != 0)
        {
            motion.StuckTimer = 0f;
            motion.LastDistanceToWaypoint = distanceToWaypoint;
            return;
        }

        float progress = motion.LastDistanceToWaypoint - distanceToWaypoint;
        float expectedProgress = math.max(agent.StuckProgressDistance, agent.MoveSpeed * deltaTime * 0.25f);
        if (motion.LastDistanceToWaypoint <= 0f || progress > expectedProgress)
        {
            motion.StuckTimer = 0f;
            motion.LastDistanceToWaypoint = distanceToWaypoint;
            return;
        }

        motion.StuckTimer += deltaTime;
        motion.LastDistanceToWaypoint = distanceToWaypoint;
        if (motion.StuckTimer < agent.StuckRepathDelay || motion.RepathCooldownRemaining > 0f)
            return;

        target.Position = target.AcceptedPosition;
        target.Dirty = 1;
        request.Pending = 0;
        status.Waiting = 0;
        motion.StuckTimer = 0f;
        motion.RepathCooldownRemaining = agent.StuckRepathCooldown;
    }

    private static void Stop(ref MapNavEcsMotionState motion)
    {
        motion.IsMoving = 0;
        motion.CurrentSpeed = 0f;
        motion.StuckTimer = 0f;
        motion.LastDistanceToWaypoint = 0f;
        motion.Velocity = float3.zero;
    }

    private static void AccumulateBlockedRepath(
        ref MapNavEcsMotionState motion,
        ref MapNavEcsTarget target,
        ref MapNavEcsPathRequest request,
        ref MapNavEcsPathStatus status,
        MapNavEcsAgent agent,
        float deltaTime)
    {
        if (agent.StuckRepathDelay <= 0f || request.Pending != 0 || status.Waiting != 0)
            return;

        motion.StuckTimer += deltaTime;
        if (motion.StuckTimer < agent.StuckRepathDelay || motion.RepathCooldownRemaining > 0f)
            return;

        target.Position = target.AcceptedPosition;
        target.Dirty = 1;
        request.Pending = 0;
        status.Waiting = 0;
        motion.StuckTimer = 0f;
        motion.RepathCooldownRemaining = agent.StuckRepathCooldown;
    }

    private static void AdvancePastStaleWaypoints(
        ref MapNavEcsMotionState motion,
        MapNavEcsAgent agent,
        float3 current,
        DynamicBuffer<MapNavEcsWaypoint> waypoints)
    {
        while (waypoints.Length > 1 && motion.WaypointIndex < waypoints.Length)
        {
            MapNavEcsWaypoint waypoint = waypoints[motion.WaypointIndex];
            float3 toCurrent = waypoint.Position - current;
            toCurrent.y = 0f;
            float advanceDistance = waypoint.Required != 0
                ? math.max(0.001f, agent.StopDistance)
                : math.max(agent.StopDistance, agent.WaypointAdvanceDistance);

            if (math.lengthsq(toCurrent) <= advanceDistance * advanceDistance)
            {
                motion.LastWaypointAnchor = waypoint.Position;
                motion.WaypointIndex++;
                continue;
            }

            if (waypoint.Required == 0 && HasPassedCurrentWaypoint(motion.LastWaypointAnchor, waypoint.Position, current))
            {
                motion.LastWaypointAnchor = waypoint.Position;
                motion.WaypointIndex++;
                continue;
            }

            break;
        }

        if (motion.WaypointIndex > 0)
        {
            int removeCount = math.min(motion.WaypointIndex, waypoints.Length);
            if (removeCount > 0)
                waypoints.RemoveRange(0, removeCount);

            motion.WaypointIndex = 0;
        }
    }

    private static bool HasPassedCurrentWaypoint(float3 anchor, float3 waypoint, float3 current)
    {
        float3 segment = waypoint - anchor;
        segment.y = 0f;
        float sqrLength = math.lengthsq(segment);
        if (sqrLength <= 0.0001f)
            return false;

        float3 fromAnchor = current - anchor;
        fromAnchor.y = 0f;
        float t = math.dot(fromAnchor, segment) / sqrLength;
        if (t < 1f)
            return false;

        float3 fromWaypoint = current - waypoint;
        fromWaypoint.y = 0f;
        return math.dot(fromWaypoint, segment) > 0f;
    }

    private static float3 GetSteeringTarget(
        MapNavEcsAgent agent,
        float3 current,
        int index,
        DynamicBuffer<MapNavEcsWaypoint> waypoints)
    {
        if (agent.CornerLookAheadDistance <= 0f || index + 1 >= waypoints.Length)
            return waypoints[index].Position;

        float3 toCurrentWaypoint = waypoints[index].Position - current;
        toCurrentWaypoint.y = 0f;
        float distance = math.length(toCurrentWaypoint);
        if (distance >= agent.CornerLookAheadDistance)
            return waypoints[index].Position;

        float blend = 1f - math.clamp(distance / math.max(0.0001f, agent.CornerLookAheadDistance), 0f, 1f);
        return math.lerp(waypoints[index].Position, waypoints[index + 1].Position, blend);
    }

    private static bool CanMoveAgainstObstacles(
        MapNavigationBlobDataContext context,
        bool hasNavigation,
        float3 current,
        float3 nextPosition,
        float boundaryTolerance,
        float agentRadius)
    {
        if (!hasNavigation)
            return true;

        Vector3 currentWorld = current;
        Vector3 nextWorld = nextPosition;

        if (!MapNavigationQuery.IsInsideAnyObstacle(context, nextWorld, boundaryTolerance))
            return true;

        if (!MapNavigationQuery.IsInsideAnyObstacle(context, currentWorld, boundaryTolerance))
            return false;

        if (!MapNavigationQuery.TryProjectOutOfAnyObstacle(
                context,
                currentWorld,
                boundaryTolerance,
                agentRadius,
                out Vector3 projected))
        {
            return false;
        }

        Vector3 currentDelta = projected - currentWorld;
        Vector3 nextDelta = projected - nextWorld;
        currentDelta.y = 0f;
        nextDelta.y = 0f;
        return nextDelta.sqrMagnitude < currentDelta.sqrMagnitude;
    }

    private static bool TryReplanAroundObstacle(
        MapNavigationBlobDataContext context,
        bool hasNavigation,
        DynamicBuffer<MapNavEcsWaypoint> waypoints,
        float3 current,
        float boundaryTolerance,
        float agentRadius,
        float stopDistance)
    {
        if (!hasNavigation || waypoints.Length == 0)
            return false;

        Vector3 currentWorld = current;
        Vector3 finalTarget = waypoints[waypoints.Length - 1].Position;

        if (!MapNavigationQuery.TryFindContainingRegion(
                context,
                currentWorld,
                boundaryTolerance,
                -1,
                1.5f,
                out MapNavRegionBlob currentRegion))
        {
            return false;
        }

        if (!MapNavigationQuery.TryFindContainingRegion(
                context,
                finalTarget,
                boundaryTolerance,
                -1,
                1.5f,
                out MapNavRegionBlob targetRegion))
        {
            return false;
        }

        if (currentRegion.Id != targetRegion.Id)
            return false;

        MapNavigationBlobPathContext pathContext = new(context);
        if (pathContext.FindInternalRegionPath(
                currentRegion.Id,
                currentWorld,
                finalTarget,
                agentRadius,
                out System.Collections.Generic.List<Vector3> internalPath) != MapNavigationQuery.InternalPathResult.PathFound)
        {
            return false;
        }

        waypoints.Clear();
        Vector3 previous = currentWorld;
        float separationSqr = math.max(stopDistance, 0.0001f) * math.max(stopDistance, 0.0001f);
        for (int i = 0; i < internalPath.Count; i++)
        {
            Vector3 wp = internalPath[i];
            Vector3 delta = wp - previous;
            delta.y = 0f;
            if (delta.sqrMagnitude <= separationSqr)
                continue;

            waypoints.Add(new MapNavEcsWaypoint
            {
                Position = wp,
                Required = 0
            });
            previous = wp;
        }

        return waypoints.Length > 0;
    }

    private static bool CanMoveToConstrainedPosition(
        MapNavigationBlobDataContext context,
        bool hasNavigation,
        float3 current,
        float3 nextPosition,
        float boundaryTolerance,
        ref EcsHeightSample heightSample)
    {
        if (!hasNavigation)
            return true;

        if (TryCacheNavigationHeightSample(context, nextPosition, boundaryTolerance, ref heightSample))
            return true;

        Vector3 currentWorld = current;
        Vector3 nextWorld = nextPosition;
        if (MapNavigationQuery.IsInsideNavigationSpace(context, currentWorld, boundaryTolerance))
            return false;

        if (!MapNavigationQuery.TryProjectToClosestNavigationSpace(
                context,
                currentWorld,
                out Vector3 projected,
                out _,
                out _))
        {
            return false;
        }

        Vector3 currentDelta = projected - currentWorld;
        Vector3 nextDelta = projected - nextWorld;
        currentDelta.y = 0f;
        nextDelta.y = 0f;
        return nextDelta.sqrMagnitude < currentDelta.sqrMagnitude;
    }

    private static bool TryCacheNavigationHeightSample(
        MapNavigationBlobDataContext context,
        float3 worldPosition,
        float boundaryTolerance,
        ref EcsHeightSample heightSample)
    {
        if (MapNavigationQuery.TryGetNavigationHeight(
                context,
                (Vector3)worldPosition,
                boundaryTolerance,
                -1,
                -1,
                out float height,
                out _,
                out _,
                out _))
        {
            heightSample.IsValid = 1;
            heightSample.Position = worldPosition;
            heightSample.Height = height;
            return true;
        }

        heightSample.IsValid = 0;
        return false;
    }

    private static float3 ApplyHeightSnap(
        float3 position,
        MapNavigationBlobDataContext context,
        bool hasNavigation,
        MapNavEcsAgent agent,
        float boundaryTolerance,
        ref EcsHeightSample heightSample)
    {
        if (!hasNavigation)
            return position;

        if (heightSample.IsValid != 0
            && math.lengthsq(heightSample.Position - position) <= 0.000001f)
        {
            position.y = ToWorldHeight(context, heightSample.Height) + agent.HeightOffset;
            heightSample.IsValid = 0;
            return position;
        }

        if (MapNavigationQuery.TryGetNavigationHeight(
                context,
                (Vector3)position,
                boundaryTolerance,
                -1,
                -1,
                out float height,
                out _,
                out _,
                out _))
        {
            position.y = ToWorldHeight(context, height) + agent.HeightOffset;
        }

        return position;
    }

    private static float ToWorldHeight(MapNavigationBlobDataContext context, float localHeight)
    {
        return context.LocalToWorldMatrix.MultiplyPoint3x4(new Vector3(0f, localHeight, 0f)).y;
    }
}
