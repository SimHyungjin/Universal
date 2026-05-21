using System.Collections.Generic;
using MapNav.Baking;
using MapNav.Core;
using MapNav.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class NavCoreSelfTest : MonoBehaviour
{
    private const float Tolerance = 1e-3f;

    [ContextMenu("Run All NavCore Tests")]
    public void RunAll()
    {
        var tests = new (string name, System.Func<string> run)[]
        {
            ("Classify_InsideRegion", Test_ClassifyInsideRegion),
            ("Classify_OutsideAll", Test_ClassifyOutsideAll),
            ("Classify_InsideTransition", Test_ClassifyInsideTransition),
            ("Classify_InsideObstacle_NotRegion", Test_ClassifyInsideObstacle),
            ("Height_InRegion", Test_HeightInRegion),
            ("IsInsideObstacle_True", Test_IsInsideObstacleTrue),
            ("IsInsideObstacle_False", Test_IsInsideObstacleFalse),
            ("ProjectToNearest_External", Test_ProjectToNearest),
            ("Graph_SameNode_Trivial", Test_GraphSameNode),
            ("Graph_TwoRegionsSharingEdge", Test_GraphTwoRegions),
            ("Graph_AcrossTransition", Test_GraphAcrossTransition),
            ("Graph_Disconnected_Fails", Test_GraphDisconnected),
            ("Funnel_NoPortals", Test_FunnelEmpty),
            ("Funnel_StraightPortal", Test_FunnelStraight),
            ("Path_SameRegion", Test_PathSameRegion),
            ("Path_AcrossTwoRegions", Test_PathAcrossTwoRegions),
            ("Path_AcrossTransition", Test_PathAcrossTransition),
            ("Path_AvoidsObstacle", Test_PathAvoidsObstacle),
        };

        int passed = 0, failed = 0;
        foreach (var t in tests)
        {
            string err;
            try { err = t.run(); }
            catch (System.Exception ex) { err = ex.ToString(); }

            if (string.IsNullOrEmpty(err))
            {
                passed++;
                Debug.Log($"[PASS] {t.name}");
            }
            else
            {
                failed++;
                Debug.LogError($"[FAIL] {t.name}: {err}");
            }
        }

        Debug.Log($"NavCore self-test: {passed}/{passed + failed} passed");
    }

    // ?? NavQuery tests ????????????????????????????????????????????????????

    private static string Test_ClassifyInsideRegion()
    {
        var blob = BuildSingleRegion();
        try
        {
            var ctx = new NavContext(blob, float4x4.identity, float4x4.identity);
            if (!NavQuery.TryClassify(in ctx, new float3(0f, 0f, 0f), 0f, out NavSpaceRef s))
                return "expected classify true";
            if (s.Kind != NavSpaceKind.Region || s.Id != 0)
                return $"expected Region(0), got {s.Kind}({s.Id})";
            return null;
        }
        finally { blob.Dispose(); }
    }

    private static string Test_ClassifyOutsideAll()
    {
        var blob = BuildSingleRegion();
        try
        {
            var ctx = new NavContext(blob, float4x4.identity, float4x4.identity);
            if (NavQuery.TryClassify(in ctx, new float3(100f, 0f, 100f), 0f, out _))
                return "expected classify false outside";
            return null;
        }
        finally { blob.Dispose(); }
    }

    private static string Test_ClassifyInsideTransition()
    {
        var blob = BuildTwoRegionsViaTransition();
        try
        {
            var ctx = new NavContext(blob, float4x4.identity, float4x4.identity);
            if (!NavQuery.TryClassify(in ctx, new float3(10f, 0f, 0f), 0f, out NavSpaceRef s))
                return "expected classify true";
            if (s.Kind != NavSpaceKind.Transition)
                return $"expected Transition, got {s.Kind}";
            return null;
        }
        finally { blob.Dispose(); }
    }

    private static string Test_ClassifyInsideObstacle()
    {
        var blob = BuildRegionWithObstacle();
        try
        {
            var ctx = new NavContext(blob, float4x4.identity, float4x4.identity);
            if (NavQuery.TryClassify(in ctx, new float3(0f, 0f, 0f), 0f, out _))
                return "expected classify false inside obstacle";
            if (!NavQuery.TryClassify(in ctx, new float3(3f, 0f, 3f), 0f, out NavSpaceRef s))
                return "expected classify true outside obstacle";
            if (s.Kind != NavSpaceKind.Region)
                return $"expected Region, got {s.Kind}";
            return null;
        }
        finally { blob.Dispose(); }
    }

    private static string Test_HeightInRegion()
    {
        var blob = BuildSingleRegion(height: 2.5f);
        try
        {
            var ctx = new NavContext(blob, float4x4.identity, float4x4.identity);
            if (!NavQuery.TryGetHeight(in ctx, new float3(1f, 0f, 1f), 0f, out float h))
                return "expected height true";
            if (math.abs(h - 2.5f) > Tolerance)
                return $"expected 2.5, got {h}";
            return null;
        }
        finally { blob.Dispose(); }
    }

    private static string Test_IsInsideObstacleTrue()
    {
        var blob = BuildRegionWithObstacle();
        try
        {
            var ctx = new NavContext(blob, float4x4.identity, float4x4.identity);
            if (!NavQuery.IsInsideObstacle(in ctx, new float3(0f, 0f, 0f), 0f))
                return "expected inside obstacle";
            return null;
        }
        finally { blob.Dispose(); }
    }

    private static string Test_IsInsideObstacleFalse()
    {
        var blob = BuildRegionWithObstacle();
        try
        {
            var ctx = new NavContext(blob, float4x4.identity, float4x4.identity);
            if (NavQuery.IsInsideObstacle(in ctx, new float3(3f, 0f, 3f), 0f))
                return "expected NOT inside obstacle";
            return null;
        }
        finally { blob.Dispose(); }
    }

    private static string Test_ProjectToNearest()
    {
        var blob = BuildSingleRegion();
        try
        {
            var ctx = new NavContext(blob, float4x4.identity, float4x4.identity);
            if (!NavQuery.TryProjectToNearestSpace(in ctx, new float3(10f, 0f, 0f), out float3 projected, out NavSpaceRef s))
                return "expected project true";
            if (s.Kind != NavSpaceKind.Region) return $"expected Region, got {s.Kind}";
            if (math.abs(projected.x - 5f) > 0.1f) return $"expected projected.x near 5, got {projected.x}";
            return null;
        }
        finally { blob.Dispose(); }
    }

    // ?? NavGraph tests ????????????????????????????????????????????????????

    private static string Test_GraphSameNode()
    {
        var blob = BuildSingleRegion();
        var scratch = new NavScratch(16, Allocator.Temp);
        var nodes = new NativeList<NavSpaceRef>(8, Allocator.Temp);
        var portals = new NativeList<NavPortal>(8, Allocator.Temp);
        try
        {
            var ctx = new NavContext(blob, float4x4.identity, float4x4.identity);
            if (!NavGraph.TryFindPath(in ctx, NavSpaceRef.Region(0), float3.zero, NavSpaceRef.Region(0), float3.zero, 0f, ref scratch, ref nodes, ref portals))
                return "expected path found";
            if (nodes.Length != 1) return $"expected 1 node, got {nodes.Length}";
            if (portals.Length != 0) return $"expected 0 portals, got {portals.Length}";
            return null;
        }
        finally
        {
            portals.Dispose(); nodes.Dispose(); scratch.Dispose(); blob.Dispose();
        }
    }

    private static string Test_GraphTwoRegions()
    {
        var blob = BuildTwoAdjacentRegions();
        var scratch = new NavScratch(16, Allocator.Temp);
        var nodes = new NativeList<NavSpaceRef>(8, Allocator.Temp);
        var portals = new NativeList<NavPortal>(8, Allocator.Temp);
        try
        {
            var ctx = new NavContext(blob, float4x4.identity, float4x4.identity);
            if (!NavGraph.TryFindPath(in ctx, NavSpaceRef.Region(0), new float3(-2f, 0f, 0f), NavSpaceRef.Region(1), new float3(10f, 0f, 0f), 0f, ref scratch, ref nodes, ref portals))
                return "expected path found";
            if (nodes.Length != 2) return $"expected 2 nodes, got {nodes.Length}";
            if (portals.Length != 1) return $"expected 1 portal, got {portals.Length}";
            return null;
        }
        finally
        {
            portals.Dispose(); nodes.Dispose(); scratch.Dispose(); blob.Dispose();
        }
    }

    private static string Test_GraphAcrossTransition()
    {
        var blob = BuildTwoRegionsViaTransition();
        var scratch = new NavScratch(16, Allocator.Temp);
        var nodes = new NativeList<NavSpaceRef>(8, Allocator.Temp);
        var portals = new NativeList<NavPortal>(8, Allocator.Temp);
        try
        {
            var ctx = new NavContext(blob, float4x4.identity, float4x4.identity);
            if (!NavGraph.TryFindPath(in ctx, NavSpaceRef.Region(0), new float3(-3f, 0f, 0f), NavSpaceRef.Region(1), new float3(20f, 0f, 0f), 0f, ref scratch, ref nodes, ref portals))
                return "expected path found";
            if (nodes.Length != 3) return $"expected 3 nodes, got {nodes.Length}";
            if (nodes[1].Kind != NavSpaceKind.Transition) return $"expected middle Transition, got {nodes[1].Kind}";
            if (portals.Length != 2) return $"expected 2 portals, got {portals.Length}";
            return null;
        }
        finally
        {
            portals.Dispose(); nodes.Dispose(); scratch.Dispose(); blob.Dispose();
        }
    }

    private static string Test_GraphDisconnected()
    {
        var blob = BuildTwoDisjointRegions();
        var scratch = new NavScratch(16, Allocator.Temp);
        var nodes = new NativeList<NavSpaceRef>(8, Allocator.Temp);
        var portals = new NativeList<NavPortal>(8, Allocator.Temp);
        try
        {
            var ctx = new NavContext(blob, float4x4.identity, float4x4.identity);
            if (NavGraph.TryFindPath(in ctx, NavSpaceRef.Region(0), float3.zero, NavSpaceRef.Region(1), new float3(35f, 0f, 0f), 0f, ref scratch, ref nodes, ref portals))
                return "expected path NOT found";
            return null;
        }
        finally
        {
            portals.Dispose(); nodes.Dispose(); scratch.Dispose(); blob.Dispose();
        }
    }

    // ?? NavFunnel tests ???????????????????????????????????????????????????

    private static string Test_FunnelEmpty()
    {
        var portals = new NativeList<NavPortal>(0, Allocator.Temp);
        var output = new NativeList<float3>(8, Allocator.Temp);
        try
        {
            NavFunnel.Smooth(new float3(0f, 0f, 0f), ref portals, new float3(10f, 0f, 0f), 0f, ref output);
            if (output.Length != 2) return $"expected 2 waypoints, got {output.Length}";
            if (math.distance(output[0].xz, float2.zero) > Tolerance) return "first waypoint should be start";
            if (math.distance(output[output.Length - 1].xz, new float2(10f, 0f)) > Tolerance) return "last waypoint should be goal";
            return null;
        }
        finally
        {
            output.Dispose(); portals.Dispose();
        }
    }

    private static string Test_FunnelStraight()
    {
        var portals = new NativeList<NavPortal>(2, Allocator.Temp);
        portals.Add(new NavPortal { A = new float3(5f, 0f, -1f), B = new float3(5f, 0f, 1f) });
        var output = new NativeList<float3>(8, Allocator.Temp);
        try
        {
            NavFunnel.Smooth(new float3(0f, 0f, 0f), ref portals, new float3(10f, 0f, 0f), 0f, ref output);
            if (output.Length < 2) return $"expected at least 2 waypoints, got {output.Length}";
            if (math.distance(output[0].xz, float2.zero) > Tolerance) return "first should be start";
            if (math.distance(output[output.Length - 1].xz, new float2(10f, 0f)) > Tolerance) return "last should be goal";
            return null;
        }
        finally
        {
            output.Dispose(); portals.Dispose();
        }
    }

    // ?? NavPath tests (integration) ???????????????????????????????????????

    private static string Test_PathSameRegion()
    {
        return RunPathTest(BuildSingleRegion(), new float3(-3f, 0f, 0f), new float3(3f, 0f, 0f));
    }

    private static string Test_PathAcrossTwoRegions()
    {
        return RunPathTest(BuildTwoAdjacentRegions(), new float3(-3f, 0f, 0f), new float3(10f, 0f, 0f));
    }

    private static string Test_PathAcrossTransition()
    {
        return RunPathTest(BuildTwoRegionsViaTransition(), new float3(-3f, 0f, 0f), new float3(20f, 0f, 0f));
    }

    private static string Test_PathAvoidsObstacle()
    {
        // Region [-5,5]x[-5,5], obstacle [-1,1]x[-1,1] in middle.
        // Path from (-4, 0, 0) to (4, 0, 0) -- straight line crosses obstacle.
        var blob = BuildRegionWithObstacle();
        var scratch = new NavScratch(16, Allocator.Temp);
        var nodes = new NativeList<NavSpaceRef>(8, Allocator.Temp);
        var portals = new NativeList<NavPortal>(8, Allocator.Temp);
        var waypoints = new NativeList<float3>(8, Allocator.Temp);
        try
        {
            var ctx = new NavContext(blob, float4x4.identity, float4x4.identity);
            float3 start = new float3(-4f, 0f, 0f);
            float3 end = new float3(4f, 0f, 0f);
            if (!NavPath.TryBuild(in ctx, start, end, agentRadius: 0.3f, boundaryTolerance: 0f, ref scratch, ref nodes, ref portals, ref waypoints))
                return "expected build true";
            if (waypoints.Length < 3)
                return $"expected at least 3 waypoints (corners around obstacle), got {waypoints.Length}";
            // Verify no waypoint sits inside the obstacle
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (NavQuery.IsInsideObstacle(in ctx, waypoints[i], 0f))
                    return $"waypoint[{i}] inside obstacle";
            }
            return null;
        }
        finally
        {
            waypoints.Dispose(); portals.Dispose(); nodes.Dispose(); scratch.Dispose(); blob.Dispose();
        }
    }

    private static string RunPathTest(BlobAssetReference<NavBlob> blob, float3 start, float3 end)
    {
        var scratch = new NavScratch(16, Allocator.Temp);
        var nodes = new NativeList<NavSpaceRef>(8, Allocator.Temp);
        var portals = new NativeList<NavPortal>(8, Allocator.Temp);
        var waypoints = new NativeList<float3>(8, Allocator.Temp);
        try
        {
            var ctx = new NavContext(blob, float4x4.identity, float4x4.identity);
            if (!NavPath.TryBuild(in ctx, start, end, 0f, 0f, ref scratch, ref nodes, ref portals, ref waypoints))
                return "expected build true";
            if (waypoints.Length < 2) return $"expected at least 2 waypoints, got {waypoints.Length}";
            if (math.distance(waypoints[0].xz, start.xz) > Tolerance) return "first waypoint should be start";
            if (math.distance(waypoints[waypoints.Length - 1].xz, end.xz) > Tolerance) return "last waypoint should be goal";
            return null;
        }
        finally
        {
            waypoints.Dispose(); portals.Dispose(); nodes.Dispose(); scratch.Dispose(); blob.Dispose();
        }
    }

    // ?? World builders ????????????????????????????????????????????????????

    private static BlobAssetReference<NavBlob> BuildSingleRegion(float height = 0f)
    {
        var regions = new List<MapNavRegion> { Square(0, height, -5f, -5f, 5f, 5f) };
        return MapNavBaker.Build(regions, new List<MapNavTransition>(), Allocator.Persistent);
    }

    private static BlobAssetReference<NavBlob> BuildRegionWithObstacle()
    {
        var r = Square(0, 0f, -5f, -5f, 5f, 5f);
        r.Obstacles.Add(SquareObstacle(-1f, -1f, 1f, 1f));
        return MapNavBaker.Build(new List<MapNavRegion> { r }, new List<MapNavTransition>(), Allocator.Persistent);
    }

    private static BlobAssetReference<NavBlob> BuildTwoAdjacentRegions()
    {
        var r0 = Square(0, 0f, -5f, -5f, 5f, 5f);
        var r1 = Square(1, 0f, 5f, -5f, 15f, 5f);
        return MapNavBaker.Build(new List<MapNavRegion> { r0, r1 }, new List<MapNavTransition>(), Allocator.Persistent);
    }

    private static BlobAssetReference<NavBlob> BuildTwoDisjointRegions()
    {
        var r0 = Square(0, 0f, -5f, -5f, 5f, 5f);
        var r1 = Square(1, 0f, 30f, -5f, 40f, 5f);
        return MapNavBaker.Build(new List<MapNavRegion> { r0, r1 }, new List<MapNavTransition>(), Allocator.Persistent);
    }

    private static BlobAssetReference<NavBlob> BuildTwoRegionsViaTransition()
    {
        var r0 = Square(0, 0f, -5f, -5f, 5f, 5f);
        var r1 = Square(1, 0f, 15f, -5f, 25f, 5f);
        var t = new MapNavTransition
        {
            Id = 0,
            FromRegionId = 0,
            ToRegionId = 1,
            Type = MapNavTransitionType.Edge,
            FromHeight = 0f,
            ToHeight = 0f,
            Bidirectional = true,
            Enabled = true
        };
        t.Points.Add(new Vector2(5f, -1f));
        t.Points.Add(new Vector2(5f, 1f));
        t.Points.Add(new Vector2(15f, 1f));
        t.Points.Add(new Vector2(15f, -1f));
        return MapNavBaker.Build(new List<MapNavRegion> { r0, r1 }, new List<MapNavTransition> { t }, Allocator.Persistent);
    }

    private static MapNavRegion Square(int id, float height, float minX, float minZ, float maxX, float maxZ)
    {
        var shape = new MapNavPolygon();
        shape.Points.Add(new Vector2(minX, minZ));
        shape.Points.Add(new Vector2(minX, maxZ));
        shape.Points.Add(new Vector2(maxX, maxZ));
        shape.Points.Add(new Vector2(maxX, minZ));
        var r = new MapNavRegion { Id = id, Height = height };
        r.Shapes.Add(shape);
        return r;
    }

    private static MapNavObstacle SquareObstacle(float minX, float minZ, float maxX, float maxZ)
    {
        var o = new MapNavObstacle();
        o.Points.Add(new Vector2(minX, minZ));
        o.Points.Add(new Vector2(minX, maxZ));
        o.Points.Add(new Vector2(maxX, maxZ));
        o.Points.Add(new Vector2(maxX, minZ));
        return o;
    }
}

