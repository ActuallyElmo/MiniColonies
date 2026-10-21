using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

[CustomPropertyDrawer(typeof(TerrainLayer))]
public sealed class TerrainLayerDrawer : PropertyDrawer
{
    private static readonly string[] Fields = { "name", "noiseThreshold", "layerColor", "minRegionSize" };
    private static readonly GUIContent[] SurfaceLabels = { new GUIContent("Flat Ground"), new GUIContent("Water Level") };

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
            OnGUI(EditorGUILayout.GetControlRect(false, GetPropertyHeight(current, GUIContent.none)), current,
                new GUIContent(current.displayName));
            serialized.ApplyModifiedProperties();
        });
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight;
        if (!property.isExpanded) return height;
        foreach (string field in Fields)
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative(field), true) + EditorGUIUtility.standardVerticalSpacing;
        return height + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, label, true);
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            foreach (string field in Fields)
            {
                row.y += row.height + EditorGUIUtility.standardVerticalSpacing;
                SerializedProperty child = property.FindPropertyRelative(field);
                row.height = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(row, child, true);
            }
            row.y += row.height + EditorGUIUtility.standardVerticalSpacing;
            row.height = EditorGUIUtility.singleLineHeight;
            SerializedProperty surface = property.FindPropertyRelative("surfaceType");
            SerializedProperty legacy = property.FindPropertyRelative("isBuildable");
            int value = surface.intValue == 0 ? (legacy.boolValue ? 0 : 1) : surface.intValue - 1;
            if (value < 0 || value >= SurfaceLabels.Length) value = -1;
            bool previousMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = surface.hasMultipleDifferentValues || (surface.intValue == 0 && legacy.hasMultipleDifferentValues);
            EditorGUI.BeginChangeCheck();
            int choice = EditorGUI.Popup(row, new GUIContent("Tile Type", "Edges are detected from the terrain shape. Resource types come from the map resource index."), value, SurfaceLabels);
            if (EditorGUI.EndChangeCheck()) surface.intValue = choice + 1;
            EditorGUI.showMixedValue = previousMixed;
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndProperty();
    }
}
