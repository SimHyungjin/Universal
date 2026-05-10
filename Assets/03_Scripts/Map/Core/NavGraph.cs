using MapNav.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace MapNav.Core
{
    public struct NavScratch : System.IDisposable
    {
        public NativeList<NavGraphHeapEntry> OpenHeap;
        public NativeHashMap<int, float> GScore;
        public NativeHashMap<int, int> CameFromKey;
        public NativeHashMap<int, NavPortal> CameFromPortal;
        public NativeHashMap<int, float2> ArrivalLocal;

        public NavScratch(int initialCapacity, Allocator allocator)
        {
            OpenHeap = new NativeList<NavGraphHeapEntry>(initialCapacity, allocator);
            GScore = new NativeHashMap<int, float>(initialCapacity, allocator);
            CameFromKey = new NativeHashMap<int, int>(initialCapacity, allocator);
            CameFromPortal = new NativeHashMap<int, NavPortal>(initialCapacity, allocator);
            ArrivalLocal = new NativeHashMap<int, float2>(initialCapacity, allocator);
        }

        public void Reset()
        {
            OpenHeap.Clear();
            GScore.Clear();
            CameFromKey.Clear();
            CameFromPortal.Clear();
            ArrivalLocal.Clear();
        }

        public void Dispose()
        {
            if (OpenHeap.IsCreated) OpenHeap.Dispose();
            if (GScore.IsCreated) GScore.Dispose();
            if (CameFromKey.IsCreated) CameFromKey.Dispose();
            if (CameFromPortal.IsCreated) CameFromPortal.Dispose();
            if (ArrivalLocal.IsCreated) ArrivalLocal.Dispose();
        }
    }

    public struct NavGraphHeapEntry
    {
        public int Key;
        public float Score;
        public float G;
    }

    public static class NavGraph
    {
        public static bool TryFindPath(
            in NavContext ctx,
            NavSpaceRef start, float3 startWorld,
            NavSpaceRef end, float3 endWorld,
            float agentRadius,
            ref NavScratch scratch,
            ref NativeList<NavSpaceRef> outNodes,
            ref NativeList<NavPortal> outPortals)
        {
            outNodes.Clear();
            outPortals.Clear();
            scratch.Reset();

            if (!ctx.IsValid || !start.IsValid || !end.IsValid)
                return false;

            if (start == end)
            {
                outNodes.Add(start);
                return true;
            }

            ref NavBlob blob = ref ctx.Blob.Value;
            int startKey = NodeKey(start);
            int endKey = NodeKey(end);

            float3 endLocal = NavMath.ToLocal3D(ctx.WorldToLocal, endWorld);
            float2 endLocal2D = new float2(endLocal.x, endLocal.z);
            float2 startLocal2D = NavMath.ToLocal2D(ctx.WorldToLocal, startWorld);

            scratch.GScore.Add(startKey, 0f);
            scratch.ArrivalLocal.Add(startKey, startLocal2D);
            float h0 = HeuristicLocal(startLocal2D, endLocal2D);
            scratch.OpenHeap.Add(new NavGraphHeapEntry { Key = startKey, Score = h0, G = 0f });
            HeapifyUp(ref scratch.OpenHeap, scratch.OpenHeap.Length - 1);

            while (scratch.OpenHeap.Length > 0)
            {
                NavGraphHeapEntry top = HeapPop(ref scratch.OpenHeap);

                if (!scratch.GScore.TryGetValue(top.Key, out float currentG))
                    continue;

                // Skip stale entry (a better path was found after this was pushed)
                if (top.G > currentG + NavMath.Epsilon)
                    continue;

                if (top.Key == endKey)
                {
                    return ReconstructPath(start, end, startKey, endKey, ref scratch, ref outNodes, ref outPortals);
                }

                NavSpaceRef current = FromKey(top.Key);
                float2 currentPoint = scratch.ArrivalLocal.TryGetValue(top.Key, out float2 storedArrival)
                    ? storedArrival
                    : GetNodeCenterLocal(ref blob, current, startLocal2D);

                if (!TryGetEdgeRange(ref blob, current, out int edgeStart, out int edgeCount))
                    continue;

                for (int e = 0; e < edgeCount; e++)
                {
                    NavEdge edge = GetEdge(ref blob, current.Kind, edgeStart + e);
                    NavSpaceRef neighbor = new NavSpaceRef(edge.ToKind, edge.ToId);
                    int neighborKey = NodeKey(neighbor);

                    if (!TryMeasureEdgeCost(in ctx, ref blob, current, edge, currentPoint, agentRadius, out float stepDistance, out float2 fromAccess, out float2 neighborArrival))
                        continue;

                    float tentativeG = currentG + edge.Cost + stepDistance;

                    if (neighbor == end)
                    {
                        if (!TryMeasureFinalCost(in ctx, ref blob, neighbor, neighborArrival, endLocal2D, agentRadius, out float finalDistance))
                            continue;
                        tentativeG += finalDistance;
                    }

                    if (scratch.GScore.TryGetValue(neighborKey, out float existingG) && tentativeG >= existingG - NavMath.Epsilon)
                        continue;

                    scratch.GScore[neighborKey] = tentativeG;
                    scratch.ArrivalLocal[neighborKey] = neighborArrival;
                    scratch.CameFromKey[neighborKey] = top.Key;
                    scratch.CameFromPortal[neighborKey] = LiftPortal(in ctx, ref blob, current, neighbor, edge, fromAccess, neighborArrival);

                    float h = neighbor == end ? 0f : HeuristicLocal(neighborArrival, endLocal2D);
                    scratch.OpenHeap.Add(new NavGraphHeapEntry { Key = neighborKey, Score = tentativeG + h, G = tentativeG });
                    HeapifyUp(ref scratch.OpenHeap, scratch.OpenHeap.Length - 1);
                }
            }

            return false;
        }

        private static bool TryMeasureEdgeCost(
            in NavContext ctx,
            ref NavBlob blob,
            NavSpaceRef current,
            NavEdge edge,
            float2 fromLocal,
            float agentRadius,
            out float cost,
            out float2 fromAccess,
            out float2 neighborArrival)
        {
            neighborArrival = (edge.PortalLocalA + edge.PortalLocalB) * 0.5f;
            fromAccess = neighborArrival;

            if (current.Kind == NavSpaceKind.Region)
            {
                if (edge.ToKind == NavSpaceKind.Transition)
                    return TryMeasureRegionToDetachedPortal(in ctx, ref blob, current.Id, fromLocal, edge, agentRadius, out cost, out fromAccess, out neighborArrival);

                fromAccess = neighborArrival;
                return NavInRegion.TryMeasurePathLocal(in ctx, current.Id, fromLocal, neighborArrival, agentRadius, out cost);
            }

            if (edge.ToKind == NavSpaceKind.Region)
            {
                if (!TrySelectTransitionToRegionAccess(ref blob, edge.ToId, fromLocal, edge, out float2 endpoint, out float2 regionPoint))
                {
                    cost = 0f;
                    return false;
                }

                fromAccess = endpoint;
                cost = math.distance(fromLocal, endpoint) + math.distance(endpoint, regionPoint);
                neighborArrival = regionPoint;
                return true;
            }

            fromAccess = neighborArrival;
            cost = math.distance(fromLocal, neighborArrival);
            return true;
        }

        private static bool TryMeasureFinalCost(
            in NavContext ctx,
            ref NavBlob blob,
            NavSpaceRef end,
            float2 fromLocal,
            float2 endLocal,
            float agentRadius,
            out float cost)
        {
            cost = 0f;
            if (end.Kind == NavSpaceKind.Region)
            {
                if (!TryGetRegionAccessPointLocal(ref blob, end.Id, fromLocal, out float2 regionFrom))
                    return false;

                fromLocal = regionFrom;
                return NavInRegion.TryMeasurePathLocal(in ctx, end.Id, fromLocal, endLocal, agentRadius, out cost);
            }

            cost = math.distance(fromLocal, endLocal);
            return true;
        }

        private static bool TryMeasureRegionToDetachedPortal(
            in NavContext ctx,
            ref NavBlob blob,
            int regionId,
            float2 fromLocal,
            NavEdge edge,
            float agentRadius,
            out float cost,
            out float2 fromAccess,
            out float2 neighborArrival)
        {
            cost = 0f;
            fromAccess = edge.PortalLocalA;
            neighborArrival = edge.PortalLocalA;

            bool hasA = TryMeasureRegionToPortalEndpoint(in ctx, ref blob, regionId, fromLocal, edge.PortalLocalA, agentRadius, out float costA, out float2 regionA);
            bool hasB = TryMeasureRegionToPortalEndpoint(in ctx, ref blob, regionId, fromLocal, edge.PortalLocalB, agentRadius, out float costB, out float2 regionB);

            if (!hasA && !hasB)
                return false;

            if (hasA && (!hasB || costA <= costB))
            {
                cost = costA;
                fromAccess = regionA;
                neighborArrival = edge.PortalLocalA;
                return true;
            }

            cost = costB;
            fromAccess = regionB;
            neighborArrival = edge.PortalLocalB;
            return true;
        }

        private static bool TryMeasureRegionToPortalEndpoint(
            in NavContext ctx,
            ref NavBlob blob,
            int regionId,
            float2 fromLocal,
            float2 portalEndpoint,
            float agentRadius,
            out float cost,
            out float2 regionPoint)
        {
            cost = 0f;
            regionPoint = portalEndpoint;
            if (!TryGetRegionAccessPointLocal(ref blob, regionId, portalEndpoint, out regionPoint))
                return false;

            if (!NavInRegion.TryMeasurePathLocal(in ctx, regionId, fromLocal, regionPoint, agentRadius, out cost))
                return false;

            cost += math.distance(regionPoint, portalEndpoint);
            return true;
        }

        private static bool TrySelectTransitionToRegionAccess(
            ref NavBlob blob,
            int regionId,
            float2 fromLocal,
            NavEdge edge,
            out float2 endpoint,
            out float2 regionPoint)
        {
            endpoint = edge.PortalLocalA;
            regionPoint = edge.PortalLocalA;

            bool hasA = TryGetRegionAccessPointLocal(ref blob, regionId, edge.PortalLocalA, out float2 regionA);
            bool hasB = TryGetRegionAccessPointLocal(ref blob, regionId, edge.PortalLocalB, out float2 regionB);
            if (!hasA && !hasB)
                return false;

            float costA = hasA ? math.distance(fromLocal, edge.PortalLocalA) + math.distance(edge.PortalLocalA, regionA) : float.PositiveInfinity;
            float costB = hasB ? math.distance(fromLocal, edge.PortalLocalB) + math.distance(edge.PortalLocalB, regionB) : float.PositiveInfinity;

            if (costA <= costB)
            {
                endpoint = edge.PortalLocalA;
                regionPoint = regionA;
                return true;
            }

            endpoint = edge.PortalLocalB;
            regionPoint = regionB;
            return true;
        }

        private static bool TryGetRegionAccessPointLocal(
            ref NavBlob blob,
            int regionId,
            float2 target,
            out float2 regionPoint)
        {
            regionPoint = target;
            if (!NavQuery.TryFindRegion(ref blob, regionId, out int regionIndex))
                return false;

            ref NavRegion region = ref blob.Regions[regionIndex];
            if (ContainsRegionPoint(ref blob, in region, target))
                return true;

            regionPoint = NavMath.ClosestPointOnPolygon(ref blob.Points, region.PointStart, region.PointCount, target, out _);
            return true;
        }

        private static bool ContainsRegionPoint(ref NavBlob blob, in NavRegion region, float2 point)
        {
            return NavMath.PolygonContains(ref blob.Points, region.PointStart, region.PointCount, point)
                || NavMath.IsNearEdge(ref blob.Points, region.PointStart, region.PointCount, point, 1e-3f);
        }

        public static int NodeKey(NavSpaceRef space) => ((int)space.Kind << 28) | (space.Id & 0x0FFFFFFF);

        public static NavSpaceRef FromKey(int key) => new NavSpaceRef((NavSpaceKind)((key >> 28) & 0xF), key & 0x0FFFFFFF);

        private static bool TryGetEdgeRange(ref NavBlob blob, NavSpaceRef space, out int edgeStart, out int edgeCount)
        {
            edgeStart = 0;
            edgeCount = 0;
            if (space.Kind == NavSpaceKind.Region)
            {
                if (!NavQuery.TryFindRegion(ref blob, space.Id, out int idx)) return false;
                int2 range = blob.RegionEdgeRange[idx];
                edgeStart = range.x;
                edgeCount = range.y;
                return true;
            }
            if (space.Kind == NavSpaceKind.Transition)
            {
                if (!NavQuery.TryFindTransition(ref blob, space.Id, out int idx)) return false;
                int2 range = blob.TransitionEdgeRange[idx];
                edgeStart = range.x;
                edgeCount = range.y;
                return true;
            }
            return false;
        }

        private static NavEdge GetEdge(ref NavBlob blob, NavSpaceKind sourceKind, int absoluteIndex)
        {
            return sourceKind == NavSpaceKind.Region
                ? blob.RegionEdges[absoluteIndex]
                : blob.TransitionEdges[absoluteIndex];
        }

        private static float2 GetNodeCenterLocal(ref NavBlob blob, NavSpaceRef space, float2 fallback)
        {
            if (space.Kind == NavSpaceKind.Region && NavQuery.TryFindRegion(ref blob, space.Id, out int rIdx))
                return blob.Regions[rIdx].Center;
            if (space.Kind == NavSpaceKind.Transition && NavQuery.TryFindTransition(ref blob, space.Id, out int tIdx))
                return blob.Transitions[tIdx].Center;
            return fallback;
        }

        private static NavPortal LiftPortal(
            in NavContext ctx,
            ref NavBlob blob,
            NavSpaceRef from,
            NavSpaceRef to,
            NavEdge edge,
            float2 fromAccess,
            float2 toArrival)
        {
            return new NavPortal
            {
                A = NavMath.ToWorld(ctx.LocalToWorld, fromAccess, GetAccessHeight(ref blob, from, edge.PortalHeight, fromAccess)),
                B = NavMath.ToWorld(ctx.LocalToWorld, toArrival, GetAccessHeight(ref blob, to, edge.PortalHeight, toArrival))
            };
        }

        private static float GetAccessHeight(ref NavBlob blob, NavSpaceRef space, float fallback, float2 local)
        {
            if (space.Kind == NavSpaceKind.Region && NavQuery.TryFindRegion(ref blob, space.Id, out int rIdx))
                return blob.Regions[rIdx].Height;
            if (space.Kind == NavSpaceKind.Transition && NavQuery.TryFindTransition(ref blob, space.Id, out int tIdx))
                return NavQuery.ComputeTransitionHeight(ref blob, blob.Transitions[tIdx], local);
            return fallback;
        }

        private static float HeuristicLocal(float2 from, float2 to)
        {
            return math.distance(from, to);
        }

        private static bool ReconstructPath(
            NavSpaceRef start,
            NavSpaceRef end,
            int startKey,
            int endKey,
            ref NavScratch scratch,
            ref NativeList<NavSpaceRef> outNodes,
            ref NativeList<NavPortal> outPortals)
        {
            // Walk back from end to start, accumulate, then reverse
            NativeList<NavSpaceRef> revNodes = new NativeList<NavSpaceRef>(8, Allocator.Temp);
            NativeList<NavPortal> revPortals = new NativeList<NavPortal>(8, Allocator.Temp);

            int cur = endKey;
            revNodes.Add(end);

            int safety = 0;
            int maxIterations = scratch.GScore.Count + 4;
            while (cur != startKey)
            {
                if (++safety > maxIterations)
                {
                    revNodes.Dispose();
                    revPortals.Dispose();
                    return false;
                }
                if (!scratch.CameFromKey.TryGetValue(cur, out int parent)) { revNodes.Dispose(); revPortals.Dispose(); return false; }
                if (!scratch.CameFromPortal.TryGetValue(cur, out NavPortal portal)) { revNodes.Dispose(); revPortals.Dispose(); return false; }
                revPortals.Add(portal);
                cur = parent;
                revNodes.Add(FromKey(parent));
            }

            outNodes.Clear();
            for (int i = revNodes.Length - 1; i >= 0; i--)
                outNodes.Add(revNodes[i]);

            outPortals.Clear();
            for (int i = revPortals.Length - 1; i >= 0; i--)
                outPortals.Add(revPortals[i]);

            revNodes.Dispose();
            revPortals.Dispose();
            return true;
        }

        // Min-heap helpers (priority queue keyed by Score)
        private static NavGraphHeapEntry HeapPop(ref NativeList<NavGraphHeapEntry> heap)
        {
            NavGraphHeapEntry top = heap[0];
            int last = heap.Length - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);

            int i = 0;
            while (true)
            {
                int l = i * 2 + 1;
                int r = i * 2 + 2;
                int smallest = i;
                if (l < heap.Length && heap[l].Score < heap[smallest].Score) smallest = l;
                if (r < heap.Length && heap[r].Score < heap[smallest].Score) smallest = r;
                if (smallest == i) break;
                NavGraphHeapEntry tmp = heap[smallest];
                heap[smallest] = heap[i];
                heap[i] = tmp;
                i = smallest;
            }
            return top;
        }

        private static void HeapifyUp(ref NativeList<NavGraphHeapEntry> heap, int from)
        {
            int i = from;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (heap[parent].Score <= heap[i].Score) break;
                NavGraphHeapEntry tmp = heap[parent];
                heap[parent] = heap[i];
                heap[i] = tmp;
                i = parent;
            }
        }
    }
}
