using System;
using UnityEngine;
using UnityEngine.EventSystems;

// View modes describe the player's access to the game, independently of tools
// such as building, road placement, or demolition (PlayerMode).
public enum PlayerViewMode
{
    Normal,
    Overview,
    Traffic
}

[DisallowMultipleComponent]
public sealed class PlayerViewModeManager : MonoBehaviour
{
    public static PlayerViewModeManager Instance { get; private set; }
    public static bool AllowsGameplayActions =>
        Instance == null || Instance.CurrentViewMode == PlayerViewMode.Normal;

    public PlayerViewMode CurrentViewMode { get; private set; } = PlayerViewMode.Normal;
    public event Action<PlayerViewMode> ViewModeChanged;
    public bool TrafficNodeViewEnabled { get; private set; }
    public event Action<bool> TrafficNodeViewChanged;
    [SerializeField] private TrafficNodeViewStyle trafficNodeAppearance = TrafficNodeViewStyle.Default;
    [SerializeField] private IntersectionBuilderStyle intersectionBuilderAppearance = IntersectionBuilderStyle.Default;
    [SerializeField] private IntersectionControlPanel intersectionControlPanel;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    public void SetNormalView() => SetViewMode(PlayerViewMode.Normal);
    public void SetOverviewView() => SetViewMode(PlayerViewMode.Overview);
    public void SetTrafficView() => SetViewMode(PlayerViewMode.Traffic);

    private void Start()
    {
        trafficNodeAppearance = trafficNodeAppearance.Validated();
        var overlay = GetComponent<TrafficNodeViewOverlay>();
        if (overlay == null) overlay = gameObject.AddComponent<TrafficNodeViewOverlay>();
        overlay.Initialize(this, TrafficSystemBackend.Instance, trafficNodeAppearance);
        var builder = GetComponent<IntersectionBuilderTool>();
        if (builder == null) builder = gameObject.AddComponent<IntersectionBuilderTool>();
        if (intersectionControlPanel == null)
        {
            intersectionControlPanel = FindFirstObjectByType<IntersectionControlPanel>(
                FindObjectsInactive.Include);
        }
        builder.ApplyAppearance(GetIntersectionBuilderAppearance());
        builder.Initialize(this, TrafficSystemBackend.Instance, CameraController.Instance, overlay);
        if (intersectionControlPanel != null)
        {
            intersectionControlPanel.Initialize(
                this, builder, intersectionBuilderAppearance);
        }
        else
        {
            Debug.LogWarning(
                "The authored Intersection Control Panel is missing. Use " +
                "Tools/Mini Colonies/Install Intersection Control Panel.",
                this);
        }
        var lightOverlay = GetComponent<IntersectionTrafficLightOverlay>();
        if (lightOverlay == null) lightOverlay = gameObject.AddComponent<IntersectionTrafficLightOverlay>();
        lightOverlay.Initialize(TrafficSystemBackend.Instance, intersectionBuilderAppearance);
    }

    private void OnValidate()
    {
        trafficNodeAppearance = trafficNodeAppearance.Validated();
        intersectionBuilderAppearance = intersectionBuilderAppearance.Validated();
    }

    public void RefreshIntersectionBuilderAppearance()
    {
        intersectionBuilderAppearance = intersectionBuilderAppearance.Validated();
        GetComponent<IntersectionBuilderTool>()?.ApplyAppearance(GetIntersectionBuilderAppearance());
        intersectionControlPanel?.ApplyAppearance(intersectionBuilderAppearance);
        GetComponent<IntersectionTrafficLightOverlay>()?.ApplyAppearance(intersectionBuilderAppearance);
    }

    private IntersectionBuilderStyle GetIntersectionBuilderAppearance()
    {
        var style = intersectionBuilderAppearance.Validated();
        if (style.trafficLightIndexFont == null && intersectionControlPanel != null)
            style.trafficLightIndexFont = intersectionControlPanel.GameFont;
        return style;
    }

    // Called explicitly by the Inspector after a style edit, never polled.
    public void RefreshTrafficNodeAppearance()
    {
        trafficNodeAppearance = trafficNodeAppearance.Validated();
        GetComponent<TrafficNodeViewOverlay>()?.ApplyAppearance(trafficNodeAppearance);
    }

    public void ToggleTrafficNodeView() => SetTrafficNodeView(!TrafficNodeViewEnabled);

    public void SetTrafficNodeView(bool enabled)
    {
        enabled &= CurrentViewMode == PlayerViewMode.Traffic;
        if (TrafficNodeViewEnabled == enabled) return;
        TrafficNodeViewEnabled = enabled;
        TrafficNodeViewChanged?.Invoke(enabled);
    }

    public void SetViewMode(PlayerViewMode mode)
    {
        if (mode != PlayerViewMode.Normal &&
            mode != PlayerViewMode.Overview && mode != PlayerViewMode.Traffic)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (mode == CurrentViewMode) return;

        // Publish the new permissions before cancellation callbacks run.
        CurrentViewMode = mode;
        if (mode != PlayerViewMode.Traffic) SetTrafficNodeView(false);
        if (!AllowsGameplayActions)
        {
            RoadBuilderInput.Instance?.TryCancelActivePlacement();
            ResourceTransportVehicleDragSource.TryCancelActiveDrag();
            PopTransportVehicleDragSource.TryCancelActiveDrag();
            PlayerActionManager.Instance?.CancelAllActions();

            foreach (var placement in FindObjectsByType<BuildingPlacementInput>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                placement.CancelPlacementPreview();

            // A button click may run after these components' Update methods.
            // Clear their visuals now, rather than leaving them visible until
            // the next frame (or indefinitely if a component is disabled).
            foreach (var grid in FindObjectsByType<GridOverlayHandler>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                grid.HideOverlay();

            foreach (var trafficHover in FindObjectsByType<TrafficHoverDebugger>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                trafficHover.ClearHover();

            foreach (var overlay in FindObjectsByType<BuildingPanelOverlayManager>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                overlay.HideCurrentPanel();

            TooltipManager.Hide();
        }

        // A hidden normal-view button must not retain keyboard focus.
        EventSystem.current?.SetSelectedGameObject(null);
        ViewModeChanged?.Invoke(mode);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
