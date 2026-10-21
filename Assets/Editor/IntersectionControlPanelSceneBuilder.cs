using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Creates the editable intersection-control panel in a loaded scene.</summary>
public static class IntersectionControlPanelSceneBuilder
{
    private const string PanelName = "IntersectionControlPanel";
    private const string GameFontPath =
        "Assets/Fonts/ari-w9500-condensed-display.asset";
    private static readonly Color Panel = new Color32(22, 27, 36, 255);
    private static readonly Color Row = new Color32(42, 49, 61, 255);
    private static readonly Color Detail = new Color32(32, 38, 48, 255);
    private static readonly Color Accent = new Color32(61, 85, 122, 255);
    private static readonly Color TextColor = new Color32(240, 243, 248, 255);
    private static readonly Color MutedText = new Color32(165, 173, 188, 255);

    private sealed class OptionReferences
    {
        public IntersectionRuleType Mode;
        public Button Button;
        public Image IntersectionIcon;
        public Image TypeIcon;
        public GameObject SelectedIndicator;
        public GameObject Details;
        public TMP_Text Dropdown;
    }

    [InitializeOnLoadMethod]
    private static void QueueInstallation()
    {
        EditorApplication.delayCall += InstallMissingPanel;
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += InstallMissingPanel;
    }

    [MenuItem("Tools/Mini Colonies/Install Intersection Control Panel")]
    public static void Install()
    {
        PlayerViewModeManager manager = FindSceneManager();
        if (manager == null)
        {
            Debug.LogWarning("No loaded PlayerViewModeManager was found.");
            return;
        }

        bool wasDirty = manager.gameObject.scene.isDirty;
        bool changed = EnsurePanel(manager, out IntersectionControlPanel panel);
        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            if (!wasDirty) EditorSceneManager.SaveScene(manager.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Installed the editable intersection control panel.");
        }
        else
        {
            Debug.Log("The editable intersection control panel is already installed.");
        }

        Selection.activeGameObject = panel != null ? panel.gameObject : null;
    }

    private static void InstallMissingPanel()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        PlayerViewModeManager manager = FindSceneManager();
        if (manager == null) return;
        Scene scene = manager.gameObject.scene;
        bool wasDirty = scene.isDirty;
        if (!EnsurePanel(manager, out _)) return;
        EditorSceneManager.MarkSceneDirty(scene);
        // Never save over unrelated pending scene edits. The new panel remains
        // visible in the hierarchy and will be included when the user saves.
        if (!wasDirty) EditorSceneManager.SaveScene(scene);
    }

    private static PlayerViewModeManager FindSceneManager() =>
        Resources.FindObjectsOfTypeAll<PlayerViewModeManager>()
            .FirstOrDefault(candidate =>
                candidate != null &&
                !EditorUtility.IsPersistent(candidate) &&
                candidate.gameObject.scene.IsValid());

    private static bool EnsurePanel(
        PlayerViewModeManager manager,
        out IntersectionControlPanel panel)
    {
        panel = Resources.FindObjectsOfTypeAll<IntersectionControlPanel>()
            .FirstOrDefault(candidate =>
                candidate != null &&
                !EditorUtility.IsPersistent(candidate) &&
                candidate.gameObject.scene == manager.gameObject.scene);
        if (panel != null && IsConfigured(panel))
        {
            bool moved = panel.ApplyScreenAnchor();
            bool actionsAdded = EnsureConnectionActions(panel);
            bool smartAdded = EnsureSmartOption(panel);
            var configuredPanel = new SerializedObject(panel);
            var priorityText = configuredPanel.FindProperty("priorityDirectionsText")
                .objectReferenceValue as TMP_Text;
            bool centered = priorityText != null &&
                priorityText.alignment != TextAlignmentOptions.Center;
            if (centered)
            {
                priorityText.alignment = TextAlignmentOptions.Center;
                EditorUtility.SetDirty(priorityText);
            }
            return AssignManagerReference(manager, panel) || moved || centered || actionsAdded || smartAdded;
        }

        if (panel != null)
        {
            if (panel.gameObject == manager.gameObject)
                Object.DestroyImmediate(panel);
            else
                Object.DestroyImmediate(panel.gameObject);
            panel = null;
        }

        Canvas canvas = manager.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            canvas = Resources.FindObjectsOfTypeAll<Canvas>()
                .FirstOrDefault(candidate =>
                    candidate != null &&
                    !EditorUtility.IsPersistent(candidate) &&
                    candidate.gameObject.scene == manager.gameObject.scene);
        }
        if (canvas == null)
        {
            Debug.LogWarning("No loaded Canvas was found for the intersection panel.");
            return false;
        }
        canvas = canvas.rootCanvas;

        GameObject root = ImageObject(PanelName, canvas.transform, Panel);
        root.layer = canvas.gameObject.layer;
        RectTransform panelRect = root.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(1f, 0.5f);
        panelRect.pivot = new Vector2(1f, 0.5f);
        panelRect.anchoredPosition = new Vector2(-20f, 0f);
        panelRect.sizeDelta = new Vector2(260f, 245f);
        Outline outline = root.AddComponent<Outline>();
        outline.effectColor = new Color32(59, 70, 88, 220);
        outline.effectDistance = new Vector2(1f, -1f);
        VerticalLayoutGroup panelLayout = root.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(8, 8, 9, 9);
        panelLayout.spacing = 5f;
        panelLayout.childAlignment = TextAnchor.UpperCenter;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;
        ContentSizeFitter panelFitter = root.AddComponent<ContentSizeFitter>();
        panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CreateHeader(root.transform);
        var options = new List<OptionReferences>(4)
        {
            CreateOption(root.transform, IntersectionRuleType.FreeForAll),
            CreateOption(root.transform, IntersectionRuleType.FIFO),
            CreateOption(root.transform, IntersectionRuleType.PriorityRoad),
            CreateOption(root.transform, IntersectionRuleType.TrafficLight)
        };

        BuildPriorityDetails(
            options[2].Details.transform,
            out TMP_Text priorityValue,
            out Button priorityButton,
            out TMP_Text priorityButtonText);
        BuildTrafficLightDetails(
            options[3].Details.transform,
            out Slider durationSlider,
            out TMP_Text durationValue);

        panel = root.AddComponent<IntersectionControlPanel>();
        SerializedObject serializedPanel = new SerializedObject(panel);
        SetReference(serializedPanel, "overlayCanvas", canvas);
        SetReference(serializedPanel, "panelRect", panelRect);
        SerializedProperty optionsProperty =
            serializedPanel.FindProperty("modeOptions");
        optionsProperty.arraySize = options.Count;
        for (int i = 0; i < options.Count; i++)
        {
            OptionReferences option = options[i];
            SerializedProperty element =
                optionsProperty.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("mode").intValue = (int)option.Mode;
            element.FindPropertyRelative("button").objectReferenceValue = option.Button;
            element.FindPropertyRelative("intersectionIcon").objectReferenceValue =
                option.IntersectionIcon;
            element.FindPropertyRelative("typeIcon").objectReferenceValue = option.TypeIcon;
            element.FindPropertyRelative("selectedIndicator").objectReferenceValue =
                option.SelectedIndicator;
            element.FindPropertyRelative("detailsRoot").objectReferenceValue = option.Details;
            element.FindPropertyRelative("dropdownLabel").objectReferenceValue = option.Dropdown;
        }
        SetReference(serializedPanel, "priorityDirectionsText", priorityValue);
        SetReference(serializedPanel, "choosePriorityRoadButton", priorityButton);
        SetReference(serializedPanel, "choosePriorityRoadButtonText", priorityButtonText);
        SetReference(serializedPanel, "lightDurationSlider", durationSlider);
        SetReference(serializedPanel, "lightDurationValueText", durationValue);
        serializedPanel.ApplyModifiedPropertiesWithoutUndo();
        EnsureSmartOption(panel);
        EnsureConnectionActions(panel);
        panel.ApplyScreenAnchor();
        AssignManagerReference(manager, panel);
        manager.RefreshIntersectionBuilderAppearance();

        for (int i = 0; i < options.Count; i++)
        {
            bool selected = options[i].Mode == IntersectionRuleType.FreeForAll;
            options[i].SelectedIndicator.SetActive(selected);
            options[i].Details.SetActive(selected);
            options[i].Dropdown.text = selected ? "v" : ">";
        }
        root.SetActive(false);
        return true;
    }

    private static bool EnsureConnectionActions(IntersectionControlPanel panel)
    {
        var serialized = new SerializedObject(panel);
        var deleteProperty = serialized.FindProperty("deleteAllConnectionsButton");
        var restoreProperty = serialized.FindProperty("restoreDefaultConnectionsButton");
        if (deleteProperty.objectReferenceValue != null &&
            restoreProperty.objectReferenceValue != null) return false;
        var panelRect = serialized.FindProperty("panelRect").objectReferenceValue as RectTransform;
        if (panelRect == null) return false;

        Transform actions = panelRect.Find("ConnectionActions");
        if (actions == null)
        {
            actions = RectObject("ConnectionActions", panelRect);
            Layout(actions.gameObject, -1f, 68f, 1f);
            var layout = actions.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }
        if (deleteProperty.objectReferenceValue == null)
            deleteProperty.objectReferenceValue = EnsureActionButton(
                actions, "DeleteAllConnections", "Delete all connections", Row);
        if (restoreProperty.objectReferenceValue == null)
            restoreProperty.objectReferenceValue = EnsureActionButton(
                actions, "RestoreDefaultConnections", "Restore to default", Accent);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(panel);
        return true;
    }

    private static Button EnsureActionButton(Transform parent, string name, string caption, Color color)
    {
        var existing = parent.Find(name)?.GetComponent<Button>();
        if (existing != null) return existing;
        GameObject buttonObject = ImageObject(name, parent, color);
        Layout(buttonObject, -1f, 32f, 1f);
        var button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();
        TMP_Text label = Text("Label", buttonObject.transform, caption, 9f, TextColor);
        Stretch(label.rectTransform);
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private static bool IsConfigured(IntersectionControlPanel panel)
    {
        SerializedObject serialized = new SerializedObject(panel);
        SerializedProperty rect = serialized.FindProperty("panelRect");
        SerializedProperty options = serialized.FindProperty("modeOptions");
        return rect != null && rect.objectReferenceValue != null &&
               options != null && options.arraySize >= 4;
    }

    // Extend existing authored panels without replacing their layout or references.
    private static bool EnsureSmartOption(IntersectionControlPanel panel)
    {
        var serialized = new SerializedObject(panel);
        SerializedProperty options = serialized.FindProperty("modeOptions");
        for (int i = 0; i < options.arraySize; i++)
            if (options.GetArrayElementAtIndex(i).FindPropertyRelative("mode").intValue ==
                (int)IntersectionRuleType.SmartTrafficLight) return false;

        var rect = serialized.FindProperty("panelRect").objectReferenceValue as RectTransform;
        if (rect == null) return false;
        OptionReferences option = CreateOption(rect, IntersectionRuleType.SmartTrafficLight);
        Transform actions = rect.Find("ConnectionActions");
        if (actions != null) option.Button.transform.parent.SetSiblingIndex(actions.GetSiblingIndex());
        BuildTrafficLightDetails(option.Details.transform, out Slider slider, out TMP_Text value, true);
        int index = options.arraySize++;
        SerializedProperty element = options.GetArrayElementAtIndex(index);
        element.FindPropertyRelative("mode").intValue = (int)option.Mode;
        element.FindPropertyRelative("button").objectReferenceValue = option.Button;
        element.FindPropertyRelative("intersectionIcon").objectReferenceValue = option.IntersectionIcon;
        element.FindPropertyRelative("typeIcon").objectReferenceValue = option.TypeIcon;
        element.FindPropertyRelative("selectedIndicator").objectReferenceValue = option.SelectedIndicator;
        element.FindPropertyRelative("detailsRoot").objectReferenceValue = option.Details;
        element.FindPropertyRelative("dropdownLabel").objectReferenceValue = option.Dropdown;
        SetReference(serialized, "smartWaitSlider", slider);
        SetReference(serialized, "smartWaitValueText", value);
        option.Details.SetActive(false);
        option.SelectedIndicator.SetActive(false);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(panel);
        return true;
    }

    private static bool AssignManagerReference(
        PlayerViewModeManager manager,
        IntersectionControlPanel panel)
    {
        SerializedObject serializedManager = new SerializedObject(manager);
        SerializedProperty property =
            serializedManager.FindProperty("intersectionControlPanel");
        if (property != null && property.objectReferenceValue != panel)
        {
            property.objectReferenceValue = panel;
            serializedManager.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }
        return false;
    }

    private static void CreateHeader(Transform parent)
    {
        RectTransform header = RectObject("Header", parent);
        Layout(header.gameObject, -1f, 48f, 1f);
        TMP_Text eyebrow = Text(
            "Subtitle", header, "INTERSECTION", 9f, MutedText);
        Place(eyebrow.rectTransform, 2f, -1f, 240f, 16f);
        TMP_Text title = Text(
            "Title", header, "Control type", 15f, TextColor);
        Place(title.rectTransform, 2f, -18f, 240f, 25f);
    }

    private static OptionReferences CreateOption(
        Transform parent,
        IntersectionRuleType mode)
    {
        RectTransform container = RectObject(mode + " Option", parent);
        VerticalLayoutGroup layout =
            container.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 3f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter =
            container.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject buttonObject = ImageObject("Button", container, Row);
        Layout(buttonObject, -1f, 35f, 1f);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();

        GameObject selected = ImageObject("Selected", buttonObject.transform, Accent);
        RectTransform selectedRect = selected.GetComponent<RectTransform>();
        selectedRect.anchorMin = new Vector2(0f, 0f);
        selectedRect.anchorMax = new Vector2(0f, 1f);
        selectedRect.pivot = new Vector2(0f, 0.5f);
        selectedRect.sizeDelta = new Vector2(3f, 0f);
        selectedRect.anchoredPosition = Vector2.zero;
        selected.GetComponent<Image>().raycastTarget = false;

        Image baseIcon = ImageObject(
            "IntersectionIcon", buttonObject.transform, Color.white)
            .GetComponent<Image>();
        Place(baseIcon.rectTransform, 7f, -4f, 27f, 27f);
        baseIcon.preserveAspect = true;
        baseIcon.raycastTarget = false;
        Image typeIcon = ImageObject(
            "TypeIcon", baseIcon.transform, Color.white).GetComponent<Image>();
        typeIcon.rectTransform.anchorMin = typeIcon.rectTransform.anchorMax =
            new Vector2(0.5f, 0.5f);
        typeIcon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        typeIcon.rectTransform.sizeDelta = new Vector2(17f, 17f);
        typeIcon.rectTransform.anchoredPosition = Vector2.zero;
        typeIcon.preserveAspect = true;
        typeIcon.raycastTarget = false;

        TMP_Text label = Text(
            "Label", buttonObject.transform, ModeName(mode), 11f, TextColor);
        Place(label.rectTransform, 42f, -3f, 174f, 29f);
        TMP_Text dropdown = Text(
            "Dropdown", buttonObject.transform, ">", 12f, MutedText);
        dropdown.alignment = TextAlignmentOptions.Center;
        Place(dropdown.rectTransform, 218f, -3f, 23f, 29f);

        GameObject details = ImageObject("Details", container, Detail);
        float detailHeight = IntersectionControlUtility.UsesTrafficLights(mode)
            ? 94f
            : mode == IntersectionRuleType.PriorityRoad ? 82f : 50f;
        Layout(details, -1f, detailHeight, 1f);
        TMP_Text description = Text(
            "Description", details.transform, ModeDescription(mode), 8.5f, MutedText);
        description.textWrappingMode = TextWrappingModes.Normal;
        SetAnchors(
            description.rectTransform, 0f, 0.42f, 1f, 1f,
            7f, -5f, -7f, -4f);

        return new OptionReferences
        {
            Mode = mode,
            Button = button,
            IntersectionIcon = baseIcon,
            TypeIcon = typeIcon,
            SelectedIndicator = selected,
            Details = details,
            Dropdown = dropdown
        };
    }

    private static void BuildPriorityDetails(
        Transform details,
        out TMP_Text value,
        out Button button,
        out TMP_Text buttonText)
    {
        value = Text(
            "PriorityValue", details, "Priority road not chosen", 8f, TextColor);
        value.alignment = TextAlignmentOptions.Center;
        SetAnchors(value.rectTransform, 0f, 0.22f, 1f, 0.46f, 7f, 0f, -7f, 0f);
        GameObject buttonObject = ImageObject("ChoosePriorityRoad", details, Accent);
        SetAnchors(
            buttonObject.GetComponent<RectTransform>(),
            0f, 0f, 1f, 0.23f, 7f, 4f, -7f, -2f);
        button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();
        buttonText = Text(
            "Label", buttonObject.transform, "Choose priority road", 8.5f, TextColor);
        Stretch(buttonText.rectTransform);
        buttonText.alignment = TextAlignmentOptions.Center;
    }

    private static void BuildTrafficLightDetails(
        Transform details,
        out Slider slider,
        out TMP_Text value,
        bool smart = false)
    {
        TMP_Text caption = Text(
            "DurationLabel", details, smart ? "Waiting threshold" : "Change every", 8f, MutedText);
        SetAnchors(caption.rectTransform, 0f, 0.25f, 0.55f, 0.46f, 7f, 0f, 0f, 0f);
        value = Text("DurationValue", details, smart ? "10 seconds" : "30 seconds", 9f, TextColor);
        value.alignment = TextAlignmentOptions.MidlineRight;
        SetAnchors(value.rectTransform, 0.55f, 0.25f, 1f, 0.46f, 0f, 0f, -7f, 0f);

        RectTransform sliderRoot = RectObject("DurationSlider", details);
        SetAnchors(sliderRoot, 0f, 0f, 1f, 0.25f, 9f, 3f, -9f, -3f);
        RectTransform background = ImageObject(
            "Background", sliderRoot, new Color32(58, 67, 82, 255))
            .GetComponent<RectTransform>();
        SetAnchors(background, 0f, 0.4f, 1f, 0.6f, 0f, 0f, 0f, 0f);
        RectTransform fill = ImageObject("Fill", sliderRoot, Accent)
            .GetComponent<RectTransform>();
        SetAnchors(fill, 0f, 0.34f, 1f, 0.66f, 0f, 0f, 0f, 0f);
        RectTransform handle = ImageObject("Handle", sliderRoot, TextColor)
            .GetComponent<RectTransform>();
        handle.sizeDelta = new Vector2(12f, 18f);
        slider = sliderRoot.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = smart ? IntersectionControlUtility.MinimumSmartWaitSeconds :
            IntersectionControlUtility.MinimumLightDurationSeconds;
        slider.maxValue = smart ? IntersectionControlUtility.MaximumSmartWaitSeconds :
            IntersectionControlUtility.MaximumLightDurationSeconds;
        slider.wholeNumbers = true;
        slider.SetValueWithoutNotify(
            smart ? IntersectionControlUtility.DefaultSmartWaitSeconds :
                IntersectionControlUtility.DefaultLightDurationSeconds);
    }

    private static string ModeName(IntersectionRuleType mode)
    {
        switch (mode)
        {
            case IntersectionRuleType.FIFO: return "First in, first out";
            case IntersectionRuleType.PriorityRoad: return "Priority road";
            case IntersectionRuleType.TrafficLight: return "Traffic lights";
            case IntersectionRuleType.SmartTrafficLight: return "Smart traffic lights";
            default: return "Free for all";
        }
    }

    private static string ModeDescription(IntersectionRuleType mode)
    {
        switch (mode)
        {
            case IntersectionRuleType.FIFO:
                return "One vehicle proceeds at a time, in arrival order.";
            case IntersectionRuleType.PriorityRoad:
                return "Selected approaches proceed first; side roads yield.";
            case IntersectionRuleType.TrafficLight:
                return "Opposing approaches alternate using the duration below.";
            case IntersectionRuleType.SmartTrafficLight:
                return "Longest eligible wait gets green. Uses simulation seconds; minimum green is 1 second.";
            default:
                return "Compatible vehicles may enter together. Best for light traffic.";
        }
    }

    private static GameObject ImageObject(string name, Transform parent, Color color)
    {
        GameObject result = new GameObject(
            name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
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
        Color color)
    {
        GameObject result = new GameObject(
            name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        result.transform.SetParent(parent, false);
        TMP_Text text = result.GetComponent<TMP_Text>();
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(GameFontPath);
        if (font != null) text.font = font;
        return text;
    }

    private static void Place(
        RectTransform rect,
        float x,
        float y,
        float width,
        float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetAnchors(
        RectTransform rect,
        float minX,
        float minY,
        float maxX,
        float maxY,
        float left,
        float bottom,
        float right,
        float top)
    {
        rect.anchorMin = new Vector2(minX, minY);
        rect.anchorMax = new Vector2(maxX, maxY);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(right, top);
    }

    private static void Layout(
        GameObject target,
        float preferredWidth,
        float preferredHeight,
        float flexibleWidth)
    {
        LayoutElement layout = target.GetComponent<LayoutElement>() ??
                               target.AddComponent<LayoutElement>();
        if (preferredWidth >= 0f) layout.preferredWidth = preferredWidth;
        if (preferredHeight >= 0f) layout.preferredHeight = preferredHeight;
        layout.flexibleWidth = flexibleWidth;
        layout.flexibleHeight = 0f;
    }

    private static void SetReference(
        SerializedObject target,
        string propertyName,
        Object value)
    {
        SerializedProperty property = target.FindProperty(propertyName);
        if (property != null) property.objectReferenceValue = value;
    }
}

[CustomEditor(typeof(IntersectionControlPanel))]
internal sealed class IntersectionControlPanelEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox(
            "This panel is authored scene UI. Edit its child RectTransforms, " +
            "Images, TMP text, buttons, and detail sections directly in the hierarchy.",
            MessageType.Info);
        DrawDefaultInspector();

        IntersectionControlPanel panel = (IntersectionControlPanel)target;
        string label = panel.gameObject.activeSelf
            ? "Hide Panel Preview"
            : "Show Panel Preview";
        if (!GUILayout.Button(label)) return;
        Undo.RecordObject(panel.gameObject, label);
        panel.gameObject.SetActive(!panel.gameObject.activeSelf);
        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
    }
}
