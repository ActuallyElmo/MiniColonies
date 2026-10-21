using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomPropertyDrawer(typeof(ProductionResourceEntry))]
public sealed class FactoryInputResourceDrawer : PropertyDrawer
{
    private static readonly string[] CommonFields = { "storageCapacity", "amountAtMaximumEfficiency" };

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!property.isExpanded) return EditorGUIUtility.singleLineHeight;
        float height = EditorGUIUtility.singleLineHeight + 2f;
        foreach (string field in VisibleFields(property))
            height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative(field), true) + 2f;
        if (IsInput(property))
        {
            int count = property.FindPropertyRelative("requiredTypes").arraySize;
            int rows = 1 + (IsTyped(property) ? Mathf.Max(1, count) + 1 : 1);
            height += rows * (EditorGUIUtility.singleLineHeight + 2f) + 38f;
        }
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        Rect line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            line.y += line.height + 2f;
            foreach (string field in VisibleFields(property))
            {
                SerializedProperty child = property.FindPropertyRelative(field);
                line.height = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(line, child, true);
                line.y += line.height + 2f;
                if (field == "inputName") DrawInputSelector(ref line, property);
            }
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndProperty();
    }

    private static void DrawInputSelector(ref Rect line, SerializedProperty property)
    {
        SerializedProperty mode = property.FindPropertyRelative("useResourceTypes");
        line.height = EditorGUIUtility.singleLineHeight;
        bool previousMixed = EditorGUI.showMixedValue;
        EditorGUI.showMixedValue = mode.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        int choice = EditorGUI.Popup(line, "Input mode", mode.boolValue ? 1 : 0,
            new[] { "Specific item", "Resource types" });
        if (EditorGUI.EndChangeCheck()) mode.boolValue = choice == 1;
        EditorGUI.showMixedValue = previousMixed;
        line.y += line.height + 2f;
        if (mode.boolValue)
        {
            DrawTypeSelectors(ref line, property);
            return;
        }
        EditorGUI.PropertyField(line, property.FindPropertyRelative("resourceType"), new GUIContent("Specific item"));
        line.y += line.height + 2f;
        line.height = 36f;
        EditorGUI.HelpBox(EditorGUI.IndentedRect(line),
            "Every shift requires this specific item. Other input slots can use resource types. Switching modes keeps both configurations.", MessageType.Info);
        line.y += line.height + 2f;
    }

    private static void DrawTypeSelectors(ref Rect line, SerializedProperty property)
    {
        var required = property.FindPropertyRelative("requiredTypes");
        var categories = new List<ResourceCategory>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        ResourceCatalog catalog = ResourceCatalog.Current;
        if (catalog != null && catalog.types != null)
            foreach (ResourceCategory category in catalog.types)
                if (category != null && !string.IsNullOrWhiteSpace(category.id) && seen.Add(category.id)) categories.Add(category);

        int count = Mathf.Max(1, required.arraySize);
        for (int i = 0; i < count; i++)
        {
            string currentId = i < required.arraySize ? required.GetArrayElementAtIndex(i).stringValue : string.Empty;
            var labels = new List<string> { "Select resource type..." };
            int selected = 0;
            for (int j = 0; j < categories.Count; j++)
            {
                labels.Add(string.IsNullOrWhiteSpace(categories[j].displayName) ? categories[j].id : categories[j].displayName);
                if (categories[j].id == currentId) selected = j + 1;
            }
            if (selected == 0 && !string.IsNullOrEmpty(currentId))
            {
                selected = labels.Count;
                labels.Add("Missing type: " + currentId);
            }
            line.height = EditorGUIUtility.singleLineHeight;
            Rect popup = line;
            bool removable = required.arraySize > 0;
            if (removable) popup.width -= 24f;
            bool previousMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = required.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int choice = EditorGUI.Popup(popup, i == 0 ? "Resource type" : "And type", selected, labels.ToArray());
            bool changed = EditorGUI.EndChangeCheck();
            EditorGUI.showMixedValue = previousMixed;
            if (changed && choice > 0 && choice <= categories.Count)
            {
                property.FindPropertyRelative("useResourceTypes").boolValue = true;
                if (required.arraySize <= i) required.arraySize = i + 1;
                required.GetArrayElementAtIndex(i).stringValue = categories[choice - 1].id;
            }
            Rect remove = new Rect(line.xMax - 20f, line.y, 20f, line.height);
            line.y += line.height + 2f;
            if (removable && GUI.Button(remove, "-"))
            {
                property.FindPropertyRelative("useResourceTypes").boolValue = true;
                required.DeleteArrayElementAtIndex(i);
                return;
            }
        }

        if (IsTyped(property))
        {
            line.height = EditorGUIUtility.singleLineHeight;
            if (GUI.Button(EditorGUI.IndentedRect(line), "Require another type (AND)"))
            {
                int index = required.arraySize++;
                required.GetArrayElementAtIndex(index).stringValue = string.Empty;
            }
            line.y += line.height + 2f;
        }
        var ids = new List<string>();
        for (int i = 0; i < required.arraySize; i++) ids.Add(required.GetArrayElementAtIndex(i).stringValue);
        var matches = new List<ResourceType>();
        catalog?.GetResourcesMatchingTypes(ids, matches);
        line.height = 36f;
        string message = $"The player chooses one of {matches.Count} matching materials for this input on each shift. Every required type must match.";
        EditorGUI.HelpBox(EditorGUI.IndentedRect(line), message,
            IsTyped(property) && matches.Count == 0 ? MessageType.Warning : MessageType.Info);
        line.y += line.height + 2f;
    }

    private static bool IsInput(SerializedProperty property) =>
        property.FindPropertyRelative("direction").intValue == (int)ProductionResourceDirection.Input;

    private static bool IsTyped(SerializedProperty property) => property.FindPropertyRelative("useResourceTypes").boolValue;

    private static IEnumerable<string> VisibleFields(SerializedProperty property)
    {
        yield return "direction";
        if (IsInput(property))
        {
            yield return "inputName";
        }
        else yield return "resourceType";
        foreach (string field in CommonFields) yield return field;
    }
}

[CustomEditor(typeof(ResourceProductionBuildingData)), CanEditMultipleObjects]
public sealed class ResourceProductionBuildingDataEditor : Editor
{
    private ReorderableList entries;

    private void OnEnable()
    {
        entries = new ReorderableList(serializedObject, serializedObject.FindProperty("resources"), true, true, true, true);
        entries.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Resource inputs and outputs");
        entries.elementHeightCallback = index => EditorGUI.GetPropertyHeight(entries.serializedProperty.GetArrayElementAtIndex(index), true) + 4f;
        entries.drawElementCallback = (rect, index, active, focused) =>
        {
            rect.y += 2f;
            rect.height -= 4f;
            SerializedProperty entry = entries.serializedProperty.GetArrayElementAtIndex(index);
            int direction = entry.FindPropertyRelative("direction").intValue;
            int ordinal = 0;
            for (int i = 0; i <= index; i++)
                if (entries.serializedProperty.GetArrayElementAtIndex(i).FindPropertyRelative("direction").intValue == direction) ordinal++;
            string name = direction == (int)ProductionResourceDirection.Input ? "Input " : "Output ";
            EditorGUI.PropertyField(rect, entry, new GUIContent(name + ordinal), true);
        };
        entries.onAddDropdownCallback = (rect, list) =>
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Resource type input"), false, () => AddEntry(true, true));
            menu.AddItem(new GUIContent("Specific item input"), false, () => AddEntry(true, false));
            menu.AddItem(new GUIContent("Output"), false, () => AddEntry(false, false));
            menu.DropDown(rect);
        };
    }

    private void AddEntry(bool input, bool matchTypes)
    {
        serializedObject.Update();
        SerializedProperty list = entries.serializedProperty;
        int index = list.arraySize++;
        SerializedProperty entry = list.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("direction").intValue = (int)(input ? ProductionResourceDirection.Input : ProductionResourceDirection.Output);
        entry.FindPropertyRelative("resourceType").intValue = (int)ResourceType.IronOre;
        entry.FindPropertyRelative("inputId").stringValue = Guid.NewGuid().ToString("N");
        entry.FindPropertyRelative("inputName").stringValue = string.Empty;
        entry.FindPropertyRelative("useResourceTypes").boolValue = input && matchTypes;
        entry.FindPropertyRelative("requiredTypes").arraySize = 0;
        entry.FindPropertyRelative("storageCapacity").intValue = 2000;
        entry.FindPropertyRelative("amountAtMaximumEfficiency").intValue = 0;
        entry.isExpanded = true;
        serializedObject.ApplyModifiedProperties();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "resources");
        if (serializedObject.isEditingMultipleObjects)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("resources"), true);
        else entries.DoLayoutList();
        serializedObject.ApplyModifiedProperties();
    }
}
