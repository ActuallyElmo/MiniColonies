using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Shows descriptive text while the pointer is over this object.
/// World objects need a Collider; UI objects need a raycastable Graphic and
/// an EventSystem in the scene.
/// </summary>
[DisallowMultipleComponent]
public class TooltipInfo : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    [SerializeField, TextArea(2, 10)]
    [Tooltip("Text shown when the player hovers over this object. TMP rich-text tags are supported.")]
    private string tooltipText;

    private bool mouseIsOver;
    private bool pointerIsOver;

    public string TooltipText
    {
        get => tooltipText;
        set
        {
            tooltipText = value;
            RefreshVisibleTooltip();
        }
    }

    /// <summary>
    /// Updates the text at runtime. If this object is hovered, the visible
    /// tooltip updates immediately.
    /// </summary>
    public void SetText(string information)
    {
        TooltipText = information;
    }

    /// <summary>
    /// Shows this object's tooltip. This can also be wired to a UnityEvent.
    /// </summary>
    public void ShowTooltip()
    {
        if (string.IsNullOrWhiteSpace(tooltipText))
        {
            TooltipManager.Hide(this);
            return;
        }

        TooltipManager.Show(tooltipText, this);
    }

    /// <summary>
    /// Hides the tooltip if this object is still the active owner.
    /// </summary>
    public void HideTooltip()
    {
        TooltipManager.Hide(this);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerIsOver = true;
        ShowTooltip();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerIsOver = false;
        HideIfNoLongerHovered();
    }

    private void OnMouseEnter()
    {
        mouseIsOver = true;
        ShowTooltip();
    }

    private void OnMouseExit()
    {
        mouseIsOver = false;
        HideIfNoLongerHovered();
    }

    private void OnDisable()
    {
        mouseIsOver = false;
        pointerIsOver = false;
        HideTooltip();
    }

    private void OnValidate()
    {
        RefreshVisibleTooltip();
    }

    private void RefreshVisibleTooltip()
    {
        if (mouseIsOver || pointerIsOver)
        {
            ShowTooltip();
        }
    }

    private void HideIfNoLongerHovered()
    {
        if (!mouseIsOver && !pointerIsOver)
        {
            HideTooltip();
        }
    }
}
