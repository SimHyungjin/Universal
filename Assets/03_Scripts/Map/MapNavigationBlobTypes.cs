using Unity.Entities;
using Unity.Mathematics;

public struct MapNavigationBlob
{
    public BlobArray<MapNavRegionBlob> Regions;
    public BlobArray<MapNavTransitionBlob> Transitions;
    public BlobArray<MapNavRegionConnectionBlob> Connections;
    public BlobArray<MapNavObstacleBlob> Obstacles;
    public BlobArray<float2> Points;
}

public struct MapNavRegionBlob
{
    public int Id;
    public int NavLayerId;
    public float Height;
    public float Cost;
    public int PointStart;
    public int PointCount;
    public int ObstacleStart;
    public int ObstacleCount;
    public int ConnectionStart;
    public int ConnectionCount;
    public float2 BoundsMin;
    public float2 BoundsMax;
    public byte HasBounds;
}

public struct MapNavRegionConnectionBlob
{
    public int FromRegionId;
    public int ToRegionId;
    public byte UsesTransition;
    public int TransitionId;
    public byte IsForward;
    public float2 PortalLocalA;
    public float2 PortalLocalB;
    public float Cost;
}

public struct MapNavTransitionBlob
{
    public int Id;
    public int FromRegionId;
    public int ToRegionId;
    public int Type;
    public float FromHeight;
    public float ToHeight;
    public float2 UpDirection;
    public float Cost;
    public float MinRadius;
    public byte CanStopInside;
    public byte CanFightInside;
    public byte Bidirectional;
    public byte Enabled;
    public int PointStart;
    public int PointCount;
    public float2 BoundsMin;
    public float2 BoundsMax;
    public byte HasBounds;
}

public struct MapNavObstacleBlob
{
    public int RegionId;
    public int PointStart;
    public int PointCount;
    public float CornerPadding;
    public float2 BoundsMin;
    public float2 BoundsMax;
    public byte HasBounds;
}

public struct MapNavigationBlobComponent : IComponentData
{
    public BlobAssetReference<MapNavigationBlob> Blob;
    public float4x4 LocalToWorldMatrix;
    public float4x4 WorldToLocalMatrix;
}
