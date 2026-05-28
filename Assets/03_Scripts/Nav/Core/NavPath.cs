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

            NativeList<float3> funnelWaypoints = new NativeList<float3>(16, Allocator.Temp);
            NavFunnel.Smooth(startWorld, ref tmpPortals, endWorld, agentRadius, ref funnelWaypoints);
            bool ok = RefineWithInRegionCorners(in ctx, agentRadius, ref funnelWaypoints, ref outWaypoints);
            funnelWaypoints.Dispose();
            if (!ok)
            {
                log.Append("refine failed");
                return false;
            }

            StringPull(in ctx, agentRadius, ref outWaypoints);
            log.Append($"funnel ok ({outWaypoints.Length} waypoints)");
            return true;
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

            // NavFunnel: portal segment 양 끝(NavPortal.A/B)을 funnel로 풀어 꺾이는 점만 박는다.
            // portal endpoint를 강제 waypoint로 두지 않으므로 polygon vertex 경유 꺾임이 사라짐.
            NativeList<float3> funnelWaypoints = new NativeList<float3>(16, Allocator.Temp);
            NavFunnel.Smooth(startWorld, ref tmpPortals, endWorld, agentRadius, ref funnelWaypoints);

            bool ok = RefineWithInRegionCorners(in ctx, agentRadius, ref funnelWaypoints, ref outWaypoints);
            funnelWaypoints.Dispose();
            if (!ok) return false;

            StringPull(in ctx, agentRadius, ref outWaypoints);
            return true;
        }

        // funnel은 portal segment 사이 직선 단순화만 함 — region 안 obstacle은 모른다.
        // 각 segment에 대해 region 측 부분(들)을 NavInRegion 가시그래프로 풀어 corner 보강.
        // segment가 두 region을 가로지르면 양쪽 모두 처리 — segment를 polygon 경계로 clamp해서
        // 각 region 내부의 a→exit / enter→b 부분만 가시그래프에 넘긴다.
        private static bool RefineWithInRegionCorners(
            in NavContext ctx,
            float agentRadius,
            ref NativeList<float3> funnelWaypoints,
            ref NativeList<float3> outWaypoints)
        {
            outWaypoints.Clear();
            if (funnelWaypoints.Length == 0) return false;

            AddIfSeparated(funnelWaypoints[0], ref outWaypoints);

            NativeList<float3> corners = new NativeList<float3>(8, Allocator.Temp);
            for (int i = 0; i < funnelWaypoints.Length - 1; i++)
            {
                float3 from = funnelWaypoints[i];
                float3 to = funnelWaypoints[i + 1];

                NavQuery.TryClassify(in ctx, from, DirectTravelTolerance, out NavSpaceRef fromSpace);
                NavQuery.TryClassify(in ctx, to, DirectTravelTolerance, out NavSpaceRef toSpace);

                bool sameRegion = fromSpace.Kind == NavSpaceKind.Region
                                  && toSpace.Kind == NavSpaceKind.Region
                                  && fromSpace == toSpace;

                if (fromSpace.Kind == NavSpaceKind.Region)
                {
                    float3 toForFromRegion = sameRegion ? to : ClampToRegion(in ctx, fromSpace.Id, to);
                    AppendInRegionCorners(in ctx, fromSpace.Id, from, toForFromRegion, agentRadius, ref corners, ref outWaypoints);
                }

                if (!sameRegion && toSpace.Kind == NavSpaceKind.Region)
                {
                    float3 fromForToRegion = ClampToRegion(in ctx, toSpace.Id, from);
                    AppendInRegionCorners(in ctx, toSpace.Id, fromForToRegion, to, agentRadius, ref corners, ref outWaypoints);
                }

                AddIfSeparated(to, ref outWaypoints);
            }
            corners.Dispose();

            return true;
        }

        private static void AppendInRegionCorners(
            in NavContext ctx,
            int regionId,
            float3 a,
            float3 b,
            float agentRadius,
            ref NativeList<float3> cornersScratch,
            ref NativeList<float3> outWaypoints)
        {
            cornersScratch.Clear();
            if (!NavInRegion.TryAppendCornerWaypoints(in ctx, regionId, a, b, agentRadius, ref cornersScratch))
                return;

            for (int i = 0; i < cornersScratch.Length; i++)
                AddIfSeparated(cornersScratch[i], ref outWaypoints);
        }

        // 점을 region polygon 안으로 clamp — 안에 있으면 그대로, 밖이면 closest edge point로.
        // segment가 region 경계를 넘는 지점을 정확히 알 수 없을 때 in-region 가시그래프 호출용 fallback.
        private static float3 ClampToRegion(in NavContext ctx, int regionId, float3 world)
        {
            ref NavBlob blob = ref ctx.Blob.Value;
            if (!NavQuery.TryFindRegion(ref blob, regionId, out int regionIndex))
                return world;

            ref NavRegion region = ref blob.Regions[regionIndex];
            float2 local = NavMath.ToLocal2D(ctx.WorldToLocal, world);
            if (NavMath.PolygonContains(ref blob.Points, region.PointStart, region.PointCount, local))
                return world;

            float2 clamped = NavMath.ClosestPointOnPolygon(ref blob.Points, region.PointStart, region.PointCount, local, out _);
            return NavMath.ToWorld(ctx.LocalToWorld, clamped, region.Height);
        }

        // Greedy line-of-sight string pull: drop any waypoint whose surrounding waypoints can
        // travel directly to each other. funnel + in-region refine 후의 추가 정리용.
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

            if (fromSpace.Kind == NavSpaceKind.Region && toSpace.Kind == NavSpaceKind.Region)
                return CanTravelDirectOnSameLayer(in ctx, from, to, fromSpace, toSpace);

            return CanTravelDirectAcrossPortal(in ctx, from, to, fromSpace, toSpace);
        }

        // transition↔region 직선 LOS. region 측 obstacle 통과만 차단하고, 모든 샘플이 from/to
        // 둘 중 하나로 분류돼야 — 제3의 영역으로 새는 직선은 거부.
        private static bool CanTravelDirectAcrossPortal(
            in NavContext ctx,
            float3 from,
            float3 to,
            NavSpaceRef fromSpace,
            NavSpaceRef toSpace)
        {
            bool oneRegion = (fromSpace.Kind == NavSpaceKind.Region) ^ (toSpace.Kind == NavSpaceKind.Region);
            bool oneTransition = (fromSpace.Kind == NavSpaceKind.Transition) ^ (toSpace.Kind == NavSpaceKind.Transition);
            if (!oneRegion || !oneTransition)
                return false;

            NavSpaceRef regionSpace = fromSpace.Kind == NavSpaceKind.Region ? fromSpace : toSpace;

            float2 fromLocal = NavMath.ToLocal2D(ctx.WorldToLocal, from);
            float2 toLocal = NavMath.ToLocal2D(ctx.WorldToLocal, to);

            if (NavInRegion.SegmentCrossesObstacleLocal(in ctx, regionSpace.Id, fromLocal, toLocal))
                return false;

            const int Samples = 16;
            for (int i = 1; i < Samples; i++)
            {
                float t = i / (float)Samples;
                float3 p = math.lerp(from, to, t);
                if (!NavQuery.TryClassify(in ctx, p, DirectTravelTolerance, out NavSpaceRef sampled))
                    return false;
                if (sampled != fromSpace && sampled != toSpace)
                    return false;
            }

            return true;
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
    }
}
