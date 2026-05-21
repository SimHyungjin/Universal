using MapNav.Data;
using System.Text;
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

            if (!TryResolvePoint(in ctx, startWorld, agentRadius, boundaryTolerance, out startWorld, out NavSpaceRef startSpace))
                return false;

            if (!TryResolvePoint(in ctx, endWorld, agentRadius, boundaryTolerance, out endWorld, out NavSpaceRef endSpace))
                return false;

            return TryBuildFromSpaces(in ctx, startSpace, startWorld, endSpace, endWorld, agentRadius, ref scratch, ref tmpNodes, ref tmpPortals, ref outWaypoints);
        }

        public static bool TryDiagnoseBuild(
            in NavContext ctx,
            float3 startWorld,
            float3 endWorld,
            float agentRadius,
            float boundaryTolerance,
            ref NavScratch scratch,
            ref NativeList<NavSpaceRef> tmpNodes,
            ref NativeList<NavPortal> tmpPortals,
            ref NativeList<float3> outWaypoints,
            StringBuilder log)
        {
            outWaypoints.Clear();
            if (!ctx.IsValid)
            {
                log.Append("ctx invalid");
                return false;
            }

            if (!TryResolvePoint(in ctx, startWorld, agentRadius, boundaryTolerance, out startWorld, out NavSpaceRef startSpace))
            {
                log.Append("start classify/project failed");
                return false;
            }

            if (!TryResolvePoint(in ctx, endWorld, agentRadius, boundaryTolerance, out endWorld, out NavSpaceRef endSpace))
            {
                log.Append("end classify/project failed");
                return false;
            }

            if (CanTravelDirect(in ctx, startWorld, endWorld, agentRadius))
            {
                AddIfSeparated(startWorld, ref outWaypoints);
                AddIfSeparated(endWorld, ref outWaypoints);
                log.Append("direct path ok");
                return true;
            }

            if (!NavGraph.TryFindPath(in ctx, startSpace, startWorld, endSpace, endWorld, agentRadius, ref scratch, ref tmpNodes, ref tmpPortals))
            {
                log.Append($"graph failed {FormatSpace(startSpace)} -> {FormatSpace(endSpace)}");
                return false;
            }

            return DiagnoseAssemblePath(in ctx, startWorld, ref tmpNodes, ref tmpPortals, endWorld, agentRadius, ref outWaypoints, log);
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

            if (!AssemblePath(in ctx, startWorld, ref tmpNodes, ref tmpPortals, endWorld, agentRadius, ref outWaypoints))
                return false;

            StringPull(in ctx, agentRadius, ref outWaypoints);
            return true;
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

        private static bool DiagnoseAssemblePath(
            in NavContext ctx,
            float3 startWorld,
            ref NativeList<NavSpaceRef> nodes,
            ref NativeList<NavPortal> portals,
            float3 endWorld,
            float agentRadius,
            ref NativeList<float3> outWaypoints,
            StringBuilder log)
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
                    && after.Kind == NavSpaceKind.Region)
                {
                    bool passed = TryAppendTransitionPass(in ctx, from, to, after, outWaypoints[outWaypoints.Length - 1], endWorld, agentRadius, ref outWaypoints);
                    if (passed)
                    {
                        i++;
                        continue;
                    }

                    log.Append($"transition pass failed i={i} {FormatSpace(from)} -> {FormatSpace(to)} -> {FormatSpace(after)} current={FormatPoint(outWaypoints[outWaypoints.Length - 1])}");
                    return false;
                }

                if (!AppendPortalCrossing(in ctx, from, to, portals[i], endWorld, agentRadius, ref outWaypoints))
                {
                    log.Append($"portal crossing failed i={i} {FormatSpace(from)} -> {FormatSpace(to)} current={FormatPoint(outWaypoints[outWaypoints.Length - 1])} portalA={FormatPoint(portals[i].A)} portalB={FormatPoint(portals[i].B)}");
                    return false;
                }
            }

            NavSpaceRef last = nodes.Length > 0 ? nodes[nodes.Length - 1] : default;
            float3 current = outWaypoints[outWaypoints.Length - 1];
            if (CanTravelDirect(in ctx, current, endWorld, agentRadius))
            {
                AddIfSeparated(endWorld, ref outWaypoints);
                log.Append("assembled ok via final direct");
                return true;
            }

            if (!AppendSegment(in ctx, last, current, endWorld, agentRadius, ref outWaypoints))
            {
                log.Append($"final segment failed {FormatSpace(last)} current={FormatPoint(current)} end={FormatPoint(endWorld)}");
                return false;
            }

            log.Append("assembled ok");
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

            for (int enterIndex = 0; enterIndex < 3; enterIndex++)
            {
                float2 enterLocal = GetPortalCandidate(enterEdge, enterIndex);
                float3 enterTransitionWorld = NavMath.ToWorld(ctx.LocalToWorld, enterLocal, enterEdge.PortalHeight);
                if (!TryGetRegionAccessPoint(in ctx, fromRegion.Id, enterTransitionWorld, out float3 enterRegionWorld))
                    continue;

                float enterRegionCost = MeasureRegionSegment(in ctx, fromRegion.Id, current, enterRegionWorld, agentRadius);
                if (float.IsPositiveInfinity(enterRegionCost))
                    continue;

                for (int exitIndex = 0; exitIndex < 3; exitIndex++)
                {
                    float2 exitLocal = GetPortalCandidate(exitEdge, exitIndex);
                    float3 exitTransitionWorld = NavMath.ToWorld(ctx.LocalToWorld, exitLocal, exitEdge.PortalHeight);
                    if (!TryGetRegionAccessPoint(in ctx, toRegion.Id, exitTransitionWorld, out float3 exitRegionWorld))
                        continue;

                    if (float.IsPositiveInfinity(MeasureRegionSegment(in ctx, toRegion.Id, exitRegionWorld, exitRegionWorld, agentRadius)))
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

        private static float2 GetPortalCandidate(NavEdge edge, int index)
        {
            return index == 0
                ? edge.PortalLocalA
                : index == 1
                    ? edge.PortalLocalB
                    : (edge.PortalLocalA + edge.PortalLocalB) * 0.5f;
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
                return AppendSameLayerRegionCrossing(in ctx, fromSpace, portal, agentRadius, ref outWaypoints);

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

        private static bool AppendSameLayerRegionCrossing(
            in NavContext ctx,
            NavSpaceRef fromSpace,
            NavPortal portal,
            float agentRadius,
            ref NativeList<float3> outWaypoints)
        {
            if (fromSpace.Kind != NavSpaceKind.Region)
                return false;

            float3 current = outWaypoints[outWaypoints.Length - 1];
            float3 crossing = (portal.A + portal.B) * 0.5f;
            return AppendSegment(in ctx, fromSpace, current, crossing, agentRadius, ref outWaypoints);
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

                AppendInRegionCorners(ref corners, ref outWaypoints);
                corners.Dispose();
            }

            AddIfSeparated(to, ref outWaypoints);
            return true;
        }

        // The visibility-graph corners from NavInRegion already form the shortest taut path
        // through the region's obstacles (visibility-graph A* is optimal), so they need no
        // further smoothing. Cross-segment straightening is handled once at the end of the
        // build by StringPull.
        private static void AppendInRegionCorners(
            ref NativeList<float3> corners,
            ref NativeList<float3> outWaypoints)
        {
            for (int i = 0; i < corners.Length; i++)
                AddIfSeparated(corners[i], ref outWaypoints);
        }

        // Greedy line-of-sight string pull over the assembled path: drop any waypoint whose
        // surrounding waypoints can travel directly to each other. CanTravelDirect is
        // obstacle- and boundary-aware and refuses to shortcut across a transition or a
        // layer change, so required transition waypoints survive automatically. Any kept
        // consecutive pair is an original (already-valid) segment.
        private static void StringPull(
            in NavContext ctx,
            float agentRadius,
            ref NativeList<float3> waypoints)
        {
            if (waypoints.Length <= 2)
                return;

            NativeList<float3> pulled = new NativeList<float3>(waypoints.Length, Allocator.Temp);
            pulled.Add(waypoints[0]);

            int anchor = 0;
            for (int i = 1; i < waypoints.Length - 1; i++)
            {
                if (CanTravelDirect(in ctx, waypoints[anchor], waypoints[i + 1], agentRadius))
                    continue;

                pulled.Add(waypoints[i]);
                anchor = i;
            }

            pulled.Add(waypoints[waypoints.Length - 1]);

            if (pulled.Length < waypoints.Length)
            {
                waypoints.Clear();
                for (int i = 0; i < pulled.Length; i++)
                    waypoints.Add(pulled[i]);
            }

            pulled.Dispose();
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

        // Resolves a raw world point to a navigable position: classifies it (or projects to
        // the nearest nav space if off-mesh), then pushes it clear of any obstacle it sits
        // inside or within agent-radius clearance of. Without the obstacle step a start/end
        // point inside an obstacle makes every in-region segment fail and the build aborts.
        private static bool TryResolvePoint(
            in NavContext ctx,
            float3 world,
            float agentRadius,
            float boundaryTolerance,
            out float3 resolved,
            out NavSpaceRef space)
        {
            if (NavQuery.TryClassify(in ctx, world, boundaryTolerance, out space))
            {
                resolved = ProjectIntoSpace(in ctx, space, world);
                ApplyObstacleClearance(in ctx, agentRadius, boundaryTolerance, ref resolved, ref space);
                return true;
            }

            // Off-mesh — may be sitting inside an obstacle; push out before falling back to
            // the (possibly distant) nearest-space projection.
            if (agentRadius > 0f
                && NavQuery.TryProjectOutOfObstacle(in ctx, world, agentRadius, out float3 cleared)
                && NavQuery.TryClassify(in ctx, cleared, boundaryTolerance, out space))
            {
                resolved = ProjectIntoSpace(in ctx, space, cleared);
                return true;
            }

            if (NavQuery.TryProjectToNearestSpace(in ctx, world, out float3 projected, out space))
            {
                resolved = projected;
                ApplyObstacleClearance(in ctx, agentRadius, boundaryTolerance, ref resolved, ref space);
                return true;
            }

            resolved = world;
            space = default;
            return false;
        }

        private static void ApplyObstacleClearance(
            in NavContext ctx,
            float agentRadius,
            float boundaryTolerance,
            ref float3 position,
            ref NavSpaceRef space)
        {
            if (agentRadius <= 0f)
                return;

            if (NavQuery.TryProjectOutOfObstacle(in ctx, position, agentRadius, out float3 cleared)
                && NavQuery.TryClassify(in ctx, cleared, boundaryTolerance, out NavSpaceRef clearedSpace))
            {
                position = ProjectIntoSpace(in ctx, clearedSpace, cleared);
                space = clearedSpace;
            }
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
            float fromHeight = blob.Regions[fromRegionIndex].Height;
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
                    || math.abs(blob.Regions[sampledRegionIndex].Height - fromHeight) > DirectTravelTolerance)
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

            return math.abs(blob.Regions[aIndex].Height - blob.Regions[bIndex].Height) <= DirectTravelTolerance;
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

        private static string FormatSpace(NavSpaceRef space)
        {
            return $"{space.Kind}:{space.Id}";
        }

        private static string FormatPoint(float3 point)
        {
            return $"({point.x:F1},{point.z:F1})";
        }

    }
}
