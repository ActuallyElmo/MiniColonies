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

    [Header("Factory Delivery Priority")]
    [SerializeField, Range(1, 9)] private int factoryPriority = 5;
    public int FactoryPriority => Mathf.Clamp(factoryPriority, 1, 9);
    public event System.Action<Building> FactoryPriorityChanged;

    public bool SetFactoryPriority(int priority)
    {
        if (!(data is ResourceProductionBuildingData)) return false;
        priority = Mathf.Clamp(priority, 1, 9);
        if (FactoryPriority == priority) return false;
        factoryPriority = priority;
        FactoryPriorityChanged?.Invoke(this);
        if (BuildingSystemBackend.Instance != null)
        {
            BuildingSystemBackend.Instance.RequestPopTransportDispatch();
            BuildingSystemBackend.Instance.RequestResourceTransportDispatch();
        }
        return true;
    }

    [Header("Factory Schedule")]
    [SerializeField]
    private bool isFactoryOpen = true;

    [SerializeField]
    private List<FactoryShift> factoryShifts = new List<FactoryShift>();

    public bool IsFactoryOpen => isFactoryOpen;
    [Header("Housing Operations")]
    [SerializeField] private bool isHousingOpen = true;
    public bool IsHousingOpen => isHousingOpen;
    public bool IsOpen => data is PopHousingBuildingData
        ? isHousingOpen : !(data is ResourceProductionBuildingData) || isFactoryOpen;
    public event System.Action<Building> OpenStateChanged;

    [Header("Closed Appearance")]
    [SerializeField, Range(0f, 1f)] private float closedBrightness = 0.75f;
    private readonly List<System.Action> restoreOpenAppearance = new List<System.Action>();

    public bool SetHousingOpen(bool isOpen)
    {
        if (!(data is PopHousingBuildingData) || isHousingOpen == isOpen)
            return false;

        // Change the state first: releasing factory reservations can request dispatch.
        isHousingOpen = isOpen;
        if (!isOpen)
        {
            if (_routeRecalculationCoroutine != null)
                StopCoroutine(_routeRecalculationCoroutine);
            _routeRecalculationCoroutine = null;
            _needsPathRecalculation = false;
            foreach (VehicleAI vehicle in _ownedVehicles.ToArray())
                if (vehicle != null) vehicle.RecallAndDeactivate();
        }
        else RequestPopTransportDispatch();

        RefreshOpenAppearance();
        OpenStateChanged?.Invoke(this);
        return true;
    }

    private void RefreshOpenAppearance()
    {
        foreach (System.Action restore in restoreOpenAppearance) restore();
        restoreOpenAppearance.Clear();
        if (IsOpen) return;

        // Tint each material slot independently; shared assets and vehicles stay intact.
        string[] colors = { "_TopColor", "_BottomColor", "_ShadowColor", "_BaseColor", "_Color", "_EmissionColor" };
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.GetComponentInParent<VehicleAI>() != null ||
                renderer.GetComponentInParent<Building>() != this) continue;
            Material[] materials = renderer.sharedMaterials;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                Material material = materials[slot];
                if (material == null) continue;
                var original = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(original, slot);
                var tint = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(tint, slot);
                if (tint.isEmpty) renderer.GetPropertyBlock(tint);
                bool changed = false;
                foreach (string property in colors)
                {
                    if (!material.HasProperty(property)) continue;
                    int id = Shader.PropertyToID(property);
                    Color color = tint.HasColor(id) ? tint.GetColor(id) : material.GetColor(id);
                    float alpha = color.a;
                    color *= closedBrightness;
                    color.a = alpha;
                    tint.SetColor(id, color);
                    changed = true;
                }
                if (!changed) continue;
                int materialSlot = slot;
                restoreOpenAppearance.Add(() =>
                {
                    if (renderer != null)
                        renderer.SetPropertyBlock(original.isEmpty ? null : original, materialSlot);
                });
                renderer.SetPropertyBlock(tint, slot);
            }
        }
    }
    public int FactoryShiftDurationHours =>
        data is ResourceProductionBuildingData productionData
            ? productionData.ShiftDurationHours
            : 0;
    public IReadOnlyList<FactoryShift> FactoryShifts
    {
        get
        {
            EnsureFactorySchedule();
            return factoryShifts;
        }
    }
    public event System.Action<Building> FactoryScheduleChanged;

    [Header("Factory Runtime")]
    [SerializeField]
    private List<ResourceAmount> factoryStorage =
        new List<ResourceAmount>();
    [SerializeField, HideInInspector]
    private bool factoryStorageInitialized;

    private readonly List<ResourceAmount> _currentShiftAllocatedInputs =
        new List<ResourceAmount>();
    private readonly List<ResourceAmount> _currentShiftProcessedInputs =
        new List<ResourceAmount>();
    private readonly List<ResourceAmount> _currentShiftPendingOutputs =
        new List<ResourceAmount>();
    private readonly Dictionary<ResourceType, float>
        _currentShiftProcessedInputExact =
            new Dictionary<ResourceType, float>();
    private readonly List<HourlyInputRequirement>
        _currentShiftHourlyInputRequirements =
            new List<HourlyInputRequirement>();

    private FactoryShift _preparingFactoryShift;
    private FactoryShift _currentFactoryShift;
    private FactoryShift _outputBlockedFactoryShift;
    private int _currentShiftElapsedHours;
    private int _currentWorkingPops;
    private float _currentShiftAccumulatedProductionHours;
    private long _lastHandledFactoryHour = -1L;
    private bool _subscribedToGameTime;
    private Coroutine _gameTimeSubscriptionCoroutine;
    private bool _synchronizingFactoryRuntime;

    public IReadOnlyList<ResourceAmount> FactoryStorage
    {
        get
        {
            EnsureFactoryStorage();
            return factoryStorage;
        }
    }
    public IReadOnlyList<ResourceAmount> CurrentShiftInputsRemaining
    {
        get
        {
            RebuildCurrentShiftInputRemainders();
            return _currentShiftInputRemainders;
        }
    }
    public IReadOnlyList<ResourceAmount> CurrentShiftPendingOutputs =>
        _currentShiftPendingOutputs;
    public FactoryShift PreparingFactoryShift => _preparingFactoryShift;
    public FactoryShift CurrentFactoryShift => _currentFactoryShift;
    public int CurrentShiftElapsedHours => _currentShiftElapsedHours;
    public int CurrentWorkingPops => _currentWorkingPops;
    public event System.Action<Building> FactoryRuntimeChanged;

    private readonly List<ResourceAmount> _currentShiftInputRemainders =
        new List<ResourceAmount>();

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
    private bool _dispatchingResourceVehicles;

    public class RouteData
    {
        public Building targetFactory;
        public RoadNetwork sharedNetwork;
        public int reservedPopCount;
        public float score;
    }

    private sealed class FactoryPopAssignment
    {
        public FactoryShift shift;
        public int popCount;
        public bool delivered;
        public long eligibleFromHour;
    }

    private struct HourlyInputRequirement
    {
        public ResourceType resourceType;
        public float amount;
    }

    private readonly Dictionary<VehicleAI, FactoryPopAssignment>
        _factoryPopAssignments =
            new Dictionary<VehicleAI, FactoryPopAssignment>();

    private sealed class FactoryResourceReservation
    {
        public ResourceType resourceType;
        public int amount;
    }

    private readonly Dictionary<VehicleAI, FactoryResourceReservation>
        _incomingResourceReservations =
            new Dictionary<VehicleAI, FactoryResourceReservation>();

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
        RemoveMissingPopAssignments();
        return _factoryPopAssignments.Count;
    }

    public int GetAssignedPopCount()
    {
        return ReservedIncomingPops + DeliveredPops;
    }

    public int PreparedPops => GetDeliveredPopsForShift(
        _preparingFactoryShift,
        false);

    public int PreparedIncomingPops => GetReservedPopsForShift(
        _preparingFactoryShift);

    public int CurrentLatePops => GetDeliveredPopsForShift(
        _currentFactoryShift,
        true);

    public float CurrentFactoryEfficiency
    {
        get
        {
            if (_currentFactoryShift == null) return 0f;
            int maximumPops = GetTargetPopCount();
            return maximumPops <= 0
                ? 1f
                : Mathf.Clamp01((float)_currentWorkingPops / maximumPops);
        }
    }

    public bool IsFactoryOutputStorageBlocked =>
        IsOutputStorageFull();

    public int GetVehicleSpaceCapacity()
    {
        if (data is PopHousingBuildingData housingData)
        {
            return Mathf.Max(
                0,
                housingData.popTransportVehiclesStorageSpace);
        }

        return data is ResourceProductionBuildingData productionData
            ? Mathf.Max(
                0,
                productionData.industrialVehiclesStorageSpace)
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
        RefreshOpenAppearance();
        originCell = origin;
        elevationLayer = layer;
        EnsureFactoryStorage();

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

        TrySubscribeToGameTime();
        SynchronizeFactoryRuntimeAtCurrentTime();
    }

    private static int GetVehicleSpaceCost(VehicleData vehicleData)
    {
        if (vehicleData is PopTransportVehicleData popVehicleData)
        {
            return Mathf.Max(1, popVehicleData.vehicleSpaceUsed);
        }

        return vehicleData is ResourceTransportVehicleData resourceVehicleData
            ? Mathf.Max(1, resourceVehicleData.vehicleSpaceUsed)
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

        TrySubscribeToGameTime();
        if (!_subscribedToGameTime &&
            _gameTimeSubscriptionCoroutine == null)
        {
            _gameTimeSubscriptionCoroutine =
                StartCoroutine(WaitForGameTimeSubscription());
        }

        if (data is ResourceProductionBuildingData)
        {
            RequestResourceTransportDispatch();
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

        if (_gameTimeSubscriptionCoroutine != null)
        {
            StopCoroutine(_gameTimeSubscriptionCoroutine);
            _gameTimeSubscriptionCoroutine = null;
        }

        UnsubscribeFromGameTime();
    }

    private void OnDestroy()
    {
        UnsubscribeFromGameTime();
        // Unity does not guarantee manager/building destruction order when
        // leaving Play Mode. Release the assignments, but only start return
        // routes while the path service still exists (normal demolition).
        ReleaseAllFactoryPopAssignments(
            VehiclePathRequestManager.Instance != null);

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
        if (BuildingSystemBackend.Instance != null)
        {
            BuildingSystemBackend.Instance.RequestResourceTransportDispatch();
        }
        else
        {
            RequestResourceTransportDispatch();
        }
    }

    public void RequestPopTransportDispatch()
    {
        if (!(data is PopHousingBuildingData) || !isHousingOpen || !isActiveAndEnabled)
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
        if (!isActiveAndEnabled || data == null || !IsOpen) return;

        if (data is ResourceProductionBuildingData)
        {
            RequestResourceTransportDispatch();
        }

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
        if (!(data is PopHousingBuildingData) || !isHousingOpen ||
            !isActiveAndEnabled || vehicle == null || vehicle.homeBuilding != this ||
            !vehicle.CanReceivePopTransportAssignment ||
            !(vehicle.vehicleData is PopTransportVehicleData))
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
        float bestScore = float.PositiveInfinity;
        float bestDistance = float.PositiveInfinity;
        var routing = new VehicleDeliveryRouting(vehicleData);
        foreach (RoadNetwork network in validNetworks)
        {
            if (!HasPortOnNetwork(this, network, PortFlowDirection.Entry, vehicleData)) continue;
            foreach (Building target in network.connectedBuildings)
            {
                if (target == null || !(target.data is ResourceProductionBuildingData) ||
                    !target.validNetworks.Contains(network) ||
                    !HasPortOnNetwork(target, network, PortFlowDirection.Exit, vehicleData)) continue;
                int amount = Mathf.Min(transportedPops, target.GetRemainingUnassignedPopCapacity());
                if (amount <= 0 || !routing.TryFindPorts(this, target, network,
                    out _, out _, out float distance)) continue;
                float score = VehicleDeliveryRouting.Score(distance, target.FactoryPriority) / amount;
                if (score < bestScore || (Mathf.Approximately(score, bestScore) && distance < bestDistance))
                {
                    bestScore = score;
                    bestDistance = distance;
                    bestRoute = new RouteData { targetFactory = target, sharedNetwork = network,
                        reservedPopCount = amount, score = score };
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

    public void RequestResourceTransportDispatch()
    {
        if (!(data is ResourceProductionBuildingData) || !isActiveAndEnabled) return;
        DispatchResourceVehicles();
    }

    private void DispatchResourceVehicles()
    {
        if (_dispatchingResourceVehicles) return;
        _dispatchingResourceVehicles = true;
        try
        {
        for (int i = 0; i < _ownedVehicles.Count; i++)
        {
            VehicleAI vehicle = _ownedVehicles[i];
            if (vehicle == null || !(vehicle.vehicleData is ResourceTransportVehicleData vd) ||
                vehicle.currentState != VehicleAI.VehicleState.Idle || !vehicle.HasAssignedResourceType) continue;
            int available = GetFactoryStoredAmount(vehicle.AssignedResourceType);
            if (available <= 0) continue;
            Building bestTarget = null;
            RoadNetwork bestNetwork = null;
            int bestAmount = 0;
            float bestScore = float.PositiveInfinity;
            float bestDistance = float.PositiveInfinity;
            var routing = new VehicleDeliveryRouting(vd);
            foreach (RoadNetwork network in validNetworks)
            {
                if (!HasPortOnNetwork(this, network, PortFlowDirection.Entry, vd)) continue;
                foreach (Building target in network.connectedBuildings)
                {
                    if (target == null || target == this ||
                        !(target.data is ResourceProductionBuildingData) ||
                        !target.validNetworks.Contains(network) ||
                        !HasPortOnNetwork(target, network, PortFlowDirection.Exit, vd)) continue;
                    int amount = Mathf.Min(vd.vehicleCapacity, Mathf.Min(available,
                        target.GetRemainingFactoryInputDemand(vehicle.AssignedResourceType) -
                        target.GetReservedIncomingResourceAmount(vehicle.AssignedResourceType)));
                    if (amount <= 0 || !routing.TryFindPorts(this, target, network,
                        out _, out _, out float distance)) continue;
                    float score = VehicleDeliveryRouting.Score(distance, target.FactoryPriority);
                    if (score < bestScore || (Mathf.Approximately(score, bestScore) && distance < bestDistance))
                    {
                        bestTarget = target;
                        bestNetwork = network;
                        bestAmount = amount;
                        bestScore = score;
                        bestDistance = distance;
                    }
                }
            }
            if (bestTarget == null || !bestTarget.TryReserveIncomingResource(vehicle,
                vehicle.AssignedResourceType, bestAmount, out int reserved)) continue;
            if (!TryTakeFactoryOutput(vehicle.AssignedResourceType, reserved, out int taken))
            {
                bestTarget.ReleaseIncomingResourceReservation(vehicle);
                continue;
            }
            if (taken != reserved) bestTarget.SetIncomingResourceReservationAmount(vehicle, taken);
            vehicle.AssignResourceCargo(bestTarget, vehicle.AssignedResourceType, taken);
            vehicle.Reroute(bestTarget, bestNetwork);
        }
        }
        finally
        {
            _dispatchingResourceVehicles = false;
        }
    }

    public bool SetFactoryOpen(bool isOpen)
    {
        if (!(data is ResourceProductionBuildingData) ||
            isFactoryOpen == isOpen)
        {
            return false;
        }

        isFactoryOpen = isOpen;
        RefreshOpenAppearance();
        NotifyFactoryScheduleChanged();
        OpenStateChanged?.Invoke(this);
        return true;
    }

    public VehicleAssignmentResult GetVehicleAssignmentResult(
        ResourceTransportVehicleData vehicleData)
    {
        if (!(data is ResourceProductionBuildingData))
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

        var compatibleOutputs = new List<ResourceType>();
        GetCompatibleFactoryOutputs(vehicleData, compatibleOutputs);
        if (compatibleOutputs.Count == 0)
        {
            return VehicleAssignmentResult.VehicleTypeNotAllowed;
        }

        return GetVehicleSpaceCost(vehicleData) > GetAvailableVehicleSpace()
            ? VehicleAssignmentResult.InsufficientSpace
            : VehicleAssignmentResult.Allowed;
    }

    public bool TryAssignVehicle(
        ResourceTransportVehicleData vehicleData,
        out VehicleAI vehicle)
    {
        vehicle = null;
        VehicleAssignmentResult result =
            GetVehicleAssignmentResult(vehicleData);
        if (result != VehicleAssignmentResult.Allowed)
        {
            Debug.LogWarning(
                $"Could not assign resource vehicle to " +
                $"{DescribeBuildingForDispatch()}: {result}.",
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
        vehicle.ClearAssignedResourceType();

        _ownedVehicles.Add(vehicle);
        VehicleOwnershipChanged?.Invoke(this);
        return true;
    }

    public void GetCompatibleFactoryOutputs(
        ResourceTransportVehicleData vehicleData,
        List<ResourceType> results)
    {
        if (results == null) return;
        results.Clear();

        if (!(data is ResourceProductionBuildingData productionData) ||
            productionData.resources == null ||
            vehicleData == null ||
            vehicleData.transportableResources == null)
        {
            return;
        }

        for (int i = 0; i < productionData.resources.Count; i++)
        {
            ProductionResourceEntry resource = productionData.resources[i];
            if (resource.direction != ProductionResourceDirection.Output ||
                results.Contains(resource.resourceType) ||
                !vehicleData.transportableResources.Contains(
                    resource.resourceType))
            {
                continue;
            }

            results.Add(resource.resourceType);
        }
    }

    public bool TrySetResourceVehicleOutput(
        VehicleAI vehicle,
        ResourceType resourceType)
    {
        if (vehicle == null ||
            vehicle.homeBuilding != this ||
            !_ownedVehicles.Contains(vehicle) ||
            !(vehicle.vehicleData is ResourceTransportVehicleData vehicleData))
        {
            return false;
        }

        var compatibleOutputs = new List<ResourceType>();
        GetCompatibleFactoryOutputs(vehicleData, compatibleOutputs);
        if (!compatibleOutputs.Contains(resourceType))
        {
            return false;
        }

        vehicle.SetAssignedResourceType(resourceType);
        VehicleOwnershipChanged?.Invoke(this);
        RequestResourceTransportDispatch();
        return true;
    }

    public bool TryClearResourceVehicleOutput(VehicleAI vehicle)
    {
        if (vehicle == null ||
            vehicle.homeBuilding != this ||
            !_ownedVehicles.Contains(vehicle) ||
            !(vehicle.vehicleData is ResourceTransportVehicleData))
        {
            return false;
        }

        vehicle.ClearAssignedResourceType();
        VehicleOwnershipChanged?.Invoke(this);
        RequestResourceTransportDispatch();
        return true;
    }

    public bool TryAddFactoryShift(out FactoryShift shift)
    {
        if (!TryCreateFactoryShift(out shift)) return false;

        NotifyFactoryScheduleChanged();
        return true;
    }

    public bool TryDuplicateFactoryShift(
        FactoryShift sourceShift,
        out FactoryShift duplicateShift)
    {
        duplicateShift = null;
        EnsureFactorySchedule();
        if (!ContainsFactoryShift(sourceShift) ||
            !TryCreateFactoryShift(out duplicateShift))
        {
            return false;
        }

        duplicateShift.SetPreparationHours(sourceShift.PreparationHours);
        duplicateShift.SetDesiredPops(sourceShift.DesiredPops);
        IReadOnlyList<ResourceAmount> resources =
            sourceShift.DesiredResources;
        for (int i = 0; i < resources.Count; i++)
        {
            duplicateShift.SetDesiredResourceAmount(
                resources[i].resourceType,
                resources[i].amount);
        }

        // A duplicate is always created disabled, even when its source is on.
        duplicateShift.SetActive(false);
        NotifyFactoryScheduleChanged();
        return true;
    }

    private bool TryCreateFactoryShift(out FactoryShift shift)
    {
        shift = null;
        if (!(data is ResourceProductionBuildingData productionData))
        {
            return false;
        }

        EnsureFactorySchedule();
        if (factoryShifts.Count >=
            GameTimeManager.HoursPerDay / FactoryShiftDurationHours)
        {
            return false;
        }

        int preferredStart = factoryShifts.Count == 0
            ? 0
            : factoryShifts[factoryShifts.Count - 1]
                .GetEndHour(FactoryShiftDurationHours);
        int startHour = FindAvailableShiftStart(preferredStart);
        if (startHour < 0) return false;

        shift = new FactoryShift(
            startHour,
            1,
            GetRequiredResourceTypes(productionData));
        factoryShifts.Add(shift);
        return true;
    }

    public bool RemoveFactoryShift(FactoryShift shift)
    {
        EnsureFactorySchedule();
        if (shift == null || !factoryShifts.Remove(shift)) return false;

        NotifyFactoryScheduleChanged();
        return true;
    }

    public bool TrySetFactoryShiftStartHour(
        FactoryShift shift,
        int startHour)
    {
        EnsureFactorySchedule();
        if (!ContainsFactoryShift(shift) || shift.IsActive) return false;

        int wrappedStart = FactoryShift.WrapHour(startHour);
        if (!IsShiftRangeAvailable(
                wrappedStart,
                FactoryShiftDurationHours,
                shift))
        {
            return false;
        }

        shift.SetStartHour(wrappedStart);
        NotifyFactoryScheduleChanged();
        return true;
    }

    /// <summary>
    /// Moves a shift by whole hours. Any shift hit in the movement direction
    /// is pushed by the same amount, recursively. The simulated result is
    /// committed only when the complete schedule remains valid.
    /// </summary>
    public bool TryMoveFactoryShiftChain(
        FactoryShift shift,
        int hourDelta)
    {
        EnsureFactorySchedule();
        if (!ContainsFactoryShift(shift) || shift.IsActive) return false;
        if (hourDelta == 0) return true;

        int direction = hourDelta > 0 ? 1 : -1;
        int stepCount = Mathf.Min(
            Mathf.Abs(hourDelta),
            GameTimeManager.HoursPerDay);
        var simulatedStarts = new Dictionary<FactoryShift, int>();
        for (int i = 0; i < factoryShifts.Count; i++)
        {
            simulatedStarts.Add(
                factoryShifts[i],
                factoryShifts[i].StartHour);
        }

        for (int step = 0; step < stepCount; step++)
        {
            var movingShifts = new List<FactoryShift> { shift };
            var movingSet = new HashSet<FactoryShift> { shift };

            for (int movingIndex = 0;
                 movingIndex < movingShifts.Count;
                 movingIndex++)
            {
                FactoryShift movingShift = movingShifts[movingIndex];
                int destinationStart = FactoryShift.WrapHour(
                    simulatedStarts[movingShift] + direction);

                for (int otherIndex = 0;
                     otherIndex < factoryShifts.Count;
                     otherIndex++)
                {
                    FactoryShift otherShift = factoryShifts[otherIndex];
                    if (movingSet.Contains(otherShift)) continue;

                    if (ShiftRangesOverlap(
                            destinationStart,
                            simulatedStarts[otherShift],
                            FactoryShiftDurationHours))
                    {
                        if (otherShift.IsActive)
                        {
                            return false;
                        }

                        movingSet.Add(otherShift);
                        movingShifts.Add(otherShift);
                    }
                }
            }

            for (int i = 0; i < movingShifts.Count; i++)
            {
                FactoryShift movingShift = movingShifts[i];
                simulatedStarts[movingShift] = FactoryShift.WrapHour(
                    simulatedStarts[movingShift] + direction);
            }

            if (!IsSimulatedScheduleValid(simulatedStarts))
            {
                return false;
            }
        }

        foreach (KeyValuePair<FactoryShift, int> entry in simulatedStarts)
        {
            entry.Key.SetStartHour(entry.Value);
        }

        NotifyFactoryScheduleChanged();
        return true;
    }

    public bool SetFactoryShiftActive(FactoryShift shift, bool isActive)
    {
        EnsureFactorySchedule();
        if (!ContainsFactoryShift(shift) || shift.IsActive == isActive)
        {
            return false;
        }

        shift.SetActive(isActive);
        NotifyFactoryScheduleChanged();
        return true;
    }

    public bool SetFactoryShiftPreparationHours(
        FactoryShift shift,
        int preparationHours)
    {
        EnsureFactorySchedule();
        if (!ContainsFactoryShift(shift)) return false;

        shift.SetPreparationHours(Mathf.Clamp(
            preparationHours,
            1,
            FactoryShiftDurationHours));
        NotifyFactoryScheduleChanged();
        return true;
    }

    public bool SetFactoryShiftDesiredPops(
        FactoryShift shift,
        int desiredPops)
    {
        EnsureFactorySchedule();
        if (!ContainsFactoryShift(shift)) return false;

        shift.SetDesiredPops(Mathf.Clamp(
            desiredPops,
            0,
            GetTargetPopCount()));
        NotifyFactoryScheduleChanged();
        return true;
    }

    public bool SetFactoryShiftDesiredResource(
        FactoryShift shift,
        ResourceType resourceType,
        int amount)
    {
        EnsureFactorySchedule();
        if (!ContainsFactoryShift(shift) ||
            !TryGetProductionResource(
                resourceType,
                ProductionResourceDirection.Input,
                out ProductionResourceEntry resource))
        {
            return false;
        }

        shift.SetDesiredResourceAmount(
            resourceType,
            Mathf.Clamp(amount, 0, resource.storageCapacity));
        NotifyFactoryScheduleChanged();
        return true;
    }

    public int GetFactoryShiftResourceCapacity(ResourceType resourceType)
    {
        return TryGetProductionResource(
            resourceType,
            ProductionResourceDirection.Input,
            out ProductionResourceEntry resource)
            ? resource.storageCapacity
            : 0;
    }

    public float GetFactoryShiftEfficiency(FactoryShift shift)
    {
        EnsureFactorySchedule();
        if (!ContainsFactoryShift(shift)) return 0f;

        // Derive staffing from delivered assignments at processing time. This
        // keeps production running even when the cached runtime counter has
        // not yet been refreshed by the hour transition callback.
        int maximumPops = GetTargetPopCount();
        int processingPops = GetDeliveredPopsForShift(_currentFactoryShift, true);
        if (processingPops > _currentWorkingPops)
        {
            _currentWorkingPops = processingPops;
        }
        return maximumPops <= 0
            ? 1f
            : Mathf.Clamp01((float)shift.DesiredPops / maximumPops);
    }

    public int GetFactoryShiftRequiredResourceAmount(
        FactoryShift shift,
        ResourceType resourceType)
    {
        if (!TryGetProductionResource(
                resourceType,
                ProductionResourceDirection.Input,
                out ProductionResourceEntry resource))
        {
            return 0;
        }

        return Mathf.CeilToInt(
            resource.amountAtMaximumEfficiency *
            GetFactoryShiftEfficiency(shift));
    }

    public bool IsFactoryScheduledAtHour(int hour)
    {
        EnsureFactorySchedule();
        int wrappedHour = FactoryShift.WrapHour(hour);
        for (int i = 0; i < factoryShifts.Count; i++)
        {
            if (ShiftContainsHour(
                    factoryShifts[i].StartHour,
                    FactoryShiftDurationHours,
                    wrappedHour))
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureFactorySchedule()
    {
        if (factoryShifts == null)
        {
            factoryShifts = new List<FactoryShift>();
        }

        for (int i = factoryShifts.Count - 1; i >= 0; i--)
        {
            if (factoryShifts[i] == null)
            {
                factoryShifts.RemoveAt(i);
            }
        }

        if (!(data is ResourceProductionBuildingData productionData)) return;

        IEnumerable<ResourceType> resourceTypes =
            GetRequiredResourceTypes(productionData);
        for (int i = 0; i < factoryShifts.Count; i++)
        {
            FactoryShift shift = factoryShifts[i];
            if (shift == null) continue;

            shift.EnsureResourceTypes(resourceTypes);
            shift.SetDesiredPops(Mathf.Clamp(
                shift.DesiredPops,
                0,
                GetTargetPopCount()));
            foreach (ResourceType resourceType in resourceTypes)
            {
                shift.SetDesiredResourceAmount(
                    resourceType,
                    Mathf.Clamp(
                        shift.GetDesiredResourceAmount(resourceType),
                        0,
                        GetFactoryShiftResourceCapacity(resourceType)));
            }
            shift.SetPreparationHours(Mathf.Clamp(
                shift.PreparationHours,
                1,
                FactoryShiftDurationHours));
        }
    }

    private bool ContainsFactoryShift(FactoryShift shift)
    {
        return shift != null && factoryShifts.Contains(shift);
    }

    private int FindAvailableShiftStart(int preferredStart)
    {
        for (int offset = 0; offset < GameTimeManager.HoursPerDay; offset++)
        {
            int candidate = FactoryShift.WrapHour(preferredStart + offset);
            if (IsShiftRangeAvailable(
                    candidate,
                    FactoryShiftDurationHours,
                    null))
            {
                return candidate;
            }
        }

        return -1;
    }

    private bool IsShiftRangeAvailable(
        int startHour,
        int durationHours,
        FactoryShift ignoredShift)
    {
        for (int hourOffset = 0; hourOffset < durationHours; hourOffset++)
        {
            int hour = FactoryShift.WrapHour(startHour + hourOffset);
            for (int shiftIndex = 0;
                 shiftIndex < factoryShifts.Count;
                 shiftIndex++)
            {
                FactoryShift otherShift = factoryShifts[shiftIndex];
                if (otherShift == null || otherShift == ignoredShift) continue;

                if (ShiftContainsHour(
                        otherShift.StartHour,
                        durationHours,
                        hour))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ShiftContainsHour(
        int startHour,
        int durationHours,
        int hour)
    {
        int offset = FactoryShift.WrapHour(hour - startHour);
        return offset < durationHours;
    }

    private bool IsSimulatedScheduleValid(
        IReadOnlyDictionary<FactoryShift, int> simulatedStarts)
    {
        for (int firstIndex = 0;
             firstIndex < factoryShifts.Count;
             firstIndex++)
        {
            for (int secondIndex = firstIndex + 1;
                 secondIndex < factoryShifts.Count;
                 secondIndex++)
            {
                if (ShiftRangesOverlap(
                        simulatedStarts[factoryShifts[firstIndex]],
                        simulatedStarts[factoryShifts[secondIndex]],
                        FactoryShiftDurationHours))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ShiftRangesOverlap(
        int firstStart,
        int secondStart,
        int durationHours)
    {
        for (int offset = 0; offset < durationHours; offset++)
        {
            int hour = FactoryShift.WrapHour(firstStart + offset);
            if (ShiftContainsHour(secondStart, durationHours, hour))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetProductionResource(
        ResourceType resourceType,
        ProductionResourceDirection direction,
        out ProductionResourceEntry matchingResource)
    {
        matchingResource = default;
        if (!(data is ResourceProductionBuildingData productionData) ||
            productionData.resources == null)
        {
            return false;
        }

        for (int i = 0; i < productionData.resources.Count; i++)
        {
            ProductionResourceEntry resource = productionData.resources[i];
            if (resource.resourceType == resourceType &&
                resource.direction == direction)
            {
                matchingResource = resource;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<ResourceType> GetRequiredResourceTypes(
        ResourceProductionBuildingData productionData)
    {
        var resourceTypes = new List<ResourceType>();
        if (productionData.resources == null)
        {
            return resourceTypes;
        }

        for (int i = 0; i < productionData.resources.Count; i++)
        {
            ProductionResourceEntry resource = productionData.resources[i];
            if (resource.direction != ProductionResourceDirection.Input)
            {
                continue;
            }

            ResourceType resourceType = resource.resourceType;
            if (!resourceTypes.Contains(resourceType))
            {
                resourceTypes.Add(resourceType);
            }
        }

        return resourceTypes;
    }

    private void NotifyFactoryScheduleChanged()
    {
        SynchronizeFactoryRuntimeAtCurrentTime();
        FactoryScheduleChanged?.Invoke(this);
        if (BuildingSystemBackend.Instance != null)
        {
            BuildingSystemBackend.Instance.RequestResourceTransportDispatch();
        }
    }

    public int GetFactoryStoredAmount(ResourceType resourceType)
    {
        EnsureFactoryStorage();
        return GetResourceAmount(factoryStorage, resourceType);
    }

    public int GetRemainingFactoryInputDemand(ResourceType resourceType)
    {
        SynchronizeFactoryRuntimeAtCurrentTime(false);
        return GetFactoryShiftRemainingInputDemand(
            _preparingFactoryShift,
            resourceType);
    }

    public int GetReservedIncomingResourceAmount(ResourceType resourceType)
    {
        int reserved = 0;
        foreach (FactoryResourceReservation reservation in
                 _incomingResourceReservations.Values)
        {
            if (reservation != null && reservation.resourceType == resourceType)
            {
                reserved += Mathf.Max(0, reservation.amount);
            }
        }

        return reserved;
    }

    public bool TryReserveIncomingResource(
        VehicleAI vehicle,
        ResourceType resourceType,
        int requestedAmount,
        out int reservedAmount)
    {
        reservedAmount = 0;
        if (vehicle == null || requestedAmount <= 0 ||
            !(data is ResourceProductionBuildingData) ||
            !TryGetProductionResource(
                resourceType,
                ProductionResourceDirection.Input,
                out _))
        {
            return false;
        }

        ReleaseIncomingResourceReservation(vehicle);
        int remainingDemand = Mathf.Max(
            0,
            GetRemainingFactoryInputDemand(resourceType) -
            GetReservedIncomingResourceAmount(resourceType));
        reservedAmount = Mathf.Min(requestedAmount, remainingDemand);
        if (reservedAmount <= 0) return false;

        _incomingResourceReservations[vehicle] =
            new FactoryResourceReservation
            {
                resourceType = resourceType,
                amount = reservedAmount
            };
        NotifyFactoryRuntimeChanged();
        return true;
    }

    internal void SetIncomingResourceReservationAmount(
        VehicleAI vehicle,
        int amount)
    {
        if (vehicle == null ||
            !_incomingResourceReservations.TryGetValue(
                vehicle,
                out FactoryResourceReservation reservation))
        {
            return;
        }

        reservation.amount = Mathf.Max(0, amount);
        if (reservation.amount == 0)
        {
            _incomingResourceReservations.Remove(vehicle);
        }
        NotifyFactoryRuntimeChanged();
    }

    internal void ReleaseIncomingResourceReservation(VehicleAI vehicle)
    {
        if (vehicle == null ||
            !_incomingResourceReservations.Remove(vehicle))
        {
            return;
        }

        NotifyFactoryRuntimeChanged();
    }

    internal bool ReceiveIncomingResource(
        VehicleAI vehicle,
        ResourceType resourceType,
        int amount)
    {
        if (vehicle == null || amount <= 0 ||
            !_incomingResourceReservations.TryGetValue(
                vehicle,
                out FactoryResourceReservation reservation) ||
            reservation.resourceType != resourceType)
        {
            return false;
        }

        _incomingResourceReservations.Remove(vehicle);
        AddFactoryResourceAllowOverfill(resourceType, amount);
        return true;
    }

    public int GetFactoryShiftRemainingInputDemand(
        FactoryShift shift,
        ResourceType resourceType)
    {
        EnsureFactorySchedule();
        if (shift == null ||
            !ContainsFactoryShift(shift) ||
            !TryGetProductionResource(
                resourceType,
                ProductionResourceDirection.Input,
                out _))
        {
            return 0;
        }

        int targetAmount = Mathf.Min(
            shift.GetDesiredResourceAmount(resourceType),
            GetFactoryResourceCapacity(resourceType));
        return Mathf.Max(
            0,
            targetAmount - GetFactoryStoredAmount(resourceType));
    }

    public void GetRemainingFactoryInputDemands(
        List<ResourceAmount> results)
    {
        if (results == null) return;
        results.Clear();
        SynchronizeFactoryRuntimeAtCurrentTime(false);

        FactoryShift shift = _preparingFactoryShift;
        if (shift == null ||
            !(data is ResourceProductionBuildingData productionData) ||
            productionData.resources == null)
        {
            return;
        }

        var added = new HashSet<ResourceType>();
        for (int i = 0; i < productionData.resources.Count; i++)
        {
            ProductionResourceEntry entry = productionData.resources[i];
            if (entry.direction != ProductionResourceDirection.Input ||
                !added.Add(entry.resourceType))
            {
                continue;
            }

            int remaining = GetFactoryShiftRemainingInputDemand(
                shift,
                entry.resourceType);
            if (remaining <= 0) continue;

            results.Add(new ResourceAmount
            {
                resourceType = entry.resourceType,
                amount = remaining
            });
        }
    }

    public int GetFactoryResourceCapacity(ResourceType resourceType)
    {
        if (!(data is ResourceProductionBuildingData productionData) ||
            productionData.resources == null)
        {
            return 0;
        }

        int capacity = 0;
        for (int i = 0; i < productionData.resources.Count; i++)
        {
            ProductionResourceEntry entry = productionData.resources[i];
            if (entry.resourceType == resourceType)
            {
                capacity = Mathf.Max(capacity, entry.storageCapacity);
            }
        }

        return capacity;
    }

    public float GetFactoryResourceThroughputPerWorkerPerHour(
        ResourceType resourceType,
        ProductionResourceDirection direction)
    {
        if (!TryGetProductionResource(
                resourceType,
                direction,
                out ProductionResourceEntry resource))
        {
            return 0f;
        }

        int workerCount = GetTargetPopCount();
        int shiftHours = FactoryShiftDurationHours;
        if (workerCount <= 0 || shiftHours <= 0)
        {
            return 0f;
        }

        return Mathf.Max(0, resource.amountAtMaximumEfficiency) /
               (float)(workerCount * shiftHours);
    }

    public bool TryStoreFactoryResource(
        ResourceType resourceType,
        int requestedAmount,
        out int storedAmount)
    {
        storedAmount = 0;
        if (requestedAmount <= 0) return false;

        EnsureFactoryStorage();
        int capacity = GetFactoryResourceCapacity(resourceType);
        int current = GetResourceAmount(factoryStorage, resourceType);
        int inProcess = Mathf.Max(
            0,
            GetResourceAmount(
                _currentShiftAllocatedInputs,
                resourceType) -
            GetResourceAmount(
                _currentShiftProcessedInputs,
                resourceType));
        int availableCapacity = Mathf.Max(
            0,
            capacity - current - inProcess);
        storedAmount = Mathf.Min(requestedAmount, availableCapacity);
        if (storedAmount <= 0) return false;

        SetResourceAmount(
            factoryStorage,
            resourceType,
            current + storedAmount);
        NotifyFactoryRuntimeChanged();
        RequestResourceTransportDispatch();
        return true;
    }

    internal void AddFactoryResourceAllowOverfill(
        ResourceType resourceType,
        int amount)
    {
        if (amount <= 0) return;

        EnsureFactoryStorage();
        int current = GetResourceAmount(factoryStorage, resourceType);
        SetResourceAmount(
            factoryStorage,
            resourceType,
            current + amount);
        NotifyFactoryRuntimeChanged();
        RequestResourceTransportDispatch();
    }

    public bool TryTakeFactoryResource(
        ResourceType resourceType,
        int requestedAmount,
        out int takenAmount)
    {
        takenAmount = 0;
        if (requestedAmount <= 0) return false;

        EnsureFactoryStorage();
        int current = GetResourceAmount(factoryStorage, resourceType);
        takenAmount = Mathf.Min(requestedAmount, current);
        if (takenAmount <= 0) return false;

        SetResourceAmount(
            factoryStorage,
            resourceType,
            current - takenAmount);
        SynchronizeFactoryRuntimeAtCurrentTime(false);
        NotifyFactoryRuntimeChanged();
        return true;
    }

    public bool TryTakeFactoryOutput(
        ResourceType resourceType,
        int requestedAmount,
        out int takenAmount)
    {
        takenAmount = 0;
        if (!TryGetProductionResource(
                resourceType,
                ProductionResourceDirection.Output,
                out _))
        {
            return false;
        }

        return TryTakeFactoryResource(
            resourceType,
            requestedAmount,
            out takenAmount);
    }

    private void EnsureFactoryStorage()
    {
        if (factoryStorage == null)
        {
            factoryStorage = new List<ResourceAmount>();
        }

        if (!(data is ResourceProductionBuildingData productionData) ||
            productionData.resources == null)
        {
            return;
        }

        bool initializeToDefaults = !factoryStorageInitialized;
        var synchronized = new List<ResourceAmount>();
        var added = new HashSet<ResourceType>();
        for (int i = 0; i < productionData.resources.Count; i++)
        {
            ProductionResourceEntry entry = productionData.resources[i];
            if (!added.Add(entry.resourceType)) continue;

            bool isInput = HasProductionResourceDirection(
                entry.resourceType,
                ProductionResourceDirection.Input);
            bool isOutput = HasProductionResourceDirection(
                entry.resourceType,
                ProductionResourceDirection.Output);
            int amount = initializeToDefaults
                ? isInput
                    ? GetFactoryResourceCapacity(entry.resourceType)
                    : 0
                : GetResourceAmount(factoryStorage, entry.resourceType);
            synchronized.Add(new ResourceAmount
            {
                resourceType = entry.resourceType,
                // Output created by a shift is allowed to overfill storage;
                // that excess is visible and blocks future shift starts.
                amount = isOutput
                    ? Mathf.Max(0, amount)
                    : Mathf.Clamp(
                        amount,
                        0,
                        GetFactoryResourceCapacity(entry.resourceType))
            });
        }

        factoryStorage = synchronized;
        factoryStorageInitialized = true;
    }

    private void SynchronizeFactoryRuntimeAtCurrentTime(
        bool publishChanges = true)
    {
        if (_synchronizingFactoryRuntime ||
            !(data is ResourceProductionBuildingData) ||
            GameTimeManager.Instance == null)
        {
            return;
        }

        _synchronizingFactoryRuntime = true;
        try
        {
            EnsureFactorySchedule();
            EnsureFactoryStorage();
            long absoluteHour = GetCurrentAbsoluteHour();
            if (_lastHandledFactoryHour < 0L)
            {
                _lastHandledFactoryHour = absoluteHour;
            }

            int hour = GameTimeManager.Instance.CurrentHour;
            FactoryShift scheduledShift = isFactoryOpen
                ? FindEnabledFactoryShiftAtHour(hour)
                : null;

            if (_outputBlockedFactoryShift != null &&
                _outputBlockedFactoryShift != scheduledShift)
            {
                _outputBlockedFactoryShift = null;
            }

            if (_currentFactoryShift != scheduledShift)
            {
                if (_currentFactoryShift != null)
                {
                    FinishCurrentFactoryShift();
                }

                if (scheduledShift != null &&
                    scheduledShift != _outputBlockedFactoryShift)
                {
                    if (IsOutputStorageFull())
                    {
                        _outputBlockedFactoryShift = scheduledShift;
                    }
                    else
                    {
                        StartFactoryShift(scheduledShift);
                    }
                }
            }

            FactoryShift preparingShift = isFactoryOpen
                ? FindPreparingFactoryShiftAtHour(hour)
                : null;
            if (preparingShift != null && IsOutputStorageFull())
            {
                preparingShift = null;
            }
            SetPreparingFactoryShift(preparingShift);
            RemoveStaleFactoryPopAssignments();
            ReleaseExcessFactoryPopAssignments();
            RefreshCurrentWorkingPops();
            AllocateInputsForCurrentWorkers();
            RecalculatePopAssignmentTotals();
        }

        finally
        {
            _synchronizingFactoryRuntime = false;
        }

        if (!publishChanges)
        {
            return;
        }

        RequestResourceTransportDispatch();

        if (_preparingFactoryShift != null &&
            GetAssignedPopsForShift(_preparingFactoryShift) <
            _preparingFactoryShift.DesiredPops)
        {
            RequestHousingPopDispatch();
        }

        NotifyFactoryRuntimeChanged();
    }

    private void HandleGameHourChanged(int currentHour)
    {
        if (!(data is ResourceProductionBuildingData)) return;

        long absoluteHour = GetCurrentAbsoluteHour();
        if (_lastHandledFactoryHour < 0L)
        {
            _lastHandledFactoryHour = absoluteHour - 1L;
        }

        if (_currentFactoryShift != null)
        {
            ProcessCurrentFactoryShiftHour();
            bool continues = isFactoryOpen &&
                             _currentFactoryShift.IsActive &&
                             ShiftContainsHour(
                                 _currentFactoryShift.StartHour,
                                 FactoryShiftDurationHours,
                                 currentHour);
            if (!continues)
            {
                FinishCurrentFactoryShift();
            }
            else
            {
                RefreshCurrentWorkingPops();
                AllocateInputsForCurrentWorkers();
            }
        }

        _lastHandledFactoryHour = absoluteHour;
        SynchronizeFactoryRuntimeAtCurrentTime();
    }

    private void StartFactoryShift(FactoryShift shift)
    {
        _currentFactoryShift = shift;
        _currentShiftElapsedHours = GetElapsedShiftHours(
            shift,
            GameTimeManager.Instance != null
                ? GameTimeManager.Instance.CurrentHour
                : shift.StartHour);
        _currentShiftAllocatedInputs.Clear();
        _currentShiftProcessedInputs.Clear();
        _currentShiftProcessedInputExact.Clear();
        _currentShiftHourlyInputRequirements.Clear();
        _currentShiftPendingOutputs.Clear();
        _currentShiftAccumulatedProductionHours = 0f;
        RefreshCurrentWorkingPops();
        AllocateInputsForCurrentWorkers();
    }

    private void ProcessCurrentFactoryShiftHour()
    {
        if (_currentFactoryShift == null) return;

        int duration = Mathf.Max(1, FactoryShiftDurationHours);
        _currentShiftElapsedHours = Mathf.Min(
            duration,
            _currentShiftElapsedHours + 1);

        int maximumPops = GetTargetPopCount();
        float workerFactor = maximumPops <= 0
            ? 1f
            : Mathf.Clamp01((float)_currentWorkingPops / maximumPops);
        float inputCoverage = 1f;
        bool hasRequiredInput = false;
        _currentShiftHourlyInputRequirements.Clear();

        if (data is ResourceProductionBuildingData productionData &&
            productionData.resources != null)
        {
            for (int i = 0; i < productionData.resources.Count; i++)
            {
                ProductionResourceEntry entry =
                    productionData.resources[i];
                if (entry.direction != ProductionResourceDirection.Input ||
                    entry.amountAtMaximumEfficiency <= 0)
                {
                    continue;
                }

                float requiredThisHour =
                    entry.amountAtMaximumEfficiency *
                    workerFactor / duration;
                if (requiredThisHour <= Mathf.Epsilon) continue;
                hasRequiredInput = true;

                int allocated = GetResourceAmount(
                    _currentShiftAllocatedInputs,
                    entry.resourceType);
                _currentShiftProcessedInputExact.TryGetValue(
                    entry.resourceType,
                    out float processedExact);
                float available = Mathf.Max(0f, allocated - processedExact);
                inputCoverage = Mathf.Min(
                    inputCoverage,
                    Mathf.Clamp01(available / requiredThisHour));
                _currentShiftHourlyInputRequirements.Add(
                    new HourlyInputRequirement
                {
                    resourceType = entry.resourceType,
                    amount = requiredThisHour
                });
            }

            // Consume every ingredient at the same recipe coverage. A
            // shortage of one input must not waste the other inputs while
            // producing less (or nothing) that hour.
            for (int i = 0;
                 i < _currentShiftHourlyInputRequirements.Count;
                 i++)
            {
                HourlyInputRequirement requirement =
                    _currentShiftHourlyInputRequirements[i];
                int allocated = GetResourceAmount(
                    _currentShiftAllocatedInputs,
                    requirement.resourceType);
                _currentShiftProcessedInputExact.TryGetValue(
                    requirement.resourceType,
                    out float processedExact);
                processedExact = Mathf.Min(
                    allocated,
                    processedExact + requirement.amount * inputCoverage);
                _currentShiftProcessedInputExact[requirement.resourceType] =
                    processedExact;
                SetResourceAmount(
                    _currentShiftProcessedInputs,
                    requirement.resourceType,
                    Mathf.Min(allocated, Mathf.CeilToInt(processedExact)));
            }

            float productionFactor = workerFactor *
                                     (hasRequiredInput
                                         ? inputCoverage
                                         : 1f);
            _currentShiftAccumulatedProductionHours = Mathf.Min(
                duration,
                _currentShiftAccumulatedProductionHours + productionFactor);

            for (int i = 0; i < productionData.resources.Count; i++)
            {
                ProductionResourceEntry entry =
                    productionData.resources[i];
                if (entry.direction != ProductionResourceDirection.Output)
                {
                    continue;
                }

                int cumulativeOutput = Mathf.RoundToInt(
                    entry.amountAtMaximumEfficiency *
                    (_currentShiftAccumulatedProductionHours / duration));
                SetResourceAmount(
                    _currentShiftPendingOutputs,
                    entry.resourceType,
                    cumulativeOutput);
            }
        }

        NotifyFactoryRuntimeChanged();
    }

    private void FinishCurrentFactoryShift()
    {
        FactoryShift finishedShift = _currentFactoryShift;
        if (finishedShift == null) return;

        // Produced outputs remain unavailable to industrial vehicles until
        // this deposit point. Early closure also returns unprocessed inputs.
        for (int i = 0; i < _currentShiftPendingOutputs.Count; i++)
        {
            ResourceAmount output = _currentShiftPendingOutputs[i];
            DepositProducedOutput(output.resourceType, output.amount);
        }

        // Hourly worker/resource availability can leave part of the amount
        // reserved for the shift unused. Return that remainder on both normal
        // completion and early interruption.
        for (int i = 0; i < _currentShiftAllocatedInputs.Count; i++)
        {
            ResourceAmount allocated = _currentShiftAllocatedInputs[i];
            int processed = GetResourceAmount(
                _currentShiftProcessedInputs,
                allocated.resourceType);
            int unprocessed = Mathf.Max(0, allocated.amount - processed);
            if (unprocessed > 0)
            {
                int current = GetResourceAmount(
                    factoryStorage,
                    allocated.resourceType);
                SetResourceAmount(
                    factoryStorage,
                    allocated.resourceType,
                    current + unprocessed);
            }
        }

        _currentFactoryShift = null;
        _currentShiftElapsedHours = 0;
        _currentWorkingPops = 0;
        _currentShiftAllocatedInputs.Clear();
        _currentShiftProcessedInputs.Clear();
        _currentShiftProcessedInputExact.Clear();
        _currentShiftHourlyInputRequirements.Clear();
        _currentShiftPendingOutputs.Clear();
        _currentShiftAccumulatedProductionHours = 0f;
        ReleasePopAssignmentsForShift(finishedShift);
    }

    private void AllocateInputsForCurrentWorkers()
    {
        if (_currentFactoryShift == null ||
            !(data is ResourceProductionBuildingData productionData) ||
            productionData.resources == null)
        {
            return;
        }

        int maximumPops = GetTargetPopCount();
        float workerFactor = maximumPops <= 0
            ? 1f
            : Mathf.Clamp01((float)_currentWorkingPops / maximumPops);
        int duration = Mathf.Max(1, FactoryShiftDurationHours);
        int remainingHours = Mathf.Max(
            0,
            duration - _currentShiftElapsedHours);

        for (int i = 0; i < productionData.resources.Count; i++)
        {
            ProductionResourceEntry entry = productionData.resources[i];
            if (entry.direction != ProductionResourceDirection.Input)
            {
                continue;
            }

            _currentShiftProcessedInputExact.TryGetValue(
                entry.resourceType,
                out float processedExact);
            float projectedRemaining =
                entry.amountAtMaximumEfficiency *
                workerFactor *
                (remainingHours / (float)duration);
            int targetAllocation = Mathf.Min(
                entry.amountAtMaximumEfficiency,
                Mathf.CeilToInt(processedExact + projectedRemaining));
            int allocated = GetResourceAmount(
                _currentShiftAllocatedInputs,
                entry.resourceType);

            // The shift value controls how much preparation should deliver.
            // It does not limit production: once the shift is running, use
            // whatever is already in storage up to the worker-scaled maximum.
            int additional = Mathf.Max(0, targetAllocation - allocated);
            if (additional <= 0) continue;

            int available = GetFactoryStoredAmount(entry.resourceType);
            int withdrawn = Mathf.Min(additional, available);
            if (withdrawn <= 0) continue;

            SetResourceAmount(
                factoryStorage,
                entry.resourceType,
                available - withdrawn);
            SetResourceAmount(
                _currentShiftAllocatedInputs,
                entry.resourceType,
                allocated + withdrawn);
        }
    }

    private void DepositProducedOutput(
        ResourceType resourceType,
        int amount)
    {
        if (amount <= 0) return;
        EnsureFactoryStorage();
        int current = GetResourceAmount(factoryStorage, resourceType);
        SetResourceAmount(factoryStorage, resourceType, current + amount);
        NotifyFactoryRuntimeChanged();
    }

    private bool IsOutputStorageFull()
    {
        if (!(data is ResourceProductionBuildingData productionData) ||
            productionData.resources == null)
        {
            return false;
        }

        EnsureFactoryStorage();
        for (int i = 0; i < productionData.resources.Count; i++)
        {
            ProductionResourceEntry entry = productionData.resources[i];
            if (entry.direction == ProductionResourceDirection.Output &&
                entry.amountAtMaximumEfficiency > 0 &&
                GetFactoryStoredAmount(entry.resourceType) >=
                entry.storageCapacity)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasProductionResourceDirection(
        ResourceType resourceType,
        ProductionResourceDirection direction)
    {
        return TryGetProductionResource(resourceType, direction, out _);
    }

    private void RebuildCurrentShiftInputRemainders()
    {
        _currentShiftInputRemainders.Clear();
        for (int i = 0; i < _currentShiftAllocatedInputs.Count; i++)
        {
            ResourceAmount allocated = _currentShiftAllocatedInputs[i];
            int processed = GetResourceAmount(
                _currentShiftProcessedInputs,
                allocated.resourceType);
            _currentShiftInputRemainders.Add(new ResourceAmount
            {
                resourceType = allocated.resourceType,
                amount = Mathf.Max(0, allocated.amount - processed)
            });
        }
    }

    private FactoryShift FindEnabledFactoryShiftAtHour(int hour)
    {
        for (int i = 0; i < factoryShifts.Count; i++)
        {
            FactoryShift shift = factoryShifts[i];
            if (shift != null && shift.IsActive &&
                ShiftContainsHour(
                    shift.StartHour,
                    FactoryShiftDurationHours,
                    hour))
            {
                return shift;
            }
        }

        return null;
    }

    private FactoryShift FindPreparingFactoryShiftAtHour(int hour)
    {
        for (int i = 0; i < factoryShifts.Count; i++)
        {
            FactoryShift shift = factoryShifts[i];
            if (shift == null || !shift.IsActive) continue;

            int preparationStart = shift.GetPreparationStartHour();
            int offset = FactoryShift.WrapHour(hour - preparationStart);
            if (offset < shift.PreparationHours)
            {
                return shift;
            }
        }

        return null;
    }

    private void SetPreparingFactoryShift(FactoryShift shift)
    {
        if (_preparingFactoryShift == shift) return;

        FactoryShift previous = _preparingFactoryShift;
        _preparingFactoryShift = shift;
        if (previous != null && previous != _currentFactoryShift)
        {
            ReleasePopAssignmentsForShift(previous);
        }

        RequestHousingPopDispatch();
    }

    private void RefreshCurrentWorkingPops()
    {
        int working = 0;
        long currentHour = GetCurrentAbsoluteHour();
        foreach (KeyValuePair<VehicleAI, FactoryPopAssignment> pair in
                 _factoryPopAssignments)
        {
            FactoryPopAssignment assignment = pair.Value;
            if (assignment != null && assignment.delivered &&
                assignment.shift == _currentFactoryShift &&
                assignment.eligibleFromHour <= currentHour)
            {
                working += assignment.popCount;
            }
        }

        int desired = _currentFactoryShift != null
            ? _currentFactoryShift.DesiredPops
            : 0;
        _currentWorkingPops = Mathf.Clamp(working, 0, desired);
    }

    private void RemoveStaleFactoryPopAssignments()
    {
        var staleVehicles = new List<VehicleAI>();
        foreach (KeyValuePair<VehicleAI, FactoryPopAssignment> pair in
                 _factoryPopAssignments)
        {
            FactoryPopAssignment assignment = pair.Value;
            if (pair.Key == null || assignment == null ||
                (assignment.shift != _preparingFactoryShift &&
                 assignment.shift != _currentFactoryShift))
            {
                staleVehicles.Add(pair.Key);
            }
        }

        for (int i = 0; i < staleVehicles.Count; i++)
        {
            VehicleAI vehicle = staleVehicles[i];
            _factoryPopAssignments.Remove(vehicle);
            if (vehicle != null)
            {
                vehicle.ReleasePopDeliveryFromFactory(this);
            }
        }
    }

    private void ReleaseExcessFactoryPopAssignments()
    {
        if (_preparingFactoryShift != null)
        {
            ReleaseExcessPopAssignmentsForShift(_preparingFactoryShift);
        }

        if (_currentFactoryShift != null &&
            _currentFactoryShift != _preparingFactoryShift)
        {
            ReleaseExcessPopAssignmentsForShift(_currentFactoryShift);
        }
    }

    private void ReleaseExcessPopAssignmentsForShift(FactoryShift shift)
    {
        int desiredPops = shift != null ? shift.DesiredPops : 0;
        int keptPops = 0;
        var vehiclesToRelease = new List<VehicleAI>();

        // Keep vehicles that have already arrived before retaining vehicles
        // which are still on the road. Vehicle loads are indivisible, so the
        // final retained vehicle may take the total slightly over the target.
        for (int pass = 0; pass < 2; pass++)
        {
            bool keepDeliveredThisPass = pass == 0;
            foreach (KeyValuePair<VehicleAI, FactoryPopAssignment> pair in
                     _factoryPopAssignments)
            {
                FactoryPopAssignment assignment = pair.Value;
                if (assignment == null ||
                    assignment.shift != shift ||
                    assignment.delivered != keepDeliveredThisPass)
                {
                    continue;
                }

                if (keptPops < desiredPops)
                {
                    keptPops += Mathf.Max(0, assignment.popCount);
                }
                else
                {
                    vehiclesToRelease.Add(pair.Key);
                }
            }
        }

        for (int i = 0; i < vehiclesToRelease.Count; i++)
        {
            VehicleAI vehicle = vehiclesToRelease[i];
            _factoryPopAssignments.Remove(vehicle);
            if (vehicle != null)
            {
                vehicle.ReleasePopDeliveryFromFactory(this);
            }
        }
    }

    private void ReleasePopAssignmentsForShift(FactoryShift shift)
    {
        if (shift == null) return;

        var vehicles = new List<VehicleAI>();
        foreach (KeyValuePair<VehicleAI, FactoryPopAssignment> pair in
                 _factoryPopAssignments)
        {
            if (pair.Value != null && pair.Value.shift == shift)
            {
                vehicles.Add(pair.Key);
            }
        }

        for (int i = 0; i < vehicles.Count; i++)
        {
            VehicleAI vehicle = vehicles[i];
            if (vehicle != null)
            {
                vehicle.ReleasePopDeliveryFromFactory(this);
            }
            _factoryPopAssignments.Remove(vehicle);
        }

        RecalculatePopAssignmentTotals();
    }

    private void ReleaseAllFactoryPopAssignments(bool returnVehiclesHome = true)
    {
        var vehicles = new List<VehicleAI>(_factoryPopAssignments.Keys);
        for (int i = 0; i < vehicles.Count; i++)
        {
            VehicleAI vehicle = vehicles[i];
            if (vehicle != null)
            {
                vehicle.ReleasePopDeliveryFromFactory(
                    this,
                    returnVehiclesHome);
            }
        }

        _factoryPopAssignments.Clear();
        RecalculatePopAssignmentTotals();
    }

    private void RemoveMissingPopAssignments()
    {
        var missing = new List<VehicleAI>();
        foreach (KeyValuePair<VehicleAI, FactoryPopAssignment> pair in
                 _factoryPopAssignments)
        {
            if (pair.Key == null) missing.Add(pair.Key);
        }

        for (int i = 0; i < missing.Count; i++)
        {
            _factoryPopAssignments.Remove(missing[i]);
        }

        if (missing.Count > 0)
        {
            RecalculatePopAssignmentTotals();
            RefreshCurrentWorkingPops();
            RequestHousingPopDispatch();
        }
    }

    private void RecalculatePopAssignmentTotals()
    {
        int reserved = 0;
        int delivered = 0;
        foreach (FactoryPopAssignment assignment in
                 _factoryPopAssignments.Values)
        {
            if (assignment == null) continue;
            if (assignment.delivered)
            {
                delivered += assignment.popCount;
            }
            else
            {
                reserved += assignment.popCount;
            }
        }

        ReservedIncomingPops = Mathf.Max(0, reserved);
        DeliveredPops = Mathf.Max(0, delivered);
    }

    private int GetAssignedPopsForShift(FactoryShift shift)
    {
        if (shift == null) return 0;
        int count = 0;
        foreach (FactoryPopAssignment assignment in
                 _factoryPopAssignments.Values)
        {
            if (assignment != null && assignment.shift == shift)
            {
                count += assignment.popCount;
            }
        }
        return count;
    }

    private int GetReservedPopsForShift(FactoryShift shift)
    {
        if (shift == null) return 0;
        int count = 0;
        foreach (FactoryPopAssignment assignment in
                 _factoryPopAssignments.Values)
        {
            if (assignment != null && !assignment.delivered &&
                assignment.shift == shift)
            {
                count += assignment.popCount;
            }
        }
        return count;
    }

    private int GetDeliveredPopsForShift(
        FactoryShift shift,
        bool onlyIneligible)
    {
        if (shift == null) return 0;
        int count = 0;
        long currentHour = GetCurrentAbsoluteHour();
        foreach (FactoryPopAssignment assignment in
                 _factoryPopAssignments.Values)
        {
            if (assignment == null || !assignment.delivered ||
                assignment.shift != shift ||
                (onlyIneligible &&
                 assignment.eligibleFromHour <= currentHour))
            {
                continue;
            }
            count += assignment.popCount;
        }
        return count;
    }

    private int GetElapsedShiftHours(FactoryShift shift, int currentHour)
    {
        return shift != null
            ? Mathf.Clamp(
                FactoryShift.WrapHour(currentHour - shift.StartHour),
                0,
                FactoryShiftDurationHours)
            : 0;
    }

    private static int GetResourceAmount(
        List<ResourceAmount> resources,
        ResourceType resourceType)
    {
        if (resources == null) return 0;
        for (int i = 0; i < resources.Count; i++)
        {
            if (resources[i].resourceType == resourceType)
            {
                return resources[i].amount;
            }
        }
        return 0;
    }

    private static void SetResourceAmount(
        List<ResourceAmount> resources,
        ResourceType resourceType,
        int amount)
    {
        if (resources == null) return;
        for (int i = 0; i < resources.Count; i++)
        {
            if (resources[i].resourceType != resourceType) continue;
            resources[i] = new ResourceAmount
            {
                resourceType = resourceType,
                amount = Mathf.Max(0, amount)
            };
            return;
        }

        resources.Add(new ResourceAmount
        {
            resourceType = resourceType,
            amount = Mathf.Max(0, amount)
        });
    }

    private long GetCurrentAbsoluteHour()
    {
        GameTimeManager time = GameTimeManager.Instance;
        return time != null
            ? ((long)Mathf.Max(1, time.CurrentDay) - 1L) *
              GameTimeManager.HoursPerDay + time.CurrentHour
            : 0L;
    }

    private void RequestHousingPopDispatch()
    {
        if (BuildingSystemBackend.Instance != null)
        {
            BuildingSystemBackend.Instance.RequestPopTransportDispatch();
        }
    }

    private void NotifyFactoryRuntimeChanged()
    {
        FactoryRuntimeChanged?.Invoke(this);
    }

    private IEnumerator WaitForGameTimeSubscription()
    {
        while (isActiveAndEnabled && GameTimeManager.Instance == null)
        {
            yield return null;
        }

        _gameTimeSubscriptionCoroutine = null;
        TrySubscribeToGameTime();
        SynchronizeFactoryRuntimeAtCurrentTime();
    }

    private bool TrySubscribeToGameTime()
    {
        if (_subscribedToGameTime || GameTimeManager.Instance == null)
        {
            return _subscribedToGameTime;
        }

        GameTimeManager.Instance.OnHourChanged += HandleGameHourChanged;
        _subscribedToGameTime = true;
        return true;
    }

    private void UnsubscribeFromGameTime()
    {
        if (_subscribedToGameTime && GameTimeManager.Instance != null)
        {
            GameTimeManager.Instance.OnHourChanged -= HandleGameHourChanged;
        }
        _subscribedToGameTime = false;
    }

    public int GetRemainingUnassignedPopCapacity()
    {
        SynchronizeFactoryRuntimeAtCurrentTime(false);
        FactoryShift shift = _preparingFactoryShift;
        int targetPops = shift != null ? shift.DesiredPops : 0;
        if (targetPops <= 0) return 0;
        return Mathf.Max(
            0,
            targetPops - GetAssignedPopsForShift(shift));
    }

    public float GetProductionEfficiency()
    {
        return CurrentFactoryEfficiency;
    }

    public bool TryReserveIncomingPops(
        VehicleAI vehicle,
        int requestedPops,
        out int reservedPops)
    {
        reservedPops = 0;
        SynchronizeFactoryRuntimeAtCurrentTime(false);
        FactoryShift shift = _preparingFactoryShift;
        if (vehicle == null || requestedPops <= 0 || shift == null)
        {
            return false;
        }

        ReleasePopAssignment(vehicle);

        int remainingCapacity = GetRemainingUnassignedPopCapacity();
        if (remainingCapacity <= 0) return false;

        reservedPops = Mathf.Min(requestedPops, remainingCapacity);
        _factoryPopAssignments[vehicle] = new FactoryPopAssignment
        {
            shift = shift,
            popCount = reservedPops,
            delivered = false,
            eligibleFromHour = long.MaxValue
        };
        RecalculatePopAssignmentTotals();
        NotifyFactoryRuntimeChanged();
        return true;
    }

    public void MarkIncomingPopsDelivered(VehicleAI vehicle)
    {
        if (vehicle == null ||
            !_factoryPopAssignments.TryGetValue(
                vehicle,
                out FactoryPopAssignment assignment))
        {
            return;
        }

        long currentHour = GetCurrentAbsoluteHour();
        bool isPreparing = assignment.shift == _preparingFactoryShift;
        bool isCurrent = assignment.shift == _currentFactoryShift;
        if (!isPreparing && !isCurrent)
        {
            ReleasePopAssignment(vehicle);
            vehicle.ReleasePopDeliveryFromFactory(this);
            return;
        }

        assignment.delivered = true;
        assignment.eligibleFromHour = isCurrent
            ? currentHour + 1L
            : currentHour;
        RecalculatePopAssignmentTotals();
        NotifyFactoryRuntimeChanged();
    }

    internal bool ShouldKeepPopTransportVehicleAtFactory(VehicleAI vehicle)
    {
        if (vehicle == null ||
            !_factoryPopAssignments.TryGetValue(
                vehicle,
                out FactoryPopAssignment assignment) ||
            assignment == null ||
            assignment.popCount <= 0)
        {
            return false;
        }

        return assignment.shift == _preparingFactoryShift ||
               assignment.shift == _currentFactoryShift;
    }

    public void ReleasePopAssignment(VehicleAI vehicle)
    {
        if (vehicle == null) return;
        if (!_factoryPopAssignments.Remove(vehicle)) return;

        RecalculatePopAssignmentTotals();
        RefreshCurrentWorkingPops();
        NotifyFactoryRuntimeChanged();

        // During preparation, a removed house/vehicle leaves a vacancy which
        // another available housing vehicle may fill. Once the shift starts,
        // GetRemainingUnassignedPopCapacity remains zero for that shift.
        RequestHousingPopDispatch();
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
