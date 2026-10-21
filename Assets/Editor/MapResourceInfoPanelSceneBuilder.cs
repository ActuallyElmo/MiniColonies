using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// All layout is authored in Edit Mode. Runtime only binds the saved controls.
public sealed class MapResourceInfoPanelSceneBuilder : IProcessSceneWithReport
{
    private static readonly Color Panel = new Color32(22, 27, 36, 255);
    private static readonly Color Row = new Color32(42, 49, 61, 255);
    private static readonly Color TextColor = new Color32(240, 243, 248, 255);
    private static readonly Color Muted = new Color32(165, 173, 188, 255);
    public int callbackOrder => 20;

    [InitializeOnLoadMethod]
    private static void QueueInstallation()
    {
        EditorApplication.delayCall += Install;
        EditorSceneManager.sceneOpened -= OnSceneOpened;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode) => Install();
    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Install;
    }

    [MenuItem("Tools/Mini Colonies/Install Resource Info Panel")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && EnsurePanel(scene, true)) EditorSceneManager.MarkSceneDirty(scene);
        }
        // Saving remains explicit: never include unrelated pending scene edits.
    }

    public void OnProcessScene(Scene scene, BuildReport report) => EnsurePanel(scene, false);

    private static bool EnsurePanel(Scene scene, bool recordUndo)
    {
        var roots = scene.GetRootGameObjects();
        var existing = roots.SelectMany(root => root.GetComponentsInChildren<MapResourceInfoPanel>(true)).FirstOrDefault();
        if (existing != null) return ApplyCompactLayout(existing, recordUndo);
        var actions = roots.SelectMany(root => root.GetComponentsInChildren<PlayerActionManager>(true)).FirstOrDefault();
        var world = roots.SelectMany(root => root.GetComponentsInChildren<WorldManager>(true)).FirstOrDefault();
        var overlay = roots.SelectMany(root => root.GetComponentsInChildren<BuildingPanelOverlayManager>(true)).FirstOrDefault();
        Canvas canvas = overlay != null ? overlay.GetComponentInParent<Canvas>() : null;
        if (canvas == null) canvas = roots.SelectMany(root => root.GetComponentsInChildren<Canvas>(true))
            .FirstOrDefault(candidate => candidate.isRootCanvas && candidate.renderMode == RenderMode.ScreenSpaceOverlay);
        if (actions == null || world == null || canvas == null) return false;
        canvas = canvas.rootCanvas;

        var root = RectObject("ResourceInfoPanel", canvas.transform);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        if (recordUndo) Undo.RegisterCreatedObjectUndo(root.gameObject, "Install resource info panel");
        var card = ImageObject("Card", root, Panel);
        card.rectTransform.anchorMin = card.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        card.rectTransform.pivot = new Vector2(1f, 0.5f);
        card.rectTransform.anchoredPosition = new Vector2(-20f, 0f);
        card.rectTransform.sizeDelta = new Vector2(220f, 140f);
        var outline = card.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color32(59, 70, 88, 220);
        outline.effectDistance = new Vector2(1f, -1f);
        var panel = root.gameObject.AddComponent<MapResourceInfoPanel>();
        var serialized = new SerializedObject(panel);
        Reference(serialized, "playerActions", actions);
        Reference(serialized, "card", card.rectTransform);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        ApplyCompactLayout(panel, false);
        card.gameObject.SetActive(false);
        return true;
    }

    private static bool ApplyCompactLayout(MapResourceInfoPanel panel, bool recordUndo)
    {
        var serialized = new SerializedObject(panel);
        var version = serialized.FindProperty("authoredLayoutVersion");
        if (version.intValue >= 1) return false;
        var card = serialized.FindProperty("card").objectReferenceValue as RectTransform;
        if (card == null) return false;
        if (recordUndo) Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Compact resource info panel");
        card.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 220f);
        var layout = card.GetComponent<VerticalLayoutGroup>() ?? card.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 7, 8);
        layout.spacing = 4f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = card.GetComponent<ContentSizeFitter>() ?? card.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var header = card.Find("Header") as RectTransform ?? RectObject("Header", card);
        var headerLayout = header.GetComponent<HorizontalLayoutGroup>() ?? header.gameObject.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 5f;
        headerLayout.childControlWidth = headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = true;
        Height(header.gameObject, 22f);
        var name = ExistingLabel(serialized, "resourceName", "ResourceName", header, "Resource", 18f, TextColor);
        var nameSize = name.GetComponent<LayoutElement>() ?? name.gameObject.AddComponent<LayoutElement>();
        nameSize.minWidth = 0f;
        nameSize.flexibleWidth = 0f;
        var kind = ExistingLabel(serialized, "sourceKind", "SourceKind", header, "Deposit", 11f, Muted);
        kind.text = kind.text == "RESOURCE NODE" || kind.text == "Node" ? "Node" : "Deposit";
        // Remove the old standalone kind row's fixed-height layout after reparenting.
        var kindSize = kind.GetComponent<LayoutElement>();
        if (kindSize != null) { kindSize.minHeight = -1f; kindSize.preferredHeight = -1f; }
        var spacer = header.Find("Spacer") as RectTransform ?? RectObject("Spacer", header);
        var spacerSize = spacer.GetComponent<LayoutElement>() ?? spacer.gameObject.AddComponent<LayoutElement>();
        spacerSize.minWidth = spacerSize.preferredWidth = 0f;
        spacerSize.flexibleWidth = 1f;
        var closeTransform = header.Find("Close");
        var close = closeTransform != null ? closeTransform.GetComponent<Image>() : ImageObject("Close", header, Row);
        var closeSize = close.GetComponent<LayoutElement>() ?? close.gameObject.AddComponent<LayoutElement>();
        closeSize.minWidth = closeSize.preferredWidth = 18f;
        var button = close.GetComponent<Button>() ?? close.gameObject.AddComponent<Button>();
        button.targetGraphic = close;
        var closeLabelTransform = close.transform.Find("Label");
        var closeLabel = closeLabelTransform != null ? closeLabelTransform.GetComponent<TMP_Text>() : Label("Label", close.transform, "X", 12f, TextColor);
        closeLabel.fontSize = 12f;
        Stretch(closeLabel.rectTransform);
        closeLabel.alignment = TextAlignmentOptions.Center;

        name.transform.SetSiblingIndex(0);
        kind.transform.SetSiblingIndex(1);
        spacer.SetSiblingIndex(2);
        close.transform.SetAsLastSibling();

        var types = ExistingLabel(serialized, "resourceTypes", "ResourceTypes", card, "Resource types", 12f, Muted);
        types.textWrappingMode = TextWrappingModes.Normal;
        Height(types.gameObject, 16f);
        types.GetComponent<LayoutElement>().preferredHeight = -1f;
        var remaining = InfoRow(card, "RemainingRow");
        var oldQuantityLabel = card.Find("QuantityLabel");
        var quantityLabel = oldQuantityLabel != null ? oldQuantityLabel.GetComponent<TMP_Text>() : Label("QuantityLabel", remaining, "Remaining", 12f, Muted);
        quantityLabel.transform.SetParent(remaining, false);
        quantityLabel.text = "Remaining";
        quantityLabel.fontSize = 12f;
        Height(quantityLabel.gameObject, 18f);
        var labelSize = quantityLabel.GetComponent<LayoutElement>();
        labelSize.flexibleWidth = 1f;
        var quantity = ExistingLabel(serialized, "quantity", "Quantity", remaining, "0 / 0", 15f, TextColor);
        Height(quantity.gameObject, 18f);
        quantity.alignment = TextAlignmentOptions.MidlineRight;
        var qualityRow = InfoRow(card, "QualityRow");
        var qualityLabelTransform = qualityRow.Find("QualityLabel");
        var qualityLabel = qualityLabelTransform != null ? qualityLabelTransform.GetComponent<TMP_Text>() : Label("QualityLabel", qualityRow, "Quality", 12f, Muted);
        (qualityLabel.GetComponent<LayoutElement>() ?? qualityLabel.gameObject.AddComponent<LayoutElement>()).flexibleWidth = 1f;
        var quality = ExistingLabel(serialized, "quality", "Quality", qualityRow, "—", 13f, Muted);
        quality.alignment = TextAlignmentOptions.MidlineRight;
        var description = ExistingLabel(serialized, "description", "Description", card, "No description available.", 12f, Muted);
        description.textWrappingMode = TextWrappingModes.Normal;
        description.alignment = TextAlignmentOptions.TopLeft;
        description.richText = false;
        // Three short lines keep descriptions from turning this into a large panel.
        Height(description.gameObject, 38f);
        header.SetSiblingIndex(0);
        types.transform.SetSiblingIndex(1);
        remaining.SetSiblingIndex(2);
        qualityRow.SetSiblingIndex(3);
        description.transform.SetSiblingIndex(4);
        Reference(serialized, "remainingFill", null);
        Reference(serialized, "closeButton", button);
        version.intValue = 1;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        foreach (string obsolete in new[] { "Divider", "QuantityBar" })
        {
            Transform child = card.Find(obsolete);
            if (child == null) continue;
            if (recordUndo) Undo.DestroyObjectImmediate(child.gameObject);
            else Object.DestroyImmediate(child.gameObject);
        }
        EditorUtility.SetDirty(panel);
        return true;
    }

    private static TMP_Text ExistingLabel(SerializedObject serialized, string field, string name,
        Transform parent, string caption, float size, Color color)
    {
        var property = serialized.FindProperty(field);
        var text = property.objectReferenceValue as TMP_Text;
        if (text == null) text = Label(name, parent, caption, size, color);
        text.transform.SetParent(parent, false);
        text.fontSize = size;
        property.objectReferenceValue = text;
        return text;
    }

    private static RectTransform InfoRow(Transform parent, string name)
    {
        var rect = parent.Find(name) as RectTransform ?? RectObject(name, parent);
        var layout = rect.GetComponent<HorizontalLayoutGroup>() ?? rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        Height(rect.gameObject, 18f);
        return rect;
    }

    private static RectTransform RectObject(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.layer = parent.gameObject.layer;
        obj.transform.SetParent(parent, false);
        return obj.GetComponent<RectTransform>();
    }

    private static Image ImageObject(string name, Transform parent, Color color)
    {
        var rect = RectObject(name, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static TMP_Text Label(string name, Transform parent, string caption, float size, Color color)
    {
        var rect = RectObject(name, parent);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/ari-w9500-condensed-display.asset");
        if (font != null) text.font = font;
        text.text = caption;
        text.fontSize = size;
        text.color = color;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        return text;
    }

    private static void Height(GameObject obj, float height)
    {
        var element = obj.GetComponent<LayoutElement>() ?? obj.AddComponent<LayoutElement>();
        element.minHeight = element.preferredHeight = height;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void Reference(SerializedObject target, string field, Object value) =>
        target.FindProperty(field).objectReferenceValue = value;
}
