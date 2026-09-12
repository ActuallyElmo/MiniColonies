using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Displays and edits a factory's delivery priority. The normal button click
/// raises priority, while a right click lowers it.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button), typeof(Image))]
public sealed class FactoryPriorityButton : MonoBehaviour,
    IPointerClickHandler
{
    public const int MinimumPriority = 1;
    public const int MaximumPriority = 9;

    [SerializeField] private Button button;
    [SerializeField] private Image background;
    [SerializeField] private TMP_Text valueText;

    private Building _building;
    private bool _buttonBound;

    public void Configure(
        Button priorityButton,
        Image priorityBackground,
        TMP_Text priorityText)
    {
        button = priorityButton;
        background = priorityBackground;
        valueText = priorityText;
        BindButton();
        Refresh();
    }

    public void Bind(Building building)
    {
        if (_building == building)
        {
            Refresh();
            return;
        }

        if (_building != null)
        {
            _building.FactoryPriorityChanged -= HandlePriorityChanged;
        }

        _building = building;
        if (_building != null)
        {
            _building.FactoryPriorityChanged += HandlePriorityChanged;
        }

        BindButton();
        Refresh();
    }

    public void IncreasePriority()
    {
        ChangePriority(1);
    }

    public void DecreasePriority()
    {
        ChangePriority(-1);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData != null &&
            eventData.button == PointerEventData.InputButton.Right)
        {
            DecreasePriority();
        }
    }

    public static Color GetPriorityColor(int priority)
    {
        int clampedPriority = Mathf.Clamp(
            priority,
            0,
            MaximumPriority);

        Color startColor = new Color(0.46f, 0.74f, 0.81f);
        Color midColor = new Color(0.78f, 0.66f, 0.14f);
        Color endColor = new Color(0.75f, 0.17f, 0.17f);

        if (clampedPriority <= 5)
        {
            return Color.Lerp(
                startColor,
                midColor,
                clampedPriority / 5f);
        }

        return Color.Lerp(
            midColor,
            endColor,
            (clampedPriority - 5) / 4f);
    }

    private void Awake()
    {
        CacheReferences();
        BindButton();
        Refresh();
    }

    private void OnDestroy()
    {
        if (_building != null)
        {
            _building.FactoryPriorityChanged -= HandlePriorityChanged;
        }

        if (_buttonBound && button != null)
        {
            button.onClick.RemoveListener(IncreasePriority);
        }
    }

    private void CacheReferences()
    {
        if (button == null) button = GetComponent<Button>();
        if (background == null) background = GetComponent<Image>();
        if (valueText == null)
        {
            valueText = GetComponentInChildren<TMP_Text>(true);
        }
    }

    private void BindButton()
    {
        CacheReferences();
        if (_buttonBound || button == null) return;

        button.onClick.AddListener(IncreasePriority);
        _buttonBound = true;
    }

    private void ChangePriority(int delta)
    {
        if (_building == null) return;

        _building.SetFactoryPriority(
            Mathf.Clamp(
                _building.FactoryPriority + delta,
                MinimumPriority,
                MaximumPriority));
        Refresh();
    }

    private void HandlePriorityChanged(Building building)
    {
        if (building == _building)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        CacheReferences();
        bool hasFactory = _building != null &&
                          _building.data is ResourceProductionBuildingData;
        int priority = hasFactory
            ? _building.FactoryPriority
            : 5;

        if (button != null) button.interactable = hasFactory;
        if (valueText != null) valueText.text = priority.ToString();
        if (background != null)
        {
            background.color = GetPriorityColor(priority);
        }
    }
}
