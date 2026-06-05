using System;
using System.Collections.Generic;
using UnityEngine;

public enum MapNavRegionType
{
    Area = 0
}

public enum MapNavTransitionType
{
    Edge = 0,
    Stair = 1,
    Ramp = 2,
    Door = 3
}

[Serializable]
public sealed class MapNavPolygon
{
    public List<Vector2> Points = new();
    [NonSerialized] public Vector2 BoundsMin;
    [NonSerialized] public Vector2 BoundsMax;
    [NonSerialized] public bool HasBounds;

    public void RecalculateBounds()
    {
        HasBounds = MapNavBoundsUtility.TryCalculateBounds(Points, out BoundsMin, out BoundsMax);
    }

    public bool Contains(Vector2 local, float tolerance = 0f)
    {
        if (!MapNavBoundsUtility.Contains(BoundsMin, BoundsMax, HasBounds, local, tolerance))
            return false;
        return MapNavGeometry.ContainsPoint(Points, local)
            || (tolerance > 0f && MapNavGeometry.IsNearEdge(Points, local, tolerance));
    }
}

[Serializable]
public sealed class MapNavRegion
{
    public int Id;
    public float Height;
    public float Cost = 1f;
    public List<MapNavPolygon> Shapes = new();
    public List<MapNavObstacle> Obstacles = new();
    [NonSerialized] public Vector2 BoundsMin;
    [NonSerialized] public Vector2 BoundsMax;
    [NonSerialized] public bool HasBounds;

    public string DisplayName => $"Region {Id}";

    public float GetHeight(Vector2 localPoint) => Height;

    public bool Contains(Vector2 localPoint) => Contains(localPoint, 0f);

    public bool Contains(Vector2 localPoint, float tolerance)
    {
        if (!ContainsBounds(localPoint, tolerance)) return false;
        for (int i = 0; i < Shapes.Count; i++)
            if (Shapes[i] != null && Shapes[i].Contains(localPoint, tolerance)) return true;
        return false;
    }

    public MapNavPolygon FindContainingShape(Vector2 localPoint, float tolerance = 0f)
    {
        for (int i = 0; i < Shapes.Count; i++)
            if (Shapes[i] != null && Shapes[i].Contains(localPoint, tolerance)) return Shapes[i];
        return null;
    }

    public void RecalculateBounds()
    {
        HasBounds = false;
        if (Shapes == null || Shapes.Count == 0) return;

        bool any = false;
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);

        for (int i = 0; i < Shapes.Count; i++)
        {
            MapNavPolygon shape = Shapes[i];
            if (shape == null) continue;
            shape.RecalculateBounds();
            if (!shape.HasBounds) continue;
            min = any ? Vector2.Min(min, shape.BoundsMin) : shape.BoundsMin;
            max = any ? Vector2.Max(max, shape.BoundsMax) : shape.BoundsMax;
            any = true;
        }

        if (!any) return;
        BoundsMin = min;
        BoundsMax = max;
        HasBounds = true;

        if (Obstacles == null) return;
        for (int i = 0; i < Obstacles.Count; i++)
            Obstacles[i]?.RecalculateBounds();
    }

    public bool ContainsBounds(Vector2 localPoint, float tolerance = 0f)
    {
        return MapNavBoundsUtility.Contains(BoundsMin, BoundsMax, HasBounds, localPoint, tolerance);
    }

    public Bounds GetLocalBounds()
    {
        if (!HasBounds) return new Bounds(Vector3.zero, Vector3.zero);
        Vector2 center = (BoundsMin + BoundsMax) * 0.5f;
        Vector2 size = BoundsMax - BoundsMin;
        return new Bounds(new Vector3(center.x, Height, center.y), new Vector3(size.x, 0f, size.y));
    }
}

[Serializable]
public sealed class MapNavTransition
{
    public int Id;
    public int FromRegionId;
    public int ToRegionId;
    public MapNavTransitionType Type;
    public float FromHeight;
    public float ToHeight;
    public Vector2 UpDirection = Vector2.up;
    public List<Vector2> Points = new();
    public float Cost = 1f;
    public float MinRadius;
    public bool CanStopInside = true;
    public bool CanFightInside;
    public bool Bidirectional = true;
    public bool Enabled = true;
    [NonSerialized] public Vector2 BoundsMin;
    [NonSerialized] public Vector2 BoundsMax;
    [NonSerialized] public bool HasBounds;

    public string DisplayName => $"Transition {Id}";

    public float GetHeight(Vector2 localPoint)
    {
        if (Type == MapNavTransitionType.Edge || Type == MapNavTransitionType.Door)
            return Mathf.Lerp(FromHeight, ToHeight, 0.5f);

        Vector2 direction = UpDirection.sqrMagnitude > 0.0001f
            ? UpDirection.normalized
            : Vector2.up;

        GetProjectedRange(direction, out float min, out float max);
        float length = Mathf.Max(0.0001f, max - min);
        float progress = Mathf.Clamp01((Vector2.Dot(localPoint, direction) - min) / length);
        return Mathf.Lerp(FromHeight, ToHeight, progress);
    }

    public bool Contains(Vector2 localPoint) => Contains(localPoint, 0f);

    public bool Contains(Vector2 localPoint, float tolerance)
    {
        if (!ContainsBounds(localPoint, tolerance)) return false;
        return MapNavGeometry.ContainsPoint(Points, localPoint)
            || MapNavGeometry.IsNearEdge(Points, localPoint, tolerance);
    }

    public void RecalculateBounds()
    {
        HasBounds = MapNavBoundsUtility.TryCalculateBounds(Points, out BoundsMin, out BoundsMax);
    }

    public bool ContainsBounds(Vector2 localPoint, float tolerance = 0f)
    {
        return MapNavBoundsUtility.Contains(BoundsMin, BoundsMax, HasBounds, localPoint, tolerance);
    }

    public Bounds GetLocalBounds()
    {
        if (Points.Count == 0) return new Bounds(Vector3.zero, Vector3.zero);
        Vector2 min = Points[0], max = Points[0];
        for (int i = 1; i < Points.Count; i++)
        {
            min = Vector2.Min(min, Points[i]);
            max = Vector2.Max(max, Points[i]);
        }
        Vector2 center = (min + max) * 0.5f;
        Vector2 size = max - min;
        return new Bounds(
            new Vector3(center.x, Mathf.Lerp(FromHeight, ToHeight, 0.5f), center.y),
            new Vector3(size.x, Mathf.Abs(ToHeight - FromHeight), size.y));
    }

    private void GetProjectedRange(Vector2 direction, out float min, out float max)
    {
        if (Points.Count == 0) { min = 0f; max = 1f; return; }
        min = Vector2.Dot(Points[0], direction);
        max = min;
        for (int i = 1; i < Points.Count; i++)
        {
            float v = Vector2.Dot(Points[i], direction);
            min = Mathf.Min(min, v);
            max = Mathf.Max(max, v);
        }
    }
}

[Serializable]
public sealed class MapNavObstacle
{
    public List<Vector2> Points = new();
    public float CornerPadding = 0.25f;
    // 장애물 윗면의 region 표면 기준 상대 높이. 콜라이더에서 자동 추출되며, 에이전트 StepHeight 이하면 밟고 통과한다.
    public float Height;
    [NonSerialized] public Vector2 BoundsMin;
    [NonSerialized] public Vector2 BoundsMax;
    [NonSerialized] public bool HasBounds;

    public bool Contains(Vector2 localPoint)
    {
        if (!ContainsBounds(localPoint)) return false;
        return MapNavGeometry.ContainsPoint(Points, localPoint);
    }

    public void RecalculateBounds()
    {
        HasBounds = MapNavBoundsUtility.TryCalculateBounds(Points, out BoundsMin, out BoundsMax);
    }

    public bool ContainsBounds(Vector2 localPoint, float tolerance = 0f)
    {
        return MapNavBoundsUtility.Contains(BoundsMin, BoundsMax, HasBounds, localPoint, tolerance);
    }
}

public static class MapNavBoundsUtility
{
    public static bool TryCalculateBounds(IReadOnlyList<Vector2> points, out Vector2 min, out Vector2 max)
    {
        min = default; max = default;
        if (points == null || points.Count == 0) return false;
        min = points[0]; max = points[0];
        for (int i = 1; i < points.Count; i++)
        {
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
        }
        return true;
    }

    public static bool Contains(Vector2 min, Vector2 max, bool hasBounds, Vector2 point, float tolerance = 0f)
    {
        if (!hasBounds) return false;
        return point.x >= min.x - tolerance && point.x <= max.x + tolerance
            && point.y >= min.y - tolerance && point.y <= max.y + tolerance;
    }
}
