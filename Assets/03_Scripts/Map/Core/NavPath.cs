using MapNav.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace MapNav.Core
{
    public static class NavPath
    {
        private const float DirectTravelTolerance = 0.05f;

        // Top-level: classifies start/end (or projects), runs A*, then funnel-smooths.
        // Caller owns the scratch + temp lists for reuse across calls.
        public static bool TryBuild(
            in NavContext ctx,
            float3 startWorld,
            float3 endWorld,
            float agentRadius,
            float boundaryTolerance,
            ref NavScratch scratch,
            ref NativeList<NavSpaceRef> tmpNodes,
            ref NativeList<NavPortal> tmpPortals,
            ref NativeList<float3> outWaypoints)
        {
            outWaypoints.Clear();
            if (!ctx.IsValid) return false;

            if (!NavQuery.TryClassify(in ctx, startWorld, boundaryTolerance, out NavSpaceRef startSpace))
            {
                if (!NavQuery.TryProjectToNearestSpace(in ctx, startWorld, out float3 projectedStart, out startSpace))
                    return false;
                startWorld = projectedStart;
            }
            else
            {
                startWorld = ProjectIntoSpace(in ctx, startSpace, startWorld);
            }

            if (!NavQuery.TryClassify(in ctx, endWorld, boundaryTolerance, out NavSpaceRef endSpace))
            {
                if (!NavQuery.TryProjectToNearestSpace(in ctx, endWorld, out float3 projectedEnd, out endSpace))
                    return false;
                endWorld = projectedEnd;
            }
            else
            {
                endWorld = ProjectIntoSpace(in ctx, endSpace, endWorld);
            }

            return TryBuildFromSpaces(in ctx, startSpace, startWorld, endSpace, endWorld, agentRadius, ref scratch, ref tmpNodes, ref tmpPortals, ref outWaypoints);
        }

        public static bool TryBuildFromSpaces(
            in NavContext ctx,
            NavSpaceRef startSpace, float3 startWorld,
            NavSpaceRef endSpace, float3 endWorld,
            float agentRadius,
            ref NavScratch scratch,
            ref NativeList<NavSpaceRef> tmpNodes,
            ref NativeList<NavPortal> tmpPortals,
            ref NativeList<float3> outWaypoints)
        {
            outWaypoints.Clear();
            if (CanTravelDirect(in ctx, startWorld, endWorld, agentRadius))
            {
                AddIfSeparated(startWorld, ref outWaypoints);
                AddIfSeparated(endWorld, ref outWaypoints);
                return true;
            }

            if (!NavGraph.TryFindPath(in ctx, startSpace, startWorld, endSpace, endWorld, agentRadius, ref scratch, ref tmpNodes, ref tmpPortals))
                return false;

            return AssemblePath(in ctx, startWorld, ref tmpNodes, ref tmpPortals, endWorld, agentRadius, ref outWaypoints);
        }

        private static bool AssemblePath(
            in NavContext ctx,
            float3 startWorld,
            ref NativeList<NavSpaceRef> nodes,
            ref NativeList<NavPortal> portals,
            float3 endWorld,
            float agentRadius,
            ref NativeList<float3> outWaypoints)
        {
            outWaypoints.Clear();
            AddIfSeparated(startWorld, ref outWaypoints);

            for (int i = 0; i < portals.Length; i++)
            {
                NavSpaceRef from = i < nodes.Length ? nodes[i] : default;
                NavSpaceRef to = i + 1 < nodes.Length ? nodes[i + 1] : default;
                NavSpaceRef after = i + 2 < nodes.Length ? nodes[i + 2] : default;
                if (from.Kind == NavSpaceKind.Region
                    && to.Kind == NavSpaceKind.Transition
                    && after.Kind == NavSpaceKind.Region
                    && TryAppendTransitionPass(in ctx, from, to, after, outWaypoints[outWaypoints.Length - 1], endWorld, agentRadius, ref outWaypoints))
                {
                    i++;
                    continue;
                }

                if (!AppendPortalCrossing(in ctx, from, to, portals[i], endWorld, agentRadius, ref outWaypoints))
                    return false;
            }

            NavSpaceRef last = nodes.Length > 0 ? nodes[nodes.Length - 1] : default;
            float3 current = outWaypoints[outWaypoints.Length - 1];
            if (CanTravelDirect(in ctx, current, endWorld, agentRadius))
            {
                AddIfSeparated(endWorld, ref outWaypoints);
            }
            else if (!AppendSegment(in ctx, last, current, endWorld, agentRadius, ref outWaypoints))
            {
                return false;
            }

            return true;
        }

        private static bool TryAppendTransitionPass(
            in NavContext ctx,
            NavSpaceRef fromRegion,
            NavSpaceRef transition,
            NavSpaceRef toRegion,
            float3 current,
            float3 endWorld,
            float agentRadius,
            ref NativeList<float3> outWaypoints)
        {
            if (!TryFindEdge(in ctx, fromRegion, transition, out NavEdge enterEdge)
                || !TryFindEdge(in ctx, transition, toRegion, out NavEdge exitEdge))
                return false;

            float best = float.PositiveInfinity;
            float3 bestEnterRegion = default;
            float3 bestEnterTransition = default;
            float3 bestExitTransition = default;
            float3 bestExitRegion = default;
            bool found = false;

            for (int enterIndex = 0; enterIndex < 2; enterIndex++)
            {
                float2 enterLocal = enterIndex == 0 ? enterEdge.PortalLocalA : enterEdge.PortalLocalB;
                float3 enterTransitionWorld = NavMath.ToWorld(ctx.LocalToWorld, enterLocal, enterEdge.PortalHeight);
                if (!TryGetRegionAccessPoint(in ctx, fromRegion.Id, enterTransitionWorld, out float3 enterRegionWorld))
                    continue;

                float enterRegionCost = MeasureRegionSegment(in ctx, fromRegion.Id, current, enterRegionWorld, agentRadius);
                if (float.IsPositiveInfinity(enterRegionCost))
                    continue;

                for (int exitIndex = 0; exitIndex < 2; exitIndex++)
                {
                    float2 exitLocal = exitIndex == 0 ? exitEdge.PortalLocalA : exitEdge.PortalLocalB;
                    float3 exitTransitionWorld = NavMath.ToWorld(ctx.LocalToWorld, exitLocal, exitEdge.PortalHeight);
                    if (!TryGetRegionAccessPoint(in ctx, toRegion.Id, exitTransitionWorld, out float3 exitRegionWorld))
                        continue;

                    float score = enterRegionCost
                        + PlanarDistance(enterRegionWorld, enterTransitionWorld)
                        + PlanarDistance(enterTransitionWorld, exitTransitionWorld)
                        + PlanarDistance(exitTransitionWorld, exitRegionWorld)
                        + PlanarDistance(exitRegionWorld, endWorld);

                    if (score >= best)
                        continue;

                    best = score;
                    bestEnterRegion = enterRegionWorld;
                    bestEnterTransition = enterTransitionWorld;
                    bestExitTransition = exitTransitionWorld;
                    bestExitRegion = exitRegionWorld;
                    found = true;
                }
            }

            if (!found)
                return false;

            if (!AppendSegment(in ctx, fromRegion, current, bestEnterRegion, agentRadius, ref outWaypoints))
                return false;

            AddIfSeparated(bestEnterTransition, ref outWaypoints);
            AddIfSeparated(bestExitTransition, ref outWaypoints);
            AddIfSeparated(bestExitRegion, ref outWaypoints);
            return true;
        }

        private static bool AppendPortalCrossing(
            in NavContext ctx,
            NavSpaceRef fromSpace,
            NavSpaceRef toSpace,
            NavPortal portal,
            float3 endWorld,
            float agentRadius,
            ref NativeList<float3> outWaypoints)
        {
            if (AreSameLayerRegions(in ctx, fromSpace, toSpace))
                return true;

            float3 current = outWaypoints[outWaypoints.Length - 1];

            if (fromSpace.Kind == NavSpaceKind.Region)
            {
                if (!AppendSegment(in ctx, fromSpace, current, portal.A, agentRadius, ref outWaypoints))
                    return false;
            }
            else
            {
                AddIfSeparated(portal.A, ref outWaypoints);
            }

            AddIfSeparated(portal.B, ref outWaypoints);

            return true;
        }

        private static bool AppendSegment(
            in NavContext ctx,
            NavSpaceRef fromSpace,
            float3 from,
            float3 to,
            float agentRadius,
            ref NativeList<float3> outWaypoints)
        {
            if (fromSpace.Kind == NavSpaceKind.Region)
            {
                ref NavBlob blob = ref ctx.Blob.Value;
                if (!NavQuery.TryFindRegion(ref blob, fromSpace.Id, out int regionIndex))
                    return false;

                ref NavRegion region = ref blob.Regions[regionIndex];
                float2 fromLocal = NavMath.ToLocal2D(ctx.WorldToLocal, from);
                float2 toLocal = NavMath.ToLocal2D(ctx.WorldToLocal, to);

                if (!ContainsRegionPoint(ref blob, in region, fromLocal)
                    || !ContainsRegionPoint(ref blob, in region, toLocal))
                    return false;

                NativeList<float3> corners = new NativeList<float3>(8, Allocator.Temp);
                bool reachable = NavInRegion.TryAppendCornerWaypointsLocal(
                    in ctx,
                    in region,
                    fromLocal,
                    toLocal,
                    agentRadius,
                    ref corners);

                if (!reachable)
                {
                    corners.Dispose();
                    return false;
                }

                AppendCornersFunnelSmoothed(from, to, agentRadius, ref corners, ref outWaypoints);
                corners.Dispose();
            }

            AddIfSeparated(to, ref outWaypoints);
            return true;
        }

        // The visibility-graph corners are LOS-reachable from each other but not necessarily
        // a tight string-pull. Run NavFunnel over them (each corner as a zero-width portal)
        // to drop redundant intermediate corners and pull the path against obstacle edges.
        private static void AppendCornersFunnelSmoothed(
            float3 from,
            float3 to,
            float agentRadius,
            ref NativeList<float3> corners,
            ref NativeList<float3> outWaypoints)
        {
            if (corners.Length == 0) return;

            NativeList<NavPortal> innerPortals = new NativeList<NavPortal>(corners.Length, Allocator.Temp);
            for (int i = 0; i < corners.Length; i++)
                innerPortals.Add(new NavPortal { A = corners[i], B = corners[i] });

            NativeList<float3> smoothed = new NativeList<float3>(corners.Length + 2, Allocator.Temp);
            NavFunnel.Smooth(from, ref innerPortals, to, agentRadius, ref smoothed);

            // smoothed[0] is `from` (already last in outWaypoints) and smoothed[last] is `to`
            // (caller appends it). Emit only the interior corners.
            for (int i = 1; i < smoothed.Length - 1; i++)
                AddIfSeparated(smoothed[i], ref outWaypoints);

            innerPortals.Dispose();
            smoothed.Dispose();
        }

        private static bool TryGetRegionAccessPoint(
            in NavContext ctx,
            int regionId,
            float3 target,
            out float3 regionPoint)
        {
            regionPoint = target;
            ref NavBlob blob = ref ctx.Blob.Value;
            if (!NavQuery.TryFindRegion(ref blob, regionId, out int regionIndex))
                return false;

            ref NavRegion region = ref blob.Regions[regionIndex];
            float2 local = NavMath.ToLocal2D(ctx.WorldToLocal, target);
            if (ContainsRegionPoint(ref blob, in region, local))
            {
                regionPoint = NavMath.ToWorld(ctx.LocalToWorld, local, region.Height);
                return true;
            }

            float2 closest = NavMath.ClosestPointOnPolygon(ref blob.Points, region.PointStart, region.PointCount, local, out _);
            regionPoint = NavMath.ToWorld(ctx.LocalToWorld, closest, region.Height);
            return true;
        }

        private static float MeasureRegionSegment(
            in NavContext ctx,
            int regionId,
            float3 from,
            float3 to,
            float agentRadius)
        {
            float2 fromLocal = NavMath.ToLocal2D(ctx.WorldToLocal, from);
            float2 toLocal = NavMath.ToLocal2D(ctx.WorldToLocal, to);
            return NavInRegion.TryMeasurePathLocal(in ctx, regionId, fromLocal, toLocal, agentRadius, out float cost)
                ? cost
                : float.PositiveInfinity;
        }

        private static bool TryFindEdge(in NavContext ctx, NavSpaceRef from, NavSpaceRef to, out NavEdge edge)
        {
            edge = default;
            ref NavBlob blob = ref ctx.Blob.Value;
            if (!TryGetEdgeRange(ref blob, from, out int edgeStart, out int edgeCount))
                return false;

            for (int i = 0; i < edgeCount; i++)
            {
                NavEdge candidate = from.Kind == NavSpaceKind.Region
                    ? blob.RegionEdges[edgeStart + i]
                    : blob.TransitionEdges[edgeStart + i];
                if (candidate.ToKind == to.Kind && candidate.ToId == to.Id)
                {
                    edge = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetEdgeRange(ref NavBlob blob, NavSpaceRef space, out int edgeStart, out int edgeCount)
        {
            edgeStart = 0;
            edgeCount = 0;
            if (space.Kind == NavSpaceKind.Region)
            {
                if (!NavQuery.TryFindRegion(ref blob, space.Id, out int regionIndex))
                    return false;
                int2 range = blob.RegionEdgeRange[regionIndex];
                edgeStart = range.x;
                edgeCount = range.y;
                return true;
            }

            if (space.Kind == NavSpaceKind.Transition)
            {
                if (!NavQuery.TryFindTransition(ref blob, space.Id, out int transitionIndex))
                    return false;
                int2 range = blob.TransitionEdgeRange[transitionIndex];
                edgeStart = range.x;
                edgeCount = range.y;
                return true;
            }

            return false;
        }

        private static float PlanarDistance(float3 a, float3 b)
        {
            float3 delta = a - b;
            delta.y = 0f;
            return math.length(delta);
        }

        private static bool ContainsRegionPoint(ref NavBlob blob, in NavRegion region, float2 point)
        {
            return NavMath.PolygonContains(ref blob.Points, region.PointStart, region.PointCount, point)
                || NavMath.IsNearEdge(ref blob.Points, region.PointStart, region.PointCount, point, 1e-3f);
        }

        private static float3 ProjectIntoSpace(in NavContext ctx, NavSpaceRef space, float3 world)
        {
            ref NavBlob blob = ref ctx.Blob.Value;
            float2 local = NavMath.ToLocal2D(ctx.WorldToLocal, world);

            if (space.Kind == NavSpaceKind.Region && NavQuery.TryFindRegion(ref blob, space.Id, out int regionIndex))
            {
                ref NavRegion region = ref blob.Regions[regionIndex];
                float2 projected = ProjectIntoPolygon(ref blob.Points, region.PointStart, region.PointCount, local);
                return NavMath.ToWorld(ctx.LocalToWorld, projected, region.Height);
            }

            if (space.Kind == NavSpaceKind.Transition && NavQuery.TryFindTransition(ref blob, space.Id, out int transitionIndex))
            {
                ref NavTransition transition = ref blob.Transitions[transitionIndex];
                float2 projected = ProjectIntoPolygon(ref blob.Points, transition.PointStart, transition.PointCount, local);
                float height = NavQuery.ComputeTransitionHeight(ref blob, transition, projected);
                return NavMath.ToWorld(ctx.LocalToWorld, projected, height);
            }

            return world;
        }

        private static float2 ProjectIntoPolygon(ref BlobArray<float2> points, int pointStart, int pointCount, float2 local)
        {
            if (NavMath.PolygonContains(ref points, pointStart, pointCount, local)
                || NavMath.IsNearEdge(ref points, pointStart, pointCount, local, 1e-3f))
                return local;

            return NavMath.ClosestPointOnPolygon(ref points, pointStart, pointCount, local, out _);
        }

        private static bool CanTravelDirect(
            in NavContext ctx,
            float3 from,
            float3 to,
            float agentRadius)
        {
            if (!NavQuery.TryClassify(in ctx, from, DirectTravelTolerance, out NavSpaceRef fromSpace))
                return false;

            if (!NavQuery.TryClassify(in ctx, to, DirectTravelTolerance, out NavSpaceRef toSpace))
                return false;

            if (fromSpace == toSpace)
            {
                if (fromSpace.Kind == NavSpaceKind.Region)
                {
                    float2 a = NavMath.ToLocal2D(ctx.WorldToLocal, from);
                    float2 b = NavMath.ToLocal2D(ctx.WorldToLocal, to);
                    return NavInRegion.CanTravelDirectLocal(in ctx, fromSpace.Id, a, b, agentRadius);
                }

                return SegmentSamplesStayInSameSpace(in ctx, from, to, fromSpace);
            }

            return CanTravelDirectOnSameLayer(in ctx, from, to, fromSpace, toSpace);
        }

        private static bool CanTravelDirectOnSameLayer(
            in NavContext ctx,
            float3 from,
            float3 to,
            NavSpaceRef fromSpace,
            NavSpaceRef toSpace)
        {
            if (!AreSameLayerRegions(in ctx, fromSpace, toSpace))
                return false;

            ref NavBlob blob = ref ctx.Blob.Value;
            NavQuery.TryFindRegion(ref blob, fromSpace.Id, out int fromRegionIndex);
            int layerId = blob.Regions[fromRegionIndex].LayerId;
            float2 fromLocal = NavMath.ToLocal2D(ctx.WorldToLocal, from);
            float2 toLocal = NavMath.ToLocal2D(ctx.WorldToLocal, to);

            const int Samples = 32;
            for (int i = 1; i < Samples; i++)
            {
                float t = i / (float)Samples;
                float3 p = math.lerp(from, to, t);
                if (!NavQuery.TryClassify(in ctx, p, DirectTravelTolerance, out NavSpaceRef sampled)
                    || sampled.Kind != NavSpaceKind.Region)
                    return false;

                if (!NavQuery.TryFindRegion(ref blob, sampled.Id, out int sampledRegionIndex)
                    || blob.Regions[sampledRegionIndex].LayerId != layerId)
                    return false;

                if (NavInRegion.SegmentCrossesObstacleLocal(in ctx, sampled.Id, fromLocal, toLocal))
                    return false;
            }

            return true;
        }

        private static bool AreSameLayerRegions(in NavContext ctx, NavSpaceRef a, NavSpaceRef b)
        {
            if (a.Kind != NavSpaceKind.Region || b.Kind != NavSpaceKind.Region)
                return false;

            ref NavBlob blob = ref ctx.Blob.Value;
            if (!NavQuery.TryFindRegion(ref blob, a.Id, out int aIndex)
                || !NavQuery.TryFindRegion(ref blob, b.Id, out int bIndex))
                return false;

            return blob.Regions[aIndex].LayerId == blob.Regions[bIndex].LayerId;
        }

        private static bool SegmentSamplesStayInSameSpace(
            in NavContext ctx,
            float3 from,
            float3 to,
            NavSpaceRef space)
        {
            const int Samples = 16;
            for (int i = 1; i < Samples; i++)
            {
                float t = i / (float)Samples;
                float3 p = math.lerp(from, to, t);
                if (!NavQuery.TryClassify(in ctx, p, DirectTravelTolerance, out NavSpaceRef sampled) || sampled != space)
                    return false;
            }

            return true;
        }

        private static void AddIfSeparated(float3 point, ref NativeList<float3> waypoints)
        {
            if (waypoints.Length > 0)
            {
                if (IsSamePoint(point, waypoints[waypoints.Length - 1]))
                    return;
            }

            waypoints.Add(point);
        }

        private static bool IsSamePoint(float3 a, float3 b)
        {
            float3 delta = a - b;
            delta.y = 0f;
            return math.lengthsq(delta) <= 1e-4f;
        }

    }
}
