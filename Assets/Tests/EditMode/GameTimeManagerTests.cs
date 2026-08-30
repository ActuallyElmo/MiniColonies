using System.Collections.Generic;
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
}
