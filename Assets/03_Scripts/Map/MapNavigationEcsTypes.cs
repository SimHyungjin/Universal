using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public struct MapNavEcsAgent : IComponentData
{
    public float AgentRadius;
    public float StopDistance;
    public float MoveSpeed;
    public float WaypointAdvanceDistance;
    public float CornerLookAheadDistance;
    public float HeightOffset;
    public float BoundaryTolerance;
    public float TargetRepathDistance;
    public float StuckRepathDelay;
    public float StuckRepathCooldown;
    public float StuckProgressDistance;
    public byte UseRegionPathfinding;
    public byte ConstrainToNavigationSpaces;
}

public struct MapNavEcsPathBuildBudget : IComponentData
{
    public int MaxPathsPerFrame;
}

public struct MapNavEcsMotionState : IComponentData
{
    public byte IsMoving;
    public int WaypointIndex;
    public float CurrentSpeed;
    public float StuckTimer;
    public float RepathCooldownRemaining;
    public float LastDistanceToWaypoint;
    public float3 LastWaypointAnchor;
    public float3 Velocity;
}

public struct MapNavEcsPathRequest : IComponentData
{
    public byte Pending;
    public float3 StartPosition;
    public float3 ActualStartPosition;
    public float3 TargetPosition;
    public MapNavigationPathSpace StartSpace;
    public MapNavigationPathSpace TargetSpace;
}

public struct MapNavEcsTarget : IComponentData
{
    public byte Dirty;
    public float3 Position;
    public float3 AcceptedPosition;
}

public struct MapNavEcsTargetCommand : IComponentData
{
    public float3 Position;
}

public struct MapNavEcsPathStatus : IComponentData
{
    public byte HasPath;
    public byte Waiting;
    public byte Failed;
    public byte UsedCrossLayerTransition;
    public int PathKind;
}

public struct MapNavEcsWaypoint : IBufferElementData
{
    public float3 Position;
    public byte Required;
}

public readonly struct MapNavigationEcsBlobPathAssembler : IMapNavigationPathAssembler
{
    private readonly MapNavigationBlobPathContext _context;
    private readonly MapNavigationPathBuildResult _result;
    private readonly float _agentRadius;
    private readonly float _stopDistance;
    private readonly float _boundaryTolerance;
    private readonly Vector3 _fallbackStart;

    public MapNavigationEcsBlobPathAssembler(
        MapNavigationBlobPathContext context,
        MapNavigationPathBuildResult result,
        float agentRadius,
        float stopDistance,
        float boundaryTolerance,
        Vector3 fallbackStart)
    {
        _context = context;
        _result = result;
        _agentRadius = agentRadius;
        _stopDistance = stopDistance;
        _boundaryTolerance = boundaryTolerance;
        _fallbackStart = fallbackStart;
    }

    public bool ResolveRegionWaypoint(int regionId, Vector3 waypoint, out Vector3 resolved)
    {
        resolved = waypoint;
        if (!_context.TryGetRegionInfo(regionId, out _))
            return false;

        MapNavigationBlobDataContext dataContext = _context.DataContext;
        if (!dataContext.TryFindRegion(regionId, out MapNavRegionBlob region))
            return false;

        Vector2 localPoint = dataContext.ToLocal2D(waypoint);
        if (!dataContext.ContainsRegion(region, localPoint, _boundaryTolerance))
            localPoint = dataContext.GetClosestPointOnRegion(region, localPoint);

        resolved = dataContext.ToWorld(region, localPoint);
        if (MapNavigationQuery.TryProjectOutOfObstacles(
                dataContext,
                region,
                resolved,
                _fallbackStart,
                _agentRadius,
                out Vector3 obstacleResolved,
                out _,
                out _))
        {
            resolved = obstacleResolved;
        }

        Vector2 resolvedLocal = dataContext.ToLocal2D(resolved);
        return dataContext.ContainsRegion(region, resolvedLocal, _boundaryTolerance)
            && !MapNavigationQuery.IsInsideRegionObstacle(dataContext, region, resolved);
    }

    public void AddRegionWaypoint(int regionId, Vector3 waypoint)
    {
        Vector3 from = _result.MutableWaypoints.Count > 0
            ? _result.MutableWaypoints[^1].Position
            : _fallbackStart;

        if (_context.FindInternalRegionPath(regionId, from, waypoint, _agentRadius, out System.Collections.Generic.List<Vector3> internalPath) == MapNavigationQuery.InternalPathResult.PathFound)
        {
            for (int i = 0; i < internalPath.Count; i++)
                AddWaypointIfSeparated(internalPath[i]);

            return;
        }

        AddWaypointIfSeparated(waypoint);
    }

    public void AddTransitionInternalWaypoint(int transitionId, Vector3 targetPosition)
    {
        AddWaypointIfSeparated(targetPosition);
    }

    private void AddWaypointIfSeparated(Vector3 waypoint)
    {
        Vector3 previous = _result.MutableWaypoints.Count > 0
            ? _result.MutableWaypoints[^1].Position
            : _fallbackStart;
        Vector3 delta = waypoint - previous;
        delta.y = 0f;
        if (delta.sqrMagnitude <= _stopDistance * _stopDistance)
            return;

        _result.MutableWaypoints.Add(new MapNavWaypoint(waypoint, false));
    }
}

public static class MapNavigationEcsConversion
{
    public static Matrix4x4 ToMatrix4x4(float4x4 matrix)
    {
        return new Matrix4x4(
            new Vector4(matrix.c0.x, matrix.c0.y, matrix.c0.z, matrix.c0.w),
            new Vector4(matrix.c1.x, matrix.c1.y, matrix.c1.z, matrix.c1.w),
            new Vector4(matrix.c2.x, matrix.c2.y, matrix.c2.z, matrix.c2.w),
            new Vector4(matrix.c3.x, matrix.c3.y, matrix.c3.z, matrix.c3.w));
    }
}
