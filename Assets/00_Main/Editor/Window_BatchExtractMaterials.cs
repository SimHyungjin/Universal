using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class Window_BatchExtractMaterials : EditorWindow
{
    [MenuItem("Main/Batch Extract Materials from FBX")]
    public static void ShowWindow()
    {
        GetWindow<Window_BatchExtractMaterials>("Batch Extract Materials");
    }

    private string _materialFolderPath = "Assets/Materials";
    private bool _useFBXLocationFolder = false;
    private string _subFolderName = "Materials";
    private bool _createSubfolderPerFBX = false;
    private Vector2 _scrollPos;

    private void OnGUI()
    {
        // 상단 타이틀 및 스타일 설정
        EditorGUILayout.Space(10);
        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
        EditorGUILayout.LabelField("📦 FBX 머티리얼 일괄 추출", titleStyle);
        EditorGUILayout.Space(5);

        // --- [설정 섹션] ---
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("추출 설정", EditorStyles.miniBoldLabel);
        EditorGUILayout.Space(2);

        // 1. 공통 옵션: 별도 폴더 생성 여부를 상단으로 배치
        _createSubfolderPerFBX = EditorGUILayout.Toggle(
            new GUIContent("FBX마다 개별 폴더 생성", "체크 시 각 FBX 이름으로 된 하위 폴더 안에 머티리얼이 저장됩니다."), 
            _createSubfolderPerFBX);
        
        // 2. 위치 모드 선택
        _useFBXLocationFolder = EditorGUILayout.Toggle(
            new GUIContent("FBX 파일 위치에 저장", "체크 시 FBX가 있는 폴더와 같은 위치에 저장됩니다."), 
            _useFBXLocationFolder);

        EditorGUILayout.Space(5);
        EditorGUILayout.EndVertical();

        // --- [경로 상세 섹션] ---
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        if (_useFBXLocationFolder)
        {
            EditorGUILayout.LabelField("저장 경로 구성", EditorStyles.miniBoldLabel);
            _subFolderName = EditorGUILayout.TextField("하위 폴더 이름", _subFolderName);

            string examplePath = _createSubfolderPerFBX 
                ? $"Assets/.../폴더/{_subFolderName}/[FBX이름]/머티리얼.mat"
                : $"Assets/.../폴더/{_subFolderName}/머티리얼.mat";
            
            EditorGUILayout.HelpBox($"예시: {examplePath}", MessageType.None);
        }
        else
        {
            EditorGUILayout.LabelField("공통 저장 폴더", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            _materialFolderPath = EditorGUILayout.TextField(_materialFolderPath);
            if (GUILayout.Button("탐색...", GUILayout.Width(60)))
                BrowseFolder();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_materialFolderPath))
            {
                if (AssetDatabase.IsValidFolder(_materialFolderPath))
                    EditorGUILayout.LabelField(" ✓ 유효한 폴더입니다.", EditorStyles.miniLabel);
                else
                    EditorGUILayout.HelpBox("⚠ 폴더가 존재하지 않습니다. 추출 시 생성됩니다.", MessageType.Warning);
            }
        }
        EditorGUILayout.EndVertical();

        // --- [실행 섹션] ---
        EditorGUILayout.Space(10);
        var fbxList = GetSelectedFBXPaths();
        bool hasSelection = fbxList.Count > 0;

        GUI.enabled = hasSelection; // 선택된 파일이 없을 때 버튼 비활성화
        Color guiColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.3f, 0.85f, 0.5f); // 추출 버튼에 강조 색상
        if (GUILayout.Button($"선택된 {fbxList.Count}개 FBX에서 머티리얼 추출", GUILayout.Height(45)))
        {
            ExtractMaterialsFromSelection();
        }
        GUI.backgroundColor = guiColor;
        GUI.enabled = true;

        // --- [선택 목록 섹션] ---
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField($"선택된 FBX 목록", EditorStyles.boldLabel);
        
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, EditorStyles.textArea, GUILayout.Height(150));
        if (hasSelection)
        {
            foreach (var path in fbxList)
                EditorGUILayout.LabelField("• " + path, EditorStyles.miniLabel);
        }
        else
        {
            EditorGUILayout.LabelField("Project 창에서 FBX 파일들을 선택해주세요.", EditorStyles.centeredGreyMiniLabel);
        }
        EditorGUILayout.EndScrollView();
    }

    private void BrowseFolder()
    {
        string startPath = string.IsNullOrEmpty(_materialFolderPath)
            ? Application.dataPath
            : Path.GetFullPath(_materialFolderPath);

        string selectedPath = EditorUtility.OpenFolderPanel(
            "머티리얼을 저장할 폴더 선택", startPath, "");

        if (string.IsNullOrEmpty(selectedPath)) return;

        string projectPath = Path.GetFullPath(Application.dataPath).Replace("\\", "/");
        string normalizedSelected = selectedPath.Replace("\\", "/");

        if (!normalizedSelected.StartsWith(projectPath))
        {
            EditorUtility.DisplayDialog(
                "잘못된 경로",
                "프로젝트의 Assets 폴더 내부에 있는 폴더를 선택해야 합니다.",
                "확인");
            return;
        }

        _materialFolderPath = "Assets" + normalizedSelected.Substring(projectPath.Length);
    }

    private List<string> GetSelectedFBXPaths()
    {
        List<string> paths = new List<string>();
    
        // Selection.GetFiltered를 사용하면 선택된 대상 중 특정 타입만 정밀하게 걸러낼 수 있습니다.
        // DefaultAsset은 폴더를 포함하며, GameObject는 FBX 프리팹 등을 포함합니다.
        Object[] selectedObjects = Selection.GetFiltered<Object>(SelectionMode.DeepAssets);

        foreach (var obj in selectedObjects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
        
            if (string.IsNullOrEmpty(path)) continue;

            // 확장자가 .fbx인 파일만 리스트에 추가
            if (path.ToLower().EndsWith(".fbx"))
            {
                // 중복 경로 방지
                if (!paths.Contains(path))
                {
                    paths.Add(path);
                }
            }
        }
        return paths;
    }

    private void ExtractMaterialsFromSelection()
    {
        var fbxPaths = GetSelectedFBXPaths();

        if (fbxPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("알림", "선택된 FBX 파일이 없습니다.", "확인");
            return;
        }

        if (!_useFBXLocationFolder && string.IsNullOrEmpty(_materialFolderPath))
        {
            EditorUtility.DisplayDialog("알림", "머티리얼 저장 폴더를 선택해 주세요.", "확인");
            return;
        }

        int totalExtracted = 0;
        int totalRemapped = 0;

        try
        {
            // ====================================================================
            // PASS 1: 모든 FBX를 순회하며 필요한 머티리얼만 추출 (StartAssetEditing 사용 안 함)
            // ====================================================================
            for (int i = 0; i < fbxPaths.Count; i++)
            {
                string fbxPath = fbxPaths[i];
                string fbxName = Path.GetFileNameWithoutExtension(fbxPath);

                EditorUtility.DisplayProgressBar(
                    "[1/2] 머티리얼 추출 중",
                    $"({i + 1}/{fbxPaths.Count}) {fbxName}",
                    (float)i / fbxPaths.Count);

                string materialDir = ResolveMaterialDirectory(fbxPath, fbxName);
                EnsureFolderExists(materialDir);

                int extracted = ExtractUniqueMaterials(fbxPath, materialDir);
                totalExtracted += extracted;
            }

            // 1차 추출 결과를 AssetDatabase에 반영
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ====================================================================
            // PASS 2: 모든 FBX를 다시 순회하며 추출된 머티리얼을 리매핑
            // ====================================================================
            for (int i = 0; i < fbxPaths.Count; i++)
            {
                string fbxPath = fbxPaths[i];
                string fbxName = Path.GetFileNameWithoutExtension(fbxPath);

                EditorUtility.DisplayProgressBar(
                    "[2/2] 머티리얼 재연결 중",
                    $"({i + 1}/{fbxPaths.Count}) {fbxName}",
                    (float)i / fbxPaths.Count);

                string materialDir = ResolveMaterialDirectory(fbxPath, fbxName);

                int remapped = RemapMaterials(fbxPath, materialDir);
                totalRemapped += remapped;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "완료",
            $"{fbxPaths.Count}개의 FBX 처리 완료\n" +
            $"• 새로 추출된 머티리얼: {totalExtracted}개\n" +
            $"• 머티리얼 슬롯 재연결: {totalRemapped}개",
            "확인");
    }

    private string ResolveMaterialDirectory(string fbxPath, string fbxName)
    {
        string materialDir;

        if (_useFBXLocationFolder)
        {
            string fbxDir = Path.GetDirectoryName(fbxPath);
            if (_createSubfolderPerFBX)
                materialDir = Path.Combine(fbxDir, fbxName + "_" + _subFolderName);
            else
                materialDir = Path.Combine(fbxDir, _subFolderName);
        }
        else
        {
            if (_createSubfolderPerFBX)
                materialDir = Path.Combine(_materialFolderPath, fbxName);
            else
                materialDir = _materialFolderPath;
        }

        return materialDir.Replace("\\", "/");
    }

    private void EnsureFolderExists(string folderPath)
    {
        // AssetDatabase가 아닌 실제 파일시스템 기준으로 체크 (StartAssetEditing 영향 회피)
        if (Directory.Exists(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!Directory.Exists(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    /// <summary>
    /// PASS 1: 대상 폴더에 아직 없는 머티리얼만 추출 (이미 있으면 건너뜀)
    /// </summary>
    private int ExtractUniqueMaterials(string fbxPath, string destinationPath)
    {
        int extractedCount = 0;
        var allAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);

        foreach (var asset in allAssets)
        {
            if (!(asset is Material material)) continue;

            string targetPath = Path.Combine(destinationPath, material.name + ".mat").Replace("\\", "/");

            // 파일시스템 기준으로 체크 (이미 추출된 머티리얼은 건너뜀 → 공유)
            if (File.Exists(targetPath))
                continue;

            string error = AssetDatabase.ExtractAsset(material, targetPath);

            if (!string.IsNullOrEmpty(error))
                Debug.LogWarning($"[{fbxPath}] '{material.name}' 추출 실패: {error}");
            else
                extractedCount++;
        }

        return extractedCount;
    }

    /// <summary>
    /// PASS 2: 대상 폴더에 있는 머티리얼들을 FBX의 슬롯에 리매핑
    /// </summary>
    private int RemapMaterials(string fbxPath, string destinationPath)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogWarning($"ModelImporter를 가져올 수 없음: {fbxPath}");
            return 0;
        }

        int remappedCount = 0;
        var allAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);

        foreach (var asset in allAssets)
        {
            if (!(asset is Material material)) continue;

            string targetPath = Path.Combine(destinationPath, material.name + ".mat").Replace("\\", "/");
            Material extracted = AssetDatabase.LoadAssetAtPath<Material>(targetPath);

            if (extracted == null)
            {
                Debug.LogWarning($"추출된 머티리얼을 찾지 못함: {targetPath}");
                continue;
            }

            // 이미 외부 머티리얼로 잘 연결돼 있으면 건너뜀
            if (material == extracted) continue;

            var identifier = new AssetImporter.SourceAssetIdentifier(typeof(Material), material.name);
            importer.AddRemap(identifier, extracted);
            remappedCount++;
        }

        if (remappedCount > 0)
        {
            importer.SaveAndReimport();
        }

        return remappedCount;
    }
}