using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace MapNav.Ecs
{
    // 각 유닛이 가장 가까운 적대 진영 대상(다른 진영 유닛 또는 플레이어)을 찾아 길찾기 타겟으로 삼는다.
    // 공간 해시 그리드로 근접 유닛만 검사해 O(n^2) 전수 검사를 피한다.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(NavTargetResolveSystem))]
    [BurstCompile]
    public partial struct NavTargetingSystem : ISystem
    {
        // 유닛이 적대 대상을 인지하는 최대 거리.
        private const float SearchRadius = 20f;

        private EntityQuery _query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _query = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<NavAgentFaction, NavAgentDeath, NavAgentSettings, LocalTransform, NavAgentTarget, NavAgentPathStatus, NavAgentPathRequest>()
                .Build(ref state);
            state.RequireForUpdate(_query);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            int count = _query.CalculateEntityCount();
            if (count <= 0)
                return;

            PlayerNavTarget player = default;
            SystemAPI.TryGetSingleton(out player);

            NativeArray<Entity> entities = _query.ToEntityArray(Allocator.TempJob);
            NativeArray<LocalTransform> transforms = _query.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            NativeArray<NavAgentFaction> factions = _query.ToComponentDataArray<NavAgentFaction>(Allocator.TempJob);
            NativeArray<NavAgentDeath> deaths = _query.ToComponentDataArray<NavAgentDeath>(Allocator.TempJob);
            NativeArray<NavAgentSettings> settings = _query.ToComponentDataArray<NavAgentSettings>(Allocator.TempJob);
            NativeArray<NavAgentPathStatus> statuses = _query.ToComponentDataArray<NavAgentPathStatus>(Allocator.TempJob);
            NativeArray<NavAgentPathRequest> requests = _query.ToComponentDataArray<NavAgentPathRequest>(Allocator.TempJob);

            float cellSize = math.max(0.01f, SearchRadius);

            // 사망 연출 중인 유닛은 타겟 후보가 아니므로 그리드에 넣지 않는다.
            NativeParallelMultiHashMap<int2, int> grid =
                new NativeParallelMultiHashMap<int2, int>(count, Allocator.TempJob);
            for (int i = 0; i < count; i++)
            {
                if (deaths[i].Dying != 0) continue;
                grid.Add(CellOf(transforms[i].Position, cellSize), i);
            }

            NavTargetingJob job = new NavTargetingJob
            {
                Entities = entities,
                Transforms = transforms,
                Factions = factions,
                Deaths = deaths,
                Settings = settings,
                Statuses = statuses,
                Requests = requests,
                Grid = grid,
                CellSize = cellSize,
                DeltaTime = SystemAPI.Time.DeltaTime,
                SearchRadiusSq = SearchRadius * SearchRadius,
                PlayerPosition = player.Position,
                PlayerValid = player.HasValue,
                TargetLookup = SystemAPI.GetComponentLookup<NavAgentTarget>(),
                CombatLookup = SystemAPI.GetComponentLookup<NavAgentCombatTarget>()
            };

            state.Dependency = job.Schedule(count, 32, state.Dependency);
            state.Dependency = entities.Dispose(state.Dependency);
            state.Dependency = transforms.Dispose(state.Dependency);
            state.Dependency = factions.Dispose(state.Dependency);
            state.Dependency = deaths.Dispose(state.Dependency);
            state.Dependency = settings.Dispose(state.Dependency);
            state.Dependency = statuses.Dispose(state.Dependency);
            state.Dependency = requests.Dispose(state.Dependency);
            state.Dependency = grid.Dispose(state.Dependency);
        }

        private static int2 CellOf(float3 position, float cellSize)
        {
            return new int2(
                (int)math.floor(position.x / cellSize),
                (int)math.floor(position.z / cellSize));
        }

        [BurstCompile]
        private struct NavTargetingJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            [ReadOnly] public NativeArray<LocalTransform> Transforms;
            [ReadOnly] public NativeArray<NavAgentFaction> Factions;
            [ReadOnly] public NativeArray<NavAgentDeath> Deaths;
            [ReadOnly] public NativeArray<NavAgentSettings> Settings;
            [ReadOnly] public NativeArray<NavAgentPathStatus> Statuses;
            [ReadOnly] public NativeArray<NavAgentPathRequest> Requests;
            [ReadOnly] public NativeParallelMultiHashMap<int2, int> Grid;
            public float CellSize;
            public float DeltaTime;
            public float SearchRadiusSq;
            public float3 PlayerPosition;
            public byte PlayerValid;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<NavAgentTarget> TargetLookup;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<NavAgentCombatTarget> CombatLookup;

            public void Execute(int index)
            {
                if (Deaths[index].Dying != 0)
                    return;

                float3 selfPos = Transforms[index].Position;
                NavFaction selfFaction = Factions[index].Faction;

                float bestDistSq = SearchRadiusSq;
                float3 bestPos = float3.zero;
                Entity bestEntity = Entity.Null;
                bool found = false;
                bool targetIsPlayer = false;

                int2 baseCell = CellOf(selfPos, CellSize);
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int2 cell = new int2(baseCell.x + dx, baseCell.y + dz);
                        if (!Grid.TryGetFirstValue(cell, out int other, out NativeParallelMultiHashMapIterator<int2> iterator))
                            continue;

                        do
                        {
                            if (other == index) continue;
                            if (Factions[other].Faction == selfFaction) continue;

                            float3 diff = Transforms[other].Position - selfPos;
                            diff.y = 0f;
                            float distSq = math.lengthsq(diff);
                            if (distSq < bestDistSq)
                            {
                                bestDistSq = distSq;
                                bestPos = Transforms[other].Position;
                                bestEntity = Entities[other];
                                found = true;
                                targetIsPlayer = false;
                            }
                        }
                        while (Grid.TryGetNextValue(out other, ref iterator));
                    }
                }

                // 적군 유닛은 플레이어(아군 진영)를 거리 제한 없이 추적한다.
                // 더 가까운 아군 유닛이 SearchRadius 안에 있으면 그쪽을 우선한다.
                // If no hostile unit is nearby, still pick the nearest hostile on the map so
                // distant allies/enemies do not remain idle just because they spawned apart.
                if (!found)
                {
                    bestDistSq = float.MaxValue;
                    for (int other = 0; other < Entities.Length; other++)
                    {
                        if (other == index) continue;
                        if (Deaths[other].Dying != 0) continue;
                        if (Factions[other].Faction == selfFaction) continue;

                        float3 diff = Transforms[other].Position - selfPos;
                        diff.y = 0f;
                        float distSq = math.lengthsq(diff);
                        if (distSq >= bestDistSq) continue;

                        bestDistSq = distSq;
                        bestPos = Transforms[other].Position;
                        bestEntity = Entities[other];
                        found = true;
                        targetIsPlayer = false;
                    }
                }

                if (PlayerValid != 0 && selfFaction == NavFaction.Enemy)
                {
                    float3 diff = PlayerPosition - selfPos;
                    diff.y = 0f;
                    float distSq = math.lengthsq(diff);
                    if (!found || distSq < bestDistSq)
                    {
                        bestPos = PlayerPosition;
                        bestEntity = Entity.Null;
                        found = true;
                        targetIsPlayer = true;
                    }
                }

                Entity self = Entities[index];

                NavAgentCombatTarget combat = CombatLookup[self];
                bool targetChanged = combat.HasTarget == 0
                    || combat.TargetEntity != bestEntity
                    || combat.IsPlayer != (byte)(targetIsPlayer ? 1 : 0);

                combat.HasTarget = (byte)(found ? 1 : 0);
                combat.TargetEntity = bestEntity;
                combat.IsPlayer = (byte)(targetIsPlayer ? 1 : 0);
                if (found)
                    combat.Position = bestPos;
                CombatLookup[self] = combat;

                if (!found)
                    return;

                NavAgentTarget target = TargetLookup[self];
                target.RefreshCooldownRemaining = math.max(0f, target.RefreshCooldownRemaining - DeltaTime);

                NavAgentSettings settings = Settings[index];
                float refreshDistance = math.max(settings.TargetRepathDistance, settings.TargetRefreshDistance);
                float3 targetDelta = bestPos - target.Position;
                targetDelta.y = 0f;
                bool hasNoUsablePath = Statuses[index].HasPath == 0
                    && Statuses[index].Waiting == 0
                    && Requests[index].Pending == 0;

                if (!targetChanged
                    && target.Dirty == 0
                    && math.lengthsq(targetDelta) <= refreshDistance * refreshDistance)
                {
                    TargetLookup[self] = target;
                    return;
                }

                if (!targetChanged
                    && !hasNoUsablePath
                    && target.RefreshCooldownRemaining > 0f)
                {
                    TargetLookup[self] = target;
                    return;
                }

                target.Position = bestPos;
                target.Dirty = 1;
                target.RefreshCooldownRemaining = GetRefreshCooldown(self, settings.TargetRefreshInterval);
                TargetLookup[self] = target;
            }

            private static float GetRefreshCooldown(Entity entity, float interval)
            {
                if (interval <= 0f)
                    return 0f;

                uint hash = math.hash(new int2(entity.Index, entity.Version));
                float jitter = (hash & 1023u) / 1023f;
                return interval * math.lerp(0.6f, 1.4f, jitter);
            }
        }
    }
}
