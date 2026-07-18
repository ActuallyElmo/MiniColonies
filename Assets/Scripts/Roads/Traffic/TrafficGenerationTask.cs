using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;

public struct RoadSegmentPayload
{
    public List<Vector3> centerline;
    public RoadProfile roadProfile;
    public Vector2Int startCell;
    public Vector2Int endCell;
}

public class TrafficGenerationTask : ISimulationTask
{
    private const float LaneChangeLength = 1.5f;
    private const float MinimumLaneChangeLength = 0.6f;
    private const float LaneChangeOpportunitySpacing = 3f;
    private const float LaneChangeEndpointMargin = 0.35f;
    private const float MinimumLaneChangeEndpointMargin = 0.1f;
    private const int ConnectorCurveResolution = 16;

    private enum TaskState { CompileSnapshot, Init, ScheduleJob, WaitForJob, BuildGraph, RouteDeadEnds, RouteTransitions, RouteIntersections, Complete }
    private TaskState _state = TaskState.CompileSnapshot;

    private List<RoadSegmentPayload> _segmentsToProcess;
    private RoadNetworkSnapshot _roadSnapshot;
    private readonly TrafficGraphCompiler _graphCompiler;
    private TrafficGraphSnapshot _compiledGraph;
    private JobHandle _jobHandle;

    // Native Data
    private NativeArray<float3> _segmentCenterlines;
    private NativeArray<int> _segmentOffsets; 
    private NativeArray<RoadTypeData> _roadTypes;
    private NativeArray<int2> _startCells;
    private NativeArray<int2> _endCells;
    
    // Outputs from Job
    private NativeList<EdgeData> _generatedEdges;
    private NativeList<float3> _generatedWaypoints;

    // Backend Buffers
    private List<TrafficNode> _newNodes = new List<TrafficNode>();
    private List<TrafficEdge> _newEdges = new List<TrafficEdge>();
    private Dictionary<Vector2Int, List<LaneEndpoint>> _newIncoming = new Dictionary<Vector2Int, List<LaneEndpoint>>();
    private Dictionary<Vector2Int, List<LaneEndpoint>> _newOutgoing = new Dictionary<Vector2Int, List<LaneEndpoint>>();
    private Dictionary<TrafficNode, List<Vector2Int>> _nodeTouchedCells = new Dictionary<TrafficNode, List<Vector2Int>>();
    private int _nextEdgeId = 0;

    // Iterators for Time-Slicing
    private int _edgeIndex = 0;
    private IEnumerator<KeyValuePair<Vector2Int, List<LaneEndpoint>>> _deadEndEnumerator;
    private IEnumerator<KeyValuePair<Vector2Int, List<LaneEndpoint>>> _transitionEnumerator;
    private IEnumerator<KeyValuePair<Vector2Int, List<LaneEndpoint>>> _intersectionEnumerator;

    public struct RoadTypeData
    {
        public int lanesPerWay;
        public bool isTwoWay;
        public float roadWidth;
        public float speedLimit;
    }

    public struct EdgeData
    {
        public int startWaypointIndex;
        public int waypointCount;
        public int laneIndex;       // Local index (0 to lanesPerWay-1)
        public int totalLanes;      // Total physical lanes
        public int lanesPerWay;     // Lanes per direction
        public bool isTwoWay;
        public float speedLimit;
        public bool isMerge;
        public int2 startCell;
        public int2 endCell;
    }

    private class RoadLeg
    {
        public Vector3 OutwardDirection;
        public Vector2Int NeighborCell;   // The intersection cell this leg physically connects to
        public List<LaneEndpoint> IncomingLanes = new List<LaneEndpoint>();
        public List<LaneEndpoint> OutgoingLanes = new List<LaneEndpoint>();
    }

    private struct LaneChangeWindow
    {
        public float StartNormalized;
        public float EndNormalized;
    }

    private int _smoothingIterations;

    public TrafficGenerationTask(
        List<RoadSegmentPayload> segments,
        RoadNetworkSnapshot roadSnapshot,
        TrafficGraphVersion targetGraphVersion,
        int smoothingIterations = 3)
    {
        _segmentsToProcess = segments;
        _roadSnapshot = roadSnapshot;
        _graphCompiler = new TrafficGraphCompiler(
            roadSnapshot,
            targetGraphVersion);
        _smoothingIterations = smoothingIterations;
    }

    public bool Process(Stopwatch timer, float maxMillisecondsPerFrame)
    {
        if (_state == TaskState.CompileSnapshot)
        {
            if (!_graphCompiler.ProcessNextStage()) return false;

            _compiledGraph = _graphCompiler.Result;
            _state = TaskState.Init;
            if (timer.ElapsedMilliseconds >= maxMillisecondsPerFrame) return false;
        }

        if (_state == TaskState.Init)
        {
            int totalPoints = 0;
            foreach (var seg in _segmentsToProcess) totalPoints += seg.centerline.Count;

            _segmentCenterlines = new NativeArray<float3>(totalPoints, Allocator.Persistent);
            _segmentOffsets = new NativeArray<int>(_segmentsToProcess.Count + 1, Allocator.Persistent);
            _roadTypes = new NativeArray<RoadTypeData>(_segmentsToProcess.Count, Allocator.Persistent);
            _startCells = new NativeArray<int2>(_segmentsToProcess.Count, Allocator.Persistent);
            _endCells = new NativeArray<int2>(_segmentsToProcess.Count, Allocator.Persistent);
            
            _generatedEdges = new NativeList<EdgeData>(Allocator.Persistent);
            _generatedWaypoints = new NativeList<float3>(Allocator.Persistent);

            int currentOffset = 0;
            for (int i = 0; i < _segmentsToProcess.Count; i++)
            {
                _segmentOffsets[i] = currentOffset;
                for (int p = 0; p < _segmentsToProcess[i].centerline.Count; p++)
                {
                    _segmentCenterlines[currentOffset + p] = _segmentsToProcess[i].centerline[p];
                }
                currentOffset += _segmentsToProcess[i].centerline.Count;

                _roadTypes[i] = new RoadTypeData
                {
                    lanesPerWay = _segmentsToProcess[i].roadProfile.ForwardLaneCount,
                    isTwoWay =
                        _segmentsToProcess[i].roadProfile.Directionality ==
                        RoadFlowDirectionality.TwoWay,
                    roadWidth = _segmentsToProcess[i].roadProfile.RoadWidthUnits,
                    speedLimit = _segmentsToProcess[i].roadProfile.SpeedLimitUnitsPerSecond
                };

                _startCells[i] = new int2(_segmentsToProcess[i].startCell.x, _segmentsToProcess[i].startCell.y);
                _endCells[i] = new int2(_segmentsToProcess[i].endCell.x, _segmentsToProcess[i].endCell.y);
            }
            _segmentOffsets[_segmentsToProcess.Count] = currentOffset;

            _state = TaskState.ScheduleJob;
        }

        if (_state == TaskState.ScheduleJob)
        {
            var job = new TrafficMathJob
            {
                centerlines = _segmentCenterlines,
                segmentOffsets = _segmentOffsets,
                roadTypes = _roadTypes,
                startCells = _startCells,
                endCells = _endCells,
                outEdges = _generatedEdges,
                outWaypoints = _generatedWaypoints,
                smoothingIterations = _smoothingIterations
            };

            _jobHandle = job.Schedule();
            SimulationTaskManager.Instance.RegisterJob(_jobHandle); 
            _state = TaskState.WaitForJob;
        }

        if (_state == TaskState.WaitForJob)
        {
            if (!_jobHandle.IsCompleted) return false; 
            
            _jobHandle.Complete();
            _state = TaskState.BuildGraph;
        }

        if (_state == TaskState.BuildGraph)
        {
            // Process edges segment by segment to generate continuous lanes
            while (_edgeIndex < _generatedEdges.Length)
            {
                int totalLanes = _generatedEdges[_edgeIndex].totalLanes;
                int lanesPerWay = _generatedEdges[_edgeIndex].lanesPerWay;
                bool isTwoWay = _generatedEdges[_edgeIndex].isTwoWay;

                List<EdgeData> segmentEdges = new List<EdgeData>();
                List<List<Vector3>> allLaneWaypoints = new List<List<Vector3>>();
                
                for(int i = 0; i < totalLanes; i++)
                {
                    EdgeData ed = _generatedEdges[_edgeIndex + i];
                    segmentEdges.Add(ed);

                    List<Vector3> pts = new List<Vector3>(ed.waypointCount);
                    for(int w = 0; w < ed.waypointCount; w++) 
                        pts.Add(_generatedWaypoints[ed.startWaypointIndex + w]);
                    
                    allLaneWaypoints.Add(pts);
                }

                // 1. Build Straight Forward Lanes
                BuildStraightLanes(allLaneWaypoints, segmentEdges, 0, lanesPerWay);

                // 2. Build Straight Reverse Lanes
                if (isTwoWay)
                {
                    BuildStraightLanes(allLaneWaypoints, segmentEdges, lanesPerWay, totalLanes);
                }

                _edgeIndex += totalLanes;
                if (timer.ElapsedMilliseconds >= maxMillisecondsPerFrame) return false;
            }

            _deadEndEnumerator = _newIncoming.GetEnumerator();
            _state = TaskState.RouteDeadEnds;
        }

        if (_state == TaskState.RouteDeadEnds)
        {
            while (_deadEndEnumerator.MoveNext())
            {
                var kvp = _deadEndEnumerator.Current;
                Vector2Int cell = kvp.Key;

                // Only process true road ends. Transitions and intersections are handled separately.
                if (_roadSnapshot.TryGetCell(cell, out RoadCellRecord deadEndRoadCell) &&
                    deadEndRoadCell.NodeKind == RoadNodeKind.RoadEnd)
                {
                    ProcessDeadEnd(cell, kvp.Value);
                }

                if (timer.ElapsedMilliseconds >= maxMillisecondsPerFrame) return false;
            }

            _transitionEnumerator = _newIncoming.GetEnumerator();
            _state = TaskState.RouteTransitions;
        }

        if (_state == TaskState.RouteTransitions)
        {
            while (_transitionEnumerator.MoveNext())
            {
                var kvp = _transitionEnumerator.Current;
                Vector2Int cell = kvp.Key;

                if (_roadSnapshot.TryGetCell(cell, out RoadCellRecord transitionCell) &&
                    transitionCell.NodeKind == RoadNodeKind.Transition)
                {
                    ProcessTransition(cell, kvp.Value);
                }

                if (timer.ElapsedMilliseconds >= maxMillisecondsPerFrame) return false;
            }

            _intersectionEnumerator = _newIncoming.GetEnumerator();
            _state = TaskState.RouteIntersections;
        }

        if (_state == TaskState.RouteIntersections)
        {
            while (_intersectionEnumerator.MoveNext())
            {
                var kvp = _intersectionEnumerator.Current;
                Vector2Int cell = kvp.Key;

                // Road ends and transition nodes are intentionally excluded from intersection controls.
                if (!_roadSnapshot.TryGetCell(cell, out RoadCellRecord intRoadCell) ||
                    intRoadCell.NodeKind != RoadNodeKind.Intersection)
                {
                    continue;
                }

                ProcessIntersection(cell, kvp.Value);

                if (timer.ElapsedMilliseconds >= maxMillisecondsPerFrame) return false;
            }

            _state = TaskState.Complete;
        }

        if (_state == TaskState.Complete)
        {
            _segmentCenterlines.Dispose();
            _segmentOffsets.Dispose();
            _roadTypes.Dispose();
            _startCells.Dispose();
            _endCells.Dispose();
            _generatedEdges.Dispose();
            _generatedWaypoints.Dispose();

            if (TrafficSystemBackend.Instance != null)
            {
                TrafficSystemBackend.Instance.TryApplyNewNetwork(
                    _roadSnapshot,
                    _compiledGraph,
                    _graphCompiler.Diagnostics,
                    _newNodes,
                    _newEdges,
                    _newIncoming,
                    _newOutgoing);
            }
            return true;
        }

        return false;
    }

    // --- STRAIGHTAWAY GENERATOR ---

    private void BuildStraightLanes(List<List<Vector3>> allLaneWaypoints, List<EdgeData> segmentEdges, int startIdx, int endIdx)
    {
        int numLanes = endIdx - startIdx;
        if (numLanes <= 0) return;

        List<Vector3>[] laneWaypoints = new List<Vector3>[numLanes];
        List<float>[] laneDistances = new List<float>[numLanes];
        EdgeData[] laneData = new EdgeData[numLanes];
        float shortestLaneLength = float.MaxValue;

        for (int i = 0; i < numLanes; i++)
        {
            int globalIdx = startIdx + i;
            laneData[i] = segmentEdges[globalIdx];
            laneWaypoints[i] = allLaneWaypoints[globalIdx];
            laneDistances[i] = BuildCumulativeDistances(laneWaypoints[i]);
            float laneLength = laneDistances[i].Count > 0 ? laneDistances[i][laneDistances[i].Count - 1] : 0f;
            shortestLaneLength = Mathf.Min(shortestLaneLength, laneLength);
        }

        if (shortestLaneLength == float.MaxValue) shortestLaneLength = 0f;

        List<LaneChangeWindow> laneChangeWindows = BuildLaneChangeWindows(shortestLaneLength);
        List<float> stationPositions = BuildLaneStations(laneChangeWindows);
        int sectionCount = stationPositions.Count - 1;
        TrafficNode[][] laneNodes = new TrafficNode[numLanes][];
        TrafficEdge[][] roadSections = new TrafficEdge[numLanes][];

        for (int lane = 0; lane < numLanes; lane++)
        {
            EdgeData ed = laneData[lane];
            List<Vector3> points = laneWaypoints[lane];
            List<float> distances = laneDistances[lane];
            float laneLength = distances.Count > 0 ? distances[distances.Count - 1] : 0f;

            laneNodes[lane] = new TrafficNode[sectionCount + 1];
            roadSections[lane] = new TrafficEdge[sectionCount];

            for (int station = 0; station <= sectionCount; station++)
            {
                float normalizedDistance = stationPositions[station];
                TrafficNode node = new TrafficNode(GetPositionAlongPolyline(points, distances, laneLength * normalizedDistance));
                laneNodes[lane][station] = node;
                _newNodes.Add(node);
            }

            Vector2Int startCell = new Vector2Int(ed.startCell.x, ed.startCell.y);
            Vector2Int endCell = new Vector2Int(ed.endCell.x, ed.endCell.y);
            Vector3 startDirection = GetDirectionAlongPolyline(points, distances, 0f);
            Vector3 endOutwardDirection = -GetDirectionAlongPolyline(points, distances, laneLength);

            RegisterOutgoingNode(startCell, laneNodes[lane][0], ed.laneIndex, numLanes, startDirection, endCell);
            RegisterIncomingNode(endCell, laneNodes[lane][sectionCount], ed.laneIndex, numLanes, endOutwardDirection, startCell);

            for (int section = 0; section < sectionCount; section++)
            {
                float sectionStart = laneLength * stationPositions[section];
                float sectionEnd = laneLength * stationPositions[section + 1];
                TrafficEdge straight = new TrafficEdge(laneNodes[lane][section], laneNodes[lane][section + 1], ed.speedLimit);
                straight.waypoints = BuildPolylineSection(points, distances, sectionStart, sectionEnd);
                straight.edgeColor = GetLaneColor(lane);
                straight.kind = TrafficEdgeKind.RoadLane;
                straight.laneIndex = ed.laneIndex;
                straight.totalLanes = numLanes;
                straight.fromLaneIndex = ed.laneIndex;
                straight.toLaneIndex = ed.laneIndex;
                AddTouchedCell(straight, startCell);
                AddTouchedCell(straight, endCell);

                laneNodes[lane][section].outgoingEdges.Add(straight);
                roadSections[lane][section] = straight;
                RegisterEdge(straight);
            }
        }

        if (numLanes <= 1 || laneChangeWindows.Count == 0)
        {
            return;
        }

        foreach (LaneChangeWindow window in laneChangeWindows)
        {
            int startStation = FindLaneStation(stationPositions, window.StartNormalized);
            int endStation = FindLaneStation(stationPositions, window.EndNormalized);
            if (startStation < 0 || endStation != startStation + 1) continue;

            for (int lane = 0; lane < numLanes - 1; lane++)
            {
                TrafficEdge changeRight = CreateLaneChangeEdge(
                    laneNodes[lane][startStation],
                    laneNodes[lane + 1][endStation],
                    laneData[lane],
                    laneWaypoints[lane],
                    laneDistances[lane],
                    lane,
                    lane + 1,
                    numLanes,
                    window.StartNormalized,
                    roadSections[lane + 1][startStation]);

                TrafficEdge changeLeft = CreateLaneChangeEdge(
                    laneNodes[lane + 1][startStation],
                    laneNodes[lane][endStation],
                    laneData[lane + 1],
                    laneWaypoints[lane + 1],
                    laneDistances[lane + 1],
                    lane + 1,
                    lane,
                    numLanes,
                    window.StartNormalized,
                    roadSections[lane][startStation]);

                changeRight.conflictingLaneChangeEdge = changeLeft;
                changeLeft.conflictingLaneChangeEdge = changeRight;
            }
        }
    }

    private TrafficEdge CreateLaneChangeEdge(
        TrafficNode startNode,
        TrafficNode endNode,
        EdgeData sourceData,
        List<Vector3> sourceWaypoints,
        List<float> sourceDistances,
        int fromLaneIndex,
        int toLaneIndex,
        int totalLanes,
        float startNormalized,
        TrafficEdge targetRoadSection)
    {
        TrafficEdge laneChange = new TrafficEdge(startNode, endNode, sourceData.speedLimit)
        {
            isMergeEdge = true,
            reservedTargetEdge = targetRoadSection,
            edgeColor = new Color(0.8f, 0.8f, 0.8f)
        };
        ConfigureLaneChangeEdge(laneChange, fromLaneIndex, toLaneIndex, totalLanes);

        float sourceLength = sourceDistances.Count > 0 ? sourceDistances[sourceDistances.Count - 1] : 0f;
        float sourceDistance = sourceLength * startNormalized;
        Vector3 startDirection = GetDirectionAlongPolyline(sourceWaypoints, sourceDistances, sourceDistance);
        Vector3 endDirection = targetRoadSection != null
            ? targetRoadSection.GetDirectionAtDistance(targetRoadSection.totalLength)
            : startDirection;

        AddCurvedConnectorWaypoints(laneChange, startNode.position, startDirection, endNode.position, endDirection);
        AddTouchedCell(laneChange, new Vector2Int(sourceData.startCell.x, sourceData.startCell.y));
        AddTouchedCell(laneChange, new Vector2Int(sourceData.endCell.x, sourceData.endCell.y));

        startNode.outgoingEdges.Add(laneChange);
        RegisterEdge(laneChange);
        return laneChange;
    }

    private List<LaneChangeWindow> BuildLaneChangeWindows(float roadLength)
    {
        List<LaneChangeWindow> windows = new List<LaneChangeWindow>();
        if (roadLength <= 0f)
        {
            return windows;
        }

        float endpointMargin = Mathf.Min(
            LaneChangeEndpointMargin,
            Mathf.Max(MinimumLaneChangeEndpointMargin, roadLength * 0.15f));
        float availableLength = roadLength - endpointMargin * 2f;
        if (availableLength < MinimumLaneChangeLength)
        {
            return windows;
        }

        float laneChangeLength = Mathf.Min(LaneChangeLength, availableLength);
        float availableEnd = roadLength - endpointMargin;
        float latestStart = availableEnd - laneChangeLength;

        for (float start = endpointMargin;
             start <= latestStart + 0.001f;
             start += LaneChangeOpportunitySpacing)
        {
            windows.Add(new LaneChangeWindow
            {
                StartNormalized = start / roadLength,
                EndNormalized = (start + laneChangeLength) / roadLength
            });
        }

        float finalStartNormalized = latestStart / roadLength;
        if (windows.Count == 0)
        {
            windows.Add(new LaneChangeWindow
            {
                StartNormalized = finalStartNormalized,
                EndNormalized = availableEnd / roadLength
            });
            return windows;
        }

        LaneChangeWindow lastWindow = windows[windows.Count - 1];
        float lastStart = lastWindow.StartNormalized * roadLength;
        float distanceFromLastStart = latestStart - lastStart;
        if (distanceFromLastStart >= laneChangeLength + 0.1f)
        {
            windows.Add(new LaneChangeWindow
            {
                StartNormalized = finalStartNormalized,
                EndNormalized = availableEnd / roadLength
            });
        }
        else if (distanceFromLastStart > 0.25f)
        {
            windows[windows.Count - 1] = new LaneChangeWindow
            {
                StartNormalized = finalStartNormalized,
                EndNormalized = availableEnd / roadLength
            };
        }

        return windows;
    }

    private List<float> BuildLaneStations(List<LaneChangeWindow> windows)
    {
        List<float> stations = new List<float> { 0f, 1f };
        foreach (LaneChangeWindow window in windows)
        {
            stations.Add(Mathf.Clamp01(window.StartNormalized));
            stations.Add(Mathf.Clamp01(window.EndNormalized));
        }

        stations.Sort();
        for (int i = stations.Count - 1; i > 0; i--)
        {
            if (Mathf.Abs(stations[i] - stations[i - 1]) <= 0.0001f)
            {
                stations.RemoveAt(i);
            }
        }

        return stations;
    }

    private int FindLaneStation(List<float> stations, float normalizedDistance)
    {
        for (int i = 0; i < stations.Count; i++)
        {
            if (Mathf.Abs(stations[i] - normalizedDistance) <= 0.0001f)
            {
                return i;
            }
        }

        return -1;
    }

    // --- INTERSECTION ROUTING LOGIC ---

    private void ProcessIntersection(Vector2Int intersectionCell, List<LaneEndpoint> incoming)
    {
        if (!_newOutgoing.ContainsKey(intersectionCell)) return;
        List<LaneEndpoint> outgoing = _newOutgoing[intersectionCell];

        // Check if there are custom rules
        bool useCustomRules = false;
        IntersectionData intersectionData = null;
        if (_roadSnapshot.TryGetIntersectionPolicy(
                intersectionCell,
                out IntersectionPolicyRecord policy))
        {
            intersectionData = CreateCompatibilityIntersectionData(policy);
            if (intersectionData.CustomRules.Count > 0)
            {
                useCustomRules = true;
            }
            intersectionData.DefaultRules.Clear(); // Clear old defaults to rebuild them
        }

        // 1. Cluster lanes into physical "Road Legs"
        List<RoadLeg> legs = new List<RoadLeg>();

        foreach (var ep in outgoing)
        {
            Vector3 dir = ep.Direction;
            RoadLeg leg = legs.Find(l => l.NeighborCell == ep.NeighborCell);
            if (leg == null) { leg = new RoadLeg { OutwardDirection = dir, NeighborCell = ep.NeighborCell }; legs.Add(leg); }
            leg.OutgoingLanes.Add(ep);
        }

        foreach (var ep in incoming)
        {
            // Incoming and Outgoing lanes of the same road share the same outward direction
            // AND, critically, the same NeighborCell - that's what makes them one leg.
            RoadLeg leg = legs.Find(l => l.NeighborCell == ep.NeighborCell);
            if (leg == null) { leg = new RoadLeg { OutwardDirection = ep.Direction, NeighborCell = ep.NeighborCell }; legs.Add(leg); }
            leg.IncomingLanes.Add(ep);
        }

        // Sort internal lanes strictly left-to-right for each leg
        foreach (var leg in legs)
        {
            leg.IncomingLanes.Sort((a, b) => a.LocalLaneIndex.CompareTo(b.LocalLaneIndex));
            leg.OutgoingLanes.Sort((a, b) => a.LocalLaneIndex.CompareTo(b.LocalLaneIndex));
        }

        if (useCustomRules)
        {
            foreach (RoadLeg inLeg in legs)
            {
                if (inLeg.IncomingLanes.Count == 0) continue;

                foreach (RoadLeg outLeg in legs)
                {
                    if (inLeg == outLeg) continue;
                    TrafficTurnType turnType = CalculateTurnType(inLeg, outLeg);
                    MapLanes(intersectionCell, intersectionData, inLeg, outLeg, turnType, Color.clear, false);
                }
            }

            // --- CUSTOM ROUTING RULES ---
            intersectionData.InvalidCustomRuleCount = 0;
            foreach (var rule in intersectionData.CustomRules)
            {
                RoadLeg inLeg = legs.Find(l =>
                    RoadGridDirectionUtility.GetDirectionBit(
                        intersectionCell,
                        l.NeighborCell) == rule.FromDirectionBit);
                RoadLeg outLeg = legs.Find(l =>
                    RoadGridDirectionUtility.GetDirectionBit(
                        intersectionCell,
                        l.NeighborCell) == rule.ToDirectionBit);
                LaneEndpoint inLane = inLeg != null ? inLeg.IncomingLanes.Find(l => l.LocalLaneIndex == rule.FromLaneIndex) : null;
                LaneEndpoint outLane = outLeg != null ? outLeg.OutgoingLanes.Find(l => l.LocalLaneIndex == rule.ToLaneIndex) : null;

                if (inLeg == null || outLeg == null || inLane == null || outLane == null || inLeg == outLeg)
                {
                    intersectionData.InvalidCustomRuleCount++;
                    continue;
                }

                TrafficTurnType customTurnType = CalculateTurnType(inLeg, outLeg);
                ConnectIntersection(
                    inLane,
                    outLane,
                    customTurnType == TrafficTurnType.Straight ? 40f : 25f,
                    GetTurnColor(customTurnType),
                    10,
                    TrafficEdgeKind.IntersectionMovement,
                    intersectionCell,
                    true,
                    rule.FromDirectionBit,
                    rule.ToDirectionBit,
                    customTurnType);
            }
            return;
        }

        if (intersectionData != null) intersectionData.InvalidCustomRuleCount = 0;

        // 2. Map turns using Signed Angle — NEVER generate U-turns inside intersections.
        //    Dead-end U-turns are handled separately in ProcessDeadEnd.
        int autoLegIdx = 0;
        foreach (RoadLeg inLeg in legs)
        {
            if (inLeg.IncomingLanes.Count == 0) continue;

            Color legColor = GetLegColor(autoLegIdx++);

            foreach (RoadLeg outLeg in legs)
            {
                if (inLeg == outLeg) continue; // Never U-turn at intersections
                TrafficTurnType turnType = CalculateTurnType(inLeg, outLeg);
                MapLanes(intersectionCell, intersectionData, inLeg, outLeg, turnType, legColor);
            }
        }
    }

    private IntersectionData CreateCompatibilityIntersectionData(
        IntersectionPolicyRecord policy)
    {
        var data = new IntersectionData(policy.GridPosition)
        {
            NodeKind = policy.NodeKind,
            RuleType = policy.RuleType,
            PriorityDirectionBitA = policy.PriorityDirectionBitA,
            PriorityDirectionBitB = policy.PriorityDirectionBitB,
            TrafficLightCycleSeconds = policy.TrafficLightCycleSeconds
        };

        for (int i = 0; i < policy.CustomRules.Count; i++)
        {
            LaneConnectionRuleRecord rule = policy.CustomRules[i];
            data.CustomRules.Add(new LaneConnectionRule(
                rule.FromDirectionBit,
                rule.FromLaneIndex,
                rule.ToDirectionBit,
                rule.ToLaneIndex));
        }

        for (int i = 0; i < policy.DisabledRules.Count; i++)
        {
            LaneConnectionRuleRecord rule = policy.DisabledRules[i];
            data.DisabledRules.Add(new LaneConnectionRule(
                rule.FromDirectionBit,
                rule.FromLaneIndex,
                rule.ToDirectionBit,
                rule.ToLaneIndex));
        }

        return data;
    }

    private TrafficTurnType CalculateTurnType(RoadLeg inLeg, RoadLeg outLeg)
    {
        if (inLeg == outLeg) return TrafficTurnType.UTurn;

        Vector3 travelIn = -inLeg.OutwardDirection;
        Vector3 outwardOut = outLeg.OutwardDirection;
        float signedAngle = Vector3.SignedAngle(travelIn, outwardOut, Vector3.up);

        if (signedAngle > 35f && signedAngle < 145f) return TrafficTurnType.Right;
        else if (signedAngle < -35f && signedAngle > -145f) return TrafficTurnType.Left;
        else return TrafficTurnType.Straight;
    }

    private void MapLanes(Vector2Int intersectionCell, IntersectionData intersectionData, RoadLeg inLeg, RoadLeg outLeg, TrafficTurnType turnType, Color legColor, bool createEdges = true)
    {
        List<LaneEndpoint> inLanes = inLeg.IncomingLanes;
        List<LaneEndpoint> outLanes = outLeg.OutgoingLanes;

        int inCount = inLanes.Count;
        int outCount = outLanes.Count;
        if (inCount == 0 || outCount == 0) return;

        int connectCount = Mathf.Min(inCount, outCount);
        float turnSpeed = (turnType == TrafficTurnType.Straight) ? 40f : 25f;
        Color turnColor = legColor;

        int inDirBit = RoadGridDirectionUtility.GetDirectionBit(
            intersectionCell,
            inLeg.NeighborCell);
        int outDirBit = RoadGridDirectionUtility.GetDirectionBit(
            intersectionCell,
            outLeg.NeighborCell);

        if (turnType == TrafficTurnType.Left)
        {
            // Left Turns: Map innermost lanes to innermost lanes (Index 0)
            for (int i = 0; i < connectCount; i++)
            {
                if (intersectionData != null) intersectionData.DefaultRules.Add(new LaneConnectionRule(inDirBit, inLanes[i].LocalLaneIndex, outDirBit, outLanes[i].LocalLaneIndex));
                if (createEdges)
                {
                    ConnectIntersection(
                        inLanes[i],
                        outLanes[i],
                        turnSpeed,
                        turnColor,
                        10,
                        TrafficEdgeKind.IntersectionMovement,
                        intersectionCell,
                        true,
                        inDirBit,
                        outDirBit,
                        turnType);
                }
            }
        }
        else if (turnType == TrafficTurnType.Right)
        {
            // Right Turns: Map outermost lanes to outermost lanes
            int inStart = inCount - connectCount;
            int outStart = outCount - connectCount;
            for (int i = 0; i < connectCount; i++)
            {
                if (intersectionData != null) intersectionData.DefaultRules.Add(new LaneConnectionRule(inDirBit, inLanes[inStart + i].LocalLaneIndex, outDirBit, outLanes[outStart + i].LocalLaneIndex));
                if (createEdges)
                {
                    ConnectIntersection(
                        inLanes[inStart + i],
                        outLanes[outStart + i],
                        turnSpeed,
                        turnColor,
                        10,
                        TrafficEdgeKind.IntersectionMovement,
                        intersectionCell,
                        true,
                        inDirBit,
                        outDirBit,
                        turnType);
                }
            }
        }
        else // Straight
        {
            // Straights: Center the lanes
            int inOffset = (inCount - connectCount) / 2;
            int outOffset = (outCount - connectCount) / 2;
            for (int i = 0; i < connectCount; i++)
            {
                if (intersectionData != null) intersectionData.DefaultRules.Add(new LaneConnectionRule(inDirBit, inLanes[inOffset + i].LocalLaneIndex, outDirBit, outLanes[outOffset + i].LocalLaneIndex));
                if (createEdges)
                {
                    ConnectIntersection(
                        inLanes[inOffset + i],
                        outLanes[outOffset + i],
                        turnSpeed,
                        turnColor,
                        10,
                        TrafficEdgeKind.IntersectionMovement,
                        intersectionCell,
                        true,
                        inDirBit,
                        outDirBit,
                        turnType);
                }
            }
        }
    }

    // --- DEAD-END ROUTING (Separate from intersection logic) ---

    private void ProcessDeadEnd(Vector2Int deadEndCell, List<LaneEndpoint> incoming)
    {
        if (!_newOutgoing.ContainsKey(deadEndCell)) return;
        List<LaneEndpoint> outgoing = _newOutgoing[deadEndCell];

        // Sort both lists by lane index for deterministic pairing
        incoming.Sort((a, b) => a.LocalLaneIndex.CompareTo(b.LocalLaneIndex));
        outgoing.Sort((a, b) => a.LocalLaneIndex.CompareTo(b.LocalLaneIndex));

        int lanes = Mathf.Min(incoming.Count, outgoing.Count);
        if (lanes == 0) return;

        // Determine direction bit for default rules (all lanes share the same single direction)
        int dirBit = 0;
        if (incoming.Count > 0)
        {
            dirBit = RoadGridDirectionUtility.GetDirectionBit(
                deadEndCell,
                incoming[0].NeighborCell);
        }

        for (int i = 0; i < lanes; i++)
        {
            ConnectIntersection(
                incoming[i],
                outgoing[i],
                15f,
                Color.gray,
                10,
                TrafficEdgeKind.RoadEndUTurn,
                deadEndCell,
                true,
                dirBit,
                dirBit,
                TrafficTurnType.UTurn);
        }
    }

    // --- ROAD TYPE TRANSITIONS ---

    private void ProcessTransition(Vector2Int transitionCell, List<LaneEndpoint> incoming)
    {
        if (!_newOutgoing.ContainsKey(transitionCell)) return;
        List<LaneEndpoint> outgoing = _newOutgoing[transitionCell];

        List<RoadLeg> legs = BuildRoadLegs(incoming, outgoing);
        if (legs.Count < 2) return;

        legs.Sort((a, b) =>
        {
            int cellCompare = a.NeighborCell.x.CompareTo(b.NeighborCell.x);
            return cellCompare != 0 ? cellCompare : a.NeighborCell.y.CompareTo(b.NeighborCell.y);
        });

        foreach (RoadLeg inLeg in legs)
        {
            if (inLeg.IncomingLanes.Count == 0) continue;

            RoadLeg outLeg = FindOppositeTransitionLeg(inLeg, legs);
            if (outLeg == null || outLeg.OutgoingLanes.Count == 0) continue;

            MapTransitionLanes(transitionCell, inLeg, outLeg);
        }
    }

    private List<RoadLeg> BuildRoadLegs(List<LaneEndpoint> incoming, List<LaneEndpoint> outgoing)
    {
        List<RoadLeg> legs = new List<RoadLeg>();

        foreach (LaneEndpoint ep in outgoing)
        {
            RoadLeg leg = legs.Find(l => l.NeighborCell == ep.NeighborCell);
            if (leg == null)
            {
                leg = new RoadLeg { OutwardDirection = ep.Direction, NeighborCell = ep.NeighborCell };
                legs.Add(leg);
            }
            leg.OutgoingLanes.Add(ep);
        }

        foreach (LaneEndpoint ep in incoming)
        {
            RoadLeg leg = legs.Find(l => l.NeighborCell == ep.NeighborCell);
            if (leg == null)
            {
                leg = new RoadLeg { OutwardDirection = ep.Direction, NeighborCell = ep.NeighborCell };
                legs.Add(leg);
            }
            leg.IncomingLanes.Add(ep);
        }

        foreach (RoadLeg leg in legs)
        {
            leg.IncomingLanes.Sort((a, b) => a.LocalLaneIndex.CompareTo(b.LocalLaneIndex));
            leg.OutgoingLanes.Sort((a, b) => a.LocalLaneIndex.CompareTo(b.LocalLaneIndex));
        }

        return legs;
    }

    private RoadLeg FindOppositeTransitionLeg(RoadLeg inLeg, List<RoadLeg> legs)
    {
        RoadLeg bestLeg = null;
        float bestAlignment = float.NegativeInfinity;
        Vector3 travelDirection = -inLeg.OutwardDirection;

        foreach (RoadLeg candidate in legs)
        {
            if (candidate == inLeg || candidate.OutgoingLanes.Count == 0) continue;

            float alignment = Vector3.Dot(travelDirection.normalized, candidate.OutwardDirection.normalized);
            if (alignment > bestAlignment)
            {
                bestAlignment = alignment;
                bestLeg = candidate;
            }
        }

        return bestLeg;
    }

    private void MapTransitionLanes(Vector2Int transitionCell, RoadLeg inLeg, RoadLeg outLeg)
    {
        int inCount = inLeg.IncomingLanes.Count;
        int outCount = outLeg.OutgoingLanes.Count;
        if (inCount == 0 || outCount == 0) return;

        int inDirBit = RoadGridDirectionUtility.GetDirectionBit(
            transitionCell,
            inLeg.NeighborCell);
        int outDirBit = RoadGridDirectionUtility.GetDirectionBit(
            transitionCell,
            outLeg.NeighborCell);

        for (int i = 0; i < inCount; i++)
        {
            LaneEndpoint inLane = inLeg.IncomingLanes[i];
            int targetIndex;

            if (inCount == outCount)
            {
                targetIndex = Mathf.Clamp(i, 0, outCount - 1);
            }
            else if (inCount > outCount)
            {
                targetIndex = Mathf.Clamp(Mathf.FloorToInt(i * (outCount / (float)inCount)), 0, outCount - 1);
            }
            else
            {
                float scale = outCount / (float)inCount;
                targetIndex = Mathf.Clamp(Mathf.RoundToInt((i + 0.5f) * scale - 0.5f), 0, outCount - 1);
            }

            ConnectTransition(transitionCell, inLane, outLeg.OutgoingLanes[targetIndex], inDirBit, outDirBit, Mathf.Abs(i - targetIndex));

            if (outCount > inCount)
            {
                int extraLeft = targetIndex - 1;
                int extraRight = targetIndex + 1;
                if (extraLeft >= 0) ConnectTransition(transitionCell, inLane, outLeg.OutgoingLanes[extraLeft], inDirBit, outDirBit, 10 + Mathf.Abs(i - extraLeft));
                if (extraRight < outCount) ConnectTransition(transitionCell, inLane, outLeg.OutgoingLanes[extraRight], inDirBit, outDirBit, 10 + Mathf.Abs(i - extraRight));
            }
        }
    }

    private void ConnectTransition(Vector2Int transitionCell, LaneEndpoint incoming, LaneEndpoint outgoing, int fromDirectionBit, int toDirectionBit, int priority)
    {
        TrafficEdge transitionEdge = new TrafficEdge(incoming.Node, outgoing.Node, 25f);
        transitionEdge.kind = TrafficEdgeKind.RoadTypeTransition;
        transitionEdge.isRoadTypeTransition = true;
        transitionEdge.transitionCell = transitionCell;
        transitionEdge.transitionPriority = priority;
        transitionEdge.controlledNodeCell = transitionCell;
        transitionEdge.hasControlledNodeCell = true;
        transitionEdge.reservedTargetEdge = FindRoadLaneLeavingNode(outgoing.Node);
        transitionEdge.fromDirectionBit = fromDirectionBit;
        transitionEdge.toDirectionBit = toDirectionBit;
        transitionEdge.fromLaneIndex = incoming.LocalLaneIndex;
        transitionEdge.toLaneIndex = outgoing.LocalLaneIndex;
        transitionEdge.laneIndex = incoming.LocalLaneIndex;
        transitionEdge.totalLanes = incoming.TotalLanes;
        transitionEdge.turnType = TrafficTurnType.Straight;
        transitionEdge.conflictMask = BuildConflictMask(fromDirectionBit, toDirectionBit);
        transitionEdge.edgeColor = new Color(0.55f, 0.9f, 1f);
        AddTouchedCell(transitionEdge, transitionCell);
        AddTouchedCell(transitionEdge, incoming.NeighborCell);
        AddTouchedCell(transitionEdge, outgoing.NeighborCell);

        Vector3 travelIn = -incoming.Direction;
        Vector3 travelOut = outgoing.Direction;
        AddCurvedConnectorWaypoints(
            transitionEdge,
            incoming.Node.position,
            travelIn,
            outgoing.Node.position,
            travelOut);

        incoming.Node.outgoingEdges.Add(transitionEdge);
        RegisterEdge(transitionEdge);
    }

    private void ConnectIntersection(
        LaneEndpoint incoming,
        LaneEndpoint outgoing,
        float speedLimit,
        Color color,
        int resolution = 10,
        TrafficEdgeKind kind = TrafficEdgeKind.IntersectionMovement,
        Vector2Int controlledCell = default(Vector2Int),
        bool hasControlledCell = false,
        int fromDirectionBit = 0,
        int toDirectionBit = 0,
        TrafficTurnType turnType = TrafficTurnType.Straight)
    {
        TrafficNode incomingEnd = incoming.Node;
        TrafficNode outgoingStart = outgoing.Node;
        resolution = Mathf.Max(resolution, 24);

        TrafficEdge intersectionEdge = new TrafficEdge(incomingEnd, outgoingStart, speedLimit, true);
        intersectionEdge.edgeColor = color;
        intersectionEdge.kind = kind;
        intersectionEdge.controlledNodeCell = controlledCell;
        intersectionEdge.hasControlledNodeCell = hasControlledCell;
        intersectionEdge.fromDirectionBit = fromDirectionBit;
        intersectionEdge.toDirectionBit = toDirectionBit;
        intersectionEdge.fromLaneIndex = incoming.LocalLaneIndex;
        intersectionEdge.toLaneIndex = outgoing.LocalLaneIndex;
        intersectionEdge.turnType = turnType;
        intersectionEdge.conflictMask = BuildConflictMask(fromDirectionBit, toDirectionBit);
        intersectionEdge.laneIndex = incoming.LocalLaneIndex;
        intersectionEdge.totalLanes = incoming.TotalLanes;
        if (hasControlledCell) AddTouchedCell(intersectionEdge, controlledCell);
        AddTouchedCell(intersectionEdge, incoming.NeighborCell);
        AddTouchedCell(intersectionEdge, outgoing.NeighborCell);
        
        Vector3 p0 = incomingEnd.position;
        Vector3 p3 = outgoingStart.position;

        float distance = Vector3.Distance(p0, p3);

        Vector3 outwardIn = incoming.Direction;  
        Vector3 outwardOut = outgoing.Direction; 
        
        Vector3 travelIn = -outwardIn;   
        Vector3 travelOut = outwardOut;  
        
        Vector3 flatInDir = new Vector3(travelIn.x, 0, travelIn.z).normalized;
        Vector3 flatOutDir = new Vector3(travelOut.x, 0, travelOut.z).normalized;
        
        float dot = Vector3.Dot(flatInDir, flatOutDir);
        float signedAngle = Vector3.SignedAngle(flatInDir, flatOutDir, Vector3.up);

        bool isUTurn = Mathf.Abs(signedAngle) > 165f || dot < -0.95f;

        if (isUTurn)
        {
            intersectionEdge.isUTurn = true;
            float controlScale = Mathf.Max(distance * 1.5f, 0.4f);
            Vector3 p1 = p0 + travelIn * controlScale;
            Vector3 p2 = p3 - travelOut * controlScale;

            for (int i = 0; i <= resolution; i++)
            {
                float t = i / (float)resolution;
                float u = 1f - t;
                float tt = t * t;
                Vector3 point = (u * u * u) * p0;
                point += 3f * (u * u) * t * p1;
                point += 3f * u * tt * p2;
                point += (tt * t) * p3;
                intersectionEdge.waypoints.Add(point);
            }
        }
        else
        {
            float tangentScale = dot > 0.85f ? 1f : 1.2f;
            AddCurvedConnectorWaypoints(
                intersectionEdge,
                p0,
                travelIn,
                p3,
                travelOut,
                tangentScale,
                resolution);
        }

        incomingEnd.outgoingEdges.Add(intersectionEdge);
        RegisterEdge(intersectionEdge);
    }

    // --- UTILITIES ---

    private TrafficEdge FindRoadLaneLeavingNode(TrafficNode node)
    {
        if (node == null || node.outgoingEdges == null) return null;

        foreach (TrafficEdge edge in node.outgoingEdges)
        {
            if (edge != null && edge.kind == TrafficEdgeKind.RoadLane)
            {
                return edge;
            }
        }

        return null;
    }

    private void AddCurvedConnectorWaypoints(
        TrafficEdge edge,
        Vector3 start,
        Vector3 startDirection,
        Vector3 end,
        Vector3 endDirection,
        float tangentScale = 1f,
        int resolution = ConnectorCurveResolution)
    {
        Vector3 fallbackDirection = end - start;
        if (fallbackDirection.sqrMagnitude <= 0.0001f)
        {
            edge.waypoints.Add(start);
            edge.waypoints.Add(end);
            return;
        }

        fallbackDirection.Normalize();
        Vector3 startTangent = startDirection.sqrMagnitude > 0.0001f
            ? startDirection.normalized
            : fallbackDirection;
        Vector3 endTangent = endDirection.sqrMagnitude > 0.0001f
            ? endDirection.normalized
            : fallbackDirection;

        float connectorLength = Vector3.Distance(start, end);
        float tangentLength = Mathf.Max(0.05f, connectorLength * tangentScale);
        Vector3 startVelocity = startTangent * tangentLength;
        Vector3 endVelocity = endTangent * tangentLength;

        for (int i = 0; i <= resolution; i++)
        {
            float t = i / (float)resolution;
            float t2 = t * t;
            float t3 = t2 * t;
            float t4 = t3 * t;
            float t5 = t4 * t;

            float startPositionBasis = 1f - 10f * t3 + 15f * t4 - 6f * t5;
            float startVelocityBasis = t - 6f * t3 + 8f * t4 - 3f * t5;
            float endPositionBasis = 10f * t3 - 15f * t4 + 6f * t5;
            float endVelocityBasis = -4f * t3 + 7f * t4 - 3f * t5;

            Vector3 point = startPositionBasis * start;
            point += startVelocityBasis * startVelocity;
            point += endPositionBasis * end;
            point += endVelocityBasis * endVelocity;
            edge.waypoints.Add(point);
        }
    }

    private List<float> BuildCumulativeDistances(List<Vector3> points)
    {
        List<float> distances = new List<float>();
        if (points == null || points.Count == 0) return distances;

        float totalDistance = 0f;
        distances.Add(0f);
        for (int i = 1; i < points.Count; i++)
        {
            totalDistance += Vector3.Distance(points[i - 1], points[i]);
            distances.Add(totalDistance);
        }

        return distances;
    }

    private Vector3 GetPositionAlongPolyline(List<Vector3> points, List<float> distances, float distance)
    {
        if (points == null || points.Count == 0) return Vector3.zero;
        if (points.Count == 1 || distances == null || distances.Count != points.Count) return points[0];

        float totalLength = distances[distances.Count - 1];
        float clampedDistance = Mathf.Clamp(distance, 0f, totalLength);
        for (int i = 1; i < distances.Count; i++)
        {
            if (clampedDistance > distances[i]) continue;

            float segmentLength = distances[i] - distances[i - 1];
            float t = segmentLength > 0.0001f
                ? (clampedDistance - distances[i - 1]) / segmentLength
                : 0f;
            return Vector3.Lerp(points[i - 1], points[i], t);
        }

        return points[points.Count - 1];
    }

    private Vector3 GetDirectionAlongPolyline(List<Vector3> points, List<float> distances, float distance)
    {
        if (points == null || points.Count < 2 || distances == null || distances.Count != points.Count)
        {
            return Vector3.forward;
        }

        float totalLength = distances[distances.Count - 1];
        float sampleDistance = Mathf.Min(0.04f, totalLength * 0.5f);
        float startDistance = Mathf.Clamp(distance - sampleDistance, 0f, totalLength);
        float endDistance = Mathf.Clamp(distance + sampleDistance, 0f, totalLength);
        Vector3 direction = GetPositionAlongPolyline(points, distances, endDistance) -
                            GetPositionAlongPolyline(points, distances, startDistance);
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
    }

    private List<Vector3> BuildPolylineSection(
        List<Vector3> points,
        List<float> distances,
        float startDistance,
        float endDistance)
    {
        List<Vector3> section = new List<Vector3>
        {
            GetPositionAlongPolyline(points, distances, startDistance)
        };

        for (int i = 1; i < points.Count - 1; i++)
        {
            if (distances[i] > startDistance + 0.001f &&
                distances[i] < endDistance - 0.001f)
            {
                section.Add(points[i]);
            }
        }

        Vector3 endPoint = GetPositionAlongPolyline(points, distances, endDistance);
        if ((endPoint - section[section.Count - 1]).sqrMagnitude > 0.000001f)
        {
            section.Add(endPoint);
        }
        else if (section.Count == 1)
        {
            section.Add(endPoint);
        }

        return section;
    }

    private void RegisterEdge(TrafficEdge edge)
    {
        edge.edgeId = _nextEdgeId++;
        edge.RecalculateLength();
        _newEdges.Add(edge);
    }

    private void ConfigureLaneChangeEdge(TrafficEdge edge, int fromLaneIndex, int toLaneIndex, int totalLanes)
    {
        edge.kind = TrafficEdgeKind.LaneChange;
        edge.laneIndex = fromLaneIndex;
        edge.totalLanes = totalLanes;
        edge.fromLaneIndex = fromLaneIndex;
        edge.toLaneIndex = toLaneIndex;
    }

    private void AddTouchedCell(TrafficEdge edge, Vector2Int cell)
    {
        if (edge == null) return;
        if (!edge.touchedCells.Contains(cell)) edge.touchedCells.Add(cell);
    }

    private void AddNodeTouchedCell(TrafficNode node, Vector2Int cell)
    {
        if (node == null) return;

        if (!_nodeTouchedCells.TryGetValue(node, out List<Vector2Int> cells))
        {
            cells = new List<Vector2Int>();
            _nodeTouchedCells[node] = cells;
        }

        if (!cells.Contains(cell)) cells.Add(cell);
    }

    private void CopyTouchedCells(TrafficEdge edge, TrafficNode a, TrafficNode b)
    {
        CopyTouchedCells(edge, a);
        CopyTouchedCells(edge, b);
    }

    private void CopyTouchedCells(TrafficEdge edge, TrafficNode node)
    {
        if (edge == null || node == null) return;
        if (!_nodeTouchedCells.TryGetValue(node, out List<Vector2Int> cells)) return;

        foreach (Vector2Int cell in cells)
        {
            AddTouchedCell(edge, cell);
        }
    }

    private int BuildConflictMask(int fromDirectionBit, int toDirectionBit)
    {
        return fromDirectionBit | toDirectionBit;
    }

    private void RegisterIncomingNode(Vector2Int intersectionCell, TrafficNode node, int localLaneIndex, int totalLanes, Vector3 direction, Vector2Int neighborCell)
    {
        if (!_newIncoming.ContainsKey(intersectionCell)) _newIncoming[intersectionCell] = new List<LaneEndpoint>();
        _newIncoming[intersectionCell].Add(new LaneEndpoint { Node = node, LocalLaneIndex = localLaneIndex, TotalLanes = totalLanes, Direction = direction, NeighborCell = neighborCell });
        AddNodeTouchedCell(node, intersectionCell);
        AddNodeTouchedCell(node, neighborCell);
    }

    private void RegisterOutgoingNode(Vector2Int intersectionCell, TrafficNode node, int localLaneIndex, int totalLanes, Vector3 direction, Vector2Int neighborCell)
    {
        if (!_newOutgoing.ContainsKey(intersectionCell)) _newOutgoing[intersectionCell] = new List<LaneEndpoint>();
        _newOutgoing[intersectionCell].Add(new LaneEndpoint { Node = node, LocalLaneIndex = localLaneIndex, TotalLanes = totalLanes, Direction = direction, NeighborCell = neighborCell });
        AddNodeTouchedCell(node, intersectionCell);
        AddNodeTouchedCell(node, neighborCell);
    }

    private Color GetLaneColor(int localIndex)
    {
        switch (localIndex)
        {
            case 0: return new Color(0.2f, 0.8f, 1f); // Cyan
            case 1: return new Color(1f, 0.8f, 0.2f); // Yellow
            case 2: return new Color(1f, 0.4f, 0.2f); // Orange
            default: return Color.white;
        }
    }

    private Color GetLegColor(int index)
    {
        Color[] colors = new Color[] {
            new Color(1f, 0.2f, 0.2f), // Red
            new Color(0.2f, 1f, 0.2f), // Green
            new Color(0.2f, 0.4f, 1f), // Blue
            new Color(1f, 0.8f, 0.1f), // Yellow
            new Color(0.1f, 1f, 1f),   // Cyan
            new Color(1f, 0.2f, 1f),   // Magenta
            new Color(1f, 0.5f, 0f),   // Orange
            new Color(0.6f, 0.1f, 1f)  // Purple
        };
        return colors[index % colors.Length];
    }

    private Color GetTurnColor(TrafficTurnType type)
    {
        switch (type)
        {
            case TrafficTurnType.Straight: return Color.green;
            case TrafficTurnType.Right: return Color.magenta;
            case TrafficTurnType.Left: return new Color(1f, 0.5f, 0f);
            default: return Color.gray;
        }
    }

    // --- BURST JOB ---

    [BurstCompile]
    public struct TrafficMathJob : IJob
    {
        [ReadOnly] public NativeArray<float3> centerlines;
        [ReadOnly] public NativeArray<int> segmentOffsets;
        [ReadOnly] public NativeArray<RoadTypeData> roadTypes;
        [ReadOnly] public NativeArray<int2> startCells;
        [ReadOnly] public NativeArray<int2> endCells;
        public int smoothingIterations;

        public NativeList<EdgeData> outEdges;
        public NativeList<float3> outWaypoints;

        public void Execute()
        {
            NativeList<float3> coarsePoints = new NativeList<float3>(64, Allocator.Temp);
            NativeList<float3> smoothedPoints = new NativeList<float3>(256, Allocator.Temp);
            NativeList<float3> tempLoop = new NativeList<float3>(256, Allocator.Temp);

            for (int i = 0; i < roadTypes.Length; i++)
            {
                int startIdx = segmentOffsets[i];
                int count = segmentOffsets[i + 1] - startIdx;
                RoadTypeData rt = roadTypes[i];

                int totalLanes = rt.isTwoWay ? rt.lanesPerWay * 2 : rt.lanesPerWay;
                float laneWidth = rt.roadWidth / totalLanes;
                
                bool isClosedLoop = math.distance(centerlines[startIdx], centerlines[startIdx + count - 1]) < 0.01f;

                // --- NEW LOGIC: Smooth Centerline FIRST ---
                smoothedPoints.Clear();
                if (isClosedLoop && count > 0) 
                {
                    for (int p = 0; p < count - 1; p++) smoothedPoints.Add(centerlines[startIdx + p]);
                }
                else
                {
                    for (int p = 0; p < count; p++) smoothedPoints.Add(centerlines[startIdx + p]);
                }

                // Apply Chaikin to Centerline
                for (int iter = 0; iter < smoothingIterations; iter++) 
                {
                    if (smoothedPoints.Length < 3) break;
                    tempLoop.Clear();
                    
                    if (!isClosedLoop) tempLoop.Add(smoothedPoints[0]);
                    
                    int loopCount = smoothedPoints.Length - (isClosedLoop ? 0 : 1);
                    for (int sp = 0; sp < loopCount; sp++)
                    {
                        float3 p0 = smoothedPoints[sp];
                        float3 p1 = smoothedPoints[(sp + 1) % smoothedPoints.Length];
                        tempLoop.Add(math.lerp(p0, p1, 0.25f));
                        tempLoop.Add(math.lerp(p0, p1, 0.75f));
                    }
                    
                    if (!isClosedLoop) tempLoop.Add(smoothedPoints[smoothedPoints.Length - 1]);
                    
                    smoothedPoints.Clear();
                    smoothedPoints.AddRange(tempLoop.AsArray());
                }

                if (isClosedLoop && smoothedPoints.Length > 0) smoothedPoints.Add(smoothedPoints[0]);
                // --- END SMOOTH CENTERLINE ---

                for (int lane = 0; lane < totalLanes; lane++)
                {
                    bool isReverse = rt.isTwoWay && (lane >= rt.lanesPerWay);
                    int localIndex = rt.isTwoWay ? (lane % rt.lanesPerWay) : lane;
                    
                    float offsetDist = rt.isTwoWay 
                        ? (localIndex * laneWidth) + (laneWidth * 0.5f) 
                        : (-((totalLanes - 1) * laneWidth * 0.5f) + (lane * laneWidth));
                    
                    if (isReverse) offsetDist = -offsetDist;

                    coarsePoints.Clear();

                    int smoothedCount = smoothedPoints.Length;
                    for (int p = 0; p < smoothedCount; p++)
                    {
                        float3 forward = float3.zero;
                        float miterScale = 1.0f;
                        float3 dirIn = float3.zero, dirOut = float3.zero;

                        if (p == 0) {
                            if (isClosedLoop && smoothedCount >= 3) {
                                dirIn = math.normalize(smoothedPoints[0] - smoothedPoints[smoothedCount - 2]);
                                dirOut = math.normalize(smoothedPoints[1] - smoothedPoints[0]);
                            } else {
                                forward = smoothedCount > 1 ? math.normalize(smoothedPoints[1] - smoothedPoints[0]) : new float3(0, 0, 1);
                            }
                        } else if (p == smoothedCount - 1) {
                            if (isClosedLoop && smoothedCount >= 3) {
                                dirIn = math.normalize(smoothedPoints[smoothedCount - 1] - smoothedPoints[smoothedCount - 2]);
                                dirOut = math.normalize(smoothedPoints[1] - smoothedPoints[0]);
                            } else {
                                forward = smoothedCount > 1 ? math.normalize(smoothedPoints[p] - smoothedPoints[p - 1]) : new float3(0, 0, 1);
                            }
                        } else {
                            dirIn = math.normalize(smoothedPoints[p] - smoothedPoints[p - 1]);
                            dirOut = math.normalize(smoothedPoints[p + 1] - smoothedPoints[p]);
                        }

                        if (math.lengthsq(dirIn) > 0.5f && math.lengthsq(dirOut) > 0.5f)
                        {
                            float3 sum = dirIn + dirOut;
                            if (math.lengthsq(sum) < 0.001f) {
                                forward = math.normalize(math.cross(new float3(0, 1, 0), dirIn));
                            } else {
                                forward = math.normalize(sum);
                                miterScale = 1.0f / math.max(0.1f, math.dot(dirIn, forward));
                            }
                        }
                        
                        miterScale = math.min(miterScale, 3.0f);
                        if (math.lengthsq(forward) < 0.001f) forward = new float3(0, 0, 1);

                        float3 right = math.normalize(math.cross(new float3(0, 1, 0), forward));
                        float3 offsetPos = smoothedPoints[p] + (right * (offsetDist * miterScale));
                        coarsePoints.Add(offsetPos);
                    }

                    if (isReverse)
                    {
                        for (int rev = 0; rev < coarsePoints.Length / 2; rev++)
                        {
                            int idxA = rev;
                            int idxB = coarsePoints.Length - 1 - rev;
                            float3 tempSwap = coarsePoints[idxA];
                            coarsePoints[idxA] = coarsePoints[idxB];
                            coarsePoints[idxB] = tempSwap;
                        }
                    }

                    int waypointStartIndex = outWaypoints.Length;
                    outWaypoints.AddRange(coarsePoints.AsArray());

                    outEdges.Add(new EdgeData
                    {
                        startWaypointIndex = waypointStartIndex,
                        waypointCount = coarsePoints.Length,
                        laneIndex = localIndex,
                        totalLanes = totalLanes,
                        lanesPerWay = rt.lanesPerWay,
                        isTwoWay = rt.isTwoWay,
                        speedLimit = rt.speedLimit,
                        isMerge = false,
                        startCell = isReverse ? endCells[i] : startCells[i],
                        endCell = isReverse ? startCells[i] : endCells[i]
                    });
                }
            }
            
            coarsePoints.Dispose();
            smoothedPoints.Dispose();
            tempLoop.Dispose();
        }
    }
}// test
