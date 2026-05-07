using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;

public class Window_PackageInstaller : EditorWindow
{
    private const string LastUsedDBPath = "LastUsedPackageDBPath";

    private int _tab;
    private readonly string[] _tabLabels = { "패키지 목록", "즐겨찾기 (체크리스트)" };

    private SO_PackageData _db;
    private Editor         _dbEditor;

    private Vector2               _scroll;
    private Vector2               _favScroll;
    private string                _searchQuery      = "";
    private string                _selectedCategory = "전체";
    private readonly HashSet<int> _selected         = new HashSet<int>();
    private bool                  _showInspector;

    private string[]                          _cachedCategories;
    private List<(PackageEntry pkg, int idx)> _cachedFiltered;
    private string _lastSearch;
    private string _lastCategory;
    private int    _lastPackageCount = -1;

    private AddRequest           _addRequest;
    private readonly Queue<PackageEntry> _installQueue = new Queue<PackageEntry>();
    private PackageEntry         _currentInstalling;
    private string               _statusMessage = "";
    private bool                 _isInstalling;
    private UnityWebRequest      _downloadRequest;
    private EditorApplication.CallbackFunction _pollDownloadDelegate;

    private ListRequest     _listRequest;
    private HashSet<string> _installedIds   = new HashSet<string>();
    private HashSet<string> _installedNames = new HashSet<string>();
    private bool            _installedReady;

    private int _favEditIdx = -1;

    // Colors & Styles
    static readonly Color ColBg        = new Color(0.13f, 0.13f, 0.15f);
    static readonly Color ColCard      = new Color(0.18f, 0.18f, 0.21f);
    static readonly Color ColAccent    = new Color(0.29f, 0.56f, 1.00f);
    static readonly Color ColText      = new Color(0.90f, 0.90f, 0.93f);
    static readonly Color ColSubText   = new Color(0.55f, 0.55f, 0.60f);
    static readonly Color ColSuccess   = new Color(0.30f, 0.85f, 0.50f);
    static readonly Color ColWarning   = new Color(1.00f, 0.75f, 0.20f);
    static readonly Color ColTagUPM    = new Color(0.25f, 0.55f, 0.95f);
    static readonly Color ColTagOUPM   = new Color(0.55f, 0.30f, 0.90f);
    static readonly Color ColTagUPKG   = new Color(0.20f, 0.70f, 0.55f);
    static readonly Color ColInstalled = new Color(0.20f, 0.55f, 0.35f);

    GUIStyle _headerStyle, _cardStyle, _tagStyle, _catBtn, _catBtnActive;
    bool     _stylesReady;
    readonly List<Texture2D> _textures = new List<Texture2D>();

    [MenuItem("Main/Package Installer")]
    public static void ShowWindow() => GetWindow<Window_PackageInstaller>("Package Installer").minSize = new Vector2(600, 440);

    void OnEnable()
    {
        string saved = EditorPrefs.GetString(LastUsedDBPath, "");
        if (!string.IsNullOrEmpty(saved)) _db = AssetDatabase.LoadAssetAtPath<SO_PackageData>(saved);
        if (_db == null) TryAutoLoadDB();
        RefreshInstalledPackages();
    }

    void OnDisable()
    {
        EditorApplication.update -= PollUPM;
        EditorApplication.update -= PollInstalledList;
        if (_pollDownloadDelegate != null) EditorApplication.update -= _pollDownloadDelegate;
        _downloadRequest?.Dispose();
    }

    void TryAutoLoadDB()
    {
        var guids = AssetDatabase.FindAssets("t:SO_PackageData");
        if (guids.Length > 0)
        {
            _db = AssetDatabase.LoadAssetAtPath<SO_PackageData>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (_db != null) EditorPrefs.SetString(LastUsedDBPath, AssetDatabase.GetAssetPath(_db));
        }
    }

    void RefreshInstalledPackages()
    {
        _installedReady = false;
        _listRequest = Client.List();
        EditorApplication.update += PollInstalledList;
    }

    void PollInstalledList()
    {
        if (!_listRequest.IsCompleted) return;
        EditorApplication.update -= PollInstalledList;
        if (_listRequest.Status == StatusCode.Success)
        {
            _installedIds.Clear(); _installedNames.Clear();
            foreach (var pkg in _listRequest.Result)
            {
                _installedIds.Add(pkg.name);
                if (!string.IsNullOrEmpty(pkg.displayName)) _installedNames.Add(pkg.displayName.ToLower());
            }
        }
        _installedReady = true;
        Repaint();
    }

    bool IsInstalled(PackageEntry pkg)
    {
        if (!_installedReady) return false;
        if (pkg.type == PackageType.UnityPackage) return false;

        // 1. URL이 있으면 ID를 추측해서 비교
        if (!string.IsNullOrEmpty(pkg.url))
        {
            // 입력된 값이 이미 com.xxx.xxx 형태라면 바로 비교
            if (_installedIds.Contains(pkg.url)) return true;

            // Git URL에서 ID 추출 (예: .../com.unity.toonshader.git?path=... -> com.unity.toonshader)
            // 1) ? 뒤의 쿼리 제거
            string id = pkg.url.Split('?')[0];
            // 2) 마지막 / 뒤의 문자열 가져오기
            id = id.Split('/').Last();
            // 3) .git 확장자 제거
            id = id.Replace(".git", "");

            if (_installedIds.Contains(id)) return true;
        
            // UniTask 같은 특수한 케이스 (URL 끝이 /Assets/Plugins 등인 경우) 대응
            // URL 전체 문자열 중 설치된 ID가 포함되어 있는지 확인
            foreach (var installedId in _installedIds)
            {
                if (pkg.url.Contains(installedId)) return true;
            }
        }

        // 2. 이름(Name)으로 대조 (UI상 표시 이름과 비교)
        // UTS3라고 적으셨는데 실제 이름은 "Unity Toon Shader"일 수 있으니 
        // "포함" 여부로 체크하면 더 잘 잡힙니다.
        if (!string.IsNullOrEmpty(pkg.name))
        {
            string lowerName = pkg.name.ToLower().Replace(" ", "");
            foreach (var installedName in _installedNames)
            {
                if (installedName.Contains(lowerName) || lowerName.Contains(installedName)) 
                    return true;
            }
        }

        return false;
    }

    void SaveSO() { if (_db != null) EditorUtility.SetDirty(_db); }

    void OnGUI()
    {
        InitStyles();
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), ColBg);
        
        // Header
        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        GUILayout.Space(12);
        GUILayout.Label("Package Installer", _headerStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(_installedReady ? "↺" : "...", GUILayout.Width(30)) && _installedReady) RefreshInstalledPackages();
        
        if (_tab == 0)
        {
            var newDB = (SO_PackageData)EditorGUILayout.ObjectField(_db, typeof(SO_PackageData), false, GUILayout.Width(180));
            if (newDB != _db) { _db = newDB; if (_db != null) EditorPrefs.SetString(LastUsedDBPath, AssetDatabase.GetAssetPath(_db)); InvalidateCache(); }
            if (_db != null && GUILayout.Button(_showInspector ? "▲" : "✏", GUILayout.Width(30))) _showInspector = !_showInspector;
        }
        GUILayout.Space(12);
        GUILayout.EndHorizontal();

        // Tabs
        GUILayout.Space(4);
        GUILayout.BeginHorizontal(); GUILayout.Space(12);
        _tab = GUILayout.Toolbar(_tab, _tabLabels, GUILayout.Height(24));
        GUILayout.Space(12); GUILayout.EndHorizontal();

        if (_tab == 0) DrawPackageListTab();
        else DrawFavoritesTab();

        DrawBottomBar();
        if (_isInstalling || !_installedReady) Repaint();
    }

    void DrawPackageListTab()
    {
        if (_db == null) { GUILayout.Label("SO_PackageData를 연결하세요."); return; }
        if (_showInspector)
        {
            if (_dbEditor == null || _dbEditor.target != _db) _dbEditor = Editor.CreateEditor(_db);
            EditorGUI.BeginChangeCheck(); _dbEditor.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck()) { InvalidateCache(); SaveSO(); }
        }

        GUILayout.Space(5);
        GUILayout.BeginHorizontal(); GUILayout.Space(12);
        var newSearch = EditorGUILayout.TextField(_searchQuery, EditorStyles.toolbarSearchField);
        if (newSearch != _searchQuery) { _searchQuery = newSearch; InvalidateCache(); }
        GUILayout.Space(12); GUILayout.EndHorizontal();

        RefreshCacheIfNeeded();
        GUILayout.Space(5);
        GUILayout.BeginHorizontal(); GUILayout.Space(12);
        foreach (var cat in _cachedCategories)
        {
            if (GUILayout.Button(cat, cat == _selectedCategory ? _catBtnActive : _catBtn)) { _selectedCategory = cat; InvalidateCache(); }
            GUILayout.Space(3);
        }
        GUILayout.EndHorizontal();

        _scroll = GUILayout.BeginScrollView(_scroll);
        foreach (var (pkg, idx) in _cachedFiltered) DrawCard(pkg, idx);
        GUILayout.Space(52);
        GUILayout.EndScrollView();
    }

    void DrawFavoritesTab()
    {
        if (_db == null) { GUILayout.Label("SO_PackageData를 연결하세요."); return; }
        
        GUILayout.Space(10);
        _favScroll = GUILayout.BeginScrollView(_favScroll);

        int removeIdx = -1;
        for (int i = 0; i < _db.favorites.Count; i++)
        {
            var item = _db.favorites[i];
            bool editing = _favEditIdx == i;
            var savedBg = GUI.backgroundColor;
            GUI.backgroundColor = item.done ? new Color(0.2f, 0.4f, 0.2f) : (editing ? new Color(0.2f, 0.3f, 0.5f) : ColCard);
            
            GUILayout.BeginVertical(_cardStyle);
            GUI.backgroundColor = savedBg;
            GUILayout.BeginHorizontal();

            // 1. 체크박스
            bool newDone = EditorGUILayout.Toggle(item.done, GUILayout.Width(20));
            if (newDone != item.done) { item.done = newDone; SaveSO(); }

            if (editing)
            {
                EditorGUI.BeginChangeCheck();
                // 편집 모드: 이름(TextField) | 타입(EnumPopup) | 설명(TextField)
                item.name = EditorGUILayout.TextField(item.name, GUILayout.Width(140));
                item.registryType = (FavoriteRegistryType)EditorGUILayout.EnumPopup(item.registryType, GUILayout.Width(110));
                item.description = EditorGUILayout.TextField(item.description, GUILayout.ExpandWidth(true));
                if (EditorGUI.EndChangeCheck()) SaveSO();
            }
            else
            {
                // 일반 모드
                GUI.color = item.done ? ColSuccess : ColText;
                GUILayout.Label(item.name, EditorStyles.boldLabel, GUILayout.Width(140));
                
                GUI.color = ColSubText;
                // Enum 이름을 보기 좋게 출력
                GUILayout.Label($"[{item.registryType}]", GUILayout.Width(110));
                GUILayout.Label(item.description, EditorStyles.miniLabel);
                GUI.color = Color.white;
            }

            // 수정/삭제 버튼
            if (GUILayout.Button(editing ? "✓" : "✏", GUILayout.Width(25))) _favEditIdx = editing ? -1 : i;
            if (GUILayout.Button("✕", GUILayout.Width(25))) removeIdx = i;

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        if (removeIdx != -1) { _db.favorites.RemoveAt(removeIdx); _favEditIdx = -1; SaveSO(); }

        // 추가 버튼 부분 동일...
        GUILayout.Space(10);
        GUILayout.BeginHorizontal(); GUILayout.Space(12);
        if (GUILayout.Button("+ 새 즐겨찾기 추가", GUILayout.Height(30)))
        {
            _db.favorites.Add(new FavoriteItem { name = "New Package", registryType = FavoriteRegistryType.UnityRegistry });
            _favEditIdx = _db.favorites.Count - 1;
            SaveSO();
        }
        GUILayout.Space(12); GUILayout.EndHorizontal();
        
        GUILayout.Space(50);
        GUILayout.EndScrollView();
    }

    void DrawCard(PackageEntry pkg, int idx)
    {
        bool sel = _selected.Contains(idx);
        bool installed = IsInstalled(pkg);
        var savedBg = GUI.backgroundColor;

        GUILayout.BeginHorizontal(); GUILayout.Space(12);
        GUI.backgroundColor = sel ? new Color(0.25f, 0.35f, 0.5f) : ColCard;
        GUILayout.BeginVertical(_cardStyle);
        GUI.backgroundColor = savedBg;

        GUILayout.BeginHorizontal();
        bool newSel = EditorGUILayout.Toggle(sel, GUILayout.Width(18));
        if (newSel != sel) { if (newSel) _selected.Add(idx); else _selected.Remove(idx); }

        GUI.color = installed ? ColSuccess : ColText;
        GUILayout.Label(pkg.name, EditorStyles.boldLabel);
        GUI.color = Color.white;

        DrawTag(pkg.type);
        
        GUI.enabled = !_isInstalling && !installed;
        GUI.backgroundColor = installed ? ColInstalled : ColAccent;
        if (GUILayout.Button(installed ? "Installed" : "Install", GUILayout.Width(70))) EnqueueInstall(pkg);
        GUI.backgroundColor = savedBg; GUI.enabled = true;
        GUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(pkg.description))
        { GUI.color = ColSubText; GUILayout.Label(pkg.description, EditorStyles.wordWrappedMiniLabel); GUI.color = Color.white; }

        GUILayout.EndVertical();
        GUILayout.Space(12); GUILayout.EndHorizontal();
    }

    void DrawTag(PackageType t)
    {
        (Color col, string label) = t switch { PackageType.UPM => (ColTagUPM, "UPM"), PackageType.OpenUPM => (ColTagOUPM, "OpenUPM"), PackageType.UnityPackage => (ColTagUPKG, "UPKG"), _ => (ColSubText, "?") };
        var savedBg = GUI.backgroundColor; GUI.backgroundColor = col;
        GUILayout.Label(label, _tagStyle, GUILayout.Width(50), GUILayout.Height(18));
        GUI.backgroundColor = savedBg;
    }

    void DrawBottomBar()
    {
        float barH = 40;
        EditorGUI.DrawRect(new Rect(0, position.height - barH, position.width, barH), new Color(0.1f, 0.1f, 0.12f));
        GUILayout.BeginArea(new Rect(12, position.height - barH + 5, position.width - 24, barH));
        GUILayout.BeginHorizontal();
        if (_tab == 0 && _db != null)
        {
            if (GUILayout.Button("Select All", EditorStyles.miniButton, GUILayout.Width(70))) { for (int i = 0; i < _db.packages.Count; i++) _selected.Add(i); }
            if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(50))) _selected.Clear();
        }
        GUILayout.Label(_statusMessage, EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        if (_tab == 0 && _selected.Count > 0)
        {
            GUI.enabled = !_isInstalling;
            if (GUILayout.Button($"Install Selected ({_selected.Count})", GUILayout.Width(160), GUILayout.Height(26))) EnqueueSelected();
            GUI.enabled = true;
        }
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    // --- Cache & Logic (Same as original but with SaveSO) ---
    void RefreshCacheIfNeeded()
    {
        if (_db == null) return;
        if (_cachedCategories != null && _lastSearch == _searchQuery && _lastCategory == _selectedCategory && _lastPackageCount == _db.packages.Count) return;
        _lastSearch = _searchQuery; _lastCategory = _selectedCategory; _lastPackageCount = _db.packages.Count;
        _cachedCategories = new[] { "전체" }.Concat(_db.packages.Select(p => p.category).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c)).ToArray();
        string q = _searchQuery.ToLower();
        _cachedFiltered = _db.packages.Select((p, i) => (p, i)).Where(x => (_selectedCategory == "전체" || x.p.category == _selectedCategory) && (string.IsNullOrEmpty(q) || x.p.name.ToLower().Contains(q) || x.p.description.ToLower().Contains(q))).ToList();
    }

    void InvalidateCache() => _cachedCategories = null;

    void EnqueueInstall(PackageEntry pkg) { if (!_installQueue.Contains(pkg)) _installQueue.Enqueue(pkg); if (!_isInstalling) StartNext(); }
    void EnqueueSelected() { foreach (var i in _selected) EnqueueInstall(_db.packages[i]); }

    void StartNext()
    {
        if (_installQueue.Count == 0) { _isInstalling = false; _statusMessage = "✅ Done"; RefreshInstalledPackages(); return; }
        _currentInstalling = _installQueue.Dequeue(); _isInstalling = true;
        _statusMessage = $"Installing: {_currentInstalling.name}...";
        switch (_currentInstalling.type)
        {
            case PackageType.UPM: InstallUPM(_currentInstalling); break;
            case PackageType.OpenUPM: InstallOpenUPM(_currentInstalling); break;
            case PackageType.UnityPackage: DownloadUnityPackage(_currentInstalling); break;
        }
    }

    void InstallUPM(PackageEntry pkg) { string url = pkg.url; if (!string.IsNullOrEmpty(pkg.version) && !url.Contains("#")) url += $"#{pkg.version}"; _addRequest = Client.Add(url); EditorApplication.update += PollUPM; }
    void PollUPM() { if (!_addRequest.IsCompleted) return; EditorApplication.update -= PollUPM; _statusMessage = _addRequest.Status == StatusCode.Success ? $"✅ Success: {_currentInstalling.name}" : $"❌ Error: {_addRequest.Error.message}"; StartNext(); }

    void InstallOpenUPM(PackageEntry pkg)
    {
        string path = "Packages/manifest.json"; if (!File.Exists(path)) return;
        string json = File.ReadAllText(path);
        string pkgId = pkg.url; string version = string.IsNullOrEmpty(pkg.version) ? "latest" : pkg.version;
        string scope = pkgId.Contains('.') ? string.Join(".", pkgId.Split('.').Take(2)) : pkgId;
        if (!json.Contains("package.openupm.com")) json = json.Replace("\"dependencies\":", $"\"scopedRegistries\": [{{ \"name\": \"package.openupm.com\", \"url\": \"https://package.openupm.com\", \"scopes\": [\"{scope}\"] }}], \"dependencies\":");
        else json = Regex.Replace(json, @"(""name""\s*:\s*""package\.openupm\.com""[\s\S]*?""scopes""\s*:\s*\[)([^\]]*?)(\])", m => { string ex = m.Groups[2].Value; return ex.Contains($"\"{scope}\"") ? m.Value : $"{m.Groups[1].Value}{ex}{(ex.Trim().Length > 0 ? "," : "")}\"{scope}\"{m.Groups[3].Value}"; });
        if (!json.Contains($"\"{pkgId}\"")) json = json.Replace("\"dependencies\": {", $"\"dependencies\": {{ \"{pkgId}\": \"{version}\",");
        File.WriteAllText(path, json); AssetDatabase.Refresh(); StartNext();
    }

    void DownloadUnityPackage(PackageEntry pkg)
    {
        string savePath = Path.Combine(Application.temporaryCachePath, pkg.name + ".unitypackage");
        _downloadRequest = UnityWebRequest.Get(pkg.url); _downloadRequest.downloadHandler = new DownloadHandlerFile(savePath); _downloadRequest.SendWebRequest();
        _pollDownloadDelegate = () => { if (!_downloadRequest.isDone) return; EditorApplication.update -= _pollDownloadDelegate; if (_downloadRequest.result == UnityWebRequest.Result.Success) AssetDatabase.ImportPackage(savePath, true); _downloadRequest.Dispose(); StartNext(); };
        EditorApplication.update += _pollDownloadDelegate;
    }

    void InitStyles()
    {
        if (_stylesReady) return; _stylesReady = true;
        _headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 17, normal = { textColor = ColText } };
        _cardStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 5, 5), normal = { background = MakeTex(ColCard) } };
        _tagStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        _catBtn = new GUIStyle(GUI.skin.button) { normal = { textColor = ColSubText, background = MakeTex(ColCard) } };
        _catBtnActive = new GUIStyle(_catBtn) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white, background = MakeTex(ColAccent) } };
    }

    Texture2D MakeTex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); _textures.Add(t); return t; }
}