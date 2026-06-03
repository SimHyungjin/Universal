using UnityEditor;
using UnityEngine;

public sealed class Window_TerrainHeightOffset : EditorWindow
{
    private float _worldHeightOffset = 1f;
    private float _edgeWorldHeight;
    private int _edgeWidth = 1;
    private float _holeEdgeWorldHeight;
    private int _holeEdgeWidth = 1;

    [MenuItem("Main/Terrain/Offset Selected Terrain Heights")]
    private static void Open()
    {
        GetWindow<Window_TerrainHeightOffset>("Terrain Height Offset");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Selected Terrain Height Offset", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Adds a world-space height offset to the selected Terrain heightmaps. Shape is preserved until values clamp at the Terrain min/max height.",
            MessageType.Info);

        _worldHeightOffset = EditorGUILayout.FloatField("World Height Offset", _worldHeightOffset);

        using (new EditorGUI.DisabledScope(GetSelectedTerrains().Length == 0))
        {
            if (GUILayout.Button("Apply To Selected Terrain"))
                ApplyToSelectedTerrains(_worldHeightOffset);
        }

        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField("Selected Terrain Edge Height", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Sets the outer heightmap edge cells to an exact world height. Use 0 to drop the selected Terrain border to its base height.",
            MessageType.Info);

        _edgeWorldHeight = EditorGUILayout.FloatField("Edge World Height", _edgeWorldHeight);
        _edgeWidth = Mathf.Max(1, EditorGUILayout.IntField("Edge Width In Samples", _edgeWidth));

        using (new EditorGUI.DisabledScope(GetSelectedTerrains().Length == 0))
        {
            if (GUILayout.Button("Set Selected Terrain Edges"))
                SetSelectedTerrainEdges(_edgeWorldHeight, _edgeWidth);
        }

        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField("Selected Terrain Hole Edge Height", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Sets heightmap samples along shared cardinal borders between visible Terrain and painted Holes. Diagonal hole corners are ignored to avoid thin corner spikes.",
            MessageType.Info);

        _holeEdgeWorldHeight = EditorGUILayout.FloatField("Hole Edge World Height", _holeEdgeWorldHeight);
        _holeEdgeWidth = Mathf.Max(1, EditorGUILayout.IntField("Hole Edge Width In Samples", _holeEdgeWidth));

        using (new EditorGUI.DisabledScope(GetSelectedTerrains().Length == 0))
        {
            if (GUILayout.Button("Set Selected Terrain Hole Edges"))
                SetSelectedTerrainHoleEdges(_holeEdgeWorldHeight, _holeEdgeWidth);
        }
    }

    private static Terrain[] GetSelectedTerrains()
    {
        return Selection.GetFiltered<Terrain>(SelectionMode.Editable | SelectionMode.ExcludePrefab);
    }

    private static void ApplyToSelectedTerrains(float worldHeightOffset)
    {
        Terrain[] terrains = GetSelectedTerrains();
        if (terrains.Length == 0)
        {
            EditorUtility.DisplayDialog("Terrain Height Offset", "Select at least one Terrain in the Hierarchy.", "OK");
            return;
        }

        foreach (Terrain terrain in terrains)
        {
            if (terrain == null || terrain.terrainData == null)
                continue;

            OffsetTerrainHeights(terrain, worldHeightOffset);
        }
    }

    private static void SetSelectedTerrainEdges(float worldHeight, int edgeWidth)
    {
        Terrain[] terrains = GetSelectedTerrains();
        if (terrains.Length == 0)
        {
            EditorUtility.DisplayDialog("Terrain Edge Height", "Select at least one Terrain in the Hierarchy.", "OK");
            return;
        }

        foreach (Terrain terrain in terrains)
        {
            if (terrain == null || terrain.terrainData == null)
                continue;

            SetTerrainEdgeHeights(terrain, worldHeight, edgeWidth);
        }
    }

    private static void SetSelectedTerrainHoleEdges(float worldHeight, int edgeWidth)
    {
        Terrain[] terrains = GetSelectedTerrains();
        if (terrains.Length == 0)
        {
            EditorUtility.DisplayDialog("Terrain Hole Edge Height", "Select at least one Terrain in the Hierarchy.", "OK");
            return;
        }

        foreach (Terrain terrain in terrains)
        {
            if (terrain == null || terrain.terrainData == null)
                continue;

            SetTerrainHoleEdgeHeights(terrain, worldHeight, edgeWidth);
        }
    }

    private static void OffsetTerrainHeights(Terrain terrain, float worldHeightOffset)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.heightmapResolution;
        float normalizedOffset = worldHeightOffset / data.size.y;

        Undo.RegisterCompleteObjectUndo(data, $"Offset {terrain.name} Heights");

        float[,] heights = data.GetHeights(0, 0, resolution, resolution);
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
                heights[y, x] = Mathf.Clamp01(heights[y, x] + normalizedOffset);
        }

        data.SetHeights(0, 0, heights);
        EditorUtility.SetDirty(data);
    }

    private static void SetTerrainEdgeHeights(Terrain terrain, float worldHeight, int edgeWidth)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.heightmapResolution;
        int clampedEdgeWidth = Mathf.Clamp(edgeWidth, 1, resolution);
        float normalizedHeight = Mathf.Clamp01(worldHeight / data.size.y);

        Undo.RegisterCompleteObjectUndo(data, $"Set {terrain.name} Edge Heights");

        float[,] heights = data.GetHeights(0, 0, resolution, resolution);
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                bool isEdge =
                    x < clampedEdgeWidth ||
                    y < clampedEdgeWidth ||
                    x >= resolution - clampedEdgeWidth ||
                    y >= resolution - clampedEdgeWidth;

                if (isEdge)
                    heights[y, x] = normalizedHeight;
            }
        }

        data.SetHeights(0, 0, heights);
        EditorUtility.SetDirty(data);
    }

    private static void SetTerrainHoleEdgeHeights(Terrain terrain, float worldHeight, int edgeWidth)
    {
        TerrainData data = terrain.terrainData;
        int heightResolution = data.heightmapResolution;
        int holeResolution = data.holesResolution;
        int clampedEdgeWidth = Mathf.Clamp(edgeWidth, 1, heightResolution);
        float normalizedHeight = Mathf.Clamp01(worldHeight / data.size.y);

        Undo.RegisterCompleteObjectUndo(data, $"Set {terrain.name} Hole Edge Heights");

        bool[,] holes = data.GetHoles(0, 0, holeResolution, holeResolution);
        float[,] heights = data.GetHeights(0, 0, heightResolution, heightResolution);

        for (int holeY = 0; holeY < holeResolution; holeY++)
        {
            for (int holeX = 0; holeX < holeResolution; holeX++)
            {
                if (holes[holeY, holeX])
                    continue;

                SetHoleCellVisibleBorders(
                    holes,
                    heights,
                    holeResolution,
                    heightResolution,
                    holeX,
                    holeY,
                    clampedEdgeWidth,
                    normalizedHeight);
            }
        }

        data.SetHeights(0, 0, heights);
        EditorUtility.SetDirty(data);
    }

    private static void SetHoleCellVisibleBorders(
        bool[,] holes,
        float[,] heights,
        int holeResolution,
        int heightResolution,
        int holeX,
        int holeY,
        int edgeWidth,
        float normalizedHeight)
    {
        int x0 = HoleBoundaryToHeightSample(holeX, holeResolution, heightResolution);
        int x1 = HoleBoundaryToHeightSample(holeX + 1, holeResolution, heightResolution);
        int y0 = HoleBoundaryToHeightSample(holeY, holeResolution, heightResolution);
        int y1 = HoleBoundaryToHeightSample(holeY + 1, holeResolution, heightResolution);

        if (IsVisibleHoleCell(holes, holeResolution, holeX - 1, holeY))
            SetVerticalBorder(heights, heightResolution, x0, y0, y1, -1, edgeWidth, normalizedHeight);

        if (IsVisibleHoleCell(holes, holeResolution, holeX + 1, holeY))
            SetVerticalBorder(heights, heightResolution, x1, y0, y1, 1, edgeWidth, normalizedHeight);

        if (IsVisibleHoleCell(holes, holeResolution, holeX, holeY - 1))
            SetHorizontalBorder(heights, heightResolution, y0, x0, x1, -1, edgeWidth, normalizedHeight);

        if (IsVisibleHoleCell(holes, holeResolution, holeX, holeY + 1))
            SetHorizontalBorder(heights, heightResolution, y1, x0, x1, 1, edgeWidth, normalizedHeight);
    }

    private static bool IsVisibleHoleCell(bool[,] holes, int holeResolution, int holeX, int holeY)
    {
        return holeX >= 0 &&
            holeY >= 0 &&
            holeX < holeResolution &&
            holeY < holeResolution &&
            holes[holeY, holeX];
    }

    private static void SetVerticalBorder(
        float[,] heights,
        int heightResolution,
        int borderX,
        int startY,
        int endY,
        int visibleDirection,
        int edgeWidth,
        float normalizedHeight)
    {
        int minY = Mathf.Min(startY, endY);
        int maxY = Mathf.Max(startY, endY);

        for (int y = minY; y <= maxY; y++)
        {
            for (int width = 0; width < edgeWidth; width++)
                SetHeightIfInBounds(heights, heightResolution, borderX + visibleDirection * width, y, normalizedHeight);
        }
    }

    private static void SetHorizontalBorder(
        float[,] heights,
        int heightResolution,
        int borderY,
        int startX,
        int endX,
        int visibleDirection,
        int edgeWidth,
        float normalizedHeight)
    {
        int minX = Mathf.Min(startX, endX);
        int maxX = Mathf.Max(startX, endX);

        for (int x = minX; x <= maxX; x++)
        {
            for (int width = 0; width < edgeWidth; width++)
                SetHeightIfInBounds(heights, heightResolution, x, borderY + visibleDirection * width, normalizedHeight);
        }
    }

    private static void SetHeightIfInBounds(
        float[,] heights,
        int heightResolution,
        int x,
        int y,
        float normalizedHeight)
    {
        if (x < 0 || y < 0 || x >= heightResolution || y >= heightResolution)
            return;

        heights[y, x] = normalizedHeight;
    }

    private static int HoleBoundaryToHeightSample(int holeBoundary, int holeResolution, int heightResolution)
    {
        if (heightResolution <= 1 || holeResolution <= 0)
            return 0;

        float normalized = holeBoundary / (float)holeResolution;
        return Mathf.Clamp(Mathf.RoundToInt(normalized * (heightResolution - 1)), 0, heightResolution - 1);
    }
}
