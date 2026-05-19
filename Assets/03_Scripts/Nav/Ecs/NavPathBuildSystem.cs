using MapNav.Core;
using MapNav.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace MapNav.Ecs
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NavTargetResolveSystem))]
    [BurstCompile]
    public partial struct NavPathBuildSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<NavBlobReference>(out NavBlobReference navRef) || !navRef.Blob.IsCreated)
                return;

            int budget = 16;
            if (SystemAPI.TryGetSingleton<NavPathBuildBudget>(out NavPathBuildBudget b))
                budget = math.max(1, b.MaxPathsPerFrame);

            NavContext ctx = new NavContext(navRef.Blob, navRef.LocalToWorld, navRef.WorldToLocal);

            NavScratch scratch = new NavScratch(64, Allocator.Temp);
            NativeList<NavSpaceRef> nodes = new NativeList<NavSpaceRef>(16, Allocator.Temp);
            NativeList<NavPortal> portals = new NativeList<NavPortal>(16, Allocator.Temp);
            NativeList<float3> waypointsBuf = new NativeList<float3>(16, Allocator.Temp);

            try
            {
                foreach ((
                    RefRW<NavAgentPathRequest> request,
                    RefRO<NavAgentSettings> settings,
                    RefRW<NavAgentMotion> motion,
                    RefRW<NavAgentPathStatus> status,
                    DynamicBuffer<NavAgentWaypoint> waypoints)
                    in SystemAPI.Query<
                        RefRW<NavAgentPathRequest>,
                        RefRO<NavAgentSettings>,
                        RefRW<NavAgentMotion>,
                        RefRW<NavAgentPathStatus>,
                        DynamicBuffer<NavAgentWaypoint>>())
                {
                    if (request.ValueRO.Pending == 0) continue;

                    if (budget <= 0)
                    {
                        status.ValueRW.Waiting = 1;
                        continue;
                    }

                    budget--;
                    status.ValueRW.Waiting = 0;
                    waypoints.Clear();

                    float3 actualStart = request.ValueRO.ActualStartWorld;
                    float3 startWorld = request.ValueRO.StartWorld;
                    float3 targetWorld = request.ValueRO.TargetWorld;
                    float radius = math.max(0f, settings.ValueRO.AgentRadius);
                    float tol = math.max(0f, settings.ValueRO.BoundaryTolerance);

                    bool built = NavPath.TryBuild(
                        in ctx, startWorld, targetWorld, radius, tol,
                        ref scratch, ref nodes, ref portals, ref waypointsBuf);

                    if (built)
                    {
                        if (NeedsRecoveryWaypoint(actualStart, startWorld, settings.ValueRO.StopDistance))
                            waypoints.Add(new NavAgentWaypoint { Position = startWorld, Required = 1 });

                        for (int i = 0; i < waypointsBuf.Length; i++)
                        {
                            float3 waypoint = waypointsBuf[i];
                            waypoints.Add(new NavAgentWaypoint
                            {
                                Position = waypoint,
                                Required = IsTransitionWaypoint(in ctx, waypoint, tol)
                            });
                        }

                        status.ValueRW.HasPath = (byte)(waypoints.Length > 0 ? 1 : 0);
                        status.ValueRW.Waiting = 0;
                        status.ValueRW.Failed = 0;
                        motion.ValueRW.WaypointIndex = 0;
                        motion.ValueRW.LastWaypointAnchor = actualStart;
                        motion.ValueRW.LastDistanceToWaypoint = 0f;
                        motion.ValueRW.StuckRetryCount = 0;
                    }
                    else
                    {
                        status.ValueRW.HasPath = 0;
                        status.ValueRW.Waiting = 0;
                        status.ValueRW.Failed = 1;
                    }

                    request.ValueRW.Pending = 0;
                }
            }
            finally
            {
                waypointsBuf.Dispose();
                portals.Dispose();
                nodes.Dispose();
                scratch.Dispose();
            }
        }

        private static bool NeedsRecoveryWaypoint(float3 actualStart, float3 projectedStart, float stopDistance)
        {
            float3 d = projectedStart - actualStart;
            d.y = 0f;
            float threshold = math.max(1e-4f, stopDistance);
            return math.lengthsq(d) > threshold * threshold;
        }

        private static byte IsTransitionWaypoint(in NavContext ctx, float3 waypoint, float tolerance)
        {
            return NavQuery.TryClassify(in ctx, waypoint, tolerance, out NavSpaceRef space)
                && space.Kind == NavSpaceKind.Transition
                    ? (byte)1
                    : (byte)0;
        }

    }
}
