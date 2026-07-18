using System;
using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public class VehiclePathfindingTask : ISimulationTask
{
    private enum TaskState { Init, Wait, Complete }
    private TaskState _state = TaskState.Init;

    private VehicleAI _requester;
    private Vector3 _targetPos;
    private Action<Queue<Vector3>> _onComplete;
    private Action<TrafficRoute> _onRouteComplete;
    private bool _snapStartToClosestEdgeEndpoint;
    private Vector2Int _startPortCell;
    private bool _hasStartPortCell;
    private Vector2Int _targetPortCell;
    private bool _hasTargetPortCell;

    // Native Data
    private NativeArray<int> _startEdgeIndices;
    private NativeArray<int> _endEdgeIndices;
    private NativeList<float3> _resultWaypoints;
    private NativeList<int> _resultEdgeIndices;
    private NativeArray<float> _congestionPenalties;
    private TrafficEdge[] _managedEdgesByIndex;
    private int[] _edgeIdsByIndex;
    private JobHandle _jobHandle;
    private Vector3 _resolvedStartPos;
    private TrafficGraphVersion _graphVersion;
    private TrafficGraphSnapshot _trafficGraph;
    private VehicleTrafficProfile _vehicleProfile;
    private BuildingPortAnchorRecord _startPortAnchor;
    private BuildingPortAnchorRecord _targetPortAnchor;
    private RouteRerouteReason _rerouteReason;
    private readonly Stopwatch _routeStopwatch = new Stopwatch();
    private StrategicCongestionSnapshot _congestionSnapshot;

    public VehiclePathfindingTask(
        VehicleAI requester,
        Vector3 target,
        Action<Queue<Vector3>> onComplete,
        Action<TrafficRoute> onRouteComplete = null,
        bool snapStartToClosestEdgeEndpoint = false,
        Vector2Int startPortCell = default,
        bool hasStartPortCell = false,
        Vector2Int targetPortCell = default,
        bool hasTargetPortCell = false,
        RouteRerouteReason rerouteReason = RouteRerouteReason.InitialRequest,
        StrategicCongestionSnapshot congestionSnapshot = null)
    {
        _requester = requester;
        _targetPos = target;
        _onComplete = onComplete;
        _onRouteComplete = onRouteComplete;
        _snapStartToClosestEdgeEndpoint = snapStartToClosestEdgeEndpoint;
        _startPortCell = startPortCell;
        _hasStartPortCell = hasStartPortCell;
        _targetPortCell = targetPortCell;
        _hasTargetPortCell = hasTargetPortCell;
        _rerouteReason = rerouteReason;
        _congestionSnapshot = congestionSnapshot;
    }

    public bool Process(Stopwatch timer, float maxMillisecondsPerFrame)
    {
        if (_state == TaskState.Init)
        {
            _routeStopwatch.Restart();
            NativeTrafficGraph graph = NativeTrafficGraph.Instance;
            TrafficGraphSnapshot currentGraph =
                TrafficSystemBackend.Instance != null
                    ? TrafficSystemBackend.Instance.CurrentTrafficGraphSnapshot
                    : null;
            _trafficGraph = currentGraph;
            _graphVersion = currentGraph != null
                ? currentGraph.Version
                : TrafficGraphVersion.Invalid;

            // Fail out if graph isn't built or vehicle died
            if (graph == null || !graph.IsReady || _requester == null || !_requester.gameObject.activeInHierarchy)
            {
                return CompleteFailure(
                    RouteFailureReason.GraphUnavailable,
                    "The native traffic graph or requesting vehicle is unavailable.");
            }

            if (_graphVersion.IsValid &&
                graph.GraphVersion.IsValid &&
                graph.GraphVersion != _graphVersion)
            {
                return CompleteFailure(
                    RouteFailureReason.GraphVersionMismatch,
                    "The native graph version does not match the immutable traffic snapshot.");
            }
            if (_congestionSnapshot != null &&
                _congestionSnapshot.GraphVersion != _graphVersion)
            {
                return CompleteFailure(
                    RouteFailureReason.GraphVersionMismatch,
                    "The congestion snapshot does not match the immutable traffic graph version.");
            }

            var profileDiagnostics = new TrafficDiagnosticCollection();
            if (_requester.vehicleData == null ||
                !_requester.vehicleData.TryCompileTrafficProfile(
                    profileDiagnostics,
                    out _vehicleProfile))
            {
                return CompleteFailure(
                    RouteFailureReason.VehicleProfileInvalid,
                    "The vehicle traffic profile is invalid.");
            }

            // CRITICAL FIX: Fetch the starting position dynamically AT THE EXACT MOMENT the task executes.
            // Because this task sat in the queue while the traffic graph generated, the vehicle 
            // kept driving. Capturing this now ensures we don't use a stale, out-of-bounds position!
            Vector3 dynamicStartPos = _requester.GetPathingOrigin();
            _resolvedStartPos = dynamicStartPos;

            // Find closest lanes on the Main Thread (Fast spatial check)
            List<TrafficEdge> startEdges = TrafficSystemBackend.Instance.GetClosestLanes(dynamicStartPos, 3f, 6);
            List<TrafficEdge> endEdges = TrafficSystemBackend.Instance.GetClosestLanes(_targetPos, 3f, 6);
            if (_hasStartPortCell)
            {
                if (!TryFindPortAnchor(_startPortCell, true, out _startPortAnchor))
                {
                    return CompleteFailure(
                        RouteFailureReason.ExactStartPortUnavailable,
                        $"No departure anchor exists for port cell {_startPortCell}.");
                }
                startEdges = GetDepartureEdgesFromPortCell(startEdges, _startPortCell, dynamicStartPos);
                startEdges = FilterEdgesByAnchors(
                    startEdges,
                    _startPortAnchor.DepartureAnchors);
            }
            else if (_snapStartToClosestEdgeEndpoint)
            {
                startEdges = FilterStartEdgesForEndpointDeparture(startEdges, dynamicStartPos);
            }

            if (_hasTargetPortCell)
            {
                if (!TryFindPortAnchor(_targetPortCell, false, out _targetPortAnchor))
                {
                    return CompleteFailure(
                        RouteFailureReason.ExactTargetPortUnavailable,
                        $"No arrival anchor exists for port cell {_targetPortCell}.");
                }
                endEdges = GetArrivalEdgesToPortCell(endEdges, _targetPortCell, _targetPos);
                endEdges = FilterEdgesByAnchors(
                    endEdges,
                    _targetPortAnchor.ArrivalAnchors);
            }

            startEdges = FilterLegalEdges(startEdges);
            endEdges = FilterLegalEdges(endEdges);
            if (startEdges.Count == 0 || endEdges.Count == 0)
            {
                return CompleteFailure(
                    RouteFailureReason.NoLegalCorridor,
                    "No legal start or destination lane exists for this vehicle profile.");
            }

            // Map Objects to Native Indices
            _startEdgeIndices = new NativeArray<int>(startEdges.Count, Allocator.TempJob);
            for (int i = 0; i < startEdges.Count; i++) _startEdgeIndices[i] = graph.EdgeToIndex[startEdges[i]];

            _endEdgeIndices = new NativeArray<int>(endEdges.Count, Allocator.TempJob);
            for (int i = 0; i < endEdges.Count; i++) _endEdgeIndices[i] = graph.EdgeToIndex[endEdges[i]];

            _managedEdgesByIndex = new TrafficEdge[graph.Edges.Length];
            _edgeIdsByIndex = new int[graph.Edges.Length];
            _congestionPenalties = new NativeArray<float>(
                graph.Edges.Length,
                Allocator.TempJob);
            foreach (KeyValuePair<TrafficEdge, int> pair in graph.EdgeToIndex)
            {
                _managedEdgesByIndex[pair.Value] = pair.Key;
                _edgeIdsByIndex[pair.Value] = graph.Edges[pair.Value].edgeId;
                _congestionPenalties[pair.Value] = _congestionSnapshot != null
                    ? _congestionSnapshot.GetPenalty(pair.Key)
                    : 0f;
            }

            _resultWaypoints = new NativeList<float3>(Allocator.TempJob);
            _resultEdgeIndices = new NativeList<int>(Allocator.TempJob);

            // Schedule the Burst Job
            VehiclePathfindingJob job = new VehiclePathfindingJob
            {
                Nodes = graph.Nodes,
                Edges = graph.Edges,
                Waypoints = graph.Waypoints,
                NodeOutConnections = graph.NodeOutConnections,
                StartEdgeIndices = _startEdgeIndices,
                EndEdgeIndices = _endEdgeIndices,
                CongestionPenalties = _congestionPenalties,
                StartPos = dynamicStartPos, // Feed the job the dynamic position
                TargetPos = _targetPos,
                VehiclePermissions = (int)_vehicleProfile.RoadPermissions,
                VehicleCapabilities = (int)_vehicleProfile.Capabilities,
                ResultWaypoints = _resultWaypoints,
                ResultEdgeIndices = _resultEdgeIndices
            };

            _jobHandle = job.Schedule();
            
            SimulationTaskManager.Instance.RegisterJob(_jobHandle);
            _state = TaskState.Wait;
        }

        if (_state == TaskState.Wait)
        {
            if (!_jobHandle.IsCompleted) return false;
            _state = TaskState.Complete;
        }

        if (_state == TaskState.Complete)
        {
            _jobHandle.Complete();
            _routeStopwatch.Stop();
            if (TrafficSystemBackend.Instance != null)
            {
                TrafficSystemBackend.Instance.UpdateRoutePerformanceCounter(
                    (float)_routeStopwatch.Elapsed.TotalMilliseconds);
            }

            TrafficGraphSnapshot currentGraph =
                TrafficSystemBackend.Instance != null
                    ? TrafficSystemBackend.Instance.CurrentTrafficGraphSnapshot
                    : null;
            if (_graphVersion.IsValid &&
                (currentGraph == null || currentGraph.Version != _graphVersion))
            {
                DisposeNativeResults();
                return CompleteFailure(
                    RouteFailureReason.GraphVersionMismatch,
                    "The immutable traffic graph changed while the route was being calculated.");
            }

            Queue<Vector3> finalPath = null;
            TrafficRoute finalRoute = null;
            if (_resultWaypoints.Length > 0)
            {
                finalPath = new Queue<Vector3>(_resultWaypoints.Length);
                for (int i = 0; i < _resultWaypoints.Length; i++)
                {
                    finalPath.Enqueue(_resultWaypoints[i]);
                }

                finalRoute = BuildTrafficRoute(finalPath);
            }

            DisposeNativeResults();

            if (finalRoute == null)
            {
                return CompleteFailure(
                    RouteFailureReason.NoLegalCorridor,
                    "No legal strategic corridor connects the exact route anchors.");
            }

            if (_requester != null && _requester.gameObject.activeInHierarchy)
            {
                _onComplete?.Invoke(finalPath);
                _onRouteComplete?.Invoke(finalRoute);
            }

            return true; 
        }

        return false;
    }

    private TrafficRoute BuildTrafficRoute(Queue<Vector3> finalPath)
    {
        TrafficRoute route = new TrafficRoute
        {
            DebugWaypoints = new Queue<Vector3>(finalPath),
            Corridor = new RouteCorridor
            {
                GraphVersion = _graphVersion,
                StartPortAnchorId = _startPortAnchor != null
                    ? _startPortAnchor.Id
                    : default,
                StartPortCell = _startPortCell,
                HasStartPortCell = _hasStartPortCell,
                TargetPortAnchorId = _targetPortAnchor != null
                    ? _targetPortAnchor.Id
                    : default,
                TargetPortCell = _targetPortCell,
                HasTargetPortCell = _hasTargetPortCell,
                RerouteReason = _rerouteReason
            },
            RerouteReason = _rerouteReason
        };

        if (_managedEdgesByIndex == null || _edgeIdsByIndex == null)
        {
            return route;
        }

        for (int i = 0; i < _resultEdgeIndices.Length; i++)
        {
            int edgeIndex = _resultEdgeIndices[i];
            if (edgeIndex < 0 || edgeIndex >= _edgeIdsByIndex.Length) continue;

            route.EdgeIds.Add(_edgeIdsByIndex[edgeIndex]);

            TrafficEdge managedEdge = _managedEdgesByIndex[edgeIndex];
            if (managedEdge != null)
            {
                route.ManagedEdges.Add(managedEdge);
                if (managedEdge.stableLaneSegmentId.IsValid)
                {
                    AddUnique(
                        route.Corridor.LaneSegmentIds,
                        managedEdge.stableLaneSegmentId);
                }
                if (managedEdge.stableMovementId.IsValid)
                {
                    AddMovementIfRequired(route.Corridor, managedEdge);
                }
                if (managedEdge.stableSectionId.IsValid)
                {
                    AddUnique(
                        route.Corridor.RoadSectionIds,
                        managedEdge.stableSectionId);
                }
            }
        }

        route.FailureReason = route.ManagedEdges.Count == 0
            ? RouteFailureReason.NoLegalCorridor
            : RouteFailureReason.None;
        route.Corridor.FailureReason = route.FailureReason;
        BuildAcceptableLaneSets(route.Corridor);

        if (route.ManagedEdges.Count > 0)
        {
            TrafficEdge firstEdge = route.ManagedEdges[0];
            TrafficEdge finalEdge = route.ManagedEdges[route.ManagedEdges.Count - 1];

            route.StartDistanceOnFirstEdge = GetStartDistanceOnFirstEdge(firstEdge);
            route.EndDistanceOnFinalEdge = GetEndDistanceOnFinalEdge(finalEdge);
        }

        return route;
    }

    private bool CompleteFailure(
        RouteFailureReason failureReason,
        string message)
    {
        TrafficRoute route = new TrafficRoute
        {
            FailureReason = failureReason,
            RerouteReason = _rerouteReason,
            Corridor = new RouteCorridor
            {
                GraphVersion = _graphVersion,
                StartPortAnchorId = _startPortAnchor != null
                    ? _startPortAnchor.Id
                    : default,
                StartPortCell = _startPortCell,
                HasStartPortCell = _hasStartPortCell,
                TargetPortAnchorId = _targetPortAnchor != null
                    ? _targetPortAnchor.Id
                    : default,
                TargetPortCell = _targetPortCell,
                HasTargetPortCell = _hasTargetPortCell,
                RerouteReason = _rerouteReason,
                FailureReason = failureReason
            }
        };
        route.Corridor.Diagnostics.Add(new RouteDiagnostic
        {
            FailureReason = failureReason,
            Message = message ?? string.Empty
        });
        _onComplete?.Invoke(null);
        _onRouteComplete?.Invoke(route);
        return true;
    }

    private void DisposeNativeResults()
    {
        if (_startEdgeIndices.IsCreated) _startEdgeIndices.Dispose();
        if (_endEdgeIndices.IsCreated) _endEdgeIndices.Dispose();
        if (_resultWaypoints.IsCreated) _resultWaypoints.Dispose();
        if (_resultEdgeIndices.IsCreated) _resultEdgeIndices.Dispose();
        if (_congestionPenalties.IsCreated) _congestionPenalties.Dispose();
    }

    private bool TryFindPortAnchor(
        Vector2Int portCell,
        bool departure,
        out BuildingPortAnchorRecord anchor)
    {
        anchor = null;
        if (_trafficGraph == null) return false;

        for (int i = 0; i < _trafficGraph.BuildingPortAnchors.Count; i++)
        {
            BuildingPortAnchorRecord candidate =
                _trafficGraph.BuildingPortAnchors[i];
            if (candidate.PortCell != portCell) continue;
            if (departure && candidate.DepartureAnchors.Count == 0) continue;
            if (!departure && candidate.ArrivalAnchors.Count == 0) continue;
            anchor = candidate;
            return true;
        }

        return false;
    }

    private List<TrafficEdge> FilterEdgesByAnchors(
        List<TrafficEdge> edges,
        IReadOnlyList<LanePositionAnchorRecord> anchors)
    {
        var allowedLanes = new HashSet<LaneId>();
        for (int i = 0; i < anchors.Count; i++)
        {
            allowedLanes.Add(anchors[i].LaneId);
        }

        var filtered = new List<TrafficEdge>();
        if (edges == null) return filtered;
        for (int i = 0; i < edges.Count; i++)
        {
            TrafficEdge edge = edges[i];
            if (edge != null &&
                edge.stableLaneId.IsValid &&
                allowedLanes.Contains(edge.stableLaneId))
            {
                filtered.Add(edge);
            }
        }

        return filtered;
    }

    private List<TrafficEdge> FilterLegalEdges(List<TrafficEdge> edges)
    {
        var filtered = new List<TrafficEdge>();
        if (edges == null || _vehicleProfile == null) return filtered;
        for (int i = 0; i < edges.Count; i++)
        {
            TrafficEdge edge = edges[i];
            if (edge != null && IsLegalForVehicle(edge))
            {
                filtered.Add(edge);
            }
        }

        return filtered;
    }

    private bool IsLegalForVehicle(TrafficEdge edge)
    {
        return (edge.requiredPermissions == RoadPermissionMask.None ||
                (edge.requiredPermissions & _vehicleProfile.RoadPermissions) !=
                RoadPermissionMask.None) &&
               (edge.requiredCapabilities == VehicleCapabilityMask.None ||
                (edge.requiredCapabilities & _vehicleProfile.Capabilities) !=
                VehicleCapabilityMask.None);
    }

    private void AddMovementIfRequired(
        RouteCorridor corridor,
        TrafficEdge edge)
    {
        if (_trafficGraph == null ||
            !_trafficGraph.TryGetMovement(
                edge.stableMovementId,
                out MovementRecord movement))
        {
            AddUnique(corridor.RequiredMovementIds, edge.stableMovementId);
            return;
        }

        if (movement.IsMandatory ||
            movement.Kind != TrafficMovementKind.OptionalLaneChange)
        {
            AddUnique(corridor.RequiredMovementIds, movement.Id);
        }
    }

    private void BuildAcceptableLaneSets(RouteCorridor corridor)
    {
        if (_trafficGraph == null || _vehicleProfile == null) return;

        for (int sectionIndex = 0;
             sectionIndex < corridor.RoadSectionIds.Count;
             sectionIndex++)
        {
            RoadSectionId sectionId = corridor.RoadSectionIds[sectionIndex];
            LaneRecord routeLane = FindRouteLane(sectionId);
            if (routeLane == null) continue;

            MovementRecord requiredExit =
                FindRequiredExitMovement(corridor, sectionId);
            var laneSet = new RouteLaneSet
            {
                SectionId = sectionId,
                RequiredExitMovementId = requiredExit != null
                    ? requiredExit.Id
                    : default
            };

            for (int laneIndex = 0;
                 laneIndex < _trafficGraph.Lanes.Count;
                 laneIndex++)
            {
                LaneRecord candidate = _trafficGraph.Lanes[laneIndex];
                if (candidate.SectionId != sectionId ||
                    candidate.FlowDirection != routeLane.FlowDirection ||
                    !IsLegalForVehicle(candidate))
                {
                    continue;
                }

                if (requiredExit != null &&
                    !LaneCanServeRequiredExit(candidate, requiredExit))
                {
                    continue;
                }

                laneSet.AcceptableLaneIds.Add(candidate.Id);
            }

            if (laneSet.AcceptableLaneIds.Count == 0)
            {
                laneSet.AcceptableLaneIds.Add(routeLane.Id);
            }
            corridor.AcceptableLaneSets.Add(laneSet);
        }
    }

    private LaneRecord FindRouteLane(RoadSectionId sectionId)
    {
        for (int i = 0; i < _resultEdgeIndices.Length; i++)
        {
            int edgeIndex = _resultEdgeIndices[i];
            if (edgeIndex < 0 || edgeIndex >= _managedEdgesByIndex.Length)
            {
                continue;
            }
            TrafficEdge edge = _managedEdgesByIndex[edgeIndex];
            if (edge != null &&
                edge.stableSectionId == sectionId &&
                edge.stableLaneId.IsValid &&
                _trafficGraph.TryGetLane(edge.stableLaneId, out LaneRecord lane))
            {
                return lane;
            }
        }
        return null;
    }

    private MovementRecord FindRequiredExitMovement(
        RouteCorridor corridor,
        RoadSectionId sectionId)
    {
        for (int i = 0; i < corridor.RequiredMovementIds.Count; i++)
        {
            if (_trafficGraph.TryGetMovement(
                    corridor.RequiredMovementIds[i],
                    out MovementRecord movement) &&
                _trafficGraph.TryGetLane(
                    movement.SourceLaneId,
                    out LaneRecord sourceLane) &&
                sourceLane.SectionId == sectionId)
            {
                return movement;
            }
        }
        return null;
    }

    private bool LaneCanServeRequiredExit(
        LaneRecord candidate,
        MovementRecord requiredExit)
    {
        if (!_trafficGraph.TryGetLane(
                requiredExit.TargetLaneId,
                out LaneRecord requiredTarget))
        {
            return false;
        }

        for (int i = 0; i < candidate.OutgoingMovementIds.Count; i++)
        {
            if (!_trafficGraph.TryGetMovement(
                    candidate.OutgoingMovementIds[i],
                    out MovementRecord movement) ||
                movement.OwnerId != requiredExit.OwnerId ||
                movement.Kind != requiredExit.Kind ||
                !IsLegalForVehicle(movement) ||
                !_trafficGraph.TryGetLane(
                    movement.TargetLaneId,
                    out LaneRecord target))
            {
                continue;
            }

            if (target.SectionId == requiredTarget.SectionId) return true;
        }

        return false;
    }

    private bool IsLegalForVehicle(LaneRecord lane)
    {
        return (lane.AllowedPermissions & _vehicleProfile.RoadPermissions) !=
               RoadPermissionMask.None &&
               (lane.AllowedCapabilities & _vehicleProfile.Capabilities) !=
               VehicleCapabilityMask.None;
    }

    private bool IsLegalForVehicle(MovementRecord movement)
    {
        return (movement.RequiredPermissions == RoadPermissionMask.None ||
                (movement.RequiredPermissions &
                 _vehicleProfile.RoadPermissions) != RoadPermissionMask.None) &&
               (movement.RequiredCapabilities == VehicleCapabilityMask.None ||
                (movement.RequiredCapabilities &
                 _vehicleProfile.Capabilities) != VehicleCapabilityMask.None);
    }

    private static void AddUnique(
        List<LaneSegmentId> values,
        LaneSegmentId value)
    {
        if (!values.Contains(value)) values.Add(value);
    }

    private static void AddUnique(
        List<MovementId> values,
        MovementId value)
    {
        if (!values.Contains(value)) values.Add(value);
    }

    private static void AddUnique(
        List<RoadSectionId> values,
        RoadSectionId value)
    {
        if (!values.Contains(value)) values.Add(value);
    }

    private List<TrafficEdge> FilterStartEdgesForEndpointDeparture(List<TrafficEdge> edges, Vector3 startPos)
    {
        List<TrafficEdge> filtered = new List<TrafficEdge>();
        if (edges == null) return filtered;

        foreach (TrafficEdge edge in edges)
        {
            if (edge == null || edge.startNode == null || edge.endNode == null) continue;

            float distanceToStart = Vector3.SqrMagnitude(startPos - edge.startNode.position);
            float distanceToEnd = Vector3.SqrMagnitude(startPos - edge.endNode.position);
            if (distanceToStart <= distanceToEnd)
            {
                filtered.Add(edge);
            }
        }

        return filtered.Count > 0 ? filtered : edges;
    }

    private List<TrafficEdge> GetDepartureEdgesFromPortCell(List<TrafficEdge> fallbackEdges, Vector2Int portCell, Vector3 startPos)
    {
        if (TrafficSystemBackend.Instance != null &&
            TrafficSystemBackend.Instance.TryGetDepartureEdgesFromPortCell(
                portCell,
                startPos,
                out List<TrafficEdge> indexedDepartureEdges))
        {
            return indexedDepartureEdges;
        }

        List<TrafficEdge> outgoingFromPort = new List<TrafficEdge>();
        List<TrafficEdge> endpointInPort = new List<TrafficEdge>();

        if (TrafficSystemBackend.Instance != null && TrafficSystemBackend.Instance.allEdges != null)
        {
            foreach (TrafficEdge edge in TrafficSystemBackend.Instance.allEdges)
            {
                if (edge == null || edge.kind != TrafficEdgeKind.RoadLane) continue;

                bool startInPortCell = edge.startNode != null && IsWorldPointInCell(edge.startNode.position, portCell);
                bool endInPortCell = edge.endNode != null && IsWorldPointInCell(edge.endNode.position, portCell);

                if (startInPortCell)
                {
                    outgoingFromPort.Add(edge);
                }
                else if (endInPortCell)
                {
                    endpointInPort.Add(edge);
                }
            }
        }

        if (outgoingFromPort.Count > 0)
        {
            SortEdgesByStartDistance(outgoingFromPort, startPos);
            return outgoingFromPort;
        }

        if (endpointInPort.Count > 0)
        {
            SortEdgesByClosestEndpointDistance(endpointInPort, startPos);
            return endpointInPort;
        }

        return FilterStartEdgesForEndpointDeparture(fallbackEdges, startPos);
    }

    private List<TrafficEdge> GetArrivalEdgesToPortCell(List<TrafficEdge> fallbackEdges, Vector2Int portCell, Vector3 targetPos)
    {
        if (TrafficSystemBackend.Instance != null &&
            TrafficSystemBackend.Instance.TryGetArrivalEdgesToPortCell(
                portCell,
                targetPos,
                out List<TrafficEdge> indexedArrivalEdges))
        {
            return indexedArrivalEdges;
        }

        List<TrafficEdge> incomingToPort = new List<TrafficEdge>();
        List<TrafficEdge> endpointInPort = new List<TrafficEdge>();

        if (TrafficSystemBackend.Instance != null && TrafficSystemBackend.Instance.allEdges != null)
        {
            foreach (TrafficEdge edge in TrafficSystemBackend.Instance.allEdges)
            {
                if (edge == null || edge.kind != TrafficEdgeKind.RoadLane) continue;

                bool startInPortCell = edge.startNode != null && IsWorldPointInCell(edge.startNode.position, portCell);
                bool endInPortCell = edge.endNode != null && IsWorldPointInCell(edge.endNode.position, portCell);

                if (endInPortCell)
                {
                    incomingToPort.Add(edge);
                }
                else if (startInPortCell)
                {
                    endpointInPort.Add(edge);
                }
            }
        }

        if (incomingToPort.Count > 0)
        {
            SortEdgesByEndDistance(incomingToPort, targetPos);
            return incomingToPort;
        }

        if (endpointInPort.Count > 0)
        {
            SortEdgesByClosestEndpointDistance(endpointInPort, targetPos);
            return endpointInPort;
        }

        return fallbackEdges;
    }

    private bool IsWorldPointInCell(Vector3 point, Vector2Int cell)
    {
        if (WorldManager.Instance == null) return false;

        float cellSize = WorldManager.Instance.cellSize;
        int x = Mathf.FloorToInt(point.x / cellSize);
        int z = Mathf.FloorToInt(point.z / cellSize);
        return x == cell.x && z == cell.y;
    }

    private void SortEdgesByStartDistance(List<TrafficEdge> edges, Vector3 startPos)
    {
        edges.Sort((a, b) =>
        {
            float aDistance = a != null && a.startNode != null ? Vector3.SqrMagnitude(startPos - a.startNode.position) : float.MaxValue;
            float bDistance = b != null && b.startNode != null ? Vector3.SqrMagnitude(startPos - b.startNode.position) : float.MaxValue;
            return aDistance.CompareTo(bDistance);
        });
    }

    private void SortEdgesByEndDistance(List<TrafficEdge> edges, Vector3 targetPos)
    {
        edges.Sort((a, b) =>
        {
            float aDistance = a != null && a.endNode != null ? Vector3.SqrMagnitude(targetPos - a.endNode.position) : float.MaxValue;
            float bDistance = b != null && b.endNode != null ? Vector3.SqrMagnitude(targetPos - b.endNode.position) : float.MaxValue;
            return aDistance.CompareTo(bDistance);
        });
    }

    private void SortEdgesByClosestEndpointDistance(List<TrafficEdge> edges, Vector3 startPos)
    {
        edges.Sort((a, b) =>
        {
            float aDistance = GetClosestEndpointDistanceSqr(a, startPos);
            float bDistance = GetClosestEndpointDistanceSqr(b, startPos);
            return aDistance.CompareTo(bDistance);
        });
    }

    private float GetClosestEndpointDistanceSqr(TrafficEdge edge, Vector3 startPos)
    {
        if (edge == null) return float.MaxValue;

        float startDistance = edge.startNode != null ? Vector3.SqrMagnitude(startPos - edge.startNode.position) : float.MaxValue;
        float endDistance = edge.endNode != null ? Vector3.SqrMagnitude(startPos - edge.endNode.position) : float.MaxValue;
        return Mathf.Min(startDistance, endDistance);
    }

    private float GetStartDistanceOnFirstEdge(TrafficEdge firstEdge)
    {
        if (firstEdge == null) return 0f;
        if (!_snapStartToClosestEdgeEndpoint)
        {
            return firstEdge.GetClosestDistanceAlongEdge(_resolvedStartPos);
        }

        float distanceToStart = firstEdge.startNode != null
            ? Vector3.SqrMagnitude(_resolvedStartPos - firstEdge.startNode.position)
            : float.MaxValue;
        float distanceToEnd = firstEdge.endNode != null
            ? Vector3.SqrMagnitude(_resolvedStartPos - firstEdge.endNode.position)
            : float.MaxValue;

        return distanceToEnd < distanceToStart ? firstEdge.totalLength : 0f;
    }

    private float GetEndDistanceOnFinalEdge(TrafficEdge finalEdge)
    {
        if (finalEdge == null) return 0f;
        if (!_hasTargetPortCell)
        {
            return finalEdge.GetClosestDistanceAlongEdge(_targetPos);
        }

        if (finalEdge.endNode != null && IsWorldPointInCell(finalEdge.endNode.position, _targetPortCell))
        {
            return finalEdge.totalLength;
        }

        if (finalEdge.startNode != null && IsWorldPointInCell(finalEdge.startNode.position, _targetPortCell))
        {
            return 0f;
        }

        return finalEdge.GetClosestDistanceAlongEdge(_targetPos);
    }
}
