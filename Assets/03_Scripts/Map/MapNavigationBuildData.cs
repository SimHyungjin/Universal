using System.Collections.Generic;
using UnityEngine;

public sealed class MapNavigationBuildData
{
    public readonly MapNavRegionData[] Regions;
    public readonly MapNavTransitionData[] Transitions;
    public readonly MapNavRegionConnectionData[] Connections;
    public readonly MapNavObstacleData[] Obstacles;
    public readonly Vector2[] Points;

    public MapNavigationBuildData(
        MapNavRegionData[] regions,
        MapNavTransitionData[] transitions,
        MapNavRegionConnectionData[] connections,
        MapNavObstacleData[] obstacles,
        Vector2[] points)
    {
        Regions = regions ?? System.Array.Empty<MapNavRegionData>();
        Transitions = transitions ?? System.Array.Empty<MapNavTransitionData>();
        Connections = connections ?? System.Array.Empty<MapNavRegionConnectionData>();
        Obstacles = obstacles ?? System.Array.Empty<MapNavObstacleData>();
        Points = points ?? System.Array.Empty<Vector2>();
    }

    public static MapNavigationBuildData FromAuthoring(MapNavigationAuthoring authoring)
    {
        return authoring != null
            ? Build(authoring.Regions, authoring.Transitions)
            : Empty();
    }

    public static MapNavigationBuildData Build(
        IReadOnlyList<MapNavRegion> sourceRegions,
        IReadOnlyList<MapNavTransition> sourceTransitions)
    {
        List<MapNavRegionData> regions = new();
        List<MapNavTransitionData> transitions = new();
        List<MapNavObstacleData> obstacles = new();
        List<Vector2> points = new();

        if (sourceRegions != null)
            AppendRegions(sourceRegions, regions, obstacles, points);

        if (sourceTransitions != null)
            AppendTransitions(sourceTransitions, transitions, points);

        regions.Sort((left, right) => left.Id.CompareTo(right.Id));
        transitions.Sort((left, right) => left.Id.CompareTo(right.Id));
        MapNavRegionConnectionData[] connections = BuildConnections(regions, transitions, points);
        ApplyConnectionRanges(regions, connections);

        return new MapNavigationBuildData(regions.ToArray(), transitions.ToArray(), connections, obstacles.ToArray(), points.ToArray());
    }

    public static MapNavigationBuildData Empty()
    {
        return new MapNavigationBuildData(null, null, null, null, null);
    }

    public bool TryFindRegionIndex(int regionId, out int index)
    {
        return TryFindRegionIndexById(Regions, regionId, out index);
    }

    public bool TryFindTransitionIndex(int transitionId, out int index)
    {
        return TryFindTransitionIndexById(Transitions, transitionId, out index);
    }

    public bool TryGetRegion(int regionId, out MapNavRegionData region)
    {
        if (TryFindRegionIndex(regionId, out int index))
        {
            region = Regions[index];
            return true;
        }

        region = default;
        return false;
    }

    public bool TryGetTransition(int transitionId, out MapNavTransitionData transition)
    {
        if (TryFindTransitionIndex(transitionId, out int index))
        {
            transition = Transitions[index];
            return true;
        }

        transition = default;
        return false;
    }

    public IReadOnlyList<Vector2> GetRegionPoints(MapNavRegionData region)
    {
        return GetPointRange(region.PointStart, region.PointCount);
    }

    public IReadOnlyList<Vector2> GetTransitionPoints(MapNavTransitionData transition)
    {
        return GetPointRange(transition.PointStart, transition.PointCount);
    }

    public IReadOnlyList<Vector2> GetObstaclePoints(MapNavObstacleData obstacle)
    {
        return GetPointRange(obstacle.PointStart, obstacle.PointCount);
    }

    public IReadOnlyList<MapNavObstacleData> GetRegionObstacles(MapNavRegionData region)
    {
        return GetObstacleRange(region.ObstacleStart, region.ObstacleCount);
    }

    public IReadOnlyList<MapNavRegionConnectionData> GetRegionConnections(MapNavRegionData region)
    {
        return GetConnectionRange(region.ConnectionStart, region.ConnectionCount);
    }

    private System.ArraySegment<Vector2> GetPointRange(int start, int count)
    {
        if (!IsValidRange(start, count, Points.Length))
            return new System.ArraySegment<Vector2>(System.Array.Empty<Vector2>());

        return new System.ArraySegment<Vector2>(Points, start, count);
    }

    private System.ArraySegment<MapNavObstacleData> GetObstacleRange(int start, int count)
    {
        if (!IsValidRange(start, count, Obstacles.Length))
            return new System.ArraySegment<MapNavObstacleData>(System.Array.Empty<MapNavObstacleData>());

        return new System.ArraySegment<MapNavObstacleData>(Obstacles, start, count);
    }

    private System.ArraySegment<MapNavRegionConnectionData> GetConnectionRange(int start, int count)
    {
        if (!IsValidRange(start, count, Connections.Length))
            return new System.ArraySegment<MapNavRegionConnectionData>(System.Array.Empty<MapNavRegionConnectionData>());

        return new System.ArraySegment<MapNavRegionConnectionData>(Connections, start, count);
    }

    private static void AppendRegions(
        IReadOnlyList<MapNavRegion> sourceRegions,
        List<MapNavRegionData> regions,
        List<MapNavObstacleData> obstacles,
        List<Vector2> points)
    {
        for (int i = 0; i < sourceRegions.Count; i++)
        {
            MapNavRegion region = sourceRegions[i];
            if (region == null)
                continue;

            region.RecalculateBounds();
            int pointStart = AppendPoints(region.Points, points);
            int obstacleStart = obstacles.Count;
            int obstacleCount = AppendObstacles(region.Id, region.Obstacles, obstacles, points);

            regions.Add(new MapNavRegionData(
                region.Id,
                region.NavLayerId,
                region.Height,
                Mathf.Max(0f, region.Cost),
                pointStart,
                GetCount(region.Points),
                obstacleStart,
                obstacleCount,
                0,
                0,
                region.BoundsMin,
                region.BoundsMax,
                region.HasBounds));
        }
    }

    private static void AppendTransitions(
        IReadOnlyList<MapNavTransition> sourceTransitions,
        List<MapNavTransitionData> transitions,
        List<Vector2> points)
    {
        for (int i = 0; i < sourceTransitions.Count; i++)
        {
            MapNavTransition transition = sourceTransitions[i];
            if (transition == null)
                continue;

            transition.RecalculateBounds();
            int pointStart = AppendPoints(transition.Points, points);
            transitions.Add(new MapNavTransitionData(
                transition.Id,
                transition.FromRegionId,
                transition.ToRegionId,
                transition.Type,
                transition.FromHeight,
                transition.ToHeight,
                transition.UpDirection,
                Mathf.Max(0f, transition.Cost),
                Mathf.Max(0f, transition.MinRadius),
                transition.CanStopInside,
                transition.CanFightInside,
                transition.Bidirectional,
                transition.Enabled,
                pointStart,
                GetCount(transition.Points),
                transition.BoundsMin,
                transition.BoundsMax,
                transition.HasBounds));
        }
    }

    private static int AppendObstacles(
        int regionId,
        IReadOnlyList<MapNavObstacle> sourceObstacles,
        List<MapNavObstacleData> obstacles,
        List<Vector2> points)
    {
        if (sourceObstacles == null)
            return 0;

        int added = 0;
        for (int i = 0; i < sourceObstacles.Count; i++)
        {
            MapNavObstacle obstacle = sourceObstacles[i];
            if (obstacle == null)
                continue;

            obstacle.RecalculateBounds();
            int pointStart = AppendPoints(obstacle.Points, points);
            obstacles.Add(new MapNavObstacleData(
                regionId,
                pointStart,
                GetCount(obstacle.Points),
                Mathf.Max(0f, obstacle.CornerPadding),
                obstacle.BoundsMin,
                obstacle.BoundsMax,
                obstacle.HasBounds));
            added++;
        }

        return added;
    }

    private static int AppendPoints(IReadOnlyList<Vector2> sourcePoints, List<Vector2> targetPoints)
    {
        int start = targetPoints.Count;
        if (sourcePoints == null)
            return start;

        for (int i = 0; i < sourcePoints.Count; i++)
            targetPoints.Add(sourcePoints[i]);

        return start;
    }

    private static int GetCount<T>(IReadOnlyList<T> items)
    {
        return items?.Count ?? 0;
    }

    private static MapNavRegionConnectionData[] BuildConnections(
        List<MapNavRegionData> regions,
        List<MapNavTransitionData> transitions,
        List<Vector2> points)
    {
        Vector2[] pointArray = points.ToArray();
        Dictionary<int, List<MapNavRegionConnectionData>> byRegionId = new();
        for (int i = 0; i < regions.Count; i++)
            byRegionId[regions[i].Id] = new List<MapNavRegionConnectionData>();

        for (int i = 0; i < transitions.Count; i++)
        {
            MapNavTransitionData transition = transitions[i];
            if (!transition.Enabled)
                continue;

            if (byRegionId.TryGetValue(transition.FromRegionId, out List<MapNavRegionConnectionData> fromConnections))
                fromConnections.Add(MapNavRegionConnectionData.Transition(transition.Id, transition.FromRegionId, transition.ToRegionId, true, transition.Cost));

            if (transition.Bidirectional && byRegionId.TryGetValue(transition.ToRegionId, out List<MapNavRegionConnectionData> toConnections))
                toConnections.Add(MapNavRegionConnectionData.Transition(transition.Id, transition.ToRegionId, transition.FromRegionId, false, transition.Cost));
        }

        for (int a = 0; a < regions.Count; a++)
        {
            MapNavRegionData regionA = regions[a];
            if (!CanLink(regionA))
                continue;

            for (int b = a + 1; b < regions.Count; b++)
            {
                MapNavRegionData regionB = regions[b];
                if (!CanLink(regionB)
                    || !MapNavigationRegionLinkUtility.CanLink(regionA.NavLayerId, regionA.Height, regionB.NavLayerId, regionB.Height))
                {
                    continue;
                }

                if (!MapNavigationRegionLinkUtility.TryFindSharedPortal(
                        new System.ArraySegment<Vector2>(pointArray, regionA.PointStart, regionA.PointCount),
                        new System.ArraySegment<Vector2>(pointArray, regionB.PointStart, regionB.PointCount),
                        out Vector2 portalA,
                        out Vector2 portalB))
                {
                    continue;
                }

                float cost = Mathf.Max(0f, (regionA.Cost + regionB.Cost) * 0.5f);
                byRegionId[regionA.Id].Add(MapNavRegionConnectionData.Region(regionA.Id, regionB.Id, portalA, portalB, cost));
                byRegionId[regionB.Id].Add(MapNavRegionConnectionData.Region(regionB.Id, regionA.Id, portalA, portalB, cost));
            }
        }

        List<MapNavRegionConnectionData> connections = new();
        for (int i = 0; i < regions.Count; i++)
            connections.AddRange(byRegionId[regions[i].Id]);

        return connections.ToArray();
    }

    private static void ApplyConnectionRanges(List<MapNavRegionData> regions, MapNavRegionConnectionData[] connections)
    {
        int cursor = 0;
        for (int i = 0; i < regions.Count; i++)
        {
            int count = 0;
            while (cursor + count < connections.Length && connections[cursor + count].FromRegionId == regions[i].Id)
                count++;

            regions[i] = regions[i].WithConnectionRange(cursor, count);
            cursor += count;
        }
    }

    private static bool CanLink(MapNavRegionData region)
    {
        return region.PointCount >= 3;
    }

    private static bool IsValidRange(int start, int count, int length)
    {
        return start >= 0 && count >= 0 && start <= length && count <= length - start;
    }

    private static bool TryFindRegionIndexById(MapNavRegionData[] regions, int regionId, out int index)
    {
        int min = 0;
        int max = regions.Length - 1;
        while (min <= max)
        {
            int midpoint = min + ((max - min) / 2);
            int id = regions[midpoint].Id;
            if (id == regionId)
            {
                index = midpoint;
                return true;
            }

            if (id < regionId)
                min = midpoint + 1;
            else
                max = midpoint - 1;
        }

        index = -1;
        return false;
    }

    private static bool TryFindTransitionIndexById(MapNavTransitionData[] transitions, int transitionId, out int index)
    {
        int min = 0;
        int max = transitions.Length - 1;
        while (min <= max)
        {
            int midpoint = min + ((max - min) / 2);
            int id = transitions[midpoint].Id;
            if (id == transitionId)
            {
                index = midpoint;
                return true;
            }

            if (id < transitionId)
                min = midpoint + 1;
            else
                max = midpoint - 1;
        }

        index = -1;
        return false;
    }
}

public readonly struct MapNavRegionData
{
    public readonly int Id;
    public readonly int NavLayerId;
    public readonly float Height;
    public readonly float Cost;
    public readonly int PointStart;
    public readonly int PointCount;
    public readonly int ObstacleStart;
    public readonly int ObstacleCount;
    public readonly int ConnectionStart;
    public readonly int ConnectionCount;
    public readonly Vector2 BoundsMin;
    public readonly Vector2 BoundsMax;
    public readonly bool HasBounds;

    public MapNavRegionData(
        int id,
        int navLayerId,
        float height,
        float cost,
        int pointStart,
        int pointCount,
        int obstacleStart,
        int obstacleCount,
        int connectionStart,
        int connectionCount,
        Vector2 boundsMin,
        Vector2 boundsMax,
        bool hasBounds)
    {
        Id = id;
        NavLayerId = navLayerId;
        Height = height;
        Cost = cost;
        PointStart = pointStart;
        PointCount = pointCount;
        ObstacleStart = obstacleStart;
        ObstacleCount = obstacleCount;
        ConnectionStart = connectionStart;
        ConnectionCount = connectionCount;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        HasBounds = hasBounds;
    }

    public MapNavRegionData WithConnectionRange(int connectionStart, int connectionCount)
    {
        return new MapNavRegionData(
            Id,
            NavLayerId,
            Height,
            Cost,
            PointStart,
            PointCount,
            ObstacleStart,
            ObstacleCount,
            connectionStart,
            connectionCount,
            BoundsMin,
            BoundsMax,
            HasBounds);
    }
}

public readonly struct MapNavRegionConnectionData
{
    public readonly int FromRegionId;
    public readonly int ToRegionId;
    public readonly bool UsesTransition;
    public readonly int TransitionId;
    public readonly bool IsForward;
    public readonly Vector2 PortalLocalA;
    public readonly Vector2 PortalLocalB;
    public readonly float Cost;

    private MapNavRegionConnectionData(
        int fromRegionId,
        int toRegionId,
        bool usesTransition,
        int transitionId,
        bool isForward,
        Vector2 portalLocalA,
        Vector2 portalLocalB,
        float cost)
    {
        FromRegionId = fromRegionId;
        ToRegionId = toRegionId;
        UsesTransition = usesTransition;
        TransitionId = transitionId;
        IsForward = isForward;
        PortalLocalA = portalLocalA;
        PortalLocalB = portalLocalB;
        Cost = cost;
    }

    public static MapNavRegionConnectionData Region(int fromRegionId, int toRegionId, Vector2 portalLocalA, Vector2 portalLocalB, float cost)
    {
        return new MapNavRegionConnectionData(fromRegionId, toRegionId, false, -1, true, portalLocalA, portalLocalB, cost);
    }

    public static MapNavRegionConnectionData Transition(int transitionId, int fromRegionId, int toRegionId, bool isForward, float cost)
    {
        return new MapNavRegionConnectionData(fromRegionId, toRegionId, true, transitionId, isForward, default, default, Mathf.Max(0f, cost));
    }
}

public readonly struct MapNavTransitionData
{
    public readonly int Id;
    public readonly int FromRegionId;
    public readonly int ToRegionId;
    public readonly MapNavTransitionType Type;
    public readonly float FromHeight;
    public readonly float ToHeight;
    public readonly Vector2 UpDirection;
    public readonly float Cost;
    public readonly float MinRadius;
    public readonly bool CanStopInside;
    public readonly bool CanFightInside;
    public readonly bool Bidirectional;
    public readonly bool Enabled;
    public readonly int PointStart;
    public readonly int PointCount;
    public readonly Vector2 BoundsMin;
    public readonly Vector2 BoundsMax;
    public readonly bool HasBounds;

    public MapNavTransitionData(
        int id,
        int fromRegionId,
        int toRegionId,
        MapNavTransitionType type,
        float fromHeight,
        float toHeight,
        Vector2 upDirection,
        float cost,
        float minRadius,
        bool canStopInside,
        bool canFightInside,
        bool bidirectional,
        bool enabled,
        int pointStart,
        int pointCount,
        Vector2 boundsMin,
        Vector2 boundsMax,
        bool hasBounds)
    {
        Id = id;
        FromRegionId = fromRegionId;
        ToRegionId = toRegionId;
        Type = type;
        FromHeight = fromHeight;
        ToHeight = toHeight;
        UpDirection = upDirection;
        Cost = cost;
        MinRadius = minRadius;
        CanStopInside = canStopInside;
        CanFightInside = canFightInside;
        Bidirectional = bidirectional;
        Enabled = enabled;
        PointStart = pointStart;
        PointCount = pointCount;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        HasBounds = hasBounds;
    }
}

public readonly struct MapNavObstacleData
{
    public readonly int RegionId;
    public readonly int PointStart;
    public readonly int PointCount;
    public readonly float CornerPadding;
    public readonly Vector2 BoundsMin;
    public readonly Vector2 BoundsMax;
    public readonly bool HasBounds;

    public MapNavObstacleData(
        int regionId,
        int pointStart,
        int pointCount,
        float cornerPadding,
        Vector2 boundsMin,
        Vector2 boundsMax,
        bool hasBounds)
    {
        RegionId = regionId;
        PointStart = pointStart;
        PointCount = pointCount;
        CornerPadding = cornerPadding;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        HasBounds = hasBounds;
    }
}
