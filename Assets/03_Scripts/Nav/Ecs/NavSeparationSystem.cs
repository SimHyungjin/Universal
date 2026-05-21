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

            // Cell size = largest separation radius, so every neighbour within an agent's
            // radius falls inside the 3x3 cells around its own — turning the O(n^2) all-pairs
            // scan into a local lookup.
            float cellSize = 0.01f;
            for (int i = 0; i < count; i++)
                cellSize = math.max(cellSize, settings[i].SeparationRadius);

            NativeParallelMultiHashMap<int2, int> grid =
                new NativeParallelMultiHashMap<int2, int>(count, Allocator.TempJob);
            for (int i = 0; i < count; i++)
                grid.Add(CellOf(transforms[i].Position, cellSize), i);

            NavSeparationJob job = new NavSeparationJob
            {
                Entities = entities,
                Transforms = transforms,
                Settings = settings,
                Grid = grid,
                CellSize = cellSize,
                SeparationLookup = SystemAPI.GetComponentLookup<NavAgentSeparation>()
            };

            state.Dependency = job.Schedule(count, 64, state.Dependency);
            state.Dependency = entities.Dispose(state.Dependency);
            state.Dependency = transforms.Dispose(state.Dependency);
            state.Dependency = settings.Dispose(state.Dependency);
            state.Dependency = grid.Dispose(state.Dependency);
        }

        private static int2 CellOf(float3 position, float cellSize)
        {
            return new int2(
                (int)math.floor(position.x / cellSize),
                (int)math.floor(position.z / cellSize));
        }

        [BurstCompile]
        private struct NavSeparationJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            [ReadOnly] public NativeArray<LocalTransform> Transforms;
            [ReadOnly] public NativeArray<NavAgentSettings> Settings;
            [ReadOnly] public NativeParallelMultiHashMap<int2, int> Grid;
            public float CellSize;

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

                int2 baseCell = CellOf(selfPos, CellSize);

                for (int dz = -1; dz <= 1 && neighbors < maxNeighbors; dz++)
                {
                    for (int dx = -1; dx <= 1 && neighbors < maxNeighbors; dx++)
                    {
                        int2 cell = new int2(baseCell.x + dx, baseCell.y + dz);
                        if (!Grid.TryGetFirstValue(cell, out int other, out NativeParallelMultiHashMapIterator<int2> iterator))
                            continue;

                        do
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
                        while (Grid.TryGetNextValue(out other, ref iterator));
                    }
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
