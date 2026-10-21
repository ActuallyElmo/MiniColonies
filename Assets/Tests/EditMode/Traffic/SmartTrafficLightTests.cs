using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class SmartTrafficLightTests
{
    private readonly List<GameObject> objects = new List<GameObject>();
    private TrafficLightIntersectionController controller;
    private IntersectionData data;
    private TrafficEdge[] movements;

    [SetUp]
    public void SetUp()
    {
        data = new IntersectionData(Vector2Int.zero)
        {
            RuleType = IntersectionRuleType.SmartTrafficLight,
            SmartTrafficLightWaitSeconds = 2f
        };
        movements = new[] { Movement(1), Movement(4), Movement(16), Movement(64) };
        controller = CreateController();
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; i--)
        {
            if (objects[i] == null) continue;
            foreach (MonoBehaviour component in objects[i].GetComponents<MonoBehaviour>())
                component.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(component, null);
            Object.DestroyImmediate(objects[i]);
        }
        objects.Clear();
    }

    [Test]
    public void IdleAndApproachingTrafficDoNotChangeGreen()
    {
        VehicleAI vehicle = Vehicle();
        controller.Tick(1000f);
        controller.ObserveApproach(vehicle, movements[1], false);
        controller.Tick(1000f);
        Assert.AreEqual(0, controller.ActiveSignalIndex);
        controller.ObserveApproach(vehicle, movements[1], true);
        controller.Tick(1.9f);
        Assert.AreEqual(0, controller.ActiveSignalIndex);
        controller.ObserveApproach(vehicle, movements[1], true);
        controller.Tick(0.1f);
        Assert.AreEqual(1, controller.ActiveSignalIndex);
        Assert.IsTrue(controller.IsGreenForLane(64, 0), "Opposing default lanes share green.");
        controller.Tick(1000f);
        Assert.AreEqual(1, controller.ActiveSignalIndex, "Green is retained without new red demand.");
    }

    [Test]
    public void LongestWaitWinsAndOtherOverdueGroupsReceiveUsableGreen()
    {
        data.SetTrafficLightIndex(16, 0, 2);
        controller = CreateController();
        VehicleAI older = Vehicle();
        VehicleAI newer = Vehicle();
        controller.ObserveApproach(older, movements[2], true);
        controller.Tick(1f);
        controller.ObserveApproach(older, movements[2], true);
        controller.ObserveApproach(newer, movements[1], true);
        controller.Tick(1f);
        Assert.AreEqual(2, controller.ActiveSignalIndex, "Age takes precedence over index order.");
        controller.ObserveApproach(newer, movements[1], true);
        controller.Tick(0.9f);
        Assert.AreEqual(2, controller.ActiveSignalIndex);
        controller.ObserveApproach(newer, movements[1], true);
        controller.Tick(0.1f);
        Assert.AreEqual(1, controller.ActiveSignalIndex);
    }

    [Test]
    public void EqualWaitsUseIndexOrderAndNeverFlipEveryTick()
    {
        data.SetTrafficLightIndex(16, 0, 2);
        controller = CreateController();
        VehicleAI first = Vehicle();
        VehicleAI second = Vehicle();
        controller.ObserveApproach(second, movements[2], true);
        controller.ObserveApproach(first, movements[1], true);
        controller.Tick(2f);
        Assert.AreEqual(1, controller.ActiveSignalIndex);
        controller.ObserveApproach(second, movements[2], true);
        controller.Tick(0.05f);
        Assert.AreEqual(1, controller.ActiveSignalIndex);
        controller.ObserveApproach(second, movements[2], true);
        controller.Tick(0.95f);
        Assert.AreEqual(2, controller.ActiveSignalIndex);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RemovedOrUnobservedVehiclesDoNotLeavePhantomDemand(bool destroyed)
    {
        VehicleAI vehicle = Vehicle();
        controller.ObserveApproach(vehicle, movements[1], true);
        controller.Tick(1f);
        if (destroyed) Object.DestroyImmediate(vehicle.gameObject);
        controller.Tick(1f);
        Assert.AreEqual(0, controller.ActiveSignalIndex);
    }

    [Test]
    public void MovingAwayOrChangingApproachStartsANewWait()
    {
        data.SetTrafficLightIndex(16, 0, 2);
        controller = CreateController();
        VehicleAI vehicle = Vehicle();
        controller.ObserveApproach(vehicle, movements[1], true);
        controller.Tick(1.5f);
        controller.ObserveApproach(vehicle, movements[1], false);
        controller.ObserveApproach(vehicle, movements[2], true);
        controller.Tick(1.5f);
        Assert.AreEqual(0, controller.ActiveSignalIndex);
        controller.ObserveApproach(vehicle, movements[2], true);
        controller.Tick(0.5f);
        Assert.AreEqual(2, controller.ActiveSignalIndex);
    }

    [Test]
    public void RebuildPreservesActualWaitWhenThresholdChangesAndClearsItAcrossModes()
    {
        VehicleAI vehicle = Vehicle();
        controller.ObserveApproach(vehicle, movements[1], true);
        controller.Tick(1.5f);
        data.SmartTrafficLightWaitSeconds = 3f;
        var rebuilt = CreateController();
        Assert.IsTrue(rebuilt.RestorePhaseFrom(controller));
        rebuilt.ObserveApproach(vehicle, movements[1], true);
        rebuilt.Tick(1.4f);
        Assert.AreEqual(0, rebuilt.ActiveSignalIndex);
        rebuilt.ObserveApproach(vehicle, movements[1], true);
        rebuilt.Tick(0.1f);
        Assert.AreEqual(1, rebuilt.ActiveSignalIndex);

        data.RuleType = IntersectionRuleType.TrafficLight;
        var normal = CreateController();
        Assert.IsTrue(normal.RestorePhaseFrom(controller));
        data.RuleType = IntersectionRuleType.SmartTrafficLight;
        rebuilt = CreateController();
        Assert.IsTrue(rebuilt.RestorePhaseFrom(normal));
        rebuilt.Tick(100f);
        Assert.AreEqual(0, rebuilt.ActiveSignalIndex);
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    [TestCase(4f)]
    public void PendingWaitFollowsSpeedChangesAndPause(float speed)
    {
        var clock = CreateComponent<GameTimeManager>();
        var steps = new TrafficSimulationClock(0.05f, 16);
        VehicleAI vehicle = Vehicle();
        void Frame(float realSeconds)
        {
            clock.AdvanceTime(realSeconds);
            int count = steps.Accumulate(GameTimeManager.SimulationDeltaTime);
            for (int i = 0; i < count; i++)
            {
                controller.ObserveApproach(vehicle, movements[1], true);
                controller.Tick(steps.FixedDeltaSeconds);
            }
        }
        clock.SimulationSpeed = 1f;
        for (int i = 0; i < 10; i++) Frame(0.1f);
        clock.SimulationSpeed = 0f;
        for (int i = 0; i < 100; i++) Frame(0.1f);
        Assert.AreEqual(0, controller.ActiveSignalIndex);
        controller.Tick(float.NaN);
        controller.Tick(float.PositiveInfinity);
        controller.Tick(-10f);
        controller.Tick(0f);
        clock.SimulationSpeed = speed;
        int frames = Mathf.RoundToInt(1f / (0.05f * speed));
        for (int i = 0; i < frames - 1; i++) Frame(0.05f);
        Assert.AreEqual(0, controller.ActiveSignalIndex);
        Frame(0.05f);
        Assert.AreEqual(1, controller.ActiveSignalIndex);
    }

    [Test]
    public void WaitSettingValidatesNotifiesOnceAndRetainsNormalDurationAndConnections()
    {
        data.AddCustomRule(1, 0, 4, 0);
        int changes = 0;
        System.Action<IntersectionData> changed = _ => changes++;
        data.Changed += changed;
        try
        {
            data.SmartTrafficLightWaitSeconds = -1f;
            data.SmartTrafficLightWaitSeconds = -2f;
            Assert.AreEqual(1, changes);
            Assert.AreEqual(1f, data.SmartTrafficLightWaitSeconds);
            data.SmartTrafficLightWaitSeconds = float.NaN;
            Assert.AreEqual(10f, data.SmartTrafficLightWaitSeconds);
            data.SmartTrafficLightWaitSeconds = 999f;
            Assert.AreEqual(120f, data.SmartTrafficLightWaitSeconds);
            Assert.AreEqual(30f, data.TrafficLightCycleSeconds);
            Assert.AreEqual(1, data.CustomRules.Count);
        }
        finally { data.Changed -= changed; }
    }

    [Test]
    public void ConveyorDetectsOnlyStoppedCarsAtTheLineEvenWhenTheMovementIsOccupied()
    {
        var conveyor = CreateComponent<ConveyorTrafficManager>();
        VehicleAI vehicle = Vehicle();
        TrafficEdge approach = vehicle.currentEdge;
        approach.totalLength = 10f;
        movements[1].exitController = controller;
        vehicle.conveyorRoute = new TrafficRoute
        {
            ManagedEdges = new List<TrafficEdge> { approach, movements[1] }
        };
        VehicleAI blocker = Vehicle();
        blocker.currentEdge = movements[1];
        var runtime = (TrafficRuntimeState)typeof(ConveyorTrafficManager)
            .GetField("_runtimeState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(conveyor);
        runtime.Register(blocker, movements[1], 0f);
        var probe = typeof(ConveyorTrafficManager).GetMethod("CanTransitionToNextEdge",
            BindingFlags.Instance | BindingFlags.NonPublic);
        void Observe() => probe.Invoke(conveyor, new object[] { vehicle, 0f, false, false });

        vehicle.conveyorDistanceOnEdge = 0f;
        Observe();
        controller.Tick(3f);
        Assert.AreEqual(0, controller.ActiveSignalIndex);
        vehicle.conveyorDistanceOnEdge = 10f - vehicle.GetVehicleLengthUnits() * 0.5f - 0.03f;
        vehicle.conveyorCurrentSpeed = 1f;
        Observe();
        controller.Tick(3f);
        Assert.AreEqual(0, controller.ActiveSignalIndex);
        vehicle.conveyorCurrentSpeed = 0f;
        Observe();
        controller.Tick(2f);
        Assert.AreEqual(1, controller.ActiveSignalIndex,
            "Exit clearance short-circuiting must not hide a stopped approach.");
        runtime.Unregister(blocker);
    }

    [Test]
    public void LongSmartRedWaitDoesNotTriggerStallRecoveryButBlockedGreenStillCounts()
    {
        data.SmartTrafficLightWaitSeconds = 120f;
        controller = CreateController();
        var conveyor = CreateComponent<ConveyorTrafficManager>();
        VehicleAI vehicle = Vehicle();
        TrafficEdge approach = vehicle.currentEdge;
        approach.waypoints.Add(Vector3.zero);
        approach.waypoints.Add(Vector3.forward * 10f);
        approach.RecalculateLength();
        TrafficEdge movement = movements[1];
        movement.waypoints.Add(Vector3.forward * 10f);
        movement.waypoints.Add(Vector3.forward * 11f);
        movement.RecalculateLength();
        movement.exitController = controller;
        Assert.IsTrue(conveyor.EnterRoute(vehicle, new TrafficRoute
        {
            ManagedEdges = new List<TrafficEdge> { approach, movement },
            StartDistanceOnFirstEdge = 10f - vehicle.GetVehicleLengthUnits() * 0.5f - 0.03f,
            EndDistanceOnFinalEdge = movement.totalLength
        }));
        VehicleAI blocker = Vehicle();
        blocker.currentEdge = movement;
        var runtime = (TrafficRuntimeState)typeof(ConveyorTrafficManager)
            .GetField("_runtimeState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(conveyor);
        runtime.Register(blocker, movement, 0f);
        var tick = typeof(ConveyorTrafficManager).GetMethod("TickVehicle",
            BindingFlags.Instance | BindingFlags.NonPublic);
        try
        {
            vehicle.trafficStationaryTime = 29.99f;
            tick.Invoke(conveyor, new object[] { vehicle, 0.1f });
            Assert.AreSame(approach, vehicle.currentEdge);
            Assert.AreEqual(0f, vehicle.trafficStationaryTime);
            controller.Tick(120f);
            Assert.IsTrue(controller.IsGreenForLane(4, 0));
            tick.Invoke(conveyor, new object[] { vehicle, 0.1f });
            Assert.Greater(vehicle.trafficStationaryTime, 0f,
                "Blocked green must still participate in stall recovery.");
        }
        finally
        {
            conveyor.LeaveTraffic(vehicle);
            runtime.Unregister(blocker);
        }
    }

    private TrafficLightIntersectionController CreateController()
    {
        var result = new TrafficLightIntersectionController();
        result.Initialize(Vector2Int.zero, data, movements);
        return result;
    }

    private VehicleAI Vehicle()
    {
        var vehicle = CreateComponent<VehicleAI>();
        vehicle.currentEdge = new TrafficEdge(new TrafficNode(Vector3.zero),
            new TrafficNode(Vector3.forward), 1f);
        return vehicle;
    }

    private T CreateComponent<T>() where T : Component
    {
        var go = new GameObject(typeof(T).Name + " smart signal test");
        objects.Add(go);
        T result = go.AddComponent<T>();
        if (result is GameTimeManager || result is ConveyorTrafficManager)
            typeof(T).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(result, null);
        return result;
    }

    private static TrafficEdge Movement(int direction) =>
        new TrafficEdge(new TrafficNode(Vector3.zero), new TrafficNode(Vector3.one), 1f, true)
        { fromDirectionBit = direction, fromLaneIndex = 0 };
}
