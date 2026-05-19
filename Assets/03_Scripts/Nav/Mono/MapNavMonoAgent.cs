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
        if (!_hasPath || _waypoints.Count == 0)
        {
            if (snapHeight)
                SnapHeight();
            return;
        }

        AdvancePastReachedWaypoints();
        if (_waypointIndex >= _waypoints.Count)
        {
            ClearPath(false);
            return;
        }

        Vector3 current = transform.position;
        Vector3 waypoint = _waypoints[_waypointIndex].Position;
        Vector3 planarDelta = waypoint - current;
        planarDelta.y = 0f;

        float reachDistance = GetReachDistance(_waypoints[_waypointIndex]);
        if (planarDelta.sqrMagnitude <= reachDistance * reachDistance)
        {
            _lastWaypointAnchor = waypoint;
            _waypointIndex++;
            return;
        }

        Vector3 steeringTarget = GetSteeringTarget(current);
        Vector3 steering = steeringTarget - current;
        steering.y = 0f;
        if (steering.sqrMagnitude <= 0.0001f || Vector3.Dot(steering, planarDelta) < 0f)
            steering = planarDelta;

        Vector3 direction = steering.normalized;
        float stepDistance = Mathf.Min(moveSpeed * Time.deltaTime, planarDelta.magnitude);
        Vector3 next = current + direction * stepDistance;

        if (!CanMoveTo(next, waypoint, reachDistance))
        {
            RebuildPathToTarget();
            return;
        }

        transform.position = next;
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

        if (snapHeight)
            SnapHeight();
    }

    public bool SetTarget(Vector3 worldTarget)
    {
        _target = worldTarget;
        return RebuildPathToTarget();
    }

    public void ClearPath(bool failed = false)
    {
        _waypoints.Clear();
        _waypointIndex = 0;
        _hasPath = false;
        _failed = failed;
        _lastWaypointAnchor = transform.position;
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
                    Required = false
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
        return true;
    }

    private void AdvancePastReachedWaypoints()
    {
        Vector3 current = transform.position;
        while (_waypointIndex < _waypoints.Count)
        {
            Waypoint waypoint = _waypoints[_waypointIndex];
            Vector3 delta = waypoint.Position - current;
            delta.y = 0f;

            if (delta.sqrMagnitude <= GetReachDistance(waypoint) * GetReachDistance(waypoint))
            {
                _lastWaypointAnchor = waypoint.Position;
                _waypointIndex++;
                continue;
            }

            if (!waypoint.Required && HasPassedWaypoint(_lastWaypointAnchor, waypoint.Position, current))
            {
                _lastWaypointAnchor = waypoint.Position;
                _waypointIndex++;
                continue;
            }

            break;
        }
    }

    private Vector3 GetSteeringTarget(Vector3 current)
    {
        Vector3 waypoint = _waypoints[_waypointIndex].Position;
        if (cornerLookAheadDistance <= 0f || _waypointIndex + 1 >= _waypoints.Count)
            return waypoint;

        Vector3 delta = waypoint - current;
        delta.y = 0f;
        float distance = delta.magnitude;
        if (distance >= cornerLookAheadDistance)
            return waypoint;

        float blend = 1f - Mathf.Clamp01(distance / Mathf.Max(0.0001f, cornerLookAheadDistance));
        return Vector3.Lerp(waypoint, _waypoints[_waypointIndex + 1].Position, blend);
    }

    private bool CanMoveTo(Vector3 nextPosition, Vector3 waypoint, float reachDistance)
    {
        if (map == null)
            return true;

        NavContext ctx = new NavContext(map.NavBlobData, map.transform.localToWorldMatrix, map.transform.worldToLocalMatrix);
        if (!ctx.IsValid)
            return true;

        if (NavQuery.TryClassify(in ctx, nextPosition, boundaryTolerance, out _))
            return true;

        if (NavQuery.TryClassify(in ctx, waypoint, boundaryTolerance, out NavSpaceRef waypointSpace)
            && waypointSpace.Kind == NavSpaceKind.Transition)
        {
            Vector3 delta = waypoint - nextPosition;
            delta.y = 0f;
            float enterDistance = Mathf.Max(reachDistance, 0.35f);
            return delta.sqrMagnitude <= enterDistance * enterDistance;
        }

        return false;
    }

    private void SnapHeight()
    {
        if (map == null)
            return;

        NavContext ctx = new NavContext(map.NavBlobData, map.transform.localToWorldMatrix, map.transform.worldToLocalMatrix);
        if (!NavQuery.TryGetHeight(in ctx, transform.position, boundaryTolerance, out float worldHeight))
            return;

        Vector3 p = transform.position;
        p.y = worldHeight + heightOffset;
        transform.position = p;
    }

    private float GetReachDistance(Waypoint waypoint)
    {
        return waypoint.Required
            ? Mathf.Max(0.0001f, stopDistance)
            : Mathf.Max(stopDistance, waypointAdvanceDistance);
    }

    private static bool HasPassedWaypoint(Vector3 anchor, Vector3 waypoint, Vector3 current)
    {
        Vector3 segment = waypoint - anchor;
        segment.y = 0f;
        if (segment.sqrMagnitude <= 0.0001f) return false;

        Vector3 fromAnchor = current - anchor;
        fromAnchor.y = 0f;
        float t = Vector3.Dot(fromAnchor, segment) / segment.sqrMagnitude;
        if (t < 1f - 0.0001f) return false;

        Vector3 fromWaypoint = current - waypoint;
        fromWaypoint.y = 0f;
        return Vector3.Dot(fromWaypoint, segment) >= -0.0001f;
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
