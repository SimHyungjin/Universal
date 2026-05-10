using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MapNavEcsAgentAuthoring : MonoBehaviour
{
    [SerializeField] private float agentRadius = 0.35f;
    [SerializeField] private float stopDistance = 0.08f;
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float waypointAdvanceDistance = 0.35f;
    [SerializeField] private float cornerLookAheadDistance;
    [SerializeField] private float heightOffset;
    [SerializeField] private float boundaryTolerance = 0.08f;
    [SerializeField] private float targetRepathDistance = 0.15f;
    [SerializeField] private float stuckRepathDelay = 0.75f;
    [SerializeField] private float stuckRepathCooldown = 1.5f;
    [SerializeField] private float stuckProgressDistance = 0.03f;
    [SerializeField] private bool useRegionPathfinding = true;
    [SerializeField] private bool constrainToNavigationSpaces = true;

    public float AgentRadius => agentRadius;
    public float StopDistance => stopDistance;
    public float MoveSpeed => moveSpeed;
    public float WaypointAdvanceDistance => waypointAdvanceDistance;
    public float CornerLookAheadDistance => cornerLookAheadDistance;
    public float HeightOffset => heightOffset;
    public float BoundaryTolerance => boundaryTolerance;
    public float TargetRepathDistance => targetRepathDistance;
    public float StuckRepathDelay => stuckRepathDelay;
    public float StuckRepathCooldown => stuckRepathCooldown;
    public float StuckProgressDistance => stuckProgressDistance;
    public bool UseRegionPathfinding => useRegionPathfinding;
    public bool ConstrainToNavigationSpaces => constrainToNavigationSpaces;
}

public sealed class MapNavEcsAgentAuthoringBaker : Baker<MapNavEcsAgentAuthoring>
{
    public override void Bake(MapNavEcsAgentAuthoring authoring)
    {
        Entity entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent(entity, new MapNavEcsAgent
        {
            AgentRadius = math.max(0f, authoring.AgentRadius),
            StopDistance = math.max(0f, authoring.StopDistance),
            MoveSpeed = math.max(0f, authoring.MoveSpeed),
            WaypointAdvanceDistance = math.max(0f, authoring.WaypointAdvanceDistance),
            CornerLookAheadDistance = math.max(0f, authoring.CornerLookAheadDistance),
            HeightOffset = authoring.HeightOffset,
            BoundaryTolerance = math.max(0f, authoring.BoundaryTolerance),
            TargetRepathDistance = math.max(0f, authoring.TargetRepathDistance),
            StuckRepathDelay = math.max(0f, authoring.StuckRepathDelay),
            StuckRepathCooldown = math.max(0f, authoring.StuckRepathCooldown),
            StuckProgressDistance = math.max(0f, authoring.StuckProgressDistance),
            UseRegionPathfinding = authoring.UseRegionPathfinding ? (byte)1 : (byte)0,
            ConstrainToNavigationSpaces = authoring.ConstrainToNavigationSpaces ? (byte)1 : (byte)0
        });

        AddComponent(entity, new MapNavEcsMotionState
        {
            IsMoving = 0,
            WaypointIndex = 0,
            CurrentSpeed = 0f,
            StuckTimer = 0f,
            RepathCooldownRemaining = 0f,
            LastDistanceToWaypoint = 0f,
            LastWaypointAnchor = default,
            Velocity = float3.zero
        });

        AddComponent(entity, new MapNavEcsPathRequest
        {
            Pending = 0,
            StartSpace = default,
            TargetSpace = default
        });

        AddComponent(entity, new MapNavEcsTarget
        {
            Dirty = 0,
            Position = default,
            AcceptedPosition = default
        });

        AddComponent(entity, new MapNavEcsPathStatus
        {
            HasPath = 0,
            Waiting = 0,
            Failed = 0,
            UsedCrossLayerTransition = 0,
            PathKind = 0
        });

        AddBuffer<MapNavEcsWaypoint>(entity);
    }
}
