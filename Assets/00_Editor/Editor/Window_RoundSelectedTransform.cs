using UnityEditor;
using UnityEngine;

public static class Window_RoundSelectedTransform
{
    private const string MenuPath = "Main/Model/Round Selected Transform";

    [MenuItem(MenuPath, true)]
    private static bool CanRoundSelectedTransform()
    {
        return Selection.transforms.Length > 0;
    }

    [MenuItem(MenuPath)]
    private static void RoundSelectedTransform()
    {
        Transform[] transforms = Selection.transforms;

        for (int i = 0; i < transforms.Length; i++)
        {
            Transform target = transforms[i];
            Undo.RecordObject(target, "Round Selected Transform");

            target.localPosition = Round(target.localPosition);
            target.localEulerAngles = Round(target.localEulerAngles);
            target.localScale = Round(target.localScale);

            EditorUtility.SetDirty(target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }
    }

    private static Vector3 Round(Vector3 value)
    {
        return new Vector3(
            Mathf.Round(value.x),
            Mathf.Round(value.y),
            Mathf.Round(value.z)
        );
    }
}
