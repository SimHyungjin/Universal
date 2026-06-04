using UnityEditor;
using UnityEngine;

public class Window_MoveSelected : EditorWindow
{
    private Vector3 offset;

    [MenuItem("Main/Model/Move Selected")]
    private static void Open()
    {
        GetWindow<Window_MoveSelected>("Move Selected");
    }

    private void OnGUI()
    {
        GUILayout.Label("Move Selected Objects", EditorStyles.boldLabel);

        offset = EditorGUILayout.Vector3Field("Offset", offset);

        GUILayout.Space(10);

        if (GUILayout.Button("Move"))
        {
            foreach (GameObject go in Selection.gameObjects)
            {
                Undo.RecordObject(go.transform, "Move Objects");

                go.transform.position += offset;
            }
        }
    }
}