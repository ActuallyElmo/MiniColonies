using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class GameClockUITests
{
    private GameObject clockObject;
    private GameObject uiObject;
    private GameTimeManager clock;
    private GameClockUI ui;
    private TMP_Text label;
    private TMP_Text dayLabel;
    private Button[] buttons;
    private GameObject[] indicators;

    [SetUp]
    public void SetUp()
    {
        clockObject = new GameObject("HUD clock test");
        clock = clockObject.AddComponent<GameTimeManager>();
        Invoke(clock, "Awake");
        uiObject = new GameObject("Clock HUD test", typeof(RectTransform));
        uiObject.SetActive(false);
        ui = uiObject.AddComponent<GameClockUI>();
        var labelObject = new GameObject("Hour", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(uiObject.transform);
        label = labelObject.GetComponent<TMP_Text>();
        var dayObject = new GameObject("Day", typeof(RectTransform), typeof(TextMeshProUGUI));
        dayObject.transform.SetParent(uiObject.transform);
        dayLabel = dayObject.GetComponent<TMP_Text>();
        buttons = new Button[5];
        indicators = new GameObject[5];
        for (int i = 0; i < 5; i++)
        {
            var buttonObject = new GameObject("Speed" + i, typeof(RectTransform), typeof(Button));
            buttonObject.transform.SetParent(uiObject.transform);
            buttons[i] = buttonObject.GetComponent<Button>();
            indicators[i] = new GameObject("Selected");
            indicators[i].transform.SetParent(buttonObject.transform);
        }
        SetField("gameTimeManager", clock);
        SetField("hourLabel", label);
        SetField("dayLabel", dayLabel);
        SetField("speedButtons", buttons);
        SetField("selectedIndicators", indicators);
        uiObject.SetActive(true);
        Invoke(ui, "OnEnable");
    }

    [TearDown]
    public void TearDown()
    {
        Invoke(ui, "OnDisable");
        Object.DestroyImmediate(uiObject);
        Invoke(clock, "OnDestroy");
        Object.DestroyImmediate(clockObject);
    }

    [TestCase(-1, "00:00")]
    [TestCase(0, "00:00")]
    [TestCase(1, "01:00")]
    [TestCase(11, "11:00")]
    [TestCase(12, "12:00")]
    [TestCase(13, "13:00")]
    [TestCase(23, "23:00")]
    [TestCase(24, "23:00")]
    public void FormatsTwentyFourHourClock(int hour, string expected) =>
        Assert.AreEqual(expected, GameClockUI.FormatHour(hour));

    [TestCase(0, 0f)]
    [TestCase(1, 0.5f)]
    [TestCase(2, 1f)]
    [TestCase(3, 2f)]
    [TestCase(4, 4f)]
    public void EachButtonSelectsItsSpeedDirectlyAndHighlightsOnlyThatButton(int index, float speed)
    {
        clock.SimulationSpeed = index == 0 ? 4f : 0f;
        buttons[index].onClick.Invoke();
        Assert.AreEqual(speed, clock.SimulationSpeed);
        for (int i = 0; i < 5; i++) Assert.AreEqual(i == index, indicators[i].activeSelf);
    }

    [Test]
    public void DisplayFollowsScaledHoursNoonMidnightAndPause()
    {
        Assert.AreEqual("00:00", label.text);
        Assert.AreEqual("Day 1", dayLabel.text);
        clock.SimulationSpeed = 4f;
        clock.AdvanceTime(clock.RealSecondsPerGameHour * 3f);
        Assert.AreEqual("12:00", label.text);
        Assert.AreEqual("Day 1", dayLabel.text);
        buttons[0].onClick.Invoke();
        clock.AdvanceTime(clock.RealSecondsPerGameHour * 12f);
        Assert.AreEqual("12:00", label.text);
        Assert.AreEqual("Day 1", dayLabel.text);
        buttons[4].onClick.Invoke();
        clock.AdvanceTime(clock.RealSecondsPerGameHour * 3f);
        Assert.AreEqual("00:00", label.text);
        Assert.AreEqual("Day 2", dayLabel.text);
    }

    [Test]
    public void DisabledHudUnsubscribesAndReenableRendersLatestBackendState()
    {
        Invoke(ui, "OnDisable");
        clock.AdvanceTime(clock.RealSecondsPerGameHour * 25f);
        clock.SimulationSpeed = 2f;
        buttons[0].onClick.Invoke();
        Assert.AreEqual(2f, clock.SimulationSpeed);
        Assert.AreEqual("00:00", label.text);
        Assert.AreEqual("Day 1", dayLabel.text);
        Invoke(ui, "OnEnable");
        Assert.AreEqual("01:00", label.text);
        Assert.AreEqual("Day 2", dayLabel.text);
        Assert.IsTrue(indicators[3].activeSelf);
        buttons[0].onClick.Invoke();
        Assert.AreEqual(0f, clock.SimulationSpeed);
    }

    [Test]
    public void InvalidIndexOrNoninteractableButtonCannotChangeSpeed()
    {
        ui.SelectSpeed(-1);
        ui.SelectSpeed(5);
        buttons[4].interactable = false;
        buttons[4].onClick.Invoke();
        Assert.AreEqual(1f, clock.SimulationSpeed);
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    [TestCase(4f)]
    public void DayDisplayFollowsMidnightAtEverySupportedSpeed(float speed)
    {
        clock.SimulationSpeed = speed;
        clock.AdvanceTime(clock.RealSecondsPerGameHour * 23f / speed);
        Assert.AreEqual("23:00", label.text);
        Assert.AreEqual("Day 1", dayLabel.text);

        clock.AdvanceTime(clock.RealSecondsPerGameHour / speed);
        Assert.AreEqual("00:00", label.text);
        Assert.AreEqual("Day 2", dayLabel.text);
    }

    private void SetField(string name, object value) =>
        typeof(GameClockUI).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ui, value);

    private static void Invoke(object target, string name) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
}
