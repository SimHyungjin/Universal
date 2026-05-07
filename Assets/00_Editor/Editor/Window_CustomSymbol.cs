using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Build;

public class SymbolsWindow : EditorWindow
{
    private const string LastUsedSettingPath = "LastUsedSymbolSettingPath";
    
    [SerializeField] private SO_CustomSymbol _data;
    private Vector2 _scroll;
    private string _searchQuery = "";

    // 스타일 관련
    private static readonly Color ColBg     = new Color(0.13f, 0.13f, 0.15f);
    private static readonly Color ColCard   = new Color(0.18f, 0.18f, 0.21f);
    private static readonly Color ColAccent = new Color(0.29f, 0.56f, 1.00f);
    private static readonly Color ColSuccess = new Color(0.30f, 0.85f, 0.50f);
    
    private GUIStyle _cardStyle, _headerStyle, _tagStyle;
    private bool _stylesReady;

    [MenuItem("Main/Custom Symbols")]
    public static void Open() => GetWindow<SymbolsWindow>("Custom Symbols").minSize = new Vector2(500, 400);

    private void OnEnable()
    {
        string path = EditorPrefs.GetString(LastUsedSettingPath, "");
        if (!string.IsNullOrEmpty(path))
            _data = AssetDatabase.LoadAssetAtPath<SO_CustomSymbol>(path);
    }

    private void InitStyles()
    {
        if (_stylesReady) return;
        _stylesReady = true;

        _headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, normal = { textColor = Color.white } };
        _cardStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 8), margin = new RectOffset(4, 4, 4, 4) };
        _tagStyle = new GUIStyle(EditorStyles.miniLabel) 
        { 
            alignment = TextAnchor.MiddleCenter, 
            normal = { textColor = Color.white, background = MakeTex(new Color(0.25f, 0.25f, 0.25f)) } 
        };
    }

    private Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    private void OnGUI()
    {
        InitStyles();
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), ColBg);

        DrawTopToolbar();
        
        if (_data == null)
        {
            GUILayout.Space(20);
            EditorGUILayout.HelpBox("SO_CustomSymbol 파일을 선택하거나 새로 생성해주세요.", MessageType.Info);
            return;
        }

        // 검색창
        GUILayout.BeginHorizontal(); GUILayout.Space(12);
        _searchQuery = EditorGUILayout.TextField(_searchQuery, EditorStyles.toolbarSearchField);
        GUILayout.Space(12); GUILayout.EndHorizontal();

        GUILayout.Space(10);
        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUIStyle.none, GUI.skin.verticalScrollbar);
        DrawSymbolCards();
        GUILayout.Space(60);
        EditorGUILayout.EndScrollView();

        DrawBottomBar();

        if (GUI.changed && _data != null) EditorUtility.SetDirty(_data);
    }

    private void DrawTopToolbar()
    {
        GUILayout.Space(10);
        GUILayout.BeginHorizontal(); GUILayout.Space(12);
        GUILayout.Label("Custom Symbols", _headerStyle);
        GUILayout.FlexibleSpace();
        
        var newData = (SO_CustomSymbol)EditorGUILayout.ObjectField(_data, typeof(SO_CustomSymbol), false, GUILayout.Width(200));
        if (newData != _data)
        {
            _data = newData;
            if (_data != null) EditorPrefs.SetString(LastUsedSettingPath, AssetDatabase.GetAssetPath(_data));
        }
        if (GUILayout.Button("New", GUILayout.Width(50))) CreateNewSo();
        GUILayout.Space(12);
        GUILayout.EndHorizontal();
        GUILayout.Space(5);
    }

    private void DrawSymbolCards()
    {
        int removeIdx = -1;
        var list = _data.Symbols;

        for (int i = 0; i < list.Count; i++)
        {
            var entry = list[i];
            if (!string.IsNullOrEmpty(_searchQuery) && !entry.Name.ToLower().Contains(_searchQuery.ToLower())) continue;

            var savedBg = GUI.backgroundColor;
            GUI.backgroundColor = entry.IncludeInBuild ? new Color(0.2f, 0.3f, 0.45f) : ColCard;
            
            GUILayout.BeginVertical(_cardStyle);
            GUI.backgroundColor = savedBg;

            // 상단 라인: 체크박스 + 이름 + 삭제버튼
            GUILayout.BeginHorizontal();
            entry.IncludeInBuild = EditorGUILayout.Toggle(entry.IncludeInBuild, GUILayout.Width(20));
            
            GUI.color = entry.IncludeInBuild ? Color.white : Color.gray;
            entry.Name = EditorGUILayout.TextField(entry.Name, EditorStyles.boldLabel, GUILayout.Width(200));
            GUI.color = Color.white;

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", GUILayout.Width(20))) removeIdx = i;
            GUILayout.EndHorizontal();

            // 중단 라인: 설명
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            entry.Description = EditorGUILayout.TextField(entry.Description, EditorStyles.miniTextField);
            GUI.color = Color.white;

            // 하단 라인: 플랫폼 선택 태그들
            GUILayout.BeginHorizontal();
            entry.AllPlatforms = GUILayout.Toggle(entry.AllPlatforms, " All Platforms ", "Button", GUILayout.Height(18));
            
            if (!entry.AllPlatforms)
            {
                GUILayout.Space(10);
                entry.Window = DrawPlatformTag("Win", entry.Window);
                entry.Mac = DrawPlatformTag("Mac", entry.Mac);
                entry.Android = DrawPlatformTag("AOS", entry.Android);
                entry.Ios = DrawPlatformTag("iOS", entry.Ios);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        if (removeIdx != -1) list.RemoveAt(removeIdx);

        GUILayout.Space(10);
        GUILayout.BeginHorizontal(); GUILayout.Space(12);
        if (GUILayout.Button("+ Add New Symbol", GUILayout.Height(30)))
            _data.Symbols.Add(new SO_CustomSymbol.CustomSymbolEntry { Name = "NEW_SYMBOL", AllPlatforms = true });
        GUILayout.Space(12); GUILayout.EndHorizontal();
    }

    private bool DrawPlatformTag(string label, bool value)
    {
        var style = new GUIStyle(GUI.skin.button) { fontSize = 10 };
        var savedCol = GUI.backgroundColor;
        if (value) GUI.backgroundColor = ColAccent;
        
        bool result = GUILayout.Toggle(value, label, style, GUILayout.Width(40), GUILayout.Height(18));
        GUI.backgroundColor = savedCol;
        return result;
    }

    private void DrawBottomBar()
    {
        float barH = 45;
        Rect barRect = new Rect(0, position.height - barH, position.width, barH);
        EditorGUI.DrawRect(barRect, new Color(0.1f, 0.1f, 0.12f));

        GUILayout.BeginArea(new Rect(12, position.height - barH + 8, position.width - 24, barH));
        GUILayout.BeginHorizontal();
        
        if (GUILayout.Button("현재 심볼 가져오기", GUILayout.Width(130), GUILayout.Height(28))) ImportCurrentSymbols();
        GUILayout.FlexibleSpace();
        
        GUI.backgroundColor = ColSuccess;
        if (GUILayout.Button("심볼 강제 적용 (Sync)", GUILayout.Width(150), GUILayout.Height(28))) ApplySymbols();
        GUI.backgroundColor = Color.white;

        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    // --- 기능 로직 ---

    private void CreateNewSo()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Save New Custom Symbol SO",
            "SO_CustomSymbol",
            "asset",
            "심볼 설정 파일을 저장할 위치를 선택하세요."
        );

        if (string.IsNullOrEmpty(path)) return;

        _data = CreateInstance<SO_CustomSymbol>();
        AssetDatabase.CreateAsset(_data, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorPrefs.SetString(LastUsedSettingPath, path);
        Selection.activeObject = _data;
    
        Debug.Log($"[Symbols] 새 설정 파일이 생성되었습니다: {path}");
    }

    private void ImportCurrentSymbols()
    {
        if (_data == null) return;
    
        var buildTarget = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
    
        string currentSymbols = PlayerSettings.GetScriptingDefineSymbols(buildTarget);
    
        var symbolList = currentSymbols.Split(';').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s));

        foreach (var sym in symbolList)
        {
            if (_data.Symbols.All(s => s.Name != sym))
            {
                _data.Symbols.Add(new SO_CustomSymbol.CustomSymbolEntry { 
                    Name = sym, 
                    IncludeInBuild = true, 
                    AllPlatforms = true 
                });
            }
        }
        Debug.Log("현재 프로젝트의 심볼을 성공적으로 가져왔습니다.");
    }

    private void ApplySymbols()
    {
        if (_data == null) return;
    
        // 1. 현재 활성화된 타겟 정보 가져오기
        BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
        var buildTarget = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));
    
        // 2. 새로운 심볼 문자열 생성 및 적용
        string newSymbols = _data.GetCombinedSymbols(target);
        PlayerSettings.SetScriptingDefineSymbols(buildTarget, newSymbols);
    
        Debug.Log($"[{target}] 플랫폼에 심볼이 적용되었습니다: {newSymbols}");
    }
}