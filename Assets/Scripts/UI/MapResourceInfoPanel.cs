using System.Globalization;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The active root owns bindings; the authored card is hidden between selections.
// Presentation and dragging use UI events, independent of simulation pause/speed.
[DisallowMultipleComponent]
public sealed class MapResourceInfoPanel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private PlayerActionManager playerActions;
    [SerializeField] private RectTransform card;
    [SerializeField] private TMP_Text resourceName;
    [SerializeField] private TMP_Text sourceKind;
    [SerializeField] private TMP_Text resourceTypes;
    [SerializeField] private TMP_Text quantity;
    [SerializeField] private TMP_Text quality;
    [SerializeField] private TMP_Text description;
    [SerializeField] private ResourceCatalog resourceCatalog;
    // Retained for older authored cards; the Editor migration removes the bar.
    [SerializeField, HideInInspector] private Image remainingFill;
    [SerializeField] private Button closeButton;
    [SerializeField, HideInInspector] private int authoredLayoutVersion;

    private PlayerActionManager subscribedActions;
    private MapResourceSource source;
    private bool dragging;
    private Vector2 dragOffset;
    public MapResourceSource CurrentSource => source;
    public bool IsVisible => card != null && card.gameObject.activeSelf;

    private void OnEnable() => Bind();
    private void Start() => Bind();

    private void Bind()
    {
        if (remainingFill != null) remainingFill.gameObject.SetActive(false);
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Close);
            closeButton.onClick.AddListener(Close);
        }
        var actions = playerActions != null ? playerActions : PlayerActionManager.Instance;
        if (subscribedActions != actions)
        {
            if (subscribedActions != null) subscribedActions.ResourceSelectionChanged -= Show;
            subscribedActions = actions;
            if (subscribedActions != null) subscribedActions.ResourceSelectionChanged += Show;
        }
        Show(subscribedActions != null ? subscribedActions.SelectedResource : null);
    }

    private void OnDisable()
    {
        if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        if (subscribedActions != null) subscribedActions.ResourceSelectionChanged -= Show;
        subscribedActions = null;
        Close();
    }

    public void Show(MapResourceSource selected)
    {
        bool reopening = !IsVisible || !ReferenceEquals(source, selected);
        if (source != null) source.QuantityChanged -= Refresh;
        source = selected;
        dragging = false;
        if (card != null) card.gameObject.SetActive(source != null && source.Definition != null);
        if (!IsVisible) return;
        if (reopening) card.anchoredPosition = new Vector2(-20f, 0f);
        source.QuantityChanged += Refresh;
        Refresh();
    }

    // Closing presentation leaves selection with its owner, as building panels do.
    public void Close() => Show(null);

    private void Refresh()
    {
        if (source == null || source.Definition == null) return;
        if (resourceName != null) resourceName.text = string.IsNullOrWhiteSpace(source.Definition.displayName)
            ? source.Definition.resourceType.ToString() : source.Definition.displayName;
        if (sourceKind != null) sourceKind.text = source.Definition.shape == MapResourceShape.Nodes
            ? "Node" : "Deposit";
        ResourceCatalog catalog = resourceCatalog != null ? resourceCatalog : ResourceCatalog.Current;
        if (resourceTypes != null) resourceTypes.text = FormatTypes(source.Definition.resourceType, catalog);
        if (quantity != null) quantity.text = FormatQuantity(source);
        if (quality != null) quality.text = "—";
        if (description != null) description.text = FormatDescription(source.Definition.resourceType, catalog);
    }

    public static string FormatTypes(ResourceType resource, ResourceCatalog catalog)
    {
        var definition = catalog != null ? catalog.Find(resource) : null;
        if (definition?.types == null) return "Unclassified";
        var names = new StringBuilder();
        var seen = new HashSet<string>();
        foreach (string id in definition.types)
        {
            if (!seen.Add(id)) continue;
            ResourceCategory category = catalog.FindType(id);
            if (category == null) continue;
            if (names.Length > 0) names.Append(", ");
            names.Append(string.IsNullOrWhiteSpace(category.displayName) ? category.id : category.displayName.Trim());
        }
        return names.Length > 0 ? names.ToString() : "Unclassified";
    }

    public static string FormatDescription(ResourceType resource, ResourceCatalog catalog)
    {
        string text = catalog != null ? catalog.Find(resource)?.description : null;
        return string.IsNullOrWhiteSpace(text) ? "No description available." : text.Trim();
    }

    public static string FormatQuantity(MapResourceSource source)
    {
        if (source == null) return string.Empty;
        if (source.Unlimited) return "Unlimited";
        return source.RemainingUnits.ToString("N0", CultureInfo.InvariantCulture) + " / " +
            source.GeneratedUnits.ToString("N0", CultureInfo.InvariantCulture);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        dragging = false;
        if (!IsVisible || eventData == null || eventData.button != PointerEventData.InputButton.Left ||
            eventData.pointerPressRaycast.gameObject != card.gameObject) return;
        if (!TryPointer(eventData, out var pointer)) return;
        dragOffset = (Vector2)card.localPosition - pointer;
        dragging = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging || !TryPointer(eventData, out var pointer)) return;
        var parent = card.parent as RectTransform;
        if (parent == null) return;
        Vector2 position = pointer + dragOffset;
        // Rects are in the same Canvas units, including a scaled Canvas.
        Rect bounds = parent.rect;
        Rect panel = card.rect;
        position.x = Mathf.Clamp(position.x, bounds.xMin - panel.xMin, Mathf.Max(bounds.xMin - panel.xMin, bounds.xMax - panel.xMax));
        position.y = Mathf.Clamp(position.y, bounds.yMin - panel.yMin, Mathf.Max(bounds.yMin - panel.yMin, bounds.yMax - panel.yMax));
        card.localPosition = new Vector3(position.x, position.y, card.localPosition.z);
    }

    private bool TryPointer(PointerEventData eventData, out Vector2 pointer)
    {
        pointer = default;
        return card != null && eventData != null && card.parent is RectTransform parent &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position,
                eventData.pressEventCamera, out pointer);
    }

    public void OnEndDrag(PointerEventData eventData) => dragging = false;
}
