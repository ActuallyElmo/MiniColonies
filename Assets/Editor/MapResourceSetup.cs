using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// One-time defaults, created through Unity APIs. Re-running preserves authored assets and settings.
public sealed class MapResourceSetup : IProcessSceneWithReport
{
    private const string Root = "Assets/MapResources";
    public int callbackOrder => 0;

    [InitializeOnLoadMethod]
    private static void QueueSetup()
    {
        EditorApplication.delayCall += ConfigureLoadedScenes;
        EditorSceneManager.sceneOpened -= OnSceneOpened;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode) => ConfigureLoadedScenes();
    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += ConfigureLoadedScenes;
    }

    [MenuItem("Tools/Mini Colonies/Configure Map Resources")]
    public static void ConfigureLoadedScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        foreach (var world in Resources.FindObjectsOfTypeAll<WorldManager>())
        {
            if (EditorUtility.IsPersistent(world) || !world.gameObject.scene.IsValid()) continue;
            if (Configure(world, true)) EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
        }
        // Leave scene saving to the user, including scenes with unrelated pending edits.
    }

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        foreach (var root in scene.GetRootGameObjects())
            foreach (var world in root.GetComponentsInChildren<WorldManager>(true))
                Configure(world, false);
    }

    private static bool Configure(WorldManager world, bool recordUndo)
    {
        bool changed = false;
        if (!world.mapResourceSetupComplete)
        {
            if (recordUndo) Undo.RecordObject(world, "Configure map resources");
            if (world.mapResources == null) world.mapResources = new System.Collections.Generic.List<MapResourceSpawnSettings>();
            if (world.mapResources.Count == 0)
            {
                var oil = Definition("Oil", ResourceType.Oil, MapResourceShape.Deposit, MapResourceDepth.Underground,
                    new Color(0.12f, 0.10f, 0.15f), false);
                var water = Definition("Water", ResourceType.Water, MapResourceShape.Deposit, MapResourceDepth.Overground,
                    new Color(0.08f, 0.4f, 0.65f), true);
                var copper = Definition("Copper", ResourceType.CopperOre, MapResourceShape.Nodes, MapResourceDepth.Overground,
                    new Color(0.65f, 0.30f, 0.13f), false);
                var carbon = Definition("Carbon", ResourceType.Carbon, MapResourceShape.Nodes, MapResourceDepth.Overground,
                    new Color(0.12f, 0.14f, 0.17f), false);
                var concrete = Definition("Concrete", ResourceType.Concrete, MapResourceShape.Deposit, MapResourceDepth.Overground,
                    new Color(0.62f, 0.59f, 0.51f), false);
                // Water uses existing low terrain. Generation never carves or raises terrain.
                world.mapResources.Add(new MapResourceSpawnSettings
                {
                    resource = water, sourceCount = new Vector2Int(1, 6), sourcesPerThousandTiles = 3f,
                    depositTiles = new Vector2Int(10, 45), requireFlatTerrain = false,
                    restrictHeight = true, heightRange = new Vector2(0f, world.heightStep * 0.2f)
                });
                world.mapResources.Add(new MapResourceSpawnSettings
                {
                    resource = oil, sourceCount = new Vector2Int(2, 6), sourcesPerThousandTiles = 0.5f,
                    depositTiles = new Vector2Int(12, 32), totalUnits = new Vector2Int(40000, 80000), requireFlatTerrain = false
                });
                world.mapResources.Add(new MapResourceSpawnSettings
                {
                    resource = concrete, sourceCount = new Vector2Int(2, 6), sourcesPerThousandTiles = 0.5f,
                    depositTiles = new Vector2Int(8, 25), totalUnits = new Vector2Int(20000, 40000),
                    restrictHeight = true, heightRange = new Vector2(world.heightStep * 0.8f, world.heightStep * world.layers.Count)
                });
                world.mapResources.Add(new MapResourceSpawnSettings
                {
                    resource = copper, sourceCount = new Vector2Int(12, 60), sourcesPerThousandTiles = 4f,
                    nodeDistribution = ResourceNodeDistribution.Clustered, nodesPerCluster = new Vector2Int(3, 7),
                    totalUnits = new Vector2Int(15000, 30000),
                    restrictHeight = true, heightRange = new Vector2(world.heightStep * 0.8f, world.heightStep * world.layers.Count)
                });
                world.mapResources.Add(new MapResourceSpawnSettings
                {
                    resource = carbon, sourceCount = new Vector2Int(12, 60), sourcesPerThousandTiles = 4f,
                    nodeDistribution = ResourceNodeDistribution.Scattered, totalUnits = new Vector2Int(15000, 30000),
                    restrictHeight = true, heightRange = new Vector2(world.heightStep * 0.8f, world.heightStep * world.layers.Count)
                });
            }
            world.mapResourceSetupComplete = true;
            EditorUtility.SetDirty(world);
            changed = true;
        }
        var hover = world.GetComponent<WorldPlacementHover>();
        if (hover == null)
        {
            hover = recordUndo ? Undo.AddComponent<WorldPlacementHover>(world.gameObject) : world.gameObject.AddComponent<WorldPlacementHover>();
            changed = true;
        }
        if (world.GetComponent<WorldPlacementHighlight>() == null)
        {
            if (recordUndo) Undo.AddComponent<WorldPlacementHighlight>(world.gameObject);
            else world.gameObject.AddComponent<WorldPlacementHighlight>();
            changed = true;
        }
        if (hover.highlightMaterial == null)
        {
            if (recordUndo) Undo.RecordObject(hover, "Configure resource highlighting");
            hover.highlightMaterial = Material("ResourceHover", Color.white, "MiniColonies/MapResourceHighlight");
            EditorUtility.SetDirty(hover);
            changed = true;
        }
        if (hover.outlineSprite == null)
        {
            var placement = Resources.FindObjectsOfTypeAll<BuildingPlacementInput>().FirstOrDefault(input =>
                input.gameObject.scene == world.gameObject.scene && input.placementOutlineSprite != null);
            Sprite outline = placement != null ? placement.placementOutlineSprite :
                AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/UI/BuildingPlacement.png")
                    .OfType<Sprite>().FirstOrDefault(sprite => sprite.name == "BuildingPlacement_1");
            if (outline != null)
            {
                if (recordUndo) Undo.RecordObject(hover, "Configure resource tile outlines");
                hover.outlineSprite = outline;
                EditorUtility.SetDirty(hover);
                changed = true;
            }
        }
        if (hover.innerCornerSprite == null)
        {
            Sprite corner = AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/UI/BuildingPlacement.png")
                .OfType<Sprite>().FirstOrDefault(sprite => sprite.name == "BuildingPlacement_10");
            if (corner != null)
            {
                if (recordUndo) Undo.RecordObject(hover, "Configure world placement inner corners");
                hover.innerCornerSprite = corner;
                EditorUtility.SetDirty(hover);
                changed = true;
            }
        }
        ConfigureTerrainMaterials(world);
        return changed;
    }

    private static void ConfigureTerrainMaterials(WorldManager world)
    {
        if (world.terrainMaterial == null || world.mapResources == null) return;
        var definitions = new HashSet<MapResourceDefinition>();
        var surfacesToUpdate = new HashSet<Material>();
        foreach (var rule in world.mapResources)
        {
            var definition = rule?.resource;
            if (definition == null || !definitions.Add(definition) || definition.terrainMaterial == null) continue;
            var surface = definition.terrainMaterial;
            bool migrateTint = definition.terrainAppearanceVersion < 1;
            bool changeShader = surface.shader != world.terrainMaterial.shader;
            if (!migrateTint && !changeShader) continue;
            surfacesToUpdate.Add(surface);
            if (migrateTint)
            {
                Undo.RecordObject(definition, "Migrate resource height colors");
                definition.terrainTint = surface.HasProperty("_BaseColor") ? surface.GetColor("_BaseColor") :
                    surface.HasProperty("_Color") ? surface.GetColor("_Color") : Color.white;
                definition.terrainAppearanceVersion = 1;
                // Original placeholders shared their surface material. Keep their solid tint independently.
                PreservePlaceholderMaterials(definition, surface);
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssetIfDirty(definition);
            }
        }
        // Capture every definition's original color before changing any shared material.
        foreach (var surface in surfacesToUpdate)
        {
            if (surface == world.terrainMaterial) continue;
            Undo.RecordObject(surface, "Use map terrain shader for resources");
            surface.shader = world.terrainMaterial.shader;
            surface.CopyMatchingPropertiesFromMaterial(world.terrainMaterial);
            surface.renderQueue = world.terrainMaterial.renderQueue;
            EditorUtility.SetDirty(surface);
            AssetDatabase.SaveAssetIfDirty(surface);
        }
    }

    private static void PreservePlaceholderMaterials(MapResourceDefinition definition, Material surface)
    {
        if (definition.prefabVariants == null) return;
        Material decoration = null;
        foreach (var prefab in definition.prefabVariants)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path) || !path.StartsWith(Root + "/Prefabs/", StringComparison.Ordinal)) continue;
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed = false;
                foreach (var renderer in contents.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] != surface) continue;
                        if (decoration == null)
                        {
                            string materialPath = $"{Root}/{definition.name} Prefab.mat";
                            decoration = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                            if (decoration == null)
                            {
                                decoration = new Material(surface) { name = definition.name + " Prefab" };
                                AssetDatabase.CreateAsset(decoration, materialPath);
                            }
                        }
                        materials[i] = decoration;
                        changed = true;
                    }
                    renderer.sharedMaterials = materials;
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
    }

    private static MapResourceDefinition Definition(string label, ResourceType type, MapResourceShape shape,
        MapResourceDepth depth, Color color, bool unlimited)
    {
        EnsureFolder(Root);
        string path = $"{Root}/{label}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<MapResourceDefinition>(path);
        if (existing != null) return existing;
        var material = Material(label, color, "Universal Render Pipeline/Lit");
        var definition = ScriptableObject.CreateInstance<MapResourceDefinition>();
        definition.displayName = label;
        definition.resourceType = type;
        definition.shape = shape;
        definition.depth = depth;
        definition.unlimited = unlimited;
        definition.terrainMaterial = material;
        if (shape == MapResourceShape.Nodes)
        {
            definition.prefabVariants.Add(Placeholder(label + " Round", material, PrimitiveType.Sphere));
            definition.prefabVariants.Add(Placeholder(label + " Angular", material, PrimitiveType.Cube));
        }
        AssetDatabase.CreateAsset(definition, path);
        return definition;
    }

    private static Material Material(string label, Color color, string shaderName)
    {
        EnsureFolder(Root);
        string path = $"{Root}/{label}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Shader shader = Shader.Find(shaderName);
        if (shader == null) throw new InvalidOperationException($"Map resource setup needs shader '{shaderName}'.");
        var material = new Material(shader) { name = label };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static GameObject Placeholder(string label, Material material, PrimitiveType primitive)
    {
        EnsureFolder(Root + "/Prefabs");
        string path = $"{Root}/Prefabs/{label}.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;
        var root = new GameObject(label);
        try
        {
            var rock = GameObject.CreatePrimitive(primitive);
            rock.name = "Placeholder Rock";
            rock.transform.SetParent(root.transform, false);
            rock.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            rock.transform.localScale = new Vector3(0.5f, 0.44f, 0.5f);
            rock.GetComponent<Renderer>().sharedMaterial = material;
            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int separator = path.LastIndexOf('/');
        string parent = path.Substring(0, separator);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
    }
}
