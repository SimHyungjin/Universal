using Unity.Entities;
using Unity.Mathematics;

namespace MapNav.Data
{
    public enum NavSpaceKind : byte
    {
        None = 0,
        Region = 1,
        Transition = 2
    }

    public struct NavSpaceRef : System.IEquatable<NavSpaceRef>
    {
        public NavSpaceKind Kind;
        public int Id;

        public NavSpaceRef(NavSpaceKind kind, int id)
        {
            Kind = kind;
            Id = id;
        }

        public static NavSpaceRef Region(int id) => new(NavSpaceKind.Region, id);
        public static NavSpaceRef Transition(int id) => new(NavSpaceKind.Transition, id);
        public static NavSpaceRef None => default;

        public bool IsValid => Kind != NavSpaceKind.None;

        public bool Equals(NavSpaceRef other) => Kind == other.Kind && Id == other.Id;
        public override bool Equals(object obj) => obj is NavSpaceRef r && Equals(r);
        public override int GetHashCode() => ((int)Kind << 24) ^ Id;
        public static bool operator ==(NavSpaceRef a, NavSpaceRef b) => a.Equals(b);
        public static bool operator !=(NavSpaceRef a, NavSpaceRef b) => !a.Equals(b);
    }

    public struct NavRegion
    {
        public int Id;
        public float Height;
        public float Cost;
        public int PointStart;
        public int PointCount;
        public int ObstacleStart;
        public int ObstacleCount;
        public float2 BoundsMin;
        public float2 BoundsMax;
        public float2 Center;
        public byte HasBounds;
    }

    public struct NavTransition
    {
        public int Id;
        public int Type;
        public int FromRegionId;
        public int ToRegionId;
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
        public float2 Center;
        public byte HasBounds;
    }

    public struct NavObstacle
    {
        public int RegionId;
        public int PointStart;
        public int PointCount;
        public float CornerPadding;
        public float2 BoundsMin;
        public float2 BoundsMax;
        public byte HasBounds;
    }

    public struct NavEdge
    {
        public NavSpaceKind ToKind;
        public int ToId;
        public float2 PortalLocalA;
        public float2 PortalLocalB;
        public float Cost;
        public float PortalHeight;
    }

    public struct NavGridEntry
    {
        public NavSpaceKind Kind;
        public int Id;
    }

    public struct NavSpatialGrid
    {
        public byte HasGrid;
        public float CellSize;
        public float2 Origin;
        public int CellsX;
        public int CellsZ;
        public BlobArray<int2> CellRanges;
        public BlobArray<NavGridEntry> Entries;
    }

    public struct NavBlob
    {
        public BlobArray<float2> Points;
        public BlobArray<NavRegion> Regions;
        public BlobArray<NavTransition> Transitions;
        public BlobArray<NavObstacle> Obstacles;
        public BlobArray<NavEdge> RegionEdges;
        public BlobArray<int2> RegionEdgeRange;
        public BlobArray<NavEdge> TransitionEdges;
        public BlobArray<int2> TransitionEdgeRange;
        public NavSpatialGrid Grid;
    }

    public struct NavBlobReference : IComponentData
    {
        public BlobAssetReference<NavBlob> Blob;
        public float4x4 LocalToWorld;
        public float4x4 WorldToLocal;
    }
}
