using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class Window_TerrainMeshExporter : EditorWindow
{
    private enum ExportResolution
    {
        Full = 1,
        Half = 2,
        Quarter = 4,
        Eighth = 8
    }

    private ExportResolution _resolution = ExportResolution.Full;
    private Material _material;
    private bool _respectTerrainHoles = true;
    private bool _addSideWalls = true;
    private float _sideWallBottomY;
    private bool _createSceneObject = true;
    private bool _addMeshCollider = true;
    private bool _markStatic = true;

    [MenuItem("Tools/Terrain/Export Terrain Mesh Asset")]
    public static void Open()
    {
        GetWindow<Window_TerrainMeshExporter>("Terrain Mesh Exporter");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Terrain Mesh Exporter", EditorStyles.boldLabel);
        EditorGUILayout.Space(6f);

        Terrain selectedTerrain = GetSelectedTerrain();
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField("Selected Terrain", selectedTerrain, typeof(Terrain), true);

        _resolution = (ExportResolution)EditorGUILayout.EnumPopup("Resolution", _resolution);
        _respectTerrainHoles = EditorGUILayout.Toggle("Respect Terrain Holes", _respectTerrainHoles);
        _addSideWalls = EditorGUILayout.Toggle("Add Side Walls", _addSideWalls);
        using (new EditorGUI.DisabledScope(!_addSideWalls))
            _sideWallBottomY = EditorGUILayout.FloatField("Side Wall Bottom Y", _sideWallBottomY);
        _material = (Material)EditorGUILayout.ObjectField("Material", _material, typeof(Material), false);
        _createSceneObject = EditorGUILayout.Toggle("Create Scene Object", _createSceneObject);

        using (new EditorGUI.DisabledScope(!_createSceneObject))
        {
            _addMeshCollider = EditorGUILayout.Toggle("Add Mesh Collider", _addMeshCollider);
            _markStatic = EditorGUILayout.Toggle("Mark As Static", _markStatic);
        }

        EditorGUILayout.Space(8f);
        DrawEstimate(selectedTerrain);

        EditorGUILayout.Space(10f);
        using (new EditorGUI.DisabledScope(selectedTerrain == null))
        {
            if (GUILayout.Button("Export Selected Terrain Mesh", GUILayout.Height(28f)))
                ExportSelectedTerrain(selectedTerrain);
        }
    }

    private void DrawEstimate(Terrain terrain)
    {
        if (terrain == null || terrain.terrainData == null)
        {
            EditorGUILayout.HelpBox("Hierarchy에서 Terrain GameObject를 선택하세요.", MessageType.Info);
            return;
        }

        int step = (int)_resolution;
        int heightmapResolution = terrain.terrainData.heightmapResolution;
        int sampleCount = Mathf.CeilToInt((heightmapResolution - 1) / (float)step) + 1;
        int vertexCount = sampleCount * sampleCount;
        int quadCount = (sampleCount - 1) * (sampleCount - 1);

        EditorGUILayout.HelpBox(
            $"Vertices: {vertexCount:N0}\nQuads before holes: {quadCount:N0}\nUV: terrain 전체 기준 0~1",
            vertexCount > 1000000 ? MessageType.Warning : MessageType.None);
    }

    private void ExportSelectedTerrain(Terrain terrain)
    {
        if (terrain == null || terrain.terrainData == null)
        {
            EditorUtility.DisplayDialog("Terrain Mesh Exporter", "Hierarchy에서 Terrain GameObject를 선택하세요.", "OK");
            return;
        }

        int step = (int)_resolution;
        int heightmapResolution = terrain.terrainData.heightmapResolution;
        int sampleCount = Mathf.CeilToInt((heightmapResolution - 1) / (float)step) + 1;
        int vertexCount = sampleCount * sampleCount;
        if (vertexCount > 1000000)
        {
            bool proceed = EditorUtility.DisplayDialog(
                "Large Mesh",
                $"생성될 vertex가 {vertexCount:N0}개입니다. Full 해상도는 무거울 수 있습니다.\n계속할까요?",
                "Export",
                "Cancel");

            if (!proceed) return;
        }

        string path = EditorUtility.SaveFilePanelInProject(
            "Save Terrain Mesh",
            $"{terrain.name}_Mesh.asset",
            "asset",
            "Terrain mesh asset 저장 위치를 선택하세요.");

        if (string.IsNullOrEmpty(path)) return;

        Mesh mesh = BuildTerrainMesh(terrain.terrainData, step, _respectTerrainHoles, _addSideWalls, _sideWallBottomY);
        mesh.name = $"{terrain.name}_Mesh";

        AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(path));
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (_createSceneObject)
            CreateSceneObject(terrain, mesh);

        EditorUtility.DisplayDialog("Terrain Mesh Exporter", "Terrain mesh export 완료.", "OK");
    }

    private Mesh BuildTerrainMesh(
        TerrainData terrainData,
        int step,
        bool respectTerrainHoles,
        bool addSideWalls,
        float sideWallBottomY)
    {
        int heightmapResolution = terrainData.heightmapResolution;
        int sampleCount = Mathf.CeilToInt((heightmapResolution - 1) / (float)step) + 1;
        float[,] heights = terrainData.GetHeights(0, 0, heightmapResolution, heightmapResolution);

        bool[,] holes = null;
        int holesResolution = 0;
        if (respectTerrainHoles)
        {
            holesResolution = terrainData.holesResolution;
            if (holesResolution > 0)
                holes = terrainData.GetHoles(0, 0, holesResolution, holesResolution);
        }

        List<Vector3> vertices = new(sampleCount * sampleCount);
        List<Vector2> uvs = new(sampleCount * sampleCount);
        List<int> triangles = new((sampleCount - 1) * (sampleCount - 1) * 6);

        Vector3 size = terrainData.size;
        int lastHeightIndex = heightmapResolution - 1;

        for (int z = 0; z < sampleCount; z++)
        {
            int heightZ = Mathf.Min(z * step, lastHeightIndex);
            float nz = heightZ / (float)lastHeightIndex;

            for (int x = 0; x < sampleCount; x++)
            {
                int heightX = Mathf.Min(x * step, lastHeightIndex);
                float nx = heightX / (float)lastHeightIndex;
                int vertexIndex = z * sampleCount + x;

                vertices.Add(new Vector3(
                    nx * size.x,
                    heights[heightZ, heightX] * size.y,
                    nz * size.z));

                uvs.Add(new Vector2(nx, nz));
            }
        }

        for (int z = 0; z < sampleCount - 1; z++)
        {
            for (int x = 0; x < sampleCount - 1; x++)
            {
                if (!IsSurfaceQuad(x, z, step, heightmapResolution, holes, holesResolution))
                    continue;

                int topLeft = z * sampleCount + x;
                int topRight = topLeft + 1;
                int bottomLeft = topLeft + sampleCount;
                int bottomRight = bottomLeft + 1;

                triangles.Add(topLeft);
                triangles.Add(bottomLeft);
                triangles.Add(topRight);

                triangles.Add(topRight);
                triangles.Add(bottomLeft);
                triangles.Add(bottomRight);

                if (!addSideWalls)
                    continue;

                if (!IsSurfaceQuad(x, z - 1, step, heightmapResolution, holes, holesResolution))
                    AddWall(vertices, uvs, triangles, topRight, topLeft, sideWallBottomY);

                if (!IsSurfaceQuad(x, z + 1, step, heightmapResolution, holes, holesResolution))
                    AddWall(vertices, uvs, triangles, bottomLeft, bottomRight, sideWallBottomY);

                if (!IsSurfaceQuad(x - 1, z, step, heightmapResolution, holes, holesResolution))
                    AddWall(vertices, uvs, triangles, topLeft, bottomLeft, sideWallBottomY);

                if (!IsSurfaceQuad(x + 1, z, step, heightmapResolution, holes, holesResolution))
                    AddWall(vertices, uvs, triangles, bottomRight, topRight, sideWallBottomY);
            }
        }

        Mesh mesh = new()
        {
            indexFormat = IndexFormat.UInt32,
            vertices = vertices.ToArray(),
            uv = uvs.ToArray(),
            triangles = triangles.ToArray()
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static bool IsSurfaceQuad(
        int x,
        int z,
        int step,
        int heightmapResolution,
        bool[,] holes,
        int holesResolution)
    {
        int sampleCount = Mathf.CeilToInt((heightmapResolution - 1) / (float)step) + 1;
        if (x < 0 || z < 0 || x >= sampleCount - 1 || z >= sampleCount - 1)
            return false;

        if (holes == null)
            return true;

        int lastHeightIndex = heightmapResolution - 1;
        int startHeightX = Mathf.Min(x * step, lastHeightIndex - 1);
        int startHeightZ = Mathf.Min(z * step, lastHeightIndex - 1);
        int endHeightX = Mathf.Min((x + 1) * step, lastHeightIndex);
        int endHeightZ = Mathf.Min((z + 1) * step, lastHeightIndex);

        int startHoleX = HeightIndexToHoleIndex(startHeightX, lastHeightIndex, holesResolution);
        int startHoleZ = HeightIndexToHoleIndex(startHeightZ, lastHeightIndex, holesResolution);
        int endHoleX = HeightIndexToHoleIndex(endHeightX, lastHeightIndex, holesResolution);
        int endHoleZ = HeightIndexToHoleIndex(endHeightZ, lastHeightIndex, holesResolution);

        for (int hz = startHoleZ; hz <= endHoleZ; hz++)
        {
            for (int hx = startHoleX; hx <= endHoleX; hx++)
            {
                if (!holes[hz, hx])
                    return false;
            }
        }

        return true;
    }

    private static int HeightIndexToHoleIndex(int heightIndex, int lastHeightIndex, int holesResolution)
    {
        if (holesResolution <= 1 || lastHeightIndex <= 0)
            return 0;

        float normalized = Mathf.Clamp01(heightIndex / (float)lastHeightIndex);
        return Mathf.Clamp(Mathf.FloorToInt(normalized * holesResolution), 0, holesResolution - 1);
    }

    private static void AddWall(
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<int> triangles,
        int topA,
        int topB,
        float bottomY)
    {
        Vector3 a = vertices[topA];
        Vector3 b = vertices[topB];
        Vector2 uvA = uvs[topA];
        Vector2 uvB = uvs[topB];

        int wallTopA = vertices.Count;
        vertices.Add(a);
        uvs.Add(uvA);

        int wallBottomA = vertices.Count;
        vertices.Add(new Vector3(a.x, bottomY, a.z));
        uvs.Add(uvA);

        int wallTopB = vertices.Count;
        vertices.Add(b);
        uvs.Add(uvB);

        int wallBottomB = vertices.Count;
        vertices.Add(new Vector3(b.x, bottomY, b.z));
        uvs.Add(uvB);

        triangles.Add(wallTopA);
        triangles.Add(wallBottomA);
        triangles.Add(wallTopB);

        triangles.Add(wallTopB);
        triangles.Add(wallBottomA);
        triangles.Add(wallBottomB);
    }

    private void CreateSceneObject(Terrain terrain, Mesh mesh)
    {
        GameObject go = new($"{terrain.name}_Mesh");
        Undo.RegisterCreatedObjectUndo(go, "Create Terrain Mesh Object");

        go.transform.SetPositionAndRotation(terrain.transform.position, terrain.transform.rotation);
        go.transform.localScale = terrain.transform.localScale;
        go.isStatic = _markStatic;

        MeshFilter filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        if (_material != null)
            renderer.sharedMaterial = _material;

        if (_addMeshCollider)
        {
            MeshCollider collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
        }

        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
    }

    private static Terrain GetSelectedTerrain()
    {
        if (Selection.activeGameObject != null)
        {
            Terrain terrain = Selection.activeGameObject.GetComponent<Terrain>();
            if (terrain != null) return terrain;

            TerrainCollider terrainCollider = Selection.activeGameObject.GetComponent<TerrainCollider>();
            if (terrainCollider != null) return terrainCollider.GetComponent<Terrain>();
        }

        return Selection.activeObject as Terrain;
    }
}
