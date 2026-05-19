using System.Collections.Generic;
using UnityEngine;

public static class MapNavSampleUtility
{
    // True if `local` lies inside `region`'s polygon, outside every obstacle, and far
    // enough from every region/obstacle edge to clear `agentRadius` (plus per-obstacle
    // CornerPadding). Use before handing a point to NavPath.TryBuild — points that pass
    // here are reachable from neighboring nav-mesh space without immediate clearance fail.
    public static bool HasClearance(MapNavRegion region, Vector2 local, float agentRadius)
    {
        if (region == null || region.Points == null || region.Points.Count < 3)
            return false;

        if (!region.Contains(local))
            return false;

        if (IsInsideOrTooCloseToObstacle(region, local, agentRadius))
            return false;

        if (agentRadius > 0f && IsTooCloseToRegionEdge(region.Points, local, agentRadius))
            return false;

        return true;
    }

    public static bool TrySampleClearPoint(MapNavRegion region, float agentRadius, int maxAttempts, out Vector2 local)
    {
        local = default;
        if (region == null || !region.HasBounds) return false;

        for (int i = 0; i < maxAttempts; i++)
        {
            Vector2 candidate = new Vector2(
                Random.Range(region.BoundsMin.x, region.BoundsMax.x),
                Random.Range(region.BoundsMin.y, region.BoundsMax.y));

            if (!HasClearance(region, candidate, agentRadius)) continue;
            local = candidate;
            return true;
        }
        return false;
    }

    private static bool IsInsideOrTooCloseToObstacle(MapNavRegion region, Vector2 local, float agentRadius)
    {
        if (region.Obstacles == null) return false;

        for (int oi = 0; oi < region.Obstacles.Count; oi++)
        {
            MapNavObstacle obstacle = region.Obstacles[oi];
            if (obstacle == null || obstacle.Points == null || obstacle.Points.Count < 3) continue;

            if (obstacle.Contains(local)) return true;

            float clearance = Mathf.Max(0f, agentRadius) + Mathf.Max(0f, obstacle.CornerPadding);
            if (clearance <= 0f) continue;

            if (IsTooClose(obstacle.Points, local, clearance))
                return true;
        }
        return false;
    }

    private static bool IsTooCloseToRegionEdge(IReadOnlyList<Vector2> points, Vector2 local, float clearance)
    {
        return IsTooClose(points, local, clearance);
    }

    private static bool IsTooClose(IReadOnlyList<Vector2> points, Vector2 local, float clearance)
    {
        float clearSq = clearance * clearance;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            if (MapNavGeometry.DistanceToSegmentSquared(local, points[j], points[i]) < clearSq)
                return true;
        }
        return false;
    }
}
