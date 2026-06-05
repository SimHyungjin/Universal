using MapNav.Data;
using Unity.Entities;
using Unity.Mathematics;

namespace MapNav.Core
{
    public readonly struct NavContext
    {
        public readonly BlobAssetReference<NavBlob> Blob;
        public readonly float4x4 LocalToWorld;
        public readonly float4x4 WorldToLocal;
        // 이 쿼리를 수행하는 에이전트가 밟고 넘을 수 있는 최대 장애물 높이. 장애물 판정·분류·높이 샘플이
        // 이 값 이하의 NavObstacle을 "없는 셈" 친다. 맵 데이터가 아니라 쿼리 주체의 능력이므로, 호출 측이
        // 에이전트별 값을 주입한다(미지정 시 0 = 모든 장애물 우회, 기존 동작과 동일).
        public readonly float StepHeight;

        public NavContext(BlobAssetReference<NavBlob> blob, float4x4 localToWorld, float4x4 worldToLocal, float stepHeight = 0f)
        {
            Blob = blob;
            LocalToWorld = localToWorld;
            WorldToLocal = worldToLocal;
            StepHeight = stepHeight;
        }

        public bool IsValid => Blob.IsCreated;
    }

    public struct NavPortal
    {
        public float3 A;
        public float3 B;
    }

    internal static class NavMath
    {
        internal const float Epsilon = 1e-5f;

        internal static float2 ToLocal2D(float4x4 worldToLocal, float3 worldPos)
        {
            float3 local = math.transform(worldToLocal, worldPos);
            return new float2(local.x, local.z);
        }

        internal static float3 ToLocal3D(float4x4 worldToLocal, float3 worldPos)
        {
            return math.transform(worldToLocal, worldPos);
        }

        internal static float3 ToWorld(float4x4 localToWorld, float2 local2D, float localHeight)
        {
            return math.transform(localToWorld, new float3(local2D.x, localHeight, local2D.y));
        }

        internal static float WorldHeightFromLocal(float4x4 localToWorld, float localHeight)
        {
            return math.transform(localToWorld, new float3(0f, localHeight, 0f)).y;
        }

        internal static bool BoundsContains(float2 min, float2 max, byte hasBounds, float2 p, float tolerance)
        {
            if (hasBounds == 0) return false;
            return p.x >= min.x - tolerance && p.x <= max.x + tolerance
                && p.y >= min.y - tolerance && p.y <= max.y + tolerance;
        }

        internal static bool PolygonContains(ref BlobArray<float2> points, int start, int count, float2 p)
        {
            if (count < 3) return false;
            bool inside = false;
            int j = count - 1;
            for (int i = 0; i < count; j = i++)
            {
                float2 a = points[start + i];
                float2 b = points[start + j];
                bool crosses = (a.y > p.y) != (b.y > p.y);
                if (!crosses) continue;
                float dy = b.y - a.y;
                if (math.abs(dy) <= Epsilon) continue;
                float x = (b.x - a.x) * (p.y - a.y) / dy + a.x;
                if (p.x < x) inside = !inside;
            }
            return inside;
        }

        internal static bool IsNearEdge(ref BlobArray<float2> points, int start, int count, float2 p, float tolerance)
        {
            if (tolerance <= 0f || count < 2) return false;
            float sqr = tolerance * tolerance;
            int j = count - 1;
            for (int i = 0; i < count; j = i++)
            {
                float2 a = points[start + j];
                float2 b = points[start + i];
                if (DistanceToSegmentSq(p, a, b) <= sqr) return true;
            }
            return false;
        }

        internal static float DistanceToSegmentSq(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float lenSq = math.lengthsq(ab);
            if (lenSq <= Epsilon) return math.lengthsq(p - a);
            float t = math.clamp(math.dot(p - a, ab) / lenSq, 0f, 1f);
            float2 closest = a + ab * t;
            return math.lengthsq(p - closest);
        }

        internal static float2 ClosestPointOnSegment(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float lenSq = math.lengthsq(ab);
            if (lenSq <= Epsilon) return a;
            float t = math.clamp(math.dot(p - a, ab) / lenSq, 0f, 1f);
            return a + ab * t;
        }

        internal static float2 ClosestPointOnPolygon(ref BlobArray<float2> points, int start, int count, float2 p, out float bestSqrDistance)
        {
            bestSqrDistance = float.PositiveInfinity;
            float2 best = p;
            if (count < 2) return best;
            int j = count - 1;
            for (int i = 0; i < count; j = i++)
            {
                float2 closest = ClosestPointOnSegment(p, points[start + j], points[start + i]);
                float sqr = math.lengthsq(closest - p);
                if (sqr < bestSqrDistance)
                {
                    bestSqrDistance = sqr;
                    best = closest;
                }
            }
            return best;
        }

        internal static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;
    }
}
