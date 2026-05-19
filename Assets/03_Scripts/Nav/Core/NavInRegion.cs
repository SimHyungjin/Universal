using MapNav.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace MapNav.Core
{
    public static class NavInRegion
    {
        // Append intermediate corner waypoints (world-space) needed to route from aWorld to bWorld
        // around obstacles inside `regionId`. Start (aWorld) and goal (bWorld) are NOT appended;
        // only the in-between corners.
        // Returns false if no path exists (e.g. obstacle fully blocks); in that case outCorners
        // is left unchanged.
        public static bool TryAppendCornerWaypoints(
            in NavContext ctx,
            int regionId,
            float3 aWorld,
            float3 bWorld,
            float agentRadius,
            ref NativeList<float3> outCorners)
        {
            ref NavBlob blob = ref ctx.Blob.Value;
            if (!NavQuery.TryFindRegion(ref blob, regionId, out int regionIdx))
                return false;

            ref NavRegion region = ref blob.Regions[regionIdx];

            float2 a = NavMath.ToLocal2D(ctx.WorldToLocal, aWorld);
            float2 b = NavMath.ToLocal2D(ctx.WorldToLocal, bWorld);

            return TryAppendCornerWaypointsLocal(in ctx, in region, a, b, agentRadius, ref outCorners);
        }

        public static bool TryAppendCornerWaypointsLocal(
            in NavContext ctx,
            in NavRegion region,
            float2 a,
            float2 b,
            float agentRadius,
            ref NativeList<float3> outCorners)
        {
            ref NavBlob blob = ref ctx.Blob.Value;

            if (!SegmentBlockedByNavigation(ref blob, in region, a, b, agentRadius))
                return true;

            // Build visibility nodes: 0 = start, 1 = goal, obstacle corners, region reflex corners.
            NativeList<float2> nodes = new NativeList<float2>(math.max(16, region.PointCount + region.ObstacleCount * 4 + 2), Allocator.Temp);
            nodes.Add(a);
            nodes.Add(b);

            for (int oi = 0; oi < region.ObstacleCount; oi++)
            {
                NavObstacle obs = blob.Obstacles[region.ObstacleStart + oi];
                for (int pi = 0; pi < obs.PointCount; pi++)
                {
                    float2 corner = ComputeObstacleCornerWaypoint(ref blob, obs, pi, agentRadius);
                    AddObstacleCornerNodeIfSeparated(ref blob, in region, corner, ref nodes);
                }
            }

            AddRegionReflexCornerNodes(ref blob, in region, agentRadius, ref nodes);

            int n = nodes.Length;
            NativeArray<float> gScore = new NativeArray<float>(n, Allocator.Temp);
            NativeArray<int> cameFrom = new NativeArray<int>(n, Allocator.Temp);
            NativeArray<bool> closed = new NativeArray<bool>(n, Allocator.Temp);
            for (int i = 0; i < n; i++)
            {
                gScore[i] = float.PositiveInfinity;
                cameFrom[i] = -1;
            }
            gScore[0] = 0f;

            // Simple A* with linear scan (n typically small)
            float2 goal = nodes[1];
            int safety = 0;
            int maxIterations = n * n + 4;

            while (safety++ < maxIterations)
            {
                int current = -1;
                float bestF = float.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    if (closed[i] || float.IsPositiveInfinity(gScore[i])) continue;
                    float f = gScore[i] + math.distance(nodes[i], goal);
                    if (f < bestF) { bestF = f; current = i; }
                }
                if (current == -1) break;
                if (current == 1) break;
                closed[current] = true;

                for (int next = 0; next < n; next++)
                {
                    if (next == current || closed[next]) continue;
                    if (SegmentBlockedByNavigation(ref blob, in region, nodes[current], nodes[next], agentRadius))
                        continue;
                    float tentativeG = gScore[current] + math.distance(nodes[current], nodes[next]);
                    if (tentativeG < gScore[next])
                    {
                        gScore[next] = tentativeG;
                        cameFrom[next] = current;
                    }
                }
            }

            bool reachable = !float.IsPositiveInfinity(gScore[1]);
            if (reachable)
            {
                NativeList<int> reverseIndices = new NativeList<int>(8, Allocator.Temp);
                int cur = cameFrom[1];
                int walk = 0;
                while (cur > 0 && walk++ < n)
                {
                    reverseIndices.Add(cur);
                    cur = cameFrom[cur];
                }
                for (int i = reverseIndices.Length - 1; i >= 0; i--)
                {
                    float2 corner = nodes[reverseIndices[i]];
                    float3 world = NavMath.ToWorld(ctx.LocalToWorld, corner, region.Height);
                    outCorners.Add(world);
                }
                reverseIndices.Dispose();
            }

            nodes.Dispose();
            gScore.Dispose();
            cameFrom.Dispose();
            closed.Dispose();
            return reachable;
        }

        // Adds visibility nodes for reflex (concave) vertices of the region polygon.
        // Non-convex regions (L/U/ㄷ shaped) need these so two interior points whose
        // direct segment exits the polygon can route around the concave corner.
        // Convex vertices are skipped — paths never bend around them from inside.
        private static void AddRegionReflexCornerNodes(
            ref NavBlob blob,
            in NavRegion region,
            float agentRadius,
            ref NativeList<float2> nodes)
        {
            int count = region.PointCount;
            int start = region.PointStart;
            if (count < 4) return; // triangle has no reflex vertex

            float offset = math.max(0f, agentRadius) + 1e-3f;

            for (int i = 0; i < count; i++)
            {
                int prev = (i - 1 + count) % count;
                int next = (i + 1) % count;
                float2 vertex = blob.Points[start + i];
                float2 e1 = vertex - blob.Points[start + prev];
                float2 e2 = blob.Points[start + next] - vertex;
                float2 n1 = math.normalizesafe(new float2(-e1.y, e1.x));
                float2 n2 = math.normalizesafe(new float2(-e2.y, e2.x));
                float2 bisector = n1 + n2;
                float lenSq = math.lengthsq(bisector);
                if (lenSq < NavMath.Epsilon) continue;

                // Reflex test: the bisector points into the polygon interior.
                float invLen = math.rsqrt(lenSq);
                float2 inwardProbe = vertex - bisector * (invLen * 1e-3f);
                if (!NavMath.PolygonContains(ref blob.Points, start, count, inwardProbe))
                    continue; // bisector goes outside polygon -> vertex is convex

                // Inward offset; clamp for very sharp reflex angles
                float scale = math.min(2f * offset / lenSq, 8f * offset);
                float2 corner = vertex - bisector * scale;

                if (!NavMath.PolygonContains(ref blob.Points, start, count, corner))
                {
                    corner = NavMath.ClosestPointOnPolygon(ref blob.Points, start, count, corner, out _);
                    if (!IsInsideRegionOrBoundary(ref blob, in region, corner)) continue;
                }

                if (IsInsideAnyObstacle(ref blob, in region, corner)) continue;

                bool duplicate = false;
                for (int n = 0; n < nodes.Length; n++)
                {
                    if (math.lengthsq(nodes[n] - corner) <= 1e-6f) { duplicate = true; break; }
                }
                if (!duplicate) nodes.Add(corner);
            }
        }

        private static bool IsInsideAnyObstacle(ref NavBlob blob, in NavRegion region, float2 point)
        {
            for (int oi = 0; oi < region.ObstacleCount; oi++)
            {
                NavObstacle obs = blob.Obstacles[region.ObstacleStart + oi];
                if (NavMath.PolygonContains(ref blob.Points, obs.PointStart, obs.PointCount, point))
                    return true;
            }
            return false;
        }

        private static void AddObstacleCornerNodeIfSeparated(
            ref NavBlob blob,
            in NavRegion region,
            float2 point,
            ref NativeList<float2> nodes)
        {
            // If the obstacle corner's outward offset spilled past the region polygon
            // (obstacle is close to the region edge), clamp it back onto the polygon so
            // the visibility graph still has a node that can route around the obstacle.
            // Without this, regions with obstacles near their boundary become unreachable.
            if (!IsInsideRegionOrBoundary(ref blob, in region, point))
            {
                float2 clamped = NavMath.ClosestPointOnPolygon(
                    ref blob.Points, region.PointStart, region.PointCount, point, out _);
                if (!IsInsideRegionOrBoundary(ref blob, in region, clamped))
                    return;
                point = clamped;
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                if (math.lengthsq(nodes[i] - point) <= 1e-6f)
                    return;
            }

            nodes.Add(point);
        }

        public static bool TryMeasurePathLocal(
            in NavContext ctx,
            int regionId,
            float2 a,
            float2 b,
            float agentRadius,
            out float cost)
        {
            cost = 0f;

            ref NavBlob blob = ref ctx.Blob.Value;
            if (!NavQuery.TryFindRegion(ref blob, regionId, out int regionIdx))
                return false;

            ref NavRegion region = ref blob.Regions[regionIdx];
            if (!SegmentBlockedByNavigation(ref blob, in region, a, b, agentRadius))
            {
                cost = math.distance(a, b);
                return true;
            }

            NativeList<float3> corners = new NativeList<float3>(8, Allocator.Temp);
            bool reachable = TryAppendCornerWaypointsLocal(in ctx, in region, a, b, agentRadius, ref corners);
            if (reachable)
            {
                float2 previous = a;
                for (int i = 0; i < corners.Length; i++)
                {
                    float2 corner = NavMath.ToLocal2D(ctx.WorldToLocal, corners[i]);
                    cost += math.distance(previous, corner);
                    previous = corner;
                }
                cost += math.distance(previous, b);
            }

            corners.Dispose();
            return reachable;
        }

        public static bool CanTravelDirectLocal(
            in NavContext ctx,
            int regionId,
            float2 a,
            float2 b,
            float agentRadius)
        {
            ref NavBlob blob = ref ctx.Blob.Value;
            if (!NavQuery.TryFindRegion(ref blob, regionId, out int regionIdx))
                return false;

            ref NavRegion region = ref blob.Regions[regionIdx];
            return !SegmentBlockedByNavigation(ref blob, in region, a, b, agentRadius);
        }

        public static bool SegmentCrossesObstacleLocal(
            in NavContext ctx,
            int regionId,
            float2 a,
            float2 b)
        {
            ref NavBlob blob = ref ctx.Blob.Value;
            if (!NavQuery.TryFindRegion(ref blob, regionId, out int regionIdx))
                return false;

            ref NavRegion region = ref blob.Regions[regionIdx];
            return SegmentBlockedByObstacles(ref blob, in region, a, b, 0f, true);
        }

        private static bool SegmentBlockedByNavigation(ref NavBlob blob, in NavRegion region, float2 a, float2 b, float agentRadius)
        {
            if (!IsInsideRegionOrBoundary(ref blob, in region, a))
                return true;

            if (!IsInsideRegionOrBoundary(ref blob, in region, b))
                return true;

            const int Samples = 7;
            for (int i = 1; i < Samples; i++)
            {
                float t = i / (float)Samples;
                float2 p = math.lerp(a, b, t);
                if (!IsInsideRegionOrBoundary(ref blob, in region, p))
                    return true;
            }

            if (SegmentCrossesRegionBoundary(ref blob, in region, a, b))
                return true;

            return SegmentBlockedByObstacles(ref blob, in region, a, b, agentRadius, false);
        }

        private static bool SegmentBlockedByObstacles(ref NavBlob blob, in NavRegion region, float2 a, float2 b, float agentRadius, bool blockTouching)
        {
            for (int oi = 0; oi < region.ObstacleCount; oi++)
            {
                NavObstacle obs = blob.Obstacles[region.ObstacleStart + oi];
                float clearance = math.max(0f, agentRadius) + obs.CornerPadding;
                if (SegmentBlockedByPolygon(ref blob, obs.PointStart, obs.PointCount, a, b, clearance, blockTouching))
                    return true;
            }
            return false;
        }

        private static bool IsInsideRegionOrBoundary(ref NavBlob blob, in NavRegion region, float2 point)
        {
            return NavMath.PolygonContains(ref blob.Points, region.PointStart, region.PointCount, point)
                || NavMath.IsNearEdge(ref blob.Points, region.PointStart, region.PointCount, point, 1e-3f);
        }

        private static bool SegmentCrossesRegionBoundary(ref NavBlob blob, in NavRegion region, float2 a, float2 b)
        {
            int j = region.PointCount - 1;
            for (int i = 0; i < region.PointCount; j = i++)
            {
                float2 p1 = blob.Points[region.PointStart + j];
                float2 p2 = blob.Points[region.PointStart + i];
                if (SegmentsIntersectStrict(a, b, p1, p2))
                    return true;
            }

            return false;
        }

        private static bool SegmentBlockedByPolygon(ref NavBlob blob, int pointStart, int pointCount, float2 a, float2 b, float clearance, bool blockTouching)
        {
            if (NavMath.PolygonContains(ref blob.Points, pointStart, pointCount, a)) return true;
            if (NavMath.PolygonContains(ref blob.Points, pointStart, pointCount, b)) return true;
            if (EndpointMovesIntoPolygon(ref blob, pointStart, pointCount, a, b)) return true;
            if (EndpointMovesIntoPolygon(ref blob, pointStart, pointCount, b, a)) return true;

            float clearanceSq = clearance * clearance;
            int j = pointCount - 1;
            for (int i = 0; i < pointCount; j = i++)
            {
                float2 p1 = blob.Points[pointStart + j];
                float2 p2 = blob.Points[pointStart + i];
                if (blockTouching)
                {
                    if (SegmentsIntersectInclusive(a, b, p1, p2)) return true;
                }
                else if (SegmentsIntersectStrict(a, b, p1, p2))
                {
                    return true;
                }

                if (clearance > 0f && SegmentDistanceSq(a, b, p1, p2) < clearanceSq)
                    return true;
            }
            return false;
        }

        private static bool EndpointMovesIntoPolygon(
            ref NavBlob blob,
            int pointStart,
            int pointCount,
            float2 endpoint,
            float2 other)
        {
            if (!TryFindPolygonVertex(ref blob, pointStart, pointCount, endpoint, out int vertexIndex))
                return false;

            float2 delta = other - endpoint;
            float lenSq = math.lengthsq(delta);
            if (lenSq <= NavMath.Epsilon)
                return false;

            if (SegmentLeavesVertexAlongAdjacentEdge(ref blob, pointStart, pointCount, vertexIndex, delta))
                return false;

            float2 probe = endpoint + delta * (1e-3f / math.sqrt(lenSq));
            return NavMath.PolygonContains(ref blob.Points, pointStart, pointCount, probe);
        }

        private static bool TryFindPolygonVertex(ref NavBlob blob, int pointStart, int pointCount, float2 point, out int vertexIndex)
        {
            vertexIndex = -1;
            for (int i = 0; i < pointCount; i++)
            {
                if (math.lengthsq(blob.Points[pointStart + i] - point) <= 1e-6f)
                {
                    vertexIndex = i;
                    return true;
                }
            }

            return false;
        }

        private static bool SegmentLeavesVertexAlongAdjacentEdge(
            ref NavBlob blob,
            int pointStart,
            int pointCount,
            int vertexIndex,
            float2 delta)
        {
            int prevIndex = (vertexIndex - 1 + pointCount) % pointCount;
            int nextIndex = (vertexIndex + 1) % pointCount;
            float2 vertex = blob.Points[pointStart + vertexIndex];
            return SameDirection(delta, blob.Points[pointStart + prevIndex] - vertex)
                || SameDirection(delta, blob.Points[pointStart + nextIndex] - vertex);
        }

        private static bool SameDirection(float2 a, float2 b)
        {
            float lenA = math.lengthsq(a);
            float lenB = math.lengthsq(b);
            if (lenA <= NavMath.Epsilon || lenB <= NavMath.Epsilon)
                return false;

            float cross = math.abs(NavMath.Cross(a, b));
            if (cross > 1e-4f * math.sqrt(lenA * lenB))
                return false;

            return math.dot(a, b) > 0f;
        }

        private static float SegmentDistanceSq(float2 a0, float2 a1, float2 b0, float2 b1)
        {
            if (SegmentsIntersectStrict(a0, a1, b0, b1))
                return 0f;

            float d0 = NavMath.DistanceToSegmentSq(a0, b0, b1);
            float d1 = NavMath.DistanceToSegmentSq(a1, b0, b1);
            float d2 = NavMath.DistanceToSegmentSq(b0, a0, a1);
            float d3 = NavMath.DistanceToSegmentSq(b1, a0, a1);
            return math.min(math.min(d0, d1), math.min(d2, d3));
        }

        private static bool SegmentsIntersectStrict(float2 p1, float2 p2, float2 q1, float2 q2)
        {
            float2 r = p2 - p1;
            float2 s = q2 - q1;
            float rxs = NavMath.Cross(r, s);
            float2 qp = q1 - p1;
            float qpxr = NavMath.Cross(qp, r);

            if (math.abs(rxs) < NavMath.Epsilon) return false;

            float t = NavMath.Cross(qp, s) / rxs;
            float u = qpxr / rxs;

            const float Slack = 1e-4f;
            return t > Slack && t < 1f - Slack && u > Slack && u < 1f - Slack;
        }

        private static bool SegmentsIntersectInclusive(float2 p1, float2 p2, float2 q1, float2 q2)
        {
            const float Epsilon = 1e-5f;
            float2 r = p2 - p1;
            float2 s = q2 - q1;
            float rxs = NavMath.Cross(r, s);
            float2 qp = q1 - p1;

            if (math.abs(rxs) < Epsilon)
            {
                if (math.abs(NavMath.Cross(qp, r)) > Epsilon)
                    return false;

                float rr = math.lengthsq(r);
                if (rr < Epsilon)
                    return math.lengthsq(q1 - p1) < Epsilon || math.lengthsq(q2 - p1) < Epsilon;

                float t0 = math.dot(q1 - p1, r) / rr;
                float t1 = math.dot(q2 - p1, r) / rr;
                if (t0 > t1) (t0, t1) = (t1, t0);
                return t0 <= 1f + Epsilon && t1 >= -Epsilon;
            }

            float t = NavMath.Cross(qp, s) / rxs;
            float u = NavMath.Cross(qp, r) / rxs;
            return t >= -Epsilon && t <= 1f + Epsilon && u >= -Epsilon && u <= 1f + Epsilon;
        }

        private static float2 ComputeObstacleCornerWaypoint(ref NavBlob blob, in NavObstacle obs, int cornerIdx, float radius)
        {
            int count = obs.PointCount;
            float2 corner = blob.Points[obs.PointStart + cornerIdx];
            if (count < 3) return corner;

            float offset = math.max(0f, radius) + math.max(0f, obs.CornerPadding);
            if (offset <= NavMath.Epsilon) return corner;
            offset += 1e-3f;

            int prev = (cornerIdx - 1 + count) % count;
            int next = (cornerIdx + 1) % count;
            float2 a = blob.Points[obs.PointStart + prev];
            float2 b = blob.Points[obs.PointStart + next];

            float2 e1 = corner - a;
            float2 e2 = b - corner;
            float2 n1 = math.normalizesafe(new float2(-e1.y, e1.x));
            float2 n2 = math.normalizesafe(new float2(-e2.y, e2.x));
            // outward = n1 + n2 has length 2*sin(θ/2) where θ is the interior angle.
            // To ensure perpendicular distance from each adjacent edge equals `offset`,
            // displace by outward * offset / sin²(θ/2) = outward * 2*offset / |outward|².
            float2 outward = n1 + n2;
            float lenSq = math.lengthsq(outward);
            if (lenSq < NavMath.Epsilon)
            {
                // 180° corner — fall back to single edge normal.
                outward = n1;
                lenSq = math.lengthsq(outward);
                if (lenSq < NavMath.Epsilon) return corner;
            }

            // Probe to determine which side is outside the polygon.
            float invLen = math.rsqrt(lenSq);
            float2 probe = corner + outward * (invLen * 1e-3f);
            if (NavMath.PolygonContains(ref blob.Points, obs.PointStart, obs.PointCount, probe))
                outward = -outward;

            // Acute corners can blow up here; clamp displacement to a sane multiple of `offset`.
            float scale = math.min(2f * offset / lenSq, 8f * offset);
            return corner + outward * scale;
        }
    }
}
