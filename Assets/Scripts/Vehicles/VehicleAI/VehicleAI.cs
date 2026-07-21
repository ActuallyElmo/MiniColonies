using UnityEngine;
using System.Collections;

public class VehicleAI : MonoBehaviour
{
    public enum VehicleState { Idle, Outbound, Inbound }
    
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
    private static ulong _nextSimulationSequence = 1UL;

    private Building _pendingTargetBuilding;
    private RoadNetwork _pendingNetwork;
    private bool _hasPendingAssignment;
    private RoadNetwork _activeNetwork;
    private Coroutine _enterRouteRetryCoroutine;

    public VehicleSimulationId SimulationId => simulationId;

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

    private void Update()
    {
    }

    // --- NEW: LATE-BINDING PATHING ORIGIN ---
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
        
        Vector2Int exitPort = homeBuilding.GetClosestPort(PortType.Exit, _activeNetwork, targetBuilding.originCell);
        TeleportToPort(exitPort);
        gameObject.SetActive(true);

        Vector2Int entryPort = targetBuilding.GetClosestPort(PortType.Entry, _activeNetwork, exitPort);
        Vector3 targetWorldPos = GetWorldPositionOfPort(entryPort);
        RequestConveyorRoute(targetWorldPos, false, true, exitPort, entryPort);
    }

    public void ReturnHome()
    {
        currentState = VehicleState.Inbound;

        // Debug.Log("returnhome");
        
        Vector2Int exitPort = targetBuilding.GetClosestPort(PortType.Exit, _activeNetwork, homeBuilding.originCell);
        TeleportToPort(exitPort);

        Vector2Int entryPort = homeBuilding.GetClosestPort(PortType.Entry, _activeNetwork, exitPort);
        Vector3 targetWorldPos = GetWorldPositionOfPort(entryPort);
        RequestConveyorRoute(targetWorldPos, false, true, exitPort, entryPort);
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
            if (targetBuilding != null) destinationPort = targetBuilding.GetClosestPort(PortType.Entry, _activeNetwork, currentCell);
            else
            {
                currentState = VehicleState.Inbound;
                destinationPort = homeBuilding.GetClosestPort(PortType.Entry, _activeNetwork, currentCell);
            }
        }
        else 
        {
            destinationPort = homeBuilding.GetClosestPort(PortType.Entry, _activeNetwork, currentCell);
        }

        Vector3 targetWorldPos = GetWorldPositionOfPort(destinationPort);
        
        RequestConveyorRoute(targetWorldPos, true, false, destinationPort);
    }

    public void RecallAndDeactivate()
    {
        if (ConveyorTrafficManager.Instance != null)
        {
            ConveyorTrafficManager.Instance.LeaveTraffic(this);
        }

        if (_enterRouteRetryCoroutine != null)
        {
            StopCoroutine(_enterRouteRetryCoroutine);
            _enterRouteRetryCoroutine = null;
        }

        currentState = VehicleState.Idle;
        targetBuilding = null;
        _pendingTargetBuilding = null;
        _pendingNetwork = null;
        _hasPendingAssignment = false;
        hasConveyorVisualPose = false;

        StopAllCoroutines();
        gameObject.SetActive(false); 
    }

    public void RequestConveyorRerouteAfterGraphRebuild()
    {
        if (currentState == VehicleState.Idle || !gameObject.activeInHierarchy)
        {
            return;
        }

        ForceConveyorReroute();
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

        if (TryEnterConveyorRoute(route)) return;

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
            yield return new WaitForSeconds(0.25f);
            if (TryEnterConveyorRoute(route))
            {
                _enterRouteRetryCoroutine = null;
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
        VehiclePathRequestManager.Instance.RequestPath(this, targetWorldPos, null, (route) =>
        {
            if (route != null && route.ManagedEdges != null && route.ManagedEdges.Count > 0)
            {
                StartConveyorRoute(route);
            }
            else
            {
                Debug.LogWarning(
                    $"{GetVehicleDebugName()} conveyor route failed. {DescribeTrafficRouteFailure(route)}");
                if (recoverOnFailure)
                {
                    RecallAndDeactivate();
                    if (retryTarget != null && retryNetwork != null)
                    {
                        DispatchTo(retryTarget, retryNetwork);
                    }
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
        Building retryTarget = targetBuilding;
        RoadNetwork retryNetwork = _activeNetwork;

        RecallAndDeactivate();
        if (retryTarget != null && retryNetwork != null)
        {
            DispatchTo(retryTarget, retryNetwork);
        }
    }

    private void ForceConveyorReroute()
    {
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
            destinationPort = targetForRoute.GetClosestPort(PortType.Entry, networkForRoute, currentCell);
        }
        else
        {
            destinationPort = homeBuilding.GetClosestPort(PortType.Entry, networkForRoute, currentCell);
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
        if (currentState == VehicleState.Outbound) StartCoroutine(WaitAtTarget());
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
        yield return new WaitForSeconds(3f); 
        ReturnHome();
    }

    private IEnumerator WaitAtHome()
    {
        yield return new WaitForSeconds(3f);
        ApplyPendingAssignment();
        if (targetBuilding != null) DispatchTo(targetBuilding, _activeNetwork);
        else RecallAndDeactivate();
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
}
