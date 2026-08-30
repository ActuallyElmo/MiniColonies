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
