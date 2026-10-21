using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Adds missing authored selectors without rebuilding panels or saving scenes.</summary>
public static class FactoryInputSelectorSetup
{
    private const string SchedulePrefab = "Assets/Prefabs/UI/FactoryScheduleRow.prefab";
    private const string VehiclePrefab = "Assets/Prefabs/UI/IndustrialVehicleRow.prefab";

    [InitializeOnLoadMethod]
    private static void QueueUpgrade()
    {
        EditorApplication.delayCall += Install;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Install;
    }

    [MenuItem("Tools/Mini Colonies/Resources/Install Factory Input Selectors")]
    public static void Install()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        UpgradePrefab(SchedulePrefab, true);
        UpgradePrefab(VehiclePrefab, false);
    }

    private static void UpgradePrefab(string path, bool schedule)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.assetPath == path)
        {
            // The open Prefab Stage owns those edits, even if it is currently clean.
            Debug.LogWarning($"Close Prefab Mode, then run Install Factory Input Selectors to upgrade {path}.");
            return;
        }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            bool changed = false;
            if (schedule)
            {
                foreach (var row in root.GetComponentsInChildren<FactoryResourceAmountRowView>(true))
                    changed |= EnsureSelector(row);
            }
            else
            {
                foreach (var dropdown in root.GetComponentsInChildren<TMP_Dropdown>(true))
                    changed |= EnsureDropdownIcons(dropdown, true);
            }
            if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // Also called by the original scene builder, so rebuilding retains this feature.
    public static bool EnsureSelector(FactoryResourceAmountRowView row)
    {
        var serialized = new SerializedObject(row);
        var dropdownProperty = serialized.FindProperty("inputResourceDropdown");
        TMP_Dropdown dropdown = dropdownProperty.objectReferenceValue as TMP_Dropdown;
        bool changed = false;
        if (dropdown == null)
        {
            dropdown = row.transform.Find("InputResourceDropdown")?.GetComponent<TMP_Dropdown>();
            if (dropdown == null)
            {
                GameObject created = TMP_DefaultControls.CreateDropdown(default);
                created.name = "InputResourceDropdown";
                created.transform.SetParent(row.transform, false);
                foreach (Transform child in created.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = row.gameObject.layer;
                created.transform.SetSiblingIndex(2);
                dropdown = created.GetComponent<TMP_Dropdown>();
                var layout = created.AddComponent<LayoutElement>();
                layout.minWidth = 65f;
                layout.preferredWidth = 90f;
                layout.flexibleWidth = 1f;
                layout.minHeight = 16f;
                layout.preferredHeight = 18f;
                TMP_Text source = serialized.FindProperty("resourceNameText").objectReferenceValue as TMP_Text;
                foreach (var text in created.GetComponentsInChildren<TMP_Text>(true))
                {
                    text.font = source != null ? source.font : TMP_Settings.defaultFontAsset;
                    text.fontSize = source != null ? source.fontSize : 8f;
                    text.overflowMode = TextOverflowModes.Ellipsis;
                }
                if (source != null && source.TryGetComponent<LayoutElement>(out var nameLayout))
                {
                    nameLayout.minWidth = 35f;
                    nameLayout.preferredWidth = 55f;
                    nameLayout.flexibleWidth = 0f;
                    source.overflowMode = TextOverflowModes.Ellipsis;
                }
                if (row.TryGetComponent<LayoutElement>(out var rowLayout))
                {
                    rowLayout.minHeight = Mathf.Max(18f, rowLayout.minHeight);
                    rowLayout.preferredHeight = Mathf.Max(18f, rowLayout.preferredHeight);
                }
            }
            dropdownProperty.objectReferenceValue = dropdown;
            changed = true;
        }
        // Remove the space previously reserved for numbered prefixes; retain custom layouts.
        TMP_Text label = serialized.FindProperty("resourceNameText").objectReferenceValue as TMP_Text;
        if (label != null && label.TryGetComponent<LayoutElement>(out var labelLayout) &&
            labelLayout.minWidth == 55f && labelLayout.preferredWidth == 85f && labelLayout.flexibleWidth == 0f)
        {
            labelLayout.minWidth = 35f;
            labelLayout.preferredWidth = 55f;
            changed = true;
        }
        var iconProperty = serialized.FindProperty("resourceIcon");
        if (iconProperty.objectReferenceValue == null)
        {
            Image icon = row.transform.Find("ResourceIcon")?.GetComponent<Image>();
            if (icon != null) { iconProperty.objectReferenceValue = icon; changed = true; }
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        changed |= EnsureDropdownIcons(dropdown, true);
        var spacerProperty = serialized.FindProperty("specificInputSpacer");
        if (spacerProperty.objectReferenceValue == null)
        {
            Transform spacer = row.transform.Find("SpecificInputSpacer");
            if (spacer == null)
            {
                var created = new GameObject("SpecificInputSpacer", typeof(RectTransform), typeof(LayoutElement));
                created.layer = row.gameObject.layer;
                created.transform.SetParent(row.transform, false);
                created.transform.SetSiblingIndex(dropdown.transform.GetSiblingIndex() + 1);
                LayoutElement source = dropdown.GetComponent<LayoutElement>();
                LayoutElement layout = created.GetComponent<LayoutElement>();
                layout.minWidth = source != null ? source.minWidth : 65f;
                layout.preferredWidth = source != null ? source.preferredWidth : 90f;
                layout.flexibleWidth = source != null ? source.flexibleWidth : 1f;
                created.SetActive(false);
                spacer = created.transform;
            }
            spacerProperty.objectReferenceValue = spacer.gameObject;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }
        SerializedProperty styleVersion = serialized.FindProperty("inputDropdownStyleVersion");
        if (styleVersion.intValue < 1)
        {
            ApplyFactoryDropdownStyle(row, dropdown, label);
            styleVersion.intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }
        if (styleVersion.intValue < 2)
        {
            // TMP's default caption has 13px of vertical padding, leaving only
            // 5px for text in our compact 18px factory row. Preserve custom padding.
            if (dropdown.captionText != null)
            {
                RectTransform caption = dropdown.captionText.rectTransform;
                Vector2 minimum = caption.offsetMin;
                Vector2 maximum = caption.offsetMax;
                if (Mathf.Approximately(minimum.y, 6f) && Mathf.Approximately(maximum.y, -7f))
                {
                    minimum.y = 2f;
                    maximum.y = -2f;
                    caption.offsetMin = minimum;
                    caption.offsetMax = maximum;
                }
            }
            styleVersion.intValue = 2;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }
        if (styleVersion.intValue < 3)
        {
            TMP_Text amount = serialized.FindProperty("amountText").objectReferenceValue as TMP_Text;
            if (amount != null) amount.alignment = TextAlignmentOptions.MidlineLeft;
            styleVersion.intValue = 3;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }
        return changed;
    }

    private static void ApplyFactoryDropdownStyle(FactoryResourceAmountRowView row, TMP_Dropdown dropdown, TMP_Text label)
    {
        // Author the same flat controls, font and colors as the neighboring factory buttons.
        // The version marker makes this a one-time migration, preserving later prefab styling.
        Button button = row.transform.Find("Decrease")?.GetComponent<Button>();
        Image control = button != null ? button.targetGraphic as Image : null;
        Color controlColor = control != null ? control.color : new Color32(53, 62, 78, 255);
        Color panelColor = row.TryGetComponent<Image>(out var rowImage) ? rowImage.color : new Color32(46, 53, 66, 255);
        Color textColor = label != null ? label.color : new Color32(240, 243, 248, 255);
        if (button != null) dropdown.colors = button.colors;
        if (dropdown.targetGraphic is Image background) StyleImage(background, control, controlColor);
        if (dropdown.template != null)
        {
            Image popup = dropdown.template.GetComponent<Image>();
            if (popup != null) StyleImage(popup, control, panelColor);
            foreach (Scrollbar scrollbar in dropdown.template.GetComponentsInChildren<Scrollbar>(true))
            {
                if (button != null) scrollbar.colors = button.colors;
                Image track = scrollbar.GetComponent<Image>();
                if (track != null) StyleImage(track, control, panelColor);
                if (scrollbar.targetGraphic is Image handle)
                    StyleImage(handle, control, Color.Lerp(panelColor, textColor, 0.5f));
                if (scrollbar.direction == Scrollbar.Direction.BottomToTop || scrollbar.direction == Scrollbar.Direction.TopToBottom)
                {
                    var rect = (RectTransform)scrollbar.transform;
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 6f);
                }
            }
            ScrollRect scroll = dropdown.template.GetComponent<ScrollRect>();
            if (scroll != null && scroll.viewport != null)
            {
                Vector2 maximum = scroll.viewport.offsetMax;
                maximum.x = -8f;
                scroll.viewport.offsetMax = maximum;
            }
        }
        Toggle item = dropdown.itemText != null ? dropdown.itemText.GetComponentInParent<Toggle>(true) : null;
        if (item != null)
        {
            if (button != null) item.colors = button.colors;
            if (item.targetGraphic is Image itemBackground) StyleImage(itemBackground, control, controlColor);
            if (item.graphic != null)
            {
                item.graphic.color = textColor;
                RectTransform mark = item.graphic.rectTransform;
                mark.anchorMin = mark.anchorMax = new Vector2(0f, 0.5f);
                mark.anchoredPosition = new Vector2(8f, 0f);
                mark.sizeDelta = new Vector2(4f, 4f);
            }
        }
        foreach (TMP_Text text in dropdown.GetComponentsInChildren<TMP_Text>(true))
        {
            text.font = label != null ? label.font : TMP_Settings.defaultFontAsset;
            text.fontSize = label != null ? label.fontSize : 8f;
            text.enableAutoSizing = false;
            text.color = textColor;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
        }
        Image arrow = dropdown.transform.Find("Arrow")?.GetComponent<Image>();
        if (arrow != null)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/UI/ScheduleSprites.png"))
                if (asset is Sprite sprite && sprite.name == "ScheduleSprites_4") { arrow.sprite = sprite; break; }
            arrow.color = textColor;
            arrow.preserveAspect = true;
            arrow.raycastTarget = false;
            arrow.rectTransform.anchorMin = arrow.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            arrow.rectTransform.anchoredPosition = new Vector2(-8f, 0f);
            arrow.rectTransform.sizeDelta = new Vector2(8f, 5f);
        }
        if (dropdown.captionText != null)
        {
            Vector2 minimum = dropdown.captionText.rectTransform.offsetMin;
            Vector2 maximum = dropdown.captionText.rectTransform.offsetMax;
            minimum.x = dropdown.captionImage != null ? 17f : 4f;
            maximum.x = -16f;
            dropdown.captionText.rectTransform.offsetMin = minimum;
            dropdown.captionText.rectTransform.offsetMax = maximum;
        }
    }

    private static void StyleImage(Image target, Image source, Color color)
    {
        target.sprite = source != null ? source.sprite : null;
        target.type = source != null ? source.type : Image.Type.Simple;
        target.color = color;
    }

    public static bool EnsureDropdownIcons(TMP_Dropdown dropdown, bool caption)
    {
        bool changed = false;
        if (dropdown.itemImage == null && dropdown.itemText != null)
        {
            Transform item = dropdown.itemText.transform.parent;
            Image icon = item.Find("ResourceItemIcon")?.GetComponent<Image>();
            if (icon == null) icon = CreateIcon(item, "ResourceItemIcon", 24f);
            dropdown.itemImage = icon;
            Vector2 minimum = dropdown.itemText.rectTransform.offsetMin;
            minimum.x = Mathf.Max(36f, minimum.x);
            dropdown.itemText.rectTransform.offsetMin = minimum;
            changed = true;
        }
        if (caption && dropdown.captionImage == null && dropdown.captionText != null)
        {
            Image icon = dropdown.transform.Find("ResourceCaptionIcon")?.GetComponent<Image>();
            if (icon == null) icon = CreateIcon(dropdown.transform, "ResourceCaptionIcon", 8f);
            dropdown.captionImage = icon;
            Vector2 minimum = dropdown.captionText.rectTransform.offsetMin;
            minimum.x = Mathf.Max(17f, minimum.x);
            dropdown.captionText.rectTransform.offsetMin = minimum;
            changed = true;
        }
        return changed;
    }

    private static Image CreateIcon(Transform parent, string name, float x)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;
        Image icon = go.GetComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        RectTransform rect = icon.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(x, 0f);
        rect.sizeDelta = new Vector2(10f, 10f);
        return icon;
    }
}
