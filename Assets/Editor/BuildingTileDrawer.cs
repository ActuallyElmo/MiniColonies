using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

[CustomPropertyDrawer(typeof(BuildingTile))]
public sealed class BuildingTileDrawer : PropertyDrawer
{
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
        if (!property.isExpanded) return height;
        foreach (string field in Fields(property))
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative(field), true) + EditorGUIUtility.standardVerticalSpacing;
        if (MissingRequiredGround(property) || UsesLegacyRules(property)) height += EditorGUIUtility.singleLineHeight * 2f + 4f;
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, label, true);
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            foreach (string field in Fields(property))
            {
                row.y += row.height + EditorGUIUtility.standardVerticalSpacing;
                SerializedProperty child = property.FindPropertyRelative(field);
                row.height = EditorGUI.GetPropertyHeight(child, true);
                var childLabel = field == "allowedTileTypes" ? new GUIContent("Optional Types", child.tooltip) :
                    field == "requiredTileTypes" ? new GUIContent("Required Types (Any)", child.tooltip) :
                    field == "allowedGrounds" ? new GUIContent("Legacy Ground Assets") : new GUIContent(child.displayName, child.tooltip);
                EditorGUI.PropertyField(row, child, childLabel, true);
            }
            if (MissingRequiredGround(property) || UsesLegacyRules(property))
            {
                row.y += row.height + 2f;
                row.height = EditorGUIUtility.singleLineHeight * 2f;
                bool missing = MissingRequiredGround(property);
                EditorGUI.HelpBox(row, missing ? "Required cell has no tile type. Placement will always be invalid." :
                    "Legacy ground assets are active until Optional or Required Types contains a type.", missing ? MessageType.Warning : MessageType.Info);
            }
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndProperty();
    }

    private static IEnumerable<string> Fields(SerializedProperty property)
    {
        yield return "localPosition";
        yield return "isPort";
        SerializedProperty port = property.FindPropertyRelative("isPort");
        if (port.boolValue || port.hasMultipleDifferentValues) yield return "portType";
        yield return "allowedTileTypes";
        yield return "requiredTileTypes";
        yield return "footprintIconOverride";
        if (property.FindPropertyRelative("allowedGrounds").arraySize > 0)
        {
            yield return "allowedGrounds";
            yield return "requireGroundMatch";
        }
        else if (MissingRequiredGround(property)) yield return "requireGroundMatch";
    }

    private static bool UsesLegacyRules(SerializedProperty property) =>
        property.FindPropertyRelative("allowedTileTypes").arraySize == 0 &&
        property.FindPropertyRelative("requiredTileTypes").arraySize == 0 &&
        property.FindPropertyRelative("allowedGrounds").arraySize > 0;

    private static bool MissingRequiredGround(SerializedProperty property)
    {
        SerializedProperty required = property.FindPropertyRelative("requireGroundMatch");
        if (!required.boolValue || required.hasMultipleDifferentValues) return false;
        if (property.FindPropertyRelative("allowedTileTypes").arraySize > 0 ||
            property.FindPropertyRelative("requiredTileTypes").arraySize > 0) return false;
        SerializedProperty grounds = property.FindPropertyRelative("allowedGrounds");
        for (int i = 0; i < grounds.arraySize; i++)
            if (grounds.GetArrayElementAtIndex(i).objectReferenceValue != null) return false;
        return true;
    }
}
