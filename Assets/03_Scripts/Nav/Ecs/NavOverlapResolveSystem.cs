using MapNav.Core;
using MapNav.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace MapNav.Ecs
{
    // 유닛이 서로 겹치지 않도록 침투 깊이만큼 위치를 직접 떼어놓는 hard 충돌 해소.
    //
    // boids soft steering(NavSeparationSystem)은 겹침을 막지 못한다:
    //  (A) 빽빽한 클러스터 중앙 유닛은 사방 이웃의 밀어내기 벡터가 상쇄돼 합이 0 → strength를 곱해도 0.
    //  (B) NavMovementSystem의 "분리가 전진을 거스르면 무시" 가드 때문에 strength가 클수록 오히려 버려진다.
    // 이 시스템은 strength와 무관하게 거리 < (r_i + r_j)면 무조건 떼어놓아 겹침을 원천 차단한다.
    //
    // NavMovementSystem(추격 이동) 다음에 돌며 그 결과 위치를 보정한다. 한 프레임 1회 보정이지만
    // 매 프레임 누적되어 빽빽한 무리도 빠르게 안 겹치는 상태로 수렴한다.
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(NavMovementSystem))]
    [BurstCompile]
    public partial struct NavOverlapResolveSystem : ISystem
    {
        private EntityQuery _query;
        private EntityQuery _charQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _query = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<NavAgentSettings, LocalTransform, NavAgentDeath, NavAgentLaunch>()
                .Build(ref state);
            // 캐릭터(플레이어/장수)는 밀 수 없는 고정 몸체로 취급한다. 없어도 잡몹끼리는 해소하므로 RequireForUpdate 안 함.
            _charQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CharacterNavTarget>()
                .Build(ref state);
            state.RequireForUpdate(_query);
            state.RequireForUpdate<NavBlobReference>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton(out NavBlobReference navRef) || !navRef.Blob.IsCreated)
                return;

            int count = _query.CalculateEntityCount();
            if (count <= 1)
                return;

            NativeArray<Entity> entities = _query.ToEntityArray(Allocator.TempJob);
            NativeArray<LocalTransform> transforms = _query.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            NativeArray<NavAgentSettings> settings = _query.ToComponentDataArray<NavAgentSettings>(Allocator.TempJob);
            NativeArray<NavAgentDeath> deaths = _query.ToComponentDataArray<NavAgentDeath>(Allocator.TempJob);
            NativeArray<NavAgentLaunch> launches = _query.ToComponentDataArray<NavAgentLaunch>(Allocator.TempJob);
            NativeArray<CharacterNavTarget> characters = _charQuery.ToComponentDataArray<CharacterNavTarget>(Allocator.TempJob);

            // 셀 크기 = 최대 충돌 지름. 이웃이 항상 3x3 셀 안에 들어온다.
            float cellSize = 0.01f;
            for (int i = 0; i < count; i++)
                cellSize = math.max(cellSize, settings[i].AgentRadius * 2f);

            NativeParallelMultiHashMap<int2, int> grid =
                new NativeParallelMultiHashMap<int2, int>(count, Allocator.TempJob);
            for (int i = 0; i < count; i++)
            {
                if (deaths[i].Dying != 0 || launches[i].Airborne != 0) continue;
                grid.Add(CellOf(transforms[i].Position, cellSize), i);
            }

            NativeArray<float3> displacement = new NativeArray<float3>(count, Allocator.TempJob);

            state.Dependency = new NavOverlapJob
            {
                Transforms = transforms,
                Settings = settings,
                Deaths = deaths,
                Launches = launches,
                Characters = characters,
                Grid = grid,
                CellSize = cellSize,
                Displacement = displacement
            }.Schedule(count, 64, state.Dependency);

            ComponentLookup<LocalTransform> transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(false);
            ComponentLookup<NavAgentSettings> settingsLookup = SystemAPI.GetComponentLookup<NavAgentSettings>(true);

            state.Dependency = new NavOverlapApplyJob
            {
                Entities = entities,
                Displacement = displacement,
                Ctx = new NavContext(navRef.Blob, navRef.LocalToWorld, navRef.WorldToLocal),
                TransformLookup = transformLookup,
                SettingsLookup = settingsLookup
            }.Schedule(count, 64, state.Dependency);

            state.Dependency = entities.Dispose(state.Dependency);
            state.Dependency = transforms.Dispose(state.Dependency);
            state.Dependency = settings.Dispose(state.Dependency);
            state.Dependency = deaths.Dispose(state.Dependency);
            state.Dependency = launches.Dispose(state.Dependency);
            state.Dependency = characters.Dispose(state.Dependency);
            state.Dependency = grid.Dispose(state.Dependency);
            state.Dependency = displacement.Dispose(state.Dependency);
        }

        private static int2 CellOf(float3 p, float cellSize)
            => new int2((int)math.floor(p.x / cellSize), (int)math.floor(p.z / cellSize));

        // 각 유닛이 겹친 이웃들에서 밀려나는 변위를 계산한다(이웃 읽기만 → 병렬 안전).
        [BurstCompile]
        private struct NavOverlapJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<LocalTransform> Transforms;
            [ReadOnly] public NativeArray<NavAgentSettings> Settings;
            [ReadOnly] public NativeArray<NavAgentDeath> Deaths;
            [ReadOnly] public NativeArray<NavAgentLaunch> Launches;
            [ReadOnly] public NativeArray<CharacterNavTarget> Characters;
            [ReadOnly] public NativeParallelMultiHashMap<int2, int> Grid;
            public float CellSize;

            [NativeDisableParallelForRestriction]
            public NativeArray<float3> Displacement;

            public void Execute(int index)
            {
                Displacement[index] = float3.zero;
                if (Deaths[index].Dying != 0 || Launches[index].Airborne != 0)
                    return;

                float3 selfPos = Transforms[index].Position;
                float selfR = Settings[index].AgentRadius;
                int2 baseCell = CellOf(selfPos, CellSize);
                float3 push = float3.zero;

                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int2 cell = new int2(baseCell.x + dx, baseCell.y + dz);
                        if (!Grid.TryGetFirstValue(cell, out int other, out NativeParallelMultiHashMapIterator<int2> it))
                            continue;

                        do
                        {
                            if (other == index)
                                continue;

                            float3 away = selfPos - Transforms[other].Position;
                            away.y = 0f;
                            float minDist = selfR + Settings[other].AgentRadius;
                            float distSq = math.lengthsq(away);
                            if (distSq >= minDist * minDist)
                                continue;

                            float dist = math.sqrt(math.max(distSq, 1e-8f));
                            // 완전 겹침(dist≈0)이면 index별 결정론적 방향으로 흩는다(좌우 대칭 교착 방지).
                            float3 dir = dist > 1e-4f ? away / dist : UnitFromIndex(index);
                            push += dir * (minDist - dist) * 0.5f; // 양쪽이 절반씩 물러난다
                        }
                        while (Grid.TryGetNextValue(out other, ref it));
                    }
                }

                // 캐릭터(플레이어/장수)는 밀 수 없는 고정 몸체 → 겹치면 잡몹이 전체 침투분만큼 물러난다(0.5 아님).
                for (int c = 0; c < Characters.Length; c++)
                {
                    CharacterNavTarget ch = Characters[c];
                    if (ch.HasValue == 0)
                        continue;

                    float3 away = selfPos - ch.Position;
                    away.y = 0f;
                    float minDist = selfR + ch.BodyRadius;
                    float distSq = math.lengthsq(away);
                    if (distSq >= minDist * minDist)
                        continue;

                    float dist = math.sqrt(math.max(distSq, 1e-8f));
                    float3 dir = dist > 1e-4f ? away / dist : UnitFromIndex(index);
                    push += dir * (minDist - dist);
                }

                Displacement[index] = push;
            }

            private static float3 UnitFromIndex(int index)
            {
                float a = index * 2.3999631f;
                math.sincos(a, out float s, out float c);
                return new float3(c, 0f, s);
            }
        }

        // 계산된 변위를 위치에 적용한다. nav 경계 안에서만 이동해 벽을 뚫지 않는다.
        // 각 워커가 서로 다른 엔티티만 쓰므로 LocalTransform write는 병렬 안전.
        [BurstCompile]
        private struct NavOverlapApplyJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity> Entities;
            [ReadOnly] public NativeArray<float3> Displacement;
            public NavContext Ctx;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<LocalTransform> TransformLookup;
            [ReadOnly] public ComponentLookup<NavAgentSettings> SettingsLookup;

            public void Execute(int index)
            {
                float3 d = Displacement[index];
                if (math.lengthsq(d) <= 1e-8f)
                    return;

                Entity e = Entities[index];
                LocalTransform tr = TransformLookup[e];
                NavAgentSettings st = SettingsLookup[e];
                float3 next = tr.Position + d;

                // StepHeight를 ctx에 실어, 밟는 장애물 위에서도 겹침 해소 이동이 막히지 않도록 한다.
                NavContext stepCtx = new NavContext(Ctx.Blob, Ctx.LocalToWorld, Ctx.WorldToLocal, st.StepHeight);
                if (NavAgentCore.CanMove(in stepCtx, tr.Position, next, next, st.AgentRadius, st.BoundaryTolerance, 0f))
                {
                    tr.Position = next;
                    TransformLookup[e] = tr;
                }
            }
        }
    }
}
