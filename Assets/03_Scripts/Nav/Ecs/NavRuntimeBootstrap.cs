using System.Collections.Generic;
using MapNav.Baking;
using MapNav.Core;
using MapNav.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Serialization;

namespace MapNav.Ecs
{
    [DisallowMultipleComponent]
    public sealed class NavRuntimeBootstrap : MonoBehaviour
    {
        [SerializeField] private MapNavigationAuthoring map;
        [SerializeField] private bool spawnAgents = true;
        [FormerlySerializedAs("agentCount")]
        [SerializeField] private int enemyAgentCount = 25;
        [SerializeField] private int allyAgentCount = 8;
        [SerializeField] private int maxSampleAttempts = 64;

        [Header("Unit stats")]
        [FormerlySerializedAs("unitStats")]
        [SerializeField] private SO_Unit_Data unitData;
        [SerializeField] private float attackRangeRandomMin = 0.9f;
        [SerializeField] private float attackRangeRandomMax = 1.1f;

        [Header("Pathfinding tuning")]
        [SerializeField] private float waypointAdvanceDistance = 0.35f;
        [SerializeField] private float cornerLookAheadDistance;
        [SerializeField] private float heightOffset;
        [SerializeField] private float boundaryTolerance = 0.05f;
        [SerializeField] private float targetRepathDistance = 0.15f;
        [SerializeField] private float movingTargetRepathDistance = 0.75f;
        [SerializeField] private float movingTargetRepathInterval = 0.2f;
        [SerializeField] private float stuckRepathDelay = 0.75f;
        [SerializeField] private float stuckRepathCooldown = 1.5f;
        [SerializeField] private float stuckProgressDistance = 0.03f;
        [SerializeField] private int stuckRetryLimit = 4;

        [Header("Separation")]
        [SerializeField] private float separationRadius = 0.35f;
        [SerializeField] private float separationStrength = 0.45f;
        [SerializeField] private int separationMaxNeighbors = 8;

        [Header("Encircle / crowd")]
        [Tooltip("포위 링 거리 = 공격 사거리 × 이 값. 작을수록 타겟에 바짝, 클수록 멀리 둘러싼다.")]
        [SerializeField] private float encircleRingFactor = 0.85f;
        [Tooltip("ring 안으로 밀려든 유닛이 타겟에서 멀어지는 후퇴 가속. 클수록 더 빠르게 튕겨나간다.")]
        [SerializeField] private float retreatGain = 3f;

        [Header("Faction cluster spawn")]
        [Tooltip("진영별 스폰을 모을 시드 클러스터 반경. 두 진영이 nav 영역 양 끝에 뭉쳐 전선을 이룬다.")]
        [SerializeField] private float factionClusterRadius = 12f;
        [Tooltip("이 비율만큼은 클러스터를 무시하고 전체 nav 영역에 랜덤 산개(난전 느낌).")]
        [SerializeField, Range(0f, 1f)] private float spawnRandomFraction = 0.3f;

        [Header("Player-aware spawn safety")]
        [SerializeField] private float playerSpawnExclusionRadius = 10f;
        [SerializeField] private float playerForwardSpawnExclusionDistance = 16f;
        [SerializeField, Range(0f, 180f)] private float playerForwardSpawnExclusionAngle = 100f;

        [Header("Path build")]
        [SerializeField] private int maxPathsPerFrame = 32;

        private readonly List<MapNavRegion> _candidateRegions = new();
        private readonly Dictionary<Entity, SO_Unit_Data> _agentData = new();
        private readonly Dictionary<MapNavigationAuthoring, BlobAssetReference<NavBlob>> _mapBlobCache = new();
        private readonly List<Vector3> _allySeeds = new();  // 현재 섹터의 진영별 스폰 시드(게이트 기반 전선).
        private readonly List<Vector3> _enemySeeds = new();
        private BlobAssetReference<NavBlob> _ownedBlob;
        private Entity _singleton = Entity.Null;
        private World _ownedWorld;
        private Transform _playerTransform;

        public static NavRuntimeBootstrap Instance { get; private set; }
        public SO_Unit_Data GetUnitData(Entity entity)
            => _agentData.TryGetValue(entity, out SO_Unit_Data data) ? data : null;
        public void ForgetUnitData(Entity entity) => _agentData.Remove(entity);

        private void Awake()  { Instance = this; }
        private void Start()  => Bootstrap();
        private void LateUpdate() => RefreshSingletonTransform();

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            DestroySingleton();
            ClearCachedBlobReferences();
        }

        private void DestroySingleton()
        {
            if (_singleton == Entity.Null) return;
            if (_ownedWorld != null && _ownedWorld.IsCreated)
            {
                EntityManager em = _ownedWorld.EntityManager;
                if (em.Exists(_singleton)) em.DestroyEntity(_singleton);
            }
            _singleton = Entity.Null;
            _ownedWorld = null;
        }

        // Keep the ECS singleton's transform in sync with the live map GameObject so a
        // moving/rotating nav map doesn't invalidate every classify/path query. The matrices
        // were otherwise captured once at bootstrap. Only the bootstrap that created the
        // singleton (_singleton != Null) refreshes it; non-owners leave it untouched.
        private void RefreshSingletonTransform()
        {
            if (_singleton == Entity.Null || map == null) return;
            if (_ownedWorld == null || !_ownedWorld.IsCreated) return;

            EntityManager em = _ownedWorld.EntityManager;
            if (!em.Exists(_singleton)) return;

            NavBlobReference navRef = em.GetComponentData<NavBlobReference>(_singleton);
            navRef.LocalToWorld = map.transform.localToWorldMatrix;
            navRef.WorldToLocal = map.transform.worldToLocalMatrix;
            em.SetComponentData(_singleton, navRef);
        }

        [ContextMenu("Bootstrap Nav ECS")]
        public void Bootstrap()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) { SetStatus("No default ECS world."); return; }
            if (map == null) { SetStatus("No MapNavigationAuthoring assigned."); return; }

            EntityManager em = world.EntityManager;
            EnsureSingleton(em);

            int spawned = spawnAgents ? SpawnAgents(em) : 0;
            LogBakedGraphStats();
            SetStatus($"Singleton=ready, Spawned={spawned}");
        }

        [ContextMenu("Log Agent Status Histogram")]
        public void LogAgentStatusHistogram()
        {
            World w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) { Debug.Log("No default ECS world."); return; }
            EntityManager em = w.EntityManager;

            EntityQuery query = em.CreateEntityQuery(
                ComponentType.ReadOnly<NavAgentPathStatus>(),
                ComponentType.ReadOnly<NavAgentMotion>(),
                ComponentType.ReadOnly<NavAgentTarget>(),
                ComponentType.ReadOnly<LocalTransform>());

            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            int total = entities.Length;
            int failed = 0, waiting = 0, hasPath = 0, moving = 0, idle = 0, atRetryLimit = 0;
            int retryLimit = math.max(0, stuckRetryLimit);
            List<string> failedSamples = new();
            const int MaxFailedSamples = 5;

            for (int i = 0; i < total; i++)
            {
                Entity e = entities[i];
                NavAgentPathStatus s = em.GetComponentData<NavAgentPathStatus>(e);
                NavAgentMotion mt = em.GetComponentData<NavAgentMotion>(e);
                NavAgentTarget tg = em.GetComponentData<NavAgentTarget>(e);
                LocalTransform tr = em.GetComponentData<LocalTransform>(e);

                if (s.HasPath != 0)
                {
                    hasPath++;
                    if (mt.IsMoving != 0) moving++;
                }
                else if (s.Failed != 0) failed++;
                else if (s.Waiting != 0) waiting++;
                else idle++;

                if (retryLimit > 0 && mt.StuckRetryCount >= retryLimit) atRetryLimit++;

                if (s.Failed != 0 && failedSamples.Count < MaxFailedSamples)
                {
                    failedSamples.Add($"  Entity#{e.Index} pos=({tr.Position.x:F2},{tr.Position.y:F2},{tr.Position.z:F2}) target=({tg.AcceptedPosition.x:F2},{tg.AcceptedPosition.y:F2},{tg.AcceptedPosition.z:F2}) retries={mt.StuckRetryCount}");
                }
            }

            entities.Dispose();
            query.Dispose();

            System.Text.StringBuilder sb = new();
            sb.AppendLine($"[NavAgent stats] Total={total} HasPath={hasPath} (Moving={moving}) Failed={failed} Waiting={waiting} Idle={idle} StuckLimitReached={atRetryLimit}");
            if (failedSamples.Count > 0)
            {
                sb.AppendLine($"First {failedSamples.Count} Failed agents:");
                for (int i = 0; i < failedSamples.Count; i++) sb.AppendLine(failedSamples[i]);
            }
            Debug.Log(sb.ToString(), this);
        }

        [ContextMenu("Probe Failed Agents")]
        public void ProbeFailedAgents()
        {
            World w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) { Debug.Log("No default ECS world."); return; }
            EntityManager em = w.EntityManager;

            EntityQuery navRefQuery = em.CreateEntityQuery(ComponentType.ReadOnly<NavBlobReference>());
            if (navRefQuery.IsEmptyIgnoreFilter) { Debug.Log("No NavBlobReference singleton."); navRefQuery.Dispose(); return; }
            NavBlobReference navRef = navRefQuery.GetSingleton<NavBlobReference>();
            navRefQuery.Dispose();
            if (!navRef.Blob.IsCreated) { Debug.Log("NavBlob not created."); return; }

            NavContext ctx = new NavContext(navRef.Blob, navRef.LocalToWorld, navRef.WorldToLocal);

            EntityQuery agentQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<NavAgentPathStatus>(),
                ComponentType.ReadOnly<NavAgentTarget>(),
                ComponentType.ReadOnly<NavAgentSettings>(),
                ComponentType.ReadOnly<LocalTransform>());
            NativeArray<Entity> entities = agentQuery.ToEntityArray(Allocator.Temp);

            const int MaxProbe = 10;
            int probed = 0;
            System.Text.StringBuilder sb = new();

            // Sanity dump: live agent settings + baked obstacle clearance
            ref NavBlob blob = ref navRef.Blob.Value;
            float minPad = float.PositiveInfinity, maxPad = float.NegativeInfinity, sumPad = 0f;
            for (int o = 0; o < blob.Obstacles.Length; o++)
            {
                float p = blob.Obstacles[o].CornerPadding;
                minPad = math.min(minPad, p);
                maxPad = math.max(maxPad, p);
                sumPad += p;
            }
            int obsCount = blob.Obstacles.Length;
            sb.AppendLine($"NavBlob.Obstacles[{obsCount}] CornerPadding min={minPad:F3} max={maxPad:F3} avg={(obsCount > 0 ? sumPad / obsCount : 0f):F3}");

            sb.AppendLine($"Probing up to {MaxProbe} Failed agents:");

            for (int i = 0; i < entities.Length && probed < MaxProbe; i++)
            {
                Entity e = entities[i];
                NavAgentPathStatus s = em.GetComponentData<NavAgentPathStatus>(e);
                if (s.Failed == 0) continue;

                NavAgentSettings settings = em.GetComponentData<NavAgentSettings>(e);
                NavAgentTarget tg = em.GetComponentData<NavAgentTarget>(e);
                LocalTransform tr = em.GetComponentData<LocalTransform>(e);

                float3 start = tr.Position;
                float3 end = tg.AcceptedPosition;
                float tol = settings.BoundaryTolerance;

                bool sClassify = NavQuery.TryClassify(in ctx, start, tol, out NavSpaceRef startSpace);
                bool sProject = false;
                if (!sClassify)
                {
                    sProject = NavQuery.TryProjectToNearestSpace(in ctx, start, out _, out NavSpaceRef startProjSpace);
                    if (sProject) startSpace = startProjSpace;
                }

                bool eClassify = NavQuery.TryClassify(in ctx, end, tol, out NavSpaceRef endSpace);
                bool eProject = false;
                if (!eClassify)
                {
                    eProject = NavQuery.TryProjectToNearestSpace(in ctx, end, out _, out NavSpaceRef endProjSpace);
                    if (eProject) endSpace = endProjSpace;
                }

                NavScratch scratch = new NavScratch(64, Allocator.Temp);
                NativeList<NavSpaceRef> nodes = new NativeList<NavSpaceRef>(16, Allocator.Temp);
                NativeList<NavPortal> portals = new NativeList<NavPortal>(16, Allocator.Temp);
                NativeList<float3> waypoints = new NativeList<float3>(16, Allocator.Temp);

                bool built = NavPath.TryBuild(in ctx, start, end, settings.AgentRadius, tol,
                    ref scratch, ref nodes, ref portals, ref waypoints);
                int wpCount = waypoints.Length;
                int nodeCount = nodes.Length;
                int portalCount = portals.Length;
                System.Text.StringBuilder diagnosis = null;
                if (!built)
                {
                    diagnosis = new System.Text.StringBuilder();
                    NavPath.TryDiagnoseBuild(in ctx, start, end, settings.AgentRadius, tol,
                        ref scratch, ref nodes, ref portals, ref waypoints, diagnosis);
                }

                string startTag = sClassify ? "C" : (sProject ? "P" : "X");
                string endTag = eClassify ? "C" : (eProject ? "P" : "X");

                sb.AppendLine(
                    $"  Entity#{e.Index} R={settings.AgentRadius:F2} tol={tol:F2}: start({start.x:F1},{start.z:F1}) {startTag}->{startSpace.Kind}:{startSpace.Id} | " +
                    $"end({end.x:F1},{end.z:F1}) {endTag}->{endSpace.Kind}:{endSpace.Id} | " +
                    $"built={built} nodes={nodeCount} portals={portalCount} wp={wpCount}");
                if (diagnosis != null && diagnosis.Length > 0)
                    sb.AppendLine($"    diagnose: {diagnosis}");

                scratch.Dispose(); nodes.Dispose(); portals.Dispose(); waypoints.Dispose();
                probed++;
            }

            entities.Dispose();
            agentQuery.Dispose();
            Debug.Log(sb.ToString(), this);
        }

        // 순간이동·넉백 벽뚫기·땅 박힘의 실제 원인을 런타임 값으로 드러내는 읽기 전용 진단.
        // 플레이 중 컴포넌트 인스펙터에서 우클릭 → 실행. 게임 로직은 건드리지 않는다.
        [ContextMenu("Probe Live Agent Nav State")]
        public void ProbeLiveAgents()
        {
            World w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) { Debug.Log("No default ECS world."); return; }
            EntityManager em = w.EntityManager;

            EntityQuery navRefQuery = em.CreateEntityQuery(ComponentType.ReadOnly<NavBlobReference>());
            if (navRefQuery.IsEmptyIgnoreFilter) { Debug.Log("No NavBlobReference singleton."); navRefQuery.Dispose(); return; }
            NavBlobReference navRef = navRefQuery.GetSingleton<NavBlobReference>();
            navRefQuery.Dispose();
            if (!navRef.Blob.IsCreated) { Debug.Log("NavBlob NOT created (dangling!)."); return; }

            NavContext ctx = new NavContext(navRef.Blob, navRef.LocalToWorld, navRef.WorldToLocal);
            ref NavBlob blob = ref navRef.Blob.Value;

            System.Text.StringBuilder sb = new();
            float4x4 l2w = navRef.LocalToWorld;
            sb.AppendLine($"[Probe] Regions={blob.Regions.Length} Transitions={blob.Transitions.Length} Obstacles={blob.Obstacles.Length}");
            sb.AppendLine($"[Probe] map translation=({l2w.c3.x:F2},{l2w.c3.y:F2},{l2w.c3.z:F2})  basisX=({l2w.c0.x:F2},{l2w.c0.y:F2},{l2w.c0.z:F2})  basisY=({l2w.c1.x:F2},{l2w.c1.y:F2},{l2w.c1.z:F2})");

            EntityQuery q = em.CreateEntityQuery(
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<NavAgentSettings>(),
                ComponentType.ReadOnly<NavAgentKnockback>(),
                ComponentType.ReadOnly<NavAgentLaunch>(),
                ComponentType.ReadOnly<NavAgentMotion>(),
                ComponentType.ReadOnly<NavAgentDeath>(),
                ComponentType.ReadOnly<NavAgentWaypoint>());
            NativeArray<Entity> entities = q.ToEntityArray(Allocator.Temp);

            int probe = math.min(64, entities.Length);
            sb.AppendLine($"[Probe] Probing {probe}/{entities.Length} live agents:");
            for (int i = 0; i < probe; i++)
            {
                Entity e = entities[i];
                LocalTransform tr = em.GetComponentData<LocalTransform>(e);
                NavAgentSettings st = em.GetComponentData<NavAgentSettings>(e);
                NavAgentKnockback kb = em.GetComponentData<NavAgentKnockback>(e);
                NavAgentLaunch lf = em.GetComponentData<NavAgentLaunch>(e);
                NavAgentMotion mo = em.GetComponentData<NavAgentMotion>(e);
                NavAgentDeath dh = em.GetComponentData<NavAgentDeath>(e);
                DynamicBuffer<NavAgentWaypoint> wps = em.GetBuffer<NavAgentWaypoint>(e);

                float3 p = tr.Position;
                bool cls = NavQuery.TryClassify(in ctx, p, st.BoundaryTolerance, out NavSpaceRef sp);
                bool clear = NavQuery.IsClearOfObstaclePadding(in ctx, p, st.AgentRadius);
                bool gotH = NavQuery.TryGetHeight(in ctx, p, st.BoundaryTolerance, out float h);
                float dy = gotH ? (h + st.HeightOffset - p.y) : 0f;

                sb.AppendLine(
                    $"#{e.Index} pos=({p.x:F2},{p.y:F2},{p.z:F2}) classify={(cls ? sp.Kind.ToString() : "FAIL")} " +
                    $"clearOfObstacle={clear} snapH={(gotH ? h.ToString("F2") : "FAIL")} dy={dy:F2} " +
                    $"kbVel=({kb.Velocity.x:F2},{kb.Velocity.z:F2}) airborne={lf.Airborne} groundY={lf.GroundY:F2} vVel={lf.VerticalVelocity:F2} lHeight={lf.Height:F2} suspend={lf.SuspendTimer:F2} dying={dh.Dying} moving={mo.IsMoving} wp={wps.Length}");

                for (int k = 0; k < math.min(3, wps.Length); k++)
                {
                    float3 wp = wps[k].Position;
                    bool wcls = NavQuery.TryClassify(in ctx, wp, st.BoundaryTolerance, out NavSpaceRef wsp);
                    sb.AppendLine($"    wp[{k}]=({wp.x:F2},{wp.y:F2},{wp.z:F2}) classify={(wcls ? wsp.Kind.ToString() : "FAIL")}");
                }
            }
            entities.Dispose();
            q.Dispose();
            Debug.Log(sb.ToString(), this);
        }

        [ContextMenu("Log Baked Graph Stats")]
        public void LogBakedGraphStats()
        {
            if (map == null) { Debug.Log("No MapNavigationAuthoring."); return; }
            var blob = _ownedBlob.IsCreated ? _ownedBlob : map.NavBlobData;
            if (!blob.IsCreated) { Debug.Log("NavBlob not created."); return; }
            ref NavBlob b = ref blob.Value;

            int totalRegionEdges = 0;
            int isolatedRegions = 0;
            for (int i = 0; i < b.RegionEdgeRange.Length; i++)
            {
                int cnt = b.RegionEdgeRange[i].y;
                totalRegionEdges += cnt;
                if (cnt == 0) isolatedRegions++;
            }
            int totalTransEdges = 0;
            for (int i = 0; i < b.TransitionEdgeRange.Length; i++)
                totalTransEdges += b.TransitionEdgeRange[i].y;

            // Debug.Log(
            //     $"[NavBlob stats] Regions={b.Regions.Length} Transitions={b.Transitions.Length} Obstacles={b.Obstacles.Length} " +
            //     $"RegionEdges={totalRegionEdges} TransitionEdges={totalTransEdges} IsolatedRegions={isolatedRegions}", this);

            for (int i = 0; i < b.Regions.Length; i++)
            {
                NavRegion r = b.Regions[i];
                int outEdges = b.RegionEdgeRange[i].y;
                //Debug.Log($"  Region id={r.Id} h={r.Height:F2} obstacles={r.ObstacleCount} outEdges={outEdges}", this);
            }
            for (int i = 0; i < b.Transitions.Length; i++)
            {
                NavTransition t = b.Transitions[i];
                int outEdges = b.TransitionEdgeRange[i].y;
                //Debug.Log($"  Transition id={t.Id} from={t.FromRegionId} to={t.ToRegionId} type={t.Type} bidir={t.Bidirectional} enabled={t.Enabled} outEdges={outEdges}", this);
            }
        }

        private void EnsureSingleton(EntityManager em)
        {
            EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<NavBlobReference>());
            bool alreadyExists = !query.IsEmptyIgnoreFilter;
            query.Dispose();

            // If a singleton already exists, another bootstrap (or domain-reload remnant) owns the blob.
            // Don't fight over it — leave it untouched and skip baking our own copy.
            if (alreadyExists) return;

            _ownedBlob = GetOrBuildCachedBlob(map);

            _ownedWorld = em.World;
            _singleton = em.CreateEntity(typeof(NavBlobReference), typeof(NavPathBuildBudget));
            em.SetComponentData(_singleton, new NavBlobReference
            {
                Blob = _ownedBlob,
                LocalToWorld = map.transform.localToWorldMatrix,
                WorldToLocal = map.transform.worldToLocalMatrix
            });
            em.SetComponentData(_singleton, new NavPathBuildBudget { MaxPathsPerFrame = math.max(1, maxPathsPerFrame) });
        }

        private int SpawnAgents(EntityManager em)
        {
            RebuildCandidates();
            if (_candidateRegions.Count == 0) return 0;

            int spawned = 0;
            spawned += SpawnFaction(em, NavFaction.Ally, allyAgentCount);
            spawned += SpawnFaction(em, NavFaction.Enemy, enemyAgentCount);
            return spawned;
        }

        // 섹터 진입 시 그 섹터 자신의 MapNavigationAuthoring으로 nav 그래프를 교체한다.
        // 공유 맵을 옮기던 기존 AlignMapTo와 달리, 각 섹터는 이미 제자리에 자기 블롭을 갖고 있으므로
        // 트랜스폼은 건드리지 않고 ECS 싱글톤이 가리키는 블롭/행렬만 그 섹터 것으로 바꾼다.
        public void SwitchMap(MapNavigationAuthoring sectorMap)
        {
            if (sectorMap == null)
            {
                Debug.LogWarning($"[{nameof(NavRuntimeBootstrap)}] SwitchMap: 섹터에 MapNavigationAuthoring이 없습니다.", this);
                return;
            }

            map = sectorMap;

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            EntityManager em = world.EntityManager;

            if (_singleton == Entity.Null)
                EnsureSingleton(em);         // 최초 진입: 이 섹터 맵으로 싱글톤 생성
            else
                RebakeOwnedBlobAndApply(em); // 이후 진입: 소유 블롭을 이 섹터 맵으로 재베이크 후 교체
        }

        // 싱글톤이 가리키는 블롭을 이 부트스트랩이 소유한 복사본으로 유지한다.
        // (authoring이 OnDestroy에서 자기 블롭을 dispose해도 앱-글로벌 ECS 월드가 댕글링되지 않도록.)
        private void RebakeOwnedBlobAndApply(EntityManager em)
        {
            if (map == null || !em.Exists(_singleton)) return;

            _ownedBlob = GetOrBuildCachedBlob(map);

            NavBlobReference navRef = em.GetComponentData<NavBlobReference>(_singleton);
            navRef.Blob         = _ownedBlob;
            navRef.LocalToWorld = map.transform.localToWorldMatrix;
            navRef.WorldToLocal = map.transform.worldToLocalMatrix;
            em.SetComponentData(_singleton, navRef);
        }

        private BlobAssetReference<NavBlob> GetOrBuildCachedBlob(MapNavigationAuthoring source)
        {
            if (source == null)
                return default;

            if (_mapBlobCache.TryGetValue(source, out BlobAssetReference<NavBlob> cached) && cached.IsCreated)
                return cached;

            // authoring이 소유·Dispose하는 NavBlobData를 그대로 참조하면, authoring이 dirty 상태에서
            // 재빌드할 때 그 blob을 Dispose해 ECS 싱글톤이 dangling이 된다(순간이동·넉백 벽뚫기·y 박힘).
            // baker를 직접 호출해 부트스트랩이 소유하는 독립 복사본을 만들어 authoring 수명과 분리한다.
            BlobAssetReference<NavBlob> blob = MapNavBaker.Build(source, Allocator.Persistent);
            _mapBlobCache[source] = blob;
            return blob;
        }

        public void PrewarmMap(MapNavigationAuthoring source)
        {
            if (source == null)
                return;

            GetOrBuildCachedBlob(source);
        }

        // 장수(Mono) 등 외부 소비자가 authoring.NavBlobData를 직접 읽으면 dirty 상태에서 dispose-rebuild를
        // 트리거해 ECS 싱글톤 blob을 dangling으로 만든다. 부트스트랩이 소유한 독립 blob을 공유해 그 트리거를 없앤다.
        public BlobAssetReference<NavBlob> GetSharedBlob(MapNavigationAuthoring source)
            => GetOrBuildCachedBlob(source);

        private void ClearCachedBlobReferences()
        {
            // 캐시된 blob은 이제 부트스트랩 소유(baker로 직접 빌드)이므로 여기서 Dispose 책임을 진다.
            // _ownedBlob은 캐시 항목 중 하나를 가리키므로 별도 Dispose하지 않는다(중복 Dispose 방지).
            foreach (BlobAssetReference<NavBlob> blob in _mapBlobCache.Values)
            {
                if (blob.IsCreated)
                    blob.Dispose();
            }
            _mapBlobCache.Clear();
            _ownedBlob = default;
        }

        public void DrainAllAgents()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            EntityManager em = world.EntityManager;
            EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<NavAgentFaction>());
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            em.DestroyEntity(entities);
            entities.Dispose();
            query.Dispose();
            _agentData.Clear();
        }

        public int SpawnAgents(NavAgentSpawnEntry[] entries)
        {
            if (entries == null || entries.Length == 0) return 0;

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return 0;

            EntityManager em = world.EntityManager;
            RebuildCandidates();
            if (_candidateRegions.Count == 0) return 0;

            int total = 0;
            for (int i = 0; i < entries.Length; i++)
                total += SpawnEntry(em, entries[i]);
            return total;
        }

        public async Cysharp.Threading.Tasks.UniTask SpawnAgentsGradually(
            NavAgentSpawnEntry[] entries,
            int batchSize = 3,
            System.Threading.CancellationToken ct = default)
        {
            if (entries == null || entries.Length == 0) return;

            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            EntityManager em = world.EntityManager;
            RebuildCandidates();
            if (_candidateRegions.Count == 0) return;

            for (int i = 0; i < entries.Length; i++)
            {
                NavAgentSpawnEntry entry = entries[i];
                if (entry.Data == null || entry.Count <= 0) continue;

                SO_Unit_Stats stats = entry.Stats;
                float clearance = stats != null ? stats.AgentRadius : 0.35f;
                float rangeMin  = math.min(attackRangeRandomMin, attackRangeRandomMax);
                float rangeMax  = math.max(attackRangeRandomMin, attackRangeRandomMax);
                int remaining   = entry.Count;

                while (remaining > 0)
                {
                    int batch = math.min(remaining, batchSize);
                    for (int j = 0; j < batch; j++)
                    {
                        if (!TryFactionSpawnPoint(entry.Faction, clearance, out Vector3 pos)) continue;
                        SpawnSingleAgent(em, entry.Data, pos, UnityEngine.Random.Range(rangeMin, rangeMax), entry.Faction);
                    }
                    remaining -= batch;
                    await Cysharp.Threading.Tasks.UniTask.Yield(
                        Cysharp.Threading.Tasks.PlayerLoopTiming.Update, ct);
                }
            }
        }

        private int SpawnEntry(EntityManager em, NavAgentSpawnEntry entry)
        {
            if (entry.Data == null || entry.Count <= 0) return 0;

            float rangeMin  = math.min(attackRangeRandomMin, attackRangeRandomMax);
            float rangeMax  = math.max(attackRangeRandomMin, attackRangeRandomMax);
            SO_Unit_Stats stats = entry.Stats;
            float clearance = stats != null ? stats.AgentRadius : 0.35f;
            int spawned = 0;

            for (int i = 0; i < entry.Count; i++)
            {
                if (!TryFactionSpawnPoint(entry.Faction, clearance, out Vector3 pos)) continue;
                SpawnSingleAgent(em, entry.Data, pos, UnityEngine.Random.Range(rangeMin, rangeMax), entry.Faction);
                spawned++;
            }
            return spawned;
        }

        private void SpawnSingleAgent(EntityManager em, SO_Unit_Data data, Vector3 pos, float attackRangeMultiplier, NavFaction faction)
        {
            SO_Unit_Stats stats = data != null ? data.StatsData : null;
            Entity e = em.CreateEntity(
                typeof(LocalTransform),
                typeof(NavAgentSettings),
                typeof(NavAgentMotion),
                typeof(NavAgentTarget),
                typeof(NavAgentPathRequest),
                typeof(NavAgentPathStatus),
                typeof(NavAgentSeparation),
                typeof(NavAgentKnockback),
                typeof(NavAgentLaunch),
                typeof(NavAgentHealth),
                typeof(NavAgentDeath),
                typeof(NavAgentFaction),
                typeof(NavAgentAttack),
                typeof(NavAgentAttackProfile),
                typeof(NavAgentCombatTarget));

            em.SetComponentData(e, LocalTransform.FromPositionRotationScale(pos, quaternion.identity, 1f));
            em.SetComponentData(e, CreateSettingsFor(data, attackRangeMultiplier));
            float health = math.max(1f, stats != null ? stats.MaxHealth : 30f);
            em.SetComponentData(e, new NavAgentHealth { Max = health, Current = health });
            em.SetComponentData(e, new NavAgentFaction { Faction = faction });
            em.SetComponentData(e, BakeAttackProfile(stats != null ? stats.EnemyAttack : null, stats != null ? stats.AttackPower : 0f));
            em.AddBuffer<NavAgentWaypoint>(e);
            if (data != null)
                _agentData[e] = data;
        }

        private int SpawnFaction(EntityManager em, NavFaction faction, int count)
        {
            SO_Unit_Stats stats = unitData != null ? unitData.StatsData : null;
            float clearance = stats != null ? stats.AgentRadius : 0.35f;
            int spawned = 0;
            float rangeMin = math.min(attackRangeRandomMin, attackRangeRandomMax);
            float rangeMax = math.max(attackRangeRandomMin, attackRangeRandomMax);
            for (int i = 0; i < math.max(0, count); i++)
            {
                if (!TrySamplePointAwayFromPlayer(out Vector3 pos, clearance)) continue;
                SpawnSingleAgent(em, unitData, pos, UnityEngine.Random.Range(rangeMin, rangeMax), faction);
                spawned++;
            }
            return spawned;
        }

        private NavAgentSettings CreateSettings(float attackRangeMultiplier = 1f)
            => CreateSettingsFor(unitData, attackRangeMultiplier);

        private NavAgentSettings CreateSettingsFor(SO_Unit_Data data, float attackRangeMultiplier = 1f)
        {
            SO_Unit_Stats stats = data != null ? data.StatsData : null;
            float agentRadius    = stats != null ? stats.AgentRadius   : 0.35f;
            float stepHeight     = stats != null ? stats.StepHeight    : 0f;
            float stopDist       = stats != null ? stats.StopDistance  : 0.08f;
            float moveSpeed      = stats != null ? stats.MoveSpeed     : 3.5f;
            float wakeupRecovery = data != null && data.ActionRecovery != null ? data.ActionRecovery.WakeupDuration : 1f;
            float defense        = stats != null ? stats.Defense       : 0f;
            SO_Attack_Data attack = stats?.EnemyAttack;
            float attackDamage   = attack != null ? attack.Damage      : 5f;
            float attackRange    = attack != null ? attack.Hitbox.offset + AttackShapeUtility.GetPlanarReach(attack.Shape) : 1.4f;
            float attackWindup   = attack != null ? attack.Duration * attack.Hitbox.timing : 0.4f;
            float attackCooldown = attack != null ? attack.Duration * (1f - attack.Hitbox.timing) : 1.2f;

            return new NavAgentSettings
            {
                AgentRadius             = math.max(0f, agentRadius),
                StepHeight              = math.max(0f, stepHeight),
                StopDistance            = math.max(0f, stopDist),
                MoveSpeed               = math.max(0f, moveSpeed),
                WaypointAdvanceDistance = math.max(0f, waypointAdvanceDistance),
                CornerLookAheadDistance = math.max(0f, cornerLookAheadDistance),
                HeightOffset            = heightOffset,
                BoundaryTolerance       = math.max(0f, boundaryTolerance),
                TargetRepathDistance    = math.max(0f, targetRepathDistance),
                TargetRefreshDistance   = math.max(targetRepathDistance, movingTargetRepathDistance),
                TargetRefreshInterval   = math.max(0f, movingTargetRepathInterval),
                StuckRepathDelay        = math.max(0f, stuckRepathDelay),
                StuckRepathCooldown     = math.max(0f, stuckRepathCooldown),
                StuckProgressDistance   = math.max(0f, stuckProgressDistance),
                SeparationRadius        = math.max(0f, separationRadius),
                SeparationStrength      = math.max(0f, separationStrength),
                SeparationMaxNeighbors  = math.max(0, separationMaxNeighbors),
                StuckRetryLimit         = math.max(0, stuckRetryLimit),
                AttackDamage            = math.max(0f, attackDamage),
                AttackRange             = math.max(0f, attackRange * math.max(0f, attackRangeMultiplier)),
                AttackWindup            = math.max(0f, attackWindup),
                AttackCooldown          = math.max(0f, attackCooldown),
                WakeupRecoveryDuration  = math.max(0f, wakeupRecovery),
                Defense                 = math.max(0f, defense),
                EncircleRingFactor      = math.max(0f, encircleRingFactor),
                RetreatGain             = math.max(0f, retreatGain)
            };
        }

        private void RebuildCandidates()
        {
            _candidateRegions.Clear();
            IReadOnlyList<MapNavRegion> regions = map.Regions;
            for (int i = 0; i < regions.Count; i++)
            {
                MapNavRegion r = regions[i];
                if (r == null || r.Shapes == null || r.Shapes.Count == 0) continue;
                if (!r.HasBounds) r.RecalculateBounds();
                if (r.HasBounds) _candidateRegions.Add(r);
            }
        }

        // 진영별 스폰 시드를 설정한다(SectorManager가 진입 시 게이트별 인접 섹터 점령 진영으로 채운다).
        public void SetFactionSeeds(IReadOnlyList<Vector3> allySeeds, IReadOnlyList<Vector3> enemySeeds)
        {
            _allySeeds.Clear();
            _enemySeeds.Clear();
            if (allySeeds != null) _allySeeds.AddRange(allySeeds);
            if (enemySeeds != null) _enemySeeds.AddRange(enemySeeds);
        }

        // 진영별 스폰 위치: spawnRandomFraction 확률로 전체 랜덤(난전), 아니면 진영 시드(게이트) 중 하나 근처(전선).
        private bool TryFactionSpawnPoint(NavFaction faction, float clearance, out Vector3 pos)
        {
            List<Vector3> seeds = faction == NavFaction.Ally ? _allySeeds : _enemySeeds;
            if (seeds.Count > 0 && UnityEngine.Random.value >= spawnRandomFraction)
            {
                Vector3 anchor = seeds[UnityEngine.Random.Range(0, seeds.Count)];
                if (TrySampleNear(anchor, factionClusterRadius, clearance, out pos)) return true;
            }
            return TrySamplePointAwayFromPlayer(out pos, clearance);
        }

        // 앵커 반경 안에서 여러 번 샘플해 가장 가까운 점을 고른다. 반경 안을 못 찾으면 false(호출처가 랜덤 폴백).
        private bool TrySampleNear(Vector3 anchor, float radius, float clearance, out Vector3 pos)
        {
            pos = default;
            float bestSq = float.MaxValue;
            bool found = false;
            float radiusSq = radius * radius;
            for (int i = 0; i < 8; i++)
            {
                if (!TrySamplePointAwayFromPlayer(out Vector3 p, clearance)) continue;
                float d = (p - anchor).sqrMagnitude;
                if (d <= radiusSq && d < bestSq) { bestSq = d; pos = p; found = true; }
            }
            return found;
        }

        private bool TrySamplePointAwayFromPlayer(out Vector3 worldPosition, float clearanceRadius = 0.35f)
        {
            for (int i = 0; i < 3; i++)
            {
                if (!TrySamplePoint(out Vector3 candidate, clearanceRadius))
                    continue;

                if (IsPlayerSafeSpawnPoint(candidate))
                {
                    worldPosition = candidate;
                    return true;
                }
            }

            worldPosition = default;
            return false;
        }

        private bool IsPlayerSafeSpawnPoint(Vector3 position)
        {
            Transform player = ResolvePlayerTransform();
            if (player == null)
                return true;

            Vector3 toSpawn = position - player.position;
            toSpawn.y = 0f;
            float distanceSq = toSpawn.sqrMagnitude;

            float minDistance = math.max(0f, playerSpawnExclusionRadius);
            if (distanceSq < minDistance * minDistance)
                return false;

            float frontDistance = math.max(0f, playerForwardSpawnExclusionDistance);
            float frontAngle = math.clamp(playerForwardSpawnExclusionAngle, 0f, 180f);
            if (frontDistance <= 0f || frontAngle <= 0f || distanceSq > frontDistance * frontDistance)
                return true;

            Vector3 forward = player.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f || toSpawn.sqrMagnitude <= 0.0001f)
                return true;

            forward.Normalize();
            toSpawn.Normalize();
            float minDot = Mathf.Cos(frontAngle * 0.5f * Mathf.Deg2Rad);
            return Vector3.Dot(forward, toSpawn) < minDot;
        }

        private Transform ResolvePlayerTransform()
        {
            if (_playerTransform != null)
                return _playerTransform;

            Character_PlayerControl player = FindAnyObjectByType<Character_PlayerControl>();
            _playerTransform = player != null ? player.transform : null;
            return _playerTransform;
        }

        private bool TrySamplePoint(out Vector3 worldPosition, float clearanceRadius = 0.35f)
        {
            float clearance = math.max(0f, clearanceRadius);
            int attemptsPerRegion = math.max(1, maxSampleAttempts / math.max(1, _candidateRegions.Count));
            for (int outer = 0; outer < maxSampleAttempts; outer++)
            {
                MapNavRegion region = _candidateRegions[UnityEngine.Random.Range(0, _candidateRegions.Count)];
                if (MapNavSampleUtility.TrySampleClearPoint(region, clearance, attemptsPerRegion, out Vector2 local))
                {
                    worldPosition = map.ToWorld(region, local);
                    return true;
                }
            }
            worldPosition = default;
            return false;
        }

        private float AgentMaxHealth => unitData != null && unitData.StatsData != null ? unitData.StatsData.MaxHealth : 30f;
        private float AttackPower    => unitData != null && unitData.StatsData != null ? unitData.StatsData.AttackPower : 0f;
        private SO_Attack_Data EnemyAttack => unitData != null ? unitData.StatsData?.EnemyAttack : null;

        private static NavAgentAttackProfile BakeAttackProfile(SO_Attack_Data attack, float attackerAttackPower)
        {
            if (attack == null) return default;
            FixedString64Bytes vfx = default;
            string vfxAddress = attack.HitVfxAddress;
            if (!string.IsNullOrEmpty(vfxAddress))
            {
                // FixedString64Bytes 용량 초과 문자열은 잘려나감. 어드레서블 키가 60자 이상이면 더 큰 FixedString 검토 필요.
                vfx = new FixedString64Bytes();
                vfx.Append(vfxAddress);
            }

            FixedString64Bytes attackStateName = default;
            string animStateName = attack.Animation.stateName;
            if (!string.IsNullOrEmpty(animStateName))
            {
                attackStateName = new FixedString64Bytes();
                attackStateName.Append(animStateName);
            }

            return new NavAgentAttackProfile
            {
                Damage = CombatFormula.ScaleAttackDamage(attackerAttackPower, attack.Damage),
                KnockbackType = attack.Knockback.type,
                KnockbackForce = attack.Knockback.force,
                KnockbackFriction = attack.Knockback.friction,
                HitstopDuration = attack.Hitstop.duration,
                HitstopTimeScale = attack.Hitstop.timeScale,
                IsDownAttack = (byte)(attack.Down.enabled ? 1 : 0),
                DownDuration = attack.Down.duration,
                LaunchEnabled = (byte)(attack.Launch.enabled ? 1 : 0),
                LaunchHeight = attack.Launch.height,
                LaunchSuspendDuration = attack.Launch.suspendDuration,
                Shape = attack.Shape.type,
                HitboxOffset = attack.Hitbox.offset,
                HitboxYOffset = attack.Hitbox.yOffset,
                HitboxVerticalTolerance = attack.Hitbox.verticalTolerance,
                ShapeRadius = attack.Shape.radius,
                ShapeAngle = attack.Shape.angle,
                ShapeLength = attack.Shape.length,
                ShapeWidth = attack.Shape.width,
                SuperArmor = attack.SuperArmor,
                SuperArmorBreak = attack.SuperArmorBreak,
                HitType = attack.HitType,
                HitSfx = attack.HitSfx,
                HitVfxAddress = vfx,
                AttackStateName = attackStateName,
                AttackTransition = attack.Animation.transition
            };
        }

        private void SetStatus(string s)
        {
            Debug.Log($"[{nameof(NavRuntimeBootstrap)}] {s}", this);
        }
    }
}
