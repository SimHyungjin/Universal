using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public static class MapNavigationValidator
{
    public static List<string> Validate(MapNavigationAuthoring map)
    {
        List<string> results = new();

        if (map == null)
        {
            results.Add("MapNavigationAuthoring is null.");
            return results;
        }

        HashSet<int> ids = new();
        IReadOnlyList<MapNavRegion> regions = map.Regions;

        for (int i = 0; i < regions.Count; i++)
        {
            MapNavRegion region = regions[i];
            if (region == null)
            {
                results.Add($"Region at index {i} is null.");
                continue;
            }

            if (region.Id < 0)
                results.Add($"{region.DisplayName} has invalid id {region.Id}.");

            if (!ids.Add(region.Id))
                results.Add($"Region id {region.Id} is duplicated.");

            if (region.Points == null || region.Points.Count < 3)
                results.Add($"Region {region.Id} needs at least 3 points.");

            if (HasSelfIntersection(region.Points))
                results.Add($"Region {region.Id} polygon has self intersection.");

            if (region.Cost < 0f)
                results.Add($"Region {region.Id} must not have negative cost.");

            if (region.Obstacles == null)
                continue;

            for (int obstacleIndex = 0; obstacleIndex < region.Obstacles.Count; obstacleIndex++)
            {
                MapNavObstacle obstacle = region.Obstacles[obstacleIndex];
                if (obstacle == null)
                {
                    results.Add($"Region {region.Id} obstacle at index {obstacleIndex} is null.");
                    continue;
                }

                if (obstacle.Points == null || obstacle.Points.Count < 3)
                    results.Add($"Region {region.Id} obstacle {obstacleIndex} needs at least 3 points.");

                if (HasSelfIntersection(obstacle.Points))
                    results.Add($"Region {region.Id} obstacle {obstacleIndex} polygon has self intersection.");

                if (obstacle.CornerPadding < 0f)
                    results.Add($"Region {region.Id} obstacle {obstacleIndex} has negative corner padding.");
            }
        }

        HashSet<int> transitionIds = new();
        IReadOnlyList<MapNavTransition> transitions = map.Transitions;
        for (int i = 0; i < transitions.Count; i++)
        {
            MapNavTransition transition = transitions[i];
            if (transition == null)
            {
                results.Add($"Transition at index {i} is null.");
                continue;
            }

            if (transition.Id < 0)
                results.Add($"{transition.DisplayName} has invalid id {transition.Id}.");

            if (!transitionIds.Add(transition.Id))
                results.Add($"Transition id {transition.Id} is duplicated.");

            MapNavRegion fromRegion = map.FindRegion(transition.FromRegionId);
            MapNavRegion toRegion = map.FindRegion(transition.ToRegionId);

            if (fromRegion == null)
                results.Add($"Transition {i} has missing FromRegionId {transition.FromRegionId}.");

            if (toRegion == null)
                results.Add($"Transition {i} has missing ToRegionId {transition.ToRegionId}.");

            if (fromRegion != null && Mathf.Abs(transition.FromHeight - fromRegion.Height) > 0.05f)
                results.Add($"Transition {transition.Id} FromHeight {transition.FromHeight:0.###} does not match {fromRegion.DisplayName} height {fromRegion.Height:0.###}.");

            if (toRegion != null && Mathf.Abs(transition.ToHeight - toRegion.Height) > 0.05f)
                results.Add($"Transition {transition.Id} ToHeight {transition.ToHeight:0.###} does not match {toRegion.DisplayName} height {toRegion.Height:0.###}.");

            if (transition.Points == null || transition.Points.Count < 3)
                results.Add($"Transition {transition.Id} needs at least 3 points.");

            if (HasSelfIntersection(transition.Points))
                results.Add($"Transition {transition.Id} polygon has self intersection.");

            if ((transition.Type == MapNavTransitionType.Stair || transition.Type == MapNavTransitionType.Ramp)
                && transition.UpDirection.sqrMagnitude < 0.0001f)
                results.Add($"Transition {transition.Id} is {transition.Type} but has no up direction.");

            if (transition.MinRadius < 0f)
                results.Add($"Transition {i} has negative MinRadius.");

            if (transition.Cost < 0f)
                results.Add($"Transition {transition.Id} must not have negative cost.");
        }

        ValidateBuildData(map, results);
        return results;
    }

    private static void ValidateBuildData(MapNavigationAuthoring map, List<string> results)
    {
        MapNavigationBuildData buildData = map.BuildData;
        if (buildData == null)
        {
            results.Add("[BuildData] Build data cache is null.");
            return;
        }

        IReadOnlyList<MapNavRegion> sourceRegions = map.Regions;
        IReadOnlyList<MapNavTransition> sourceTransitions = map.Transitions;
        int sourceRegionCount = CountNonNull(sourceRegions);
        int sourceTransitionCount = CountNonNull(sourceTransitions);
        int sourceObstacleCount = CountNonNullObstacles(sourceRegions);

        if (buildData.Regions.Length != sourceRegionCount)
            results.Add($"[BuildData] Region count {buildData.Regions.Length} does not match source count {sourceRegionCount}.");

        if (buildData.Transitions.Length != sourceTransitionCount)
            results.Add($"[BuildData] Transition count {buildData.Transitions.Length} does not match source count {sourceTransitionCount}.");

        if (buildData.Obstacles.Length != sourceObstacleCount)
            results.Add($"[BuildData] Obstacle count {buildData.Obstacles.Length} does not match source count {sourceObstacleCount}.");

        ValidateRegionBuildData(buildData, sourceRegions, results);
        ValidateTransitionBuildData(buildData, sourceTransitions, results);
        ValidateObstacleBuildData(buildData, sourceRegions, results);
        ValidateBuildDataContextParity(map, results);
        ValidateBlobData(map, buildData, results);
    }

    private static void ValidateBlobData(MapNavigationAuthoring map, MapNavigationBuildData buildData, List<string> results)
    {
        BlobAssetReference<MapNavigationBlob> blob = MapNavigationBlobBuilder.CreateBlobAsset(buildData, Allocator.Persistent);
        try
        {
            MapNavigationBlobDataContext blobContext = new(blob, map.transform.localToWorldMatrix, map.transform.worldToLocalMatrix);
            if (!blobContext.IsValid)
            {
                results.Add("[BlobData] Blob data is not created.");
                return;
            }

            if (blobContext.RegionCount != buildData.Regions.Length)
                results.Add($"[BlobData] Region count {blobContext.RegionCount} does not match BuildData count {buildData.Regions.Length}.");

            if (blobContext.TransitionCount != buildData.Transitions.Length)
                results.Add($"[BlobData] Transition count {blobContext.TransitionCount} does not match BuildData count {buildData.Transitions.Length}.");

            if (blobContext.ObstacleCount != buildData.Obstacles.Length)
                results.Add($"[BlobData] Obstacle count {blobContext.ObstacleCount} does not match BuildData count {buildData.Obstacles.Length}.");

            if (blobContext.PointCount != buildData.Points.Length)
                results.Add($"[BlobData] Point count {blobContext.PointCount} does not match BuildData count {buildData.Points.Length}.");

            ValidateBlobRegionData(blobContext, buildData, results);
            ValidateBlobTransitionData(blobContext, buildData, results);
            ValidateBlobObstacleData(blobContext, buildData, results);
            ValidateBlobPointData(blobContext, buildData, results);
            ValidateBlobDataContextParity(map.QueryContext, blobContext, map.Regions, map.Transitions, results);
        }
        finally
        {
            if (blob.IsCreated)
                blob.Dispose();
        }
    }

    private static void ValidateBlobRegionData(MapNavigationBlobDataContext blobContext, MapNavigationBuildData buildData, List<string> results)
    {
        for (int i = 0; i < buildData.Regions.Length; i++)
        {
            if (!blobContext.TryGetRegionAt(i, out MapNavRegionBlob blobRegion))
            {
                results.Add($"[BlobData] Missing region at index {i}.");
                continue;
            }

            MapNavRegionData region = buildData.Regions[i];
            if (blobRegion.Id != region.Id
                || blobRegion.NavLayerId != region.NavLayerId
                || !NearlyEqual(blobRegion.Height, region.Height)
                || !NearlyEqual(blobRegion.Cost, region.Cost)
                || blobRegion.PointStart != region.PointStart
                || blobRegion.PointCount != region.PointCount
                || blobRegion.ObstacleStart != region.ObstacleStart
                || blobRegion.ObstacleCount != region.ObstacleCount)
            {
                results.Add($"[BlobData] Region {region.Id} does not match BuildData.");
            }

            if (!blobContext.IsValidPointRange(blobRegion.PointStart, blobRegion.PointCount))
                results.Add($"[BlobData] Region {blobRegion.Id} point range start={blobRegion.PointStart}, count={blobRegion.PointCount} is out of bounds.");

            if (!blobContext.IsValidObstacleRange(blobRegion.ObstacleStart, blobRegion.ObstacleCount))
                results.Add($"[BlobData] Region {blobRegion.Id} obstacle range start={blobRegion.ObstacleStart}, count={blobRegion.ObstacleCount} is out of bounds.");
        }
    }

    private static void ValidateBlobTransitionData(MapNavigationBlobDataContext blobContext, MapNavigationBuildData buildData, List<string> results)
    {
        for (int i = 0; i < buildData.Transitions.Length; i++)
        {
            if (!blobContext.TryGetTransitionAt(i, out MapNavTransitionBlob blobTransition))
            {
                results.Add($"[BlobData] Missing transition at index {i}.");
                continue;
            }

            MapNavTransitionData transition = buildData.Transitions[i];
            if (blobTransition.Id != transition.Id
                || blobTransition.FromRegionId != transition.FromRegionId
                || blobTransition.ToRegionId != transition.ToRegionId
                || blobTransition.Type != (int)transition.Type
                || !NearlyEqual(blobTransition.FromHeight, transition.FromHeight)
                || !NearlyEqual(blobTransition.ToHeight, transition.ToHeight)
                || !NearlyEqual(blobTransition.UpDirection.x, transition.UpDirection.x)
                || !NearlyEqual(blobTransition.UpDirection.y, transition.UpDirection.y)
                || !NearlyEqual(blobTransition.Cost, transition.Cost)
                || !NearlyEqual(blobTransition.MinRadius, transition.MinRadius)
                || FromByte(blobTransition.CanStopInside) != transition.CanStopInside
                || FromByte(blobTransition.CanFightInside) != transition.CanFightInside
                || FromByte(blobTransition.Bidirectional) != transition.Bidirectional
                || FromByte(blobTransition.Enabled) != transition.Enabled
                || blobTransition.PointStart != transition.PointStart
                || blobTransition.PointCount != transition.PointCount)
            {
                results.Add($"[BlobData] Transition {transition.Id} does not match BuildData.");
            }

            if (!blobContext.IsValidPointRange(blobTransition.PointStart, blobTransition.PointCount))
                results.Add($"[BlobData] Transition {blobTransition.Id} point range start={blobTransition.PointStart}, count={blobTransition.PointCount} is out of bounds.");
        }
    }

    private static void ValidateBlobObstacleData(MapNavigationBlobDataContext blobContext, MapNavigationBuildData buildData, List<string> results)
    {
        for (int i = 0; i < buildData.Obstacles.Length; i++)
        {
            if (!blobContext.TryGetObstacleAt(i, out MapNavObstacleBlob blobObstacle))
            {
                results.Add($"[BlobData] Missing obstacle at index {i}.");
                continue;
            }

            MapNavObstacleData obstacle = buildData.Obstacles[i];
            if (blobObstacle.RegionId != obstacle.RegionId
                || blobObstacle.PointStart != obstacle.PointStart
                || blobObstacle.PointCount != obstacle.PointCount
                || !NearlyEqual(blobObstacle.CornerPadding, obstacle.CornerPadding))
            {
                results.Add($"[BlobData] Obstacle {i} does not match BuildData.");
            }

            if (!blobContext.IsValidPointRange(blobObstacle.PointStart, blobObstacle.PointCount))
                results.Add($"[BlobData] Obstacle {i} point range start={blobObstacle.PointStart}, count={blobObstacle.PointCount} is out of bounds.");
        }
    }

    private static void ValidateBlobPointData(MapNavigationBlobDataContext blobContext, MapNavigationBuildData buildData, List<string> results)
    {
        for (int i = 0; i < buildData.Points.Length; i++)
        {
            if (!blobContext.TryGetPointAt(i, out Vector2 point))
            {
                results.Add($"[BlobData] Missing point at index {i}.");
                continue;
            }

            if (!NearlyEqual(point.x, buildData.Points[i].x) || !NearlyEqual(point.y, buildData.Points[i].y))
                results.Add($"[BlobData] Point {i} mismatch. BuildData={buildData.Points[i]:F3}, Blob={point:F3}.");
        }
    }

    private static void ValidateBlobDataContextParity(
        MapNavigationQueryContext sourceContext,
        MapNavigationBlobDataContext blobContext,
        IReadOnlyList<MapNavRegion> sourceRegions,
        IReadOnlyList<MapNavTransition> sourceTransitions,
        List<string> results)
    {
        if (!sourceContext.IsValid || !blobContext.IsValid)
        {
            results.Add("[BlobDataContext] Cannot compare parity because one context is invalid.");
            return;
        }

        ValidateBlobRegionContextParity(sourceContext, blobContext, sourceRegions, results);
        ValidateBlobTransitionContextParity(sourceContext, blobContext, sourceTransitions, results);
        ValidateBlobObstacleContextParity(sourceContext, blobContext, sourceRegions, results);
        ValidateBlobProjectionParity(sourceContext, blobContext, sourceRegions, sourceTransitions, results);
        ValidateBlobContainingRegionParity(sourceContext, blobContext, sourceRegions, sourceTransitions, results);
        ValidateBlobNavigationHeightParity(sourceContext, blobContext, sourceRegions, sourceTransitions, results);
        ValidateBlobInsideNavigationSpaceParity(sourceContext, blobContext, sourceRegions, sourceTransitions, results);
        ValidateBlobObstacleProjectionParity(sourceContext, blobContext, sourceRegions, results);
    }

    private static void ValidateBlobRegionContextParity(
        MapNavigationQueryContext sourceContext,
        MapNavigationBlobDataContext blobContext,
        IReadOnlyList<MapNavRegion> sourceRegions,
        List<string> results)
    {
        for (int i = 0; i < sourceRegions.Count; i++)
        {
            MapNavRegion source = sourceRegions[i];
            if (source == null || !blobContext.TryFindRegion(source.Id, out MapNavRegionBlob blobRegion))
                continue;

            CompareBlobRegionPoint(sourceContext, blobContext, source, blobRegion, blobContext.GetRegionCenter(blobRegion), "center", results);
            IReadOnlyList<Vector2> points = sourceContext.GetRegionPoints(source);
            for (int p = 0; p < points.Count; p++)
                CompareBlobRegionPoint(sourceContext, blobContext, source, blobRegion, points[p], $"point {p}", results);
        }
    }

    private static void CompareBlobRegionPoint(
        MapNavigationQueryContext sourceContext,
        MapNavigationBlobDataContext blobContext,
        MapNavRegion source,
        MapNavRegionBlob blobRegion,
        Vector2 point,
        string pointLabel,
        List<string> results)
    {
        bool sourceContains = sourceContext.ContainsRegion(source, point);
        bool blobContains = blobContext.ContainsRegion(blobRegion, point);
        if (sourceContains != blobContains)
            results.Add($"[BlobDataContext] Region {source.Id} {pointLabel} contains mismatch. Source={sourceContains}, Blob={blobContains}.");

        float sourceHeight = sourceContext.GetRegionHeight(source, point);
        if (!NearlyEqual(sourceHeight, blobRegion.Height))
            results.Add($"[BlobDataContext] Region {source.Id} {pointLabel} height mismatch. Source={sourceHeight:0.###}, Blob={blobRegion.Height:0.###}.");
    }

    private static void ValidateBlobTransitionContextParity(
        MapNavigationQueryContext sourceContext,
        MapNavigationBlobDataContext blobContext,
        IReadOnlyList<MapNavTransition> sourceTransitions,
        List<string> results)
    {
        for (int i = 0; i < sourceTransitions.Count; i++)
        {
            MapNavTransition source = sourceTransitions[i];
            if (source == null || !blobContext.TryFindTransition(source.Id, out MapNavTransitionBlob blobTransition))
                continue;

            CompareBlobTransitionPoint(sourceContext, blobContext, source, blobTransition, blobContext.GetTransitionCenter(blobTransition), "center", results);
            IReadOnlyList<Vector2> points = sourceContext.GetTransitionPoints(source);
            for (int p = 0; p < points.Count; p++)
                CompareBlobTransitionPoint(sourceContext, blobContext, source, blobTransition, points[p], $"point {p}", results);
        }
    }

    private static void CompareBlobTransitionPoint(
        MapNavigationQueryContext sourceContext,
        MapNavigationBlobDataContext blobContext,
        MapNavTransition source,
        MapNavTransitionBlob blobTransition,
        Vector2 point,
        string pointLabel,
        List<string> results)
    {
        bool sourceContains = sourceContext.ContainsTransition(source, point);
        bool blobContains = blobContext.ContainsTransition(blobTransition, point);
        if (sourceContains != blobContains)
            results.Add($"[BlobDataContext] Transition {source.Id} {pointLabel} contains mismatch. Source={sourceContains}, Blob={blobContains}.");

        float sourceHeight = sourceContext.GetTransitionHeight(source, point);
        float blobHeight = blobContext.GetTransitionHeight(blobTransition, point);
        if (!NearlyEqual(sourceHeight, blobHeight))
            results.Add($"[BlobDataContext] Transition {source.Id} {pointLabel} height mismatch. Source={sourceHeight:0.###}, Blob={blobHeight:0.###}.");
    }

    private static void ValidateBlobObstacleContextParity(
        MapNavigationQueryContext sourceContext,
        MapNavigationBlobDataContext blobContext,
        IReadOnlyList<MapNavRegion> sourceRegions,
        List<string> results)
    {
        for (int r = 0; r < sourceRegions.Count; r++)
        {
            MapNavRegion region = sourceRegions[r];
            if (region == null || region.Obstacles == null || !blobContext.TryFindRegion(region.Id, out MapNavRegionBlob blobRegion))
                continue;

            for (int o = 0; o < region.Obstacles.Count; o++)
            {
                MapNavObstacle source = region.Obstacles[o];
                int obstacleIndex = blobRegion.ObstacleStart + o;
                if (source == null || !blobContext.TryGetObstacleAt(obstacleIndex, out MapNavObstacleBlob blobObstacle))
                    continue;

                CompareBlobObstaclePoint(sourceContext, blobContext, source, blobObstacle, region.Id, o, blobContext.GetObstacleCenter(blobObstacle), "center", results);
                IReadOnlyList<Vector2> points = sourceContext.GetObstaclePoints(source);
                for (int p = 0; p < points.Count; p++)
                    CompareBlobObstaclePoint(sourceContext, blobContext, source, blobObstacle, region.Id, o, points[p], $"point {p}", results);
            }
        }
    }

    private static void CompareBlobObstaclePoint(
        MapNavigationQueryContext sourceContext,
        MapNavigationBlobDataContext blobContext,
        MapNavObstacle source,
        MapNavObstacleBlob blobObstacle,
        int regionId,
        int obstacleIndex,
        Vector2 point,
        string pointLabel,
        List<string> results)
    {
        bool sourceContains = sourceContext.ContainsObstacle(source, point);
        bool blobContains = blobContext.ContainsObstacle(blobObstacle, point);
        if (sourceContains != blobContains)
            results.Add($"[BlobDataContext] Region {regionId} obstacle {obstacleIndex} {pointLabel} contains mismatch. Source={sourceContains}, Blob={blobContains}.");
    }

    private static void ValidateBlobProjectionParity(
        MapNavigationQueryContext sourceContext,
        MapNavigationBlobDataContext blobContext,
        IReadOnlyList<MapNavRegion> sourceRegions,
        IReadOnlyList<MapNavTransition> sourceTransitions,
        List<string> results)
    {
        for (int i = 0; i < sourceRegions.Count; i++)
        {
            MapNavRegion region = sourceRegions[i];
            if (region == null)
                continue;

            CompareBlobProjection(sourceContext, blobContext, sourceContext.ToWorld(region, sourceContext.GetRegionCenter(region)), $"Region {region.Id} center", results);
        }

        for (int i = 0; i < sourceTransitions.Count; i++)
        {
            MapNavTransition transition = sourceTransitions[i];
            if (transition == null)
                continue;

            Vector2 center = MapNavGeometry.AveragePoint(sourceContext.GetTransitionPoints(transition));
            CompareBlobProjection(sourceContext, blobContext, sourceContext.ToWorld(transition, center), $"Transition {transition.Id} center", results);
        }
    }

    private static void CompareBlobProjection(
        MapNavigationQueryContext sourceContext,
        MapNavigationBlobDataContext blobContext,
        Vector3 worldPoint,
        string pointLabel,
        List<string> results)
    {
        bool sourceFound = MapNavigationQuery.TryProjectToClosestNavigationSpace(sourceContext, worldPoint, out Vector3 sourceProjected, out string sourceName, out float sourceDistance);
        bool blobFound = MapNavigationQuery.TryProjectToClosestNavigationSpace(blobContext, worldPoint, out Vector3 blobProjected, out string blobName, out float blobDistance);
        if (sourceFound != blobFound)
        {
            results.Add($"[BlobDataContext] {pointLabel} projection found mismatch. Source={sourceFound}, Blob={blobFound}.");
            return;
        }

        if (!sourceFound)
            return;

        if (!NearlyEqual(sourceDistance, blobDistance) || !NearlyEqualPlanar(sourceProjected, blobProjected))
            results.Add($"[BlobDataContext] {pointLabel} projection mismatch. Source={sourceName} {sourceProjected:F3} d={sourceDistance:0.###}, Blob={blobName} {blobProjected:F3} d={blobDistance:0.###}.");
    }

    private static void ValidateBlobContainingRegionParity(
        MapNavigationQueryContext sourceContext,
        MapNavigationBlobDataContext blobContext,
        IReadOnlyList<MapNavRegion> sourceRegions,
        IReadOnlyList<MapNavTransition> sourceTransitions,
        List<string> results)
    {
        for (int i = 0; i < sourceRegions.Count; i++)
        {
            MapNavRegion region = sourceRegions[i];
            if (region != null)
                CompareBlobContainingRegion(sourceContext, blobContext, sourceContext.ToWorld(region, sourceContext.GetRegionCenter(region)), $"Region {region.Id} center", results);
        }

        for (int i = 0; i < sourceTransitions.Count; i++)
        {
            MapNavTransition transition = sourceTransitions[i];
            if (transition == null)
                continue;

            Vector2 center = MapNavGeometry.AveragePoint(sourceContext.GetTransitionPoints(transition));
            CompareBlobContainingRegion(sourceContext, blobContext, sourceContext.ToWorld(transition, center), $"Transition {transition.Id} center", results);
        }
    }

    private static void CompareBlobContainingRegion(
        MapNavigationQueryContext sourceContext,
        MapNavigationBlobDataContext blobContext,
        Vector3 worldPoint,
        string pointLabel,
        List<string> results)
    {
        MapNavRegion sourceRegion = MapNavigationQuery.FindContainingRegion(sourceContext, worldPoint, 0f);
        bool blobFound = MapNavigationQuery.TryFindContainingRegion(blobContext, worldPoint, 0f, out MapNavRegionBlob blobRegion);

        if ((sourceRegion != null) != blobFound)
        {
            results.Add($"[BlobDataContext] {pointLabel} containing region found mismatch. Source={sourceRegion != null}, Blob={blobFound}.");
            return;
        }

        if (sourceRegion != null && sourceRegion.Id != blobRegion.Id)
            results.Add($"[BlobDataContext] {pointLabel} containing region mismatch. Source={sourceRegion.Id}, Blob={blobRegion.Id}.");
    }

    private static void ValidateBlobNavigationHeightParity(
        MapNavigationQueryContext sourceContext,
        MapNavigationBlobDataContext blobContext,
        IReadOnlyList<MapNavRegion> sourceRegions,
        IReadOnlyList<MapNavTransition> sourceTransitions,
        List<string> results)
    {
        for (int i = 0; i < sourceRegions.Count; i++)
        {
            MapNavRegion region = sourceRegions[i];
            if (region != null)
                CompareBlobNavigationHeight(sourceContext, blobContext, sourceContext.ToWorld(region, sourceContext.GetRegionCenter(region)), -1, region.Id, $"Region {region.Id} center", results);
        }

        for (int i = 0; i < sourceTransitions.Count; i++)
        {
            MapNavTransition transition = sourceTransitions[i];
            if (transition == null)
                continue;

            Vector2 center = MapNavGeometry.AveragePoint(sourceContext.GetTransitionPoints(transition));
            CompareBlobNavigationHeight(sourceContext, blobContext, sourceContext.ToWorld(transition, center), transition.Id, -1, $"Transition {transition.Id} center", results);
        }
    }

    private static void CompareBlobNavigationHeight(
        MapNavigationQueryContext sourceContext,
        MapNavigationBlobDataContext blobContext,
        Vector3 worldPoint,
        int previousTransitionId,
        int previousRegionId,
        string pointLabel,
        List<string> results)
    {
        bool sourceFound = MapNavigationQuery.TryGetNavigationHeight(sourceContext, worldPoint, 0f, previousTransitionId, previousRegionId, out float sourceHeight, out _, out int sourceTransitionId, out int sourceRegionId);
        bool blobFound = MapNavigationQuery.TryGetNavigationHeight(blobContext, worldPoint, 0f, previousTransitionId, previousRegionId, out float blobHeight, out _, out int blobTransitionId, out int blobRegionId);

        if (sourceFound != blobFound)
        {
            results.Add($"[BlobDataContext] {pointLabel} height found mismatch. Source={sourceFound}, Blob={blobFound}.");
            return;
        }

        if (sourceFound && (!NearlyEqual(sourceHeight, blobHeight) || sourceTransitionId != blobTransitionId || sourceRegionId != blobRegionId))
            results.Add($"[BlobDataContext] {pointLabel} height result mismatch. Source h={sourceHeight:0.###}, t={sourceTransitionId}, r={sourceRegionId}; Blob h={blobHeight:0.###}, t={blobTransitionId}, r={blobRegionId}.");
    }

    private static void ValidateBlobInsideNavigationSpaceParity(
        MapNavigationQueryContext sourceContext,
        MapNavigationBlobDataContext blobContext,
        IReadOnlyList<MapNavRegion> sourceRegions,
        IReadOnlyList<MapNavTransition> sourceTransitions,
        List<string> results)
    {
        for (int i = 0; i < sourceRegions.Count; i++)
        {
            MapNavRegion region = sourceRegions[i];
            if (region == null)
                continue;

            CompareBlobInsideNavigationSpace(sourceContext, blobContext, sourceContext.ToWorld(region, sourceContext.GetRegionCenter(region)), $"Region {region.Id} center", results);

            IReadOnlyList<MapNavObstacle> obstacles = sourceContext.GetRegionObstacles(region);
            for (int o = 0; o < obstacles.Count; o++)
            {
                IReadOnlyList<Vector2> points = sourceContext.GetObstaclePoints(obstacles[o]);
                if (points.Count > 0)
                    CompareBlobInsideNavigationSpace(sourceContext, blobContext, sourceContext.ToWorld(region, MapNavGeometry.AveragePoint(points)), $"Region {region.Id} obstacle {o} center", results);
            }
        }

        for (int i = 0; i < sourceTransitions.Count; i++)
        {
            MapNavTransition transition = sourceTransitions[i];
            if (transition == null)
                continue;

            Vector2 center = MapNavGeometry.AveragePoint(sourceContext.GetTransitionPoints(transition));
            CompareBlobInsideNavigationSpace(sourceContext, blobContext, sourceContext.ToWorld(transition, center), $"Transition {transition.Id} center", results);
        }
    }

    private static void CompareBlobInsideNavigationSpace(
        MapNavigationQueryContext sourceContext,
        MapNavigationBlobDataContext blobContext,
        Vector3 worldPoint,
        string pointLabel,
        List<string> results)
    {
        bool sourceInside = MapNavigationQuery.IsInsideNavigationSpace(sourceContext, worldPoint, 0f);
        bool blobInside = MapNavigationQuery.IsInsideNavigationSpace(blobContext, worldPoint, 0f);
        if (sourceInside != blobInside)
            results.Add($"[BlobDataContext] {pointLabel} inside navigation mismatch. Source={sourceInside}, Blob={blobInside}.");
    }

    private static void ValidateBlobObstacleProjectionParity(
        MapNavigationQueryContext sourceContext,
        MapNavigationBlobDataContext blobContext,
        IReadOnlyList<MapNavRegion> sourceRegions,
        List<string> results)
    {
        for (int r = 0; r < sourceRegions.Count; r++)
        {
            MapNavRegion region = sourceRegions[r];
            if (region == null || !blobContext.TryFindRegion(region.Id, out MapNavRegionBlob blobRegion))
                continue;

            Vector3 referenceWorld = sourceContext.ToWorld(region, sourceContext.GetRegionCenter(region));
            IReadOnlyList<MapNavObstacle> obstacles = sourceContext.GetRegionObstacles(region);
            for (int o = 0; o < obstacles.Count; o++)
            {
                IReadOnlyList<Vector2> points = sourceContext.GetObstaclePoints(obstacles[o]);
                if (points.Count < 3)
                    continue;

                Vector3 obstacleWorld = sourceContext.ToWorld(region, MapNavGeometry.AveragePoint(points));
                CompareBlobObstacleProjection(sourceContext, blobContext, region, blobRegion, obstacleWorld, referenceWorld, $"Region {region.Id} obstacle {o} center", results);
            }
        }
    }

    private static void CompareBlobObstacleProjection(
        MapNavigationQueryContext sourceContext,
        MapNavigationBlobDataContext blobContext,
        MapNavRegion sourceRegion,
        MapNavRegionBlob blobRegion,
        Vector3 worldPoint,
        Vector3 referenceWorld,
        string pointLabel,
        List<string> results)
    {
        const float Padding = 0.1f;
        bool sourceFound = MapNavigationQuery.TryProjectOutOfObstacles(sourceContext, sourceRegion, worldPoint, referenceWorld, Padding, out Vector3 sourceProjected, out _, out float sourceDistance);
        bool blobFound = MapNavigationQuery.TryProjectOutOfObstacles(blobContext, blobRegion, worldPoint, referenceWorld, Padding, out Vector3 blobProjected, out _, out float blobDistance);

        if (sourceFound != blobFound)
        {
            results.Add($"[BlobDataContext] {pointLabel} obstacle projection found mismatch. Source={sourceFound}, Blob={blobFound}.");
            return;
        }

        if (sourceFound && (!NearlyEqual(sourceDistance, blobDistance) || !NearlyEqualPlanar(sourceProjected, blobProjected)))
            results.Add($"[BlobDataContext] {pointLabel} obstacle projection mismatch. Source={sourceProjected:F3} d={sourceDistance:0.###}, Blob={blobProjected:F3} d={blobDistance:0.###}.");
    }

    private static void ValidateBuildDataContextParity(MapNavigationAuthoring map, List<string> results)
    {
        MapNavigationQueryContext sourceContext = map.QueryContext;
        MapNavigationBuildDataContext buildContext = map.BuildDataContext;
        if (!sourceContext.IsValid || !buildContext.IsValid)
        {
            results.Add("[BuildDataContext] Cannot compare parity because one context is invalid.");
            return;
        }

        ValidateRegionContextParity(sourceContext, buildContext, map.Regions, results);
        ValidateTransitionContextParity(sourceContext, buildContext, map.Transitions, results);
        ValidateObstacleContextParity(sourceContext, buildContext, map.Regions, results);
        ValidateProjectionParity(sourceContext, buildContext, map.Regions, map.Transitions, results);
        ValidateContainingRegionParity(sourceContext, buildContext, map.Regions, map.Transitions, results);
        ValidateNavigationHeightParity(sourceContext, buildContext, map.Regions, map.Transitions, results);
        ValidateInsideNavigationSpaceParity(sourceContext, buildContext, map.Regions, map.Transitions, results);
        ValidateObstacleProjectionParity(sourceContext, buildContext, map.Regions, results);
    }

    private static void ValidateRegionContextParity(
        MapNavigationQueryContext sourceContext,
        MapNavigationBuildDataContext buildContext,
        IReadOnlyList<MapNavRegion> sourceRegions,
        List<string> results)
    {
        for (int i = 0; i < sourceRegions.Count; i++)
        {
            MapNavRegion source = sourceRegions[i];
            if (source == null || !buildContext.TryFindRegion(source.Id, out MapNavRegionData data))
                continue;

            CompareRegionPoint(sourceContext, buildContext, source, data, buildContext.GetRegionCenter(data), "center", results);
            IReadOnlyList<Vector2> points = sourceContext.GetRegionPoints(source);
            for (int p = 0; p < points.Count; p++)
                CompareRegionPoint(sourceContext, buildContext, source, data, points[p], $"point {p}", results);
        }
    }

    private static void CompareRegionPoint(
        MapNavigationQueryContext sourceContext,
        MapNavigationBuildDataContext buildContext,
        MapNavRegion source,
        MapNavRegionData data,
        Vector2 point,
        string pointLabel,
        List<string> results)
    {
        bool sourceContains = sourceContext.ContainsRegion(source, point);
        bool buildContains = buildContext.ContainsRegion(data, point);
        if (sourceContains != buildContains)
            results.Add($"[BuildDataContext] Region {source.Id} {pointLabel} contains mismatch. Source={sourceContains}, BuildData={buildContains}.");

        float sourceHeight = sourceContext.GetRegionHeight(source, point);
        float buildHeight = buildContext.GetRegionHeight(data, point);
        if (!NearlyEqual(sourceHeight, buildHeight))
            results.Add($"[BuildDataContext] Region {source.Id} {pointLabel} height mismatch. Source={sourceHeight:0.###}, BuildData={buildHeight:0.###}.");
    }

    private static void ValidateTransitionContextParity(
        MapNavigationQueryContext sourceContext,
        MapNavigationBuildDataContext buildContext,
        IReadOnlyList<MapNavTransition> sourceTransitions,
        List<string> results)
    {
        for (int i = 0; i < sourceTransitions.Count; i++)
        {
            MapNavTransition source = sourceTransitions[i];
            if (source == null || !buildContext.TryFindTransition(source.Id, out MapNavTransitionData data))
                continue;

            IReadOnlyList<Vector2> points = sourceContext.GetTransitionPoints(source);
            Vector2 center = MapNavGeometry.AveragePoint(points);
            CompareTransitionPoint(sourceContext, buildContext, source, data, center, "center", results);
            for (int p = 0; p < points.Count; p++)
                CompareTransitionPoint(sourceContext, buildContext, source, data, points[p], $"point {p}", results);
        }
    }

    private static void CompareTransitionPoint(
        MapNavigationQueryContext sourceContext,
        MapNavigationBuildDataContext buildContext,
        MapNavTransition source,
        MapNavTransitionData data,
        Vector2 point,
        string pointLabel,
        List<string> results)
    {
        bool sourceContains = sourceContext.ContainsTransition(source, point);
        bool buildContains = buildContext.ContainsTransition(data, point);
        if (sourceContains != buildContains)
            results.Add($"[BuildDataContext] Transition {source.Id} {pointLabel} contains mismatch. Source={sourceContains}, BuildData={buildContains}.");

        float sourceHeight = sourceContext.GetTransitionHeight(source, point);
        float buildHeight = buildContext.GetTransitionHeight(data, point);
        if (!NearlyEqual(sourceHeight, buildHeight))
            results.Add($"[BuildDataContext] Transition {source.Id} {pointLabel} height mismatch. Source={sourceHeight:0.###}, BuildData={buildHeight:0.###}.");
    }

    private static void ValidateObstacleContextParity(
        MapNavigationQueryContext sourceContext,
        MapNavigationBuildDataContext buildContext,
        IReadOnlyList<MapNavRegion> sourceRegions,
        List<string> results)
    {
        for (int r = 0; r < sourceRegions.Count; r++)
        {
            MapNavRegion region = sourceRegions[r];
            if (region == null || region.Obstacles == null || !buildContext.TryFindRegion(region.Id, out MapNavRegionData regionData))
                continue;

            IReadOnlyList<MapNavObstacleData> obstacleData = buildContext.GetRegionObstacles(regionData);
            for (int o = 0; o < region.Obstacles.Count; o++)
            {
                MapNavObstacle source = region.Obstacles[o];
                if (source == null || o >= obstacleData.Count)
                    continue;

                IReadOnlyList<Vector2> points = sourceContext.GetObstaclePoints(source);
                Vector2 center = MapNavGeometry.AveragePoint(points);
                CompareObstaclePoint(sourceContext, buildContext, source, obstacleData[o], region.Id, o, center, "center", results);
                for (int p = 0; p < points.Count; p++)
                    CompareObstaclePoint(sourceContext, buildContext, source, obstacleData[o], region.Id, o, points[p], $"point {p}", results);
            }
        }
    }

    private static void CompareObstaclePoint(
        MapNavigationQueryContext sourceContext,
        MapNavigationBuildDataContext buildContext,
        MapNavObstacle source,
        MapNavObstacleData data,
        int regionId,
        int obstacleIndex,
        Vector2 point,
        string pointLabel,
        List<string> results)
    {
        bool sourceContains = sourceContext.ContainsObstacle(source, point);
        bool buildContains = buildContext.ContainsObstacle(data, point);
        if (sourceContains != buildContains)
            results.Add($"[BuildDataContext] Region {regionId} obstacle {obstacleIndex} {pointLabel} contains mismatch. Source={sourceContains}, BuildData={buildContains}.");
    }

    private static void ValidateProjectionParity(
        MapNavigationQueryContext sourceContext,
        MapNavigationBuildDataContext buildContext,
        IReadOnlyList<MapNavRegion> sourceRegions,
        IReadOnlyList<MapNavTransition> sourceTransitions,
        List<string> results)
    {
        for (int i = 0; i < sourceRegions.Count; i++)
        {
            MapNavRegion region = sourceRegions[i];
            if (region == null)
                continue;

            CompareProjection(sourceContext, buildContext, sourceContext.ToWorld(region, sourceContext.GetRegionCenter(region)), $"Region {region.Id} center", results);
        }

        for (int i = 0; i < sourceTransitions.Count; i++)
        {
            MapNavTransition transition = sourceTransitions[i];
            if (transition == null)
                continue;

            Vector2 center = MapNavGeometry.AveragePoint(sourceContext.GetTransitionPoints(transition));
            CompareProjection(sourceContext, buildContext, sourceContext.ToWorld(transition, center), $"Transition {transition.Id} center", results);
        }
    }

    private static void CompareProjection(
        MapNavigationQueryContext sourceContext,
        MapNavigationBuildDataContext buildContext,
        Vector3 worldPoint,
        string pointLabel,
        List<string> results)
    {
        bool sourceFound = MapNavigationQuery.TryProjectToClosestNavigationSpace(sourceContext, worldPoint, out Vector3 sourceProjected, out string sourceName, out float sourceDistance);
        bool buildFound = MapNavigationQuery.TryProjectToClosestNavigationSpace(buildContext, worldPoint, out Vector3 buildProjected, out string buildName, out float buildDistance);
        if (sourceFound != buildFound)
        {
            results.Add($"[BuildDataContext] {pointLabel} projection found mismatch. Source={sourceFound}, BuildData={buildFound}.");
            return;
        }

        if (!sourceFound)
            return;

        if (!NearlyEqual(sourceDistance, buildDistance) || !NearlyEqualPlanar(sourceProjected, buildProjected))
        {
            results.Add($"[BuildDataContext] {pointLabel} projection mismatch. Source={sourceName} {sourceProjected:F3} d={sourceDistance:0.###}, BuildData={buildName} {buildProjected:F3} d={buildDistance:0.###}.");
        }
    }

    private static void ValidateContainingRegionParity(
        MapNavigationQueryContext sourceContext,
        MapNavigationBuildDataContext buildContext,
        IReadOnlyList<MapNavRegion> sourceRegions,
        IReadOnlyList<MapNavTransition> sourceTransitions,
        List<string> results)
    {
        for (int i = 0; i < sourceRegions.Count; i++)
        {
            MapNavRegion region = sourceRegions[i];
            if (region == null)
                continue;

            CompareContainingRegion(sourceContext, buildContext, sourceContext.ToWorld(region, sourceContext.GetRegionCenter(region)), $"Region {region.Id} center", results);
        }

        for (int i = 0; i < sourceTransitions.Count; i++)
        {
            MapNavTransition transition = sourceTransitions[i];
            if (transition == null)
                continue;

            Vector2 center = MapNavGeometry.AveragePoint(sourceContext.GetTransitionPoints(transition));
            CompareContainingRegion(sourceContext, buildContext, sourceContext.ToWorld(transition, center), $"Transition {transition.Id} center", results);
        }
    }

    private static void CompareContainingRegion(
        MapNavigationQueryContext sourceContext,
        MapNavigationBuildDataContext buildContext,
        Vector3 worldPoint,
        string pointLabel,
        List<string> results)
    {
        MapNavRegion sourceRegion = MapNavigationQuery.FindContainingRegion(sourceContext, worldPoint, 0f);
        bool buildFound = MapNavigationQuery.TryFindContainingRegion(buildContext, worldPoint, 0f, out MapNavRegionData buildRegion);

        if ((sourceRegion != null) != buildFound)
        {
            results.Add($"[BuildDataContext] {pointLabel} containing region found mismatch. Source={sourceRegion != null}, BuildData={buildFound}.");
            return;
        }

        if (sourceRegion == null)
            return;

        if (sourceRegion.Id != buildRegion.Id)
            results.Add($"[BuildDataContext] {pointLabel} containing region mismatch. Source={sourceRegion.Id}, BuildData={buildRegion.Id}.");
    }

    private static void ValidateNavigationHeightParity(
        MapNavigationQueryContext sourceContext,
        MapNavigationBuildDataContext buildContext,
        IReadOnlyList<MapNavRegion> sourceRegions,
        IReadOnlyList<MapNavTransition> sourceTransitions,
        List<string> results)
    {
        for (int i = 0; i < sourceRegions.Count; i++)
        {
            MapNavRegion region = sourceRegions[i];
            if (region == null)
                continue;

            CompareNavigationHeight(sourceContext, buildContext, sourceContext.ToWorld(region, sourceContext.GetRegionCenter(region)), -1, region.Id, $"Region {region.Id} center", results);
        }

        for (int i = 0; i < sourceTransitions.Count; i++)
        {
            MapNavTransition transition = sourceTransitions[i];
            if (transition == null)
                continue;

            Vector2 center = MapNavGeometry.AveragePoint(sourceContext.GetTransitionPoints(transition));
            CompareNavigationHeight(sourceContext, buildContext, sourceContext.ToWorld(transition, center), transition.Id, -1, $"Transition {transition.Id} center", results);
        }
    }

    private static void CompareNavigationHeight(
        MapNavigationQueryContext sourceContext,
        MapNavigationBuildDataContext buildContext,
        Vector3 worldPoint,
        int previousTransitionId,
        int previousRegionId,
        string pointLabel,
        List<string> results)
    {
        bool sourceFound = MapNavigationQuery.TryGetNavigationHeight(
            sourceContext,
            worldPoint,
            0f,
            previousTransitionId,
            previousRegionId,
            out float sourceHeight,
            out _,
            out int sourceTransitionId,
            out int sourceRegionId);
        bool buildFound = MapNavigationQuery.TryGetNavigationHeight(
            buildContext,
            worldPoint,
            0f,
            previousTransitionId,
            previousRegionId,
            out float buildHeight,
            out _,
            out int buildTransitionId,
            out int buildRegionId);

        if (sourceFound != buildFound)
        {
            results.Add($"[BuildDataContext] {pointLabel} height found mismatch. Source={sourceFound}, BuildData={buildFound}.");
            return;
        }

        if (!sourceFound)
            return;

        if (!NearlyEqual(sourceHeight, buildHeight)
            || sourceTransitionId != buildTransitionId
            || sourceRegionId != buildRegionId)
        {
            results.Add($"[BuildDataContext] {pointLabel} height result mismatch. Source h={sourceHeight:0.###}, t={sourceTransitionId}, r={sourceRegionId}; BuildData h={buildHeight:0.###}, t={buildTransitionId}, r={buildRegionId}.");
        }
    }

    private static void ValidateInsideNavigationSpaceParity(
        MapNavigationQueryContext sourceContext,
        MapNavigationBuildDataContext buildContext,
        IReadOnlyList<MapNavRegion> sourceRegions,
        IReadOnlyList<MapNavTransition> sourceTransitions,
        List<string> results)
    {
        for (int i = 0; i < sourceRegions.Count; i++)
        {
            MapNavRegion region = sourceRegions[i];
            if (region == null)
                continue;

            CompareInsideNavigationSpace(sourceContext, buildContext, sourceContext.ToWorld(region, sourceContext.GetRegionCenter(region)), $"Region {region.Id} center", results);

            IReadOnlyList<MapNavObstacle> obstacles = sourceContext.GetRegionObstacles(region);
            for (int o = 0; o < obstacles.Count; o++)
            {
                IReadOnlyList<Vector2> points = sourceContext.GetObstaclePoints(obstacles[o]);
                if (points.Count > 0)
                    CompareInsideNavigationSpace(sourceContext, buildContext, sourceContext.ToWorld(region, MapNavGeometry.AveragePoint(points)), $"Region {region.Id} obstacle {o} center", results);
            }
        }

        for (int i = 0; i < sourceTransitions.Count; i++)
        {
            MapNavTransition transition = sourceTransitions[i];
            if (transition == null)
                continue;

            Vector2 center = MapNavGeometry.AveragePoint(sourceContext.GetTransitionPoints(transition));
            CompareInsideNavigationSpace(sourceContext, buildContext, sourceContext.ToWorld(transition, center), $"Transition {transition.Id} center", results);
        }
    }

    private static void CompareInsideNavigationSpace(
        MapNavigationQueryContext sourceContext,
        MapNavigationBuildDataContext buildContext,
        Vector3 worldPoint,
        string pointLabel,
        List<string> results)
    {
        bool sourceInside = MapNavigationQuery.IsInsideNavigationSpace(sourceContext, worldPoint, 0f);
        bool buildInside = MapNavigationQuery.IsInsideNavigationSpace(buildContext, worldPoint, 0f);
        if (sourceInside != buildInside)
            results.Add($"[BuildDataContext] {pointLabel} inside navigation mismatch. Source={sourceInside}, BuildData={buildInside}.");
    }

    private static void ValidateObstacleProjectionParity(
        MapNavigationQueryContext sourceContext,
        MapNavigationBuildDataContext buildContext,
        IReadOnlyList<MapNavRegion> sourceRegions,
        List<string> results)
    {
        for (int r = 0; r < sourceRegions.Count; r++)
        {
            MapNavRegion region = sourceRegions[r];
            if (region == null || !buildContext.TryFindRegion(region.Id, out MapNavRegionData regionData))
                continue;

            Vector3 referenceWorld = sourceContext.ToWorld(region, sourceContext.GetRegionCenter(region));
            IReadOnlyList<MapNavObstacle> obstacles = sourceContext.GetRegionObstacles(region);
            IReadOnlyList<MapNavObstacleData> buildObstacles = buildContext.GetRegionObstacles(regionData);
            for (int o = 0; o < obstacles.Count; o++)
            {
                if (obstacles[o] == null || o >= buildObstacles.Count)
                    continue;

                IReadOnlyList<Vector2> points = sourceContext.GetObstaclePoints(obstacles[o]);
                if (points.Count < 3)
                    continue;

                Vector3 obstacleWorld = sourceContext.ToWorld(region, MapNavGeometry.AveragePoint(points));
                CompareObstacleProjection(sourceContext, buildContext, region, regionData, obstacleWorld, referenceWorld, $"Region {region.Id} obstacle {o} center", results);
            }
        }
    }

    private static void CompareObstacleProjection(
        MapNavigationQueryContext sourceContext,
        MapNavigationBuildDataContext buildContext,
        MapNavRegion sourceRegion,
        MapNavRegionData buildRegion,
        Vector3 worldPoint,
        Vector3 referenceWorld,
        string pointLabel,
        List<string> results)
    {
        const float Padding = 0.1f;
        bool sourceFound = MapNavigationQuery.TryProjectOutOfObstacles(
            sourceContext,
            sourceRegion,
            worldPoint,
            referenceWorld,
            Padding,
            out Vector3 sourceProjected,
            out _,
            out float sourceDistance);
        bool buildFound = MapNavigationQuery.TryProjectOutOfObstacles(
            buildContext,
            buildRegion,
            worldPoint,
            referenceWorld,
            Padding,
            out Vector3 buildProjected,
            out _,
            out float buildDistance);

        if (sourceFound != buildFound)
        {
            results.Add($"[BuildDataContext] {pointLabel} obstacle projection found mismatch. Source={sourceFound}, BuildData={buildFound}.");
            return;
        }

        if (!sourceFound)
            return;

        if (!NearlyEqual(sourceDistance, buildDistance) || !NearlyEqualPlanar(sourceProjected, buildProjected))
        {
            results.Add($"[BuildDataContext] {pointLabel} obstacle projection mismatch. Source={sourceProjected:F3} d={sourceDistance:0.###}, BuildData={buildProjected:F3} d={buildDistance:0.###}.");
        }
    }

    private static void ValidateRegionBuildData(MapNavigationBuildData buildData, IReadOnlyList<MapNavRegion> sourceRegions, List<string> results)
    {
        for (int i = 0; i < sourceRegions.Count; i++)
        {
            MapNavRegion source = sourceRegions[i];
            if (source == null)
                continue;

            if (!buildData.TryGetRegion(source.Id, out MapNavRegionData data))
            {
                results.Add($"[BuildData] Missing region {source.Id}.");
                continue;
            }

            ValidatePointRange(buildData, data.PointStart, data.PointCount, $"Region {source.Id}", results);
            ValidateObstacleRange(buildData, data.ObstacleStart, data.ObstacleCount, $"Region {source.Id}", results);

            if (data.PointCount != GetCount(source.Points))
                results.Add($"[BuildData] Region {source.Id} point count {data.PointCount} does not match source count {GetCount(source.Points)}.");

            if (data.ObstacleCount != GetCount(source.Obstacles))
                results.Add($"[BuildData] Region {source.Id} obstacle count {data.ObstacleCount} does not match source count {GetCount(source.Obstacles)}.");
        }
    }

    private static void ValidateTransitionBuildData(MapNavigationBuildData buildData, IReadOnlyList<MapNavTransition> sourceTransitions, List<string> results)
    {
        for (int i = 0; i < sourceTransitions.Count; i++)
        {
            MapNavTransition source = sourceTransitions[i];
            if (source == null)
                continue;

            if (!buildData.TryGetTransition(source.Id, out MapNavTransitionData data))
            {
                results.Add($"[BuildData] Missing transition {source.Id}.");
                continue;
            }

            ValidatePointRange(buildData, data.PointStart, data.PointCount, $"Transition {source.Id}", results);

            if (data.PointCount != GetCount(source.Points))
                results.Add($"[BuildData] Transition {source.Id} point count {data.PointCount} does not match source count {GetCount(source.Points)}.");
        }
    }

    private static void ValidateObstacleBuildData(MapNavigationBuildData buildData, IReadOnlyList<MapNavRegion> sourceRegions, List<string> results)
    {
        for (int r = 0; r < sourceRegions.Count; r++)
        {
            MapNavRegion region = sourceRegions[r];
            if (region == null || region.Obstacles == null)
                continue;

            if (!buildData.TryGetRegion(region.Id, out MapNavRegionData regionData))
                continue;

            IReadOnlyList<MapNavObstacleData> obstacleData = buildData.GetRegionObstacles(regionData);
            for (int o = 0; o < region.Obstacles.Count; o++)
            {
                MapNavObstacle source = region.Obstacles[o];
                if (source == null || o >= obstacleData.Count)
                    continue;

                MapNavObstacleData data = obstacleData[o];
                ValidatePointRange(buildData, data.PointStart, data.PointCount, $"Region {region.Id} obstacle {o}", results);

                if (data.RegionId != region.Id)
                    results.Add($"[BuildData] Region {region.Id} obstacle {o} has RegionId {data.RegionId}.");

                if (data.PointCount != GetCount(source.Points))
                    results.Add($"[BuildData] Region {region.Id} obstacle {o} point count {data.PointCount} does not match source count {GetCount(source.Points)}.");
            }
        }
    }

    private static void ValidatePointRange(MapNavigationBuildData buildData, int start, int count, string label, List<string> results)
    {
        if (!IsValidRange(start, count, buildData.Points.Length))
            results.Add($"[BuildData] {label} point range start={start}, count={count} is out of bounds for Points length {buildData.Points.Length}.");
    }

    private static void ValidateObstacleRange(MapNavigationBuildData buildData, int start, int count, string label, List<string> results)
    {
        if (!IsValidRange(start, count, buildData.Obstacles.Length))
            results.Add($"[BuildData] {label} obstacle range start={start}, count={count} is out of bounds for Obstacles length {buildData.Obstacles.Length}.");
    }

    private static int CountNonNull<T>(IReadOnlyList<T> items) where T : class
    {
        if (items == null)
            return 0;

        int count = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null)
                count++;
        }

        return count;
    }

    private static int CountNonNullObstacles(IReadOnlyList<MapNavRegion> regions)
    {
        if (regions == null)
            return 0;

        int count = 0;
        for (int i = 0; i < regions.Count; i++)
        {
            if (regions[i]?.Obstacles == null)
                continue;

            count += CountNonNull(regions[i].Obstacles);
        }

        return count;
    }

    private static int GetCount<T>(IReadOnlyList<T> items)
    {
        return items?.Count ?? 0;
    }

    private static bool IsValidRange(int start, int count, int length)
    {
        return start >= 0 && count >= 0 && start <= length && count <= length - start;
    }

    private static bool NearlyEqual(float a, float b)
    {
        return Mathf.Abs(a - b) <= 0.001f;
    }

    private static bool NearlyEqualPlanar(Vector3 a, Vector3 b)
    {
        return NearlyEqual(a.x, b.x) && NearlyEqual(a.z, b.z) && NearlyEqual(a.y, b.y);
    }

    private static bool FromByte(byte value)
    {
        return value != 0;
    }

    private static bool HasSelfIntersection(IReadOnlyList<Vector2> points)
    {
        if (points == null || points.Count < 4)
            return false;

        for (int a = 0; a < points.Count; a++)
        {
            int b = (a + 1) % points.Count;

            for (int c = a + 1; c < points.Count; c++)
            {
                int d = (c + 1) % points.Count;
                if (a == c || a == d || b == c || b == d)
                    continue;

                if (MapNavGeometry.SegmentsIntersect(points[a], points[b], points[c], points[d]))
                    return true;
            }
        }

        return false;
    }
}
