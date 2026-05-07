using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class Window_AudioManager : EditorWindow
{
    [MenuItem("Main/Audio")]
    public static void Open() => GetWindow<Window_AudioManager>("Audio Manager").Show();

    private const string DataResourcePath = "Assets/Resources/SO_AudioData.asset";
    private const string BgmEnumPath      = "Assets/01_Scripts/00_Main/AudioManager/BgmType.cs";
    private const string SfxEnumPath      = "Assets/01_Scripts/00_Main/AudioManager/SfxType.cs";

    private SO_AudioData _data;
    private Vector2      _scroll;
    private int          _tab;

    private readonly Dictionary<int, bool> _bgmFoldouts = new();
    private readonly Dictionary<int, bool> _sfxFoldouts = new();

    private static readonly string[] TabLabels = { "BGM", "SFX" };

    // ════════════════════════════════════════════════

    private void OnEnable()
    {
        _data = AssetDatabase.LoadAssetAtPath<SO_AudioData>(DataResourcePath);
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (_data == null)
        {
            EditorGUILayout.HelpBox("SO_AudioData가 없습니다. [Create] 버튼으로 생성하세요.", MessageType.Info);
            return;
        }

        _tab    = GUILayout.Toolbar(_tab, TabLabels);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        if (_tab == 0) DrawEntryList(_data.Bgm, _bgmFoldouts, "BGM");
        else           DrawEntryList(_data.Sfx, _sfxFoldouts, "SFX");

        EditorGUILayout.EndScrollView();

        if (GUI.changed) EditorUtility.SetDirty(_data);
    }

    // ── 툴바 ────────────────────────────────────────

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        var loaded = (SO_AudioData)EditorGUILayout.ObjectField(
            _data, typeof(SO_AudioData), false, GUILayout.Width(220));
        if (loaded != _data) _data = loaded;

        if (GUILayout.Button("Create", EditorStyles.toolbarButton, GUILayout.Width(60)))
            CreateDataAsset();

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Enum 생성", EditorStyles.toolbarButton, GUILayout.Width(80)))
            GenerateEnums();

        EditorGUILayout.EndHorizontal();
    }

    // ── 항목 목록 ────────────────────────────────────

    private void DrawEntryList(List<AudioEntry> list, Dictionary<int, bool> foldouts, string label)
    {
        EditorGUILayout.Space(4);

        int removeIdx = -1;

        for (int i = 0; i < list.Count; i++)
        {
            var entry = list[i];
            if (!foldouts.ContainsKey(i)) foldouts[i] = false;

            EditorGUILayout.BeginVertical("box");

            // ── 헤더 행 ──
            EditorGUILayout.BeginHorizontal();

            // foldoutHeader 스타일은 가로 전체를 차지해 클립 박스와 겹침 → foldout 스타일 사용
            float foldoutW = Mathf.Max(60f, position.width - 310f);
            Rect  foldoutRect = GUILayoutUtility.GetRect(foldoutW, EditorGUIUtility.singleLineHeight,
                EditorStyles.foldout, GUILayout.Width(foldoutW));
            foldouts[i] = EditorGUI.Foldout(foldoutRect, foldouts[i], $"[{i + 1}]  {entry.Name}", true, EditorStyles.foldout);

            // 클립 미리보기
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField(entry.Clip, typeof(AudioClip), false, GUILayout.Width(140));
            EditorGUI.EndDisabledGroup();

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("▲", GUILayout.Width(24)) && i > 0)            { (list[i], list[i - 1]) = (list[i - 1], list[i]); RefreshFoldouts(foldouts, list.Count); }
            if (GUILayout.Button("▼", GUILayout.Width(24)) && i < list.Count-1) { (list[i], list[i + 1]) = (list[i + 1], list[i]); RefreshFoldouts(foldouts, list.Count); }
            if (GUILayout.Button("X", GUILayout.Width(24))) removeIdx = i;

            EditorGUILayout.EndHorizontal();

            // ── 세부 설정 ──
            if (foldouts[i])
            {
                EditorGUI.indentLevel++;
                DrawEntry(entry);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        if (removeIdx >= 0)
        {
            list.RemoveAt(removeIdx);
            foldouts.Remove(removeIdx);
        }

        EditorGUILayout.Space(4);
        if (GUILayout.Button($"+ {label} 추가", GUILayout.Height(26)))
        {
            list.Add(new AudioEntry { Name = $"New{label}{list.Count + 1}" });
            foldouts[list.Count - 1] = true;
        }
    }

    private void DrawEntry(AudioEntry e)
    {
        e.Name    = EditorGUILayout.TextField("Name", e.Name);
        e.Clip    = (AudioClip)EditorGUILayout.ObjectField("Clip", e.Clip, typeof(AudioClip), false);
        e.Channel = (AudioChannelType)EditorGUILayout.EnumPopup("Channel", e.Channel);

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Playback", EditorStyles.boldLabel);
        e.Volume   = EditorGUILayout.Slider("Volume",   e.Volume,  0f, 1f);
        e.Pitch    = EditorGUILayout.Slider("Pitch",    e.Pitch,  -3f, 3f);
        e.Priority = EditorGUILayout.IntSlider("Priority", e.Priority, 0, 256);

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Loop", EditorStyles.boldLabel);
        e.Loop = EditorGUILayout.Toggle("Loop", e.Loop);
        if (e.Loop)
        {
            e.UseLoopPoint = EditorGUILayout.Toggle("Use Loop Point", e.UseLoopPoint);
            if (e.UseLoopPoint)
            {
                float clipLen = e.Clip != null ? e.Clip.length : 0f;
                e.LoopStartTime = EditorGUILayout.Slider("Loop Start (sec)", e.LoopStartTime, 0f, clipLen);
                if (e.Clip != null)
                    EditorGUILayout.HelpBox($"루프 구간: {e.LoopStartTime:F2}s → {clipLen:F2}s", MessageType.None);
            }
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Fade", EditorStyles.boldLabel);
        e.FadeInDuration  = EditorGUILayout.FloatField("Fade In (sec)",  e.FadeInDuration);
        e.FadeOutDuration = EditorGUILayout.FloatField("Fade Out (sec)", e.FadeOutDuration);

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("3D Spatial", EditorStyles.boldLabel);
        e.SpatialBlend = EditorGUILayout.Slider("Spatial Blend", e.SpatialBlend, 0f, 1f);
        if (e.SpatialBlend > 0f)
        {
            e.MinDistance = EditorGUILayout.FloatField("Min Distance", e.MinDistance);
            e.MaxDistance = EditorGUILayout.FloatField("Max Distance", e.MaxDistance);
            e.RolloffMode = (AudioRolloffMode)EditorGUILayout.EnumPopup("Rolloff", e.RolloffMode);
        }
    }

    // ── Enum 생성 ────────────────────────────────────

    private void GenerateEnums()
    {
        if (_data == null) return;

        WriteEnum(BgmEnumPath, "BgmType", _data.Bgm);
        WriteEnum(SfxEnumPath, "SfxType", _data.Sfx);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[AudioManager] BgmType.cs / SfxType.cs 생성 완료");
    }

    private static void WriteEnum(string path, string enumName, List<AudioEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Auto-generated by Window_AudioManager. Do not edit manually.");
        sb.AppendLine($"public enum {enumName}");
        sb.AppendLine("{");
        sb.AppendLine("    None = 0,");

        for (int i = 0; i < entries.Count; i++)
        {
            string name = entries[i].Name.Trim().Replace(" ", "_");
            if (!string.IsNullOrEmpty(name))
                sb.AppendLine($"    {name} = {i + 1},");
        }

        sb.AppendLine("}");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    // ── 유틸 ────────────────────────────────────────

    private void CreateDataAsset()
    {
        Directory.CreateDirectory("Assets/Resources");

        _data = CreateInstance<SO_AudioData>();
        AssetDatabase.CreateAsset(_data, DataResourcePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = _data;
        Debug.Log($"[AudioManager] SO_AudioData 생성: {DataResourcePath}");
    }

    private static void RefreshFoldouts(Dictionary<int, bool> foldouts, int count)
    {
        foldouts.Clear();
        for (int i = 0; i < count; i++) foldouts[i] = false;
    }
}
