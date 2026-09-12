using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared panel presenter for every PopHousingBuildingData asset.
/// Assign the text fields from the housing panel hierarchy in the Inspector.
/// </summary>
[ExecuteAlways]
public sealed class HousingBuildingInfoPanel : BuildingInfoPanel
{
    private const string LayoutVersionMarker = "HousingLayoutV4";
    private static readonly Vector2 PanelSize = new Vector2(240f, 240f);

    [Header("Housing Fields")]
    [SerializeField] private TMP_FontAsset panelFont;
    [SerializeField] private TMP_Text buildingNameText;
    [SerializeField] private TMP_Text vehicleStorageText;
    [SerializeField] private TMP_Text ownedVehicleCountText;
    [SerializeField] private TMP_Text totalPopsSentText;
    [SerializeField] private Button openClosedButton;
    [SerializeField] private TMP_Text openClosedButtonText;

    [Header("Combined Housing Layout")]
    [SerializeField] private TMP_Text vehicleListCountText;
    [SerializeField] private GameObject emptyOwnedVehiclesState;

    [Header("Owned Vehicle List")]
    [Tooltip(
        "Optional. When omitted, the panel finds its first child ScrollRect.")]
    [SerializeField] private ScrollRect ownedVehiclesScrollRect;

    [Tooltip(
        "Optional override for the row parent. When omitted, the ScrollRect's " +
        "Content transform is used.")]
    [SerializeField] private RectTransform vehicleListContent;

    [Tooltip("Prefab instantiated once for every vehicle the house owns.")]
    [SerializeField] private GameObject vehicleRowPrefab;

    [SerializeField] private bool resetVehicleScrollPositionOnOpen = true;

    [Header("Live Refresh")]
    [SerializeField, Min(0.05f)] private float refreshInterval = 0.25f;
    private float nextRefreshTime;
    private readonly List<HousingVehicleRowUI> vehicleRows =
        new List<HousingVehicleRowUI>();
    private bool reportedMissingVehicleListConfiguration;

    private void Awake()
    {
        EnsureCompactLayout();
        ConfigureLayoutReferences();
    }

    private void OnEnable()
    {
        EnsureCompactLayout();
        ConfigureLayoutReferences();
    }

    public void EnsureCompactLayoutForEditor()
    {
        EnsureCompactLayout();
        ConfigureLayoutReferences();
    }

    internal TMP_FontAsset PanelFont => buildingNameText != null
        ? (panelFont != null ? panelFont : buildingNameText.font)
        : panelFont;

    public void ApplyPanelFontForEditor()
    {
        TMP_FontAsset font = PanelFont;
        if (font == null)
        {
            return;
        }

        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null)
            {
                texts[i].font = font;
            }
        }
    }

    public void RebuildCompactLayoutForEditor()
    {
        GameObject existingVehiclePrefab = vehicleRowPrefab;
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(transform.GetChild(i).gameObject);
        }

        buildingNameText = null;
        vehicleStorageText = null;
        ownedVehicleCountText = null;
        totalPopsSentText = null;
        vehicleListCountText = null;
        emptyOwnedVehiclesState = null;
        ownedVehiclesScrollRect = null;
        vehicleListContent = null;
        vehicleRowPrefab = existingVehiclePrefab;
        EnsureCompactLayout();
        ConfigureLayoutReferences();
    }

    private void EnsureCompactLayout()
    {
        Transform existingCard = transform.Find("HousingPanelCard");
        // Once a card exists it is authored scene content. Never infer that
        // it is stale from its children or version marker: doing so destroys
        // Inspector edits whenever Play Mode toggles the scene.
        if (existingCard != null)
        {
            return;
        }

        if (existingCard != null)
        {
            existingCard.gameObject.SetActive(false);
            DestroyObject(existingCard.gameObject);
        }

        TMP_FontAsset font = PanelFont != null
            ? PanelFont
            : GetComponentInChildren<TMP_Text>(true)?.font;

        Transform legacyBackground = transform.Find("Background");
        if (legacyBackground != null)
        {
            legacyBackground.gameObject.SetActive(false);
        }

        if (transform is RectTransform panelRect)
        {
            panelRect.sizeDelta = PanelSize;
        }

        RectTransform card = CreateImage(
            "HousingPanelCard",
            transform,
            new Color32(22, 27, 36, 255));
        card.anchorMin = new Vector2(0.5f, 0.5f);
        card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.anchoredPosition = Vector2.zero;
        card.sizeDelta = PanelSize;
        RectTransform versionMarker = CreateRect(LayoutVersionMarker, card);
        versionMarker.sizeDelta = Vector2.zero;
        Outline outline = card.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color32(59, 70, 88, 220);
        outline.effectDistance = new Vector2(1f, -1f);

        BuildingPanelOverlayManager overlay =
            FindFirstObjectByType<BuildingPanelOverlayManager>();
        if (overlay != null)
        {
            BuildingPanelDragHandle dragHandle = card.gameObject.AddComponent<
                BuildingPanelDragHandle>();
            dragHandle.Initialize(overlay, this);
        }

        RectTransform header = CreateRect("Header", card);
        PlaceTop(header, 8f, 8f, 8f, 34f);
        TMP_Text eyebrow = CreateText(
            "Subtitle",
            header,
            "Pop Housing building",
            8f,
            TextAlignmentOptions.Center,
            new Color32(165, 173, 188, 255),
            font);
        SetAnchors(eyebrow.rectTransform, 0f, 0.5f, 1f, 1f, 0f, 0f);
        buildingNameText = CreateText(
            "BuildingName",
            header,
            "Housing",
            14f,
            TextAlignmentOptions.Center,
            new Color32(240, 243, 248, 255),
            font);
        SetAnchors(
            buildingNameText.rectTransform,
            0f,
            0f,
            1f,
            0.5f,
            0f,
            0f);

        RectTransform overview = CreateRect("HousingInfo", card);
        overview.anchorMin = Vector2.zero;
        overview.anchorMax = Vector2.one;
        overview.offsetMin = new Vector2(7f, 125f);
        overview.offsetMax = new Vector2(-7f, -48f);
        BuildCompactOverview(overview, font);

        RectTransform vehicles = CreateRect("HousedVehicles", card);
        vehicles.anchorMin = Vector2.zero;
        vehicles.anchorMax = Vector2.one;
        vehicles.offsetMin = new Vector2(7f, 7f);
        vehicles.offsetMax = new Vector2(-7f, -123f);
        BuildCompactVehicleTab(vehicles, font);
        vehicles.gameObject.SetActive(true);
    }

    private void BuildCompactOverview(Transform parent, TMP_FontAsset font)
    {
        RectTransform stats = CreateRect("Stats", parent);
        PlaceTop(stats, 2f, 2f, 2f, 66f);
        VerticalLayoutGroup layout = stats.gameObject.AddComponent<
            VerticalLayoutGroup>();
        layout.spacing = 3f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        vehicleStorageText = CreateStatRow(
            stats,
            "Vehicle parking",
            font);
        ownedVehicleCountText = CreateStatRow(
            stats,
            "Vehicles housed",
            font);
        totalPopsSentText = CreateStatRow(
            stats,
            "Population served",
            font);

    }

    private void BuildCompactVehicleTab(
        Transform parent,
        TMP_FontAsset font)
    {
        RectTransform header = CreateRect("VehiclesHeader", parent);
        PlaceTop(header, 2f, 2f, 2f, 20f);
        TMP_Text heading = CreateText(
            "Heading",
            header,
            "HOUSED VEHICLES",
            8f,
            TextAlignmentOptions.MidlineLeft,
            new Color32(240, 243, 248, 255),
            font);
        SetAnchors(heading.rectTransform, 0f, 0f, 0.62f, 1f, 0f, 0f);
        vehicleListCountText = CreateText(
            "VehicleCount",
            header,
            "0 vehicles",
            8f,
            TextAlignmentOptions.MidlineRight,
            new Color32(165, 173, 188, 255),
            font);
        SetAnchors(
            vehicleListCountText.rectTransform,
            0.55f,
            0f,
            1f,
            1f,
            0f,
            0f);

        RectTransform columns = CreateImage(
            "VehicleColumns",
            parent,
            new Color32(28, 33, 42, 255));
        PlaceTop(columns, 2f, 2f, 25f, 15f);
        CreateColumn(columns, "Vehicle", 0f, 0.43f, font);
        CreateColumn(columns, "Status", 0.43f, 0.70f, font);
        CreateColumn(columns, "Pops", 0.70f, 0.85f, font);
        CreateColumn(columns, "Space", 0.85f, 1f, font);

        ownedVehiclesScrollRect = CreateCompactScrollView(
            parent,
            out vehicleListContent);
        RectTransform scrollRect =
            ownedVehiclesScrollRect.GetComponent<RectTransform>();
        scrollRect.anchorMin = Vector2.zero;
        scrollRect.anchorMax = Vector2.one;
        scrollRect.pivot = new Vector2(0.5f, 0.5f);
        SetOffsets(scrollRect, 2f, 2f, -2f, -44f);

        TMP_Text empty = CreateText(
            "EmptyOwnedVehicles",
            vehicleListContent,
            "No vehicles housed\n\nDrag a population vehicle here",
            8f,
            TextAlignmentOptions.Center,
            new Color32(165, 173, 188, 255),
            font);
        AddLayout(empty.gameObject, -1f, 52f, 1f);
        emptyOwnedVehiclesState = empty.gameObject;

        GameObject valid = CreateHighlighter(
            parent,
            "DropVehicleHere",
            "Drop population vehicle here",
            new Color(0.25f, 0.75f, 0.35f, 0.32f),
            font);
        GameObject full = CreateHighlighter(
            parent,
            "NotEnoughVehicleSpace",
            "Not enough vehicle space",
            new Color(1f, 0.55f, 0.15f, 0.34f),
            font);
        GameObject invalid = CreateHighlighter(
            parent,
            "WrongVehicleType",
            "Vehicle type not allowed",
            new Color(1f, 0.25f, 0.25f, 0.34f),
            font);
        // Match the industrial panel: the complete vehicle view (header,
        // columns, and list) is the drop surface. House details remain outside
        // this target.
        HousingVehicleDropTarget dropTarget = parent.gameObject.AddComponent<
            HousingVehicleDropTarget>();
        dropTarget.Configure(this, valid, full, invalid);
    }

    private void Update()
    {
        if (CurrentBuilding == null ||
            Time.unscaledTime < nextRefreshTime ||
            !(CurrentBuilding.data is PopHousingBuildingData housingData))
        {
            return;
        }

        nextRefreshTime = Time.unscaledTime + refreshInterval;
        RefreshFields(CurrentBuilding, housingData);
        RefreshVehicleRows();
    }

    protected override void OnBuildingInitialized(Building building)
    {
        ConfigureLayoutReferences();

        if (!(building.data is PopHousingBuildingData housingData))
        {
            Debug.LogError(
                "A housing panel received a non-housing building.",
                this);
            return;
        }

        building.VehicleOwnershipChanged += HandleVehicleOwnershipChanged;
        building.OpenStateChanged += HandleOpenStateChanged;
        nextRefreshTime = Time.unscaledTime + refreshInterval;
        RefreshFields(building, housingData);
        RebuildVehicleRows(
            building,
            resetVehicleScrollPositionOnOpen);
    }

    protected override void OnBuildingCleared(Building previousBuilding)
    {
        if (previousBuilding != null)
        {
            previousBuilding.VehicleOwnershipChanged -=
                HandleVehicleOwnershipChanged;
            previousBuilding.OpenStateChanged -= HandleOpenStateChanged;
        }

        ClearVehicleRows();
    }

    private void HandleVehicleOwnershipChanged(Building building)
    {
        if (building == CurrentBuilding &&
            building.data is PopHousingBuildingData housingData)
        {
            RefreshFields(building, housingData);
            RebuildVehicleRows(building, false);
        }
    }

    private void HandleOpenStateChanged(Building building)
    {
        if (building != CurrentBuilding ||
            !(building.data is PopHousingBuildingData housingData)) return;
        RefreshFields(building, housingData);
        RefreshVehicleRows();
    }

    public void ToggleHousingOpen()
    {
        if (CurrentBuilding != null)
            CurrentBuilding.SetHousingOpen(!CurrentBuilding.IsHousingOpen);
    }

    private void EnsureOpenClosedButton(Transform root)
    {
        if (openClosedButton == null)
            openClosedButton = root.Find("OpenClosedButton")?.GetComponent<Button>();
        if (openClosedButton == null)
        {
            RectTransform rect = CreateImage("OpenClosedButton", root,
                new Color32(73, 155, 86, 255));
            ConfigureOpenClosedButtonRect(rect);
            openClosedButton = rect.gameObject.AddComponent<Button>();
            openClosedButton.targetGraphic = rect.GetComponent<Image>();
            openClosedButtonText = CreateText("Label", rect, "Open",
                9f, TextAlignmentOptions.Center, new Color32(240, 243, 248, 255), PanelFont);
            Stretch(openClosedButtonText.rectTransform);
        }
        if (openClosedButtonText == null)
            openClosedButtonText = openClosedButton.GetComponentInChildren<TMP_Text>(true);
        // Upgrade the previous footer once, preserving later Inspector layout edits.
        RectTransform buttonRect = openClosedButton.GetComponent<RectTransform>();
        if (buttonRect.anchorMin == Vector2.zero &&
            buttonRect.anchorMax == new Vector2(1f, 0f))
            ConfigureOpenClosedButtonRect(buttonRect);
        UpdateOpenClosedButton(CurrentBuilding == null || CurrentBuilding.IsHousingOpen);
        openClosedButton.onClick.RemoveListener(ToggleHousingOpen);
        openClosedButton.onClick.AddListener(ToggleHousingOpen);
    }

    private static void ConfigureOpenClosedButtonRect(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(6f, -5f);
        rect.sizeDelta = new Vector2(42.4f, 20f);
    }

    private void UpdateOpenClosedButton(bool isOpen)
    {
        SetText(openClosedButtonText, isOpen ? "Open" : "Closed");
        if (openClosedButton != null && openClosedButton.targetGraphic != null)
            openClosedButton.targetGraphic.color = isOpen
                ? new Color32(73, 155, 86, 255) : new Color32(170, 68, 64, 255);
    }

    private void RebuildVehicleRows(
        Building building,
        bool resetScrollPosition)
    {
        if (!TryResolveVehicleListConfiguration())
        {
            ClearVehicleRows();
            SetText(vehicleListCountText, "0 vehicles");
            if (emptyOwnedVehiclesState != null)
            {
                emptyOwnedVehiclesState.SetActive(true);
            }
            return;
        }

        ClearVehicleRows();

        IReadOnlyList<VehicleAI> ownedVehicles = building.OwnedVehicles;
        int displayedCount = 0;
        for (int i = 0; i < ownedVehicles.Count; i++)
        {
            VehicleAI vehicle = ownedVehicles[i];
            if (vehicle == null)
            {
                continue;
            }

            GameObject rowObject = Instantiate(
                vehicleRowPrefab,
                vehicleListContent,
                false);

            if (!rowObject.TryGetComponent(
                    out HousingVehicleRowUI row))
            {
                Debug.LogError(
                    $"{vehicleRowPrefab.name} needs a " +
                    $"{nameof(HousingVehicleRowUI)} component.",
                    vehicleRowPrefab);
                Destroy(rowObject);
                continue;
            }

            row.Bind(vehicle);
            row.gameObject.SetActive(true);
            vehicleRows.Add(row);
            displayedCount++;
        }

        if (emptyOwnedVehiclesState != null)
        {
            emptyOwnedVehiclesState.SetActive(displayedCount == 0);
        }

        SetText(
            vehicleListCountText,
            displayedCount == 1
                ? "1 vehicle"
                : $"{displayedCount} vehicles");

        RefreshVehicleListLayout(resetScrollPosition);
    }

    private void RefreshVehicleRows()
    {
        bool anyValueChanged = false;
        for (int i = vehicleRows.Count - 1; i >= 0; i--)
        {
            HousingVehicleRowUI row = vehicleRows[i];
            if (row == null)
            {
                vehicleRows.RemoveAt(i);
                continue;
            }

            anyValueChanged |= row.Refresh();
        }

        if (anyValueChanged)
        {
            RefreshVehicleListLayout(false);
        }
    }

    private void RefreshVehicleListLayout(bool resetScrollPosition)
    {
        if (ownedVehiclesScrollRect == null ||
            vehicleListContent == null)
        {
            return;
        }

        float previousScrollPosition =
            ownedVehiclesScrollRect.verticalNormalizedPosition;

        LayoutRebuilder.ForceRebuildLayoutImmediate(vehicleListContent);
        Canvas.ForceUpdateCanvases();

        RectTransform viewport = ownedVehiclesScrollRect.viewport;
        bool requiresScrolling =
            viewport != null &&
            vehicleListContent.rect.height >
                viewport.rect.height + 0.5f;

        Scrollbar verticalScrollbar =
            ownedVehiclesScrollRect.verticalScrollbar;
        if (verticalScrollbar != null &&
            verticalScrollbar.gameObject.activeSelf != requiresScrolling)
        {
            verticalScrollbar.gameObject.SetActive(requiresScrolling);
            Canvas.ForceUpdateCanvases();
        }

        ownedVehiclesScrollRect.vertical = requiresScrolling;
        ownedVehiclesScrollRect.StopMovement();

        ownedVehiclesScrollRect.verticalNormalizedPosition =
            !requiresScrolling || resetScrollPosition
                ? 1f
                : Mathf.Clamp01(previousScrollPosition);
    }

    private void ClearVehicleRows()
    {
        if (vehicleListContent != null)
        {
            HousingVehicleRowUI[] rowsInContent =
                vehicleListContent.GetComponentsInChildren
                    <HousingVehicleRowUI>(true);

            for (int i = 0; i < rowsInContent.Length; i++)
            {
                HousingVehicleRowUI row = rowsInContent[i];
                if (row == null) continue;

                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
        }
        else
        {
            for (int i = 0; i < vehicleRows.Count; i++)
            {
                HousingVehicleRowUI row = vehicleRows[i];
                if (row == null) continue;

                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
        }

        vehicleRows.Clear();
    }

    private bool TryResolveVehicleListConfiguration()
    {
        Transform compactScroll = transform.Find(
            "HousingPanelCard/HousedVehicles/OwnedVehiclesScrollView");
        if (compactScroll != null)
        {
            ScrollRect compactRect = compactScroll.GetComponent<ScrollRect>();
            if (compactRect != null)
            {
                ownedVehiclesScrollRect = compactRect;
                vehicleListContent = compactRect.content;
            }
        }

        if (ownedVehiclesScrollRect == null)
        {
            ownedVehiclesScrollRect =
                GetComponentInChildren<ScrollRect>(true);
        }

        if (vehicleListContent == null &&
            ownedVehiclesScrollRect != null)
        {
            vehicleListContent = ownedVehiclesScrollRect.content;
        }

        if (ownedVehiclesScrollRect != null &&
            vehicleListContent != null &&
            vehicleRowPrefab != null)
        {
            ownedVehiclesScrollRect.movementType =
                ScrollRect.MovementType.Clamped;
            ownedVehiclesScrollRect.inertia = false;
            // The panel controls scrollbar visibility itself. Expanding the
            // viewport when the bar appears changes every row's width and can
            // produce a visible horizontal jump.
            ownedVehiclesScrollRect.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.AutoHide;
            reportedMissingVehicleListConfiguration = false;
            return true;
        }

        if (!reportedMissingVehicleListConfiguration)
        {
            Debug.LogWarning(
                "The housing vehicle list needs a Content RectTransform and " +
                "a HousingVehicleRowUI prefab. Assign them on the housing " +
                "panel, or put a configured ScrollRect below the panel.",
                this);
            reportedMissingVehicleListConfiguration = true;
        }

        return false;
    }

    private void RefreshFields(
        Building building,
        PopHousingBuildingData housingData)
    {
        int capacity = building.GetVehicleSpaceCapacity();
        UpdateOpenClosedButton(building.IsHousingOpen);
        int usedSpace = building.GetUsedVehicleSpace();
        int totalVehicles = building.GetOwnedVehicleCount();
        int totalPopsSent =
            building.GetTotalPopsSentByOwnedVehicles();
        int totalPopCapacity =
            building.GetTotalPopCapacityOfOwnedVehicles();
        int sentVehicles = building.GetSentPopTransportVehicleCount();
        int availableVehicles = Mathf.Max(0, totalVehicles - sentVehicles);

        string vehicleSpaceSummary = $"{usedSpace}/{capacity}";
        string ownedVehicleSummary = $"{totalVehicles}";
        string totalPopsSentSummary =
            $"{totalPopsSent}/{totalPopCapacity}";
        string vehicleStatusSummary =
            $"{availableVehicles}\n" +
            $"{sentVehicles}";

        bool useCombinedFallback =
            vehicleStorageText == null &&
            ownedVehicleCountText == null &&
            totalPopsSentText == null;

        if (useCombinedFallback)
        {
            SetText(
                buildingNameText,
                $"{housingData.buildingName}\n\n" +
                $"{vehicleSpaceSummary}\n" +
                $"{ownedVehicleSummary}\n" +
                $"{totalPopsSentSummary}\n" +
                vehicleStatusSummary);
        }
        else
        {
            SetText(buildingNameText, housingData.buildingName);
            SetText(vehicleStorageText, vehicleSpaceSummary);
            SetText(ownedVehicleCountText, ownedVehicleSummary);
            SetText(totalPopsSentText, totalPopsSentSummary);
        }

    }

    private void ConfigureLayoutReferences()
    {
        Transform root = transform.Find("HousingPanelCard") ??
                         transform.Find("Background") ?? transform;
        EnsureOpenClosedButton(root);

        if (buildingNameText == null)
        {
            buildingNameText = root.Find("Header/BuildingName")
                ?.GetComponent<TMP_Text>();
        }

        if (vehicleListCountText == null)
        {
            vehicleListCountText = root.Find("HousedVehicles")
                ?.Find("VehiclesHeader")?.Find("VehicleCount")
                ?.GetComponent<TMP_Text>();
        }

        if (emptyOwnedVehiclesState == null)
        {
            emptyOwnedVehiclesState = root.Find(
                "HousedVehicles/OwnedVehiclesScrollView/Viewport/Content/" +
                "EmptyOwnedVehicles")?.gameObject;
        }

    }

    private TMP_Text CreateStatRow(
        Transform parent,
        string label,
        TMP_FontAsset font)
    {
        RectTransform row = CreateImage(
            "Stat_" + label,
            parent,
            new Color32(46, 53, 66, 255));
        AddLayout(row.gameObject, -1f, 19f, 0f);
        TMP_Text caption = CreateText(
            "Label",
            row,
            label,
            8f,
            TextAlignmentOptions.MidlineLeft,
            new Color32(165, 173, 188, 255),
            font);
        SetAnchors(caption.rectTransform, 0f, 0f, 0.7f, 1f, 7f, 0f);
        TMP_Text value = CreateText(
            "Value",
            row,
            "--",
            9f,
            TextAlignmentOptions.MidlineRight,
            new Color32(240, 243, 248, 255),
            font);
        value.rectTransform.anchorMin = new Vector2(0.7f, 0f);
        value.rectTransform.anchorMax = Vector2.one;
        value.rectTransform.offsetMin = Vector2.zero;
        value.rectTransform.offsetMax = new Vector2(-7f, 0f);
        return value;
    }

    private static void CreateColumn(
        Transform parent,
        string label,
        float minX,
        float maxX,
        TMP_FontAsset font)
    {
        TMP_Text text = CreateText(
            label,
            parent,
            label,
            6.5f,
            TextAlignmentOptions.Center,
            new Color32(165, 173, 188, 255),
            font);
        text.rectTransform.anchorMin = new Vector2(minX, 0f);
        text.rectTransform.anchorMax = new Vector2(maxX, 1f);
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;
    }

    private ScrollRect CreateCompactScrollView(
        Transform parent,
        out RectTransform content)
    {
        RectTransform scrollTransform = CreateImage(
            "OwnedVehiclesScrollView",
            parent,
            new Color32(21, 25, 32, 255));
        ScrollRect scroll = scrollTransform.gameObject.AddComponent<ScrollRect>();
        scrollTransform.anchorMin = Vector2.zero;
        scrollTransform.anchorMax = Vector2.one;
        scrollTransform.pivot = new Vector2(0.5f, 0.5f);
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = false;
        scroll.scrollSensitivity = 18f;

        RectTransform viewport = CreateImage(
            "Viewport",
            scrollTransform,
            new Color(1f, 1f, 1f, 0.02f));
        Stretch(viewport);
        viewport.offsetMax = new Vector2(-7f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();
        content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = content.gameObject.AddComponent<
            VerticalLayoutGroup>();
        layout.padding = new RectOffset(3, 3, 3, 3);
        layout.spacing = 3f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.gameObject.AddComponent<
            ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform scrollbarRect = CreateImage(
            "Scrollbar",
            scrollTransform,
            new Color32(42, 49, 61, 255));
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = Vector2.one;
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.sizeDelta = new Vector2(7f, 0f);
        RectTransform handle = CreateImage(
            "Handle",
            scrollbarRect,
            new Color32(61, 85, 122, 255));
        Stretch(handle);
        Scrollbar scrollbar = scrollbarRect.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handle.GetComponent<Image>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        scroll.viewport = viewport;
        scroll.content = content;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility =
            ScrollRect.ScrollbarVisibility.AutoHide;
        return scroll;
    }

    private static GameObject CreateHighlighter(
        Transform parent,
        string name,
        string label,
        Color color,
        TMP_FontAsset font)
    {
        RectTransform rect = CreateImage(name, parent, color);
        Stretch(rect);
        rect.offsetMin = new Vector2(3f, 3f);
        rect.offsetMax = new Vector2(-3f, -44f);
        Image image = rect.GetComponent<Image>();
        image.raycastTarget = false;
        TMP_Text text = CreateText(
            "Label",
            rect,
            label,
            8f,
            TextAlignmentOptions.Center,
            new Color32(240, 243, 248, 255),
            font);
        Stretch(text.rectTransform);
        rect.gameObject.SetActive(false);
        return rect.gameObject;
    }

    private static void DestroyObject(GameObject target)
    {
        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private static RectTransform CreateTab(string name, Transform parent)
    {
        RectTransform tab = CreateRect(name, parent);
        tab.anchorMin = Vector2.zero;
        tab.anchorMax = Vector2.one;
        tab.offsetMin = new Vector2(7f, 7f);
        tab.offsetMax = new Vector2(-7f, -72f);
        return tab;
    }

    private static RectTransform CreateImage(
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
        return result.GetComponent<RectTransform>();
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject result = new GameObject(name, typeof(RectTransform));
        result.transform.SetParent(parent, false);
        return result.GetComponent<RectTransform>();
    }

    private static TMP_Text CreateText(
        string name,
        Transform parent,
        string value,
        float size,
        TextAlignmentOptions alignment,
        Color color,
        TMP_FontAsset font)
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
        if (font != null) text.font = font;
        return text;
    }

    private static LayoutElement AddLayout(
        GameObject target,
        float width,
        float height,
        float flexibleWidth)
    {
        LayoutElement element = target.GetComponent<LayoutElement>() ??
            target.AddComponent<LayoutElement>();
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

    private static void SetAnchors(
        RectTransform rect,
        float minX,
        float minY,
        float maxX,
        float maxY,
        float leftPadding,
        float bottomPadding)
    {
        rect.anchorMin = new Vector2(minX, minY);
        rect.anchorMax = new Vector2(maxX, maxY);
        rect.offsetMin = new Vector2(leftPadding, bottomPadding);
        rect.offsetMax = Vector2.zero;
    }

    private static void SetOffsets(
        RectTransform rect,
        float left,
        float bottom,
        float right,
        float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(right, top);
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

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null && target.text != value)
        {
            target.text = value;
        }
    }
}
