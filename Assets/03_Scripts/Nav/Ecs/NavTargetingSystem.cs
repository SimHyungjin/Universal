using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace MapNav.Ecs
{
    // 각 유닛이 가장 가까운 적대 진영 대상(다른 진영 유닛 또는 캐릭터)을 찾아 길찾기 타겟으로 삼는다.
    // 공간 해시 그리드로 근접 유닛만 검사해 O(n^2) 전수 검사를 피한다.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(NavTargetResolveSystem))]
    [BurstCompile]
    public partial struct NavTargetingSystem : ISystem
    {
        // 유닛이 적대 대상을 인지하는 최대 거리.
        private const float SearchRadius = 20f;

        private EntityQuery _query;
        private EntityQuery _characterQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _query = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<NavAgentFaction, NavAgentDeath, NavAgentSettings, LocalTransform, NavAgentTarget, NavAgentPathStatus, NavAgentPathRequest>()
                .Build(ref state);
            // 캐릭터(플레이어/장수)는 0명 이상일 수 있다. RequireForUpdate를 걸지 않아 캐릭터가 없어도 잡몹끼리 타겟팅한다.
            _characterQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CharacterNavTarget>()
                .Build(ref state);
            state.RequireForUpdate(_query);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            int count = _query.CalculateEntityCount();
            if (count <= 0)
                return;

            NativeArray<Entity> characterEntities = _characterQuery.ToEntityArray(Allocator.TempJob);
            NativeArray<CharacterNavTarget> characters = _characterQuery.ToComponentDataArray<CharacterNavTarget>(Allocator.TempJob);

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
                Characters = characters,
                CharacterEntities = characterEntities,
                TargetLookup = SystemAPI.GetComponentLookup<NavAgentTarget>(),
                CombatLookup = SystemAPI.GetComponentLookup<NavAgentCombatTarget>(),
                RequestLookup = SystemAPI.GetComponentLookup<NavAgentPathRequest>(),
                StatusLookup = SystemAPI.GetComponentLookup<NavAgentPathStatus>(),
                MotionLookup = SystemAPI.GetComponentLookup<NavAgentMotion>(),
                WaypointLookup = SystemAPI.GetBufferLookup<NavAgentWaypoint>()
            };

            state.Dependency = job.Schedule(count, 32, state.Dependency);
            state.Dependency = characters.Dispose(state.Dependency);
            state.Dependency = characterEntities.Dispose(state.Dependency);
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
            [ReadOnly] public NativeArray<CharacterNavTarget> Characters;
            [ReadOnly] public NativeArray<Entity> CharacterEntities;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<NavAgentTarget> TargetLookup;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<NavAgentCombatTarget> CombatLookup;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<NavAgentPathRequest> RequestLookup;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<NavAgentPathStatus> StatusLookup;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<NavAgentMotion> MotionLookup;
            [NativeDisableParallelForRestriction]
            public BufferLookup<NavAgentWaypoint> WaypointLookup;

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
                bool targetIsCharacter = false;

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
                                targetIsCharacter = false;
                            }
                        }
                        while (Grid.TryGetNextValue(out other, ref iterator));
                    }
                }

                // GameObject 캐릭터(플레이어/장수)는 감지 반경(SearchRadius) 안에 들어오면 더 가까운 잡몹이
                // 있어도 무조건 우선 타겟이 된다 — 반경 안에서는 항상 플레이어 우선. 반경 밖이면 무시하고
                // 잡몹은 진영전(유닛 타겟)을 유지한다(맵 전역 잡몹이 플레이어로 수렴하지 않게). 반경 밖에서
                // 맞은 경우는 아래 강제 어그로가 이 제한을 무시한다. 캐릭터가 여럿이면 그중 최근접을 고른다.
                float bestCharDistSq = SearchRadiusSq;
                for (int c = 0; c < Characters.Length; c++)
                {
                    CharacterNavTarget ch = Characters[c];
                    if (ch.HasValue == 0 || ch.Faction == selfFaction)
                        continue;

                    float3 diff = ch.Position - selfPos;
                    diff.y = 0f;
                    float distSq = math.lengthsq(diff);
                    if (distSq > bestCharDistSq)
                        continue;

                    bestCharDistSq = distSq;
                    bestPos = ch.Position;
                    bestEntity = CharacterEntities[c];
                    found = true;
                    targetIsCharacter = true;
                }

                // 감지 반경(SearchRadius) 안에 적 유닛도 캐릭터도 없으면, 범위를 무제한으로 넓혀
                // 맵 전체에서 최근접 적(유닛 또는 플레이어/장수)을 찾는다. 멀리 떨어져 스폰된 유닛이
                // 가만히 있거나, 적 유닛이 없을 때 플레이어가 반경 밖이라고 멈춰 서는 것을 막는다.
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
                        targetIsCharacter = false;
                    }
                    for (int c = 0; c < Characters.Length; c++)
                    {
                        CharacterNavTarget ch = Characters[c];
                        if (ch.HasValue == 0 || ch.Faction == selfFaction)
                            continue;

                        float3 diff = ch.Position - selfPos;
                        diff.y = 0f;
                        float distSq = math.lengthsq(diff);
                        if (distSq >= bestDistSq)
                            continue;

                        bestDistSq = distSq;
                        bestPos = ch.Position;
                        bestEntity = CharacterEntities[c];
                        found = true;
                        targetIsCharacter = true;
                    }
                }

                Entity self = Entities[index];

                NavAgentCombatTarget combat = CombatLookup[self];

                // 피격 강제 어그로: 타이머가 살아있으면 일반 탐색 결과를 덮어 공격자(캐릭터)를 거리 무시하고
                // 우선 추적한다. 공격자가 사라졌거나 같은 진영이 되면(또는 타이머 만료) 자연 해제된다.
                combat.ForcedTimer = math.max(0f, combat.ForcedTimer - DeltaTime);
                if (combat.ForcedTimer > 0f && combat.ForcedEntity != Entity.Null)
                {
                    for (int c = 0; c < CharacterEntities.Length; c++)
                    {
                        if (CharacterEntities[c] != combat.ForcedEntity)
                            continue;

                        CharacterNavTarget ch = Characters[c];
                        if (ch.HasValue != 0 && ch.Faction != selfFaction)
                        {
                            bestPos = ch.Position;
                            bestEntity = combat.ForcedEntity;
                            found = true;
                            targetIsCharacter = true;
                        }
                        break;
                    }
                }

                bool targetChanged = combat.HasTarget == 0
                    || combat.TargetEntity != bestEntity
                    || combat.IsCharacterTarget != (byte)(targetIsCharacter ? 1 : 0);

                combat.HasTarget = (byte)(found ? 1 : 0);
                combat.TargetEntity = bestEntity;
                combat.IsCharacterTarget = (byte)(targetIsCharacter ? 1 : 0);
                if (found)
                    combat.Position = bestPos;
                CombatLookup[self] = combat;

                if (!found)
                {
                    ClearNavigation(self, selfPos);
                    return;
                }

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
                    && !hasNoUsablePath
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

            private void ClearNavigation(Entity self, float3 selfPos)
            {
                NavAgentTarget target = TargetLookup[self];
                target.Dirty = 0;
                target.Position = selfPos;
                target.AcceptedPosition = selfPos;
                target.RefreshCooldownRemaining = 0f;
                TargetLookup[self] = target;

                NavAgentPathRequest request = RequestLookup[self];
                request.Pending = 0;
                RequestLookup[self] = request;

                NavAgentPathStatus status = StatusLookup[self];
                status.HasPath = 0;
                status.Waiting = 0;
                status.Failed = 0;
                StatusLookup[self] = status;

                NavAgentMotion motion = MotionLookup[self];
                motion.IsMoving = 0;
                motion.WaypointIndex = 0;
                motion.CurrentSpeed = 0f;
                motion.StuckTimer = 0f;
                motion.LastDistanceToWaypoint = 0f;
                motion.LastWaypointAnchor = selfPos;
                motion.Velocity = float3.zero;
                MotionLookup[self] = motion;

                if (WaypointLookup.HasBuffer(self))
                    WaypointLookup[self].Clear();
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
