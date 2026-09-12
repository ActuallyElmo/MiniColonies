using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Persists the compact housing hierarchy into the active scene. The panel
/// also creates the same hierarchy automatically in edit mode when absent.
/// </summary>
public static class HousingPanelSceneBuilder
{
    private const string VehicleRowPrefabPath =
        "Assets/Prefabs/UI/PopHousingVehicleRow.prefab";
    private const string VehicleRowLayoutMarker = "HousingRowLayoutV3";
    private const string PanelFontPath =
        "Assets/Fonts/ari-w9500-condensed-display.asset";

    [MenuItem("Tools/Mini Colonies/Rebuild Compact Housing Panel")]
    public static void Install()
    {
        EnsureVehicleRowPrefabForEditor();
        HousingBuildingInfoPanel panel = Resources.FindObjectsOfTypeAll<
            HousingBuildingInfoPanel>()
            .FirstOrDefault(candidate =>
                candidate != null &&
                !EditorUtility.IsPersistent(candidate) &&
                candidate.gameObject.scene.IsValid());
        if (panel == null)
        {
            Debug.LogWarning("No loaded HousingBuildingInfoPanel was found.");
            return;
        }

        panel.RebuildCompactLayoutForEditor();
        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
        EditorSceneManager.SaveScene(panel.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Rebuilt the editable compact housing panel hierarchy.");
    }

    public static void EnsureVehicleRowPrefabForEditor()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(
            VehicleRowPrefabPath);
        if (existing == null ||
            existing.transform.Find(VehicleRowLayoutMarker) != null)
        {
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(
            VehicleRowPrefabPath);
        try
        {
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            }

            RectTransform rootRect = root.transform as RectTransform;
            rootRect.sizeDelta = new Vector2(0f, 28f);
            Image background = root.GetComponent<Image>() ??
                root.AddComponent<Image>();
            background.color = new Color32(46, 53, 66, 255);
            background.raycastTarget = false;

            LayoutElement rootLayout = root.GetComponent<LayoutElement>() ??
                root.AddComponent<LayoutElement>();
            rootLayout.minHeight = 28f;
            rootLayout.preferredHeight = 28f;
            rootLayout.flexibleWidth = 1f;
            rootLayout.flexibleHeight = 0f;

            HorizontalLayoutGroup layout =
                root.GetComponent<HorizontalLayoutGroup>() ??
                root.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 2, 2);
            layout.spacing = 3f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            HousingVehicleRowUI view = root.GetComponent<HousingVehicleRowUI>() ??
                root.AddComponent<HousingVehicleRowUI>();
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                PanelFontPath);

            GameObject iconObject = new GameObject(
                "VehicleIcon",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            iconObject.transform.SetParent(root.transform, false);
            Image icon = iconObject.GetComponent<Image>();
            icon.color = new Color32(110, 150, 205, 255);
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            SetRowLayout(iconObject, 20f, 0f);

            TMP_Text name = CreateRowText(
                "VehicleName", root.transform, "Vehicle", 8f,
                TextAlignmentOptions.MidlineLeft, font);
            SetRowLayout(name.gameObject, 76f, 1f);

            TMP_Text status = CreateRowText(
                "VehicleStatus", root.transform, "Parked", 7f,
                TextAlignmentOptions.Center, font,
                new Color32(165, 173, 188, 255));
            SetRowLayout(status.gameObject, 48f, 0f);

            TMP_Text capacity = CreateRowText(
                "VehicleCapacity", root.transform, "0", 7f,
                TextAlignmentOptions.Center, font);
            SetRowLayout(capacity.gameObject, 24f, 0f);

            TMP_Text occupied = CreateRowText(
                "VehicleOccupiedSpace", root.transform, "0", 7f,
                TextAlignmentOptions.Center, font);
            SetRowLayout(occupied.gameObject, 24f, 0f);

            Button remove = CreateRowButton(
                root.transform, "RemoveVehicleButton", "X", font);
            SetRowLayout(remove.gameObject, 22f, 0f);

            SerializedObject serializedView = new SerializedObject(view);
            serializedView.FindProperty("vehicleIcon").objectReferenceValue = icon;
            serializedView.FindProperty("vehicleNameText").objectReferenceValue = name;
            serializedView.FindProperty("vehicleStatusText").objectReferenceValue = status;
            serializedView.FindProperty("vehicleCapacityText").objectReferenceValue = capacity;
            serializedView.FindProperty("vehicleOccupiedSpaceText").objectReferenceValue = occupied;
            serializedView.FindProperty("removeVehicleButton").objectReferenceValue = remove;
            serializedView.ApplyModifiedPropertiesWithoutUndo();

            RectTransform marker = new GameObject(
                VehicleRowLayoutMarker, typeof(RectTransform))
                .GetComponent<RectTransform>();
            marker.SetParent(root.transform, false);
            marker.sizeDelta = Vector2.zero;
            marker.gameObject.SetActive(false);

            PrefabUtility.SaveAsPrefabAsset(root, VehicleRowPrefabPath);
            AssetDatabase.ImportAsset(VehicleRowPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static TMP_Text CreateRowText(
        string name,
        Transform parent,
        string value,
        float size,
        TextAlignmentOptions alignment,
        TMP_FontAsset font,
        Color? color = null)
    {
        GameObject textObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.text = value;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color ?? new Color32(240, 243, 248, 255);
        text.font = font;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreateRowButton(
        Transform parent,
        string name,
        string label,
        TMP_FontAsset font)
    {
        GameObject buttonObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color32(153, 67, 63, 255);
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        TMP_Text text = CreateRowText(
            "Label", buttonObject.transform, label, 8f,
            TextAlignmentOptions.Center, font);
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        return button;
    }

    private static void SetRowLayout(GameObject target, float width, float flexible)
    {
        LayoutElement element = target.GetComponent<LayoutElement>() ??
            target.AddComponent<LayoutElement>();
        element.preferredWidth = width;
        element.flexibleWidth = flexible;
        element.flexibleHeight = 1f;
    }
}
