using MapNav.Core;
using MapNav.Data;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MapNav.Ecs
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NavSeparationSystem))]
    [UpdateBefore(typeof(NavMovementSystem))]
    public partial struct NavKnockbackSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;

            NavContext ctx = default;
            if (SystemAPI.TryGetSingleton<NavBlobReference>(out NavBlobReference navRef) && navRef.Blob.IsCreated)
                ctx = new NavContext(navRef.Blob, navRef.LocalToWorld, navRef.WorldToLocal);

            foreach (var (knockback, pathStatus, target, settings, transform, waypoints) in
                SystemAPI.Query<
                    RefRW<NavAgentKnockback>,
                    RefRW<NavAgentPathStatus>,
                    RefRW<NavAgentTarget>,
                    RefRO<NavAgentSettings>,
                    RefRW<LocalTransform>,
                    DynamicBuffer<NavAgentWaypoint>>())
            {
                float timer     = knockback.ValueRO.Timer;
                float lockTimer = knockback.ValueRO.MotionLockTimer;

                if (timer <= 0f && lockTimer <= 0f) continue;

                float newTimer     = math.max(0f, timer     - dt);
                float newLockTimer = math.max(0f, lockTimer - dt);

                knockback.ValueRW.Timer           = newTimer;
                knockback.ValueRW.MotionLockTimer = newLockTimer;

                if (timer > 0f)
                {
                    float3 vel = knockback.ValueRO.Velocity;
                    float3 cur = transform.ValueRO.Position;
                    bool hitBoundary = false;

                    if (ctx.IsValid)
                    {
                        float tolerance   = settings.ValueRO.BoundaryTolerance;
                        float agentRadius = math.max(0f, settings.ValueRO.AgentRadius);

                        if (!NavQuery.TryClassify(in ctx, cur, tolerance, out _) &&
                            NavQuery.TryProjectToNearestSpace(in ctx, cur, out float3 snapped, out _))
                            cur = snapped;

                        cur = ProjectToClearPosition(in ctx, cur, agentRadius, tolerance);

                        float3 next = cur + new float3(vel.x * dt, 0f, vel.z * dt);
                        if (!TryMoveKnockback(in ctx, cur, next, agentRadius, tolerance, out next))
                            hitBoundary = true;

                        transform.ValueRW.Position = ProjectToClearPosition(in ctx, next, agentRadius, tolerance);
                    }
                    else
                    {
                        transform.ValueRW.Position = cur + new float3(vel.x * dt, 0f, vel.z * dt);
                    }

                    if (hitBoundary)
                    {
                        knockback.ValueRW.Velocity = float3.zero;
                        knockback.ValueRW.Timer    = 0f;
                        newTimer = 0f;
                    }
                    else
                    {
                        float decay = math.max(0f, 1f - knockback.ValueRO.Friction * dt);
                        knockback.ValueRW.Velocity = vel * decay;
                    }
                }

                pathStatus.ValueRW.HasPath = 0;

                if (lockTimer > 0f && newLockTimer <= 0f)
                {
                    if (ctx.IsValid)
                    {
                        float3 pos       = transform.ValueRO.Position;
                        float  tolerance = settings.ValueRO.BoundaryTolerance;
                        float  agentRadius = math.max(0f, settings.ValueRO.AgentRadius);
                        if (!NavQuery.TryClassify(in ctx, pos, tolerance, out _) &&
                            NavQuery.TryProjectToNearestSpace(in ctx, pos, out float3 snapped, out _))
                            pos = snapped;
                        transform.ValueRW.Position = ProjectToClearPosition(in ctx, pos, agentRadius, tolerance);
                    }
                    waypoints.Clear();
                    target.ValueRW.Dirty = 1;
                }
            }
        }

        private static float3 ProjectToClearPosition(in NavContext ctx, float3 position, float agentRadius, float tolerance)
        {
            if (agentRadius <= 0f)
                return position;

            float3 current = position;
            for (int i = 0; i < 3; i++)
            {
                if (NavQuery.TryClassify(in ctx, current, tolerance, out _)
                    && NavQuery.IsClearOfObstaclePadding(in ctx, current, agentRadius))
                    return current;

                if (!NavQuery.TryProjectOutOfObstacle(in ctx, current, agentRadius, out float3 projected))
                    break;

                if (!NavQuery.TryClassify(in ctx, projected, tolerance, out _))
                    break;

                current = projected;
            }

            return NavQuery.TryClassify(in ctx, current, tolerance, out _)
                ? current
                : position;
        }

        private static bool TryMoveKnockback(
            in NavContext ctx,
            float3 from,
            float3 to,
            float agentRadius,
            float tolerance,
            out float3 safePosition)
        {
            bool fromValid = NavQuery.TryClassify(in ctx, from, tolerance, out _);
            if (!fromValid && !NavQuery.TryProjectToNearestSpace(in ctx, from, out _, out _))
            {
                safePosition = from;
                return false;
            }

            float3 delta = to - from;
            delta.y = 0f;

            float distance = math.length(delta);
            if (distance <= 1e-4f)
            {
                safePosition = to;
                return true;
            }

            if (TryMoveStraight(in ctx, from, to, agentRadius, tolerance, out safePosition))
                return true;

            float3 direction = delta / distance;
            float3 sideA = new float3(-direction.z, 0f, direction.x);
            float3 sideB = new float3(direction.z, 0f, -direction.x);
            float slideDistance = distance * 0.85f;

            bool movedA = TryMoveStraight(in ctx, from, from + sideA * slideDistance, agentRadius, tolerance, out float3 slideA);
            bool movedB = TryMoveStraight(in ctx, from, from + sideB * slideDistance, agentRadius, tolerance, out float3 slideB);

            if (movedA || movedB)
            {
                float movedASq = math.lengthsq((slideA - from).xz);
                float movedBSq = math.lengthsq((slideB - from).xz);
                safePosition = movedA && (!movedB || movedASq >= movedBSq) ? slideA : slideB;
                return math.lengthsq((safePosition - from).xz) > 1e-6f;
            }

            safePosition = from;
            return false;
        }

        private static bool TryMoveStraight(
            in NavContext ctx,
            float3 from,
            float3 to,
            float agentRadius,
            float tolerance,
            out float3 safePosition)
        {
            safePosition = from;

            float3 delta = to - from;
            delta.y = 0f;
            float distance = math.length(delta);
            if (distance <= 1e-4f)
            {
                safePosition = to;
                return true;
            }

            float stepLength = math.max(0.08f, agentRadius * 0.5f);
            int steps = math.clamp((int)math.ceil(distance / stepLength), 1, 6);
            float3 previous = from;

            for (int i = 1; i <= steps; i++)
            {
                float3 candidate = math.lerp(from, to, i / (float)steps);
                if (IsKnockbackPositionSafe(in ctx, candidate, agentRadius, tolerance))
                {
                    safePosition = candidate;
                    previous = candidate;
                    continue;
                }

                safePosition = FindLastSafeEndpoint(in ctx, previous, candidate, agentRadius, tolerance);
                return false;
            }

            return true;
        }

        private static float3 FindLastSafeEndpoint(
            in NavContext ctx,
            float3 from,
            float3 to,
            float agentRadius,
            float tolerance)
        {
            float3 safe = from;
            float min = 0f;
            float max = 1f;

            for (int i = 0; i < 5; i++)
            {
                float mid = (min + max) * 0.5f;
                float3 candidate = math.lerp(from, to, mid);
                if (IsKnockbackPositionSafe(in ctx, candidate, agentRadius, tolerance))
                {
                    min = mid;
                    safe = candidate;
                }
                else
                {
                    max = mid;
                }
            }

            return safe;
        }

        // A knockback step is safe only if the agent's body (not just its centre point) clears
        // obstacles — TryClassify alone accepts a point whose centre is just outside an obstacle
        // polygon while the agent radius is still buried in the wall.
        private static bool IsKnockbackPositionSafe(in NavContext ctx, float3 position, float agentRadius, float tolerance)
        {
            return NavQuery.TryClassify(in ctx, position, tolerance, out _)
                && NavQuery.IsClearOfObstaclePadding(in ctx, position, agentRadius);
        }
    }
}
