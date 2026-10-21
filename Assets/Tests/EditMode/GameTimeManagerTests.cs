using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class GameTimeManagerTests
{
    private GameObject _managerObject;
    private GameTimeManager _manager;

    [SetUp]
    public void SetUp()
    {
        _managerObject = new GameObject("GameTimeManagerTests");
        _manager = _managerObject.AddComponent<GameTimeManager>();
        typeof(GameTimeManager).GetMethod("Awake",
            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_manager, null);
        _manager.RealMinutesPerDay = 1f;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_managerObject);
    }

    [Test]
    public void OneRealDayDurationAdvancesExactlyTwentyFourHours()
    {
        var changedHours = new List<int>();
        var changedDays = new List<int>();
        _manager.OnHourChanged += changedHours.Add;
        _manager.OnDayChanged += changedDays.Add;

        _manager.AdvanceTime(60f);

        Assert.AreEqual(2, _manager.CurrentDay);
        Assert.AreEqual(0, _manager.CurrentHour);
        Assert.AreEqual(24, changedHours.Count);
        Assert.AreEqual(0, changedHours[changedHours.Count - 1]);
        CollectionAssert.AreEqual(new[] { 2 }, changedDays);
    }

    [Test]
    public void PartialHourDoesNotInvokeChangeEvents()
    {
        int hourEventCount = 0;
        int dayEventCount = 0;
        _manager.OnHourChanged += _ => hourEventCount++;
        _manager.OnDayChanged += _ => dayEventCount++;

        _manager.AdvanceTime(_manager.RealSecondsPerGameHour * 0.5f);

        Assert.AreEqual(1, _manager.CurrentDay);
        Assert.AreEqual(0, _manager.CurrentHour);
        Assert.AreEqual(0.5f, _manager.CurrentHourProgress, 0.0001f);
        Assert.AreEqual(0, hourEventCount);
        Assert.AreEqual(0, dayEventCount);
    }

    [Test]
    public void LargeAdvanceInvokesEveryCrossedHourAndDay()
    {
        var changedHours = new List<int>();
        var changedDays = new List<int>();
        _manager.OnHourChanged += changedHours.Add;
        _manager.OnDayChanged += changedDays.Add;

        _manager.AdvanceTime(_manager.RealSecondsPerGameHour * 49f);

        Assert.AreEqual(3, _manager.CurrentDay);
        Assert.AreEqual(1, _manager.CurrentHour);
        Assert.AreEqual(49, changedHours.Count);
        CollectionAssert.AreEqual(new[] { 2, 3 }, changedDays);
    }

    [Test]
    public void InvalidDayLengthFallsBackToSafeDefault()
    {
        _manager.RealMinutesPerDay = float.NaN;
        Assert.AreEqual(10f, _manager.RealMinutesPerDay);
        Assert.DoesNotThrow(() => _manager.AdvanceTime(1f));

        _manager.RealMinutesPerDay = float.PositiveInfinity;
        Assert.AreEqual(10f, _manager.RealMinutesPerDay);
    }

    [TestCase(0f)]
    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    [TestCase(4f)]
    public void SelectedSpeedScalesCalendarAndSimulationButLeavesUnityTimeUnchanged(float speed)
    {
        float unityScale = Time.timeScale;
        float fixedDelta = Time.fixedDeltaTime;
        _manager.SimulationSpeed = speed;

        _manager.AdvanceTime(5f);

        Assert.AreEqual(5f * speed, GameTimeManager.SimulationTimeSeconds, 0.0001d);
        Assert.AreEqual(5f * speed, GameTimeManager.SimulationDeltaTime, 0.0001f);
        Assert.AreEqual((int)(2f * speed), _manager.CurrentHour);
        Assert.AreEqual(unityScale, Time.timeScale);
        Assert.AreEqual(fixedDelta, Time.fixedDeltaTime);
    }

    [Test]
    public void SpeedStepsClampAtEndpointsAndOnlyNotifyOnChange()
    {
        var changes = new List<float>();
        _manager.OnSimulationSpeedChanged += changes.Add;
        _manager.IncreaseSimulationSpeed();
        _manager.IncreaseSimulationSpeed();
        _manager.IncreaseSimulationSpeed();
        _manager.DecreaseSimulationSpeed();
        _manager.DecreaseSimulationSpeed();
        _manager.DecreaseSimulationSpeed();
        _manager.DecreaseSimulationSpeed();

        _manager.DecreaseSimulationSpeed();
        _manager.IncreaseSimulationSpeed();
        CollectionAssert.AreEqual(new[] { 2f, 4f, 2f, 1f, 0.5f, 0f, 0.5f }, changes);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(-1f)]
    [TestCase(3f)]
    public void UnsupportedSpeedUsesNormalSpeed(float invalidSpeed)
    {
        _manager.SimulationSpeed = invalidSpeed;
        _manager.AdvanceTime(1f);
        Assert.AreEqual(1f, _manager.SimulationSpeed);
        Assert.AreEqual(1d, GameTimeManager.SimulationTimeSeconds);
    }

    [Test]
    public void PendingGameplayWaitAndPartialHourFollowLiveSpeedChanges()
    {
        var wait = GameTimeManager.WaitForSimulationSeconds(5f);
        Assert.IsTrue(wait.MoveNext());
        _manager.AdvanceTime(1f);
        Assert.IsTrue(wait.MoveNext());
        _manager.SimulationSpeed = 4f;
        _manager.AdvanceTime(0.5f);
        Assert.IsTrue(wait.MoveNext());
        _manager.SimulationSpeed = 0.5f;
        _manager.AdvanceTime(4f);

        Assert.IsFalse(wait.MoveNext());
        Assert.AreEqual(2, _manager.CurrentHour);
        Assert.AreEqual(0f, _manager.CurrentHourProgress, 0.0001f);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PausePreservesPartialHourAndPendingWaitWithoutCatchingUpOnResume(bool unscaled)
    {
        _manager.UseUnscaledTime = unscaled;
        var wait = GameTimeManager.WaitForSimulationSeconds(2.5f);
        Assert.IsTrue(wait.MoveNext());
        _manager.AdvanceTime(1f);
        _manager.SimulationSpeed = 0f;
        Assert.AreEqual(0f, GameTimeManager.SimulationDeltaTime);
        int hourEvents = 0;
        _manager.OnHourChanged += _ => hourEvents++;
        _manager.AdvanceTime(100f);
        Assert.AreEqual(1d, GameTimeManager.SimulationTimeSeconds);
        Assert.AreEqual(0.4f, _manager.CurrentHourProgress, 0.0001f);
        Assert.AreEqual(0, hourEvents);
        Assert.IsTrue(wait.MoveNext());

        _manager.IncreaseSimulationSpeed();
        _manager.AdvanceTime(3f);
        Assert.AreEqual(1, hourEvents);
        Assert.IsFalse(wait.MoveNext());
    }

    [Test]
    public void FastAdvanceAcrossMidnightKeepsEveryHourAndDayNotificationInOrder()
    {
        _manager.AdvanceTime(23f * _manager.RealSecondsPerGameHour);
        var events = new List<string>();
        _manager.OnDayChanged += day => events.Add("day " + day);
        _manager.OnHourChanged += hour => events.Add("hour " + hour);
        _manager.SimulationSpeed = 4f;

        _manager.AdvanceTime(_manager.RealSecondsPerGameHour);

        CollectionAssert.AreEqual(
            new[] { "day 2", "hour 0", "hour 1", "hour 2", "hour 3" }, events);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(-1f)]
    [TestCase(0f)]
    public void InvalidDeltaDoesNotAdvanceSimulationOrPoisonLaterAdvances(float seconds)
    {
        _manager.AdvanceTime(seconds);
        Assert.AreEqual(0f, GameTimeManager.SimulationDeltaTime);
        Assert.AreEqual(0d, GameTimeManager.SimulationTimeSeconds);
        _manager.AdvanceTime(1f);
        Assert.AreEqual(1d, GameTimeManager.SimulationTimeSeconds);
    }
}
