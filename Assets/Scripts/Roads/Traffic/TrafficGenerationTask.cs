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
    public RoadType roadType;
    public Vector2Int startCell;
    public Vector2Int endCell;
}

public class TrafficGenerationTask : ISimulationTask
{
    private enum TaskState { Init, ScheduleJob, WaitForJob, BuildGraph, RouteIntersections, Complete }
    private TaskState _state = TaskState.Init;

    private List<RoadSegmentPayload> _segmentsToProcess;
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

    // Iterators for Time-Slicing
    private int _edgeIndex = 0;
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
        public float Angle; // Stores the clockwise angle relative to the junction center
    }

    private enum TurnType { UTurn, Left, Straight, Right }

    private int _smoothingIterations;

    public TrafficGenerationTask(List<RoadSegmentPayload> segments, int smoothingIterations = 3)
    {
        _segmentsToProcess = segments;
        _smoothingIterations = smoothingIterations;
    }

    public bool Process(Stopwatch timer, float maxMillisecondsPerFrame)
    {
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
                    lanesPerWay = _segmentsToProcess[i].roadType.lanesPerWay,
                    isTwoWay = _segmentsToProcess[i].roadType.isTwoWay,
                    roadWidth = _segmentsToProcess[i].roadType.roadWidth,
                    speedLimit = _segmentsToProcess[i].roadType.speedLimit
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

            _intersectionEnumerator = _newIncoming.GetEnumerator();
            _state = TaskState.RouteIntersections;
        }

        if (_state == TaskState.RouteIntersections)
        {
            while (_intersectionEnumerator.MoveNext())
            {
                var kvp = _intersectionEnumerator.Current;
                ProcessIntersection(kvp.Key, kvp.Value);

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

            TrafficSystemBackend.Instance.ApplyNewNetwork(_newNodes, _newEdges, _newIncoming, _newOutgoing);
            return true;
        }

        return false;
    }

    // --- STRAIGHTAWAY GENERATOR ---

    private void BuildStraightLanes(List<List<Vector3>> allLaneWaypoints, List<EdgeData> segmentEdges, int startIdx, int endIdx)
    {
        int numLanes = endIdx - startIdx;
        if (numLanes <= 0) return;

        TrafficNode[] startNodes = new TrafficNode[numLanes];
        TrafficNode[] endNodes = new TrafficNode[numLanes]; // NEW: Cache end nodes

        for (int i = 0; i < numLanes; i++)
        {
            int globalIdx = startIdx + i;
            EdgeData ed = segmentEdges[globalIdx];
            List<Vector3> pts = allLaneWaypoints[globalIdx];

            TrafficNode startNode = new TrafficNode(pts[0]);
            TrafficNode endNode = new TrafficNode(pts[pts.Count - 1]);
            
            startNodes[i] = startNode;
            endNodes[i] = endNode; // Store the end node
            
            _newNodes.Add(startNode);
            _newNodes.Add(endNode);

            Vector3 startDir = pts.Count > 1 ? (pts[1] - pts[0]).normalized : Vector3.forward; 
            Vector3 endDir = pts.Count > 1 ? (pts[pts.Count - 2] - pts[pts.Count - 1]).normalized : Vector3.back;

            RegisterOutgoingNode(new Vector2Int(ed.startCell.x, ed.startCell.y), startNode, ed.laneIndex, numLanes, startDir, new Vector2Int(ed.endCell.x, ed.endCell.y));
            RegisterIncomingNode(new Vector2Int(ed.endCell.x, ed.endCell.y), endNode, ed.laneIndex, numLanes, endDir, new Vector2Int(ed.startCell.x, ed.startCell.y));

            TrafficEdge straight = new TrafficEdge(startNode, endNode, ed.speedLimit);
            straight.waypoints = new List<Vector3>(pts);
            straight.edgeColor = GetLaneColor(i);
            straight.laneIndex = ed.laneIndex;
            straight.totalLanes = numLanes;
            
            startNode.outgoingEdges.Add(straight);
            _newEdges.Add(straight);
        }

        // --- VIRTUAL LATERAL EDGES ---
        for (int i = 0; i < numLanes - 1; i++)
        {
            TrafficNode sA = startNodes[i];
            TrafficNode sB = startNodes[i + 1];
            TrafficNode eA = endNodes[i];       // NEW
            TrafficNode eB = endNodes[i + 1];   // NEW

            // 1. Merges at the START of the road segment
            TrafficEdge mergeAB = new TrafficEdge(sA, sB, 20f) { isMergeEdge = true };
            mergeAB.waypoints.Add(sA.position); mergeAB.waypoints.Add(sB.position);
            sA.outgoingEdges.Add(mergeAB); _newEdges.Add(mergeAB);

            TrafficEdge mergeBA = new TrafficEdge(sB, sA, 20f) { isMergeEdge = true };
            mergeBA.waypoints.Add(sB.position); mergeBA.waypoints.Add(sA.position);
            sB.outgoingEdges.Add(mergeBA); _newEdges.Add(mergeBA);

            // 2. Merges at the END of the road segment (Prevents getting trapped before turning)
            TrafficEdge endMergeAB = new TrafficEdge(eA, eB, 20f) { isMergeEdge = true };
            endMergeAB.waypoints.Add(eA.position); endMergeAB.waypoints.Add(eB.position);
            eA.outgoingEdges.Add(endMergeAB); _newEdges.Add(endMergeAB);

            TrafficEdge endMergeBA = new TrafficEdge(eB, eA, 20f) { isMergeEdge = true };
            endMergeBA.waypoints.Add(eB.position); endMergeBA.waypoints.Add(eA.position);
            eB.outgoingEdges.Add(endMergeBA); _newEdges.Add(endMergeBA);
        }
    }

    // --- INTERSECTION ROUTING LOGIC ---

    private void ProcessIntersection(Vector2Int intersectionCell, List<LaneEndpoint> incoming)
    {
        if (!_newOutgoing.ContainsKey(intersectionCell)) return;
        List<LaneEndpoint> outgoing = _newOutgoing[intersectionCell];

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

        // 2. Map turns using Signed Angle (Eliminates the T-Junction skip bug)
        foreach (RoadLeg inLeg in legs)
        {
            if (inLeg.IncomingLanes.Count == 0) continue;

            List<KeyValuePair<RoadLeg, TurnType>> targetLegs = new List<KeyValuePair<RoadLeg, TurnType>>();

            if (legs.Count == 1)
            {
                targetLegs.Add(new KeyValuePair<RoadLeg, TurnType>(inLeg, TurnType.UTurn));
            }
            else
            {
                foreach (RoadLeg outLeg in legs)
                {
                    if (inLeg == outLeg) continue; // Skip U-turns unless it's a dead end

                    // Travel direction INTO intersection vs Outward direction of target leg
                    Vector3 travelIn = -inLeg.OutwardDirection;
                    Vector3 outwardOut = outLeg.OutwardDirection;

                    // Clockwise angle: Right is positive (+), Left is negative (-)
                    float signedAngle = Vector3.SignedAngle(travelIn, outwardOut, Vector3.up);

                    TurnType turnType;
                    if (signedAngle > 35f && signedAngle < 145f) turnType = TurnType.Right;
                    else if (signedAngle < -35f && signedAngle > -145f) turnType = TurnType.Left;
                    else turnType = TurnType.Straight;

                    targetLegs.Add(new KeyValuePair<RoadLeg, TurnType>(outLeg, turnType));
                }
            }

            // Route the lanes to the designated target legs
            foreach (var target in targetLegs)
            {
                MapLanes(inLeg, target.Key, target.Value);
            }
        }
    }

    private void MapLanes(RoadLeg inLeg, RoadLeg outLeg, TurnType turnType)
    {
        if (turnType == TurnType.UTurn)
        {
            GenerateUTurns(inLeg);
            return;
        }

        List<LaneEndpoint> inLanes = inLeg.IncomingLanes;
        List<LaneEndpoint> outLanes = outLeg.OutgoingLanes;

        int inCount = inLanes.Count;
        int outCount = outLanes.Count;
        if (inCount == 0 || outCount == 0) return;

        int connectCount = Mathf.Min(inCount, outCount);
        float turnSpeed = (turnType == TurnType.Straight) ? 40f : 25f;
        Color turnColor = GetTurnColor(turnType);

        if (turnType == TurnType.Left)
        {
            // Left Turns: Map innermost lanes to innermost lanes (Index 0)
            for (int i = 0; i < connectCount; i++)
            {
                ConnectIntersection(inLanes[i], outLanes[i], turnSpeed, turnColor);
            }
        }
        else if (turnType == TurnType.Right)
        {
            // Right Turns: Map outermost lanes to outermost lanes
            int inStart = inCount - connectCount;
            int outStart = outCount - connectCount;
            for (int i = 0; i < connectCount; i++)
            {
                ConnectIntersection(inLanes[inStart + i], outLanes[outStart + i], turnSpeed, turnColor);
            }
        }
        else // Straight
        {
            // Straights: Center the lanes
            int inOffset = (inCount - connectCount) / 2;
            int outOffset = (outCount - connectCount) / 2;
            for (int i = 0; i < connectCount; i++)
            {
                ConnectIntersection(inLanes[inOffset + i], outLanes[outOffset + i], turnSpeed, turnColor);
            }
        }
    }

    private void GenerateUTurns(RoadLeg deadEndLeg)
    {
        int lanes = Mathf.Min(deadEndLeg.IncomingLanes.Count, deadEndLeg.OutgoingLanes.Count);
        for (int i = 0; i < lanes; i++)
        {
            ConnectIntersection(deadEndLeg.IncomingLanes[i], deadEndLeg.OutgoingLanes[i], 15f, Color.gray);
        }
    }

    private void ConnectIntersection(LaneEndpoint incoming, LaneEndpoint outgoing, float speedLimit, Color color, int resolution = 10)
    {
        TrafficNode incomingEnd = incoming.Node;
        TrafficNode outgoingStart = outgoing.Node;

        TrafficEdge intersectionEdge = new TrafficEdge(incomingEnd, outgoingStart, speedLimit, true);
        intersectionEdge.edgeColor = color;
        
        Vector3 p0 = incomingEnd.position;
        Vector3 p3 = outgoingStart.position;

        float distance = Vector3.Distance(p0, p3);

        if (distance < 0.1f)
        {
            intersectionEdge.waypoints.Add(p0);
            intersectionEdge.waypoints.Add(p3);
            incomingEnd.outgoingEdges.Add(intersectionEdge);
            _newEdges.Add(intersectionEdge);
            return;
        }

        Vector3 outwardIn = incoming.Direction;  
        Vector3 outwardOut = outgoing.Direction; 
        
        Vector3 travelIn = -outwardIn;   
        Vector3 travelOut = outwardOut;  
        
        Vector3 flatInDir = new Vector3(travelIn.x, 0, travelIn.z).normalized;
        Vector3 flatOutDir = new Vector3(travelOut.x, 0, travelOut.z).normalized;
        
        // FIX: Calculate the strict topological angle to detect U-Turns, 
        // ignoring the physical distance squashing of the Bezier curve
        float dot = Vector3.Dot(flatInDir, flatOutDir);
        float signedAngle = Vector3.SignedAngle(flatInDir, flatOutDir, Vector3.up);

        float controlScale;
        
        // A pure U-Turn is mechanically close to 180 degrees (or -180)
        if (Mathf.Abs(signedAngle) > 165f || dot < -0.95f) 
        {
            intersectionEdge.isUTurn = true;
            controlScale = Mathf.Max(distance * 1.5f, 0.4f);
        }
        else if (dot > 0.85f) // Straight
        {
            controlScale = distance * 0.4f;
        }
        else // Sharp/Standard Turn
        {
            float det = (flatInDir.x * flatOutDir.z) - (flatInDir.z * flatOutDir.x);
            if (Mathf.Abs(det) > 0.15f) 
            {
                float dx = p3.x - p0.x;
                float dz = p3.z - p0.z;
                float a = (dx * flatOutDir.z - dz * flatOutDir.x) / det;
                controlScale = Mathf.Clamp(a * 0.6f, distance * 0.2f, distance * 0.8f);
            }
            else
            {
                controlScale = distance * 0.45f;
            }
        }

        Vector3 p1 = p0 + (travelIn * controlScale);
        Vector3 p2 = p3 - (travelOut * controlScale); 

        for (int i = 0; i <= resolution; i++)
        {
            float t = i / (float)resolution;
            float u = 1 - t;
            float tt = t * t;
            Vector3 p = (u * u * u) * p0;
            p += 3 * (u * u) * t * p1;
            p += 3 * u * tt * p2;
            p += (tt * t) * p3;
            intersectionEdge.waypoints.Add(p);
        }

        incomingEnd.outgoingEdges.Add(intersectionEdge);
        _newEdges.Add(intersectionEdge);
    }

    // --- UTILITIES ---

    private void RegisterIncomingNode(Vector2Int intersectionCell, TrafficNode node, int localLaneIndex, int totalLanes, Vector3 direction, Vector2Int neighborCell)
    {
        if (!_newIncoming.ContainsKey(intersectionCell)) _newIncoming[intersectionCell] = new List<LaneEndpoint>();
        _newIncoming[intersectionCell].Add(new LaneEndpoint { Node = node, LocalLaneIndex = localLaneIndex, TotalLanes = totalLanes, Direction = direction, NeighborCell = neighborCell });
    }

    private void RegisterOutgoingNode(Vector2Int intersectionCell, TrafficNode node, int localLaneIndex, int totalLanes, Vector3 direction, Vector2Int neighborCell)
    {
        if (!_newOutgoing.ContainsKey(intersectionCell)) _newOutgoing[intersectionCell] = new List<LaneEndpoint>();
        _newOutgoing[intersectionCell].Add(new LaneEndpoint { Node = node, LocalLaneIndex = localLaneIndex, TotalLanes = totalLanes, Direction = direction, NeighborCell = neighborCell });
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

    private Color GetTurnColor(TurnType type)
    {
        switch (type)
        {
            case TurnType.Straight: return Color.green;
            case TurnType.Right: return Color.magenta;
            case TurnType.Left: return new Color(1f, 0.5f, 0f);
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
}