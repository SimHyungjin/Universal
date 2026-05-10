using System.Collections.Generic;
using UnityEngine;

public readonly struct MapNavigationBuildDataContext
{
    public readonly MapNavigationBuildData BuildData;
    public readonly Matrix4x4 LocalToWorldMatrix;
    public readonly Matrix4x4 WorldToLocalMatrix;

    public MapNavigationBuildDataContext(
        MapNavigationBuildData buildData,
        Matrix4x4 localToWorldMatrix,
        Matrix4x4 worldToLocalMatrix)
    {
        BuildData = buildData;
        LocalToWorldMatrix = localToWorldMatrix;
        WorldToLocalMatrix = worldToLocalMatrix;
    }

    public bool IsValid => BuildData != null;
    public int RegionCount => BuildData?.Regions.Length ?? 0;
    public int TransitionCount => BuildData?.Transitions.Length ?? 0;
    public int ConnectionCount => BuildData?.Connections.Length ?? 0;
    public int ObstacleCount => BuildData?.Obstacles.Length ?? 0;

    public bool TryGetRegionAt(int index, out MapNavRegionData region)
    {
        if (BuildData != null && index >= 0 && index < BuildData.Regions.Length)
        {
            region = BuildData.Regions[index];
            return true;
        }

        region = default;
        return false;
    }

    public bool TryGetTransitionAt(int index, out MapNavTransitionData transition)
    {
        if (BuildData != null && index >= 0 && index < BuildData.Transitions.Length)
        {
            transition = BuildData.Transitions[index];
            return true;
        }

        transition = default;
        return false;
    }

    public bool TryGetObstacleAt(int index, out MapNavObstacleData obstacle)
    {
        if (BuildData != null && index >= 0 && index < BuildData.Obstacles.Length)
        {
            obstacle = BuildData.Obstacles[index];
            return true;
        }

        obstacle = default;
        return false;
    }

    public bool TryGetConnectionAt(int index, out MapNavRegionConnectionData connection)
    {
        if (BuildData != null && index >= 0 && index < BuildData.Connections.Length)
        {
            connection = BuildData.Connections[index];
            return true;
        }

        connection = default;
        return false;
    }

    public bool TryFindRegion(int regionId, out MapNavRegionData region)
    {
        if (BuildData != null && BuildData.TryGetRegion(regionId, out region))
            return true;

        region = default;
        return false;
    }

    public bool TryFindTransition(int transitionId, out MapNavTransitionData transition)
    {
        if (BuildData != null && BuildData.TryGetTransition(transitionId, out transition))
            return true;

        transition = default;
        return false;
    }

    public IReadOnlyList<Vector2> GetRegionPoints(MapNavRegionData region)
    {
        return BuildData != null ? BuildData.GetRegionPoints(region) : System.Array.Empty<Vector2>();
    }

    public IReadOnlyList<Vector2> GetTransitionPoints(MapNavTransitionData transition)
    {
        return BuildData != null ? BuildData.GetTransitionPoints(transition) : System.Array.Empty<Vector2>();
    }

    public IReadOnlyList<Vector2> GetObstaclePoints(MapNavObstacleData obstacle)
    {
        return BuildData != null ? BuildData.GetObstaclePoints(obstacle) : System.Array.Empty<Vector2>();
    }

    public IReadOnlyList<MapNavObstacleData> GetRegionObstacles(MapNavRegionData region)
    {
        return BuildData != null ? BuildData.GetRegionObstacles(region) : System.Array.Empty<MapNavObstacleData>();
    }

    public IReadOnlyList<MapNavRegionConnectionData> GetRegionConnections(MapNavRegionData region)
    {
        return BuildData != null ? BuildData.GetRegionConnections(region) : System.Array.Empty<MapNavRegionConnectionData>();
    }

    public Vector3 ToLocal3D(Vector3 worldPosition)
    {
        return WorldToLocalMatrix.MultiplyPoint3x4(worldPosition);
    }

    public Vector2 ToLocal2D(Vector3 worldPosition)
    {
        Vector3 local = ToLocal3D(worldPosition);
        return new Vector2(local.x, local.z);
    }

    public Vector3 ToWorld(MapNavRegionData region, Vector2 localPoint)
    {
        return LocalToWorldMatrix.MultiplyPoint3x4(new Vector3(localPoint.x, GetRegionHeight(region, localPoint), localPoint.y));
    }

    public Vector3 ToWorld(MapNavTransitionData transition, Vector2 localPoint)
    {
        return LocalToWorldMatrix.MultiplyPoint3x4(new Vector3(localPoint.x, GetTransitionHeight(transition, localPoint), localPoint.y));
    }

    public bool ContainsRegion(MapNavRegionData region, Vector2 localPoint, float tolerance = 0f)
    {
        if (!MapNavBoundsUtility.Contains(region.BoundsMin, region.BoundsMax, region.HasBounds, localPoint, tolerance))
            return false;

        IReadOnlyList<Vector2> points = GetRegionPoints(region);
        return MapNavGeometry.ContainsPoint(points, localPoint)
            || MapNavGeometry.IsNearEdge(points, localPoint, tolerance);
    }

    public bool ContainsTransition(MapNavTransitionData transition, Vector2 localPoint, float tolerance = 0f)
    {
        if (!MapNavBoundsUtility.Contains(transition.BoundsMin, transition.BoundsMax, transition.HasBounds, localPoint, tolerance))
            return false;

        IReadOnlyList<Vector2> points = GetTransitionPoints(transition);
        return MapNavGeometry.ContainsPoint(points, localPoint)
            || MapNavGeometry.IsNearEdge(points, localPoint, tolerance);
    }

    public bool ContainsObstacle(MapNavObstacleData obstacle, Vector2 localPoint)
    {
        if (!MapNavBoundsUtility.Contains(obstacle.BoundsMin, obstacle.BoundsMax, obstacle.HasBounds, localPoint))
            return false;

        return MapNavGeometry.ContainsPoint(GetObstaclePoints(obstacle), localPoint);
    }

    public float GetRegionHeight(MapNavRegionData region, Vector2 localPoint)
    {
        return region.Height;
    }

    public float GetTransitionHeight(MapNavTransitionData transition, Vector2 localPoint)
    {
        if (transition.Type == MapNavTransitionType.Edge || transition.Type == MapNavTransitionType.Door)
            return Mathf.Lerp(transition.FromHeight, transition.ToHeight, 0.5f);

        Vector2 direction = transition.UpDirection.sqrMagnitude > 0.0001f
            ? transition.UpDirection.normalized
            : Vector2.up;

        GetProjectedRange(GetTransitionPoints(transition), direction, out float min, out float max);
        float length = Mathf.Max(0.0001f, max - min);
        float progress = Mathf.Clamp01((Vector2.Dot(localPoint, direction) - min) / length);
        return Mathf.Lerp(transition.FromHeight, transition.ToHeight, progress);
    }

    public bool HasEnoughRegionPoints(MapNavRegionData region, int minimum)
    {
        return HasEnoughDeclaredAndResolvedPoints(region.PointCount, GetRegionPoints(region).Count, minimum);
    }

    public bool HasEnoughTransitionPoints(MapNavTransitionData transition, int minimum)
    {
        return HasEnoughDeclaredAndResolvedPoints(transition.PointCount, GetTransitionPoints(transition).Count, minimum);
    }

    public bool HasEnoughObstaclePoints(MapNavObstacleData obstacle, int minimum)
    {
        return HasEnoughDeclaredAndResolvedPoints(obstacle.PointCount, GetObstaclePoints(obstacle).Count, minimum);
    }

    public Vector2 GetClosestPointOnRegion(MapNavRegionData region, Vector2 localPoint)
    {
        IReadOnlyList<Vector2> points = GetRegionPoints(region);
        return points.Count >= 2 ? MapNavGeometry.ClosestPointOnPolygon(points, localPoint) : localPoint;
    }

    public Vector2 GetClosestPointOnTransition(MapNavTransitionData transition, Vector2 localPoint)
    {
        IReadOnlyList<Vector2> points = GetTransitionPoints(transition);
        return points.Count >= 2 ? MapNavGeometry.ClosestPointOnPolygon(points, localPoint) : localPoint;
    }

    public Vector2 GetClosestPointOnObstacle(MapNavObstacleData obstacle, Vector2 localPoint)
    {
        IReadOnlyList<Vector2> points = GetObstaclePoints(obstacle);
        return points.Count >= 2 ? MapNavGeometry.ClosestPointOnPolygon(points, localPoint) : localPoint;
    }

    public Vector2 GetRegionCenter(MapNavRegionData region)
    {
        return MapNavGeometry.AveragePoint(GetRegionPoints(region));
    }

    private static void GetProjectedRange(IReadOnlyList<Vector2> points, Vector2 direction, out float min, out float max)
    {
        if (points == null || points.Count == 0)
        {
            min = 0f;
            max = 1f;
            return;
        }

        min = Vector2.Dot(points[0], direction);
        max = min;

        for (int i = 1; i < points.Count; i++)
        {
            float value = Vector2.Dot(points[i], direction);
            min = Mathf.Min(min, value);
            max = Mathf.Max(max, value);
        }
    }

    private static bool HasEnoughDeclaredAndResolvedPoints(int declaredCount, int resolvedCount, int minimum)
    {
        return declaredCount >= minimum && resolvedCount >= minimum;
    }
}
