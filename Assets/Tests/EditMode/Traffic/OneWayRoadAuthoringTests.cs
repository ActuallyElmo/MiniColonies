using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

[Ignore("One-way roads are disabled for now; see Assets/Scripts/Roads/Traffic/OneWayRoadAttempt.md.")]
public sealed class OneWayRoadAuthoringTests
{
    private RoadSystemBackend _backend;
    private GameObject _backendObject;
    private RoadType _oneWayRoad;

    [SetUp]
    public void SetUp()
    {
        _backendObject = new GameObject("RoadSystemBackendTest");
        _backend = _backendObject.AddComponent<RoadSystemBackend>();
        _oneWayRoad = ScriptableObject.CreateInstance<RoadType>();
        _oneWayRoad.roadName = "One Way Test Road";
        _oneWayRoad.trafficProfileKey = "one-way-test-road";
        _oneWayRoad.isTwoWay = false;
        _oneWayRoad.lanesPerWay = 1;
        _oneWayRoad.speedLimit = 12f;
        _oneWayRoad.roadWidth = 0.5f;
        _oneWayRoad.curbWidth = 0.1f;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_oneWayRoad);
        Object.DestroyImmediate(_backendObject);
    }

    [Test]
    public void OneWayBuildUsesPlacementDirectionWithoutExistingContinuation()
    {
        Dictionary<Vector2Int, RoadCell> roads = Roads();
        Vector2Int a = new Vector2Int(0, 0);
        Vector2Int b = new Vector2Int(1, 0);
        roads.Add(a, new RoadCell(a, 0, _oneWayRoad));
        roads.Add(b, new RoadCell(b, 0, _oneWayRoad));

        _backend.ConnectCellsSafe(a, b, _oneWayRoad);

        AssertCanTravel(roads[a], 4, true);
        AssertCanTravel(roads[b], 64, false);
    }

    [Test]
    public void OneWayConnectionAlignsRecentPartToExistingContinuation()
    {
        Dictionary<Vector2Int, RoadCell> roads = Roads();
        Vector2Int a = new Vector2Int(0, 0);
        Vector2Int b = new Vector2Int(1, 0);
        Vector2Int c = new Vector2Int(2, 0);
        roads.Add(a, new RoadCell(a, 0, _oneWayRoad));
        roads.Add(b, new RoadCell(b, 0, _oneWayRoad));
        roads.Add(c, new RoadCell(c, 0, _oneWayRoad));
        roads[c].SetPreview(true);

        _backend.ConnectCellsSafe(a, b, _oneWayRoad);
        _backend.ConnectCellsSafe(c, b, _oneWayRoad);

        AssertCanTravel(roads[a], 4, true);
        AssertCanTravel(roads[b], 64, false);
        AssertCanTravel(roads[b], 4, true);
        AssertCanTravel(roads[c], 64, false);
        Assert.AreEqual(RoadNodeKind.ThroughRoad, _backend.GetRoadNodeKind(roads[b]));
    }

    [Test]
    public void OneWayConnectionNormalizesConflictingPermanentContinuations()
    {
        Dictionary<Vector2Int, RoadCell> roads = Roads();
        Vector2Int a = new Vector2Int(0, 0);
        Vector2Int b = new Vector2Int(1, 0);
        Vector2Int c = new Vector2Int(2, 0);
        Vector2Int d = new Vector2Int(3, 0);
        roads.Add(a, new RoadCell(a, 0, _oneWayRoad));
        roads.Add(b, new RoadCell(b, 0, _oneWayRoad));
        roads.Add(c, new RoadCell(c, 0, _oneWayRoad));
        roads.Add(d, new RoadCell(d, 0, _oneWayRoad));

        _backend.ConnectCellsSafe(a, b, _oneWayRoad);
        _backend.ConnectCellsSafe(d, c, _oneWayRoad);
        _backend.ConnectCellsSafe(b, c, _oneWayRoad);

        AssertCanTravel(roads[a], 4, true);
        AssertCanTravel(roads[b], 64, false);
        AssertCanTravel(roads[b], 4, true);
        AssertCanTravel(roads[c], 64, false);
        AssertCanTravel(roads[c], 4, true);
        AssertCanTravel(roads[d], 64, false);
        Assert.AreEqual(RoadNodeKind.ThroughRoad, _backend.GetRoadNodeKind(roads[b]));
        Assert.AreEqual(RoadNodeKind.ThroughRoad, _backend.GetRoadNodeKind(roads[c]));
    }

    [Test]
    public void SameOneWayUpgradeReversesWholeRoadEdge()
    {
        Dictionary<Vector2Int, RoadCell> roads = Roads();
        Vector2Int a = new Vector2Int(0, 0);
        Vector2Int b = new Vector2Int(1, 0);
        Vector2Int c = new Vector2Int(2, 0);
        roads.Add(a, new RoadCell(a, 0, _oneWayRoad));
        roads.Add(b, new RoadCell(b, 0, _oneWayRoad));
        roads.Add(c, new RoadCell(c, 0, _oneWayRoad));
        _backend.ConnectCellsSafe(a, b, _oneWayRoad);
        _backend.ConnectCellsSafe(b, c, _oneWayRoad);

        _backend.UpgradeRoadBlock(b, _oneWayRoad);

        AssertCanTravel(roads[a], 4, false);
        AssertCanTravel(roads[b], 64, true);
        AssertCanTravel(roads[b], 4, false);
        AssertCanTravel(roads[c], 64, true);
    }

    [Test]
    public void CompilerPublishesMultipleLanesForOneWayRoad()
    {
        RoadProfile profile = CompileRoadProfile("two-lane-one-way", false, 2);
        RoadNetworkSnapshot snapshot = RoadNetworkSnapshot.CreateForCompilation(
            1,
            0,
            1f,
            1f,
            new[]
            {
                CreateRoadCell(
                    new Vector2Int(0, 0),
                    4,
                    4,
                    0,
                    RoadNodeKind.RoadEnd,
                    profile),
                CreateRoadCell(
                    new Vector2Int(1, 0),
                    4 | 64,
                    4,
                    64,
                    RoadNodeKind.ThroughRoad,
                    profile),
                CreateRoadCell(
                    new Vector2Int(2, 0),
                    64,
                    0,
                    64,
                    RoadNodeKind.RoadEnd,
                    profile)
            },
            new[] { profile });

        Assert.IsTrue(TrafficGraphCompiler.TryCompile(
            snapshot,
            new TrafficGraphVersion(1),
            out TrafficGraphSnapshot graph,
            out TrafficDiagnosticCollection diagnostics));
        Assert.IsFalse(diagnostics.HasErrors);
        Assert.AreEqual(2, graph.Lanes.Count);
        for (int i = 0; i < graph.Lanes.Count; i++)
        {
            Assert.AreEqual(
                TrafficLaneFlowDirection.SectionStartToEnd,
                graph.Lanes[i].FlowDirection);
        }
    }

    [Test]
    public void CompilerAllowsOneWayRoadToTerminateAtIntersection()
    {
        RoadProfile profile = CompileRoadProfile(
            "three-lane-one-way-intersection-sink",
            false,
            3);
        RoadNetworkSnapshot snapshot = RoadNetworkSnapshot.CreateForCompilation(
            1,
            0,
            1f,
            1f,
            new[]
            {
                CreateRoadCell(
                    new Vector2Int(0, 0),
                    4,
                    4,
                    0,
                    RoadNodeKind.RoadEnd,
                    profile),
                CreateRoadCell(
                    new Vector2Int(1, 0),
                    64,
                    0,
                    64,
                    RoadNodeKind.Intersection,
                    profile)
            },
            new[] { profile });

        Assert.IsTrue(TrafficGraphCompiler.TryCompile(
            snapshot,
            new TrafficGraphVersion(1),
            out TrafficGraphSnapshot graph,
            out TrafficDiagnosticCollection diagnostics));
        Assert.IsFalse(diagnostics.HasErrors);
        Assert.AreEqual(3, graph.Lanes.Count);
        Assert.AreEqual(
            3,
            CountRejections(
                graph,
                TrafficMovementRejectionReason.NoLegalOutgoingLane));
    }

    private Dictionary<Vector2Int, RoadCell> Roads()
    {
        FieldInfo field = typeof(RoadSystemBackend).GetField(
            "_roads",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return (Dictionary<Vector2Int, RoadCell>)field.GetValue(_backend);
    }

    private static void AssertCanTravel(
        RoadCell cell,
        int directionBit,
        bool canExit)
    {
        Assert.AreEqual(
            canExit,
            (cell.outConnections & directionBit) != 0,
            "Unexpected outgoing connection state.");
        Assert.AreEqual(
            !canExit,
            (cell.inConnections & directionBit) != 0,
            "Unexpected incoming connection state.");
    }

    private static RoadCellRecord CreateRoadCell(
        Vector2Int cell,
        int connections,
        int outgoing,
        int incoming,
        RoadNodeKind nodeKind,
        RoadProfile profile)
    {
        return new RoadCellRecord(
            cell,
            0,
            connections,
            outgoing,
            incoming,
            profile.Id,
            nodeKind,
            new Vector3(cell.x + 0.5f, 0f, cell.y + 0.5f));
    }

    private static int CountRejections(
        TrafficGraphSnapshot graph,
        TrafficMovementRejectionReason reason)
    {
        int count = 0;
        for (int i = 0; i < graph.MovementRejections.Count; i++)
        {
            if (graph.MovementRejections[i].Reason == reason)
            {
                count++;
            }
        }

        return count;
    }

    private static RoadProfile CompileRoadProfile(
        string key,
        bool isTwoWay,
        int lanesPerWay)
    {
        RoadType roadType = ScriptableObject.CreateInstance<RoadType>();
        roadType.trafficProfileKey = key;
        roadType.roadName = key;
        roadType.lanesPerWay = lanesPerWay;
        roadType.isTwoWay = isTwoWay;
        roadType.speedLimit = 12f;
        roadType.roadWidth = 0.5f;
        roadType.curbWidth = 0.1f;
        roadType.allowedTrafficPermissions = RoadPermissionMask.All;
        roadType.allowedVehicleCapabilities = VehicleCapabilityMask.All;
        roadType.supportedTrafficMovements = RoadMovementPolicyMask.All;

        var diagnostics = new TrafficDiagnosticCollection();
        Assert.IsTrue(roadType.TryCompileTrafficProfile(
            diagnostics,
            out RoadProfile profile));
        Object.DestroyImmediate(roadType);
        return profile;
    }
}
