using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds only the factory status tab and its missing children. Existing
/// scheduling, vehicle, sprite and layout edits are left untouched.
/// </summary>
public static class FactoryStatusTabSceneBuilder
{
    private const string ResourceRowPrefabPath =
        "Assets/Prefabs/UI/FactoryStatusResourceRow.prefab";
    private const string GameFontPath =
        "Assets/Fonts/ari-w9500-condensed-display.asset";

    private static readonly Color Section = new Color32(28, 33, 42, 255);
    private static readonly Color Control = new Color32(46, 53, 66, 255);
    private static readonly Color Accent = new Color32(61, 85, 122, 255);
    private static readonly Color TextColor =
        new Color32(240, 243, 248, 255);
    private static readonly Color MutedText =
        new Color32(165, 173, 188, 255);

    [MenuItem("Tools/Mini Colonies/Install Factory Status Tab")]
    public static void Install()
    {
        IndustrialBuildingInfoPanel panel = Resources
            .FindObjectsOfTypeAll<IndustrialBuildingInfoPanel>()
            .FirstOrDefault(candidate =>
                candidate != null &&
                !EditorUtility.IsPersistent(candidate) &&
                candidate.gameObject.scene.IsValid());
        if (panel == null)
        {
            Debug.LogWarning("No loaded industrial building panel was found.");
            return;
        }

        Transform background = panel.transform.Find("Background") ??
                               panel.transform;
        Transform tabBar = background.Find("IndustrialTabButtons");
        if (tabBar == null)
        {
            Debug.LogWarning(
                "IndustrialTabButtons is missing; install the industrial " +
                "vehicle tab first.");
            return;
        }

        Button statusButton = EnsureButton(
            tabBar,
            "FactoryStatusTabButton",
            "Production");
        RectTransform statusTab = EnsureFullRect(
            background,
            "FactoryStatusTab");
        FactoryOperationsTabView view = statusTab.GetComponent<
            FactoryOperationsTabView>();
        if (view == null)
        {
            view = statusTab.gameObject.AddComponent<
                FactoryOperationsTabView>();
        }

        FactoryStatusResourceRowView rowPrefab = EnsureResourceRowPrefab();
        BuildStatusContents(
            statusTab,
            out RectTransform storageRows,
            out TMP_Text preparationState,
            out TMP_Text preparedPops,
            out TMP_Text currentState,
            out TMP_Text currentWorkers,
            out TMP_Text currentEfficiency,
            out RectTransform currentInputs,
            out RectTransform pendingOutputs);

        SerializedObject viewObject = new SerializedObject(view);
        SetReference(viewObject, "storageRowsRoot", storageRows);
        SetReference(viewObject, "preparationStateText", preparationState);
        SetReference(viewObject, "preparedPopsText", preparedPops);
        SetReference(viewObject, "currentShiftStateText", currentState);
        SetReference(viewObject, "currentWorkersText", currentWorkers);
        SetReference(viewObject, "currentEfficiencyText", currentEfficiency);
        SetReference(viewObject, "currentInputRowsRoot", currentInputs);
        SetReference(viewObject, "pendingOutputRowsRoot", pendingOutputs);
        SetReference(viewObject, "resourceRowPrefab", rowPrefab);
        viewObject.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject panelObject = new SerializedObject(panel);
        SetReference(panelObject, "factoryStatusTabButton", statusButton);
        SetReference(panelObject, "factoryStatusTab", statusTab.gameObject);
        SetReference(panelObject, "factoryStatusView", view);
        panelObject.ApplyModifiedPropertiesWithoutUndo();

        // The panel must always reopen on Scheduling.
        Transform scheduling = background.Find("SchedulingTab");
        Transform vehicles = background.Find("OwnedVehiclesTab");
        if (scheduling != null) scheduling.gameObject.SetActive(true);
        if (vehicles != null) vehicles.gameObject.SetActive(false);
        statusTab.gameObject.SetActive(false);

        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
        EditorSceneManager.SaveScene(panel.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Installed the factory production/status tab.");
    }

    [MenuItem("Tools/Mini Colonies/Install Factory Performance Section")]
    public static void InstallPerformanceSection()
    {
        IndustrialBuildingInfoPanel panel = Resources
            .FindObjectsOfTypeAll<IndustrialBuildingInfoPanel>()
            .FirstOrDefault(candidate =>
                candidate != null &&
                !EditorUtility.IsPersistent(candidate) &&
                candidate.gameObject.scene.IsValid());
        if (panel == null)
        {
            Debug.LogWarning("No loaded industrial building panel was found.");
            return;
        }

        Transform background = panel.transform.Find("Background") ??
                               panel.transform;
        Transform statusTab = background.Find("FactoryStatusTab");
        FactoryOperationsTabView view = statusTab != null
            ? statusTab.GetComponent<FactoryOperationsTabView>()
            : null;
        ScrollRect scroll = statusTab != null
            ? statusTab.Find("FactoryStatusScrollView")
                ?.GetComponent<ScrollRect>()
            : null;
        if (view == null || scroll == null || scroll.content == null)
        {
            Debug.LogWarning(
                "The loaded factory production tab is missing its view or " +
                "scroll content.");
            return;
        }

        RectTransform performance = EnsureSection(
            scroll.content,
            "FactoryPerformanceSection");
        EnsureText(
            performance,
            "Title",
            "Factory performance",
            9f,
            TextColor,
            13f);
        TMP_Text workerEfficiency = EnsureText(
            performance,
            "WorkerEfficiency",
            "Worker efficiency: --",
            8f,
            TextColor,
            12f);
        TMP_Text shiftProgress = EnsureText(
            performance,
            "ShiftProgress",
            "Shift progress: --",
            8f,
            TextColor,
            12f);
        EnsureText(
            performance,
            "ThroughputLabel",
            "Throughput per worker / hour (100% efficiency)",
            7f,
            MutedText,
            10f);
        RectTransform throughputRows = EnsureRowsRoot(
            performance,
            "ThroughputRows");
        performance.SetSiblingIndex(0);

        SerializedObject viewObject = new SerializedObject(view);
        SetReference(viewObject, "workerEfficiencyText", workerEfficiency);
        SetReference(viewObject, "shiftProgressText", shiftProgress);
        SetReference(viewObject, "throughputRowsRoot", throughputRows);
        viewObject.ApplyModifiedPropertiesWithoutUndo();

        Transform oldEfficiency = scroll.content.Find(
            "CurrentShiftSection/CurrentEfficiency");
        if (oldEfficiency != null)
        {
            oldEfficiency.gameObject.SetActive(false);
        }

        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
        EditorSceneManager.SaveScene(panel.gameObject.scene);
        Debug.Log("Installed the factory performance section.");
    }

    private static void BuildStatusContents(
        RectTransform tab,
        out RectTransform storageRows,
        out TMP_Text preparationState,
        out TMP_Text preparedPops,
        out TMP_Text currentState,
        out TMP_Text currentWorkers,
        out TMP_Text currentEfficiency,
        out RectTransform currentInputs,
        out RectTransform pendingOutputs)
    {
        Transform existingScroll = tab.Find("FactoryStatusScrollView");
        RectTransform content;
        if (existingScroll == null)
        {
            content = CreateScrollView(tab);
        }
        else
        {
            ScrollRect scroll = existingScroll.GetComponent<ScrollRect>();
            content = scroll != null ? scroll.content : null;
        }

        RectTransform storage = EnsureSection(content, "StorageSection");
        EnsureText(storage, "Title", "Storage", 9f, TextColor, 13f);
        storageRows = EnsureRowsRoot(storage, "StorageRows");

        RectTransform preparation = EnsureSection(
            content,
            "PreparationSection");
        EnsureText(
            preparation,
            "Title",
            "Next shift preparation",
            9f,
            TextColor,
            13f);
        preparationState = EnsureText(
            preparation,
            "PreparationState",
            "No shift is being prepared",
            8f,
            MutedText,
            12f);
        preparedPops = EnsureText(
            preparation,
            "PreparedPops",
            "Pops: --",
            8f,
            TextColor,
            12f);

        RectTransform current = EnsureSection(content, "CurrentShiftSection");
        EnsureText(
            current,
            "Title",
            "Current shift",
            9f,
            TextColor,
            13f);
        currentState = EnsureText(
            current,
            "CurrentState",
            "No shift currently running",
            8f,
            MutedText,
            12f);
        currentWorkers = EnsureText(
            current,
            "CurrentWorkers",
            "Workers: --",
            8f,
            TextColor,
            12f);
        currentEfficiency = EnsureText(
            current,
            "CurrentEfficiency",
            "Efficiency: --",
            8f,
            TextColor,
            12f);
        EnsureText(
            current,
            "InputsLabel",
            "Unprocessed inputs",
            7f,
            MutedText,
            10f);
        currentInputs = EnsureRowsRoot(current, "CurrentInputRows");
        EnsureText(
            current,
            "OutputsLabel",
            "Outputs awaiting deposit",
            7f,
            MutedText,
            10f);
        pendingOutputs = EnsureRowsRoot(current, "PendingOutputRows");
    }

    private static RectTransform CreateScrollView(Transform parent)
    {
        GameObject scrollObject = ImageObject(
            "FactoryStatusScrollView",
            parent,
            new Color32(21, 25, 32, 255));
        RectTransform scrollRectTransform =
            scrollObject.GetComponent<RectTransform>();
        Stretch(scrollRectTransform);
        scrollRectTransform.offsetMin = new Vector2(6f, 6f);
        scrollRectTransform.offsetMax = new Vector2(-6f, -45f);

        ScrollRect scroll = scrollObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = false;
        scroll.scrollSensitivity = 18f;

        GameObject viewport = ImageObject(
            "Viewport",
            scrollObject.transform,
            new Color32(255, 255, 255, 3));
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        viewportRect.offsetMax = new Vector2(-7f, 0f);
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
            Control);
        RectTransform scrollbarRect =
            scrollbarObject.GetComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = Vector2.one;
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.sizeDelta = new Vector2(6f, 0f);
        GameObject handle = ImageObject(
            "Handle",
            scrollbarObject.transform,
            Accent);
        Stretch(handle.GetComponent<RectTransform>());
        Scrollbar scrollbar = scrollbarObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle.GetComponent<RectTransform>();
        scrollbar.targetGraphic = handle.GetComponent<Image>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        scroll.viewport = viewportRect;
        scroll.content = content;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility =
            ScrollRect.ScrollbarVisibility.AutoHide;
        return content;
    }

    private static RectTransform EnsureSection(Transform parent, string name)
    {
        Transform existing = parent != null ? parent.Find(name) : null;
        if (existing != null) return existing as RectTransform;

        GameObject section = ImageObject(name, parent, Section);
        VerticalLayoutGroup layout =
            section.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(4, 4, 3, 3);
        layout.spacing = 1f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = section.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return section.GetComponent<RectTransform>();
    }

    private static RectTransform EnsureRowsRoot(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing as RectTransform;

        RectTransform root = RectObject(name, parent);
        VerticalLayoutGroup layout = root.gameObject.AddComponent<
            VerticalLayoutGroup>();
        layout.spacing = 1f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = root.gameObject.AddComponent<
            ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return root;
    }

    private static FactoryStatusResourceRowView EnsureResourceRowPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(
            ResourceRowPrefabPath);
        if (existing != null)
        {
            return existing.GetComponent<FactoryStatusResourceRowView>();
        }

        GameObject row = ImageObject(
            "FactoryStatusResourceRow",
            null,
            Control);
        LayoutElement rowLayout = row.AddComponent<LayoutElement>();
        rowLayout.minHeight = 14f;
        rowLayout.preferredHeight = 14f;
        rowLayout.flexibleHeight = 0f;
        HorizontalLayoutGroup layout =
            row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(2, 3, 1, 1);
        layout.spacing = 3f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        FactoryStatusResourceRowView view =
            row.AddComponent<FactoryStatusResourceRowView>();
        GameObject iconObject = ImageObject(
            "ResourceIcon",
            row.transform,
            new Color32(116, 129, 151, 255));
        Layout(iconObject, 11f, 11f, 0f);
        Image icon = iconObject.GetComponent<Image>();
        icon.raycastTarget = false;
        TMP_Text name = Text(
            "ResourceName",
            row.transform,
            "Resource",
            7f,
            TextColor);
        Layout(name.gameObject, -1f, -1f, 1f);
        TMP_Text amount = Text(
            "Amount",
            row.transform,
            "0 / 0",
            7f,
            TextColor);
        amount.alignment = TextAlignmentOptions.MidlineRight;
        Layout(amount.gameObject, 90f, -1f, 0f);

        SerializedObject viewObject = new SerializedObject(view);
        SetReference(viewObject, "resourceIcon", icon);
        SetReference(viewObject, "resourceNameText", name);
        SetReference(viewObject, "amountText", amount);
        viewObject.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
            row,
            ResourceRowPrefabPath);
        Object.DestroyImmediate(row);
        return prefab.GetComponent<FactoryStatusResourceRowView>();
    }

    private static Button EnsureButton(
        Transform parent,
        string name,
        string label)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing.GetComponent<Button>();

        GameObject buttonObject = ImageObject(name, parent, Accent);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();
        TMP_Text text = Text(
            "Label",
            buttonObject.transform,
            label,
            7f,
            TextColor);
        Stretch(text.rectTransform);
        text.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private static RectTransform EnsureFullRect(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing as RectTransform;
        RectTransform rect = RectObject(name, parent);
        Stretch(rect);
        return rect;
    }

    private static TMP_Text EnsureText(
        Transform parent,
        string name,
        string value,
        float size,
        Color color,
        float height)
    {
        Transform existing = parent.Find(name);
        TMP_Text text = existing != null
            ? existing.GetComponent<TMP_Text>()
            : Text(name, parent, value, size, color);
        if (text != null && existing == null)
        {
            text.alignment = TextAlignmentOptions.MidlineLeft;
            Layout(text.gameObject, -1f, height, 0f);
        }
        return text;
    }

    private static TMP_Text Text(
        string name,
        Transform parent,
        string value,
        float size,
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
        text.color = color;
        text.raycastTarget = false;
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            GameFontPath);
        if (font != null) text.font = font;
        return text;
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

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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
