using System.Collections.Generic;
using MapNav.Core;
using MapNav.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace MapNav.Ecs
{
    [DisallowMultipleComponent]
    public sealed class NavRandomTargetRequester : MonoBehaviour
    {
        [SerializeField] private MapNavigationAuthoring map;
        [SerializeField] private bool requestOnEnable = true;
        [SerializeField] private bool repeat = true;
        [SerializeField] private float interval = 2f;
        [SerializeField] private bool requestIdleAgentsContinuously;
        [SerializeField] private int maxSampleAttempts = 32;
        [SerializeField] private int maxTargetWritesPerFrame = 32;
        [SerializeField] private float minTargetChangeDistance = 0.25f;
        [SerializeField] private float sampleClearance = 0.35f;
        [SerializeField] private bool validateReachabilityBeforeRequest;
        [SerializeField] private int maxReachabilityChecksPerAgent = 4;

        private readonly List<MapNavRegion> _candidateRegions = new();
        private EntityQuery _agentQuery;
        private EntityManager _em;
        private World _world;
        private bool _hasAgentQuery;
        private float _nextTime;
        private bool _dispatching;
        private int _dispatchCursor;

        private void OnEnable()
        {
            TryInitQuery();
            _nextTime = requestOnEnable ? 0f : Time.time + math.max(0f, interval);
            _dispatching = false;
            _dispatchCursor = 0;
        }

        private void OnDisable()
        {
            if (_hasAgentQuery) { _agentQuery.Dispose(); _hasAgentQuery = false; }
        }

        private void Update()
        {
            if (_dispatching)
            {
                if (DispatchRandomTargets(math.max(1, maxTargetWritesPerFrame)))
                    FinishDispatch();
                return;
            }

            if (requestIdleAgentsContinuously)
            {
                BeginDispatch();
                if (DispatchRandomTargets(math.max(1, maxTargetWritesPerFrame), idleOnly: true))
                    FinishDispatch(scheduleNextRepeat: false);
                return;
            }

            if (!repeat && !requestOnEnable) return;
            if (Time.time < _nextTime) return;
            BeginDispatch();
            if (DispatchRandomTargets(math.max(1, maxTargetWritesPerFrame)))
                FinishDispatch();
        }

        [ContextMenu("Request Random Targets")]
        public void RequestRandomTargets()
        {
            BeginDispatch();
            DispatchRandomTargets(int.MaxValue);
            _dispatching = false;
            _dispatchCursor = 0;
        }

        private void BeginDispatch()
        {
            _dispatching = true;
            _dispatchCursor = 0;
        }

        private void FinishDispatch(bool scheduleNextRepeat = true)
        {
            _dispatching = false;
            _dispatchCursor = 0;
            if (scheduleNextRepeat)
            {
                _nextTime = Time.time + math.max(0.02f, interval);
                if (!repeat) requestOnEnable = false;
            }
        }

        private bool DispatchRandomTargets(int maxWrites, bool idleOnly = false)
        {
            if (map == null) { SetStatus("No MapNavigationAuthoring."); return true; }
            if (!TryInitQuery()) { SetStatus("No default ECS world."); return true; }

            RebuildCandidates();
            if (_candidateRegions.Count == 0) { SetStatus("No candidate regions."); return true; }

            using NativeArray<Entity> entities = _agentQuery.ToEntityArray(Allocator.Temp);
            if (_dispatchCursor >= entities.Length)
                return true;

            int written = 0;
            if (validateReachabilityBeforeRequest)
            {
                EntityQuery navRefQuery = _em.CreateEntityQuery(ComponentType.ReadOnly<NavBlobReference>());
                if (navRefQuery.IsEmptyIgnoreFilter) { SetStatus("No NavBlobReference singleton."); navRefQuery.Dispose(); return true; }
                NavBlobReference navRef = navRefQuery.GetSingleton<NavBlobReference>();
                navRefQuery.Dispose();
                if (!navRef.Blob.IsCreated) { SetStatus("NavBlob not created."); return true; }

                NavContext ctx = new NavContext(navRef.Blob, navRef.LocalToWorld, navRef.WorldToLocal);
                NavScratch scratch = new NavScratch(64, Allocator.Temp);
                NativeList<NavSpaceRef> nodes = new NativeList<NavSpaceRef>(16, Allocator.Temp);
                NativeList<NavPortal> portals = new NativeList<NavPortal>(16, Allocator.Temp);
                NativeList<float3> waypoints = new NativeList<float3>(16, Allocator.Temp);

                try
                {
                    for (int i = _dispatchCursor; i < entities.Length && written < maxWrites; i++)
                    {
                        Entity e = entities[i];
                        _dispatchCursor = i + 1;
                        if (idleOnly && !IsIdleForNewTarget(e))
                            continue;

                        if (!TrySampleReachablePoint(
                                e,
                                in ctx,
                                ref scratch,
                                ref nodes,
                                ref portals,
                                ref waypoints,
                                out Vector3 pos))
                        {
                            continue;
                        }

                        SetTarget(e, pos);
                        written++;
                    }
                }
                finally
                {
                    waypoints.Dispose();
                    portals.Dispose();
                    nodes.Dispose();
                    scratch.Dispose();
                }
            }
            else
            {
                for (int i = _dispatchCursor; i < entities.Length && written < maxWrites; i++)
                {
                    Entity e = entities[i];
                    _dispatchCursor = i + 1;
                    if (idleOnly && !IsIdleForNewTarget(e))
                        continue;

                    if (!TrySamplePoint(e, out Vector3 pos)) continue;

                    SetTarget(e, pos);
                    written++;
                }
            }

            //SetStatus($"Agents={entities.Length}, Written={written}");
            return _dispatchCursor >= entities.Length;
        }

        private bool IsIdleForNewTarget(Entity entity)
        {
            if (!_em.Exists(entity)
                || !_em.HasComponent<NavAgentTarget>(entity)
                || !_em.HasComponent<NavAgentPathRequest>(entity)
                || !_em.HasComponent<NavAgentPathStatus>(entity))
            {
                return false;
            }

            NavAgentTarget target = _em.GetComponentData<NavAgentTarget>(entity);
            NavAgentPathRequest request = _em.GetComponentData<NavAgentPathRequest>(entity);
            NavAgentPathStatus status = _em.GetComponentData<NavAgentPathStatus>(entity);
            return target.Dirty == 0
                && request.Pending == 0
                && status.HasPath == 0
                && status.Waiting == 0;
        }

        private bool ShouldWrite(Entity entity, Vector3 target)
        {
            if (!_em.Exists(entity) || !_em.HasComponent<NavAgentTarget>(entity)) return false;
            NavAgentTarget t = _em.GetComponentData<NavAgentTarget>(entity);
            float3 prev = t.Dirty != 0 ? t.Position : t.AcceptedPosition;
            float minDist = math.max(0f, minTargetChangeDistance);
            return math.lengthsq(prev - (float3)(Vector3)target) >= minDist * minDist;
        }

        private void SetTarget(Entity entity, Vector3 position)
        {
            if (_em.HasComponent<NavAgentTargetCommand>(entity))
                _em.SetComponentData(entity, new NavAgentTargetCommand { Position = position });
            else
                _em.AddComponentData(entity, new NavAgentTargetCommand { Position = position });
        }

        private bool TryInitQuery()
        {
            World w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) return false;
            if (_world == w && _hasAgentQuery) return true;
            if (_hasAgentQuery) { _agentQuery.Dispose(); _hasAgentQuery = false; }
            _world = w;
            _em = w.EntityManager;
            _agentQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<NavAgentSettings>(),
                ComponentType.ReadOnly<NavAgentTarget>(),
                ComponentType.ReadOnly<LocalTransform>());
            _hasAgentQuery = true;
            return true;
        }

        private bool TrySampleReachablePoint(
            Entity entity,
            in NavContext ctx,
            ref NavScratch scratch,
            ref NativeList<NavSpaceRef> nodes,
            ref NativeList<NavPortal> portals,
            ref NativeList<float3> waypoints,
            out Vector3 worldPosition)
        {
            worldPosition = default;
            if (!_em.Exists(entity)
                || !_em.HasComponent<NavAgentSettings>(entity)
                || !_em.HasComponent<LocalTransform>(entity))
            {
                return false;
            }

            NavAgentSettings settings = _em.GetComponentData<NavAgentSettings>(entity);
            LocalTransform transform = _em.GetComponentData<LocalTransform>(entity);
            float clearance = math.max(math.max(0f, sampleClearance), settings.AgentRadius);
            int attemptsPerRegion = math.max(1, maxSampleAttempts / math.max(1, _candidateRegions.Count));
            int validationAttempts = math.max(1, math.min(maxSampleAttempts, maxReachabilityChecksPerAgent));

            for (int outer = 0; outer < validationAttempts; outer++)
            {
                MapNavRegion region = _candidateRegions[UnityEngine.Random.Range(0, _candidateRegions.Count)];
                if (!MapNavSampleUtility.TrySampleClearPoint(region, clearance, attemptsPerRegion, out Vector2 local))
                    continue;

                Vector3 candidate = map.ToWorld(region, local);
                if (!ShouldWrite(entity, candidate))
                    continue;

                bool built = NavPath.TryBuild(
                    in ctx,
                    transform.Position,
                    candidate,
                    settings.AgentRadius,
                    settings.BoundaryTolerance,
                    ref scratch,
                    ref nodes,
                    ref portals,
                    ref waypoints);

                if (!built)
                    continue;

                worldPosition = candidate;
                return true;
            }

            return false;
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

        private bool TrySamplePoint(Entity entity, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (!_em.Exists(entity) || !_em.HasComponent<NavAgentSettings>(entity))
                return false;

            NavAgentSettings settings = _em.GetComponentData<NavAgentSettings>(entity);
            float clearance = math.max(math.max(0f, sampleClearance), settings.AgentRadius);
            int attemptsPerRegion = math.max(1, maxSampleAttempts / math.max(1, _candidateRegions.Count));
            for (int outer = 0; outer < maxSampleAttempts; outer++)
            {
                MapNavRegion region = _candidateRegions[UnityEngine.Random.Range(0, _candidateRegions.Count)];
                if (MapNavSampleUtility.TrySampleClearPoint(region, clearance, attemptsPerRegion, out Vector2 local))
                {
                    Vector3 candidate = map.ToWorld(region, local);
                    if (!ShouldWrite(entity, candidate))
                        continue;

                    worldPosition = candidate;
                    return true;
                }
            }
            return false;
        }

        private void SetStatus(string s)
        {
            Debug.Log($"[{nameof(NavRandomTargetRequester)}] {s}", this);
        }
    }
}
