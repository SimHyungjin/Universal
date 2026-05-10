using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public readonly struct MapNavigationBlobDataContext
{
    public readonly BlobAssetReference<MapNavigationBlob> Blob;
    public readonly Matrix4x4 LocalToWorldMatrix;
    public readonly Matrix4x4 WorldToLocalMatrix;

    public MapNavigationBlobDataContext(
        BlobAssetReference<MapNavigationBlob> blob,
        Matrix4x4 localToWorldMatrix,
        Matrix4x4 worldToLocalMatrix)
    {
        Blob = blob;
        LocalToWorldMatrix = localToWorldMatrix;
        WorldToLocalMatrix = worldToLocalMatrix;
    }

    public bool IsValid => Blob.IsCreated;
    public int RegionCount => IsValid ? Blob.Value.Regions.Length : 0;
    public int TransitionCount => IsValid ? Blob.Value.Transitions.Length : 0;
    public int ConnectionCount => IsValid ? Blob.Value.Connections.Length : 0;
    public int ObstacleCount => IsValid ? Blob.Value.Obstacles.Length : 0;
    public int PointCount => IsValid ? Blob.Value.Points.Length : 0;

    public bool TryGetRegionAt(int index, out MapNavRegionBlob region)
    {
        if (IsValid && index >= 0 && index < Blob.Value.Regions.Length)
        {
            region = Blob.Value.Regions[index];
            return true;
        }

        region = default;
        return false;
    }

    public bool TryGetTransitionAt(int index, out MapNavTransitionBlob transition)
    {
        if (IsValid && index >= 0 && index < Blob.Value.Transitions.Length)
        {
            transition = Blob.Value.Transitions[index];
            return true;
        }

        transition = default;
        return false;
    }

    public bool TryGetConnectionAt(int index, out MapNavRegionConnectionBlob connection)
    {
        if (IsValid && index >= 0 && index < Blob.Value.Connections.Length)
        {
            connection = Blob.Value.Connections[index];
            return true;
        }

        connection = default;
        return false;
    }

    public bool TryGetObstacleAt(int index, out MapNavObstacleBlob obstacle)
    {
        if (IsValid && index >= 0 && index < Blob.Value.Obstacles.Length)
        {
            obstacle = Blob.Value.Obstacles[index];
            return true;
        }

        obstacle = default;
        return false;
    }

    public bool TryGetPointAt(int index, out Vector2 point)
    {
        if (IsValid && index >= 0 && index < Blob.Value.Points.Length)
        {
            point = ToVector2(Blob.Value.Points[index]);
            return true;
        }

        point = default;
        return false;
    }

    public bool TryFindRegion(int regionId, out MapNavRegionBlob region)
    {
        if (!IsValid)
        {
            region = default;
            return false;
        }

        int min = 0;
        int max = RegionCount - 1;
        while (min <= max)
        {
            int index = min + ((max - min) / 2);
            MapNavRegionBlob candidate = Blob.Value.Regions[index];
            if (candidate.Id == regionId)
            {
                region = candidate;
                return true;
            }

            if (candidate.Id < regionId)
                min = index + 1;
            else
                max = index - 1;
        }

        region = default;
        return false;
    }

    public bool TryFindTransition(int transitionId, out MapNavTransitionBlob transition)
    {
        if (!IsValid)
        {
            transition = default;
            return false;
        }

        int min = 0;
        int max = TransitionCount - 1;
        while (min <= max)
        {
            int index = min + ((max - min) / 2);
            MapNavTransitionBlob candidate = Blob.Value.Transitions[index];
            if (candidate.Id == transitionId)
            {
                transition = candidate;
                return true;
            }

            if (candidate.Id < transitionId)
                min = index + 1;
            else
                max = index - 1;
        }

        transition = default;
        return false;
    }

    public bool IsValidPointRange(int start, int count)
    {
        return IsValidRange(start, count, PointCount);
    }

    public bool IsValidObstacleRange(int start, int count)
    {
        return IsValidRange(start, count, ObstacleCount);
    }

    public Vector3 ToLocal3D(Vector3 worldPosition)
    {
        return WorldToLocalMatrix.MultiplyPoint3x4(worldPosition);
    }

    public Vector2 ToLocal2D(Vector3 worldPosition)
    {
        Vector3 local = ToLocal3D(worldPosition);
        return new Vector2(local.x, local.z);
    }

    public Vector3 ToWorld(MapNavRegionBlob region, Vector2 localPoint)
    {
        return LocalToWorldMatrix.MultiplyPoint3x4(new Vector3(localPoint.x, region.Height, localPoint.y));
    }

    public Vector3 ToWorld(MapNavTransitionBlob transition, Vector2 localPoint)
    {
        return LocalToWorldMatrix.MultiplyPoint3x4(new Vector3(localPoint.x, GetTransitionHeight(transition, localPoint), localPoint.y));
    }

    public bool ContainsRegion(MapNavRegionBlob region, Vector2 localPoint, float tolerance = 0f)
    {
        if (!ContainsBounds(region.BoundsMin, region.BoundsMax, region.HasBounds, localPoint, tolerance))
            return false;

        return ContainsPoint(region.PointStart, region.PointCount, localPoint)
            || IsNearEdge(region.PointStart, region.PointCount, localPoint, tolerance);
    }

    public bool ContainsTransition(MapNavTransitionBlob transition, Vector2 localPoint, float tolerance = 0f)
    {
        if (!ContainsBounds(transition.BoundsMin, transition.BoundsMax, transition.HasBounds, localPoint, tolerance))
            return false;

        return ContainsPoint(transition.PointStart, transition.PointCount, localPoint)
            || IsNearEdge(transition.PointStart, transition.PointCount, localPoint, tolerance);
    }

    public bool ContainsObstacle(MapNavObstacleBlob obstacle, Vector2 localPoint)
    {
        if (!ContainsBounds(obstacle.BoundsMin, obstacle.BoundsMax, obstacle.HasBounds, localPoint, 0f))
            return false;

        return ContainsPoint(obstacle.PointStart, obstacle.PointCount, localPoint);
    }

    public bool HasEnoughRegionPoints(MapNavRegionBlob region, int minimum)
    {
        return HasEnoughDeclaredPointsInValidRange(region.PointStart, region.PointCount, minimum);
    }

    public bool HasEnoughTransitionPoints(MapNavTransitionBlob transition, int minimum)
    {
        return HasEnoughDeclaredPointsInValidRange(transition.PointStart, transition.PointCount, minimum);
    }

    public bool HasEnoughObstaclePoints(MapNavObstacleBlob obstacle, int minimum)
    {
        return HasEnoughDeclaredPointsInValidRange(obstacle.PointStart, obstacle.PointCount, minimum);
    }

    public Vector2 GetClosestPointOnRegion(MapNavRegionBlob region, Vector2 localPoint)
    {
        return GetClosestPointOnPolygon(region.PointStart, region.PointCount, localPoint);
    }

    public Vector2 GetClosestPointOnTransition(MapNavTransitionBlob transition, Vector2 localPoint)
    {
        return GetClosestPointOnPolygon(transition.PointStart, transition.PointCount, localPoint);
    }

    public Vector2 GetClosestPointOnObstacle(MapNavObstacleBlob obstacle, Vector2 localPoint)
    {
        return GetClosestPointOnPolygon(obstacle.PointStart, obstacle.PointCount, localPoint);
    }

    public Vector2 GetRegionCenter(MapNavRegionBlob region)
    {
        return AveragePoint(region.PointStart, region.PointCount);
    }

    public Vector2 GetTransitionCenter(MapNavTransitionBlob transition)
    {
        return AveragePoint(transition.PointStart, transition.PointCount);
    }

    public Vector2 GetObstacleCenter(MapNavObstacleBlob obstacle)
    {
        return AveragePoint(obstacle.PointStart, obstacle.PointCount);
    }

    public float GetTransitionHeight(MapNavTransitionBlob transition, Vector2 localPoint)
    {
        MapNavTransitionType type = (MapNavTransitionType)transition.Type;
        if (type == MapNavTransitionType.Edge || type == MapNavTransitionType.Door)
            return Mathf.Lerp(transition.FromHeight, transition.ToHeight, 0.5f);

        Vector2 direction = ToVector2(transition.UpDirection);
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.up;

        GetProjectedRange(transition.PointStart, transition.PointCount, direction, out float min, out float max);
        float length = Mathf.Max(0.0001f, max - min);
        float progress = Mathf.Clamp01((Vector2.Dot(localPoint, direction) - min) / length);
        return Mathf.Lerp(transition.FromHeight, transition.ToHeight, progress);
    }

    private void GetProjectedRange(int pointStart, int pointCount, Vector2 direction, out float min, out float max)
    {
        if (!IsValidPointRange(pointStart, pointCount) || pointCount == 0)
        {
            min = 0f;
            max = 1f;
            return;
        }

        min = Vector2.Dot(ToVector2(Blob.Value.Points[pointStart]), direction);
        max = min;

        for (int i = 1; i < pointCount; i++)
        {
            float value = Vector2.Dot(ToVector2(Blob.Value.Points[pointStart + i]), direction);
            min = Mathf.Min(min, value);
            max = Mathf.Max(max, value);
        }
    }

    private bool ContainsPoint(int pointStart, int pointCount, Vector2 point)
    {
        if (!IsValidPointRange(pointStart, pointCount))
            return false;

        bool inside = false;
        for (int i = 0, j = pointCount - 1; i < pointCount; j = i++)
        {
            Vector2 a = ToVector2(Blob.Value.Points[pointStart + i]);
            Vector2 b = ToVector2(Blob.Value.Points[pointStart + j]);

            bool crosses = (a.y > point.y) != (b.y > point.y);
            if (!crosses)
                continue;

            float x = ((b.x - a.x) * (point.y - a.y) / (b.y - a.y)) + a.x;
            if (point.x < x)
                inside = !inside;
        }

        return inside;
    }

    private bool IsNearEdge(int pointStart, int pointCount, Vector2 point, float tolerance)
    {
        if (tolerance <= 0f || !IsValidPointRange(pointStart, pointCount) || pointCount < 2)
            return false;

        float sqrTolerance = tolerance * tolerance;
        for (int i = 0, j = pointCount - 1; i < pointCount; j = i++)
        {
            Vector2 a = ToVector2(Blob.Value.Points[pointStart + j]);
            Vector2 b = ToVector2(Blob.Value.Points[pointStart + i]);
            if (MapNavGeometry.DistanceToSegmentSquared(point, a, b) <= sqrTolerance)
                return true;
        }

        return false;
    }

    private Vector2 GetClosestPointOnPolygon(int pointStart, int pointCount, Vector2 point)
    {
        if (!IsValidPointRange(pointStart, pointCount) || pointCount == 0)
            return point;

        Vector2 best = ToVector2(Blob.Value.Points[pointStart]);
        float bestSqrDistance = float.PositiveInfinity;

        for (int i = 0, j = pointCount - 1; i < pointCount; j = i++)
        {
            Vector2 closest = MapNavGeometry.ClosestPointOnSegment(
                point,
                ToVector2(Blob.Value.Points[pointStart + j]),
                ToVector2(Blob.Value.Points[pointStart + i]));
            float sqrDistance = (closest - point).sqrMagnitude;
            if (sqrDistance >= bestSqrDistance)
                continue;

            bestSqrDistance = sqrDistance;
            best = closest;
        }

        return best;
    }

    private Vector2 AveragePoint(int pointStart, int pointCount)
    {
        if (!IsValidPointRange(pointStart, pointCount) || pointCount == 0)
            return Vector2.zero;

        Vector2 sum = Vector2.zero;
        for (int i = 0; i < pointCount; i++)
            sum += ToVector2(Blob.Value.Points[pointStart + i]);

        return sum / pointCount;
    }

    private static Vector2 ToVector2(float2 value)
    {
        return new Vector2(value.x, value.y);
    }

    private static bool ContainsBounds(float2 min, float2 max, byte hasBounds, Vector2 point, float tolerance)
    {
        return hasBounds != 0
            && point.x >= min.x - tolerance
            && point.x <= max.x + tolerance
            && point.y >= min.y - tolerance
            && point.y <= max.y + tolerance;
    }

    private bool HasEnoughDeclaredPointsInValidRange(int pointStart, int pointCount, int minimum)
    {
        return pointCount >= minimum && IsValidPointRange(pointStart, pointCount);
    }

    private static bool IsValidRange(int start, int count, int length)
    {
        return start >= 0 && count >= 0 && start <= length && count <= length - start;
    }
}
