using System;
using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public sealed class GameTimeManager : MonoBehaviour
{
    public const int HoursPerDay = 24;
    // Bound catch-up work equally for the calendar, coroutines and traffic.
    public const float MaximumFrameSeconds = 0.2f;

    private const float MinimumMinutesPerDay = 0.01f;
    private const float DefaultRealMinutesPerDay = 10f;

    public static GameTimeManager Instance { get; private set; }

    [Header("Day Length")]
    [SerializeField, Min(MinimumMinutesPerDay)]
    [Tooltip("Minutes for 24 in-game hours at x1 simulation speed.")]
    private float realMinutesPerDay = DefaultRealMinutesPerDay;

    [SerializeField]
    [Tooltip("When enabled, the entire simulation uses unscaled time, including while Time.timeScale is zero.")]
    private bool useUnscaledTime;

    [Header("Simulation Speed")]
    [SerializeField, Tooltip("Supported multipliers: 0 (paused), 0.5, 1, 2 and 4. Does not change Unity time scale.")]
    private float simulationSpeed = 1f;

    [Header("Starting Time")]
    [SerializeField, Min(1)]
    private int startingDay = 1;

    [SerializeField, Range(0, HoursPerDay - 1)]
    private int startingHour;

    public event Action<int> OnDayChanged;

    public event Action<int> OnHourChanged;
    public event Action<float> OnSimulationSpeedChanged;

    public float SimulationSpeed
    {
        get => SanitizeSimulationSpeed(simulationSpeed);
        set
        {
            float sanitized = SanitizeSimulationSpeed(value);
            if (simulationSpeed == sanitized) return;
            simulationSpeed = sanitized;
            if (simulationSpeed == 0f) _simulationDeltaTime = 0f;
            OnSimulationSpeedChanged?.Invoke(simulationSpeed);
        }
    }

    public static float SimulationDeltaTime => Instance != null
        ? Instance._simulationDeltaTime
        : Mathf.Min(Time.deltaTime, MaximumFrameSeconds);

    public static double SimulationTimeSeconds => Instance != null
        ? Instance._simulationTimeSeconds
        : Time.timeAsDouble;

    public void IncreaseSimulationSpeed() =>
        SimulationSpeed = SimulationSpeed == 0f ? 0.5f : Mathf.Min(4f, SimulationSpeed * 2f);
    public void DecreaseSimulationSpeed() =>
        SimulationSpeed = SimulationSpeed <= 0.5f ? 0f : SimulationSpeed * 0.5f;

    public int CurrentDay { get; private set; }

    public int CurrentHour { get; private set; }

    public float CurrentHourProgress => (float)_currentHourProgress;

    public float CurrentDayProgress =>
        (CurrentHour + CurrentHourProgress) / HoursPerDay;

    public float RealMinutesPerDay
    {
        get => realMinutesPerDay;
        set => realMinutesPerDay = SanitizeDayLength(value);
    }

    public bool UseUnscaledTime
    {
        get => useUnscaledTime;
        set => useUnscaledTime = value;
    }

    public float RealSecondsPerGameHour =>
        (RealMinutesPerDay * 60f) / HoursPerDay;

    private double _currentHourProgress;
    private float _simulationDeltaTime;
    private double _simulationTimeSeconds;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        simulationSpeed = SanitizeSimulationSpeed(simulationSpeed);
        CurrentDay = Mathf.Max(1, startingDay);
        CurrentHour = Mathf.Clamp(startingHour, 0, HoursPerDay - 1);
        _currentHourProgress = 0d;
    }

    private void Update()
    {
        AdvanceTime(Mathf.Min(
            useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime,
            MaximumFrameSeconds));
    }

    /// <summary>
    /// Advances the simulation by real-life seconds at the selected speed.
    /// Explicit advances are not frame-clamped; the runtime Update is bounded.
    /// Every crossed hour and day invokes its corresponding event.
    /// </summary>
    public void AdvanceTime(float realSeconds)
    {
        _simulationDeltaTime = 0f;
        if (realSeconds <= 0f ||
            float.IsNaN(realSeconds) ||
            float.IsInfinity(realSeconds))
        {
            return;
        }

        float simulationSeconds = realSeconds * SimulationSpeed;
        if (float.IsInfinity(simulationSeconds)) return;
        _simulationDeltaTime = simulationSeconds;
        _simulationTimeSeconds += simulationSeconds;
        _currentHourProgress += simulationSeconds / RealSecondsPerGameHour;

        while (_currentHourProgress >= 1d)
        {
            _currentHourProgress -= 1d;
            AdvanceOneHour();
        }
    }

    /// <summary>Gameplay delay whose remaining duration follows live speed changes.</summary>
    public static IEnumerator WaitForSimulationSeconds(float seconds)
    {
        if (seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds))
            yield break;

        double deadline = SimulationTimeSeconds + seconds;
        while (SimulationTimeSeconds < deadline)
            yield return null;
    }

    private static float SanitizeSimulationSpeed(float value) =>
        value == 0f || value == 0.5f || value == 1f || value == 2f || value == 4f ? value : 1f;

    private void AdvanceOneHour()
    {
        CurrentHour++;

        if (CurrentHour >= HoursPerDay)
        {
            CurrentHour = 0;
            CurrentDay++;
            OnDayChanged?.Invoke(CurrentDay);
        }

        OnHourChanged?.Invoke(CurrentHour);
    }

    private void OnValidate()
    {
        realMinutesPerDay = SanitizeDayLength(realMinutesPerDay);
        simulationSpeed = SanitizeSimulationSpeed(simulationSpeed);
        startingDay = Mathf.Max(1, startingDay);
        startingHour = Mathf.Clamp(startingHour, 0, HoursPerDay - 1);
    }

    private void OnDisable()
    {
        _simulationDeltaTime = 0f;
    }

    private static float SanitizeDayLength(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value)
            ? DefaultRealMinutesPerDay
            : Mathf.Max(MinimumMinutesPerDay, value);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
