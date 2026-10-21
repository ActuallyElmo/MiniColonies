using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class TrafficSimulationSpeedTests
{
    private readonly List<Object> _objects = new List<Object>();
    private GameTimeManager _time;
    private ConveyorTrafficManager _conveyor;
    private VehicleAI _vehicle;

    [SetUp]
    public void SetUp()
    {
        _time = CreateComponent<GameTimeManager>("Traffic speed clock");
        _conveyor = CreateComponent<ConveyorTrafficManager>("Traffic speed conveyor");
    }

    [TearDown]
    public void TearDown()
    {
        if (_conveyor != null && _vehicle != null) _conveyor.LeaveTraffic(_vehicle);
        for (int i = _objects.Count - 1; i >= 0; i--)
        {
            if (_objects[i] is GameObject gameObject)
            {
                foreach (MonoBehaviour component in gameObject.GetComponents<MonoBehaviour>())
                    InvokeLifecycle(component, "OnDestroy");
            }
            Object.DestroyImmediate(_objects[i]);
        }
        _objects.Clear();
    }

    [TestCase(0.5f, 1)]
    [TestCase(1f, 1)]
    [TestCase(2f, 4)]
    [TestCase(4f, 1)]
    public void TenFpsFramesAdvanceVehiclesAndSignalsAtSelectedSpeed(float speed, int greenDirection)
    {
        TrafficLightIntersectionController signal = CreateSignal();
        EnterVehicle();
        _time.SimulationSpeed = speed;

        // Ten FPS requires eight fixed traffic steps per frame at x4.
        // The old four-step limit silently lost half of the simulated time.
        for (int frame = 0; frame < 30; frame++) AdvanceFrame(0.1f);

        float expectedSeconds = 3f * speed;
        Assert.AreEqual(expectedSeconds, _vehicle.conveyorDistanceOnEdge, 0.001f);
        Assert.AreEqual(greenDirection, signal.ActiveDirectionMask);
        Assert.AreEqual((expectedSeconds % 5f) / 5f, signal.PhaseProgress, 0.001f);
    }

    [Test]
    public void PauseFreezesVehiclesAndSignalsAndResumeKeepsFixedStepRemainder()
    {
        TrafficLightIntersectionController signal = CreateSignal();
        EnterVehicle();
        AdvanceFrame(0.125f);
        float distance = _vehicle.conveyorDistanceOnEdge;
        float phase = signal.PhaseProgress;
        Vector3 position = _vehicle.transform.position;
        _time.SimulationSpeed = 0f;
        for (int frame = 0; frame < 20; frame++) AdvanceFrame(0.1f);
        Assert.AreEqual(distance, _vehicle.conveyorDistanceOnEdge);
        Assert.AreEqual(phase, signal.PhaseProgress);
        Assert.AreEqual(position, _vehicle.transform.position);

        _time.IncreaseSimulationSpeed();
        AdvanceFrame(0.1f);
        Assert.AreEqual(distance + 0.05f, _vehicle.conveyorDistanceOnEdge, 0.001f);
        Assert.AreEqual(phase + 0.01f, signal.PhaseProgress, 0.001f);
    }

    [Test]
    public void MaximumSupportedFrameAtFourTimesSpeedKeepsAllSixteenTrafficSteps()
    {
        TrafficLightIntersectionController signal = CreateSignal();
        EnterVehicle();
        _time.SimulationSpeed = 4f;

        AdvanceFrame(GameTimeManager.MaximumFrameSeconds);

        Assert.AreEqual(0.8f, _vehicle.conveyorDistanceOnEdge, 0.001f);
        Assert.AreEqual(0.16f, signal.PhaseProgress, 0.001f);
    }

    [Test]
    public void SignalsAdvanceWithoutAnyVehicles()
    {
        TrafficLightIntersectionController signal = CreateSignal();
        _time.SimulationSpeed = 4f;

        for (int frame = 0; frame < 15; frame++) AdvanceFrame(0.1f);

        Assert.AreEqual(4, signal.ActiveDirectionMask);
        Assert.AreEqual(0.2f, signal.PhaseProgress, 0.001f);
    }

    [Test]
    public void PendingDriverReactionExpiresThroughTrafficTicksWithoutWallClockWait()
    {
        EnterVehicle();
        _vehicle.conveyorCurrentSpeed = 0f;
        _vehicle.trafficWasBlocked = true;
        // An already assigned deadline avoids the randomized initial reaction.
        _vehicle.trafficReleaseTime = 0.25f;

        AdvanceFrame(0.1f);
        AdvanceFrame(0.1f);
        Assert.AreEqual(0f, _vehicle.conveyorDistanceOnEdge);
        Assert.IsTrue(_vehicle.trafficWasBlocked);

        AdvanceFrame(0.1f);
        Assert.Greater(_vehicle.conveyorDistanceOnEdge, 0f);
        Assert.IsFalse(_vehicle.trafficWasBlocked);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    [TestCase(-1f)]
    public void InvalidFrameDeltaDoesNotPoisonFixedStepRemainder(float invalidDelta)
    {
        var clock = new TrafficSimulationClock();
        Assert.AreEqual(0, clock.Accumulate(0.025f));
        Assert.AreEqual(0, clock.Accumulate(invalidDelta));
        Assert.AreEqual(1, clock.Accumulate(0.025f));
        Assert.AreEqual(0f, clock.InterpolationAlpha, 0.0001f);
    }

    private void AdvanceFrame(float realSeconds)
    {
        _time.AdvanceTime(realSeconds);
        typeof(ConveyorTrafficManager).GetMethod("Update",
            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_conveyor, null);
    }

    private void EnterVehicle()
    {
        var data = ScriptableObject.CreateInstance<PopTransportVehicleData>();
        _objects.Add(data);
        _vehicle = CreateComponent<VehicleAI>("Traffic speed vehicle");
        data.maximumVehicleSpeed = 1f;
        data.accelerationUnitsPerSecondSquared = 1f;
        data.maximumJerkUnitsPerSecondCubed = 10f;
        _vehicle.vehicleData = data;
        TrafficEdge lane = CreateEdge(0, Vector3.forward * 100f);
        lane.kind = TrafficEdgeKind.RoadLane;
        Assert.IsTrue(_conveyor.EnterRoute(_vehicle, new TrafficRoute
        {
            ManagedEdges = new List<TrafficEdge> { lane },
            StartDistanceOnFirstEdge = 0f,
            EndDistanceOnFinalEdge = lane.totalLength
        }));
        _vehicle.conveyorCurrentSpeed = 1f;
    }

    private TrafficLightIntersectionController CreateSignal()
    {
        var backend = CreateComponent<TrafficSystemBackend>("Traffic speed backend");
        var data = new IntersectionData(Vector2Int.zero)
        {
            RuleType = IntersectionRuleType.TrafficLight,
            TrafficLightCycleSeconds = 5f
        };
        data.SetTrafficLightIndex(1, 0, 0);
        data.SetTrafficLightIndex(4, 0, 1);
        var signal = new TrafficLightIntersectionController();
        signal.Initialize(Vector2Int.zero, data,
            new[] { CreateEdge(1, Vector3.forward), CreateEdge(4, Vector3.right) });
        var controllers = (Dictionary<Vector2Int, IIntersectionController>)
            typeof(TrafficSystemBackend).GetField("_intersectionControllers",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(backend);
        controllers.Add(Vector2Int.zero, signal);
        return signal;
    }

    private T CreateComponent<T>(string name) where T : Component
    {
        var gameObject = new GameObject(name);
        _objects.Add(gameObject);
        T component = gameObject.AddComponent<T>();
        InvokeLifecycle(component, "Awake");
        return component;
    }

    private static void InvokeLifecycle(object target, string methodName) =>
        target.GetType().GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(target, null);

    private static TrafficEdge CreateEdge(int direction, Vector3 end)
    {
        var startNode = new TrafficNode(Vector3.zero);
        var edge = new TrafficEdge(startNode, new TrafficNode(end), 1f, direction != 0)
        {
            edgeId = direction + 1,
            fromDirectionBit = direction,
            fromLaneIndex = 0,
            waypoints = new List<Vector3> { Vector3.zero, end }
        };
        edge.RecalculateLength();
        startNode.outgoingEdges.Add(edge);
        return edge;
    }
}
