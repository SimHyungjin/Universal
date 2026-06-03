using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(AttackShapeData))]
public class Editor_AttackShape_DataDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        float lineH   = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        Rect row = new Rect(position.x, position.y, position.width, lineH);

        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, label, true);
        if (!property.isExpanded)
        {
            EditorGUI.EndProperty();
            return;
        }

        EditorGUI.indentLevel++;

        SerializedProperty typeProp = property.FindPropertyRelative("type");
        row.y += lineH + spacing;
        EditorGUI.PropertyField(row, typeProp);

        AttackShape shape = (AttackShape)typeProp.intValue;
        row.y += lineH + spacing;

        switch (shape)
        {
            case AttackShape.Sphere:
                DrawField(ref row, property, "radius", lineH, spacing);
                break;

            case AttackShape.Cone:
                DrawField(ref row, property, "radius", lineH, spacing);
                DrawField(ref row, property, "angle",  lineH, spacing);
                DrawField(ref row, property, "length", lineH, spacing);
                break;

            case AttackShape.Box:
                DrawField(ref row, property, "length", lineH, spacing);
                DrawField(ref row, property, "width",  lineH, spacing);
                break;
        }

        EditorGUI.indentLevel--;
        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!property.isExpanded)
            return EditorGUIUtility.singleLineHeight;

        AttackShape shape = (AttackShape)property.FindPropertyRelative("type").intValue;
        int fieldCount = shape switch
        {
            AttackShape.Sphere => 1,
            AttackShape.Cone   => 3,
            AttackShape.Box    => 2,
            _                  => 1
        };

        float lineH   = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        return (2 + fieldCount) * (lineH + spacing) - spacing; // foldout + type + shape fields
    }

    private static void DrawField(ref Rect row, SerializedProperty parent, string name, float lineH, float spacing)
    {
        EditorGUI.PropertyField(row, parent.FindPropertyRelative(name));
        row.y += lineH + spacing;
    }
}
