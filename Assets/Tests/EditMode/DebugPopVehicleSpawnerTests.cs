using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class DebugPopVehicleSpawnerTests
{
    private readonly List<Object> cleanup = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        for (int i = cleanup.Count - 1; i >= 0; i--)
        {
            if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
        }
        cleanup.Clear();
    }

    [Test]
    public void NearestRoadNodeUsesClosestLegalLaneDeparture()
    {
        PopTransportVehicleData data = CreateVehicleData();
        var nearestButForbidden = new TrafficNode(Vector3.right);
        var nearestLegal = new TrafficNode(Vector3.right * 2f);
        var fartherLegal = new TrafficNode(Vector3.right * 8f);
        var end = new TrafficNode(Vector3.forward);

        var edges = new List<TrafficEdge>
        {
            Edge(nearestButForbidden, end, RoadPermissionMask.ServiceRoad),
            Edge(fartherLegal, end, RoadPermissionMask.GeneralRoad),
            Edge(nearestLegal, end, RoadPermissionMask.GeneralRoad)
        };

        Assert.IsTrue(DebugPopVehicleSpawner.TryFindNearestRoadNode(
            edges,
            Vector3.zero,
            data,
            out TrafficNode result));
        Assert.AreSame(nearestLegal, result);
    }

    [Test]
    public void ClosestFactorySelectsItsNearestCompatiblePopEntry()
    {
        PopTransportVehicleData data = CreateVehicleData();
        Building nearFactory = CreateBuilding<ResourceProductionBuildingData>(
            "Near Factory",
            new Vector2Int(2, 0));
        nearFactory.globalPorts[new Vector2Int(3, 0)] = PortType.PopEntry;
        nearFactory.globalPorts[new Vector2Int(2, 1)] = PortType.ResourceEntry;

        Building farFactory = CreateBuilding<ResourceProductionBuildingData>(
            "Far Factory",
            new Vector2Int(10, 0));
        farFactory.globalPorts[new Vector2Int(10, 0)] = PortType.PopEntry;

        Building nearbyHouse = CreateBuilding<PopHousingBuildingData>(
            "Nearby House",
            Vector2Int.zero);
        nearbyHouse.globalPorts[Vector2Int.zero] = PortType.PopEntry;

        Assert.IsTrue(DebugPopVehicleSpawner.TryFindClosestFactory(
            new[] { nearbyHouse, farFactory, nearFactory },
            Vector3.zero,
            data,
            1f,
            out Building result,
            out Vector2Int port));
        Assert.AreSame(nearFactory, result);
        Assert.AreEqual(new Vector2Int(3, 0), port);
    }

    [Test]
    public void DebugVehicleCarriesNoPopsAndIsMarkedOneWay()
    {
        PopTransportVehicleData data = CreateVehicleData();
        GameObject vehicleObject = Track(new GameObject("Debug Vehicle"));
        VehicleAI vehicle = vehicleObject.AddComponent<VehicleAI>();

        vehicle.InitializeDebugVehicle(data);

        Assert.IsTrue(vehicle.IsDebugVehicle);
        Assert.AreEqual(0, vehicle.AssignedPopCount);
        Assert.AreEqual(VehicleAI.VehicleState.Outbound, vehicle.currentState);
        Assert.IsNull(vehicle.homeBuilding);
    }

    [Test]
    public void DebugSpawnButtonOnlyAcceptsInputInNormalViewAndReflectsToggle()
    {
        GameObject root = Track(new GameObject("Debug UI"));
        PlayerViewModeManager views =
            root.AddComponent<PlayerViewModeManager>();
        InvokeLifecycle(views, "Awake");
        PlayerViewModeUI ui = root.AddComponent<PlayerViewModeUI>();

        GameObject normalButtonObject = Track(new GameObject(
            "Normal",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button)));
        normalButtonObject.transform.SetParent(root.transform);
        RectTransform normalRect =
            normalButtonObject.GetComponent<RectTransform>();
        normalRect.anchoredPosition = new Vector2(-100f, -68f);
        normalRect.sizeDelta = new Vector2(160f, 30f);
        GameObject trafficButtonObject = Track(new GameObject(
            "Traffic",
            typeof(RectTransform),
            typeof(Image),
            typeof(Button)));
        trafficButtonObject.transform.SetParent(root.transform);
        RectTransform trafficRect =
            trafficButtonObject.GetComponent<RectTransform>();
        trafficRect.anchoredPosition = new Vector2(-100f, -106f);
        trafficRect.sizeDelta = new Vector2(160f, 30f);
        SetField(ui, "viewModeManager", views);
        SetField(ui, "normalButton", normalButtonObject.GetComponent<Button>());
        SetField(ui, "trafficButton", trafficButtonObject.GetComponent<Button>());
        InvokeLifecycle(ui, "OnEnable");

        Button debugButton =
            (Button)GetField(ui, "debugPopVehicleButton");
        DebugPopVehicleSpawner spawner =
            root.GetComponent<DebugPopVehicleSpawner>();
        Assert.IsNotNull(debugButton);
        Assert.IsTrue(debugButton.gameObject.activeSelf);
        RectTransform debugRect = debugButton.GetComponent<RectTransform>();
        Assert.That(
            Mathf.Abs(debugRect.anchoredPosition.y -
                      trafficRect.anchoredPosition.y),
            Is.GreaterThanOrEqualTo(
                (debugRect.rect.height + trafficRect.rect.height) * 0.5f),
            "The debug toggle must not cover the Traffic view button.");
        StringAssert.Contains(
            "Off",
            debugButton.GetComponentInChildren<TMP_Text>().text);

        debugButton.onClick.Invoke();
        Assert.IsTrue(spawner.SpawnEnabled);
        StringAssert.Contains(
            "On",
            debugButton.GetComponentInChildren<TMP_Text>().text);

        views.SetTrafficView();
        Assert.IsFalse(debugButton.gameObject.activeSelf);
    }

    [Test]
    public void DestinationCandidatesSkipUnconnectedEntrancesAndIncludeOtherFactories()
    {
        var data = CreateVehicleData();
        Building near = CreateBuilding<ResourceProductionBuildingData>("Near", new Vector2Int(2, 0));
        Building far = CreateBuilding<ResourceProductionBuildingData>("Far", new Vector2Int(8, 0));
        near.globalPorts[new Vector2Int(2, 0)] = PortType.PopEntry;
        near.globalPorts[new Vector2Int(3, 0)] = PortType.PopEntry;
        far.globalPorts[new Vector2Int(8, 0)] = PortType.PopEntry;
        var anchors = new[]
        {
            Anchor(near, new Vector2Int(2, 0), false),
            Anchor(far, new Vector2Int(8, 0), true),
            Anchor(near, new Vector2Int(3, 0), true)
        };
        var candidates = DebugPopVehicleSpawner.FindConnectedFactoryDestinations(
            new[] { far, near }, Vector3.zero, data, 1f, anchors);
        Assert.AreEqual(2, candidates.Count);
        Assert.AreSame(near, candidates[0].Factory);
        Assert.AreEqual(new Vector2Int(3, 0), candidates[0].EntryPort,
            "A compatible port without an arrival anchor must not win by distance.");
        Assert.AreSame(far, candidates[1].Factory, "Keep other connected factories for routing fallback.");
        Assert.IsEmpty(DebugPopVehicleSpawner.FindConnectedFactoryDestinations(
            new[] { near, far }, Vector3.zero, data, 1f, null));
    }

    private static BuildingPortAnchorRecord Anchor(Building factory, Vector2Int port, bool connected) =>
        new BuildingPortAnchorRecord(BuildingPortAnchorId.FromStableKey("debug-test-" + port),
            factory.originCell, port, PortType.PopEntry,
            connected ? new[] { new LanePositionAnchorRecord(LaneId.FromStableKey("test-lane"), 0f) } : null,
            null);

    private PopTransportVehicleData CreateVehicleData()
    {
        PopTransportVehicleData data = Track(
            ScriptableObject.CreateInstance<PopTransportVehicleData>());
        data.vehicleName = "Debug test car";
        data.trafficCapabilities = VehicleCapabilityMask.GroundRoad;
        data.trafficRoadPermissions = RoadPermissionMask.GeneralRoad;
        return data;
    }

    private Building CreateBuilding<T>(string name, Vector2Int origin)
        where T : BuildingData
    {
        T data = Track(ScriptableObject.CreateInstance<T>());
        data.buildingName = name;
        GameObject buildingObject = Track(new GameObject(name));
        Building building = buildingObject.AddComponent<Building>();
        building.Initialize(data, origin, 0, new List<BuildingTile>());
        return building;
    }

    private static TrafficEdge Edge(
        TrafficNode start,
        TrafficNode end,
        RoadPermissionMask permission)
    {
        var edge = new TrafficEdge(start, end, 1f)
        {
            kind = TrafficEdgeKind.RoadLane,
            requiredPermissions = permission,
            requiredCapabilities = VehicleCapabilityMask.GroundRoad
        };
        start.outgoingEdges.Add(edge);
        return edge;
    }

    private T Track<T>(T value) where T : Object
    {
        cleanup.Add(value);
        return value;
    }

    private static void InvokeLifecycle(object target, string name)
    {
        target.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(target, null);
    }

    private static void SetField(object target, string name, object value)
    {
        target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);
    }

    private static object GetField(object target, string name)
    {
        return target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(target);
    }
}
