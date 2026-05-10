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

                if (!MapNavigationRegionLinkUtility.CanLink(regionA.NavLayerId, regionA.Height, regionB.NavLayerId, regionB.Height))
                    continue;

                if (!MapNavigationRegionLinkUtility.TryFindSharedPortal(regionA.Points, regionB.Points, out Vector2 portalA, out Vector2 portalB))
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

}
