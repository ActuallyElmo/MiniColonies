using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class Building : MonoBehaviour
{
    public enum VehicleAssignmentResult
    {
        Allowed,
        UnsupportedBuilding,
        MissingVehicleData,
        MissingVehiclePrefab,
        VehicleTypeNotAllowed,
        InsufficientSpace
    }

    public BuildingData data { get; private set; }
    public Vector2Int originCell { get; private set; }
    public int elevationLayer { get; private set; }

    [Header("Vehicle Management")]
    private readonly List<VehicleAI> _ownedVehicles = new List<VehicleAI>();

    public IReadOnlyList<VehicleAI> OwnedVehicles => _ownedVehicles;
    public event System.Action<Building> VehicleOwnershipChanged;

    [Header("Network Data")]
    public Dictionary<Vector2Int, RoadNetwork> portNetworks = new Dictionary<Vector2Int, RoadNetwork>();
    public HashSet<RoadNetwork> validNetworks = new HashSet<RoadNetwork>();

    public HashSet<Vector2Int> occupiedCells = new HashSet<Vector2Int>();
    public Dictionary<Vector2Int, PortType> globalPorts = new Dictionary<Vector2Int, PortType>();

    private List<GameObject> portIcons = new List<GameObject>();
    private bool _needsPathRecalculation = false;
    private Coroutine _routeRecalculationCoroutine;
    private Coroutine _trafficBackendSubscriptionCoroutine;
    private bool _subscribedToTrafficReady;
    private bool _suppressVehicleDispatchRequests;

    public class RouteData
    {
        public Building targetFactory;
        public RoadNetwork sharedNetwork;
        public int reservedPopCount;
        public float score;
    }

    private const float PopDeliveryEfficiencyPower = 2f;
    private readonly Dictionary<VehicleAI, int> _incomingPopReservations = new Dictionary<VehicleAI, int>();
    private readonly Dictionary<VehicleAI, int> _deliveredPopAssignments = new Dictionary<VehicleAI, int>();

    public int ReservedIncomingPops { get; private set; }
    public int DeliveredPops { get; private set; }

    public int GetTotalPopTransportVehicleCount()
    {
        int count = 0;
        for (int i = 0; i < _ownedVehicles.Count; i++)
        {
            VehicleAI vehicle = _ownedVehicles[i];
            if (vehicle != null && vehicle.vehicleData is PopTransportVehicleData)
            {
                count++;
            }
        }

        return count;
    }

    public int GetSentPopTransportVehicleCount()
    {
        int count = 0;
        for (int i = 0; i < _ownedVehicles.Count; i++)
        {
            VehicleAI vehicle = _ownedVehicles[i];
            if (vehicle != null &&
                vehicle.vehicleData is PopTransportVehicleData &&
                !vehicle.CanReceivePopTransportAssignment)
            {
                count++;
            }
        }

        return count;
    }

    public int GetTotalPopsSentByOwnedVehicles()
    {
        int totalPops = 0;
        for (int i = 0; i < _ownedVehicles.Count; i++)
        {
            VehicleAI vehicle = _ownedVehicles[i];
            if (vehicle != null &&
                vehicle.vehicleData is PopTransportVehicleData)
            {
                totalPops += Mathf.Max(0, vehicle.AssignedPopCount);
            }
        }

        return totalPops;
    }

    public int GetTotalPopCapacityOfOwnedVehicles()
    {
        int totalCapacity = 0;
        for (int i = 0; i < _ownedVehicles.Count; i++)
        {
            VehicleAI vehicle = _ownedVehicles[i];
            if (vehicle != null &&
                vehicle.vehicleData is PopTransportVehicleData vehicleData)
            {
                totalCapacity += Mathf.Max(
                    0,
                    vehicleData.amountOfPopsTransported);
            }
        }

        return totalCapacity;
    }

    public int GetAssignedPopTransportVehicleCount()
    {
        return _incomingPopReservations.Count + _deliveredPopAssignments.Count;
    }

    public int GetAssignedPopCount()
    {
        return ReservedIncomingPops + DeliveredPops;
    }

    public int GetVehicleSpaceCapacity()
    {
        return data is PopHousingBuildingData housingData
            ? Mathf.Max(0, housingData.popTransportVehiclesStorageSpace)
            : 0;
    }

    public int GetUsedVehicleSpace()
    {
        int usedSpace = 0;
        for (int i = 0; i < _ownedVehicles.Count; i++)
        {
            VehicleAI vehicle = _ownedVehicles[i];
            if (vehicle != null)
            {
                usedSpace += GetVehicleSpaceCost(vehicle.vehicleData);
            }
        }

        return usedSpace;
    }

    public int GetAvailableVehicleSpace()
    {
        return Mathf.Max(0, GetVehicleSpaceCapacity() - GetUsedVehicleSpace());
    }

    public int GetOwnedVehicleCount()
    {
        int count = 0;
        for (int i = 0; i < _ownedVehicles.Count; i++)
        {
            if (_ownedVehicles[i] != null)
            {
                count++;
            }
        }

        return count;
    }

    public bool CanAssignVehicle(
        PopTransportVehicleData vehicleData,
        out string failureReason)
    {
        VehicleAssignmentResult result =
            GetVehicleAssignmentResult(vehicleData);

        switch (result)
        {
            case VehicleAssignmentResult.Allowed:
                failureReason = null;
                return true;

            case VehicleAssignmentResult.UnsupportedBuilding:
                failureReason =
                    "Only population housing can own population " +
                    "transport vehicles.";
                return false;

            case VehicleAssignmentResult.MissingVehicleData:
                failureReason = "No vehicle type was provided.";
                return false;

            case VehicleAssignmentResult.MissingVehiclePrefab:
                failureReason = $"{vehicleData.name} has no vehicle prefab.";
                return false;

            case VehicleAssignmentResult.VehicleTypeNotAllowed:
                var housingData = (PopHousingBuildingData)data;
                failureReason =
                    $"{vehicleData.name} is not allowed by " +
                    $"{housingData.buildingName}.";
                return false;

            case VehicleAssignmentResult.InsufficientSpace:
                int requiredSpace = GetVehicleSpaceCost(vehicleData);
                int availableSpace = GetAvailableVehicleSpace();
                failureReason =
                    $"The vehicle requires {requiredSpace} space, but only " +
                    $"{availableSpace} is available.";
                return false;

            default:
                failureReason = "The vehicle cannot be assigned.";
                return false;
        }
    }

    public VehicleAssignmentResult GetVehicleAssignmentResult(
        PopTransportVehicleData vehicleData)
    {
        if (!(data is PopHousingBuildingData housingData))
        {
            return VehicleAssignmentResult.UnsupportedBuilding;
        }

        if (vehicleData == null)
        {
            return VehicleAssignmentResult.MissingVehicleData;
        }

        if (vehicleData.vehiclePrefab == null)
        {
            return VehicleAssignmentResult.MissingVehiclePrefab;
        }

        if (housingData.typesOfPopTransportVehiclesAllowed == null ||
            !housingData.typesOfPopTransportVehiclesAllowed.Contains(vehicleData))
        {
            return VehicleAssignmentResult.VehicleTypeNotAllowed;
        }

        return GetVehicleSpaceCost(vehicleData) > GetAvailableVehicleSpace()
            ? VehicleAssignmentResult.InsufficientSpace
            : VehicleAssignmentResult.Allowed;
    }

    public bool TryAssignVehicle(
        PopTransportVehicleData vehicleData,
        out VehicleAI vehicle)
    {
        vehicle = null;
        if (!CanAssignVehicle(vehicleData, out string failureReason))
        {
            Debug.LogWarning(
                $"Could not assign vehicle to {DescribeBuildingForDispatch()}: " +
                failureReason,
                this);
            return false;
        }

        GameObject vehicleObject = Instantiate(
            vehicleData.vehiclePrefab,
            transform.position,
            Quaternion.identity,
            transform);
        vehicleObject.SetActive(false);

        vehicle = vehicleObject.GetComponent<VehicleAI>();
        if (vehicle == null)
        {
            vehicle = vehicleObject.AddComponent<VehicleAI>();
        }

        vehicle.Initialize(this, vehicleData);
        _ownedVehicles.Add(vehicle);
        VehicleOwnershipChanged?.Invoke(this);
        if (!_suppressVehicleDispatchRequests)
        {
            RequestPopTransportDispatch();
        }
        return true;
    }

    public bool TryRemoveOwnedVehicle(VehicleAI vehicle)
    {
        if (vehicle == null ||
            vehicle.homeBuilding != this ||
            !_ownedVehicles.Contains(vehicle))
        {
            return false;
        }

        vehicle.RecallAndDeactivate();
        _ownedVehicles.Remove(vehicle);
        VehicleOwnershipChanged?.Invoke(this);
        Destroy(vehicle.gameObject);

        if (BuildingSystemBackend.Instance != null)
        {
            BuildingSystemBackend.Instance.RequestPopTransportDispatch();
        }

        return true;
    }

    public bool TryRemoveOwnedVehicle(PopTransportVehicleData vehicleData)
    {
        if (vehicleData == null) return false;

        VehicleAI dispatchedMatch = null;
        for (int i = 0; i < _ownedVehicles.Count; i++)
        {
            VehicleAI candidate = _ownedVehicles[i];
            if (candidate == null || candidate.vehicleData != vehicleData)
            {
                continue;
            }

            if (candidate.CanReceivePopTransportAssignment)
            {
                return TryRemoveOwnedVehicle(candidate);
            }

            if (dispatchedMatch == null)
            {
                dispatchedMatch = candidate;
            }
        }

        return dispatchedMatch != null &&
               TryRemoveOwnedVehicle(dispatchedMatch);
    }

    public void Initialize(BuildingData buildingData, Vector2Int origin, int layer, List<BuildingTile> activeFootprint)
    {
        data = buildingData;
        originCell = origin;
        elevationLayer = layer;

        foreach (var tile in activeFootprint)
        {
            Vector2Int globalPos = origin + tile.localPosition;
            occupiedCells.Add(globalPos);

            if (tile.isPort)
            {
                globalPorts.Add(globalPos, tile.portType);
            }
        }

        // Preserve the current testing behavior, but route it through the same
        // capacity-checked ownership API that future player UI will call.
        if (data is PopHousingBuildingData popData &&
            popData.autoFillVehicleSpaceForTesting)
        {
            _suppressVehicleDispatchRequests = true;
            try
            {
                PopTransportVehicleData testVehicleData =
                    popData.testingAutoFillVehicle;
                if (testVehicleData == null &&
                    popData.typesOfPopTransportVehiclesAllowed != null &&
                    popData.typesOfPopTransportVehiclesAllowed.Count > 0)
                {
                    testVehicleData =
                        popData.typesOfPopTransportVehiclesAllowed[0];
                }

                while (testVehicleData != null &&
                       CanAssignVehicle(testVehicleData, out _))
                {
                    if (!TryAssignVehicle(testVehicleData, out _))
                    {
                        break;
                    }
                }
            }
            finally
            {
                _suppressVehicleDispatchRequests = false;
            }
        }

        bool wasAlreadySubscribed = _subscribedToTrafficReady;
        TrySubscribeToTrafficReady();
        if (wasAlreadySubscribed && HasPublishedTrafficNetwork())
        {
            HandleTrafficReady();
        }
    }

    private static int GetVehicleSpaceCost(VehicleData vehicleData)
    {
        return vehicleData is PopTransportVehicleData popVehicleData
            ? Mathf.Max(1, popVehicleData.vehicleSpaceUsed)
            : 0;
    }

    public void RegisterPortIcon(GameObject icon)
    {
        portIcons.Add(icon);
        icon.SetActive(false); 
    }

    public void SetPortIconsVisibility(bool isVisible)
    {
        foreach (var icon in portIcons)
        {
            if (icon != null) icon.SetActive(isVisible);
        }
    }

    public Vector2Int GetClosestPort(PortType desiredFlow, RoadNetwork targetNetwork, Vector2Int referenceCell)
    {
        PortFlowDirection flowDirection = desiredFlow == PortType.Exit
            ? PortFlowDirection.Exit
            : PortFlowDirection.Entry;
        return TryGetClosestPort(flowDirection, targetNetwork, referenceCell, null, out Vector2Int port)
            ? port
            : originCell;
    }

    public bool TryGetClosestPort(
        PortFlowDirection desiredFlow,
        RoadNetwork targetNetwork,
        Vector2Int referenceCell,
        VehicleData vehicleData,
        out Vector2Int bestPort)
    {
        bestPort = originCell;
        float minSqrDist = float.MaxValue;
        bool found = false;

        foreach (var kvp in globalPorts)
        {
            if (kvp.Value.AllowsVehicleFlow(desiredFlow, vehicleData))
            {
                if (portNetworks.TryGetValue(kvp.Key, out RoadNetwork net) && net == targetNetwork)
                {
                    float sqrDist = (kvp.Key - referenceCell).sqrMagnitude;
                    if (sqrDist < minSqrDist)
                    {
                        minSqrDist = sqrDist;
                        bestPort = kvp.Key;
                        found = true;
                    }
                }
            }
        }
        
        if (!found)
        {
            Debug.LogWarning(
                $"Building {data.buildingName} could not find a {desiredFlow} port for {DescribeVehicleForPorts(vehicleData)} on network!");
        }
        return found;
    }

    // --- UPDATED AI EVENT LISTENERS ---
    
    private void OnEnable()
    {
        TrySubscribeToTrafficReady();
        if (!_subscribedToTrafficReady && _trafficBackendSubscriptionCoroutine == null)
        {
            _trafficBackendSubscriptionCoroutine = StartCoroutine(WaitForTrafficBackendSubscription());
        }
    }

    private void OnDisable()
    {
        if (_trafficBackendSubscriptionCoroutine != null)
        {
            StopCoroutine(_trafficBackendSubscriptionCoroutine);
            _trafficBackendSubscriptionCoroutine = null;
        }

        if (_subscribedToTrafficReady && TrafficSystemBackend.Instance != null)
        {
            TrafficSystemBackend.Instance.OnTrafficNetworkReady -= HandleTrafficReady;
        }

        _subscribedToTrafficReady = false;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _ownedVehicles.Count; i++)
        {
            VehicleAI vehicle = _ownedVehicles[i];
            if (vehicle != null)
            {
                vehicle.RecallAndDeactivate();
            }
        }

        _ownedVehicles.Clear();
    }

    public void OnNetworksUpdated()
    {
        _needsPathRecalculation = true; 
    }

    public void RequestPopTransportDispatch()
    {
        if (!(data is PopHousingBuildingData) || !isActiveAndEnabled)
        {
            return;
        }

        _needsPathRecalculation = true;
        if (_routeRecalculationCoroutine == null)
        {
            _routeRecalculationCoroutine =
                StartCoroutine(StaggeredRouteRecalculation());
        }
    }

    private void HandleTrafficReady()
    {
        if (!isActiveAndEnabled || data == null) return;

        bool shouldRecalculate = _needsPathRecalculation || data is PopHousingBuildingData;
        if (!shouldRecalculate) return;
        _needsPathRecalculation = false;
        
        if (_routeRecalculationCoroutine != null)
        {
            StopCoroutine(_routeRecalculationCoroutine);
        }
        _routeRecalculationCoroutine = StartCoroutine(StaggeredRouteRecalculation());
    }

    private System.Collections.IEnumerator StaggeredRouteRecalculation()
    {
        _needsPathRecalculation = false;

        // Stagger the requests so hundreds of buildings don't choke the frame
        int randomFrameDelay = Random.Range(0, 60);
        for (int i = 0; i < randomFrameDelay; i++) yield return null;

        if (data is PopHousingBuildingData)
        {
            VehicleData firstVehicleData = GetFirstSpawnedVehicleData();
            RouteData bestRoute = FindBestFactoryRoute(firstVehicleData);

            if (bestRoute == null && HasAssignablePopTransportVehicle())
            {
                Debug.LogWarning(
                    $"{DescribeBuildingForDispatch()} could not dispatch vehicles to factories. {DescribeFactoryRouteFailure(firstVehicleData)}");
            }
            
            VehicleAI[] ownedVehicleSnapshot =
                _ownedVehicles.ToArray();
            for (int vehicleIndex = 0;
                 vehicleIndex < ownedVehicleSnapshot.Length;
                 vehicleIndex++)
            {
                VehicleAI vAI = ownedVehicleSnapshot[vehicleIndex];
                if (vAI == null || !_ownedVehicles.Contains(vAI)) continue;
                if (!vAI.CanReceivePopTransportAssignment)
                {
                    continue;
                }

                if (TryDispatchPopTransportVehicle(vAI))
                {
                    yield return new WaitForSeconds(1.5f); // Throttle departures to create a natural flow
                }
                else 
                {
                    vAI.RecallAndDeactivate();
                }
            }
        }

        _routeRecalculationCoroutine = null;
        if (_needsPathRecalculation)
        {
            RequestPopTransportDispatch();
        }
    }

    public bool TryDispatchPopTransportVehicle(VehicleAI vehicle)
    {
        if (vehicle == null || !(vehicle.vehicleData is PopTransportVehicleData))
        {
            return false;
        }

        RouteData route = FindBestFactoryRoute(vehicle.vehicleData);
        if (route == null ||
            route.targetFactory == null ||
            route.sharedNetwork == null ||
            route.reservedPopCount <= 0)
        {
            return false;
        }

        if (!route.targetFactory.TryReserveIncomingPops(
                vehicle,
                route.reservedPopCount,
                out int reservedPops))
        {
            return false;
        }

        vehicle.AssignPopDelivery(route.targetFactory, reservedPops);
        vehicle.Reroute(route.targetFactory, route.sharedNetwork);
        return true;
    }

    private RouteData FindBestFactoryRoute(VehicleData vehicleData)
    {
        int transportedPops = GetPopTransportAmount(vehicleData);
        if (transportedPops <= 0) return null;

        RouteData bestRoute = null;
        float bestScore = float.MaxValue;
        float bestSqrDistance = float.MaxValue;

        foreach (RoadNetwork network in validNetworks)
        {
            List<Vector2Int> validExits = new List<Vector2Int>();
            foreach (var kvp in globalPorts)
            {
                if (kvp.Value.AllowsVehicleFlow(PortFlowDirection.Exit, vehicleData) &&
                    HasPortOnNetwork(this, network, PortFlowDirection.Entry, vehicleData) &&
                    portNetworks.TryGetValue(kvp.Key, out RoadNetwork net) && net == network)
                {
                    validExits.Add(kvp.Key);
                }
            }

            if (validExits.Count == 0) continue; 

            foreach (Building b in network.connectedBuildings)
            {
                if (b.data is ResourceProductionBuildingData && b.validNetworks.Contains(network))
                {
                    int remainingPopNeed = b.GetRemainingUnassignedPopCapacity();
                    int effectivePopsDelivered = Mathf.Min(transportedPops, remainingPopNeed);
                    if (effectivePopsDelivered <= 0) continue;

                    foreach (var kvp in b.globalPorts)
                    {
                        if (kvp.Value.AllowsVehicleFlow(PortFlowDirection.Entry, vehicleData) &&
                            HasPortOnNetwork(b, network, PortFlowDirection.Exit, vehicleData) &&
                            b.portNetworks.TryGetValue(kvp.Key, out RoadNetwork bNet) && bNet == network)
                        {
                            foreach (Vector2Int exitPort in validExits)
                            {
                                float sqrDist = (kvp.Key - exitPort).sqrMagnitude;
                                float score = CalculatePopDeliveryScore(sqrDist, effectivePopsDelivered);
                                if (score < bestScore ||
                                    (Mathf.Approximately(score, bestScore) && sqrDist < bestSqrDistance))
                                {
                                    bestScore = score;
                                    bestSqrDistance = sqrDist;
                                    bestRoute = new RouteData
                                    {
                                        targetFactory = b,
                                        sharedNetwork = network,
                                        reservedPopCount = effectivePopsDelivered,
                                        score = score
                                    };
                                }
                            }
                        }
                    }
                }
            }
        }
        return bestRoute;
    }

    public int GetTargetPopCount()
    {
        return data is ResourceProductionBuildingData productionData
            ? Mathf.Max(0, productionData.popRequiredForMaxEfficiency)
            : 0;
    }

    public int GetRemainingUnassignedPopCapacity()
    {
        int targetPops = GetTargetPopCount();
        if (targetPops <= 0) return 0;
        return Mathf.Max(0, targetPops - DeliveredPops - ReservedIncomingPops);
    }

    public float GetProductionEfficiency()
    {
        int targetPops = GetTargetPopCount();
        if (targetPops <= 0) return 1f;
        return Mathf.Clamp01((float)DeliveredPops / targetPops);
    }

    public bool TryReserveIncomingPops(
        VehicleAI vehicle,
        int requestedPops,
        out int reservedPops)
    {
        reservedPops = 0;
        if (vehicle == null || requestedPops <= 0) return false;

        ReleasePopAssignment(vehicle);

        int remainingCapacity = GetRemainingUnassignedPopCapacity();
        if (remainingCapacity <= 0) return false;

        reservedPops = Mathf.Min(requestedPops, remainingCapacity);
        _incomingPopReservations[vehicle] = reservedPops;
        ReservedIncomingPops += reservedPops;
        return true;
    }

    public void MarkIncomingPopsDelivered(VehicleAI vehicle)
    {
        if (vehicle == null || !_incomingPopReservations.TryGetValue(vehicle, out int popCount))
        {
            return;
        }

        _incomingPopReservations.Remove(vehicle);
        ReservedIncomingPops = Mathf.Max(0, ReservedIncomingPops - popCount);
        _deliveredPopAssignments[vehicle] = popCount;
        DeliveredPops = Mathf.Min(GetTargetPopCount(), DeliveredPops + popCount);
    }

    public void ReleasePopAssignment(VehicleAI vehicle)
    {
        if (vehicle == null) return;

        if (_incomingPopReservations.TryGetValue(vehicle, out int reservedPops))
        {
            _incomingPopReservations.Remove(vehicle);
            ReservedIncomingPops = Mathf.Max(0, ReservedIncomingPops - reservedPops);
        }

        if (_deliveredPopAssignments.TryGetValue(vehicle, out int deliveredPops))
        {
            _deliveredPopAssignments.Remove(vehicle);
            DeliveredPops = Mathf.Max(0, DeliveredPops - deliveredPops);
        }
    }

    private int GetPopTransportAmount(VehicleData vehicleData)
    {
        return vehicleData is PopTransportVehicleData popVehicleData
            ? Mathf.Max(0, popVehicleData.amountOfPopsTransported)
            : 0;
    }

    private VehicleData GetFirstSpawnedVehicleData()
    {
        for (int i = 0; i < _ownedVehicles.Count; i++)
        {
            if (_ownedVehicles[i] != null) return _ownedVehicles[i].vehicleData;
        }

        return null;
    }

    private bool HasAssignablePopTransportVehicle()
    {
        for (int i = 0; i < _ownedVehicles.Count; i++)
        {
            VehicleAI vehicle = _ownedVehicles[i];
            if (vehicle != null && vehicle.CanReceivePopTransportAssignment)
            {
                return true;
            }
        }

        return false;
    }

    private float CalculatePopDeliveryScore(float sqrDistance, int effectivePopsDelivered)
    {
        float deliveredPopWeight = Mathf.Pow(
            Mathf.Max(1, effectivePopsDelivered),
            PopDeliveryEfficiencyPower);
        return sqrDistance / deliveredPopWeight;
    }

    private IEnumerator WaitForTrafficBackendSubscription()
    {
        while (isActiveAndEnabled && TrafficSystemBackend.Instance == null)
        {
            yield return null;
        }

        _trafficBackendSubscriptionCoroutine = null;
        TrySubscribeToTrafficReady();
    }

    private bool TrySubscribeToTrafficReady()
    {
        if (_subscribedToTrafficReady || TrafficSystemBackend.Instance == null)
        {
            return _subscribedToTrafficReady;
        }

        TrafficSystemBackend.Instance.OnTrafficNetworkReady += HandleTrafficReady;
        _subscribedToTrafficReady = true;

        if (HasPublishedTrafficNetwork())
        {
            HandleTrafficReady();
        }

        return true;
    }

    private bool HasPublishedTrafficNetwork()
    {
        return TrafficSystemBackend.Instance != null &&
               TrafficSystemBackend.Instance.allEdges != null &&
               TrafficSystemBackend.Instance.allEdges.Count > 0;
    }

    private string DescribeFactoryRouteFailure(VehicleData vehicleData)
    {
        int networkCount = validNetworks != null ? validNetworks.Count : 0;
        int networksWithExitPorts = 0;
        int factoriesOnSharedNetworks = 0;
        int factoriesWithEntryPorts = 0;

        if (validNetworks != null)
        {
            foreach (RoadNetwork network in validNetworks)
            {
                if (network == null) continue;

                if (HasPortOnNetwork(this, network, PortFlowDirection.Exit, vehicleData))
                {
                    networksWithExitPorts++;
                }

                if (network.connectedBuildings == null) continue;

                foreach (Building building in network.connectedBuildings)
                {
                    if (building == null || !(building.data is ResourceProductionBuildingData))
                    {
                        continue;
                    }

                    factoriesOnSharedNetworks++;
                    if (building.validNetworks.Contains(network) &&
                        HasPortOnNetwork(building, network, PortFlowDirection.Entry, vehicleData))
                    {
                        factoriesWithEntryPorts++;
                    }
                }
            }
        }

        return
            $"vehicle={DescribeVehicleForPorts(vehicleData)}, validNetworks={networkCount}, networksWithExitPorts={networksWithExitPorts}, factoriesOnSharedNetworks={factoriesOnSharedNetworks}, factoriesWithEntryPorts={factoriesWithEntryPorts}.";
    }

    private bool HasPortOnNetwork(
        Building building,
        RoadNetwork network,
        PortFlowDirection desiredFlow,
        VehicleData vehicleData)
    {
        if (building == null || network == null) return false;

        foreach (var kvp in building.globalPorts)
        {
            bool flowMatches = kvp.Value.AllowsVehicleFlow(desiredFlow, vehicleData);
            if (flowMatches &&
                building.portNetworks.TryGetValue(kvp.Key, out RoadNetwork portNetwork) &&
                portNetwork == network)
            {
                return true;
            }
        }

        return false;
    }

    private string DescribeBuildingForDispatch()
    {
        string buildingName = data != null ? data.buildingName : name;
        return $"Building {buildingName} at {originCell}";
    }

    private string DescribeVehicleForPorts(VehicleData vehicleData)
    {
        if (vehicleData == null) return "any vehicle";
        if (!string.IsNullOrEmpty(vehicleData.vehicleName)) return vehicleData.vehicleName;
        return vehicleData.GetType().Name;
    }
}
