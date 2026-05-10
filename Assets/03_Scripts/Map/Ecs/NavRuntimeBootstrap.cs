using System.Collections.Generic;
using MapNav.Baking;
using MapNav.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace MapNav.Ecs
{
    [DisallowMultipleComponent]
    public sealed class NavRuntimeBootstrap : MonoBehaviour
    {
        [SerializeField] private MapNavigationAuthoring map;
        [SerializeField] private bool spawnAgents = true;
        [SerializeField] private int agentCount = 25;
        [SerializeField] private int onlyNavLayerId = -1;
        [SerializeField] private int maxSampleAttempts = 64;

        [Header("Agent settings")]
        [SerializeField] private float agentRadius = 0.35f;
        [SerializeField] private float stopDistance = 0.08f;
        [SerializeField] private float moveSpeed = 3.5f;
        [SerializeField] private float waypointAdvanceDistance = 0.35f;
        [SerializeField] private float cornerLookAheadDistance;
        [SerializeField] private float heightOffset;
        [SerializeField] private float boundaryTolerance = 0.05f;
        [SerializeField] private float targetRepathDistance = 0.15f;
        [SerializeField] private float stuckRepathDelay = 0.75f;
        [SerializeField] private float stuckRepathCooldown = 1.5f;
        [SerializeField] private float stuckProgressDistance = 0.03f;
        [SerializeField] private int stuckRetryLimit = 4;

        [Header("Path build")]
        [SerializeField] private int maxPathsPerFrame = 32;

        private readonly List<MapNavRegion> _candidateRegions = new();
        private BlobAssetReference<NavBlob> _ownedBlob;
        private Entity _singleton = Entity.Null;
        private World _ownedWorld;

        private void Start() => Bootstrap();

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

            Debug.Log(
                $"[NavBlob stats] Regions={b.Regions.Length} Transitions={b.Transitions.Length} Obstacles={b.Obstacles.Length} " +
                $"RegionEdges={totalRegionEdges} TransitionEdges={totalTransEdges} IsolatedRegions={isolatedRegions}", this);

            for (int i = 0; i < b.Regions.Length; i++)
            {
                NavRegion r = b.Regions[i];
                int outEdges = b.RegionEdgeRange[i].y;
                Debug.Log($"  Region id={r.Id} layer={r.LayerId} h={r.Height:F2} obstacles={r.ObstacleCount} outEdges={outEdges}", this);
            }
            for (int i = 0; i < b.Transitions.Length; i++)
            {
                NavTransition t = b.Transitions[i];
                int outEdges = b.TransitionEdgeRange[i].y;
                Debug.Log($"  Transition id={t.Id} from={t.FromRegionId} to={t.ToRegionId} type={t.Type} bidir={t.Bidirectional} enabled={t.Enabled} outEdges={outEdges}", this);
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
            for (int i = 0; i < math.max(0, agentCount); i++)
            {
                if (!TrySamplePoint(out Vector3 pos)) continue;

                Entity e = em.CreateEntity(
                    typeof(LocalTransform),
                    typeof(NavAgentSettings),
                    typeof(NavAgentMotion),
                    typeof(NavAgentTarget),
                    typeof(NavAgentPathRequest),
                    typeof(NavAgentPathStatus));

                em.SetComponentData(e, LocalTransform.FromPositionRotationScale(pos, quaternion.identity, 1f));
                em.SetComponentData(e, CreateSettings());
                em.AddBuffer<NavAgentWaypoint>(e);
                spawned++;
            }
            return spawned;
        }

        private NavAgentSettings CreateSettings()
        {
            return new NavAgentSettings
            {
                AgentRadius = math.max(0f, agentRadius),
                StopDistance = math.max(0f, stopDistance),
                MoveSpeed = math.max(0f, moveSpeed),
                WaypointAdvanceDistance = math.max(0f, waypointAdvanceDistance),
                CornerLookAheadDistance = math.max(0f, cornerLookAheadDistance),
                HeightOffset = heightOffset,
                BoundaryTolerance = math.max(0f, boundaryTolerance),
                TargetRepathDistance = math.max(0f, targetRepathDistance),
                StuckRepathDelay = math.max(0f, stuckRepathDelay),
                StuckRepathCooldown = math.max(0f, stuckRepathCooldown),
                StuckProgressDistance = math.max(0f, stuckProgressDistance),
                StuckRetryLimit = math.max(0, stuckRetryLimit)
            };
        }

        private void RebuildCandidates()
        {
            _candidateRegions.Clear();
            IReadOnlyList<MapNavRegion> regions = map.Regions;
            for (int i = 0; i < regions.Count; i++)
            {
                MapNavRegion r = regions[i];
                if (r == null || r.Points == null || r.Points.Count < 3) continue;
                if (onlyNavLayerId >= 0 && r.NavLayerId != onlyNavLayerId) continue;
                if (!r.HasBounds) r.RecalculateBounds();
                if (r.HasBounds) _candidateRegions.Add(r);
            }
        }

        private bool TrySamplePoint(out Vector3 worldPosition)
        {
            for (int i = 0; i < maxSampleAttempts; i++)
            {
                MapNavRegion region = _candidateRegions[UnityEngine.Random.Range(0, _candidateRegions.Count)];
                Vector2 local = new Vector2(
                    UnityEngine.Random.Range(region.BoundsMin.x, region.BoundsMax.x),
                    UnityEngine.Random.Range(region.BoundsMin.y, region.BoundsMax.y));
                if (!region.Contains(local) || IsInsideObstacle(region, local)) continue;
                worldPosition = map.ToWorld(region, local);
                return true;
            }
            worldPosition = default;
            return false;
        }

        private static bool IsInsideObstacle(MapNavRegion region, Vector2 localPoint)
        {
            if (region.Obstacles == null) return false;
            for (int i = 0; i < region.Obstacles.Count; i++)
            {
                MapNavObstacle o = region.Obstacles[i];
                if (o != null && o.Contains(localPoint)) return true;
            }
            return false;
        }

        private void SetStatus(string s)
        {
            Debug.Log($"[{nameof(NavRuntimeBootstrap)}] {s}", this);
        }
    }
}
