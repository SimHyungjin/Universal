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
        [SerializeField] private SO_UnitStats unitStats;
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

        [Header("Path build")]
        [SerializeField] private int maxPathsPerFrame = 32;

        private readonly List<MapNavRegion> _candidateRegions = new();
        private BlobAssetReference<NavBlob> _ownedBlob;
        private Entity _singleton = Entity.Null;
        private World _ownedWorld;

        private void Start() => Bootstrap();

        private void LateUpdate() => RefreshSingletonTransform();

        private void OnDestroy()
        {
            DestroySingleton();
            if (_ownedBlob.IsCreated) _ownedBlob.Dispose();
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

            // Bake our own blob copy so we control its lifetime independently of MapNavigationAuthoring.
            // (Authoring would otherwise dispose the BlobAsset via its own OnDestroy and leave ECS dangling.)
            if (_ownedBlob.IsCreated) _ownedBlob.Dispose();
            _ownedBlob = MapNavBaker.Build(map, Allocator.Persistent);

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

        private int SpawnFaction(EntityManager em, NavFaction faction, int count)
        {
            int spawned = 0;
            for (int i = 0; i < math.max(0, count); i++)
            {
                if (!TrySamplePoint(out Vector3 pos)) continue;

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
                em.SetComponentData(e, CreateSettings(UnityEngine.Random.Range(
                    math.min(attackRangeRandomMin, attackRangeRandomMax),
                    math.max(attackRangeRandomMin, attackRangeRandomMax))));
                float health = math.max(1f, AgentMaxHealth);
                em.SetComponentData(e, new NavAgentHealth { Max = health, Current = health });
                em.SetComponentData(e, new NavAgentFaction { Faction = faction });
                em.SetComponentData(e, BakeAttackProfile(EnemyAttack, AttackPower));
                em.AddBuffer<NavAgentWaypoint>(e);
                spawned++;
            }
            return spawned;
        }

        private NavAgentSettings CreateSettings(float attackRangeMultiplier = 1f)
        {
            return new NavAgentSettings
            {
                AgentRadius = math.max(0f, AgentRadius),
                StopDistance = math.max(0f, StopDistance),
                MoveSpeed = math.max(0f, MoveSpeed),
                WaypointAdvanceDistance = math.max(0f, waypointAdvanceDistance),
                CornerLookAheadDistance = math.max(0f, cornerLookAheadDistance),
                HeightOffset = heightOffset,
                BoundaryTolerance = math.max(0f, boundaryTolerance),
                TargetRepathDistance = math.max(0f, targetRepathDistance),
                TargetRefreshDistance = math.max(targetRepathDistance, movingTargetRepathDistance),
                TargetRefreshInterval = math.max(0f, movingTargetRepathInterval),
                StuckRepathDelay = math.max(0f, stuckRepathDelay),
                StuckRepathCooldown = math.max(0f, stuckRepathCooldown),
                StuckProgressDistance = math.max(0f, stuckProgressDistance),
                SeparationRadius = math.max(0f, separationRadius),
                SeparationStrength = math.max(0f, separationStrength),
                SeparationMaxNeighbors = math.max(0, separationMaxNeighbors),
                StuckRetryLimit = math.max(0, stuckRetryLimit),
                AttackDamage = math.max(0f, AttackDamage),
                AttackRange = math.max(0f, AttackRange * math.max(0f, attackRangeMultiplier)),
                AttackWindup = math.max(0f, AttackWindup),
                AttackCooldown = math.max(0f, AttackCooldown),
                Defense = math.max(0f, Defense)
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

        private bool TrySamplePoint(out Vector3 worldPosition)
        {
            float clearance = math.max(0f, AgentRadius);
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

        private float AgentRadius => unitStats != null ? unitStats.AgentRadius : 0.35f;
        private float StopDistance => unitStats != null ? unitStats.StopDistance : 0.08f;
        private float MoveSpeed => unitStats != null ? unitStats.MoveSpeed : 3.5f;
        private float AgentMaxHealth => unitStats != null ? unitStats.MaxHealth : 30f;
        private float AttackPower => unitStats != null ? unitStats.AttackPower : 0f;
        private float Defense => unitStats != null ? unitStats.Defense : 0f;
        private SO_AttackData EnemyAttack => unitStats != null ? unitStats.EnemyAttack : null;
        private float AttackDamage => EnemyAttack != null ? EnemyAttack.Damage : 5f;
        private float AttackRange => EnemyAttack != null ? EnemyAttack.Hitbox.offset + AttackShapeUtility.GetPlanarReach(EnemyAttack.Shape) : 1.4f;
        private float AttackWindup => EnemyAttack != null ? EnemyAttack.Duration * EnemyAttack.Hitbox.timing : 0.4f;
        private float AttackCooldown => EnemyAttack != null ? EnemyAttack.Duration * (1f - EnemyAttack.Hitbox.timing) : 1.2f;

        private static NavAgentAttackProfile BakeAttackProfile(SO_AttackData attack, float attackerAttackPower)
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
                KnockbackDuration = attack.Knockback.duration,
                KnockbackFriction = attack.Knockback.friction,
                HitstopDuration = attack.Hitstop.duration,
                HitstopTimeScale = attack.Hitstop.timeScale,
                IsDownAttack = (byte)(attack.Down.enabled ? 1 : 0),
                DownDuration = attack.Down.duration,
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
