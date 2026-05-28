using MapNav.Data;
using Unity.Mathematics;

namespace MapNav.Core
{
    // Host-agnostic agent movement decisions shared by the ECS movement job and the
    // MonoBehaviour agent. Pure float3 math + NavQuery lookups only — no managed types,
    // no host state — so it is Burst-compatible and single-sources every algorithm.
    public static class NavAgentCore
    {
        // Reach distance for a waypoint: required (transition) waypoints use a tight stop
        // distance and must be hit precisely; ordinary waypoints can be advanced early.
        public static float GetReachDistance(float stopDistance, float waypointAdvanceDistance, bool required)
        {
            return required
                ? math.max(1e-4f, stopDistance)
                : math.max(stopDistance, waypointAdvanceDistance);
        }

        // True once the agent has travelled past `waypoint` along the anchor->waypoint segment.
        public static bool HasPassedWaypoint(float3 anchor, float3 waypoint, float3 current)
        {
            float3 segment = waypoint - anchor;
            segment.y = 0f;
            float lenSq = math.lengthsq(segment);
            if (lenSq <= 1e-4f) return false;

            float3 fromAnchor = current - anchor;
            fromAnchor.y = 0f;
            if (math.dot(fromAnchor, segment) / lenSq < 1f - 1e-4f) return false;

            float3 fromWaypoint = current - waypoint;
            fromWaypoint.y = 0f;
            return math.dot(fromWaypoint, segment) >= -1e-4f;
        }

        // Blends the steering target toward the next waypoint as the agent nears a corner for
        // smoother turns. Returns the active waypoint when no look-ahead applies.
        public static float3 GetSteeringTarget(
            float3 current, float3 waypoint, float3 nextWaypoint, bool hasNext, float cornerLookAheadDistance)
        {
            if (cornerLookAheadDistance <= 0f || !hasNext)
                return waypoint;

            float3 delta = waypoint - current;
            delta.y = 0f;
            float distance = math.length(delta);
            if (distance >= cornerLookAheadDistance)
                return waypoint;

            float blend = 1f - math.saturate(distance / math.max(1e-4f, cornerLookAheadDistance));
            return math.lerp(waypoint, nextWaypoint, blend);
        }

        // Picks the actual steering vector: the look-ahead steering, unless it has collapsed
        // or points away from the waypoint, in which case the straight direction is used.
        public static float3 ResolveSteering(float3 steeringTarget, float3 current, float3 planarDelta)
        {
            float3 steering = steeringTarget - current;
            steering.y = 0f;
            if (math.lengthsq(steering) <= 1e-4f || math.dot(steering, planarDelta) < 0f)
                return planarDelta;
            return steering;
        }

        // A waypoint inside a transition must be passed through, not corner-cut.
        public static bool IsTransitionWaypoint(in NavContext ctx, float3 waypoint, float tolerance)
        {
            return NavQuery.TryClassify(in ctx, waypoint, tolerance, out NavSpaceRef space)
                && space.Kind == NavSpaceKind.Transition;
        }

        // Snaps `position`'s height onto the nav surface beneath it. Returns false (and leaves
        // `snapped` = position) when there is no nav surface to sample.
        public static bool TrySnapHeight(
            in NavContext ctx, float3 position, float boundaryTolerance, float heightOffset, out float3 snapped)
        {
            snapped = position;
            if (!ctx.IsValid) return false;
            if (!NavQuery.TryGetHeight(in ctx, position, boundaryTolerance, out float worldHeight))
                return false;

            snapped = new float3(position.x, worldHeight + heightOffset, position.z);
            return true;
        }

        // Whether the agent may step from `current` to `next`. Steps onto nav space, or just
        // short of a transition waypoint it is approaching, are allowed; an off-mesh agent may
        // step only if it moves closer to the nearest nav space.
        public static bool CanMove(
            in NavContext ctx, float3 current, float3 next, float3 waypoint,
            float agentRadius, float boundaryTolerance, float waypointReachDistance)
        {
            if (!ctx.IsValid)
                return true;

            if (NavQuery.TryClassify(in ctx, next, boundaryTolerance, out _))
            {
                if (NavQuery.IsClearOfObstaclePadding(in ctx, next, agentRadius))
                    return true;

                return IsRecoveringOutOfObstaclePadding(in ctx, current, next, agentRadius);
            }

            if (NavQuery.TryClassify(in ctx, waypoint, boundaryTolerance, out NavSpaceRef waypointSpace)
                && waypointSpace.Kind == NavSpaceKind.Transition
                && IsMovingToward(current, next, waypoint)
                && IsCloseEnoughToEnterTransition(next, waypoint, waypointReachDistance))
                return true;

            if (NavQuery.TryClassify(in ctx, current, boundaryTolerance, out _))
                return false;

            if (!NavQuery.TryProjectToNearestSpace(in ctx, current, out float3 projected, out _))
                return false;

            float3 currentDelta = projected - current;
            float3 nextDelta = projected - next;
            currentDelta.y = 0f;
            nextDelta.y = 0f;
            return math.lengthsq(nextDelta) < math.lengthsq(currentDelta);
        }

        private static bool IsRecoveringOutOfObstaclePadding(
            in NavContext ctx,
            float3 current,
            float3 next,
            float agentRadius)
        {
            if (NavQuery.IsClearOfObstaclePadding(in ctx, current, agentRadius))
                return false;

            if (!NavQuery.TryProjectOutOfObstacle(in ctx, current, agentRadius, out float3 projected))
                return false;

            float3 currentDelta = projected - current;
            float3 nextDelta = projected - next;
            currentDelta.y = 0f;
            nextDelta.y = 0f;
            return math.lengthsq(nextDelta) < math.lengthsq(currentDelta);
        }

        private static bool IsMovingToward(float3 current, float3 next, float3 waypoint)
        {
            float3 currentDelta = waypoint - current;
            float3 nextDelta = waypoint - next;
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

        // ── Stuck detection ──────────────────────────────────────────────

        // Accumulates `stuckTimer` while the agent fails to close meaningful distance on its
        // waypoint. Returns true once it has been stalled long enough (and the cooldown has
        // elapsed) to warrant a repath.
        public static bool EvaluateProgressStuck(
            ref float stuckTimer, ref float lastDistanceToWaypoint,
            float cooldownRemaining, float distanceToWaypoint,
            float repathDelay, float progressDistance, float moveSpeed, float deltaTime)
        {
            if (repathDelay <= 0f)
            {
                stuckTimer = 0f;
                lastDistanceToWaypoint = distanceToWaypoint;
                return false;
            }

            float progress = lastDistanceToWaypoint - distanceToWaypoint;
            float expectedProgress = math.max(progressDistance, moveSpeed * deltaTime * 0.25f);
            if (lastDistanceToWaypoint <= 0f || progress > expectedProgress)
            {
                stuckTimer = 0f;
                lastDistanceToWaypoint = distanceToWaypoint;
                return false;
            }

            stuckTimer += deltaTime;
            lastDistanceToWaypoint = distanceToWaypoint;
            return stuckTimer >= repathDelay && cooldownRemaining <= 0f;
        }

        // Accumulates `stuckTimer` for a hard block (a move was rejected). Returns true once
        // the block has persisted long enough (and the cooldown has elapsed) to repath.
        public static bool EvaluateBlockedStuck(
            ref float stuckTimer, float cooldownRemaining, float repathDelay, float deltaTime)
        {
            if (repathDelay <= 0f)
                return false;

            stuckTimer += deltaTime;
            return stuckTimer >= repathDelay && cooldownRemaining <= 0f;
        }

        // Resolves a stuck repath: clears the timer, arms the cooldown and bumps the retry
        // counter. Returns true when the retry limit is reached and the agent should give up.
        public static bool CommitStuckRepath(
            ref float stuckTimer, ref float cooldownRemaining, ref int retryCount,
            float cooldownDuration, int retryLimit)
        {
            stuckTimer = 0f;
            cooldownRemaining = cooldownDuration;
            retryCount++;
            return retryLimit > 0 && retryCount >= retryLimit;
        }
    }
}
