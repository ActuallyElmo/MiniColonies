using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

[CustomPropertyDrawer(typeof(MapResourceSpawnSettings))]
public sealed class MapResourceSpawnSettingsDrawer : PropertyDrawer
{
    // Use the same IMGUI rendering/visibility in Unity's UI Toolkit default Inspector
    // and in any IMGUI custom Inspector, without keeping a second copy of the values.
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        var serialized = property.serializedObject;
        string path = property.propertyPath;
        var label = new GUIContent(property.displayName, property.tooltip);
        return new IMGUIContainer(() =>
        {
            if (serialized.targetObject == null) return;
            serialized.UpdateIfRequiredOrScript();
            var current = serialized.FindProperty(path);
            if (current == null) return;
            Rect position = EditorGUILayout.GetControlRect(false, GetPropertyHeight(current, label));
            OnGUI(position, current, label);
            serialized.ApplyModifiedProperties();
        });
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight;
        if (property.isExpanded)
            foreach (var child in VisibleFields(property))
                height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(child, true);
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        int indent = EditorGUI.indentLevel;
        try
        {
            var resource = property.FindPropertyRelative(nameof(MapResourceSpawnSettings.resource));
            var definition = resource.hasMultipleDifferentValues ? null : resource.objectReferenceValue as MapResourceDefinition;
            string resourceName = definition != null && !string.IsNullOrWhiteSpace(definition.displayName)
                ? definition.displayName : definition != null ? definition.name : label.text;
            var title = definition != null ? new GUIContent($"{resourceName} ({definition.shape})", label.tooltip) : label;
            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, title, true);
            if (!property.isExpanded) return;
            EditorGUI.indentLevel++;
            float y = row.yMax;
            foreach (var child in VisibleFields(property))
            {
                y += EditorGUIUtility.standardVerticalSpacing;
                float height = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(new Rect(position.x, y, position.width, height), child, true);
                y += height;
            }
        }
        finally
        {
            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }

    private static IEnumerable<SerializedProperty> VisibleFields(SerializedProperty property)
    {
        var child = property.Copy();
        var end = property.GetEndProperty();
        if (!child.NextVisible(true)) yield break;
        do
        {
            if (IsVisible(property, child.name)) yield return child.Copy();
        } while (child.NextVisible(false) && !SerializedProperty.EqualContents(child, end));
    }

    private static bool IsVisible(SerializedProperty property, string field)
    {
        if (field == nameof(MapResourceSpawnSettings.enabled) || field == nameof(MapResourceSpawnSettings.resource)) return true;
        var reference = property.FindPropertyRelative(nameof(MapResourceSpawnSettings.resource));
        var definition = reference.hasMultipleDifferentValues ? null : reference.objectReferenceValue as MapResourceDefinition;
        if (definition == null && !reference.hasMultipleDifferentValues) return false;
        bool deposit = definition != null && definition.shape == MapResourceShape.Deposit;
        bool nodes = definition != null && definition.shape == MapResourceShape.Nodes;
        switch (field)
        {
            case nameof(MapResourceSpawnSettings.depositTiles):
            case nameof(MapResourceSpawnSettings.requireFlatTerrain): return deposit;
            case nameof(MapResourceSpawnSettings.nodeDistribution): return nodes;
            case nameof(MapResourceSpawnSettings.nodesPerCluster):
                var distribution = property.FindPropertyRelative(nameof(MapResourceSpawnSettings.nodeDistribution));
                return nodes && !distribution.hasMultipleDifferentValues &&
                    distribution.intValue == (int)ResourceNodeDistribution.Clustered;
            case nameof(MapResourceSpawnSettings.totalUnits): return definition != null && !definition.unlimited;
            case nameof(MapResourceSpawnSettings.heightRange): return Enabled(property, nameof(MapResourceSpawnSettings.restrictHeight));
            case nameof(MapResourceSpawnSettings.centerDistanceRange): return Enabled(property, nameof(MapResourceSpawnSettings.restrictDistanceFromCenter));
            case nameof(MapResourceSpawnSettings.mapRegion): return Enabled(property, nameof(MapResourceSpawnSettings.restrictMapPosition));
            default: return true;
        }
    }

    private static bool Enabled(SerializedProperty property, string name)
    {
        var toggle = property.FindPropertyRelative(name);
        return !toggle.hasMultipleDifferentValues && toggle.boolValue;
    }
}
