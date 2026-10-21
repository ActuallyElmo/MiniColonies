using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Compact read-only resource row used by the factory status tab.</summary>
public sealed class FactoryStatusResourceRowView : MonoBehaviour
{
    [SerializeField] private Image resourceIcon;
    [SerializeField] private TMP_Text resourceNameText;
    [SerializeField] private TMP_Text amountText;

    [Header("Header Style")]
    [SerializeField] private Color headerBackgroundColor =
        new Color32(61, 85, 122, 255);
    [SerializeField] private Color headerTextColor = Color.white;

    public void Bind(string resourceName, string amount)
    {
        Bind(resourceName, amount, ResourceCatalog.Current != null ? ResourceCatalog.Current.fallbackIcon : null);
    }

    public void Bind(ResourceType resource, string amount) =>
        Bind(ResourceCatalog.GetDisplayName(resource), amount, ResourceCatalog.GetIcon(resource));

    public void Bind(string resourceName, string amount, Sprite icon)
    {
        if (resourceNameText != null)
        {
            resourceNameText.gameObject.SetActive(true);
            resourceNameText.text = resourceName;
            resourceNameText.fontStyle = FontStyles.Normal;
        }
        if (amountText != null)
        {
            amountText.gameObject.SetActive(true);
            amountText.text = amount;
        }
        if (resourceIcon != null)
        {
            resourceIcon.gameObject.SetActive(icon != null);
            resourceIcon.sprite = icon;
            resourceIcon.color = Color.white;
            resourceIcon.preserveAspect = true;
        }
    }

    public void BindHeader(string title)
    {
        if (resourceNameText != null)
        {
            resourceNameText.gameObject.SetActive(true);
            resourceNameText.text = title;
            resourceNameText.fontStyle = FontStyles.Bold;
            resourceNameText.color = headerTextColor;
        }
        if (resourceIcon != null) resourceIcon.gameObject.SetActive(false);
        if (amountText != null) amountText.gameObject.SetActive(false);

        Image background = GetComponent<Image>();
        if (background != null) background.color = headerBackgroundColor;
    }
}
