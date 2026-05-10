using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public sealed class MapNavAgent : MonoBehaviour, IMapNavigationPathAssembler
{
    private const float RegionSelectionHeightTolerance = 1.5f;

    [SerializeField] private MapNavigationAuthoring navigation;
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float stopDistance = 0.08f;
    [SerializeField] private float waypointAdvanceDistance = 0.35f;
    [SerializeField] private float agentRadius = 0.35f;
    [SerializeField] private float boundaryTolerance = 0.08f;
    [SerializeField] private float cornerLookAheadDistance;
    [SerializeField] private float heightOffset;
    [SerializeField] private bool useRegionPathfinding = true;
    [SerializeField] private bool useBuildDataContextQueries;
    [SerializeField] private bool useBlobDataContextQueries;
    [SerializeField] private bool constrainToNavigationSpaces = true;
    [SerializeField] private bool logDebug;

    private Vector3 _targetPosition;
    private readonly List<MapNavWaypoint> _waypoints = new();
    private readonly List<MapNavWaypoint> _previousWaypoints = new();
    private readonly MapNavigationPathBuildResult _pathBuildResult = new();
    private MapNavigationBuildData _cachedBuildPathData;
    private Matrix4x4 _cachedBuildPathLocalToWorld;
    private Matrix4x4 _cachedBuildPathWorldToLocal;
    private MapNavigationBuildDataPathContext _cachedBuildPathContext;
    private Unity.Entities.BlobAssetReference<MapNavigationBlob> _cachedBlobPathData;
    private Matrix4x4 _cachedBlobPathLocalToWorld;
    private Matrix4x4 _cachedBlobPathWorldToLocal;
    private MapNavigationBlobPathContext _cachedBlobPathContext;
    private bool _hasTarget;
    private string _currentSpaceName = "None";
    private string _lastLoggedSpaceName;
    private string _lastLogMessage;
    private Vector3 _lastWaypointAnchor;
    private int _currentRegionId = -1;
    private int _currentTransitionId = -1;
    private NavigationHeightSample _lastMoveNavigationSample;

    public string CurrentSpaceName => _currentSpaceName;
    public bool HasTarget => _hasTarget;
    public Vector3 TargetPosition => _targetPosition;

    private struct NavigationHeightSample
    {
        public bool IsValid;
        public int Frame;
        public Vector3 Position;
        public float Height;
        public string SpaceName;
        public int TransitionId;
        public int RegionId;
    }

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
        bool hadTarget = _hasTarget;
        Vector3 previousTarget = _targetPosition;
        Vector3 previousAnchor = _lastWaypointAnchor;
        bool hasPreviousWaypoints = hadTarget && _waypoints.Count > 0;
        _previousWaypoints.Clear();
        if (hasPreviousWaypoints)
            _previousWaypoints.AddRange(_waypoints);

        BuildWaypoints(worldPosition);
        if (_waypoints.Count > 0)
        {
            _targetPosition = _waypoints[^1].Position;
            _hasTarget = true;
            return;
        }

        if (hasPreviousWaypoints)
        {
            ClearWaypoints();
            _waypoints.AddRange(_previousWaypoints);
            _targetPosition = previousTarget;
            _lastWaypointAnchor = previousAnchor;
            _hasTarget = true;
            Log($"Ignored target because no valid waypoint was built. Target={worldPosition}");
            return;
        }

        _hasTarget = false;
    }

    private void MoveToTarget(float deltaTime)
    {
        _lastMoveNavigationSample.IsValid = false;

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

        Vector3 toTarget = _waypoints[0].Position - current;
        toTarget.y = 0f;

        float distance = toTarget.magnitude;
        float reachDistance = GetCurrentWaypointReachDistance();
        if (distance <= reachDistance)
        {
            RemoveWaypointAt(0);
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

        float moveDistance = moveSpeed * deltaTime;
        Vector3 step;
        if ((steering - toTarget).sqrMagnitude <= 0.000001f)
        {
            step = toTarget * (moveDistance / distance);
        }
        else
        {
            step = steering.normalized * moveDistance;
        }

        if (moveDistance > distance)
            step = toTarget;

        Vector3 nextPosition = current + step;
        if (!CanMoveAgainstObstacles(current, nextPosition))
        {
            if (TryReplanAroundObstacle(current))
                return;

            LogOnce($"Blocked move into obstacle. CurrentWaypoint={_waypoints[0].Position}, RemainingWaypoints={_waypoints.Count}");
            return;
        }

        if (constrainToNavigationSpaces && !CanMoveToConstrainedPosition(current, nextPosition))
        {
            LogOnce($"Blocked move outside navigation spaces. CurrentWaypoint={_waypoints[0].Position}, RemainingWaypoints={_waypoints.Count}");
            return;
        }

        transform.position = nextPosition;
        //Log($"Moving. Position={transform.position}, Target={_targetPosition}, Remaining={distance:0.###}");
    }

    private bool TryReplanAroundObstacle(Vector3 current)
    {
        if (navigation == null || _waypoints.Count == 0)
            return false;

        MapNavRegion currentRegion = FindContainingRegion(current, true);
        MapNavRegion targetRegion = FindContainingRegion(_waypoints[^1].Position);
        if (currentRegion == null || targetRegion == null || currentRegion.Id != targetRegion.Id)
            return false;

        if (FindInternalRegionPath(currentRegion.Id, current, _waypoints[^1].Position, out List<Vector3> internalPath) != MapNavigationQuery.InternalPathResult.PathFound)
            return false;

        ClearWaypoints();
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

        if (!IsInsideAnyObstacle(nextPosition))
            return true;

        if (!IsInsideAnyObstacle(current))
            return false;

        if (!TryProjectOutOfAnyObstacle(current, out Vector3 projected))
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

        if (TryCacheNavigationHeightSample(nextPosition))
            return true;

        if (IsInsideNavigationSpace(current))
            return false;

        if (!TryProjectToClosestNavigationSpace(current, out Vector3 currentProjection, out _, out _))
            return false;

        Vector3 currentDelta = currentProjection - current;
        Vector3 nextDelta = currentProjection - nextPosition;
        currentDelta.y = 0f;
        nextDelta.y = 0f;
        return nextDelta.sqrMagnitude < currentDelta.sqrMagnitude;
    }

    private bool IsInsideNavigationSpace(Vector3 worldPosition)
    {
        if (navigation == null)
            return true;

        if (useBlobDataContextQueries)
            return MapNavigationQuery.IsInsideNavigationSpace(navigation.BlobDataContext, worldPosition, boundaryTolerance);

        if (useBuildDataContextQueries)
            return MapNavigationQuery.IsInsideNavigationSpace(navigation.BuildDataContext, worldPosition, boundaryTolerance);

        return MapNavigationQuery.IsInsideNavigationSpace(navigation.QueryContext, worldPosition, boundaryTolerance);
    }

    private bool IsInsideAnyObstacle(Vector3 worldPosition)
    {
        if (navigation == null)
            return false;

        MapNavRegion region = FindContainingRegion(worldPosition, true);
        if (region == null)
            return false;

        if (useBlobDataContextQueries)
        {
            return navigation.BlobDataContext.TryFindRegion(region.Id, out MapNavRegionBlob blobRegion)
                && MapNavigationQuery.IsInsideRegionObstacle(navigation.BlobDataContext, blobRegion, worldPosition);
        }

        if (useBuildDataContextQueries)
        {
            return navigation.BuildDataContext.TryFindRegion(region.Id, out MapNavRegionData regionData)
                && MapNavigationQuery.IsInsideRegionObstacle(navigation.BuildDataContext, regionData, worldPosition);
        }

        return MapNavigationQuery.IsInsideRegionObstacle(navigation.QueryContext, region, worldPosition);
    }

    private bool TryProjectOutOfAnyObstacle(Vector3 worldPosition, out Vector3 projected)
    {
        if (navigation == null)
        {
            projected = worldPosition;
            return false;
        }

        MapNavRegion region = FindContainingRegion(worldPosition, true);
        if (region == null)
        {
            projected = worldPosition;
            return false;
        }

        return TryProjectOutOfObstacles(region, worldPosition, worldPosition, out projected, out _, out _);
    }

    private void AdvancePastStaleWaypoints(Vector3 current)
    {
        while (_waypoints.Count > 1)
        {
            Vector3 toCurrent = _waypoints[0].Position - current;
            toCurrent.y = 0f;
            float advanceDistance = IsRequiredWaypoint(0) ? stopDistance : waypointAdvanceDistance;

            if (toCurrent.sqrMagnitude <= advanceDistance * advanceDistance)
            {
                _lastWaypointAnchor = _waypoints[0].Position;
                RemoveWaypointAt(0);
                continue;
            }

            if (!IsRequiredWaypoint(0) && HasPassedCurrentWaypoint(_lastWaypointAnchor, _waypoints[0].Position, current))
            {
                Log($"Waypoint passed by projection. Waypoint={_waypoints[0].Position}, Position={current}");
                _lastWaypointAnchor = _waypoints[0].Position;
                RemoveWaypointAt(0);
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
            return _waypoints[0].Position;

        if (_waypoints.Count <= 1)
            return _waypoints[0].Position;

        Vector3 toCurrentWaypoint = _waypoints[0].Position - current;
        toCurrentWaypoint.y = 0f;
        float distance = toCurrentWaypoint.magnitude;

        if (distance >= cornerLookAheadDistance)
            return _waypoints[0].Position;

        float blend = 1f - Mathf.Clamp01(distance / Mathf.Max(0.0001f, cornerLookAheadDistance));
        return Vector3.Lerp(_waypoints[0].Position, _waypoints[1].Position, blend);
    }

    private float GetCurrentWaypointReachDistance()
    {
        if (_waypoints.Count <= 1)
            return stopDistance;

        return Mathf.Max(stopDistance, waypointAdvanceDistance);
    }

    private void BuildWaypoints(Vector3 worldTarget)
    {
        ClearWaypoints();
        _lastWaypointAnchor = transform.position;

        if (navigation == null)
        {
            AddWaypoint(worldTarget, false);
            return;
        }

        Vector3 startPosition = transform.position;
        MapNavigationSpace startSpace = ResolveStartSpace(out startPosition);
        if (startSpace.Kind == MapNavigationSpaceKind.None)
        {
            Log($"Blocked target because start space is null. Agent={transform.position}, Target={worldTarget}");
            return;
        }

        MapNavigationSpace targetSpace = ResolveTargetSpace(worldTarget, startSpace, out Vector3 resolvedTarget);
        if (targetSpace.Kind == MapNavigationSpaceKind.None)
        {
            Log($"Blocked target because target space is null. Start={MapNavigationPathBuilder.DescribeSpace(startSpace)}, Target={worldTarget}");
            return;
        }

        MapNavigationPathBuildRequest request = CreatePathBuildRequest(startPosition, resolvedTarget);

        if (!BuildPath(startSpace, targetSpace, request))
        {
            return;
        }

        _waypoints.AddRange(_pathBuildResult.Waypoints);

        if (_pathBuildResult.UsedCrossLayerTransition && _pathBuildResult.SelectedPath != null)
            Log($"R->R transition path selected. {DescribePath(_pathBuildResult.SelectedPath)}");

        Log($"{_pathBuildResult.DebugSummary} {DescribeWaypoints()}");
    }

    private MapNavigationPathBuildRequest CreatePathBuildRequest(Vector3 startPosition, Vector3 resolvedTarget)
    {
        MapNavigationPathBuildSettings settings = new(
            agentRadius,
            stopDistance,
            useRegionPathfinding);

        return new MapNavigationPathBuildRequest(
            startPosition,
            resolvedTarget,
            settings);
    }

    private bool BuildPath(
        MapNavigationSpace startSpace,
        MapNavigationSpace targetSpace,
        MapNavigationPathBuildRequest request)
    {
        if (useBlobDataContextQueries)
        {
            MapNavigationPathSpace startPathSpace = ToPathSpace(startSpace);
            MapNavigationPathSpace targetPathSpace = ToPathSpace(targetSpace);
            return MapNavigationPathBuilder.Build(
                GetBlobPathContext(),
                startPathSpace,
                targetPathSpace,
                request,
                transform.position,
                this,
                _pathBuildResult);
        }

        if (useBuildDataContextQueries)
        {
            MapNavigationPathSpace startPathSpace = ToPathSpace(startSpace);
            MapNavigationPathSpace targetPathSpace = ToPathSpace(targetSpace);
            return MapNavigationPathBuilder.Build(
                GetBuildDataPathContext(),
                startPathSpace,
                targetPathSpace,
                request,
                transform.position,
                this,
                _pathBuildResult);
        }

        return MapNavigationPathBuilder.Build(
            navigation.QueryContext,
            startSpace,
            targetSpace,
            request,
            transform.position,
            this,
            _pathBuildResult);
    }

    private MapNavigationBuildDataPathContext GetBuildDataPathContext()
    {
        MapNavigationBuildData buildData = navigation.BuildData;
        Matrix4x4 localToWorld = navigation.transform.localToWorldMatrix;
        Matrix4x4 worldToLocal = navigation.transform.worldToLocalMatrix;
        if (_cachedBuildPathData != buildData
            || _cachedBuildPathLocalToWorld != localToWorld
            || _cachedBuildPathWorldToLocal != worldToLocal)
        {
            _cachedBuildPathData = buildData;
            _cachedBuildPathLocalToWorld = localToWorld;
            _cachedBuildPathWorldToLocal = worldToLocal;
            _cachedBuildPathContext = new MapNavigationBuildDataPathContext(navigation.BuildDataContext);
        }

        return _cachedBuildPathContext;
    }

    private MapNavigationBlobPathContext GetBlobPathContext()
    {
        MapNavigationBlobDataContext blobContext = navigation.BlobDataContext;
        Unity.Entities.BlobAssetReference<MapNavigationBlob> blob = blobContext.Blob;
        Matrix4x4 localToWorld = navigation.transform.localToWorldMatrix;
        Matrix4x4 worldToLocal = navigation.transform.worldToLocalMatrix;
        if (!_cachedBlobPathData.Equals(blob)
            || _cachedBlobPathLocalToWorld != localToWorld
            || _cachedBlobPathWorldToLocal != worldToLocal)
        {
            _cachedBlobPathData = blob;
            _cachedBlobPathLocalToWorld = localToWorld;
            _cachedBlobPathWorldToLocal = worldToLocal;
            _cachedBlobPathContext = new MapNavigationBlobPathContext(blobContext);
        }

        return _cachedBlobPathContext;
    }

    private static MapNavigationPathSpace ToPathSpace(MapNavigationSpace space)
    {
        return space.Kind switch
        {
            MapNavigationSpaceKind.Region => space.Region != null ? MapNavigationPathSpace.Region(space.Region.Id) : default,
            MapNavigationSpaceKind.Transition => space.Transition != null ? MapNavigationPathSpace.Transition(space.Transition.Id) : default,
            _ => default
        };
    }

    private MapNavigationSpace ResolveStartSpace(out Vector3 startPosition)
    {
        startPosition = transform.position;

        MapNavigationSpace space = ResolveNavigationSpace(
            startPosition,
            preferCurrentRegion: true,
            preferCurrentTransition: true,
            transitionFirst: false,
            out _);
        if (space.Kind != MapNavigationSpaceKind.None)
            return space;

        MapNavRegion region;
        if (!TryAddOutsideRecoveryWaypoint(out Vector3 recoveryPosition, out region))
            return default;

        startPosition = recoveryPosition;
        Log($"Continuing from recovered {region.DisplayName}. Agent={transform.position}, Recovery={recoveryPosition}");
        return new MapNavigationSpace(region);
    }

    private MapNavigationSpace ResolveTargetSpace(Vector3 worldTarget, MapNavigationSpace startSpace, out Vector3 resolvedTarget)
    {
        MapNavigationQueryContext context = navigation.QueryContext;

        MapNavigationSpace targetSpace = ResolveNavigationSpace(
            worldTarget,
            preferCurrentRegion: false,
            preferCurrentTransition: false,
            transitionFirst: true,
            out resolvedTarget);
        if (targetSpace.Kind != MapNavigationSpaceKind.None)
            return targetSpace;

        MapNavRegion currentRegion = startSpace.Kind == MapNavigationSpaceKind.Region ? startSpace.Region : null;
        resolvedTarget = currentRegion != null ? ResolveNavigationTarget(worldTarget, currentRegion) : worldTarget;

        targetSpace = ResolveNavigationSpace(
            resolvedTarget,
            preferCurrentRegion: false,
            preferCurrentTransition: false,
            transitionFirst: true,
            out resolvedTarget);
        if (targetSpace.Kind != MapNavigationSpaceKind.None)
            return targetSpace;

        if (TryProjectToClosestNavigationSpace(resolvedTarget, out Vector3 projected, out string spaceName, out float distance))
        {
            resolvedTarget = projected;

            targetSpace = ResolveNavigationSpace(
                resolvedTarget,
                preferCurrentRegion: false,
                preferCurrentTransition: false,
                transitionFirst: true,
                out resolvedTarget);
            if (targetSpace.Kind != MapNavigationSpaceKind.None)
            {
                Log($"Reprojected blocked target to {spaceName}. Projected={projected}, Distance={distance:0.###}");
                return targetSpace;
            }
        }

        return default;
    }

    private MapNavigationSpace ResolveNavigationSpace(
        Vector3 worldPosition,
        bool preferCurrentRegion,
        bool preferCurrentTransition,
        bool transitionFirst,
        out Vector3 resolvedPosition)
    {
        resolvedPosition = worldPosition;

        if (transitionFirst)
        {
            MapNavTransition transition = FindContainingTransition(worldPosition, preferCurrentTransition);
            if (transition != null)
                return new MapNavigationSpace(transition);

            MapNavRegion region = FindContainingRegionOnly(worldPosition, preferCurrentRegion);
            return region != null ? new MapNavigationSpace(region) : default;
        }

        MapNavRegion regionFirst = FindContainingRegionOnly(worldPosition, preferCurrentRegion);
        if (regionFirst != null)
            return new MapNavigationSpace(regionFirst);

        MapNavTransition transitionFallback = FindContainingTransition(worldPosition, preferCurrentTransition);
        if (transitionFallback != null)
            return new MapNavigationSpace(transitionFallback);

        return default;
    }

    private MapNavRegion FindContainingRegionOnly(Vector3 worldPosition, bool preferCurrentRegion)
    {
        if (navigation == null)
            return null;

        MapNavigationQueryContext context = navigation.QueryContext;
        if (!context.IsValid)
            return null;

        Vector3 local = context.ToLocal3D(worldPosition);
        Vector2 localPoint = new(local.x, local.z);
        MapNavRegion preferredRegion = preferCurrentRegion ? context.FindRegion(_currentRegionId) : null;
        if (preferredRegion != null
            && context.ContainsRegion(preferredRegion, localPoint, boundaryTolerance)
            && Mathf.Abs(local.y - context.GetRegionHeight(preferredRegion, localPoint)) <= RegionSelectionHeightTolerance)
        {
            return preferredRegion;
        }

        MapNavRegion bestRegion = null;
        float bestHeightDelta = float.PositiveInfinity;
        for (int i = 0; i < context.RegionCount; i++)
        {
            MapNavRegion candidate = context.GetRegionAt(i);
            if (candidate == null || !context.ContainsRegion(candidate, localPoint, boundaryTolerance))
                continue;

            float heightDelta = Mathf.Abs(local.y - context.GetRegionHeight(candidate, localPoint));
            if (heightDelta > RegionSelectionHeightTolerance || heightDelta >= bestHeightDelta)
                continue;

            bestHeightDelta = heightDelta;
            bestRegion = candidate;
        }

        return bestRegion;
    }

    private MapNavTransition FindContainingTransition(Vector3 worldPosition, bool preferCurrentTransition)
    {
        if (navigation == null)
            return null;

        MapNavigationQueryContext context = navigation.QueryContext;
        if (!context.IsValid)
            return null;

        if (preferCurrentTransition)
        {
            MapNavTransition preferredTransition = context.FindTransition(_currentTransitionId);
            if (preferredTransition != null
                && preferredTransition.Enabled
                && context.ContainsTransition(preferredTransition, context.ToLocal2D(worldPosition), boundaryTolerance))
            {
                return preferredTransition;
            }
        }

        return MapNavigationQuery.TryFindContainingTransition(context, worldPosition, boundaryTolerance, out MapNavTransition transition)
            ? transition
            : null;
    }

    private void AddTransitionInternalWaypoint(
        MapNavigationQueryContext context,
        MapNavTransition transition,
        Vector3 transitionTarget,
        IList<MapNavWaypoint> waypoints)
    {
        AddWaypointIfSeparated(waypoints, transitionTarget);
    }

    public void AddRegionWaypoint(int regionId, Vector3 waypoint)
    {
        MapNavRegion region = navigation.FindRegion(regionId);
        Vector3 from = _pathBuildResult.MutableWaypoints.Count > 0
            ? _pathBuildResult.MutableWaypoints[^1].Position
            : transform.position;
        if (region != null && TryAddInternalRegionPath(region, from, waypoint, _pathBuildResult.MutableWaypoints))
            return;

        AddWaypointIfSeparated(_pathBuildResult.MutableWaypoints, waypoint);
    }

    public void AddTransitionInternalWaypoint(int transitionId, Vector3 targetPosition)
    {
        if (navigation == null)
        {
            AddWaypointIfSeparated(_pathBuildResult.MutableWaypoints, targetPosition);
            return;
        }

        MapNavTransition transition = navigation.QueryContext.FindTransition(transitionId);
        AddTransitionInternalWaypoint(navigation.QueryContext, transition, targetPosition, _pathBuildResult.MutableWaypoints);
    }

    public bool ResolveRegionWaypoint(int regionId, Vector3 waypoint, out Vector3 resolved)
    {
        resolved = waypoint;
        if (navigation == null)
            return false;

        MapNavRegion region = navigation.FindRegion(regionId);
        if (region == null)
            return false;

        MapNavigationQueryContext context = navigation.QueryContext;
        Vector2 localPoint = context.ToLocal2D(waypoint);
        if (!context.ContainsRegion(region, localPoint, boundaryTolerance))
            localPoint = context.GetClosestPointOnRegion(region, localPoint);

        resolved = context.ToWorld(region, localPoint);
        if (TryProjectOutOfObstacles(region, resolved, transform.position, out Vector3 obstacleResolved, out _, out _))
            resolved = obstacleResolved;

        Vector2 resolvedLocal = context.ToLocal2D(resolved);
        return context.ContainsRegion(region, resolvedLocal, boundaryTolerance)
            && !MapNavigationQuery.IsInsideRegionObstacle(context, region, resolved);
    }

    private bool TryAddInternalRegionPath(MapNavRegion region, Vector3 from, Vector3 to)
    {
        return TryAddInternalRegionPath(region, from, to, _waypoints);
    }

    private bool TryAddInternalRegionPath(MapNavRegion region, Vector3 from, Vector3 to, IList<MapNavWaypoint> waypoints)
    {
        if (region == null || FindInternalRegionPath(region.Id, from, to, out List<Vector3> internalPath) != MapNavigationQuery.InternalPathResult.PathFound)
            return false;

        for (int i = 0; i < internalPath.Count; i++)
            AddWaypointIfSeparated(waypoints, internalPath[i]);

        return true;
    }

    private MapNavigationQuery.InternalPathResult FindInternalRegionPath(
        int regionId,
        Vector3 from,
        Vector3 to,
        out List<Vector3> internalPath)
    {
        if (navigation == null)
        {
            internalPath = new List<Vector3>();
            return MapNavigationQuery.InternalPathResult.Failed;
        }

        if (useBlobDataContextQueries)
            return GetBlobPathContext().FindInternalRegionPath(regionId, from, to, agentRadius, out internalPath);

        if (useBuildDataContextQueries)
            return GetBuildDataPathContext().FindInternalRegionPath(regionId, from, to, agentRadius, out internalPath);

        MapNavigationQueryPathContext pathContext = new(navigation.QueryContext);
        return pathContext.FindInternalRegionPath(regionId, from, to, agentRadius, out internalPath);
    }

    private Vector3 ResolveNavigationTarget(Vector3 worldTarget, MapNavRegion currentRegion)
    {
        if (navigation == null)
            return worldTarget;

        MapNavRegion targetRegion = FindContainingRegion(worldTarget);
        if (targetRegion != null)
        {
            Vector3 obstacleResolvedTarget = ResolveTargetOutOfObstacles(targetRegion, worldTarget);
            if (obstacleResolvedTarget != worldTarget)
                return obstacleResolvedTarget;
        }

        return ResolveTargetToNavigationSpace(worldTarget, currentRegion);
    }

    private Vector3 ResolveTargetToNavigationSpace(Vector3 worldTarget, MapNavRegion currentRegion)
    {
        MapNavRegion targetRegion = FindContainingRegion(worldTarget);
        if (targetRegion != null)
            return worldTarget;

        if (!TryProjectToClosestNavigationSpace(worldTarget, out Vector3 projected, out string spaceName, out float distance))
            return worldTarget;

        Log($"Projected outside target to {spaceName}. Original={worldTarget}, Projected={projected}, Distance={distance:0.###}");
        return projected;
    }

    private Vector3 ResolveTargetOutOfObstacles(MapNavRegion targetRegion, Vector3 worldTarget)
    {
        if (!TryProjectOutOfObstacles(targetRegion, worldTarget, transform.position, out Vector3 projected, out string obstacleName, out float distance))
        {
            return worldTarget;
        }

        Log($"Projected target out of {targetRegion.DisplayName} {obstacleName}. Original={worldTarget}, Projected={projected}, Distance={distance:0.###}");
        return projected;
    }

    private bool TryProjectOutOfObstacles(
        MapNavRegion targetRegion,
        Vector3 worldTarget,
        Vector3 referenceWorldPosition,
        out Vector3 projected,
        out string obstacleName,
        out float distance)
    {
        if (navigation == null || targetRegion == null)
        {
            projected = worldTarget;
            obstacleName = "None";
            distance = 0f;
            return false;
        }

        if (useBlobDataContextQueries
            && navigation.BlobDataContext.TryFindRegion(targetRegion.Id, out MapNavRegionBlob blobRegion))
        {
            return MapNavigationQuery.TryProjectOutOfObstacles(
                navigation.BlobDataContext,
                blobRegion,
                worldTarget,
                referenceWorldPosition,
                agentRadius,
                out projected,
                out obstacleName,
                out distance);
        }

        if (useBuildDataContextQueries
            && navigation.BuildDataContext.TryFindRegion(targetRegion.Id, out MapNavRegionData regionData))
        {
            return MapNavigationQuery.TryProjectOutOfObstacles(
                navigation.BuildDataContext,
                regionData,
                worldTarget,
                referenceWorldPosition,
                agentRadius,
                out projected,
                out obstacleName,
                out distance);
        }

        return MapNavigationQuery.TryProjectOutOfObstacles(
            navigation.QueryContext,
            targetRegion,
            worldTarget,
            referenceWorldPosition,
            agentRadius,
            out projected,
            out obstacleName,
            out distance);
    }

    private bool TryAddOutsideRecoveryWaypoint(out Vector3 recoveryPosition, out MapNavRegion recoveryRegion)
    {
        recoveryPosition = transform.position;
        recoveryRegion = null;
        if (navigation == null || !TryProjectToClosestNavigationSpace(transform.position, out Vector3 projected, out string spaceName, out float distance))
            return false;

        AddWaypointIfSeparated(projected);
        recoveryPosition = projected;
        recoveryRegion = FindContainingRegion(projected, true);
        Log($"Added outside recovery waypoint to {spaceName}. Projected={projected}, Distance={distance:0.###}");
        return recoveryRegion != null;
    }

    private MapNavRegion FindContainingRegion(Vector3 worldPosition, bool preferCurrentRegion = false)
    {
        if (navigation == null)
            return null;

        int preferredRegionId = preferCurrentRegion ? _currentRegionId : -1;

        if (useBlobDataContextQueries
            && MapNavigationQuery.TryFindContainingRegion(
                navigation.BlobDataContext,
                worldPosition,
                boundaryTolerance,
                preferredRegionId,
                RegionSelectionHeightTolerance,
                out MapNavRegionBlob blobRegion))
        {
            return navigation.FindRegion(blobRegion.Id);
        }

        if (useBuildDataContextQueries
            && MapNavigationQuery.TryFindContainingRegion(
                navigation.BuildDataContext,
                worldPosition,
                boundaryTolerance,
                preferredRegionId,
                RegionSelectionHeightTolerance,
                out MapNavRegionData regionData))
        {
            return navigation.FindRegion(regionData.Id);
        }

        return MapNavigationQuery.FindContainingRegion(
            navigation.QueryContext,
            worldPosition,
            boundaryTolerance,
            preferredRegionId,
            RegionSelectionHeightTolerance);
    }

    private bool TryProjectToClosestNavigationSpace(Vector3 worldPosition, out Vector3 projected, out string spaceName, out float distance)
    {
        return TryProjectToClosestNavigationSpace(worldPosition, null, out projected, out spaceName, out distance);
    }

    private bool TryProjectToClosestNavigationSpace(Vector3 worldPosition, MapNavRegion requiredDirectRegion, out Vector3 projected, out string spaceName, out float distance)
    {
        if (navigation == null)
        {
            projected = worldPosition;
            spaceName = "No Navigation";
            distance = 0f;
            return false;
        }

        if (useBlobDataContextQueries)
            return TryProjectToClosestNavigationSpaceWithFilter(navigation.BlobDataContext, worldPosition, requiredDirectRegion, out projected, out spaceName, out distance);

        if (useBuildDataContextQueries)
            return TryProjectToClosestNavigationSpaceWithFilter(navigation.BuildDataContext, worldPosition, requiredDirectRegion, out projected, out spaceName, out distance);

        return TryProjectToClosestNavigationSpaceWithFilter(navigation.QueryContext, worldPosition, requiredDirectRegion, out projected, out spaceName, out distance);
    }

    private bool TryProjectToClosestNavigationSpaceWithFilter(MapNavigationQueryContext context, Vector3 worldPosition, MapNavRegion requiredDirectRegion, out Vector3 projected, out string spaceName, out float distance)
    {
        projected = worldPosition;
        spaceName = "Outside";
        distance = 0f;

        if (!MapNavigationQuery.TryProjectToClosestNavigationSpace(context, worldPosition, out Vector3 candidate, out string candidateName, out float candidateDistance))
            return false;

        if (requiredDirectRegion == null)
        {
            projected = candidate;
            spaceName = candidateName;
            distance = candidateDistance;
            return true;
        }

        MapNavRegion projectedRegion = MapNavigationQuery.FindContainingRegion(
            context,
            candidate,
            boundaryTolerance,
            requiredDirectRegion.Id,
            RegionSelectionHeightTolerance);
        if (projectedRegion == null || !MapNavigationPathBuilder.IsSameTraversalLayer(requiredDirectRegion, projectedRegion))
            return false;

        projected = candidate;
        spaceName = candidateName;
        distance = candidateDistance;
        return true;
    }

    private bool TryProjectToClosestNavigationSpaceWithFilter(MapNavigationBuildDataContext context, Vector3 worldPosition, MapNavRegion requiredDirectRegion, out Vector3 projected, out string spaceName, out float distance)
    {
        projected = worldPosition;
        spaceName = "Outside";
        distance = 0f;

        if (!MapNavigationQuery.TryProjectToClosestNavigationSpace(context, worldPosition, out Vector3 candidate, out string candidateName, out float candidateDistance))
            return false;

        if (requiredDirectRegion == null)
        {
            projected = candidate;
            spaceName = candidateName;
            distance = candidateDistance;
            return true;
        }

        if (!MapNavigationQuery.TryFindContainingRegion(
                context,
                candidate,
                boundaryTolerance,
                requiredDirectRegion.Id,
                RegionSelectionHeightTolerance,
                out MapNavRegionData projectedRegionData))
        {
            return false;
        }

        MapNavRegion projectedRegion = navigation.FindRegion(projectedRegionData.Id);
        if (!MapNavigationPathBuilder.IsSameTraversalLayer(requiredDirectRegion, projectedRegion))
            return false;

        projected = candidate;
        spaceName = candidateName;
        distance = candidateDistance;
        return true;
    }

    private bool TryProjectToClosestNavigationSpaceWithFilter(MapNavigationBlobDataContext context, Vector3 worldPosition, MapNavRegion requiredDirectRegion, out Vector3 projected, out string spaceName, out float distance)
    {
        projected = worldPosition;
        spaceName = "Outside";
        distance = 0f;

        if (!MapNavigationQuery.TryProjectToClosestNavigationSpace(context, worldPosition, out Vector3 candidate, out string candidateName, out float candidateDistance))
            return false;

        if (requiredDirectRegion == null)
        {
            projected = candidate;
            spaceName = candidateName;
            distance = candidateDistance;
            return true;
        }

        if (!MapNavigationQuery.TryFindContainingRegion(
                context,
                candidate,
                boundaryTolerance,
                requiredDirectRegion.Id,
                RegionSelectionHeightTolerance,
                out MapNavRegionBlob projectedRegionBlob))
        {
            return false;
        }

        MapNavRegion projectedRegion = navigation.FindRegion(projectedRegionBlob.Id);
        if (!MapNavigationPathBuilder.IsSameTraversalLayer(requiredDirectRegion, projectedRegion))
            return false;

        projected = candidate;
        spaceName = candidateName;
        distance = candidateDistance;
        return true;
    }

    private bool TryGetNavigationHeight(
        Vector3 worldPosition,
        float tolerance,
        int previousTransitionId,
        int previousRegionId,
        out float height,
        out string spaceName,
        out int transitionId,
        out int regionId)
    {
        if (navigation == null)
        {
            height = 0f;
            spaceName = "No Navigation";
            transitionId = -1;
            regionId = -1;
            return false;
        }

        if (useBlobDataContextQueries)
        {
            return MapNavigationQuery.TryGetNavigationHeight(
                navigation.BlobDataContext,
                worldPosition,
                tolerance,
                previousTransitionId,
                previousRegionId,
                out height,
                out spaceName,
                out transitionId,
                out regionId);
        }

        if (useBuildDataContextQueries)
        {
            return MapNavigationQuery.TryGetNavigationHeight(
                navigation.BuildDataContext,
                worldPosition,
                tolerance,
                previousTransitionId,
                previousRegionId,
                out height,
                out spaceName,
                out transitionId,
                out regionId);
        }

        return MapNavigationQuery.TryGetNavigationHeight(
            navigation.QueryContext,
            worldPosition,
            tolerance,
            previousTransitionId,
            previousRegionId,
            out height,
            out spaceName,
            out transitionId,
            out regionId);
    }

    private bool TryCacheNavigationHeightSample(Vector3 worldPosition)
    {
        if (!TryGetNavigationHeight(
                worldPosition,
                boundaryTolerance,
                _currentTransitionId,
                _currentRegionId,
                out float height,
                out string spaceName,
                out int transitionId,
                out int regionId))
        {
            _lastMoveNavigationSample.IsValid = false;
            return false;
        }

        _lastMoveNavigationSample = new NavigationHeightSample
        {
            IsValid = true,
            Frame = Time.frameCount,
            Position = worldPosition,
            Height = height,
            SpaceName = spaceName,
            TransitionId = transitionId,
            RegionId = regionId
        };
        return true;
    }

    private bool TryConsumeCachedNavigationHeightSample(
        Vector3 worldPosition,
        out float height,
        out string spaceName,
        out int transitionId,
        out int regionId)
    {
        if (_lastMoveNavigationSample.IsValid
            && _lastMoveNavigationSample.Frame == Time.frameCount
            && (_lastMoveNavigationSample.Position - worldPosition).sqrMagnitude <= 0.000001f)
        {
            height = _lastMoveNavigationSample.Height;
            spaceName = _lastMoveNavigationSample.SpaceName;
            transitionId = _lastMoveNavigationSample.TransitionId;
            regionId = _lastMoveNavigationSample.RegionId;
            _lastMoveNavigationSample.IsValid = false;
            return true;
        }

        height = 0f;
        spaceName = "Outside";
        transitionId = -1;
        regionId = -1;
        return false;
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
        AddWaypointIfSeparated(_waypoints, waypoint);
    }

    private void AddWaypoint(Vector3 waypoint, bool required)
    {
        _waypoints.Add(new MapNavWaypoint(waypoint, required));
    }

    private bool AddWaypointIfSeparated(IList<MapNavWaypoint> waypoints, Vector3 waypoint)
    {
        Vector3 previous = waypoints.Count > 0 ? waypoints[^1].Position : transform.position;
        Vector3 delta = waypoint - previous;
        delta.y = 0f;

        if (delta.sqrMagnitude <= stopDistance * stopDistance)
            return false;

        waypoints.Add(new MapNavWaypoint(waypoint, false));
        return true;
    }

    private void RemoveWaypointAt(int index)
    {
        _waypoints.RemoveAt(index);
    }

    private void ClearWaypoints()
    {
        _waypoints.Clear();
    }

    private bool IsRequiredWaypoint(int index)
    {
        return index >= 0 && index < _waypoints.Count && _waypoints[index].Required;
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
            builder.Append(_waypoints[i].Position.ToString("F2"));
            builder.Append("/");
            builder.Append(GetWaypointSpaceName(_waypoints[i].Position));
        }

        return builder.ToString();
    }

    private string GetWaypointSpaceName(Vector3 waypoint)
    {
        if (navigation == null)
            return "No Navigation";

        if (TryGetNavigationHeight(
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

        Vector3 currentPosition = transform.position;
        bool foundNavigationHeight = TryConsumeCachedNavigationHeightSample(
            currentPosition,
            out float navHeight,
            out string spaceName,
            out int transitionId,
            out int regionId);

        if (!foundNavigationHeight)
        {
            foundNavigationHeight = TryGetNavigationHeight(
                currentPosition,
                boundaryTolerance,
                _currentTransitionId,
                _currentRegionId,
                out navHeight,
                out spaceName,
                out transitionId,
                out regionId);
        }

        if (foundNavigationHeight)
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
                Gizmos.DrawWireSphere(_waypoints[i].Position, 0.14f);
                Gizmos.DrawLine(from, _waypoints[i].Position);
                from = _waypoints[i].Position;
            }
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.15f);
    }
}


