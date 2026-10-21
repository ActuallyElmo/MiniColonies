using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Binds the authored intersection-control panel to backend state.</summary>
[DisallowMultipleComponent]
public sealed class IntersectionControlPanel : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Serializable]
    public sealed class ModeOptionView
    {
        public IntersectionRuleType mode = IntersectionRuleType.FreeForAll;
        public Button button;
        public Image intersectionIcon;
        public Image typeIcon;
        public GameObject selectedIndicator;
        public GameObject detailsRoot;
        public TMP_Text dropdownLabel;
    }

    [Header("Authored panel")]
    [SerializeField] private Canvas overlayCanvas;
    [SerializeField] private RectTransform panelRect;
    // Retained for compatibility with the previous world-following layout.
    [SerializeField, HideInInspector]
    private float intersectionPanelDistance = 96f;
    [SerializeField, Min(0f)]
    [Tooltip("Inset from the right screen edge, in Canvas units.")]
    private float rightScreenMargin = 20f;
    [SerializeField] private List<ModeOptionView> modeOptions =
        new List<ModeOptionView>();

    [Header("Priority road controls")]
    [SerializeField] private TMP_Text priorityDirectionsText;
    [SerializeField] private Button choosePriorityRoadButton;
    [SerializeField] private TMP_Text choosePriorityRoadButtonText;

    [Header("Traffic light controls")]
    [SerializeField] private Slider lightDurationSlider;
    [SerializeField] private TMP_Text lightDurationValueText;
    [SerializeField] private Slider smartWaitSlider;
    [SerializeField] private TMP_Text smartWaitValueText;

    [Header("Connection controls")]
    [SerializeField] private Button deleteAllConnectionsButton;
    [SerializeField] private Button restoreDefaultConnectionsButton;

    private PlayerViewModeManager views;
    private IntersectionBuilderTool builder;
    private IntersectionBuilderStyle appearance;
    private Vector2Int focusedCell;
    private Vector2Int pendingCycleCell;
    private bool hasFocusedCell;
    private bool cycleChangePending;
    private float pendingCycleSeconds;
    private bool pendingSmartWait;
    private float cycleCommitTime;
    private readonly List<UnityAction> modeCallbacks = new List<UnityAction>();
    private UnityAction choosePriorityCallback;
    private UnityAction<float> lightDurationCallback;
    private UnityAction<float> smartWaitCallback;
    private UnityAction deleteAllConnectionsCallback;
    private UnityAction restoreDefaultConnectionsCallback;
    private bool dragging;
    private Vector3 dragStartPanelPosition;
    private Vector3 dragStartPointerPosition;
    private readonly Vector3[] panelWorldCorners = new Vector3[4];

    public bool IsVisible => panelRect != null && panelRect.gameObject.activeSelf;
    public TMP_FontAsset GameFont => lightDurationValueText != null
        ? lightDurationValueText.font
        : priorityDirectionsText != null ? priorityDirectionsText.font : null;

    public void Initialize(
        PlayerViewModeManager viewManager,
        IntersectionBuilderTool intersectionBuilder,
        IntersectionBuilderStyle style)
    {
        UnwireInput();
        Unsubscribe();
        views = viewManager;
        builder = intersectionBuilder;
        appearance = style.Validated();
        ApplyScreenAnchor();
        WireInput();
        Subscribe();
        RefreshPanel();
    }

    public void ApplyAppearance(IntersectionBuilderStyle style)
    {
        appearance = style.Validated();
        ApplyIconAppearance();
    }

    private void Subscribe()
    {
        if (builder != null)
            builder.FocusedIntersectionChanged += RefreshPanel;
        if (views != null)
            views.ViewModeChanged += HandleViewChanged;
    }

    private void Unsubscribe()
    {
        if (builder != null)
            builder.FocusedIntersectionChanged -= RefreshPanel;
        if (views != null)
            views.ViewModeChanged -= HandleViewChanged;
    }

    private void WireInput()
    {
        modeCallbacks.Clear();
        for (int i = 0; i < modeOptions.Count; i++)
        {
            ModeOptionView option = modeOptions[i];
            IntersectionRuleType mode = option.mode;
            UnityAction callback = () => SelectMode(mode);
            modeCallbacks.Add(callback);
            option.button?.onClick.AddListener(callback);
        }

        choosePriorityCallback = BeginPriorityRoadSelection;
        choosePriorityRoadButton?.onClick.AddListener(choosePriorityCallback);
        lightDurationCallback = PreviewCycleDuration;
        lightDurationSlider?.onValueChanged.AddListener(lightDurationCallback);
        smartWaitCallback = seconds => PreviewDuration(seconds, true);
        smartWaitSlider?.onValueChanged.AddListener(smartWaitCallback);
        deleteAllConnectionsCallback = DeleteAllConnections;
        deleteAllConnectionsButton?.onClick.AddListener(deleteAllConnectionsCallback);
        restoreDefaultConnectionsCallback = RestoreDefaultConnections;
        restoreDefaultConnectionsButton?.onClick.AddListener(restoreDefaultConnectionsCallback);
    }

    private void UnwireInput()
    {
        int count = Mathf.Min(modeOptions.Count, modeCallbacks.Count);
        for (int i = 0; i < count; i++)
            modeOptions[i].button?.onClick.RemoveListener(modeCallbacks[i]);
        modeCallbacks.Clear();

        if (choosePriorityCallback != null)
            choosePriorityRoadButton?.onClick.RemoveListener(choosePriorityCallback);
        if (lightDurationCallback != null)
            lightDurationSlider?.onValueChanged.RemoveListener(lightDurationCallback);
        if (smartWaitCallback != null)
            smartWaitSlider?.onValueChanged.RemoveListener(smartWaitCallback);
        if (deleteAllConnectionsCallback != null)
            deleteAllConnectionsButton?.onClick.RemoveListener(deleteAllConnectionsCallback);
        if (restoreDefaultConnectionsCallback != null)
            restoreDefaultConnectionsButton?.onClick.RemoveListener(restoreDefaultConnectionsCallback);
        choosePriorityCallback = null;
        lightDurationCallback = null;
        smartWaitCallback = null;
        deleteAllConnectionsCallback = null;
        restoreDefaultConnectionsCallback = null;
    }

    private void OnDestroy()
    {
        UnwireInput();
        Unsubscribe();
    }

    private void OnEnable()
    {
        dragging = false;
        ApplyScreenAnchor();
    }

    private void OnDisable()
    {
        dragging = false;
        CommitPendingCycle();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        dragging = false;
        // Drag events bubble from child controls. Only the empty card background
        // may move the panel; buttons and sliders keep their own input behavior.
        if (!isActiveAndEnabled || !IsVisible || eventData == null ||
            eventData.button != PointerEventData.InputButton.Left ||
            eventData.pointerPressRaycast.gameObject != panelRect.gameObject ||
            !TryGetDragContext(out RectTransform parent, out Camera camera) ||
            !RectTransformUtility.ScreenPointToWorldPointInRectangle(
                parent, eventData.position, camera, out dragStartPointerPosition)) return;

        dragStartPanelPosition = panelRect.position;
        dragging = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging || !isActiveAndEnabled || !IsVisible || eventData == null ||
            !TryGetDragContext(out RectTransform parent, out Camera camera) ||
            !RectTransformUtility.ScreenPointToWorldPointInRectangle(
                parent, eventData.position, camera, out Vector3 pointerPosition)) return;

        // UI input follows the pointer directly, including while simulation is paused.
        panelRect.position = dragStartPanelPosition + pointerPosition - dragStartPointerPosition;
        Canvas.ForceUpdateCanvases();
        ClampPanelToScreen(parent, camera);
    }

    public void OnEndDrag(PointerEventData eventData) => dragging = false;

    private bool TryGetDragContext(out RectTransform parent, out Camera camera)
    {
        parent = panelRect != null ? panelRect.parent as RectTransform : null;
        if (overlayCanvas == null && panelRect != null)
            overlayCanvas = panelRect.GetComponentInParent<Canvas>();
        Canvas canvas = overlayCanvas != null ? overlayCanvas.rootCanvas : null;
        camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
        return parent != null && canvas != null;
    }

    private void ClampPanelToScreen(RectTransform parent, Camera camera)
    {
        const float padding = 8f;
        panelRect.GetWorldCorners(panelWorldCorners);
        Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < panelWorldCorners.Length; i++)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, panelWorldCorners[i]);
            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);
        }

        Vector2 correction = new Vector2(
            ScreenCorrection(minimum.x, maximum.x, Screen.width, padding),
            ScreenCorrection(minimum.y, maximum.y, Screen.height, padding));
        if (correction == Vector2.zero) return;
        Vector2 pivot = RectTransformUtility.WorldToScreenPoint(camera, panelRect.position);
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                parent, pivot + correction, camera, out Vector3 position))
            panelRect.position = position;
    }

    private static float ScreenCorrection(float minimum, float maximum, float size, float padding)
    {
        if (maximum - minimum > size - 2f * padding)
            return size * 0.5f - (minimum + maximum) * 0.5f;
        if (minimum < padding) return padding - minimum;
        if (maximum > size - padding) return size - padding - maximum;
        return 0f;
    }

    private void Update()
    {
        if (cycleChangePending && Time.unscaledTime >= cycleCommitTime)
            CommitPendingCycle();
    }

    private void OnValidate() => ApplyScreenAnchor();

    private void HandleViewChanged(PlayerViewMode mode)
    {
        if (mode != PlayerViewMode.Traffic) CommitPendingCycle();
        RefreshPanel();
    }

    private void RefreshPanel()
    {
        if (panelRect == null) return;
        ControlledNodeRecord node = null;
        bool visible = views != null &&
                       views.CurrentViewMode == PlayerViewMode.Traffic &&
                       builder != null &&
                       builder.TryGetFocusedIntersection(out node, out _);
        if (!visible) CommitPendingCycle();
        if (!visible) dragging = false;
        if (visible && (!hasFocusedCell || focusedCell != node.GridPosition))
        {
            dragging = false;
            ApplyScreenAnchor();
        }
        panelRect.gameObject.SetActive(visible);
        hasFocusedCell = visible;
        if (!visible) return;

        if (cycleChangePending && pendingCycleCell != node.GridPosition) CommitPendingCycle();
        focusedCell = node.GridPosition;
        IntersectionData data = GetCurrentData();
        IntersectionRuleType selected = data != null
            ? data.RuleType
            : IntersectionRuleType.FreeForAll;
        RefreshModeOptions(selected);
        RefreshPriorityDetails(data);
        RefreshTrafficLightDetails(data);
        bool canEditConnections = builder.CanEditFocusedConnections;
        if (deleteAllConnectionsButton != null)
            deleteAllConnectionsButton.interactable = canEditConnections;
        if (restoreDefaultConnectionsButton != null)
            restoreDefaultConnectionsButton.interactable = canEditConnections;
        ApplyIconAppearance();
    }

    private void RefreshModeOptions(IntersectionRuleType selected)
    {
        for (int i = 0; i < modeOptions.Count; i++)
        {
            ModeOptionView option = modeOptions[i];
            bool isSelected = option.mode == selected;
            option.selectedIndicator?.SetActive(isSelected);
            option.detailsRoot?.SetActive(isSelected);
            if (option.dropdownLabel != null)
                option.dropdownLabel.text = isSelected ? "v" : ">";
        }
    }

    private void RefreshPriorityDetails(IntersectionData data)
    {
        if (priorityDirectionsText != null)
        {
            priorityDirectionsText.alignment = TextAlignmentOptions.Center;
            priorityDirectionsText.text = data != null &&
                                          data.PriorityDirectionBitA != 0
                ? $"Priority: {DirectionName(data.PriorityDirectionBitA)} / " +
                  DirectionName(data.PriorityDirectionBitB)
                : "Priority road not chosen";
        }

        bool choosing = builder != null && builder.IsChoosingPriorityRoad;
        if (choosePriorityRoadButton != null)
            choosePriorityRoadButton.interactable = !choosing;
        if (choosePriorityRoadButtonText != null)
        {
            choosePriorityRoadButtonText.text = choosing
                ? "Select an incoming road..."
                : "Choose priority road";
        }
    }

    private void RefreshTrafficLightDetails(IntersectionData data)
    {
        float duration = data != null
            ? data.TrafficLightCycleSeconds
            : IntersectionControlUtility.DefaultLightDurationSeconds;
        if (lightDurationSlider != null)
        {
            lightDurationSlider.minValue =
                IntersectionControlUtility.MinimumLightDurationSeconds;
            lightDurationSlider.maxValue =
                IntersectionControlUtility.MaximumLightDurationSeconds;
            lightDurationSlider.wholeNumbers = true;
            lightDurationSlider.SetValueWithoutNotify(
                IntersectionControlUtility.ValidateLightDuration(duration));
        }
        if (lightDurationValueText != null)
            lightDurationValueText.text = FormatDuration(duration);
        float wait = data != null ? data.SmartTrafficLightWaitSeconds :
            IntersectionControlUtility.DefaultSmartWaitSeconds;
        if (smartWaitSlider != null)
        {
            smartWaitSlider.minValue = IntersectionControlUtility.MinimumSmartWaitSeconds;
            smartWaitSlider.maxValue = IntersectionControlUtility.MaximumSmartWaitSeconds;
            smartWaitSlider.wholeNumbers = true;
            smartWaitSlider.SetValueWithoutNotify(wait);
        }
        if (smartWaitValueText != null) smartWaitValueText.text = FormatDuration(wait);
    }

    private void ApplyIconAppearance()
    {
        for (int i = 0; i < modeOptions.Count; i++)
        {
            ModeOptionView option = modeOptions[i];
            if (option.intersectionIcon != null)
            {
                option.intersectionIcon.sprite = appearance.intersectionSprite;
                option.intersectionIcon.color = appearance.intersectionIconColor;
                option.intersectionIcon.preserveAspect = true;
            }
            if (option.typeIcon == null) continue;
            option.typeIcon.sprite = appearance.GetRuleTypeSprite(option.mode);
            option.typeIcon.color = appearance.intersectionTypeIconColor;
            option.typeIcon.preserveAspect = true;
            option.typeIcon.enabled = option.typeIcon.sprite != null;
        }
    }

    private void SelectMode(IntersectionRuleType mode)
    {
        CommitPendingCycle();
        IntersectionData previous = GetCurrentData();
        bool needsPriorityChoice = mode == IntersectionRuleType.PriorityRoad &&
            (previous == null || previous.PriorityDirectionBitA == 0 ||
             previous.PriorityDirectionBitB == 0);
        RoadSystemBackend roads = RoadSystemBackend.Instance;
        if (!hasFocusedCell || roads == null ||
            !roads.SetIntersectionRuleType(focusedCell, mode)) return;

        if (mode == IntersectionRuleType.PriorityRoad && needsPriorityChoice)
            builder?.BeginPriorityRoadSelection();
        else if (mode != IntersectionRuleType.PriorityRoad)
            builder?.CancelPriorityRoadSelection();
        RefreshPanel();
    }

    private void DeleteAllConnections()
    {
        builder?.ClearAllConnections();
        RefreshPanel();
    }

    private void RestoreDefaultConnections()
    {
        builder?.RestoreDefaultConnections();
        RefreshPanel();
    }

    private void BeginPriorityRoadSelection() =>
        builder?.BeginPriorityRoadSelection();

    private void PreviewCycleDuration(float seconds) => PreviewDuration(seconds, false);

    private void PreviewDuration(float seconds, bool smart)
    {
        if (!hasFocusedCell) return;
        pendingCycleCell = focusedCell;
        pendingSmartWait = smart;
        pendingCycleSeconds = Mathf.Round(seconds);
        cycleChangePending = true;
        // UI debounce stays responsive while simulation time is paused.
        cycleCommitTime = Time.unscaledTime + 0.3f;
        TMP_Text valueText = smart ? smartWaitValueText : lightDurationValueText;
        if (valueText != null) valueText.text = FormatDuration(pendingCycleSeconds);
    }

    private void CommitPendingCycle()
    {
        if (!cycleChangePending) return;
        cycleChangePending = false;
        if (pendingSmartWait)
        {
            RoadSystemBackend.Instance?.SetIntersectionSmartTrafficLightWait(
                pendingCycleCell, pendingCycleSeconds);
            return;
        }
        RoadSystemBackend.Instance?.SetIntersectionTrafficLightCycle(
            pendingCycleCell,
            pendingCycleSeconds);
    }

    private IntersectionData GetCurrentData()
    {
        RoadSystemBackend roads = RoadSystemBackend.Instance;
        if (!hasFocusedCell || roads == null ||
            !roads.Intersections.TryGetValue(
                focusedCell, out IntersectionData data)) return null;
        return data;
    }

    /// <summary>Used by runtime initialization and the authored-panel editor utility.</summary>
    public bool ApplyScreenAnchor()
    {
        rightScreenMargin = float.IsNaN(rightScreenMargin) || float.IsInfinity(rightScreenMargin)
            ? 20f : Mathf.Max(0f, rightScreenMargin);
        if (panelRect == null) return false;
        var anchor = new Vector2(1f, 0.5f);
        var position = new Vector2(-rightScreenMargin, 0f);
        if (panelRect.anchorMin == anchor && panelRect.anchorMax == anchor &&
            panelRect.pivot == anchor && panelRect.anchoredPosition == position) return false;
        panelRect.anchorMin = panelRect.anchorMax = anchor;
        panelRect.pivot = anchor;
        panelRect.anchoredPosition = position;
        return true;
    }

    private static string DirectionName(int directionBit)
    {
        switch (directionBit)
        {
            case 1: return "North";
            case 2: return "North-east";
            case 4: return "East";
            case 8: return "South-east";
            case 16: return "South";
            case 32: return "South-west";
            case 64: return "West";
            case 128: return "North-west";
            default: return "Unassigned";
        }
    }

    private static string FormatDuration(float seconds) =>
        $"{Mathf.RoundToInt(seconds)} seconds";
}
