using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ResourceCatalogSetup
{
    public const string CatalogPath = "Assets/Resources/ResourceCatalog.asset";

    [InitializeOnLoadMethod]
    private static void QueueSetup() => EditorApplication.delayCall += EnsureCatalog;

    private static void EnsureCatalog()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (AssetDatabase.LoadAssetAtPath<ResourceCatalog>(CatalogPath) != null) return;
        if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(CatalogPath)))
        {
            Debug.LogError($"Cannot install resource catalog: an incompatible asset already exists at {CatalogPath}.");
            return;
        }
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        var catalog = ScriptableObject.CreateInstance<ResourceCatalog>();
        ResourceCatalogDefaults.PopulateEmpty(catalog);
        catalog.fallbackIcon = AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/Placeholder/PlaceholderSheet1.png")
            .OfType<Sprite>().FirstOrDefault(sprite => sprite.name == "PlaceholderSheet1_2");
        AssetDatabase.CreateAsset(catalog, CatalogPath);
        AssetDatabase.SaveAssetIfDirty(catalog);
    }

    [MenuItem("Tools/Mini Colonies/Resources/Edit Resource Catalog")]
    public static void EditCatalog()
    {
        EnsureCatalog();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<ResourceCatalog>(CatalogPath);
        EditorGUIUtility.PingObject(Selection.activeObject);
        ResourceCatalogWindow.ShowCatalog(Selection.activeObject as ResourceCatalog);
    }
}

[CustomEditor(typeof(ResourceCatalog))]
public sealed class ResourceCatalogEditor : Editor
{
    private readonly List<string> errors = new List<string>();

    public override void OnInspectorGUI()
    {
        var catalog = (ResourceCatalog)target;
        EditorGUILayout.HelpBox("Create resources and resource types in the Resource Editor. Select a resource there to edit its name, icon, origins and type checkboxes. New resources need no code changes.", MessageType.Info);
        EditorGUILayout.LabelField("Resources", (catalog.resources?.Count ?? 0).ToString());
        EditorGUILayout.LabelField("Resource types", (catalog.types?.Count ?? 0).ToString());
        if (GUILayout.Button("Edit / Add Resources")) ResourceCatalogWindow.ShowCatalog(catalog);
        if (GUILayout.Button("Edit / Add Resource Types")) ResourceCatalogWindow.ShowCatalog(catalog, true);
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("fallbackIcon"), new GUIContent("Default Icon"));
        serializedObject.ApplyModifiedProperties();
        catalog.GetValidationErrors(errors);
        for (int i = 0; i < errors.Count; i++) EditorGUILayout.HelpBox(errors[i], MessageType.Error);
    }
}
