using System.Collections.Generic;
using UnityEngine;

public enum MapNavigationSpaceKind
{
    None,
    Region,
    Transition
}

public readonly struct MapNavigationSpace
{
    public readonly MapNavigationSpaceKind Kind;
    public readonly MapNavRegion Region;
    public readonly MapNavTransition Transition;

    public MapNavigationSpace(MapNavRegion region)
    {
        Kind = region != null ? MapNavigationSpaceKind.Region : MapNavigationSpaceKind.None;
        Region = region;
        Transition = null;
    }

    public MapNavigationSpace(MapNavTransition transition)
    {
        Kind = transition != null ? MapNavigationSpaceKind.Transition : MapNavigationSpaceKind.None;
        Region = null;
        Transition = transition;
    }
}

public readonly struct MapNavigationPathSpace
{
    public readonly MapNavigationSpaceKind Kind;
    public readonly int RegionId;
    public readonly int TransitionId;

    public MapNavigationPathSpace(MapNavigationSpaceKind kind, int regionId, int transitionId)
    {
        Kind = kind;
        RegionId = regionId;
        TransitionId = transitionId;
    }

    public static MapNavigationPathSpace Region(int regionId)
    {
        return new MapNavigationPathSpace(MapNavigationSpaceKind.Region, regionId, -1);
    }

    public static MapNavigationPathSpace Transition(int transitionId)
    {
        return new MapNavigationPathSpace(MapNavigationSpaceKind.Transition, -1, transitionId);
    }
}

public readonly struct MapNavWaypoint
{
    public readonly Vector3 Position;
    public readonly bool Required;

    public MapNavWaypoint(Vector3 position, bool required)
    {
        Position = position;
        Required = required;
    }
}

public readonly struct MapNavigationPathBuildSettings
{
    public readonly float AgentRadius;
    public readonly float StopDistance;
    public readonly bool UseRegionPathfinding;

    public MapNavigationPathBuildSettings(
        float agentRadius,
        float stopDistance,
        bool useRegionPathfinding)
    {
        AgentRadius = agentRadius;
        StopDistance = stopDistance;
        UseRegionPathfinding = useRegionPathfinding;
    }
}

public readonly struct MapNavigationPathBuildRequest
{
    public readonly Vector3 StartPosition;
    public readonly Vector3 TargetPosition;
    public readonly MapNavigationPathBuildSettings Settings;

    public MapNavigationPathBuildRequest(
        Vector3 startPosition,
        Vector3 targetPosition,
        MapNavigationPathBuildSettings settings)
    {
        StartPosition = startPosition;
        TargetPosition = targetPosition;
        Settings = settings;
    }
}

public sealed class MapNavigationPathBuildResult
{
    private readonly List<MapNavWaypoint> _waypoints = new();

    public IReadOnlyList<MapNavWaypoint> Waypoints => _waypoints;
    internal IList<MapNavWaypoint> MutableWaypoints => _waypoints;
    public Vector3 ResolvedTarget { get; private set; }
    public string PathKind { get; private set; }
    public bool UsedCrossLayerTransition { get; private set; }
    public IReadOnlyList<MapNavigationQuery.PathStep> SelectedPath { get; private set; }
    public string DebugSummary { get; private set; }
    public bool Success => _waypoints.Count > 0;

    public void Clear()
    {
        _waypoints.Clear();
        ResolvedTarget = default;
        PathKind = null;
        UsedCrossLayerTransition = false;
        SelectedPath = null;
        DebugSummary = null;
    }

    public void AddWaypoint(Vector3 position, bool required)
    {
        _waypoints.Add(new MapNavWaypoint(position, required));
        ResolvedTarget = position;
    }

    public void RefreshResolvedTarget()
    {
        ResolvedTarget = _waypoints.Count > 0 ? _waypoints[^1].Position : default;
    }

    public void SetDebugSummary(string summary)
    {
        DebugSummary = summary;
    }

    public void SetPathMetadata(
        string pathKind,
        bool usedCrossLayerTransition,
        IReadOnlyList<MapNavigationQuery.PathStep> selectedPath)
    {
        PathKind = pathKind;
        UsedCrossLayerTransition = usedCrossLayerTransition;
        SelectedPath = selectedPath;
    }
}

public interface IMapNavigationPathAssembler
{
    bool ResolveRegionWaypoint(int regionId, Vector3 waypoint, out Vector3 resolved);
    void AddRegionWaypoint(int regionId, Vector3 waypoint);
    void AddTransitionInternalWaypoint(int transitionId, Vector3 targetPosition);
}
