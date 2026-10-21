using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

[CustomPropertyDrawer(typeof(BuildingRange))]
public sealed class BuildingRangeDrawer : PropertyDrawer
{
    private static readonly string[] Fields = { "name", "radius", "color", "requiredTileTypes" };

    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        var serialized = property.serializedObject;
        string path = property.propertyPath;
        return new IMGUIContainer(() =>
        {
            if (serialized.targetObject == null) return;
            serialized.UpdateIfRequiredOrScript();
            SerializedProperty current = serialized.FindProperty(path);
            if (current == null) return;
            var label = new GUIContent(current.displayName);
            OnGUI(EditorGUILayout.GetControlRect(false, GetPropertyHeight(current, label)), current, label);
            serialized.ApplyModifiedProperties();
        });
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight;
        if (property.isExpanded)
            foreach (string field in Fields)
                height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative(field), true);
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        SerializedProperty name = property.FindPropertyRelative("name");
        SerializedProperty radius = property.FindPropertyRelative("radius");
        string title = name.hasMultipleDifferentValues ? label.text : string.IsNullOrWhiteSpace(name.stringValue) ? label.text : name.stringValue;
        var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded,
            new GUIContent(title + " (" + radius.intValue + " tiles)", "Distance from the nearest footprint cell, including diagonals. Required types are alternatives within this range."), true);
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            foreach (string field in Fields)
            {
                row.y += row.height + EditorGUIUtility.standardVerticalSpacing;
                SerializedProperty child = property.FindPropertyRelative(field);
                row.height = EditorGUI.GetPropertyHeight(child, true);
                if (field == "color")
                {
                    var colorLabel = new GUIContent(child.displayName, child.tooltip);
                    EditorGUI.BeginProperty(row, colorLabel, child);
                    bool previousMixed = EditorGUI.showMixedValue;
                    EditorGUI.showMixedValue = child.hasMultipleDifferentValues;
                    EditorGUI.BeginChangeCheck();
                    Color chosen = EditorGUI.ColorField(row, colorLabel,
                        BuildingRange.ResolveOutlineColor(child.colorValue), true, false, false);
                    if (EditorGUI.EndChangeCheck()) child.colorValue = chosen;
                    EditorGUI.showMixedValue = previousMixed;
                    EditorGUI.EndProperty();
                    continue;
                }
                EditorGUI.PropertyField(row, child, field == "requiredTileTypes"
                    ? new GUIContent("Required Tiles (Any)", child.tooltip) : new GUIContent(child.displayName, child.tooltip), true);
            }
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndProperty();
    }
}
