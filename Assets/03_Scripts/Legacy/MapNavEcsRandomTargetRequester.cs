using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MapNavEcsRandomTargetRequester : MonoBehaviour
{
    [SerializeField] private MapNavigationAuthoring map;
    [SerializeField] private bool requestOnEnable = true;
    [SerializeField] private bool repeat = true;
    [SerializeField] private float interval = 2f;
    [SerializeField] private int maxAgentsPerTick = 128;
    [SerializeField] private int maxSampleAttempts = 32;
    [SerializeField] private float minTargetChangeDistance = 0.25f;
    [SerializeField] private int onlyNavLayerId = -1;
    [SerializeField] private Transform sampleCenter;
    [SerializeField] private float sampleRadius = 0f;
    [SerializeField] private bool limitSampleHeight = false;
    [SerializeField] private float minSampleHeight = -0.5f;
    [SerializeField] private float maxSampleHeight = 1.5f;
    [SerializeField] private bool force = false;
    [SerializeField] private bool logRequests = false;
    [SerializeField] private string lastStatus = "";
    [SerializeField] private int lastCandidateRegionCount;
    [SerializeField] private int lastQueriedAgentCount;
    [SerializeField] private int lastWrittenTargetCount;

    private readonly List<MapNavRegion> _candidateRegions = new List<MapNavRegion>();
    private readonly List<Entity> _entitiesScratch = new List<Entity>();
    private EntityQuery _agentQuery;
    private EntityManager _entityManager;
    private World _world;
    private bool _hasAgentQuery;
    private float _nextRequestTime;

    private void OnEnable()
    {
        TryInitializeQuery();
        _nextRequestTime = requestOnEnable ? 0f : Time.time + Mathf.Max(0f, interval);
    }

    private void OnDisable()
    {
        if (_hasAgentQuery)
        {
            _agentQuery.Dispose();
            _hasAgentQuery = false;
        }
    }

    private void Update()
    {
        if (!repeat && !requestOnEnable)
            return;

        if (Time.time < _nextRequestTime)
            return;

        RequestRandomTargets();
        _nextRequestTime = Time.time + Mathf.Max(0.02f, interval);

        if (!repeat)
            requestOnEnable = false;
    }

    [ContextMenu("Request Random Targets")]
    public void RequestRandomTargets()
    {
        lastWrittenTargetCount = 0;
        if (map == null)
        {
            SetStatus("No MapNavigationAuthoring assigned.");
            return;
        }

        if (!TryInitializeQuery())
        {
            SetStatus("No default ECS world.");
            return;
        }

        RebuildCandidateRegions();
        lastCandidateRegionCount = _candidateRegions.Count;
        if (_candidateRegions.Count == 0)
        {
            SetStatus("No candidate regions.");
            return;
        }

        _entitiesScratch.Clear();
        using NativeArray<Entity> entities = _agentQuery.ToEntityArray(Allocator.Temp);
        lastQueriedAgentCount = entities.Length;
        int count = math.min(entities.Length, math.max(0, maxAgentsPerTick));
        for (int i = 0; i < count; i++)
            _entitiesScratch.Add(entities[i]);

        if (_entitiesScratch.Count == 0)
        {
            SetStatus("No ECS agents matched MapNavEcsAgent + MapNavEcsTarget + LocalTransform.");
            return;
        }

        for (int i = 0; i < _entitiesScratch.Count; i++)
        {
            Entity entity = _entitiesScratch[i];
            if (!TrySampleRegionPoint(out Vector3 targetPosition))
                continue;

            if (!ShouldWriteTarget(entity, targetPosition))
                continue;

            MapNavigationEcsTargetUtility.SetTarget(_entityManager, entity, targetPosition, force);
            lastWrittenTargetCount++;
        }

        SetStatus($"Agents={lastQueriedAgentCount}, Regions={lastCandidateRegionCount}, WrittenTargets={lastWrittenTargetCount}");
    }

    private bool TryInitializeQuery()
    {
        World defaultWorld = World.DefaultGameObjectInjectionWorld;
        if (defaultWorld == null || !defaultWorld.IsCreated)
            return false;

        if (_world == defaultWorld && _hasAgentQuery)
            return true;

        if (_hasAgentQuery)
        {
            _agentQuery.Dispose();
            _hasAgentQuery = false;
        }

        _world = defaultWorld;
        _entityManager = _world.EntityManager;
        _agentQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<MapNavEcsAgent>(),
            ComponentType.ReadOnly<MapNavEcsTarget>(),
            ComponentType.ReadOnly<LocalTransform>());
        _hasAgentQuery = true;
        return true;
    }

    private void RebuildCandidateRegions()
    {
        _candidateRegions.Clear();
        IReadOnlyList<MapNavRegion> regions = map.Regions;
        for (int i = 0; i < regions.Count; i++)
        {
            MapNavRegion region = regions[i];
            if (region == null || region.Points == null || region.Points.Count < 3)
                continue;

            if (onlyNavLayerId >= 0 && region.NavLayerId != onlyNavLayerId)
                continue;

            if (!region.HasBounds)
                region.RecalculateBounds();

            if (!region.HasBounds)
                continue;

            _candidateRegions.Add(region);
        }
    }

    private bool TrySampleRegionPoint(out Vector3 worldPosition)
    {
        for (int i = 0; i < maxSampleAttempts; i++)
        {
            MapNavRegion region = _candidateRegions[UnityEngine.Random.Range(0, _candidateRegions.Count)];
            Vector2 localPoint = new Vector2(
                UnityEngine.Random.Range(region.BoundsMin.x, region.BoundsMax.x),
                UnityEngine.Random.Range(region.BoundsMin.y, region.BoundsMax.y));

            if (!region.Contains(localPoint) || IsInsideObstacle(region, localPoint))
                continue;

            worldPosition = map.ToWorld(region, localPoint);
            if (!IsWithinSampleRadius(worldPosition))
                continue;

            if (!IsWithinSampleHeight(worldPosition))
                continue;

            return true;
        }

        worldPosition = default;
        return false;
    }

    private bool ShouldWriteTarget(Entity entity, Vector3 targetPosition)
    {
        if (!_entityManager.Exists(entity) || !_entityManager.HasComponent<MapNavEcsTarget>(entity))
            return false;

        MapNavEcsTarget target = _entityManager.GetComponentData<MapNavEcsTarget>(entity);
        float3 previous = target.Dirty != 0 ? target.Position : target.AcceptedPosition;
        float minDistance = math.max(0f, minTargetChangeDistance);
        return math.lengthsq(previous - (float3)targetPosition) >= minDistance * minDistance;
    }

    private bool IsWithinSampleRadius(Vector3 worldPosition)
    {
        if (sampleRadius <= 0f)
            return true;

        Vector3 center = sampleCenter != null ? sampleCenter.position : transform.position;
        Vector3 delta = worldPosition - center;
        delta.y = 0f;
        return delta.sqrMagnitude <= sampleRadius * sampleRadius;
    }

    private bool IsWithinSampleHeight(Vector3 worldPosition)
    {
        if (!limitSampleHeight)
            return true;

        return worldPosition.y >= minSampleHeight && worldPosition.y <= maxSampleHeight;
    }

    private static bool IsInsideObstacle(MapNavRegion region, Vector2 localPoint)
    {
        if (region.Obstacles == null)
            return false;

        for (int i = 0; i < region.Obstacles.Count; i++)
        {
            MapNavObstacle obstacle = region.Obstacles[i];
            if (obstacle != null && obstacle.Contains(localPoint))
                return true;
        }

        return false;
    }

    private void SetStatus(string status)
    {
        lastStatus = status;
        if (logRequests)
            Debug.Log($"[{nameof(MapNavEcsRandomTargetRequester)}] {status}", this);
    }
}
