using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presents the authored industrial-building schedule hierarchy and binds its
/// reusable row templates to the currently selected factory.
/// </summary>
public sealed class IndustrialBuildingInfoPanel : BuildingInfoPanel
{
    private static readonly Color OpenToggleColor =
        new Color32(73, 155, 86, 255);
    private static readonly Color ClosedToggleColor =
        new Color32(170, 68, 64, 255);

    [Header("Schedule Hour Colors")]
    [SerializeField] private Color unscheduledHourColor =
        new Color32(53, 62, 78, 255);
    [Tooltip("Normal clock color for an active shift.")]
    [SerializeField] private Color scheduledHourColor =
        new Color32(102, 205, 116, 255);
    [SerializeField] private Color closedScheduledHourColor =
        new Color32(130, 135, 145, 255);
    [Tooltip("Highlighted clock color for an active shift.")]
    [SerializeField] private Color focusedHourColor =
        new Color32(80, 145, 255, 255);
    [Tooltip("Clock color for another active shift while one shift is highlighted.")]
    [SerializeField] private Color dimmedScheduledHourColor =
        new Color32(64, 68, 76, 255);
    [Tooltip("Normal clock color for an inactive shift.")]
    [SerializeField] private Color inactiveScheduledHourColor =
        new Color32(181, 126, 76, 255);
    [Tooltip("Highlighted clock color for an inactive shift.")]
    [SerializeField] private Color inactiveFocusedHourColor =
        new Color32(235, 164, 91, 255);
    [Tooltip("Clock color for another inactive shift while one shift is highlighted.")]
    [SerializeField] private Color inactiveDimmedScheduledHourColor =
        new Color32(75, 59, 48, 255);
    [Tooltip("Empty hours while one shift is focused.")]
    [SerializeField] private Color dimmedUnscheduledHourColor =
        new Color32(42, 45, 51, 255);

    [Header("Shift Row Colors")]
    [Tooltip("Normal row color for an active shift.")]
    [SerializeField] private Color normalShiftRowColor =
        new Color32(39, 45, 57, 255);
    [Tooltip("Highlighted row color for an active shift.")]
    [SerializeField] private Color focusedShiftRowColor =
        new Color32(61, 85, 122, 255);
    [Tooltip("Row color for another active shift while one shift is highlighted.")]
    [SerializeField] private Color dimmedShiftRowColor =
        new Color32(25, 29, 37, 255);
    [Tooltip("Normal row color for an inactive shift.")]
    [SerializeField] private Color inactiveNormalShiftRowColor =
        new Color32(57, 47, 45, 255);
    [Tooltip("Highlighted row color for an inactive shift.")]
    [SerializeField] private Color inactiveFocusedShiftRowColor =
        new Color32(122, 81, 61, 255);
    [Tooltip("Row color for another inactive shift while one shift is highlighted.")]
    [SerializeField] private Color inactiveDimmedShiftRowColor =
        new Color32(37, 31, 30, 255);

    [Header("Preparation Colors")]
    [SerializeField] private Color preparationSpanColor =
        new Color32(246, 177, 86, 110);

    [Header("Legacy Industrial Fields")]
    [SerializeField] private TMP_Text buildingNameText;
    [SerializeField] private TMP_Text populationText;
    [SerializeField] private TMP_Text incomingPopulationText;
    [SerializeField] private TMP_Text efficiencyText;
    [SerializeField] private TMP_Text requiredResourcesText;
    [SerializeField] private TMP_Text producedResourcesText;

    [Header("Factory Header")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text summaryText;
    [SerializeField] private Toggle openToggle;
    [SerializeField] private Button closeButton;

    [Header("Factory Priority")]
    [Tooltip("Optional. A compact top-corner priority button is created at runtime when omitted.")]
    [SerializeField] private FactoryPriorityButton priorityButton;

    [Header("Schedule Controls")]
    [SerializeField] private TMP_Text durationText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button addShiftButton;

    [Header("24-Hour Clock")]
    [SerializeField] private RectTransform hourCellsRoot;
    [SerializeField] private RectTransform currentHourArrow;
    [SerializeField] private Sprite currentHourArrowSprite;
    [Tooltip("Optional artwork drawn across the focused shift and its preparation hours.")]
    [SerializeField] private Sprite preparationSpanSprite;

    [Header("Shift List")]
    [SerializeField] private ScrollRect shiftScrollRect;
    [SerializeField] private RectTransform shiftListContent;
    [SerializeField] private GameObject emptyScheduleState;
    [SerializeField] private FactoryShiftRowView shiftRowPrefab;

    [Header("Industrial Panel Tabs")]
    [SerializeField] private Button schedulingTabButton;
    [SerializeField] private Button ownedVehiclesTabButton;
    [SerializeField] private Button factoryStatusTabButton;
    [SerializeField] private GameObject schedulingTab;
    [SerializeField] private GameObject ownedVehiclesTab;
    [SerializeField] private GameObject factoryStatusTab;
    [SerializeField] private FactoryOperationsTabView factoryStatusView;
    [SerializeField] private Color selectedTabColor =
        new Color32(61, 85, 122, 255);
    [SerializeField] private Color unselectedTabColor =
        new Color32(46, 53, 66, 255);

    [Header("Owned Industrial Vehicles")]
    [SerializeField] private TMP_Text ownedVehicleCountText;
    [SerializeField] private ScrollRect ownedVehiclesScrollRect;
    [SerializeField] private RectTransform ownedVehicleListContent;
    [SerializeField] private GameObject emptyOwnedVehiclesState;
    [SerializeField] private GameObject industrialVehicleRowPrefab;
    [SerializeField, Min(0.05f)] private float vehicleRefreshInterval = 0.25f;

    private readonly List<Image> _hourSquares = new List<Image>();
    private readonly Dictionary<FactoryShift, FactoryShiftRowView> _shiftRows =
        new Dictionary<FactoryShift, FactoryShiftRowView>();
    private readonly List<Image> _preparationSpanImages = new List<Image>();
    private readonly List<IndustrialVehicleRowUI> _vehicleRows =
        new List<IndustrialVehicleRowUI>();

    private Building _subscribedBuilding;
    private FactoryShift _focusedShift;
    private ScheduleFocusSource _focusSource;
    private int _hoveredHour = -1;
    private bool _isClockHovered;
    private FactoryShift _draggedShift;
    private int _lastDragHour;
    private float _nextVehicleRefreshTime;
    private bool _controlsBound;

    private enum ScheduleFocusSource
    {
        None,
        ShiftRow,
        Clock
    }

    private void Awake()
    {
        EnsurePriorityButton();
        CacheHourCells();
        ConfigureShiftListLayout();
        BindStaticControls();
    }

    private void Update()
    {
        bool scheduleVisible = schedulingTab == null ||
                               schedulingTab.activeInHierarchy;
        if (scheduleVisible)
        {
            UpdateClockHover();
            UpdateClockDrag();
        }

        GameTimeManager gameTime = GameTimeManager.Instance;
        float currentTime = gameTime != null
            ? gameTime.CurrentHour + gameTime.CurrentHourProgress
            : 0f;
        UpdateCurrentHourArrow(currentTime);

        if (ownedVehiclesTab != null &&
            ownedVehiclesTab.activeInHierarchy &&
            Time.unscaledTime >= _nextVehicleRefreshTime)
        {
            _nextVehicleRefreshTime =
                Time.unscaledTime + vehicleRefreshInterval;
            RefreshVehicleRows();
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromBuilding();
    }

    protected override void OnBuildingInitialized(Building building)
    {
        UnsubscribeFromBuilding();
        if (priorityButton != null) priorityButton.Bind(null);
        _hoveredHour = -1;
        _isClockHovered = false;
        _draggedShift = null;
        ClearScheduleFocus();

        if (!(building.data is ResourceProductionBuildingData productionData))
        {
            Debug.LogError(
                "An industrial panel received a non-industrial building.",
                this);
            return;
        }

        _subscribedBuilding = building;
        _subscribedBuilding.FactoryScheduleChanged +=
            HandleFactoryScheduleChanged;
        _subscribedBuilding.VehicleOwnershipChanged +=
            HandleVehicleOwnershipChanged;
        _subscribedBuilding.FactoryRuntimeChanged +=
            HandleFactoryRuntimeChanged;
        if (priorityButton != null) priorityButton.Bind(building);

        ShowSchedulingTab();
        if (factoryStatusView != null) factoryStatusView.Bind(building);

        SetText(buildingNameText, productionData.buildingName);
        SetText(
            populationText,
            $"Workers: {building.DeliveredPops}/{building.GetTargetPopCount()}");
        SetText(
            incomingPopulationText,
            $"Incoming workers: {building.ReservedIncomingPops}");
        SetText(
            efficiencyText,
            $"Efficiency: {building.GetProductionEfficiency():P0}");

        RefreshInterface();
        RebuildVehicleRows(true);
    }

    protected override void OnBuildingCleared(Building previousBuilding)
    {
        UnsubscribeFromBuilding();
        _hoveredHour = -1;
        _isClockHovered = false;
        _draggedShift = null;
        ClearScheduleFocus();
        ClearVehicleRows();
        if (priorityButton != null) priorityButton.Bind(null);
        if (factoryStatusView != null) factoryStatusView.Bind(null);
        ShowSchedulingTab();
    }

    private void EnsurePriorityButton()
    {
        if (priorityButton != null) return;

        priorityButton = GetComponentInChildren<FactoryPriorityButton>(true);
        if (priorityButton != null) return;

        RectTransform panelBackground = openToggle != null
            ? openToggle.transform.parent as RectTransform
            : transform as RectTransform;
        if (panelBackground == null) return;

        var buttonObject = new GameObject(
            "FactoryPriorityButton",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button),
            typeof(FactoryPriorityButton));
        buttonObject.layer = panelBackground.gameObject.layer;
        buttonObject.transform.SetParent(panelBackground, false);

        RectTransform buttonRect =
            buttonObject.GetComponent<RectTransform>();
        if (openToggle != null &&
            openToggle.transform is RectTransform openRect)
        {
            buttonRect.anchorMin = openRect.anchorMin;
            buttonRect.anchorMax = openRect.anchorMax;
            buttonRect.pivot = openRect.pivot;
            float buttonSize = Mathf.Max(18f, openRect.sizeDelta.y);
            buttonRect.sizeDelta = new Vector2(buttonSize, buttonSize);
            buttonRect.anchoredPosition = openRect.anchoredPosition +
                Vector2.right * (openRect.sizeDelta.x + 4f);
            buttonRect.SetSiblingIndex(
                openToggle.transform.GetSiblingIndex() + 1);
        }
        else
        {
            buttonRect.anchorMin = new Vector2(0f, 1f);
            buttonRect.anchorMax = new Vector2(0f, 1f);
            buttonRect.pivot = new Vector2(0f, 1f);
            buttonRect.anchoredPosition = new Vector2(5f, -5f);
            buttonRect.sizeDelta = new Vector2(20f, 20f);
        }

        Image background = buttonObject.GetComponent<Image>();
        background.raycastTarget = true;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation
        {
            mode = Navigation.Mode.None
        };

        TMP_Text valueLabel = CreatePriorityLabel(buttonRect);
        priorityButton =
            buttonObject.GetComponent<FactoryPriorityButton>();
        priorityButton.Configure(button, background, valueLabel);

        TooltipInfo tooltip = buttonObject.AddComponent<TooltipInfo>();
        tooltip.SetText(
            "Factory priority\nLeft click: increase\nRight click: decrease");
    }

    private TMP_Text CreatePriorityLabel(RectTransform buttonRect)
    {
        TMP_Text openLabel = openToggle != null
            ? openToggle.transform.Find("Label")?.GetComponent<TMP_Text>()
            : null;
        TMP_Text label;
        if (openLabel != null)
        {
            GameObject labelObject = Instantiate(
                openLabel.gameObject,
                buttonRect,
                false);
            labelObject.name = "Label";
            label = labelObject.GetComponent<TMP_Text>();
        }
        else
        {
            var labelObject = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(TextMeshProUGUI));
            labelObject.layer = buttonRect.gameObject.layer;
            labelObject.transform.SetParent(buttonRect, false);
            label = labelObject.GetComponent<TextMeshProUGUI>();
            label.fontSize = 12f;
        }

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        label.text = "5";
        return label;
    }

    private void BindStaticControls()
    {
        if (_controlsBound) return;
        _controlsBound = true;

        if (openToggle != null)
        {
            openToggle.onValueChanged.AddListener(HandleOpenChanged);
        }

        if (addShiftButton != null)
        {
            addShiftButton.onClick.AddListener(AddShift);
        }

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(ClosePanel);
        }

        if (schedulingTabButton != null)
        {
            schedulingTabButton.onClick.AddListener(ShowSchedulingTab);
        }

        if (ownedVehiclesTabButton != null)
        {
            ownedVehiclesTabButton.onClick.AddListener(ShowOwnedVehiclesTab);
        }

        if (factoryStatusTabButton != null)
        {
            factoryStatusTabButton.onClick.AddListener(ShowFactoryStatusTab);
        }
    }

    private void HandleFactoryScheduleChanged(Building building)
    {
        if (building == CurrentBuilding)
        {
            RefreshInterface();
        }
    }

    private void HandleVehicleOwnershipChanged(Building building)
    {
        if (building == CurrentBuilding)
        {
            RebuildVehicleRows(false);
        }
    }

    private void HandleFactoryRuntimeChanged(Building building)
    {
        if (building != CurrentBuilding) return;

        SetText(
            populationText,
            $"Workers: {building.CurrentWorkingPops}/" +
            $"{building.CurrentFactoryShift?.DesiredPops ?? 0}");
        SetText(
            incomingPopulationText,
            $"Incoming workers: {building.PreparedIncomingPops}");
        SetText(
            efficiencyText,
            $"Efficiency: {building.CurrentFactoryEfficiency:P0}");
    }

    private void HandleOpenChanged(bool isOpen)
    {
        UpdateToggleVisual(isOpen);
        if (CurrentBuilding != null)
        {
            CurrentBuilding.SetFactoryOpen(isOpen);
        }
    }

    private void RefreshInterface()
    {
        Building building = CurrentBuilding;
        if (building == null ||
            !(building.data is ResourceProductionBuildingData productionData))
        {
            return;
        }

        SetText(titleText, productionData.buildingName);
        SetText(
            summaryText,
            $"Workers {building.DeliveredPops}/{building.GetTargetPopCount()}" +
            $"   •   Incoming {building.ReservedIncomingPops}" +
            $"   •   Efficiency {building.GetProductionEfficiency():P0}");
        SetText(
            durationText,
            $"{building.FactoryShiftDurationHours}h");

        if (openToggle != null)
        {
            openToggle.SetIsOnWithoutNotify(building.IsFactoryOpen);
            UpdateToggleVisual(building.IsFactoryOpen);
        }

        RebuildShiftList(building);
        RefreshTimeline(building);
        UpdatePreparationSpan(building, _focusedShift);
    }

    private void RebuildShiftList(Building building)
    {
        if (shiftListContent == null ||
            shiftRowPrefab == null)
        {
            return;
        }

        float previousScrollPosition = shiftScrollRect != null
            ? shiftScrollRect.verticalNormalizedPosition
            : 1f;

        _shiftRows.Clear();
        if (_focusedShift != null &&
            !BuildingContainsShift(building, _focusedShift))
        {
            _focusedShift = null;
            _focusSource = ScheduleFocusSource.None;
        }

        for (int i = shiftListContent.childCount - 1; i >= 0; i--)
        {
            Transform child = shiftListContent.GetChild(i);
            if (emptyScheduleState != null &&
                child.gameObject == emptyScheduleState)
            {
                continue;
            }

            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        IReadOnlyList<FactoryShift> shifts = building.FactoryShifts;
        if (emptyScheduleState != null)
        {
            emptyScheduleState.SetActive(shifts.Count == 0);
        }

        for (int i = 0; i < shifts.Count; i++)
        {
            FactoryShiftRowView row = Instantiate(
                shiftRowPrefab,
                shiftListContent,
                false);
            row.name = $"Shift {i + 1:00}";
            row.gameObject.SetActive(true);
            _shiftRows.Add(shifts[i], row);
            row.Bind(
                building,
                shifts[i],
                i,
                SetStatus,
                HandleShiftHoverChanged);
            row.SetBackgroundColor(GetShiftRowColor(shifts[i]));
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(shiftListContent);
        if (shiftScrollRect != null)
        {
            shiftScrollRect.verticalNormalizedPosition =
                previousScrollPosition;
        }
    }

    private void ConfigureShiftListLayout()
    {
        if (shiftListContent == null) return;

        VerticalLayoutGroup layout =
            shiftListContent.GetComponent<VerticalLayoutGroup>();
        if (layout == null) return;

        // Shift rows publish a preferred height based on their resource count.
        // The list must consume that value while stretching every row to the
        // edited viewport width.
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private void HandleShiftHoverChanged(FactoryShift shift, bool isHovered)
    {
        if (isHovered)
        {
            SetScheduleFocus(
                shift,
                ScheduleFocusSource.ShiftRow,
                false);
        }
        else if (_focusSource == ScheduleFocusSource.ShiftRow &&
                 _focusedShift == shift)
        {
            ClearScheduleFocus();
        }
    }

    private void HandleHourHoverChanged(int hour, bool isHovered)
    {
        if (isHovered)
        {
            _hoveredHour = hour;
            FactoryShift shift = FindShiftAtHour(hour);
            if (shift != null)
            {
                SetScheduleFocus(
                    shift,
                    ScheduleFocusSource.Clock,
                    true);
            }
            else if (_focusSource == ScheduleFocusSource.Clock)
            {
                ClearScheduleFocus();
            }
        }
        else if (_hoveredHour == hour)
        {
            _hoveredHour = -1;
            if (_focusSource == ScheduleFocusSource.Clock)
            {
                ClearScheduleFocus();
            }
        }
    }

    private void UpdateClockHover()
    {
        if (hourCellsRoot == null) return;

        Canvas canvas = hourCellsRoot.GetComponentInParent<Canvas>();
        Camera eventCamera = canvas != null &&
                             canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        bool containsPointer = RectTransformUtility.RectangleContainsScreenPoint(
            hourCellsRoot,
            Input.mousePosition,
            eventCamera);

        if (!containsPointer)
        {
            if (_draggedShift != null)
            {
                // Keep the grabbed shift focused if the pointer briefly leaves
                // the clock while the mouse button is still held.
                _hoveredHour = -1;
            }
            else if (_isClockHovered && _hoveredHour >= 0)
            {
                HandleHourHoverChanged(_hoveredHour, false);
            }

            _isClockHovered = false;
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                hourCellsRoot,
                Input.mousePosition,
                eventCamera,
                out Vector2 localPoint) ||
            hourCellsRoot.rect.width <= 0f)
        {
            return;
        }

        float normalizedX = Mathf.Clamp01(
            (localPoint.x - hourCellsRoot.rect.xMin) /
            hourCellsRoot.rect.width);
        int hour = Mathf.Min(
            GameTimeManager.HoursPerDay - 1,
            Mathf.FloorToInt(normalizedX * GameTimeManager.HoursPerDay));

        _isClockHovered = true;
        if (hour != _hoveredHour)
        {
            if (_draggedShift != null)
            {
                // Hover focus must not jump to a shift that is being pushed by
                // the active drag.
                _hoveredHour = hour;
            }
            else
            {
                HandleHourHoverChanged(hour, true);
            }
        }
    }

    private void UpdateClockDrag()
    {
        if (Input.GetMouseButtonUp(0))
        {
            _draggedShift = null;
            if (_isClockHovered && _hoveredHour >= 0)
            {
                HandleHourHoverChanged(_hoveredHour, true);
            }

            return;
        }

        Building building = CurrentBuilding;
        if (building == null || !_isClockHovered || _hoveredHour < 0)
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            FactoryShift shift = FindShiftAtHour(_hoveredHour);
            if (shift == null) return;
            if (shift.IsActive)
            {
                SetScheduleFocus(
                    shift,
                    ScheduleFocusSource.Clock,
                    true);
                SetStatus("Active shifts cannot be moved.");
                return;
            }

            _draggedShift = shift;
            _lastDragHour = _hoveredHour;
            SetScheduleFocus(
                shift,
                ScheduleFocusSource.Clock,
                true);
            return;
        }

        if (_draggedShift == null || !Input.GetMouseButton(0)) return;

        int hourDelta = GetShortestHourDelta(
            _lastDragHour,
            _hoveredHour);
        if (hourDelta == 0) return;

        if (building.TryMoveFactoryShiftChain(_draggedShift, hourDelta))
        {
            _lastDragHour = _hoveredHour;
            SetScheduleFocus(
                _draggedShift,
                ScheduleFocusSource.Clock,
                false);
            SetStatus(string.Empty);
        }
        else
        {
            SetStatus("Unable to move this shift.");
        }
    }

    private static int GetShortestHourDelta(int fromHour, int toHour)
    {
        int forward = FactoryShift.WrapHour(toHour - fromHour);
        return forward <= GameTimeManager.HoursPerDay / 2
            ? forward
            : forward - GameTimeManager.HoursPerDay;
    }

    private void SetScheduleFocus(
        FactoryShift shift,
        ScheduleFocusSource source,
        bool scrollToShift)
    {
        if (shift == null || CurrentBuilding == null) return;

        _focusedShift = shift;
        _focusSource = source;
        ApplyScheduleFocus();

        if (scrollToShift &&
            _shiftRows.TryGetValue(shift, out FactoryShiftRowView row))
        {
            ScrollShiftIntoView(row.transform as RectTransform);
        }
    }

    private void ClearScheduleFocus()
    {
        _focusedShift = null;
        _focusSource = ScheduleFocusSource.None;
        ApplyScheduleFocus();
    }

    private void ApplyScheduleFocus()
    {
        foreach (KeyValuePair<FactoryShift, FactoryShiftRowView> pair in
                 _shiftRows)
        {
            if (pair.Value != null)
            {
                pair.Value.SetBackgroundColor(
                    GetShiftRowColor(pair.Key));
            }
        }

        Building building = CurrentBuilding;
        if (building != null)
        {
            RefreshTimeline(building);
        }

        UpdatePreparationSpan(building, _focusedShift);
    }

    private FactoryShift FindShiftAtHour(int hour)
    {
        Building building = CurrentBuilding;
        if (building == null) return null;

        IReadOnlyList<FactoryShift> shifts = building.FactoryShifts;
        for (int i = 0; i < shifts.Count; i++)
        {
            if (ShiftContainsHour(
                    shifts[i],
                    building.FactoryShiftDurationHours,
                    hour))
            {
                return shifts[i];
            }
        }

        return null;
    }

    private void ScrollShiftIntoView(RectTransform row)
    {
        if (row == null ||
            shiftScrollRect == null ||
            shiftListContent == null ||
            shiftScrollRect.viewport == null)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(shiftListContent);

        RectTransform viewport = shiftScrollRect.viewport;
        float scrollableHeight =
            shiftListContent.rect.height - viewport.rect.height;
        if (scrollableHeight <= 0f)
        {
            shiftScrollRect.verticalNormalizedPosition = 1f;
            return;
        }

        Bounds rowBounds = RectTransformUtility
            .CalculateRelativeRectTransformBounds(shiftListContent, row);
        float rowCenterFromTop =
            shiftListContent.rect.yMax - rowBounds.center.y;
        float targetOffset = Mathf.Clamp(
            rowCenterFromTop - viewport.rect.height * 0.5f,
            0f,
            scrollableHeight);

        shiftScrollRect.StopMovement();
        shiftScrollRect.verticalNormalizedPosition =
            1f - targetOffset / scrollableHeight;
    }

    private void UpdatePreparationSpan(
        Building building,
        FactoryShift shift)
    {
        HidePreparationSpans();
        if (building == null || shift == null || hourCellsRoot == null)
        {
            return;
        }

        int duration = building.FactoryShiftDurationHours;
        int spanStart = shift.GetPreparationStartHour();
        int spanLength = duration + shift.PreparationHours;
        int firstEnd = Mathf.Min(
            GameTimeManager.HoursPerDay,
            spanStart + spanLength);

        ShowPreparationSpan(0, spanStart, firstEnd);
        int wrappedLength = spanStart + spanLength -
            GameTimeManager.HoursPerDay;
        if (wrappedLength > 0)
        {
            ShowPreparationSpan(1, 0, wrappedLength);
        }
    }

    private void ShowPreparationSpan(int index, int startHour, int endHour)
    {
        while (_preparationSpanImages.Count <= index)
        {
            var spanObject = new GameObject(
                $"PreparationSpan {_preparationSpanImages.Count + 1}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            spanObject.layer = hourCellsRoot.gameObject.layer;
            spanObject.transform.SetParent(hourCellsRoot, false);

            Image image = spanObject.GetComponent<Image>();
            image.raycastTarget = false;
            _preparationSpanImages.Add(image);
        }

        Image span = _preparationSpanImages[index];
        span.sprite = preparationSpanSprite;
        span.type = preparationSpanSprite != null &&
                    preparationSpanSprite.border.sqrMagnitude > 0f
            ? Image.Type.Sliced
            : Image.Type.Simple;
        span.preserveAspect = false;
        span.fillCenter = true;
        span.color = preparationSpanColor;
        span.gameObject.SetActive(true);
        span.transform.SetAsLastSibling();

        RectTransform rect = span.rectTransform;
        rect.anchorMin = new Vector2(
            startHour / (float)GameTimeManager.HoursPerDay,
            0f);
        rect.anchorMax = new Vector2(
            endHour / (float)GameTimeManager.HoursPerDay,
            1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void HidePreparationSpans()
    {
        for (int i = 0; i < _preparationSpanImages.Count; i++)
        {
            if (_preparationSpanImages[i] != null)
            {
                _preparationSpanImages[i].gameObject.SetActive(false);
            }
        }
    }

    private static bool BuildingContainsShift(
        Building building,
        FactoryShift shift)
    {
        IReadOnlyList<FactoryShift> shifts = building.FactoryShifts;
        for (int i = 0; i < shifts.Count; i++)
        {
            if (shifts[i] == shift) return true;
        }

        return false;
    }

    private static bool ShiftContainsHour(
        FactoryShift shift,
        int durationHours,
        int hour)
    {
        if (shift == null) return false;

        int offset = FactoryShift.WrapHour(hour - shift.StartHour);
        return offset < durationHours;
    }

    private void AddShift()
    {
        if (CurrentBuilding == null) return;

        if (CurrentBuilding.TryAddFactoryShift(out _))
        {
            SetStatus(string.Empty);
        }
        else
        {
            SetStatus("No free time remains for another shift.");
        }
    }

    private void CacheHourCells()
    {
        _hourSquares.Clear();
        if (hourCellsRoot == null) return;

        for (int i = 0; i < hourCellsRoot.childCount; i++)
        {
            if (_hourSquares.Count >= GameTimeManager.HoursPerDay) break;

            RectTransform hourRect =
                hourCellsRoot.GetChild(i) as RectTransform;
            if (hourRect == null) continue;

            Image hourImage = hourRect.GetComponent<Image>();
            if (hourImage == null) continue;

            _hourSquares.Add(hourImage);
        }

    }

    private void RefreshTimeline(Building building)
    {
        for (int hour = 0; hour < _hourSquares.Count; hour++)
        {
            if (_focusedShift != null)
            {
                bool belongsToFocusedShift = ShiftContainsHour(
                        _focusedShift,
                        building.FactoryShiftDurationHours,
                        hour);
                if (belongsToFocusedShift)
                {
                    _hourSquares[hour].color = _focusedShift.IsActive
                        ? focusedHourColor
                        : inactiveFocusedHourColor;
                    continue;
                }

                FactoryShift otherShift = FindShiftAtHour(hour);
                _hourSquares[hour].color = otherShift == null
                    ? dimmedUnscheduledHourColor
                    : otherShift.IsActive
                        ? dimmedScheduledHourColor
                        : inactiveDimmedScheduledHourColor;
                continue;
            }

            FactoryShift scheduledShift = FindShiftAtHour(hour);
            if (scheduledShift == null)
            {
                _hourSquares[hour].color = unscheduledHourColor;
            }
            else if (!building.IsFactoryOpen)
            {
                _hourSquares[hour].color = closedScheduledHourColor;
            }
            else
            {
                _hourSquares[hour].color = scheduledShift.IsActive
                    ? scheduledHourColor
                    : inactiveScheduledHourColor;
            }
        }
    }

    private Color GetShiftRowColor(FactoryShift shift)
    {
        bool highlighted = shift == _focusedShift;
        bool dimmed = _focusedShift != null && !highlighted;

        if (shift != null && !shift.IsActive)
        {
            return highlighted
                ? inactiveFocusedShiftRowColor
                : dimmed
                    ? inactiveDimmedShiftRowColor
                    : inactiveNormalShiftRowColor;
        }

        return highlighted
            ? focusedShiftRowColor
            : dimmed
                ? dimmedShiftRowColor
                : normalShiftRowColor;
    }

    private void UpdateCurrentHourArrow(float currentTime)
    {
        if (currentHourArrow == null || hourCellsRoot == null)
        {
            return;
        }

        if (currentHourArrow.parent != hourCellsRoot)
        {
            currentHourArrow.SetParent(hourCellsRoot, false);
        }

        float normalizedTime = Mathf.Repeat(
            currentTime,
            GameTimeManager.HoursPerDay) / GameTimeManager.HoursPerDay;
        currentHourArrow.anchorMin = new Vector2(normalizedTime, 1f);
        currentHourArrow.anchorMax = new Vector2(normalizedTime, 1f);
        currentHourArrow.pivot = new Vector2(0.5f, 0f);
        currentHourArrow.anchoredPosition = new Vector2(0f, -1f);
        currentHourArrow.sizeDelta = new Vector2(8f, 5f);
        // Preparation/highlight images are created after the authored cells.
        // Keeping the arrow last guarantees that it renders above all of them.
        currentHourArrow.SetAsLastSibling();
    }

    private void UpdateToggleVisual(bool isOpen)
    {
        if (openToggle == null) return;

        Image toggleImage = openToggle.targetGraphic as Image;
        if (toggleImage != null)
        {
            toggleImage.color = isOpen
                ? OpenToggleColor
                : ClosedToggleColor;
        }

        TMP_Text label = openToggle.transform
            .Find("Label")?.GetComponent<TMP_Text>();
        if (label != null)
        {
            label.text = isOpen ? "Open" : "Closed";
        }
    }

    private void ShowSchedulingTab()
    {
        SetTabVisible(schedulingTab, true);
        SetTabVisible(ownedVehiclesTab, false);
        SetTabVisible(factoryStatusTab, false);
        SetTabButtonColor(schedulingTabButton, selectedTabColor);
        SetTabButtonColor(ownedVehiclesTabButton, unselectedTabColor);
        SetTabButtonColor(factoryStatusTabButton, unselectedTabColor);
        _hoveredHour = -1;
        _isClockHovered = false;
        _draggedShift = null;
    }

    private void ShowOwnedVehiclesTab()
    {
        SetTabVisible(schedulingTab, false);
        SetTabVisible(ownedVehiclesTab, true);
        SetTabVisible(factoryStatusTab, false);
        SetTabButtonColor(schedulingTabButton, unselectedTabColor);
        SetTabButtonColor(ownedVehiclesTabButton, selectedTabColor);
        SetTabButtonColor(factoryStatusTabButton, unselectedTabColor);
        _hoveredHour = -1;
        _isClockHovered = false;
        _draggedShift = null;
        _nextVehicleRefreshTime = 0f;
        RebuildVehicleRows(false);
    }

    private void ShowFactoryStatusTab()
    {
        SetTabVisible(schedulingTab, false);
        SetTabVisible(ownedVehiclesTab, false);
        SetTabVisible(factoryStatusTab, true);
        SetTabButtonColor(schedulingTabButton, unselectedTabColor);
        SetTabButtonColor(ownedVehiclesTabButton, unselectedTabColor);
        SetTabButtonColor(factoryStatusTabButton, selectedTabColor);
        _hoveredHour = -1;
        _isClockHovered = false;
        _draggedShift = null;
        if (factoryStatusView != null) factoryStatusView.Refresh();
    }

    private void RebuildVehicleRows(bool resetScrollPosition)
    {
        ClearVehicleRows();

        Building building = CurrentBuilding;
        if (building == null || ownedVehicleListContent == null ||
            industrialVehicleRowPrefab == null)
        {
            SetText(ownedVehicleCountText, "0 vehicles");
            return;
        }

        int displayedCount = 0;
        IReadOnlyList<VehicleAI> vehicles = building.OwnedVehicles;
        for (int i = 0; i < vehicles.Count; i++)
        {
            VehicleAI vehicle = vehicles[i];
            if (vehicle == null ||
                !(vehicle.vehicleData is ResourceTransportVehicleData))
            {
                continue;
            }

            GameObject rowObject = Instantiate(
                industrialVehicleRowPrefab,
                ownedVehicleListContent,
                false);
            IndustrialVehicleRowUI row =
                rowObject.GetComponent<IndustrialVehicleRowUI>();
            if (row == null)
            {
                Debug.LogError(
                    $"{industrialVehicleRowPrefab.name} needs an " +
                    $"{nameof(IndustrialVehicleRowUI)} component.",
                    industrialVehicleRowPrefab);
                Destroy(rowObject);
                continue;
            }

            rowObject.SetActive(true);
            row.Bind(building, vehicle);
            _vehicleRows.Add(row);
            displayedCount++;
        }

        if (emptyOwnedVehiclesState != null)
        {
            emptyOwnedVehiclesState.SetActive(displayedCount == 0);
        }

        SetText(
            ownedVehicleCountText,
            displayedCount == 1
                ? "1 vehicle"
                : $"{displayedCount} vehicles");
        RefreshVehicleListLayout(resetScrollPosition);
    }

    private void RefreshVehicleRows()
    {
        bool layoutChanged = false;
        for (int i = _vehicleRows.Count - 1; i >= 0; i--)
        {
            IndustrialVehicleRowUI row = _vehicleRows[i];
            if (row == null)
            {
                _vehicleRows.RemoveAt(i);
                layoutChanged = true;
                continue;
            }

            layoutChanged |= row.Refresh();
        }

        if (layoutChanged)
        {
            RefreshVehicleListLayout(false);
        }
    }

    private void ClearVehicleRows()
    {
        if (ownedVehicleListContent != null)
        {
            IndustrialVehicleRowUI[] rows =
                ownedVehicleListContent.GetComponentsInChildren<
                    IndustrialVehicleRowUI>(true);
            for (int i = 0; i < rows.Length; i++)
            {
                IndustrialVehicleRowUI row = rows[i];
                if (row == null) continue;
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
        }

        _vehicleRows.Clear();
    }

    private void RefreshVehicleListLayout(bool resetScrollPosition)
    {
        if (ownedVehiclesScrollRect == null ||
            ownedVehicleListContent == null)
        {
            return;
        }

        float previousPosition =
            ownedVehiclesScrollRect.verticalNormalizedPosition;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(
            ownedVehicleListContent);
        ownedVehiclesScrollRect.StopMovement();
        ownedVehiclesScrollRect.verticalNormalizedPosition =
            resetScrollPosition
                ? 1f
                : Mathf.Clamp01(previousPosition);
    }

    private static void SetTabVisible(GameObject tab, bool visible)
    {
        if (tab != null && tab.activeSelf != visible)
        {
            tab.SetActive(visible);
        }
    }

    private static void SetTabButtonColor(Button button, Color color)
    {
        if (button != null && button.targetGraphic is Image image)
        {
            image.color = color;
        }
    }

    private void ClosePanel()
    {
        BuildingPanelOverlayManager manager =
            FindFirstObjectByType<BuildingPanelOverlayManager>();
        if (manager != null)
        {
            manager.HideCurrentPanel();
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private void UnsubscribeFromBuilding()
    {
        if (_subscribedBuilding != null)
        {
            _subscribedBuilding.FactoryScheduleChanged -=
                HandleFactoryScheduleChanged;
            _subscribedBuilding.VehicleOwnershipChanged -=
                HandleVehicleOwnershipChanged;
            _subscribedBuilding.FactoryRuntimeChanged -=
                HandleFactoryRuntimeChanged;
            _subscribedBuilding = null;
        }
    }

    private void SetStatus(string message)
    {
        SetText(statusText, message);
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null)
        {
            target.text = value;
        }
    }
}
