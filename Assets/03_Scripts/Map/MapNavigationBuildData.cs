using System.Collections.Generic;
using UnityEngine;

public sealed class MapNavigationBuildData
{
    public readonly MapNavRegionData[] Regions;
    public readonly MapNavTransitionData[] Transitions;
    public readonly MapNavObstacleData[] Obstacles;
    public readonly Vector2[] Points;

    public MapNavigationBuildData(
        MapNavRegionData[] regions,
        MapNavTransitionData[] transitions,
        MapNavObstacleData[] obstacles,
        Vector2[] points)
    {
        Regions = regions ?? System.Array.Empty<MapNavRegionData>();
        Transitions = transitions ?? System.Array.Empty<MapNavTransitionData>();
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

        return new MapNavigationBuildData(regions.ToArray(), transitions.ToArray(), obstacles.ToArray(), points.ToArray());
    }

    public static MapNavigationBuildData Empty()
    {
        return new MapNavigationBuildData(null, null, null, null);
    }

    public bool TryFindRegionIndex(int regionId, out int index)
    {
        for (int i = 0; i < Regions.Length; i++)
        {
            if (Regions[i].Id != regionId)
                continue;

            index = i;
            return true;
        }

        index = -1;
        return false;
    }

    public bool TryFindTransitionIndex(int transitionId, out int index)
    {
        for (int i = 0; i < Transitions.Length; i++)
        {
            if (Transitions[i].Id != transitionId)
                continue;

            index = i;
            return true;
        }

        index = -1;
        return false;
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
                obstacleCount));
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
                GetCount(transition.Points)));
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

            int pointStart = AppendPoints(obstacle.Points, points);
            obstacles.Add(new MapNavObstacleData(regionId, pointStart, GetCount(obstacle.Points), Mathf.Max(0f, obstacle.CornerPadding)));
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

    private static bool IsValidRange(int start, int count, int length)
    {
        return start >= 0 && count >= 0 && start <= length && count <= length - start;
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

    public MapNavRegionData(int id, int navLayerId, float height, float cost, int pointStart, int pointCount, int obstacleStart, int obstacleCount)
    {
        Id = id;
        NavLayerId = navLayerId;
        Height = height;
        Cost = cost;
        PointStart = pointStart;
        PointCount = pointCount;
        ObstacleStart = obstacleStart;
        ObstacleCount = obstacleCount;
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
        int pointCount)
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
    }
}

public readonly struct MapNavObstacleData
{
    public readonly int RegionId;
    public readonly int PointStart;
    public readonly int PointCount;
    public readonly float CornerPadding;

    public MapNavObstacleData(int regionId, int pointStart, int pointCount, float cornerPadding)
    {
        RegionId = regionId;
        PointStart = pointStart;
        PointCount = pointCount;
        CornerPadding = cornerPadding;
    }
}
