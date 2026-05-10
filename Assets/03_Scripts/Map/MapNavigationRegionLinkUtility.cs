using System.Collections.Generic;
using UnityEngine;

public static class MapNavigationRegionLinkUtility
{
    public const float HeightTolerance = 0.05f;
    public const float LineTolerance = 0.08f;
    public const float MinimumPortalLength = 0.05f;

    public static bool CanLink(int navLayerA, float heightA, int navLayerB, float heightB)
    {
        return navLayerA == navLayerB
            && Mathf.Abs(heightA - heightB) <= HeightTolerance;
    }

    public static bool TryFindSharedPortal(IReadOnlyList<Vector2> pointsA, IReadOnlyList<Vector2> pointsB, out Vector2 portalA, out Vector2 portalB)
    {
        portalA = default;
        portalB = default;
        if (pointsA == null || pointsB == null || pointsA.Count < 3 || pointsB.Count < 3)
            return false;

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
                if (sqrDistance > LineTolerance * LineTolerance || sqrDistance >= bestSqrDistance)
                    continue;

                bestSqrDistance = sqrDistance;
                portalA = closestA;
                portalB = closestB;
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
        if (Mathf.Abs(MapNavGeometry.Cross(direction, b0 - a0)) > LineTolerance
            || Mathf.Abs(MapNavGeometry.Cross(direction, b1 - a0)) > LineTolerance)
            return false;

        float bProjection0 = Vector2.Dot(b0 - a0, direction);
        float bProjection1 = Vector2.Dot(b1 - a0, direction);
        float overlapMin = Mathf.Max(0f, Mathf.Min(bProjection0, bProjection1));
        float overlapMax = Mathf.Min(length, Mathf.Max(bProjection0, bProjection1));
        if (overlapMax - overlapMin < MinimumPortalLength)
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
