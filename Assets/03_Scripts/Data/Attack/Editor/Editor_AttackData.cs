using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(SO_AttackData))]
public class Editor_AttackData : Editor
{
    private ReorderableList _list;
    private SerializedProperty _additionalHitsProp;

    private void OnEnable()
    {
        _additionalHitsProp = serializedObject.FindProperty("additionalHits");

        _list = new ReorderableList(serializedObject, _additionalHitsProp,
            draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true);

        _list.drawHeaderCallback = rect =>
            EditorGUI.LabelField(rect, "Additional Hits");

        _list.elementHeightCallback = index =>
            EditorGUI.GetPropertyHeight(_additionalHitsProp.GetArrayElementAtIndex(index), true) + 2f;

        _list.drawElementCallback = (rect, index, isActive, isFocused) =>
        {
            rect.y += 1f;
            EditorGUI.PropertyField(rect, _additionalHitsProp.GetArrayElementAtIndex(index), true);
        };

        _list.onAddCallback = list =>
        {
            int newIndex = list.serializedProperty.arraySize;
            list.serializedProperty.arraySize++;
            list.index = newIndex;
            CopySOValuesToHitResult(list.serializedProperty.GetArrayElementAtIndex(newIndex));
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty prop = serializedObject.GetIterator();
        prop.NextVisible(true);

        do
        {
            if (prop.propertyPath == "additionalHits")
            {
                EditorGUILayout.Space(2f);
                _list.DoLayoutList();
                continue;
            }

            using (new EditorGUI.DisabledScope(prop.propertyPath == "m_Script"))
                EditorGUILayout.PropertyField(prop, true);
        }
        while (prop.NextVisible(false));

        serializedObject.ApplyModifiedProperties();
    }

    private void CopySOValuesToHitResult(SerializedProperty element)
    {
        SO_AttackData so = (SO_AttackData)target;

        CopyStruct(element.FindPropertyRelative("shape"), so.Shape);
        CopyStruct(element.FindPropertyRelative("hitbox"), so.Hitbox);
        CopyStruct(element.FindPropertyRelative("repeat"), so.Repeat);

        SerializedProperty hr = element.FindPropertyRelative("hitResult");
        hr.FindPropertyRelative("damage").floatValue = so.Damage;
        hr.FindPropertyRelative("hitType").intValue  = (int)so.HitType;
        CopyStruct(hr.FindPropertyRelative("knockback"), so.Knockback);
        CopyStruct(hr.FindPropertyRelative("launch"), so.Launch);
        CopyStruct(hr.FindPropertyRelative("down"), so.Down);
    }

    private static void CopyStruct(SerializedProperty dst, AttackShapeData src)
    {
        dst.FindPropertyRelative("type").intValue     = (int)src.type;
        dst.FindPropertyRelative("radius").floatValue = src.radius;
        dst.FindPropertyRelative("angle").floatValue  = src.angle;
        dst.FindPropertyRelative("length").floatValue = src.length;
        dst.FindPropertyRelative("width").floatValue  = src.width;
    }

    private static void CopyStruct(SerializedProperty dst, AttackHitboxData src)
    {
        dst.FindPropertyRelative("timing").floatValue            = src.timing;
        dst.FindPropertyRelative("offset").floatValue            = src.offset;
        dst.FindPropertyRelative("yOffset").floatValue           = src.yOffset;
        dst.FindPropertyRelative("verticalTolerance").floatValue = src.verticalTolerance;
    }

    private static void CopyStruct(SerializedProperty dst, AttackRepeatData src)
    {
        dst.FindPropertyRelative("enabled").boolValue            = src.enabled;
        dst.FindPropertyRelative("interval").floatValue          = src.interval;
        dst.FindPropertyRelative("hitSameTargetOnce").boolValue  = src.hitSameTargetOnce;
        dst.FindPropertyRelative("cancelOnMiss").boolValue       = src.cancelOnMiss;
    }

    private static void CopyStruct(SerializedProperty dst, AttackKnockbackData src)
    {
        dst.FindPropertyRelative("type").intValue     = (int)src.type;
        dst.FindPropertyRelative("force").floatValue  = src.force;
        dst.FindPropertyRelative("duration").floatValue = src.duration;
        dst.FindPropertyRelative("friction").floatValue = src.friction;
    }

    private static void CopyStruct(SerializedProperty dst, AttackLaunchData src)
    {
        dst.FindPropertyRelative("enabled").boolValue          = src.enabled;
        dst.FindPropertyRelative("height").floatValue          = src.height;
        dst.FindPropertyRelative("duration").floatValue        = src.duration;
        dst.FindPropertyRelative("suspendDuration").floatValue = src.suspendDuration;
    }

    private static void CopyStruct(SerializedProperty dst, AttackDownData src)
    {
        dst.FindPropertyRelative("enabled").boolValue  = src.enabled;
        dst.FindPropertyRelative("duration").floatValue = src.duration;
    }
}
