using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A screen-corner tooltip that can be used without keeping a scene reference.
/// Call <see cref="Show(string)"/> and <see cref="Hide"/> from any script.
/// </summary>
[DisallowMultipleComponent]
public sealed class TooltipManager : MonoBehaviour
{
    public enum ScreenCorner
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    private const string AutomaticallyCreatedObjectName = "[Tooltip Manager]";

    private static TooltipManager instance;

    [Header("Position")]
    [SerializeField] private ScreenCorner corner = ScreenCorner.TopLeft;
    [SerializeField] private Vector2 screenOffset = new Vector2(24f, 24f);

    [Header("Size")]
    [SerializeField, Min(1f)]
    [Tooltip("Maximum total width of the tooltip, including its padding.")]
    private float maximumWidth = 420f;
    [SerializeField] private Vector2 padding = new Vector2(16f, 12f);

    [Header("Appearance")]
    [SerializeField]
    [Tooltip("Optional TMP font used by the tooltip. The default TMP font is used when left empty.")]
    private TMP_FontAsset font;
    [SerializeField, Min(1f)] private float fontSize = 22f;
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private Color backgroundColor =
        new Color(0.035f, 0.045f, 0.06f, 0.94f);

    private RectTransform panelRect;
    private Image background;
    private TextMeshProUGUI tooltipText;
    private TMP_FontAsset defaultFont;
    private Object activeOwner;
    private bool hasActiveOwner;

    public static bool IsVisible =>
        instance != null &&
        instance.panelRect != null &&
        instance.panelRect.gameObject.activeSelf;

    public static string CurrentText =>
        IsVisible ? instance.tooltipText.text : string.Empty;

    /// <summary>
    /// Shows or updates a general-purpose tooltip.
    /// </summary>
    public static void Show(string information)
    {
        if (!PlayerViewModeManager.AllowsGameplayActions) return;
        GetOrCreate().ShowInternal(information, null, false);
    }

    /// <summary>
    /// Shows or updates a tooltip owned by a specific caller. Ownership prevents
    /// an older caller from hiding a newer caller's tooltip.
    /// </summary>
    public static void Show(string information, Object owner)
    {
        if (!PlayerViewModeManager.AllowsGameplayActions) return;
        GetOrCreate().ShowInternal(information, owner, owner != null);
    }

    /// <summary>
    /// Hides the currently visible tooltip, regardless of who showed it.
    /// </summary>
    public static void Hide()
    {
        if (instance != null)
        {
            instance.HideInternal();
        }
    }

    /// <summary>
    /// Hides the tooltip only if it is still owned by this caller.
    /// </summary>
    public static void Hide(Object owner)
    {
        if (instance != null &&
            instance.hasActiveOwner &&
            instance.activeOwner == owner)
        {
            instance.HideInternal();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
    }

    private static TooltipManager GetOrCreate()
    {
        if (instance != null)
        {
            return instance;
        }

        instance = FindFirstObjectByType<TooltipManager>(
            FindObjectsInactive.Include);

        if (instance != null)
        {
            instance.BuildInterface();
            return instance;
        }

        GameObject managerObject =
            new GameObject(AutomaticallyCreatedObjectName);
        instance = managerObject.AddComponent<TooltipManager>();
        DontDestroyOnLoad(managerObject);
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        BuildInterface();
        HideInternal();
    }

    private void LateUpdate()
    {
        if (hasActiveOwner && activeOwner == null)
        {
            HideInternal();
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void OnValidate()
    {
        maximumWidth = Mathf.Max(1f, maximumWidth);
        padding.x = Mathf.Max(0f, padding.x);
        padding.y = Mathf.Max(0f, padding.y);
        screenOffset.x = Mathf.Max(0f, screenOffset.x);
        screenOffset.y = Mathf.Max(0f, screenOffset.y);

        if (panelRect == null)
        {
            return;
        }

        ApplyAppearance();
        ApplyCorner();

        if (panelRect.gameObject.activeSelf)
        {
            ResizeToFitText();
        }
    }

    private void BuildInterface()
    {
        if (panelRect != null && tooltipText != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject(
            "Tooltip Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panelObject = new GameObject(
            "Tooltip Background",
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(Image));
        panelObject.transform.SetParent(canvasObject.transform, false);

        CanvasGroup canvasGroup = panelObject.GetComponent<CanvasGroup>();
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        panelRect = panelObject.GetComponent<RectTransform>();
        background = panelObject.GetComponent<Image>();
        background.raycastTarget = false;

        GameObject textObject = new GameObject(
            "Tooltip Text",
            typeof(RectTransform),
            typeof(TextMeshProUGUI));
        textObject.transform.SetParent(panelObject.transform, false);

        tooltipText = textObject.GetComponent<TextMeshProUGUI>();
        defaultFont = tooltipText.font;
        tooltipText.alignment = TextAlignmentOptions.TopLeft;
        tooltipText.textWrappingMode = TextWrappingModes.Normal;
        tooltipText.overflowMode = TextOverflowModes.Overflow;
        tooltipText.richText = true;
        tooltipText.raycastTarget = false;

        RectTransform textRect = tooltipText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = padding;
        textRect.offsetMax = -padding;

        ApplyAppearance();
        ApplyCorner();
    }

    private void ApplyAppearance()
    {
        if (background != null)
        {
            background.color = backgroundColor;
        }

        if (tooltipText != null)
        {
            tooltipText.font = font != null ? font : defaultFont;
            tooltipText.fontSize = fontSize;
            tooltipText.color = textColor;

            RectTransform textRect = tooltipText.rectTransform;
            textRect.offsetMin = padding;
            textRect.offsetMax = -padding;
        }
    }

    private void ApplyCorner()
    {
        if (panelRect == null)
        {
            return;
        }

        Vector2 anchor;
        Vector2 direction;

        switch (corner)
        {
            case ScreenCorner.TopRight:
                anchor = new Vector2(1f, 1f);
                direction = new Vector2(-1f, -1f);
                break;
            case ScreenCorner.BottomLeft:
                anchor = Vector2.zero;
                direction = new Vector2(1f, 1f);
                break;
            case ScreenCorner.BottomRight:
                anchor = new Vector2(1f, 0f);
                direction = new Vector2(-1f, 1f);
                break;
            default:
                anchor = new Vector2(0f, 1f);
                direction = new Vector2(1f, -1f);
                break;
        }

        panelRect.anchorMin = anchor;
        panelRect.anchorMax = anchor;
        panelRect.pivot = anchor;
        panelRect.anchoredPosition =
            Vector2.Scale(screenOffset, direction);
    }

    private void ShowInternal(
        string information,
        Object owner,
        bool isOwned)
    {
        BuildInterface();

        if (string.IsNullOrWhiteSpace(information))
        {
            if (!isOwned || !hasActiveOwner || activeOwner == owner)
            {
                HideInternal();
            }

            return;
        }

        activeOwner = owner;
        hasActiveOwner = isOwned;
        tooltipText.text = information;
        panelRect.gameObject.SetActive(true);
        ResizeToFitText();
    }

    private void ResizeToFitText()
    {
        float maximumTextWidth =
            Mathf.Max(1f, maximumWidth - padding.x * 2f);
        Vector2 preferredSize = tooltipText.GetPreferredValues(
            tooltipText.text,
            maximumTextWidth,
            0f);

        float textWidth = Mathf.Min(
            maximumTextWidth,
            Mathf.Ceil(preferredSize.x));
        float textHeight = Mathf.Ceil(preferredSize.y);

        panelRect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            textWidth + padding.x * 2f);
        panelRect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            textHeight + padding.y * 2f);

        tooltipText.ForceMeshUpdate();
    }

    private void HideInternal()
    {
        activeOwner = null;
        hasActiveOwner = false;

        if (panelRect != null)
        {
            panelRect.gameObject.SetActive(false);
        }
    }
}
