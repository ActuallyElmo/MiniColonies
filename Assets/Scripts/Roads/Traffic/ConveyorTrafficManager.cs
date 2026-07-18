using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public class ConveyorTrafficManager : MonoBehaviour
{
    private class ConnectorReservationRequest
    {
        public VehicleAI vehicle;
        public TrafficEdge connector;
        public TrafficEdge targetEdge;
        public float minDistance;
        public float maxDistance;
        public int sequence;
        public bool releasing;
        public int releaseRouteEdgeIndex;
        public float releaseDistanceOnEdge;
        public float releaseClearanceDistance;
        public float releaseTargetBaseDistance;
    }

    private class PendingIntersectionExit
    {
        public TrafficEdge movementEdge;
        public IIntersectionController controller;
        public int releaseRouteEdgeIndex;
        public float releaseDistanceOnEdge;
        public float clearanceDistance;
    }

    public static ConveyorTrafficManager Instance { get; private set; }

    private readonly List<VehicleAI> _activeVehicles = new List<VehicleAI>();
    private readonly HashSet<TrafficEdge> _activeEdges = new HashSet<TrafficEdge>();
    private readonly List<TrafficEdge> _tickEdges = new List<TrafficEdge>();
    private readonly List<VehicleAI> _graphRebuildVehicles = new List<VehicleAI>();
    private readonly TrafficRuntimeState _runtimeState = new TrafficRuntimeState();
    private readonly TrafficReservationService _reservationService =
        new TrafficReservationService();
    private readonly TrafficSimulationClock _simulationClock =
        new TrafficSimulationClock();
    private readonly TacticalLanePlanner _tacticalLanePlanner =
        new TacticalLanePlanner();
    private readonly HashSet<Vector2Int> _dirtyRouteCells = new HashSet<Vector2Int>();
    private readonly Dictionary<VehicleAI, ConnectorReservationRequest> _connectorRequests =
        new Dictionary<VehicleAI, ConnectorReservationRequest>();
    private readonly Dictionary<VehicleAI, List<PendingIntersectionExit>> _pendingIntersectionExits =
        new Dictionary<VehicleAI, List<PendingIntersectionExit>>();
    private readonly Stopwatch _tickStopwatch = new Stopwatch();
    private TrafficCongestionSnapshot _lastCongestionSnapshot;
    private int _nextConnectorRequestSequence;
    private int _reservationContentionEvents;
    private float _congestionSnapshotAccumulator;
    private const float VehicleRoadClearance = 0.03f;
    private const float StopLineLookaheadUnits = 3f;
    private const float RouteFollowingLookaheadUnits = 12f;
    private const float StopLineBodyClearance = 0.03f;
    private const float IntersectionRearClearance = 0.05f;
    private const float IntersectionEntrySpeedUnitsPerSecond = 0.4f;
    private const float StopPositionEpsilon = 0.05f;
    private const float DriverReactionDelayMin = 0.25f;
    private const float DriverReactionDelayMax = 0.75f;
    private const float TrafficStuckSecondsBeforeRespawn = 30f;
    private const float TrafficStuckSpeedThreshold = 0.05f;
    private const float TrafficStuckDistanceEpsilon = 0.01f;
    private const float CongestionSnapshotIntervalSeconds = 0.5f;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Update()
    {
        int steps = _simulationClock.Accumulate(Time.deltaTime);
        for (int i = 0; i < steps; i++)
        {
            _tickStopwatch.Restart();
            Tick(_simulationClock.FixedDeltaSeconds);
            _tickStopwatch.Stop();
            PublishRuntimePerformanceCounters(
                _simulationClock.FixedDeltaSeconds,
                (float)_tickStopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private void Start()
    {
        if (RoadSystemBackend.Instance != null)
        {
            RoadSystemBackend.Instance.OnRoadCellChanged += HandleRoadCellChanged;
        }

        if (BuildingSystemBackend.Instance != null)
        {
            BuildingSystemBackend.Instance.OnBuildingPlaced += HandleBuildingChanged;
            BuildingSystemBackend.Instance.OnBuildingRemoved += HandleBuildingChanged;
        }
    }

    private void OnDestroy()
    {
        if (RoadSystemBackend.Instance != null)
        {
            RoadSystemBackend.Instance.OnRoadCellChanged -= HandleRoadCellChanged;
        }

        if (BuildingSystemBackend.Instance != null)
        {
            BuildingSystemBackend.Instance.OnBuildingPlaced -= HandleBuildingChanged;
            BuildingSystemBackend.Instance.OnBuildingRemoved -= HandleBuildingChanged;
        }
    }

    public bool EnterRoute(VehicleAI vehicle, TrafficRoute route)
    {
        if (vehicle == null || route == null || route.ManagedEdges == null || route.ManagedEdges.Count == 0)
        {
            return false;
        }

        if (!IsRouteCurrent(route))
        {
            route.FailureReason = RouteFailureReason.GraphVersionMismatch;
            return false;
        }

        LeaveTraffic(vehicle);

        TrafficEdge firstEdge = route.ManagedEdges[0];
        float entryDistance = firstEdge != null ? Mathf.Clamp(route.StartDistanceOnFirstEdge, 0f, firstEdge.totalLength) : 0f;
        if (firstEdge == null ||
            !firstEdge.HasSpaceAtDistance(entryDistance, vehicle.GetVehicleLengthUnits(), vehicle.GetMinimumFollowingGapUnits(), vehicle))
        {
            return false;
        }

        vehicle.conveyorRoute = route;
        vehicle.conveyorRouteEdgeIndex = 0;
        vehicle.currentEdge = firstEdge;
        vehicle.conveyorDistanceOnEdge = entryDistance;
        vehicle.conveyorCurrentSpeed = 0f;
        vehicle.conveyorPreviousAccelerationUnitsPerSecondSquared = 0f;
        vehicle.isConveyorMoving = true;
        ClearTrafficBlock(vehicle);

        _runtimeState.Register(vehicle, firstEdge, entryDistance);
        _activeVehicles.Add(vehicle);
        _activeEdges.Add(firstEdge);
        UpdateVehicleTransform(vehicle);
        return true;
    }

    public void LeaveTraffic(VehicleAI vehicle)
    {
        if (vehicle == null) return;

        ReleaseConnectorReservation(vehicle);
        ReleasePendingIntersectionExits(vehicle);

        if (vehicle.currentEdge != null)
        {
            if (vehicle.currentEdge.kind == TrafficEdgeKind.IntersectionMovement &&
                vehicle.currentEdge.exitController != null)
            {
                vehicle.currentEdge.exitController.NotifyExited(vehicle, vehicle.currentEdge);
            }

            _runtimeState.Unregister(vehicle);
            if (vehicle.currentEdge.occupants.Count == 0) _activeEdges.Remove(vehicle.currentEdge);
        }

        _activeVehicles.Remove(vehicle);
        vehicle.isConveyorMoving = false;
        vehicle.currentEdge = null;
        vehicle.conveyorRoute = null;
        vehicle.conveyorRouteEdgeIndex = 0;
        vehicle.conveyorDistanceOnEdge = 0f;
        vehicle.conveyorCurrentSpeed = 0f;
        vehicle.conveyorPreviousAccelerationUnitsPerSecondSquared = 0f;
        ClearTrafficBlock(vehicle);
    }

    public void HandleTrafficGraphRebuilt()
    {
        HandleTrafficGraphRebuilt(
            TrafficSystemBackend.Instance != null &&
            TrafficSystemBackend.Instance.CurrentTrafficGraphSnapshot != null
                ? TrafficSystemBackend.Instance.CurrentTrafficGraphSnapshot.Version
                : TrafficGraphVersion.Invalid);
    }

    public void HandleTrafficGraphRebuilt(TrafficGraphVersion graphVersion)
    {
        _graphRebuildVehicles.Clear();
        _graphRebuildVehicles.AddRange(_activeVehicles);

        foreach (VehicleAI vehicle in _graphRebuildVehicles)
        {
            ReleaseVehicleGraphReferences(vehicle);
        }

        _runtimeState.Clear();
        _activeVehicles.Clear();
        _activeEdges.Clear();
        _connectorRequests.Clear();
        _pendingIntersectionExits.Clear();
        _dirtyRouteCells.Clear();

        foreach (VehicleAI vehicle in _graphRebuildVehicles)
        {
            if (vehicle == null || !vehicle.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (TryRemapVehicleToCurrentGraph(vehicle, graphVersion))
            {
                continue;
            }

            ClearVehicleTrafficState(vehicle);
            vehicle.RequestConveyorRerouteAfterGraphRebuild();
        }

        _graphRebuildVehicles.Clear();
    }

    private void ReleaseVehicleGraphReferences(VehicleAI vehicle)
    {
        if (vehicle == null) return;

        ReleaseConnectorReservation(vehicle);
        ReleasePendingIntersectionExits(vehicle);

        if (vehicle.currentEdge != null &&
            vehicle.currentEdge.kind == TrafficEdgeKind.IntersectionMovement &&
            vehicle.currentEdge.exitController != null)
        {
            vehicle.currentEdge.exitController.NotifyExited(vehicle, vehicle.currentEdge);
        }
    }

    private bool TryRemapVehicleToCurrentGraph(
        VehicleAI vehicle,
        TrafficGraphVersion graphVersion)
    {
        if (vehicle == null ||
            vehicle.conveyorRoute == null ||
            vehicle.conveyorRoute.ManagedEdges == null ||
            vehicle.currentEdge == null ||
            TrafficSystemBackend.Instance == null ||
            !graphVersion.IsValid)
        {
            return false;
        }

        TrafficRoute remappedRoute = new TrafficRoute
        {
            StartDistanceOnFirstEdge = vehicle.conveyorRoute.StartDistanceOnFirstEdge,
            EndDistanceOnFinalEdge = vehicle.conveyorRoute.EndDistanceOnFinalEdge,
            RerouteReason = RouteRerouteReason.GraphRebuild,
            Corridor = new RouteCorridor
            {
                GraphVersion = graphVersion,
                StartPortAnchorId = vehicle.conveyorRoute.Corridor != null
                    ? vehicle.conveyorRoute.Corridor.StartPortAnchorId
                    : default,
                StartPortCell = vehicle.conveyorRoute.Corridor != null
                    ? vehicle.conveyorRoute.Corridor.StartPortCell
                    : default,
                HasStartPortCell = vehicle.conveyorRoute.Corridor != null &&
                                   vehicle.conveyorRoute.Corridor.HasStartPortCell,
                TargetPortAnchorId = vehicle.conveyorRoute.Corridor != null
                    ? vehicle.conveyorRoute.Corridor.TargetPortAnchorId
                    : default,
                TargetPortCell = vehicle.conveyorRoute.Corridor != null
                    ? vehicle.conveyorRoute.Corridor.TargetPortCell
                    : default,
                HasTargetPortCell = vehicle.conveyorRoute.Corridor != null &&
                                    vehicle.conveyorRoute.Corridor.HasTargetPortCell,
                RerouteReason = RouteRerouteReason.GraphRebuild
            }
        };

        int remappedCurrentIndex = -1;
        for (int i = 0; i < vehicle.conveyorRoute.ManagedEdges.Count; i++)
        {
            TrafficEdge oldEdge = vehicle.conveyorRoute.ManagedEdges[i];
            if (!TrafficSystemBackend.Instance.TryGetCurrentManagedEdge(
                    oldEdge,
                    out TrafficEdge newEdge))
            {
                return false;
            }

            remappedRoute.ManagedEdges.Add(newEdge);
            remappedRoute.EdgeIds.Add(newEdge.edgeId);
            if (newEdge.stableLaneSegmentId.IsValid)
            {
                AddUnique(remappedRoute.Corridor.LaneSegmentIds, newEdge.stableLaneSegmentId);
            }
            if (newEdge.stableMovementId.IsValid)
            {
                AddUnique(remappedRoute.Corridor.RequiredMovementIds, newEdge.stableMovementId);
            }
            if (newEdge.stableSectionId.IsValid)
            {
                AddUnique(remappedRoute.Corridor.RoadSectionIds, newEdge.stableSectionId);
            }

            if (oldEdge == vehicle.currentEdge)
            {
                remappedCurrentIndex = i;
            }
        }

        if (remappedCurrentIndex < 0 ||
            remappedCurrentIndex >= remappedRoute.ManagedEdges.Count)
        {
            return false;
        }

        TrafficEdge currentEdge = remappedRoute.ManagedEdges[remappedCurrentIndex];
        float distanceRatio =
            vehicle.currentEdge.totalLength > 0f
                ? Mathf.Clamp01(vehicle.conveyorDistanceOnEdge / vehicle.currentEdge.totalLength)
                : 0f;
        float remappedDistance = Mathf.Clamp(
            currentEdge.totalLength * distanceRatio,
            0f,
            currentEdge.totalLength);

        remappedRoute.FailureReason = RouteFailureReason.None;
        remappedRoute.Corridor.FailureReason = RouteFailureReason.None;
        vehicle.conveyorRoute = remappedRoute;
        vehicle.conveyorRouteEdgeIndex = remappedCurrentIndex;
        vehicle.currentEdge = currentEdge;
        vehicle.conveyorDistanceOnEdge = remappedDistance;
        vehicle.isConveyorMoving = true;
        ClearTrafficBlock(vehicle);

        _runtimeState.Register(vehicle, currentEdge, remappedDistance);
        _activeVehicles.Add(vehicle);
        _activeEdges.Add(currentEdge);
        UpdateVehicleTransform(vehicle);
        return true;
    }

    private void AddUnique<T>(List<T> list, T value)
    {
        if (list != null && !list.Contains(value))
        {
            list.Add(value);
        }
    }

    private void ClearVehicleTrafficState(VehicleAI vehicle)
    {
        if (vehicle == null) return;

        vehicle.isConveyorMoving = false;
        vehicle.currentEdge = null;
        vehicle.conveyorRoute = null;
        vehicle.conveyorRouteEdgeIndex = 0;
        vehicle.conveyorDistanceOnEdge = 0f;
        vehicle.conveyorCurrentSpeed = 0f;
        vehicle.conveyorPreviousAccelerationUnitsPerSecondSquared = 0f;
        ClearTrafficBlock(vehicle);
    }

    private bool IsRouteCurrent(TrafficRoute route)
    {
        if (TrafficSystemBackend.Instance == null ||
            TrafficSystemBackend.Instance.CurrentTrafficGraphSnapshot == null)
        {
            return true;
        }

        return route != null &&
               route.Corridor != null &&
               route.MatchesGraph(
                   TrafficSystemBackend.Instance.CurrentTrafficGraphSnapshot.Version);
    }

    private bool IsVehicleRouteCurrent(VehicleAI vehicle)
    {
        return vehicle != null && IsRouteCurrent(vehicle.conveyorRoute);
    }

    private TrafficGraphVersion GetCurrentRouteGraphVersion(VehicleAI vehicle)
    {
        return IsVehicleRouteCurrent(vehicle) &&
               vehicle.conveyorRoute.Corridor != null
            ? vehicle.conveyorRoute.Corridor.GraphVersion
            : TrafficGraphVersion.Invalid;
    }

    private void HandleRoadCellChanged(Vector2Int cell)
    {
        _dirtyRouteCells.Add(cell);
    }

    private void HandleBuildingChanged(Building building)
    {
        if (building == null) return;

        foreach (Vector2Int cell in building.occupiedCells)
        {
            _dirtyRouteCells.Add(cell);
        }

        foreach (Vector2Int portCell in building.globalPorts.Keys)
        {
            _dirtyRouteCells.Add(portCell);
        }
    }

    private void PublishRuntimePerformanceCounters(
        float deltaTime,
        float lastTickMilliseconds)
    {
        _congestionSnapshotAccumulator += deltaTime;
        if (_congestionSnapshotAccumulator >= CongestionSnapshotIntervalSeconds)
        {
            _congestionSnapshotAccumulator = 0f;
            _lastCongestionSnapshot = BuildCongestionSnapshot();
        }

        if (TrafficSystemBackend.Instance != null)
        {
            TrafficSystemBackend.Instance.UpdateRuntimePerformanceCounters(
                lastTickMilliseconds,
                _activeVehicles.Count,
                _activeEdges.Count,
                CountReservedEdges(),
                _reservationContentionEvents,
                _lastCongestionSnapshot);
        }
    }

    private TrafficCongestionSnapshot BuildCongestionSnapshot()
    {
        float speedSum = 0f;
        int movingCount = 0;
        for (int i = 0; i < _activeVehicles.Count; i++)
        {
            VehicleAI vehicle = _activeVehicles[i];
            if (vehicle == null || !vehicle.isConveyorMoving) continue;
            speedSum += vehicle.conveyorCurrentSpeed;
            movingCount++;
        }

        TrafficGraphVersion version =
            TrafficSystemBackend.Instance != null &&
            TrafficSystemBackend.Instance.CurrentTrafficGraphSnapshot != null
                ? TrafficSystemBackend.Instance.CurrentTrafficGraphSnapshot.Version
                : TrafficGraphVersion.Invalid;

        return new TrafficCongestionSnapshot(
            version,
            _activeVehicles.Count,
            _activeEdges.Count,
            CountReservedEdges(),
            _reservationContentionEvents,
            movingCount > 0 ? speedSum / movingCount : 0f);
    }

    public StrategicCongestionSnapshot BuildStrategicCongestionSnapshot()
    {
        TrafficGraphVersion version =
            TrafficSystemBackend.Instance != null &&
            TrafficSystemBackend.Instance.CurrentTrafficGraphSnapshot != null
                ? TrafficSystemBackend.Instance.CurrentTrafficGraphSnapshot.Version
                : TrafficGraphVersion.Invalid;
        var lanePenalties = new Dictionary<LaneSegmentId, float>();
        var movementPenalties = new Dictionary<MovementId, float>();

        foreach (TrafficEdge edge in _activeEdges)
        {
            if (edge == null) continue;

            float penalty = 0f;
            if (edge.occupants != null) penalty += edge.occupants.Count;
            if (edge.reservations != null) penalty += edge.reservations.Count * 0.5f;
            if (penalty <= 0f) continue;

            if (edge.stableLaneSegmentId.IsValid)
            {
                lanePenalties[edge.stableLaneSegmentId] = penalty;
            }
            if (edge.stableMovementId.IsValid)
            {
                movementPenalties[edge.stableMovementId] = penalty;
            }
        }

        return new StrategicCongestionSnapshot(
            version,
            lanePenalties,
            movementPenalties);
    }

    private int CountReservedEdges()
    {
        int count = 0;
        foreach (TrafficEdge edge in _activeEdges)
        {
            if (edge != null && edge.reservations != null && edge.reservations.Count > 0)
            {
                count++;
            }
        }

        return count;
    }

    private void Tick(float deltaTime)
    {
        if (_activeVehicles.Count == 0) return;

        if (TrafficSystemBackend.Instance != null)
        {
            TrafficSystemBackend.Instance.TickIntersectionControllers(deltaTime);
        }

        _tickEdges.Clear();
        foreach (TrafficEdge edge in _activeEdges)
        {
            if (edge != null) _tickEdges.Add(edge);
        }

        _tickEdges.Sort((a, b) => a.edgeId.CompareTo(b.edgeId));

        foreach (TrafficEdge edge in _tickEdges)
        {
            edge.occupants.RemoveAll(v => v == null || v.currentEdge != edge || !v.isConveyorMoving);
            edge.occupants.Sort((a, b) =>
            {
                int distanceCompare = b.conveyorDistanceOnEdge.CompareTo(a.conveyorDistanceOnEdge);
                return distanceCompare != 0
                    ? distanceCompare
                    : a.EnsureSimulationId().CompareTo(b.EnsureSimulationId());
            });

            int count = edge.occupants.Count;
            for (int i = 0; i < count && i < edge.occupants.Count; i++)
            {
                VehicleAI vehicle = edge.occupants[i];
                if (vehicle != null && vehicle.currentEdge == edge) TickVehicle(vehicle, deltaTime);
            }
        }

        _activeEdges.RemoveWhere(edge => edge == null || edge.occupants.Count == 0);
    }

    private void TickVehicle(VehicleAI vehicle, float deltaTime)
    {
        UpdateConnectorReservation(vehicle);
        UpdatePendingIntersectionExits(vehicle);

        TrafficEdge edge = vehicle.currentEdge;
        if (edge == null || vehicle.conveyorRoute == null) return;
        if (!IsVehicleRouteCurrent(vehicle))
        {
            LeaveTraffic(vehicle);
            vehicle.RequestConveyorRerouteAfterGraphRebuild();
            return;
        }

        float currentDistance = vehicle.conveyorDistanceOnEdge;
        float targetDistanceOnEdge = GetRouteTargetDistanceOnCurrentEdge(vehicle);
        TrafficEdge nextRouteEdge = IsOnFinalEdge(vehicle) ? null : GetRouteEdgeAtOffset(vehicle, 1);
        float maxDistance = targetDistanceOnEdge;
        float stopDistanceAlongRoute = float.PositiveInfinity;
        float speedAtStopDistance = 0f;
        bool mustStopAtLimit = false;

        if (nextRouteEdge != null)
        {
            float distanceToEdgeEnd = Mathf.Max(0f, targetDistanceOnEdge - currentDistance);
            vehicle.lastTacticalLaneDecision =
                _tacticalLanePlanner.Decide(
                    vehicle,
                    nextRouteEdge,
                    distanceToEdgeEnd);
            PreAcquireApproachingConnector(
                vehicle,
                nextRouteEdge,
                distanceToEdgeEnd);
        }

        if (TryGetFollowingStopDistance(
                vehicle,
                out float followingStopDistance,
                out float followingSpeedAtDistance))
        {
            mustStopAtLimit = true;
            stopDistanceAlongRoute = followingStopDistance;
            speedAtStopDistance = followingSpeedAtDistance;

            float distanceToCurrentEdgeEnd = Mathf.Max(0f, targetDistanceOnEdge - currentDistance);
            if (followingStopDistance <= distanceToCurrentEdgeEnd)
            {
                maxDistance = Mathf.Min(maxDistance, currentDistance + Mathf.Max(0f, followingStopDistance));
            }
        }

        if (!IsOnFinalEdge(vehicle))
        {
            float distanceToEdgeEnd = Mathf.Max(0f, targetDistanceOnEdge - currentDistance);
            float transitionLookahead = GetBrakingApproachDistance(vehicle);
            if (distanceToEdgeEnd <= transitionLookahead &&
                !CanTransitionToNextEdge(vehicle, 0f, false, false))
            {
                float blockedStopDistance = GetBlockedTransitionStopDistance(
                    vehicle,
                    edge,
                    nextRouteEdge,
                    targetDistanceOnEdge);
                float distanceToStopLine = Mathf.Max(0f, blockedStopDistance - currentDistance);
                mustStopAtLimit = true;
                maxDistance = Mathf.Min(maxDistance, blockedStopDistance);
                if (distanceToStopLine < stopDistanceAlongRoute)
                {
                    stopDistanceAlongRoute = distanceToStopLine;
                    speedAtStopDistance = 0f;
                }
            }
        }

        float distanceToLimit = mustStopAtLimit
            ? Mathf.Max(0f, stopDistanceAlongRoute)
            : Mathf.Max(0f, maxDistance - currentDistance);
        bool blockedAtStop = mustStopAtLimit && distanceToLimit <= StopPositionEpsilon;
        if (blockedAtStop)
        {
            MarkTrafficBlocked(vehicle);
        }

        float desiredSpeed = Mathf.Min(vehicle.GetMaximumSpeedUnitsPerSecond(), edge.speedLimit);
        if (nextRouteEdge != null &&
            nextRouteEdge.kind == TrafficEdgeKind.IntersectionMovement)
        {
            float decel = Mathf.Max(0.1f, vehicle.GetDecelerationUnitsPerSecondSquared());
            float distanceToEntry = Mathf.Max(0f, targetDistanceOnEdge - currentDistance);
            float entrySpeed = Mathf.Min(IntersectionEntrySpeedUnitsPerSecond, desiredSpeed);
            float approachSpeed = Mathf.Sqrt(
                entrySpeed * entrySpeed +
                2f * decel * distanceToEntry);
            desiredSpeed = Mathf.Min(desiredSpeed, approachSpeed);
        }

        if (mustStopAtLimit)
        {
            float decel = Mathf.Max(0.1f, vehicle.GetDecelerationUnitsPerSecondSquared());
            float stoppingSpeed = Mathf.Sqrt(
                speedAtStopDistance * speedAtStopDistance +
                2f * decel * Mathf.Max(0f, distanceToLimit - StopPositionEpsilon));
            desiredSpeed = Mathf.Min(desiredSpeed, stoppingSpeed);
        }

        if (!mustStopAtLimit && vehicle.trafficWasBlocked && !DriverReactionReady(vehicle))
        {
            desiredSpeed = 0f;
            maxDistance = vehicle.conveyorDistanceOnEdge;
        }

        LongitudinalControlResult control = LongitudinalController.Step(
            new LongitudinalControlInput(
                vehicle.conveyorCurrentSpeed,
                desiredSpeed,
                deltaTime,
                vehicle.conveyorPreviousAccelerationUnitsPerSecondSquared,
                vehicle.GetAccelerationUnitsPerSecondSquared(),
                vehicle.GetDecelerationUnitsPerSecondSquared(),
                vehicle.GetEmergencyDecelerationUnitsPerSecondSquared(),
                vehicle.GetMaximumJerkUnitsPerSecondCubed()));
        vehicle.conveyorCurrentSpeed = control.SpeedUnitsPerSecond;
        vehicle.conveyorPreviousAccelerationUnitsPerSecondSquared =
            control.AccelerationUnitsPerSecondSquared;

        float nextDistance = Mathf.Min(currentDistance + vehicle.conveyorCurrentSpeed * deltaTime, maxDistance);
        nextDistance = Mathf.Max(currentDistance, nextDistance);

        bool stoppedInTraffic = mustStopAtLimit &&
                                vehicle.conveyorCurrentSpeed <= TrafficStuckSpeedThreshold &&
                                nextDistance <= currentDistance + TrafficStuckDistanceEpsilon;
        if (stoppedInTraffic)
        {
            vehicle.trafficStationaryTime += deltaTime;
            if (vehicle.trafficStationaryTime >= TrafficStuckSecondsBeforeRespawn)
            {
                vehicle.RecoverFromTrafficStall();
                return;
            }
        }
        else
        {
            vehicle.trafficStationaryTime = 0f;
        }

        if (nextDistance >= targetDistanceOnEdge - 0.001f)
        {
            if (IsOnFinalEdge(vehicle))
            {
                FinishVehicleRoute(vehicle);
                return;
            }

            float remainder = Mathf.Max(0f, vehicle.conveyorDistanceOnEdge + vehicle.conveyorCurrentSpeed * deltaTime - edge.totalLength);
            if (TryTransitionToNextEdge(vehicle, remainder))
            {
                return;
            }

            vehicle.conveyorCurrentSpeed = 0f;
            nextDistance = edge.totalLength;
        }

        vehicle.conveyorDistanceOnEdge = nextDistance;
        _runtimeState.UpdateVehicle(vehicle);
        UpdateVehicleTransform(vehicle);
    }

    private bool TryGetFollowingStopDistance(
        VehicleAI vehicle,
        out float stopDistance,
        out float speedAtStopDistance)
    {
        stopDistance = float.PositiveInfinity;
        speedAtStopDistance = 0f;
        if (vehicle == null ||
            vehicle.currentEdge == null ||
            vehicle.conveyorRoute == null ||
            vehicle.conveyorRoute.ManagedEdges == null)
        {
            return false;
        }

        float followingGap =
            vehicle.GetMinimumFollowingGapUnits() +
            vehicle.conveyorCurrentSpeed *
            vehicle.GetDesiredTimeHeadwaySeconds();
        float vehicleHalfLength = vehicle.GetVehicleLengthUnits() * 0.5f;
        VehicleAI sameEdgeVehicle =
            _runtimeState.GetVehicleAhead(vehicle, vehicle.currentEdge);
        if (sameEdgeVehicle != null)
        {
            float centerClearance = vehicleHalfLength +
                                    sameEdgeVehicle.GetVehicleLengthUnits() * 0.5f +
                                    Mathf.Max(
                                        followingGap,
                                        sameEdgeVehicle.GetMinimumFollowingGapUnits());
            ConsiderFollowingConstraint(
                sameEdgeVehicle.conveyorDistanceOnEdge -
                vehicle.conveyorDistanceOnEdge -
                centerClearance,
                sameEdgeVehicle.conveyorCurrentSpeed,
                ref stopDistance,
                ref speedAtStopDistance);
        }

        foreach (TrafficEdgeReservation reservation in vehicle.currentEdge.reservations)
        {
            if (reservation == null ||
                reservation.vehicle == null ||
                reservation.vehicle == vehicle ||
                reservation.maxDistance <= vehicle.conveyorDistanceOnEdge - vehicleHalfLength)
            {
                continue;
            }

            float reservationStopDistance = reservation.minDistance -
                                            vehicleHalfLength -
                                            vehicle.conveyorDistanceOnEdge;
            ConsiderFollowingConstraint(
                reservationStopDistance,
                0f,
                ref stopDistance,
                ref speedAtStopDistance);
        }

        float deceleration = Mathf.Max(0.1f, vehicle.GetDecelerationUnitsPerSecondSquared());
        float brakingDistance = vehicle.conveyorCurrentSpeed * vehicle.conveyorCurrentSpeed / (2f * deceleration);
        float lookaheadDistance = Mathf.Max(
            RouteFollowingLookaheadUnits,
            brakingDistance + vehicle.GetVehicleLengthUnits() + followingGap);
        float distanceToEdgeStart = Mathf.Max(
            0f,
            vehicle.currentEdge.totalLength - vehicle.conveyorDistanceOnEdge);

        for (int edgeIndex = vehicle.conveyorRouteEdgeIndex + 1;
             edgeIndex < vehicle.conveyorRoute.ManagedEdges.Count && distanceToEdgeStart <= lookaheadDistance;
             edgeIndex++)
        {
            TrafficEdge routeEdge = vehicle.conveyorRoute.ManagedEdges[edgeIndex];
            if (routeEdge == null) break;

            foreach (VehicleAI candidate in routeEdge.occupants)
            {
                if (candidate == null || candidate == vehicle) continue;

                float centerClearance = vehicleHalfLength +
                                        candidate.GetVehicleLengthUnits() * 0.5f +
                                        Mathf.Max(
                                            followingGap,
                                            candidate.GetMinimumFollowingGapUnits());
                float candidateStopDistance = distanceToEdgeStart +
                                              candidate.conveyorDistanceOnEdge -
                                              centerClearance;
                ConsiderFollowingConstraint(
                    candidateStopDistance,
                    candidate.conveyorCurrentSpeed,
                    ref stopDistance,
                    ref speedAtStopDistance);
            }

            foreach (TrafficEdgeReservation reservation in routeEdge.reservations)
            {
                if (reservation == null ||
                    reservation.vehicle == null ||
                    reservation.vehicle == vehicle)
                {
                    continue;
                }

                float reservationStopDistance = distanceToEdgeStart +
                                                reservation.minDistance -
                                                vehicleHalfLength;
                ConsiderFollowingConstraint(
                    reservationStopDistance,
                    0f,
                    ref stopDistance,
                    ref speedAtStopDistance);
            }

            distanceToEdgeStart += routeEdge.totalLength;
        }

        return !float.IsPositiveInfinity(stopDistance);
    }

    private void ConsiderFollowingConstraint(
        float candidateDistance,
        float candidateSpeed,
        ref float stopDistance,
        ref float speedAtStopDistance)
    {
        if (candidateDistance > stopDistance + 0.001f) return;

        if (Mathf.Abs(candidateDistance - stopDistance) <= 0.001f)
        {
            speedAtStopDistance = Mathf.Min(
                speedAtStopDistance,
                Mathf.Max(0f, candidateSpeed));
            return;
        }

        stopDistance = candidateDistance;
        speedAtStopDistance = Mathf.Max(0f, candidateSpeed);
    }

    private float GetBlockedTransitionStopDistance(
        VehicleAI vehicle,
        TrafficEdge currentEdge,
        TrafficEdge nextEdge,
        float edgeTargetDistance)
    {
        if (vehicle == null || currentEdge == null) return edgeTargetDistance;

        float frontExtent = vehicle.GetVehicleLengthUnits() * 0.5f;
        float boundaryClearance =
            nextEdge != null &&
            nextEdge.kind == TrafficEdgeKind.IntersectionMovement
                ? StopLineBodyClearance
                : 0f;
        return Mathf.Clamp(
            edgeTargetDistance - frontExtent - boundaryClearance,
            0f,
            edgeTargetDistance);
    }

    private bool TryTransitionToNextEdge(VehicleAI vehicle, float remainderDistance)
    {
        TrafficRoute route = vehicle.conveyorRoute;
        int nextIndex = vehicle.conveyorRouteEdgeIndex + 1;
        if (route == null || nextIndex >= route.ManagedEdges.Count) return false;

        TrafficEdge current = vehicle.currentEdge;
        TrafficEdge next = route.ManagedEdges[nextIndex];
        float insertionDistance = Mathf.Max(0f, remainderDistance);
        if (!CanTransitionToNextEdge(vehicle, insertionDistance, true, true))
        {
            return false;
        }

        _runtimeState.Transfer(
            vehicle,
            next,
            Mathf.Clamp(insertionDistance, 0f, next.totalLength));
        if (current.occupants.Count == 0) _activeEdges.Remove(current);

        bool completedReservedConnector =
            current.kind == TrafficEdgeKind.LaneChange ||
            current.kind == TrafficEdgeKind.RoadTypeTransition;
        bool exitedIntersection =
            current.kind == TrafficEdgeKind.IntersectionMovement &&
            current.exitController != null;

        vehicle.currentEdge = next;
        vehicle.conveyorRouteEdgeIndex = nextIndex;
        vehicle.conveyorDistanceOnEdge = Mathf.Clamp(insertionDistance, 0f, next.totalLength);
        _activeEdges.Add(next);

        IIntersectionController enteringController = next.kind == TrafficEdgeKind.IntersectionMovement ? next.exitController : null;
        if (enteringController != null)
        {
            enteringController.NotifyEntered(vehicle, next);
        }

        if (exitedIntersection)
        {
            BeginPendingIntersectionExit(
                vehicle,
                current,
                current.exitController,
                nextIndex,
                vehicle.conveyorDistanceOnEdge);
        }

        if (completedReservedConnector)
        {
            BeginConnectorReservationRelease(
                vehicle,
                nextIndex,
                vehicle.conveyorDistanceOnEdge);
        }

        UpdateVehicleTransform(vehicle);
        return true;
    }

    private bool CanTransitionToNextEdge(VehicleAI vehicle, float remainderDistance, bool updateBlockState, bool applyReactionDelay)
    {
        if (!IsVehicleRouteCurrent(vehicle))
        {
            if (updateBlockState) MarkTrafficBlocked(vehicle);
            return false;
        }

        TrafficRoute route = vehicle.conveyorRoute;
        int nextIndex = vehicle.conveyorRouteEdgeIndex + 1;
        if (route == null || nextIndex >= route.ManagedEdges.Count) return false;

        TrafficEdge current = vehicle.currentEdge;
        TrafficEdge next = route.ManagedEdges[nextIndex];
        float insertionDistance = Mathf.Max(0f, remainderDistance);
        TrafficEdge followingEdge = nextIndex + 1 < route.ManagedEdges.Count ? route.ManagedEdges[nextIndex + 1] : null;
        IIntersectionController enteringController = next != null && next.kind == TrafficEdgeKind.IntersectionMovement ? next.exitController : null;

        bool canProceed = next != null &&
                          next.HasSpaceAtDistance(insertionDistance, vehicle.GetVehicleLengthUnits(), vehicle.GetMinimumFollowingGapUnits(), vehicle) &&
                          HasConnectorClearance(vehicle, next, updateBlockState) &&
                          (enteringController == null ||
                           enteringController.CanEnter(
                               new TrafficMovementRequest(
                                   vehicle,
                                   current,
                                   next,
                                   followingEdge)));

        if (!canProceed)
        {
            if (updateBlockState) MarkTrafficBlocked(vehicle);
            return false;
        }

        bool shouldApplyReactionDelay =
            applyReactionDelay &&
            enteringController != null;
        if (shouldApplyReactionDelay &&
            !DriverReactionReady(vehicle))
        {
            return false;
        }

        if (applyReactionDelay || !vehicle.trafficWasBlocked)
        {
            ClearTrafficBlock(vehicle);
        }

        return true;
    }

    private void PreAcquireApproachingConnector(
        VehicleAI vehicle,
        TrafficEdge connector,
        float distanceToConnector)
    {
        if (vehicle == null ||
            connector == null ||
            (connector.kind != TrafficEdgeKind.LaneChange &&
             connector.kind != TrafficEdgeKind.RoadTypeTransition))
        {
            return;
        }

        float acquisitionDistance = GetBrakingApproachDistance(vehicle);

        if (distanceToConnector <= acquisitionDistance)
        {
            HasConnectorClearance(vehicle, connector, true);
        }
    }

    private float GetBrakingApproachDistance(VehicleAI vehicle)
    {
        if (vehicle == null) return StopLineLookaheadUnits;

        float deceleration = Mathf.Max(
            0.1f,
            vehicle.GetDecelerationUnitsPerSecondSquared());
        float brakingDistance =
            vehicle.conveyorCurrentSpeed *
            vehicle.conveyorCurrentSpeed /
            (2f * deceleration);
        return Mathf.Max(
            StopLineLookaheadUnits,
            brakingDistance +
            vehicle.GetVehicleLengthUnits() +
            vehicle.GetMinimumFollowingGapUnits());
    }

    private bool HasConnectorClearance(
        VehicleAI vehicle,
        TrafficEdge connector,
        bool commitReservation)
    {
        if (connector == null) return false;
        if (connector.kind != TrafficEdgeKind.LaneChange &&
            connector.kind != TrafficEdgeKind.RoadTypeTransition)
        {
            return true;
        }

        if (connector.conflictingLaneChangeEdge != null &&
            connector.conflictingLaneChangeEdge.occupants.Count > 0)
        {
            RecordReservationContention(commitReservation);
            return false;
        }

        TrafficEdge targetEdge = connector.reservedTargetEdge;
        if (targetEdge == null) return true;
        return CanAcquireConnectorReservation(
            vehicle,
            connector,
            targetEdge,
            commitReservation);
    }

    private bool CanAcquireConnectorReservation(
        VehicleAI vehicle,
        TrafficEdge connector,
        TrafficEdge targetEdge,
        bool commitReservation)
    {
        if (vehicle == null || connector == null || targetEdge == null) return false;

        GetConnectorReservationInterval(
            vehicle,
            connector,
            targetEdge,
            out float minDistance,
            out float maxDistance);

        ConnectorReservationRequest request = null;
        if (_connectorRequests.TryGetValue(vehicle, out ConnectorReservationRequest existing))
        {
            request = existing;
            if (request.connector != connector || request.targetEdge != targetEdge)
            {
                if (request.releasing)
                {
                    return false;
                }

                ReleaseConnectorReservation(vehicle);
                request = null;
            }
        }

        if (request == null && commitReservation)
        {
            request = new ConnectorReservationRequest
            {
                vehicle = vehicle,
                connector = connector,
                targetEdge = targetEdge,
                minDistance = minDistance,
                maxDistance = maxDistance,
                sequence = _nextConnectorRequestSequence++
            };
            _connectorRequests[vehicle] = request;
        }

        int requestSequence = request != null ? request.sequence : int.MaxValue;
        foreach (ConnectorReservationRequest other in _connectorRequests.Values)
        {
            if (other == null ||
                other.vehicle == null ||
                other.vehicle == vehicle ||
                other.targetEdge != targetEdge ||
                other.sequence >= requestSequence)
            {
                continue;
            }

            if (IntervalsOverlap(minDistance, maxDistance, other.minDistance, other.maxDistance))
            {
                RecordReservationContention(commitReservation);
                return false;
            }
        }

        if (!targetEdge.CanReserveInterval(
            minDistance,
            maxDistance,
            vehicle,
            vehicle.GetMinimumFollowingGapUnits()))
        {
            RecordReservationContention(commitReservation);
            return false;
        }

        if (commitReservation && request != null)
        {
            request.minDistance = minDistance;
            request.maxDistance = maxDistance;
            request.releasing = false;
            if (!_reservationService.Reserve(
                targetEdge,
                vehicle,
                minDistance,
                maxDistance,
                request.sequence,
                GetCurrentRouteGraphVersion(vehicle)))
            {
                RecordReservationContention(commitReservation);
                return false;
            }
        }

        return true;
    }

    private void RecordReservationContention(bool commitReservation)
    {
        if (commitReservation) _reservationContentionEvents++;
    }

    private void GetConnectorReservationInterval(
        VehicleAI vehicle,
        TrafficEdge connector,
        TrafficEdge targetEdge,
        out float minDistance,
        out float maxDistance)
    {
        float clearance = vehicle.GetVehicleLengthUnits() * 0.5f +
                          vehicle.GetMinimumFollowingGapUnits();

        if (connector.kind == TrafficEdgeKind.LaneChange)
        {
            float forwardLookahead = GetConnectorReservationLookahead(
                vehicle,
                targetEdge);
            minDistance = -clearance;
            maxDistance = clearance + forwardLookahead;
            return;
        }

        minDistance = -clearance;
        maxDistance = clearance;
    }

    private void UpdateConnectorReservation(VehicleAI vehicle)
    {
        if (vehicle == null ||
            !_connectorRequests.TryGetValue(vehicle, out ConnectorReservationRequest request) ||
            request == null ||
            request.targetEdge == null)
        {
            return;
        }

        if (!IsVehicleRouteCurrent(vehicle))
        {
            ReleaseConnectorReservation(vehicle);
            return;
        }

        if (!request.releasing &&
            vehicle.currentEdge == request.connector &&
            request.connector != null &&
            request.connector.kind == TrafficEdgeKind.LaneChange)
        {
            float progress = request.connector.totalLength > 0.0001f
                ? Mathf.Clamp01(vehicle.conveyorDistanceOnEdge / request.connector.totalLength)
                : 1f;
            float clearance = vehicle.GetVehicleLengthUnits() * 0.5f +
                              vehicle.GetMinimumFollowingGapUnits();
            float forwardLookahead = GetConnectorReservationLookahead(
                vehicle,
                request.targetEdge);
            float projectedDistance = request.targetEdge.totalLength * progress;
            request.minDistance = projectedDistance - clearance;
            request.maxDistance =
                projectedDistance +
                clearance +
                forwardLookahead;
            if (!_reservationService.Reserve(
                request.targetEdge,
                vehicle,
                request.minDistance,
                request.maxDistance,
                request.sequence,
                GetCurrentRouteGraphVersion(vehicle)))
            {
                ReleaseConnectorReservation(vehicle);
            }
            return;
        }

        if (!request.releasing ||
            vehicle.conveyorRoute == null ||
            vehicle.conveyorRoute.ManagedEdges == null ||
            vehicle.conveyorRouteEdgeIndex < request.releaseRouteEdgeIndex)
        {
            return;
        }

        float traveledDistance = -request.releaseDistanceOnEdge;
        for (int edgeIndex = request.releaseRouteEdgeIndex;
             edgeIndex < vehicle.conveyorRouteEdgeIndex &&
             edgeIndex < vehicle.conveyorRoute.ManagedEdges.Count;
             edgeIndex++)
        {
            TrafficEdge traversedEdge = vehicle.conveyorRoute.ManagedEdges[edgeIndex];
            if (traversedEdge != null) traveledDistance += traversedEdge.totalLength;
        }
        traveledDistance += vehicle.conveyorDistanceOnEdge;

        request.minDistance = request.releaseTargetBaseDistance +
                              traveledDistance -
                              request.releaseClearanceDistance;
        request.maxDistance = request.releaseTargetBaseDistance +
                              traveledDistance +
                              request.releaseClearanceDistance;

        if (request.minDistance >= request.releaseTargetBaseDistance)
        {
            ReleaseConnectorReservation(vehicle);
            return;
        }

        if (!_reservationService.Reserve(
            request.targetEdge,
            vehicle,
            request.minDistance,
            request.maxDistance,
            request.sequence,
            GetCurrentRouteGraphVersion(vehicle)))
        {
            ReleaseConnectorReservation(vehicle);
        }
    }

    private float GetConnectorReservationLookahead(
        VehicleAI vehicle,
        TrafficEdge targetEdge)
    {
        if (vehicle == null || targetEdge == null) return 0f;

        float maximumSpeed = Mathf.Min(
            vehicle.GetMaximumSpeedUnitsPerSecond(),
            targetEdge.speedLimit);
        float deceleration = Mathf.Max(
            0.1f,
            vehicle.GetDecelerationUnitsPerSecondSquared());
        return maximumSpeed * maximumSpeed / (2f * deceleration);
    }

    private void BeginConnectorReservationRelease(
        VehicleAI vehicle,
        int routeEdgeIndex,
        float distanceOnEdge)
    {
        if (vehicle == null ||
            !_connectorRequests.TryGetValue(vehicle, out ConnectorReservationRequest request) ||
            request == null)
        {
            return;
        }

        request.releasing = true;
        request.releaseRouteEdgeIndex = routeEdgeIndex;
        request.releaseDistanceOnEdge = distanceOnEdge;
        request.releaseClearanceDistance =
            vehicle.GetVehicleLengthUnits() * 0.5f +
            vehicle.GetMinimumFollowingGapUnits();
        request.releaseTargetBaseDistance =
            request.connector != null &&
            request.connector.kind == TrafficEdgeKind.LaneChange
                ? request.targetEdge.totalLength
                : 0f;
        UpdateConnectorReservation(vehicle);
    }

    private void ReleaseConnectorReservation(VehicleAI vehicle)
    {
        if (vehicle == null) return;
        if (!_connectorRequests.TryGetValue(vehicle, out ConnectorReservationRequest request)) return;

        if (request != null && request.targetEdge != null)
        {
            _reservationService.Release(request.targetEdge, vehicle);
        }

        _connectorRequests.Remove(vehicle);
    }

    private void BeginPendingIntersectionExit(
        VehicleAI vehicle,
        TrafficEdge movementEdge,
        IIntersectionController controller,
        int routeEdgeIndex,
        float distanceOnEdge)
    {
        if (vehicle == null || movementEdge == null || controller == null) return;

        if (!_pendingIntersectionExits.TryGetValue(
                vehicle,
                out List<PendingIntersectionExit> pendingExits))
        {
            pendingExits = new List<PendingIntersectionExit>();
            _pendingIntersectionExits[vehicle] = pendingExits;
        }

        pendingExits.Add(new PendingIntersectionExit
        {
            movementEdge = movementEdge,
            controller = controller,
            releaseRouteEdgeIndex = routeEdgeIndex,
            releaseDistanceOnEdge = distanceOnEdge,
            clearanceDistance =
                vehicle.GetVehicleLengthUnits() * 0.5f +
                IntersectionRearClearance
        });
        UpdatePendingIntersectionExits(vehicle);
    }

    private void UpdatePendingIntersectionExits(VehicleAI vehicle)
    {
        if (vehicle == null ||
            vehicle.conveyorRoute == null ||
            vehicle.conveyorRoute.ManagedEdges == null ||
            !_pendingIntersectionExits.TryGetValue(
                vehicle,
                out List<PendingIntersectionExit> pendingExits))
        {
            return;
        }

        for (int i = pendingExits.Count - 1; i >= 0; i--)
        {
            PendingIntersectionExit pending = pendingExits[i];
            if (pending == null ||
                pending.controller == null ||
                vehicle.conveyorRouteEdgeIndex < pending.releaseRouteEdgeIndex)
            {
                continue;
            }

            float traveledDistance = -pending.releaseDistanceOnEdge;
            for (int edgeIndex = pending.releaseRouteEdgeIndex;
                 edgeIndex < vehicle.conveyorRouteEdgeIndex &&
                 edgeIndex < vehicle.conveyorRoute.ManagedEdges.Count;
                 edgeIndex++)
            {
                TrafficEdge traversedEdge = vehicle.conveyorRoute.ManagedEdges[edgeIndex];
                if (traversedEdge != null) traveledDistance += traversedEdge.totalLength;
            }
            traveledDistance += vehicle.conveyorDistanceOnEdge;

            if (traveledDistance < pending.clearanceDistance) continue;

            pending.controller.NotifyExited(vehicle, pending.movementEdge);
            pendingExits.RemoveAt(i);
        }

        if (pendingExits.Count == 0)
        {
            _pendingIntersectionExits.Remove(vehicle);
        }
    }

    private void ReleasePendingIntersectionExits(VehicleAI vehicle)
    {
        if (vehicle == null ||
            !_pendingIntersectionExits.TryGetValue(
                vehicle,
                out List<PendingIntersectionExit> pendingExits))
        {
            return;
        }

        foreach (PendingIntersectionExit pending in pendingExits)
        {
            if (pending != null && pending.controller != null)
            {
                pending.controller.NotifyExited(vehicle, pending.movementEdge);
            }
        }

        _pendingIntersectionExits.Remove(vehicle);
    }

    private bool IntervalsOverlap(float aMin, float aMax, float bMin, float bMax)
    {
        return aMax > bMin && bMax > aMin;
    }

    private bool IsOnFinalEdge(VehicleAI vehicle)
    {
        return vehicle.conveyorRoute == null ||
               vehicle.conveyorRouteEdgeIndex >= vehicle.conveyorRoute.ManagedEdges.Count - 1;
    }

    private float GetRouteTargetDistanceOnCurrentEdge(VehicleAI vehicle)
    {
        if (vehicle == null || vehicle.currentEdge == null) return 0f;
        if (!IsOnFinalEdge(vehicle)) return vehicle.currentEdge.totalLength;

        return Mathf.Clamp(vehicle.conveyorRoute.EndDistanceOnFinalEdge, 0f, vehicle.currentEdge.totalLength);
    }

    private void MarkTrafficBlocked(VehicleAI vehicle)
    {
        if (vehicle == null) return;

        if (!vehicle.trafficWasBlocked)
        {
            vehicle.trafficWasBlocked = true;
            vehicle.trafficReleaseTime = 0f;
        }
    }

    private bool DriverReactionReady(VehicleAI vehicle)
    {
        if (vehicle == null || !vehicle.trafficWasBlocked) return true;

        if (vehicle.trafficReleaseTime <= 0f)
        {
            vehicle.trafficReleaseTime = Time.time + Random.Range(DriverReactionDelayMin, DriverReactionDelayMax);
        }

        if (Time.time < vehicle.trafficReleaseTime)
        {
            return false;
        }

        ClearTrafficBlock(vehicle);
        return true;
    }

    private void ClearTrafficBlock(VehicleAI vehicle)
    {
        if (vehicle == null) return;
        vehicle.trafficWasBlocked = false;
        vehicle.trafficReleaseTime = 0f;
        vehicle.trafficStationaryTime = 0f;
    }

    private void FinishVehicleRoute(VehicleAI vehicle)
    {
        LeaveTraffic(vehicle);
        vehicle.NotifyConveyorDestinationReached();
    }

    private void UpdateVehicleTransform(VehicleAI vehicle)
    {
        TrafficEdge edge = vehicle.currentEdge;
        if (edge == null) return;

        Vector3 position = edge.GetPositionAtDistance(vehicle.conveyorDistanceOnEdge);
        Vector3 direction = GetVehicleVisualDirection(vehicle, edge);

        vehicle.transform.position = position + (Vector3.up * VehicleRoadClearance);
        if (direction.sqrMagnitude > 0.001f)
        {
            vehicle.transform.rotation = Quaternion.LookRotation(direction);
        }
    }

    private Vector3 GetVehicleVisualDirection(VehicleAI vehicle, TrafficEdge edge)
    {
        const float tangentSampleUnits = 0.04f;
        return GetEdgeLocalVisualDirection(
            edge,
            vehicle.conveyorDistanceOnEdge,
            tangentSampleUnits);
    }

    private TrafficEdge GetRouteEdgeAtOffset(VehicleAI vehicle, int edgeOffset)
    {
        if (vehicle == null ||
            vehicle.conveyorRoute == null ||
            vehicle.conveyorRoute.ManagedEdges == null)
        {
            return null;
        }

        int edgeIndex = vehicle.conveyorRouteEdgeIndex + edgeOffset;
        if (edgeIndex < 0 || edgeIndex >= vehicle.conveyorRoute.ManagedEdges.Count)
        {
            return null;
        }

        return vehicle.conveyorRoute.ManagedEdges[edgeIndex];
    }

    private Vector3 GetEdgeLocalVisualDirection(TrafficEdge edge, float distanceOnEdge, float sampleUnits)
    {
        if (edge == null)
        {
            return Vector3.forward;
        }

        float backDistance = Mathf.Clamp(distanceOnEdge - sampleUnits, 0f, edge.totalLength);
        float forwardDistance = Mathf.Clamp(distanceOnEdge + sampleUnits, 0f, edge.totalLength);
        if (Mathf.Abs(forwardDistance - backDistance) > 0.001f)
        {
            Vector3 direction = edge.GetPositionAtDistance(forwardDistance) - edge.GetPositionAtDistance(backDistance);
            if (direction.sqrMagnitude > 0.001f)
            {
                return direction.normalized;
            }
        }

        return edge.GetDirectionAtDistance(distanceOnEdge);
    }

}
