using System.Collections.Generic;
using MapNav.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MapNav.Baking
{
    public static class MapNavBaker
    {
        private const int ExtraShapeIdBase = 100000;

        public static BlobAssetReference<NavBlob> Build(MapNavigationAuthoring authoring, Allocator allocator)
        {
            if (authoring == null)
                return BuildEmpty(allocator);

            return Build(authoring.Regions, authoring.Transitions, allocator);
        }

        public static BlobAssetReference<NavBlob> Build(
            IReadOnlyList<MapNavRegion> sourceRegions,
            IReadOnlyList<MapNavTransition> sourceTransitions,
            Allocator allocator)
        {
            List<MapNavRegion> regions = SortById(sourceRegions, r => r.Id);
            List<MapNavTransition> transitions = SortById(sourceTransitions, t => t.Id);

            List<ShapeEntry> shapeEntries = ExpandShapeEntries(regions);

            List<float2> points = new();
            NavRegion[] regionData = new NavRegion[shapeEntries.Count];
            NavTransition[] transitionData = new NavTransition[transitions.Count];
            List<NavObstacle> obstacleData = new();

            BuildRegions(shapeEntries, regionData, obstacleData, points);
            BuildTransitions(transitions, transitionData, points);

            List<NavEdge>[] regionEdges = BuildRegionEdgeBuckets(shapeEntries, regionData, points);
            List<NavEdge>[] transitionEdges = new List<NavEdge>[transitionData.Length];
            for (int i = 0; i < transitionEdges.Length; i++)
                transitionEdges[i] = new List<NavEdge>();

            AddTransitionEdges(shapeEntries, regionData, transitionData, points, regionEdges, transitionEdges);

            return CreateBlob(points, regionData, transitionData, obstacleData, regionEdges, transitionEdges, allocator);
        }

        public static BlobAssetReference<NavBlob> BuildEmpty(Allocator allocator)
        {
            using BlobBuilder builder = new(Allocator.Temp);
            ref NavBlob root = ref builder.ConstructRoot<NavBlob>();
            builder.Allocate(ref root.Points, 0);
            builder.Allocate(ref root.Regions, 0);
            builder.Allocate(ref root.Transitions, 0);
            builder.Allocate(ref root.Obstacles, 0);
            builder.Allocate(ref root.RegionEdges, 0);
            builder.Allocate(ref root.RegionEdgeRange, 0);
            builder.Allocate(ref root.TransitionEdges, 0);
            builder.Allocate(ref root.TransitionEdgeRange, 0);
            builder.Allocate(ref root.Grid.CellRanges, 0);
            builder.Allocate(ref root.Grid.Entries, 0);
            return builder.CreateBlobAssetReference<NavBlob>(allocator);
        }

        // ────────────────────────────────────────────────────────────────

        private readonly struct ShapeEntry
        {
            public readonly MapNavRegion Region;
            public readonly MapNavPolygon Shape;
            public readonly int Id;

            public ShapeEntry(MapNavRegion region, MapNavPolygon shape, int id)
            {
                Region = region;
                Shape = shape;
                Id = id;
            }
        }

        private static List<ShapeEntry> ExpandShapeEntries(List<MapNavRegion> regions)
        {
            var entries = new List<ShapeEntry>();
            int extraIdx = 0;

            foreach (MapNavRegion r in regions)
            {
                if (r?.Shapes == null || r.Shapes.Count == 0) continue;
                for (int s = 0; s < r.Shapes.Count; s++)
                {
                    MapNavPolygon shape = r.Shapes[s];
                    if (shape == null) continue;
                    int id = s == 0 ? r.Id : ExtraShapeIdBase + extraIdx++;
                    entries.Add(new ShapeEntry(r, shape, id));
                }
            }

            entries.Sort((a, b) => a.Id.CompareTo(b.Id));
            return entries;
        }

        // ────────────────────────────────────────────────────────────────

        private static List<T> SortById<T>(IReadOnlyList<T> source, System.Func<T, int> idSelector) where T : class
        {
            List<T> list = new();
            if (source == null)
                return list;

            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] != null)
                    list.Add(source[i]);
            }

            list.Sort((a, b) => idSelector(a).CompareTo(idSelector(b)));
            return list;
        }

        private static void BuildRegions(
            List<ShapeEntry> entries,
            NavRegion[] regionData,
            List<NavObstacle> obstacleData,
            List<float2> points)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                ShapeEntry entry = entries[i];
                entry.Shape.RecalculateBounds();

                int pointStart = points.Count;
                AppendPoints(entry.Shape.Points, points);

                int obstacleStart = obstacleData.Count;
                int obstacleCount = AppendObstaclesForShape(entry.Id, entry.Region.Obstacles, entry.Shape, obstacleData, points);

                regionData[i] = new NavRegion
                {
                    Id = entry.Id,
                    Height = entry.Region.Height,
                    Cost = math.max(1f, entry.Region.Cost),
                    PointStart = pointStart,
                    PointCount = entry.Shape.Points?.Count ?? 0,
                    ObstacleStart = obstacleStart,
                    ObstacleCount = obstacleCount,
                    BoundsMin = ToFloat2(entry.Shape.BoundsMin),
                    BoundsMax = ToFloat2(entry.Shape.BoundsMax),
                    Center = ComputeCenter(entry.Shape.Points),
                    HasBounds = ToByte(entry.Shape.HasBounds)
                };
            }
        }

        private static void BuildTransitions(
            List<MapNavTransition> transitions,
            NavTransition[] transitionData,
            List<float2> points)
        {
            for (int i = 0; i < transitions.Count; i++)
            {
                MapNavTransition transition = transitions[i];
                transition.RecalculateBounds();

                int pointStart = points.Count;
                AppendPoints(transition.Points, points);

                transitionData[i] = new NavTransition
                {
                    Id = transition.Id,
                    Type = (int)transition.Type,
                    FromRegionId = transition.FromRegionId,
                    ToRegionId = transition.ToRegionId,
                    FromHeight = transition.FromHeight,
                    ToHeight = transition.ToHeight,
                    UpDirection = ToFloat2(transition.UpDirection),
                    Cost = math.max(0f, transition.Cost),
                    MinRadius = math.max(0f, transition.MinRadius),
                    CanStopInside = ToByte(transition.CanStopInside),
                    CanFightInside = ToByte(transition.CanFightInside),
                    Bidirectional = ToByte(transition.Bidirectional),
                    Enabled = ToByte(transition.Enabled),
                    PointStart = pointStart,
                    PointCount = transition.Points?.Count ?? 0,
                    BoundsMin = ToFloat2(transition.BoundsMin),
                    BoundsMax = ToFloat2(transition.BoundsMax),
                    Center = ComputeCenter(transition.Points),
                    HasBounds = ToByte(transition.HasBounds)
                };
            }
        }

        private static int AppendObstaclesForShape(
            int shapeId,
            IReadOnlyList<MapNavObstacle> sourceObstacles,
            MapNavPolygon shape,
            List<NavObstacle> obstacleData,
            List<float2> points)
        {
            if (sourceObstacles == null)
                return 0;

            int added = 0;
            for (int i = 0; i < sourceObstacles.Count; i++)
            {
                MapNavObstacle obstacle = sourceObstacles[i];
                if (obstacle?.Points == null || obstacle.Points.Count < 3) continue;
                if (!ObstacleOverlapsShape(obstacle, shape)) continue;

                obstacle.RecalculateBounds();
                int pointStart = points.Count;
                AppendPoints(obstacle.Points, points);

                obstacleData.Add(new NavObstacle
                {
                    RegionId = shapeId,
                    PointStart = pointStart,
                    PointCount = obstacle.Points.Count,
                    CornerPadding = math.max(0f, obstacle.CornerPadding),
                    BoundsMin = ToFloat2(obstacle.BoundsMin),
                    BoundsMax = ToFloat2(obstacle.BoundsMax),
                    HasBounds = ToByte(obstacle.HasBounds)
                });
                added++;
            }

            return added;
        }

        private static bool ObstacleOverlapsShape(MapNavObstacle obstacle, MapNavPolygon shape)
        {
            const float tol = 0.05f;
            foreach (Vector2 p in obstacle.Points)
                if (shape.Contains(p, tol)) return true;
            return false;
        }

        private static void AppendPoints(IReadOnlyList<Vector2> source, List<float2> target)
        {
            if (source == null)
                return;

            for (int i = 0; i < source.Count; i++)
                target.Add(new float2(source[i].x, source[i].y));
        }

        private static List<NavEdge>[] BuildRegionEdgeBuckets(List<ShapeEntry> entries, NavRegion[] regionData, List<float2> points)
        {
            List<NavEdge>[] buckets = new List<NavEdge>[regionData.Length];
            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = new List<NavEdge>();

            for (int a = 0; a < regionData.Length; a++)
            {
                NavRegion regionA = regionData[a];
                if (regionA.PointCount < 3)
                    continue;

                IReadOnlyList<Vector2> pointsA = ToVector2Slice(points, regionA.PointStart, regionA.PointCount);
                for (int b = a + 1; b < regionData.Length; b++)
                {
                    NavRegion regionB = regionData[b];
                    if (regionB.PointCount < 3)
                        continue;

                    if (!MapNavigationRegionLinkUtility.CanLink(regionA.Height, regionB.Height))
                        continue;

                    IReadOnlyList<Vector2> pointsB = ToVector2Slice(points, regionB.PointStart, regionB.PointCount);
                    bool linked = MapNavigationRegionLinkUtility.TryFindSharedPortal(pointsA, pointsB, out Vector2 portalA, out Vector2 portalB);

                    // Shapes of one region often overlap (composing a non-convex area) instead
                    // of sharing a clean edge; link those so the region stays one graph component.
                    if (!linked && entries[a].Region == entries[b].Region)
                        linked = MapNavigationRegionLinkUtility.TryFindOverlapPortal(pointsA, pointsB, out portalA, out portalB);

                    if (!linked)
                        continue;

                    float cost = math.max(0f, (regionA.Cost + regionB.Cost) * 0.5f);
                    float portalHeight = (regionA.Height + regionB.Height) * 0.5f;
                    buckets[a].Add(new NavEdge
                    {
                        ToKind = NavSpaceKind.Region,
                        ToId = regionB.Id,
                        PortalLocalA = ToFloat2(portalA),
                        PortalLocalB = ToFloat2(portalB),
                        Cost = cost,
                        PortalHeight = portalHeight
                    });
                    buckets[b].Add(new NavEdge
                    {
                        ToKind = NavSpaceKind.Region,
                        ToId = regionA.Id,
                        PortalLocalA = ToFloat2(portalA),
                        PortalLocalB = ToFloat2(portalB),
                        Cost = cost,
                        PortalHeight = portalHeight
                    });
                }
            }

            return buckets;
        }

        private static void AddTransitionEdges(
            List<ShapeEntry> entries,
            NavRegion[] regionData,
            NavTransition[] transitionData,
            List<float2> points,
            List<NavEdge>[] regionEdges,
            List<NavEdge>[] transitionEdges)
        {
            for (int t = 0; t < transitionData.Length; t++)
            {
                NavTransition trans = transitionData[t];
                if (trans.Enabled == 0 || trans.PointCount < 3)
                    continue;

                int fromIdx = FindRegionIndexById(regionData, trans.FromRegionId);
                int toIdx = FindRegionIndexById(regionData, trans.ToRegionId);
                if (fromIdx < 0 || toIdx < 0)
                    continue;

                GetTransitionEndpointPortals(trans, points, out float2 fromA, out float2 fromB, out float2 toA, out float2 toB);

                AddRegionTransitionEdges(entries, regionData, points, trans.FromRegionId, trans.Id, fromA, fromB,
                    trans.Cost, trans.FromHeight, regionEdges, fallbackRegionIndex: fromIdx);
                AddTransitionRegionEdges(entries, regionData, points, trans.ToRegionId, toA, toB,
                    trans.Cost, trans.ToHeight, transitionEdges[t], fallbackRegionIndex: toIdx);

                if (trans.Bidirectional == 0)
                    continue;

                AddRegionTransitionEdges(entries, regionData, points, trans.ToRegionId, trans.Id, toA, toB,
                    trans.Cost, trans.ToHeight, regionEdges, fallbackRegionIndex: toIdx);
                AddTransitionRegionEdges(entries, regionData, points, trans.FromRegionId, fromA, fromB,
                    trans.Cost, trans.FromHeight, transitionEdges[t], fallbackRegionIndex: fromIdx);
            }
        }

        private static void AddRegionTransitionEdges(
            List<ShapeEntry> entries,
            NavRegion[] regionData,
            List<float2> points,
            int logicalRegionId,
            int transitionId,
            float2 portalA,
            float2 portalB,
            float cost,
            float portalHeight,
            List<NavEdge>[] regionEdges,
            int fallbackRegionIndex)
        {
            bool added = false;
            for (int i = 0; i < regionData.Length; i++)
            {
                if (entries[i].Region.Id != logicalRegionId)
                    continue;

                if (!PortalTouchesRegion(regionData[i], points, portalA, portalB))
                    continue;

                regionEdges[i].Add(new NavEdge
                {
                    ToKind = NavSpaceKind.Transition,
                    ToId = transitionId,
                    PortalLocalA = portalA,
                    PortalLocalB = portalB,
                    Cost = cost,
                    PortalHeight = portalHeight
                });
                added = true;
            }

            if (added)
                return;

            regionEdges[fallbackRegionIndex].Add(new NavEdge
            {
                ToKind = NavSpaceKind.Transition,
                ToId = transitionId,
                PortalLocalA = portalA,
                PortalLocalB = portalB,
                Cost = cost,
                PortalHeight = portalHeight
            });
        }

        private static void AddTransitionRegionEdges(
            List<ShapeEntry> entries,
            NavRegion[] regionData,
            List<float2> points,
            int logicalRegionId,
            float2 portalA,
            float2 portalB,
            float cost,
            float portalHeight,
            List<NavEdge> transitionEdges,
            int fallbackRegionIndex)
        {
            bool added = false;
            for (int i = 0; i < regionData.Length; i++)
            {
                if (entries[i].Region.Id != logicalRegionId)
                    continue;

                if (!PortalTouchesRegion(regionData[i], points, portalA, portalB))
                    continue;

                transitionEdges.Add(new NavEdge
                {
                    ToKind = NavSpaceKind.Region,
                    ToId = regionData[i].Id,
                    PortalLocalA = portalA,
                    PortalLocalB = portalB,
                    Cost = cost,
                    PortalHeight = portalHeight
                });
                added = true;
            }

            if (added)
                return;

            transitionEdges.Add(new NavEdge
            {
                ToKind = NavSpaceKind.Region,
                ToId = regionData[fallbackRegionIndex].Id,
                PortalLocalA = portalA,
                PortalLocalB = portalB,
                Cost = cost,
                PortalHeight = portalHeight
            });
        }

        private static bool PortalTouchesRegion(NavRegion region, List<float2> points, float2 portalA, float2 portalB)
        {
            const float tolerance = MapNavigationRegionLinkUtility.LineTolerance;
            if (region.HasBounds == 0 || region.PointCount < 3)
                return false;

            float2 segMin = math.min(portalA, portalB);
            float2 segMax = math.max(portalA, portalB);
            if (segMax.x < region.BoundsMin.x - tolerance
                || segMin.x > region.BoundsMax.x + tolerance
                || segMax.y < region.BoundsMin.y - tolerance
                || segMin.y > region.BoundsMax.y + tolerance)
                return false;

            if (ContainsRegionPoint(region, points, portalA)
                || ContainsRegionPoint(region, points, portalB)
                || ContainsRegionPoint(region, points, (portalA + portalB) * 0.5f))
                return true;

            float toleranceSq = tolerance * tolerance;
            int previous = region.PointCount - 1;
            for (int i = 0; i < region.PointCount; previous = i++)
            {
                float2 edgeA = points[region.PointStart + previous];
                float2 edgeB = points[region.PointStart + i];
                if (SegmentsIntersect(portalA, portalB, edgeA, edgeB))
                    return true;

                if (SegmentDistanceSq(portalA, portalB, edgeA, edgeB) <= toleranceSq)
                    return true;
            }

            return false;
        }

        private static bool ContainsRegionPoint(NavRegion region, List<float2> points, float2 point)
        {
            const float tolerance = MapNavigationRegionLinkUtility.LineTolerance;
            if (point.x < region.BoundsMin.x - tolerance
                || point.x > region.BoundsMax.x + tolerance
                || point.y < region.BoundsMin.y - tolerance
                || point.y > region.BoundsMax.y + tolerance)
                return false;

            if (PolygonContains(points, region.PointStart, region.PointCount, point))
                return true;

            float toleranceSq = tolerance * tolerance;
            int previous = region.PointCount - 1;
            for (int i = 0; i < region.PointCount; previous = i++)
            {
                if (DistanceToSegmentSq(point, points[region.PointStart + previous], points[region.PointStart + i]) <= toleranceSq)
                    return true;
            }

            return false;
        }

        private static bool PolygonContains(List<float2> points, int start, int count, float2 point)
        {
            bool inside = false;
            int previous = count - 1;
            for (int i = 0; i < count; previous = i++)
            {
                float2 a = points[start + i];
                float2 b = points[start + previous];
                bool crosses = (a.y > point.y) != (b.y > point.y);
                if (!crosses)
                    continue;

                float dy = b.y - a.y;
                if (math.abs(dy) <= 1e-5f)
                    continue;

                float x = (b.x - a.x) * (point.y - a.y) / dy + a.x;
                if (point.x < x)
                    inside = !inside;
            }

            return inside;
        }

        private static float SegmentDistanceSq(float2 a0, float2 a1, float2 b0, float2 b1)
        {
            if (SegmentsIntersect(a0, a1, b0, b1))
                return 0f;

            float d0 = DistanceToSegmentSq(a0, b0, b1);
            float d1 = DistanceToSegmentSq(a1, b0, b1);
            float d2 = DistanceToSegmentSq(b0, a0, a1);
            float d3 = DistanceToSegmentSq(b1, a0, a1);
            return math.min(math.min(d0, d1), math.min(d2, d3));
        }

        private static float DistanceToSegmentSq(float2 point, float2 a, float2 b)
        {
            float2 ab = b - a;
            float lenSq = math.lengthsq(ab);
            if (lenSq <= 1e-5f)
                return math.lengthsq(point - a);

            float t = math.clamp(math.dot(point - a, ab) / lenSq, 0f, 1f);
            float2 closest = a + ab * t;
            return math.lengthsq(point - closest);
        }

        private static bool SegmentsIntersect(float2 p1, float2 p2, float2 q1, float2 q2)
        {
            const float epsilon = 1e-5f;
            float2 r = p2 - p1;
            float2 s = q2 - q1;
            float rxs = Cross(r, s);
            float2 qp = q1 - p1;

            if (math.abs(rxs) < epsilon)
            {
                if (math.abs(Cross(qp, r)) > epsilon)
                    return false;

                float rr = math.lengthsq(r);
                if (rr < epsilon)
                    return math.lengthsq(q1 - p1) < epsilon || math.lengthsq(q2 - p1) < epsilon;

                float t0 = math.dot(q1 - p1, r) / rr;
                float t1 = math.dot(q2 - p1, r) / rr;
                if (t0 > t1)
                    (t0, t1) = (t1, t0);
                return t0 <= 1f + epsilon && t1 >= -epsilon;
            }

            float t = Cross(qp, s) / rxs;
            float u = Cross(qp, r) / rxs;
            return t >= -epsilon && t <= 1f + epsilon && u >= -epsilon && u <= 1f + epsilon;
        }

        private static float Cross(float2 a, float2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static void GetTransitionEndpointPortals(
            NavTransition transition,
            List<float2> points,
            out float2 fromA,
            out float2 fromB,
            out float2 toA,
            out float2 toB)
        {
            if (transition.PointCount <= 0)
            {
                fromA = fromB = toA = toB = transition.Center;
                return;
            }

            float2 direction = math.lengthsq(transition.UpDirection) > 0.0001f
                ? math.normalize(transition.UpDirection)
                : new float2(0f, 1f);

            GetEndpointSupportEdge(points, transition.PointStart, transition.PointCount, direction, true, out fromA, out fromB);
            GetEndpointSupportEdge(points, transition.PointStart, transition.PointCount, direction, false, out toA, out toB);
        }

        private static void GetEndpointSupportEdge(
            List<float2> points,
            int pointStart,
            int pointCount,
            float2 direction,
            bool useMin,
            out float2 a,
            out float2 b)
        {
            a = points[pointStart];
            b = pointCount > 1 ? points[pointStart + 1] : a;
            float aProjection = math.dot(a, direction);
            float bProjection = math.dot(b, direction);
            if (IsBetterProjection(bProjection, aProjection, useMin))
            {
                (a, b) = (b, a);
                (aProjection, bProjection) = (bProjection, aProjection);
            }

            for (int i = 2; i < pointCount; i++)
            {
                float2 point = points[pointStart + i];
                float projection = math.dot(point, direction);
                if (IsBetterProjection(projection, aProjection, useMin))
                {
                    b = a;
                    bProjection = aProjection;
                    a = point;
                    aProjection = projection;
                    continue;
                }

                if (IsBetterProjection(projection, bProjection, useMin))
                {
                    b = point;
                    bProjection = projection;
                }
            }
        }

        private static bool IsBetterProjection(float candidate, float current, bool useMin)
        {
            return useMin ? candidate < current : candidate > current;
        }

        private static int FindRegionIndexById(NavRegion[] regions, int id)
        {
            int min = 0;
            int max = regions.Length - 1;
            while (min <= max)
            {
                int mid = min + ((max - min) / 2);
                int candidate = regions[mid].Id;
                if (candidate == id)
                    return mid;

                if (candidate < id)
                    min = mid + 1;
                else
                    max = mid - 1;
            }

            return -1;
        }

        private static BlobAssetReference<NavBlob> CreateBlob(
            List<float2> points,
            NavRegion[] regionData,
            NavTransition[] transitionData,
            List<NavObstacle> obstacleData,
            List<NavEdge>[] regionEdges,
            List<NavEdge>[] transitionEdges,
            Allocator allocator)
        {
            using BlobBuilder builder = new(Allocator.Temp);
            ref NavBlob root = ref builder.ConstructRoot<NavBlob>();

            BlobBuilderArray<float2> pointsArr = builder.Allocate(ref root.Points, points.Count);
            for (int i = 0; i < points.Count; i++)
                pointsArr[i] = points[i];

            BlobBuilderArray<NavRegion> regionsArr = builder.Allocate(ref root.Regions, regionData.Length);
            for (int i = 0; i < regionData.Length; i++)
                regionsArr[i] = regionData[i];

            BlobBuilderArray<NavTransition> transitionsArr = builder.Allocate(ref root.Transitions, transitionData.Length);
            for (int i = 0; i < transitionData.Length; i++)
                transitionsArr[i] = transitionData[i];

            BlobBuilderArray<NavObstacle> obstaclesArr = builder.Allocate(ref root.Obstacles, obstacleData.Count);
            for (int i = 0; i < obstacleData.Count; i++)
                obstaclesArr[i] = obstacleData[i];

            FlattenEdges(builder, ref root.RegionEdges, ref root.RegionEdgeRange, regionEdges);
            FlattenEdges(builder, ref root.TransitionEdges, ref root.TransitionEdgeRange, transitionEdges);

            BuildSpatialGrid(builder, ref root.Grid, regionData, transitionData);

            return builder.CreateBlobAssetReference<NavBlob>(allocator);
        }

        private static void BuildSpatialGrid(
            BlobBuilder builder,
            ref NavSpatialGrid grid,
            NavRegion[] regions,
            NavTransition[] transitions)
        {
            float2 min = new float2(float.PositiveInfinity);
            float2 max = new float2(float.NegativeInfinity);
            bool any = false;

            for (int i = 0; i < regions.Length; i++)
            {
                if (regions[i].HasBounds == 0 || regions[i].PointCount < 3) continue;
                min = math.min(min, regions[i].BoundsMin);
                max = math.max(max, regions[i].BoundsMax);
                any = true;
            }
            for (int i = 0; i < transitions.Length; i++)
            {
                if (transitions[i].HasBounds == 0 || transitions[i].Enabled == 0 || transitions[i].PointCount < 3) continue;
                min = math.min(min, transitions[i].BoundsMin);
                max = math.max(max, transitions[i].BoundsMax);
                any = true;
            }

            if (!any)
            {
                grid.HasGrid = 0;
                grid.CellSize = 0f;
                grid.Origin = float2.zero;
                grid.CellsX = 0;
                grid.CellsZ = 0;
                builder.Allocate(ref grid.CellRanges, 0);
                builder.Allocate(ref grid.Entries, 0);
                return;
            }

            float2 size = math.max(new float2(0.001f), max - min);
            float minDim = math.min(size.x, size.y);
            float cellSize = math.max(0.5f, minDim / 16f);
            int cellsX = math.max(1, (int)math.ceil(size.x / cellSize));
            int cellsZ = math.max(1, (int)math.ceil(size.y / cellSize));

            const int MaxCells = 4096;
            while (cellsX * cellsZ > MaxCells)
            {
                cellSize *= 1.5f;
                cellsX = math.max(1, (int)math.ceil(size.x / cellSize));
                cellsZ = math.max(1, (int)math.ceil(size.y / cellSize));
            }
            int cellCount = cellsX * cellsZ;

            List<NavGridEntry>[] perCell = new List<NavGridEntry>[cellCount];
            for (int c = 0; c < cellCount; c++) perCell[c] = new List<NavGridEntry>();

            for (int i = 0; i < regions.Length; i++)
            {
                ref NavRegion r = ref regions[i];
                if (r.HasBounds == 0 || r.PointCount < 3) continue;
                AppendToCells(perCell, min, cellSize, cellsX, cellsZ, r.BoundsMin, r.BoundsMax,
                    new NavGridEntry { Kind = NavSpaceKind.Region, Id = r.Id });
            }

            for (int i = 0; i < transitions.Length; i++)
            {
                ref NavTransition t = ref transitions[i];
                if (t.HasBounds == 0 || t.Enabled == 0 || t.PointCount < 3) continue;
                AppendToCells(perCell, min, cellSize, cellsX, cellsZ, t.BoundsMin, t.BoundsMax,
                    new NavGridEntry { Kind = NavSpaceKind.Transition, Id = t.Id });
            }

            int total = 0;
            for (int c = 0; c < cellCount; c++) total += perCell[c].Count;

            BlobBuilderArray<int2> rangeArr = builder.Allocate(ref grid.CellRanges, cellCount);
            BlobBuilderArray<NavGridEntry> entryArr = builder.Allocate(ref grid.Entries, total);
            int cursor = 0;
            for (int c = 0; c < cellCount; c++)
            {
                rangeArr[c] = new int2(cursor, perCell[c].Count);
                for (int e = 0; e < perCell[c].Count; e++)
                    entryArr[cursor++] = perCell[c][e];
            }

            grid.HasGrid = 1;
            grid.CellSize = cellSize;
            grid.Origin = min;
            grid.CellsX = cellsX;
            grid.CellsZ = cellsZ;
        }

        private static void AppendToCells(
            List<NavGridEntry>[] perCell,
            float2 origin,
            float cellSize,
            int cellsX,
            int cellsZ,
            float2 bMin,
            float2 bMax,
            NavGridEntry entry)
        {
            int x0 = math.clamp((int)math.floor((bMin.x - origin.x) / cellSize), 0, cellsX - 1);
            int x1 = math.clamp((int)math.floor((bMax.x - origin.x) / cellSize), 0, cellsX - 1);
            int z0 = math.clamp((int)math.floor((bMin.y - origin.y) / cellSize), 0, cellsZ - 1);
            int z1 = math.clamp((int)math.floor((bMax.y - origin.y) / cellSize), 0, cellsZ - 1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    perCell[z * cellsX + x].Add(entry);
        }

        private static void FlattenEdges(
            BlobBuilder builder,
            ref BlobArray<NavEdge> edgesField,
            ref BlobArray<int2> rangeField,
            List<NavEdge>[] buckets)
        {
            int total = 0;
            for (int i = 0; i < buckets.Length; i++)
                total += buckets[i].Count;

            BlobBuilderArray<NavEdge> edgesArr = builder.Allocate(ref edgesField, total);
            BlobBuilderArray<int2> rangeArr = builder.Allocate(ref rangeField, buckets.Length);

            int cursor = 0;
            for (int i = 0; i < buckets.Length; i++)
            {
                List<NavEdge> bucket = buckets[i];
                rangeArr[i] = new int2(cursor, bucket.Count);
                for (int e = 0; e < bucket.Count; e++)
                    edgesArr[cursor++] = bucket[e];
            }
        }

        private static IReadOnlyList<Vector2> ToVector2Slice(List<float2> source, int start, int count)
        {
            Vector2[] result = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                float2 p = source[start + i];
                result[i] = new Vector2(p.x, p.y);
            }
            return result;
        }

        private static float2 ComputeCenter(IReadOnlyList<Vector2> source)
        {
            if (source == null || source.Count == 0)
                return float2.zero;

            float2 sum = float2.zero;
            for (int i = 0; i < source.Count; i++)
                sum += new float2(source[i].x, source[i].y);

            return sum / source.Count;
        }

        private static float2 ToFloat2(Vector2 value) => new float2(value.x, value.y);
        private static byte ToByte(bool value) => value ? (byte)1 : (byte)0;
    }
}
