using MapNav.Data;
using Unity.Mathematics;

namespace MapNav.Core
{
    public static class NavQuery
    {
        public static bool TryClassify(in NavContext ctx, float3 worldPos, float tolerance, out NavSpaceRef space)
        {
            space = default;
            if (!ctx.IsValid) return false;
            ref NavBlob blob = ref ctx.Blob.Value;
            float2 local = NavMath.ToLocal2D(ctx.WorldToLocal, worldPos);

            if (blob.Grid.HasGrid != 0)
                return TryClassifyGrid(ref blob, local, tolerance, out space);

            return TryClassifyLinear(ref blob, local, tolerance, out space);
        }

        private static bool TryClassifyLinear(ref NavBlob blob, float2 local, float tolerance, out NavSpaceRef space)
        {
            space = default;

            // Transition first (transitions usually overlap regions slightly; transition wins)
            for (int i = 0; i < blob.Transitions.Length; i++)
            {
                ref NavTransition t = ref blob.Transitions[i];
                if (t.Enabled == 0) continue;
                if (!NavMath.BoundsContains(t.BoundsMin, t.BoundsMax, t.HasBounds, local, tolerance))
                    continue;
                if (NavMath.PolygonContains(ref blob.Points, t.PointStart, t.PointCount, local)
                    || NavMath.IsNearEdge(ref blob.Points, t.PointStart, t.PointCount, local, tolerance))
                {
                    space = NavSpaceRef.Transition(t.Id);
                    return true;
                }
            }

            for (int i = 0; i < blob.Regions.Length; i++)
            {
                ref NavRegion r = ref blob.Regions[i];
                if (!NavMath.BoundsContains(r.BoundsMin, r.BoundsMax, r.HasBounds, local, tolerance))
                    continue;
                if (NavMath.PolygonContains(ref blob.Points, r.PointStart, r.PointCount, local)
                    || NavMath.IsNearEdge(ref blob.Points, r.PointStart, r.PointCount, local, tolerance))
                {
                    if (IsInsideRegionObstacle(ref blob, r, local))
                        continue;
                    space = NavSpaceRef.Region(r.Id);
                    return true;
                }
            }

            return false;
        }

        private static bool TryClassifyGrid(ref NavBlob blob, float2 local, float tolerance, out NavSpaceRef space)
        {
            space = default;
            ref NavSpatialGrid grid = ref blob.Grid;
            int cx = (int)math.floor((local.x - grid.Origin.x) / grid.CellSize);
            int cz = (int)math.floor((local.y - grid.Origin.y) / grid.CellSize);
            int radius = tolerance > 0f ? math.max(1, (int)math.ceil(tolerance / grid.CellSize)) : 0;
            int x0 = math.max(0, cx - radius);
            int x1 = math.min(grid.CellsX - 1, cx + radius);
            int z0 = math.max(0, cz - radius);
            int z1 = math.min(grid.CellsZ - 1, cz + radius);
            if (x1 < x0 || z1 < z0) return false;

            // Pass 1: transitions take priority
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int2 range = grid.CellRanges[z * grid.CellsX + x];
                    for (int e = 0; e < range.y; e++)
                    {
                        NavGridEntry entry = grid.Entries[range.x + e];
                        if (entry.Kind != NavSpaceKind.Transition) continue;
                        if (!TryFindTransition(ref blob, entry.Id, out int idx)) continue;
                        ref NavTransition t = ref blob.Transitions[idx];
                        if (t.Enabled == 0) continue;
                        if (!NavMath.BoundsContains(t.BoundsMin, t.BoundsMax, t.HasBounds, local, tolerance)) continue;
                        if (NavMath.PolygonContains(ref blob.Points, t.PointStart, t.PointCount, local)
                            || NavMath.IsNearEdge(ref blob.Points, t.PointStart, t.PointCount, local, tolerance))
                        {
                            space = NavSpaceRef.Transition(t.Id);
                            return true;
                        }
                    }
                }
            }

            // Pass 2: regions
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int2 range = grid.CellRanges[z * grid.CellsX + x];
                    for (int e = 0; e < range.y; e++)
                    {
                        NavGridEntry entry = grid.Entries[range.x + e];
                        if (entry.Kind != NavSpaceKind.Region) continue;
                        if (!TryFindRegion(ref blob, entry.Id, out int idx)) continue;
                        ref NavRegion r = ref blob.Regions[idx];
                        if (!NavMath.BoundsContains(r.BoundsMin, r.BoundsMax, r.HasBounds, local, tolerance)) continue;
                        if (NavMath.PolygonContains(ref blob.Points, r.PointStart, r.PointCount, local)
                            || NavMath.IsNearEdge(ref blob.Points, r.PointStart, r.PointCount, local, tolerance))
                        {
                            if (IsInsideRegionObstacle(ref blob, r, local))
                                continue;
                            space = NavSpaceRef.Region(r.Id);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public static bool TryGetHeight(in NavContext ctx, float3 worldPos, float tolerance, out float worldHeight)
        {
            worldHeight = 0f;
            if (!ctx.IsValid) return false;

            if (!TryClassify(in ctx, worldPos, tolerance, out NavSpaceRef space))
                return false;

            ref NavBlob blob = ref ctx.Blob.Value;
            float2 local = NavMath.ToLocal2D(ctx.WorldToLocal, worldPos);

            float localHeight;
            if (space.Kind == NavSpaceKind.Region)
            {
                if (!TryFindRegion(ref blob, space.Id, out int idx)) return false;
                localHeight = blob.Regions[idx].Height;
            }
            else if (space.Kind == NavSpaceKind.Transition)
            {
                if (!TryFindTransition(ref blob, space.Id, out int idx)) return false;
                localHeight = ComputeTransitionHeight(ref blob, blob.Transitions[idx], local);
            }
            else
            {
                return false;
            }

            worldHeight = NavMath.WorldHeightFromLocal(ctx.LocalToWorld, localHeight);
            return true;
        }

        public static bool IsInsideObstacle(in NavContext ctx, float3 worldPos, float tolerance)
        {
            if (!ctx.IsValid) return false;
            ref NavBlob blob = ref ctx.Blob.Value;
            float2 local = NavMath.ToLocal2D(ctx.WorldToLocal, worldPos);
            for (int i = 0; i < blob.Obstacles.Length; i++)
            {
                ref NavObstacle obs = ref blob.Obstacles[i];
                if (!NavMath.BoundsContains(obs.BoundsMin, obs.BoundsMax, obs.HasBounds, local, tolerance))
                    continue;
                if (NavMath.PolygonContains(ref blob.Points, obs.PointStart, obs.PointCount, local))
                    return true;
            }
            return false;
        }

        public static bool TryProjectOutOfObstacle(in NavContext ctx, float3 worldPos, float radius, out float3 projected)
        {
            projected = worldPos;
            if (!ctx.IsValid) return false;
            ref NavBlob blob = ref ctx.Blob.Value;
            float2 local = NavMath.ToLocal2D(ctx.WorldToLocal, worldPos);

            float bestSqr = float.PositiveInfinity;
            float2 bestLocal = local;
            bool found = false;

            for (int i = 0; i < blob.Obstacles.Length; i++)
            {
                ref NavObstacle obs = ref blob.Obstacles[i];
                if (!NavMath.BoundsContains(obs.BoundsMin, obs.BoundsMax, obs.HasBounds, local, radius))
                    continue;
                if (!NavMath.PolygonContains(ref blob.Points, obs.PointStart, obs.PointCount, local))
                    continue;

                float2 closest = NavMath.ClosestPointOnPolygon(ref blob.Points, obs.PointStart, obs.PointCount, local, out float sqr);
                float2 toClosest = closest - local;
                float lenSq = math.lengthsq(toClosest);
                float2 outward = lenSq > NavMath.Epsilon ? toClosest / math.sqrt(lenSq) : new float2(1f, 0f);
                float push = math.max(radius + obs.CornerPadding, math.sqrt(lenSq) + NavMath.Epsilon);
                float2 candidate = closest + outward * push;
                float candidateSqr = math.lengthsq(candidate - local);
                if (candidateSqr < bestSqr)
                {
                    bestSqr = candidateSqr;
                    bestLocal = candidate;
                    found = true;
                }
            }

            if (!found) return false;

            // Lift to world; preserve original height
            float3 originalLocal3D = NavMath.ToLocal3D(ctx.WorldToLocal, worldPos);
            projected = math.transform(ctx.LocalToWorld, new float3(bestLocal.x, originalLocal3D.y, bestLocal.y));
            return true;
        }

        public static bool TryProjectToNearestSpace(in NavContext ctx, float3 worldPos, out float3 projected, out NavSpaceRef space)
        {
            projected = worldPos;
            space = default;
            if (!ctx.IsValid) return false;
            ref NavBlob blob = ref ctx.Blob.Value;
            float2 local = NavMath.ToLocal2D(ctx.WorldToLocal, worldPos);

            float bestSqr = float.PositiveInfinity;
            float2 bestLocal = local;
            float bestHeight = 0f;
            NavSpaceRef bestSpace = default;

            for (int i = 0; i < blob.Regions.Length; i++)
            {
                ref NavRegion r = ref blob.Regions[i];
                if (r.PointCount < 3) continue;
                float2 closest = NavMath.ClosestPointOnPolygon(ref blob.Points, r.PointStart, r.PointCount, local, out float sqr);
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestLocal = closest;
                    bestHeight = r.Height;
                    bestSpace = NavSpaceRef.Region(r.Id);
                }
            }

            for (int i = 0; i < blob.Transitions.Length; i++)
            {
                ref NavTransition t = ref blob.Transitions[i];
                if (t.Enabled == 0 || t.PointCount < 3) continue;
                float2 closest = NavMath.ClosestPointOnPolygon(ref blob.Points, t.PointStart, t.PointCount, local, out float sqr);
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestLocal = closest;
                    bestHeight = ComputeTransitionHeight(ref blob, t, closest);
                    bestSpace = NavSpaceRef.Transition(t.Id);
                }
            }

            if (!bestSpace.IsValid) return false;

            projected = NavMath.ToWorld(ctx.LocalToWorld, bestLocal, bestHeight);
            space = bestSpace;
            return true;
        }

        public static bool TryFindRegion(ref NavBlob blob, int id, out int index)
        {
            int min = 0;
            int max = blob.Regions.Length - 1;
            while (min <= max)
            {
                int mid = min + ((max - min) / 2);
                int candidate = blob.Regions[mid].Id;
                if (candidate == id) { index = mid; return true; }
                if (candidate < id) min = mid + 1;
                else max = mid - 1;
            }
            index = -1;
            return false;
        }

        public static bool TryFindTransition(ref NavBlob blob, int id, out int index)
        {
            int min = 0;
            int max = blob.Transitions.Length - 1;
            while (min <= max)
            {
                int mid = min + ((max - min) / 2);
                int candidate = blob.Transitions[mid].Id;
                if (candidate == id) { index = mid; return true; }
                if (candidate < id) min = mid + 1;
                else max = mid - 1;
            }
            index = -1;
            return false;
        }

        internal static bool IsInsideRegionObstacle(ref NavBlob blob, in NavRegion region, float2 localPoint)
        {
            for (int i = 0; i < region.ObstacleCount; i++)
            {
                ref NavObstacle obs = ref blob.Obstacles[region.ObstacleStart + i];
                if (!NavMath.BoundsContains(obs.BoundsMin, obs.BoundsMax, obs.HasBounds, localPoint, 0f))
                    continue;
                if (NavMath.PolygonContains(ref blob.Points, obs.PointStart, obs.PointCount, localPoint))
                    return true;
            }
            return false;
        }

        internal static float ComputeTransitionHeight(ref NavBlob blob, in NavTransition t, float2 localPoint)
        {
            // Edge / Door => midpoint
            // Stair / Ramp => project onto UpDirection
            int type = t.Type;
            if (type == 0 || type == 3) // Edge=0, Door=3
                return math.lerp(t.FromHeight, t.ToHeight, 0.5f);

            float2 dir = math.lengthsq(t.UpDirection) > NavMath.Epsilon
                ? math.normalize(t.UpDirection)
                : new float2(0f, 1f);

            // Project polygon points to find min/max along direction
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            for (int i = 0; i < t.PointCount; i++)
            {
                float v = math.dot(blob.Points[t.PointStart + i], dir);
                min = math.min(min, v);
                max = math.max(max, v);
            }

            float length = math.max(NavMath.Epsilon, max - min);
            float progress = math.saturate((math.dot(localPoint, dir) - min) / length);
            return math.lerp(t.FromHeight, t.ToHeight, progress);
        }
    }
}
