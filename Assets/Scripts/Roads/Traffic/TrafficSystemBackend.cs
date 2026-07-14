using System;
using System.Collections.Generic;
using UnityEngine;

public class TrafficSystemBackend : MonoBehaviour
{
    public static TrafficSystemBackend Instance { get; private set; }

    [Header("Active Network Data")]
    public List<TrafficNode> allNodes = new List<TrafficNode>();
    public List<TrafficEdge> allEdges = new List<TrafficEdge>();

    private Dictionary<Vector2Int, List<LaneEndpoint>> _intersectionIncoming = new Dictionary<Vector2Int, List<LaneEndpoint>>();
    private Dictionary<Vector2Int, List<LaneEndpoint>> _intersectionOutgoing = new Dictionary<Vector2Int, List<LaneEndpoint>>();

    [Header("Debug Visualization")]
    public bool showWaypoints = true;
    public bool showLogicalConnections = false;
    public float intersectionNodesPullback = 0.45f;
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
        // 1. Gather the payloads
        List<RoadSegmentPayload> segmentsToProcess = ExtractRoadSegments();

        // 2. Queue the heavy generation task
        TrafficGenerationTask task = new TrafficGenerationTask(segmentsToProcess);
        SimulationTaskManager.Instance.EnqueueTask(task);
    }

    private List<RoadSegmentPayload> ExtractRoadSegments()
    {
        List<RoadSegmentPayload> payloads = new List<RoadSegmentPayload>();
        var roads = RoadSystemBackend.Instance.Roads;
        HashSet<string> processedSegments = new HashSet<string>();
        
        // NEW: Track physical cells that belong to closed loops to prevent O(N^2) explosion
        HashSet<Vector2Int> visitedLoopCells = new HashSet<Vector2Int>();

        // PASS 1: Trace all paths starting from "Real Nodes" (Intersections & Dead Ends/Ports)
        foreach (var kvp in roads)
        {
            Vector2Int currentCell = kvp.Key;
            RoadCell cellData = kvp.Value;

            if (IsRealNode(cellData))
            {
                for (int i = 0; i < 8; i++)
                {
                    int bit = 1 << i;
                    if (cellData.HasConnection(bit) && (cellData.outConnections & bit) != 0)
                    {
                        Vector2Int neighborCell = RoadSystemBackend.Instance.GetNeighborPosition(currentCell, bit);

                        RoadType trueRoadType = GetTrueSegmentRoadType(currentCell, neighborCell, cellData);

                        List<Vector3> fullPath = TraceTrafficPath(currentCell, neighborCell, out Vector2Int endCell);

                        string segmentHash = trueRoadType.isTwoWay 
                        ? GetTwoWayHash(currentCell, endCell) 
                        : $"{currentCell}->{endCell}";

                    if (!processedSegments.Contains(segmentHash))
                    {
                        processedSegments.Add(segmentHash);
                        payloads.Add(new RoadSegmentPayload
                        {
                            centerline = fullPath, // Passed raw! No Chaikin here.
                            roadType = trueRoadType, 
                            startCell = currentCell,
                            endCell = endCell
                        });
                    }
                    }
                }
            }
        }

        // PASS 2: Fallback for isolated closed loops (like 2x2 roundabouts)
        foreach (var kvp in roads)
        {
            Vector2Int currentCell = kvp.Key;
            RoadCell cellData = kvp.Value;

            // FIX: If we already mapped this loop from a different starting cell, skip it instantly!
            if (!IsRealNode(cellData) && !visitedLoopCells.Contains(currentCell))
            {
                for (int i = 0; i < 8; i++)
                {
                    int bit = 1 << i;
                    if (cellData.HasConnection(bit) && (cellData.outConnections & bit) != 0)
                    {
                        Vector2Int neighborCell = RoadSystemBackend.Instance.GetNeighborPosition(currentCell, bit);
                        
                        // Trace the loop and capture exactly which cells belong to it
                        List<Vector2Int> cellsInLoop = new List<Vector2Int>();
                        List<Vector3> fullPath = TraceTrafficPathWithCells(currentCell, neighborCell, out Vector2Int endCell, cellsInLoop);
                        
                        // Mark all cells in this circle as visited so we never process them again
                        foreach (Vector2Int loopCell in cellsInLoop) visitedLoopCells.Add(loopCell);

                        string segmentHash = cellData.roadType.isTwoWay 
                            ? GetTwoWayHash(currentCell, endCell) 
                            : $"{currentCell}->{endCell}";

                        if (!processedSegments.Contains(segmentHash))
                        {
                            processedSegments.Add(segmentHash);
                            payloads.Add(new RoadSegmentPayload
                            {
                                centerline = fullPath, // Passed raw! No Chaikin here.
                                roadType = cellData.roadType,
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

    

    // This method swaps out the old network for the newly generated one in a single frame.
    public void ApplyNewNetwork(
        List<TrafficNode> newNodes, 
        List<TrafficEdge> newEdges, 
        Dictionary<Vector2Int, List<LaneEndpoint>> newIncoming, 
        Dictionary<Vector2Int, List<LaneEndpoint>> newOutgoing)
    {
        allNodes = newNodes;
        allEdges = newEdges;
        _intersectionIncoming = newIncoming;
        _intersectionOutgoing = newOutgoing;

        if (NativeTrafficGraph.Instance != null)
        {
            NativeTrafficGraph.Instance.RebuildGraph(allNodes, allEdges);
        }

        OnTrafficNetworkReady?.Invoke();
    }

    // --- RUNTIME HELPERS ---

    private List<Vector3> TraceTrafficPathWithCells(Vector2Int startCell, Vector2Int firstNeighbor, out Vector2Int endCell, List<Vector2Int> visitedCells)
    {
        WorldManager wm = WorldManager.Instance;
        float cellSize = wm.cellSize;
        var roads = RoadSystemBackend.Instance.Roads;

        List<Vector3> rawWaypoints = new List<Vector3>();

        Vector3 GetWorldPos(Vector2Int gridPos) {
            float y = wm.GetPhysicalHeight(gridPos.x + 0.5f, gridPos.y + 0.5f) * wm.heightStep;
            return new Vector3(gridPos.x * cellSize + (cellSize / 2), y, gridPos.y * cellSize + (cellSize / 2));
        }

        Vector3 startPos = GetWorldPos(startCell);
        Vector3 firstPos = GetWorldPos(firstNeighbor);

        RoadCell startCellData = RoadSystemBackend.Instance.Roads[startCell];
        
        // NEW: Pull back the start node for BOTH Intersections and Transitions
        if (IsRealIntersection(startCellData) || IsTransitionNode(startCellData))
            rawWaypoints.Add(Vector3.Lerp(startPos, firstPos, intersectionNodesPullback));
        else
            rawWaypoints.Add(startPos);
            
        visitedCells.Add(startCell);

        Vector2Int prev = startCell;
        Vector2Int curr = firstNeighbor;
        endCell = firstNeighbor; 

        while (roads.TryGetValue(curr, out RoadCell currCell))
        {
            visitedCells.Add(curr);
            endCell = curr;

            bool isIntersection = IsRealIntersection(currCell);
            bool isTransition = IsTransitionNode(currCell);

            if (isIntersection || isTransition || curr == startCell) 
            {
                // NEW: Apply the pullback gap to BOTH Intersections and Transitions
                if (isIntersection || isTransition)
                {
                    Vector3 endPos = GetWorldPos(curr);
                    Vector3 prevPos = GetWorldPos(prev);
                    rawWaypoints.Add(Vector3.Lerp(endPos, prevPos, intersectionNodesPullback));
                }
                else
                {
                    rawWaypoints.Add(GetWorldPos(curr)); 
                }
                break;
            }
            else
            {
                rawWaypoints.Add(GetWorldPos(curr));
            }

            Vector2Int nextPos = curr;
            for (int i = 0; i < 8; i++)
            {
                int bit = 1 << i;
                if (currCell.HasConnection(bit)) 
                {
                    Vector2Int nPos = RoadSystemBackend.Instance.GetNeighborPosition(curr, bit);
                    if (nPos != prev) { nextPos = nPos; break; }
                }
            }

            if (nextPos == curr) break; 

            prev = curr;
            curr = nextPos;
        }

        return rawWaypoints;
    }
    
    private bool IsRealIntersection(RoadCell cell)
    {
        // 1-way (dead end) or 3+ way connections are true intersections
        return RoadSystemBackend.Instance.GetConnectionCount(cell) != 2;
    }

    private bool IsTransitionNode(RoadCell cell)
    {
        // Transitions are strictly mid-block (exactly 2 connections)
        if (RoadSystemBackend.Instance.GetConnectionCount(cell) != 2) return false;
        
        for (int i = 0; i < 8; i++)
        {
            int bit = 1 << i;
            if (cell.HasConnection(bit))
            {
                Vector2Int nPos = RoadSystemBackend.Instance.GetNeighborPosition(cell.gridPosition, bit);
                if (RoadSystemBackend.Instance.Roads.TryGetValue(nPos, out RoadCell neighbor))
                {
                    // Ignore intersections! Let the road run right up into the junction.
                    if (RoadSystemBackend.Instance.GetConnectionCount(neighbor) != 2) 
                        continue; 

                    if (neighbor.roadType != cell.roadType)
                    {
                        // CRITICAL FIX: The Deterministic Tie-Breaker
                        // Prevents BOTH sides of the border from becoming nodes, eliminating the 1-tile ghost segment.
                        // Only the cell with the strictly 'lesser' grid position becomes the single transition node.
                        if (cell.gridPosition.x < neighbor.gridPosition.x || 
                        (cell.gridPosition.x == neighbor.gridPosition.x && cell.gridPosition.y < neighbor.gridPosition.y))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }

    private bool IsRealNode(RoadCell cell)
    {
        // Keeps your ExtractRoadSegments loop functioning without modification
        return IsRealIntersection(cell) || IsTransitionNode(cell);
    }

    private List<Vector3> TraceTrafficPath(Vector2Int startCell, Vector2Int firstNeighbor, out Vector2Int endCell)
    {
        WorldManager wm = WorldManager.Instance;
        float cellSize = wm.cellSize;
        var roads = RoadSystemBackend.Instance.Roads;

        List<Vector3> rawWaypoints = new List<Vector3>();

        Vector3 GetWorldPos(Vector2Int gridPos) {
            float y = wm.GetPhysicalHeight(gridPos.x + 0.5f, gridPos.y + 0.5f) * wm.heightStep;
            return new Vector3(gridPos.x * cellSize + (cellSize / 2), y, gridPos.y * cellSize + (cellSize / 2));
        }

        Vector3 startPos = GetWorldPos(startCell);
        Vector3 firstPos = GetWorldPos(firstNeighbor);

        RoadCell startCellData = RoadSystemBackend.Instance.Roads[startCell];
        
        // NEW: Pull back the start node for BOTH Intersections and Transitions
        if (IsRealIntersection(startCellData) || IsTransitionNode(startCellData))
            rawWaypoints.Add(Vector3.Lerp(startPos, firstPos, intersectionNodesPullback));
        else
            rawWaypoints.Add(startPos);

        Vector2Int prev = startCell;
        Vector2Int curr = firstNeighbor;
        endCell = firstNeighbor; 

        while (roads.TryGetValue(curr, out RoadCell currCell))
        {
            endCell = curr;

            bool isIntersection = IsRealIntersection(currCell);
            bool isTransition = IsTransitionNode(currCell);

            if (isIntersection || isTransition || curr == startCell) 
            {
                // NEW: Apply the pullback gap to BOTH Intersections and Transitions
                if (isIntersection || isTransition)
                {
                    Vector3 endPos = GetWorldPos(curr);
                    Vector3 prevPos = GetWorldPos(prev);
                    rawWaypoints.Add(Vector3.Lerp(endPos, prevPos, intersectionNodesPullback));
                }
                else
                {
                    rawWaypoints.Add(GetWorldPos(curr)); 
                }
                break;
            }
            else
            {
                rawWaypoints.Add(GetWorldPos(curr));
            }

            Vector2Int nextPos = curr;
            for (int i = 0; i < 8; i++)
            {
                int bit = 1 << i;
                if (currCell.HasConnection(bit)) 
                {
                    Vector2Int nPos = RoadSystemBackend.Instance.GetNeighborPosition(curr, bit);
                    if (nPos != prev) { nextPos = nPos; break; }
                }
            }

            if (nextPos == curr) break; 

            prev = curr;
            curr = nextPos;
        }

        return rawWaypoints;
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

    private RoadType GetTrueSegmentRoadType(Vector2Int startCellPos, Vector2Int neighborCellPos, RoadCell startCell)
    {
        // If the neighbor is a pure straight road or a dead end, it holds the true physical road type
        if (RoadSystemBackend.Instance.Roads.TryGetValue(neighborCellPos, out RoadCell nCell))
        {
            if (RoadSystemBackend.Instance.GetConnectionCount(nCell) <= 2)
                return nCell.roadType;
        }
        
        // If the neighbor was an intersection, fallback to the start cell if it's a pure segment
        if (RoadSystemBackend.Instance.GetConnectionCount(startCell) <= 2)
            return startCell.roadType;

        // Failsafe if we are generating a tiny 1-tile link strictly between two massive intersections
        return startCell.roadType;
    }

    public TrafficEdge GetClosestLane(Vector3 worldPoint, float searchRadius)
    {
        TrafficEdge closestEdge = null;
        float minDistance = searchRadius;

        foreach (TrafficEdge edge in allEdges)
        {
            if (edge.isIntersection || edge.isMergeEdge) continue;

            for (int i = 0; i < edge.waypoints.Count - 1; i++)
            {
                float dist = DistanceToLineSegment(worldPoint, edge.waypoints[i], edge.waypoints[i + 1]);
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
        List<KeyValuePair<TrafficEdge, float>> candidates = new List<KeyValuePair<TrafficEdge, float>>();

        foreach (TrafficEdge edge in allEdges)
        {
            if (edge.isIntersection) continue; 

            float minDistanceForEdge = float.MaxValue;
            for (int i = 0; i < edge.waypoints.Count - 1; i++)
            {
                float dist = DistanceToLineSegment(worldPoint, edge.waypoints[i], edge.waypoints[i + 1]);
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

    private float DistanceToLineSegment(Vector3 point, Vector3 lineStart, Vector3 lineEnd)
    {
        Vector3 lineDir = lineEnd - lineStart;
        float lineLength = lineDir.magnitude;
        lineDir.Normalize();

        Vector3 pointVector = point - lineStart;
        float projectLength = Vector3.Dot(pointVector, lineDir);

        if (projectLength < 0f) return Vector3.Distance(point, lineStart);
        if (projectLength > lineLength) return Vector3.Distance(point, lineEnd);

        Vector3 projection = lineStart + lineDir * projectLength;
        return Vector3.Distance(point, projection);
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