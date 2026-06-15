using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SO_Attack_Data))]
[CanEditMultipleObjects]
public class Editor_Attack_Data : Editor
{
    public override void OnInspectorGUI()
    {
        DrawSchemaSummary();
        DrawDefaultInspector();
    }

    private void DrawSchemaSummary()
    {
        if (targets.Length > 1)
            return;

        SO_Attack_Data attack = (SO_Attack_Data)target;
        bool hasNewSchema = attack.HasDeliveryEvents || attack.HasMovementEvents || attack.HasFeedbackEvents;
        if (hasNewSchema)
        {
            EditorGUILayout.HelpBox(
                $"Timeline schema active. deliveryEvents={attack.DeliveryEvents?.Length ?? 0}, " +
                $"movementEvents={attack.MovementEvents?.Length ?? 0}, feedbackEvents={attack.FeedbackEvents?.Length ?? 0}.",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("deliveryEvents is empty. Runtime requires at least one delivery event.", MessageType.Error);
        }

        if (GUILayout.Button("Open Report"))
            Window_AttackDataReport.Open();

        EditorGUILayout.Space(6f);
    }
}
