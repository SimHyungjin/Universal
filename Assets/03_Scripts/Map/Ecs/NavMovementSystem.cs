using MapNav.Core;
using MapNav.Data;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MapNav.Ecs
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NavPathBuildSystem))]
    [BurstCompile]
    public partial struct NavMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<NavBlobReference>(out NavBlobReference navRef) || !navRef.Blob.IsCreated)
                return;

            NavContext ctx = new NavContext(navRef.Blob, navRef.LocalToWorld, navRef.WorldToLocal);
            float deltaTime = SystemAPI.Time.DeltaTime;

            new NavMovementJob
            {
                Ctx = ctx,
                DeltaTime = deltaTime
            }.ScheduleParallel();
        }
    }

    [BurstCompile]
    public partial struct NavMovementJob : IJobEntity
    {
        public NavContext Ctx;
        public float DeltaTime;

        public void Execute(
            in NavAgentSettings settings,
            ref NavAgentMotion motion,
            ref NavAgentTarget target,
            ref NavAgentPathRequest request,
            ref NavAgentPathStatus status,
            ref LocalTransform transform,
            DynamicBuffer<NavAgentWaypoint> waypoints)
        {
            motion.RepathCooldownRemaining = math.max(0f, motion.RepathCooldownRemaining - DeltaTime);

            if (status.HasPath == 0 || waypoints.Length == 0)
            {
                Stop(ref motion);
                ApplyHeightSnap(ref transform, in settings);
                return;
            }

            float3 current = transform.Position;
            AdvancePastStaleWaypoints(ref motion, in settings, current, waypoints);

            if (motion.WaypointIndex >= waypoints.Length)
            {
                status.HasPath = 0;
                motion.WaypointIndex = 0;
                motion.LastWaypointAnchor = current;
                Stop(ref motion);
                ApplyHeightSnap(ref transform, in settings);
                return;
            }

            int index = motion.WaypointIndex;
            float3 wp = waypoints[index].Position;
            float3 steeringTarget = GetSteeringTarget(in settings, current, index, waypoints);
            float3 planarDelta = wp - current;
            planarDelta.y = 0f;

            float reachDistance = GetReachDistance(in settings, waypoints[index]);
            if (math.lengthsq(planarDelta) <= reachDistance * reachDistance)
            {
                index++;
                if (index >= waypoints.Length)
                {
                    status.HasPath = 0;
                    motion.WaypointIndex = 0;
                    motion.LastWaypointAnchor = current;
                    Stop(ref motion);
                    ApplyHeightSnap(ref transform, in settings);
                    return;
                }
                motion.WaypointIndex = index;
                motion.LastWaypointAnchor = current;
                wp = waypoints[index].Position;
                steeringTarget = GetSteeringTarget(in settings, current, index, waypoints);
                planarDelta = wp - current;
                planarDelta.y = 0f;
                reachDistance = GetReachDistance(in settings, waypoints[index]);
            }

            float distance = math.length(planarDelta);
            if (distance <= 1e-4f)
            {
                Stop(ref motion);
                ApplyHeightSnap(ref transform, in settings);
                return;
            }

            UpdateStuckState(ref motion, ref target, ref request, ref status, in settings, distance);
            if (target.Dirty != 0)
            {
                status.HasPath = 0;
                waypoints.Clear();
                Stop(ref motion);
                ApplyHeightSnap(ref transform, in settings);
                return;
            }

            float moveDistance = math.min(settings.MoveSpeed * DeltaTime, distance);
            float3 steering = steeringTarget - current;
            steering.y = 0f;
            if (math.lengthsq(steering) < 1e-4f || math.dot(steering, planarDelta) < 0f)
                steering = planarDelta;

            float3 direction = math.normalizesafe(steering, planarDelta / distance);
            float3 step = direction * moveDistance;
            if (moveDistance > distance)
                step = planarDelta;

            float3 nextPosition = current + step;

            if (!CanMoveToConstrainedPosition(in Ctx, current, nextPosition, wp, settings.BoundaryTolerance, reachDistance))
            {
                AccumulateBlockedRepath(ref motion, ref target, ref request, ref status, in settings);
                ApplyHeightSnap(ref transform, in settings);
                return;
            }

            transform.Position = nextPosition;

            if (math.lengthsq(direction) > 1e-4f)
                transform.Rotation = quaternion.LookRotationSafe(direction, math.up());

            motion.IsMoving = 1;
            motion.CurrentSpeed = DeltaTime > 0f ? moveDistance / DeltaTime : 0f;
            motion.Velocity = DeltaTime > 0f ? step / DeltaTime : float3.zero;

            ApplyHeightSnap(ref transform, in settings);
        }

        private static void Stop(ref NavAgentMotion motion)
        {
            motion.IsMoving = 0;
            motion.CurrentSpeed = 0f;
            motion.StuckTimer = 0f;
            motion.LastDistanceToWaypoint = 0f;
            motion.Velocity = float3.zero;
        }

        private void ApplyHeightSnap(ref LocalTransform transform, in NavAgentSettings settings)
        {
            if (NavQuery.TryGetHeight(in Ctx, transform.Position, settings.BoundaryTolerance, out float worldHeight))
            {
                float3 p = transform.Position;
                p.y = worldHeight + settings.HeightOffset;
                transform.Position = p;
            }
        }

        private static void AdvancePastStaleWaypoints(
            ref NavAgentMotion motion,
            in NavAgentSettings settings,
            float3 current,
            DynamicBuffer<NavAgentWaypoint> waypoints)
        {
            while (motion.WaypointIndex < waypoints.Length)
            {
                NavAgentWaypoint wp = waypoints[motion.WaypointIndex];
                float3 toCurrent = wp.Position - current;
                toCurrent.y = 0f;
                float advanceDistance = GetReachDistance(in settings, wp);
                float advanceSqr = advanceDistance * advanceDistance;

                if (math.lengthsq(toCurrent) <= advanceSqr)
                {
                    motion.LastWaypointAnchor = wp.Position;
                    motion.WaypointIndex++;
                    continue;
                }

                if (wp.Required == 0 && HasPassedCurrentWaypoint(motion.LastWaypointAnchor, wp.Position, current))
                {
                    motion.LastWaypointAnchor = wp.Position;
                    motion.WaypointIndex++;
                    continue;
                }

                break;
            }
        }

        private static bool HasPassedCurrentWaypoint(float3 anchor, float3 waypoint, float3 current)
        {
            float3 segment = waypoint - anchor;
            segment.y = 0f;
            float lenSq = math.lengthsq(segment);
            if (lenSq <= 1e-4f) return false;

            float3 fromAnchor = current - anchor;
            fromAnchor.y = 0f;
            float t = math.dot(fromAnchor, segment) / lenSq;
            if (t < 1f - 1e-4f) return false;

            float3 fromWp = current - waypoint;
            fromWp.y = 0f;
            return math.dot(fromWp, segment) >= -1e-4f;
        }

        private static float GetReachDistance(in NavAgentSettings settings, in NavAgentWaypoint waypoint)
        {
            return waypoint.Required != 0
                ? math.max(1e-4f, settings.StopDistance)
                : math.max(settings.StopDistance, settings.WaypointAdvanceDistance);
        }

        private static float3 GetSteeringTarget(
            in NavAgentSettings settings,
            float3 current,
            int index,
            DynamicBuffer<NavAgentWaypoint> waypoints)
        {
            if (settings.CornerLookAheadDistance <= 0f || index + 1 >= waypoints.Length)
                return waypoints[index].Position;

            float3 toCurrent = waypoints[index].Position - current;
            toCurrent.y = 0f;
            float distance = math.length(toCurrent);
            if (distance >= settings.CornerLookAheadDistance)
                return waypoints[index].Position;

            float blend = 1f - math.clamp(distance / math.max(1e-4f, settings.CornerLookAheadDistance), 0f, 1f);
            return math.lerp(waypoints[index].Position, waypoints[index + 1].Position, blend);
        }

        private void UpdateStuckState(
            ref NavAgentMotion motion,
            ref NavAgentTarget target,
            ref NavAgentPathRequest request,
            ref NavAgentPathStatus status,
            in NavAgentSettings settings,
            float distanceToWaypoint)
        {
            if (settings.StuckRepathDelay <= 0f || request.Pending != 0 || status.Waiting != 0)
            {
                motion.StuckTimer = 0f;
                motion.LastDistanceToWaypoint = distanceToWaypoint;
                return;
            }

            float progress = motion.LastDistanceToWaypoint - distanceToWaypoint;
            float expectedProgress = math.max(settings.StuckProgressDistance, settings.MoveSpeed * DeltaTime * 0.25f);
            if (motion.LastDistanceToWaypoint <= 0f || progress > expectedProgress)
            {
                motion.StuckTimer = 0f;
                motion.LastDistanceToWaypoint = distanceToWaypoint;
                return;
            }

            motion.StuckTimer += DeltaTime;
            motion.LastDistanceToWaypoint = distanceToWaypoint;
            if (motion.StuckTimer < settings.StuckRepathDelay || motion.RepathCooldownRemaining > 0f)
                return;

            TriggerStuckRepath(ref motion, ref target, ref request, ref status, in settings);
        }

        private void AccumulateBlockedRepath(
            ref NavAgentMotion motion,
            ref NavAgentTarget target,
            ref NavAgentPathRequest request,
            ref NavAgentPathStatus status,
            in NavAgentSettings settings)
        {
            if (settings.StuckRepathDelay <= 0f || request.Pending != 0 || status.Waiting != 0)
                return;

            motion.StuckTimer += DeltaTime;
            if (motion.StuckTimer < settings.StuckRepathDelay || motion.RepathCooldownRemaining > 0f)
                return;

            TriggerStuckRepath(ref motion, ref target, ref request, ref status, in settings);
        }

        private static void TriggerStuckRepath(
            ref NavAgentMotion motion,
            ref NavAgentTarget target,
            ref NavAgentPathRequest request,
            ref NavAgentPathStatus status,
            in NavAgentSettings settings)
        {
            motion.StuckRetryCount++;
            motion.StuckTimer = 0f;
            motion.RepathCooldownRemaining = settings.StuckRepathCooldown;

            if (settings.StuckRetryLimit > 0 && motion.StuckRetryCount >= settings.StuckRetryLimit)
            {
                request.Pending = 0;
                status.HasPath = 0;
                status.Waiting = 0;
                status.Failed = 1;
                target.Dirty = 0;
                motion.IsMoving = 0;
                motion.CurrentSpeed = 0f;
                motion.Velocity = float3.zero;
                return;
            }

            target.Position = target.AcceptedPosition;
            target.Dirty = 1;
            request.Pending = 0;
            status.HasPath = 0;
            status.Waiting = 0;
        }

        private static bool CanMoveToConstrainedPosition(
            in NavContext ctx,
            float3 current,
            float3 nextPosition,
            float3 waypoint,
            float tolerance,
            float waypointReachDistance)
        {
            if (!ctx.IsValid)
                return true;

            if (NavQuery.TryClassify(in ctx, nextPosition, tolerance, out _))
                return true;

            if (NavQuery.TryClassify(in ctx, waypoint, tolerance, out NavSpaceRef waypointSpace)
                && waypointSpace.Kind == NavSpaceKind.Transition
                && IsMovingTowardWaypoint(current, nextPosition, waypoint)
                && IsCloseEnoughToEnterTransition(nextPosition, waypoint, waypointReachDistance))
            {
                return true;
            }

            if (NavQuery.TryClassify(in ctx, current, tolerance, out _))
                return false;

            if (!NavQuery.TryProjectToNearestSpace(in ctx, current, out float3 projected, out _))
                return false;

            float3 currentDelta = projected - current;
            float3 nextDelta = projected - nextPosition;
            currentDelta.y = 0f;
            nextDelta.y = 0f;
            return math.lengthsq(nextDelta) < math.lengthsq(currentDelta);
        }

        private static bool IsMovingTowardWaypoint(float3 current, float3 nextPosition, float3 waypoint)
        {
            float3 currentDelta = waypoint - current;
            float3 nextDelta = waypoint - nextPosition;
            currentDelta.y = 0f;
            nextDelta.y = 0f;
            return math.lengthsq(nextDelta) < math.lengthsq(currentDelta);
        }

        private static bool IsCloseEnoughToEnterTransition(float3 position, float3 waypoint, float waypointReachDistance)
        {
            float3 delta = waypoint - position;
            delta.y = 0f;
            float enterDistance = math.max(waypointReachDistance, 0.35f);
            return math.lengthsq(delta) <= enterDistance * enterDistance;
        }
    }
}
