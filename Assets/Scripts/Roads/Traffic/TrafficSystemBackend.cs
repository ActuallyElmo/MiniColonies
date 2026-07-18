using System;
using System.Collections.Generic;
using UnityEngine;

public class TrafficSystemBackend : MonoBehaviour
{
    public static TrafficSystemBackend Instance { get; private set; }

    [Header("Active Network Data")]
    public List<TrafficNode> allNodes = new List<TrafficNode>();
    public List<TrafficEdge> allEdges = new List<TrafficEdge>();
    public RoadNetworkSnapshot CurrentRoadSnapshot { get; private set; }
    public TrafficGraphSnapshot CurrentTrafficGraphSnapshot { get; private set; }
    public ManagedTrafficGraphAdapterResult CurrentManagedAdapterResult { get; private set; }
    public TrafficPerformanceSnapshot CurrentPerformanceSnapshot { get; private set; } =
        new TrafficPerformanceSnapshot();
    public IReadOnlyList<TrafficDiagnostic> LastSnapshotDiagnostics =>
        _lastSnapshotDiagnostics.Items;
    public IReadOnlyList<TrafficDiagnostic> LastCompilerDiagnostics =>
        _lastCompilerDiagnostics.Items;

    private Dictionary<Vector2Int, List<LaneEndpoint>> _intersectionIncoming = new Dictionary<Vector2Int, List<LaneEndpoint>>();
    private Dictionary<Vector2Int, List<LaneEndpoint>> _intersectionOutgoing = new Dictionary<Vector2Int, List<LaneEndpoint>>();
    private Dictionary<Vector2Int, IIntersectionController> _intersectionControllers = new Dictionary<Vector2Int, IIntersectionController>();
    private readonly TrafficSpatialIndex _spatialIndex = new TrafficSpatialIndex();
    private readonly Dictionary<LaneSegmentId, TrafficEdge> _edgeByLaneSegmentId =
        new Dictionary<LaneSegmentId, TrafficEdge>();
    private readonly Dictionary<MovementId, TrafficEdge> _edgeByMovementId =
        new Dictionary<MovementId, TrafficEdge>();
    private TrafficDiagnosticCollection _lastSnapshotDiagnostics = new TrafficDiagnosticCollection();
    private TrafficDiagnosticCollection _lastCompilerDiagnostics =
        new TrafficDiagnosticCollection();
    private int _lastReservedGraphVersion;

    [Header("Intersection Geometry")]
    [Range(0.1f, 0.9f)] public float intersectionNodesPullback = 0.45f;
    [Range(0.1f, 0.9f)] public float maxIntersectionNodesPullback = 0.82f;
    [Min(0f)] public float intersectionBoundaryPadding = 0.03f;

    [Header("Snapshot Compatibility")]
    public bool compareSnapshotCompatibilityAdapter;

    [Header("Debug Visualization")]
    public bool showWaypoints = true;
    public bool showLogicalConnections = false;
    private Color _nodeColor = new Color(1f, 1f, 1f, 0.5f);

    public event Action OnTrafficNetworkReady;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        if (RoadNetworkManager.Instance != null)
        {
            RoadNetworkManager.Instance.OnNetworkReady += HandleNetworkReady;
        }
    }

    private void OnDestroy()
    {
        if (RoadNetworkManager.Instance != null)
        {
            RoadNetworkManager.Instance.OnNetworkReady -= HandleNetworkReady;
        }
    }

    private void HandleNetworkReady()
    {
        _lastSnapshotDiagnostics = new TrafficDiagnosticCollection();
        if (!RoadNetworkSnapshotBuilder.TryBuild(
                RoadSystemBackend.Instance,
                BuildingSystemBackend.Instance,
                _lastSnapshotDiagnostics,
                out RoadNetworkSnapshot roadSnapshot))
        {
            LogSnapshotErrors();
            return;
        }

        List<RoadSegmentPayload> segmentsToProcess = ExtractRoadSegments(roadSnapshot);

        int smoothingIterations = 3;
        RoadVisualSystem rvs = FindFirstObjectByType<RoadVisualSystem>();
        if (rvs != null) smoothingIterations = rvs.smoothingIterations;

        TrafficGenerationTask task = new TrafficGenerationTask(
            segmentsToProcess,
            roadSnapshot,
            ReserveNextGraphVersion(),
            smoothingIterations);
        SimulationTaskManager.Instance.EnqueueTask(task);
    }

    private List<RoadSegmentPayload> ExtractRoadSegments(RoadNetworkSnapshot roadSnapshot)
    {
        List<RoadSegmentPayload> payloads = new List<RoadSegmentPayload>();
        HashSet<string> processedSegments = new HashSet<string>();
        HashSet<Vector2Int> visitedLoopCells = new HashSet<Vector2Int>();

        foreach (RoadCellRecord cellData in roadSnapshot.Cells)
        {
            Vector2Int currentCell = cellData.GridPosition;

            if (IsRealNode(cellData))
            {
                for (int i = 0; i < 8; i++)
                {
                    int bit = 1 << i;
                    if (cellData.HasPhysicalConnection(bit) && cellData.CanExit(bit))
                    {
                        Vector2Int neighborCell =
                            RoadGridDirectionUtility.GetNeighborPosition(currentCell, bit);
                        RoadProfile trueRoadProfile = GetTrueSegmentRoadProfile(
                            roadSnapshot,
                            currentCell,
                            neighborCell,
                            cellData);
                        if (trueRoadProfile == null) continue;

                        List<Vector3> fullPath = TraceTrafficPath(
                            roadSnapshot,
                            currentCell,
                            neighborCell,
                            out Vector2Int endCell);
                        string segmentHash =
                            trueRoadProfile.Directionality == RoadFlowDirectionality.TwoWay
                                ? GetTwoWayHash(currentCell, endCell)
                                : $"{currentCell}->{endCell}";

                        if (!processedSegments.Contains(segmentHash))
                        {
                            processedSegments.Add(segmentHash);
                            payloads.Add(new RoadSegmentPayload
                            {
                                centerline = fullPath,
                                roadProfile = trueRoadProfile,
                                startCell = currentCell,
                                endCell = endCell
                            });
                        }
                    }
                }
            }
        }

        foreach (RoadCellRecord cellData in roadSnapshot.Cells)
        {
            Vector2Int currentCell = cellData.GridPosition;

            if (!IsRealNode(cellData) && !visitedLoopCells.Contains(currentCell))
            {
                for (int i = 0; i < 8; i++)
                {
                    int bit = 1 << i;
                    if (cellData.HasPhysicalConnection(bit) && cellData.CanExit(bit))
                    {
                        Vector2Int neighborCell =
                            RoadGridDirectionUtility.GetNeighborPosition(currentCell, bit);
                        List<Vector2Int> cellsInLoop = new List<Vector2Int>();
                        List<Vector3> fullPath = TraceTrafficPathWithCells(
                            roadSnapshot,
                            currentCell,
                            neighborCell,
                            out Vector2Int endCell,
                            cellsInLoop);
                        foreach (Vector2Int loopCell in cellsInLoop) visitedLoopCells.Add(loopCell);

                        if (!roadSnapshot.TryGetRoadProfile(
                                cellData.RoadProfileId,
                                out RoadProfile roadProfile))
                        {
                            continue;
                        }

                        string segmentHash =
                            roadProfile.Directionality == RoadFlowDirectionality.TwoWay
                                ? GetTwoWayHash(currentCell, endCell)
                                : $"{currentCell}->{endCell}";

                        if (!processedSegments.Contains(segmentHash))
                        {
                            processedSegments.Add(segmentHash);
                            payloads.Add(new RoadSegmentPayload
                            {
                                centerline = fullPath,
                                roadProfile = roadProfile,
                                startCell = currentCell,
                                endCell = endCell
                            });
                        }
                    }
                }
            }
        }

        return payloads;
    }

    public bool TryApplyNewNetwork(
        RoadNetworkSnapshot sourceSnapshot,
        TrafficGraphSnapshot compiledGraph,
        TrafficDiagnosticCollection compilerDiagnostics,
        List<TrafficNode> newNodes,
        List<TrafficEdge> newEdges,
        Dictionary<Vector2Int, List<LaneEndpoint>> newIncoming,
        Dictionary<Vector2Int, List<LaneEndpoint>> newOutgoing)
    {
        if (sourceSnapshot == null ||
            !sourceSnapshot.MatchesCurrentSources(
                RoadSystemBackend.Instance,
                BuildingSystemBackend.Instance))
        {
            _lastSnapshotDiagnostics = new TrafficDiagnosticCollection();
            _lastSnapshotDiagnostics.AddWarning(
                TrafficDiagnosticCode.SnapshotSourceChanged,
                "Discarded traffic generation output because its authoring snapshot is stale.");
            return false;
        }

        _lastCompilerDiagnostics =
            compilerDiagnostics ?? new TrafficDiagnosticCollection();
        bool compiledGraphCanPublish =
            compiledGraph != null &&
            !_lastCompilerDiagnostics.HasErrors &&
            compiledGraph.SourceRoadRevision ==
                sourceSnapshot.RoadAuthoringRevision &&
            compiledGraph.SourceBuildingRevision ==
                sourceSnapshot.BuildingAuthoringRevision &&
            (CurrentTrafficGraphSnapshot == null ||
             compiledGraph.Version > CurrentTrafficGraphSnapshot.Version);

        ManagedTrafficGraphAdapterResult adapterResult = null;
        if (compiledGraphCanPublish)
        {
            bool adapterBuilt = ManagedTrafficGraphAdapter.TryBuild(
                compiledGraph,
                _lastCompilerDiagnostics,
                out adapterResult);
            if (!adapterBuilt)
            {
                compiledGraphCanPublish = false;
            }
            else if (compareSnapshotCompatibilityAdapter)
            {
                ManagedTrafficGraphAdapter.Compare(
                    adapterResult,
                    newNodes,
                    newEdges,
                    _lastCompilerDiagnostics);
            }
        }

        if (!compiledGraphCanPublish)
        {
            if (compiledGraph == null)
            {
                _lastCompilerDiagnostics.AddError(
                    TrafficDiagnosticCode.CompilerStageFailed,
                    "Traffic generation produced no immutable graph; refusing to publish the legacy managed graph.",
                    TrafficDiagnosticSource.None);
            }
            else if (!_lastCompilerDiagnostics.HasErrors)
            {
                _lastCompilerDiagnostics.AddError(
                    TrafficDiagnosticCode.GraphVersionMismatch,
                    "The compiled graph did not match its source revision or monotonic publication version.",
                    new TrafficDiagnosticSource(
                        compiledGraph.Version,
                        string.Empty,
                        string.Empty,
                        Vector2Int.zero,
                        false,
                        VehicleSimulationId.Invalid));
            }

            LogCompilerErrors();
            return false;
        }

        if (compiledGraphCanPublish)
        {
            // Atomic reference publication: readers see the old complete graph
            // or this complete immutable graph, never compiler working buffers.
            CurrentTrafficGraphSnapshot = compiledGraph;
            CurrentManagedAdapterResult = adapterResult;
            if (adapterResult != null)
            {
                newNodes = adapterResult.Nodes;
                newEdges = adapterResult.Edges;
                newIncoming = adapterResult.IncomingByCell;
                newOutgoing = adapterResult.OutgoingByCell;
            }
        }
        CurrentRoadSnapshot = sourceSnapshot;
        allNodes = newNodes ?? new List<TrafficNode>();
        allEdges = newEdges ?? new List<TrafficEdge>();
        _intersectionIncoming =
            newIncoming ?? new Dictionary<Vector2Int, List<LaneEndpoint>>();
        _intersectionOutgoing =
            newOutgoing ?? new Dictionary<Vector2Int, List<LaneEndpoint>>();
        RebuildStableEdgeLookup();
        _spatialIndex.Rebuild(allEdges);
        RebuildIntersectionControllers();
        RefreshPerformanceSnapshot();

        if (NativeTrafficGraph.Instance != null)
        {
            if (SimulationTaskManager.Instance != null)
            {
                SimulationTaskManager.Instance.CompleteActiveJobs();
            }

            NativeTrafficGraph.Instance.RebuildGraph(allNodes, allEdges);
        }

        if (ConveyorTrafficManager.Instance != null)
        {
            ConveyorTrafficManager.Instance.HandleTrafficGraphRebuilt(
                CurrentTrafficGraphSnapshot != null
                    ? CurrentTrafficGraphSnapshot.Version
                    : TrafficGraphVersion.Invalid);
        }

        OnTrafficNetworkReady?.Invoke();
        return true;
    }

    private TrafficGraphVersion ReserveNextGraphVersion()
    {
        int currentPublished = CurrentTrafficGraphSnapshot != null
            ? CurrentTrafficGraphSnapshot.Version.Value
            : 0;
        int basis = Mathf.Max(_lastReservedGraphVersion, currentPublished);
        if (basis == int.MaxValue)
        {
            throw new InvalidOperationException(
                "Traffic graph version overflow.");
        }

        _lastReservedGraphVersion = basis + 1;
        return new TrafficGraphVersion(_lastReservedGraphVersion);
    }

    private void LogSnapshotErrors()
    {
        for (int i = 0; i < _lastSnapshotDiagnostics.Count; i++)
        {
            TrafficDiagnostic diagnostic = _lastSnapshotDiagnostics[i];
            if (diagnostic.Severity == TrafficDiagnosticSeverity.Error)
            {
                Debug.LogError(diagnostic.ToString());
            }
        }
    }

    private void LogCompilerErrors()
    {
        for (int i = 0; i < _lastCompilerDiagnostics.Count; i++)
        {
            TrafficDiagnostic diagnostic = _lastCompilerDiagnostics[i];
            if (diagnostic.Severity == TrafficDiagnosticSeverity.Error)
            {
                Debug.LogError(diagnostic.ToString());
            }
        }
    }

    public void TickIntersectionControllers(float deltaTime)
    {
        foreach (IIntersectionController controller in _intersectionControllers.Values)
        {
            controller.Tick(deltaTime);
        }
    }

    public List<LaneEndpoint> GetIncomingLaneEndpoints(Vector2Int cell)
    {
        if (_intersectionIncoming.TryGetValue(cell, out List<LaneEndpoint> endpoints))
        {
            return new List<LaneEndpoint>(endpoints);
        }

        return new List<LaneEndpoint>();
    }

    public List<LaneEndpoint> GetOutgoingLaneEndpoints(Vector2Int cell)
    {
        if (_intersectionOutgoing.TryGetValue(cell, out List<LaneEndpoint> endpoints))
        {
            return new List<LaneEndpoint>(endpoints);
        }

        return new List<LaneEndpoint>();
    }

    public bool TryGetCurrentManagedEdge(
        TrafficEdge previousEdge,
        out TrafficEdge currentEdge)
    {
        currentEdge = null;
        if (previousEdge == null) return false;

        if (previousEdge.stableLaneSegmentId.IsValid &&
            _edgeByLaneSegmentId.TryGetValue(
                previousEdge.stableLaneSegmentId,
                out currentEdge))
        {
            return true;
        }

        if (previousEdge.stableMovementId.IsValid &&
            _edgeByMovementId.TryGetValue(
                previousEdge.stableMovementId,
                out currentEdge))
        {
            return true;
        }

        return false;
    }

    public bool TryGetDepartureEdgesFromPortCell(
        Vector2Int portCell,
        Vector3 startPos,
        out List<TrafficEdge> edges)
    {
        edges = null;
        return _spatialIndex.IsReady &&
               _spatialIndex.TryGetDepartureEdgesFromPortCell(
                   portCell,
                   startPos,
                   out edges);
    }

    public bool TryGetArrivalEdgesToPortCell(
        Vector2Int portCell,
        Vector3 targetPos,
        out List<TrafficEdge> edges)
    {
        edges = null;
        return _spatialIndex.IsReady &&
               _spatialIndex.TryGetArrivalEdgesToPortCell(
                   portCell,
                   targetPos,
                   out edges);
    }

    public void UpdateRuntimePerformanceCounters(
        float lastTickMilliseconds,
        int activeVehicles,
        int activeLanes,
        int reservedEdges,
        int reservationContentionEvents,
        TrafficCongestionSnapshot congestionSnapshot)
    {
        if (CurrentPerformanceSnapshot == null)
        {
            CurrentPerformanceSnapshot = new TrafficPerformanceSnapshot();
        }

        CurrentPerformanceSnapshot.LastTickMilliseconds = lastTickMilliseconds;
        CurrentPerformanceSnapshot.ActiveVehicles = activeVehicles;
        CurrentPerformanceSnapshot.ActiveLanes = activeLanes;
        CurrentPerformanceSnapshot.ReservedEdges = reservedEdges;
        CurrentPerformanceSnapshot.ReservationContentionEvents =
            reservationContentionEvents;
        CurrentPerformanceSnapshot.CongestionSnapshot = congestionSnapshot;
    }

    public void UpdateRoutePerformanceCounter(float lastRouteMilliseconds)
    {
        if (CurrentPerformanceSnapshot == null)
        {
            CurrentPerformanceSnapshot = new TrafficPerformanceSnapshot();
        }

        CurrentPerformanceSnapshot.LastRouteMilliseconds = lastRouteMilliseconds;
    }

    private void RebuildStableEdgeLookup()
    {
        _edgeByLaneSegmentId.Clear();
        _edgeByMovementId.Clear();
        if (allEdges == null) return;

        for (int i = 0; i < allEdges.Count; i++)
        {
            TrafficEdge edge = allEdges[i];
            if (edge == null) continue;

            if (edge.stableLaneSegmentId.IsValid &&
                !_edgeByLaneSegmentId.ContainsKey(edge.stableLaneSegmentId))
            {
                _edgeByLaneSegmentId.Add(edge.stableLaneSegmentId, edge);
            }

            if (edge.stableMovementId.IsValid &&
                !_edgeByMovementId.ContainsKey(edge.stableMovementId))
            {
                _edgeByMovementId.Add(edge.stableMovementId, edge);
            }
        }
    }

    private void RefreshPerformanceSnapshot()
    {
        if (CurrentPerformanceSnapshot == null)
        {
            CurrentPerformanceSnapshot = new TrafficPerformanceSnapshot();
        }

        CurrentPerformanceSnapshot.GraphVersion =
            CurrentTrafficGraphSnapshot != null
                ? CurrentTrafficGraphSnapshot.Version
                : TrafficGraphVersion.Invalid;
        CurrentPerformanceSnapshot.GraphNodeCount = allNodes != null ? allNodes.Count : 0;
        CurrentPerformanceSnapshot.GraphEdgeCount = allEdges != null ? allEdges.Count : 0;
        CurrentPerformanceSnapshot.IndexedLaneCount =
            _spatialIndex.IndexedLaneCount;
        CurrentPerformanceSnapshot.IndexedLaneSegmentCount =
            _spatialIndex.IndexedSegmentCount;
        CurrentPerformanceSnapshot.LastCompilerMilliseconds = 0f;
        CurrentPerformanceSnapshot.ClosestLaneCandidateSegments =
            _spatialIndex.LastCandidateSegmentCount;
        CurrentPerformanceSnapshot.ClosestLaneDistanceTests =
            _spatialIndex.LastDistanceTestCount;
    }

    private void RebuildIntersectionControllers()
    {
        _intersectionControllers.Clear();

        Dictionary<Vector2Int, List<TrafficEdge>> movementEdgesByCell = new Dictionary<Vector2Int, List<TrafficEdge>>();
        foreach (TrafficEdge edge in allEdges)
        {
            edge.exitController = null;
            if (edge.kind != TrafficEdgeKind.IntersectionMovement || !edge.hasControlledNodeCell)
            {
                continue;
            }

            if (!movementEdgesByCell.TryGetValue(edge.controlledNodeCell, out List<TrafficEdge> movementEdges))
            {
                movementEdges = new List<TrafficEdge>();
                movementEdgesByCell[edge.controlledNodeCell] = movementEdges;
            }
            movementEdges.Add(edge);
        }

        foreach (KeyValuePair<Vector2Int, List<TrafficEdge>> kvp in movementEdgesByCell)
        {
            IntersectionData data = null;
            if (RoadSystemBackend.Instance != null)
            {
                RoadSystemBackend.Instance.Intersections.TryGetValue(kvp.Key, out data);
            }

            IIntersectionController controller = CreateIntersectionController(kvp.Key, data, kvp.Value);
            _intersectionControllers[kvp.Key] = controller;

            foreach (TrafficEdge edge in kvp.Value)
            {
                edge.exitController = controller;
            }
        }
    }

    private IIntersectionController CreateIntersectionController(Vector2Int cell, IntersectionData data, List<TrafficEdge> movementEdges)
    {
        IntersectionRuleType ruleType = data != null ? data.RuleType : IntersectionRuleType.FreeForAll;
        IIntersectionController controller = ruleType == IntersectionRuleType.FIFO
            ? new FIFOIntersectionController()
            : new FreeForAllIntersectionController();
        controller.Initialize(cell, data, movementEdges);
        return controller;
    }

    // --- RUNTIME HELPERS ---

    private List<Vector3> TraceTrafficPathWithCells(
        RoadNetworkSnapshot roadSnapshot,
        Vector2Int startCell,
        Vector2Int firstNeighbor,
        out Vector2Int endCell,
        List<Vector2Int> visitedCells)
    {
        return TraceTrafficPathInternal(
            roadSnapshot,
            startCell,
            firstNeighbor,
            out endCell,
            visitedCells);
    }

    private List<Vector3> TraceTrafficPath(
        RoadNetworkSnapshot roadSnapshot,
        Vector2Int startCell,
        Vector2Int firstNeighbor,
        out Vector2Int endCell)
    {
        return TraceTrafficPathInternal(
            roadSnapshot,
            startCell,
            firstNeighbor,
            out endCell,
            null);
    }

    private List<Vector3> TraceTrafficPathInternal(
        RoadNetworkSnapshot roadSnapshot,
        Vector2Int startCell,
        Vector2Int firstNeighbor,
        out Vector2Int endCell,
        List<Vector2Int> visitedCells)
    {
        var rawWaypoints = new List<Vector3>();
        endCell = firstNeighbor;

        if (!roadSnapshot.TryGetCell(startCell, out RoadCellRecord startCellData) ||
            !roadSnapshot.TryGetCell(firstNeighbor, out RoadCellRecord firstCellData))
        {
            return rawWaypoints;
        }

        if (IsRealIntersection(startCellData) || IsTransitionNode(startCellData))
        {
            rawWaypoints.Add(Vector3.Lerp(
                startCellData.WorldCenter,
                firstCellData.WorldCenter,
                GetNodePullback(roadSnapshot, startCell, firstNeighbor, startCellData)));
        }
        else
        {
            rawWaypoints.Add(startCellData.WorldCenter);
        }

        visitedCells?.Add(startCell);

        Vector2Int previous = startCell;
        Vector2Int current = firstNeighbor;
        while (roadSnapshot.TryGetCell(current, out RoadCellRecord currentCell))
        {
            visitedCells?.Add(current);
            endCell = current;

            bool isIntersection = IsRealIntersection(currentCell);
            bool isTransition = IsTransitionNode(currentCell);
            if (isIntersection || isTransition || current == startCell)
            {
                if (isIntersection || isTransition)
                {
                    if (roadSnapshot.TryGetCell(previous, out RoadCellRecord previousCell))
                    {
                        rawWaypoints.Add(Vector3.Lerp(
                            currentCell.WorldCenter,
                            previousCell.WorldCenter,
                            GetNodePullback(
                                roadSnapshot,
                                current,
                                previous,
                                currentCell)));
                    }
                }
                else
                {
                    rawWaypoints.Add(currentCell.WorldCenter);
                }

                break;
            }

            rawWaypoints.Add(currentCell.WorldCenter);

            Vector2Int next = current;
            for (int directionIndex = 0; directionIndex < 8; directionIndex++)
            {
                int directionBit = 1 << directionIndex;
                if (!currentCell.HasPhysicalConnection(directionBit)) continue;

                Vector2Int candidate =
                    RoadGridDirectionUtility.GetNeighborPosition(current, directionBit);
                if (candidate != previous)
                {
                    next = candidate;
                    break;
                }
            }

            if (next == current) break;
            previous = current;
            current = next;
        }

        return rawWaypoints;
    }

    private bool IsRealIntersection(RoadCellRecord cell) =>
        cell.NodeKind == RoadNodeKind.Intersection;

    private bool IsTransitionNode(RoadCellRecord cell) =>
        cell.NodeKind == RoadNodeKind.Transition;

    private bool IsRealNode(RoadCellRecord cell)
    {
        return cell.NodeKind == RoadNodeKind.RoadEnd ||
               cell.NodeKind == RoadNodeKind.Transition ||
               cell.NodeKind == RoadNodeKind.Intersection;
    }

    private float GetNodePullback(
        RoadNetworkSnapshot roadSnapshot,
        Vector2Int nodeCell,
        Vector2Int legNeighbor,
        RoadCellRecord nodeData)
    {
        float basePullback = Mathf.Clamp01(intersectionNodesPullback);
        if (!IsRealIntersection(nodeData) ||
            !roadSnapshot.TryGetCell(legNeighbor, out RoadCellRecord legNeighborData))
        {
            return basePullback;
        }

        Vector2 legOffset = legNeighbor - nodeCell;
        float gridDistance = legOffset.magnitude;
        if (gridDistance <= 0.001f) return basePullback;

        Vector2 legDirection = legOffset / gridDistance;
        float legWorldDistance = Vector3.Distance(
            nodeData.WorldCenter,
            legNeighborData.WorldCenter);
        if (legWorldDistance <= 0.001f) return basePullback;

        RoadProfile legRoadProfile = GetTrueSegmentRoadProfile(
            roadSnapshot,
            nodeCell,
            legNeighbor,
            nodeData);
        float legHalfWidth =
            legRoadProfile != null ? legRoadProfile.RoadWidthUnits * 0.5f : 0f;
        float requiredWorldDistance = basePullback * legWorldDistance;

        for (int directionIndex = 0; directionIndex < 8; directionIndex++)
        {
            int directionBit = 1 << directionIndex;
            if (!nodeData.HasPhysicalConnection(directionBit)) continue;

            Vector2Int otherNeighbor =
                RoadGridDirectionUtility.GetNeighborPosition(nodeCell, directionBit);
            if (otherNeighbor == legNeighbor) continue;

            Vector2 otherOffset = otherNeighbor - nodeCell;
            if (otherOffset.sqrMagnitude <= 0.001f) continue;

            Vector2 otherDirection = otherOffset.normalized;
            float crossingSine = Mathf.Abs(
                legDirection.x * otherDirection.y -
                legDirection.y * otherDirection.x);
            if (crossingSine <= 0.001f) continue;

            RoadProfile otherRoadProfile = GetTrueSegmentRoadProfile(
                roadSnapshot,
                nodeCell,
                otherNeighbor,
                nodeData);
            float otherHalfWidth =
                otherRoadProfile != null ? otherRoadProfile.RoadWidthUnits * 0.5f : 0f;
            float directionAlignment = Mathf.Abs(Vector2.Dot(
                legDirection,
                otherDirection));
            float overlapDistance =
                (otherHalfWidth +
                 legHalfWidth * directionAlignment +
                 intersectionBoundaryPadding) /
                crossingSine;
            requiredWorldDistance = Mathf.Max(requiredWorldDistance, overlapDistance);
        }

        float requiredPullback = requiredWorldDistance / legWorldDistance;
        float maximumPullback = Mathf.Max(
            basePullback,
            maxIntersectionNodesPullback);
        if (roadSnapshot.TryGetCell(legNeighbor, out RoadCellRecord neighborData) &&
            IsRealNode(neighborData))
        {
            maximumPullback = Mathf.Min(maximumPullback, 0.49f);
        }

        return Mathf.Clamp(requiredPullback, basePullback, maximumPullback);
    }

    private string GetTwoWayHash(Vector2Int a, Vector2Int b)
    {
        // Ensure the hash is identical regardless of which direction we check first
        return (a.x < b.x || (a.x == b.x && a.y < b.y)) 
            ? $"{a}<->{b}" 
            : $"{b}<->{a}";
    }

    private List<Vector3> ApplyTrafficChaikin(List<Vector3> path, int iterations)
    {
        if (path.Count < 3 || iterations == 0) return path;
        bool isClosedLoop = Vector3.Distance(path[0], path[path.Count - 1]) < 0.01f;
        
        List<Vector3> workingPath = new List<Vector3>(path);
        if (isClosedLoop) workingPath.RemoveAt(workingPath.Count - 1);

        for (int iter = 0; iter < iterations; iter++)
        {
            List<Vector3> newPath = new List<Vector3>();
            if (!isClosedLoop) newPath.Add(workingPath[0]);

            for (int i = 0; i < workingPath.Count - (isClosedLoop ? 0 : 1); i++)
            {
                Vector3 p0 = workingPath[i];
                Vector3 p1 = workingPath[(i + 1) % workingPath.Count];
                newPath.Add(Vector3.Lerp(p0, p1, 0.25f));
                newPath.Add(Vector3.Lerp(p0, p1, 0.75f));
            }

            if (!isClosedLoop) newPath.Add(workingPath[workingPath.Count - 1]);
            workingPath = newPath;
        }

        if (isClosedLoop) workingPath.Add(workingPath[0]);
        return workingPath;
    }

    private RoadProfile GetTrueSegmentRoadProfile(
        RoadNetworkSnapshot roadSnapshot,
        Vector2Int startCellPosition,
        Vector2Int neighborCellPosition,
        RoadCellRecord startCell)
    {
        if (roadSnapshot.TryGetCell(neighborCellPosition, out RoadCellRecord neighborCell) &&
            CountConnections(neighborCell.PhysicalConnections) <= 2 &&
            roadSnapshot.TryGetRoadProfile(neighborCell.RoadProfileId, out RoadProfile neighborProfile))
        {
            return neighborProfile;
        }

        if (CountConnections(startCell.PhysicalConnections) <= 2 &&
            roadSnapshot.TryGetRoadProfile(startCell.RoadProfileId, out RoadProfile startProfile))
        {
            return startProfile;
        }

        roadSnapshot.TryGetRoadProfile(startCell.RoadProfileId, out RoadProfile fallbackProfile);
        return fallbackProfile;
    }

    private int CountConnections(int connections)
    {
        int count = 0;
        for (int directionIndex = 0; directionIndex < 8; directionIndex++)
        {
            if ((connections & (1 << directionIndex)) != 0) count++;
        }

        return count;
    }

    public TrafficEdge GetClosestLane(Vector3 worldPoint, float searchRadius)
    {
        if (_spatialIndex.IsReady)
        {
            TrafficEdge indexed = _spatialIndex.GetClosestLane(worldPoint, searchRadius);
            RefreshPerformanceSnapshot();
            return indexed;
        }

        TrafficEdge closestEdge = null;
        float minDistance = searchRadius;

        foreach (TrafficEdge edge in allEdges)
        {
            if (edge.kind != TrafficEdgeKind.RoadLane) continue;

            for (int i = 0; i < edge.waypoints.Count - 1; i++)
            {
                float dist = TrafficSpatialIndex.DistanceToLineSegment(worldPoint, edge.waypoints[i], edge.waypoints[i + 1]);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closestEdge = edge;
                }
            }
        }
        return closestEdge;
    }

    public List<TrafficEdge> GetClosestLanes(Vector3 worldPoint, float searchRadius, int maxResults = 3)
    {
        if (_spatialIndex.IsReady)
        {
            List<TrafficEdge> indexed =
                _spatialIndex.GetClosestLanes(worldPoint, searchRadius, maxResults);
            RefreshPerformanceSnapshot();
            return indexed;
        }

        List<KeyValuePair<TrafficEdge, float>> candidates = new List<KeyValuePair<TrafficEdge, float>>();

        foreach (TrafficEdge edge in allEdges)
        {
            if (edge.kind != TrafficEdgeKind.RoadLane) continue;

            float minDistanceForEdge = float.MaxValue;
            for (int i = 0; i < edge.waypoints.Count - 1; i++)
            {
                float dist = TrafficSpatialIndex.DistanceToLineSegment(worldPoint, edge.waypoints[i], edge.waypoints[i + 1]);
                if (dist < minDistanceForEdge) minDistanceForEdge = dist;
            }

            if (minDistanceForEdge <= searchRadius)
            {
                candidates.Add(new KeyValuePair<TrafficEdge, float>(edge, minDistanceForEdge));
            }
        }

        candidates.Sort((a, b) => a.Value.CompareTo(b.Value));
        
        List<TrafficEdge> result = new List<TrafficEdge>();
        for(int i = 0; i < Mathf.Min(maxResults, candidates.Count); i++) result.Add(candidates[i].Key);
        
        return result;
    }

    // --- GIZMO DEBUGGING ---
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (allEdges == null || allNodes == null) return;

        // Massive performance saver: Only render traffic nodes/paths around the active SceneView camera!
        Camera cam = Camera.current ?? UnityEditor.SceneView.currentDrawingSceneView?.camera;
        Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
        float cullDistanceSqr = 200f * 200f; // Limit rendering to a 200 unit radius

        if (showLogicalConnections)
        {
            foreach (TrafficEdge edge in allEdges)
            {
                if (cam != null && (edge.startNode.position - camPos).sqrMagnitude > cullDistanceSqr) continue;

                Gizmos.color = edge.edgeColor;
                if (edge.waypoints.Count >= 2)
                {
                    // Swapped slow DrawAAPolyLine for ultra-fast Gizmos.DrawLine
                    for (int i = 0; i < edge.waypoints.Count - 1; i++)
                        Gizmos.DrawLine(edge.waypoints[i], edge.waypoints[i + 1]);
                }

                Gizmos.color = _nodeColor;
                Gizmos.DrawSphere(edge.endNode.position + Vector3.up * 0.3f, 0.05f);
            }
            return; 
        }

        if (!showWaypoints) return;

        foreach (TrafficNode node in allNodes)
        {
            if (cam != null && (node.position - camPos).sqrMagnitude > cullDistanceSqr) continue;

            if (node.outgoingEdges.Count == 0)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(node.position + Vector3.up * 0.5f, 0.4f);
            }
            Gizmos.color = _nodeColor;
            Gizmos.DrawSphere(node.position, 0.03f);
        }

        foreach (TrafficEdge edge in allEdges)
        {
            if (cam != null && (edge.startNode.position - camPos).sqrMagnitude > cullDistanceSqr) continue;

            Gizmos.color = edge.edgeColor;

            if (edge.isIntersection)
            {
                Vector3 prev = edge.waypoints[0] + (Vector3.up * 0.15f);
                for (int i = 1; i < edge.waypoints.Count; i++)
                {
                    Vector3 curr = edge.waypoints[i] + (Vector3.up * 0.15f);
                    Gizmos.DrawLine(prev, curr);
                    prev = curr;
                }

                if (edge.waypoints.Count > 0)
                {
                    Gizmos.DrawSphere(edge.waypoints[edge.waypoints.Count - 1] + (Vector3.up * 0.15f), 0.04f);
                }
            }
            else
            {
                for (int i = 0; i < edge.waypoints.Count - 1; i++)
                    Gizmos.DrawLine(edge.waypoints[i], edge.waypoints[i + 1]);
            }
        }
    }
#endif
}
