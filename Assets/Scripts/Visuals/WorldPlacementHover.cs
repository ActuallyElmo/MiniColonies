using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent, RequireComponent(typeof(WorldManager), typeof(WorldPlacementHighlight))]
public class WorldPlacementHover : MonoBehaviour
{
    // Preserve field names/types from the legacy MapResourceHover component.
    public Material highlightMaterial;
    [Tooltip("The top-edge sprite used by building placement.")]
    public Sprite outlineSprite;
    [Tooltip("Single-pixel inner corner in the same atlas as the edge sprite.")]
    public Sprite innerCornerSprite;
    public Color highlightColor = new Color(1f, 0.8f, 0.25f, 1f);
    [Min(0f)] public float highlightHeightOffset = 0.025f;
    private WorldManager world;
    private WorldPlacementHighlight highlight;
    private Camera viewCamera;
    private object hovered;
    private int buildingRevision;
    private Building outlinedSelection;
    private MapResourceSource outlinedResource;
    private int selectionRevision;
    private PlayerActionManager actions;
    private PlayerViewModeManager views;
    private readonly BuildingRangeDisplay hoveredRanges = new BuildingRangeDisplay();
    private readonly BuildingRangeDisplay selectedRanges = new BuildingRangeDisplay();

    protected virtual void OnEnable()
    {
        world = GetComponent<WorldManager>();
        highlight = GetComponent<WorldPlacementHighlight>();
        // Legacy serialized components can predate RequireComponent.
        if (highlight == null) highlight = gameObject.AddComponent<WorldPlacementHighlight>();
        world.ResourcesChanged += ClearFeedback;
        BindEvents();
        RefreshSelection();
    }

    protected virtual void OnDisable()
    {
        if (world != null) world.ResourcesChanged -= ClearFeedback;
        UnbindEvents();
        ClearFeedback();
    }

    protected virtual void LateUpdate()
    {
        // Managers may awaken after this component or be replaced on scene load.
        BindEvents();
        // This bounded check catches disabled/destroyed targets without per-building polling.
        RefreshSelection();
        // Camera motion changes hover even with a stationary pointer. One nonallocating pick per frame.
        if (world == null || world.IsGenerating || !PlayerViewModeManager.AllowsGameplayActions ||
            (PlayerActionManager.Instance != null && PlayerActionManager.Instance.currentMode != PlayerMode.None) ||
            (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
        {
            ClearHover();
            return;
        }
        if (viewCamera == null || !viewCamera.isActiveAndEnabled) viewCamera = Camera.main;
        Building building = null;
        MapResourceSource source = null;
        if (viewCamera != null && Physics.Raycast(viewCamera.ScreenPointToRay(Input.mousePosition),
                out var hit, 10000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            WorldPlacementPicking.Resolve(world, hit, out building, out source);
        }
        SetHovered(building, source);
    }

    private void SetHovered(Building building, MapResourceSource source)
    {
        int revision = BuildingSystemBackend.Instance != null ? BuildingSystemBackend.Instance.AuthoringRevision : 0;
        object target = building != null ? (object)building : source;
        if (ReferenceEquals(hovered, target) && (building == null || (buildingRevision == revision &&
            (building == outlinedSelection || RangeStateIsCurrent(building, hoveredRanges))))) return;
        ClearHover();
        hovered = target;
        buildingRevision = revision;
        // Selection wins on the same footprint; another building can still hover independently.
        if (building != null && building != outlinedSelection)
        {
            highlight.Show(building.occupiedCells, highlightMaterial, outlineSprite, innerCornerSprite,
                highlightColor, highlightHeightOffset);
            ShowRanges(building, hoveredRanges);
        }
        else if (source != null)
        {
            if (source.Definition != null) TooltipManager.Show(source.Definition.displayName, this);
            if (!ReferenceEquals(source, outlinedResource))
                highlight.Show(source.Cells, highlightMaterial, outlineSprite, innerCornerSprite,
                    highlightColor, highlightHeightOffset);
        }
    }

    private void ClearHover()
    {
        if (hovered == null) return;
        hoveredRanges.Clear();
        TooltipManager.Hide(this);
        hovered = null;
        if (highlight != null) highlight.Clear();
    }

    private void RefreshSelection()
    {
        Building building = actions != null ? actions.SelectedBuilding : null;
        MapResourceSource resource = actions != null ? actions.SelectedResource : null;
        if (world == null || world.IsGenerating || !PlayerViewModeManager.AllowsGameplayActions ||
            (actions != null && actions.currentMode != PlayerMode.None))
        {
            building = null;
            resource = null;
        }
        if (building != null && !building.isActiveAndEnabled) building = null;
        int revision = BuildingSystemBackend.Instance != null ? BuildingSystemBackend.Instance.AuthoringRevision : 0;
        if (ReferenceEquals(outlinedSelection, building) && ReferenceEquals(outlinedResource, resource) &&
            selectionRevision == revision && RangeStateIsCurrent(building, selectedRanges)) return;
        selectedRanges.Clear();
        if (highlight != null) highlight.ClearSelection();
        outlinedSelection = building;
        outlinedResource = resource;
        selectionRevision = revision;
        if (building != null)
        {
            highlight.ShowSelection(building.occupiedCells, highlightMaterial, outlineSprite, innerCornerSprite,
                highlightColor, highlightHeightOffset);
            ShowRanges(building, selectedRanges);
        }
        else if (resource != null)
            highlight.ShowSelection(resource.Cells, highlightMaterial, outlineSprite, innerCornerSprite,
                highlightColor, highlightHeightOffset);
    }

    private static bool RangeStateIsCurrent(Building building, BuildingRangeDisplay display)
    {
        bool hasRanges = building != null && building.data != null && building.data.ranges != null && building.data.ranges.Count > 0;
        return hasRanges ? display.Evaluation != null && display.Evaluation.IsCurrent() : display.Evaluation == null;
    }

    private void ShowRanges(Building building, BuildingRangeDisplay display)
    {
        if (building.data == null || building.data.ranges == null || building.data.ranges.Count == 0 ||
            BuildingSystemBackend.Instance == null) return;
        display.Show(BuildingSystemBackend.Instance.RequestBuildingRanges(world, building), highlight, highlightMaterial,
            outlineSprite, innerCornerSprite, highlightHeightOffset + 0.015f);
    }

    private void HandleSelectionChanged(Building building)
    {
        // Re-pick after state transitions, including deselection under a stationary pointer.
        ClearHover();
        RefreshSelection();
    }

    private void HandleModeChanged(PlayerMode mode)
    {
        ClearHover();
        RefreshSelection();
    }

    private void HandleResourceSelectionChanged(MapResourceSource source)
    {
        ClearHover();
        RefreshSelection();
    }

    private void HandleViewChanged(PlayerViewMode mode)
    {
        ClearHover();
        RefreshSelection();
    }

    private void ClearFeedback()
    {
        ClearHover();
        outlinedSelection = null;
        outlinedResource = null;
        selectedRanges.Clear();
        if (highlight != null) highlight.ClearSelection();
    }

    private void BindEvents()
    {
        if (!ReferenceEquals(actions, PlayerActionManager.Instance))
        {
            if (actions != null)
            {
                actions.BuildingSelectionChanged -= HandleSelectionChanged;
                actions.ResourceSelectionChanged -= HandleResourceSelectionChanged;
                actions.ModeChanged -= HandleModeChanged;
            }
            actions = PlayerActionManager.Instance;
            if (actions != null)
            {
                actions.BuildingSelectionChanged += HandleSelectionChanged;
                actions.ResourceSelectionChanged += HandleResourceSelectionChanged;
                actions.ModeChanged += HandleModeChanged;
            }
            ClearHover();
        }
        if (!ReferenceEquals(views, PlayerViewModeManager.Instance))
        {
            if (views != null) views.ViewModeChanged -= HandleViewChanged;
            views = PlayerViewModeManager.Instance;
            if (views != null) views.ViewModeChanged += HandleViewChanged;
        }
    }

    private void UnbindEvents()
    {
        if (actions != null)
        {
            actions.BuildingSelectionChanged -= HandleSelectionChanged;
            actions.ResourceSelectionChanged -= HandleResourceSelectionChanged;
            actions.ModeChanged -= HandleModeChanged;
        }
        if (views != null) views.ViewModeChanged -= HandleViewChanged;
        actions = null;
        views = null;
    }
}
