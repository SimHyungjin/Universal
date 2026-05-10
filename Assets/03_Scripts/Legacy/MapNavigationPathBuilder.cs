using System.Collections.Generic;
using UnityEngine;

public static class MapNavigationPathBuilder
{
    private const float DirectRegionHeightTolerance = 0.05f;

    public static bool Build(
        MapNavigationQueryContext context,
        MapNavigationSpace startSpace,
        MapNavigationSpace targetSpace,
        MapNavigationPathBuildRequest request,
        Vector3 fallbackStart,
        IMapNavigationPathAssembler assembler,
        MapNavigationPathBuildResult result)
    {
        MapNavigationQueryPathContext pathContext = new(context);
        MapNavigationPathSpace startPathSpace = ToPathSpace(startSpace);
        MapNavigationPathSpace targetPathSpace = ToPathSpace(targetSpace);
        return Build(
            pathContext,
            startPathSpace,
            targetPathSpace,
            request,
            fallbackStart,
            assembler,
            result);
    }

    public static bool Build<TContext>(
        TContext context,
        MapNavigationPathSpace startSpace,
        MapNavigationPathSpace targetSpace,
        MapNavigationPathBuildRequest request,
        Vector3 fallbackStart,
        IMapNavigationPathAssembler assembler,
        MapNavigationPathBuildResult result)
        where TContext : IMapNavigationPathContext
    {
        result.Clear();
        bool built = BuildPathBetweenSpaces(
            context,
            startSpace,
            targetSpace,
            request.StartPosition,
            request.TargetPosition,
            request.Settings.AgentRadius,
            request.Settings.StopDistance,
            request.Settings.UseRegionPathfinding,
            result.MutableWaypoints,
            fallbackStart,
            assembler,
            out string pathKind,
            out bool usedCrossLayerTransition,
            out List<MapNavigationQuery.PathStep> selectedPath);

        if (!built)
        {
            result.Clear();
            return false;
        }

        result.RefreshResolvedTarget();
        result.SetPathMetadata(pathKind, usedCrossLayerTransition, selectedPath);
        result.SetDebugSummary($"Built {pathKind} path. {DescribeSpace(context, startSpace)} -> {DescribeSpace(context, targetSpace)}.");

        return true;
    }

    private static MapNavigationPathSpace ToPathSpace(MapNavigationSpace space)
    {
        return space.Kind switch
        {
            MapNavigationSpaceKind.Region => space.Region != null ? MapNavigationPathSpace.Region(space.Region.Id) : default,
            MapNavigationSpaceKind.Transition => space.Transition != null ? MapNavigationPathSpace.Transition(space.Transition.Id) : default,
            _ => default
        };
    }

    private static void AddWaypoint(IList<MapNavWaypoint> waypoints, Vector3 position, bool required)
    {
        waypoints.Add(new MapNavWaypoint(position, required));
    }

    private static bool AddWaypointIfSeparated(
        IList<MapNavWaypoint> waypoints,
        Vector3 fallbackStart,
        Vector3 position,
        float stopDistance)
    {
        Vector3 previous = waypoints.Count > 0 ? waypoints[^1].Position : fallbackStart;
        Vector3 delta = position - previous;
        delta.y = 0f;

        if (delta.sqrMagnitude <= stopDistance * stopDistance)
            return false;

        AddWaypoint(waypoints, position, false);
        return true;
    }

    private static bool AddRequiredWaypoint(
        IList<MapNavWaypoint> waypoints,
        Vector3 fallbackStart,
        Vector3 position)
    {
        Vector3 previous = waypoints.Count > 0 ? waypoints[^1].Position : fallbackStart;
        Vector3 delta = position - previous;
        delta.y = 0f;

        if (delta.sqrMagnitude <= 0.000001f)
            return false;

        AddWaypoint(waypoints, position, true);
        return true;
    }

    public static bool IsSameTraversalLayer(MapNavRegion currentRegion, MapNavRegion targetRegion)
    {
        if (currentRegion == null || targetRegion == null)
            return false;

        if (currentRegion.Id == targetRegion.Id)
            return true;

        return currentRegion.NavLayerId == targetRegion.NavLayerId
            && Mathf.Abs(currentRegion.Height - targetRegion.Height) <= DirectRegionHeightTolerance;
    }

    private static bool IsSameTraversalLayer(MapNavigationRegionInfo currentRegion, MapNavigationRegionInfo targetRegion)
    {
        if (currentRegion.Id == targetRegion.Id)
            return true;

        return currentRegion.NavLayerId == targetRegion.NavLayerId
            && Mathf.Abs(currentRegion.Height - targetRegion.Height) <= DirectRegionHeightTolerance;
    }

    private static bool TryFindRegionPathOrSame<TContext>(
        TContext context,
        int startRegionId,
        int targetRegionId,
        Vector3 startPosition,
        Vector3 targetPosition,
        float agentRadius,
        bool allowTransitions,
        out List<MapNavigationQuery.PathStep> path,
        out float cost)
        where TContext : IMapNavigationPathContext
    {
        path = new List<MapNavigationQuery.PathStep>();
        cost = 0f;
        if (!context.TryGetRegionInfo(startRegionId, out MapNavigationRegionInfo startRegion)
            || !context.TryGetRegionInfo(targetRegionId, out MapNavigationRegionInfo targetRegion))
        {
            return false;
        }

        if (!allowTransitions && !IsSameTraversalLayer(startRegion, targetRegion))
            return false;

        if (startRegionId == targetRegionId)
        {
            MapNavigationQuery.InternalPathResult result = context.FindInternalRegionPath(startRegionId, startPosition, targetPosition, agentRadius, out List<Vector3> internalPath);
            if (result == MapNavigationQuery.InternalPathResult.Failed)
                return false;

            cost = GetWaypointPathCost(startPosition, targetPosition, internalPath);
            return true;
        }

        return context.TryFindRegionPath(
            startRegionId,
            targetRegionId,
            startPosition,
            targetPosition,
            agentRadius,
            allowTransitions,
            out path,
            out cost);
    }

    private static float GetWaypointPathCost(Vector3 startPosition, Vector3 targetPosition, IReadOnlyList<Vector3> waypoints)
    {
        if (waypoints == null || waypoints.Count == 0)
            return MapNavGeometry.PlanarDistance(startPosition, targetPosition);

        float cost = 0f;
        Vector3 cursor = startPosition;
        for (int i = 0; i < waypoints.Count; i++)
        {
            cost += MapNavGeometry.PlanarDistance(cursor, waypoints[i]);
            cursor = waypoints[i];
        }

        return cost + MapNavGeometry.PlanarDistance(cursor, targetPosition);
    }

    private static void AppendRegionPath<TContext>(
        TContext context,
        IReadOnlyList<MapNavigationQuery.PathStep> path,
        float agentRadius,
        IList<MapNavWaypoint> waypoints,
        Vector3 fallbackStart,
        float stopDistance,
        IMapNavigationPathAssembler assembler)
        where TContext : IMapNavigationPathContext
    {
        for (int i = 0; i < path.Count; i++)
        {
            if (path[i].UsesTransition)
            {
                if (!context.TryGetTransitionInfo(path[i].TransitionId, out _))
                    continue;

                context.GetTransitionEndpointWorld(path[i].TransitionId, path[i].IsForward, agentRadius, out Vector3 transitionEntry, out Vector3 transitionExit);
                if (assembler.ResolveRegionWaypoint(path[i].FromRegionId, transitionEntry, out Vector3 resolvedEntry))
                {
                    assembler.AddRegionWaypoint(path[i].FromRegionId, resolvedEntry);
                    AddRequiredWaypoint(waypoints, fallbackStart, resolvedEntry);
                }

                if (assembler.ResolveRegionWaypoint(path[i].ToRegionId, transitionExit, out Vector3 resolvedExit))
                    AddRequiredWaypoint(waypoints, fallbackStart, resolvedExit);

                continue;
            }

            if (assembler.ResolveRegionWaypoint(path[i].FromRegionId, path[i].EntryWorld, out Vector3 resolvedRegionEntry))
                assembler.AddRegionWaypoint(path[i].FromRegionId, resolvedRegionEntry);

            if (assembler.ResolveRegionWaypoint(path[i].ToRegionId, path[i].ExitWorld, out Vector3 resolvedRegionExit))
                AddWaypointIfSeparated(waypoints, fallbackStart, resolvedRegionExit, stopDistance);
        }
    }

    private static bool BuildRegionToTransitionPath<TContext>(
        TContext context,
        int startRegionId,
        int targetTransitionId,
        Vector3 startPosition,
        Vector3 transitionTarget,
        float agentRadius,
        float stopDistance,
        IList<MapNavWaypoint> waypoints,
        Vector3 fallbackStart,
        IMapNavigationPathAssembler assembler)
        where TContext : IMapNavigationPathContext
    {
        if (!assembler.ResolveRegionWaypoint(startRegionId, startPosition, out startPosition))
            return false;

        if (!TryGetTransitionEndpointForRegionLayer(context, targetTransitionId, startRegionId, out int entryRegionId, out bool fromSide))
            return false;

        GetTransitionEndpointWorldForSide(context, targetTransitionId, fromSide, agentRadius, out Vector3 entryWorld);
        if (!assembler.ResolveRegionWaypoint(entryRegionId, entryWorld, out Vector3 resolvedEntry))
            return false;

        if (!TryFindRegionPathOrSame(context, startRegionId, entryRegionId, startPosition, resolvedEntry, agentRadius, false, out List<MapNavigationQuery.PathStep> regionPath, out _))
            return false;

        AppendRegionPath(context, regionPath, agentRadius, waypoints, fallbackStart, stopDistance, assembler);
        assembler.AddRegionWaypoint(entryRegionId, resolvedEntry);
        AddRequiredWaypoint(waypoints, fallbackStart, resolvedEntry);
        AddWaypointIfSeparated(waypoints, fallbackStart, transitionTarget, stopDistance);
        return true;
    }

    private static bool TryGetTransitionEndpointForRegionLayer<TContext>(
        TContext context,
        int transitionId,
        int regionId,
        out int endpointRegionId,
        out bool fromSide)
        where TContext : IMapNavigationPathContext
    {
        if (!context.TryGetTransitionInfo(transitionId, out MapNavigationTransitionInfo transition))
        {
            endpointRegionId = -1;
            fromSide = true;
            return false;
        }

        if (transition.FromRegionId == regionId)
        {
            endpointRegionId = transition.FromRegionId;
            fromSide = true;
            return true;
        }

        if (transition.Bidirectional && transition.ToRegionId == regionId)
        {
            endpointRegionId = transition.ToRegionId;
            fromSide = false;
            return true;
        }

        bool hasRegion = context.TryGetRegionInfo(regionId, out MapNavigationRegionInfo region);
        bool hasFromRegion = context.TryGetRegionInfo(transition.FromRegionId, out MapNavigationRegionInfo fromRegion);
        bool hasToRegion = context.TryGetRegionInfo(transition.ToRegionId, out MapNavigationRegionInfo toRegion);
        bool matchesFromLayer = hasRegion && hasFromRegion && IsSameTraversalLayer(region, fromRegion);
        bool matchesToLayer = transition.Bidirectional && hasRegion && hasToRegion && IsSameTraversalLayer(region, toRegion);

        if (matchesFromLayer)
        {
            endpointRegionId = transition.FromRegionId;
            fromSide = true;
            return true;
        }

        if (matchesToLayer)
        {
            endpointRegionId = transition.ToRegionId;
            fromSide = false;
            return true;
        }

        endpointRegionId = -1;
        fromSide = true;
        return false;
    }

    private static void GetTransitionEndpointWorldForSide<TContext>(
        TContext context,
        int transitionId,
        bool fromSide,
        float agentRadius,
        out Vector3 endpointWorld)
        where TContext : IMapNavigationPathContext
    {
        // A side endpoint is a point on the transition, not an instruction to cross it.
        if (fromSide)
            context.GetTransitionEndpointWorld(transitionId, true, agentRadius, out endpointWorld, out _);
        else
            context.GetTransitionEndpointWorld(transitionId, true, agentRadius, out _, out endpointWorld);
    }

    private static bool BuildTransitionToRegionPath<TContext>(
        TContext context,
        int startTransitionId,
        int targetRegionId,
        Vector3 startPosition,
        Vector3 targetPosition,
        float agentRadius,
        float stopDistance,
        IList<MapNavWaypoint> waypoints,
        Vector3 fallbackStart,
        IMapNavigationPathAssembler assembler)
        where TContext : IMapNavigationPathContext
    {
        if (!assembler.ResolveRegionWaypoint(targetRegionId, targetPosition, out targetPosition))
            return false;

        if (!TryGetTransitionEndpointForRegionLayer(context, startTransitionId, targetRegionId, out int exitRegionId, out bool fromSide))
            return false;

        GetTransitionEndpointWorldForSide(context, startTransitionId, fromSide, agentRadius, out Vector3 exitWorld);
        if (!assembler.ResolveRegionWaypoint(exitRegionId, exitWorld, out Vector3 resolvedExit))
            return false;

        AddRequiredWaypoint(waypoints, fallbackStart, exitWorld);
        if (!TryFindRegionPathOrSame(context, exitRegionId, targetRegionId, resolvedExit, targetPosition, agentRadius, false, out List<MapNavigationQuery.PathStep> regionPath, out _))
            return false;

        AppendRegionPath(context, regionPath, agentRadius, waypoints, fallbackStart, stopDistance, assembler);
        assembler.AddRegionWaypoint(targetRegionId, targetPosition);
        return true;
    }

    private static bool BuildTransitionToTransitionCompositePath<TContext>(
        TContext context,
        int startTransitionId,
        int targetTransitionId,
        Vector3 startPosition,
        Vector3 transitionTarget,
        float agentRadius,
        float stopDistance,
        IList<MapNavWaypoint> waypoints,
        Vector3 fallbackStart,
        IMapNavigationPathAssembler assembler)
        where TContext : IMapNavigationPathContext
    {
        if (!TrySelectTransitionToTransitionRegionSegment(
                context,
                startTransitionId,
                targetTransitionId,
                startPosition,
                transitionTarget,
                agentRadius,
                assembler,
                out int exitRegionId,
                out Vector3 exitWorld,
                out int entryRegionId,
                out Vector3 entryWorld,
                out List<MapNavigationQuery.PathStep> regionPath))
        {
            return false;
        }

        AddRequiredWaypoint(waypoints, fallbackStart, exitWorld);
        AppendRegionPath(context, regionPath, agentRadius, waypoints, fallbackStart, stopDistance, assembler);
        assembler.AddRegionWaypoint(entryRegionId, entryWorld);
        AddRequiredWaypoint(waypoints, fallbackStart, entryWorld);
        AddWaypointIfSeparated(waypoints, fallbackStart, transitionTarget, stopDistance);
        return true;
    }

    private static bool TrySelectTransitionToTransitionRegionSegment<TContext>(
        TContext context,
        int startTransitionId,
        int targetTransitionId,
        Vector3 startPosition,
        Vector3 transitionTarget,
        float agentRadius,
        IMapNavigationPathAssembler assembler,
        out int bestExitRegionId,
        out Vector3 bestExitWorld,
        out int bestEntryRegionId,
        out Vector3 bestEntryWorld,
        out List<MapNavigationQuery.PathStep> bestRegionPath)
        where TContext : IMapNavigationPathContext
    {
        bestExitRegionId = -1;
        bestExitWorld = default;
        bestEntryRegionId = -1;
        bestEntryWorld = default;
        bestRegionPath = null;
        if (!context.TryGetTransitionInfo(startTransitionId, out MapNavigationTransitionInfo startTransition)
            || !context.TryGetTransitionInfo(targetTransitionId, out MapNavigationTransitionInfo targetTransition))
        {
            return false;
        }

        float bestCost = float.PositiveInfinity;
        int localBestExitRegionId = -1;
        Vector3 localBestExitWorld = default;
        int localBestEntryRegionId = -1;
        Vector3 localBestEntryWorld = default;
        List<MapNavigationQuery.PathStep> localBestRegionPath = null;

        EvaluateExit(startTransition.FromRegionId, true);
        if (startTransition.Bidirectional)
            EvaluateExit(startTransition.ToRegionId, false);

        bestExitRegionId = localBestExitRegionId;
        bestExitWorld = localBestExitWorld;
        bestEntryRegionId = localBestEntryRegionId;
        bestEntryWorld = localBestEntryWorld;
        bestRegionPath = localBestRegionPath;
        return bestRegionPath != null;

        void EvaluateExit(int exitRegionId, bool exitFromSide)
        {
            GetTransitionEndpointWorldForSide(context, startTransition.Id, exitFromSide, agentRadius, out Vector3 exitWorld);
            if (!assembler.ResolveRegionWaypoint(exitRegionId, exitWorld, out Vector3 resolvedExit))
                return;

            EvaluateEntry(exitRegionId, resolvedExit, targetTransition.FromRegionId, true);
            if (targetTransition.Bidirectional)
                EvaluateEntry(exitRegionId, resolvedExit, targetTransition.ToRegionId, false);
        }

        void EvaluateEntry(int exitRegionId, Vector3 exitWorld, int entryRegionId, bool entryFromSide)
        {
            if (!context.TryGetRegionInfo(exitRegionId, out MapNavigationRegionInfo exitRegion)
                || !context.TryGetRegionInfo(entryRegionId, out MapNavigationRegionInfo entryRegion)
                || !IsSameTraversalLayer(exitRegion, entryRegion))
            {
                return;
            }

            GetTransitionEndpointWorldForSide(context, targetTransition.Id, entryFromSide, agentRadius, out Vector3 entryWorld);
            if (!assembler.ResolveRegionWaypoint(entryRegionId, entryWorld, out Vector3 resolvedEntry))
                return;

            if (!TryFindRegionPathOrSame(context, exitRegionId, entryRegionId, exitWorld, resolvedEntry, agentRadius, false, out List<MapNavigationQuery.PathStep> regionPath, out float regionCost))
                return;

            float cost = MapNavGeometry.PlanarDistance(startPosition, exitWorld)
                + regionCost
                + MapNavGeometry.PlanarDistance(resolvedEntry, transitionTarget);
            if (cost >= bestCost)
                return;

            bestCost = cost;
            localBestExitRegionId = exitRegionId;
            localBestExitWorld = exitWorld;
            localBestEntryRegionId = entryRegionId;
            localBestEntryWorld = resolvedEntry;
            localBestRegionPath = regionPath;
        }
    }

    private static bool BuildRegionToRegionPath<TContext>(
        TContext context,
        int startRegionId,
        int targetRegionId,
        Vector3 startPosition,
        Vector3 targetPosition,
        float agentRadius,
        float stopDistance,
        bool useRegionPathfinding,
        IList<MapNavWaypoint> waypoints,
        Vector3 fallbackStart,
        IMapNavigationPathAssembler assembler,
        out bool usedCrossLayerTransition,
        out List<MapNavigationQuery.PathStep> selectedPath)
        where TContext : IMapNavigationPathContext
    {
        usedCrossLayerTransition = false;
        selectedPath = null;

        if (!assembler.ResolveRegionWaypoint(startRegionId, startPosition, out startPosition)
            || !assembler.ResolveRegionWaypoint(targetRegionId, targetPosition, out targetPosition))
        {
            return false;
        }

        if (!context.TryGetRegionInfo(startRegionId, out MapNavigationRegionInfo startRegion)
            || !context.TryGetRegionInfo(targetRegionId, out MapNavigationRegionInfo targetRegion))
        {
            return false;
        }

        if (startRegionId == targetRegionId)
        {
            assembler.AddRegionWaypoint(startRegionId, targetPosition);
            return true;
        }

        if (!IsSameTraversalLayer(startRegion, targetRegion))
        {
            usedCrossLayerTransition = true;
            return BuildRegionToRegionViaTransitionPath(
                context,
                startRegionId,
                targetRegionId,
                startPosition,
                targetPosition,
                agentRadius,
                stopDistance,
                waypoints,
                fallbackStart,
                assembler,
                out selectedPath);
        }

        if (!useRegionPathfinding)
        {
            AddWaypointIfSeparated(waypoints, fallbackStart, targetPosition, stopDistance);
            return true;
        }

        if (!TryFindRegionPathOrSame(context, startRegionId, targetRegionId, startPosition, targetPosition, agentRadius, false, out List<MapNavigationQuery.PathStep> path, out _))
        {
            AddWaypointIfSeparated(waypoints, fallbackStart, targetPosition, stopDistance);
            return true;
        }

        selectedPath = path;
        AppendRegionPath(context, path, agentRadius, waypoints, fallbackStart, stopDistance, assembler);
        assembler.AddRegionWaypoint(targetRegionId, targetPosition);
        return true;
    }

    private static bool BuildPathBetweenSpaces<TContext>(
        TContext context,
        MapNavigationPathSpace startSpace,
        MapNavigationPathSpace targetSpace,
        Vector3 startPosition,
        Vector3 targetPosition,
        float agentRadius,
        float stopDistance,
        bool useRegionPathfinding,
        IList<MapNavWaypoint> waypoints,
        Vector3 fallbackStart,
        IMapNavigationPathAssembler assembler,
        out string pathKind,
        out bool usedCrossLayerTransition,
        out List<MapNavigationQuery.PathStep> selectedPath)
        where TContext : IMapNavigationPathContext
    {
        // Path cost selects the region/transition sequence. Waypoints are assembled only from:
        // R->R same-layer path, T->T internal target, R->T endpoint then internal target, and T->R endpoint then same-layer path.
        pathKind = "None";
        usedCrossLayerTransition = false;
        selectedPath = null;

        if (startSpace.Kind == MapNavigationSpaceKind.Region && targetSpace.Kind == MapNavigationSpaceKind.Region)
        {
            pathKind = "R->R";
            return BuildRegionToRegionPath(
                context,
                startSpace.RegionId,
                targetSpace.RegionId,
                startPosition,
                targetPosition,
                agentRadius,
                stopDistance,
                useRegionPathfinding,
                waypoints,
                fallbackStart,
                assembler,
                out usedCrossLayerTransition,
                out selectedPath);
        }

        if (startSpace.Kind == MapNavigationSpaceKind.Region && targetSpace.Kind == MapNavigationSpaceKind.Transition)
        {
            pathKind = "R->T";
            return BuildRegionToTransitionPath(
                context,
                startSpace.RegionId,
                targetSpace.TransitionId,
                startPosition,
                targetPosition,
                agentRadius,
                stopDistance,
                waypoints,
                fallbackStart,
                assembler);
        }

        if (startSpace.Kind == MapNavigationSpaceKind.Transition && targetSpace.Kind == MapNavigationSpaceKind.Region)
        {
            pathKind = "T->R";
            return BuildTransitionToRegionPath(
                context,
                startSpace.TransitionId,
                targetSpace.RegionId,
                startPosition,
                targetPosition,
                agentRadius,
                stopDistance,
                waypoints,
                fallbackStart,
                assembler);
        }

        if (startSpace.Kind == MapNavigationSpaceKind.Transition && targetSpace.Kind == MapNavigationSpaceKind.Transition)
        {
            if (startSpace.TransitionId == targetSpace.TransitionId)
            {
                pathKind = "T->T internal";
                assembler?.AddTransitionInternalWaypoint(startSpace.TransitionId, targetPosition);
                return true;
            }

            pathKind = "T->T";
            return BuildTransitionToTransitionCompositePath(
                context,
                startSpace.TransitionId,
                targetSpace.TransitionId,
                startPosition,
                targetPosition,
                agentRadius,
                stopDistance,
                waypoints,
                fallbackStart,
                assembler);
        }

        return false;
    }

    private static bool BuildRegionToRegionViaTransitionPath<TContext>(
        TContext context,
        int startRegionId,
        int targetRegionId,
        Vector3 startPosition,
        Vector3 targetPosition,
        float agentRadius,
        float stopDistance,
        IList<MapNavWaypoint> waypoints,
        Vector3 fallbackStart,
        IMapNavigationPathAssembler assembler,
        out List<MapNavigationQuery.PathStep> selectedPath)
        where TContext : IMapNavigationPathContext
    {
        selectedPath = null;
        if (!TryFindRegionPathOrSame(context, startRegionId, targetRegionId, startPosition, targetPosition, agentRadius, true, out List<MapNavigationQuery.PathStep> path, out _))
            return false;

        selectedPath = path;
        int currentRegionId = startRegionId;
        Vector3 currentPosition = startPosition;
        bool usedTransition = false;
        for (int i = 0; i < path.Count; i++)
        {
            if (!path[i].UsesTransition)
                continue;

            if (!context.TryGetTransitionInfo(path[i].TransitionId, out _))
                return false;

            context.GetTransitionEndpointWorld(path[i].TransitionId, path[i].IsForward, agentRadius, out Vector3 entryWorld, out Vector3 exitWorld);
            if (!assembler.ResolveRegionWaypoint(path[i].FromRegionId, entryWorld, out Vector3 resolvedEntry)
                || !assembler.ResolveRegionWaypoint(path[i].ToRegionId, exitWorld, out Vector3 resolvedExit))
            {
                return false;
            }

            if (!TryFindRegionPathOrSame(context, currentRegionId, path[i].FromRegionId, currentPosition, resolvedEntry, agentRadius, false, out List<MapNavigationQuery.PathStep> entryPath, out _))
                return false;

            AppendRegionPath(context, entryPath, agentRadius, waypoints, fallbackStart, stopDistance, assembler);
            assembler.AddRegionWaypoint(path[i].FromRegionId, resolvedEntry);
            AddRequiredWaypoint(waypoints, fallbackStart, resolvedEntry);
            AddRequiredWaypoint(waypoints, fallbackStart, resolvedExit);

            currentRegionId = path[i].ToRegionId;
            currentPosition = resolvedExit;
            usedTransition = true;
        }

        if (!usedTransition)
        {
            AppendRegionPath(context, path, agentRadius, waypoints, fallbackStart, stopDistance, assembler);
            assembler.AddRegionWaypoint(targetRegionId, targetPosition);
            return true;
        }

        if (!TryFindRegionPathOrSame(context, currentRegionId, targetRegionId, currentPosition, targetPosition, agentRadius, false, out List<MapNavigationQuery.PathStep> targetPath, out _))
            return false;

        AppendRegionPath(context, targetPath, agentRadius, waypoints, fallbackStart, stopDistance, assembler);
        assembler.AddRegionWaypoint(targetRegionId, targetPosition);
        return true;
    }

    public static string DescribeSpace(MapNavigationSpace space)
    {
        return space.Kind switch
        {
            MapNavigationSpaceKind.Region => space.Region != null ? DescribeRegion(space.Region) : "Region null",
            MapNavigationSpaceKind.Transition => space.Transition != null ? space.Transition.DisplayName : "Transition null",
            _ => "None"
        };
    }

    public static string DescribeSpace<TContext>(TContext context, MapNavigationPathSpace space)
        where TContext : IMapNavigationPathContext
    {
        return space.Kind switch
        {
            MapNavigationSpaceKind.Region => context.TryGetRegionInfo(space.RegionId, out MapNavigationRegionInfo region)
                ? DescribeRegion(region)
                : "Region null",
            MapNavigationSpaceKind.Transition => context.TryGetTransitionInfo(space.TransitionId, out MapNavigationTransitionInfo transition)
                ? transition.DisplayName
                : "Transition null",
            _ => "None"
        };
    }

    private static string DescribeRegion(MapNavRegion region)
    {
        return region != null
            ? $"{region.DisplayName}(Layer={region.NavLayerId}, Height={region.Height:0.###})"
            : "Region null";
    }

    private static string DescribeRegion(MapNavigationRegionInfo region)
    {
        return $"Region {region.Id}(Layer={region.NavLayerId}, Height={region.Height:0.###})";
    }
}

