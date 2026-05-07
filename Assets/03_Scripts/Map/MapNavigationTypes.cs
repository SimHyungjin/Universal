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
public sealed class MapNavRegion
{
    public int Id;
    public int NavLayerId;
    public float Height;
    public float Cost = 1f;
    public List<Vector2> Points = new();
    public List<MapNavObstacle> Obstacles = new();

    public string DisplayName => $"Region {Id}";

    public float GetHeight(Vector2 localPoint)
    {
        return Height;
    }

    public bool Contains(Vector2 localPoint)
    {
        return Contains(localPoint, 0f);
    }

    public bool Contains(Vector2 localPoint, float tolerance)
    {
        return MapNavGeometry.ContainsPoint(Points, localPoint)
            || MapNavGeometry.IsNearEdge(Points, localPoint, tolerance);
    }

    public Bounds GetLocalBounds()
    {
        if (Points.Count == 0)
            return new Bounds(Vector3.zero, Vector3.zero);

        Vector2 min = Points[0];
        Vector2 max = Points[0];

        for (int i = 1; i < Points.Count; i++)
        {
            min = Vector2.Min(min, Points[i]);
            max = Vector2.Max(max, Points[i]);
        }

        Vector2 center = (min + max) * 0.5f;
        Vector2 size = max - min;
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

    public bool Contains(Vector2 localPoint)
    {
        return Contains(localPoint, 0f);
    }

    public bool Contains(Vector2 localPoint, float tolerance)
    {
        return MapNavGeometry.ContainsPoint(Points, localPoint)
            || MapNavGeometry.IsNearEdge(Points, localPoint, tolerance);
    }

    public Bounds GetLocalBounds()
    {
        if (Points.Count == 0)
            return new Bounds(Vector3.zero, Vector3.zero);

        Vector2 min = Points[0];
        Vector2 max = Points[0];

        for (int i = 1; i < Points.Count; i++)
        {
            min = Vector2.Min(min, Points[i]);
            max = Vector2.Max(max, Points[i]);
        }

        Vector2 center = (min + max) * 0.5f;
        Vector2 size = max - min;
        return new Bounds(new Vector3(center.x, Mathf.Lerp(FromHeight, ToHeight, 0.5f), center.y), new Vector3(size.x, Mathf.Abs(ToHeight - FromHeight), size.y));
    }

    private void GetProjectedRange(Vector2 direction, out float min, out float max)
    {
        if (Points.Count == 0)
        {
            min = 0f;
            max = 1f;
            return;
        }

        min = Vector2.Dot(Points[0], direction);
        max = min;

        for (int i = 1; i < Points.Count; i++)
        {
            float value = Vector2.Dot(Points[i], direction);
            min = Mathf.Min(min, value);
            max = Mathf.Max(max, value);
        }
    }
}

[Serializable]
public sealed class MapNavObstacle
{
    public List<Vector2> Points = new();
    public float CornerPadding = 0.25f;

    public bool Contains(Vector2 localPoint)
    {
        return MapNavGeometry.ContainsPoint(Points, localPoint);
    }
}
