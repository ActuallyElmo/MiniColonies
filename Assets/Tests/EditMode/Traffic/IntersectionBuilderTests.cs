using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class IntersectionBuilderTests
{
    private RoadType roadType;
    private RoadProfile profile;
    private RoadNetworkSnapshot roads;
    private TrafficGraphSnapshot graph;
    private ControlledNodeRecord node;
    private GameObject backendObject;

    [SetUp]
    public void SetUp()
    {
        roadType = ScriptableObject.CreateInstance<RoadType>();
        roadType.roadName = roadType.trafficProfileKey = "intersection-builder-test";
        roadType.isTwoWay = true;
        roadType.lanesPerWay = 2;
        roadType.speedLimit = 12f;
        roadType.roadWidth = 0.5f;
        roadType.curbWidth = 0.1f;
        roadType.allowedTrafficPermissions = RoadPermissionMask.GeneralRoad;
        roadType.allowedVehicleCapabilities = VehicleCapabilityMask.GroundRoad;
        roadType.supportedTrafficMovements = RoadMovementPolicyMask.All;
        Assert.IsTrue(roadType.TryCompileTrafficProfile(new TrafficDiagnosticCollection(), out profile));
        roads = CreateRoads();
        graph = Compile(roads);
        node = graph.ControlledNodes.Single();
    }

    [TearDown]
    public void TearDown()
    {
        if (backendObject != null) Object.DestroyImmediate(backendObject);
        Object.DestroyImmediate(roadType);
    }

    [Test]
    public void SmartPolicyPublishesIndependentWaitSettingAndLaneSignals()
    {
        var policy = new IntersectionData(Vector2Int.zero)
        {
            RuleType = IntersectionRuleType.SmartTrafficLight,
            SmartTrafficLightWaitSeconds = 17f,
            TrafficLightCycleSeconds = 45f
        };
        policy.SetTrafficLightIndex(1, 0, 9);
        var captured = CreateRoads(authoredPolicy: policy);
        policy.SmartTrafficLightWaitSeconds = 3f;
        var published = Compile(captured);
        var publishedNode = published.ControlledNodes.Single();
        Assert.AreEqual(17f, publishedNode.SmartTrafficLightWaitSeconds);
        Assert.AreEqual(45f, publishedNode.TrafficLightCycleSeconds);
        Assert.AreEqual(IntersectionRuleType.SmartTrafficLight, publishedNode.RuleType);
        var trafficObject = new GameObject("Published smart signal backend");
        try
        {
            var traffic = trafficObject.AddComponent<TrafficSystemBackend>();
            typeof(TrafficSystemBackend).GetProperty("CurrentTrafficGraphSnapshot").SetValue(traffic, published);
            Assert.IsTrue(ManagedTrafficGraphAdapter.TryBuild(published,
                new TrafficDiagnosticCollection(), out var adapter));
            traffic.allEdges = adapter.Edges;
            typeof(TrafficSystemBackend).GetMethod("RebuildIntersectionControllers",
                BindingFlags.NonPublic | BindingFlags.Instance).Invoke(traffic, null);
            var controllers = (Dictionary<Vector2Int, IIntersectionController>)typeof(TrafficSystemBackend)
                .GetField("_intersectionControllers", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(traffic);
            var light = (TrafficLightIntersectionController)controllers[Vector2Int.zero];
            Assert.AreEqual(IntersectionRuleType.SmartTrafficLight, light.RuleType);
            Assert.AreEqual(17f, light.SmartWaitSeconds);
            Assert.IsTrue(light.TryGetSignalIndex(1, 0, out int index));
            Assert.AreEqual(9, index);
            int active = light.ActiveSignalIndex;
            traffic.TickIntersectionControllers(1000f);
            Assert.AreEqual(active, light.ActiveSignalIndex, "No traffic means no automatic cycling.");
            foreach (LaneId lane in publishedNode.IncomingLaneIds)
                Assert.IsTrue(traffic.TryGetTrafficLightState(Vector2Int.zero, lane, out _));
        }
        finally { Object.DestroyImmediate(trafficObject); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SignalIndicesSurviveCompilationAndDriveLaneStateFromPublishedPolicy(bool clearConnections)
    {
        var indices = new List<LaneTrafficLightIndex>
        {
            new LaneTrafficLightIndex(1, 0, 9),
            new LaneTrafficLightIndex(1, 1, 2),
            new LaneTrafficLightIndex(4, 0, 2),
            new LaneTrafficLightIndex(4, 1, 9),
            new LaneTrafficLightIndex(64, 0, 9),
            new LaneTrafficLightIndex(64, 1, 2)
        };
        var source = CreateRoads(rules: clearConnections ? new LaneConnectionRule[0] : null,
            signalIndices: indices);
        indices[0] = new LaneTrafficLightIndex(1, 0, 0);
        var published = Compile(source);
        Assert.AreEqual(9, published.ControlledNodes.Single().TrafficLightIndices[0].Index,
            "Later authoring mutations must not change captured policy.");
        var trafficObject = new GameObject("Published lane signal backend");
        try
        {
            var traffic = trafficObject.AddComponent<TrafficSystemBackend>();
            typeof(TrafficSystemBackend).GetProperty("CurrentTrafficGraphSnapshot")
                .SetValue(traffic, published);
            Assert.IsTrue(ManagedTrafficGraphAdapter.TryBuild(published,
                new TrafficDiagnosticCollection(), out var adapter));
            traffic.allEdges = adapter.Edges;
            typeof(TrafficSystemBackend).GetMethod("RebuildIntersectionControllers",
                BindingFlags.NonPublic | BindingFlags.Instance).Invoke(traffic, null);
            foreach (LaneId id in published.ControlledNodes.Single().IncomingLaneIds)
            {
                published.TryGetLane(id, out var lane);
                int expected = source.IntersectionPolicies.Single().TrafficLightIndices
                    .Single(signal => signal.Source.DirectionBit == lane.EndLegDirectionBit &&
                        signal.Source.LaneIndex == lane.LaneOrdinal).Index;
                Assert.IsTrue(traffic.TryGetTrafficLightState(Vector2Int.zero, id, out bool green));
                Assert.AreEqual(expected == 2, green);
                traffic.TickIntersectionControllers(30f);
                Assert.IsTrue(traffic.TryGetTrafficLightState(Vector2Int.zero, id, out green));
                Assert.AreEqual(expected == 9, green);
                traffic.TickIntersectionControllers(30f);
            }
            Assert.IsFalse(traffic.TryGetTrafficLightState(Vector2Int.one,
                published.ControlledNodes.Single().IncomingLaneIds[0], out _));
            Assert.IsFalse(traffic.TryGetTrafficLightState(Vector2Int.zero, LaneId.Invalid, out _));
        }
        finally { Object.DestroyImmediate(trafficObject); }
    }

    [TestCase(-0.25f)]
    [TestCase(0f)]
    [TestCase(0.25f)]
    public void SignalIconOffsetMovesAlongEveryIncomingRoadInBothDirections(float offset)
    {
        var published = Compile(CreateRoads(includeSouthRoad: true,
            signalIndices: new LaneTrafficLightIndex[0]));
        backendObject = new GameObject("Signal icon offset test");
        var traffic = backendObject.AddComponent<TrafficSystemBackend>();
        typeof(TrafficSystemBackend).GetProperty("CurrentTrafficGraphSnapshot")
            .SetValue(traffic, published);
        var overlay = backendObject.AddComponent<IntersectionTrafficLightOverlay>();
        var style = IntersectionBuilderStyle.Default;
        style.trafficLightIconOutwardOffset = 0f;
        overlay.Initialize(traffic, style);
        var root = (GameObject)typeof(IntersectionTrafficLightOverlay).GetField("visualRoot",
            BindingFlags.NonPublic | BindingFlags.Instance).GetValue(overlay);
        var icons = root.GetComponentsInChildren<SpriteRenderer>();
        var lanes = published.ControlledNodes.Single().IncomingLaneIds;
        Assert.Greater(icons.Length, 0);
        Assert.AreEqual(lanes.Count, icons.Length);
        Vector3[] originalPositions = icons.Select(icon => icon.transform.position).ToArray();

        style.trafficLightIconOutwardOffset = offset;
        overlay.ApplyAppearance(style);

        for (int i = 0; i < icons.Length; i++)
        {
            Assert.IsTrue(published.TryGetLane(lanes[i], out var lane));
            Vector2 outward = IntersectionControlUtility.DirectionVector(lane.EndLegDirectionBit);
            Vector3 shift = icons[i].transform.position - originalPositions[i];
            Assert.That(Vector3.Dot(shift, new Vector3(outward.x, 0f, outward.y)),
                Is.EqualTo(offset).Within(0.0001f));
            Assert.That(Vector3.Dot(shift, new Vector3(-outward.y, 0f, outward.x)),
                Is.EqualTo(0f).Within(0.0001f), "The icon must stay aligned with its lane.");
            Assert.That(shift.y, Is.EqualTo(0f).Within(0.0001f));
        }
    }

    [Test]
    public void SignalIndexBackendValidatesLaneAndRejectsStaleEdits()
    {
        backendObject = new GameObject("Signal index editing backend");
        var backend = backendObject.AddComponent<RoadSystemBackend>();
        var cells = (Dictionary<Vector2Int, RoadCell>)typeof(RoadSystemBackend)
            .GetField("_roads", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(backend);
        var cell = new RoadCell(Vector2Int.zero, 0, roadType);
        cell.AddConnection(1 | 4 | 64);
        cells.Add(Vector2Int.zero, cell);
        Assert.IsTrue(backend.SetIntersectionRuleType(Vector2Int.zero, IntersectionRuleType.TrafficLight));
        var source = CreateRoads(revision: backend.AuthoringRevision,
            signalIndices: new LaneTrafficLightIndex[0]);
        var published = Compile(source);
        var intersection = published.ControlledNodes.Single();
        int changes = 0;
        System.Action<Vector2Int> changed = _ => changes++;
        backend.OnRoadCellChanged += changed;
        try
        {
            LaneId entry = intersection.IncomingLaneIds[0];
            Assert.IsFalse(backend.TrySetIntersectionTrafficLightIndex(source, published,
                intersection.Id, entry, 10));
            Assert.IsFalse(backend.TrySetIntersectionTrafficLightIndex(source, published,
                intersection.Id, intersection.OutgoingLaneIds[0], 9));
            var mismatchedGraph = Compile(CreateRoads(revision: source.Revision + 1,
                signalIndices: new LaneTrafficLightIndex[0]));
            Assert.IsFalse(backend.TrySetIntersectionTrafficLightIndex(source, mismatchedGraph,
                intersection.Id, entry, 9));
            Assert.AreEqual(0, changes);
            Assert.IsTrue(backend.TrySetIntersectionTrafficLightIndex(source, published,
                intersection.Id, entry, 9));
            Assert.AreEqual(1, changes);
            Assert.AreEqual(1, backend.Intersections[Vector2Int.zero].TrafficLightIndices.Count);
            Assert.IsFalse(backend.TrySetIntersectionTrafficLightIndex(source, published,
                intersection.Id, entry, 8));
            Assert.AreEqual(1, changes);
            backend.SetIntersectionRuleType(Vector2Int.zero, IntersectionRuleType.FreeForAll);
            backend.SetIntersectionRuleType(Vector2Int.zero, IntersectionRuleType.TrafficLight);
            Assert.AreEqual(9, backend.Intersections[Vector2Int.zero].TrafficLightIndices[0].Index);
            Assert.IsEmpty(backend.Intersections[Vector2Int.zero].CustomRules);
        }
        finally { backend.OnRoadCellChanged -= changed; }
    }

    [Test]
    public void DeletingOnePathPreservesEveryOtherEntryAndPath()
    {
        var movement = IntersectionMovements(graph).First();
        Assert.IsTrue(IntersectionConnectionEditing.TryCreateRules(roads, graph, node.Id,
            movement.SourceLaneId, movement.TargetLaneId, false, out var rules));
        var changed = Compile(CreateRoads(rules));
        CollectionAssert.AreEquivalent(
            IntersectionMovements(graph).Where(m => m.Id != movement.Id).Select(m => m.Id),
            IntersectionMovements(changed).Select(m => m.Id));
    }

    [Test]
    public void AddingAnotherExitPreservesDefaultsAndRejectsDuplicate()
    {
        var source = graph.Lanes.First(l => l.EndAnchorCell == Vector2Int.zero && l.LaneOrdinal == 0);
        var target = graph.Lanes.First(l => l.StartAnchorCell == Vector2Int.zero &&
            l.StartLegDirectionBit != source.EndLegDirectionBit && l.LaneOrdinal == 1);
        Assert.IsTrue(IntersectionConnectionEditing.TryCreateRules(roads, graph, node.Id,
            source.Id, target.Id, true, out var rules));
        var changedRoads = CreateRoads(rules);
        var changed = Compile(changedRoads);
        Assert.AreEqual(IntersectionMovements(graph).Count() + 1, IntersectionMovements(changed).Count());
        foreach (var movement in IntersectionMovements(graph))
            Assert.IsTrue(changed.TryGetMovement(movement.Id, out _));
        Assert.IsFalse(IntersectionConnectionEditing.TryCreateRules(changedRoads, changed, node.Id,
            source.Id, target.Id, true, out _));
    }

    [Test]
    public void NewIncomingRoadReceivesDefaultsWithoutRestoringEditedEntries()
    {
        LaneRecord editedEntry = graph.Lanes.First(
            lane => lane.EndAnchorCell == Vector2Int.zero);
        List<LaneConnectionSource> originalSources =
            IntersectionConnectionEditing.CollectIncomingSources(graph, node);
        var retainedRules = IntersectionMovements(graph)
            .Where(movement => movement.SourceLaneId != editedEntry.Id)
            .Select(movement =>
            {
                graph.TryGetLane(movement.SourceLaneId, out LaneRecord source);
                graph.TryGetLane(movement.TargetLaneId, out LaneRecord target);
                return new LaneConnectionRule(
                    source.EndLegDirectionBit,
                    source.LaneOrdinal,
                    target.StartLegDirectionBit,
                    target.LaneOrdinal);
            })
            .ToArray();

        TrafficGraphSnapshot expanded = Compile(CreateRoads(
            retainedRules,
            sourceLanes: originalSources,
            includeSouthRoad: true));
        LaneRecord preservedEmptyEntry = expanded.Lanes.First(
            lane => lane.Id == editedEntry.Id);
        Assert.IsFalse(IntersectionMovements(expanded).Any(
            movement => movement.SourceLaneId == preservedEmptyEntry.Id),
            "An entry that the player cleared must remain empty.");

        LaneRecord newEntry = expanded.Lanes.First(
            lane => lane.EndAnchorCell == Vector2Int.zero &&
                    lane.EndLegDirectionBit == 16);
        Assert.IsTrue(IntersectionMovements(expanded).Any(
            movement => movement.SourceLaneId == newEntry.Id),
            "A newly connected incoming road must receive automatic turns.");
    }

    [Test]
    public void DeletingLastConnectionDoesNotRegenerateDefaultsAndCanBeReconnected()
    {
        var movement = IntersectionMovements(graph).First();
        graph.TryGetLane(movement.SourceLaneId, out var source);
        graph.TryGetLane(movement.TargetLaneId, out var target);
        var onlyRule = new LaneConnectionRule(source.EndLegDirectionBit, source.LaneOrdinal,
            target.StartLegDirectionBit, target.LaneOrdinal);
        var singleRoads = CreateRoads(new[] { onlyRule });
        var single = Compile(singleRoads);
        Assert.IsTrue(IntersectionConnectionEditing.TryCreateRules(singleRoads, single, node.Id,
            source.Id, target.Id, false, out var empty));
        Assert.IsEmpty(empty);
        var emptyRoads = CreateRoads(empty);
        var emptyGraph = Compile(emptyRoads);
        Assert.IsEmpty(IntersectionMovements(emptyGraph));
        Assert.AreEqual(node.IncomingLaneIds.Count, emptyGraph.ControlledNodes.Single().IncomingLaneIds.Count);
        Assert.IsTrue(IntersectionConnectionEditing.TryCreateRules(emptyRoads, emptyGraph, node.Id,
            source.Id, target.Id, true, out var restored));
        Assert.AreEqual(1, IntersectionMovements(Compile(CreateRoads(restored))).Count());
    }

    [Test]
    public void InvalidEndpointsUTurnsAndMismatchedSnapshotsAreRejected()
    {
        var entry = graph.Lanes.First(l => l.EndAnchorCell == Vector2Int.zero);
        var sameLeg = graph.Lanes.First(l => l.StartAnchorCell == Vector2Int.zero &&
            l.StartLegDirectionBit == entry.EndLegDirectionBit);
        Assert.IsFalse(IntersectionConnectionEditing.CanConnect(roads, graph, node.Id, entry.Id, sameLeg.Id));
        Assert.IsFalse(IntersectionConnectionEditing.CanConnect(roads, graph, node.Id, LaneId.Invalid, sameLeg.Id));
        Assert.IsFalse(IntersectionConnectionEditing.CanConnect(roads, graph, node.Id, sameLeg.Id, entry.Id));
        var movement = IntersectionMovements(graph).First();
        Assert.IsFalse(IntersectionConnectionEditing.CanConnect(CreateRoads(revision: 7), graph, node.Id,
            movement.SourceLaneId, movement.TargetLaneId));
        Assert.IsFalse(IntersectionConnectionEditing.TryCreateRules(roads, graph, node.Id,
            entry.Id, sameLeg.Id, false, out _));
    }

    [Test]
    public void BackendPublishesOneAuthoringChangeAndRejectsStaleRepeatClick()
    {
        backendObject = new GameObject("Intersection editing backend");
        var backend = backendObject.AddComponent<RoadSystemBackend>();
        var cells = (Dictionary<Vector2Int, RoadCell>)typeof(RoadSystemBackend)
            .GetField("_roads", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(backend);
        var cell = new RoadCell(Vector2Int.zero, 0, roadType);
        cell.AddConnection(1 | 4 | 64);
        cells.Add(Vector2Int.zero, cell);
        var initialRoads = CreateRoads(revision: backend.AuthoringRevision);
        var initialGraph = Compile(initialRoads);
        var movement = IntersectionMovements(initialGraph).First();
        int changes = 0;
        System.Action<Vector2Int> onChange = _ => changes++;
        backend.OnRoadCellChanged += onChange;
        try
        {
            Assert.IsTrue(backend.TrySetIntersectionConnection(initialRoads, initialGraph, node.Id,
                movement.SourceLaneId, movement.TargetLaneId, false));
            Assert.AreEqual(1, changes);
            Assert.IsTrue(backend.Intersections[Vector2Int.zero].UseExplicitConnections);
            Assert.IsTrue(
                backend.Intersections[Vector2Int.zero]
                    .HasExplicitSourceLaneBaseline);
            Assert.AreEqual(
                initialGraph.ControlledNodes.Single().IncomingLaneIds.Count,
                backend.Intersections[Vector2Int.zero]
                    .ExplicitSourceLanes.Count);
            Assert.IsFalse(backend.TrySetIntersectionConnection(initialRoads, initialGraph, node.Id,
                movement.SourceLaneId, movement.TargetLaneId, false));
            Assert.AreEqual(1, changes);
        }
        finally { backend.OnRoadCellChanged -= onChange; }
    }

    [TestCase(IntersectionRuleType.FreeForAll)]
    [TestCase(IntersectionRuleType.FIFO)]
    [TestCase(IntersectionRuleType.PriorityRoad)]
    [TestCase(IntersectionRuleType.TrafficLight)]
    public void ClearAllAndRestoreDefaultsReplaceEveryEntryAndPreserveControlSettings(
        IntersectionRuleType mode)
    {
        RoadSystemBackend backend = CreateConnectionResetBackend();
        IntersectionData data = backend.GetEditableIntersection(Vector2Int.zero);
        data.RuleType = mode;
        data.SetPriorityDirections(1, 64);
        data.TrafficLightCycleSeconds = 7f;
        data.SetTrafficLightIndex(1, 0, 9);
        var movement = IntersectionMovements(graph).First();
        graph.TryGetLane(movement.SourceLaneId, out var sourceLane);
        graph.TryGetLane(movement.TargetLaneId, out var targetLane);
        var custom = new LaneConnectionRule(sourceLane.EndLegDirectionBit, sourceLane.LaneOrdinal,
            targetLane.StartLegDirectionBit, targetLane.LaneOrdinal);
        data.ReplaceConnections(new[] { custom },
            IntersectionConnectionEditing.CollectIncomingSources(graph, node));
        data.AddDisabledRule(1, 0, 4, 0);
        var initialRoads = CreateRoads(revision: backend.AuthoringRevision, authoredPolicy: data);
        var initialGraph = Compile(initialRoads);
        int changes = 0;
        System.Action<Vector2Int> changed = _ => changes++;
        backend.OnRoadCellChanged += changed;
        try
        {
            Assert.IsTrue(backend.TryClearIntersectionConnections(initialRoads, initialGraph, node.Id));
            Assert.AreEqual(1, changes);
            Assert.IsTrue(data.UseExplicitConnections);
            Assert.IsTrue(data.HasExplicitSourceLaneBaseline);
            Assert.AreEqual(node.IncomingLaneIds.Count, data.ExplicitSourceLanes.Count);
            Assert.IsEmpty(data.CustomRules);
            Assert.IsEmpty(data.DisabledRules);
            Assert.IsFalse(backend.TryClearIntersectionConnections(initialRoads, initialGraph, node.Id));
            Assert.IsFalse(backend.TryRestoreDefaultIntersectionConnections(initialRoads, initialGraph, node.Id));
            Assert.AreEqual(1, changes);

            var clearedRoads = CreateRoads(revision: backend.AuthoringRevision, authoredPolicy: data);
            var clearedGraph = Compile(clearedRoads);
            Assert.IsEmpty(IntersectionMovements(clearedGraph));
            Assert.AreEqual(node.IncomingLaneIds.Count, clearedGraph.ControlledNodes.Single().IncomingLaneIds.Count);
            Assert.AreEqual(node.OutgoingLaneIds.Count, clearedGraph.ControlledNodes.Single().OutgoingLaneIds.Count);

            Assert.IsTrue(backend.TryRestoreDefaultIntersectionConnections(clearedRoads, clearedGraph, node.Id));
            Assert.AreEqual(2, changes);
            Assert.IsFalse(data.UseExplicitConnections);
            Assert.IsFalse(data.HasExplicitSourceLaneBaseline);
            Assert.IsEmpty(data.ExplicitSourceLanes);
            var restoredRoads = CreateRoads(revision: backend.AuthoringRevision, authoredPolicy: data);
            var restoredGraph = Compile(restoredRoads);
            CollectionAssert.AreEquivalent(
                IntersectionMovements(graph).Select(m => m.Id),
                IntersectionMovements(restoredGraph).Select(m => m.Id));
            Assert.IsTrue(backend.TryRestoreDefaultIntersectionConnections(restoredRoads, restoredGraph, node.Id));
            Assert.AreEqual(2, changes, "Restoring unchanged defaults should not rebuild again.");
            Assert.AreEqual(mode, data.RuleType);
            Assert.AreEqual(1, data.PriorityDirectionBitA);
            Assert.AreEqual(64, data.PriorityDirectionBitB);
            Assert.AreEqual(7f, data.TrafficLightCycleSeconds);
            Assert.AreEqual(9, data.TrafficLightIndices.Single().Index);
            Assert.AreEqual(node.GridPosition, restoredGraph.ControlledNodes.Single().GridPosition);
        }
        finally { backend.OnRoadCellChanged -= changed; }
    }

    [Test]
    public void ClearAllConnectionsPersistsAnEmptyPolicyStartingFromAutomaticDefaults()
    {
        RoadSystemBackend backend = CreateConnectionResetBackend();
        var source = CreateRoads(revision: backend.AuthoringRevision);
        var published = Compile(source);
        Assert.IsNotEmpty(IntersectionMovements(published));
        Assert.IsTrue(backend.TryClearIntersectionConnections(source, published, node.Id));
        IntersectionData data = backend.Intersections[Vector2Int.zero];
        Assert.IsTrue(data.UseExplicitConnections);
        Assert.IsTrue(data.HasExplicitSourceLaneBaseline);
        Assert.IsEmpty(IntersectionMovements(Compile(CreateRoads(
            revision: backend.AuthoringRevision, authoredPolicy: data))));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RestoringDefaultsClearsDisabledOverridesWithOneNotification(bool explicitConnections)
    {
        var data = new IntersectionData(Vector2Int.zero);
        if (explicitConnections)
            data.ReplaceConnections(new[] { new LaneConnectionRule(1, 0, 4, 0) },
                IntersectionConnectionEditing.CollectIncomingSources(graph, node));
        data.AddDisabledRule(1, 0, 4, 0);
        int changes = 0;
        System.Action<IntersectionData> changed = _ => changes++;
        data.Changed += changed;
        try
        {
            Assert.IsTrue(data.RestoreDefaultConnections());
            Assert.AreEqual(1, changes);
            Assert.IsEmpty(data.CustomRules);
            Assert.IsEmpty(data.DisabledRules);
            Assert.IsFalse(data.UseExplicitConnections);
            Assert.IsFalse(data.HasExplicitSourceLaneBaseline);
            Assert.IsEmpty(data.ExplicitSourceLanes);
            CollectionAssert.AreEquivalent(IntersectionMovements(graph).Select(m => m.Id),
                IntersectionMovements(Compile(CreateRoads(authoredPolicy: data))).Select(m => m.Id));
            Assert.IsFalse(data.RestoreDefaultConnections());
            Assert.AreEqual(1, changes);
        }
        finally { data.Changed -= changed; }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ConnectionResetRejectsInvalidAndMismatchedSnapshotsWithoutMutation(bool restoreDefaults)
    {
        RoadSystemBackend backend = CreateConnectionResetBackend();
        var source = CreateRoads(revision: backend.AuthoringRevision);
        var published = Compile(source);
        bool Reset(RoadNetworkSnapshot input, TrafficGraphSnapshot snapshot, ControlledNodeId id) =>
            restoreDefaults ? backend.TryRestoreDefaultIntersectionConnections(input, snapshot, id)
            : backend.TryClearIntersectionConnections(input, snapshot, id);
        int revision = backend.AuthoringRevision;
        Assert.IsFalse(Reset(null, published, node.Id));
        Assert.IsFalse(Reset(source, null, node.Id));
        Assert.IsFalse(Reset(source, published, ControlledNodeId.Invalid));
        Assert.IsFalse(Reset(source, Compile(CreateRoads(revision: revision + 1)), node.Id));
        Assert.IsFalse(Reset(CreateRoads(revision: revision + 1), published, node.Id));
        Assert.AreEqual(revision, backend.AuthoringRevision);
        Assert.IsEmpty(backend.Intersections);
    }

    private RoadSystemBackend CreateConnectionResetBackend()
    {
        backendObject = new GameObject("Intersection connection reset backend");
        var backend = backendObject.AddComponent<RoadSystemBackend>();
        var cells = (Dictionary<Vector2Int, RoadCell>)typeof(RoadSystemBackend)
            .GetField("_roads", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(backend);
        var cell = new RoadCell(Vector2Int.zero, 0, roadType);
        cell.AddConnection(1 | 4 | 64);
        cells.Add(Vector2Int.zero, cell);
        return backend;
    }

    [Test]
    public void RightClickBacksOutOneStageAndCompletionKeepsEntrySelected()
    {
        var session = new IntersectionBuilderSession();
        Assert.IsTrue(session.Focus(graph, node.Id));
        Assert.IsFalse(session.SelectEntry(graph, LaneId.Invalid));
        var entry = node.IncomingLaneIds[0];
        Assert.IsTrue(session.SelectEntry(graph, entry));
        Assert.AreEqual(IntersectionBuilderStage.Entry, session.Stage);
        Assert.IsTrue(session.SelectEntry(graph, entry));
        Assert.AreEqual(IntersectionBuilderStage.Drawing, session.Stage);
        session.CompleteConnection();
        Assert.AreEqual(entry, session.Entry);
        Assert.AreEqual(IntersectionBuilderStage.Entry, session.Stage);
        session.SelectEntry(graph, entry);
        session.Back();
        Assert.AreEqual(IntersectionBuilderStage.Entry, session.Stage);
        session.Back();
        Assert.AreEqual(IntersectionBuilderStage.Intersection, session.Stage);
        Assert.IsFalse(session.Entry.IsValid);
        session.Back();
        Assert.AreEqual(IntersectionBuilderStage.Overview, session.Stage);
        Assert.IsFalse(session.Intersection.IsValid);
    }

    [Test]
    public void GraphPublicationCancelsGestureAndMissingIntersectionClearsFocus()
    {
        var session = new IntersectionBuilderSession();
        session.Focus(graph, node.Id);
        session.SelectEntry(graph, node.IncomingLaneIds[0]);
        session.SelectEntry(graph, node.IncomingLaneIds[0]);
        session.Reconcile(Compile(CreateRoads()));
        Assert.AreEqual(IntersectionBuilderStage.Entry, session.Stage);
        session.Reconcile(null);
        Assert.AreEqual(IntersectionBuilderStage.Overview, session.Stage);
    }

    [Test]
    public void ResettingExplicitEmptyPolicyRestoresAutomaticConnections()
    {
        var data = new IntersectionData(Vector2Int.zero);
        data.ReplaceConnections(new LaneConnectionRule[0]);
        Assert.IsTrue(data.UseExplicitConnections);
        data.ClearCustomRules();
        Assert.IsFalse(data.UseExplicitConnections);
        Assert.IsNotEmpty(IntersectionMovements(Compile(CreateRoads())));
    }

    [Test]
    public void UnsupportedRoadMovementPolicyCannotBeOverriddenByTheBuilder()
    {
        roadType.supportedTrafficMovements = RoadMovementPolicyMask.LaneContinuation;
        Assert.IsTrue(roadType.TryCompileTrafficProfile(new TrafficDiagnosticCollection(), out profile));
        roads = CreateRoads();
        graph = Compile(roads);
        var entry = graph.Lanes.First(l => l.EndAnchorCell == Vector2Int.zero);
        var exit = graph.Lanes.First(l => l.StartAnchorCell == Vector2Int.zero &&
            l.StartLegDirectionBit != entry.EndLegDirectionBit);
        Assert.IsFalse(IntersectionConnectionEditing.CanConnect(roads, graph, node.Id, entry.Id, exit.Id));
    }

    [Test]
    public void CameraFocusCentersTiltedViewAndRestoresPositionAndZoom()
    {
        backendObject = new GameObject("Intersection camera test");
        backendObject.transform.position = new Vector3(5f, 20f, -10f);
        backendObject.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
        var camera = backendObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 15f;
        var controller = backendObject.AddComponent<CameraController>();
        controller.cam = camera;
        typeof(CameraController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(controller, null);
        Vector3 original = backendObject.transform.position;
        Vector3 center = new Vector3(12f, 2f, 8f);
        Assert.IsTrue(controller.FocusIntersection(center));
        Assert.AreEqual(original, backendObject.transform.position, "Focus must not snap on click.");
        var advance = typeof(CameraController).GetMethod("AdvanceIntersectionFocus",
            BindingFlags.Instance | BindingFlags.NonPublic);
        for (int i = 0; i < 180; i++) advance.Invoke(controller, new object[] { 1f / 60f });
        Vector3 screen = camera.WorldToViewportPoint(center);
        Assert.AreEqual(0.5f, screen.x, 0.001f);
        Assert.AreEqual(0.5f, screen.y, 0.001f);
        Assert.AreEqual(controller.intersectionZoom, camera.orthographicSize, 0.001f);
        controller.intersectionZoom = 7f;
        for (int i = 0; i < 180; i++) advance.Invoke(controller, new object[] { 1f / 60f });
        Assert.AreEqual(7f, camera.orthographicSize, 0.001f, "Zoom edits should apply during focus.");
        controller.ReleaseIntersectionFocus();
        for (int i = 0; i < 180; i++) advance.Invoke(controller, new object[] { 1f / 60f });
        Assert.Less(Vector3.Distance(original, backendObject.transform.position), 0.001f);
        Assert.AreEqual(15f, camera.orthographicSize, 0.001f);
        Assert.IsFalse(controller.IsIntersectionFocused);
    }

    [Test]
    public void BuilderAppearanceSanitizesInvalidDimensionsAndPreservesDesignerSizes()
    {
        var style = IntersectionBuilderStyle.Default;
        style.intersectionCircleRadius = 0.75f;
        style.laneCircleRadius = float.NaN;
        style.circleThicknessRatio = 3f;
        style.connectionLineWidth = -1f;
        style.heightOffset = float.PositiveInfinity;
        style.trafficLightIconOutwardOffset = float.NegativeInfinity;
        var validated = style.Validated();
        Assert.AreEqual(0.75f, validated.intersectionCircleRadius);
        Assert.AreEqual(IntersectionBuilderStyle.Default.laneCircleRadius, validated.laneCircleRadius);
        Assert.AreEqual(1f, validated.circleThicknessRatio);
        Assert.Greater(validated.connectionLineWidth, 0f);
        Assert.AreEqual(IntersectionBuilderStyle.Default.heightOffset, validated.heightOffset);
        Assert.AreEqual(IntersectionBuilderStyle.Default.trafficLightIconOutwardOffset,
            validated.trafficLightIconOutwardOffset);
        style.trafficLightIndexColorGradient = null;
        style.trafficLightIndexHoverLightness = float.NaN;
        var indexValidated = style.Validated();
        Assert.IsNotNull(indexValidated.trafficLightIndexColorGradient);
        Assert.AreEqual(0.25f, indexValidated.trafficLightIndexHoverLightness);
        Assert.AreEqual(validated, validated.Validated());
    }

    [Test]
    public void ConnectedExitResolvesTheSameMovementAsItsPathOnlyForTheSelectedEntry()
    {
        var movement = IntersectionMovements(graph).First();
        var session = new IntersectionBuilderSession();
        session.Focus(graph, node.Id);
        Assert.IsNull(session.FindEditableConnection(graph, movement.TargetLaneId));
        session.SelectEntry(graph, movement.SourceLaneId);
        Assert.AreEqual(movement.Id, session.FindEditableConnection(graph, movement.TargetLaneId).Id);
        Assert.IsNull(session.FindEditableConnection(graph, LaneId.Invalid));
        session.SelectEntry(graph, movement.SourceLaneId);
        Assert.IsNull(session.FindEditableConnection(graph, movement.TargetLaneId),
            "Drawing a new connection must not accidentally delete an existing one.");
    }

    [Test]
    public void PriorityRoadBackgroundPathsKeepTheirColorWithoutHoverAndOnlyInPriorityMode()
    {
        backendObject = new GameObject("Priority background paths test");
        var backend = backendObject.AddComponent<RoadSystemBackend>();
        var cells = (Dictionary<Vector2Int, RoadCell>)typeof(RoadSystemBackend)
            .GetField("_roads", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(backend);
        var cell = new RoadCell(Vector2Int.zero, 0, roadType);
        cell.AddConnection(1 | 4 | 64);
        cells.Add(Vector2Int.zero, cell);
        Assert.IsTrue(backend.SetIntersectionRuleType(Vector2Int.zero, IntersectionRuleType.PriorityRoad));
        Assert.IsTrue(backend.SetIntersectionPriorityDirections(Vector2Int.zero, 4, 64));
        var views = backendObject.AddComponent<PlayerViewModeManager>();
        views.SetTrafficView();
        var tool = backendObject.AddComponent<IntersectionBuilderTool>();
        var style = IntersectionBuilderStyle.Default;
        style.priorityRoadBackgroundPathColor = new Color(1f, 0f, 1f, 0.45f);
        style.showIntersectionArrows = false;
        tool.ApplyAppearance(style);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(IntersectionBuilderTool).GetField("views", flags).SetValue(tool, views);
        typeof(IntersectionBuilderTool).GetField("graph", flags).SetValue(tool, graph);
        typeof(IntersectionBuilderTool).GetField("roads", flags).SetValue(tool, roads);
        var session = (IntersectionBuilderSession)typeof(IntersectionBuilderTool)
            .GetField("session", flags).GetValue(tool);
        session.Focus(graph, node.Id);
        var vertexColors = (List<Color>)typeof(IntersectionBuilderTool)
            .GetField("colors", flags).GetValue(tool);
        void Append()
        {
            var root = new GameObject("Priority background root");
            root.transform.SetParent(backendObject.transform);
            typeof(IntersectionBuilderTool).GetField("visualRoot", flags).SetValue(tool, root);
            using (var iterator = (IEnumerator<int>)typeof(IntersectionBuilderTool)
                       .GetMethod("AppendVisuals", flags).Invoke(tool, null))
                while (iterator.MoveNext()) { }
        }
        void Clear() => typeof(IntersectionBuilderTool).GetMethod("ClearGeometry", flags).Invoke(tool, null);
        int PathVertices(System.Func<MovementRecord, bool> predicate) =>
            IntersectionMovements(graph).Where(predicate).Sum(movement =>
            {
                graph.TryGetGeometry(movement.GeometryId, out var geometry);
                return (geometry.Samples.Count - 1) * 4;
            });
        bool IsPriority(MovementRecord movement) =>
            movement.FromDirectionBit == 4 || movement.FromDirectionBit == 64;

        Append();
        Assert.AreEqual(PathVertices(IsPriority),
            vertexColors.Count(color => color == style.priorityRoadBackgroundPathColor));
        Assert.AreEqual(PathVertices(movement => !IsPriority(movement)),
            vertexColors.Count(color => color == style.backgroundPathColor));
        Clear();
        backend.SetIntersectionRuleType(Vector2Int.zero, IntersectionRuleType.FreeForAll);
        Append();
        Assert.IsFalse(vertexColors.Contains(style.priorityRoadBackgroundPathColor));
        Assert.AreEqual(PathVertices(_ => true),
            vertexColors.Count(color => color == style.backgroundPathColor));
        Clear();
        backend.SetIntersectionRuleType(Vector2Int.zero, IntersectionRuleType.PriorityRoad);
        LaneId entry = graph.Lanes.First(lane => lane.EndAnchorCell == Vector2Int.zero &&
            lane.EndLegDirectionBit == 4).Id;
        session.SelectEntry(graph, entry);
        Append();
        Assert.AreEqual(PathVertices(movement => movement.SourceLaneId == entry),
            vertexColors.Count(color => color == style.connectedColor));
        Assert.AreEqual(PathVertices(movement => IsPriority(movement) && movement.SourceLaneId != entry),
            vertexColors.Count(color => color == style.priorityRoadBackgroundPathColor));
    }

    [Test]
    public void BackgroundPathsStayVisibleAndHoverChangesOnlyTheHighlightedPath()
    {
        backendObject = new GameObject("Intersection path visuals test");
        var tool = backendObject.AddComponent<IntersectionBuilderTool>();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(IntersectionBuilderTool).GetField("graph", flags).SetValue(tool, graph);
        typeof(IntersectionBuilderTool).GetField("roads", flags).SetValue(tool, roads);
        var session = (IntersectionBuilderSession)typeof(IntersectionBuilderTool).GetField("session", flags).GetValue(tool);
        var pathList = (List<MovementRecord>)typeof(IntersectionBuilderTool).GetField("paths", flags).GetValue(tool);
        var vertexColors = (List<Color>)typeof(IntersectionBuilderTool).GetField("colors", flags).GetValue(tool);
        var style = IntersectionBuilderStyle.Default;
        style.hoveredMarkerColor = Color.magenta;
        tool.ApplyAppearance(style);
        session.Focus(graph, node.Id);
        void Append()
        {
            using (var iterator = (IEnumerator<int>)typeof(IntersectionBuilderTool)
                       .GetMethod("AppendVisuals", flags).Invoke(tool, null))
                while (iterator.MoveNext()) { }
        }
        int BackgroundVertices(LaneId selected) => IntersectionMovements(graph)
            .Where(m => m.SourceLaneId != selected).Sum(m =>
            {
                graph.TryGetGeometry(m.GeometryId, out var geometry);
                return (geometry.Samples.Count - 1) * 4;
            });
        Append();
        Assert.AreEqual(BackgroundVertices(LaneId.Invalid), vertexColors.Count(c => c == style.backgroundPathColor));
        Assert.IsEmpty(pathList);
        typeof(IntersectionBuilderTool).GetMethod("ClearGeometry", flags).Invoke(tool, null);
        session.SelectEntry(graph, node.IncomingLaneIds[0]);
        Append();
        Assert.AreEqual(BackgroundVertices(session.Entry), vertexColors.Count(c => c == style.backgroundPathColor));
        Assert.IsTrue(pathList.All(m => m.SourceLaneId == session.Entry));
        Assert.AreEqual(IntersectionMovements(graph).Count(m => m.SourceLaneId == session.Entry), pathList.Count);
        int laneVertices = vertexColors.Count(c => c == style.incomingLaneColor || c == style.outgoingLaneColor);

        var root = new GameObject("Hover visual root");
        root.transform.SetParent(backendObject.transform);
        typeof(IntersectionBuilderTool).GetField("visualRoot", flags).SetValue(tool, root);
        var hover = typeof(IntersectionBuilderTool).GetMethod("UpdateHoverTargets", flags);
        hover.Invoke(tool, new object[] { -1, pathList[0] });
        var filter = root.GetComponentInChildren<MeshFilter>();
        Assert.IsNotNull(filter);
        Assert.IsTrue(filter.sharedMesh.colors.All(c => c == style.hoveredPathColor));
        Assert.AreEqual(1, backendObject.GetComponentsInChildren<SpriteRenderer>()
            .Count(renderer => renderer.color == style.hoveredMarkerColor));
        var sameMesh = filter.sharedMesh;
        hover.Invoke(tool, new object[] { -1, pathList[0] });
        Assert.AreSame(sameMesh, filter.sharedMesh);
        hover.Invoke(tool, new object[] { -1, null });
        Assert.IsFalse(filter.gameObject.activeSelf);
        Assert.IsFalse(backendObject.GetComponentsInChildren<SpriteRenderer>()
            .Any(renderer => renderer.color == style.hoveredMarkerColor));

        style.showIntersectionArrows = false;
        tool.ApplyAppearance(style);
        Append();
        int expectedPathVertices = pathList.Sum(m =>
        {
            graph.TryGetGeometry(m.GeometryId, out var geometry);
            return (geometry.Samples.Count - 1) * 4;
        });
        Assert.AreEqual(expectedPathVertices, vertexColors.Count(c => c == style.connectedColor));
        Assert.AreEqual(laneVertices, vertexColors.Count(c => c == style.incomingLaneColor || c == style.outgoingLaneColor),
            "Disabling intersection arrows must leave road lane arrows intact.");
    }

    #if UNITY_EDITOR
    [Test]
    public void IndexDigitsStayCenteredOnTheirBackgroundWithTheGameFontAndAnOffsetSpritePivot()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/Fonts/ari-w9500-condensed-display.asset");
        Assert.IsNotNull(font);
        backendObject = new GameObject("Index glyph centering test");
        var backgroundObject = new GameObject("Offset pivot background", typeof(SpriteRenderer));
        backgroundObject.transform.SetParent(backendObject.transform);
        var background = backgroundObject.GetComponent<SpriteRenderer>();
        Sprite sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.zero);
        try
        {
            background.sprite = sprite;
            background.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            background.transform.localScale = Vector3.one * 0.22f;
            var labelObject = new GameObject("Index digit", typeof(TextMeshPro));
            labelObject.transform.SetParent(backendObject.transform);
            labelObject.transform.rotation = background.transform.rotation;
            var label = labelObject.GetComponent<TextMeshPro>();
            label.font = font;
            label.fontSize = 7f;
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.sizeDelta = Vector2.one * 0.22f;
            var centerLabel = typeof(IntersectionBuilderTool).GetMethod("CenterIndexLabel",
                BindingFlags.NonPublic | BindingFlags.Static);
            var center = new Vector3(2f, 0.185f, 3f);
            for (int index = 0; index <= 9; index++)
            {
                label.text = index.ToString();
                centerLabel.Invoke(null, new object[] { label, background, center, 0.11f });
                var digit = label.textInfo.characterInfo[0];
                Vector3[] glyphVertices = label.textInfo.meshInfo[digit.materialReferenceIndex].vertices;
                Vector3 renderedCenter = Vector3.zero;
                for (int corner = 0; corner < 4; corner++)
                    renderedCenter += label.transform.TransformPoint(glyphVertices[digit.vertexIndex + corner]);
                renderedCenter *= 0.25f;
                Assert.Less(Vector3.Distance(center, background.bounds.center), 0.0001f);
                Assert.Less(Vector3.Distance(background.bounds.center, renderedCenter),
                    0.0001f, $"Visible digit {index} must share its background's center.");
            }
        }
        finally { Object.DestroyImmediate(sprite); }
    }
    #endif

    [Test]
    public void MarkerImagesUseSeparateIntersectionEntryExitAndConnectedExitSprites()
    {
        backendObject = new GameObject("Intersection sprite test");
        var tool = backendObject.AddComponent<IntersectionBuilderTool>();
        var sprites = new Sprite[4];
        try
        {
            for (int i = 0; i < sprites.Length; i++)
                sprites[i] = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f);
            var style = IntersectionBuilderStyle.Default;
            style.intersectionSprite = sprites[0];
            style.entrySprite = sprites[1];
            style.exitSprite = sprites[2];
            style.connectedExitSprite = sprites[3];
            style.hoveredMarkerColor = Color.magenta;
            style.availableExitColor = Color.blue;
            style.laneCircleRadius = 0.2f;
            style.exitIconRadius = 0.4f;
            tool.ApplyAppearance(style);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(IntersectionBuilderTool).GetField("graph", flags).SetValue(tool, graph);
            typeof(IntersectionBuilderTool).GetField("roads", flags).SetValue(tool, roads);
            void CreateVisualRoot()
            {
                var root = new GameObject("Test marker root");
                root.transform.SetParent(backendObject.transform);
                typeof(IntersectionBuilderTool).GetField("visualRoot", flags).SetValue(tool, root);
            }
            void Append()
            {
                using (var iterator = (IEnumerator<int>)typeof(IntersectionBuilderTool)
                           .GetMethod("AppendVisuals", flags).Invoke(tool, null))
                    while (iterator.MoveNext()) { }
            }
            CreateVisualRoot();
            Append();
            var images = backendObject.GetComponentsInChildren<SpriteRenderer>();
            Assert.AreEqual(1, images.Length);
            Assert.AreSame(sprites[0], images[0].sprite);
            var updateHover = typeof(IntersectionBuilderTool).GetMethod("UpdateHoveredMarker", flags);
            updateHover.Invoke(tool, new object[] { 0 });
            Assert.AreEqual(style.hoveredMarkerColor, images[0].color);
            updateHover.Invoke(tool, new object[] { -1 });
            Assert.AreEqual(style.entryColor, images[0].color);
            typeof(IntersectionBuilderTool).GetMethod("ClearGeometry", flags).Invoke(tool, null);
            var session = (IntersectionBuilderSession)typeof(IntersectionBuilderTool).GetField("session", flags).GetValue(tool);
            session.Focus(graph, node.Id);
            session.SelectEntry(graph, node.IncomingLaneIds[0]);
            CreateVisualRoot();
            Append();
            images = backendObject.GetComponentsInChildren<SpriteRenderer>();
            int connectedExits = IntersectionMovements(graph)
                .Where(movement => movement.SourceLaneId == session.Entry)
                .Select(movement => movement.TargetLaneId)
                .Distinct()
                .Count();
            Assert.AreEqual(node.IncomingLaneIds.Count, images.Count(i => i.sprite == sprites[1]));
            Assert.AreEqual(node.OutgoingLaneIds.Count - connectedExits, images.Count(i => i.sprite == sprites[2]));
            Assert.AreEqual(connectedExits, images.Count(i => i.sprite == sprites[3]));
            Assert.IsTrue(images.Any(i => i.sprite == sprites[2] && i.color == style.availableExitColor));
            foreach (var image in images)
            {
                float diameter = image.sprite == sprites[1] ? 0.4f : 0.8f;
                float renderedSize = Mathf.Max(image.sprite.bounds.size.x * image.transform.lossyScale.x,
                    image.sprite.bounds.size.y * image.transform.lossyScale.y);
                Assert.AreEqual(diameter, renderedSize, 0.0001f);
                Assert.AreEqual(ShadowCastingMode.Off, image.shadowCastingMode);
                Assert.IsFalse(image.receiveShadows);
            }
            updateHover.Invoke(tool, new object[] { 0 });
            Assert.AreEqual(style.hoveredMarkerColor, images.Single(i => i.color == style.hoveredMarkerColor).color);
            updateHover.Invoke(tool, new object[] { -1 });
            Assert.IsFalse(images.Any(i => i.color == style.hoveredMarkerColor));
        }
        finally
        {
            foreach (var sprite in sprites) if (sprite != null) Object.DestroyImmediate(sprite);
        }
    }

    [Test]
    public void RebuildKeepsPublishedVisualsUntilReplacementIsReady()
    {
        backendObject = new GameObject("Intersection visual swap test");
        var tool = backendObject.AddComponent<IntersectionBuilderTool>();
        var oldRoot = new GameObject("Published intersection visuals");
        var oldMesh = new Mesh();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(IntersectionBuilderTool).GetField("visualRoot", flags).SetValue(tool, oldRoot);
        typeof(IntersectionBuilderTool).GetField("geometryReady", flags).SetValue(tool, true);
        var meshes = (List<Mesh>)typeof(IntersectionBuilderTool).GetField("meshes", flags).GetValue(tool);
        meshes.Add(oldMesh);

        typeof(IntersectionBuilderTool).GetMethod("PrepareRebuild", flags).Invoke(tool, null);

        Assert.IsTrue(oldRoot.activeSelf, "The published visuals must remain visible while rebuilding.");
        Assert.AreSame(oldRoot, typeof(IntersectionBuilderTool).GetField("retiringRoot", flags).GetValue(tool));
        Assert.IsFalse(oldMesh == null);

        var replacement = new GameObject("Replacement intersection visuals");
        replacement.SetActive(false);
        typeof(IntersectionBuilderTool).GetField("visualRoot", flags).SetValue(tool, replacement);
        typeof(IntersectionBuilderTool).GetMethod("CompleteRebuild", flags).Invoke(tool, null);

        Assert.IsTrue(replacement.activeSelf);
        Assert.IsTrue((bool)typeof(IntersectionBuilderTool).GetField("geometryReady", flags).GetValue(tool));
        Assert.IsTrue(oldRoot == null);
        Assert.IsTrue(oldMesh == null);
    }

    [Test]
    public void OpeningIntersectionTurnsTrafficNodesOffAndDoesNotRestoreTheToggle()
    {
        backendObject = new GameObject("Intersection view toggle test");
        backendObject.transform.position = new Vector3(0f, 20f, -10f);
        backendObject.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
        var camera = backendObject.AddComponent<Camera>();
        camera.orthographic = true;
        var controller = backendObject.AddComponent<CameraController>();
        var views = backendObject.AddComponent<PlayerViewModeManager>();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(CameraController).GetMethod("Awake", flags).Invoke(controller, null);
        typeof(PlayerViewModeManager).GetMethod("Awake", flags).Invoke(views, null);
        var tool = backendObject.AddComponent<IntersectionBuilderTool>();
        tool.Initialize(views, null, controller, null);
        // Disable rendering for this deterministic navigation check.
        tool.enabled = false;
        typeof(IntersectionBuilderTool).GetField("graph", flags).SetValue(tool, graph);
        typeof(IntersectionBuilderTool).GetField("roads", flags).SetValue(tool, roads);
        views.SetTrafficView();
        views.SetTrafficNodeView(true);
        Assert.IsTrue(tool.FocusIntersection(node.Id));
        Assert.IsFalse(views.TrafficNodeViewEnabled);
        var session = (IntersectionBuilderSession)typeof(IntersectionBuilderTool).GetField("session", flags).GetValue(tool);
        session.Back();
        typeof(IntersectionBuilderTool).GetMethod("ApplyFocus", flags).Invoke(tool, null);
        Assert.AreEqual(PlayerViewMode.Traffic, views.CurrentViewMode);
        Assert.IsFalse(views.TrafficNodeViewEnabled);
    }

    [TestCase(1f, 0f)]
    [TestCase(0f, 0.5f)]
    public void CameraInputInterruptsReturnWithoutSnappingOrResumingIt(float movement, float scroll)
    {
        backendObject = new GameObject("Interrupt camera return test");
        backendObject.transform.position = new Vector3(5f, 20f, -10f);
        backendObject.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
        var camera = backendObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 15f;
        var controller = backendObject.AddComponent<CameraController>();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(CameraController).GetMethod("Awake", flags).Invoke(controller, null);
        var advance = typeof(CameraController).GetMethod("AdvanceIntersectionFocus", flags);
        Assert.IsTrue(controller.FocusIntersection(new Vector3(15f, 0f, 12f)));
        Assert.IsFalse(controller.TryInterruptIntersectionReturn(Vector2.right, 1f),
            "Input should not unlock the camera while editing.");
        advance.Invoke(controller, new object[] { 0.5f });
        controller.ReleaseIntersectionFocus();
        Assert.IsFalse(controller.TryInterruptIntersectionReturn(Vector2.zero, 0f));
        advance.Invoke(controller, new object[] { 0.1f });
        Vector3 beforeInterrupt = backendObject.transform.position;
        float zoomBeforeInterrupt = camera.orthographicSize;
        Assert.IsTrue(controller.TryInterruptIntersectionReturn(new Vector2(movement, 0f), scroll));
        Assert.AreEqual(beforeInterrupt, backendObject.transform.position);
        Assert.AreEqual(zoomBeforeInterrupt, camera.orthographicSize);
        advance.Invoke(controller, new object[] { 1f });
        Assert.AreEqual(beforeInterrupt, backendObject.transform.position);
        Assert.AreEqual(zoomBeforeInterrupt, camera.orthographicSize);
        Assert.IsFalse(controller.TryInterruptIntersectionReturn(Vector2.right, 1f));
    }

    [Test]
    public void CurvedPreviewFollowsLaneTangentsAndMovesWithTheEndpoint()
    {
        Vector3 start = Vector3.zero;
        Vector3 end = new Vector3(2f, 1f, 2f);
        Vector3 Sample(float t) => TrafficMovementCurve.Evaluate(
            start, end, Vector3.forward, Vector3.right, t);
        Assert.AreEqual(start, Sample(0f));
        Assert.AreEqual(end, Sample(1f));
        Assert.Greater(Vector3.Dot((Sample(0.001f) - start).normalized, Vector3.forward), 0.99f);
        Assert.Greater(Vector3.Dot((end - Sample(0.999f)).normalized, Vector3.right), 0.99f);
        Assert.Greater(Vector3.Distance(Vector3.Lerp(start, end, 0.5f), Sample(0.5f)), 0.1f);
        Vector3 moved = TrafficMovementCurve.Evaluate(start, end + Vector3.right,
            Vector3.forward, Vector3.right, 0.5f);
        Assert.Greater(Vector3.Distance(moved, Sample(0.5f)), 0.1f);
        Assert.AreEqual(start, TrafficMovementCurve.Evaluate(start, start,
            Vector3.zero, Vector3.zero, 0.5f));
    }

    [Test]
    public void PreviewMatchesEverySampleOfCompiledTurnsIncludingNewConnections()
    {
        var samples = new List<Vector3>();
        foreach (var movement in IntersectionMovements(graph))
        {
            samples.Clear();
            Assert.IsTrue(IntersectionBuilderTool.TryAppendConnectionPreview(graph,
                movement.SourceLaneId, movement.TargetLaneId, samples));
            graph.TryGetGeometry(movement.GeometryId, out var geometry);
            Assert.AreEqual(geometry.Samples.Count, samples.Count);
            for (int i = 0; i < samples.Count; i++)
                Assert.Less(Vector3.Distance(geometry.Samples[i], samples[i]), 0.00001f);
        }
        var entry = graph.Lanes.First(l => l.EndAnchorCell == Vector2Int.zero && l.LaneOrdinal == 0);
        var exit = graph.Lanes.First(l => l.StartAnchorCell == Vector2Int.zero && l.LaneOrdinal == 1 &&
            l.StartLegDirectionBit != entry.EndLegDirectionBit);
        samples.Clear();
        Assert.IsTrue(IntersectionBuilderTool.TryAppendConnectionPreview(graph, entry.Id, exit.Id, samples));
        Assert.IsTrue(IntersectionConnectionEditing.TryCreateRules(roads, graph, node.Id,
            entry.Id, exit.Id, true, out var rules));
        var published = Compile(CreateRoads(rules));
        var added = IntersectionMovements(published).Single(m => m.SourceLaneId == entry.Id && m.TargetLaneId == exit.Id);
        published.TryGetGeometry(added.GeometryId, out var addedGeometry);
        Assert.AreEqual(addedGeometry.Samples.Count, samples.Count);
        for (int i = 0; i < samples.Count; i++)
            Assert.Less(Vector3.Distance(samples[i], addedGeometry.Samples[i]), 0.00001f,
                "Confirming an exit must preserve the exact preview shape.");
    }

    [Test]
    public void DuplicateLaneEndpointsDoNotReplaceTurnTangentsWithAStraightChord()
    {
        var entry = new[] { Vector3.back, Vector3.zero, Vector3.zero };
        var exit = new[] { Vector3.one, Vector3.one, Vector3.one + Vector3.right };
        var start = TrafficMovementCurve.SampleAtDistance(entry, 1f, out var incoming);
        var end = TrafficMovementCurve.SampleAtDistance(exit, 0f, out var outgoing);
        Assert.AreEqual(Vector3.forward, incoming);
        Assert.AreEqual(Vector3.right, outgoing);
        Assert.Greater(Vector3.Distance(TrafficMovementCurve.Evaluate(start, end, incoming, outgoing, 0.5f),
            Vector3.Lerp(start, end, 0.5f)), 0.1f);
    }

    [Test]
    public void ExistingAppearanceReceivesPathDefaultsWithoutLosingAuthoredCircleSizes()
    {
        var style = JsonUtility.FromJson<IntersectionBuilderStyle>(
            "{\"intersectionCircleRadius\":0.8,\"laneCircleRadius\":0.2,\"connectionLineWidth\":0.06}").Validated();
        Assert.AreEqual(0.8f, style.intersectionCircleRadius);
        Assert.AreEqual(0.2f, style.laneCircleRadius);
        Assert.AreEqual(0.2f, style.exitIconRadius);
        Assert.IsTrue(style.showIntersectionArrows);
        Assert.AreEqual(IntersectionBuilderStyle.Default.hoveredMarkerColor, style.hoveredMarkerColor);
        Assert.AreEqual(IntersectionBuilderStyle.Default.availableExitColor, style.availableExitColor);
        Assert.AreEqual(IntersectionBuilderStyle.Default.intersectionIconColor, style.intersectionIconColor);
        Assert.AreEqual(IntersectionBuilderStyle.Default.intersectionTypeIconScale, style.intersectionTypeIconScale);
        Assert.AreEqual(IntersectionBuilderStyle.Default.trafficLightIconRadius, style.trafficLightIconRadius);
        Assert.AreEqual(
            IntersectionBuilderStyle.Default.trafficLightPopOvershoot,
            style.trafficLightPopOvershoot);
        Assert.AreEqual(IntersectionBuilderStyle.Default.hoveredPathColor, style.hoveredPathColor);
        Assert.AreEqual(IntersectionBuilderStyle.Default.priorityRoadBackgroundPathColor,
            style.priorityRoadBackgroundPathColor);
        Assert.IsNotNull(style.trafficLightIndexColorGradient);
        Assert.AreEqual(0.25f, style.trafficLightIndexHoverLightness);
        Assert.Greater(style.backgroundPathColor.a, 0f);
        Assert.Less(style.backgroundPathColor.a, 1f);
        Assert.Less(style.backgroundPathWidth, style.connectionLineWidth);
        Assert.Greater(style.previewCurveStrength, 0f);
        Assert.AreEqual(style, style.Validated());
    }

    [Test]
    public void PathPickingClampsToEndpointsAndHandlesZeroLengthSegments()
    {
        Assert.AreEqual(1f, IntersectionBuilderTool.DistanceToSegment(new Vector2(2f, 0f),
            Vector2.zero, Vector2.right), 0.001f);
        Assert.AreEqual(1f, IntersectionBuilderTool.DistanceToSegment(new Vector2(0.5f, 1f),
            Vector2.zero, Vector2.right), 0.001f);
        Assert.AreEqual(1f, IntersectionBuilderTool.DistanceToSegment(Vector2.right,
            Vector2.zero, Vector2.zero), 0.001f);
    }

    private RoadNetworkSnapshot CreateRoads(
        IReadOnlyList<LaneConnectionRule> rules = null,
        int revision = 1,
        IReadOnlyList<LaneConnectionSource> sourceLanes = null,
        bool includeSouthRoad = false,
        IReadOnlyList<LaneTrafficLightIndex> signalIndices = null,
        IntersectionData authoredPolicy = null)
    {
        IntersectionPolicyRecord[] policies = null;
        if (authoredPolicy != null)
        {
            policies = new[] { new IntersectionPolicyRecord(
                node.Id, authoredPolicy.GridPosition, authoredPolicy.NodeKind, authoredPolicy.RuleType,
                authoredPolicy.PriorityDirectionBitA, authoredPolicy.PriorityDirectionBitB,
                authoredPolicy.TrafficLightCycleSeconds,
                authoredPolicy.CustomRules.Select(r => new LaneConnectionRuleRecord(
                    r.FromDirectionBit, r.FromLaneIndex, r.ToDirectionBit, r.ToLaneIndex)).ToArray(),
                authoredPolicy.DisabledRules.Select(r => new LaneConnectionRuleRecord(
                    r.FromDirectionBit, r.FromLaneIndex, r.ToDirectionBit, r.ToLaneIndex)).ToArray(),
                authoredPolicy.UseExplicitConnections,
                authoredPolicy.HasExplicitSourceLaneBaseline
                    ? authoredPolicy.ExplicitSourceLanes.Select(s =>
                        new LaneConnectionSourceRecord(s.DirectionBit, s.LaneIndex)).ToArray() : null,
                authoredPolicy.TrafficLightIndices, authoredPolicy.SmartTrafficLightWaitSeconds) };
        }
        else if (rules != null || signalIndices != null)
        {
            var records = rules?.Select(r => new LaneConnectionRuleRecord(r.FromDirectionBit,
                r.FromLaneIndex, r.ToDirectionBit, r.ToLaneIndex)).ToArray();
            LaneConnectionSourceRecord[] sourceRecords = sourceLanes == null
                ? null
                : sourceLanes.Select(source => new LaneConnectionSourceRecord(
                    source.DirectionBit,
                    source.LaneIndex)).ToArray();
            policies = new[] { new IntersectionPolicyRecord(
                ControlledNodeId.FromStableKey("CONTROLLED_NODE:0,0"), Vector2Int.zero,
                RoadNodeKind.Intersection, signalIndices != null ? IntersectionRuleType.TrafficLight :
                    IntersectionRuleType.FreeForAll, 0, 0, 30f,
                records, null, rules != null, sourceRecords, signalIndices) };
        }
        var cells = new List<RoadCellRecord>
        {
            Cell(new Vector2Int(-1, 0), 4, RoadNodeKind.RoadEnd),
            Cell(
                Vector2Int.zero,
                includeSouthRoad ? 1 | 4 | 16 | 64 : 1 | 4 | 64,
                RoadNodeKind.Intersection),
            Cell(new Vector2Int(1, 0), 64, RoadNodeKind.RoadEnd),
            Cell(new Vector2Int(0, 1), 16, RoadNodeKind.RoadEnd)
        };
        if (includeSouthRoad)
            cells.Add(Cell(new Vector2Int(0, -1), 1, RoadNodeKind.RoadEnd));
        return RoadNetworkSnapshot.CreateForCompilation(
            revision,
            0,
            1f,
            1f,
            cells,
            new[] { profile },
            intersectionPolicies: policies);
    }

    private RoadCellRecord Cell(Vector2Int position, int connections, RoadNodeKind kind) =>
        new RoadCellRecord(position, 0, connections, connections, connections, profile.Id,
            kind, new Vector3(position.x + 0.5f, 0f, position.y + 0.5f));

    private static IEnumerable<MovementRecord> IntersectionMovements(TrafficGraphSnapshot snapshot) =>
        snapshot.Movements.Where(m => m.Kind == TrafficMovementKind.Intersection);

    private static TrafficGraphSnapshot Compile(RoadNetworkSnapshot source)
    {
        Assert.IsTrue(TrafficGraphCompiler.TryCompile(source, new TrafficGraphVersion(1),
            out var result, out var diagnostics), string.Join("\n", diagnostics.Items.Select(d => d.Message)));
        Assert.IsFalse(diagnostics.HasErrors);
        return result;
    }
}
