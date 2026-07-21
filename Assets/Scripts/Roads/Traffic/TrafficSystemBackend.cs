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

    private Dictionary<Vector2Int, List<LaneEndpoint>> _intersectionIncoming =
        new Dictionary<Vector2Int, List<LaneEndpoint>>();
    private Dictionary<Vector2Int, List<LaneEndpoint>> _intersectionOutgoing =
        new Dictionary<Vector2Int, List<LaneEndpoint>>();
    private Dictionary<Vector2Int, IIntersectionController> _intersectionControllers =
        new Dictionary<Vector2Int, IIntersectionController>();
    private readonly TrafficSpatialIndex _spatialIndex = new TrafficSpatialIndex();
    private readonly Dictionary<LaneSegmentId, TrafficEdge> _edgeByLaneSegmentId =
        new Dictionary<LaneSegmentId, TrafficEdge>();
    private readonly Dictionary<MovementId, TrafficEdge> _edgeByMovementId =
        new Dictionary<MovementId, TrafficEdge>();
    private TrafficDiagnosticCollection _lastSnapshotDiagnostics =
        new TrafficDiagnosticCollection();
    private TrafficDiagnosticCollection _lastCompilerDiagnostics =
        new TrafficDiagnosticCollection();
    private int _lastReservedGraphVersion;

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

        TrafficGenerationTask task = new TrafficGenerationTask(
            roadSnapshot,
            ReserveNextGraphVersion());
        SimulationTaskManager.Instance.EnqueueTask(task);
    }

    public bool TryApplyNewNetwork(
        RoadNetworkSnapshot sourceSnapshot,
        TrafficGraphSnapshot compiledGraph,
        TrafficDiagnosticCollection compilerDiagnostics)
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
        if (compiledGraphCanPublish &&
            !ManagedTrafficGraphAdapter.TryBuild(
                compiledGraph,
                _lastCompilerDiagnostics,
                out adapterResult))
        {
            compiledGraphCanPublish = false;
        }

        if (!compiledGraphCanPublish)
        {
            if (compiledGraph == null)
            {
                _lastCompilerDiagnostics.AddError(
                    TrafficDiagnosticCode.CompilerStageFailed,
                    "Traffic graph compilation produced no immutable graph.",
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

        CurrentTrafficGraphSnapshot = compiledGraph;
        CurrentManagedAdapterResult = adapterResult;
        CurrentRoadSnapshot = sourceSnapshot;
        allNodes = adapterResult != null
            ? adapterResult.Nodes
            : new List<TrafficNode>();
        allEdges = adapterResult != null
            ? adapterResult.Edges
            : new List<TrafficEdge>();
        _intersectionIncoming = adapterResult != null
            ? adapterResult.IncomingByCell
            : new Dictionary<Vector2Int, List<LaneEndpoint>>();
        _intersectionOutgoing = adapterResult != null
            ? adapterResult.OutgoingByCell
            : new Dictionary<Vector2Int, List<LaneEndpoint>>();

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
        CurrentPerformanceSnapshot.GraphNodeCount = allNodes != null
            ? allNodes.Count
            : 0;
        CurrentPerformanceSnapshot.GraphEdgeCount = allEdges != null
            ? allEdges.Count
            : 0;
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

        var movementEdgesByCell = new Dictionary<Vector2Int, List<TrafficEdge>>();
        foreach (TrafficEdge edge in allEdges)
        {
            edge.exitController = null;
            if (edge.kind != TrafficEdgeKind.IntersectionMovement ||
                !edge.hasControlledNodeCell)
            {
                continue;
            }

            if (!movementEdgesByCell.TryGetValue(
                    edge.controlledNodeCell,
                    out List<TrafficEdge> movementEdges))
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

            IIntersectionController controller =
                CreateIntersectionController(kvp.Key, data, kvp.Value);
            _intersectionControllers[kvp.Key] = controller;

            foreach (TrafficEdge edge in kvp.Value)
            {
                edge.exitController = controller;
            }
        }
    }

    private IIntersectionController CreateIntersectionController(
        Vector2Int cell,
        IntersectionData data,
        List<TrafficEdge> movementEdges)
    {
        IntersectionRuleType ruleType = data != null
            ? data.RuleType
            : IntersectionRuleType.FreeForAll;
        IIntersectionController controller = ruleType == IntersectionRuleType.FIFO
            ? new FIFOIntersectionController()
            : new FreeForAllIntersectionController();
        controller.Initialize(cell, data, movementEdges);
        return controller;
    }

    public TrafficEdge GetClosestLane(Vector3 worldPoint, float searchRadius)
    {
        if (_spatialIndex.IsReady)
        {
            TrafficEdge indexed =
                _spatialIndex.GetClosestLane(worldPoint, searchRadius);
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
                float dist = TrafficSpatialIndex.DistanceToLineSegment(
                    worldPoint,
                    edge.waypoints[i],
                    edge.waypoints[i + 1]);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closestEdge = edge;
                }
            }
        }

        return closestEdge;
    }

    public List<TrafficEdge> GetClosestLanes(
        Vector3 worldPoint,
        float searchRadius,
        int maxResults = 3)
    {
        if (_spatialIndex.IsReady)
        {
            List<TrafficEdge> indexed =
                _spatialIndex.GetClosestLanes(
                    worldPoint,
                    searchRadius,
                    maxResults);
            RefreshPerformanceSnapshot();
            return indexed;
        }

        var candidates = new List<KeyValuePair<TrafficEdge, float>>();

        foreach (TrafficEdge edge in allEdges)
        {
            if (edge.kind != TrafficEdgeKind.RoadLane) continue;

            float minDistanceForEdge = float.MaxValue;
            for (int i = 0; i < edge.waypoints.Count - 1; i++)
            {
                float dist = TrafficSpatialIndex.DistanceToLineSegment(
                    worldPoint,
                    edge.waypoints[i],
                    edge.waypoints[i + 1]);
                if (dist < minDistanceForEdge) minDistanceForEdge = dist;
            }

            if (minDistanceForEdge <= searchRadius)
            {
                candidates.Add(
                    new KeyValuePair<TrafficEdge, float>(
                        edge,
                        minDistanceForEdge));
            }
        }

        candidates.Sort((left, right) => left.Value.CompareTo(right.Value));

        var result = new List<TrafficEdge>();
        int count = Mathf.Min(maxResults, candidates.Count);
        for (int i = 0; i < count; i++) result.Add(candidates[i].Key);

        return result;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (allEdges == null || allNodes == null) return;

        Camera cam = Camera.current ??
                     UnityEditor.SceneView.currentDrawingSceneView?.camera;
        Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
        float cullDistanceSqr = 200f * 200f;

        if (showLogicalConnections)
        {
            foreach (TrafficEdge edge in allEdges)
            {
                if (cam != null &&
                    (edge.startNode.position - camPos).sqrMagnitude >
                    cullDistanceSqr)
                {
                    continue;
                }

                Gizmos.color = edge.edgeColor;
                if (edge.waypoints.Count >= 2)
                {
                    for (int i = 0; i < edge.waypoints.Count - 1; i++)
                    {
                        Gizmos.DrawLine(edge.waypoints[i], edge.waypoints[i + 1]);
                    }
                }

                Gizmos.color = _nodeColor;
                Gizmos.DrawSphere(
                    edge.endNode.position + Vector3.up * 0.3f,
                    0.05f);
            }

            return;
        }

        if (!showWaypoints) return;

        foreach (TrafficNode node in allNodes)
        {
            if (cam != null &&
                (node.position - camPos).sqrMagnitude > cullDistanceSqr)
            {
                continue;
            }

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
            if (cam != null &&
                (edge.startNode.position - camPos).sqrMagnitude >
                cullDistanceSqr)
            {
                continue;
            }

            Gizmos.color = edge.edgeColor;

            if (edge.isIntersection)
            {
                Vector3 previous = edge.waypoints[0] + Vector3.up * 0.15f;
                for (int i = 1; i < edge.waypoints.Count; i++)
                {
                    Vector3 current = edge.waypoints[i] + Vector3.up * 0.15f;
                    Gizmos.DrawLine(previous, current);
                    previous = current;
                }

                if (edge.waypoints.Count > 0)
                {
                    Gizmos.DrawSphere(
                        edge.waypoints[edge.waypoints.Count - 1] +
                        Vector3.up * 0.15f,
                        0.04f);
                }
            }
            else
            {
                for (int i = 0; i < edge.waypoints.Count - 1; i++)
                {
                    Gizmos.DrawLine(edge.waypoints[i], edge.waypoints[i + 1]);
                }
            }
        }
    }
#endif
}
