using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace MapNav.Ecs
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NavPathBuildSystem))]
    [UpdateBefore(typeof(NavMovementSystem))]
    [BurstCompile]
    public partial struct NavSeparationSystem : ISystem
    {
        private EntityQuery _query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _query = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<NavAgentSettings, LocalTransform, NavAgentSeparation>()
                .Build(ref state);
            state.RequireForUpdate(_query);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            int count = _query.CalculateEntityCount();
            if (count <= 0)
                return;

            NativeArray<Entity> entities = _query.ToEntityArray(Allocator.TempJob);
            NativeArray<LocalTransform> transforms = _query.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            NativeArray<NavAgentSettings> settings = _query.ToComponentDataArray<NavAgentSettings>(Allocator.TempJob);

            NavSeparationJob job = new NavSeparationJob
            {
                Entities = entities,
                Transforms = transforms,
                Settings = settings,
                SeparationLookup = SystemAPI.GetComponentLookup<NavAgentSeparation>()
            };

            state.Dependency = job.Schedule(count, 64, state.Dependency);
            state.Dependency = entities.Dispose(state.Dependency);
            state.Dependency = transforms.Dispose(state.Dependency);
            state.Dependency = settings.Dispose(state.Dependency);
        }

        [BurstCompile]
        private struct NavSeparationJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            [ReadOnly] public NativeArray<LocalTransform> Transforms;
            [ReadOnly] public NativeArray<NavAgentSettings> Settings;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<NavAgentSeparation> SeparationLookup;

            public void Execute(int index)
            {
                NavAgentSettings selfSettings = Settings[index];
                float radius = math.max(0f, selfSettings.SeparationRadius);
                float strength = math.max(0f, selfSettings.SeparationStrength);
                int maxNeighbors = math.max(0, selfSettings.SeparationMaxNeighbors);

                if (radius <= 1e-4f || strength <= 1e-4f || maxNeighbors == 0)
                {
                    SeparationLookup[Entities[index]] = default;
                    return;
                }

                float3 selfPos = Transforms[index].Position;
                float radiusSq = radius * radius;
                float3 steering = float3.zero;
                int neighbors = 0;

                for (int other = 0; other < Transforms.Length; other++)
                {
                    if (other == index)
                        continue;

                    float3 away = selfPos - Transforms[other].Position;
                    away.y = 0f;

                    float distSq = math.lengthsq(away);
                    if (distSq > radiusSq)
                        continue;

                    float2 fallback = UnitFromIndex(index);
                    float dist = math.sqrt(math.max(distSq, 1e-8f));
                    float3 dir = distSq > 1e-8f
                        ? away / dist
                        : new float3(fallback.x, 0f, fallback.y);
                    float weight = 1f - math.saturate(dist / radius);
                    steering += dir * weight;
                    neighbors++;

                    if (neighbors >= maxNeighbors)
                        break;
                }

                if (neighbors > 0)
                {
                    steering /= neighbors;
                    float lenSq = math.lengthsq(steering);
                    if (lenSq > 1f)
                        steering *= math.rsqrt(lenSq);
                }

                SeparationLookup[Entities[index]] = new NavAgentSeparation
                {
                    Steering = steering,
                    NeighborCount = neighbors
                };
            }

            private static float2 UnitFromIndex(int index)
            {
                float angle = index * 2.3999631f;
                math.sincos(angle, out float s, out float c);
                return new float2(c, s);
            }
        }
    }
}
