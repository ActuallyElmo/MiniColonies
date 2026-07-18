using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile(CompileSynchronously = true)]
public struct VehiclePathfindingJob : IJob
{
    [ReadOnly] public NativeArray<NativeTrafficNode> Nodes;
    [ReadOnly] public NativeArray<NativeTrafficEdge> Edges;
    [ReadOnly] public NativeArray<float3> Waypoints;
    [ReadOnly] public NativeArray<int> NodeOutConnections;

    [ReadOnly] public NativeArray<int> StartEdgeIndices;
    [ReadOnly] public NativeArray<int> EndEdgeIndices;
    [ReadOnly] public NativeArray<float> CongestionPenalties;
    public int VehiclePermissions;
    public int VehicleCapabilities;
    
    public float3 StartPos;
    public float3 TargetPos;

    // The result output
    public NativeList<float3> ResultWaypoints;
    public NativeList<int> ResultEdgeIndices;

    private struct NodeRecord
    {
        public int previousNodeIndex;
        public int edgeUsedIndex;
        public int initialStartEdgeIndex;
        public float costSoFar;
        public float estimatedTotalCost;
        public bool isClosed;
    }

    public void Execute()
    {
        if (StartEdgeIndices.Length == 0 || EndEdgeIndices.Length == 0) return;

        // --- ALLOCATE ZERO-GC TEMP BUFFERS ---
        NativeHashMap<int, NodeRecord> records = new NativeHashMap<int, NodeRecord>(Nodes.Length, Allocator.Temp);
        NativeList<int> openList = new NativeList<int>(Allocator.Temp);

        // Init Start Nodes
        for (int i = 0; i < StartEdgeIndices.Length; i++)
        {
            int edgeIdx = StartEdgeIndices[i];
            NativeTrafficEdge startEdge = Edges[edgeIdx];
            if (!IsLegal(startEdge)) continue;
            
            // Check direct match
            for (int e = 0; e < EndEdgeIndices.Length; e++)
            {
                if (edgeIdx == EndEdgeIndices[e] && CanTravelDirectlyOnEdge(startEdge))
                {
                    ExtractWaypoints(edgeIdx, out records);
                    DisposeTemp(records, openList);
                    return;
                }
            }

            int startNodeIdx = startEdge.endNodeIndex;
            float initialCost = GetCostFromPointToEdgeEnd(startEdge, StartPos);

            if (!records.ContainsKey(startNodeIdx))
            {
                records.Add(startNodeIdx, new NodeRecord
                {
                    previousNodeIndex = -1,
                    edgeUsedIndex = edgeIdx,
                    initialStartEdgeIndex = edgeIdx,
                    costSoFar = initialCost, 
                    estimatedTotalCost = initialCost + math.distance(Nodes[startNodeIdx].position, TargetPos),
                    isClosed = false
                });
                openList.Add(startNodeIdx);
            }
        }

        int iterations = 0;

        // Main A* Loop
        while (openList.Length > 0 && iterations < 15000)
        {
            iterations++;

            // Find lowest cost
            int bestIndex = 0;
            float lowestCost = float.MaxValue;
            for (int i = 0; i < openList.Length; i++)
            {
                float cost = records[openList[i]].estimatedTotalCost;
                if (cost < lowestCost)
                {
                    lowestCost = cost;
                    bestIndex = i;
                }
            }

            int currentIdx = openList[bestIndex];
            openList.RemoveAtSwapBack(bestIndex);

            NodeRecord currentRecord = records[currentIdx];
            currentRecord.isClosed = true;
            records[currentIdx] = currentRecord;

            // Check if we hit an end edge
            for (int i = 0; i < EndEdgeIndices.Length; i++)
            {
                if (currentRecord.edgeUsedIndex == EndEdgeIndices[i])
                {
                    ReconstructPath(currentIdx, records);
                    DisposeTemp(records, openList);
                    return; // Success
                }
            }

            NativeTrafficNode currentNode = Nodes[currentIdx];

            // Expand neighbors
            for (int i = 0; i < currentNode.edgeCount; i++)
            {
                int outEdgeIdx = NodeOutConnections[currentNode.edgeStartIndex + i];
                NativeTrafficEdge edge = Edges[outEdgeIdx];
                if (!IsLegal(edge)) continue;
                int neighborIdx = edge.endNodeIndex;

                float totalEdgeCost = edge.cost;
                if (outEdgeIdx < CongestionPenalties.Length)
                {
                    totalEdgeCost += CongestionPenalties[outEdgeIdx];
                }

                // Apply standard lateral merge penalty
                if (edge.isMergeEdge)
                {
                    totalEdgeCost += edge.laneChangePenalty;
                }

                if (edge.isRoadTypeTransition)
                {
                    totalEdgeCost += 1.0f + (edge.transitionPriority * 0.25f);
                }

                // FIX: Only apply the massive U-Turn penalty if it's a true U-Turn.
                if (edge.isUTurn)
                {
                    totalEdgeCost += edge.uTurnPenalty;
                }
                float newCostSoFar = currentRecord.costSoFar + totalEdgeCost;

                if (records.TryGetValue(neighborIdx, out NodeRecord neighborRecord))
                {
                    if (neighborRecord.isClosed || newCostSoFar >= neighborRecord.costSoFar) continue;
                }
                else
                {
                    neighborRecord = new NodeRecord { isClosed = false };
                }

                neighborRecord.costSoFar = newCostSoFar;
                neighborRecord.edgeUsedIndex = outEdgeIdx;
                neighborRecord.previousNodeIndex = currentIdx;
                neighborRecord.initialStartEdgeIndex = currentRecord.initialStartEdgeIndex;
                neighborRecord.estimatedTotalCost = newCostSoFar + math.distance(Nodes[neighborIdx].position, TargetPos);

                if (!records.ContainsKey(neighborIdx)) openList.Add(neighborIdx);
                records[neighborIdx] = neighborRecord;
            }
        }

        DisposeTemp(records, openList);
    }

    private void ExtractWaypoints(int singleEdgeIdx, out NativeHashMap<int, NodeRecord> unused)
    {
        // Simple direct edge path (trimmed to targets)
        NativeList<int> pathEdges = new NativeList<int>(1, Allocator.Temp);
        pathEdges.Add(singleEdgeIdx);
        BuildTrimmedResult(pathEdges);
        pathEdges.Dispose();
        unused = default; 
    }

    private void ReconstructPath(int targetNodeIdx, NativeHashMap<int, NodeRecord> records)
    {
        NativeList<int> pathEdges = new NativeList<int>(Allocator.Temp);
        int currentIdx = targetNodeIdx;

        while (records.ContainsKey(currentIdx))
        {
            NodeRecord rec = records[currentIdx];
            pathEdges.Add(rec.edgeUsedIndex);
            
            if (rec.edgeUsedIndex == rec.initialStartEdgeIndex) break;
            currentIdx = rec.previousNodeIndex;
        }

        // Reverse the edge list
        for (int i = 0; i < pathEdges.Length / 2; i++)
        {
            int tmp = pathEdges[i];
            pathEdges[i] = pathEdges[pathEdges.Length - 1 - i];
            pathEdges[pathEdges.Length - 1 - i] = tmp;
        }

        BuildTrimmedResult(pathEdges);
        pathEdges.Dispose();
    }

    private void BuildTrimmedResult(NativeList<int> pathEdges)
    {
        NativeList<float3> rawPoints = new NativeList<float3>(Allocator.Temp);

        for (int i = 0; i < pathEdges.Length; i++)
        {
            ResultEdgeIndices.Add(pathEdges[i]);
        }
        
        bool isFirstValidEdge = true;

        for (int i = 0; i < pathEdges.Length; i++)
        {
            NativeTrafficEdge edge = Edges[pathEdges[i]];
            
            int startIndex = 0;
            int endIndex = edge.waypointCount - 1;

            if (isFirstValidEdge)
            {
                float minDist = float.MaxValue;
                for (int j = 0; j < edge.waypointCount; j++)
                {
                    float d = math.distance(StartPos, Waypoints[edge.waypointStartIndex + j]);
                    if (d < minDist) { minDist = d; startIndex = j; }
                }
                isFirstValidEdge = false;
            }

            if (i == pathEdges.Length - 1)
            {
                float minDist = float.MaxValue;
                for (int j = 0; j < edge.waypointCount; j++)
                {
                    float d = math.distance(TargetPos, Waypoints[edge.waypointStartIndex + j]);
                    if (d < minDist) { minDist = d; endIndex = j; }
                }
            }

            // Failsafe for short 1-edge paths
            if (i == 0 && pathEdges.Length == 1 && startIndex > endIndex) return;

            // Append the actual physical waypoints of the new lane
            for (int j = startIndex; j <= endIndex; j++)
            {
                rawPoints.Add(Waypoints[edge.waypointStartIndex + j]);
            }
        }

        // Filter out identical sequential points to keep the path queue clean
        float3 lastAdded = new float3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        for (int i = 0; i < rawPoints.Length; i++)
        {
            if (math.distance(lastAdded, rawPoints[i]) > 0.05f)
            {
                ResultWaypoints.Add(rawPoints[i]);
                lastAdded = rawPoints[i];
            }
        }

        ResultWaypoints.Add(TargetPos);
        rawPoints.Dispose();
    }

    private bool CanTravelDirectlyOnEdge(NativeTrafficEdge edge)
    {
        float startDistance = GetClosestDistanceAlongEdge(edge, StartPos);
        float targetDistance = GetClosestDistanceAlongEdge(edge, TargetPos);
        return targetDistance + 0.05f >= startDistance;
    }

    private bool IsLegal(NativeTrafficEdge edge)
    {
        return (edge.requiredPermissions == 0 ||
                (edge.requiredPermissions & VehiclePermissions) != 0) &&
               (edge.requiredCapabilities == 0 ||
                (edge.requiredCapabilities & VehicleCapabilities) != 0);
    }

    private float GetCostFromPointToEdgeEnd(NativeTrafficEdge edge, float3 point)
    {
        float alongEdgeDistance = GetClosestDistanceAlongEdge(edge, point);
        float edgeLength = GetEdgeLength(edge);
        float distanceToLane = GetClosestDistanceToEdge(edge, point);
        return distanceToLane + math.max(0f, edgeLength - alongEdgeDistance);
    }

    private float GetClosestDistanceToEdge(NativeTrafficEdge edge, float3 point)
    {
        if (edge.waypointCount <= 1) return math.distance(point, Nodes[edge.endNodeIndex].position);

        float bestDistanceSq = float.MaxValue;
        for (int i = 1; i < edge.waypointCount; i++)
        {
            float3 segmentStart = Waypoints[edge.waypointStartIndex + i - 1];
            float3 segmentEnd = Waypoints[edge.waypointStartIndex + i];
            float3 segment = segmentEnd - segmentStart;
            float segmentLengthSq = math.lengthsq(segment);
            if (segmentLengthSq <= 0.0001f) continue;

            float t = math.clamp(math.dot(point - segmentStart, segment) / segmentLengthSq, 0f, 1f);
            float3 projected = segmentStart + segment * t;
            bestDistanceSq = math.min(bestDistanceSq, math.lengthsq(point - projected));
        }

        return math.sqrt(bestDistanceSq);
    }

    private float GetEdgeLength(NativeTrafficEdge edge)
    {
        float length = 0f;
        for (int i = 1; i < edge.waypointCount; i++)
        {
            length += math.distance(Waypoints[edge.waypointStartIndex + i - 1], Waypoints[edge.waypointStartIndex + i]);
        }

        return length;
    }

    private float GetClosestDistanceAlongEdge(NativeTrafficEdge edge, float3 point)
    {
        if (edge.waypointCount <= 1) return 0f;

        float bestDistance = 0f;
        float bestDistanceSq = float.MaxValue;
        float cumulativeDistance = 0f;

        for (int i = 1; i < edge.waypointCount; i++)
        {
            float3 segmentStart = Waypoints[edge.waypointStartIndex + i - 1];
            float3 segmentEnd = Waypoints[edge.waypointStartIndex + i];
            float3 segment = segmentEnd - segmentStart;
            float segmentLengthSq = math.lengthsq(segment);
            if (segmentLengthSq <= 0.0001f) continue;

            float t = math.clamp(math.dot(point - segmentStart, segment) / segmentLengthSq, 0f, 1f);
            float3 projected = segmentStart + segment * t;
            float distanceSq = math.lengthsq(point - projected);
            if (distanceSq < bestDistanceSq)
            {
                bestDistanceSq = distanceSq;
                bestDistance = cumulativeDistance + math.sqrt(segmentLengthSq) * t;
            }

            cumulativeDistance += math.sqrt(segmentLengthSq);
        }

        return bestDistance;
    }

    private void DisposeTemp(NativeHashMap<int, NodeRecord> records, NativeList<int> openList)
    {
        if (records.IsCreated) records.Dispose();
        if (openList.IsCreated) openList.Dispose();
    }
}
