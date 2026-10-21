using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class TrafficRuntimeSafetyTests
{
    [TestCase(1 | 4)]
    [TestCase(2 | 4)]
    public void SharpWideRoadTurnIsClassifiedAsIntersection(int connections)
    {
        var backendObject = new GameObject("WideRoadIntersectionTest");
        RoadType wideRoad = ScriptableObject.CreateInstance<RoadType>();
        wideRoad.IsWide = true;

        try
        {
            RoadSystemBackend backend =
                backendObject.AddComponent<RoadSystemBackend>();
            var cell = new RoadCell(Vector2Int.zero, 0, wideRoad);
            AddConnections(cell, connections);

            Assert.AreEqual(
                RoadNodeKind.Intersection,
                backend.GetRoadNodeKind(cell));
        }
        finally
        {
            Object.DestroyImmediate(wideRoad);
            Object.DestroyImmediate(backendObject);
        }
    }

    [TestCase(4 | 64)]
    [TestCase(4 | 128)]
    public void StraightOrGentleWideRoadTurnRemainsThroughRoad(int connections)
    {
        var backendObject = new GameObject("WideRoadThroughTest");
        RoadType wideRoad = ScriptableObject.CreateInstance<RoadType>();
        wideRoad.IsWide = true;

        try
        {
            RoadSystemBackend backend =
                backendObject.AddComponent<RoadSystemBackend>();
            var cell = new RoadCell(Vector2Int.zero, 0, wideRoad);
            AddConnections(cell, connections);

            Assert.AreEqual(
                RoadNodeKind.ThroughRoad,
                backend.GetRoadNodeKind(cell));
        }
        finally
        {
            Object.DestroyImmediate(wideRoad);
            Object.DestroyImmediate(backendObject);
        }
    }

    [Test]
    public void StandardRoadRightAngleRemainsThroughRoad()
    {
        var backendObject = new GameObject("StandardRoadTurnTest");
        RoadType standardRoad = ScriptableObject.CreateInstance<RoadType>();

        try
        {
            RoadSystemBackend backend =
                backendObject.AddComponent<RoadSystemBackend>();
            var cell = new RoadCell(Vector2Int.zero, 0, standardRoad);
            AddConnections(cell, 1 | 4);

            Assert.AreEqual(
                RoadNodeKind.ThroughRoad,
                backend.GetRoadNodeKind(cell));
        }
        finally
        {
            Object.DestroyImmediate(standardRoad);
            Object.DestroyImmediate(backendObject);
        }
    }

    [Test]
    public void RoadUpgradeRequiresMatchingWideClassification()
    {
        var backendObject = new GameObject("WideRoadUpgradeTest");
        RoadType standardA = ScriptableObject.CreateInstance<RoadType>();
        RoadType standardB = ScriptableObject.CreateInstance<RoadType>();
        RoadType wideA = ScriptableObject.CreateInstance<RoadType>();
        RoadType wideB = ScriptableObject.CreateInstance<RoadType>();
        wideA.IsWide = true;
        wideB.IsWide = true;

        try
        {
            RoadSystemBackend backend =
                backendObject.AddComponent<RoadSystemBackend>();
            Dictionary<Vector2Int, RoadCell> roads = Roads(backend);
            Vector2Int standardCell = Vector2Int.zero;
            Vector2Int wideCell = Vector2Int.right;
            roads.Add(
                standardCell,
                new RoadCell(standardCell, 0, standardA));
            roads.Add(wideCell, new RoadCell(wideCell, 0, wideA));

            Assert.IsFalse(backend.CanUpgradeRoadBlock(standardCell, wideA));
            Assert.IsFalse(backend.CanUpgradeRoadBlock(wideCell, standardA));
            backend.UpgradeRoadBlock(standardCell, wideA);
            backend.UpgradeRoadBlock(wideCell, standardA);
            Assert.AreSame(standardA, roads[standardCell].roadType);
            Assert.AreSame(wideA, roads[wideCell].roadType);

            Assert.IsTrue(backend.CanUpgradeRoadBlock(standardCell, standardB));
            Assert.IsTrue(backend.CanUpgradeRoadBlock(wideCell, wideB));
            backend.UpgradeRoadBlock(standardCell, standardB);
            backend.UpgradeRoadBlock(wideCell, wideB);
            Assert.AreSame(standardB, roads[standardCell].roadType);
            Assert.AreSame(wideB, roads[wideCell].roadType);
        }
        finally
        {
            Object.DestroyImmediate(standardA);
            Object.DestroyImmediate(standardB);
            Object.DestroyImmediate(wideA);
            Object.DestroyImmediate(wideB);
            Object.DestroyImmediate(backendObject);
        }
    }

    [Test]
    public void RoadPlacementAllowsOnlyAuthoredVehiclePortBuildingCells()
    {
        var buildingBackendObject = new GameObject("BuildingSystemBackendTest");
        var roadBackendObject = new GameObject("RoadSystemBackendTest");
        var buildingObject = new GameObject("BuildingTest");
        BuildingData buildingData = ScriptableObject.CreateInstance<BuildingData>();
        RoadType roadType = ScriptableObject.CreateInstance<RoadType>();
        RoadType portBlockedRoadType = ScriptableObject.CreateInstance<RoadType>();
        RoadType portAllowedUpgrade = ScriptableObject.CreateInstance<RoadType>();
        roadType.CanConnectToPort = true;
        portBlockedRoadType.CanConnectToPort = false;
        portAllowedUpgrade.CanConnectToPort = true;

        try
        {
            BuildingSystemBackend buildingBackend =
                buildingBackendObject.AddComponent<BuildingSystemBackend>();
            RoadSystemBackend roadBackend =
                roadBackendObject.AddComponent<RoadSystemBackend>();
            Building building = buildingObject.AddComponent<Building>();
            var footprint = new List<BuildingTile>
            {
                new BuildingTile
                {
                    localPosition = Vector2Int.zero,
                    isPort = false,
                    portType = PortType.None
                },
                new BuildingTile
                {
                    localPosition = Vector2Int.right,
                    isPort = true,
                    portType = PortType.Both
                },
                new BuildingTile
                {
                    localPosition = Vector2Int.right * 2,
                    isPort = true,
                    portType = PortType.None
                }
            };

            building.Initialize(buildingData, Vector2Int.zero, 0, footprint);
            buildingBackend.RegisterBuilding(building, footprint);

            Assert.IsFalse(buildingBackend.CanBuildRoadAt(Vector2Int.zero));
            Assert.IsTrue(buildingBackend.CanBuildRoadAt(Vector2Int.right));
            Assert.IsFalse(buildingBackend.CanBuildRoadAt(Vector2Int.right * 2));
            Assert.IsTrue(buildingBackend.CanBuildRoadAt(Vector2Int.up));

            Assert.IsTrue(roadBackend.CanPlaceRoadTypeAt(
                Vector2Int.right,
                roadType));
            Assert.IsFalse(roadBackend.CanPlaceRoadTypeAt(
                Vector2Int.right,
                portBlockedRoadType));
            Assert.IsFalse(roadBackend.CanPlaceRoadTypeAt(
                Vector2Int.zero,
                roadType));
            Assert.IsTrue(roadBackend.CanPlaceRoadTypeAt(
                Vector2Int.up,
                portBlockedRoadType));

            roadBackend.ConnectCellsSafe(
                Vector2Int.zero,
                Vector2Int.up,
                roadType);

            Assert.IsEmpty(roadBackend.Roads);

            Dictionary<Vector2Int, RoadCell> roads = Roads(roadBackend);
            roads.Add(
                Vector2Int.right,
                new RoadCell(Vector2Int.right, 0, roadType));
            Assert.IsFalse(roadBackend.CanUpgradeRoadBlock(
                Vector2Int.right,
                portBlockedRoadType));
            Assert.IsTrue(roadBackend.CanUpgradeRoadBlock(
                Vector2Int.right,
                portAllowedUpgrade));
            roadBackend.UpgradeRoadBlock(
                Vector2Int.right,
                portBlockedRoadType);
            Assert.AreSame(roadType, roads[Vector2Int.right].roadType);
        }
        finally
        {
            Object.DestroyImmediate(portAllowedUpgrade);
            Object.DestroyImmediate(portBlockedRoadType);
            Object.DestroyImmediate(roadType);
            Object.DestroyImmediate(buildingData);
            Object.DestroyImmediate(buildingObject);
            Object.DestroyImmediate(roadBackendObject);
            Object.DestroyImmediate(buildingBackendObject);
        }
    }

    [Test]
    public void ClosestLaneQueryReturnsNearestCandidateInsteadOfFirstIndexed()
    {
        TrafficEdge farther = CreateRoadLane(
            1,
            new Vector3(0.8f, 0f, 0f),
            new Vector3(0.8f, 0f, 1f));
        TrafficEdge nearer = CreateRoadLane(
            2,
            new Vector3(0.2f, 0f, 0f),
            new Vector3(0.2f, 0f, 1f));
        var index = new TrafficSpatialIndex();
        index.Rebuild(new[] { farther, nearer }, 1f);

        TrafficEdge result = index.GetClosestLane(
            new Vector3(0f, 0f, 0.5f),
            1f);

        Assert.AreSame(nearer, result);
        Assert.AreEqual(2, index.LastDistanceTestCount);
    }

    [Test]
    public void InvalidClosestLaneQueryResetsLastQueryCounters()
    {
        TrafficEdge lane = CreateRoadLane(
            1,
            Vector3.zero,
            Vector3.forward);
        var index = new TrafficSpatialIndex();
        index.Rebuild(new[] { lane }, 1f);

        index.GetClosestLane(Vector3.zero, 1f);
        Assert.Greater(index.LastDistanceTestCount, 0);

        var results = new List<TrafficEdge>();
        index.QueryClosestLanes(Vector3.zero, -1f, 1, results);

        Assert.AreEqual(0, index.LastCandidateSegmentCount);
        Assert.AreEqual(0, index.LastDistanceTestCount);
        Assert.IsEmpty(results);
    }

    [TestCase(IntersectionRuleType.FreeForAll)]
    [TestCase(IntersectionRuleType.FIFO)]
    [TestCase(IntersectionRuleType.TrafficLight)]
    [TestCase(IntersectionRuleType.PriorityRoad)]
    public void MovingVehicleDoesNotReapplyReactionDelayAtIntersectionEntry(
        IntersectionRuleType rule)
    {
        using (var scenario = new IntersectionRestartScenario(rule))
        {
            scenario.EnterFollower(0.95f, 0.6f);
            scenario.EnterLeader(scenario.Exit, 8f, 0.6f);
            scenario.SetPendingReaction();

            scenario.Tick(0.1f);

            Assert.AreSame(scenario.Movement, scenario.Follower.currentEdge);
            Assert.Greater(scenario.Follower.conveyorDistanceOnEdge, 0f);
            Assert.GreaterOrEqual(scenario.Follower.conveyorCurrentSpeed, 0.6f);
            Assert.IsFalse(scenario.Follower.trafficWasBlocked);
            Assert.AreEqual(0f, scenario.Follower.trafficReleaseTime);
        }
    }

    [TestCase(IntersectionRuleType.FreeForAll)]
    [TestCase(IntersectionRuleType.FIFO)]
    [TestCase(IntersectionRuleType.TrafficLight)]
    [TestCase(IntersectionRuleType.PriorityRoad)]
    public void MovingVehicleDoesNotReapplyReactionDelayInsideIntersection(
        IntersectionRuleType rule)
    {
        using (var scenario = new IntersectionRestartScenario(rule))
        {
            scenario.EnterFollower(0.02f, 0.6f, true);
            scenario.SetPendingReaction();

            scenario.Tick(0.1f);

            Assert.AreSame(scenario.Movement, scenario.Follower.currentEdge);
            Assert.Greater(scenario.Follower.conveyorDistanceOnEdge, 0.02f);
            Assert.GreaterOrEqual(scenario.Follower.conveyorCurrentSpeed, 0.6f);
            Assert.IsFalse(scenario.Follower.trafficWasBlocked);
        }
    }

    [TestCase(IntersectionRuleType.FreeForAll, 0.85f)]
    [TestCase(IntersectionRuleType.FIFO, 0.85f)]
    [TestCase(IntersectionRuleType.TrafficLight, 0.85f)]
    [TestCase(IntersectionRuleType.PriorityRoad, 0.85f)]
    [TestCase(IntersectionRuleType.FreeForAll, 1f)]
    [TestCase(IntersectionRuleType.FIFO, 1f)]
    [TestCase(IntersectionRuleType.TrafficLight, 1f)]
    [TestCase(IntersectionRuleType.PriorityRoad, 1f)]
    public void StoppedVehicleWaitsBeforeRestartingEvenWithDistantLeader(
        IntersectionRuleType rule,
        float startDistance)
    {
        using (var scenario = new IntersectionRestartScenario(rule))
        {
            scenario.EnterFollower(startDistance, 0f);
            scenario.EnterLeader(scenario.Exit, 8f, 0.6f);
            scenario.SetPendingReaction();

            scenario.Tick(0.1f);

            Assert.AreSame(scenario.Approach, scenario.Follower.currentEdge);
            Assert.AreEqual(startDistance, scenario.Follower.conveyorDistanceOnEdge);
            Assert.AreEqual(0f, scenario.Follower.conveyorCurrentSpeed);
            Assert.IsTrue(scenario.Follower.trafficWasBlocked);
            Assert.AreEqual(float.MaxValue, scenario.Follower.trafficReleaseTime);
        }
    }

    [TestCase(IntersectionRuleType.FreeForAll)]
    [TestCase(IntersectionRuleType.FIFO)]
    [TestCase(IntersectionRuleType.TrafficLight)]
    [TestCase(IntersectionRuleType.PriorityRoad)]
    public void MovingVehicleStillStopsForOccupiedIntersectionEntry(
        IntersectionRuleType rule)
    {
        using (var scenario = new IntersectionRestartScenario(rule))
        {
            scenario.EnterFollower(0.8f, 0.6f);
            scenario.EnterLeader(scenario.Movement, 0.1f, 0f);
            scenario.SetPendingReaction();

            scenario.Tick(0.1f);

            Assert.AreSame(scenario.Approach, scenario.Follower.currentEdge);
            Assert.AreEqual(0.8f, scenario.Follower.conveyorDistanceOnEdge);
            Assert.AreEqual(0f, scenario.Follower.conveyorCurrentSpeed);
            Assert.IsTrue(scenario.Follower.trafficWasBlocked);
        }
    }

    [Test]
    public void MovingVehicleStillStopsAtRedLight()
    {
        using (var scenario = new IntersectionRestartScenario(IntersectionRuleType.TrafficLight))
        {
            scenario.EnterFollower(0.8f, 0.6f);
            scenario.SetPendingReaction();
            scenario.TurnLightRed();

            scenario.Tick(0.1f);

            Assert.AreSame(scenario.Approach, scenario.Follower.currentEdge);
            Assert.LessOrEqual(scenario.Follower.conveyorDistanceOnEdge, 0.8501f);
            Assert.Less(scenario.Follower.conveyorCurrentSpeed, 0.6f);
        }
    }

    private sealed class IntersectionRestartScenario : System.IDisposable
    {
        private readonly GameObject conveyorObject = new GameObject("Restart test conveyor");
        private readonly GameObject followerObject = new GameObject("Restart test follower");
        private readonly GameObject leaderObject = new GameObject("Restart test leader");
        private readonly PopTransportVehicleData data =
            ScriptableObject.CreateInstance<PopTransportVehicleData>();
        private readonly ConveyorTrafficManager conveyor;
        private readonly VehicleAI leader;
        private readonly IIntersectionController controller;

        public readonly VehicleAI Follower;
        public readonly TrafficEdge Approach = CreateRoadLane(1, Vector3.zero, Vector3.forward);
        public readonly TrafficEdge Movement = CreateRoadLane(2, Vector3.forward, Vector3.forward * 2f);
        public readonly TrafficEdge Exit = CreateRoadLane(3, Vector3.forward * 2f, Vector3.forward * 12f);

        public IntersectionRestartScenario(IntersectionRuleType rule)
        {
            try
            {
                conveyor = conveyorObject.AddComponent<ConveyorTrafficManager>();
                Follower = followerObject.AddComponent<VehicleAI>();
                leader = leaderObject.AddComponent<VehicleAI>();
                data.vehicleLengthUnits = 0.24f;
                data.minimumFollowingGapUnits = 0.1f;
                data.maximumVehicleSpeed = 1f;
                data.accelerationUnitsPerSecondSquared = 0.7f;
                data.decelerationUnitsPerSecondSquared = 0.5f;
                data.emergencyDecelerationUnitsPerSecondSquared = 0.5f;
                data.maximumJerkUnitsPerSecondCubed = 10f;
                Follower.vehicleData = data;
                leader.vehicleData = data;
                Movement.kind = TrafficEdgeKind.IntersectionMovement;
                Movement.fromDirectionBit = 1;
                Movement.fromLaneIndex = 0;
                switch (rule)
                {
                    case IntersectionRuleType.FIFO:
                        controller = new FIFOIntersectionController();
                        break;
                    case IntersectionRuleType.TrafficLight:
                        controller = new TrafficLightIntersectionController();
                        break;
                    case IntersectionRuleType.PriorityRoad:
                        controller = new PriorityRoadIntersectionController();
                        break;
                    default:
                        controller = new FreeForAllIntersectionController();
                        break;
                }
                var intersection = new IntersectionData(Vector2Int.zero) { RuleType = rule };
                intersection.SetPriorityDirections(1, 16);
                var crossMovement = CreateRoadLane(4, Vector3.left, Vector3.right);
                crossMovement.kind = TrafficEdgeKind.IntersectionMovement;
                crossMovement.fromDirectionBit = 4;
                crossMovement.fromLaneIndex = 0;
                controller.Initialize(Vector2Int.zero, intersection, new[] { Movement, crossMovement });
                Movement.exitController = controller;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void EnterFollower(float distance, float speed, bool insideIntersection = false)
        {
            var edges = insideIntersection
                ? new List<TrafficEdge> { Movement, Exit }
                : new List<TrafficEdge> { Approach, Movement, Exit };
            Enter(Follower, edges, distance, speed);
        }

        public void EnterLeader(TrafficEdge edge, float distance, float speed)
        {
            var edges = edge == Movement
                ? new List<TrafficEdge> { Movement, Exit }
                : new List<TrafficEdge> { Exit };
            Enter(leader, edges, distance, speed);
        }

        private void Enter(VehicleAI vehicle, List<TrafficEdge> edges, float distance, float speed)
        {
            Assert.IsTrue(conveyor.EnterRoute(vehicle, new TrafficRoute
            {
                ManagedEdges = edges,
                StartDistanceOnFirstEdge = distance,
                EndDistanceOnFinalEdge = Exit.totalLength
            }));
            vehicle.conveyorCurrentSpeed = speed;
            if (vehicle.currentEdge == Movement) controller.NotifyEntered(vehicle, Movement);
        }

        public void SetPendingReaction()
        {
            Follower.trafficWasBlocked = true;
            // Explicitly keep the reaction pending; no wall-clock waits or randomness.
            Follower.trafficReleaseTime = float.MaxValue;
        }

        public void TurnLightRed()
        {
            var signal = (TrafficLightIntersectionController)controller;
            signal.Tick(signal.LightDurationSeconds);
            Assert.IsFalse(signal.IsGreenForLane(1, 0));
        }

        public void Tick(float deltaSeconds)
        {
            typeof(ConveyorTrafficManager).GetMethod(
                "TickVehicle", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(conveyor, new object[] { Follower, deltaSeconds });
        }

        public void Dispose()
        {
            if (conveyor != null)
            {
                conveyor.LeaveTraffic(Follower);
                conveyor.LeaveTraffic(leader);
            }
            Object.DestroyImmediate(followerObject);
            Object.DestroyImmediate(leaderObject);
            Object.DestroyImmediate(conveyorObject);
            Object.DestroyImmediate(data);
        }
    }

    private static TrafficEdge CreateRoadLane(
        int edgeId,
        Vector3 start,
        Vector3 end)
    {
        var startNode = new TrafficNode(start);
        var endNode = new TrafficNode(end);
        var edge = new TrafficEdge(startNode, endNode, 1f)
        {
            edgeId = edgeId,
            kind = TrafficEdgeKind.RoadLane,
            waypoints = new List<Vector3> { start, end }
        };
        edge.RecalculateLength();
        startNode.outgoingEdges.Add(edge);
        return edge;
    }

    private static void AddConnections(RoadCell cell, int connections)
    {
        for (int i = 0; i < 8; i++)
        {
            int bit = 1 << i;
            if ((connections & bit) != 0) cell.AddConnection(bit);
        }
    }

    private static Dictionary<Vector2Int, RoadCell> Roads(
        RoadSystemBackend backend)
    {
        FieldInfo field = typeof(RoadSystemBackend).GetField(
            "_roads",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return (Dictionary<Vector2Int, RoadCell>)field.GetValue(backend);
    }
}
