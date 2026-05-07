using System.Collections.Generic;
using UnityEngine;

public static class MapNavigationQuery
{
    public enum InternalPathResult
    {
        Direct,
        PathFound,
        Failed
    }

    public readonly struct PathStep
    {
        public readonly int FromRegionId;
        public readonly int ToRegionId;
        public readonly int TransitionId;
        public readonly bool IsForward;
        public readonly Vector3 EntryWorld;
        public readonly Vector3 ExitWorld;
        public readonly bool UsesTransition;

        public PathStep(int fromRegionId, int toRegionId, int transitionId, bool isForward)
        {
            FromRegionId = fromRegionId;
            ToRegionId = toRegionId;
            TransitionId = transitionId;
            IsForward = isForward;
            EntryWorld = default;
            ExitWorld = default;
            UsesTransition = true;
        }

        public PathStep(int fromRegionId, int toRegionId, Vector3 entryWorld, Vector3 exitWorld)
        {
            FromRegionId = fromRegionId;
            ToRegionId = toRegionId;
            TransitionId = -1;
            IsForward = true;
            EntryWorld = entryWorld;
            ExitWorld = exitWorld;
            UsesTransition = false;
        }
    }

    public static bool IsInsideNavigationSpace(MapNavigationAuthoring navigation, Vector3 worldPosition)
    {
        return IsInsideNavigationSpace(navigation, worldPosition, 0f);
    }

    public static bool IsInsideNavigationSpace(MapNavigationAuthoring navigation, Vector3 worldPosition, float tolerance)
    {
        if (navigation == null)
            return true;

        return IsInsideNavigationSpace(navigation.QueryContext, worldPosition, tolerance);
    }

    public static bool IsInsideNavigationSpace(MapNavigationQueryContext context, Vector3 worldPosition, float tolerance)
    {
        if (!context.IsValid)
            return true;

        if (TryFindBestTransition(context, worldPosition, tolerance, out _))
            return true;

        if (!TryFindBestRegion(context, worldPosition, tolerance, out MapNavRegion region))
            return false;

        return !IsInsideRegionObstacle(context, region, worldPosition);
    }

    public static bool IsInsideNavigationSpace(MapNavigationBuildDataContext context, Vector3 worldPosition, float tolerance)
    {
        if (!context.IsValid)
            return true;

        if (TryFindBestTransition(context, worldPosition, tolerance, out _))
            return true;

        if (!TryFindBestRegion(context, worldPosition, tolerance, out MapNavRegionData region))
            return false;

        return !IsInsideRegionObstacle(context, region, worldPosition);
    }

    public static bool TryProjectToClosestNavigationSpace(
        MapNavigationAuthoring navigation,
        Vector3 worldPosition,
        out Vector3 projectedWorldPosition,
        out string spaceName,
        out float planarDistance)
    {
        projectedWorldPosition = worldPosition;
        spaceName = "Outside";
        planarDistance = 0f;

        return navigation != null
            && TryProjectToClosestNavigationSpace(navigation.QueryContext, worldPosition, out projectedWorldPosition, out spaceName, out planarDistance);
    }

    public static bool TryProjectToClosestNavigationSpace(
        MapNavigationQueryContext context,
        Vector3 worldPosition,
        out Vector3 projectedWorldPosition,
        out string spaceName,
        out float planarDistance)
    {
        projectedWorldPosition = worldPosition;
        spaceName = "Outside";
        planarDistance = 0f;

        if (!context.IsValid)
            return false;

        Vector2 localPoint = context.ToLocal2D(worldPosition);
        float bestSqrDistance = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < context.TransitionCount; i++)
        {
            MapNavTransition transition = context.GetTransitionAt(i);
            if (!context.HasEnoughTransitionPoints(transition, 2))
                continue;

            Vector2 closest = context.GetClosestPointOnTransition(transition, localPoint);
            float sqrDistance = (closest - localPoint).sqrMagnitude;
            if (sqrDistance >= bestSqrDistance)
                continue;

            bestSqrDistance = sqrDistance;
            projectedWorldPosition = context.ToWorld(transition, closest);
            spaceName = context.GetTransitionDisplayName(transition);
            found = true;
        }

        for (int i = 0; i < context.RegionCount; i++)
        {
            MapNavRegion region = context.GetRegionAt(i);
            if (!context.HasEnoughRegionPoints(region, 2))
                continue;

            Vector2 closest = context.ContainsRegion(region, localPoint)
                ? localPoint
                : context.GetClosestPointOnRegion(region, localPoint);
            float sqrDistance = (closest - localPoint).sqrMagnitude;
            if (sqrDistance >= bestSqrDistance)
                continue;

            bestSqrDistance = sqrDistance;
            projectedWorldPosition = context.ToWorld(region, closest);
            spaceName = context.GetRegionDisplayName(region);
            found = true;
        }

        planarDistance = found ? Mathf.Sqrt(bestSqrDistance) : 0f;
        return found;
    }

    public static bool TryProjectToClosestNavigationSpace(
        MapNavigationBuildDataContext context,
        Vector3 worldPosition,
        out Vector3 projectedWorldPosition,
        out string spaceName,
        out float planarDistance)
    {
        projectedWorldPosition = worldPosition;
        spaceName = "Outside";
        planarDistance = 0f;

        if (!context.IsValid)
            return false;

        Vector2 localPoint = context.ToLocal2D(worldPosition);
        float bestSqrDistance = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < context.TransitionCount; i++)
        {
            if (!context.TryGetTransitionAt(i, out MapNavTransitionData transition) || !context.HasEnoughTransitionPoints(transition, 2))
                continue;

            Vector2 closest = context.GetClosestPointOnTransition(transition, localPoint);
            float sqrDistance = (closest - localPoint).sqrMagnitude;
            if (sqrDistance >= bestSqrDistance)
                continue;

            bestSqrDistance = sqrDistance;
            projectedWorldPosition = context.ToWorld(transition, closest);
            spaceName = $"Transition {transition.Id}";
            found = true;
        }

        for (int i = 0; i < context.RegionCount; i++)
        {
            if (!context.TryGetRegionAt(i, out MapNavRegionData region) || !context.HasEnoughRegionPoints(region, 2))
                continue;

            Vector2 closest = context.ContainsRegion(region, localPoint)
                ? localPoint
                : context.GetClosestPointOnRegion(region, localPoint);
            float sqrDistance = (closest - localPoint).sqrMagnitude;
            if (sqrDistance >= bestSqrDistance)
                continue;

            bestSqrDistance = sqrDistance;
            projectedWorldPosition = context.ToWorld(region, closest);
            spaceName = $"Region {region.Id}";
            found = true;
        }

        planarDistance = found ? Mathf.Sqrt(bestSqrDistance) : 0f;
        return found;
    }

    public static bool TryProjectOutOfObstacles(
        MapNavigationAuthoring navigation,
        MapNavRegion region,
        Vector3 worldPosition,
        float padding,
        out Vector3 projectedWorldPosition,
        out string obstacleName,
        out float planarDistance)
    {
        return TryProjectOutOfObstacles(
            navigation,
            region,
            worldPosition,
            worldPosition,
            padding,
            out projectedWorldPosition,
            out obstacleName,
            out planarDistance);
    }

    public static bool TryProjectOutOfObstacles(
        MapNavigationAuthoring navigation,
        MapNavRegion region,
        Vector3 worldPosition,
        Vector3 referenceWorldPosition,
        float padding,
        out Vector3 projectedWorldPosition,
        out string obstacleName,
        out float planarDistance)
    {
        projectedWorldPosition = worldPosition;
        obstacleName = "None";
        planarDistance = 0f;

        if (navigation == null)
            return false;

        return TryProjectOutOfObstacles(
            navigation.QueryContext,
            region,
            worldPosition,
            referenceWorldPosition,
            padding,
            out projectedWorldPosition,
            out obstacleName,
            out planarDistance);
    }

    public static bool TryProjectOutOfObstacles(
        MapNavigationQueryContext context,
        MapNavRegion region,
        Vector3 worldPosition,
        Vector3 referenceWorldPosition,
        float padding,
        out Vector3 projectedWorldPosition,
        out string obstacleName,
        out float planarDistance)
    {
        projectedWorldPosition = worldPosition;
        obstacleName = "None";
        planarDistance = 0f;

        IReadOnlyList<MapNavObstacle> obstacles = context.GetRegionObstacles(region);
        if (!context.IsValid || region == null || obstacles == null)
            return false;

        Vector2 localPoint = context.ToLocal2D(worldPosition);
        Vector2 referencePoint = context.ToLocal2D(referenceWorldPosition);
        float bestSqrDistance = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < obstacles.Count; i++)
        {
            MapNavObstacle obstacle = obstacles[i];
            if (!context.HasEnoughObstaclePoints(obstacle, 2) || !context.ContainsObstacle(obstacle, localPoint))
                continue;

            Vector2 closest = context.GetClosestPointOnObstacle(obstacle, localPoint);
            float resolvedPadding = Mathf.Max(padding, obstacle.CornerPadding);
            if (!TryFindNearestClearPoint(context, region, obstacles, closest, localPoint, referencePoint, resolvedPadding, out Vector2 projected))
                continue;

            float sqrDistance = (projected - referencePoint).sqrMagnitude + ((projected - localPoint).sqrMagnitude * 0.15f);
            if (sqrDistance >= bestSqrDistance)
                continue;

            bestSqrDistance = sqrDistance;
            projectedWorldPosition = context.ToWorld(region, projected);
            obstacleName = $"Obstacle {i}";
            found = true;
        }

        planarDistance = found ? Mathf.Sqrt(bestSqrDistance) : 0f;
        return found;
    }

    public static bool TryProjectOutOfObstacles(
        MapNavigationBuildDataContext context,
        MapNavRegionData region,
        Vector3 worldPosition,
        Vector3 referenceWorldPosition,
        float padding,
        out Vector3 projectedWorldPosition,
        out string obstacleName,
        out float planarDistance)
    {
        projectedWorldPosition = worldPosition;
        obstacleName = "None";
        planarDistance = 0f;

        if (!context.IsValid)
            return false;

        IReadOnlyList<MapNavObstacleData> obstacles = context.GetRegionObstacles(region);
        Vector2 localPoint = context.ToLocal2D(worldPosition);
        Vector2 referencePoint = context.ToLocal2D(referenceWorldPosition);
        float bestSqrDistance = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < obstacles.Count; i++)
        {
            MapNavObstacleData obstacle = obstacles[i];
            if (!context.HasEnoughObstaclePoints(obstacle, 2) || !context.ContainsObstacle(obstacle, localPoint))
                continue;

            Vector2 closest = context.GetClosestPointOnObstacle(obstacle, localPoint);
            float resolvedPadding = Mathf.Max(padding, obstacle.CornerPadding);
            if (!TryFindNearestClearPoint(context, region, obstacles, closest, localPoint, referencePoint, resolvedPadding, out Vector2 projected))
                continue;

            float sqrDistance = (projected - referencePoint).sqrMagnitude + ((projected - localPoint).sqrMagnitude * 0.15f);
            if (sqrDistance >= bestSqrDistance)
                continue;

            bestSqrDistance = sqrDistance;
            projectedWorldPosition = context.ToWorld(region, projected);
            obstacleName = $"Obstacle {i}";
            found = true;
        }

        planarDistance = found ? Mathf.Sqrt(bestSqrDistance) : 0f;
        return found;
    }

    public static bool IsInsideRegionObstacle(MapNavigationAuthoring navigation, MapNavRegion region, Vector3 worldPosition)
    {
        return navigation != null && IsInsideRegionObstacle(navigation.QueryContext, region, worldPosition);
    }

    public static bool IsInsideRegionObstacle(MapNavigationQueryContext context, MapNavRegion region, Vector3 worldPosition)
    {
        if (!context.IsValid || region == null)
            return false;

        Vector2 localPoint = context.ToLocal2D(worldPosition);
        return IsInsideAnyObstacle(localPoint, context.GetRegionObstacles(region));
    }

    public static bool IsInsideRegionObstacle(MapNavigationBuildDataContext context, MapNavRegionData region, Vector3 worldPosition)
    {
        if (!context.IsValid)
            return false;

        Vector2 localPoint = context.ToLocal2D(worldPosition);
        return IsInsideAnyObstacle(context, localPoint, context.GetRegionObstacles(region));
    }

    public static bool IsInsideAnyObstacle(MapNavigationAuthoring navigation, Vector3 worldPosition, float tolerance)
    {
        return navigation != null && IsInsideAnyObstacle(navigation.QueryContext, worldPosition, tolerance);
    }

    public static bool IsInsideAnyObstacle(MapNavigationQueryContext context, Vector3 worldPosition, float tolerance)
    {
        if (!context.IsValid)
            return false;

        if (!TryFindBestRegion(context, worldPosition, tolerance, out MapNavRegion region))
            return false;

        return IsInsideRegionObstacle(context, region, worldPosition);
    }

    public static bool IsInsideAnyObstacle(MapNavigationBuildDataContext context, Vector3 worldPosition, float tolerance)
    {
        if (!context.IsValid)
            return false;

        if (!TryFindBestRegion(context, worldPosition, tolerance, out MapNavRegionData region))
            return false;

        return IsInsideRegionObstacle(context, region, worldPosition);
    }

    public static bool TryProjectOutOfAnyObstacle(
        MapNavigationAuthoring navigation,
        Vector3 worldPosition,
        float tolerance,
        float padding,
        out Vector3 projectedWorldPosition)
    {
        projectedWorldPosition = worldPosition;

        if (navigation == null)
            return false;

        return TryProjectOutOfAnyObstacle(navigation.QueryContext, worldPosition, tolerance, padding, out projectedWorldPosition);
    }

    public static bool TryProjectOutOfAnyObstacle(
        MapNavigationQueryContext context,
        Vector3 worldPosition,
        float tolerance,
        float padding,
        out Vector3 projectedWorldPosition)
    {
        projectedWorldPosition = worldPosition;

        if (!context.IsValid)
            return false;

        if (!TryFindBestRegion(context, worldPosition, tolerance, out MapNavRegion region))
            return false;

        return TryProjectOutOfObstacles(context, region, worldPosition, worldPosition, padding, out projectedWorldPosition, out _, out _);
    }

    public static bool TryProjectOutOfAnyObstacle(
        MapNavigationBuildDataContext context,
        Vector3 worldPosition,
        float tolerance,
        float padding,
        out Vector3 projectedWorldPosition)
    {
        projectedWorldPosition = worldPosition;

        if (!context.IsValid)
            return false;

        if (!TryFindBestRegion(context, worldPosition, tolerance, out MapNavRegionData region))
            return false;

        return TryProjectOutOfObstacles(context, region, worldPosition, worldPosition, padding, out projectedWorldPosition, out _, out _);
    }

    public static MapNavRegion FindContainingRegion(MapNavigationAuthoring navigation, Vector3 worldPosition)
    {
        return FindContainingRegion(navigation, worldPosition, 0f);
    }

    public static MapNavRegion FindContainingRegion(MapNavigationAuthoring navigation, Vector3 worldPosition, float tolerance)
    {
        return navigation != null ? FindContainingRegion(navigation.QueryContext, worldPosition, tolerance) : null;
    }

    public static MapNavRegion FindContainingRegion(MapNavigationQueryContext context, Vector3 worldPosition, float tolerance)
    {
        if (!context.IsValid)
            return null;

        if (TryFindBestRegion(context, worldPosition, tolerance, out MapNavRegion region))
            return region;

        if (!TryFindBestTransition(context, worldPosition, tolerance, out MapNavTransition transition))
            return null;

        return context.FindRegion(transition.ToRegionId) ?? context.FindRegion(transition.FromRegionId);
    }

    public static bool TryFindContainingRegion(
        MapNavigationBuildDataContext context,
        Vector3 worldPosition,
        float tolerance,
        out MapNavRegionData region)
    {
        region = default;
        if (!context.IsValid)
            return false;

        Vector2 localPoint = context.ToLocal2D(worldPosition);
        for (int i = 0; i < context.RegionCount; i++)
        {
            if (!context.TryGetRegionAt(i, out MapNavRegionData candidate))
                continue;

            if (!context.ContainsRegion(candidate, localPoint, tolerance))
                continue;

            region = candidate;
            return true;
        }

        for (int i = 0; i < context.TransitionCount; i++)
        {
            if (!context.TryGetTransitionAt(i, out MapNavTransitionData transition))
                continue;

            if (!context.ContainsTransition(transition, localPoint, tolerance))
                continue;

            if (context.TryFindRegion(transition.ToRegionId, out region)
                || context.TryFindRegion(transition.FromRegionId, out region))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryGetNavigationHeight(MapNavigationAuthoring navigation, Vector3 worldPosition, out float height, out string spaceName)
    {
        return TryGetNavigationHeight(navigation, worldPosition, 0f, -1, -1, out height, out spaceName, out _, out _);
    }

    public static bool TryGetNavigationHeight(
        MapNavigationAuthoring navigation,
        Vector3 worldPosition,
        float tolerance,
        int previousTransitionId,
        int previousRegionId,
        out float height,
        out string spaceName,
        out int transitionId,
        out int regionId)
    {
        if (navigation == null)
        {
            height = 0f;
            spaceName = "No Navigation";
            transitionId = -1;
            regionId = -1;
            return false;
        }

        return TryGetNavigationHeight(navigation.QueryContext, worldPosition, tolerance, previousTransitionId, previousRegionId, out height, out spaceName, out transitionId, out regionId);
    }

    public static bool TryGetNavigationHeight(
        MapNavigationQueryContext context,
        Vector3 worldPosition,
        float tolerance,
        int previousTransitionId,
        int previousRegionId,
        out float height,
        out string spaceName,
        out int transitionId,
        out int regionId)
    {
        if (!context.IsValid)
        {
            height = 0f;
            spaceName = "No Navigation";
            transitionId = -1;
            regionId = -1;
            return false;
        }

        Vector3 local = context.ToLocal3D(worldPosition);
        Vector2 localPoint = new(local.x, local.z);
        MapNavTransition previousTransition = context.FindTransition(previousTransitionId);
        if (context.ContainsTransition(previousTransition, localPoint, tolerance))
        {
            float previousHeight = context.GetTransitionHeight(previousTransition, localPoint);
            if (Mathf.Abs(local.y - previousHeight) <= 1.5f)
            {
                height = previousHeight;
                spaceName = context.GetTransitionDisplayName(previousTransition);
                transitionId = previousTransition.Id;
                regionId = -1;
                return true;
            }
        }

        MapNavRegion previousRegion = context.FindRegion(previousRegionId);
        if (context.ContainsRegion(previousRegion, localPoint, tolerance))
        {
            float previousHeight = context.GetRegionHeight(previousRegion, localPoint);
            if (Mathf.Abs(local.y - previousHeight) <= 1.5f)
            {
                height = previousHeight;
                spaceName = context.GetRegionDisplayName(previousRegion);
                transitionId = -1;
                regionId = previousRegion.Id;
                return true;
            }
        }

        if (TryFindBestTransition(context, worldPosition, tolerance, out MapNavTransition transition))
        {
            height = context.GetTransitionHeight(transition, localPoint);
            spaceName = context.GetTransitionDisplayName(transition);
            transitionId = transition.Id;
            regionId = -1;
            return true;
        }

        if (TryFindBestRegion(context, worldPosition, tolerance, out MapNavRegion region))
        {
            height = context.GetRegionHeight(region, localPoint);
            spaceName = context.GetRegionDisplayName(region);
            transitionId = -1;
            regionId = region.Id;
            return true;
        }

        height = 0f;
        spaceName = "Outside";
        transitionId = -1;
        regionId = -1;
        return false;
    }

    public static bool TryGetNavigationHeight(
        MapNavigationBuildDataContext context,
        Vector3 worldPosition,
        float tolerance,
        int previousTransitionId,
        int previousRegionId,
        out float height,
        out string spaceName,
        out int transitionId,
        out int regionId)
    {
        if (!context.IsValid)
        {
            height = 0f;
            spaceName = "No Navigation";
            transitionId = -1;
            regionId = -1;
            return false;
        }

        Vector3 local = context.ToLocal3D(worldPosition);
        Vector2 localPoint = new(local.x, local.z);
        if (context.TryFindTransition(previousTransitionId, out MapNavTransitionData previousTransition)
            && context.ContainsTransition(previousTransition, localPoint, tolerance))
        {
            float previousHeight = context.GetTransitionHeight(previousTransition, localPoint);
            if (Mathf.Abs(local.y - previousHeight) <= 1.5f)
            {
                height = previousHeight;
                spaceName = $"Transition {previousTransition.Id}";
                transitionId = previousTransition.Id;
                regionId = -1;
                return true;
            }
        }

        if (context.TryFindRegion(previousRegionId, out MapNavRegionData previousRegion)
            && context.ContainsRegion(previousRegion, localPoint, tolerance))
        {
            float previousHeight = context.GetRegionHeight(previousRegion, localPoint);
            if (Mathf.Abs(local.y - previousHeight) <= 1.5f)
            {
                height = previousHeight;
                spaceName = $"Region {previousRegion.Id}";
                transitionId = -1;
                regionId = previousRegion.Id;
                return true;
            }
        }

        if (TryFindBestTransition(context, worldPosition, tolerance, out MapNavTransitionData transition))
        {
            height = context.GetTransitionHeight(transition, localPoint);
            spaceName = $"Transition {transition.Id}";
            transitionId = transition.Id;
            regionId = -1;
            return true;
        }

        if (TryFindBestRegion(context, worldPosition, tolerance, out MapNavRegionData region))
        {
            height = context.GetRegionHeight(region, localPoint);
            spaceName = $"Region {region.Id}";
            transitionId = -1;
            regionId = region.Id;
            return true;
        }

        height = 0f;
        spaceName = "Outside";
        transitionId = -1;
        regionId = -1;
        return false;
    }

    public static bool TryFindRegionPath(
        MapNavigationAuthoring navigation,
        int startRegionId,
        int goalRegionId,
        Vector3 startWorldPosition,
        Vector3 worldTarget,
        float agentRadius,
        out List<PathStep> path)
    {
        if (navigation == null)
        {
            path = new List<PathStep>();
            return false;
        }

        return TryFindRegionPath(
            navigation.QueryContext,
            startRegionId,
            goalRegionId,
            startWorldPosition,
            worldTarget,
            agentRadius,
            out path);
    }

    public static bool TryFindRegionPath(
        MapNavigationQueryContext context,
        int startRegionId,
        int goalRegionId,
        Vector3 startWorldPosition,
        Vector3 worldTarget,
        float agentRadius,
        out List<PathStep> path)
    {
        path = new List<PathStep>();

        if (!context.IsValid)
            return false;

        List<int> open = new();
        HashSet<int> openSet = new();
        HashSet<int> closed = new();
        Dictionary<int, float> bestCost = new();
        Dictionary<int, PathStep> cameFrom = new();
        Dictionary<int, Vector3> endPositions = new();

        open.Add(startRegionId);
        openSet.Add(startRegionId);
        bestCost[startRegionId] = 0f;
        endPositions[startRegionId] = startWorldPosition;

        while (open.Count > 0)
        {
            int current = PopCheapest(open, bestCost, goalRegionId, worldTarget, endPositions);
            openSet.Remove(current);
            if (!closed.Add(current))
                continue;

            IReadOnlyList<MapNavigationRuntimeData.RegionConnection> connections = context.GetConnectionsForRegion(current);
            for (int i = 0; i < connections.Count; i++)
            {
                if (!TryEvaluateConnection(
                    context,
                    connections[i],
                    current,
                    goalRegionId,
                    endPositions[current],
                    worldTarget,
                    agentRadius,
                    out int nextRegionId,
                    out float edgeCost,
                    out Vector3 exit,
                    out PathStep pathStep))
                {
                    continue;
                }

                float nextCost = bestCost[current] + edgeCost;
                if (bestCost.TryGetValue(nextRegionId, out float knownCost) && nextCost >= knownCost)
                    continue;

                cameFrom[nextRegionId] = pathStep;
                bestCost[nextRegionId] = nextCost;
                endPositions[nextRegionId] = exit;

                if (openSet.Add(nextRegionId))
                    open.Add(nextRegionId);
            }
        }

        if (!bestCost.ContainsKey(goalRegionId))
            return false;

        int cursor = goalRegionId;
        while (cursor != startRegionId)
        {
            PathStep step = cameFrom[cursor];
            path.Add(step);
            cursor = step.FromRegionId;
        }

        path.Reverse();
        return path.Count > 0;
    }

    public static InternalPathResult FindInternalRegionPath(
        MapNavigationAuthoring navigation,
        MapNavRegion region,
        Vector3 startWorldPosition,
        Vector3 targetWorldPosition,
        float agentRadius,
        out List<Vector3> waypoints)
    {
        if (navigation == null || region == null)
        {
            waypoints = new List<Vector3>();
            return InternalPathResult.Failed;
        }

        return FindInternalRegionPath(navigation.QueryContext, region, startWorldPosition, targetWorldPosition, agentRadius, out waypoints);
    }

    public static InternalPathResult FindInternalRegionPath(
        MapNavigationQueryContext context,
        MapNavRegion region,
        Vector3 startWorldPosition,
        Vector3 targetWorldPosition,
        float agentRadius,
        out List<Vector3> waypoints)
    {
        waypoints = new List<Vector3>();

        if (!context.IsValid || region == null)
            return InternalPathResult.Failed;

        Vector2 start = context.ToLocal2D(startWorldPosition);
        Vector2 target = context.ToLocal2D(targetWorldPosition);
        IReadOnlyList<MapNavObstacle> obstacles = context.GetRegionObstacles(region);
        if (IsInsideAnyObstacle(start, obstacles))
            return InternalPathResult.Failed;

        if (IsInsideAnyObstacle(target, obstacles))
            return InternalPathResult.Failed;

        if (obstacles.Count == 0 || HasLineOfSight(start, target, obstacles))
            return InternalPathResult.Direct;

        List<Vector2> nodes = new() { start, target };
        for (int i = 0; i < obstacles.Count; i++)
        {
            MapNavObstacle obstacle = obstacles[i];
            IReadOnlyList<Vector2> obstaclePoints = context.GetObstaclePoints(obstacle);
            if (obstaclePoints.Count < 3)
                continue;

            Vector2 center = MapNavGeometry.AveragePoint(obstaclePoints);
            float padding = Mathf.Max(0f, agentRadius) + Mathf.Max(0f, obstacle.CornerPadding);
            for (int p = 0; p < obstaclePoints.Count; p++)
            {
                Vector2 fromCenter = obstaclePoints[p] - center;
                Vector2 candidate = obstaclePoints[p] + (fromCenter.sqrMagnitude > 0.0001f ? fromCenter.normalized * padding : Vector2.zero);
                if (!context.ContainsRegion(region, candidate, 0f))
                    continue;

                if (IsInsideAnyObstacle(candidate, obstacles))
                    continue;

                nodes.Add(candidate);
            }
        }

        if (!TryFindVisibilityPath(nodes, obstacles, out List<int> pathIndices))
            return InternalPathResult.Failed;

        for (int i = 1; i < pathIndices.Count; i++)
        {
            Vector2 local = nodes[pathIndices[i]];
            waypoints.Add(context.ToWorld(region, local));
        }

        return waypoints.Count > 0 ? InternalPathResult.PathFound : InternalPathResult.Direct;
    }

    public static void GetTransitionEndpointWorld(
        MapNavigationAuthoring navigation,
        MapNavTransition transition,
        bool isForward,
        float agentRadius,
        out Vector3 entry,
        out Vector3 exit)
    {
        if (navigation == null)
        {
            entry = Vector3.zero;
            exit = Vector3.zero;
            return;
        }

        GetTransitionEndpointWorld(navigation.QueryContext, transition, isForward, agentRadius, out entry, out exit);
    }

    public static void GetTransitionEndpointWorld(
        MapNavigationQueryContext context,
        MapNavTransition transition,
        bool isForward,
        float agentRadius,
        out Vector3 entry,
        out Vector3 exit)
    {
        GetTransitionEndpointCenters(context, transition, out Vector2 fromCenter, out Vector2 toCenter);

        Vector2 entryLocal = isForward ? fromCenter : toCenter;
        Vector2 exitLocal = isForward ? toCenter : fromCenter;
        Vector2 inward = exitLocal - entryLocal;

        if (inward.sqrMagnitude > 0.0001f && agentRadius > 0f)
        {
            Vector2 offset = inward.normalized * agentRadius;
            entryLocal += offset;
            exitLocal -= offset;
        }

        entry = context.ToWorld(transition, entryLocal);
        exit = context.ToWorld(transition, exitLocal);
    }

    public static Vector2 ToNavigationLocal2D(MapNavigationAuthoring navigation, Vector3 worldPosition)
    {
        return navigation.QueryContext.ToLocal2D(worldPosition);
    }

    private static Vector2 GetBestPortalPoint(
        MapNavigationQueryContext context,
        MapNavigationRuntimeData.RegionLink link,
        Vector3 fromWorld,
        Vector3 toWorld)
    {
        Vector2 from = context.ToLocal2D(fromWorld);
        Vector2 to = context.ToLocal2D(toWorld);
        Vector2 segment = link.PortalLocalB - link.PortalLocalA;
        float sqrLength = segment.sqrMagnitude;
        if (sqrLength <= 0.000001f)
            return link.PortalLocalA;

        if (MapNavGeometry.TryLineSegmentIntersection(from, to, link.PortalLocalA, link.PortalLocalB, out Vector2 intersection))
            return intersection;

        float tFrom = Mathf.Clamp01(Vector2.Dot(from - link.PortalLocalA, segment) / sqrLength);
        float tTo = Mathf.Clamp01(Vector2.Dot(to - link.PortalLocalA, segment) / sqrLength);
        float t = (tFrom + tTo) * 0.5f;
        return link.PortalLocalA + segment * t;
    }

    private static bool TryEvaluateConnection(
        MapNavigationQueryContext context,
        MapNavigationRuntimeData.RegionConnection connection,
        int currentRegionId,
        int goalRegionId,
        Vector3 currentEnd,
        Vector3 worldTarget,
        float agentRadius,
        out int nextRegionId,
        out float edgeCost,
        out Vector3 exit,
        out PathStep pathStep)
    {
        nextRegionId = -1;
        edgeCost = 0f;
        exit = default;
        pathStep = default;

        if (connection.UsesTransition)
            return TryEvaluateTransitionConnection(context, connection.TransitionId, currentRegionId, currentEnd, agentRadius, out nextRegionId, out edgeCost, out exit, out pathStep);

        return TryEvaluateRegionLinkConnection(context, connection.Link, currentRegionId, goalRegionId, currentEnd, worldTarget, out nextRegionId, out edgeCost, out exit, out pathStep);
    }

    private static bool TryEvaluateTransitionConnection(
        MapNavigationQueryContext context,
        int transitionId,
        int currentRegionId,
        Vector3 currentEnd,
        float agentRadius,
        out int nextRegionId,
        out float edgeCost,
        out Vector3 exit,
        out PathStep pathStep)
    {
        nextRegionId = -1;
        edgeCost = 0f;
        exit = default;
        pathStep = default;

        MapNavTransition transition = context.FindTransition(transitionId);
        if (transition == null || !transition.Enabled)
            return false;

        if (!TryGetNeighbor(currentRegionId, transition, out nextRegionId, out bool isForward))
            return false;

        GetTransitionEndpointWorld(context, transition, isForward, agentRadius, out Vector3 entry, out exit);
        edgeCost = GetPlanarDistance(currentEnd, entry)
            + GetPlanarDistance(entry, exit)
            + Mathf.Max(0f, transition.Cost);
        pathStep = new PathStep(currentRegionId, nextRegionId, transition.Id, isForward);
        return true;
    }

    private static bool TryEvaluateRegionLinkConnection(
        MapNavigationQueryContext context,
        MapNavigationRuntimeData.RegionLink link,
        int currentRegionId,
        int goalRegionId,
        Vector3 currentEnd,
        Vector3 worldTarget,
        out int nextRegionId,
        out float edgeCost,
        out Vector3 exit,
        out PathStep pathStep)
    {
        nextRegionId = link.ToRegionId;
        edgeCost = 0f;
        exit = default;
        pathStep = default;

        MapNavRegion fromRegion = context.FindRegion(link.FromRegionId);
        MapNavRegion toRegion = context.FindRegion(link.ToRegionId);
        if (fromRegion == null || toRegion == null)
            return false;

        Vector2 portalLocal = GetBestPortalPoint(
            context,
            link,
            currentEnd,
            link.ToRegionId == goalRegionId ? worldTarget : context.ToWorld(toRegion, context.GetRegionCenter(toRegion)));
        Vector3 entry = context.ToWorld(fromRegion, portalLocal);
        exit = context.ToWorld(toRegion, portalLocal);
        edgeCost = GetPlanarDistance(currentEnd, entry)
            + GetPlanarDistance(entry, exit)
            + Mathf.Max(0f, link.Cost);
        pathStep = new PathStep(currentRegionId, link.ToRegionId, entry, exit);
        return true;
    }

    private static bool TryFindBestTransition(MapNavigationQueryContext context, Vector3 worldPosition, float tolerance, out MapNavTransition bestTransition)
    {
        Vector3 local = context.ToLocal3D(worldPosition);
        Vector2 localPoint = new(local.x, local.z);
        float bestHeightDelta = float.PositiveInfinity;
        bestTransition = null;

        for (int i = 0; i < context.TransitionCount; i++)
        {
            MapNavTransition transition = context.GetTransitionAt(i);
            if (!context.ContainsTransition(transition, localPoint, tolerance))
                continue;

            float height = context.GetTransitionHeight(transition, localPoint);
            float heightDelta = Mathf.Abs(local.y - height);
            if (heightDelta >= bestHeightDelta)
                continue;

            bestHeightDelta = heightDelta;
            bestTransition = transition;
        }

        return bestTransition != null;
    }

    private static bool TryFindBestRegion(MapNavigationQueryContext context, Vector3 worldPosition, float tolerance, out MapNavRegion bestRegion)
    {
        Vector3 local = context.ToLocal3D(worldPosition);
        Vector2 localPoint = new(local.x, local.z);
        float bestHeightDelta = float.PositiveInfinity;
        bestRegion = null;

        for (int i = 0; i < context.RegionCount; i++)
        {
            MapNavRegion region = context.GetRegionAt(i);
            if (!context.ContainsRegion(region, localPoint, tolerance))
                continue;

            float heightDelta = Mathf.Abs(local.y - context.GetRegionHeight(region, localPoint));
            if (heightDelta >= bestHeightDelta)
                continue;

            bestHeightDelta = heightDelta;
            bestRegion = region;
        }

        return bestRegion != null;
    }

    private static bool TryFindBestTransition(MapNavigationBuildDataContext context, Vector3 worldPosition, float tolerance, out MapNavTransitionData bestTransition)
    {
        Vector3 local = context.ToLocal3D(worldPosition);
        Vector2 localPoint = new(local.x, local.z);
        float bestHeightDelta = float.PositiveInfinity;
        bestTransition = default;
        bool found = false;

        for (int i = 0; i < context.TransitionCount; i++)
        {
            if (!context.TryGetTransitionAt(i, out MapNavTransitionData transition))
                continue;

            if (!context.ContainsTransition(transition, localPoint, tolerance))
                continue;

            float height = context.GetTransitionHeight(transition, localPoint);
            float heightDelta = Mathf.Abs(local.y - height);
            if (heightDelta >= bestHeightDelta)
                continue;

            bestHeightDelta = heightDelta;
            bestTransition = transition;
            found = true;
        }

        return found;
    }

    private static bool TryFindBestRegion(MapNavigationBuildDataContext context, Vector3 worldPosition, float tolerance, out MapNavRegionData bestRegion)
    {
        Vector3 local = context.ToLocal3D(worldPosition);
        Vector2 localPoint = new(local.x, local.z);
        float bestHeightDelta = float.PositiveInfinity;
        bestRegion = default;
        bool found = false;

        for (int i = 0; i < context.RegionCount; i++)
        {
            if (!context.TryGetRegionAt(i, out MapNavRegionData region))
                continue;

            if (!context.ContainsRegion(region, localPoint, tolerance))
                continue;

            float heightDelta = Mathf.Abs(local.y - context.GetRegionHeight(region, localPoint));
            if (heightDelta >= bestHeightDelta)
                continue;

            bestHeightDelta = heightDelta;
            bestRegion = region;
            found = true;
        }

        return found;
    }

    private static int PopCheapest(List<int> open, IReadOnlyDictionary<int, float> bestCost, int goalRegionId, Vector3 worldTarget, IReadOnlyDictionary<int, Vector3> endPositions)
    {
        int bestOpenIndex = 0;
        float bestScore = GetSearchScore(open[0], bestCost, goalRegionId, worldTarget, endPositions);

        for (int i = 1; i < open.Count; i++)
        {
            float score = GetSearchScore(open[i], bestCost, goalRegionId, worldTarget, endPositions);
            if (score >= bestScore)
                continue;

            bestScore = score;
            bestOpenIndex = i;
        }

        int regionId = open[bestOpenIndex];
        open.RemoveAt(bestOpenIndex);
        return regionId;
    }

    private static float GetSearchScore(int regionId, IReadOnlyDictionary<int, float> bestCost, int goalRegionId, Vector3 worldTarget, IReadOnlyDictionary<int, Vector3> endPositions)
    {
        float score = bestCost[regionId];

        if (regionId == goalRegionId && endPositions.TryGetValue(regionId, out Vector3 endPosition))
            score += GetPlanarDistance(endPosition, worldTarget);

        return score;
    }

    private static float GetPlanarDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static bool TryGetNeighbor(int currentRegionId, MapNavTransition transition, out int nextRegionId, out bool isForward)
    {
        if (transition.FromRegionId == currentRegionId)
        {
            nextRegionId = transition.ToRegionId;
            isForward = true;
            return true;
        }

        if (transition.Bidirectional && transition.ToRegionId == currentRegionId)
        {
            nextRegionId = transition.FromRegionId;
            isForward = false;
            return true;
        }

        nextRegionId = -1;
        isForward = true;
        return false;
    }

    public static void GetTransitionEndpointCenters(MapNavTransition transition, out Vector2 fromCenter, out Vector2 toCenter)
    {
        IReadOnlyList<Vector2> points = transition?.Points != null ? transition.Points : System.Array.Empty<Vector2>();
        Vector2 direction = transition != null && transition.UpDirection.sqrMagnitude > 0.0001f
            ? transition.UpDirection.normalized
            : Vector2.up;

        fromCenter = GetEndpointSupportCenter(points, direction, true);
        toCenter = GetEndpointSupportCenter(points, direction, false);
    }

    public static void GetTransitionEndpointCenters(MapNavigationQueryContext context, MapNavTransition transition, out Vector2 fromCenter, out Vector2 toCenter)
    {
        Vector2 direction = transition != null && transition.UpDirection.sqrMagnitude > 0.0001f
            ? transition.UpDirection.normalized
            : Vector2.up;

        IReadOnlyList<Vector2> points = context.GetTransitionPoints(transition);
        fromCenter = GetEndpointSupportCenter(points, direction, true);
        toCenter = GetEndpointSupportCenter(points, direction, false);
    }

    private static Vector2 GetEndpointSupportCenter(IReadOnlyList<Vector2> points, Vector2 direction, bool useMin)
    {
        if (points == null || points.Count == 0)
            return Vector2.zero;

        if (points.Count <= 2)
            return points[0];

        int firstIndex = 0;
        int secondIndex = points.Count > 1 ? 1 : 0;
        float firstProjection = Vector2.Dot(points[firstIndex], direction);
        float secondProjection = Vector2.Dot(points[secondIndex], direction);

        if (IsBetterProjection(secondProjection, firstProjection, useMin))
        {
            (firstIndex, secondIndex) = (secondIndex, firstIndex);
            (firstProjection, secondProjection) = (secondProjection, firstProjection);
        }

        for (int i = 2; i < points.Count; i++)
        {
            float projected = Vector2.Dot(points[i], direction);
            if (IsBetterProjection(projected, firstProjection, useMin))
            {
                secondIndex = firstIndex;
                secondProjection = firstProjection;
                firstIndex = i;
                firstProjection = projected;
                continue;
            }

            if (IsBetterProjection(projected, secondProjection, useMin))
            {
                secondIndex = i;
                secondProjection = projected;
            }
        }

        return (points[firstIndex] + points[secondIndex]) * 0.5f;
    }

    private static bool IsBetterProjection(float candidate, float current, bool useMin)
    {
        return useMin ? candidate < current : candidate > current;
    }

    private static bool TryFindVisibilityPath(IReadOnlyList<Vector2> nodes, IReadOnlyList<MapNavObstacle> obstacles, out List<int> path)
    {
        path = new List<int>();
        List<int> open = new() { 0 };
        HashSet<int> openSet = new() { 0 };
        HashSet<int> closed = new();
        Dictionary<int, float> bestCost = new() { [0] = 0f };
        Dictionary<int, int> cameFrom = new();

        while (open.Count > 0)
        {
            int current = PopCheapestNode(open, bestCost, nodes, 1);
            openSet.Remove(current);
            if (current == 1)
                break;

            if (!closed.Add(current))
                continue;

            for (int next = 0; next < nodes.Count; next++)
            {
                if (next == current || closed.Contains(next))
                    continue;

                if (!HasLineOfSight(nodes[current], nodes[next], obstacles))
                    continue;

                float nextCost = bestCost[current] + Vector2.Distance(nodes[current], nodes[next]);
                if (bestCost.TryGetValue(next, out float knownCost) && nextCost >= knownCost)
                    continue;

                bestCost[next] = nextCost;
                cameFrom[next] = current;
                if (openSet.Add(next))
                    open.Add(next);
            }
        }

        if (!bestCost.ContainsKey(1))
            return false;

        int cursor = 1;
        while (cursor != 0)
        {
            path.Add(cursor);
            cursor = cameFrom[cursor];
        }

        path.Add(0);
        path.Reverse();
        return true;
    }

    private static int PopCheapestNode(List<int> open, IReadOnlyDictionary<int, float> bestCost, IReadOnlyList<Vector2> nodes, int goalIndex)
    {
        int bestOpenIndex = 0;
        float bestScore = bestCost[open[0]] + Vector2.Distance(nodes[open[0]], nodes[goalIndex]);

        for (int i = 1; i < open.Count; i++)
        {
            float score = bestCost[open[i]] + Vector2.Distance(nodes[open[i]], nodes[goalIndex]);
            if (score >= bestScore)
                continue;

            bestScore = score;
            bestOpenIndex = i;
        }

        int node = open[bestOpenIndex];
        open.RemoveAt(bestOpenIndex);
        return node;
    }

    private static bool HasLineOfSight(Vector2 from, Vector2 to, IReadOnlyList<MapNavObstacle> obstacles)
    {
        for (int i = 0; i < obstacles.Count; i++)
        {
            MapNavObstacle obstacle = obstacles[i];
            IReadOnlyList<Vector2> points = GetObstaclePoints(obstacle);
            if (points.Count < 3)
                continue;

            if (MapNavGeometry.ContainsPoint(points, from) || MapNavGeometry.ContainsPoint(points, to))
                return false;

            if (MapNavGeometry.ContainsPoint(points, (from + to) * 0.5f))
                return false;

            for (int p = 0, previous = points.Count - 1; p < points.Count; previous = p++)
            {
                if (MapNavGeometry.SegmentsIntersect(from, to, points[previous], points[p]))
                    return false;
            }
        }

        return true;
    }

    private static bool IsInsideAnyObstacle(Vector2 point, IReadOnlyList<MapNavObstacle> obstacles)
    {
        for (int i = 0; i < obstacles.Count; i++)
        {
            IReadOnlyList<Vector2> points = GetObstaclePoints(obstacles[i]);
            if (points.Count >= 3 && MapNavGeometry.ContainsPoint(points, point))
                return true;
        }

        return false;
    }

    private static bool IsInsideAnyObstacle(MapNavigationBuildDataContext context, Vector2 point, IReadOnlyList<MapNavObstacleData> obstacles)
    {
        for (int i = 0; i < obstacles.Count; i++)
        {
            if (context.HasEnoughObstaclePoints(obstacles[i], 3) && context.ContainsObstacle(obstacles[i], point))
                return true;
        }

        return false;
    }

    private static bool TryFindNearestClearPoint(
        MapNavigationQueryContext context,
        MapNavRegion region,
        IReadOnlyList<MapNavObstacle> obstacles,
        Vector2 closestOnObstacle,
        Vector2 originalPoint,
        Vector2 referencePoint,
        float padding,
        out Vector2 clearPoint)
    {
        clearPoint = default;

        Vector2 centerDirection = closestOnObstacle - originalPoint;
        Vector2 polygonDirection = closestOnObstacle - MapNavGeometry.AveragePoint(GetContainingObstaclePoints(obstacles, originalPoint));
        Vector2[] directions =
        {
            centerDirection,
            polygonDirection,
            Vector2.up,
            Vector2.down,
            Vector2.left,
            Vector2.right,
            new Vector2(1f, 1f).normalized,
            new Vector2(-1f, 1f).normalized,
            new Vector2(1f, -1f).normalized,
            new Vector2(-1f, -1f).normalized
        };

        float bestSqrDistance = float.PositiveInfinity;
        for (int d = 0; d < directions.Length; d++)
        {
            Vector2 direction = directions[d];
            if (direction.sqrMagnitude <= 0.0001f)
                continue;

            for (int step = 1; step <= 8; step++)
            {
                Vector2 candidate = closestOnObstacle + direction.normalized * (padding * step);
                if (!context.ContainsRegion(region, candidate, 0f))
                    continue;

                if (IsInsideAnyObstacle(candidate, obstacles))
                    continue;

                float sqrDistance = (candidate - referencePoint).sqrMagnitude + ((candidate - originalPoint).sqrMagnitude * 0.15f);
                if (sqrDistance >= bestSqrDistance)
                    continue;

                bestSqrDistance = sqrDistance;
                clearPoint = candidate;
            }
        }

        return bestSqrDistance < float.PositiveInfinity;
    }

    private static bool TryFindNearestClearPoint(
        MapNavigationBuildDataContext context,
        MapNavRegionData region,
        IReadOnlyList<MapNavObstacleData> obstacles,
        Vector2 closestOnObstacle,
        Vector2 originalPoint,
        Vector2 referencePoint,
        float padding,
        out Vector2 clearPoint)
    {
        clearPoint = default;

        Vector2 centerDirection = closestOnObstacle - originalPoint;
        Vector2 polygonDirection = closestOnObstacle - MapNavGeometry.AveragePoint(GetContainingObstaclePoints(context, obstacles, originalPoint));
        Vector2[] directions =
        {
            centerDirection,
            polygonDirection,
            Vector2.up,
            Vector2.down,
            Vector2.left,
            Vector2.right,
            new Vector2(1f, 1f).normalized,
            new Vector2(-1f, 1f).normalized,
            new Vector2(1f, -1f).normalized,
            new Vector2(-1f, -1f).normalized
        };

        float bestSqrDistance = float.PositiveInfinity;
        for (int d = 0; d < directions.Length; d++)
        {
            Vector2 direction = directions[d];
            if (direction.sqrMagnitude <= 0.0001f)
                continue;

            for (int step = 1; step <= 8; step++)
            {
                Vector2 candidate = closestOnObstacle + direction.normalized * (padding * step);
                if (!context.ContainsRegion(region, candidate, 0f))
                    continue;

                if (IsInsideAnyObstacle(context, candidate, obstacles))
                    continue;

                float sqrDistance = (candidate - referencePoint).sqrMagnitude + ((candidate - originalPoint).sqrMagnitude * 0.15f);
                if (sqrDistance >= bestSqrDistance)
                    continue;

                bestSqrDistance = sqrDistance;
                clearPoint = candidate;
            }
        }

        return bestSqrDistance < float.PositiveInfinity;
    }

    private static IReadOnlyList<Vector2> GetContainingObstaclePoints(IReadOnlyList<MapNavObstacle> obstacles, Vector2 point)
    {
        for (int i = 0; i < obstacles.Count; i++)
        {
            IReadOnlyList<Vector2> points = GetObstaclePoints(obstacles[i]);
            if (points.Count >= 3 && MapNavGeometry.ContainsPoint(points, point))
                return points;
        }

        return System.Array.Empty<Vector2>();
    }

    private static IReadOnlyList<Vector2> GetContainingObstaclePoints(MapNavigationBuildDataContext context, IReadOnlyList<MapNavObstacleData> obstacles, Vector2 point)
    {
        for (int i = 0; i < obstacles.Count; i++)
        {
            IReadOnlyList<Vector2> points = context.GetObstaclePoints(obstacles[i]);
            if (points.Count >= 3 && MapNavGeometry.ContainsPoint(points, point))
                return points;
        }

        return System.Array.Empty<Vector2>();
    }

    private static IReadOnlyList<Vector2> GetObstaclePoints(MapNavObstacle obstacle)
    {
        return obstacle?.Points != null ? obstacle.Points : System.Array.Empty<Vector2>();
    }

}
