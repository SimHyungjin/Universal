using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public static class MapNavigationBlobBuilder
{
    public static BlobAssetReference<MapNavigationBlob> CreateBlobAsset(MapNavigationBuildData buildData, Allocator allocator)
    {
        buildData ??= MapNavigationBuildData.Empty();

        using BlobBuilder builder = new(Allocator.Temp);
        ref MapNavigationBlob root = ref builder.ConstructRoot<MapNavigationBlob>();

        FillRegions(builder, ref root, buildData.Regions);
        FillTransitions(builder, ref root, buildData.Transitions);
        FillConnections(builder, ref root, buildData.Connections);
        FillObstacles(builder, ref root, buildData.Obstacles);
        FillPoints(builder, ref root, buildData.Points);

        return builder.CreateBlobAssetReference<MapNavigationBlob>(allocator);
    }

    public static BlobAssetReference<MapNavigationBlob> CreateBlobAsset(MapNavigationAuthoring authoring, Allocator allocator)
    {
        return CreateBlobAsset(MapNavigationBuildData.FromAuthoring(authoring), allocator);
    }

    private static void FillRegions(BlobBuilder builder, ref MapNavigationBlob root, MapNavRegionData[] source)
    {
        BlobBuilderArray<MapNavRegionBlob> regions = builder.Allocate(ref root.Regions, source.Length);
        for (int i = 0; i < source.Length; i++)
        {
            MapNavRegionData region = source[i];
            regions[i] = new MapNavRegionBlob
            {
                Id = region.Id,
                NavLayerId = region.NavLayerId,
                Height = region.Height,
                Cost = region.Cost,
                PointStart = region.PointStart,
                PointCount = region.PointCount,
                ObstacleStart = region.ObstacleStart,
                ObstacleCount = region.ObstacleCount,
                ConnectionStart = region.ConnectionStart,
                ConnectionCount = region.ConnectionCount,
                BoundsMin = ToFloat2(region.BoundsMin),
                BoundsMax = ToFloat2(region.BoundsMax),
                HasBounds = ToByte(region.HasBounds)
            };
        }
    }

    private static void FillTransitions(BlobBuilder builder, ref MapNavigationBlob root, MapNavTransitionData[] source)
    {
        BlobBuilderArray<MapNavTransitionBlob> transitions = builder.Allocate(ref root.Transitions, source.Length);
        for (int i = 0; i < source.Length; i++)
        {
            MapNavTransitionData transition = source[i];
            transitions[i] = new MapNavTransitionBlob
            {
                Id = transition.Id,
                FromRegionId = transition.FromRegionId,
                ToRegionId = transition.ToRegionId,
                Type = (int)transition.Type,
                FromHeight = transition.FromHeight,
                ToHeight = transition.ToHeight,
                UpDirection = ToFloat2(transition.UpDirection),
                Cost = transition.Cost,
                MinRadius = transition.MinRadius,
                CanStopInside = ToByte(transition.CanStopInside),
                CanFightInside = ToByte(transition.CanFightInside),
                Bidirectional = ToByte(transition.Bidirectional),
                Enabled = ToByte(transition.Enabled),
                PointStart = transition.PointStart,
                PointCount = transition.PointCount,
                BoundsMin = ToFloat2(transition.BoundsMin),
                BoundsMax = ToFloat2(transition.BoundsMax),
                HasBounds = ToByte(transition.HasBounds)
            };
        }
    }

    private static void FillConnections(BlobBuilder builder, ref MapNavigationBlob root, MapNavRegionConnectionData[] source)
    {
        BlobBuilderArray<MapNavRegionConnectionBlob> connections = builder.Allocate(ref root.Connections, source.Length);
        for (int i = 0; i < source.Length; i++)
        {
            MapNavRegionConnectionData connection = source[i];
            connections[i] = new MapNavRegionConnectionBlob
            {
                FromRegionId = connection.FromRegionId,
                ToRegionId = connection.ToRegionId,
                UsesTransition = ToByte(connection.UsesTransition),
                TransitionId = connection.TransitionId,
                IsForward = ToByte(connection.IsForward),
                PortalLocalA = ToFloat2(connection.PortalLocalA),
                PortalLocalB = ToFloat2(connection.PortalLocalB),
                Cost = connection.Cost
            };
        }
    }

    private static void FillObstacles(BlobBuilder builder, ref MapNavigationBlob root, MapNavObstacleData[] source)
    {
        BlobBuilderArray<MapNavObstacleBlob> obstacles = builder.Allocate(ref root.Obstacles, source.Length);
        for (int i = 0; i < source.Length; i++)
        {
            MapNavObstacleData obstacle = source[i];
            obstacles[i] = new MapNavObstacleBlob
            {
                RegionId = obstacle.RegionId,
                PointStart = obstacle.PointStart,
                PointCount = obstacle.PointCount,
                CornerPadding = obstacle.CornerPadding,
                BoundsMin = ToFloat2(obstacle.BoundsMin),
                BoundsMax = ToFloat2(obstacle.BoundsMax),
                HasBounds = ToByte(obstacle.HasBounds)
            };
        }
    }

    private static void FillPoints(BlobBuilder builder, ref MapNavigationBlob root, Vector2[] source)
    {
        BlobBuilderArray<float2> points = builder.Allocate(ref root.Points, source.Length);
        for (int i = 0; i < source.Length; i++)
            points[i] = ToFloat2(source[i]);
    }

    private static float2 ToFloat2(Vector2 value)
    {
        return new float2(value.x, value.y);
    }

    private static byte ToByte(bool value)
    {
        return value ? (byte)1 : (byte)0;
    }
}
