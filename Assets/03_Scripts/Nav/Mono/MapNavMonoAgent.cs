using System.Collections.Generic;
using MapNav.Core;
using MapNav.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MapNavMonoAgent : MonoBehaviour
{
    [SerializeField] private MapNavigationAuthoring map;
    [SerializeField] private float agentRadius = 0.25f;
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float stopDistance = 0.08f;
    [SerializeField] private float waypointAdvanceDistance = 0.18f;
    [SerializeField] private float cornerLookAheadDistance = 0.35f;
    [SerializeField] private float heightOffset;
    [SerializeField] private float boundaryTolerance = 0.05f;

    [Header("Stuck recovery")]
    [SerializeField] private float stuckRepathDelay = 0.75f;
    [SerializeField] private float stuckRepathCooldown = 1.5f;
    [SerializeField] private float stuckProgressDistance = 0.03f;
    [SerializeField] private int stuckRetryLimit = 4;

    [SerializeField] private bool snapHeight = true;
    [SerializeField] private bool drawPath = true;
    [SerializeField] private Color pathColor = new(0.1f, 0.85f, 1f, 1f);
    [SerializeField] private Color waypointColor = new(1f, 0.85f, 0.1f, 1f);

    private readonly List<Waypoint> _waypoints = new();
    private int _waypointIndex;
    private Vector3 _lastWaypointAnchor;
    private Vector3 _target;
    private bool _hasPath;
    private bool _failed;

    private float _stuckTimer;
    private float _repathCooldownRemaining;
    private float _lastDistanceToWaypoint;
    private int _stuckRetryCount;

    private NavScratch _scratch;
    private NativeList<NavSpaceRef> _scratchNodes;
    private NativeList<NavPortal> _scratchPortals;
    private NativeList<float3> _scratchWaypoints;
    private bool _scratchInitialized;

    public bool HasPath => _hasPath;
    public bool Failed => _failed;
    public Vector3 Target => _target;
    public IReadOnlyList<Vector3> Waypoints
    {
        get
        {
            _publicWaypoints.Clear();
            for (int i = 0; i < _waypoints.Count; i++)
                _publicWaypoints.Add(_waypoints[i].Position);
            return _publicWaypoints;
        }
    }

    private readonly List<Vector3> _publicWaypoints = new();

    private void Reset()
    {
        map = FindFirstObjectByType<MapNavigationAuthoring>();
    }

    private void OnDestroy()
    {
        DisposeScratch();
    }

    private void EnsureScratchInitialized()
    {
        if (_scratchInitialized) return;
        _scratch = new NavScratch(64, Allocator.Persistent);
        _scratchNodes = new NativeList<NavSpaceRef>(16, Allocator.Persistent);
        _scratchPortals = new NativeList<NavPortal>(16, Allocator.Persistent);
        _scratchWaypoints = new NativeList<float3>(16, Allocator.Persistent);
        _scratchInitialized = true;
    }

    private void DisposeScratch()
    {
        if (!_scratchInitialized) return;
        if (_scratchWaypoints.IsCreated) _scratchWaypoints.Dispose();
        if (_scratchPortals.IsCreated) _scratchPortals.Dispose();
        if (_scratchNodes.IsCreated) _scratchNodes.Dispose();
        _scratch.Dispose();
        _scratchInitialized = false;
    }

    private void Update()
    {
        _repathCooldownRemaining = Mathf.Max(0f, _repathCooldownRemaining - Time.deltaTime);

        if (!_hasPath || _waypoints.Count == 0)
        {
            ApplyHeightSnap();
            return;
        }

        AdvancePastReachedWaypoints();
        if (_waypointIndex >= _waypoints.Count)
        {
            ClearPath(false);
            return;
        }

        Vector3 current = transform.position;
        Waypoint active = _waypoints[_waypointIndex];
        Vector3 planarDelta = active.Position - current;
        planarDelta.y = 0f;

        float reachDistance = NavAgentCore.GetReachDistance(stopDistance, waypointAdvanceDistance, active.Required);
        if (planarDelta.sqrMagnitude <= reachDistance * reachDistance)
        {
            _lastWaypointAnchor = active.Position;
            _waypointIndex++;
            ResetStuckTracking();
            return;
        }

        if (UpdateStuckProgress(planarDelta.magnitude))
            return;

        bool hasNext = _waypointIndex + 1 < _waypoints.Count;
        Vector3 nextWaypoint = hasNext ? _waypoints[_waypointIndex + 1].Position : current;
        Vector3 steeringTarget = NavAgentCore.GetSteeringTarget(current, active.Position, nextWaypoint, hasNext, cornerLookAheadDistance);
        Vector3 steering = NavAgentCore.ResolveSteering(steeringTarget, current, planarDelta);

        Vector3 direction = steering.normalized;
        float stepDistance = Mathf.Min(moveSpeed * Time.deltaTime, planarDelta.magnitude);
        Vector3 next = current + direction * stepDistance;

        NavContext ctx = CreateContext();
        if (!NavAgentCore.CanMove(in ctx, current, next, active.Position, boundaryTolerance, reachDistance))
        {
            AccumulateBlockedStuck();
            ApplyHeightSnap();
            return;
        }

        transform.position = next;
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

        ApplyHeightSnap();
    }

    public bool SetTarget(Vector3 worldTarget)
    {
        _target = worldTarget;
        _stuckRetryCount = 0;
        return RebuildPathToTarget();
    }

    public void ClearPath(bool failed = false)
    {
        _waypoints.Clear();
        _waypointIndex = 0;
        _hasPath = false;
        _failed = failed;
        _lastWaypointAnchor = transform.position;
        ResetStuckTracking();
    }

    private bool RebuildPathToTarget()
    {
        if (map == null)
        {
            ClearPath(true);
            return false;
        }

        BlobAssetReference<NavBlob> blob = map.NavBlobData;
        if (!blob.IsCreated)
        {
            ClearPath(true);
            return false;
        }

        EnsureScratchInitialized();

        NavContext ctx = new NavContext(blob, map.transform.localToWorldMatrix, map.transform.worldToLocalMatrix);
        _scratchNodes.Clear();
        _scratchPortals.Clear();
        _scratchWaypoints.Clear();

        bool built = NavPath.TryBuild(
            in ctx,
            transform.position,
            _target,
            agentRadius,
            boundaryTolerance,
            ref _scratch,
            ref _scratchNodes,
            ref _scratchPortals,
            ref _scratchWaypoints);

        _waypoints.Clear();
        if (built)
        {
            for (int i = 0; i < _scratchWaypoints.Length; i++)
            {
                Vector3 p = _scratchWaypoints[i];
                _waypoints.Add(new Waypoint
                {
                    Position = p,
                    Required = NavAgentCore.IsTransitionWaypoint(in ctx, p, boundaryTolerance)
                });
            }
        }

        if (!built || _waypoints.Count == 0)
        {
            ClearPath(true);
            return false;
        }

        _waypointIndex = 0;
        _lastWaypointAnchor = transform.position;
        _hasPath = true;
        _failed = false;
        ResetStuckTracking();
        return true;
    }

    private void AdvancePastReachedWaypoints()
    {
        Vector3 current = transform.position;
        int startIndex = _waypointIndex;
        while (_waypointIndex < _waypoints.Count)
        {
            Waypoint waypoint = _waypoints[_waypointIndex];
            Vector3 delta = waypoint.Position - current;
            delta.y = 0f;

            float reach = NavAgentCore.GetReachDistance(stopDistance, waypointAdvanceDistance, waypoint.Required);
            if (delta.sqrMagnitude <= reach * reach)
            {
                _lastWaypointAnchor = waypoint.Position;
                _waypointIndex++;
                continue;
            }

            if (!waypoint.Required && NavAgentCore.HasPassedWaypoint(_lastWaypointAnchor, waypoint.Position, current))
            {
                _lastWaypointAnchor = waypoint.Position;
                _waypointIndex++;
                continue;
            }

            break;
        }

        if (_waypointIndex != startIndex)
            ResetStuckTracking();
    }

    private void ResetStuckTracking()
    {
        _stuckTimer = 0f;
        _lastDistanceToWaypoint = 0f;
    }

    private bool UpdateStuckProgress(float distanceToWaypoint)
    {
        if (NavAgentCore.EvaluateProgressStuck(
                ref _stuckTimer, ref _lastDistanceToWaypoint, _repathCooldownRemaining,
                distanceToWaypoint, stuckRepathDelay, stuckProgressDistance, moveSpeed, Time.deltaTime))
            return TriggerStuckRepath();

        return false;
    }

    private void AccumulateBlockedStuck()
    {
        if (NavAgentCore.EvaluateBlockedStuck(ref _stuckTimer, _repathCooldownRemaining, stuckRepathDelay, Time.deltaTime))
            TriggerStuckRepath();
    }

    // Bumps the per-target retry counter and either repaths or, once stuckRetryLimit is hit,
    // gives up (Failed). _stuckRetryCount is cleared only by SetTarget, so the limit bounds
    // total stuck repaths per target and prevents an endless repath loop.
    private bool TriggerStuckRepath()
    {
        bool giveUp = NavAgentCore.CommitStuckRepath(
            ref _stuckTimer, ref _repathCooldownRemaining, ref _stuckRetryCount,
            stuckRepathCooldown, stuckRetryLimit);

        if (giveUp)
            ClearPath(true);
        else
            RebuildPathToTarget();

        return true;
    }

    private void ApplyHeightSnap()
    {
        if (!snapHeight)
            return;

        NavContext ctx = CreateContext();
        if (NavAgentCore.TrySnapHeight(in ctx, transform.position, boundaryTolerance, heightOffset, out float3 snapped))
            transform.position = snapped;
    }

    private NavContext CreateContext()
    {
        return map != null
            ? new NavContext(map.NavBlobData, map.transform.localToWorldMatrix, map.transform.worldToLocalMatrix)
            : default;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawPath || _waypoints.Count == 0)
            return;

        Gizmos.color = pathColor;
        Vector3 previous = transform.position;
        for (int i = _waypointIndex; i < _waypoints.Count; i++)
        {
            Gizmos.DrawLine(previous, _waypoints[i].Position);
            previous = _waypoints[i].Position;
        }

        Gizmos.color = waypointColor;
        for (int i = _waypointIndex; i < _waypoints.Count; i++)
            Gizmos.DrawSphere(_waypoints[i].Position, 0.08f);
    }

    private struct Waypoint
    {
        public Vector3 Position;
        public bool Required;
    }
}
