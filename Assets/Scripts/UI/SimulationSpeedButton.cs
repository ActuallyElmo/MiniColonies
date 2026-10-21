using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Displays simulation state on an authored button without owning a second speed value.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class SimulationSpeedButton : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private GameTimeManager gameTimeManager;
    [SerializeField] private TMP_Text speedLabel;

    private Button button;
    private GameTimeManager boundManager;

    private void OnEnable()
    {
        button = GetComponent<Button>();
        // Button.onClick covers left mouse clicks and keyboard/controller submit.
        button.onClick.AddListener(LowerSpeed);
        BindManager();
    }

    private void Start()
    {
        // The fallback singleton may not have completed Awake during OnEnable.
        if (boundManager == null) BindManager();
    }

    private void OnDisable()
    {
        if (button != null) button.onClick.RemoveListener(LowerSpeed);
        if (boundManager != null)
            boundManager.OnSimulationSpeedChanged -= RefreshLabel;
        boundManager = null;
    }

    private void BindManager()
    {
        if (boundManager != null)
            boundManager.OnSimulationSpeedChanged -= RefreshLabel;
        boundManager = gameTimeManager != null ? gameTimeManager : GameTimeManager.Instance;
        if (boundManager != null)
            boundManager.OnSimulationSpeedChanged += RefreshLabel;
        RefreshLabel(boundManager != null ? boundManager.SimulationSpeed : 1f);
    }

    private void LowerSpeed()
    {
        if (isActiveAndEnabled && button.IsInteractable() && boundManager != null)
            boundManager.DecreaseSimulationSpeed();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null || !isActiveAndEnabled || button == null || !button.IsActive() || !button.IsInteractable() ||
            boundManager == null || eventData.button != PointerEventData.InputButton.Right)
            return;

        boundManager.IncreaseSimulationSpeed();
        eventData.Use();
    }

    private void RefreshLabel(float speed)
    {
        if (speedLabel == null) return;
        string multiplier = speed == 0.5f ? "x0.5" : speed == 2f ? "x2" : speed == 4f ? "x4" : "x1";
        speedLabel.text = speed == 0f ? "Simulation Paused" : "Simulation " + multiplier;
    }
}
