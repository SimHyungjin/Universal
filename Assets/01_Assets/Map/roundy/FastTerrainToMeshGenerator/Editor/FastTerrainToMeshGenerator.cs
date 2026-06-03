using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;
using System.Threading.Tasks;

// ===================================================================================================
// Enums and Asset Management System (Unchanged from original FastTerrainToMeshGenerator code)
// ===================================================================================================
public enum TerrainHandling
{
    KeepEnabled,
    DisableGameObject,
    DisableRendering
}

public enum TerrainShaderType
{
    Unlit,
    LitBIRP,
    LitURP
}

public class AssetManagementSystem
{
    private const string BASE_FOLDER = "Assets/FastTerrainAssets";
    private readonly string terrainFolderName;
    private readonly string uniqueId;

    public string UniqueId => uniqueId;
    public string TerrainFolderName => terrainFolderName;

    public AssetManagementSystem(string terrainName)
    {
        // Generate deterministic unique identifier using timestamp and random component
        uniqueId = GenerateUniqueId(terrainName);
        terrainFolderName = SanitizeFileName($"{terrainName}_{uniqueId}");
    }

    private string GenerateUniqueId(string baseName)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        int randomComponent = UnityEngine.Random.Range(1000, 9999);
        int nameHash = baseName.GetHashCode() & 0xFFFF; // Use only lower 16 bits
        return $"{timestamp}_{nameHash:X4}_{randomComponent}";
    }

    public string GetUniqueAssetPath(string baseName, string extension)
    {
        string sanitizedName = SanitizeFileName(baseName);
        string baseFilePath = Path.Combine(GetTerrainFolderPath(), $"{sanitizedName}_{uniqueId}{extension}");

        // If file exists, append an incremental index
        string finalPath = baseFilePath;
        int counter = 1;
        while (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(finalPath) != null)
        {
            finalPath = Path.Combine(GetTerrainFolderPath(), $"{sanitizedName}_{uniqueId}_{counter}{extension}");
            counter++;
        }

        return finalPath;
    }

    public string GetTerrainFolderPath()
    {
        return Path.Combine(BASE_FOLDER, terrainFolderName);
    }

    public void EnsureFoldersExist()
    {
        if (!AssetDatabase.IsValidFolder(BASE_FOLDER))
        {
            string[] folders = BASE_FOLDER.Split('/');
            string currentPath = "Assets";
            for (int i = 1; i < folders.Length; i++)
            {
                string newPath = Path.Combine(currentPath, folders[i]);
                if (!AssetDatabase.IsValidFolder(newPath))
                {
                    AssetDatabase.CreateFolder(currentPath, folders[i]);
                }
                currentPath = newPath;
            }
        }

        if (!AssetDatabase.IsValidFolder(GetTerrainFolderPath()))
        {
            AssetDatabase.CreateFolder(BASE_FOLDER, terrainFolderName);
        }
    }

    private string SanitizeFileName(string fileName)
    {
        string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
        foreach (char c in invalid)
        {
            fileName = fileName.Replace(c.ToString(), "");
        }
        return fileName.Replace(" ", "_").Trim('.');
    }
}

// ===================================================================================================
// Main Editor Window: FastTerrainToMeshGenerator (Merged with MeshGroundAdjuster as 3rd tab)
// ===================================================================================================
public class FastTerrainToMeshGenerator : EditorWindow
{
    private const float SPACING = 10f;
    private const float HEADER_SPACING = 15f;

    // -----------------------------------------------------------------------------------------------
    // Tabs
    // -----------------------------------------------------------------------------------------------
    private int tabIndex = 0;
    private string[] tabOptions = new string[] { "Terrain", "Vegetation", "Mesh Adjustment" };

    // -----------------------------------------------------------------------------------------------
    // TERRAIN/VEGETATION TAB VARIABLES (original FastTerrainToMeshGenerator fields)
    // -----------------------------------------------------------------------------------------------
    // Shader type drop-down
    private TerrainShaderType shaderType = TerrainShaderType.Unlit;

    // Distance resampling toggle
    private bool enableDistanceResampling = false;

    // If Lit (BIRP or URP), enable normal maps
    private bool enableNormalMaps = false;

    private Terrain selectedTerrain;
    private TerrainHandling terrainHandling = TerrainHandling.DisableGameObject;
    private bool addMeshColliders = true;
    private bool markAsStatic = true;

    private int selectedMeshLayer = 0;
    private int chunkAmount = 1;
    private int resolutionPerChunk = 32;
    private int splatResolution = 1024;
    private string[] splatResolutionOptions = { "128", "256", "512", "1024", "2048" };
    private int selectedSplatResolutionIndex = 3;
    private int terrainCounter = 0;

    // For tree export
    private bool disableSourceTrees = false;
    private List<GameObject> createdTreeObjects = new List<GameObject>();
    private GameObject treeRootParent;

    // Terrain detail (grass, etc.)
    private bool disableSourceDetails = false;
    private List<GameObject> createdDetailObjects = new List<GameObject>();
    private float userDensityFactor = 1.0f;
    private float minimumSeparation = 1f;
    private int maxInstancesPerPatch = 500;
    private float oldDetailDistance = -1f;
    private GameObject lastExportedDetailParent = null;

    // LOD properties
    private bool enableLOD = false;
    private int lodLevels = 2;
    private float lodReductionStrength = 1f;
    private float[] lodTransitionHeights = new float[] { 0.6f, 0.3f, 0.1f };

    // GUI styling
    private GUIStyle headerStyle;
    private GUIStyle sectionStyle;

    private Vector2 mainScrollPosition; // Renamed from original 'scrollPosition' to avoid conflict with mesh tab

    // -----------------------------------------------------------------------------------------------
    // MESH ADJUSTMENT TAB VARIABLES (from MeshGroundAdjuster, exact logic/fields, minor rename for collisions)
    // -----------------------------------------------------------------------------------------------
    private float sinkValue = 0f;
    private int selectedLayer = 0;
    private float raycastDistance = 100f;
    private bool debugMode = true;
    private bool alignToNormal = true;
    private float maxSlopeAngle = 45f;
    //private bool usePivot = false; // In the original code, it's declared but not used. Keeping for exactness.
    private bool fastMode = false;
    private bool liftBeforeRaycast = true;

    private GameObject parentObject;
    private Vector2 meshAdjustmentScrollPosition;
    private bool processingInProgress;
    private int totalPrefabs;
    private int processedPrefabs;
    private List<GameObject> foundPrefabs = new List<GameObject>();
    private Dictionary<UnityEngine.Object, int> prefabTypeCount = new Dictionary<UnityEngine.Object, int>();
    private bool showPrefabList = false;

    // -----------------------------------------------------------------------------------------------
    // Asset Management
    // -----------------------------------------------------------------------------------------------
    private AssetManagementSystem assetManager;

    // -----------------------------------------------------------------------------------------------
    // Menu
    // -----------------------------------------------------------------------------------------------
    [MenuItem("Tools/Roundy/Fast Terrain To Mesh Generator")]
    public static void ShowWindow()
    {
        FastTerrainToMeshGenerator window = GetWindow<FastTerrainToMeshGenerator>("Fast Terrain To Mesh Generator");
        window.minSize = new Vector2(400, 800);
    }

    // -----------------------------------------------------------------------------------------------
    // Initialization
    // -----------------------------------------------------------------------------------------------
    private void OnEnable()
    {
        InitializeStyles();
    }

    private void InitializeStyles()
    {
        headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleLeft,
            margin = new RectOffset(0, 0, 10, 10)
        };

        sectionStyle = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(10, 10, 10, 10),
            margin = new RectOffset(0, 0, 8, 8)
        };
    }

    // -----------------------------------------------------------------------------------------------
    // Main OnGUI (Tabs)
    // -----------------------------------------------------------------------------------------------
    private void OnGUI()
    {
        mainScrollPosition = EditorGUILayout.BeginScrollView(mainScrollPosition);

        EditorGUILayout.Space(SPACING);
        DrawHeader();
        EditorGUILayout.Space(HEADER_SPACING);

        // Draw Tabs
        tabIndex = GUILayout.Toolbar(tabIndex, tabOptions);
        EditorGUILayout.Space(10);

        if (tabIndex == 0)
        {
            DrawTerrainTab();
        }
        else if (tabIndex == 1)
        {
            DrawVegetationTab();
        }
        else
        {
            // 3rd Tab: Mesh Adjustment
            DrawMeshAdjustmentTab();
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        EditorGUILayout.LabelField("Fast Terrain To Mesh Generator v0.3", headerStyle);
        EditorGUILayout.LabelField("Convert Unity Terrain to mesh with fast unlit or lit shaders",
            EditorStyles.miniLabel);
        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.normal.textColor = new Color(0.2f, 0.6f, 1.0f); // Ko-fi blue color
        if (GUILayout.Button("Buy me a Ko-fi :)", buttonStyle, GUILayout.Width(120)))
        {
            Application.OpenURL("https://ko-fi.com/roundy");
        }
    }

    // -----------------------------------------------------------------------------------------------
    // TAB 1: TERRAIN
    // -----------------------------------------------------------------------------------------------
    private void DrawTerrainTab()
    {
        using (new EditorGUILayout.VerticalScope(sectionStyle))
        {
            DrawTerrainSelection();
        }
        EditorGUILayout.Space(SPACING);

        using (new EditorGUILayout.VerticalScope(sectionStyle))
        {
            DrawQualitySettings();
        }
        EditorGUILayout.Space(SPACING);

        using (new EditorGUILayout.VerticalScope(sectionStyle))
        {
            DrawMeshSettings();
        }
        EditorGUILayout.Space(SPACING);

        using (new EditorGUILayout.VerticalScope(sectionStyle))
        {
            DrawLODSettings();
        }
        EditorGUILayout.Space(SPACING);

        using (new EditorGUILayout.VerticalScope(sectionStyle))
        {
            DrawTextureSettings();
        }
        EditorGUILayout.Space(SPACING);

        DrawConvertButton();
    }

    private void DrawTerrainSelection()
    {
        EditorGUILayout.LabelField("Terrain Source", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        Terrain newTerrain = (Terrain)EditorGUILayout.ObjectField(
            new GUIContent("Source Terrain", "The terrain to convert into a mesh"),
            selectedTerrain, typeof(Terrain), true);

        // If user drags a new terrain, check the layer count
        if (newTerrain != selectedTerrain)
        {
            selectedTerrain = newTerrain;
            if (selectedTerrain != null)
            {
                // Check terrain layers
                int layerCount = selectedTerrain.terrainData.terrainLayers.Length;
                if (layerCount > 4)
                {
                    EditorUtility.DisplayDialog(
                        "Warning: More Than 4 Layers",
                        "This tool does not officially support more than 4 terrain layers. " +
                        "Results may be incorrect or incomplete.",
                        "OK"
                    );
                }
            }
        }

        terrainHandling = (TerrainHandling)EditorGUILayout.EnumPopup(
            new GUIContent("Original Terrain Handling",
                "KeepEnabled: Keep terrain as is\n" +
                "DisableGameObject: Disable the terrain GameObject\n" +
                "DisableRendering: Keep terrain enabled but disable its rendering"),
            terrainHandling);

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Generated Mesh Settings", EditorStyles.boldLabel);

        addMeshColliders = EditorGUILayout.Toggle(
            new GUIContent("Add Mesh Colliders", "Add mesh colliders to generated chunks"),
            addMeshColliders);

        markAsStatic = EditorGUILayout.Toggle(
            new GUIContent("Mark As Static", "Mark generated chunks as static objects"),
            markAsStatic);
    }

    private void DrawQualitySettings()
    {
        EditorGUILayout.LabelField("Shader & Quality Settings", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        // Drop-down: Unlit, Lit BIRP, or Lit URP
        shaderType = (TerrainShaderType)EditorGUILayout.EnumPopup(
            new GUIContent("Shader Type", "Choose Unlit, Lit BIRP, or Lit URP"),
            shaderType);

        // Enable/disable distance resampling
        enableDistanceResampling = EditorGUILayout.Toggle(
            new GUIContent("Distance Resampling", "Enable or disable distance-based texture resampling"),
            enableDistanceResampling);

        // Enable normal maps if Lit (BIRP or URP)
        if (shaderType == TerrainShaderType.LitBIRP || shaderType == TerrainShaderType.LitURP)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Normal Maps (Optional)", EditorStyles.boldLabel);
            enableNormalMaps = EditorGUILayout.Toggle(
                new GUIContent("Enable Normal Maps", "Use a lit shader with normal map support."),
                enableNormalMaps);
        }
    }

    private void DrawMeshSettings()
    {
        EditorGUILayout.LabelField("Mesh Generation", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        chunkAmount = EditorGUILayout.IntSlider(
            new GUIContent("Chunk Amount", "Number of chunks per axis (total chunks = this value squared)"),
            chunkAmount, 1, 10);

        resolutionPerChunk = EditorGUILayout.IntSlider(
            new GUIContent("Resolution Per Chunk", "Vertices per chunk (higher = more detailed but slower)"),
            resolutionPerChunk, 16, 256);

        selectedMeshLayer = EditorGUILayout.LayerField(
            new GUIContent("Mesh Layer", "Layer for the generated mesh chunks"),
            selectedMeshLayer);
    }

    private void DrawLODSettings()
    {
        EditorGUILayout.LabelField("LOD Settings", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        enableLOD = EditorGUILayout.Toggle(
            new GUIContent("Enable LOD Groups", "Generate LOD levels for each chunk"),
            enableLOD);

        if (enableLOD)
        {
            EditorGUI.indentLevel++;

            lodLevels = EditorGUILayout.IntSlider(
                new GUIContent("LOD Levels", "Number of LOD levels (2-4)"),
                lodLevels, 2, 4);

            lodReductionStrength = EditorGUILayout.Slider(
                new GUIContent("Reduction Strength", "1 = half size, 0.5 = 75%, 2 = 25%"),
                lodReductionStrength, 0.5f, 2f);

            EditorGUILayout.LabelField("LOD Transition Heights", EditorStyles.boldLabel);
            for (int i = 1; i < lodLevels; i++)
            {
                lodTransitionHeights[i - 1] = EditorGUILayout.Slider(
                    $"LOD {i - 1} to {i}",
                    lodTransitionHeights[i - 1],
                    0.01f,
                    i == 1 ? 0.9f : lodTransitionHeights[i - 2]);
            }

            EditorGUI.indentLevel--;
        }
    }

    private void DrawTextureSettings()
    {
        EditorGUILayout.LabelField("Texture Settings", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        // Splat resolution
        selectedSplatResolutionIndex = EditorGUILayout.Popup(
            new GUIContent("Splat Resolution", "Resolution of the generated splat texture"),
            selectedSplatResolutionIndex, splatResolutionOptions);
        splatResolution = int.Parse(splatResolutionOptions[selectedSplatResolutionIndex]);
    }

    private void DrawConvertButton()
    {
        GUI.enabled = selectedTerrain != null;

        EditorGUILayout.Space(SPACING);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Convert Terrain", GUILayout.Width(200), GUILayout.Height(30)))
            {
                ConvertTerrainToMesh();
            }

            GUILayout.FlexibleSpace();
        }

        GUI.enabled = true;
    }

    private void ConvertTerrainToMesh()
    {
        if (selectedTerrain == null) return;

        TerrainData terrainData = selectedTerrain.terrainData;

        // Initialize asset management
        assetManager = new AssetManagementSystem(selectedTerrain.name);
        assetManager.EnsureFoldersExist();

        // Make sure we can read from the terrain textures (especially normal maps)
        List<TextureImporter> importersToRestore = MakeTerrainTexturesReadable(terrainData);

        GameObject parentObject = CreateParentObject();

        // Create and save splat texture
        Texture2D splatTexture = CreateSplatTexture(terrainData, splatResolution);
        string splatPath = SaveTextureAsset(splatTexture, "SplatMap", ".png", false);

        // Generate mesh chunks
        ProcessMeshChunks(terrainData, parentObject, assetManager.GetTerrainFolderPath());

        // Create and assign terrain material
        Material material = CreateTerrainMaterial(terrainData, splatPath);
        AssignMaterialToChunks(parentObject, material);

        // Restore textures to non-readable
        RestoreTextureImporters(importersToRestore);

        // Final handling
        FinalizeConversion(parentObject);
    }

    private GameObject CreateParentObject()
    {
        string objectName = $"{selectedTerrain.name}_Converted_{(assetManager != null ? assetManager.UniqueId : "NA")}";
        GameObject parentObject = new GameObject(objectName);
        Undo.RegisterCreatedObjectUndo(parentObject, "Create Fast Terrain Mesh");
        parentObject.transform.position = selectedTerrain.transform.position;
        return parentObject;
    }

    private List<TextureImporter> MakeTerrainTexturesReadable(TerrainData terrainData)
    {
        List<TextureImporter> importersToRestore = new List<TextureImporter>();

        foreach (TerrainLayer layer in terrainData.terrainLayers)
        {
            if (layer != null && layer.diffuseTexture != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(layer.diffuseTexture);
                TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null && !importer.isReadable)
                {
                    importersToRestore.Add(importer);
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }
            }
            // Also handle normal map read/write
            if (layer != null && layer.normalMapTexture != null)
            {
                string assetPathN = AssetDatabase.GetAssetPath(layer.normalMapTexture);
                TextureImporter importerN = AssetImporter.GetAtPath(assetPathN) as TextureImporter;
                if (importerN != null && !importerN.isReadable)
                {
                    importersToRestore.Add(importerN);
                    importerN.isReadable = true;
                    importerN.SaveAndReimport();
                }
            }
        }

        return importersToRestore;
    }

    private Material CreateTerrainMaterial(TerrainData terrainData, string splatPath)
    {
        Shader chosenShader = null;

        // Choose shader based on the new enum
        if (shaderType == TerrainShaderType.Unlit)
        {
            chosenShader = Shader.Find("Roundy/FastUnlitTerrainShader");
        }
        else if (shaderType == TerrainShaderType.LitBIRP)
        {
            chosenShader = Shader.Find("Roundy/FastLitTerrainShaderBIRP");
        }
        else if (shaderType == TerrainShaderType.LitURP)
        {
            chosenShader = Shader.Find("Roundy/FastLitTerrainShaderURP");
        }

        if (chosenShader == null)
        {
            Debug.LogError("Chosen shader not found, falling back to URP/Lit.");
            chosenShader = Shader.Find("Universal Render Pipeline/Lit");
        }

        Material material = new Material(chosenShader);

        // Sync up toggles/keywords for distance resampling & normal maps
        material.SetFloat("_EnableResampling", enableDistanceResampling ? 1f : 0f);

        if ((shaderType == TerrainShaderType.LitBIRP || shaderType == TerrainShaderType.LitURP) && enableNormalMaps)
            material.SetFloat("_EnableNormalMaps", 1f);
        else
            material.SetFloat("_EnableNormalMaps", 0f);

        if (enableDistanceResampling)
        {
            material.EnableKeyword("ENABLE_RESAMPLING");
        }
        else
        {
            material.DisableKeyword("ENABLE_RESAMPLING");
        }

        if ((shaderType == TerrainShaderType.LitBIRP || shaderType == TerrainShaderType.LitURP) && enableNormalMaps)
        {
            material.EnableKeyword("ENABLE_NORMAL_MAPS");
        }
        else
        {
            material.DisableKeyword("ENABLE_NORMAL_MAPS");
        }

        // Assign splat texture
        Texture2D splatControl = AssetDatabase.LoadAssetAtPath<Texture2D>(splatPath);
        if (splatControl != null)
        {
            material.SetTexture("_SplatTex", splatControl);
        }
        else
        {
            Debug.LogError($"Failed to load splat texture at path: {splatPath}");
        }

        // Use original textures (no cloning) from up to 4 terrain layers
        TerrainLayer[] layers = terrainData.terrainLayers;
        int maxLayers = Mathf.Min(layers.Length, 4);

        for (int i = 0; i < maxLayers; i++)
        {
            TerrainLayer layer = layers[i];
            string mainTexProperty = $"_MainTex{i}";
            string tintColorProperty = $"_TintColor{i}";
            string bumpTexProperty = $"_BumpMap{i}";

            if (layer != null && layer.diffuseTexture != null)
            {
                // Assign the actual terrain-layer diffuse & tiling
                material.SetTexture(mainTexProperty, layer.diffuseTexture);

                float scaleX = terrainData.size.x / layer.tileSize.x;
                float scaleY = terrainData.size.z / layer.tileSize.y;
                material.SetVector($"{mainTexProperty}_ST",
                    new Vector4(scaleX, scaleY, layer.tileOffset.x, layer.tileOffset.y));

                // Tint color from the diffuseRemapMax
                material.SetColor(tintColorProperty, layer.diffuseRemapMax);

                // If normal maps are enabled & layer has one, assign it
                if ((shaderType == TerrainShaderType.LitBIRP || shaderType == TerrainShaderType.LitURP)
                    && enableNormalMaps
                    && layer.normalMapTexture != null)
                {
                    material.SetTexture(bumpTexProperty, layer.normalMapTexture);
                    material.SetVector($"{bumpTexProperty}_ST",
                        new Vector4(scaleX, scaleY, layer.tileOffset.x, layer.tileOffset.y));
                }
            }
            else
            {
                // No terrain layer or texture? Use default white
                ConfigureDefaultLayerProperties(material, mainTexProperty, tintColorProperty);
            }
        }

        // If fewer than 4 layers, default the remainder
        for (int i = maxLayers; i < 4; i++)
        {
            string mainTexProperty = $"_MainTex{i}";
            string tintColorProperty = $"_TintColor{i}";
            ConfigureDefaultLayerProperties(material, mainTexProperty, tintColorProperty);
        }

        // Save material
        string materialPath = assetManager.GetUniqueAssetPath("TerrainMaterial", ".mat");
        AssetDatabase.CreateAsset(material, materialPath);
        AssetDatabase.SaveAssets();

        return material;
    }

    private void ConfigureDefaultLayerProperties(Material material, string texProperty, string colorProperty)
    {
        Texture2D defaultTex = GetDefaultTexture();
        material.SetTexture(texProperty, defaultTex);
        material.SetVector($"{texProperty}_ST", new Vector4(1, 1, 0, 0));
        material.SetColor(colorProperty, Color.white);
    }

    private static Texture2D defaultTexture;
    private Texture2D GetDefaultTexture()
    {
        if (defaultTexture == null)
        {
            defaultTexture = new Texture2D(1, 1, TextureFormat.RGB24, false);
            defaultTexture.SetPixel(0, 0, Color.white);
            defaultTexture.Apply();
        }
        return defaultTexture;
    }

    private void ProcessMeshChunks(TerrainData terrainData, GameObject parentObject, string folderPath)
    {
        int heightmapResolution = terrainData.heightmapResolution;
        float[,] heights2D = terrainData.GetHeights(0, 0, heightmapResolution, heightmapResolution);
        float chunkSizeFloat = (heightmapResolution - 1) / (float)chunkAmount;

        int totalChunks = chunkAmount * chunkAmount;
        int currentChunk = 0;

        for (int chunkY = 0; chunkY < chunkAmount; chunkY++)
        {
            for (int chunkX = 0; chunkX < chunkAmount; chunkX++)
            {
                EditorUtility.DisplayProgressBar("Converting Terrain to Mesh",
                    $"Processing chunk {currentChunk + 1} of {totalChunks}",
                    (float)currentChunk / totalChunks);

                if (enableLOD)
                {
                    ProcessChunkWithLOD(chunkX, chunkY, chunkSizeFloat, heights2D, heightmapResolution,
                        terrainData, parentObject, folderPath);
                }
                else
                {
                    ProcessSingleChunk(chunkX, chunkY, chunkSizeFloat, heights2D, heightmapResolution,
                        terrainData, parentObject, folderPath);
                }

                currentChunk++;
            }
        }

        EditorUtility.ClearProgressBar();
    }

    private void ProcessChunkWithLOD(int chunkX, int chunkY, float chunkSizeFloat, float[,] heights2D,
        int heightmapResolution, TerrainData terrainData, GameObject parentObject, string folderPath)
    {
        // Create LOD Group parent
        GameObject lodGroupObject = new GameObject($"TerrainChunk_LOD_{chunkX}_{chunkY}");
        Undo.RegisterCreatedObjectUndo(lodGroupObject, "Create LOD Group");
        lodGroupObject.transform.SetParent(parentObject.transform);
        lodGroupObject.layer = selectedMeshLayer;

        LODGroup lodGroup = lodGroupObject.AddComponent<LODGroup>();

        float chunkWorldWidth = terrainData.size.x / chunkAmount;
        float chunkWorldLength = terrainData.size.z / chunkAmount;
        lodGroupObject.transform.localPosition = new Vector3(chunkX * chunkWorldWidth, 0, chunkY * chunkWorldLength);

        LOD[] lods = new LOD[lodLevels];
        for (int lodLevel = 0; lodLevel < lodLevels; lodLevel++)
        {
            int currentResolution = CalculateLODResolution(lodLevel);

            GameObject lodObject = new GameObject($"LOD_{lodLevel}");
            lodObject.transform.SetParent(lodGroupObject.transform);
            lodObject.transform.localPosition = Vector3.zero;

            Mesh lodMesh = CreateTerrainChunkMesh(
                Mathf.RoundToInt(chunkX * chunkSizeFloat),
                Mathf.RoundToInt(chunkY * chunkSizeFloat),
                Mathf.RoundToInt(chunkSizeFloat),
                Mathf.RoundToInt(chunkSizeFloat),
                heights2D,
                heightmapResolution,
                terrainData,
                chunkX,
                chunkY,
                currentResolution
            );

            lodObject.AddComponent<MeshFilter>().sharedMesh = lodMesh;
            lodObject.AddComponent<MeshRenderer>();
            lodObject.layer = selectedMeshLayer;

            if (addMeshColliders && lodLevel == 0)
            {
                MeshCollider meshCollider = lodObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = lodMesh;
            }

            // Mark as static if needed
            if (markAsStatic)
            {
                MarkGameObjectAsStaticRecursive(lodObject);
            }

            SaveMeshAsset(lodMesh, $"TerrainChunk_{chunkX}_{chunkY}_LOD{lodLevel}");

            Renderer[] renderers = { lodObject.GetComponent<Renderer>() };
            float lodPercentage = (lodLevel == lodLevels - 1) ? 0.01f : lodTransitionHeights[lodLevel];
            lods[lodLevel] = new LOD(lodPercentage, renderers);
        }

        lodGroup.SetLODs(lods);
        lodGroup.RecalculateBounds();

        // Mark the LOD parent as static if needed
        if (markAsStatic)
        {
            MarkGameObjectAsStaticRecursive(lodGroupObject);
        }
    }

    private int CalculateLODResolution(int lodLevel)
    {
        if (lodLevel == 0)
            return resolutionPerChunk;

        float reductionFactor = Mathf.Pow(2, lodLevel);
        reductionFactor = Mathf.Pow(reductionFactor, lodReductionStrength);

        int newResolution = Mathf.Max(4, Mathf.RoundToInt(resolutionPerChunk / reductionFactor));
        // Force even number
        newResolution = Mathf.Max(4, newResolution + (newResolution % 2));

        Debug.Log($"LOD {lodLevel}: Original Resolution = {resolutionPerChunk}, " +
                  $"Reduction Factor = {reductionFactor}, " +
                  $"New Resolution = {newResolution}");

        return newResolution;
    }

    private void ProcessSingleChunk(int chunkX, int chunkY, float chunkSizeFloat, float[,] heights2D,
        int heightmapResolution, TerrainData terrainData, GameObject parentObject, string folderPath)
    {
        int startX = Mathf.RoundToInt(chunkX * chunkSizeFloat);
        int startY = Mathf.RoundToInt(chunkY * chunkSizeFloat);
        int endX = (chunkX == chunkAmount - 1) ? (heightmapResolution - 1) :
            Mathf.RoundToInt((chunkX + 1) * chunkSizeFloat);
        int endY = (chunkY == chunkAmount - 1) ? (heightmapResolution - 1) :
            Mathf.RoundToInt((chunkY + 1) * chunkSizeFloat);

        int currentChunkWidth = endX - startX;
        int currentChunkHeight = endY - startY;

        Mesh mesh = CreateTerrainChunkMesh(
            startX, startY,
            currentChunkWidth, currentChunkHeight,
            heights2D,
            heightmapResolution,
            terrainData,
            chunkX, chunkY,
            resolutionPerChunk
        );

        GameObject chunkObject = new GameObject($"TerrainChunk_{chunkX}_{chunkY}");
        Undo.RegisterCreatedObjectUndo(chunkObject, "Create Terrain Chunk");
        chunkObject.transform.SetParent(parentObject.transform);
        chunkObject.layer = selectedMeshLayer;
        chunkObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        chunkObject.AddComponent<MeshRenderer>();

        if (addMeshColliders)
        {
            MeshCollider meshCollider = chunkObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
        }

        float chunkWorldWidth = terrainData.size.x / chunkAmount;
        float chunkWorldLength = terrainData.size.z / chunkAmount;
        chunkObject.transform.localPosition = new Vector3(chunkX * chunkWorldWidth, 0, chunkY * chunkWorldLength);

        // Mark as static if needed
        if (markAsStatic)
        {
            MarkGameObjectAsStaticRecursive(chunkObject);
        }

        SaveMeshAsset(mesh, $"TerrainChunk_{chunkX}_{chunkY}");
    }

    private Mesh CreateTerrainChunkMesh(int startX, int startY, int chunkWidth, int chunkHeight,
        float[,] heights2D, int terrainResolution, TerrainData terrainData, int chunkX, int chunkY,
        int resolution)
    {
        float chunkWorldWidth = terrainData.size.x / chunkAmount;
        float chunkWorldLength = terrainData.size.z / chunkAmount;

        int vertexResolutionX = resolution + 1;
        int vertexResolutionY = resolution + 1;

        Vector3[] vertices = new Vector3[vertexResolutionX * vertexResolutionY];
        Vector2[] uvs = new Vector2[vertexResolutionX * vertexResolutionY];
        List<int> triangles = new List<int>();

        float xScale = chunkWorldWidth / resolution;
        float zScale = chunkWorldLength / resolution;

        float worldStartX = chunkX * chunkWorldWidth;
        float worldStartZ = chunkY * chunkWorldLength;

        float uvScaleX = 1.0f / terrainData.size.x;
        float uvScaleZ = 1.0f / terrainData.size.z;

        for (int y = 0; y < vertexResolutionY; y++)
        {
            for (int x = 0; x < vertexResolutionX; x++)
            {
                int index = y * vertexResolutionX + x;

                float normalizedX = (float)x / resolution;
                float normalizedY = (float)y / resolution;

                float terrainX = Mathf.Lerp(startX, startX + chunkWidth, normalizedX);
                float terrainY = Mathf.Lerp(startY, startY + chunkHeight, normalizedY);

                int mapX = Mathf.Clamp(Mathf.FloorToInt(terrainX), 0, terrainResolution - 1);
                int mapY = Mathf.Clamp(Mathf.FloorToInt(terrainY), 0, terrainResolution - 1);

                float heightValue = heights2D[mapY, mapX] * terrainData.size.y;

                vertices[index] = new Vector3(x * xScale, heightValue, y * zScale);

                float worldX = worldStartX + (x * xScale);
                float worldZ = worldStartZ + (y * zScale);

                uvs[index] = new Vector2(
                    worldX * uvScaleX,
                    worldZ * uvScaleZ
                );

                if (x < resolution && y < resolution)
                {
                    int topLeft = index;
                    int topRight = topLeft + 1;
                    int bottomLeft = topLeft + vertexResolutionX;
                    int bottomRight = bottomLeft + 1;

                    triangles.Add(topLeft);
                    triangles.Add(bottomLeft);
                    triangles.Add(topRight);

                    triangles.Add(topRight);
                    triangles.Add(bottomLeft);
                    triangles.Add(bottomRight);
                }
            }
        }

        Mesh mesh = new Mesh
        {
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
            vertices = vertices,
            uv = uvs,
            triangles = triangles.ToArray()
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Texture2D CreateSplatTexture(TerrainData terrainData, int resolution)
    {
        Texture2D splatTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        float[,,] splatmapData = terrainData.GetAlphamaps(0, 0, terrainData.alphamapWidth, terrainData.alphamapHeight);
        int layerCount = terrainData.terrainLayers.Length;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float splatX = (float)x / resolution * (terrainData.alphamapWidth - 1);
                float splatY = (float)y / resolution * (terrainData.alphamapHeight - 1);

                int sx = Mathf.FloorToInt(splatX);
                int sy = Mathf.FloorToInt(splatY);

                // Safe check for alpha data
                Color splatColor = new Color(
                    layerCount > 0 ? splatmapData[sy, sx, 0] : 1,
                    layerCount > 1 ? splatmapData[sy, sx, 1] : 0,
                    layerCount > 2 ? splatmapData[sy, sx, 2] : 0,
                    layerCount > 3 ? splatmapData[sy, sx, 3] : 0
                );

                // If everything is 0, default to layer 0
                if (splatColor.r + splatColor.g + splatColor.b + splatColor.a == 0)
                {
                    splatColor.r = 1;
                }

                splatTexture.SetPixel(x, y, splatColor);
            }
        }

        splatTexture.Apply();
        return splatTexture;
    }

    private void SaveMeshAsset(Mesh mesh, string baseName)
    {
        string path = assetManager.GetUniqueAssetPath(baseName, ".asset");
        AssetDatabase.CreateAsset(mesh, path);
    }

    private string SaveTextureAsset(Texture2D texture, string baseName, string extension, bool isNormalMap)
    {
        string path = assetManager.GetUniqueAssetPath(baseName, extension);
        byte[] pngData = texture.EncodeToPNG();
        if (pngData != null)
        {
            File.WriteAllBytes(path, pngData);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                // We only do quick default import for the splat map
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true; // Usually fine for splat
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
        }
        return path;
    }

    private void AssignMaterialToChunks(GameObject parentObject, Material material)
    {
        MeshRenderer[] meshRenderers = parentObject.GetComponentsInChildren<MeshRenderer>();
        foreach (MeshRenderer renderer in meshRenderers)
        {
            renderer.sharedMaterial = material;
        }
    }

    private void RestoreTextureImporters(List<TextureImporter> importers)
    {
        foreach (TextureImporter importer in importers)
        {
            importer.isReadable = false;
            importer.SaveAndReimport();
        }
    }

    private void FinalizeConversion(GameObject parentObject)
    {
        switch (terrainHandling)
        {
            case TerrainHandling.DisableGameObject:
                Undo.RecordObject(selectedTerrain.gameObject, "Disable Terrain");
                selectedTerrain.gameObject.SetActive(false);
                break;
            case TerrainHandling.DisableRendering:
                Undo.RecordObject(selectedTerrain, "Disable Terrain Rendering");
                selectedTerrain.drawHeightmap = false;
                selectedTerrain.drawInstanced = false;
                break;
            case TerrainHandling.KeepEnabled:
                // Do nothing
                break;
        }

        // Mark parent as static if needed
        if (markAsStatic)
        {
            MarkGameObjectAsStaticRecursive(parentObject);
        }

        EditorUtility.ClearProgressBar();
        AssetDatabase.Refresh();
        terrainCounter++;

        EditorUtility.DisplayDialog("Conversion Complete",
            "Terrain has been successfully converted to mesh.", "OK");
    }

    private void MarkGameObjectAsStaticRecursive(GameObject go)
    {
        // Record for undo
        Undo.RecordObject(go, "Mark GameObject as Static");

        // Typical static flags
        StaticEditorFlags flags = StaticEditorFlags.BatchingStatic
                                | StaticEditorFlags.OccluderStatic
                                | StaticEditorFlags.OccludeeStatic
                                | StaticEditorFlags.ReflectionProbeStatic
                                | StaticEditorFlags.ContributeGI;

        GameObjectUtility.SetStaticEditorFlags(go, flags);

        // Recursively handle children
        for (int i = 0; i < go.transform.childCount; i++)
        {
            MarkGameObjectAsStaticRecursive(go.transform.GetChild(i).gameObject);
        }
    }

    // -----------------------------------------------------------------------------------------------
    // TAB 2: VEGETATION
    // -----------------------------------------------------------------------------------------------
    private void DrawVegetationTab()
    {
        using (new EditorGUILayout.VerticalScope(sectionStyle))
        {
            // --- TREES ---
            EditorGUILayout.LabelField("Export Trees", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Toggle to disable/hide original terrain trees after exporting
            disableSourceTrees = EditorGUILayout.Toggle(
                new GUIContent("Disable Source Trees", "Hide the original terrain trees after export"),
                disableSourceTrees);

            EditorGUILayout.Space(10);

            if (GUILayout.Button("Export Trees", GUILayout.Height(30)))
            {
                if (selectedTerrain == null)
                {
                    EditorUtility.DisplayDialog("No Terrain Selected",
                        "Please select a terrain on the Terrain tab first.", "OK");
                    return;
                }

                // Ensure we have an AssetManagementSystem
                if (assetManager == null)
                {
                    assetManager = new AssetManagementSystem(selectedTerrain.name);
                    assetManager.EnsureFoldersExist();
                }

                // Create a parent object specifically for trees
                GameObject treesParent = new GameObject($"{selectedTerrain.name}_Trees_{assetManager.UniqueId}");
                Undo.RegisterCreatedObjectUndo(treesParent, "Create Trees Parent");
                treesParent.transform.position = selectedTerrain.transform.position;

                ExportTrees(treesParent);
            }

            EditorGUILayout.Space(15);

            // --- DETAILS ---
            EditorGUILayout.LabelField("Export Terrain Details", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            userDensityFactor = EditorGUILayout.Slider(
                new GUIContent("Global Density Factor", "Overall spawn density (0.1 = sparser, 10 = denser)"),
                userDensityFactor, 0.1f, 10f);

            minimumSeparation = EditorGUILayout.FloatField(
                new GUIContent("Min Distance Between Instances", "Minimum separation for spawned details"),
                minimumSeparation);

            maxInstancesPerPatch = EditorGUILayout.IntField(
                new GUIContent("Max Instances Per Patch", "Cap on how many details spawn in one patch"),
                maxInstancesPerPatch);

            EditorGUILayout.Space(5);

            disableSourceDetails = EditorGUILayout.Toggle(
                new GUIContent("Disable Source Details", "Hide (clear) the original terrain details after export"),
                disableSourceDetails);

            EditorGUILayout.Space(10);

            if (GUILayout.Button("Export Terrain Details", GUILayout.Height(30)))
            {
                if (selectedTerrain == null)
                {
                    EditorUtility.DisplayDialog("No Terrain Selected",
                        "Please select a terrain on the Terrain tab first.", "OK");
                    return;
                }

                // Ensure we have an AssetManagementSystem
                if (assetManager == null)
                {
                    assetManager = new AssetManagementSystem(selectedTerrain.name);
                    assetManager.EnsureFoldersExist();
                }

                // Create a parent object specifically for details
                GameObject detailsParent = new GameObject($"{selectedTerrain.name}_Details_{assetManager.UniqueId}");
                Undo.RegisterCreatedObjectUndo(detailsParent, "Create Details Parent");
                detailsParent.transform.position = selectedTerrain.transform.position;

                ExportTerrainDetails(detailsParent);
            }

            EditorGUILayout.Space(10);

            if (GUILayout.Button("Undo Last Detail Export", GUILayout.Height(30)))
            {
                UndoDetailExport();
            }

            // If no terrain is selected, show a warning
            if (selectedTerrain == null)
            {
                EditorGUILayout.HelpBox("No terrain selected. Please select a terrain on the Terrain tab.", MessageType.Warning);
            }
        }
    }

    private void ExportTrees(GameObject parentObject)
    {
        if (selectedTerrain == null) return;

        TerrainData terrainData = selectedTerrain.terrainData;
        TreeInstance[] trees = terrainData.treeInstances;
        TreePrototype[] treePrototypes = terrainData.treePrototypes;

        treeRootParent = new GameObject("Trees");
        Undo.RegisterCreatedObjectUndo(treeRootParent, "Create Tree Parent");
        treeRootParent.transform.SetParent(parentObject.transform, false);
        createdTreeObjects.Add(treeRootParent);

        Dictionary<GameObject, (GameObject parent, bool hasLOD)> prototypeInfo =
            new Dictionary<GameObject, (GameObject parent, bool hasLOD)>();

        foreach (TreePrototype prototype in treePrototypes)
        {
            GameObject prefab = prototype.prefab;
            if (!prototypeInfo.ContainsKey(prefab))
            {
                GameObject prototypeParent = new GameObject(prefab.name);
                prototypeParent.transform.SetParent(treeRootParent.transform, false);
                bool hasLOD = prefab.GetComponent<LODGroup>() != null;
                prototypeInfo.Add(prefab, (prototypeParent, hasLOD));
                createdTreeObjects.Add(prototypeParent);
            }
        }

        int batchSize = 1000;
        int currentBatch = 0;
        int totalTrees = trees.Length;

        for (int i = 0; i < totalTrees; i++)
        {
            if (i % batchSize == 0)
            {
                currentBatch++;
                EditorUtility.DisplayProgressBar("Exporting Trees",
                    $"Processing trees {i + 1}-{Mathf.Min(i + batchSize, totalTrees)} of {totalTrees}",
                    (float)i / totalTrees);
                Undo.IncrementCurrentGroup();
            }

            TreeInstance tree = trees[i];
            GameObject prefab = treePrototypes[tree.prototypeIndex].prefab;
            var (prototypeParent, hasLOD) = prototypeInfo[prefab];

            Vector3 position = new Vector3(
                tree.position.x * terrainData.size.x,
                terrainData.GetInterpolatedHeight(tree.position.x, tree.position.z),
                tree.position.z * terrainData.size.z
            );

            GameObject treeInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            createdTreeObjects.Add(treeInstance);

            treeInstance.transform.SetParent(prototypeParent.transform, false);
            treeInstance.transform.localPosition = position;

            if (!hasLOD)
            {
                treeInstance.transform.localRotation = Quaternion.Euler(0, tree.rotation * 360f, 0);
            }

            Vector3 baseScale = prefab.transform.localScale;
            treeInstance.transform.localScale = new Vector3(
                baseScale.x * tree.widthScale,
                baseScale.y * tree.heightScale,
                baseScale.z * tree.widthScale
            );
        }

        EditorUtility.ClearProgressBar();

        if (disableSourceTrees)
        {
            Undo.RecordObject(selectedTerrain, "Toggle Terrain Trees Visibility");
            selectedTerrain.drawTreesAndFoliage = false;
        }
    }

    // -----------------------------------------------------------------------------------------------
    // Terrain Details Export (Patch-Based)
    // -----------------------------------------------------------------------------------------------
    private static Mesh crossQuadMesh;
    private static Material defaultDetailMaterial;

    private Dictionary<string, GameObject> detailPrefabs = new Dictionary<string, GameObject>();

    private const string RESOURCES_PATH = "Assets/Roundy/FastTerrainToMeshGenerator/Resources";
    private const string PREFABS_PATH = "Assets/FastTerrainAssets/TerrainDetails";
    private const string MESH_RESOURCE_PATH = "Assets/Roundy/FastTerrainToMeshGenerator/Resources/CrossQuad.asset";
    private const string MATERIAL_RESOURCE_PATH = "Assets/Roundy/FastTerrainToMeshGenerator/Resources/TerrainDetailMaterial.mat";

    private void ExportTerrainDetails(GameObject parentObject)
    {
        if (selectedTerrain == null) return;

        TerrainData terrainData = selectedTerrain.terrainData;
        if (terrainData == null)
        {
            Debug.LogError("TerrainData is null. Cannot export details.");
            return;
        }

        // Make cross-quad mesh from resources
        if (!LoadCrossQuadMeshFromResourcesPatchApproach())
        {
            Debug.LogError("Aborting detail export: CrossQuad mesh not found in Resources.");
            return;
        }

        // Prepare or load detail material
        CreateDetailMaterialPatchApproach();

        // Create a root for detail objects
        GameObject detailRoot = new GameObject("Details_PatchBased");
        Undo.RegisterCreatedObjectUndo(detailRoot, "Create Detail Root (Patch-Based)");
        detailRoot.transform.SetParent(parentObject.transform, false);
        createdDetailObjects.Add(detailRoot);

        // Keep track for undo
        lastExportedDetailParent = detailRoot;

        Vector3 terrainPos = selectedTerrain.transform.position;
        detailRoot.transform.position = terrainPos;

        // Local dictionary for any new texture-based prefabs we generate
        Dictionary<string, GameObject> localPrefabs = new Dictionary<string, GameObject>();

        // Read detail prototypes
        DetailPrototype[] prototypes = terrainData.detailPrototypes;
        if (prototypes == null || prototypes.Length == 0)
        {
            Debug.LogWarning("No DetailPrototypes found in TerrainData. Nothing to export.");
            return;
        }

        float densityFactor = userDensityFactor / 10000f;
        int patchRes = terrainData.detailResolutionPerPatch;
        int patchCountX = terrainData.detailWidth / patchRes;
        int patchCountZ = terrainData.detailHeight / patchRes;

        float cellSizeX = terrainData.size.x / terrainData.detailWidth;
        float cellSizeZ = terrainData.size.z / terrainData.detailHeight;

        int totalPatchCount = patchCountX * patchCountZ * prototypes.Length;
        int processedCount = 0;
        bool userCanceled = false;

        // Store old detail distance if we plan to disable them
        if (disableSourceDetails)
        {
            oldDetailDistance = selectedTerrain.detailObjectDistance;
        }

        try
        {
            // For each detail prototype
            for (int layerIndex = 0; layerIndex < prototypes.Length; layerIndex++)
            {
                DetailPrototype prototype = prototypes[layerIndex];
                GameObject detailPrefab = GetOrCreateDetailPrefabPatchApproach(prototype, layerIndex, localPrefabs);
                if (detailPrefab == null)
                {
                    Debug.Log($"Skipping prototype {layerIndex} - no valid prefab/texture found.");
                    continue;
                }

                // Read entire detail map for this layer
                int[,] detailMap = terrainData.GetDetailLayer(
                    0, 0, terrainData.detailWidth, terrainData.detailHeight, layerIndex);

                // Create sub-parent for this layer
                string layerObjName = $"DetailLayer_{layerIndex}_{detailPrefab.name}";
                GameObject layerParent = new GameObject(layerObjName);
                layerParent.transform.SetParent(detailRoot.transform, false);
                createdDetailObjects.Add(layerParent);

                // Iterate patches
                for (int pz = 0; pz < patchCountZ; pz++)
                {
                    if (userCanceled) break;

                    for (int px = 0; px < patchCountX; px++)
                    {
                        processedCount++;
                        float progress = (float)processedCount / totalPatchCount;
                        if (EditorUtility.DisplayCancelableProgressBar(
                            "Exporting Terrain Details (Patch-Based)",
                            $"Layer {layerIndex + 1}/{prototypes.Length}, Patch ({px},{pz})",
                            progress))
                        {
                            userCanceled = true;
                            break;
                        }

                        // Sum raw density in this patch
                        int patchStartX = px * patchRes;
                        int patchStartZ = pz * patchRes;
                        int patchSum = 0;

                        for (int zz = 0; zz < patchRes; zz++)
                        {
                            int globalZ = patchStartZ + zz;
                            if (globalZ >= terrainData.detailHeight) break;

                            for (int xx = 0; xx < patchRes; xx++)
                            {
                                int globalX = patchStartX + xx;
                                if (globalX >= terrainData.detailWidth) break;
                                patchSum += detailMap[globalZ, globalX];
                            }
                        }

                        // If patchSum == 0 => skip
                        if (patchSum == 0) continue;

                        List<Vector3> spawnedPositions = new List<Vector3>();
                        int spawnedInPatch = 0;

                        // Loop cells in this patch
                        for (int zz = 0; zz < patchRes; zz++)
                        {
                            if (spawnedInPatch >= maxInstancesPerPatch) break;
                            int globalZ = patchStartZ + zz;
                            if (globalZ >= terrainData.detailHeight) break;

                            for (int xx = 0; xx < patchRes; xx++)
                            {
                                if (spawnedInPatch >= maxInstancesPerPatch) break;
                                int globalX = patchStartX + xx;
                                if (globalX >= terrainData.detailWidth) break;

                                int rawDensity = detailMap[globalZ, globalX];
                                if (rawDensity <= 0) continue;

                                for (int i = 0; i < rawDensity; i++)
                                {
                                    if (spawnedInPatch >= maxInstancesPerPatch) break;
                                    if (UnityEngine.Random.value > densityFactor)
                                        continue;

                                    float centerX = (globalX + 0.5f) * cellSizeX;
                                    float centerZ = (globalZ + 0.5f) * cellSizeZ;
                                    float randX = UnityEngine.Random.Range(-0.5f * cellSizeX, 0.5f * cellSizeX);
                                    float randZ = UnityEngine.Random.Range(-0.5f * cellSizeZ, 0.5f * cellSizeZ);

                                    float finalX = centerX + randX;
                                    float finalZ = centerZ + randZ;

                                    float normX = finalX / terrainData.size.x;
                                    float normZ = finalZ / terrainData.size.z;
                                    float yHeight = terrainData.GetInterpolatedHeight(normX, normZ);

                                    Vector3 localPos = new Vector3(finalX, yHeight, finalZ);

                                    // Min separation
                                    bool tooClose = false;
                                    for (int s = 0; s < spawnedPositions.Count; s++)
                                    {
                                        if ((spawnedPositions[s] - localPos).magnitude < minimumSeparation)
                                        {
                                            tooClose = true;
                                            break;
                                        }
                                    }
                                    if (tooClose) continue;

                                    // Spawn
                                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(detailPrefab);
                                    instance.transform.SetParent(layerParent.transform, false);
                                    instance.transform.localPosition = localPos;

                                    // Random tilt & rotation
                                    float randomTiltX = UnityEngine.Random.Range(-10f, 10f);
                                    float randomTiltZ = UnityEngine.Random.Range(-10f, 10f);
                                    float randomRotY = UnityEngine.Random.Range(0f, 360f);
                                    instance.transform.localRotation =
                                        Quaternion.Euler(randomTiltX, randomRotY, randomTiltZ);

                                    // Scale from min/max
                                    float randomWidth = UnityEngine.Random.Range(prototype.minWidth, prototype.maxWidth);
                                    float randomHeight = UnityEngine.Random.Range(prototype.minHeight, prototype.maxHeight);
                                    if (prototype.minHeight == 0f && prototype.maxHeight == 0f)
                                        randomHeight = randomWidth;

                                    instance.transform.localScale =
                                        new Vector3(randomWidth, randomHeight, randomWidth);

                                    spawnedPositions.Add(localPos);
                                    createdDetailObjects.Add(instance);
                                    spawnedInPatch++;
                                }
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // If user canceled, remove partial
        if (userCanceled)
        {
            Debug.LogWarning("User canceled detail export. Removing partial results...");
            if (lastExportedDetailParent != null)
            {
                DestroyImmediate(lastExportedDetailParent);
                lastExportedDetailParent = null;
            }
            return;
        }

        // Optionally disable source details by setting detail distance = 0
        if (disableSourceDetails)
        {
            selectedTerrain.detailObjectDistance = 0f;
            Debug.Log("Source terrain's detail distance set to 0 (hidden).");
        }

        Debug.Log($"[Patch-Based Detail Export] Completed for terrain: {selectedTerrain.name}");
    }

    private bool LoadCrossQuadMeshFromResourcesPatchApproach()
    {
        if (crossQuadMesh != null)
            return true; // already loaded

        crossQuadMesh = Resources.Load<Mesh>("CrossQuad");
        if (crossQuadMesh == null)
        {
            Debug.LogError(
                "Could not find 'CrossQuad' mesh in Resources. " +
                "Please place 'CrossQuad.asset' under a Resources folder named 'CrossQuad'."
            );
            return false;
        }

        Debug.Log("Successfully loaded 'CrossQuad' mesh from Resources (Patch Approach).");
        return true;
    }

    private void CreateDetailMaterialPatchApproach()
    {
        if (defaultDetailMaterial != null)
            return; // already assigned

        // First try loading from Resources
        defaultDetailMaterial = Resources.Load<Material>("TerrainDetailMaterial");
        if (defaultDetailMaterial != null)
        {
            Debug.Log("Loaded 'TerrainDetailMaterial' from Resources (Patch Approach).");
            return;
        }

        // Next try from known asset path
        defaultDetailMaterial = AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_RESOURCE_PATH);
        if (defaultDetailMaterial != null)
        {
            Debug.Log($"Loaded 'TerrainDetailMaterial' from path: {MATERIAL_RESOURCE_PATH} (Patch Approach).");
            return;
        }

        // Otherwise create a fallback URP Lit material
        defaultDetailMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        defaultDetailMaterial.name = "TerrainDetailMaterial_Fallback";
        defaultDetailMaterial.EnableKeyword("_ALPHATEST_ON");
        defaultDetailMaterial.SetFloat("_AlphaClip", 1f);
        defaultDetailMaterial.SetFloat("_Cutoff", 0.5f);

        Debug.LogWarning("No 'TerrainDetailMaterial' found; created a fallback URP material in memory (Patch Approach).");
    }

    private GameObject GetOrCreateDetailPrefabPatchApproach(
        DetailPrototype prototype,
        int layerIndex,
        Dictionary<string, GameObject> localCache)
    {
        if (prototype == null) return null;

        // If it's a prefab-based detail
        if (prototype.prototype != null)
        {
            return prototype.prototype;
        }
        // If it's a texture-based detail
        else if (prototype.prototypeTexture != null)
        {
            Texture2D tex = prototype.prototypeTexture;
            string prefabName = $"Detail_Texture_{tex.name}";
            string prefabPath = $"{PREFABS_PATH}/{prefabName}.prefab";

            // Check local cache
            if (localCache.TryGetValue(prefabPath, out GameObject cached))
                return cached;

            // Try loading from disk
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null)
            {
                localCache[prefabPath] = existing;
                return existing;
            }

            // Not found => create a new cross-quad prefab
            GameObject tempGO = new GameObject(prefabName);
            MeshFilter mf = tempGO.AddComponent<MeshFilter>();
            mf.sharedMesh = crossQuadMesh;

            MeshRenderer mr = tempGO.AddComponent<MeshRenderer>();
            Material instanceMat = new Material(defaultDetailMaterial);
            instanceMat.mainTexture = tex;

            // Create a Materials folder if needed
            string materialDir = $"{PREFABS_PATH}/Materials";
            EnsureDirectoryExists(materialDir);

            string matPath = $"{materialDir}/{prefabName}_Material.mat";
            AssetDatabase.CreateAsset(instanceMat, matPath);
            mr.sharedMaterial = instanceMat;

            EnsureDirectoryExists(PREFABS_PATH);
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(tempGO, prefabPath);
            DestroyImmediate(tempGO);

            localCache[prefabPath] = savedPrefab;
            return savedPrefab;
        }

        return null;
    }

    private void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
        }
    }

    private void UndoDetailExport()
    {
        Debug.Log("Undo Export triggered...");

        // Restore old detail distance if we had disabled it
        if (disableSourceDetails && oldDetailDistance >= 0f && selectedTerrain != null)
        {
            selectedTerrain.detailObjectDistance = oldDetailDistance;
            Debug.Log($"Restored terrain detail distance to {oldDetailDistance}.");
        }

        // Remove the last exported detail parent
        if (lastExportedDetailParent != null)
        {
            Debug.Log($"Removing '{lastExportedDetailParent.name}' from the scene...");
            DestroyImmediate(lastExportedDetailParent);
            lastExportedDetailParent = null;
        }

        Debug.Log("Undo Export complete.");
    }

    // -----------------------------------------------------------------------------------------------
    // TAB 3: MESH ADJUSTMENT (EXACT LOGIC FROM MeshGroundAdjuster, now integrated)
    // -----------------------------------------------------------------------------------------------
    private void DrawMeshAdjustmentTab()
    {
        meshAdjustmentScrollPosition = EditorGUILayout.BeginScrollView(meshAdjustmentScrollPosition);

        EditorGUILayout.LabelField("Mesh Ground Adjustment Settings", EditorStyles.boldLabel);

        DrawParentSelection();
        DrawAdjustmentSettings();
        DrawProgressInfo();

        if (GUILayout.Button("Find Prefabs in Parent"))
        {
            FindPrefabsInParent();
        }

        GUI.enabled = foundPrefabs.Count > 0 && !processingInProgress;
        if (GUILayout.Button("Adjust Found Prefabs"))
        {
            AdjustFoundPrefabs();
        }
        GUI.enabled = true;

        GUI.enabled = true;

        EditorGUILayout.Space(20);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("How to Use:", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("This tool fixes floating or sunken objects that can occur after terrain conversion " +
            "due to mesh simplification. It's also useful for bulk-adjusting any objects to terrain height.", EditorStyles.wordWrappedLabel);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Steps:", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("1. Drag the parent containing your objects (trees, grass, rocks, etc.) to 'Parent Object'",
            EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("2. Set 'Sink Value' (e.g., 0.1 means object base is 0.1 units in the ground)",
            EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("3. Set 'Ground Layer' to your terrain's layer (ensure terrain has a mesh collider)",
            EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("4. Enable 'Align to Surface Normal' if objects should rotate with the terrain",
            EditorStyles.wordWrappedLabel);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Tips:", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope())
        {
            EditorGUILayout.LabelField("• Fast Mode: Better performance but no undo functionality",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("• Debug Mode: Shows adjustment rays (requires Gizmos to be enabled)",
                EditorStyles.wordWrappedLabel);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndScrollView();
    }

    private void DrawParentSelection()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Parent Object Selection", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        parentObject = (GameObject)EditorGUILayout.ObjectField("Parent Object", parentObject, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck())
        {
            foundPrefabs.Clear();
            prefabTypeCount.Clear();
        }

        if (foundPrefabs.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Total Prefabs Found: {foundPrefabs.Count}", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            if (prefabTypeCount.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Unique Prefab Types: {prefabTypeCount.Count}", EditorStyles.boldLabel);
                showPrefabList = EditorGUILayout.Toggle("Show Details", showPrefabList, GUILayout.Width(100));
                EditorGUILayout.EndHorizontal();

                if (showPrefabList)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    foreach (var kvp in prefabTypeCount.OrderByDescending(x => x.Value))
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.ObjectField(kvp.Key, typeof(UnityEngine.Object), false, GUILayout.Width(200));
                        EditorGUILayout.LabelField($"Count: {kvp.Value}", GUILayout.Width(100));
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndVertical();
                }
            }
        }
    }

    private void DrawAdjustmentSettings()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Position Settings", EditorStyles.boldLabel);

        sinkValue = EditorGUILayout.FloatField(new GUIContent("Sink Value", "Distance below the contact point."), sinkValue);
        liftBeforeRaycast = EditorGUILayout.Toggle(new GUIContent("Lift Before Raycast", "Temporarily move up 5 units before shooting down."), liftBeforeRaycast);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Ground Settings", EditorStyles.boldLabel);
        selectedLayer = EditorGUILayout.LayerField("Ground Layer", selectedLayer);
        raycastDistance = EditorGUILayout.FloatField("Raycast Distance", raycastDistance);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Normal Alignment Settings", EditorStyles.boldLabel);
        alignToNormal = EditorGUILayout.Toggle("Align to Surface Normal", alignToNormal);
        if (alignToNormal)
        {
            EditorGUI.indentLevel++;
            maxSlopeAngle = EditorGUILayout.Slider("Max Slope Angle", maxSlopeAngle, 0f, 90f);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Performance Settings", EditorStyles.boldLabel);
        fastMode = EditorGUILayout.Toggle(new GUIContent("Fast Mode", "Disables undo functionality for better performance with large hierarchies"), fastMode);
        debugMode = EditorGUILayout.Toggle("Debug Mode", debugMode);
    }

    private void DrawProgressInfo()
    {
        if (processingInProgress)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Processing Progress", EditorStyles.boldLabel);
            EditorGUI.ProgressBar(
                EditorGUILayout.GetControlRect(false, 20),
                (float)processedPrefabs / totalPrefabs,
                $"Processing: {processedPrefabs}/{totalPrefabs}"
            );
        }
    }

    private void FindPrefabsInParent()
    {
        if (parentObject == null)
        {
            EditorUtility.DisplayDialog("No Parent Selected", "Please select a parent object to search for prefabs.", "OK");
            return;
        }

        foundPrefabs.Clear();
        prefabTypeCount.Clear();

        EditorUtility.DisplayProgressBar("Finding Prefabs", "Scanning hierarchy...", 0f);
        CollectPrefabsRecursively(parentObject.transform, foundPrefabs);

        // Build prefab type statistics
        foreach (var prefab in foundPrefabs)
        {
            var prefabAsset = PrefabUtility.GetCorrespondingObjectFromOriginalSource(prefab);
            if (prefabAsset != null)
            {
                if (!prefabTypeCount.ContainsKey(prefabAsset))
                {
                    prefabTypeCount[prefabAsset] = 0;
                }
                prefabTypeCount[prefabAsset]++;
            }
        }

        EditorUtility.ClearProgressBar();

        if (foundPrefabs.Count == 0)
        {
            EditorUtility.DisplayDialog("No Prefabs Found", "No prefab instances found in the selected parent hierarchy.", "OK");
        }

        Repaint();
    }

    private void CollectPrefabsRecursively(Transform parent, List<GameObject> prefabs)
    {
        // Check if the current object is a prefab instance
        GameObject prefabInstance = PrefabUtility.GetOutermostPrefabInstanceRoot(parent.gameObject);
        if (prefabInstance != null && prefabInstance == parent.gameObject)
        {
            prefabs.Add(parent.gameObject);
            return; // Don't process children of prefab instances
        }

        // Process children
        foreach (Transform child in parent)
        {
            CollectPrefabsRecursively(child, prefabs);
        }
    }

    private async void AdjustFoundPrefabs()
    {
        if (foundPrefabs.Count == 0) return;

        processingInProgress = true;
        totalPrefabs = foundPrefabs.Count;
        processedPrefabs = 0;

        // Only record undo operations if not in fast mode
        if (!fastMode)
        {
            Undo.RecordObjects(foundPrefabs.Select(p => p.transform).ToArray(), "Adjust Prefabs to Ground");
        }

        foreach (var prefab in foundPrefabs)
        {
            ProcessObject(prefab);
            processedPrefabs++;

            // Update progress every few objects
            if (processedPrefabs % 5 == 0)
            {
                EditorUtility.DisplayProgressBar(
                    "Adjusting Prefabs",
                    $"Processing prefab {processedPrefabs} of {totalPrefabs}",
                    (float)processedPrefabs / totalPrefabs
                );
                // tiny async pause so editor remains responsive
                await Task.Delay(1);
            }

            if (!fastMode && PrefabUtility.IsPartOfPrefabInstance(prefab))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(prefab);
            }
        }

        EditorUtility.ClearProgressBar();
        processingInProgress = false;
        SceneView.RepaintAll();
        Repaint();
    }

    private Vector3? GetLowestVertexWorldSpace(GameObject rootObject)
    {
        var lodGroups = rootObject.GetComponentsInChildren<LODGroup>(true);
        List<MeshFilter> meshFilters = new List<MeshFilter>();

        if (lodGroups.Length > 0)
        {
            // Collect from LOD0 of each LODGroup
            foreach (LODGroup lg in lodGroups)
            {
                var lods = lg.GetLODs();
                if (lods.Length > 0)
                {
                    // LOD0
                    var lod0 = lods[0];
                    foreach (var rend in lod0.renderers)
                    {
                        if (rend == null) continue;
                        MeshFilter mf = rend.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null)
                        {
                            meshFilters.Add(mf);
                        }
                    }
                }
            }
        }
        else
        {
            // No LODGroup => gather all MeshFilters
            var allMF = rootObject.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in allMF)
            {
                if (mf.sharedMesh != null)
                {
                    meshFilters.Add(mf);
                }
            }
        }

        if (meshFilters.Count == 0) return null;

        Vector3? lowestPoint = null;
        foreach (MeshFilter mf in meshFilters)
        {
            Mesh mesh = mf.sharedMesh;
            var vertices = mesh.vertices;
            foreach (var vert in vertices)
            {
                Vector3 worldV = mf.transform.TransformPoint(vert);
                if (!lowestPoint.HasValue || worldV.y < lowestPoint.Value.y)
                {
                    lowestPoint = worldV;
                }
            }
        }

        return lowestPoint;
    }

    private void ProcessObject(GameObject rootObject)
    {
        if (rootObject == null) return;

        // 1) Lift the entire object up before raycast if requested
        if (liftBeforeRaycast)
        {
            rootObject.transform.position += Vector3.up * 5f;
        }

        // 2) Now find the new lowest vertex
        Vector3? lowestPoint = GetLowestVertexWorldSpace(rootObject);
        if (!lowestPoint.HasValue)
        {
            if (debugMode)
            {
                Debug.LogError($"[MeshGroundAdjuster] No valid mesh found in {rootObject.name}");
            }
            return;
        }

        // 3) Debug lines for the lowest point
        if (debugMode)
        {
            // small cross at the lowest vertex
            Vector3 lp = lowestPoint.Value;
            float crossSize = 0.2f;
            UnityEngine.Debug.DrawLine(lp + Vector3.left * crossSize, lp + Vector3.right * crossSize, Color.cyan, 5f);
            UnityEngine.Debug.DrawLine(lp + Vector3.forward * crossSize, lp + Vector3.back * crossSize, Color.cyan, 5f);
            // line downward
            UnityEngine.Debug.DrawLine(lp, lp + Vector3.down * raycastDistance, Color.red, 5f);
        }

        // 4) Fire ray downward
        int layerMask = 1 << selectedLayer;
        if (Physics.Raycast(lowestPoint.Value, Vector3.down, out RaycastHit hit, raycastDistance, layerMask))
        {
            float sink = sinkValue;
            Vector3 desiredPositionForLowestVertex = hit.point + (Vector3.down * sink);
            Vector3 currentLowestVertexPos = lowestPoint.Value;

            // delta to move rootObject by
            Vector3 delta = desiredPositionForLowestVertex - currentLowestVertexPos;

            // Save original rotation
            Vector3 originalEuler = rootObject.transform.eulerAngles;

            // Move the entire object
            rootObject.transform.position += delta;

            // Debug lines for contact point
            if (debugMode)
            {
                UnityEngine.Debug.DrawLine(hit.point + Vector3.left * 0.2f, hit.point + Vector3.right * 0.2f, Color.green, 5f);
                UnityEngine.Debug.DrawRay(hit.point, hit.normal * 0.5f, Color.blue, 5f);
            }

            // Align to terrain normal if slope is within limit
            if (alignToNormal)
            {
                float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);
                if (debugMode)
                {
                    Debug.Log($"[MeshGroundAdjuster] {rootObject.name} slope angle = {slopeAngle:0.##}°");
                }

                if (slopeAngle <= maxSlopeAngle)
                {
                    Quaternion alignmentRotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
                    rootObject.transform.rotation = alignmentRotation * rootObject.transform.rotation;

                    // preserve original Y rotation only
                    Vector3 newEuler = rootObject.transform.eulerAngles;
                    rootObject.transform.eulerAngles = new Vector3(newEuler.x, originalEuler.y, newEuler.z);
                }
            }
        }
        else
        {
            Debug.LogWarning($"[MeshGroundAdjuster] No ground hit for {rootObject.name} using layer {LayerMask.LayerToName(selectedLayer)}");
        }
    }
}
