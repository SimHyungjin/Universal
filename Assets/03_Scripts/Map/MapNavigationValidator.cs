using System.Collections.Generic;
using UnityEngine;

public static class MapNavigationValidator
{
    public static List<string> Validate(MapNavigationAuthoring map)
    {
        List<string> results = new();

        if (map == null)
        {
            results.Add("MapNavigationAuthoring is null.");
            return results;
        }

        HashSet<int> ids = new();
        IReadOnlyList<MapNavRegion> regions = map.Regions;

        for (int i = 0; i < regions.Count; i++)
        {
            MapNavRegion region = regions[i];
            if (region == null)
            {
                results.Add($"Region at index {i} is null.");
                continue;
            }

            if (region.Id < 0)
                results.Add($"{region.DisplayName} has invalid id {region.Id}.");

            if (!ids.Add(region.Id))
                results.Add($"Region id {region.Id} is duplicated.");

            if (region.Points == null || region.Points.Count < 3)
                results.Add($"Region {region.Id} needs at least 3 points.");

            if (HasSelfIntersection(region.Points))
                results.Add($"Region {region.Id} polygon has self intersection.");

            if (region.Cost < 0f)
                results.Add($"Region {region.Id} must not have negative cost.");

            if (region.Obstacles == null)
                continue;

            for (int obstacleIndex = 0; obstacleIndex < region.Obstacles.Count; obstacleIndex++)
            {
                MapNavObstacle obstacle = region.Obstacles[obstacleIndex];
                if (obstacle == null)
                {
                    results.Add($"Region {region.Id} obstacle at index {obstacleIndex} is null.");
                    continue;
                }

                if (obstacle.Points == null || obstacle.Points.Count < 3)
                    results.Add($"Region {region.Id} obstacle {obstacleIndex} needs at least 3 points.");

                if (HasSelfIntersection(obstacle.Points))
                    results.Add($"Region {region.Id} obstacle {obstacleIndex} polygon has self intersection.");

                if (obstacle.CornerPadding < 0f)
                    results.Add($"Region {region.Id} obstacle {obstacleIndex} has negative corner padding.");
            }
        }

        HashSet<int> transitionIds = new();
        IReadOnlyList<MapNavTransition> transitions = map.Transitions;
        for (int i = 0; i < transitions.Count; i++)
        {
            MapNavTransition transition = transitions[i];
            if (transition == null)
            {
                results.Add($"Transition at index {i} is null.");
                continue;
            }

            if (transition.Id < 0)
                results.Add($"{transition.DisplayName} has invalid id {transition.Id}.");

            if (!transitionIds.Add(transition.Id))
                results.Add($"Transition id {transition.Id} is duplicated.");

            MapNavRegion fromRegion = map.FindRegion(transition.FromRegionId);
            MapNavRegion toRegion = map.FindRegion(transition.ToRegionId);

            if (fromRegion == null)
                results.Add($"Transition {i} has missing FromRegionId {transition.FromRegionId}.");

            if (toRegion == null)
                results.Add($"Transition {i} has missing ToRegionId {transition.ToRegionId}.");

            if (fromRegion != null && Mathf.Abs(transition.FromHeight - fromRegion.Height) > 0.05f)
                results.Add($"Transition {transition.Id} FromHeight {transition.FromHeight:0.###} does not match {fromRegion.DisplayName} height {fromRegion.Height:0.###}.");

            if (toRegion != null && Mathf.Abs(transition.ToHeight - toRegion.Height) > 0.05f)
                results.Add($"Transition {transition.Id} ToHeight {transition.ToHeight:0.###} does not match {toRegion.DisplayName} height {toRegion.Height:0.###}.");

            if (transition.Points == null || transition.Points.Count < 3)
                results.Add($"Transition {transition.Id} needs at least 3 points.");

            if (HasSelfIntersection(transition.Points))
                results.Add($"Transition {transition.Id} polygon has self intersection.");

            if ((transition.Type == MapNavTransitionType.Stair || transition.Type == MapNavTransitionType.Ramp)
                && transition.UpDirection.sqrMagnitude < 0.0001f)
                results.Add($"Transition {transition.Id} is {transition.Type} but has no up direction.");

            if (transition.MinRadius < 0f)
                results.Add($"Transition {i} has negative MinRadius.");

            if (transition.Cost < 0f)
                results.Add($"Transition {transition.Id} must not have negative cost.");
        }

        return results;
    }

    private static bool HasSelfIntersection(IReadOnlyList<Vector2> points)
    {
        if (points == null || points.Count < 4)
            return false;

        for (int a = 0; a < points.Count; a++)
        {
            int b = (a + 1) % points.Count;

            for (int c = a + 1; c < points.Count; c++)
            {
                int d = (c + 1) % points.Count;
                if (a == c || a == d || b == c || b == d)
                    continue;

                if (MapNavGeometry.SegmentsIntersect(points[a], points[b], points[c], points[d]))
                    return true;
            }
        }

        return false;
    }
}
