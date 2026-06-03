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
        // 거시 경로 캐시. 추격처럼 같은 리전 쌍을 반복 질의할 때 상위 A*(가장 비싼 단계)를 건너뛴다.
        // 메인스레드 foreach에서만 접근하므로 잠금이 필요 없다.
        private NavMacroPathCache _macroCache;
        private BlobAssetReference<NavBlob> _cachedForBlob;
        private float4x4 _cachedLocalToWorld;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _macroCache = new NavMacroPathCache(256, Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (_macroCache.IsCreated) _macroCache.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton<NavBlobReference>(out NavBlobReference navRef) || !navRef.Blob.IsCreated)
                return;

            // 맵(블롭/transform)이 바뀌면 캐시된 거시 경로가 무효 — 통째 비운다.
            if (!navRef.Blob.Equals(_cachedForBlob) || !_cachedLocalToWorld.Equals(navRef.LocalToWorld))
            {
                _macroCache.Clear();
                _cachedForBlob = navRef.Blob;
                _cachedLocalToWorld = navRef.LocalToWorld;
            }

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
                    RefRW<NavAgentTarget> target,
                    RefRO<NavAgentSettings> settings,
                    RefRW<NavAgentMotion> motion,
                    RefRW<NavAgentPathStatus> status,
                    RefRO<NavAgentDeath> death,
                    DynamicBuffer<NavAgentWaypoint> waypoints)
                    in SystemAPI.Query<
                        RefRW<NavAgentPathRequest>,
                        RefRW<NavAgentTarget>,
                        RefRO<NavAgentSettings>,
                        RefRW<NavAgentMotion>,
                        RefRW<NavAgentPathStatus>,
                        RefRO<NavAgentDeath>,
                        DynamicBuffer<NavAgentWaypoint>>())
                {
                    if (death.ValueRO.Dying != 0)
                    {
                        request.ValueRW.Pending = 0;
                        status.ValueRW.HasPath = 0;
                        status.ValueRW.Waiting = 0;
                        status.ValueRW.Failed = 0;
                        waypoints.Clear();
                        continue;
                    }

                    if (request.ValueRO.Pending == 0) continue;

                    if (budget <= 0)
                    {
                        status.ValueRW.Waiting = 1;
                        continue;
                    }

                    budget--;
                    status.ValueRW.Waiting = 0;
                    bool hadExistingPath = status.ValueRO.HasPath != 0 && waypoints.Length > 0;

                    float3 actualStart = request.ValueRO.ActualStartWorld;
                    float3 startWorld = request.ValueRO.StartWorld;
                    float3 targetWorld = request.ValueRO.TargetWorld;
                    float radius = math.max(0f, settings.ValueRO.AgentRadius);
                    float tol = math.max(0f, settings.ValueRO.BoundaryTolerance);

                    bool built = NavPath.TryBuild(
                        in ctx, startWorld, targetWorld, radius, tol,
                        ref scratch, ref nodes, ref portals, ref waypointsBuf, ref _macroCache);

                    if (built)
                    {
                        waypoints.Clear();

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
                        target.ValueRW.AcceptedPosition = targetWorld;
                        target.ValueRW.Position = targetWorld;
                        target.ValueRW.Dirty = 0;
                        motion.ValueRW.WaypointIndex = 0;
                        motion.ValueRW.LastWaypointAnchor = actualStart;
                        motion.ValueRW.LastDistanceToWaypoint = 0f;
                        motion.ValueRW.StuckRetryCount = 0;
                    }
                    else
                    {
                        if (!hadExistingPath)
                        {
                            waypoints.Clear();
                            status.ValueRW.HasPath = 0;
                            status.ValueRW.Failed = 1;
                        }
                        else
                        {
                            status.ValueRW.HasPath = 1;
                            status.ValueRW.Failed = 0;
                        }
                        status.ValueRW.Waiting = 0;
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
            => NavAgentCore.IsTransitionWaypoint(in ctx, waypoint, tolerance) ? (byte)1 : (byte)0;

    }
}
