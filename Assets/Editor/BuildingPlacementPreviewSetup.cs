using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Wire existing placement components without replacing any authored scene data.
public sealed class BuildingPlacementPreviewSetup : IProcessSceneWithReport
{
    private const string SheetPath = "Assets/Sprites/UI/BuildingPlacement.png";
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
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += ConfigureLoadedScenes;
    }

    [MenuItem("Tools/Mini Colonies/Configure Building Placement Preview")]
    private static void ConfigureLoadedScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach (BuildingPlacementInput input in Resources.FindObjectsOfTypeAll<BuildingPlacementInput>())
        {
            if (EditorUtility.IsPersistent(input) || !input.gameObject.scene.IsValid()) continue;
            if (Configure(input)) EditorSceneManager.MarkSceneDirty(input.gameObject.scene);
        }
    }

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (BuildingPlacementInput input in root.GetComponentsInChildren<BuildingPlacementInput>(true))
                Configure(input);
    }

    private static bool Configure(BuildingPlacementInput input)
    {
        if (input.placementHatchSprite != null && input.placementOutlineSprite != null &&
            input.optionalGroundFillSprite != null && input.requiredGroundFillSprite != null &&
            input.rangeHighlightMaterial != null && input.rangeInnerCornerSprite != null) return false;
        Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(SheetPath).OfType<Sprite>().ToArray();
        bool changed = false;
        if (input.placementHatchSprite == null)
        {
            input.placementHatchSprite = sprites.FirstOrDefault(sprite => sprite.name == "BuildingPlacement_5");
            changed |= input.placementHatchSprite != null;
        }
        if (input.placementOutlineSprite == null)
        {
            input.placementOutlineSprite = sprites.FirstOrDefault(sprite => sprite.name == "BuildingPlacement_1");
            changed |= input.placementOutlineSprite != null;
        }
        if (input.optionalGroundFillSprite == null)
        {
            input.optionalGroundFillSprite = sprites.FirstOrDefault(sprite => sprite.name == "BuildingPlacement_11");
            changed |= input.optionalGroundFillSprite != null;
        }
        if (input.requiredGroundFillSprite == null)
        {
            input.requiredGroundFillSprite = sprites.FirstOrDefault(sprite => sprite.name == "BuildingPlacement_12");
            changed |= input.requiredGroundFillSprite != null;
        }
        if (input.rangeHighlightMaterial == null)
        {
            WorldPlacementHover hover = input.worldManager != null ? input.worldManager.GetComponent<WorldPlacementHover>() : null;
            input.rangeHighlightMaterial = hover != null && hover.highlightMaterial != null ? hover.highlightMaterial :
                AssetDatabase.LoadAssetAtPath<Material>("Assets/MapResources/ResourceHover.mat");
            changed |= input.rangeHighlightMaterial != null;
        }
        if (input.rangeInnerCornerSprite == null)
        {
            input.rangeInnerCornerSprite = sprites.FirstOrDefault(sprite => sprite.name == "BuildingPlacement_10");
            changed |= input.rangeInnerCornerSprite != null;
        }
        if (changed) EditorUtility.SetDirty(input);
        return changed;
    }
}
