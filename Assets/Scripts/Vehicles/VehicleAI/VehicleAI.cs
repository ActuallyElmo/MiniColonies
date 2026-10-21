using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class VehicleAI : MonoBehaviour
{
    public enum VehicleState { Idle, Outbound, Inbound, AtDestination }
    
    [Header("State Data")]
    public VehicleState currentState = VehicleState.Idle;
    public Building homeBuilding;
    public Building targetBuilding;
    public VehicleData vehicleData;

    [Header("Conveyor Navigation")]
    public TrafficEdge currentEdge;
    public float conveyorDistanceOnEdge;
    public TrafficRoute conveyorRoute;
    public int conveyorRouteEdgeIndex;
    public float conveyorCurrentSpeed;
    public bool isConveyorMoving;
    [HideInInspector] public bool trafficWasBlocked;
    [HideInInspector] public float trafficReleaseTime;
    [HideInInspector] public float trafficStationaryTime;
    [HideInInspector] public VehicleSimulationId simulationId;
    [HideInInspector] public float conveyorPreviousAccelerationUnitsPerSecondSquared;
    [HideInInspector] public TacticalLaneDecision lastTacticalLaneDecision;
    [HideInInspector] public VehicleSimulationId followingReactionLeaderId;
    [HideInInspector] public float followingReactionReleaseTime;
    [HideInInspector] public float followingReactionObservedLeaderSpeed;
    [HideInInspector] public Vector3 conveyorPreviousVisualPosition;
    [HideInInspector] public Vector3 conveyorCurrentVisualPosition;
    [HideInInspector] public Quaternion conveyorPreviousVisualRotation = Quaternion.identity;
    [HideInInspector] public Quaternion conveyorCurrentVisualRotation = Quaternion.identity;
    [HideInInspector] public bool hasConveyorVisualPose;

    private const float DefaultAcceleration = 12f;
    private const float DefaultDeceleration = 15f;
    private const float DefaultEmergencyDeceleration = 30f;
    private const float DefaultMaximumJerk = 30f;
    private const float DefaultDriverReactionTime = 0.25f;
    private const float VehicleRoadClearance = 0.03f;
    private const int MaxReturnHomeRouteFailures = 3;
    private const float ReturnHomeRetryDelaySeconds = 0.75f;
    private static ulong _nextSimulationSequence = 1UL;
    private static readonly HashSet<VehicleAI> _vehiclesWaitingForReturn =
        new HashSet<VehicleAI>();

    private Building _pendingTargetBuilding;
    private RoadNetwork _pendingNetwork;
    private bool _hasPendingAssignment;
    private RoadNetwork _activeNetwork;
    private Coroutine _enterRouteRetryCoroutine;
    private Coroutine _returnHomeRetryCoroutine;
    private Building _assignedPopFactory;
    private int _assignedPopCount;
    private bool _assignedPopsDelivered;
    private bool _popReturnRequested;
    private int _returnHomeRouteFailures;
    private bool _isDebugOneWayTrip;
    private Coroutine _debugRouteSearch;
    private TrafficSystemBackend _debugRoutingBackend;
    private int _debugRouteGeneration;

    [Header("Resource Transport Assignment")]
    [SerializeField] private bool hasAssignedResourceType;
    [SerializeField] private ResourceType assignedResourceType;
    [SerializeField] private int assignedResourceAmount;
    private Building assignedResourceFactory;
    private bool hasPendingResourceAssignment;
    private ResourceType pendingResourceType;
    private bool pendingResourceAssigned;

    public VehicleSimulationId SimulationId => simulationId;
    public int AssignedPopCount => _assignedPopCount;
    public bool HasAssignedResourceType => hasAssignedResourceType;
    public ResourceType AssignedResourceType => assignedResourceType;
    public int AssignedResourceAmount => assignedResourceAmount;
    public bool HasAssignedResourceCargo => assignedResourceAmount > 0;
    public bool IsDebugVehicle => _isDebugOneWayTrip;
    public bool CanReceivePopTransportAssignment =>
        currentState == VehicleState.Idle &&
        !isConveyorMoving &&
        _assignedPopFactory == null;

    public VehicleSimulationId EnsureSimulationId()
    {
        if (!simulationId.IsValid)
        {
            string vehicleKey = vehicleData != null
                ? vehicleData.vehicleName
                : name;
            simulationId = VehicleSimulationId.FromStableKey(
                $"{vehicleKey}:{_nextSimulationSequence++}");
        }

        return simulationId;
    }

    public void Initialize(Building home, VehicleData data)
    {
        homeBuilding = home;
        vehicleData = data;
        gameObject.SetActive(false); 
    }

    public void InitializeDebugVehicle(PopTransportVehicleData data)
    {
        homeBuilding = null;
        targetBuilding = null;
        vehicleData = data;
        currentState = VehicleState.Outbound;
        _activeNetwork = null;
        _assignedPopFactory = null;
        _assignedPopCount = 0;
        _assignedPopsDelivered = false;
        _popReturnRequested = false;
        _isDebugOneWayTrip = true;
        gameObject.SetActive(true);
    }

    public void BeginDebugFactoryTrip()
    {
        if (!_isDebugOneWayTrip || vehicleData == null || !isActiveAndEnabled) return;
        StopDebugRouteSearch();
        _debugRoutingBackend = TrafficSystemBackend.Instance;
        if (_debugRoutingBackend != null)
            _debugRoutingBackend.OnTrafficNetworkReady += RetryDebugRouteAfterPublication;
        currentState = VehicleState.Outbound;
        _debugRouteSearch = StartCoroutine(FindDebugFactoryRoute(_debugRouteGeneration));
    }

    private IEnumerator FindDebugFactoryRoute(int generation)
    {
        // Pathfinding is infrastructure work; wait on its callback in real frames,
        // including while paused. Vehicle approach and driving use simulation time.
        yield return null;
        var graph = _debugRoutingBackend != null ? _debugRoutingBackend.CurrentTrafficGraphSnapshot : null;
        var destinations = DebugPopVehicleSpawner.FindConnectedFactoryDestinations(
            BuildingSystemBackend.Instance != null ? BuildingSystemBackend.Instance.GetActiveBuildings() : null,
            GetPathingOrigin(), vehicleData, WorldManager.Instance != null ? WorldManager.Instance.cellSize : 1f,
            graph?.BuildingPortAnchors);
        for (int i = 0; i < destinations.Count; i++)
        {
            // Even a synchronous rejection (for example disabled pathfinding)
            // must not process every factory in one frame.
            yield return null;
            var destination = destinations[i];
            if (destination.Factory == null || VehiclePathRequestManager.Instance == null) continue;
            bool complete = false;
            TrafficRoute result = null;
            VehiclePathRequestManager.Instance.RequestPath(this, GetWorldPositionOfPort(destination.EntryPort),
                route =>
                {
                    if (this == null || generation != _debugRouteGeneration) return;
                    result = route;
                    complete = true;
                }, false, default, false, destination.EntryPort, true);
            while (!complete) yield return null;
            if (_debugRoutingBackend == null || graph != _debugRoutingBackend.CurrentTrafficGraphSnapshot) break;
            if (destination.Factory == null || result == null || result.ManagedEdges == null ||
                result.ManagedEdges.Count == 0) continue;
            targetBuilding = destination.Factory;
            _debugRouteSearch = null;
            StopDebugRouteSearch();
            StartConveyorRoute(result);
            yield break;
        }
        _debugRouteSearch = null;
        if (_debugRoutingBackend != null && graph != _debugRoutingBackend.CurrentTrafficGraphSnapshot)
        {
            BeginDebugFactoryTrip();
            yield break;
        }
        const string message = "Debug car: no reachable factory entrance. Connect a factory to this road; " +
            "the car will retry when the road network updates.";
        Debug.LogWarning(message, this);
        TooltipManager.Show(message, this);
    }

    private void RetryDebugRouteAfterPublication()
    {
        if (_debugRouteSearch == null && _isDebugOneWayTrip && isActiveAndEnabled)
            BeginDebugFactoryTrip();
    }

    private void StopDebugRouteSearch()
    {
        _debugRouteGeneration++;
        if (_debugRouteSearch != null) StopCoroutine(_debugRouteSearch);
        _debugRouteSearch = null;
        if (_debugRoutingBackend != null)
            _debugRoutingBackend.OnTrafficNetworkReady -= RetryDebugRouteAfterPublication;
        _debugRoutingBackend = null;
        TooltipManager.Hide(this);
    }

    public void DestroyDebugVehicle()
    {
        if (!_isDebugOneWayTrip) return;
        StopDebugRouteSearch();

        if (ConveyorTrafficManager.Instance != null)
        {
            ConveyorTrafficManager.Instance.LeaveTraffic(this);
        }

        _isDebugOneWayTrip = false;
        StopAllCoroutines();
        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
    }

    internal void SetAssignedResourceType(ResourceType resourceType)
    {
        if (currentState != VehicleState.Idle ||
            isConveyorMoving ||
            assignedResourceAmount > 0)
        {
            pendingResourceType = resourceType;
            pendingResourceAssigned = true;
            hasPendingResourceAssignment = true;
            return;
        }

        assignedResourceType = resourceType;
        hasAssignedResourceType = true;
    }

    internal void ClearAssignedResourceType()
    {
        if (currentState != VehicleState.Idle ||
            isConveyorMoving ||
            assignedResourceAmount > 0)
        {
            pendingResourceAssigned = false;
            hasPendingResourceAssignment = true;
            return;
        }

        hasAssignedResourceType = false;
        assignedResourceAmount = 0;
        assignedResourceFactory = null;
    }

    internal void AssignResourceCargo(Building destination, ResourceType type, int amount)
    { assignedResourceFactory = destination; assignedResourceType = type; hasAssignedResourceType = true; assignedResourceAmount = Mathf.Max(0, amount); }

    private void ApplyPendingResourceAssignment()
    {
        if (!hasPendingResourceAssignment) return;

        hasAssignedResourceType = pendingResourceAssigned;
        if (pendingResourceAssigned)
        {
            assignedResourceType = pendingResourceType;
        }

        hasPendingResourceAssignment = false;
    }

    public Vector3 GetPathingOrigin()
    {
        if (isConveyorMoving && currentEdge != null)
        {
            return currentEdge.GetPositionAtDistance(conveyorDistanceOnEdge);
        }

        return transform.position;
    }

    public void DispatchTo(Building target, RoadNetwork network)
    {
        targetBuilding = target;
        _activeNetwork = network;
        currentState = VehicleState.Outbound;
        
        if (!TryGetRoutePorts(homeBuilding, targetBuilding, targetBuilding.originCell, out Vector2Int exitPort, out Vector2Int entryPort))
        {
            Debug.LogWarning($"{GetVehicleDebugName()} could not dispatch because a compatible outbound port is unavailable. {DescribeRoutePortFailure(homeBuilding, targetBuilding)}");
            RecallAndDeactivate();
            return;
        }

        TeleportToPort(exitPort);
        gameObject.SetActive(true);

        Vector3 targetWorldPos = GetWorldPositionOfPort(entryPort);
        RequestConveyorRoute(targetWorldPos, false, true, exitPort, entryPort);
    }

    public bool ReturnHome()
    {
        if (TryBeginReturnHome())
        {
            return true;
        }

        StartReturnHomeRetry();
        return false;
    }

    private bool TryBeginReturnHome()
    {
        if (isConveyorMoving)
        {
            return false;
        }

        RefreshActiveNetworkAfterGraphRebuild();

        if (!TryGetRoutePorts(targetBuilding, homeBuilding, homeBuilding.originCell, out Vector2Int exitPort, out Vector2Int entryPort))
        {
            Debug.LogWarning($"{GetVehicleDebugName()} is waiting to return home because a compatible inbound port is unavailable. {DescribeRoutePortFailure(targetBuilding, homeBuilding)}");
            return false;
        }

        currentState = VehicleState.Inbound;
        TeleportToPort(exitPort);

        Vector3 targetWorldPos = GetWorldPositionOfPort(entryPort);
        RequestConveyorRoute(targetWorldPos, false, true, exitPort, entryPort);
        return true;
    }

    public void Reroute(Building newTarget, RoadNetwork network)
    {
        if (isConveyorMoving)
        {
            _pendingTargetBuilding = newTarget;
            _pendingNetwork = network;
            _hasPendingAssignment = true;
            return;
        }

        if (currentState == VehicleState.Idle || !gameObject.activeSelf)
        {
            if (newTarget != null) DispatchTo(newTarget, network);
            return;
        }

        targetBuilding = newTarget;
        _activeNetwork = network; 
        Vector2Int destinationPort;

        Vector2Int currentCell = new Vector2Int(
            Mathf.FloorToInt(transform.position.x / WorldManager.Instance.cellSize), 
            Mathf.FloorToInt(transform.position.z / WorldManager.Instance.cellSize)
        );

        if (currentState == VehicleState.Outbound)
        {
            if (targetBuilding != null)
            {
                if (!targetBuilding.TryGetClosestPort(PortFlowDirection.Entry, _activeNetwork, currentCell, vehicleData, out destinationPort))
                {
                    RecallAndDeactivate();
                    return;
                }
            }
            else
            {
                currentState = VehicleState.Inbound;
                if (!homeBuilding.TryGetClosestPort(PortFlowDirection.Entry, _activeNetwork, currentCell, vehicleData, out destinationPort))
                {
                    RecallAndDeactivate();
                    return;
                }
            }
        }
        else 
        {
            if (!homeBuilding.TryGetClosestPort(PortFlowDirection.Entry, _activeNetwork, currentCell, vehicleData, out destinationPort))
            {
                RecallAndDeactivate();
                return;
            }
        }

        Vector3 targetWorldPos = GetWorldPositionOfPort(destinationPort);
        
        RequestConveyorRoute(targetWorldPos, true, false, destinationPort);
    }

    public void RecallAndDeactivate()
    {
        if (_isDebugOneWayTrip)
        {
            DestroyDebugVehicle();
            return;
        }

        ReturnResourceCargoToHome();
        if (ConveyorTrafficManager.Instance != null)
        {
            ConveyorTrafficManager.Instance.LeaveTraffic(this);
        }

        if (_enterRouteRetryCoroutine != null)
        {
            StopCoroutine(_enterRouteRetryCoroutine);
            _enterRouteRetryCoroutine = null;
        }

        if (_returnHomeRetryCoroutine != null)
        {
            StopCoroutine(_returnHomeRetryCoroutine);
            _returnHomeRetryCoroutine = null;
        }

        currentState = VehicleState.Idle;
        targetBuilding = null;
        _pendingTargetBuilding = null;
        _pendingNetwork = null;
        _hasPendingAssignment = false;
        hasConveyorVisualPose = false;
        ApplyPendingResourceAssignment();

        if (homeBuilding != null)
        {
            transform.position =
                GetWorldPositionOfPort(homeBuilding.originCell) +
                (Vector3.up * VehicleRoadClearance);
        }

        ReleasePopDeliveryAssignment();
        StopAllCoroutines();
        gameObject.SetActive(false); 
    }

    private void ReturnResourceCargoToHome()
    {
        if (assignedResourceFactory != null)
        {
            assignedResourceFactory.ReleaseIncomingResourceReservation(this);
        }

        int amount = assignedResourceAmount;
        assignedResourceAmount = 0;
        assignedResourceFactory = null;
        if (amount > 0 && homeBuilding != null)
        {
            homeBuilding.AddFactoryResourceAllowOverfill(
                assignedResourceType,
                amount);
        }
    }

    private void OnDisable()
    {
        if (_isDebugOneWayTrip)
        {
            StopDebugRouteSearch();
            if (_enterRouteRetryCoroutine != null)
            {
                StopCoroutine(_enterRouteRetryCoroutine);
                _enterRouteRetryCoroutine = null;
            }
        }
        if (_isDebugOneWayTrip && isConveyorMoving &&
            ConveyorTrafficManager.Instance != null)
        {
            ConveyorTrafficManager.Instance.LeaveTraffic(this);
        }

        if (vehicleData is ResourceTransportVehicleData)
        {
            if (isConveyorMoving && ConveyorTrafficManager.Instance != null)
            {
                ConveyorTrafficManager.Instance.LeaveTraffic(this);
            }

            ReturnResourceCargoToHome();
            if (currentState != VehicleState.Idle)
            {
                currentState = VehicleState.Idle;
                targetBuilding = null;
                _activeNetwork = null;
                _pendingTargetBuilding = null;
                _pendingNetwork = null;
                _hasPendingAssignment = false;
                ApplyPendingResourceAssignment();
            }
        }
    }

    private void OnDestroy()
    {
        StopDebugRouteSearch();
        if (_isDebugOneWayTrip && isConveyorMoving &&
            ConveyorTrafficManager.Instance != null)
        {
            ConveyorTrafficManager.Instance.LeaveTraffic(this);
        }

        if (vehicleData is ResourceTransportVehicleData)
        {
            ReturnResourceCargoToHome();
        }
    }

    public void RequestConveyorRerouteAfterGraphRebuild()
    {
        RequestConveyorRerouteAfterGraphRebuild(false);
    }

    public void RequestConveyorRerouteAfterGraphRebuild(bool wasConveyorMoving)
    {
        if (currentState == VehicleState.Idle || !gameObject.activeInHierarchy)
        {
            return;
        }

        if (!isConveyorMoving &&
            _assignedPopFactory != null &&
            _assignedPopsDelivered)
        {
            if (!_popReturnRequested)
            {
                // Delivered population vehicles wait at the factory until
                // that specific shift releases them.
                return;
            }

            if (wasConveyorMoving)
            {
                ForceConveyorReroute();
            }
            else
            {
                ReturnHome();
            }

            return;
        }

        ForceConveyorReroute();
    }

    public static void NotifyTrafficGraphRebuiltForWaitingVehicles()
    {
        if (_vehiclesWaitingForReturn.Count == 0) return;

        var waitingVehicles = new List<VehicleAI>(_vehiclesWaitingForReturn);
        for (int i = 0; i < waitingVehicles.Count; i++)
        {
            VehicleAI vehicle = waitingVehicles[i];
            if (vehicle == null || !vehicle.gameObject.activeInHierarchy)
            {
                _vehiclesWaitingForReturn.Remove(vehicle);
                continue;
            }

            vehicle.RequestConveyorRerouteAfterGraphRebuild();
        }
    }

    public bool RefreshActiveNetworkAfterGraphRebuild()
    {
        RoadNetworkManager networkManager = RoadNetworkManager.Instance;
        if (networkManager == null)
        {
            return false;
        }

        if (_activeNetwork != null &&
            networkManager.activeNetworks.Contains(_activeNetwork))
        {
            return true;
        }

        RoadNetwork replacement = null;
        RouteCorridor corridor = conveyorRoute != null
            ? conveyorRoute.Corridor
            : null;

        if (corridor != null &&
            corridor.HasTargetPortCell &&
            networkManager.cellToNetwork.TryGetValue(
                corridor.TargetPortCell,
                out RoadNetwork targetPortNetwork) &&
            IsNetworkValidForCurrentTrip(targetPortNetwork))
        {
            replacement = targetPortNetwork;
        }

        if (replacement == null &&
            corridor != null &&
            corridor.HasStartPortCell &&
            networkManager.cellToNetwork.TryGetValue(
                corridor.StartPortCell,
                out RoadNetwork startPortNetwork) &&
            IsNetworkValidForCurrentTrip(startPortNetwork))
        {
            replacement = startPortNetwork;
        }

        if (replacement == null && WorldManager.Instance != null)
        {
            Vector2Int currentCell = new Vector2Int(
                Mathf.FloorToInt(transform.position.x / WorldManager.Instance.cellSize),
                Mathf.FloorToInt(transform.position.z / WorldManager.Instance.cellSize));

            if (networkManager.cellToNetwork.TryGetValue(
                    currentCell,
                    out RoadNetwork currentCellNetwork) &&
                IsNetworkValidForCurrentTrip(currentCellNetwork))
            {
                replacement = currentCellNetwork;
            }
        }

        if (replacement == null && _activeNetwork != null)
        {
            foreach (Vector2Int oldNetworkCell in _activeNetwork.roadCells)
            {
                if (networkManager.cellToNetwork.TryGetValue(
                        oldNetworkCell,
                        out RoadNetwork overlappingNetwork) &&
                    IsNetworkValidForCurrentTrip(overlappingNetwork))
                {
                    replacement = overlappingNetwork;
                    break;
                }
            }
        }

        if (replacement == null && homeBuilding != null)
        {
            foreach (RoadNetwork homeNetwork in homeBuilding.validNetworks)
            {
                if (IsNetworkValidForCurrentTrip(homeNetwork))
                {
                    replacement = homeNetwork;
                    break;
                }
            }
        }

        if (replacement == null)
        {
            return false;
        }

        _activeNetwork = replacement;
        if (_hasPendingAssignment)
        {
            _pendingNetwork = replacement;
        }

        return true;
    }

    private bool IsNetworkValidForCurrentTrip(RoadNetwork network)
    {
        if (network == null)
        {
            return false;
        }

        if (homeBuilding != null &&
            !homeBuilding.validNetworks.Contains(network))
        {
            return false;
        }

        return targetBuilding == null ||
               targetBuilding.validNetworks.Contains(network);
    }

    // --- MOVEMENT LOGIC ---

    private void StartConveyorRoute(TrafficRoute route)
    {
        if (route == null || route.ManagedEdges == null || route.ManagedEdges.Count == 0 || route.ManagedEdges[0] == null)
        {
            Debug.LogWarning($"{vehicleData.vehicleName} received an invalid conveyor route.");
            RecallAndDeactivate();
            return;
        }

        if (_enterRouteRetryCoroutine != null)
        {
            StopCoroutine(_enterRouteRetryCoroutine);
            _enterRouteRetryCoroutine = null;
        }

        if (TryEnterConveyorRoute(route))
        {
            if (currentState == VehicleState.Inbound)
            {
                ResetReturnHomeRetryState();
            }

            return;
        }

        Debug.LogWarning(
            $"{GetVehicleDebugName()} is waiting to enter conveyor route. {DescribeTrafficRouteFailure(route)}");

        _enterRouteRetryCoroutine = StartCoroutine(RetryEnterConveyorRoute(route));
    }

    private bool TryEnterConveyorRoute(TrafficRoute route)
    {
        if (ConveyorTrafficManager.Instance == null)
        {
            Debug.LogWarning($"{vehicleData.vehicleName} could not find conveyor traffic manager.");
            RecallAndDeactivate();
            return true;
        }

        return ConveyorTrafficManager.Instance.EnterRoute(this, route);
    }

    private IEnumerator RetryEnterConveyorRoute(TrafficRoute route)
    {
        while (currentState != VehicleState.Idle && gameObject.activeInHierarchy)
        {
            yield return GameTimeManager.WaitForSimulationSeconds(0.25f);
            if (TryEnterConveyorRoute(route))
            {
                if (currentState == VehicleState.Inbound)
                {
                    ResetReturnHomeRetryState();
                }

                _enterRouteRetryCoroutine = null;
                yield break;
            }

            if (route != null &&
                route.FailureReason == RouteFailureReason.GraphVersionMismatch)
            {
                _enterRouteRetryCoroutine = null;
                if (currentState == VehicleState.Inbound && _assignedPopFactory != null)
                {
                    HandleReturnHomeRouteFailure("return route became stale before the vehicle could enter traffic");
                    yield break;
                }

                RequestConveyorRerouteAfterGraphRebuild();
                yield break;
            }
        }

        _enterRouteRetryCoroutine = null;
    }

    private void RequestConveyorRoute(Vector3 targetWorldPos, bool recoverOnFailure = false, bool snapStartToClosestEdgeEndpoint = false)
    {
        RequestConveyorRoute(targetWorldPos, recoverOnFailure, snapStartToClosestEdgeEndpoint, Vector2Int.zero, false, Vector2Int.zero, false);
    }

    private void RequestConveyorRoute(
        Vector3 targetWorldPos,
        bool recoverOnFailure,
        bool snapStartToClosestEdgeEndpoint,
        Vector2Int targetPortCell,
        RouteRerouteReason rerouteReason = RouteRerouteReason.InitialRequest)
    {
        RequestConveyorRoute(
            targetWorldPos,
            recoverOnFailure,
            snapStartToClosestEdgeEndpoint,
            Vector2Int.zero,
            false,
            targetPortCell,
            true,
            rerouteReason);
    }

    private void RequestConveyorRoute(Vector3 targetWorldPos, bool recoverOnFailure, bool snapStartToClosestEdgeEndpoint, Vector2Int startPortCell, Vector2Int targetPortCell)
    {
        RequestConveyorRoute(targetWorldPos, recoverOnFailure, snapStartToClosestEdgeEndpoint, startPortCell, true, targetPortCell, true);
    }

    private void RequestConveyorRoute(
        Vector3 targetWorldPos,
        bool recoverOnFailure,
        bool snapStartToClosestEdgeEndpoint,
        Vector2Int startPortCell,
        bool hasStartPortCell,
        Vector2Int targetPortCell,
        bool hasTargetPortCell,
        RouteRerouteReason rerouteReason = RouteRerouteReason.InitialRequest)
    {
        StrategicCongestionSnapshot congestionSnapshot =
            ConveyorTrafficManager.Instance != null
                ? ConveyorTrafficManager.Instance.BuildStrategicCongestionSnapshot()
                : null;

        if (VehiclePathRequestManager.Instance == null)
        {
            Debug.LogError(
                $"{GetVehicleDebugName()} could not request a conveyor route because VehiclePathRequestManager.Instance is missing. " +
                $"home={DescribeBuilding(homeBuilding)}, target={DescribeBuilding(targetBuilding)}, activeNetwork={DescribeNetwork(_activeNetwork)}");
            RecallAndDeactivate();
            return;
        }

        Building retryTarget = targetBuilding;
        RoadNetwork retryNetwork = _activeNetwork;
        VehiclePathRequestManager.Instance.RequestPath(this, targetWorldPos, (route) =>
        {
            if (route != null && route.ManagedEdges != null && route.ManagedEdges.Count > 0)
            {
                StartConveyorRoute(route);
            }
            else
            {
                Debug.LogWarning(
                    $"{GetVehicleDebugName()} conveyor route failed. {DescribeTrafficRouteFailure(route)}");
                if (currentState == VehicleState.Inbound && _assignedPopFactory != null)
                {
                    HandleReturnHomeRouteFailure("no return route could be built");
                    return;
                }

                if (recoverOnFailure)
                {
                    ResetAtHomeAndRetryDispatch(retryTarget, retryNetwork);
                }
                else
                {
                    RecallAndDeactivate();
                }
            }
        }, snapStartToClosestEdgeEndpoint, startPortCell, hasStartPortCell, targetPortCell, hasTargetPortCell, rerouteReason, congestionSnapshot);
    }

    public void RecoverFromTrafficStall()
    {
        if (_isDebugOneWayTrip)
        {
            ConveyorTrafficManager.Instance?.LeaveTraffic(this);
            BeginDebugFactoryTrip();
            return;
        }
        Building retryTarget = targetBuilding;
        RoadNetwork retryNetwork = _activeNetwork;

        ResetAtHomeAndRetryDispatch(retryTarget, retryNetwork);
    }

    private void ForceConveyorReroute()
    {
        if (_isDebugOneWayTrip)
        {
            BeginDebugFactoryTrip();
            return;
        }

        RefreshActiveNetworkAfterGraphRebuild();

        Building targetForRoute = targetBuilding;
        RoadNetwork networkForRoute = _activeNetwork;

        if (currentState == VehicleState.Idle || networkForRoute == null)
        {
            return;
        }

        Vector2Int currentCell = new Vector2Int(
            Mathf.FloorToInt(transform.position.x / WorldManager.Instance.cellSize),
            Mathf.FloorToInt(transform.position.z / WorldManager.Instance.cellSize)
        );

        Vector2Int destinationPort;
        if (currentState == VehicleState.Outbound && targetForRoute != null)
        {
            if (!targetForRoute.TryGetClosestPort(PortFlowDirection.Entry, networkForRoute, currentCell, vehicleData, out destinationPort))
            {
                RecallAndDeactivate();
                return;
            }
        }
        else
        {
            if (!homeBuilding.TryGetClosestPort(PortFlowDirection.Entry, networkForRoute, currentCell, vehicleData, out destinationPort))
            {
                RecallAndDeactivate();
                return;
            }
        }

        RequestConveyorRoute(
            GetWorldPositionOfPort(destinationPort),
            true,
            false,
            destinationPort,
            RouteRerouteReason.GraphRebuild);
    }

    private void OnDestinationReached()
    {
        if (_isDebugOneWayTrip)
        {
            DestroyDebugVehicle();
            return;
        }

        if (currentState == VehicleState.Outbound)
        {
            if (vehicleData is ResourceTransportVehicleData && assignedResourceAmount > 0)
            {
                int amount = assignedResourceAmount;
                bool delivered = targetBuilding != null &&
                    targetBuilding.ReceiveIncomingResource(
                        this,
                        assignedResourceType,
                        amount);
                if (delivered)
                {
                    assignedResourceAmount = 0;
                    assignedResourceFactory = null;
                }
                else
                {
                    ReturnResourceCargoToHome();
                }
            }
            MarkPopDeliveryArrived();
            if (vehicleData is PopTransportVehicleData)
            {
                bool usefulAtFactory =
                    _assignedPopFactory != null &&
                    _assignedPopFactory
                        .ShouldKeepPopTransportVehicleAtFactory(this);
                if (!usefulAtFactory)
                {
                    _popReturnRequested = true;
                    if (!isConveyorMoving &&
                        (currentState == VehicleState.Outbound ||
                         currentState == VehicleState.AtDestination))
                    {
                        ReturnHome();
                    }
                    return;
                }

                currentState = VehicleState.AtDestination;
            }

            if (currentState == VehicleState.Outbound ||
                currentState == VehicleState.AtDestination)
            {
                StartCoroutine(WaitAtTarget());
            }
        }
        else if (currentState == VehicleState.Inbound) StartCoroutine(WaitAtHome());
    }

    public void NotifyConveyorDestinationReached()
    {
        isConveyorMoving = false;
        conveyorCurrentSpeed = 0f;
        OnDestinationReached();
    }

    public float GetVehicleLengthUnits()
    {
        if (vehicleData == null) return 1f;
        if (vehicleData.vehicleLengthUnits > 0f) return vehicleData.vehicleLengthUnits;
        return vehicleData.isLongVehicle ? 2.5f : 1f;
    }

    public float GetMinimumFollowingGapUnits()
    {
        return vehicleData != null ? Mathf.Max(0f, vehicleData.minimumFollowingGapUnits) : 0.5f;
    }

    public float GetDesiredTimeHeadwaySeconds()
    {
        return vehicleData != null ? Mathf.Max(0f, vehicleData.desiredTimeHeadwaySeconds) : 1.5f;
    }

    public float GetDriverReactionTimeSeconds()
    {
        return vehicleData != null ? Mathf.Max(0f, vehicleData.driverReactionTimeSeconds) : DefaultDriverReactionTime;
    }

    public float GetAccelerationUnitsPerSecondSquared()
    {
        return vehicleData != null && vehicleData.accelerationUnitsPerSecondSquared > 0f ? vehicleData.accelerationUnitsPerSecondSquared : DefaultAcceleration;
    }

    public float GetDecelerationUnitsPerSecondSquared()
    {
        return vehicleData != null && vehicleData.decelerationUnitsPerSecondSquared > 0f ? vehicleData.decelerationUnitsPerSecondSquared : DefaultDeceleration;
    }

    public float GetEmergencyDecelerationUnitsPerSecondSquared()
    {
        return vehicleData != null && vehicleData.emergencyDecelerationUnitsPerSecondSquared > 0f ? vehicleData.emergencyDecelerationUnitsPerSecondSquared : DefaultEmergencyDeceleration;
    }

    public float GetMaximumJerkUnitsPerSecondCubed()
    {
        return vehicleData != null && vehicleData.maximumJerkUnitsPerSecondCubed > 0f ? vehicleData.maximumJerkUnitsPerSecondCubed : DefaultMaximumJerk;
    }

    public float GetMaximumSpeedUnitsPerSecond()
    {
        return vehicleData != null ? Mathf.Max(0f, vehicleData.maximumVehicleSpeed) : 0f;
    }

    private IEnumerator WaitAtTarget()
    {
        yield return GameTimeManager.WaitForSimulationSeconds(3f);

        bool resourceVehicleFinishedWaiting =
            currentState == VehicleState.Outbound &&
            !(vehicleData is PopTransportVehicleData);
        bool releasedPopVehicleFinishedWaiting =
            (currentState == VehicleState.AtDestination ||
             currentState == VehicleState.Outbound) &&
            vehicleData is PopTransportVehicleData &&
            _popReturnRequested;
        if (resourceVehicleFinishedWaiting ||
            releasedPopVehicleFinishedWaiting)
        {
            ReturnHome();
        }
    }

    private IEnumerator WaitAtHome()
    {
        if (vehicleData is PopTransportVehicleData)
        {
            CompletePopTransportReturnHome();
            yield return GameTimeManager.WaitForSimulationSeconds(3f);

            if (!CanReceivePopTransportAssignment)
            {
                yield break;
            }

            if (homeBuilding != null && homeBuilding.TryDispatchPopTransportVehicle(this))
            {
                yield break;
            }

            RecallAndDeactivate();
            yield break;
        }

        // Resource trucks must be reassigned on every return trip. Their
        // previous destination is only a completed delivery and must not be
        // reused without loading a fresh cargo amount.
        if (vehicleData is ResourceTransportVehicleData)
        {
            RecallAndDeactivate();
            if (homeBuilding != null)
            {
                homeBuilding.RequestResourceTransportDispatch();
            }
            yield break;
        }

        yield return GameTimeManager.WaitForSimulationSeconds(3f);
        ApplyPendingAssignment();
        if (targetBuilding != null) DispatchTo(targetBuilding, _activeNetwork);
        else RecallAndDeactivate();
    }

    private void CompletePopTransportReturnHome()
    {
        currentState = VehicleState.Idle;
        targetBuilding = null;
        _pendingTargetBuilding = null;
        _pendingNetwork = null;
        _hasPendingAssignment = false;
        ReleasePopDeliveryAssignment();
    }

    private void ResetAtHomeAndRetryDispatch(
        Building retryTarget,
        RoadNetwork retryNetwork)
    {
        RecallAndDeactivate();

        if (vehicleData is PopTransportVehicleData)
        {
            if (BuildingSystemBackend.Instance != null)
            {
                BuildingSystemBackend.Instance.RequestPopTransportDispatch();
            }
            else if (homeBuilding != null)
            {
                homeBuilding.TryDispatchPopTransportVehicle(this);
            }

            return;
        }

        if (retryTarget != null && retryNetwork != null)
        {
            DispatchTo(retryTarget, retryNetwork);
        }
    }

    private void ApplyPendingAssignment()
    {
        if (!_hasPendingAssignment) return;

        targetBuilding = _pendingTargetBuilding;
        _activeNetwork = _pendingNetwork;
        _pendingTargetBuilding = null;
        _pendingNetwork = null;
        _hasPendingAssignment = false;
    }

    // --- HELPERS ---

    private void TeleportToPort(Vector2Int portCell)
    {
        transform.position = GetWorldPositionOfPort(portCell) + (Vector3.up * VehicleRoadClearance);
    }

    private void StartReturnHomeRetry()
    {
        if (_returnHomeRetryCoroutine != null ||
            currentState == VehicleState.Idle ||
            !gameObject.activeInHierarchy)
        {
            return;
        }

        _vehiclesWaitingForReturn.Add(this);
        _returnHomeRetryCoroutine = StartCoroutine(RetryReturnHome());
    }

    private IEnumerator RetryReturnHome()
    {
        while (currentState != VehicleState.Idle &&
               gameObject.activeInHierarchy)
        {
            // Graph compilation and path requests run on real frame budgets. Keep
            // their bounded recovery attempts independent of simulation speed.
            yield return new WaitForSecondsRealtime(ReturnHomeRetryDelaySeconds);

            if (isConveyorMoving)
            {
                continue;
            }

            if (TryBeginReturnHome())
            {
                StopReturnHomeRetryWait();
                yield break;
            }

            if (!RegisterReturnHomeFailure("compatible return ports or route were unavailable"))
            {
                yield break;
            }
        }

        StopReturnHomeRetryWait();
    }

    private void HandleReturnHomeRouteFailure(string reason)
    {
        if (!RegisterReturnHomeFailure(reason))
        {
            return;
        }

        StartReturnHomeRetry();
    }

    private bool RegisterReturnHomeFailure(string reason)
    {
        if (currentState == VehicleState.Idle || !gameObject.activeInHierarchy)
        {
            ResetReturnHomeRetryState();
            return false;
        }

        _returnHomeRouteFailures++;
        if (_returnHomeRouteFailures < MaxReturnHomeRouteFailures)
        {
            return true;
        }

        Debug.LogWarning(
            $"{GetVehicleDebugName()} could not return home after {_returnHomeRouteFailures} attempts ({reason}). Resetting vehicle and releasing factory pop assignment.");
        ResetAtHomeAndRetryDispatch(null, null);
        return false;
    }

    private void StopReturnHomeRetryWait()
    {
        _vehiclesWaitingForReturn.Remove(this);
        _returnHomeRetryCoroutine = null;
    }

    private void ResetReturnHomeRetryState()
    {
        StopReturnHomeRetryWait();
        _returnHomeRouteFailures = 0;
    }

    public void AssignPopDelivery(Building factory, int popCount)
    {
        if (_assignedPopFactory != null && _assignedPopFactory != factory)
        {
            ReleasePopDeliveryAssignment();
        }

        _assignedPopFactory = factory;
        _assignedPopCount = Mathf.Max(0, popCount);
        _assignedPopsDelivered = false;
        _popReturnRequested = false;
        _returnHomeRouteFailures = 0;
    }

    public void ReleasePopDeliveryFromFactory(
        Building factory,
        bool returnHome = true)
    {
        if (factory == null || _assignedPopFactory != factory)
        {
            return;
        }

        _popReturnRequested = true;
        if (returnHome &&
            (currentState == VehicleState.Outbound ||
             currentState == VehicleState.AtDestination) &&
            !isConveyorMoving)
        {
            ReturnHome();
        }
    }

    private void MarkPopDeliveryArrived()
    {
        if (_assignedPopFactory == null ||
            _assignedPopsDelivered ||
            _assignedPopCount <= 0 ||
            targetBuilding != _assignedPopFactory)
        {
            return;
        }

        _assignedPopsDelivered = true;
        _assignedPopFactory.MarkIncomingPopsDelivered(this);
    }

    private void ReleasePopDeliveryAssignment()
    {
        ResetReturnHomeRetryState();

        if (_assignedPopFactory != null)
        {
            _assignedPopFactory.ReleasePopAssignment(this);
        }

        _assignedPopFactory = null;
        _assignedPopCount = 0;
        _assignedPopsDelivered = false;
        _popReturnRequested = false;
    }

    private bool TryGetRoutePorts(
        Building source,
        Building destination,
        Vector2Int destinationReferenceCell,
        out Vector2Int exitPort,
        out Vector2Int entryPort)
    {
        return new VehicleDeliveryRouting(vehicleData).TryFindPorts(
            source, destination, _activeNetwork, out exitPort, out entryPort, out _);
    }

    private Vector3 GetWorldPositionOfPort(Vector2Int portCell)
    {
        if (WorldManager.Instance == null) return transform.position;

        float x = portCell.x * WorldManager.Instance.cellSize + (WorldManager.Instance.cellSize * 0.5f);
        float z = portCell.y * WorldManager.Instance.cellSize + (WorldManager.Instance.cellSize * 0.5f);
        float y = WorldManager.Instance.GetPhysicalHeight(portCell.x + 0.5f, portCell.y + 0.5f) * WorldManager.Instance.heightStep;
        
        return new Vector3(x, y, z);
    }

    private string GetVehicleDebugName()
    {
        if (vehicleData != null && !string.IsNullOrEmpty(vehicleData.vehicleName))
        {
            return vehicleData.vehicleName;
        }

        return name;
    }

    private string DescribeTrafficRouteFailure(TrafficRoute route)
    {
        string description =
            $"state={currentState}, home={DescribeBuilding(homeBuilding)}, target={DescribeBuilding(targetBuilding)}, activeNetwork={DescribeNetwork(_activeNetwork)}";

        if (route == null)
        {
            return description + ", reason=Path task returned null route.";
        }

        description +=
            $", reason={route.FailureReason}, rerouteReason={route.RerouteReason}, edgeCount={(route.ManagedEdges != null ? route.ManagedEdges.Count : 0)}";

        if (route.Corridor != null)
        {
            description +=
                $", graphVersion={route.Corridor.GraphVersion}, hasStartPort={route.Corridor.HasStartPortCell}, startPort={route.Corridor.StartPortCell}, hasTargetPort={route.Corridor.HasTargetPortCell}, targetPort={route.Corridor.TargetPortCell}";

            if (route.Corridor.Diagnostics.Count > 0)
            {
                RouteDiagnostic firstDiagnostic = route.Corridor.Diagnostics[0];
                description +=
                    $", diagnostic={firstDiagnostic.FailureReason}: {firstDiagnostic.Message}";
            }
        }

        return description;
    }

    private string DescribeBuilding(Building building)
    {
        if (building == null) return "null";
        return building.data != null ? building.data.buildingName : building.name;
    }

    private string DescribeNetwork(RoadNetwork network)
    {
        return network != null ? network.id.ToString() : "null";
    }

    private string DescribeRoutePortFailure(Building source, Building destination)
    {
        return $"source={DescribeBuilding(source)}, destination={DescribeBuilding(destination)}, activeNetwork={DescribeNetwork(_activeNetwork)}";
    }
}
