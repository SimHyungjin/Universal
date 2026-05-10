using Unity.Collections;
using Unity.Mathematics;

namespace MapNav.Core
{
    public static class NavFunnel
    {
        // Simple Stupid Funnel (Mikko Mononen). Portals must be ordered along the path
        // and oriented so the agent traverses them; orientation (left/right) is resolved
        // automatically using path direction.
        public static void Smooth(
            float3 start,
            ref NativeList<NavPortal> portals,
            float3 goal,
            float agentRadius,
            ref NativeList<float3> outWaypoints)
        {
            outWaypoints.Clear();
            outWaypoints.Add(start);

            if (portals.Length == 0)
            {
                outWaypoints.Add(goal);
                return;
            }

            // Inset portals by agent radius (shrink portal segment from each end).
            // Then orient each portal as (left, right) relative to a reference direction.
            int portalCount = portals.Length;
            NativeArray<float3> portalLeft = new NativeArray<float3>(portalCount + 1, Allocator.Temp);
            NativeArray<float3> portalRight = new NativeArray<float3>(portalCount + 1, Allocator.Temp);

            for (int i = 0; i < portalCount; i++)
            {
                NavPortal p = portals[i];
                float3 a = p.A;
                float3 b = p.B;
                a.y = 0f;
                b.y = 0f;
                ApplyInset(ref a, ref b, agentRadius);

                // Determine orientation: use direction from previous waypoint center to next portal center
                float3 prev = i == 0 ? FlatY(start) : FlatY(MidPoint(portals[i - 1].A, portals[i - 1].B));
                float3 next = FlatY(MidPoint(p.A, p.B));
                float3 dir = next - prev;
                if (math.lengthsq(dir.xz) < NavMath.Epsilon)
                    dir = new float3(1f, 0f, 0f);

                // 2D cross product (in xz plane): positive => point on left of direction
                float crossA = dir.x * (a.z - prev.z) - dir.z * (a.x - prev.x);
                if (crossA >= 0f)
                {
                    portalLeft[i] = a;
                    portalRight[i] = b;
                }
                else
                {
                    portalLeft[i] = b;
                    portalRight[i] = a;
                }
            }

            // Sentinel goal portal: both sides == goal
            float3 goalFlat = FlatY(goal);
            portalLeft[portalCount] = goalFlat;
            portalRight[portalCount] = goalFlat;

            // Funnel
            float3 apex = FlatY(start);
            float3 leftEdge = portalLeft[0];
            float3 rightEdge = portalRight[0];
            int apexIndex = 0;
            int leftIndex = 0;
            int rightIndex = 0;

            for (int i = 1; i <= portalCount; i++)
            {
                float3 left = portalLeft[i];
                float3 right = portalRight[i];

                // Right side update
                if (TriArea2(apex, rightEdge, right) <= 0f)
                {
                    if (Vec3Equal(apex, rightEdge) || TriArea2(apex, leftEdge, right) > 0f)
                    {
                        rightEdge = right;
                        rightIndex = i;
                    }
                    else
                    {
                        // Left over right => insert left as corner, restart from leftEdge
                        outWaypoints.Add(LiftY(leftEdge, start, goal));
                        apex = leftEdge;
                        apexIndex = leftIndex;
                        leftEdge = apex;
                        rightEdge = apex;
                        leftIndex = apexIndex;
                        rightIndex = apexIndex;
                        i = apexIndex;
                        continue;
                    }
                }

                // Left side update
                if (TriArea2(apex, leftEdge, left) >= 0f)
                {
                    if (Vec3Equal(apex, leftEdge) || TriArea2(apex, rightEdge, left) < 0f)
                    {
                        leftEdge = left;
                        leftIndex = i;
                    }
                    else
                    {
                        // Right over left => insert right as corner, restart from rightEdge
                        outWaypoints.Add(LiftY(rightEdge, start, goal));
                        apex = rightEdge;
                        apexIndex = rightIndex;
                        leftEdge = apex;
                        rightEdge = apex;
                        leftIndex = apexIndex;
                        rightIndex = apexIndex;
                        i = apexIndex;
                        continue;
                    }
                }
            }

            outWaypoints.Add(goal);

            portalLeft.Dispose();
            portalRight.Dispose();
        }

        private static void ApplyInset(ref float3 a, ref float3 b, float radius)
        {
            if (radius <= 0f) return;
            float3 ab = b - a;
            ab.y = 0f;
            float lenSq = math.lengthsq(ab);
            if (lenSq <= NavMath.Epsilon) return;
            float len = math.sqrt(lenSq);
            float inset = math.min(radius, len * 0.49f);
            float3 dir = ab / len;
            a += dir * inset;
            b -= dir * inset;
        }

        private static float TriArea2(float3 a, float3 b, float3 c)
        {
            // 2D signed area in xz plane
            float ax = b.x - a.x;
            float az = b.z - a.z;
            float bx = c.x - a.x;
            float bz = c.z - a.z;
            return bx * az - ax * bz;
        }

        private static bool Vec3Equal(float3 a, float3 b)
        {
            float3 d = a - b;
            return math.lengthsq(d.xz) < (NavMath.Epsilon * NavMath.Epsilon);
        }

        private static float3 FlatY(float3 v) => new float3(v.x, 0f, v.z);
        private static float3 MidPoint(float3 a, float3 b) => (a + b) * 0.5f;

        private static float3 LiftY(float3 flat, float3 start, float3 goal)
        {
            // Estimate y by linear interpolation between start.y and goal.y over xz progress.
            float3 dir = new float3(goal.x - start.x, 0f, goal.z - start.z);
            float lenSq = math.lengthsq(dir);
            float t = lenSq > NavMath.Epsilon
                ? math.saturate(math.dot(new float3(flat.x - start.x, 0f, flat.z - start.z), dir) / lenSq)
                : 0f;
            float y = math.lerp(start.y, goal.y, t);
            return new float3(flat.x, y, flat.z);
        }
    }
}
