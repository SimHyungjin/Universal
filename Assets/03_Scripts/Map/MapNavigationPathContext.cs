using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public readonly struct MapNavigationRegionInfo
{
    public readonly int Id;
    public readonly int NavLayerId;
    public readonly float Height;
    public readonly float Cost;

    public MapNavigationRegionInfo(int id, int navLayerId, float height, float cost)
    {
        Id = id;
        NavLayerId = navLayerId;
        Height = height;
        Cost = cost;
    }
}

public readonly struct MapNavigationTransitionInfo
{
    public readonly int Id;
    public readonly int FromRegionId;
    public readonly int ToRegionId;
    public readonly bool Bidirectional;
    public readonly string DisplayName;
    public readonly float Cost;

    public MapNavigationTransitionInfo(int id, int fromRegionId, int toRegionId, bool bidirectional, string displayName, float cost)
    {
        Id = id;
        FromRegionId = fromRegionId;
        ToRegionId = toRegionId;
        Bidirectional = bidirectional;
        DisplayName = displayName;
        Cost = cost;
    }
}

public interface IMapNavigationPathContext
{
    bool TryGetRegionInfo(int regionId, out MapNavigationRegionInfo region);
    bool TryGetTransitionInfo(int transitionId, out MapNavigationTransitionInfo transition);
    bool TryFindRegionPath(
        int startRegionId,
        int targetRegionId,
        Vector3 startPosition,
        Vector3 targetPosition,
        float agentRadius,
        bool allowTransitions,
        out List<MapNavigationQuery.PathStep> path,
        out float cost);
    MapNavigationQuery.InternalPathResult FindInternalRegionPath(
        int regionId,
        Vector3 startPosition,
        Vector3 targetPosition,
        float agentRadius,
        out List<Vector3> waypoints);
    void GetTransitionEndpointWorld(
        int transitionId,
        bool isForward,
        float agentRadius,
        out Vector3 entry,
        out Vector3 exit);
}

public readonly struct MapNavigationQueryPathContext : IMapNavigationPathContext
{
    private readonly MapNavigationQueryContext _context;

    public MapNavigationQueryPathContext(MapNavigationQueryContext context)
    {
        _context = context;
    }

    public bool TryGetRegionInfo(int regionId, out MapNavigationRegionInfo region)
    {
        MapNavRegion source = _context.FindRegion(regionId);
        if (source == null)
        {
            region = default;
            return false;
        }

        region = new MapNavigationRegionInfo(source.Id, source.NavLayerId, source.Height, source.Cost);
        return true;
    }

    public bool TryGetTransitionInfo(int transitionId, out MapNavigationTransitionInfo transition)
    {
        MapNavTransition source = _context.FindTransition(transitionId);
        if (source == null)
        {
            transition = default;
            return false;
        }

        transition = new MapNavigationTransitionInfo(
            source.Id,
            source.FromRegionId,
            source.ToRegionId,
            source.Bidirectional,
            source.DisplayName,
            source.Cost);
        return true;
    }

    public bool TryFindRegionPath(
        int startRegionId,
        int targetRegionId,
        Vector3 startPosition,
        Vector3 targetPosition,
        float agentRadius,
        bool allowTransitions,
        out List<MapNavigationQuery.PathStep> path,
        out float cost)
    {
        return MapNavigationQuery.TryFindRegionPath(
            _context,
            startRegionId,
            targetRegionId,
            startPosition,
            targetPosition,
            agentRadius,
            allowTransitions,
            out path,
            out cost);
    }

    public MapNavigationQuery.InternalPathResult FindInternalRegionPath(
        int regionId,
        Vector3 startPosition,
        Vector3 targetPosition,
        float agentRadius,
        out List<Vector3> waypoints)
    {
        MapNavRegion region = _context.FindRegion(regionId);
        if (region == null)
        {
            waypoints = new List<Vector3>();
            return MapNavigationQuery.InternalPathResult.Failed;
        }

        return MapNavigationQuery.FindInternalRegionPath(_context, region, startPosition, targetPosition, agentRadius, out waypoints);
    }

    public void GetTransitionEndpointWorld(
        int transitionId,
        bool isForward,
        float agentRadius,
        out Vector3 entry,
        out Vector3 exit)
    {
        MapNavTransition transition = _context.FindTransition(transitionId);
        if (transition == null)
        {
            entry = default;
            exit = default;
            return;
        }

        MapNavigationQuery.GetTransitionEndpointWorld(_context, transition, isForward, agentRadius, out entry, out exit);
    }
}

public readonly struct MapNavigationBuildDataPathContext : IMapNavigationPathContext
{
    private readonly MapNavigationBuildDataContext _context;

    public MapNavigationBuildDataPathContext(MapNavigationBuildDataContext context)
    {
        _context = context;
    }

    public bool TryGetRegionInfo(int regionId, out MapNavigationRegionInfo region)
    {
        if (!_context.TryFindRegion(regionId, out MapNavRegionData source))
        {
            region = default;
            return false;
        }

        region = new MapNavigationRegionInfo(source.Id, source.NavLayerId, source.Height, source.Cost);
        return true;
    }

    public bool TryGetTransitionInfo(int transitionId, out MapNavigationTransitionInfo transition)
    {
        if (!_context.TryFindTransition(transitionId, out MapNavTransitionData source))
        {
            transition = default;
            return false;
        }

        transition = new MapNavigationTransitionInfo(
            source.Id,
            source.FromRegionId,
            source.ToRegionId,
            source.Bidirectional,
            $"Transition {source.Id}",
            source.Cost);
        return true;
    }

    public bool TryFindRegionPath(
        int startRegionId,
        int targetRegionId,
        Vector3 startPosition,
        Vector3 targetPosition,
        float agentRadius,
        bool allowTransitions,
        out List<MapNavigationQuery.PathStep> path,
        out float cost)
    {
        return MapNavigationPathContextUtility.TryFindRegionPath(
            _context,
            startRegionId,
            targetRegionId,
            startPosition,
            targetPosition,
            agentRadius,
            allowTransitions,
            out path,
            out cost);
    }

    public MapNavigationQuery.InternalPathResult FindInternalRegionPath(
        int regionId,
        Vector3 startPosition,
        Vector3 targetPosition,
        float agentRadius,
        out List<Vector3> waypoints)
    {
        return MapNavigationPathContextUtility.FindInternalRegionPath(_context, regionId, startPosition, targetPosition, agentRadius, out waypoints);
    }

    public void GetTransitionEndpointWorld(
        int transitionId,
        bool isForward,
        float agentRadius,
        out Vector3 entry,
        out Vector3 exit)
    {
        if (!_context.TryFindTransition(transitionId, out MapNavTransitionData transition))
        {
            entry = default;
            exit = default;
            return;
        }

        MapNavigationPathContextUtility.GetTransitionEndpointWorld(_context, transition, isForward, agentRadius, out entry, out exit);
    }
}

public readonly struct MapNavigationBlobPathContext : IMapNavigationPathContext
{
    private readonly MapNavigationBlobDataContext _context;
    public MapNavigationBlobDataContext DataContext => _context;

    public MapNavigationBlobPathContext(MapNavigationBlobDataContext context)
    {
        _context = context;
    }

    public bool TryGetRegionInfo(int regionId, out MapNavigationRegionInfo region)
    {
        if (!_context.TryFindRegion(regionId, out MapNavRegionBlob source))
        {
            region = default;
            return false;
        }

        region = new MapNavigationRegionInfo(source.Id, source.NavLayerId, source.Height, source.Cost);
        return true;
    }

    public bool TryGetTransitionInfo(int transitionId, out MapNavigationTransitionInfo transition)
    {
        if (!_context.TryFindTransition(transitionId, out MapNavTransitionBlob source))
        {
            transition = default;
            return false;
        }

        transition = new MapNavigationTransitionInfo(
            source.Id,
            source.FromRegionId,
            source.ToRegionId,
            source.Bidirectional != 0,
            $"Transition {source.Id}",
            source.Cost);
        return true;
    }

    public bool TryFindRegionPath(
        int startRegionId,
        int targetRegionId,
        Vector3 startPosition,
        Vector3 targetPosition,
        float agentRadius,
        bool allowTransitions,
        out List<MapNavigationQuery.PathStep> path,
        out float cost)
    {
        return MapNavigationPathContextUtility.TryFindRegionPath(
            _context,
            startRegionId,
            targetRegionId,
            startPosition,
            targetPosition,
            agentRadius,
            allowTransitions,
            out path,
            out cost);
    }

    public MapNavigationQuery.InternalPathResult FindInternalRegionPath(
        int regionId,
        Vector3 startPosition,
        Vector3 targetPosition,
        float agentRadius,
        out List<Vector3> waypoints)
    {
        return MapNavigationPathContextUtility.FindInternalRegionPath(_context, regionId, startPosition, targetPosition, agentRadius, out waypoints);
    }

    public void GetTransitionEndpointWorld(
        int transitionId,
        bool isForward,
        float agentRadius,
        out Vector3 entry,
        out Vector3 exit)
    {
        if (!_context.TryFindTransition(transitionId, out MapNavTransitionBlob transition))
        {
            entry = default;
            exit = default;
            return;
        }

        MapNavigationPathContextUtility.GetTransitionEndpointWorld(_context, transition, isForward, agentRadius, out entry, out exit);
    }
}

public static class MapNavigationPathContextUtility
{
    private const float RegionLinkHeightTolerance = 0.05f;
    private const int MaxPooledContainersPerType = 8;
    private static readonly Stack<List<RegionSearchNode>> RegionSearchNodeListPool = new();
    private static readonly Stack<List<VisibilitySearchNode>> VisibilitySearchNodeListPool = new();
    private static readonly Stack<List<Vector2>> Vector2ListPool = new();
    private static readonly Stack<List<int>> IntListPool = new();
    private static readonly Stack<HashSet<int>> IntHashSetPool = new();
    private static readonly Stack<Dictionary<int, float>> IntFloatDictionaryPool = new();
    private static readonly Stack<Dictionary<int, Vector3>> IntVector3DictionaryPool = new();
    private static readonly Stack<Dictionary<int, int>> IntIntDictionaryPool = new();
    private static readonly Stack<Dictionary<int, MapNavigationQuery.PathStep>> IntPathStepDictionaryPool = new();

    private readonly struct RegionSearchNode
    {
        public readonly int RegionId;
        public readonly float Cost;
        public readonly float Score;

        public RegionSearchNode(int regionId, float cost, float score)
        {
            RegionId = regionId;
            Cost = cost;
            Score = score;
        }
    }

    private readonly struct VisibilitySearchNode
    {
        public readonly int NodeIndex;
        public readonly float Cost;
        public readonly float Score;

        public VisibilitySearchNode(int nodeIndex, float cost, float score)
        {
            NodeIndex = nodeIndex;
            Cost = cost;
            Score = score;
        }
    }

    private readonly struct RegionEdge
    {
        public readonly int ToRegionId;
        public readonly bool UsesTransition;
        public readonly int TransitionId;
        public readonly bool IsForward;
        public readonly Vector3 EntryWorld;
        public readonly Vector3 ExitWorld;
        public readonly float Cost;

        public RegionEdge(int toRegionId, int transitionId, bool isForward, Vector3 entryWorld, Vector3 exitWorld, float cost)
        {
            ToRegionId = toRegionId;
            UsesTransition = true;
            TransitionId = transitionId;
            IsForward = isForward;
            EntryWorld = entryWorld;
            ExitWorld = exitWorld;
            Cost = cost;
        }

        public RegionEdge(int toRegionId, Vector3 entryWorld, Vector3 exitWorld, float cost)
        {
            ToRegionId = toRegionId;
            UsesTransition = false;
            TransitionId = -1;
            IsForward = true;
            EntryWorld = entryWorld;
            ExitWorld = exitWorld;
            Cost = cost;
        }
    }

    private readonly struct RegionConnectionInfo
    {
        public readonly int FromRegionId;
        public readonly int ToRegionId;
        public readonly bool UsesTransition;
        public readonly int TransitionId;
        public readonly bool IsForward;
        public readonly Vector2 PortalLocalA;
        public readonly Vector2 PortalLocalB;
        public readonly float Cost;

        public RegionConnectionInfo(
            int fromRegionId,
            int toRegionId,
            bool usesTransition,
            int transitionId,
            bool isForward,
            Vector2 portalLocalA,
            Vector2 portalLocalB,
            float cost)
        {
            FromRegionId = fromRegionId;
            ToRegionId = toRegionId;
            UsesTransition = usesTransition;
            TransitionId = transitionId;
            IsForward = isForward;
            PortalLocalA = portalLocalA;
            PortalLocalB = portalLocalB;
            Cost = cost;
        }
    }

    public static bool TryFindRegionPath(
        MapNavigationBuildDataContext context,
        int startRegionId,
        int targetRegionId,
        Vector3 startPosition,
        Vector3 targetPosition,
        float agentRadius,
        bool allowTransitions,
        out List<MapNavigationQuery.PathStep> path,
        out float cost)
    {
        return TryFindRegionPath(
            new BuildDataRegionGraph(context),
            startRegionId,
            targetRegionId,
            startPosition,
            targetPosition,
            agentRadius,
            allowTransitions,
            out path,
            out cost);
    }

    public static MapNavigationQuery.InternalPathResult FindInternalRegionPath(
        MapNavigationBuildDataContext context,
        int regionId,
        Vector3 startWorldPosition,
        Vector3 targetWorldPosition,
        float agentRadius,
        out List<Vector3> waypoints)
    {
        waypoints = new List<Vector3>();
        if (!context.TryFindRegion(regionId, out MapNavRegionData region))
            return MapNavigationQuery.InternalPathResult.Failed;

        Vector2 start = context.ToLocal2D(startWorldPosition);
        Vector2 target = context.ToLocal2D(targetWorldPosition);
        IReadOnlyList<MapNavObstacleData> obstacles = context.GetRegionObstacles(region);
        if (IsInsideAnyObstacle(context, start, obstacles) || IsInsideAnyObstacle(context, target, obstacles))
            return MapNavigationQuery.InternalPathResult.Failed;

        if (obstacles.Count == 0 || HasLineOfSight(context, start, target, obstacles))
            return MapNavigationQuery.InternalPathResult.Direct;

        List<Vector2> nodes = Rent(Vector2ListPool);
        List<int> pathIndices = Rent(IntListPool);
        try
        {
            nodes.Add(start);
            nodes.Add(target);
            for (int i = 0; i < obstacles.Count; i++)
            {
                MapNavObstacleData obstacle = obstacles[i];
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

                    if (IsInsideAnyObstacle(context, candidate, obstacles))
                        continue;

                    nodes.Add(candidate);
                }
            }

            if (!TryFindVisibilityPath(nodes, new BuildDataVisibilityGraph(context, obstacles), pathIndices))
                return MapNavigationQuery.InternalPathResult.Failed;

            for (int i = 1; i < pathIndices.Count; i++)
                waypoints.Add(context.ToWorld(region, nodes[pathIndices[i]]));

            return waypoints.Count > 0 ? MapNavigationQuery.InternalPathResult.PathFound : MapNavigationQuery.InternalPathResult.Direct;
        }
        finally
        {
            Return(nodes, Vector2ListPool);
            Return(pathIndices, IntListPool);
        }
    }

    public static bool TryFindRegionPath(
        MapNavigationBlobDataContext context,
        int startRegionId,
        int targetRegionId,
        Vector3 startPosition,
        Vector3 targetPosition,
        float agentRadius,
        bool allowTransitions,
        out List<MapNavigationQuery.PathStep> path,
        out float cost)
    {
        return TryFindRegionPath(
            new BlobDataRegionGraph(context),
            startRegionId,
            targetRegionId,
            startPosition,
            targetPosition,
            agentRadius,
            allowTransitions,
            out path,
            out cost);
    }

    public static MapNavigationQuery.InternalPathResult FindInternalRegionPath(
        MapNavigationBlobDataContext context,
        int regionId,
        Vector3 startWorldPosition,
        Vector3 targetWorldPosition,
        float agentRadius,
        out List<Vector3> waypoints)
    {
        waypoints = new List<Vector3>();
        if (!context.TryFindRegion(regionId, out MapNavRegionBlob region))
            return MapNavigationQuery.InternalPathResult.Failed;

        Vector2 start = context.ToLocal2D(startWorldPosition);
        Vector2 target = context.ToLocal2D(targetWorldPosition);
        if (IsInsideAnyObstacle(context, region, start) || IsInsideAnyObstacle(context, region, target))
            return MapNavigationQuery.InternalPathResult.Failed;

        if (region.ObstacleCount == 0 || HasLineOfSight(context, region, start, target))
            return MapNavigationQuery.InternalPathResult.Direct;

        List<Vector2> nodes = Rent(Vector2ListPool);
        List<int> pathIndices = Rent(IntListPool);
        try
        {
            nodes.Add(start);
            nodes.Add(target);
            for (int i = 0; i < region.ObstacleCount; i++)
            {
                if (!context.TryGetObstacleAt(region.ObstacleStart + i, out MapNavObstacleBlob obstacle))
                    continue;

                BlobPointRange obstaclePoints = new(context, obstacle.PointStart, obstacle.PointCount);
                if (obstaclePoints.Count < 3)
                    continue;

                Vector2 center = AveragePoint(obstaclePoints);
                float padding = Mathf.Max(0f, agentRadius) + Mathf.Max(0f, obstacle.CornerPadding);
                for (int p = 0; p < obstaclePoints.Count; p++)
                {
                    Vector2 fromCenter = obstaclePoints[p] - center;
                    Vector2 candidate = obstaclePoints[p] + (fromCenter.sqrMagnitude > 0.0001f ? fromCenter.normalized * padding : Vector2.zero);
                    if (!context.ContainsRegion(region, candidate, 0f))
                        continue;

                    if (IsInsideAnyObstacle(context, region, candidate))
                        continue;

                    nodes.Add(candidate);
                }
            }

            if (!TryFindVisibilityPath(nodes, new BlobVisibilityGraph(context, region), pathIndices))
                return MapNavigationQuery.InternalPathResult.Failed;

            for (int i = 1; i < pathIndices.Count; i++)
                waypoints.Add(context.ToWorld(region, nodes[pathIndices[i]]));

            return waypoints.Count > 0 ? MapNavigationQuery.InternalPathResult.PathFound : MapNavigationQuery.InternalPathResult.Direct;
        }
        finally
        {
            Return(nodes, Vector2ListPool);
            Return(pathIndices, IntListPool);
        }
    }

    private static bool TryFindRegionPath<TGraph>(
        TGraph graph,
        int startRegionId,
        int targetRegionId,
        Vector3 startPosition,
        Vector3 targetPosition,
        float agentRadius,
        bool allowTransitions,
        out List<MapNavigationQuery.PathStep> path,
        out float cost)
        where TGraph : struct, IRegionGraph
    {
        path = new List<MapNavigationQuery.PathStep>();
        cost = 0f;
        if (!graph.TryGetRegion(startRegionId, out MapNavigationRegionInfo startRegion)
            || !graph.TryGetRegion(targetRegionId, out _))
        {
            return false;
        }

        if (startRegionId == targetRegionId)
            return true;

        List<RegionSearchNode> open = Rent(RegionSearchNodeListPool);
        HashSet<int> closed = Rent(IntHashSetPool);
        Dictionary<int, float> bestCost = Rent(IntFloatDictionaryPool);
        Dictionary<int, Vector3> endPositions = Rent(IntVector3DictionaryPool);
        Dictionary<int, MapNavigationQuery.PathStep> cameFrom = Rent(IntPathStepDictionaryPool);
        try
        {
            bestCost[startRegionId] = 0f;
            endPositions[startRegionId] = startPosition;
            Enqueue(open, new RegionSearchNode(startRegionId, 0f, MapNavGeometry.PlanarDistance(startPosition, targetPosition)));

            while (open.Count > 0)
            {
                RegionSearchNode node = Pop(open);
                if (!bestCost.TryGetValue(node.RegionId, out float currentCost) || !Mathf.Approximately(currentCost, node.Cost))
                    continue;

                if (!closed.Add(node.RegionId))
                    continue;

                if (node.RegionId == targetRegionId)
                    break;

                Vector3 currentEnd = endPositions[node.RegionId];
            int connectionCount = graph.GetConnectionCount(node.RegionId);
            for (int connectionIndex = 0; connectionIndex < connectionCount; connectionIndex++)
            {
                if (!graph.TryGetConnectionAt(node.RegionId, connectionIndex, out RegionConnectionInfo connection))
                    continue;

                if (!TryEvaluateConnection(graph, connection, targetRegionId, currentEnd, targetPosition, agentRadius, allowTransitions, out RegionEdge edge))
                    continue;

                Visit(edge);
            }

                void Visit(RegionEdge edge)
                {
                    float nextCost = currentCost + edge.Cost;
                    if (bestCost.TryGetValue(edge.ToRegionId, out float knownCost) && nextCost >= knownCost)
                        return;

                    MapNavigationQuery.PathStep step = edge.UsesTransition
                        ? new MapNavigationQuery.PathStep(node.RegionId, edge.ToRegionId, edge.TransitionId, edge.IsForward)
                        : new MapNavigationQuery.PathStep(node.RegionId, edge.ToRegionId, edge.EntryWorld, edge.ExitWorld);
                    cameFrom[edge.ToRegionId] = step;
                    bestCost[edge.ToRegionId] = nextCost;
                    endPositions[edge.ToRegionId] = edge.ExitWorld;
                    Enqueue(open, new RegionSearchNode(edge.ToRegionId, nextCost, nextCost + MapNavGeometry.PlanarDistance(edge.ExitWorld, targetPosition)));
                }
            }

            if (!bestCost.ContainsKey(targetRegionId))
                return false;

            cost = bestCost[targetRegionId];
            int cursor = targetRegionId;
            while (cursor != startRegionId)
            {
                MapNavigationQuery.PathStep step = cameFrom[cursor];
                path.Add(step);
                cursor = step.FromRegionId;
            }

            path.Reverse();
            return path.Count > 0;
        }
        finally
        {
            Return(open, RegionSearchNodeListPool);
            Return(closed, IntHashSetPool);
            Return(bestCost, IntFloatDictionaryPool);
            Return(endPositions, IntVector3DictionaryPool);
            Return(cameFrom, IntPathStepDictionaryPool);
        }
    }

    private interface IRegionGraph
    {
        int RegionCount { get; }
        bool TryGetRegion(int regionId, out MapNavigationRegionInfo region);
        int GetConnectionCount(int regionId);
        bool TryGetConnectionAt(int regionId, int connectionIndex, out RegionConnectionInfo connection);
        Vector2 WorldToLocal2D(Vector3 worldPosition);
        Vector3 RegionToWorld(int regionId, Vector2 localPoint);
        void GetTransitionEndpointWorld(int transitionId, bool isForward, float agentRadius, out Vector3 entry, out Vector3 exit);
    }

    private readonly struct BuildDataRegionGraph : IRegionGraph
    {
        private readonly MapNavigationBuildDataContext _context;

        public BuildDataRegionGraph(MapNavigationBuildDataContext context)
        {
            _context = context;
        }

        public int RegionCount => _context.RegionCount;

        public bool TryGetRegion(int regionId, out MapNavigationRegionInfo region)
        {
            if (!_context.TryFindRegion(regionId, out MapNavRegionData source))
            {
                region = default;
                return false;
            }

            region = new MapNavigationRegionInfo(source.Id, source.NavLayerId, source.Height, source.Cost);
            return true;
        }

        public int GetConnectionCount(int regionId)
        {
            return _context.TryFindRegion(regionId, out MapNavRegionData region)
                ? region.ConnectionCount
                : 0;
        }

        public bool TryGetConnectionAt(int regionId, int connectionIndex, out RegionConnectionInfo connection)
        {
            if (!_context.TryFindRegion(regionId, out MapNavRegionData region)
                || !_context.TryGetConnectionAt(region.ConnectionStart + connectionIndex, out MapNavRegionConnectionData source))
            {
                connection = default;
                return false;
            }

            connection = new RegionConnectionInfo(
                source.FromRegionId,
                source.ToRegionId,
                source.UsesTransition,
                source.TransitionId,
                source.IsForward,
                source.PortalLocalA,
                source.PortalLocalB,
                source.Cost);
            return connection.FromRegionId == regionId;
        }

        public Vector3 RegionToWorld(int regionId, Vector2 localPoint)
        {
            return _context.TryFindRegion(regionId, out MapNavRegionData region)
                ? _context.ToWorld(region, localPoint)
                : default;
        }

        public Vector2 WorldToLocal2D(Vector3 worldPosition)
        {
            return _context.ToLocal2D(worldPosition);
        }

        public void GetTransitionEndpointWorld(int transitionId, bool isForward, float agentRadius, out Vector3 entry, out Vector3 exit)
        {
            if (!_context.TryFindTransition(transitionId, out MapNavTransitionData transition))
            {
                entry = default;
                exit = default;
                return;
            }

            MapNavigationPathContextUtility.GetTransitionEndpointWorld(_context, transition, isForward, agentRadius, out entry, out exit);
        }
    }

    private readonly struct BlobDataRegionGraph : IRegionGraph
    {
        private readonly MapNavigationBlobDataContext _context;

        public BlobDataRegionGraph(MapNavigationBlobDataContext context)
        {
            _context = context;
        }

        public int RegionCount => _context.RegionCount;

        public bool TryGetRegion(int regionId, out MapNavigationRegionInfo region)
        {
            if (!_context.TryFindRegion(regionId, out MapNavRegionBlob source))
            {
                region = default;
                return false;
            }

            region = new MapNavigationRegionInfo(source.Id, source.NavLayerId, source.Height, source.Cost);
            return true;
        }

        public int GetConnectionCount(int regionId)
        {
            return _context.TryFindRegion(regionId, out MapNavRegionBlob region)
                ? region.ConnectionCount
                : 0;
        }

        public bool TryGetConnectionAt(int regionId, int connectionIndex, out RegionConnectionInfo connection)
        {
            if (!_context.TryFindRegion(regionId, out MapNavRegionBlob region)
                || !_context.TryGetConnectionAt(region.ConnectionStart + connectionIndex, out MapNavRegionConnectionBlob source))
            {
                connection = default;
                return false;
            }

            connection = new RegionConnectionInfo(
                source.FromRegionId,
                source.ToRegionId,
                source.UsesTransition != 0,
                source.TransitionId,
                source.IsForward != 0,
                new Vector2(source.PortalLocalA.x, source.PortalLocalA.y),
                new Vector2(source.PortalLocalB.x, source.PortalLocalB.y),
                source.Cost);
            return connection.FromRegionId == regionId;
        }

        public Vector3 RegionToWorld(int regionId, Vector2 localPoint)
        {
            return _context.TryFindRegion(regionId, out MapNavRegionBlob region)
                ? _context.ToWorld(region, localPoint)
                : default;
        }

        public Vector2 WorldToLocal2D(Vector3 worldPosition)
        {
            return _context.ToLocal2D(worldPosition);
        }

        public void GetTransitionEndpointWorld(int transitionId, bool isForward, float agentRadius, out Vector3 entry, out Vector3 exit)
        {
            if (!_context.TryFindTransition(transitionId, out MapNavTransitionBlob transition))
            {
                entry = default;
                exit = default;
                return;
            }

            MapNavigationPathContextUtility.GetTransitionEndpointWorld(_context, transition, isForward, agentRadius, out entry, out exit);
        }
    }

    private static bool TryEvaluateConnection<TGraph>(
        TGraph graph,
        RegionConnectionInfo connection,
        int targetRegionId,
        Vector3 currentEnd,
        Vector3 targetPosition,
        float agentRadius,
        bool allowTransitions,
        out RegionEdge edge)
        where TGraph : struct, IRegionGraph
    {
        edge = default;
        if (connection.UsesTransition)
        {
            if (!allowTransitions)
                return false;

            graph.GetTransitionEndpointWorld(connection.TransitionId, connection.IsForward, agentRadius, out Vector3 transitionEntry, out Vector3 transitionExit);
            float transitionCost = MapNavGeometry.PlanarDistance(currentEnd, transitionEntry)
                + MapNavGeometry.PlanarDistance(transitionEntry, transitionExit)
                + Mathf.Max(0f, connection.Cost);
            edge = new RegionEdge(connection.ToRegionId, connection.TransitionId, connection.IsForward, transitionEntry, transitionExit, transitionCost);
            return true;
        }

        Vector2 portal = GetBestPortalPoint(
            connection.PortalLocalA,
            connection.PortalLocalB,
            currentEnd,
            connection.ToRegionId == targetRegionId ? targetPosition : graph.RegionToWorld(connection.ToRegionId, (connection.PortalLocalA + connection.PortalLocalB) * 0.5f),
            graph);
        Vector3 entry = graph.RegionToWorld(connection.FromRegionId, portal);
        Vector3 exit = graph.RegionToWorld(connection.ToRegionId, portal);
        float cost = MapNavGeometry.PlanarDistance(currentEnd, entry)
            + MapNavGeometry.PlanarDistance(entry, exit)
            + Mathf.Max(0f, connection.Cost);
        edge = new RegionEdge(connection.ToRegionId, entry, exit, cost);
        return true;
    }

    public static void GetTransitionEndpointWorld(
        MapNavigationBuildDataContext context,
        MapNavTransitionData transition,
        bool isForward,
        float agentRadius,
        out Vector3 entry,
        out Vector3 exit)
    {
        GetTransitionEndpointCenters(context.GetTransitionPoints(transition), transition.UpDirection, out Vector2 fromCenter, out Vector2 toCenter);
        GetEndpointLocal(fromCenter, toCenter, isForward, agentRadius, out Vector2 entryLocal, out Vector2 exitLocal);
        entry = context.ToWorld(transition, entryLocal);
        exit = context.ToWorld(transition, exitLocal);
    }

    public static void GetTransitionEndpointWorld(
        MapNavigationBlobDataContext context,
        MapNavTransitionBlob transition,
        bool isForward,
        float agentRadius,
        out Vector3 entry,
        out Vector3 exit)
    {
        GetTransitionEndpointCenters(context, transition, out Vector2 fromCenter, out Vector2 toCenter);
        GetEndpointLocal(fromCenter, toCenter, isForward, agentRadius, out Vector2 entryLocal, out Vector2 exitLocal);
        entry = context.ToWorld(transition, entryLocal);
        exit = context.ToWorld(transition, exitLocal);
    }

    private static void GetEndpointLocal(
        Vector2 fromCenter,
        Vector2 toCenter,
        bool isForward,
        float agentRadius,
        out Vector2 entryLocal,
        out Vector2 exitLocal)
    {
        entryLocal = isForward ? fromCenter : toCenter;
        exitLocal = isForward ? toCenter : fromCenter;
        Vector2 inward = exitLocal - entryLocal;

        if (inward.sqrMagnitude > 0.0001f && agentRadius > 0f)
        {
            Vector2 offset = inward.normalized * agentRadius;
            entryLocal += offset;
            exitLocal -= offset;
        }
    }

    private static void GetTransitionEndpointCenters(
        IReadOnlyList<Vector2> points,
        Vector2 upDirection,
        out Vector2 fromCenter,
        out Vector2 toCenter)
    {
        Vector2 direction = upDirection.sqrMagnitude > 0.0001f ? upDirection.normalized : Vector2.up;
        fromCenter = GetEndpointCenter(points, direction, useMin: true);
        toCenter = GetEndpointCenter(points, direction, useMin: false);
    }

    private static void GetTransitionEndpointCenters(
        MapNavigationBlobDataContext context,
        MapNavTransitionBlob transition,
        out Vector2 fromCenter,
        out Vector2 toCenter)
    {
        Vector2 direction = new(transition.UpDirection.x, transition.UpDirection.y);
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.up;
        fromCenter = GetEndpointCenter(context, transition.PointStart, transition.PointCount, direction, useMin: true);
        toCenter = GetEndpointCenter(context, transition.PointStart, transition.PointCount, direction, useMin: false);
    }

    private static Vector2 GetEndpointCenter(IReadOnlyList<Vector2> points, Vector2 direction, bool useMin)
    {
        if (points == null || points.Count == 0)
            return Vector2.zero;

        if (points.Count == 1)
            return points[0];

        int firstIndex = 0;
        int secondIndex = 1;
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

    private static Vector2 GetEndpointCenter(MapNavigationBlobDataContext context, int pointStart, int pointCount, Vector2 direction, bool useMin)
    {
        if (pointCount <= 0 || !context.TryGetPointAt(pointStart, out Vector2 first))
            return Vector2.zero;

        if (pointCount == 1 || !context.TryGetPointAt(pointStart + 1, out Vector2 second))
            return first;

        Vector2 best = first;
        Vector2 secondBest = second;
        float bestProjection = Vector2.Dot(best, direction);
        float secondProjection = Vector2.Dot(secondBest, direction);
        if (IsBetterProjection(secondProjection, bestProjection, useMin))
        {
            (best, secondBest) = (secondBest, best);
            (bestProjection, secondProjection) = (secondProjection, bestProjection);
        }

        for (int i = 2; i < pointCount; i++)
        {
            if (!context.TryGetPointAt(pointStart + i, out Vector2 point))
                continue;

            float projected = Vector2.Dot(point, direction);
            if (IsBetterProjection(projected, bestProjection, useMin))
            {
                secondBest = best;
                secondProjection = bestProjection;
                best = point;
                bestProjection = projected;
                continue;
            }

            if (IsBetterProjection(projected, secondProjection, useMin))
            {
                secondBest = point;
                secondProjection = projected;
            }
        }

        return (best + secondBest) * 0.5f;
    }

    private static bool IsBetterProjection(float candidate, float current, bool useMin)
    {
        return useMin ? candidate < current : candidate > current;
    }

    private interface IVisibilityGraph
    {
        bool HasLineOfSight(Vector2 from, Vector2 to);
    }

    private readonly struct BuildDataVisibilityGraph : IVisibilityGraph
    {
        private readonly MapNavigationBuildDataContext _context;
        private readonly IReadOnlyList<MapNavObstacleData> _obstacles;

        public BuildDataVisibilityGraph(MapNavigationBuildDataContext context, IReadOnlyList<MapNavObstacleData> obstacles)
        {
            _context = context;
            _obstacles = obstacles;
        }

        public bool HasLineOfSight(Vector2 from, Vector2 to)
        {
            return MapNavigationPathContextUtility.HasLineOfSight(_context, from, to, _obstacles);
        }
    }

    private readonly struct BlobVisibilityGraph : IVisibilityGraph
    {
        private readonly MapNavigationBlobDataContext _context;
        private readonly MapNavRegionBlob _region;

        public BlobVisibilityGraph(MapNavigationBlobDataContext context, MapNavRegionBlob region)
        {
            _context = context;
            _region = region;
        }

        public bool HasLineOfSight(Vector2 from, Vector2 to)
        {
            return MapNavigationPathContextUtility.HasLineOfSight(_context, _region, from, to);
        }
    }

    private static bool TryFindVisibilityPath<TGraph>(IReadOnlyList<Vector2> nodes, TGraph graph, List<int> path)
        where TGraph : struct, IVisibilityGraph
    {
        path.Clear();
        List<VisibilitySearchNode> open = Rent(VisibilitySearchNodeListPool);
        HashSet<int> closed = Rent(IntHashSetPool);
        Dictionary<int, float> bestCost = Rent(IntFloatDictionaryPool);
        Dictionary<int, int> cameFrom = Rent(IntIntDictionaryPool);
        try
        {
            bestCost[0] = 0f;
            Enqueue(open, new VisibilitySearchNode(0, 0f, Vector2.Distance(nodes[0], nodes[1])));

            while (open.Count > 0)
            {
                VisibilitySearchNode node = Pop(open);
                if (!bestCost.TryGetValue(node.NodeIndex, out float currentCost) || !Mathf.Approximately(currentCost, node.Cost))
                    continue;

                if (node.NodeIndex == 1)
                    break;

                if (!closed.Add(node.NodeIndex))
                    continue;

                for (int next = 0; next < nodes.Count; next++)
                {
                    if (next == node.NodeIndex || closed.Contains(next))
                        continue;

                    if (!graph.HasLineOfSight(nodes[node.NodeIndex], nodes[next]))
                        continue;

                    float nextCost = currentCost + Vector2.Distance(nodes[node.NodeIndex], nodes[next]);
                    if (bestCost.TryGetValue(next, out float knownCost) && nextCost >= knownCost)
                        continue;

                    bestCost[next] = nextCost;
                    cameFrom[next] = node.NodeIndex;
                    Enqueue(open, new VisibilitySearchNode(next, nextCost, nextCost + Vector2.Distance(nodes[next], nodes[1])));
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
        finally
        {
            Return(open, VisibilitySearchNodeListPool);
            Return(closed, IntHashSetPool);
            Return(bestCost, IntFloatDictionaryPool);
            Return(cameFrom, IntIntDictionaryPool);
        }
    }

    private static bool HasLineOfSight(MapNavigationBuildDataContext context, Vector2 from, Vector2 to, IReadOnlyList<MapNavObstacleData> obstacles)
    {
        for (int i = 0; i < obstacles.Count; i++)
        {
            IReadOnlyList<Vector2> points = context.GetObstaclePoints(obstacles[i]);
            if (BlocksLine(points, from, to))
                return false;
        }

        return true;
    }

    private static bool HasLineOfSight(MapNavigationBlobDataContext context, MapNavRegionBlob region, Vector2 from, Vector2 to)
    {
        for (int i = 0; i < region.ObstacleCount; i++)
        {
            if (!context.TryGetObstacleAt(region.ObstacleStart + i, out MapNavObstacleBlob obstacle))
                continue;

            if (BlocksLine(new BlobPointRange(context, obstacle.PointStart, obstacle.PointCount), from, to))
                return false;
        }

        return true;
    }

    private static bool BlocksLine(IReadOnlyList<Vector2> points, Vector2 from, Vector2 to)
    {
        return BlocksLineCore(points, from, to);
    }

    private static bool BlocksLine(BlobPointRange points, Vector2 from, Vector2 to)
    {
        return BlocksLineCore(points, from, to);
    }

    private static bool BlocksLineCore<TPoints>(TPoints points, Vector2 from, Vector2 to)
        where TPoints : IReadOnlyList<Vector2>
    {
        if (points.Count < 3)
            return false;

        if (MapNavGeometry.ContainsPoint(points, from) || MapNavGeometry.ContainsPoint(points, to))
            return true;

        if (MapNavGeometry.ContainsPoint(points, (from + to) * 0.5f))
            return true;

        for (int p = 0, previous = points.Count - 1; p < points.Count; previous = p++)
        {
            if (MapNavGeometry.SegmentsIntersect(from, to, points[previous], points[p]))
                return true;
        }

        return false;
    }

    private static Vector2 AveragePoint(BlobPointRange points)
    {
        if (points.Count == 0)
            return Vector2.zero;

        Vector2 sum = Vector2.zero;
        for (int i = 0; i < points.Count; i++)
            sum += points[i];

        return sum / points.Count;
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

    private static bool IsInsideAnyObstacle(MapNavigationBlobDataContext context, MapNavRegionBlob region, Vector2 point)
    {
        for (int i = 0; i < region.ObstacleCount; i++)
        {
            if (!context.TryGetObstacleAt(region.ObstacleStart + i, out MapNavObstacleBlob obstacle))
                continue;

            if (context.HasEnoughObstaclePoints(obstacle, 3) && context.ContainsObstacle(obstacle, point))
                return true;
        }

        return false;
    }

    private readonly struct BlobPointRange : IReadOnlyList<Vector2>
    {
        private readonly MapNavigationBlobDataContext _context;
        private readonly int _pointStart;

        public BlobPointRange(MapNavigationBlobDataContext context, int pointStart, int pointCount)
        {
            _context = context;
            _pointStart = pointStart;
            Count = Mathf.Max(0, pointCount);
        }

        public int Count { get; }

        public Vector2 this[int index]
        {
            get
            {
                if (index < 0 || index >= Count)
                    return default;

                return _context.TryGetPointAt(_pointStart + index, out Vector2 point) ? point : default;
            }
        }

        public IEnumerator<Vector2> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
                yield return this[i];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private static List<T> Rent<T>(Stack<List<T>> pool)
    {
        return pool.Count > 0 ? pool.Pop() : new List<T>();
    }

    private static HashSet<T> Rent<T>(Stack<HashSet<T>> pool)
    {
        return pool.Count > 0 ? pool.Pop() : new HashSet<T>();
    }

    private static Dictionary<TKey, TValue> Rent<TKey, TValue>(Stack<Dictionary<TKey, TValue>> pool)
    {
        return pool.Count > 0 ? pool.Pop() : new Dictionary<TKey, TValue>();
    }

    private static void Return<T>(List<T> list, Stack<List<T>> pool)
    {
        list.Clear();
        if (pool.Count < MaxPooledContainersPerType)
            pool.Push(list);
    }

    private static void Return<T>(HashSet<T> set, Stack<HashSet<T>> pool)
    {
        set.Clear();
        if (pool.Count < MaxPooledContainersPerType)
            pool.Push(set);
    }

    private static void Return<TKey, TValue>(Dictionary<TKey, TValue> dictionary, Stack<Dictionary<TKey, TValue>> pool)
    {
        dictionary.Clear();
        if (pool.Count < MaxPooledContainersPerType)
            pool.Push(dictionary);
    }

    private static void Enqueue(List<RegionSearchNode> heap, RegionSearchNode node)
    {
        heap.Add(node);
        int index = heap.Count - 1;
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (heap[parent].Score <= heap[index].Score)
                break;

            (heap[parent], heap[index]) = (heap[index], heap[parent]);
            index = parent;
        }
    }

    private static RegionSearchNode Pop(List<RegionSearchNode> heap)
    {
        RegionSearchNode result = heap[0];
        int last = heap.Count - 1;
        heap[0] = heap[last];
        heap.RemoveAt(last);

        int index = 0;
        while (true)
        {
            int left = (index * 2) + 1;
            int right = left + 1;
            if (left >= heap.Count)
                break;

            int best = right < heap.Count && heap[right].Score < heap[left].Score ? right : left;
            if (heap[index].Score <= heap[best].Score)
                break;

            (heap[index], heap[best]) = (heap[best], heap[index]);
            index = best;
        }

        return result;
    }

    private static void Enqueue(List<VisibilitySearchNode> heap, VisibilitySearchNode node)
    {
        heap.Add(node);
        int index = heap.Count - 1;
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (heap[parent].Score <= heap[index].Score)
                break;

            (heap[parent], heap[index]) = (heap[index], heap[parent]);
            index = parent;
        }
    }

    private static VisibilitySearchNode Pop(List<VisibilitySearchNode> heap)
    {
        VisibilitySearchNode result = heap[0];
        int last = heap.Count - 1;
        heap[0] = heap[last];
        heap.RemoveAt(last);

        int index = 0;
        while (true)
        {
            int left = (index * 2) + 1;
            int right = left + 1;
            if (left >= heap.Count)
                break;

            int best = right < heap.Count && heap[right].Score < heap[left].Score ? right : left;
            if (heap[index].Score <= heap[best].Score)
                break;

            (heap[index], heap[best]) = (heap[best], heap[index]);
            index = best;
        }

        return result;
    }

    private static Vector2 GetBestPortalPoint<TGraph>(Vector2 portalA, Vector2 portalB, Vector3 fromWorld, Vector3 toWorld, TGraph graph)
        where TGraph : struct, IRegionGraph
    {
        Vector2 from = graph.WorldToLocal2D(fromWorld);
        Vector2 to = graph.WorldToLocal2D(toWorld);
        Vector2 segment = portalB - portalA;
        float sqrLength = segment.sqrMagnitude;
        if (sqrLength <= 0.000001f)
            return portalA;

        if (MapNavGeometry.TryLineSegmentIntersection(from, to, portalA, portalB, out Vector2 intersection))
            return intersection;

        float tFrom = Mathf.Clamp01(Vector2.Dot(from - portalA, segment) / sqrLength);
        float tTo = Mathf.Clamp01(Vector2.Dot(to - portalA, segment) / sqrLength);
        float t = (tFrom + tTo) * 0.5f;
        return portalA + segment * t;
    }
}
