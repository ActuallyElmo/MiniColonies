using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class FactoryShiftRowView : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private const float FixedRowHeight = 75f;
    private const float RowHeightWithoutResources = 63f;
    private const float ResourceRowHeight = 13f;

    [Header("Sizing")]
    [SerializeField] private LayoutElement layoutElement;

    [Header("Title Row")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private Button duplicateButton;
    [SerializeField] private Image duplicateIconImage;
    [SerializeField] private Toggle shiftActiveToggle;
    [SerializeField] private Button deleteButton;
    [SerializeField] private Color activeToggleColor =
        new Color32(73, 155, 86, 255);
    [SerializeField] private Color inactiveToggleColor =
        new Color32(170, 68, 64, 255);

    [Header("Start / End Row")]
    [SerializeField] private TMP_Text startHourText;
    [SerializeField] private TMP_Text endHourText;
    [SerializeField] private Button earlierStartButton;
    [SerializeField] private Button laterStartButton;

    [Header("Preparation Row")]
    [SerializeField] private TMP_Text preparationHourText;
    [SerializeField] private Button earlierPreparationButton;
    [SerializeField] private Button laterPreparationButton;

    [Header("Population Row")]
    [SerializeField] private TMP_Text desiredPopsText;
    [SerializeField] private Button decreasePopsButton;
    [SerializeField] private Button increasePopsButton;

    [Header("Resource Rows")]
    [SerializeField] private TMP_Text resourcesHeadingText;
    [SerializeField] private RectTransform resourcesRoot;
    [SerializeField] private LayoutElement resourcesLayoutElement;
    [SerializeField] private TMP_Text noResourcesText;
    [SerializeField] private FactoryResourceAmountRowView resourceRowTemplate;

    private FactoryShift _boundShift;
    private Action<FactoryShift, bool> _hoverChanged;
    private Image _backgroundImage;

    private void Awake()
    {
        _backgroundImage = GetComponent<Image>();
    }

    public void Bind(
        Building building,
        FactoryShift shift,
        int shiftIndex,
        Action<string> setStatus,
        Action<FactoryShift, bool> hoverChanged)
    {
        _boundShift = shift;
        _hoverChanged = hoverChanged;
        EnsureActiveToggle();
        EnsureDuplicateButton();
        if (titleText != null) titleText.text = $"Shift {shiftIndex + 1}";
        RefreshValues(building, shift);

        if (shiftActiveToggle != null)
        {
            shiftActiveToggle.SetIsOnWithoutNotify(shift.IsActive);
            UpdateActiveToggleVisual(shift.IsActive);
            shiftActiveToggle.onValueChanged.RemoveAllListeners();
            shiftActiveToggle.onValueChanged.AddListener(isActive =>
            {
                UpdateActiveToggleVisual(isActive);
                building.SetFactoryShiftActive(shift, isActive);
                setStatus?.Invoke(string.Empty);
            });
        }

        BindButton(deleteButton, () =>
        {
            building.RemoveFactoryShift(shift);
            setStatus?.Invoke(string.Empty);
        });
        BindButton(duplicateButton, () =>
        {
            bool duplicated = building.TryDuplicateFactoryShift(
                shift,
                out _);
            setStatus?.Invoke(
                duplicated
                    ? string.Empty
                    : "No free time remains for another shift.");
        });
        BindButton(earlierStartButton, () =>
            ChangeStart(building, shift, -1, setStatus));
        BindButton(laterStartButton, () =>
            ChangeStart(building, shift, 1, setStatus));
        BindButton(earlierPreparationButton, () =>
            building.SetFactoryShiftPreparationHours(
                shift,
                shift.PreparationHours - 1));
        BindButton(laterPreparationButton, () =>
            building.SetFactoryShiftPreparationHours(
                shift,
                shift.PreparationHours + 1));
        BindButton(decreasePopsButton, () =>
            ChangePops(building, shift, false));
        BindButton(increasePopsButton, () =>
            ChangePops(building, shift, true));

        RebuildResourceRows(building, shift);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_boundShift != null)
        {
            _hoverChanged?.Invoke(_boundShift, true);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_boundShift != null)
        {
            _hoverChanged?.Invoke(_boundShift, false);
        }
    }

    public void SetBackgroundColor(Color color)
    {
        if (_backgroundImage == null)
        {
            _backgroundImage = GetComponent<Image>();
        }

        if (_backgroundImage != null)
        {
            _backgroundImage.color = color;
        }
    }

    private void RebuildResourceRows(
        Building building,
        FactoryShift shift)
    {
        IReadOnlyList<ResourceAmount> resources = shift.DesiredResources;
        bool hasResources = HasInputResources(building);
        if (resourcesHeadingText == null)
        {
            resourcesHeadingText = transform
                .Find("ResourcesHeading")
                ?.GetComponent<TMP_Text>();
        }
        if (resourcesHeadingText != null)
        {
            resourcesHeadingText.gameObject.SetActive(hasResources);
        }
        if (resourcesRoot != null)
        {
            resourcesRoot.gameObject.SetActive(hasResources);
        }
        if (noResourcesText != null)
        {
            noResourcesText.gameObject.SetActive(false);
        }

        if (resourcesRoot == null || resourceRowTemplate == null)
        {
            UpdateRowHeight(hasResources, 0f);
            return;
        }

        for (int i = resourcesRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = resourcesRoot.GetChild(i);
            if (noResourcesText != null &&
                child.gameObject == noResourcesText.gameObject)
            {
                continue;
            }

            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        for (int i = 0; hasResources && i < resources.Count; i++)
        {
            ResourceType resourceType = resources[i].resourceType;
            FactoryResourceAmountRowView resourceRow = Instantiate(
                resourceRowTemplate,
                resourcesRoot,
                false);
            resourceRow.name = NicifyName(resourceType.ToString());

            LayoutElement resourceLayout =
                resourceRow.GetComponent<LayoutElement>();
            if (resourceLayout != null)
            {
                // The prefab template is ignored by the parent row layout.
                // Its runtime copies belong to ResourceRows, so they must take
                // part in that layout and contribute their compact height.
                resourceLayout.ignoreLayout = false;
                resourceLayout.minHeight = ResourceRowHeight;
                resourceLayout.preferredHeight = ResourceRowHeight;
                resourceLayout.flexibleHeight = 0f;
            }

            resourceRow.gameObject.SetActive(true);
            resourceRow.Bind(
                NicifyName(resourceType.ToString()),
                shift.GetDesiredResourceAmount(resourceType),
                building.GetFactoryShiftRequiredResourceAmount(
                    shift,
                    resourceType),
                () => ChangeResource(
                    building,
                    shift,
                    resourceType,
                    false),
                () => ChangeResource(
                    building,
                    shift,
                    resourceType,
                    true));
        }

        VerticalLayoutGroup resourcesLayout =
            resourcesRoot.GetComponent<VerticalLayoutGroup>();
        float resourceSpacing = resourcesLayout != null
            ? resourcesLayout.spacing
            : 0f;
        int displayedResourceCount = hasResources ? resources.Count : 0;
        float resourcesHeight =
            displayedResourceCount * ResourceRowHeight +
            Mathf.Max(0, displayedResourceCount - 1) * resourceSpacing;
        UpdateRowHeight(hasResources, resourcesHeight);

        LayoutRebuilder.ForceRebuildLayoutImmediate(resourcesRoot);
        if (transform is RectTransform rowRect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rowRect);
        }
    }

    private static bool HasInputResources(Building building)
    {
        if (!(building?.data is ResourceProductionBuildingData productionData) ||
            productionData.resources == null)
        {
            return false;
        }

        for (int i = 0; i < productionData.resources.Count; i++)
        {
            if (productionData.resources[i].direction ==
                ProductionResourceDirection.Input)
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateRowHeight(bool hasResources, float resourcesHeight)
    {
        float rowHeight = hasResources
            ? FixedRowHeight + resourcesHeight
            : RowHeightWithoutResources;
        if (layoutElement != null)
        {
            layoutElement.minHeight = rowHeight;
            layoutElement.preferredHeight = rowHeight;
            layoutElement.flexibleHeight = 0f;
        }
        if (resourcesLayoutElement != null)
        {
            resourcesLayoutElement.minHeight = resourcesHeight;
            resourcesLayoutElement.preferredHeight = resourcesHeight;
            resourcesLayoutElement.flexibleHeight = 0f;
        }

        if (transform is RectTransform rowRect)
        {
            // Keep the row correct under list layouts that do not control
            // their children's height.
            rowRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                rowHeight);
        }
    }

    private void RefreshValues(Building building, FactoryShift shift)
    {
        if (startHourText != null)
        {
            startHourText.text = FormatHour(shift.StartHour);
        }

        if (endHourText != null)
        {
            endHourText.text = FormatHour(
                shift.GetEndHour(building.FactoryShiftDurationHours));
        }

        if (preparationHourText != null)
        {
            preparationHourText.text = $"{shift.PreparationHours}h";
        }

        if (desiredPopsText != null)
        {
            desiredPopsText.text =
                $"{shift.DesiredPops}/{building.GetTargetPopCount()} " +
                $"• {building.GetFactoryShiftEfficiency(shift):P0}";
        }
    }

    private static void ChangePops(
        Building building,
        FactoryShift shift,
        bool increase)
    {
        building.SetFactoryShiftDesiredPops(
            shift,
            AdjustAmount(
                shift.DesiredPops,
                building.GetTargetPopCount(),
                increase));
    }

    private static void ChangeResource(
        Building building,
        FactoryShift shift,
        ResourceType resourceType,
        bool increase)
    {
        building.SetFactoryShiftDesiredResource(
            shift,
            resourceType,
            AdjustAmount(
                shift.GetDesiredResourceAmount(resourceType),
                building.GetFactoryShiftResourceCapacity(resourceType),
                increase));
    }

    private static int AdjustAmount(
        int currentAmount,
        int maximumAmount,
        bool increase)
    {
        int step = GetClickStep();
        long adjusted = increase
            ? (long)currentAmount + step
            : (long)currentAmount - step;
        return (int)Math.Max(0L, Math.Min(maximumAmount, adjusted));
    }

    private static int GetClickStep()
    {
        bool controlHeld =
            Input.GetKey(KeyCode.LeftControl) ||
            Input.GetKey(KeyCode.RightControl);
        if (controlHeld) return 100;

        bool shiftHeld =
            Input.GetKey(KeyCode.LeftShift) ||
            Input.GetKey(KeyCode.RightShift);
        return shiftHeld ? 10 : 1;
    }

    private static void ChangeStart(
        Building building,
        FactoryShift shift,
        int delta,
        Action<string> setStatus)
    {
        if (shift.IsActive)
        {
            setStatus?.Invoke("Active shifts cannot be moved.");
            return;
        }

        bool changed = building.TryMoveFactoryShiftChain(shift, delta);
        setStatus?.Invoke(
            changed
                ? string.Empty
                : "Only inactive shifts can be moved or pushed.");
    }

    private void EnsureActiveToggle()
    {
        if (shiftActiveToggle != null) return;

        Transform titleRow = titleText != null
            ? titleText.transform.parent
            : deleteButton != null
                ? deleteButton.transform.parent
                : null;
        if (titleRow == null) return;

        var toggleObject = new GameObject(
            "ShiftActiveToggle",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Toggle),
            typeof(LayoutElement));
        toggleObject.layer = gameObject.layer;
        toggleObject.transform.SetParent(titleRow, false);
        if (deleteButton != null)
        {
            toggleObject.transform.SetSiblingIndex(
                deleteButton.transform.GetSiblingIndex());
        }

        LayoutElement toggleLayout =
            toggleObject.GetComponent<LayoutElement>();
        toggleLayout.minWidth = 45f;
        toggleLayout.preferredWidth = 45f;
        toggleLayout.minHeight = 12f;
        toggleLayout.preferredHeight = 12f;
        toggleLayout.flexibleWidth = 0f;
        toggleLayout.flexibleHeight = 0f;

        Image toggleImage = toggleObject.GetComponent<Image>();
        toggleImage.sprite = null;
        shiftActiveToggle = toggleObject.GetComponent<Toggle>();
        shiftActiveToggle.targetGraphic = toggleImage;
        shiftActiveToggle.graphic = null;

        TMP_Text labelTemplate = deleteButton != null
            ? deleteButton.GetComponentInChildren<TMP_Text>(true)
            : titleText;
        if (labelTemplate == null) return;

        GameObject labelObject = Instantiate(
            labelTemplate.gameObject,
            toggleObject.transform,
            false);
        labelObject.name = "Label";
        TMP_Text label = labelObject.GetComponent<TMP_Text>();
        label.fontSize = 7f;
        label.alignment = TextAlignmentOptions.Center;
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
    }

    private void EnsureDuplicateButton()
    {
        if (duplicateButton != null) return;

        Transform titleRow = titleText != null
            ? titleText.transform.parent
            : shiftActiveToggle != null
                ? shiftActiveToggle.transform.parent
                : null;
        if (titleRow == null) return;

        var buttonObject = new GameObject(
            "DuplicateShift",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button),
            typeof(LayoutElement));
        buttonObject.layer = gameObject.layer;
        buttonObject.transform.SetParent(titleRow, false);
        if (shiftActiveToggle != null)
        {
            buttonObject.transform.SetSiblingIndex(
                shiftActiveToggle.transform.GetSiblingIndex());
        }

        LayoutElement buttonLayout =
            buttonObject.GetComponent<LayoutElement>();
        buttonLayout.minWidth = 12f;
        buttonLayout.preferredWidth = 12f;
        buttonLayout.minHeight = 12f;
        buttonLayout.preferredHeight = 12f;
        buttonLayout.flexibleWidth = 0f;
        buttonLayout.flexibleHeight = 0f;

        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = new Color32(91, 98, 112, 255);
        duplicateButton = buttonObject.GetComponent<Button>();
        duplicateButton.targetGraphic = buttonImage;

        var iconObject = new GameObject(
            "PlaceholderIcon",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        iconObject.layer = gameObject.layer;
        iconObject.transform.SetParent(buttonObject.transform, false);
        duplicateIconImage = iconObject.GetComponent<Image>();
        duplicateIconImage.sprite = null;
        duplicateIconImage.color = new Color32(174, 183, 197, 255);
        duplicateIconImage.preserveAspect = true;
        duplicateIconImage.raycastTarget = false;

        RectTransform iconRect = duplicateIconImage.rectTransform;
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = new Vector2(3f, 3f);
        iconRect.offsetMax = new Vector2(-3f, -3f);
    }

    private void UpdateActiveToggleVisual(bool isActive)
    {
        if (shiftActiveToggle == null) return;

        Image toggleImage = shiftActiveToggle.targetGraphic as Image;
        if (toggleImage != null)
        {
            toggleImage.color = isActive
                ? activeToggleColor
                : inactiveToggleColor;
        }

        TMP_Text label = shiftActiveToggle.transform
            .Find("Label")?.GetComponent<TMP_Text>();
        if (label != null)
        {
            label.text = isActive ? "Active" : "Inactive";
        }
    }

    private static void BindButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
    }

    private static string FormatHour(int hour)
    {
        return $"{FactoryShift.WrapHour(hour):00}:00";
    }

    private static string NicifyName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var result = new System.Text.StringBuilder(name.Length + 4);
        result.Append(name[0]);
        for (int i = 1; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && !char.IsWhiteSpace(name[i - 1]))
            {
                result.Append(' ');
            }

            result.Append(name[i]);
        }

        return result.ToString();
    }
}
