using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class FastTerrainShaderGUI : ShaderGUI
{
    private Dictionary<string, MaterialProperty> properties = new Dictionary<string, MaterialProperty>();
    private bool isUnlit = false;
    private bool isURP = false;

    private MaterialProperty SafeFindProperty(string name, MaterialProperty[] properties)
    {
        return FindProperty(name, properties, false);
    }

    private void DrawPropertyIfExists(MaterialEditor editor, string name, string displayName = null)
    {
        if (properties.ContainsKey(name))
        {
            editor.ShaderProperty(properties[name], displayName ?? name);
        }
    }

    private void DrawTexturePropertyIfExists(MaterialEditor editor, string name, string displayName = null, bool showScaleOffset = true)
    {
        if (properties.ContainsKey(name))
        {
            editor.TexturePropertySingleLine(new GUIContent(displayName ?? name), properties[name]);
            if (showScaleOffset)
            {
                editor.TextureScaleOffsetProperty(properties[name]);
            }
        }
    }

    private void DrawHeaderWithKoFiButton(string header)
    {
        EditorGUILayout.BeginHorizontal();

        // Header with larger text
        GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel);
        headerStyle.fontSize = 16;
        EditorGUILayout.LabelField(header, headerStyle);

        // Ko-fi button
        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.normal.textColor = new Color(0.2f, 0.6f, 1.0f); // Ko-fi blue color
        if (GUILayout.Button("Buy me a Ko-fi :)", buttonStyle, GUILayout.Width(120)))
        {
            Application.OpenURL("https://ko-fi.com/roundy");
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();
    }

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props)
    {
        Material material = materialEditor.target as Material;
        isUnlit = material.shader.name.Contains("Unlit");
        isURP = material.shader.name.Contains("URP");

        properties.Clear();
        foreach (var prop in props)
        {
            properties[prop.name] = prop;
        }

        EditorGUILayout.Space();
        DrawHeaderWithKoFiButton("Fast Terrain Shader");
        EditorGUILayout.Space();

        // Feature Toggles
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        if (!isUnlit) DrawPropertyIfExists(materialEditor, "_EnableNormalMaps", "Enable Normal Maps");
        DrawPropertyIfExists(materialEditor, "_EnableSmoothnessFlag", "Enable Smoothness");
        DrawPropertyIfExists(materialEditor, "_EnableResampling", "Enable Resampling");
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();

        // Main Properties
        EditorGUILayout.LabelField("Main Properties", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        DrawTexturePropertyIfExists(materialEditor, "_SplatTex", "Splat Map", false);
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();

        // Textures and Normal Maps
        EditorGUILayout.LabelField("Texture Properties", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        bool showNormalMaps = !isUnlit &&
            (!properties.ContainsKey("_EnableNormalMaps") || properties["_EnableNormalMaps"].floatValue > 0.5f);
        bool showSmoothness = properties.ContainsKey("_EnableSmoothnessFlag") &&
            properties["_EnableSmoothnessFlag"].floatValue > 0.5f;

        for (int i = 0; i < 4; i++)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Main texture
            string texName = $"_MainTex{i}";
            DrawTexturePropertyIfExists(materialEditor, texName, $"Texture {i}");

            // Normal map (if enabled, right under its corresponding diffuse)
            if (showNormalMaps)
            {
                EditorGUI.indentLevel++;
                DrawTexturePropertyIfExists(materialEditor, $"_BumpMap{i}", $"Normal Map", false);
                EditorGUI.indentLevel--;
            }

            // Per-texture properties for unlit shader
            if (isUnlit && showSmoothness)
            {
                EditorGUI.indentLevel++;
                DrawPropertyIfExists(materialEditor, $"_Texture{i + 1}", $"Smoothness Strength");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();

        // Tint Colors
        EditorGUILayout.LabelField("Tint Colors", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        for (int i = 0; i < 4; i++)
        {
            DrawPropertyIfExists(materialEditor, $"_TintColor{i}", $"Tint Color {i}");
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();

        // Blending Properties
        EditorGUILayout.LabelField("Blending Properties", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        DrawPropertyIfExists(materialEditor, "_HeightBlendDistance", "Height Blend Distance");
        DrawPropertyIfExists(materialEditor, "_HeightBlendStrength", "Height Blend Strength");
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();

        // Smoothness Properties
        if (showSmoothness)
        {
            if (!isUnlit)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawPropertyIfExists(materialEditor, "_SmoothnessStrength", "Smoothness Strength");
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
            else
            {
                EditorGUILayout.LabelField("Smoothness Effects", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                DrawPropertyIfExists(materialEditor, "_SmoothnessColor", "Smoothness Color");
                DrawPropertyIfExists(materialEditor, "_FresnelPower", "Fresnel Power");
                DrawPropertyIfExists(materialEditor, "_FresnelIntensity", "Fresnel Intensity");
                DrawPropertyIfExists(materialEditor, "_SpecularPower", "Specular Power");
                DrawPropertyIfExists(materialEditor, "_SpecularIntensity", "Specular Intensity");
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
        }

        // Resampling Properties
        bool showResampling = properties.ContainsKey("_EnableResampling") &&
            properties["_EnableResampling"].floatValue > 0.5f;

        if (showResampling)
        {
            EditorGUILayout.LabelField("Resampling Properties", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawPropertyIfExists(materialEditor, "_ResampleDistance", "Distance (Start, End)");
            DrawPropertyIfExists(materialEditor, "_ResampleTiling", "Tiling");
            EditorGUILayout.EndVertical();
        }

        // URP-specific properties
        if (isURP)
        {
            EditorGUILayout.Space();
            materialEditor.RenderQueueField();
            materialEditor.EnableInstancingField();
            materialEditor.DoubleSidedGIField();
        }
    }
}