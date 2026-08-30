using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Makes a vehicle-type UI button draggable into the currently open housing
/// panel. Duplicate the button and assign another vehicle type to add more
/// choices later.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class PopTransportVehicleDragSource : MonoBehaviour,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    private static PopTransportVehicleDragSource activeDragSource;

    [SerializeField] private PopTransportVehicleData vehicleType;
    [Tooltip("Optional. When omitted, the closest root Canvas is used.")]
    [SerializeField] private Canvas dragCanvas;

    private RectTransform dragPreview;
    private bool dragAccepted;

    public PopTransportVehicleData VehicleType => vehicleType;

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (activeDragSource != null && activeDragSource != this)
        {
            activeDragSource.FinishDrag();
        }

        dragAccepted =
            eventData.button == PointerEventData.InputButton.Left &&
            HousingVehicleDropTarget.TryBeginDrag(this);

        if (!dragAccepted)
        {
            return;
        }

        activeDragSource = this;
        CreateDragPreview();
        MoveDragPreview(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragAccepted)
        {
            HousingVehicleDropTarget.RefreshDragFeedback(this);
            MoveDragPreview(eventData);
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

    private void CreateDragPreview()
    {
        Canvas canvas = ResolveDragCanvas();
        if (canvas == null)
        {
            return;
        }

        GameObject previewObject = Instantiate(gameObject, canvas.transform);
        previewObject.name = $"{name} Drag Preview";
        previewObject.transform.SetAsLastSibling();

        Canvas previewCanvas = previewObject.GetComponent<Canvas>();
        if (previewCanvas == null)
        {
            previewCanvas = previewObject.AddComponent<Canvas>();
        }

        previewCanvas.overrideSorting = true;
        previewCanvas.sortingOrder = short.MaxValue;

        PopTransportVehicleDragSource clonedSource =
            previewObject.GetComponent<PopTransportVehicleDragSource>();
        if (clonedSource != null)
        {
            clonedSource.enabled = false;
        }

        Selectable[] selectables =
            previewObject.GetComponentsInChildren<Selectable>(true);
        for (int i = 0; i < selectables.Length; i++)
        {
            selectables[i].interactable = false;
        }

        CanvasGroup canvasGroup =
            previewObject.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = previewObject.AddComponent<CanvasGroup>();
        }

        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        canvasGroup.ignoreParentGroups = true;
        canvasGroup.alpha = 0.85f;

        dragPreview = previewObject.GetComponent<RectTransform>();
    }

    private void MoveDragPreview(PointerEventData eventData)
    {
        if (dragPreview == null)
        {
            return;
        }

        Canvas canvas = ResolveDragCanvas();
        RectTransform canvasRect = canvas != null
            ? canvas.transform as RectTransform
            : null;
        if (canvasRect == null)
        {
            return;
        }

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

    private Canvas ResolveDragCanvas()
    {
        if (dragCanvas == null)
        {
            Canvas nearestCanvas = GetComponentInParent<Canvas>();
            dragCanvas = nearestCanvas != null
                ? nearestCanvas.rootCanvas
                : null;
        }

        return dragCanvas;
    }

    private void FinishDrag()
    {
        if (dragAccepted)
        {
            HousingVehicleDropTarget.EndDrag(this);
        }

        dragAccepted = false;
        if (activeDragSource == this)
        {
            activeDragSource = null;
        }

        if (dragPreview != null)
        {
            Destroy(dragPreview.gameObject);
            dragPreview = null;
        }
    }

    /// <summary>
    /// Cancels the current vehicle drag, if one exists. Callers can use the
    /// return value to consume their cancel input before applying another
    /// right-click action.
    /// </summary>
    public static bool TryCancelActiveDrag()
    {
        if (activeDragSource == null || !activeDragSource.dragAccepted)
        {
            return false;
        }

        activeDragSource.FinishDrag();
        return true;
    }
}
