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
        float height = region != null ? region.GetHeight(localPoint) : 0f;
        return LocalToWorldMatrix.MultiplyPoint3x4(new Vector3(localPoint.x, height, localPoint.y));
    }

    public Vector3 ToWorld(MapNavTransition transition, Vector2 localPoint)
    {
        float height = transition != null ? transition.GetHeight(localPoint) : 0f;
        return LocalToWorldMatrix.MultiplyPoint3x4(new Vector3(localPoint.x, height, localPoint.y));
    }
}
