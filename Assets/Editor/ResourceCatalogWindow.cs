using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

public sealed class ResourceCatalogWindow : EditorWindow
{
    [SerializeField] private ResourceCatalog catalog;
    [SerializeField] private int tab;
    [SerializeField] private int resourceIndex;
    [SerializeField] private int typeIndex;
    private string search = string.Empty;
    private Vector2 listScroll, detailScroll;
    private SerializedObject data;
    private readonly List<string> errors = new List<string>();

    [MenuItem("Tools/Mini Colonies/Resources/Resource Editor")]
    private static void OpenDefault() => ResourceCatalogSetup.EditCatalog();

    public static void ShowCatalog(ResourceCatalog target, bool showTypes = false)
    {
        var window = GetWindow<ResourceCatalogWindow>("Resource Editor");
        window.catalog = target;
        window.tab = showTypes ? 1 : 0;
        window.search = string.Empty;
        window.minSize = new Vector2(700f, 420f);
        window.Show();
        window.Focus();
    }

    [OnOpenAsset]
    private static bool OpenAsset(int instanceId, int line)
    {
        var target = EditorUtility.InstanceIDToObject(instanceId) as ResourceCatalog;
        if (target == null) return false;
        ShowCatalog(target);
        return true;
    }

    private void OnEnable() => Undo.undoRedoPerformed += Repaint;

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= Repaint;
        data?.Dispose();
        data = null;
    }

    private void OnGUI()
    {
        if (catalog == null) catalog = AssetDatabase.LoadAssetAtPath<ResourceCatalog>(ResourceCatalogSetup.CatalogPath);
        if (catalog == null)
        {
            EditorGUILayout.HelpBox("Create the default resource catalog to begin.", MessageType.Info);
            if (GUILayout.Button("Create Resource Catalog")) ResourceCatalogSetup.EditCatalog();
            return;
        }
        if (data == null || data.targetObject != catalog)
        {
            data?.Dispose();
            data = new SerializedObject(catalog);
        }
        data.Update();
        DrawToolbar();
        EditorGUILayout.ObjectField("Catalog", catalog, typeof(ResourceCatalog), false);
        EditorGUILayout.PropertyField(data.FindProperty("fallbackIcon"), new GUIContent("Default Icon"));
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawList();
            using (new EditorGUILayout.VerticalScope())
            {
                detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
                SerializedProperty entries = data.FindProperty(tab == 0 ? "resources" : "types");
                int index = tab == 0 ? resourceIndex : typeIndex;
                if (entries.arraySize == 0)
                    EditorGUILayout.HelpBox(tab == 0 ? "Click Add Resource to create your first resource." :
                        "Click Add Resource Type to create your first type.", MessageType.Info);
                else
                {
                    index = Mathf.Clamp(index, 0, entries.arraySize - 1);
                    if (tab == 0) resourceIndex = index; else typeIndex = index;
                    DrawDetails(entries.GetArrayElementAtIndex(index));
                }
                EditorGUILayout.EndScrollView();
            }
        }
        data.ApplyModifiedProperties();
        catalog.GetValidationErrors(errors);
        foreach (string error in errors) EditorGUILayout.HelpBox(error, MessageType.Error);
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            int previousTab = tab;
            tab = GUILayout.Toolbar(tab, new[] { "Resources", "Resource Types" }, EditorStyles.toolbarButton, GUILayout.Width(245f));
            if (previousTab != tab) search = string.Empty;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(tab == 0 ? "Add Resource" : "Add Resource Type", EditorStyles.toolbarButton)) AddEntry();
            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(45f)))
            {
                data.ApplyModifiedProperties();
                AssetDatabase.SaveAssetIfDirty(catalog);
            }
        }
    }

    private void DrawList()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(220f)))
        {
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            SerializedProperty entries = data.FindProperty(tab == 0 ? "resources" : "types");
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                string name = entry.FindPropertyRelative("displayName").stringValue;
                string id = tab == 0 ? entry.FindPropertyRelative("resource").intValue.ToString() : entry.FindPropertyRelative("id").stringValue;
                if (!string.IsNullOrEmpty(search) && name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    id.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (GUILayout.Toggle((tab == 0 ? resourceIndex : typeIndex) == i,
                    new GUIContent(string.IsNullOrWhiteSpace(name) ? "Unnamed" : name, "Reference ID: " + id), "Button"))
                {
                    if (tab == 0) resourceIndex = i; else typeIndex = i;
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField(entries.arraySize + (tab == 0 ? " resources" : " resource types"), EditorStyles.miniLabel);
        }
    }

    private void DrawDetails(SerializedProperty entry)
    {
        EditorGUILayout.LabelField(tab == 0 ? "Resource" : "Resource Type", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(entry.FindPropertyRelative("displayName"), new GUIContent("Name"));
        EditorGUILayout.PropertyField(entry.FindPropertyRelative("icon"), new GUIContent("Icon (optional)"));
        EditorGUILayout.PropertyField(entry.FindPropertyRelative("description"));
        if (tab == 0)
        {
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("worldTileIcon"),
                new GUIContent("World Tile Icon (optional)", "Used for node/deposit footprint markers. Falls back to the regular Icon, then Default Icon."));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("availableFrom"), new GUIContent("Available From"));
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Resource Types", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Tick every type this resource belongs to. Types are independent; they do not include other types automatically.", MessageType.Info);
            DrawMemberships(entry.FindPropertyRelative("types"));
            if (GUILayout.Button("Add a New Resource Type..."))
            {
                tab = 1;
                AddEntry();
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Assign this type to resources on the Resources tab. Factories can require one or several types.", MessageType.Info);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Resources With This Type", EditorStyles.boldLabel);
            string typeId = entry.FindPropertyRelative("id").stringValue;
            int count = 0;
            SerializedProperty resources = data.FindProperty("resources");
            for (int i = 0; i < resources.arraySize; i++)
            {
                SerializedProperty resource = resources.GetArrayElementAtIndex(i);
                if (FindMembership(resource.FindPropertyRelative("types"), typeId) < 0) continue;
                count++;
                if (GUILayout.Button(resource.FindPropertyRelative("displayName").stringValue))
                {
                    resourceIndex = i;
                    tab = 0;
                    search = string.Empty;
                    break;
                }
            }
            if (count == 0) EditorGUILayout.LabelField("No resources assigned yet.");
        }
        EditorGUILayout.Space();
        string reference = entry.FindPropertyRelative("id")?.stringValue ?? entry.FindPropertyRelative("resource").intValue.ToString();
        EditorGUILayout.LabelField("Reference ID (automatic)", reference, EditorStyles.miniLabel);
        EditorGUILayout.HelpBox(tab == 0
            ? "Renaming keeps existing references intact. World Tile Icon falls back to Icon, then Default Icon."
            : "Renaming changes the display name while keeping existing references intact. Missing icons use the catalog's Default Icon.", MessageType.None);
    }

    private void DrawMemberships(SerializedProperty memberships)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        SerializedProperty types = data.FindProperty("types");
        for (int i = 0; i < types.arraySize; i++)
        {
            SerializedProperty type = types.GetArrayElementAtIndex(i);
            string id = type.FindPropertyRelative("id").stringValue;
            if (string.IsNullOrWhiteSpace(id) || !known.Add(id)) continue;
            int existing = FindMembership(memberships, id);
            bool assigned = existing >= 0;
            bool value = EditorGUILayout.ToggleLeft(new GUIContent(type.FindPropertyRelative("displayName").stringValue, id), assigned);
            if (value == assigned) continue;
            if (value)
            {
                int index = memberships.arraySize++;
                memberships.GetArrayElementAtIndex(index).stringValue = id;
            }
            else
                for (int j = memberships.arraySize - 1; j >= 0; j--)
                    if (memberships.GetArrayElementAtIndex(j).stringValue == id) memberships.DeleteArrayElementAtIndex(j);
        }
        for (int i = 0; i < memberships.arraySize; i++)
        {
            string id = memberships.GetArrayElementAtIndex(i).stringValue;
            if (known.Contains(id)) continue;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Missing type: " + id);
                if (GUILayout.Button("Remove", GUILayout.Width(65f)))
                {
                    memberships.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
        }
    }

    private static int FindMembership(SerializedProperty memberships, string id)
    {
        for (int i = 0; i < memberships.arraySize; i++)
            if (memberships.GetArrayElementAtIndex(i).stringValue == id) return i;
        return -1;
    }

    private void AddEntry()
    {
        data.ApplyModifiedProperties();
        Undo.RecordObject(catalog, tab == 0 ? "Add Resource" : "Add Resource Type");
        if (tab == 0)
        {
            catalog.AddResource("New resource");
            resourceIndex = catalog.resources.Count - 1;
        }
        else
        {
            catalog.AddType("New type");
            typeIndex = catalog.types.Count - 1;
        }
        EditorUtility.SetDirty(catalog);
        search = string.Empty;
        detailScroll = Vector2.zero;
        data.Update();
        GUIUtility.ExitGUI();
    }
}
