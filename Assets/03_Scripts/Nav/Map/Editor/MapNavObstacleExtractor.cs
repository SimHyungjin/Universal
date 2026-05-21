using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class MapNavObstacleExtractor
{
    private const int CircleSegments = 12;

    public static void Extract(MapNavigationAuthoring map)
    {
        if (map == null) return;
        if (map.ObstacleLayerMask == 0)
        {
            Debug.LogWarning("[NavObstacle] ObstacleLayerMask가 설정되지 않았습니다.", map);
            return;
        }

        Transform nav = map.transform;
        float heightTol = map.ObstacleHeightTolerance;
        float padding = map.DefaultObstacleCornerPadding;

        Collider[] candidates = CollectCandidates(map.ObstacleLayerMask);

        Undo.RecordObject(map, "Bake Nav Obstacles from Colliders");

        foreach (MapNavRegion region in map.Regions)
        {
            if (region == null) continue;

            region.Obstacles.Clear();

            float regionWorldY = nav.TransformPoint(new Vector3(0f, region.Height, 0f)).y;

            foreach (Collider col in candidates)
            {
                if (!col.enabled) continue;
                if (!OverlapsHeight(col, regionWorldY, heightTol)) continue;

                List<Vector2> polygon = ExtractPolygon(col, nav);
                if (polygon == null || polygon.Count < 3) continue;

                if (!OverlapsRegionXZ(polygon, region)) continue;

                region.Obstacles.Add(new MapNavObstacle
                {
                    Points = polygon,
                    CornerPadding = padding
                });
            }

            region.RecalculateBounds();
        }

        map.RebuildRuntimeData();
        EditorUtility.SetDirty(map);
    }

    // ────────────────────────────────────────────────────────────────

    private static Collider[] CollectCandidates(LayerMask mask)
    {
        var all = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
        var result = new List<Collider>(all.Length);
        foreach (Collider c in all)
        {
            if ((mask.value & (1 << c.gameObject.layer)) == 0) continue;
            if (c is TerrainCollider) continue;
            result.Add(c);
        }
        return result.ToArray();
    }

    private static bool OverlapsHeight(Collider col, float regionWorldY, float tolerance)
    {
        Bounds b = col.bounds;
        return b.min.y <= regionWorldY + tolerance && b.max.y >= regionWorldY - tolerance;
    }

    // ────────────────────────────────────────────────────────────────

    private static List<Vector2> ExtractPolygon(Collider col, Transform nav)
    {
        return col switch
        {
            BoxCollider box         => ExtractBox(box, nav),
            SphereCollider sphere   => ExtractSphere(sphere, nav),
            CapsuleCollider capsule => ExtractCapsule(capsule, nav),
            MeshCollider mesh       => ExtractMesh(mesh, nav),
            _                       => null
        };
    }

    private static List<Vector2> ExtractBox(BoxCollider box, Transform nav)
    {
        Vector3 c = box.center;
        Vector3 e = box.size * 0.5f;
        Vector3[] corners =
        {
            box.transform.TransformPoint(c + new Vector3(-e.x, -e.y, -e.z)),
            box.transform.TransformPoint(c + new Vector3(-e.x, -e.y,  e.z)),
            box.transform.TransformPoint(c + new Vector3( e.x, -e.y,  e.z)),
            box.transform.TransformPoint(c + new Vector3( e.x, -e.y, -e.z)),
            box.transform.TransformPoint(c + new Vector3(-e.x,  e.y, -e.z)),
            box.transform.TransformPoint(c + new Vector3(-e.x,  e.y,  e.z)),
            box.transform.TransformPoint(c + new Vector3( e.x,  e.y,  e.z)),
            box.transform.TransformPoint(c + new Vector3( e.x,  e.y, -e.z)),
        };
        return ConvexHull(WorldToNav(corners, nav));
    }

    private static List<Vector2> ExtractSphere(SphereCollider sphere, Transform nav)
    {
        Vector3 worldCenter = sphere.transform.TransformPoint(sphere.center);
        Vector3 scale = sphere.transform.lossyScale;
        float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));

        var pts = new List<Vector2>(CircleSegments);
        for (int i = 0; i < CircleSegments; i++)
        {
            float a = i * 2f * Mathf.PI / CircleSegments;
            Vector3 wp = worldCenter + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Vector3 lp = nav.InverseTransformPoint(wp);
            pts.Add(new Vector2(lp.x, lp.z));
        }
        return pts;
    }

    private static List<Vector2> ExtractCapsule(CapsuleCollider cap, Transform nav)
    {
        float halfLen = Mathf.Max(0f, cap.height * 0.5f - cap.radius);
        Vector3 axis = cap.direction switch
        {
            0 => new Vector3(halfLen, 0f, 0f),
            2 => new Vector3(0f, 0f, halfLen),
            _ => new Vector3(0f, halfLen, 0f)
        };

        Vector3 top = cap.transform.TransformPoint(cap.center + axis);
        Vector3 bot = cap.transform.TransformPoint(cap.center - axis);
        float radius = cap.radius * Mathf.Max(
            Mathf.Abs(cap.transform.lossyScale.x),
            Mathf.Abs(cap.transform.lossyScale.z));

        var world = new List<Vector3>(CircleSegments * 2);
        for (int i = 0; i < CircleSegments; i++)
        {
            float a = i * 2f * Mathf.PI / CircleSegments;
            Vector3 offset = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            world.Add(top + offset);
            world.Add(bot + offset);
        }
        return ConvexHull(WorldToNav(world.ToArray(), nav));
    }

    private static List<Vector2> ExtractMesh(MeshCollider meshCol, Transform nav)
    {
        if (meshCol.sharedMesh == null) return null;

        Vector3[] verts = meshCol.sharedMesh.vertices;
        if (verts.Length == 0) return null;

        var world = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++)
            world[i] = meshCol.transform.TransformPoint(verts[i]);

        return ConvexHull(WorldToNav(world, nav));
    }

    private static List<Vector2> WorldToNav(Vector3[] world, Transform nav)
    {
        var result = new List<Vector2>(world.Length);
        foreach (Vector3 wp in world)
        {
            Vector3 lp = nav.InverseTransformPoint(wp);
            result.Add(new Vector2(lp.x, lp.z));
        }
        return result;
    }

    // ────────────────────────────────────────────────────────────────

    private static bool OverlapsRegionXZ(List<Vector2> polygon, MapNavRegion region)
    {
        if (!region.HasBounds) return false;

        // AABB 빠른 거부
        Vector2 pMin = polygon[0], pMax = polygon[0];
        foreach (Vector2 p in polygon)
        {
            pMin = Vector2.Min(pMin, p);
            pMax = Vector2.Max(pMax, p);
        }
        if (pMax.x < region.BoundsMin.x || pMin.x > region.BoundsMax.x) return false;
        if (pMax.y < region.BoundsMin.y || pMin.y > region.BoundsMax.y) return false;

        // 각 Shape에 대해 겹침 체크
        for (int si = 0; si < region.Shapes.Count; si++)
        {
            MapNavPolygon shape = region.Shapes[si];
            if (shape?.Points == null || shape.Points.Count < 3) continue;

            // 폴리곤 꼭짓점이 Shape 안에 있는지
            foreach (Vector2 p in polygon)
                if (shape.Contains(p)) return true;

            // Shape 꼭짓점이 폴리곤 안에 있는지
            foreach (Vector2 p in shape.Points)
                if (MapNavGeometry.ContainsPoint(polygon, p)) return true;

            // 엣지 교차 체크
            int pn = polygon.Count, rn = shape.Points.Count;
            for (int pi = 0, pj = pn - 1; pi < pn; pj = pi++)
            for (int ri = 0, rj = rn - 1; ri < rn; rj = ri++)
                if (MapNavGeometry.SegmentsIntersect(polygon[pj], polygon[pi], shape.Points[rj], shape.Points[ri]))
                    return true;
        }

        return false;
    }

    // Andrew's Monotone Chain 볼록 껍질
    private static List<Vector2> ConvexHull(List<Vector2> pts)
    {
        int n = pts.Count;
        if (n < 3) return pts;

        pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        // 중복 제거
        for (int i = pts.Count - 1; i > 0; i--)
            if ((pts[i] - pts[i - 1]).sqrMagnitude < 1e-6f)
                pts.RemoveAt(i);

        if (pts.Count < 3) return pts;

        var h = new List<Vector2>(pts.Count * 2);

        // 하단 껍질
        foreach (Vector2 p in pts)
        {
            while (h.Count >= 2 && HullCross(h[h.Count - 2], h[h.Count - 1], p) <= 0f)
                h.RemoveAt(h.Count - 1);
            h.Add(p);
        }

        // 상단 껍질
        int lower = h.Count;
        for (int i = pts.Count - 2; i >= 0; i--)
        {
            while (h.Count > lower && HullCross(h[h.Count - 2], h[h.Count - 1], pts[i]) <= 0f)
                h.RemoveAt(h.Count - 1);
            h.Add(pts[i]);
        }

        h.RemoveAt(h.Count - 1);
        return h;
    }

    private static float HullCross(Vector2 o, Vector2 a, Vector2 b)
        => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
}
