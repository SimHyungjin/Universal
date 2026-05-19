using MapNav.Core;
using MapNav.Data;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MapNav.Ecs
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NavTargetCommandSystem))]
    [BurstCompile]
    public partial struct NavTargetResolveSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton(out NavBlobReference navRef) || !navRef.Blob.IsCreated)
                return;

            NavContext ctx = new NavContext(navRef.Blob, navRef.LocalToWorld, navRef.WorldToLocal);

            foreach ((
                RefRW<NavAgentTarget> target,
                RefRO<NavAgentSettings> settings,
                RefRW<NavAgentPathRequest> request,
                RefRW<NavAgentPathStatus> status,
                RefRW<NavAgentMotion> motion,
                RefRO<LocalTransform> transform)
                in SystemAPI.Query<
                    RefRW<NavAgentTarget>,
                    RefRO<NavAgentSettings>,
                    RefRW<NavAgentPathRequest>,
                    RefRW<NavAgentPathStatus>,
                    RefRW<NavAgentMotion>,
                    RefRO<LocalTransform>>())
            {
                if (target.ValueRO.Dirty == 0) continue;

                float3 actualStart = transform.ValueRO.Position;
                float3 startWorld = actualStart;
                float3 targetWorld = target.ValueRO.Position;
                float repathDistance = math.max(0f, settings.ValueRO.TargetRepathDistance);

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

                float tol = math.max(0f, settings.ValueRO.BoundaryTolerance);
                if (!ResolveOrProject(in ctx, startWorld, tol, out startWorld)
                    || !ResolveOrProject(in ctx, targetWorld, tol, out targetWorld))
                {
                    target.ValueRW.Dirty = 0;
                    request.ValueRW.Pending = 0;
                    status.ValueRW.HasPath = 0;
                    status.ValueRW.Waiting = 0;
                    status.ValueRW.Failed = 1;
                    motion.ValueRW.IsMoving = 0;
                    motion.ValueRW.CurrentSpeed = 0f;
                    motion.ValueRW.Velocity = float3.zero;
                    continue;
                }

                request.ValueRW.Pending = 1;
                request.ValueRW.StartWorld = startWorld;
                request.ValueRW.ActualStartWorld = actualStart;
                request.ValueRW.TargetWorld = targetWorld;
                target.ValueRW.Dirty = 0;
                target.ValueRW.AcceptedPosition = targetWorld;
                status.ValueRW.Waiting = 1;
                status.ValueRW.Failed = 0;
            }
        }

        private static bool ResolveOrProject(in NavContext ctx, float3 worldPos, float tolerance, out float3 resolved)
        {
            if (NavQuery.TryClassify(in ctx, worldPos, tolerance, out _))
            {
                resolved = worldPos;
                return true;
            }

            if (NavQuery.TryProjectToNearestSpace(in ctx, worldPos, out float3 projected, out _))
            {
                resolved = projected;
                return true;
            }

            resolved = worldPos;
            return false;
        }
    }
}
