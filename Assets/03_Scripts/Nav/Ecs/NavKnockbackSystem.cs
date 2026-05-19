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

                        float3 next = cur + new float3(vel.x * dt, 0f, vel.z * dt);
                        if (!TryMoveKnockback(in ctx, cur, next, agentRadius, tolerance, out next))
                            hitBoundary = true;

                        transform.ValueRW.Position = next;
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
                        if (!NavQuery.TryClassify(in ctx, pos, tolerance, out _) &&
                            NavQuery.TryProjectToNearestSpace(in ctx, pos, out float3 snapped, out _))
                            transform.ValueRW.Position = snapped;
                    }
                    waypoints.Clear();
                    target.ValueRW.Dirty = 1;
                }
            }
        }

        private static bool TryMoveKnockback(
            in NavContext ctx,
            float3 from,
            float3 to,
            float agentRadius,
            float tolerance,
            out float3 safePosition)
        {
            safePosition = from;

            bool fromValid = NavQuery.TryClassify(in ctx, from, tolerance, out _);
            if (!fromValid && !NavQuery.TryProjectToNearestSpace(in ctx, from, out _, out _))
                return false;

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
                if (NavQuery.TryClassify(in ctx, candidate, tolerance, out _))
                {
                    safePosition = candidate;
                    previous = candidate;
                    continue;
                }

                safePosition = FindLastSafeEndpoint(in ctx, previous, candidate, tolerance);
                return false;
            }

            return true;
        }

        private static float3 FindLastSafeEndpoint(
            in NavContext ctx,
            float3 from,
            float3 to,
            float tolerance)
        {
            float3 safe = from;
            float min = 0f;
            float max = 1f;

            for (int i = 0; i < 5; i++)
            {
                float mid = (min + max) * 0.5f;
                float3 candidate = math.lerp(from, to, mid);
                if (NavQuery.TryClassify(in ctx, candidate, tolerance, out _))
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
    }
}
