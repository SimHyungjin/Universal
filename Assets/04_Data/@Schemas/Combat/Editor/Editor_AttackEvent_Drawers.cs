using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// AttackDeliveryEvent / AttackMovementEvent는 type에 따라 쓰는 필드가 달라지는 union이다.
// 각 드로어가 type별로 "그릴 필드 이름 목록"만 반환하면 공용 유틸이 폴드아웃+필드를 그린다.
// → 타입 추가 시 ResolveFieldNames에 한 줄만 더하면 됨.

[CustomPropertyDrawer(typeof(AttackDeliveryEvent))]
public sealed class Editor_AttackDeliveryEvent_Drawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        => AttackEventDrawerUtil.Draw(position, property, label, ResolveFieldNames(property));

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => AttackEventDrawerUtil.Height(property, ResolveFieldNames(property));

    private static List<string> ResolveFieldNames(SerializedProperty property)
    {
        var names = new List<string> { "label", "enabled", "startTime", "type" };

        var type = AttackEventDrawerUtil.EnumValue<AttackDeliveryType>(property.FindPropertyRelative("type"));
        names.Add(type switch
        {
            AttackDeliveryType.Projectile => "projectile",
            AttackDeliveryType.Field => "field",
            _ => "melee"
        });

        names.Add("dedupe");
        names.Add("hitResult");
        return names;
    }
}

[CustomPropertyDrawer(typeof(AttackMovementEvent))]
public sealed class Editor_AttackMovementEvent_Drawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        => AttackEventDrawerUtil.Draw(position, property, label, ResolveFieldNames(property));

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => AttackEventDrawerUtil.Height(property, ResolveFieldNames(property));

    private static List<string> ResolveFieldNames(SerializedProperty property)
    {
        var names = new List<string> { "label", "enabled", "startTime", "type" };

        var type = AttackEventDrawerUtil.EnumValue<AttackMovementType>(property.FindPropertyRelative("type"));
        switch (type)
        {
            case AttackMovementType.SelfJump:
                names.Add("duration");
                names.Add("height");
                break;
            case AttackMovementType.Suspend:
                names.Add("duration");
                break;
            case AttackMovementType.Slam:
                names.Add("speed");
                break;
            default: // Lunge
                names.Add("duration");
                names.Add("distance");
                names.Add("curve");
                break;
        }
        return names;
    }
}

[CustomPropertyDrawer(typeof(AttackFeedbackEvent))]
public sealed class Editor_AttackFeedbackEvent_Drawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        => AttackEventDrawerUtil.Draw(position, property, label, ResolveFieldNames(property));

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => AttackEventDrawerUtil.Height(property, ResolveFieldNames(property));

    private static List<string> ResolveFieldNames(SerializedProperty property)
    {
        var names = new List<string> { "label", "enabled", "trigger" };

        var trigger = AttackEventDrawerUtil.EnumValue<AttackFeedbackTrigger>(property.FindPropertyRelative("trigger"));
        bool timeline = trigger == AttackFeedbackTrigger.Timeline;
        if (timeline)
        {
            names.Add("startTime");
            names.Add("deferUntilWindupEnd");
            names.Add("motionAfterimages");
        }
        else // DeliveryFire
        {
            names.Add("deliveryIndex");
        }
        names.Add("localPlayerOnly");

        // VFX: 주소가 있을 때만 배치/스케일 필드 노출
        names.Add("vfxAddress");
        SerializedProperty vfxAddr = property.FindPropertyRelative("vfxAddress");
        bool hasVfx = vfxAddr != null && !string.IsNullOrEmpty(vfxAddr.stringValue);
        if (hasVfx)
        {
            names.Add("vfxOrigin");
            names.Add("vfxOffset");
            names.Add("vfxEuler");
            names.Add("vfxScale");
        }

        // endTime: Actor VFX 디스폰/잔상 창 — vfx가 있거나 모션 잔상일 때만 의미
        bool motionAfter = timeline && (property.FindPropertyRelative("motionAfterimages")?.boolValue ?? false);
        if (hasVfx || motionAfter)
            names.Add("endTime");

        names.Add("sfx");
        names.Add("cameraShake");
        names.Add("slowMo");
        names.Add("cameraCue");
        return names;
    }
}

internal static class AttackEventDrawerUtil
{
    public static void Draw(Rect position, SerializedProperty property, GUIContent label, List<string> fieldNames)
    {
        EditorGUI.BeginProperty(position, label, property);

        float lineH = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        Rect row = new Rect(position.x, position.y, position.width, lineH);

        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, ResolveHeader(property, label), true);
        if (!property.isExpanded)
        {
            EditorGUI.EndProperty();
            return;
        }

        EditorGUI.indentLevel++;
        row.y += lineH + spacing;
        for (int i = 0; i < fieldNames.Count; i++)
        {
            SerializedProperty child = property.FindPropertyRelative(fieldNames[i]);
            if (child == null)
                continue;

            float h = EditorGUI.GetPropertyHeight(child, true);
            EditorGUI.PropertyField(new Rect(row.x, row.y, row.width, h), child, true);
            row.y += h + spacing;
        }
        EditorGUI.indentLevel--;

        EditorGUI.EndProperty();
    }

    public static float Height(SerializedProperty property, List<string> fieldNames)
    {
        float lineH = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        if (!property.isExpanded)
            return lineH;

        float total = lineH + spacing; // 폴드아웃 + 첫 필드 전 간격
        for (int i = 0; i < fieldNames.Count; i++)
        {
            SerializedProperty child = property.FindPropertyRelative(fieldNames[i]);
            if (child == null)
                continue;
            total += EditorGUI.GetPropertyHeight(child, true) + spacing;
        }
        return total;
    }

    // 배열 원소의 폴드아웃 헤더를 element의 label 필드 값으로(없으면 기본 "Element N").
    private static GUIContent ResolveHeader(SerializedProperty property, GUIContent fallback)
    {
        SerializedProperty labelProp = property.FindPropertyRelative("label");
        return labelProp != null && !string.IsNullOrEmpty(labelProp.stringValue)
            ? new GUIContent(labelProp.stringValue)
            : fallback;
    }

    // gap이 있는 enum(예: AttackMovementType {Lunge=0, SelfJump=3...})도 정확히 값으로 변환.
    // 멀티 편집 등으로 index가 -1(혼합)이면 첫 값으로 폴백(예외 방지).
    public static T EnumValue<T>(SerializedProperty enumProp) where T : Enum
    {
        Array values = Enum.GetValues(typeof(T));
        int idx = enumProp.enumValueIndex;
        if (idx < 0 || idx >= values.Length)
            idx = 0;
        return (T)values.GetValue(idx);
    }
}
