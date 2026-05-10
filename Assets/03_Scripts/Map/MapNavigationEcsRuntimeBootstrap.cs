using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MapNavigationEcsRuntimeBootstrap : MonoBehaviour
{
    [SerializeField] private MapNavigationAuthoring map;
    [SerializeField] private bool createNavigationSingleton = true;
    [SerializeField] private bool spawnAgents = true;
    [SerializeField] private int agentCount = 25;
    [SerializeField] private int onlyNavLayerId = -1;
    [SerializeField] private int maxSampleAttempts = 64;
    [SerializeField] private Transform sampleCenter;
    [SerializeField] private float sampleRadius = 0f;
    [SerializeField] private bool limitSampleHeight = false;
    [SerializeField] private float minSampleHeight = -0.5f;
    [SerializeField] private float maxSampleHeight = 1.5f;
    [SerializeField] private float agentRadius = 0.35f;
    [SerializeField] private float stopDistance = 0.08f;
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float waypointAdvanceDistance = 0.35f;
    [SerializeField] private float cornerLookAheadDistance;
    [SerializeField] private float heightOffset;
    [SerializeField] private float targetRepathDistance = 0.15f;
    [SerializeField] private float stuckRepathDelay = 0.75f;
    [SerializeField] private float stuckRepathCooldown = 1.5f;
    [SerializeField] private float stuckProgressDistance = 0.03f;
    [SerializeField] private bool useRegionPathfinding = true;
    [SerializeField] private bool constrainToNavigationSpaces = true;
    [SerializeField] private string lastStatus = "";

    private readonly List<MapNavRegion> _candidateRegions = new List<MapNavRegion>();

    private void Start()
    {
        Bootstrap();
    }

    [ContextMenu("Bootstrap ECS Navigation")]
    public void Bootstrap()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            SetStatus("No default ECS world.");
            return;
        }

        if (map == null)
        {
            SetStatus("No MapNavigationAuthoring assigned.");
            return;
        }

        EntityManager entityManager = world.EntityManager;
        if (createNavigationSingleton)
            EnsureNavigationSingleton(entityManager);

        int spawned = spawnAgents ? SpawnAgents(entityManager) : 0;
        SetStatus($"NavigationSingleton={(createNavigationSingleton ? "ready" : "skipped")}, SpawnedAgents={spawned}");
    }

    private void EnsureNavigationSingleton(EntityManager entityManager)
    {
        EntityQuery query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MapNavigationBlobComponent>());
        if (!query.IsEmptyIgnoreFilter)
        {
            query.Dispose();
            return;
        }

        query.Dispose();
        Entity navigationEntity = entityManager.CreateEntity(typeof(MapNavigationBlobComponent));
        entityManager.SetComponentData(navigationEntity, new MapNavigationBlobComponent
        {
            Blob = map.BlobData,
            LocalToWorldMatrix = ToFloat4x4(map.transform.localToWorldMatrix),
            WorldToLocalMatrix = ToFloat4x4(map.transform.worldToLocalMatrix)
        });
    }

    private int SpawnAgents(EntityManager entityManager)
    {
        RebuildCandidateRegions();
        if (_candidateRegions.Count == 0)
            return 0;

        int spawned = 0;
        for (int i = 0; i < math.max(0, agentCount); i++)
        {
            if (!TrySampleRegionPoint(out Vector3 position))
                continue;

            Entity entity = entityManager.CreateEntity(
                typeof(LocalTransform),
                typeof(MapNavEcsAgent),
                typeof(MapNavEcsMotionState),
                typeof(MapNavEcsPathRequest),
                typeof(MapNavEcsTarget),
                typeof(MapNavEcsPathStatus));

            entityManager.SetComponentData(entity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 1f));
            entityManager.SetComponentData(entity, CreateAgent());
            entityManager.SetComponentData(entity, new MapNavEcsMotionState());
            entityManager.SetComponentData(entity, new MapNavEcsPathRequest());
            entityManager.SetComponentData(entity, new MapNavEcsTarget());
            entityManager.SetComponentData(entity, new MapNavEcsPathStatus());
            entityManager.AddBuffer<MapNavEcsWaypoint>(entity);
            spawned++;
        }

        return spawned;
    }

    private MapNavEcsAgent CreateAgent()
    {
        return new MapNavEcsAgent
        {
            AgentRadius = math.max(0f, agentRadius),
            StopDistance = math.max(0f, stopDistance),
            MoveSpeed = math.max(0f, moveSpeed),
            WaypointAdvanceDistance = math.max(0f, waypointAdvanceDistance),
            CornerLookAheadDistance = math.max(0f, cornerLookAheadDistance),
            HeightOffset = heightOffset,
            TargetRepathDistance = math.max(0f, targetRepathDistance),
            StuckRepathDelay = math.max(0f, stuckRepathDelay),
            StuckRepathCooldown = math.max(0f, stuckRepathCooldown),
            StuckProgressDistance = math.max(0f, stuckProgressDistance),
            UseRegionPathfinding = useRegionPathfinding ? (byte)1 : (byte)0,
            ConstrainToNavigationSpaces = constrainToNavigationSpaces ? (byte)1 : (byte)0
        };
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

            if (region.HasBounds)
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

    private static float4x4 ToFloat4x4(Matrix4x4 matrix)
    {
        return new float4x4(
            matrix.m00, matrix.m01, matrix.m02, matrix.m03,
            matrix.m10, matrix.m11, matrix.m12, matrix.m13,
            matrix.m20, matrix.m21, matrix.m22, matrix.m23,
            matrix.m30, matrix.m31, matrix.m32, matrix.m33);
    }

    private void SetStatus(string status)
    {
        lastStatus = status;
        Debug.Log($"[{nameof(MapNavigationEcsRuntimeBootstrap)}] {status}", this);
    }
}
