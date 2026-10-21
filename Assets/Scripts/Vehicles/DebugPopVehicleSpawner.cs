using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class DebugPopVehicleSpawner : MonoBehaviour
{
    private const float VehicleRoadClearance = 0.03f;
    private const float MinimumApproachSpeed = 1f;
    private const float ArrivalDistance = 0.01f;

    [SerializeField] private PopTransportVehicleData vehicleData;
    [SerializeField, Min(MinimumApproachSpeed)] private float approachSpeed = 4f;

    private bool inputAllowed = true;
    private readonly HashSet<VehicleAI> approachingVehicles =
        new HashSet<VehicleAI>();

    public bool SpawnEnabled { get; private set; }
    public event Action<bool> SpawnEnabledChanged;

    public void Configure(PopTransportVehicleData data)
    {
        if (data != null)
        {
            vehicleData = data;
        }
    }

    public void ToggleSpawning()
    {
        SetSpawnEnabled(!SpawnEnabled);
    }

    public void SetSpawnEnabled(bool enabled)
    {
        if (SpawnEnabled == enabled) return;
        SpawnEnabled = enabled;
        SpawnEnabledChanged?.Invoke(enabled);
    }

    public void SetInputAllowed(bool allowed)
    {
        inputAllowed = allowed;
    }

    private void Update()
    {
        if (!SpawnEnabled || !inputAllowed ||
            !PlayerViewModeManager.AllowsGameplayActions ||
            !Input.GetMouseButtonDown(0))
        {
            return;
        }

        if (EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        Camera camera = Camera.main;
        if (camera == null) return;

        int terrainLayer = LayerMask.NameToLayer("Terrain");
        if (terrainLayer < 0) return;

        Ray ray = camera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(
                ray,
                out RaycastHit hit,
                1000f,
                1 << terrainLayer,
                QueryTriggerInteraction.Ignore))
        {
            TrySpawnAtWorldPoint(hit.point);
        }
    }

    public bool TrySpawnAtWorldPoint(Vector3 worldPoint)
    {
        PopTransportVehicleData resolvedData = ResolveVehicleData();
        TrafficSystemBackend traffic = TrafficSystemBackend.Instance;
        if (resolvedData == null || resolvedData.vehiclePrefab == null)
        {
            Debug.LogWarning(
                "A debug pop vehicle could not spawn because no pop vehicle prefab is available.");
            return false;
        }

        if (traffic == null ||
            !TryFindNearestRoadNode(
                traffic.allEdges,
                worldPoint,
                resolvedData,
                out TrafficNode roadNode))
        {
            Debug.LogWarning(
                "A debug pop vehicle could not spawn because the traffic graph has no legal road node.");
            return false;
        }

        Vector3 spawnPosition = worldPoint + Vector3.up * VehicleRoadClearance;
        GameObject vehicleObject = Instantiate(
            resolvedData.vehiclePrefab,
            spawnPosition,
            Quaternion.identity);
        vehicleObject.name = $"Debug {resolvedData.vehicleName}";

        VehicleAI vehicle = vehicleObject.GetComponent<VehicleAI>();
        if (vehicle == null)
        {
            vehicle = vehicleObject.AddComponent<VehicleAI>();
        }

        vehicle.InitializeDebugVehicle(resolvedData);
        approachingVehicles.Add(vehicle);
        StartCoroutine(ApproachRoadAndDispatch(vehicle, roadNode));
        return true;
    }

    private IEnumerator ApproachRoadAndDispatch(
        VehicleAI vehicle,
        TrafficNode roadNode)
    {
        Vector3 roadPosition = roadNode.position +
                               Vector3.up * VehicleRoadClearance;
        float speed = Mathf.Max(
            MinimumApproachSpeed,
            vehicle.vehicleData != null
                ? vehicle.vehicleData.maximumVehicleSpeed
                : approachSpeed,
            approachSpeed);

        while (vehicle != null &&
               (vehicle.transform.position - roadPosition).sqrMagnitude >
               ArrivalDistance * ArrivalDistance)
        {
            Vector3 previous = vehicle.transform.position;
            Vector3 next = Vector3.MoveTowards(
                previous,
                roadPosition,
                speed * GameTimeManager.SimulationDeltaTime);
            Vector3 direction = next - previous;
            if (direction.sqrMagnitude > 0.000001f)
            {
                vehicle.transform.rotation = Quaternion.LookRotation(
                    direction.normalized,
                    Vector3.up);
            }

            vehicle.transform.position = next;
            yield return null;
        }

        if (vehicle == null)
        {
            approachingVehicles.Remove(vehicle);
            yield break;
        }
        vehicle.transform.position = roadPosition;

        approachingVehicles.Remove(vehicle);
        vehicle.BeginDebugFactoryTrip();
    }

    private void OnDestroy()
    {
        if (approachingVehicles.Count == 0) return;

        var vehicles = new List<VehicleAI>(approachingVehicles);
        approachingVehicles.Clear();
        for (int i = 0; i < vehicles.Count; i++)
        {
            if (vehicles[i] != null) vehicles[i].DestroyDebugVehicle();
        }
    }

    public static bool TryFindNearestRoadNode(
        IReadOnlyList<TrafficEdge> edges,
        Vector3 worldPoint,
        VehicleData data,
        out TrafficNode nearestNode)
    {
        nearestNode = null;
        if (edges == null || data == null) return false;

        var diagnostics = new TrafficDiagnosticCollection();
        if (!data.TryCompileTrafficProfile(diagnostics, out VehicleTrafficProfile profile))
        {
            return false;
        }

        float nearestDistanceSqr = float.MaxValue;
        for (int i = 0; i < edges.Count; i++)
        {
            TrafficEdge edge = edges[i];
            if (edge == null || edge.kind != TrafficEdgeKind.RoadLane ||
                edge.startNode == null || !IsLegal(edge, profile))
            {
                continue;
            }

            Vector3 offset = edge.startNode.position - worldPoint;
            offset.y = 0f;
            float distanceSqr = offset.sqrMagnitude;
            if (distanceSqr < nearestDistanceSqr)
            {
                nearestDistanceSqr = distanceSqr;
                nearestNode = edge.startNode;
            }
        }

        return nearestNode != null;
    }

    public static bool TryFindClosestFactory(
        IReadOnlyList<Building> buildings,
        Vector3 referencePosition,
        VehicleData data,
        float cellSize,
        out Building factory,
        out Vector2Int entryPort)
    {
        factory = null;
        entryPort = default;
        if (buildings == null || data == null) return false;

        cellSize = Mathf.Max(0.001f, cellSize);
        float closestFactoryDistanceSqr = float.MaxValue;
        for (int i = 0; i < buildings.Count; i++)
        {
            Building candidate = buildings[i];
            if (candidate == null ||
                !(candidate.data is ResourceProductionBuildingData))
            {
                continue;
            }

            Vector3 factoryPosition = CellCenter(candidate.originCell, cellSize);
            Vector3 factoryOffset = factoryPosition - referencePosition;
            factoryOffset.y = 0f;
            float factoryDistanceSqr = factoryOffset.sqrMagnitude;
            if (factoryDistanceSqr >= closestFactoryDistanceSqr) continue;

            bool foundPort = false;
            float closestPortDistanceSqr = float.MaxValue;
            Vector2Int closestPort = default;
            foreach (KeyValuePair<Vector2Int, PortType> port in candidate.globalPorts)
            {
                if (!port.Value.AllowsVehicleFlow(
                        PortFlowDirection.Entry,
                        data))
                {
                    continue;
                }

                Vector3 portPosition = CellCenter(port.Key, cellSize);
                Vector3 portOffset = portPosition - referencePosition;
                portOffset.y = 0f;
                float portDistanceSqr = portOffset.sqrMagnitude;
                if (portDistanceSqr < closestPortDistanceSqr)
                {
                    closestPortDistanceSqr = portDistanceSqr;
                    closestPort = port.Key;
                    foundPort = true;
                }
            }

            if (!foundPort) continue;
            closestFactoryDistanceSqr = factoryDistanceSqr;
            factory = candidate;
            entryPort = closestPort;
        }

        return factory != null;
    }

    public readonly struct FactoryDestination
    {
        public readonly Building Factory;
        public readonly Vector2Int EntryPort;
        public FactoryDestination(Building factory, Vector2Int entryPort)
        { Factory = factory; EntryPort = entryPort; }
    }

    public static List<FactoryDestination> FindConnectedFactoryDestinations(
        IReadOnlyList<Building> buildings, Vector3 referencePosition, VehicleData data,
        float cellSize, IReadOnlyList<BuildingPortAnchorRecord> anchors)
    {
        var result = new List<FactoryDestination>();
        if (buildings == null || data == null || anchors == null) return result;
        var connected = new HashSet<(Vector2Int, Vector2Int)>();
        for (int i = 0; i < anchors.Count; i++)
        {
            BuildingPortAnchorRecord anchor = anchors[i];
            if (anchor != null && anchor.ArrivalAnchors.Count > 0 &&
                anchor.PortType.AllowsVehicleFlow(PortFlowDirection.Entry, data))
                connected.Add((anchor.BuildingOriginCell, anchor.PortCell));
        }
        for (int i = 0; i < buildings.Count; i++)
        {
            Building factory = buildings[i];
            if (factory == null || !(factory.data is ResourceProductionBuildingData)) continue;
            foreach (var port in factory.globalPorts)
                if (port.Value.AllowsVehicleFlow(PortFlowDirection.Entry, data) &&
                    connected.Contains((factory.originCell, port.Key)))
                    result.Add(new FactoryDestination(factory, port.Key));
        }
        cellSize = Mathf.Max(0.001f, cellSize);
        referencePosition.y = 0f;
        result.Sort((a, b) =>
        {
            int order = (CellCenter(a.Factory.originCell, cellSize) - referencePosition).sqrMagnitude.CompareTo(
                (CellCenter(b.Factory.originCell, cellSize) - referencePosition).sqrMagnitude);
            if (order != 0) return order;
            order = (CellCenter(a.EntryPort, cellSize) - referencePosition).sqrMagnitude.CompareTo(
                (CellCenter(b.EntryPort, cellSize) - referencePosition).sqrMagnitude);
            if (order != 0) return order;
            order = a.EntryPort.x.CompareTo(b.EntryPort.x);
            return order != 0 ? order : a.EntryPort.y.CompareTo(b.EntryPort.y);
        });
        return result;
    }

    private PopTransportVehicleData ResolveVehicleData()
    {
        if (vehicleData != null && vehicleData.vehiclePrefab != null)
        {
            return vehicleData;
        }

        PopTransportVehicleData[] loaded =
            Resources.FindObjectsOfTypeAll<PopTransportVehicleData>();
        Array.Sort(loaded, (left, right) =>
            string.CompareOrdinal(left != null ? left.name : string.Empty,
                                  right != null ? right.name : string.Empty));
        for (int i = 0; i < loaded.Length; i++)
        {
            if (loaded[i] != null && loaded[i].vehiclePrefab != null)
            {
                vehicleData = loaded[i];
                return vehicleData;
            }
        }

        return null;
    }

    private static bool IsLegal(
        TrafficEdge edge,
        VehicleTrafficProfile profile)
    {
        return (edge.requiredPermissions == RoadPermissionMask.None ||
                (edge.requiredPermissions & profile.RoadPermissions) !=
                RoadPermissionMask.None) &&
               (edge.requiredCapabilities == VehicleCapabilityMask.None ||
                (edge.requiredCapabilities & profile.Capabilities) !=
                VehicleCapabilityMask.None);
    }

    private static Vector3 CellCenter(Vector2Int cell, float cellSize)
    {
        return new Vector3(
            (cell.x + 0.5f) * cellSize,
            0f,
            (cell.y + 0.5f) * cellSize);
    }
}
