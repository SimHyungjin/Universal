using System.Collections.Generic;
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
        [SerializeField] private int maxSampleAttempts = 32;
        [SerializeField] private float minTargetChangeDistance = 0.25f;
        [SerializeField] private int onlyNavLayerId = -1;

        private readonly List<MapNavRegion> _candidateRegions = new();
        private EntityQuery _agentQuery;
        private EntityManager _em;
        private World _world;
        private bool _hasAgentQuery;
        private float _nextTime;

        private void OnEnable()
        {
            TryInitQuery();
            _nextTime = requestOnEnable ? 0f : Time.time + math.max(0f, interval);
        }

        private void OnDisable()
        {
            if (_hasAgentQuery) { _agentQuery.Dispose(); _hasAgentQuery = false; }
        }

        private void Update()
        {
            if (!repeat && !requestOnEnable) return;
            if (Time.time < _nextTime) return;
            RequestRandomTargets();
            _nextTime = Time.time + math.max(0.02f, interval);
            if (!repeat) requestOnEnable = false;
        }

        [ContextMenu("Request Random Targets")]
        public void RequestRandomTargets()
        {
            if (map == null) { SetStatus("No MapNavigationAuthoring."); return; }
            if (!TryInitQuery()) { SetStatus("No default ECS world."); return; }

            RebuildCandidates();
            if (_candidateRegions.Count == 0) { SetStatus("No candidate regions."); return; }

            using NativeArray<Entity> entities = _agentQuery.ToEntityArray(Allocator.Temp);

            int written = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];
                if (!TrySamplePoint(out Vector3 pos)) continue;
                if (!ShouldWrite(e, pos)) continue;
                SetTarget(e, pos);
                written++;
            }

            SetStatus($"Agents={entities.Length}, Written={written}");
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
            Debug.Log($"[{nameof(NavRandomTargetRequester)}] {s}", this);
        }
    }
}
