using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Adds the industrial vehicle tab without rebuilding or replacing the user's
/// authored industrial panel hierarchy.
/// </summary>
public static class IndustrialVehicleTabSceneBuilder
{
    private const string RowPrefabPath =
        "Assets/Prefabs/UI/IndustrialVehicleRow.prefab";

    private static readonly Color PanelSection =
        new Color32(28, 33, 42, 255);
    private static readonly Color Control =
        new Color32(46, 53, 66, 255);
    private static readonly Color Accent =
        new Color32(61, 85, 122, 255);
    private static readonly Color TextColor =
        new Color32(240, 243, 248, 255);
    private static readonly Color MutedText =
        new Color32(165, 173, 188, 255);

    [MenuItem("Tools/Mini Colonies/Install Industrial Vehicle Tab")]
    public static void Install()
    {
        IndustrialBuildingInfoPanel panel = FindPanel();
        if (panel == null)
        {
            Debug.LogWarning(
                "No loaded IndustrialBuildingInfoPanel was found.");
            return;
        }

        Transform background = panel.transform.Find("Background") ??
                               panel.transform;
        RectTransform tabBar = EnsureTabBar(background);
        Button schedulingButton = EnsureTabButton(
            tabBar,
            "SchedulingTabButton",
            "Scheduling");
        Button vehiclesButton = EnsureTabButton(
            tabBar,
            "OwnedVehiclesTabButton",
            "Owned Vehicles");

        RectTransform schedulingTab = EnsureFullRect(
            background,
            "SchedulingTab");
        RectTransform vehiclesTab = EnsureFullRect(
            background,
            "OwnedVehiclesTab");

        if (MoveExistingSchedule(background, schedulingTab))
        {
            EnsureScheduleRoom(schedulingTab);
        }

        GameObject rowPrefab = EnsureVehicleRowPrefab();
        BuildVehiclesTab(
            panel,
            vehiclesTab,
            rowPrefab,
            out TMP_Text vehicleCount,
            out ScrollRect scrollRect,
            out RectTransform content,
            out GameObject emptyState);

        schedulingTab.gameObject.SetActive(true);
        vehiclesTab.gameObject.SetActive(false);

        SerializedObject panelObject = new SerializedObject(panel);
        SetReference(panelObject, "schedulingTabButton", schedulingButton);
        SetReference(panelObject, "ownedVehiclesTabButton", vehiclesButton);
        SetReference(panelObject, "schedulingTab", schedulingTab.gameObject);
        SetReference(panelObject, "ownedVehiclesTab", vehiclesTab.gameObject);
        SetReference(panelObject, "ownedVehicleCountText", vehicleCount);
        SetReference(panelObject, "ownedVehiclesScrollRect", scrollRect);
        SetReference(panelObject, "ownedVehicleListContent", content);
        SetReference(panelObject, "emptyOwnedVehiclesState", emptyState);
        SetReference(panelObject, "industrialVehicleRowPrefab", rowPrefab);
        panelObject.ApplyModifiedPropertiesWithoutUndo();

        Scene scene = panel.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log(
            "Installed industrial scheduling/owned-vehicles tabs without " +
            "rebuilding the industrial panel.");
    }

    [MenuItem("Tools/Mini Colonies/Update Industrial Vehicle List Header And Feedback")]
    public static void InstallVehicleListPolish()
    {
        IndustrialBuildingInfoPanel panel = FindPanel();
        Transform tab = panel != null
            ? (panel.transform.Find("Background") ?? panel.transform)
                .Find("OwnedVehiclesTab")
            : null;
        if (panel == null || tab == null)
        {
            Debug.LogWarning(
                "No loaded industrial OwnedVehiclesTab was found.");
            return;
        }

        ApplyVehicleListPolish(panel, tab);
        EnsureVehicleRowPrefab();
        Scene scene = panel.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log(
            "Updated the industrial vehicle column header and drag feedback.");
    }

    private static IndustrialBuildingInfoPanel FindPanel()
    {
        return Resources.FindObjectsOfTypeAll<IndustrialBuildingInfoPanel>()
            .FirstOrDefault(candidate =>
                candidate != null &&
                !EditorUtility.IsPersistent(candidate) &&
                candidate.gameObject.scene.IsValid());
    }

    private static RectTransform EnsureTabBar(Transform parent)
    {
        Transform existing = parent.Find("IndustrialTabButtons");
        if (existing != null) return existing as RectTransform;

        RectTransform bar = RectObject("IndustrialTabButtons", parent);
        bar.anchorMin = new Vector2(0.5f, 1f);
        bar.anchorMax = new Vector2(0.5f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.anchoredPosition = new Vector2(0f, -27f);
        bar.sizeDelta = new Vector2(184f, 14f);
        HorizontalLayoutGroup layout =
            bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 3f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
        return bar;
    }

    private static Button EnsureTabButton(
        Transform parent,
        string objectName,
        string label)
    {
        Transform existing = parent.Find(objectName);
        if (existing != null) return existing.GetComponent<Button>();
        return ButtonObject(objectName, parent, label, Accent);
    }

    private static RectTransform EnsureFullRect(
        Transform parent,
        string objectName)
    {
        Transform existing = parent.Find(objectName);
        if (existing != null) return existing as RectTransform;

        RectTransform rect = RectObject(objectName, parent);
        Stretch(rect);
        return rect;
    }

    private static bool MoveExistingSchedule(
        Transform background,
        Transform schedulingTab)
    {
        string[] names =
        {
            "ScheduleBackground",
            "ScheduleControls",
            "ScheduleScrollView"
        };

        bool movedAny = false;
        for (int i = 0; i < names.Length; i++)
        {
            Transform child = background.Find(names[i]);
            if (child != null)
            {
                child.SetParent(schedulingTab, false);
                movedAny = true;
            }
        }

        return movedAny;
    }

    private static void EnsureScheduleRoom(Transform schedulingTab)
    {
        MoveRectDown(schedulingTab.Find("ScheduleBackground"), 13f, false);
        MoveRectDown(schedulingTab.Find("ScheduleControls"), 13f, false);
        MoveRectDown(schedulingTab.Find("ScheduleScrollView"), 13f, true);
    }

    private static void MoveRectDown(
        Transform target,
        float amount,
        bool preserveBottom)
    {
        if (!(target is RectTransform rect))
        {
            return;
        }

        if (preserveBottom)
        {
            rect.offsetMax = new Vector2(
                rect.offsetMax.x,
                rect.offsetMax.y - amount);
        }
        else
        {
            rect.anchoredPosition += Vector2.down * amount;
        }

    }

    private static void BuildVehiclesTab(
        IndustrialBuildingInfoPanel panel,
        RectTransform tab,
        GameObject rowPrefab,
        out TMP_Text countText,
        out ScrollRect scrollRect,
        out RectTransform content,
        out GameObject emptyState)
    {
        Transform existingHeader = tab.Find("VehiclesHeader");
        countText = existingHeader != null
            ? existingHeader.Find("VehicleCount")?.GetComponent<TMP_Text>()
            : null;
        if (existingHeader == null)
        {
            RectTransform header = RectObject("VehiclesHeader", tab);
            PlaceTop(header, 6f, 6f, 44f, 14f);
            TMP_Text heading = Text(
                "Heading",
                header,
                "Industrial vehicles",
                9f,
                TextAlignmentOptions.MidlineLeft,
                TextColor);
            heading.rectTransform.anchorMin = Vector2.zero;
            heading.rectTransform.anchorMax = Vector2.one;
            heading.rectTransform.offsetMin = Vector2.zero;
            heading.rectTransform.offsetMax = new Vector2(-75f, 0f);
            countText = Text(
                "VehicleCount",
                header,
                "0 vehicles",
                8f,
                TextAlignmentOptions.MidlineRight,
                MutedText);
            countText.rectTransform.anchorMin = new Vector2(1f, 0f);
            countText.rectTransform.anchorMax = Vector2.one;
            countText.rectTransform.pivot = new Vector2(1f, 0.5f);
            countText.rectTransform.sizeDelta = new Vector2(72f, 0f);
        }

        EnsureVehicleColumnHeader(tab);

        Transform existingScroll = tab.Find("OwnedVehiclesScrollView");
        if (existingScroll == null)
        {
            content = CreateScrollView(tab, out scrollRect);
        }
        else
        {
            scrollRect = existingScroll.GetComponent<ScrollRect>();
            content = scrollRect != null ? scrollRect.content : null;
        }

        MoveVehicleScrollBelowColumnHeader(scrollRect);

        Transform existingEmpty = content != null
            ? content.Find("EmptyOwnedVehicles")
            : null;
        if (existingEmpty == null && content != null)
        {
            TMP_Text empty = Text(
                "EmptyOwnedVehicles",
                content,
                "No industrial vehicles\nDrag a compatible vehicle here",
                9f,
                TextAlignmentOptions.Center,
                MutedText);
            Layout(empty.gameObject, -1f, 42f, 1f);
            emptyState = empty.gameObject;
        }
        else
        {
            emptyState = existingEmpty != null
                ? existingEmpty.gameObject
                : null;
        }

        IndustrialVehicleDropTarget dropTarget =
            tab.GetComponent<IndustrialVehicleDropTarget>();
        if (dropTarget == null)
        {
            dropTarget = tab.gameObject.AddComponent<
                IndustrialVehicleDropTarget>();
        }

        EnsureIndustrialVehicleHighlighters(
            tab,
            scrollRect,
            out GameObject valid,
            out GameObject full,
            out GameObject invalid);

        SerializedObject dropObject = new SerializedObject(dropTarget);
        SetReference(dropObject, "industrialPanel", panel);
        SetReference(dropObject, "validDropHighlighter", valid);
        SetReference(dropObject, "fullSpaceHighlighter", full);
        SetReference(dropObject, "invalidDropHighlighter", invalid);
        dropObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ApplyVehicleListPolish(
        IndustrialBuildingInfoPanel panel,
        Transform tab)
    {
        EnsureVehicleColumnHeader(tab);
        ScrollRect scrollRect = tab.Find("OwnedVehiclesScrollView")
            ?.GetComponent<ScrollRect>();
        MoveVehicleScrollBelowColumnHeader(scrollRect);

        IndustrialVehicleDropTarget dropTarget =
            tab.GetComponent<IndustrialVehicleDropTarget>();
        if (dropTarget == null)
        {
            dropTarget = tab.gameObject.AddComponent<
                IndustrialVehicleDropTarget>();
        }

        EnsureIndustrialVehicleHighlighters(
            tab,
            scrollRect,
            out GameObject valid,
            out GameObject full,
            out GameObject invalid);

        SerializedObject dropObject = new SerializedObject(dropTarget);
        SetReference(dropObject, "industrialPanel", panel);
        SetReference(dropObject, "validDropHighlighter", valid);
        SetReference(dropObject, "fullSpaceHighlighter", full);
        SetReference(dropObject, "invalidDropHighlighter", invalid);
        dropObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static RectTransform EnsureVehicleColumnHeader(Transform tab)
    {
        Transform existing = tab.Find("VehicleColumnHeader");
        if (existing != null)
        {
            return existing as RectTransform;
        }

        GameObject headerObject = ImageObject(
            "VehicleColumnHeader",
            tab,
            new Color32(37, 43, 54, 255));
        RectTransform header = headerObject.GetComponent<RectTransform>();
        PlaceTop(header, 9f, 17f, 60f, 12f);

        HorizontalLayoutGroup layout =
            headerObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 3f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        TMP_Text name = Text(
            "Name",
            header,
            "Name",
            7f,
            TextAlignmentOptions.MidlineLeft,
            MutedText);
        Layout(name.gameObject, -1f, -1f, 1f);

        TMP_Text status = Text(
            "Status",
            header,
            "Status",
            7f,
            TextAlignmentOptions.Center,
            MutedText);
        Layout(status.gameObject, 46f, -1f, 0f);

        TMP_Text capacity = Text(
            "Capacity",
            header,
            "Capacity",
            7f,
            TextAlignmentOptions.Center,
            MutedText);
        Layout(capacity.gameObject, 36f, -1f, 0f);

        TMP_Text carrying = Text(
            "Carrying",
            header,
            "Carrying",
            7f,
            TextAlignmentOptions.Center,
            MutedText);
        Layout(carrying.gameObject, 78f, -1f, 0f);

        RectTransform removeSpacer = RectObject("RemoveSpacer", header);
        Layout(removeSpacer.gameObject, 18f, -1f, 0f);
        return header;
    }

    private static void MoveVehicleScrollBelowColumnHeader(
        ScrollRect scrollRect)
    {
        if (scrollRect == null ||
            !(scrollRect.transform is RectTransform scrollTransform))
        {
            return;
        }

        scrollTransform.offsetMax = new Vector2(
            scrollTransform.offsetMax.x,
            Mathf.Min(scrollTransform.offsetMax.y, -74f));
    }

    private static void EnsureIndustrialVehicleHighlighters(
        Transform tab,
        ScrollRect scrollRect,
        out GameObject valid,
        out GameObject full,
        out GameObject invalid)
    {
        // These are deliberately kept as direct children of the vehicles tab.
        // That makes the objects visible and editable in the authored panel
        // hierarchy instead of silently substituting housing-panel clones.
        valid = tab.Find("ValidVehicleDropHighlight")?.gameObject;
        full = tab.Find("FullVehicleDropHighlight")?.gameObject ??
               tab.Find("NotEnoughSpaceHighlighter")?.gameObject;
        invalid = tab.Find("InvalidVehicleDropHighlight")?.gameObject;

        if (valid == null)
        {
            valid = EnsureHighlighter(
                tab,
                "ValidVehicleDropHighlight",
                "Drop vehicle here",
                new Color(0.25f, 0.75f, 0.35f, 0.32f));
        }

        if (full == null)
        {
            full = CloneIndustrialHighlighter(
                valid,
                tab,
                "FullVehicleDropHighlight",
                "Not enough vehicle space");
        }

        if (invalid == null)
        {
            // Start from the authored negative-state overlay so its sprite,
            // border, pivots and layout remain consistent with the user's UI.
            invalid = CloneIndustrialHighlighter(
                full,
                tab,
                "InvalidVehicleDropHighlight",
                "Vehicle type not allowed");
        }

        valid.SetActive(false);
        full.SetActive(false);
        invalid.SetActive(false);
        valid.transform.SetAsLastSibling();
        full.transform.SetAsLastSibling();
        invalid.transform.SetAsLastSibling();

        RemoveGeneratedScrollHighlighter(
            scrollRect,
            "AdditionAreaHighlighter");
        RemoveGeneratedScrollHighlighter(
            scrollRect,
            "NotEnoughSpaceHighlighter");
        RemoveGeneratedScrollHighlighter(scrollRect, "BadTypeHighlighter");
    }

    private static GameObject CloneIndustrialHighlighter(
        GameObject source,
        Transform parent,
        string objectName,
        string message)
    {
        GameObject clone = Object.Instantiate(source, parent, false);
        clone.name = objectName;
        clone.SetActive(false);

        TMP_Text[] labels = clone.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i].text = message;
            labels[i].raycastTarget = false;
        }

        Graphic[] graphics = clone.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            graphics[i].raycastTarget = false;
        }

        return clone;
    }

    private static void RemoveGeneratedScrollHighlighter(
        ScrollRect scrollRect,
        string objectName)
    {
        Transform generated = scrollRect != null
            ? scrollRect.transform.Find(objectName)
            : null;
        if (generated != null)
        {
            Object.DestroyImmediate(generated.gameObject);
        }
    }

    private static GameObject EnsureHighlighter(
        Transform parent,
        string name,
        string message,
        Color color)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing.gameObject;

        GameObject highlighter = ImageObject(name, parent, color);
        RectTransform rect = highlighter.GetComponent<RectTransform>();
        Stretch(rect);
        rect.offsetMin = new Vector2(6f, 6f);
        rect.offsetMax = new Vector2(-6f, -61f);
        highlighter.GetComponent<Image>().raycastTarget = false;
        TMP_Text label = Text(
            "Label",
            rect,
            message,
            9f,
            TextAlignmentOptions.Center,
            TextColor);
        Stretch(label.rectTransform);
        label.raycastTarget = false;
        highlighter.transform.SetAsLastSibling();
        return highlighter;
    }

    private static RectTransform CreateScrollView(
        Transform parent,
        out ScrollRect scrollRect)
    {
        GameObject scrollObject = ImageObject(
            "OwnedVehiclesScrollView",
            parent,
            new Color32(21, 25, 32, 255));
        RectTransform scrollTransform =
            scrollObject.GetComponent<RectTransform>();
        scrollTransform.anchorMin = Vector2.zero;
        scrollTransform.anchorMax = Vector2.one;
        scrollTransform.offsetMin = new Vector2(6f, 6f);
        scrollTransform.offsetMax = new Vector2(-6f, -61f);

        scrollRect = scrollObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.inertia = false;
        scrollRect.scrollSensitivity = 18f;

        GameObject viewport = ImageObject(
            "Viewport",
            scrollObject.transform,
            new Color32(255, 255, 255, 4));
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        viewportRect.offsetMax = new Vector2(-8f, 0f);
        viewport.AddComponent<RectMask2D>();

        RectTransform content = RectObject("Content", viewport.transform);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout =
            content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(3, 3, 3, 3);
        layout.spacing = 3f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter =
            content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject scrollbarObject = ImageObject(
            "Scrollbar",
            scrollObject.transform,
            new Color32(42, 49, 61, 255));
        RectTransform scrollbarRect =
            scrollbarObject.GetComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = Vector2.one;
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.sizeDelta = new Vector2(7f, 0f);
        GameObject handle = ImageObject(
            "Handle",
            scrollbarObject.transform,
            Accent);
        Stretch(handle.GetComponent<RectTransform>());
        Scrollbar scrollbar = scrollbarObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle.GetComponent<RectTransform>();
        scrollbar.targetGraphic = handle.GetComponent<Image>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        scrollRect.viewport = viewportRect;
        scrollRect.content = content;
        scrollRect.verticalScrollbar = scrollbar;
        scrollRect.verticalScrollbarVisibility =
            ScrollRect.ScrollbarVisibility.AutoHide;
        return content;
    }

    private static GameObject EnsureVehicleRowPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(
            RowPrefabPath);
        if (existing != null)
        {
            ApplyDropdownFont(existing);
            return existing;
        }

        GameObject row = ImageObject(
            "IndustrialVehicleRow",
            null,
            PanelSection);
        Layout(row, 280f, 34f, 0f);
        HorizontalLayoutGroup layout =
            row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(3, 3, 3, 3);
        layout.spacing = 3f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        IndustrialVehicleRowUI view =
            row.AddComponent<IndustrialVehicleRowUI>();
        GameObject iconObject = ImageObject(
            "VehicleIcon",
            row.transform,
            new Color32(110, 150, 205, 255));
        Image icon = iconObject.GetComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        Layout(iconObject, 24f, 24f, 0f);

        TMP_Text name = Text(
            "VehicleName",
            row.transform,
            "Vehicle",
            8f,
            TextAlignmentOptions.MidlineLeft,
            TextColor);
        Layout(name.gameObject, 50f, -1f, 1f);

        TMP_Text status = Text(
            "VehicleStatus",
            row.transform,
            "Parked",
            7f,
            TextAlignmentOptions.Center,
            MutedText);
        Layout(status.gameObject, 46f, -1f, 0f);

        TMP_Text capacity = Text(
            "VehicleCapacity",
            row.transform,
            "Cap 0",
            7f,
            TextAlignmentOptions.Center,
            TextColor);
        Layout(capacity.gameObject, 36f, -1f, 0f);

        TMP_DefaultControls.Resources dropdownResources = default;
        GameObject dropdownObject =
            TMP_DefaultControls.CreateDropdown(dropdownResources);
        dropdownObject.name = "CarriedOutputDropdown";
        dropdownObject.transform.SetParent(row.transform, false);
        TMP_Dropdown dropdown = dropdownObject.GetComponent<TMP_Dropdown>();
        Layout(dropdownObject, 78f, 24f, 0f);
        if (dropdown.captionText != null)
        {
            dropdown.captionText.fontSize = 7f;
        }

        TMP_Text[] dropdownTexts =
            dropdownObject.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < dropdownTexts.Length; i++)
        {
            dropdownTexts[i].fontSize = 7f;
            dropdownTexts[i].font = name.font;
        }

        Button remove = ButtonObject(
            "RemoveVehicle",
            row.transform,
            "X",
            new Color32(153, 67, 63, 255));
        Layout(remove.gameObject, 18f, 24f, 0f);

        SerializedObject rowObject = new SerializedObject(view);
        SetReference(rowObject, "vehicleIcon", icon);
        SetReference(rowObject, "vehicleNameText", name);
        SetReference(rowObject, "vehicleStatusText", status);
        SetReference(rowObject, "vehicleCapacityText", capacity);
        SetReference(rowObject, "carriedResourceDropdown", dropdown);
        SetReference(rowObject, "removeVehicleButton", remove);
        rowObject.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
            row,
            RowPrefabPath);
        Object.DestroyImmediate(row);
        return prefab;
    }

    private static void ApplyDropdownFont(GameObject rowPrefab)
    {
        TMP_Dropdown dropdown = rowPrefab
            .GetComponentInChildren<TMP_Dropdown>(true);
        TMP_Text sourceText = rowPrefab.transform
            .Find("VehicleName")
            ?.GetComponent<TMP_Text>();
        if (dropdown == null || sourceText == null || sourceText.font == null)
        {
            return;
        }

        TMP_Text[] dropdownTexts = dropdown
            .GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < dropdownTexts.Length; i++)
        {
            if (dropdownTexts[i].font == sourceText.font) continue;
            dropdownTexts[i].font = sourceText.font;
            EditorUtility.SetDirty(dropdownTexts[i]);
        }
    }

    private static GameObject ImageObject(
        string name,
        Transform parent,
        Color color)
    {
        GameObject result = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        result.transform.SetParent(parent, false);
        result.GetComponent<Image>().color = color;
        return result;
    }

    private static RectTransform RectObject(string name, Transform parent)
    {
        GameObject result = new GameObject(name, typeof(RectTransform));
        result.transform.SetParent(parent, false);
        return result.GetComponent<RectTransform>();
    }

    private static TMP_Text Text(
        string name,
        Transform parent,
        string value,
        float size,
        TextAlignmentOptions alignment,
        Color color)
    {
        GameObject result = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        result.transform.SetParent(parent, false);
        TMP_Text text = result.GetComponent<TMP_Text>();
        text.text = value;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static Button ButtonObject(
        string name,
        Transform parent,
        string label,
        Color color)
    {
        GameObject result = ImageObject(name, parent, color);
        Button button = result.AddComponent<Button>();
        button.targetGraphic = result.GetComponent<Image>();
        TMP_Text text = Text(
            "Label",
            result.transform,
            label,
            8f,
            TextAlignmentOptions.Center,
            TextColor);
        Stretch(text.rectTransform);
        return button;
    }

    private static LayoutElement Layout(
        GameObject target,
        float width,
        float height,
        float flexibleWidth)
    {
        LayoutElement element = target.GetComponent<LayoutElement>();
        if (element == null) element = target.AddComponent<LayoutElement>();
        if (width >= 0f) element.preferredWidth = width;
        if (height >= 0f) element.preferredHeight = height;
        element.flexibleWidth = flexibleWidth;
        element.flexibleHeight = 0f;
        return element;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void PlaceTop(
        RectTransform rect,
        float left,
        float right,
        float top,
        float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(left, -top - height);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void SetReference(
        SerializedObject serializedObject,
        string propertyName,
        Object value)
    {
        SerializedProperty property =
            serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.objectReferenceValue = value;
        }
    }
}
