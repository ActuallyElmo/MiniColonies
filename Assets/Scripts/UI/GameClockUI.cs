using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>Binds an authored clock and direct speed controls to simulation state.</summary>
[DisallowMultipleComponent]
public sealed class GameClockUI : MonoBehaviour
{
    private static readonly float[] Speeds = { 0f, 0.5f, 1f, 2f, 4f };

    [SerializeField] private GameTimeManager gameTimeManager;
    [SerializeField] private TMP_Text hourLabel;
    [SerializeField] private TMP_Text dayLabel;
    [SerializeField, Tooltip("Order: Pause, x0.5, x1, x2, x4.")]
    private Button[] speedButtons = new Button[5];
    [SerializeField, Tooltip("Selected indicators in the same order as the buttons.")]
    private GameObject[] selectedIndicators = new GameObject[5];

    private GameTimeManager boundManager;
    private UnityAction[] clickActions;

    private void OnEnable()
    {
        if (clickActions == null)
        {
            clickActions = new UnityAction[Speeds.Length];
            for (int i = 0; i < Speeds.Length; i++)
            {
                int index = i;
                clickActions[i] = () => SelectSpeed(index);
            }
        }

        for (int i = 0; i < Speeds.Length; i++)
        {
            Button button = GetButton(i);
            if (button == null) continue;
            button.onClick.RemoveListener(clickActions[i]);
            button.onClick.AddListener(clickActions[i]);
        }
        BindManager();
    }

    private void Start()
    {
        if (boundManager == null) BindManager();
    }

    private void OnDisable()
    {
        if (clickActions != null)
            for (int i = 0; i < Speeds.Length; i++)
                GetButton(i)?.onClick.RemoveListener(clickActions[i]);
        UnbindManager();
    }

    private Button GetButton(int index) =>
        speedButtons != null && index < speedButtons.Length ? speedButtons[index] : null;

    private void BindManager()
    {
        UnbindManager();
        boundManager = gameTimeManager != null ? gameTimeManager : GameTimeManager.Instance;
        if (boundManager != null)
        {
            boundManager.OnHourChanged += RefreshHour;
            boundManager.OnDayChanged += RefreshDay;
            boundManager.OnSimulationSpeedChanged += RefreshSpeed;
        }
        RefreshHour(boundManager != null ? boundManager.CurrentHour : 0);
        RefreshDay(boundManager != null ? boundManager.CurrentDay : 1);
        RefreshSpeed(boundManager != null ? boundManager.SimulationSpeed : -1f);
    }

    private void UnbindManager()
    {
        if (boundManager != null)
        {
            boundManager.OnHourChanged -= RefreshHour;
            boundManager.OnDayChanged -= RefreshDay;
            boundManager.OnSimulationSpeedChanged -= RefreshSpeed;
        }
        boundManager = null;
    }

    public void SelectSpeed(int index)
    {
        if (!isActiveAndEnabled || boundManager == null || index < 0 || index >= Speeds.Length)
            return;
        Button button = GetButton(index);
        if (button != null && button.IsActive() && button.IsInteractable())
            boundManager.SimulationSpeed = Speeds[index];
    }

    public static string FormatHour(int hour)
    {
        hour = Mathf.Clamp(hour, 0, 23);
        return hour.ToString("D2") + ":00";
    }

    private void RefreshHour(int hour)
    {
        if (hourLabel != null) hourLabel.text = FormatHour(hour);
    }

    private void RefreshDay(int day)
    {
        if (dayLabel != null) dayLabel.text = "Day " + Mathf.Max(1, day);
    }

    private void RefreshSpeed(float speed)
    {
        if (selectedIndicators == null) return;
        for (int i = 0; i < selectedIndicators.Length; i++)
            if (selectedIndicators[i] != null)
                selectedIndicators[i].SetActive(i < Speeds.Length && speed == Speeds[i]);
    }
}
