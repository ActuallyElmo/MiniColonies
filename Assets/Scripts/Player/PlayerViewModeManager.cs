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
