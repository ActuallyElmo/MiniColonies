using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Receives vehicle-type drags for the housing building whose information
/// panel is currently open.
/// </summary>
[DisallowMultipleComponent]
public sealed class HousingVehicleDropTarget : MonoBehaviour, IDropHandler
{
    [SerializeField] private HousingBuildingInfoPanel housingPanel;
    [Header("Drag Feedback")]
    [Tooltip("Shown when the dragged vehicle can be added to the house.")]
    [SerializeField] private GameObject areaHighlighter;
    [Tooltip(
        "Shown when the vehicle is allowed, but the house has too little " +
        "free vehicle space. A labeled orange copy is created when omitted.")]
    [SerializeField] private GameObject notEnoughSpaceHighlighter;
    [Tooltip(
        "Shown when the house does not allow the dragged vehicle type. A " +
        "labeled red copy is created when omitted.")]
    [SerializeField] private GameObject wrongVehicleTypeHighlighter;

    private static HousingVehicleDropTarget activeTarget;
    private PopTransportVehicleDragSource activeDragSource;
    private Building.VehicleAssignmentResult displayedResult;
    private bool hasDisplayedResult;

    private void Awake()
    {
        if (housingPanel == null)
        {
            housingPanel = GetComponentInParent<HousingBuildingInfoPanel>(true);
        }

        ResolveFallbackHighlighters();
        HideAllHighlighters();
    }

    private void OnEnable()
    {
        activeTarget = this;
        HideAllHighlighters();
    }

    private void OnDisable()
    {
        if (activeTarget == this)
        {
            activeTarget = null;
        }

        activeDragSource = null;
        HideAllHighlighters();
    }

    public void OnDrop(PointerEventData eventData)
    {
        PopTransportVehicleDragSource source = eventData.pointerDrag != null
            ? eventData.pointerDrag.GetComponent<PopTransportVehicleDragSource>()
            : null;

        if (source == null ||
            source != activeDragSource)
        {
            return;
        }

        Building building = housingPanel.CurrentBuilding;
        if (GetAssignmentResult(source.VehicleType) ==
            Building.VehicleAssignmentResult.Allowed)
        {
            building.TryAssignVehicle(source.VehicleType, out _);
        }

        EndDragInternal(source);
    }

    public static bool TryBeginDrag(
        PopTransportVehicleDragSource source)
    {
        if (activeTarget == null ||
            source == null ||
            activeTarget.GetCurrentBuilding() == null)
        {
            return false;
        }

        activeTarget.activeDragSource = source;
        activeTarget.ShowAssignmentHighlight(source.VehicleType);
        return true;
    }

    public static void EndDrag(PopTransportVehicleDragSource source)
    {
        if (activeTarget != null)
        {
            activeTarget.EndDragInternal(source);
        }
    }

    public static void RefreshDragFeedback(
        PopTransportVehicleDragSource source)
    {
        if (activeTarget != null &&
            activeTarget.activeDragSource == source)
        {
            activeTarget.ShowAssignmentHighlight(source.VehicleType);
        }
    }

    private Building GetCurrentBuilding()
    {
        return housingPanel != null
            ? housingPanel.CurrentBuilding
            : null;
    }

    private Building.VehicleAssignmentResult GetAssignmentResult(
        PopTransportVehicleData vehicleType)
    {
        Building building = GetCurrentBuilding();
        return building != null
            ? building.GetVehicleAssignmentResult(vehicleType)
            : Building.VehicleAssignmentResult.UnsupportedBuilding;
    }

    private void EndDragInternal(PopTransportVehicleDragSource source)
    {
        if (activeDragSource != source)
        {
            return;
        }

        activeDragSource = null;
        HideAllHighlighters();
    }

    private void ShowAssignmentHighlight(
        PopTransportVehicleData vehicleType)
    {
        Building.VehicleAssignmentResult result =
            GetAssignmentResult(vehicleType);
        if (hasDisplayedResult && displayedResult == result)
        {
            return;
        }

        HideAllHighlighters();

        if (result == Building.VehicleAssignmentResult.Allowed)
        {
            SetHighlighterActive(areaHighlighter, true);
        }
        else if (result ==
                 Building.VehicleAssignmentResult.InsufficientSpace)
        {
            SetHighlighterActive(notEnoughSpaceHighlighter, true);
        }
        else
        {
            SetHighlighterActive(wrongVehicleTypeHighlighter, true);
        }

        displayedResult = result;
        hasDisplayedResult = true;
    }

    private void ResolveFallbackHighlighters()
    {
        if (areaHighlighter == null)
        {
            return;
        }

        if (notEnoughSpaceHighlighter == null)
        {
            notEnoughSpaceHighlighter = CreateFallbackHighlighter(
                "NotEnoughSpaceHighlighter",
                "Not enough vehicle space",
                new Color(1f, 0.55f, 0.15f, 1f));
        }

        if (wrongVehicleTypeHighlighter == null)
        {
            wrongVehicleTypeHighlighter = CreateFallbackHighlighter(
                "WrongVehicleTypeHighlighter",
                "Vehicle type not allowed",
                new Color(1f, 0.25f, 0.25f, 1f));
        }
    }

    private GameObject CreateFallbackHighlighter(
        string objectName,
        string message,
        Color tint)
    {
        GameObject highlighter = Instantiate(
            areaHighlighter,
            areaHighlighter.transform.parent);
        highlighter.name = objectName;

        TMP_Text label = highlighter.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = message;
            label.raycastTarget = false;
        }

        Image[] images = highlighter.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            images[i].color = tint;
            images[i].raycastTarget = false;
        }

        highlighter.SetActive(false);
        return highlighter;
    }

    private void HideAllHighlighters()
    {
        SetHighlighterActive(areaHighlighter, false);
        SetHighlighterActive(notEnoughSpaceHighlighter, false);
        SetHighlighterActive(wrongVehicleTypeHighlighter, false);
        hasDisplayedResult = false;
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
