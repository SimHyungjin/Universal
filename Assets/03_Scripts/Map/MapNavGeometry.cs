using System.Collections.Generic;
using UnityEngine;

public static class MapNavGeometry
{
    public static bool ContainsPoint(IReadOnlyList<Vector2> points, Vector2 point)
    {
        bool inside = false;
        int count = points?.Count ?? 0;

        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[j];

            bool crosses = (a.y > point.y) != (b.y > point.y);
            if (!crosses)
                continue;

            float x = ((b.x - a.x) * (point.y - a.y) / (b.y - a.y)) + a.x;
            if (point.x < x)
                inside = !inside;
        }

        return inside;
    }

    public static bool IsNearEdge(IReadOnlyList<Vector2> points, Vector2 point, float tolerance)
    {
        if (tolerance <= 0f || points == null || points.Count < 2)
            return false;

        float sqrTolerance = tolerance * tolerance;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            if (DistanceToSegmentSquared(point, points[j], points[i]) <= sqrTolerance)
                return true;
        }

        return false;
    }

    public static float DistanceToSegmentSquared(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float sqrLength = ab.sqrMagnitude;
        if (sqrLength <= 0.000001f)
            return (point - a).sqrMagnitude;

        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / sqrLength);
        Vector2 closest = a + ab * t;
        return (point - closest).sqrMagnitude;
    }

    public static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float sqrLength = ab.sqrMagnitude;
        if (sqrLength <= 0.000001f)
            return a;

        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / sqrLength);
        return a + ab * t;
    }

    public static Vector2 ClosestPointOnPolygon(IReadOnlyList<Vector2> points, Vector2 point)
    {
        if (points == null || points.Count == 0)
            return point;

        Vector2 best = points[0];
        float bestSqrDistance = float.PositiveInfinity;

        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            Vector2 closest = ClosestPointOnSegment(point, points[j], points[i]);
            float sqrDistance = (closest - point).sqrMagnitude;
            if (sqrDistance >= bestSqrDistance)
                continue;

            bestSqrDistance = sqrDistance;
            best = closest;
        }

        return best;
    }

    public static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float abC = Cross(b - a, c - a);
        float abD = Cross(b - a, d - a);
        float cdA = Cross(d - c, a - c);
        float cdB = Cross(d - c, b - c);

        return abC * abD < 0f && cdA * cdB < 0f;
    }

    public static bool TryLineSegmentIntersection(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 intersection)
    {
        intersection = default;
        Vector2 r = b - a;
        Vector2 s = d - c;
        float denominator = Cross(r, s);
        if (Mathf.Abs(denominator) <= 0.000001f)
            return false;

        float t = Cross(c - a, s) / denominator;
        float u = Cross(c - a, r) / denominator;
        if (t < 0f || t > 1f || u < 0f || u > 1f)
            return false;

        intersection = a + r * t;
        return true;
    }

    public static float Cross(Vector2 a, Vector2 b)
    {
        return (a.x * b.y) - (a.y * b.x);
    }

    public static Vector2 AveragePoint(IReadOnlyList<Vector2> points)
    {
        if (points == null || points.Count == 0)
            return Vector2.zero;

        Vector2 sum = Vector2.zero;
        for (int i = 0; i < points.Count; i++)
            sum += points[i];

        return sum / points.Count;
    }
}
