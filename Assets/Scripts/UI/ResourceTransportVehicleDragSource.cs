using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Makes a resource-vehicle type entry draggable into an industrial building's
/// owned-vehicles tab. Add this component to a vehicle catalogue UI entry and
/// assign the ResourceTransportVehicleData that entry represents.
/// </summary>
[AddComponentMenu("Mini Colonies/UI/Resource Transport Vehicle Drag Source")]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class ResourceTransportVehicleDragSource : MonoBehaviour,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    [Header("Industrial Vehicle Drag Source")]
    [Tooltip("The industrial vehicle type created when this UI entry is dropped on a factory's Owned Vehicles tab.")]
    [SerializeField] private ResourceTransportVehicleData vehicleType;

    [Tooltip("Optional. When omitted, the closest root Canvas is used.")]
    [SerializeField] private Canvas dragCanvas;

    private static ResourceTransportVehicleDragSource activeSource;
    private RectTransform dragPreview;
    private bool dragAccepted;

    public ResourceTransportVehicleData VehicleType => vehicleType;

    /// <summary>
    /// Lets a vehicle catalogue bind pooled/generated entries without relying
    /// on Inspector references.
    /// </summary>
    public void Configure(
        ResourceTransportVehicleData resourceVehicleType,
        Canvas canvas = null)
    {
        vehicleType = resourceVehicleType;
        dragCanvas = canvas;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!PlayerViewModeManager.AllowsGameplayActions) return;

        if (activeSource != null && activeSource != this)
        {
            activeSource.FinishDrag();
        }

        dragAccepted =
            eventData.button == PointerEventData.InputButton.Left &&
            vehicleType != null &&
            IndustrialVehicleDropTarget.TryBeginDrag(this);
        if (!dragAccepted)
        {
            return;
        }

        activeSource = this;
        CreatePreview();
        MovePreview(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragAccepted)
        {
            IndustrialVehicleDropTarget.RefreshDragFeedback(this);
            MovePreview(eventData);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        FinishDrag();
    }

    private void OnDisable()
    {
        FinishDrag();
    }

    private void CreatePreview()
    {
        Canvas canvas = ResolveCanvas();
        if (canvas == null) return;

        GameObject preview = Instantiate(gameObject, canvas.transform);
        preview.name = $"{name} Drag Preview";
        preview.transform.SetAsLastSibling();

        Canvas previewCanvas = preview.GetComponent<Canvas>();
        if (previewCanvas == null)
        {
            previewCanvas = preview.AddComponent<Canvas>();
        }

        previewCanvas.overrideSorting = true;
        previewCanvas.sortingOrder = short.MaxValue;

        ResourceTransportVehicleDragSource clonedSource =
            preview.GetComponent<ResourceTransportVehicleDragSource>();
        if (clonedSource != null)
        {
            clonedSource.enabled = false;
        }

        Selectable[] selectables =
            preview.GetComponentsInChildren<Selectable>(true);
        for (int i = 0; i < selectables.Length; i++)
        {
            selectables[i].interactable = false;
        }

        CanvasGroup group = preview.GetComponent<CanvasGroup>();
        if (group == null) group = preview.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.ignoreParentGroups = true;
        group.alpha = 0.85f;
        dragPreview = preview.GetComponent<RectTransform>();
    }

    private void MovePreview(PointerEventData eventData)
    {
        Canvas canvas = ResolveCanvas();
        RectTransform canvasRect = canvas != null
            ? canvas.transform as RectTransform
            : null;
        if (dragPreview == null || canvasRect == null) return;

        Camera eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : canvas.worldCamera;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                canvasRect,
                eventData.position,
                eventCamera,
                out Vector3 worldPosition))
        {
            dragPreview.position = worldPosition;
        }
    }

    private Canvas ResolveCanvas()
    {
        if (dragCanvas == null)
        {
            Canvas nearest = GetComponentInParent<Canvas>();
            dragCanvas = nearest != null ? nearest.rootCanvas : null;
        }

        return dragCanvas;
    }

    private void FinishDrag()
    {
        if (dragAccepted)
        {
            IndustrialVehicleDropTarget.EndDrag(this);
        }

        dragAccepted = false;
        if (activeSource == this) activeSource = null;
        if (dragPreview != null)
        {
            Destroy(dragPreview.gameObject);
            dragPreview = null;
        }
    }

    /// <summary>
    /// Cancels the active resource-vehicle drag without closing or changing
    /// the selected building.
    /// </summary>
    public static bool TryCancelActiveDrag()
    {
        if (activeSource == null || !activeSource.dragAccepted)
        {
            return false;
        }

        activeSource.FinishDrag();
        return true;
    }
}
