using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public sealed class MapNavAgent : MonoBehaviour
{
    [SerializeField] private MapNavigationAuthoring navigation;
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float stopDistance = 0.08f;
    [SerializeField] private float waypointAdvanceDistance = 0.35f;
    [SerializeField] private float agentRadius = 0.35f;
    [SerializeField] private float boundaryTolerance = 0.08f;
    [SerializeField] private float cornerLookAheadDistance;
    [SerializeField] private float heightOffset;
    [SerializeField] private bool useRegionPathfinding = true;
    [SerializeField] private bool constrainToNavigationSpaces = true;
    [SerializeField] private bool logDebug;

    private Vector3 _targetPosition;
    private readonly List<Vector3> _waypoints = new();
    private bool _hasTarget;
    private string _currentSpaceName = "None";
    private string _lastLoggedSpaceName;
    private string _lastLogMessage;
    private Vector3 _lastWaypointAnchor;
    private int _currentRegionId = -1;
    private int _currentTransitionId = -1;

    public string CurrentSpaceName => _currentSpaceName;
    public bool HasTarget => _hasTarget;
    public Vector3 TargetPosition => _targetPosition;

    private void Awake()
    {
        if (navigation == null)
            navigation = FindFirstObjectByType<MapNavigationAuthoring>();
    }

    private void OnEnable()
    {
        Log($"Enabled. Navigation={(navigation != null ? navigation.name : "null")}");
    }

    private void Update()
    {
        MoveToTarget(Time.deltaTime);
        SnapHeightToNavigation();
    }

    public void SetTarget(Vector3 worldPosition)
    {
        BuildWaypoints(ResolveNavigationTarget(worldPosition));
        _hasTarget = true;
    }

    private void MoveToTarget(float deltaTime)
    {
        if (!_hasTarget)
            return;

        if (_waypoints.Count == 0)
        {
            _hasTarget = false;
            return;
        }

        AdvancePastStaleWaypoints(transform.position);

        if (_waypoints.Count == 0)
        {
            _hasTarget = false;
            return;
        }

        Vector3 current = transform.position;
        _targetPosition = GetSteeringTarget(current);

        Vector3 toTarget = _waypoints[0] - current;
        toTarget.y = 0f;

        float distance = toTarget.magnitude;
        float reachDistance = GetCurrentWaypointReachDistance();
        if (distance <= reachDistance)
        {
            _waypoints.RemoveAt(0);
            _lastWaypointAnchor = transform.position;
            _hasTarget = _waypoints.Count > 0;
            Log($"Waypoint reached. Position={transform.position}, RemainingWaypoints={_waypoints.Count}");
            return;
        }

        Vector3 steering = _targetPosition - current;
        steering.y = 0f;
        if (steering.sqrMagnitude < 0.0001f)
            steering = toTarget;

        if (Vector3.Dot(steering, toTarget) < 0f)
            steering = toTarget;

        Vector3 step = steering.normalized * (moveSpeed * deltaTime);
        if (step.magnitude > distance)
            step = toTarget;

        Vector3 nextPosition = current + step;
        if (!CanMoveAgainstObstacles(current, nextPosition))
        {
            if (TryReplanAroundObstacle(current))
                return;

            LogOnce($"Blocked move into obstacle. CurrentWaypoint={_waypoints[0]}, RemainingWaypoints={_waypoints.Count}");
            return;
        }

        if (constrainToNavigationSpaces && !CanMoveToConstrainedPosition(current, nextPosition))
        {
            LogOnce($"Blocked move outside navigation spaces. CurrentWaypoint={_waypoints[0]}, RemainingWaypoints={_waypoints.Count}");
            return;
        }

        transform.position = nextPosition;
        Log($"Moving. Position={transform.position}, Target={_targetPosition}, Remaining={distance:0.###}");
    }

    private bool TryReplanAroundObstacle(Vector3 current)
    {
        if (navigation == null || _waypoints.Count == 0)
            return false;

        MapNavigationQueryContext context = navigation.QueryContext;
        MapNavRegion currentRegion = MapNavigationQuery.FindContainingRegion(context, current, boundaryTolerance);
        MapNavRegion targetRegion = MapNavigationQuery.FindContainingRegion(context, _waypoints[^1], boundaryTolerance);
        if (currentRegion == null || targetRegion == null || currentRegion.Id != targetRegion.Id)
            return false;

        if (MapNavigationQuery.FindInternalRegionPath(context, currentRegion, current, _waypoints[^1], agentRadius, out List<Vector3> internalPath) != MapNavigationQuery.InternalPathResult.PathFound)
            return false;

        _waypoints.Clear();
        _lastWaypointAnchor = current;
        for (int i = 0; i < internalPath.Count; i++)
            AddWaypointIfSeparated(internalPath[i]);

        Log($"Replanned around obstacle in {currentRegion.DisplayName}. {DescribeWaypoints()}");
        return _waypoints.Count > 0;
    }

    private bool CanMoveAgainstObstacles(Vector3 current, Vector3 nextPosition)
    {
        if (navigation == null)
            return true;

        MapNavigationQueryContext context = navigation.QueryContext;
        if (!MapNavigationQuery.IsInsideAnyObstacle(context, nextPosition, boundaryTolerance))
            return true;

        if (!MapNavigationQuery.IsInsideAnyObstacle(context, current, boundaryTolerance))
            return false;

        if (!MapNavigationQuery.TryProjectOutOfAnyObstacle(context, current, boundaryTolerance, agentRadius, out Vector3 projected))
            return false;

        Vector3 currentDelta = projected - current;
        Vector3 nextDelta = projected - nextPosition;
        currentDelta.y = 0f;
        nextDelta.y = 0f;
        return nextDelta.sqrMagnitude < currentDelta.sqrMagnitude;
    }

    private bool CanMoveToConstrainedPosition(Vector3 current, Vector3 nextPosition)
    {
        if (navigation == null)
            return true;

        MapNavigationQueryContext context = navigation.QueryContext;
        if (MapNavigationQuery.IsInsideNavigationSpace(context, nextPosition, boundaryTolerance))
            return true;

        if (MapNavigationQuery.IsInsideNavigationSpace(context, current, boundaryTolerance))
            return false;

        if (!MapNavigationQuery.TryProjectToClosestNavigationSpace(context, current, out Vector3 currentProjection, out _, out _))
            return false;

        Vector3 currentDelta = currentProjection - current;
        Vector3 nextDelta = currentProjection - nextPosition;
        currentDelta.y = 0f;
        nextDelta.y = 0f;
        return nextDelta.sqrMagnitude < currentDelta.sqrMagnitude;
    }

    private void AdvancePastStaleWaypoints(Vector3 current)
    {
        while (_waypoints.Count > 1)
        {
            Vector3 toCurrent = _waypoints[0] - current;
            toCurrent.y = 0f;

            if (toCurrent.sqrMagnitude <= waypointAdvanceDistance * waypointAdvanceDistance)
            {
                _lastWaypointAnchor = _waypoints[0];
                _waypoints.RemoveAt(0);
                continue;
            }

            if (HasPassedCurrentWaypoint(_lastWaypointAnchor, _waypoints[0], current))
            {
                Log($"Waypoint passed by projection. Waypoint={_waypoints[0]}, Position={current}");
                _lastWaypointAnchor = _waypoints[0];
                _waypoints.RemoveAt(0);
                continue;
            }

            break;
        }
    }

    private static bool HasPassedCurrentWaypoint(Vector3 anchor, Vector3 waypoint, Vector3 current)
    {
        Vector3 segment = waypoint - anchor;
        segment.y = 0f;
        float sqrLength = segment.sqrMagnitude;
        if (sqrLength <= 0.0001f)
            return false;

        Vector3 fromAnchor = current - anchor;
        fromAnchor.y = 0f;
        float t = Vector3.Dot(fromAnchor, segment) / sqrLength;
        if (t < 1f)
            return false;

        Vector3 fromWaypoint = current - waypoint;
        fromWaypoint.y = 0f;
        return Vector3.Dot(fromWaypoint, segment) > 0f;
    }

    private Vector3 GetSteeringTarget(Vector3 current)
    {
        if (cornerLookAheadDistance <= 0f)
            return _waypoints[0];

        if (_waypoints.Count <= 1)
            return _waypoints[0];

        Vector3 toCurrentWaypoint = _waypoints[0] - current;
        toCurrentWaypoint.y = 0f;
        float distance = toCurrentWaypoint.magnitude;

        if (distance >= cornerLookAheadDistance)
            return _waypoints[0];

        float blend = 1f - Mathf.Clamp01(distance / Mathf.Max(0.0001f, cornerLookAheadDistance));
        return Vector3.Lerp(_waypoints[0], _waypoints[1], blend);
    }

    private float GetCurrentWaypointReachDistance()
    {
        if (_waypoints.Count <= 1)
            return stopDistance;

        return Mathf.Max(stopDistance, waypointAdvanceDistance);
    }

    private void BuildWaypoints(Vector3 worldTarget)
    {
        _waypoints.Clear();
        _lastWaypointAnchor = transform.position;

        if (navigation == null)
        {
            _waypoints.Add(worldTarget);
            return;
        }

        MapNavigationQueryContext context = navigation.QueryContext;
        Vector3 resolvedTarget = worldTarget;
        MapNavRegion currentRegion = MapNavigationQuery.FindContainingRegion(context, transform.position, boundaryTolerance);
        MapNavRegion targetRegion = MapNavigationQuery.FindContainingRegion(context, resolvedTarget, boundaryTolerance);

        if (currentRegion == null)
        {
            AddOutsideRecoveryWaypoint();
            _waypoints.Add(resolvedTarget);
            Log($"Direct target because current region is null. Agent={transform.position}, Target={resolvedTarget}");
            return;
        }

        if (targetRegion == null)
        {
            if (MapNavigationQuery.TryProjectToClosestNavigationSpace(context, resolvedTarget, out Vector3 projected, out string spaceName, out float distance))
            {
                resolvedTarget = projected;
                targetRegion = MapNavigationQuery.FindContainingRegion(context, resolvedTarget, boundaryTolerance);
                Log($"Reprojected blocked target to {spaceName}. Projected={projected}, Distance={distance:0.###}");
            }
        }

        if (targetRegion == null)
        {
            _waypoints.Add(resolvedTarget);
            Log($"Direct target because target region is null. Current={currentRegion.DisplayName}, Target={resolvedTarget}");
            return;
        }

        if (currentRegion.Id == targetRegion.Id)
        {
            if (TryAddInternalRegionPath(currentRegion, transform.position, resolvedTarget))
            {
                Log($"Built internal region path in {currentRegion.DisplayName}. {DescribeWaypoints()}");
                return;
            }

            _waypoints.Add(resolvedTarget);
            Log($"Direct target because current and target are both {currentRegion.DisplayName}. Agent={transform.position}, Target={resolvedTarget}");
            return;
        }

        if (!useRegionPathfinding)
        {
            _waypoints.Add(resolvedTarget);
            Log($"Direct target because region pathfinding is disabled. Agent={transform.position}, Target={resolvedTarget}");
            return;
        }

        if (!MapNavigationQuery.TryFindRegionPath(context, currentRegion.Id, targetRegion.Id, transform.position, resolvedTarget, agentRadius, out List<MapNavigationQuery.PathStep> path))
        {
            _waypoints.Add(resolvedTarget);
            Log($"No region path from Region {currentRegion.Id} to Region {targetRegion.Id}. Using direct target.");
            return;
        }

        for (int i = 0; i < path.Count; i++)
        {
            if (path[i].UsesTransition)
            {
                MapNavTransition transition = context.FindTransition(path[i].TransitionId);
                if (transition == null)
                    continue;

                MapNavigationQuery.GetTransitionEndpointWorld(context, transition, path[i].IsForward, agentRadius, out Vector3 transitionEntry, out Vector3 transitionExit);
                AddRegionWaypoint(path[i].FromRegionId, transitionEntry);
                AddWaypointIfSeparated(transitionExit);
                continue;
            }

            AddRegionWaypoint(path[i].FromRegionId, path[i].EntryWorld);
            AddWaypointIfSeparated(path[i].ExitWorld);
        }

        AddRegionWaypoint(targetRegion.Id, resolvedTarget);
        Log($"Built region path: Region {currentRegion.Id} -> Region {targetRegion.Id}. Steps={path.Count}. {DescribePath(path)} {DescribeWaypoints()}");
    }

    private void AddRegionWaypoint(int regionId, Vector3 waypoint)
    {
        MapNavRegion region = navigation.FindRegion(regionId);
        Vector3 from = _waypoints.Count > 0 ? _waypoints[^1] : transform.position;
        if (region != null && TryAddInternalRegionPath(region, from, waypoint))
            return;

        AddWaypointIfSeparated(waypoint);
    }

    private bool TryAddInternalRegionPath(MapNavRegion region, Vector3 from, Vector3 to)
    {
        if (navigation == null || MapNavigationQuery.FindInternalRegionPath(navigation.QueryContext, region, from, to, agentRadius, out List<Vector3> internalPath) != MapNavigationQuery.InternalPathResult.PathFound)
            return false;

        for (int i = 0; i < internalPath.Count; i++)
            AddWaypointIfSeparated(internalPath[i]);

        return true;
    }

    private Vector3 ResolveNavigationTarget(Vector3 worldTarget)
    {
        if (navigation == null)
            return worldTarget;

        MapNavigationQueryContext context = navigation.QueryContext;
        MapNavRegion targetRegion = MapNavigationQuery.FindContainingRegion(context, worldTarget, boundaryTolerance);
        if (targetRegion != null)
        {
            Vector3 obstacleResolvedTarget = ResolveTargetOutOfObstacles(targetRegion, worldTarget);
            if (obstacleResolvedTarget != worldTarget)
                return obstacleResolvedTarget;
        }

        return ResolveTargetToNavigationSpace(worldTarget);
    }

    private Vector3 ResolveTargetToNavigationSpace(Vector3 worldTarget)
    {
        MapNavigationQueryContext context = navigation.QueryContext;
        if (MapNavigationQuery.IsInsideNavigationSpace(context, worldTarget, boundaryTolerance))
            return worldTarget;

        if (!MapNavigationQuery.TryProjectToClosestNavigationSpace(context, worldTarget, out Vector3 projected, out string spaceName, out float distance))
            return worldTarget;

        Log($"Projected outside target to {spaceName}. Original={worldTarget}, Projected={projected}, Distance={distance:0.###}");
        return projected;
    }

    private Vector3 ResolveTargetOutOfObstacles(MapNavRegion targetRegion, Vector3 worldTarget)
    {
        if (!MapNavigationQuery.TryProjectOutOfObstacles(
            navigation.QueryContext,
            targetRegion,
            worldTarget,
            transform.position,
            agentRadius,
            out Vector3 projected,
            out string obstacleName,
            out float distance))
        {
            return worldTarget;
        }

        Log($"Projected target out of {targetRegion.DisplayName} {obstacleName}. Original={worldTarget}, Projected={projected}, Distance={distance:0.###}");
        return projected;
    }

    private void AddOutsideRecoveryWaypoint()
    {
        if (navigation == null || !MapNavigationQuery.TryProjectToClosestNavigationSpace(navigation.QueryContext, transform.position, out Vector3 projected, out string spaceName, out float distance))
            return;

        AddWaypointIfSeparated(projected);
        Log($"Added outside recovery waypoint to {spaceName}. Projected={projected}, Distance={distance:0.###}");
    }

    private static string DescribePath(IReadOnlyList<MapNavigationQuery.PathStep> path)
    {
        if (path.Count == 0)
            return "No steps.";

        System.Text.StringBuilder builder = new();
        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0)
                builder.Append(" -> ");

            builder.Append(path[i].FromRegionId);
            if (path[i].UsesTransition)
            {
                builder.Append(" --T");
                builder.Append(path[i].TransitionId);
                builder.Append(path[i].IsForward ? "-> " : "<- ");
            }
            else
            {
                builder.Append(" --R-> ");
            }

            builder.Append(path[i].ToRegionId);
        }

        return builder.ToString();
    }

    private void AddWaypointIfSeparated(Vector3 waypoint)
    {
        Vector3 previous = _waypoints.Count > 0 ? _waypoints[^1] : transform.position;
        Vector3 delta = waypoint - previous;
        delta.y = 0f;

        if (delta.sqrMagnitude <= stopDistance * stopDistance)
            return;

        _waypoints.Add(waypoint);
    }

    private string DescribeWaypoints()
    {
        if (_waypoints.Count == 0)
            return "Waypoints=None";

        System.Text.StringBuilder builder = new("Waypoints=");
        for (int i = 0; i < _waypoints.Count; i++)
        {
            if (i > 0)
                builder.Append(" | ");

            builder.Append(i);
            builder.Append(":");
            builder.Append(_waypoints[i].ToString("F2"));
            builder.Append("/");
            builder.Append(GetWaypointSpaceName(_waypoints[i]));
        }

        return builder.ToString();
    }

    private string GetWaypointSpaceName(Vector3 waypoint)
    {
        if (navigation == null)
            return "No Navigation";

        if (MapNavigationQuery.TryGetNavigationHeight(
            navigation.QueryContext,
            waypoint,
            boundaryTolerance,
            -1,
            -1,
            out _,
            out string spaceName,
            out _,
            out _))
        {
            return spaceName;
        }

        return "Outside";
    }

    private void SnapHeightToNavigation()
    {
        if (navigation == null)
        {
            _currentSpaceName = "No Navigation";
            LogSpaceChange("Navigation reference is null.");
            return;
        }

        if (MapNavigationQuery.TryGetNavigationHeight(
            navigation.QueryContext,
            transform.position,
            boundaryTolerance,
            _currentTransitionId,
            _currentRegionId,
            out float navHeight,
            out string spaceName,
            out int transitionId,
            out int regionId))
        {
            ApplyHeight(navHeight);
            _currentSpaceName = spaceName;
            _currentTransitionId = transitionId;
            _currentRegionId = regionId;
            LogSpaceChange($"On {spaceName}, height={navHeight:0.###}");
            return;
        }

        _currentSpaceName = spaceName;
        _currentTransitionId = -1;
        _currentRegionId = -1;
        LogSpaceChange("Outside navigation spaces.");
    }

    private void ApplyHeight(float navHeight)
    {
        Vector3 position = transform.position;
        position.y = navigation.transform.TransformPoint(new Vector3(0f, navHeight, 0f)).y + heightOffset;
        transform.position = position;
    }

    private void Log(string message)
    {
        if (!logDebug)
            return;

        Debug.Log($"[MapNavAgent] {message}", this);
    }

    private void LogOnce(string message)
    {
        if (_lastLogMessage == message)
            return;

        _lastLogMessage = message;
        Log(message);
    }

    private void LogSpaceChange(string message)
    {
        if (_lastLoggedSpaceName == _currentSpaceName)
            return;

        _lastLoggedSpaceName = _currentSpaceName;
        Log(message);
    }

    private void OnDrawGizmosSelected()
    {
        if (_hasTarget)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(_targetPosition, 0.18f);

            Vector3 from = transform.position;
            for (int i = 0; i < _waypoints.Count; i++)
            {
                Gizmos.DrawWireSphere(_waypoints[i], 0.14f);
                Gizmos.DrawLine(from, _waypoints[i]);
                from = _waypoints[i];
            }
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.15f);
    }
}
