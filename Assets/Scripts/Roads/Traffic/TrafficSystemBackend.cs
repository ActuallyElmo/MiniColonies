using System;
using System.Collections.Generic;
using System.Text;
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
    private readonly HashSet<string> _loggedDiagnosticKeys =
        new HashSet<string>(StringComparer.Ordinal);
    private int _lastReservedGraphVersion;
    private const int MaxLoggedDiagnosticLines = 8;

    [Header("Debug Visualization")]
    public bool showWaypoints = true;
    public bool showLogicalConnections = false;
    private Color _nodeColor = new Color(1f, 1f, 1f, 0.5f);

    public event Action OnTrafficNetworkReady;
    // Payload is the active 0-9 signal index. Direction masks cannot distinguish lanes.
    public event Action<Vector2Int, int> OnTrafficLightStateChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
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

        if (Instance == this) Instance = null;
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
        LogDiagnosticErrorsOnce("Traffic snapshot", _lastSnapshotDiagnostics);
    }

    private void LogCompilerErrors()
    {
        LogDiagnosticErrorsOnce("Traffic compiler", _lastCompilerDiagnostics);
    }

    private void LogDiagnosticErrorsOnce(
        string label,
        TrafficDiagnosticCollection diagnostics)
    {
        if (diagnostics == null || !diagnostics.HasErrors) return;

        string diagnosticKey = BuildDiagnosticErrorKey(label, diagnostics);
        if (!_loggedDiagnosticKeys.Add(diagnosticKey)) return;

        int errorCount = 0;
        for (int i = 0; i < diagnostics.Count; i++)
        {
            if (diagnostics[i].Severity == TrafficDiagnosticSeverity.Error)
            {
                errorCount++;
            }
        }

        var message = new StringBuilder(512);
        message.Append(label);
        message.Append(" failed with ");
        message.Append(errorCount);
        message.Append(errorCount == 1 ? " error." : " errors.");

        int loggedLines = 0;
        for (int i = 0;
             i < diagnostics.Count && loggedLines < MaxLoggedDiagnosticLines;
             i++)
        {
            TrafficDiagnostic diagnostic = diagnostics[i];
            if (diagnostic.Severity != TrafficDiagnosticSeverity.Error)
            {
                continue;
            }

            message.AppendLine();
            message.Append(diagnostic);
            loggedLines++;
        }

        if (errorCount > loggedLines)
        {
            message.AppendLine();
            message.Append("Additional errors suppressed: ");
            message.Append(errorCount - loggedLines);
        }

        Debug.LogError(message.ToString());
    }

    private string BuildDiagnosticErrorKey(
        string label,
        TrafficDiagnosticCollection diagnostics)
    {
        var key = new StringBuilder(512);
        key.Append(label);
        for (int i = 0; i < diagnostics.Count; i++)
        {
            TrafficDiagnostic diagnostic = diagnostics[i];
            if (diagnostic.Severity != TrafficDiagnosticSeverity.Error)
            {
                continue;
            }

            TrafficDiagnosticSource source = diagnostic.Source;
            key.Append('|');
            key.Append((int)diagnostic.Code);
            key.Append(':');
            key.Append(diagnostic.Message);
            key.Append(":stable=");
            key.Append(source.StableId);
            key.Append(":profile=");
            key.Append(source.ProfileKey);
            if (source.HasSourceCell)
            {
                key.Append(":cell=");
                key.Append(source.SourceCell.x);
                key.Append(',');
                key.Append(source.SourceCell.y);
            }
            if (source.VehicleId.IsValid)
            {
                key.Append(":vehicle=");
                key.Append(source.VehicleId.Value);
            }
        }

        return key.ToString();
    }

    public void TickIntersectionControllers(float deltaTime)
    {
        foreach (IIntersectionController controller in _intersectionControllers.Values)
        {
            int previousIndex = controller is TrafficLightIntersectionController previousLight
                ? previousLight.ActiveSignalIndex
                : -1;
            controller.Tick(deltaTime);
            if (controller is TrafficLightIntersectionController light &&
                light.ActiveSignalIndex != previousIndex)
            {
                OnTrafficLightStateChanged?.Invoke(controller.Cell, light.ActiveSignalIndex);
            }
        }
    }

    public bool TryGetTrafficLightState(
        Vector2Int cell,
        int incomingDirectionBit,
        out bool green)
    {
        green = false;
        if (!_intersectionControllers.TryGetValue(cell, out IIntersectionController controller) ||
            !(controller is TrafficLightIntersectionController light)) return false;
        green = light.IsGreenForDirection(incomingDirectionBit);
        return true;
    }

    public bool TryGetTrafficLightState(Vector2Int cell, LaneId incomingLane, out bool green)
    {
        green = false;
        if (CurrentTrafficGraphSnapshot == null ||
            !CurrentTrafficGraphSnapshot.TryGetLane(incomingLane, out var lane) ||
            lane.EndAnchorCell != cell ||
            !_intersectionControllers.TryGetValue(cell, out var controller) ||
            !(controller is TrafficLightIntersectionController light) ||
            !light.TryGetSignalIndex(lane.EndLegDirectionBit, lane.LaneOrdinal, out _)) return false;
        green = light.IsGreenForLane(lane.EndLegDirectionBit, lane.LaneOrdinal);
        return true;
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
        Dictionary<Vector2Int, IIntersectionController> previousControllers =
            _intersectionControllers;
        var rebuiltControllers =
            new Dictionary<Vector2Int, IIntersectionController>();

        var movementEdgesByCell = new Dictionary<Vector2Int, List<TrafficEdge>>();
        var publishedNodesByCell = new Dictionary<Vector2Int, ControlledNodeRecord>();
        if (CurrentTrafficGraphSnapshot != null)
            foreach (var node in CurrentTrafficGraphSnapshot.ControlledNodes)
                publishedNodesByCell[node.GridPosition] = node;
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

        // A cleared entry still has a light, even if the node has no movements.
        if (CurrentTrafficGraphSnapshot != null)
            foreach (var node in CurrentTrafficGraphSnapshot.ControlledNodes)
                if (IntersectionControlUtility.UsesTrafficLights(node.RuleType) &&
                    !movementEdgesByCell.ContainsKey(node.GridPosition))
                    movementEdgesByCell.Add(node.GridPosition, new List<TrafficEdge>());

        foreach (KeyValuePair<Vector2Int, List<TrafficEdge>> kvp in movementEdgesByCell)
        {
            IntersectionData data = null;
            if (RoadSystemBackend.Instance != null)
            {
                RoadSystemBackend.Instance.Intersections.TryGetValue(kvp.Key, out data);
            }

            IIntersectionController controller =
                CreateIntersectionController(kvp.Key, data, kvp.Value,
                    publishedNodesByCell.TryGetValue(kvp.Key, out var publishedNode) ? publishedNode : null);
            if (controller is TrafficLightIntersectionController light &&
                previousControllers.TryGetValue(
                    kvp.Key, out IIntersectionController previous) &&
                previous is TrafficLightIntersectionController previousLight)
            {
                light.RestorePhaseFrom(previousLight);
            }
            rebuiltControllers[kvp.Key] = controller;

            foreach (TrafficEdge edge in kvp.Value)
            {
                edge.exitController = controller;
            }
        }

        _intersectionControllers = rebuiltControllers;
    }

    private IIntersectionController CreateIntersectionController(
        Vector2Int cell,
        IntersectionData data,
        List<TrafficEdge> movementEdges,
        ControlledNodeRecord publishedNode)
    {
        if (publishedNode != null && IntersectionControlUtility.UsesTrafficLights(publishedNode.RuleType))
        {
            // Signals use captured policy until the next validated publication.
            var publishedData = new IntersectionData(cell);
            publishedData.RuleType = publishedNode.RuleType;
            publishedData.SmartTrafficLightWaitSeconds = publishedNode.SmartTrafficLightWaitSeconds;
            publishedData.TrafficLightCycleSeconds = publishedNode.TrafficLightCycleSeconds;
            for (int i = 0; i < publishedNode.TrafficLightIndices.Count; i++)
            {
                var signal = publishedNode.TrafficLightIndices[i];
                publishedData.SetTrafficLightIndex(signal.Source.DirectionBit,
                    signal.Source.LaneIndex, signal.Index);
            }
            var sources = IntersectionConnectionEditing.CollectIncomingSources(
                CurrentTrafficGraphSnapshot, publishedNode);
            var light = new TrafficLightIntersectionController();
            light.Initialize(cell, publishedData, movementEdges, sources);
            return light;
        }
        IntersectionRuleType ruleType = publishedNode != null ? publishedNode.RuleType :
            data != null ? data.RuleType : IntersectionRuleType.FreeForAll;
        IIntersectionController controller;
        switch (ruleType)
        {
            case IntersectionRuleType.FIFO:
                controller = new FIFOIntersectionController();
                break;
            case IntersectionRuleType.PriorityRoad:
                controller = new PriorityRoadIntersectionController();
                break;
            case IntersectionRuleType.TrafficLight:
            case IntersectionRuleType.SmartTrafficLight:
                controller = new TrafficLightIntersectionController();
                break;
            default:
                controller = new FreeForAllIntersectionController();
                break;
        }
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
