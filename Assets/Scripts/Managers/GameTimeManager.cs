using System;
using UnityEngine;

public sealed class GameTimeManager : MonoBehaviour
{
    public const int HoursPerDay = 24;

    private const float MinimumMinutesPerDay = 0.01f;
    private const float DefaultRealMinutesPerDay = 10f;

    public static GameTimeManager Instance { get; private set; }

    [Header("Day Length")]
    [SerializeField, Min(MinimumMinutesPerDay)]
    [Tooltip("How many real-life minutes it takes for 24 in-game hours to pass.")]
    private float realMinutesPerDay = DefaultRealMinutesPerDay;

    [SerializeField]
    [Tooltip("When enabled, game time continues while Time.timeScale is zero.")]
    private bool useUnscaledTime;

    [Header("Starting Time")]
    [SerializeField, Min(1)]
    private int startingDay = 1;

    [SerializeField, Range(0, HoursPerDay - 1)]
    private int startingHour;

    public event Action<int> OnDayChanged;

    public event Action<int> OnHourChanged;

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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        CurrentDay = Mathf.Max(1, startingDay);
        CurrentHour = Mathf.Clamp(startingHour, 0, HoursPerDay - 1);
        _currentHourProgress = 0d;
    }

    private void Update()
    {
        AdvanceTime(useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
    }

    /// <summary>
    /// Advances the clock by a number of real-life seconds.
    /// Every crossed hour and day invokes its corresponding event.
    /// </summary>
    public void AdvanceTime(float realSeconds)
    {
        if (realSeconds <= 0f ||
            float.IsNaN(realSeconds) ||
            float.IsInfinity(realSeconds))
        {
            return;
        }

        _currentHourProgress += realSeconds / RealSecondsPerGameHour;

        while (_currentHourProgress >= 1d)
        {
            _currentHourProgress -= 1d;
            AdvanceOneHour();
        }
    }

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
        startingDay = Mathf.Max(1, startingDay);
        startingHour = Mathf.Clamp(startingHour, 0, HoursPerDay - 1);
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
