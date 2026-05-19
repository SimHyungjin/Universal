using System.IO;
using UnityEditor;
using UnityEngine;

public static class Window_AnimationInPlace
{
    private const string MenuPath = "Main/Model/AnimationInPlace";

    [MenuItem(MenuPath, true)]
    private static bool CanCreateInPlaceCopies()
    {
        Object[] selection = Selection.objects;
        for (int i = 0; i < selection.Length; i++)
        {
            if (selection[i] is AnimationClip && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(selection[i])))
                return true;
        }
        return false;
    }

    [MenuItem(MenuPath)]
    private static void CreateInPlaceCopies()
    {
        int created = 0;
        Object[] selection = Selection.objects;

        for (int i = 0; i < selection.Length; i++)
        {
            if (selection[i] is not AnimationClip clip)
                continue;

            string sourcePath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(sourcePath) || !sourcePath.EndsWith(".anim"))
            {
                Debug.LogWarning($"Skip {clip.name}: only standalone .anim clips can be copied in-place.");
                continue;
            }

            string directory = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
            string fileName = Path.GetFileNameWithoutExtension(sourcePath);
            string copyPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{fileName}_InPlace.anim");

            if (!AssetDatabase.CopyAsset(sourcePath, copyPath))
            {
                Debug.LogWarning($"Failed to copy animation clip: {sourcePath}");
                continue;
            }

            AnimationClip copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(copyPath);
            if (copy == null)
            {
                Debug.LogWarning($"Failed to load copied animation clip: {copyPath}");
                continue;
            }

            int flattened = FlattenPlanarRootCurves(copy);
            Selection.activeObject = copy;
            Debug.Log($"Created in-place clip: {copyPath} (flattened {flattened} root XZ curves)", copy);
            created++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Created {created} in-place animation clip copy/copies.");
    }

    private static int FlattenPlanarRootCurves(AnimationClip clip)
    {
        int count = 0;
        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);

        for (int i = 0; i < bindings.Length; i++)
        {
            EditorCurveBinding binding = bindings[i];
            if (!IsPlanarRootCurve(binding.propertyName))
                continue;

            AnimationCurve source = AnimationUtility.GetEditorCurve(clip, binding);
            if (source == null)
                continue;

            Keyframe[] keys = source.keys;
            for (int k = 0; k < keys.Length; k++)
            {
                keys[k].value = 0f;
                keys[k].inTangent = 0f;
                keys[k].outTangent = 0f;
            }

            AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(keys));
            count++;
        }

        EditorUtility.SetDirty(clip);
        return count;
    }

    private static bool IsPlanarRootCurve(string propertyName)
        => propertyName == "RootT.x"
        || propertyName == "RootT.z"
        || propertyName == "MotionT.x"
        || propertyName == "MotionT.z";
}
