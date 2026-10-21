using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Preserve integer asset references while offering all catalog-defined resources,
// including identities without a legacy enum alias.
[CustomPropertyDrawer(typeof(ResourceType))]
public sealed class ResourceTypeDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        var ids = new List<ResourceType>();
        var labels = new List<GUIContent>();
        ResourceCatalog catalog = ResourceCatalog.Current;
        if (catalog != null && catalog.resources != null)
        {
            foreach (ResourceDefinition definition in catalog.resources)
            {
                if (definition == null || (int)definition.resource < 0 || ids.Contains(definition.resource)) continue;
                ids.Add(definition.resource);
                labels.Add(new GUIContent(catalog.GetResourceName(definition.resource), "Resource ID: " + (int)definition.resource));
            }
        }
        else if (catalog == null)
        {
            foreach (ResourceType resource in Enum.GetValues(typeof(ResourceType)))
            {
                ids.Add(resource);
                labels.Add(new GUIContent(resource.ToString()));
            }
        }
        int selected = ids.IndexOf((ResourceType)property.intValue);
        if (selected < 0)
        {
            selected = ids.Count;
            ids.Add((ResourceType)property.intValue);
            labels.Add(new GUIContent("Missing resource (#" + property.intValue + ")"));
        }
        bool previousMixed = EditorGUI.showMixedValue;
        EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        Rect popup = position;
        popup.width -= 24f;
        int choice = EditorGUI.Popup(popup, label, selected, labels.ToArray());
        if (EditorGUI.EndChangeCheck() && choice >= 0 && choice < ids.Count) property.intValue = (int)ids[choice];
        EditorGUI.showMixedValue = previousMixed;
        Rect edit = new Rect(position.xMax - 20f, position.y, 20f, position.height);
        if (GUI.Button(edit, new GUIContent("...", "Edit or add resources in the Resource Editor")))
            ResourceCatalogSetup.EditCatalog();
        EditorGUI.EndProperty();
    }
}
