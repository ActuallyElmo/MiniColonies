using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Accepts resource-vehicle type drags while the industrial vehicle tab is
/// visible.
/// </summary>
[DisallowMultipleComponent]
public sealed class IndustrialVehicleDropTarget : MonoBehaviour, IDropHandler
{
    [SerializeField] private IndustrialBuildingInfoPanel industrialPanel;
    [Header("Drag Feedback")]
    [Tooltip("Shown when the industrial vehicle can be added.")]
    [SerializeField] private GameObject validDropHighlighter;
    [Tooltip("Shown when the factory does not have enough vehicle space.")]
    [SerializeField] private GameObject fullSpaceHighlighter;
    [Tooltip("Shown when the vehicle type or carried outputs are incompatible.")]
    [SerializeField] private GameObject invalidDropHighlighter;

    private static IndustrialVehicleDropTarget activeTarget;
    private ResourceTransportVehicleDragSource activeSource;

    private void Awake()
    {
        if (industrialPanel == null)
        {
            industrialPanel =
                GetComponentInParent<IndustrialBuildingInfoPanel>(true);
        }

        HideAllHighlighters();
    }

    private void OnEnable()
    {
        activeTarget = this;
        HideAllHighlighters();
    }

    private void OnDisable()
    {
        if (activeTarget == this) activeTarget = null;
        activeSource = null;
        HideAllHighlighters();
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (!PlayerViewModeManager.AllowsGameplayActions) return;

        ResourceTransportVehicleDragSource source =
            eventData.pointerDrag != null
                ? eventData.pointerDrag.GetComponent<
                    ResourceTransportVehicleDragSource>()
                : null;
        if (source == null || source != activeSource) return;

        Building building = GetBuilding();
        if (building != null &&
            building.GetVehicleAssignmentResult(source.VehicleType) ==
            Building.VehicleAssignmentResult.Allowed)
        {
            building.TryAssignVehicle(source.VehicleType, out _);
        }

        EndDragInternal(source);
    }

    public static bool TryBeginDrag(ResourceTransportVehicleDragSource source)
    {
        if (activeTarget == null || source == null ||
            activeTarget.GetBuilding() == null)
        {
            return false;
        }

        activeTarget.activeSource = source;
        activeTarget.RefreshFeedback(source);
        return true;
    }

    public static void RefreshDragFeedback(
        ResourceTransportVehicleDragSource source)
    {
        if (activeTarget != null && activeTarget.activeSource == source)
        {
            activeTarget.RefreshFeedback(source);
        }
    }

    public static void EndDrag(ResourceTransportVehicleDragSource source)
    {
        activeTarget?.EndDragInternal(source);
    }

    private void RefreshFeedback(ResourceTransportVehicleDragSource source)
    {
        Building building = GetBuilding();
        Building.VehicleAssignmentResult result = building != null
            ? building.GetVehicleAssignmentResult(source.VehicleType)
            : Building.VehicleAssignmentResult.UnsupportedBuilding;

        HideAllHighlighters();
        if (result == Building.VehicleAssignmentResult.Allowed)
        {
            SetHighlighterActive(validDropHighlighter, true);
        }
        else if (result == Building.VehicleAssignmentResult.InsufficientSpace)
        {
            SetHighlighterActive(fullSpaceHighlighter, true);
        }
        else
        {
            SetHighlighterActive(invalidDropHighlighter, true);
        }
    }

    private void EndDragInternal(ResourceTransportVehicleDragSource source)
    {
        if (activeSource != source) return;
        activeSource = null;
        HideAllHighlighters();
    }

    private Building GetBuilding()
    {
        return industrialPanel != null
            ? industrialPanel.CurrentBuilding
            : null;
    }

    private void HideAllHighlighters()
    {
        SetHighlighterActive(validDropHighlighter, false);
        SetHighlighterActive(fullSpaceHighlighter, false);
        SetHighlighterActive(invalidDropHighlighter, false);
    }

    private static void SetHighlighterActive(
        GameObject highlighter,
        bool active)
    {
        if (highlighter != null && highlighter.activeSelf != active)
        {
            highlighter.SetActive(active);
        }
    }
}
