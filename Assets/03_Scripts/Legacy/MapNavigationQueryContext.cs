using System.Collections.Generic;
using UnityEngine;

public readonly struct MapNavigationQueryContext
{
    public readonly IReadOnlyList<MapNavRegion> Regions;
    public readonly IReadOnlyList<MapNavTransition> Transitions;
    public readonly MapNavigationRuntimeData RuntimeData;
    public readonly Matrix4x4 LocalToWorldMatrix;
    public readonly Matrix4x4 WorldToLocalMatrix;

    public MapNavigationQueryContext(
        IReadOnlyList<MapNavRegion> regions,
        IReadOnlyList<MapNavTransition> transitions,
        MapNavigationRuntimeData runtimeData,
        Matrix4x4 localToWorldMatrix,
        Matrix4x4 worldToLocalMatrix)
    {
        Regions = regions;
        Transitions = transitions;
        RuntimeData = runtimeData;
        LocalToWorldMatrix = localToWorldMatrix;
        WorldToLocalMatrix = worldToLocalMatrix;
    }

    public bool IsValid => Regions != null && Transitions != null && RuntimeData != null;
    public int RegionCount => Regions?.Count ?? 0;
    public int TransitionCount => Transitions?.Count ?? 0;

    public MapNavRegion GetRegionAt(int index)
    {
        return Regions != null && index >= 0 && index < Regions.Count ? Regions[index] : null;
    }

    public MapNavTransition GetTransitionAt(int index)
    {
        return Transitions != null && index >= 0 && index < Transitions.Count ? Transitions[index] : null;
    }

    public MapNavRegion FindRegion(int regionId)
    {
        return RuntimeData?.FindRegion(regionId);
    }

    public MapNavTransition FindTransition(int transitionId)
    {
        return RuntimeData?.FindTransition(transitionId);
    }

    public IReadOnlyList<MapNavTransition> GetTransitionsForRegion(int regionId)
    {
        return RuntimeData != null
            ? RuntimeData.GetTransitionsForRegion(regionId)
            : System.Array.Empty<MapNavTransition>();
    }

    public IReadOnlyList<MapNavigationRuntimeData.RegionLink> GetRegionLinksForRegion(int regionId)
    {
        return RuntimeData != null
            ? RuntimeData.GetRegionLinksForRegion(regionId)
            : System.Array.Empty<MapNavigationRuntimeData.RegionLink>();
    }

    public IReadOnlyList<MapNavigationRuntimeData.RegionConnection> GetConnectionsForRegion(int regionId)
    {
        return RuntimeData != null
            ? RuntimeData.GetConnectionsForRegion(regionId)
            : System.Array.Empty<MapNavigationRuntimeData.RegionConnection>();
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

    public Vector3 ToWorld(MapNavRegion region, Vector2 localPoint)
    {
        float height = GetRegionHeight(region, localPoint);
        return LocalToWorldMatrix.MultiplyPoint3x4(new Vector3(localPoint.x, height, localPoint.y));
    }

    public Vector3 ToWorld(MapNavTransition transition, Vector2 localPoint)
    {
        float height = GetTransitionHeight(transition, localPoint);
        return LocalToWorldMatrix.MultiplyPoint3x4(new Vector3(localPoint.x, height, localPoint.y));
    }

    public bool ContainsRegion(MapNavRegion region, Vector2 localPoint, float tolerance = 0f)
    {
        return region != null && region.Contains(localPoint, tolerance);
    }

    public bool ContainsTransition(MapNavTransition transition, Vector2 localPoint, float tolerance = 0f)
    {
        return transition != null && transition.Contains(localPoint, tolerance);
    }

    public float GetRegionHeight(MapNavRegion region, Vector2 localPoint)
    {
        return region != null ? region.GetHeight(localPoint) : 0f;
    }

    public float GetTransitionHeight(MapNavTransition transition, Vector2 localPoint)
    {
        return transition != null ? transition.GetHeight(localPoint) : 0f;
    }

    public string GetRegionDisplayName(MapNavRegion region)
    {
        return region != null ? region.DisplayName : "None";
    }

    public string GetTransitionDisplayName(MapNavTransition transition)
    {
        return transition != null ? transition.DisplayName : "None";
    }

    public IReadOnlyList<Vector2> GetRegionPoints(MapNavRegion region)
    {
        return region?.Points != null ? region.Points : System.Array.Empty<Vector2>();
    }

    public IReadOnlyList<Vector2> GetTransitionPoints(MapNavTransition transition)
    {
        return transition?.Points != null ? transition.Points : System.Array.Empty<Vector2>();
    }

    public IReadOnlyList<MapNavObstacle> GetRegionObstacles(MapNavRegion region)
    {
        return region?.Obstacles != null ? region.Obstacles : System.Array.Empty<MapNavObstacle>();
    }

    public bool ContainsObstacle(MapNavObstacle obstacle, Vector2 localPoint)
    {
        return obstacle != null && obstacle.Contains(localPoint);
    }

    public IReadOnlyList<Vector2> GetObstaclePoints(MapNavObstacle obstacle)
    {
        return obstacle?.Points != null ? obstacle.Points : System.Array.Empty<Vector2>();
    }

    public bool HasEnoughObstaclePoints(MapNavObstacle obstacle, int minimum)
    {
        return obstacle?.Points != null && obstacle.Points.Count >= minimum;
    }

    public Vector2 GetClosestPointOnObstacle(MapNavObstacle obstacle, Vector2 localPoint)
    {
        IReadOnlyList<Vector2> points = GetObstaclePoints(obstacle);
        return points.Count >= 2 ? MapNavGeometry.ClosestPointOnPolygon(points, localPoint) : localPoint;
    }

    public bool HasEnoughRegionPoints(MapNavRegion region, int minimum)
    {
        return region?.Points != null && region.Points.Count >= minimum;
    }

    public bool HasEnoughTransitionPoints(MapNavTransition transition, int minimum)
    {
        return transition?.Points != null && transition.Points.Count >= minimum;
    }

    public Vector2 GetClosestPointOnRegion(MapNavRegion region, Vector2 localPoint)
    {
        IReadOnlyList<Vector2> points = GetRegionPoints(region);
        return points.Count >= 2 ? MapNavGeometry.ClosestPointOnPolygon(points, localPoint) : localPoint;
    }

    public Vector2 GetClosestPointOnTransition(MapNavTransition transition, Vector2 localPoint)
    {
        IReadOnlyList<Vector2> points = GetTransitionPoints(transition);
        return points.Count >= 2 ? MapNavGeometry.ClosestPointOnPolygon(points, localPoint) : localPoint;
    }

    public Vector2 GetRegionCenter(MapNavRegion region)
    {
        return MapNavGeometry.AveragePoint(GetRegionPoints(region));
    }
}
