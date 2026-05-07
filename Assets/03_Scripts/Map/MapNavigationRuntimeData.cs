using System.Collections.Generic;
using UnityEngine;

public sealed class MapNavigationRuntimeData
{
    public readonly struct RegionLink
    {
        public readonly int FromRegionId;
        public readonly int ToRegionId;
        public readonly Vector2 PortalLocalA;
        public readonly Vector2 PortalLocalB;
        public readonly float Cost;

        public RegionLink(int fromRegionId, int toRegionId, Vector2 portalLocalA, Vector2 portalLocalB, float cost)
        {
            FromRegionId = fromRegionId;
            ToRegionId = toRegionId;
            PortalLocalA = portalLocalA;
            PortalLocalB = portalLocalB;
            Cost = cost;
        }
    }

    public readonly struct RegionConnection
    {
        public readonly bool UsesTransition;
        public readonly int TransitionId;
        public readonly RegionLink Link;

        public RegionConnection(int transitionId)
        {
            UsesTransition = true;
            TransitionId = transitionId;
            Link = default;
        }

        public RegionConnection(RegionLink link)
        {
            UsesTransition = false;
            TransitionId = -1;
            Link = link;
        }
    }

    private const float RegionLinkHeightTolerance = 0.05f;
    private const float RegionLinkLineTolerance = 0.08f;
    private const float MinimumRegionLinkPortalLength = 0.05f;

    private readonly LookupTable _lookup = new();
    private readonly RegionGraph _graph = new();

    public void Rebuild(IReadOnlyList<MapNavRegion> regions, IReadOnlyList<MapNavTransition> transitions)
    {
        _lookup.Clear();
        _graph.Clear();

        for (int i = 0; i < regions.Count; i++)
        {
            MapNavRegion region = regions[i];
            if (region == null)
                continue;

            _lookup.AddRegion(region);
            _graph.EnsureRegion(region.Id);
        }

        for (int i = 0; i < transitions.Count; i++)
        {
            MapNavTransition transition = transitions[i];
            if (transition == null)
                continue;

            _lookup.AddTransition(transition);
            _graph.AddTransition(transition.FromRegionId, transition);

            if (transition.Bidirectional)
                _graph.AddTransition(transition.ToRegionId, transition);
        }

        BuildRegionLinks(regions);
    }

    public MapNavRegion FindRegion(int regionId)
    {
        return _lookup.FindRegion(regionId);
    }

    public MapNavTransition FindTransition(int transitionId)
    {
        return _lookup.FindTransition(transitionId);
    }

    public IReadOnlyList<MapNavTransition> GetTransitionsForRegion(int regionId)
    {
        return _graph.GetTransitions(regionId);
    }

    public IReadOnlyList<RegionLink> GetRegionLinksForRegion(int regionId)
    {
        return _graph.GetLinks(regionId);
    }

    public IReadOnlyList<RegionConnection> GetConnectionsForRegion(int regionId)
    {
        return _graph.GetConnections(regionId);
    }

    private void BuildRegionLinks(IReadOnlyList<MapNavRegion> regions)
    {
        for (int a = 0; a < regions.Count; a++)
        {
            MapNavRegion regionA = regions[a];
            if (!CanLink(regionA))
                continue;

            for (int b = a + 1; b < regions.Count; b++)
            {
                MapNavRegion regionB = regions[b];
                if (!CanLink(regionB))
                    continue;

                if (regionA.NavLayerId != regionB.NavLayerId)
                    continue;

                if (Mathf.Abs(regionA.Height - regionB.Height) > RegionLinkHeightTolerance)
                    continue;

                if (!TryFindSharedPortal(regionA.Points, regionB.Points, out Vector2 portalA, out Vector2 portalB))
                    continue;

                float cost = Mathf.Max(0f, (regionA.Cost + regionB.Cost) * 0.5f);
                _graph.AddLink(new RegionLink(regionA.Id, regionB.Id, portalA, portalB, cost));
                _graph.AddLink(new RegionLink(regionB.Id, regionA.Id, portalA, portalB, cost));
            }
        }
    }

    private static bool CanLink(MapNavRegion region)
    {
        return region != null && region.Points != null && region.Points.Count >= 3;
    }

    private sealed class LookupTable
    {
        private readonly Dictionary<int, MapNavRegion> _regionsById = new();
        private readonly Dictionary<int, MapNavTransition> _transitionsById = new();

        public void Clear()
        {
            _regionsById.Clear();
            _transitionsById.Clear();
        }

        public void AddRegion(MapNavRegion region)
        {
            _regionsById[region.Id] = region;
        }

        public void AddTransition(MapNavTransition transition)
        {
            _transitionsById[transition.Id] = transition;
        }

        public MapNavRegion FindRegion(int regionId)
        {
            return _regionsById.GetValueOrDefault(regionId);
        }

        public MapNavTransition FindTransition(int transitionId)
        {
            return _transitionsById.GetValueOrDefault(transitionId);
        }
    }

    private sealed class RegionGraph
    {
        private readonly Dictionary<int, List<MapNavTransition>> _transitionsByRegionId = new();
        private readonly Dictionary<int, List<RegionLink>> _linksByRegionId = new();
        private readonly Dictionary<int, List<RegionConnection>> _connectionsByRegionId = new();

        public void Clear()
        {
            _transitionsByRegionId.Clear();
            _linksByRegionId.Clear();
            _connectionsByRegionId.Clear();
        }

        public void EnsureRegion(int regionId)
        {
            _transitionsByRegionId.TryAdd(regionId, new List<MapNavTransition>());
            _linksByRegionId.TryAdd(regionId, new List<RegionLink>());
            _connectionsByRegionId.TryAdd(regionId, new List<RegionConnection>());
        }

        public void AddTransition(int regionId, MapNavTransition transition)
        {
            GetOrCreateTransitions(regionId).Add(transition);
            GetOrCreateConnections(regionId).Add(new RegionConnection(transition.Id));
        }

        public void AddLink(RegionLink link)
        {
            GetOrCreateLinks(link.FromRegionId).Add(link);
            GetOrCreateConnections(link.FromRegionId).Add(new RegionConnection(link));
        }

        public IReadOnlyList<MapNavTransition> GetTransitions(int regionId)
        {
            return _transitionsByRegionId.TryGetValue(regionId, out List<MapNavTransition> transitions)
                ? transitions
                : System.Array.Empty<MapNavTransition>();
        }

        public IReadOnlyList<RegionLink> GetLinks(int regionId)
        {
            return _linksByRegionId.TryGetValue(regionId, out List<RegionLink> links)
                ? links
                : System.Array.Empty<RegionLink>();
        }

        public IReadOnlyList<RegionConnection> GetConnections(int regionId)
        {
            return _connectionsByRegionId.TryGetValue(regionId, out List<RegionConnection> connections)
                ? connections
                : System.Array.Empty<RegionConnection>();
        }

        private List<MapNavTransition> GetOrCreateTransitions(int regionId)
        {
            if (!_transitionsByRegionId.TryGetValue(regionId, out List<MapNavTransition> transitions))
            {
                transitions = new List<MapNavTransition>();
                _transitionsByRegionId[regionId] = transitions;
            }

            return transitions;
        }

        private List<RegionLink> GetOrCreateLinks(int regionId)
        {
            if (!_linksByRegionId.TryGetValue(regionId, out List<RegionLink> links))
            {
                links = new List<RegionLink>();
                _linksByRegionId[regionId] = links;
            }

            return links;
        }

        private List<RegionConnection> GetOrCreateConnections(int regionId)
        {
            if (!_connectionsByRegionId.TryGetValue(regionId, out List<RegionConnection> connections))
            {
                connections = new List<RegionConnection>();
                _connectionsByRegionId[regionId] = connections;
            }

            return connections;
        }
    }

    private static bool TryFindSharedPortal(IReadOnlyList<Vector2> pointsA, IReadOnlyList<Vector2> pointsB, out Vector2 portalA, out Vector2 portalB)
    {
        portalA = default;
        portalB = default;
        float bestSqrDistance = float.PositiveInfinity;
        bool foundNearbyPortal = false;

        for (int a = 0, previousA = pointsA.Count - 1; a < pointsA.Count; previousA = a++)
        {
            Vector2 a0 = pointsA[previousA];
            Vector2 a1 = pointsA[a];

            for (int b = 0, previousB = pointsB.Count - 1; b < pointsB.Count; previousB = b++)
            {
                Vector2 b0 = pointsB[previousB];
                Vector2 b1 = pointsB[b];
                if (TryGetCollinearOverlap(a0, a1, b0, b1, out portalA, out portalB))
                    return true;

                GetClosestPointsOnSegments(a0, a1, b0, b1, out Vector2 closestA, out Vector2 closestB);
                float sqrDistance = (closestA - closestB).sqrMagnitude;
                if (sqrDistance > RegionLinkLineTolerance * RegionLinkLineTolerance || sqrDistance >= bestSqrDistance)
                    continue;

                bestSqrDistance = sqrDistance;
                Vector2 portal = (closestA + closestB) * 0.5f;
                portalA = portal;
                portalB = portal;
                foundNearbyPortal = true;
            }
        }

        return foundNearbyPortal;
    }

    private static bool TryGetCollinearOverlap(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, out Vector2 overlapA, out Vector2 overlapB)
    {
        overlapA = default;
        overlapB = default;
        Vector2 axis = a1 - a0;
        float length = axis.magnitude;
        if (length <= 0.000001f)
            return false;

        Vector2 direction = axis / length;
        if (Mathf.Abs(MapNavGeometry.Cross(direction, b0 - a0)) > RegionLinkLineTolerance
            || Mathf.Abs(MapNavGeometry.Cross(direction, b1 - a0)) > RegionLinkLineTolerance)
            return false;

        float bProjection0 = Vector2.Dot(b0 - a0, direction);
        float bProjection1 = Vector2.Dot(b1 - a0, direction);
        float overlapMin = Mathf.Max(0f, Mathf.Min(bProjection0, bProjection1));
        float overlapMax = Mathf.Min(length, Mathf.Max(bProjection0, bProjection1));
        if (overlapMax - overlapMin < MinimumRegionLinkPortalLength)
            return false;

        overlapA = a0 + direction * overlapMin;
        overlapB = a0 + direction * overlapMax;
        return true;
    }

    private static void GetClosestPointsOnSegments(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, out Vector2 pointA, out Vector2 pointB)
    {
        pointA = a0;
        pointB = b0;
        float bestSqrDistance = float.PositiveInfinity;
        TestPair(a0, MapNavGeometry.ClosestPointOnSegment(a0, b0, b1), ref pointA, ref pointB, ref bestSqrDistance);
        TestPair(a1, MapNavGeometry.ClosestPointOnSegment(a1, b0, b1), ref pointA, ref pointB, ref bestSqrDistance);
        TestPair(MapNavGeometry.ClosestPointOnSegment(b0, a0, a1), b0, ref pointA, ref pointB, ref bestSqrDistance);
        TestPair(MapNavGeometry.ClosestPointOnSegment(b1, a0, a1), b1, ref pointA, ref pointB, ref bestSqrDistance);
    }

    private static void TestPair(Vector2 candidateA, Vector2 candidateB, ref Vector2 pointA, ref Vector2 pointB, ref float bestSqrDistance)
    {
        float sqrDistance = (candidateA - candidateB).sqrMagnitude;
        if (sqrDistance >= bestSqrDistance)
            return;

        pointA = candidateA;
        pointB = candidateB;
        bestSqrDistance = sqrDistance;
    }
}
