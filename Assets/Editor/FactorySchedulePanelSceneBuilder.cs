using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Rebuilds the compact factory schedule panel and its reusable row prefab.
/// </summary>
public static class FactorySchedulePanelSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string RowPrefabPath =
        "Assets/Prefabs/UI/FactoryScheduleRow.prefab";

    private static readonly Color PanelColor =
        new Color32(27, 31, 39, 250);
    private static readonly Color SectionColor =
        new Color32(39, 45, 57, 255);
    private static readonly Color ControlColor =
        new Color32(53, 62, 78, 255);
    private static readonly Color AccentColor =
        new Color32(80, 145, 255, 255);
    private static readonly Color TextColor =
        new Color32(240, 243, 248, 255);
    private static readonly Color MutedTextColor =
        new Color32(174, 183, 197, 255);

    private static TMP_FontAsset _font;

    [MenuItem("Tools/Mini Colonies/Rebuild Factory Schedule Panel")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) return;

        GameObject panelObject = FindInScene(scene, "IndustrialBuildingPanel");
        if (panelObject == null) return;

        IndustrialBuildingInfoPanel panel =
            panelObject.GetComponent<IndustrialBuildingInfoPanel>();
        if (panel == null) return;

        TMP_Text existingText =
            panelObject.GetComponentInChildren<TMP_Text>(true);
        _font = existingText != null
            ? existingText.font
            : TMP_Settings.defaultFontAsset;

        Sprite arrowSprite = AssetDatabase
            .LoadAllAssetsAtPath("Assets/Sprites/UI/ScheduleSprites.png")
            .OfType<Sprite>()
            .FirstOrDefault(sprite =>
                sprite.name == "ScheduleSprites_4");

        RemovePreviousGeneratedHierarchy(panelObject.transform);

        RectTransform panelRect = panelObject.transform as RectTransform;
        panelRect.sizeDelta = new Vector2(300f, 200f);

        RectTransform background = EnsureBackground(panelObject.transform);
        ClearChildrenExceptScheduleBackground(background);
        Image backgroundImage = background.GetComponent<Image>();
        if (backgroundImage == null)
        {
            backgroundImage = background.gameObject.AddComponent<Image>();
        }
        backgroundImage.color = PanelColor;
        Stretch(background);

        TMP_Text title = Text(
            "BuildingName", background, "Factory", 15f,
            TextAlignmentOptions.Center, TextColor);
        PlaceTop(title.rectTransform, 74f, 30f, 4f, 23f);
        title.fontStyle = FontStyles.Bold;

        Toggle openToggle = CreateCompactToggle(background);
        Button closeButton = ButtonObject(
            "Close", background, "×", 20f,
            new Color32(91, 98, 112, 255));
        PlaceTopRight(
            closeButton.GetComponent<RectTransform>(), 5f, 5f, 20f, 20f);

        RectTransform scheduleBackground =
            background.Find("ScheduleBackground") as RectTransform;
        if (scheduleBackground == null)
        {
            scheduleBackground = ImageObject(
                "ScheduleBackground", background, SectionColor)
                .GetComponent<RectTransform>();
        }
        scheduleBackground.gameObject.SetActive(true);
        PlaceTop(scheduleBackground, 6f, 6f, 31f, 18f);
        Image scheduleImage =
            scheduleBackground.GetComponent<Image>();
        if (scheduleImage == null)
        {
            scheduleImage =
                scheduleBackground.gameObject.AddComponent<Image>();
        }
        scheduleImage.color = SectionColor;
        ClearChildren(scheduleBackground);

        for (int hour = 0; hour < GameTimeManager.HoursPerDay; hour++)
        {
            GameObject cell = ImageObject(
                $"Hour {hour:00}", scheduleBackground, ControlColor);
            Image cellImage = cell.GetComponent<Image>();
            cellImage.sprite = null;
            RectTransform cellRect = cell.GetComponent<RectTransform>();
            cellRect.anchorMin = new Vector2(
                hour / (float)GameTimeManager.HoursPerDay, 0f);
            cellRect.anchorMax = new Vector2(
                (hour + 1f) / GameTimeManager.HoursPerDay, 1f);
            cellRect.offsetMin = new Vector2(0.5f, 1f);
            cellRect.offsetMax = new Vector2(-0.5f, -1f);
        }

        GameObject arrow = ImageObject(
            "CurrentHourArrow", scheduleBackground, Color.white);
        Image arrowImage = arrow.GetComponent<Image>();
        arrowImage.sprite = arrowSprite;
        arrowImage.preserveAspect = true;
        arrowImage.raycastTarget = false;
        RectTransform arrowRect = arrow.GetComponent<RectTransform>();
        arrowRect.anchorMin = new Vector2(0f, 1f);
        arrowRect.anchorMax = new Vector2(0f, 1f);
        arrowRect.pivot = new Vector2(0.5f, 0f);
        arrowRect.anchoredPosition = new Vector2(0f, -1f);
        arrowRect.sizeDelta = new Vector2(8f, 5f);

        RectTransform controls = RectObject("ScheduleControls", background);
        PlaceTop(controls, 6f, 6f, 53f, 21f);
        HorizontalLayoutGroup controlsLayout =
            controls.gameObject.AddComponent<HorizontalLayoutGroup>();
        controlsLayout.spacing = 4f;
        controlsLayout.childAlignment = TextAnchor.MiddleLeft;
        controlsLayout.childControlWidth = false;
        controlsLayout.childControlHeight = true;
        controlsLayout.childForceExpandWidth = false;
        controlsLayout.childForceExpandHeight = true;

        TMP_Text durationLabel = Text(
            "DurationLabel", controls, "Shift length", 9f,
            TextAlignmentOptions.MidlineLeft, MutedTextColor);
        Layout(durationLabel.gameObject, preferredWidth: 55f);
        TMP_Text duration = Text(
            "Duration", controls, "8h", 10f,
            TextAlignmentOptions.Center, TextColor);
        Layout(duration.gameObject, preferredWidth: 28f);
        TMP_Text status = Text(
            "Status", controls, string.Empty, 8f,
            TextAlignmentOptions.MidlineLeft,
            new Color32(246, 177, 86, 255));
        Layout(status.gameObject, flexibleWidth: 1f);
        Button addShift = ButtonObject(
            "AddShift", controls, "+ Shift", 54f, AccentColor);

        RectTransform shiftContent = CreateCompactScrollView(
            background, out ScrollRect shiftScroll);
        TMP_Text emptyState = Text(
            "EmptySchedule", shiftContent,
            "No shifts scheduled", 10f,
            TextAlignmentOptions.Center, MutedTextColor);
        Layout(emptyState.gameObject, preferredHeight: 45f);

        FactoryShiftRowView rowPrefab = CreateRowPrefab();

        SetReference(panel, "buildingNameText", title);
        SetReference(panel, "titleText", title);
        SetReference(panel, "summaryText", null);
        SetReference(panel, "openToggle", openToggle);
        SetReference(panel, "closeButton", closeButton);
        SetReference(panel, "durationText", duration);
        SetReference(panel, "statusText", status);
        SetReference(panel, "addShiftButton", addShift);
        SetReference(panel, "hourCellsRoot", scheduleBackground);
        SetReference(panel, "currentHourArrow", arrowRect);
        SetReference(panel, "currentHourArrowSprite", arrowSprite);
        SetReference(panel, "shiftScrollRect", shiftScroll);
        SetReference(panel, "shiftListContent", shiftContent);
        SetReference(panel, "emptyScheduleState", emptyState.gameObject);
        SetReference(panel, "shiftRowPrefab", rowPrefab);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Rebuilt compact 300x200 factory schedule panel.");
    }

    private static FactoryShiftRowView CreateRowPrefab()
    {
        GameObject row = ImageObject(
            "FactoryScheduleRow", null, SectionColor);
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(280f, 88f);
        LayoutElement rowElement = Layout(row, preferredHeight: 88f);
        VerticalLayoutGroup rowLayout =
            row.AddComponent<VerticalLayoutGroup>();
        rowLayout.padding = new RectOffset(2, 2, 2, 2);
        rowLayout.spacing = 1f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = false;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;
        FactoryShiftRowView view = row.AddComponent<FactoryShiftRowView>();

        GameObject titleRow = Horizontal(row.transform, "TitleRow", 14f, 2f);
        TMP_Text title = Text(
            "Title", titleRow.transform, "Shift 1", 8f,
            TextAlignmentOptions.MidlineLeft, TextColor);
        title.fontStyle = FontStyles.Bold;
        Layout(title.gameObject, flexibleWidth: 1f);
        GameObject duplicateButtonObject = ImageObject(
            "DuplicateShift",
            titleRow.transform,
            new Color32(91, 98, 112, 255));
        Layout(duplicateButtonObject, 12f, 12f);
        Button duplicateButton = duplicateButtonObject.AddComponent<Button>();
        duplicateButton.targetGraphic =
            duplicateButtonObject.GetComponent<Image>();
        GameObject duplicateIconObject = ImageObject(
            "PlaceholderIcon",
            duplicateButtonObject.transform,
            MutedTextColor);
        Image duplicateIcon = duplicateIconObject.GetComponent<Image>();
        duplicateIcon.sprite = null;
        duplicateIcon.preserveAspect = true;
        duplicateIcon.raycastTarget = false;
        Stretch(duplicateIcon.rectTransform);
        duplicateIcon.rectTransform.offsetMin = new Vector2(3f, 3f);
        duplicateIcon.rectTransform.offsetMax = new Vector2(-3f, -3f);
        GameObject activeToggleObject = ImageObject(
            "ShiftActiveToggle",
            titleRow.transform,
            new Color32(170, 68, 64, 255));
        Layout(activeToggleObject, 45f, 12f);
        Toggle activeToggle = activeToggleObject.AddComponent<Toggle>();
        TMP_Text activeToggleLabel = Text(
            "Label",
            activeToggleObject.transform,
            "Inactive",
            7f,
            TextAlignmentOptions.Center,
            TextColor);
        Stretch(activeToggleLabel.rectTransform);
        activeToggle.targetGraphic = activeToggleObject.GetComponent<Image>();
        activeToggle.graphic = null;
        activeToggle.isOn = false;
        Button delete = ButtonObject(
            "DeleteShift", titleRow.transform, "×", 20f,
            new Color32(153, 67, 63, 255));

        GameObject timeRow = DataRow(row.transform, "StartEndRow");
        AddRowLabel(timeRow.transform, "Shift time");
        Button earlier = ButtonObject("Earlier", timeRow.transform, "−", 18f);
        TMP_Text start = ValueText(timeRow.transform, "StartHour", "00:00", 35f);
        Button later = ButtonObject("Later", timeRow.transform, "+", 18f);
        TMP_Text separator = Text(
            "Separator", timeRow.transform, "to", 8f,
            TextAlignmentOptions.Center, MutedTextColor);
        Layout(separator.gameObject, preferredWidth: 12f);
        TMP_Text end = ValueText(timeRow.transform, "EndHour", "08:00", 35f);

        GameObject preparationRow = DataRow(row.transform, "PreparationRow");
        AddRowLabel(preparationRow.transform, "Prep hours");
        Button earlierPreparation = ButtonObject(
            "Earlier", preparationRow.transform, "−", 18f);
        TMP_Text preparation = ValueText(
            preparationRow.transform, "PreparationHour", "23:00", 35f);
        Button laterPreparation = ButtonObject(
            "Later", preparationRow.transform, "+", 18f);

        GameObject popsRow = DataRow(row.transform, "PopulationRow");
        GameObject popsIcon = ImageObject(
            "PopsIcon",
            popsRow.transform,
            new Color32(102, 204, 255, 255));
        Image popsIconImage = popsIcon.GetComponent<Image>();
        popsIconImage.sprite = null;
        popsIconImage.preserveAspect = true;
        popsIconImage.raycastTarget = false;
        Layout(popsIcon, 10f, 10f);
        AddRowLabel(popsRow.transform, "Prepared pops");
        Button decreasePops = ButtonObject(
            "Decrease", popsRow.transform, "−", 18f);
        TMP_Text pops = ValueText(
            popsRow.transform,
            "Amount",
            "0/0 • 0%",
            65f);
        Button increasePops = ButtonObject(
            "Increase", popsRow.transform, "+", 18f);

        TMP_Text resourcesHeading = Text(
            "ResourcesHeading", row.transform,
            "Resources (to deliver/max processing)", 9f,
            TextAlignmentOptions.MidlineLeft, MutedTextColor);
        Layout(resourcesHeading.gameObject, preferredHeight: 10f);

        RectTransform resources = RectObject("ResourceRows", row.transform);
        LayoutElement resourcesElement =
            Layout(resources.gameObject, preferredHeight: 13f);
        VerticalLayoutGroup resourcesLayout =
            resources.gameObject.AddComponent<VerticalLayoutGroup>();
        resourcesLayout.spacing = 1f;
        resourcesLayout.childControlWidth = true;
        resourcesLayout.childControlHeight = true;
        resourcesLayout.childForceExpandWidth = true;
        resourcesLayout.childForceExpandHeight = false;
        TMP_Text noResources = Text(
            "NoResources", resources, "No resource inputs", 8f,
            TextAlignmentOptions.Center, MutedTextColor);
        Layout(noResources.gameObject, preferredHeight: 12f);

        GameObject resourceTemplateObject = DataRow(
            row.transform, "ResourceRowTemplate", 13f);
        LayoutElement ignoredTemplateLayout =
            resourceTemplateObject.GetComponent<LayoutElement>();
        ignoredTemplateLayout.ignoreLayout = true;
        FactoryResourceAmountRowView resourceTemplate =
            resourceTemplateObject.AddComponent<FactoryResourceAmountRowView>();
        GameObject resourceIcon = ImageObject(
            "ResourceIcon",
            resourceTemplateObject.transform,
            new Color32(237, 176, 87, 255));
        Image resourceIconImage = resourceIcon.GetComponent<Image>();
        resourceIconImage.sprite = null;
        resourceIconImage.preserveAspect = true;
        resourceIconImage.raycastTarget = false;
        Layout(resourceIcon, 10f, 10f);
        TMP_Text resourceName = Text(
            "ResourceName", resourceTemplateObject.transform,
            "Resource", 8f, TextAlignmentOptions.MidlineLeft, TextColor);
        Layout(resourceName.gameObject, flexibleWidth: 1f);
        Button resourceDecrease = ButtonObject(
            "Decrease", resourceTemplateObject.transform, "−", 18f);
        TMP_Text resourceAmount = ValueText(
            resourceTemplateObject.transform, "Amount", "0/0", 48f);
        Button resourceIncrease = ButtonObject(
            "Increase", resourceTemplateObject.transform, "+", 18f);
        resourceTemplateObject.SetActive(false);

        SetReference(resourceTemplate, "resourceNameText", resourceName);
        SetReference(resourceTemplate, "amountText", resourceAmount);
        SetReference(resourceTemplate, "decreaseButton", resourceDecrease);
        SetReference(resourceTemplate, "increaseButton", resourceIncrease);

        SetReference(view, "layoutElement", rowElement);
        SetReference(view, "titleText", title);
        SetReference(view, "duplicateButton", duplicateButton);
        SetReference(view, "duplicateIconImage", duplicateIcon);
        SetReference(view, "shiftActiveToggle", activeToggle);
        SetReference(view, "deleteButton", delete);
        SetReference(view, "startHourText", start);
        SetReference(view, "endHourText", end);
        SetReference(view, "earlierStartButton", earlier);
        SetReference(view, "laterStartButton", later);
        SetReference(view, "preparationHourText", preparation);
        SetReference(view, "earlierPreparationButton", earlierPreparation);
        SetReference(view, "laterPreparationButton", laterPreparation);
        SetReference(view, "desiredPopsText", pops);
        SetReference(view, "decreasePopsButton", decreasePops);
        SetReference(view, "increasePopsButton", increasePops);
        SetReference(view, "resourcesHeadingText", resourcesHeading);
        SetReference(view, "resourcesRoot", resources);
        SetReference(view, "resourcesLayoutElement", resourcesElement);
        SetReference(view, "noResourcesText", noResources);
        SetReference(view, "resourceRowTemplate", resourceTemplate);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
            row, RowPrefabPath);
        Object.DestroyImmediate(row);
        return prefab.GetComponent<FactoryShiftRowView>();
    }

    private static RectTransform CreateCompactScrollView(
        Transform parent,
        out ScrollRect scrollRect)
    {
        GameObject scrollObject = ImageObject(
            "ScheduleScrollView", parent,
            new Color32(21, 25, 32, 255));
        RectTransform scrollRectTransform =
            scrollObject.GetComponent<RectTransform>();
        scrollRectTransform.anchorMin = Vector2.zero;
        scrollRectTransform.anchorMax = Vector2.one;
        scrollRectTransform.offsetMin = new Vector2(6f, 6f);
        scrollRectTransform.offsetMax = new Vector2(-6f, -78f);

        scrollRect = scrollObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 18f;

        GameObject viewport = ImageObject(
            "Viewport", scrollObject.transform,
            new Color32(255, 255, 255, 4));
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        viewportRect.offsetMax = new Vector2(-9f, 0f);
        viewport.AddComponent<RectMask2D>();

        RectTransform content = RectObject("Content", viewport.transform);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup contentLayout =
            content.gameObject.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(3, 3, 3, 3);
        contentLayout.spacing = 3f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        ContentSizeFitter fitter =
            content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject scrollbarObject = ImageObject(
            "Scrollbar", scrollObject.transform,
            new Color32(42, 49, 61, 255));
        RectTransform scrollbarRect =
            scrollbarObject.GetComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = new Vector2(1f, 1f);
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.sizeDelta = new Vector2(7f, 0f);
        GameObject handle = ImageObject(
            "Handle", scrollbarObject.transform, AccentColor);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        Stretch(handleRect);
        Scrollbar scrollbar = scrollbarObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handle.GetComponent<Image>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        scrollRect.viewport = viewportRect;
        scrollRect.content = content;
        scrollRect.verticalScrollbar = scrollbar;
        scrollRect.verticalScrollbarVisibility =
            ScrollRect.ScrollbarVisibility.AutoHide;
        return content;
    }

    private static Toggle CreateCompactToggle(Transform parent)
    {
        GameObject toggleObject = ImageObject(
            "FactoryOpenToggle",
            parent,
            new Color32(73, 155, 86, 255));
        RectTransform toggleRect =
            toggleObject.GetComponent<RectTransform>();
        toggleRect.anchorMin = new Vector2(0f, 1f);
        toggleRect.anchorMax = new Vector2(0f, 1f);
        toggleRect.pivot = new Vector2(0f, 1f);
        toggleRect.anchoredPosition = new Vector2(6f, -5f);
        toggleRect.sizeDelta = new Vector2(66f, 20f);
        Toggle toggle = toggleObject.AddComponent<Toggle>();
        TMP_Text label = Text(
            "Label", toggleRect, "Open", 9f,
            TextAlignmentOptions.Center, TextColor);
        Stretch(label.rectTransform);

        toggle.targetGraphic = toggleObject.GetComponent<Image>();
        toggle.graphic = null;
        toggle.isOn = true;
        return toggle;
    }

    private static void RemovePreviousGeneratedHierarchy(Transform panel)
    {
        string[] generatedNames =
        {
            "AuthoredFactoryScheduleUI",
            "FactoryScheduleTemplates"
        };

        foreach (string generatedName in generatedNames)
        {
            Transform generated = panel.Find(generatedName);
            if (generated != null)
            {
                Object.DestroyImmediate(generated.gameObject);
            }
        }
    }

    private static RectTransform EnsureBackground(Transform panel)
    {
        RectTransform background = panel.Find("Background") as RectTransform;
        if (background != null) return background;

        return ImageObject("Background", panel, PanelColor)
            .GetComponent<RectTransform>();
    }

    private static void ClearChildrenExceptScheduleBackground(
        RectTransform background)
    {
        Transform scheduleBackground =
            background.Find("ScheduleBackground");
        if (scheduleBackground != null)
        {
            scheduleBackground.SetParent(background.parent, false);
        }

        ClearChildren(background);

        if (scheduleBackground != null)
        {
            scheduleBackground.SetParent(background, false);
        }
    }

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }
    }

    private static GameObject DataRow(
        Transform parent,
        string name,
        float height = 14f)
    {
        GameObject row = Horizontal(parent, name, height, 3f);
        row.AddComponent<Image>().color = new Color32(46, 53, 66, 255);
        return row;
    }

    private static void AddRowLabel(Transform parent, string value)
    {
        TMP_Text label = Text(
            "Label", parent, value, 8f,
            TextAlignmentOptions.MidlineLeft, TextColor);
        Layout(label.gameObject, preferredWidth: 52f);
    }

    private static TMP_Text ValueText(
        Transform parent,
        string name,
        string value,
        float width)
    {
        TMP_Text text = Text(
            name, parent, value, 8f,
            TextAlignmentOptions.Center, TextColor);
        Layout(text.gameObject, preferredWidth: width);
        return text;
    }

    private static GameObject Horizontal(
        Transform parent,
        string name,
        float height,
        float spacing)
    {
        RectTransform rect = RectObject(name, parent);
        Layout(rect.gameObject, preferredHeight: height);
        HorizontalLayoutGroup group =
            rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.spacing = spacing;
        group.childAlignment = TextAnchor.MiddleLeft;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = true;
        return rect.gameObject;
    }

    private static Button ButtonObject(
        string name,
        Transform parent,
        string label,
        float width,
        Color? color = null)
    {
        GameObject buttonObject =
            ImageObject(name, parent, color ?? ControlColor);
        Layout(buttonObject, width, 12f);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();
        TMP_Text buttonText = Text(
            "Label", buttonObject.transform, label, 8f,
            TextAlignmentOptions.Center, TextColor);
        Stretch(buttonText.rectTransform);
        return button;
    }

    private static TMP_Text Text(
        string name,
        Transform parent,
        string value,
        float fontSize,
        TextAlignmentOptions alignment,
        Color color)
    {
        RectTransform rect = RectObject(name, parent);
        TextMeshProUGUI text =
            rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (_font != null) text.font = _font;
        text.text = value;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject ImageObject(
        string name,
        Transform parent,
        Color color)
    {
        RectTransform rect = RectObject(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        return rect.gameObject;
    }

    private static RectTransform RectObject(
        string name,
        Transform parent)
    {
        var gameObject = new GameObject(name, typeof(RectTransform));
        if (parent != null)
        {
            gameObject.layer = parent.gameObject.layer;
            gameObject.transform.SetParent(parent, false);
        }
        return gameObject.GetComponent<RectTransform>();
    }

    private static LayoutElement Layout(
        GameObject target,
        float preferredWidth = -1f,
        float preferredHeight = -1f,
        float flexibleWidth = -1f,
        float flexibleHeight = -1f)
    {
        LayoutElement element = target.GetComponent<LayoutElement>();
        if (element == null) element = target.AddComponent<LayoutElement>();
        if (preferredWidth >= 0f) element.preferredWidth = preferredWidth;
        if (preferredHeight >= 0f) element.preferredHeight = preferredHeight;
        if (flexibleWidth >= 0f) element.flexibleWidth = flexibleWidth;
        if (flexibleHeight >= 0f) element.flexibleHeight = flexibleHeight;
        return element;
    }

    private static void PlaceTop(
        RectTransform rect,
        float left,
        float right,
        float top,
        float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(left, -top - height);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void PlaceTopRight(
        RectTransform rect,
        float right,
        float top,
        float width,
        float height)
    {
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-right, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetReference(
        Object target,
        string propertyName,
        Object value)
    {
        var serializedObject = new SerializedObject(target);
        SerializedProperty property =
            serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            Debug.LogError(
                $"Missing serialized property {propertyName} on {target}.");
            return;
        }

        property.objectReferenceValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject FindInScene(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform match = FindRecursive(root.transform, objectName);
            if (match != null) return match.gameObject;
        }
        return null;
    }

    private static Transform FindRecursive(
        Transform root,
        string objectName)
    {
        if (root.name == objectName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform match =
                FindRecursive(root.GetChild(i), objectName);
            if (match != null) return match;
        }
        return null;
    }
}
