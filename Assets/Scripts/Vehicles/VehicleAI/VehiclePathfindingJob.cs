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
    
    public float3 StartPos;
    public float3 TargetPos;

    // The result output
    public NativeList<float3> ResultWaypoints;

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
            
            // Check direct match
            for (int e = 0; e < EndEdgeIndices.Length; e++)
            {
                if (edgeIdx == EndEdgeIndices[e])
                {
                    ExtractWaypoints(edgeIdx, out records);
                    DisposeTemp(records, openList);
                    return;
                }
            }

            int startNodeIdx = startEdge.endNodeIndex;
            float initialCost = math.distance(StartPos, Nodes[startNodeIdx].position); // Calculate actual travel distance

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
                int neighborIdx = edge.endNodeIndex;

                float totalEdgeCost = edge.cost;

                // Apply standard lateral merge penalty
                if (edge.isMergeEdge)
                {
                    totalEdgeCost += edge.laneChangePenalty;
                }

                // FIX: Only apply the massive U-Turn penalty if it's a true U-Turn.
                if (edge.isUTurn)
                {
                    totalEdgeCost += edge.uTurnPenalty;
                }
                // NEW FALLBACK: If a car is trapped in the wrong lane at an intersection, 
                // allow it to take the correct turn but heavily tax it so it prefers proper merging.
                else if (edge.isIntersection && edge.laneChangePenalty > 0f) 
                {
                    totalEdgeCost += 15.0f; // Artificial tax for a sloppy intersection merge
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
        
        bool justMerged = false;
        bool isFirstValidEdge = true; // Protects the StartPos trim if edge 0 is a merge

        for (int i = 0; i < pathEdges.Length; i++)
        {
            NativeTrafficEdge edge = Edges[pathEdges[i]];
            
            // Skip the sharp 90-degree lateral points completely
            if (edge.isMergeEdge)
            {
                justMerged = true;
                continue; 
            }

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

            // --- SMOOTH S-CURVE BEZIER INJECTION ---
            if (justMerged)
            {
                float mergeDistance = 0.5f; // Extended slightly to make the S-curve look natural
                float distAccum = 0f;
                int mergeSkipIndex = startIndex;
                
                for (int j = startIndex; j < endIndex; j++)
                {
                    distAccum += math.distance(Waypoints[edge.waypointStartIndex + j], Waypoints[edge.waypointStartIndex + j + 1]);
                    mergeSkipIndex = j + 1;
                    if (distAccum >= mergeDistance) break;
                }
                
                mergeSkipIndex = math.min(mergeSkipIndex, endIndex);

                // If we have enough history to know the car's forward direction, draw a Bezier curve
                if (rawPoints.Length >= 2) 
                {
                    float3 p0 = rawPoints[rawPoints.Length - 1];
                    float3 p_prev = rawPoints[rawPoints.Length - 2];
                    float3 dir0 = math.normalize(p0 - p_prev); // Tangent leaving current lane

                    float3 p3 = Waypoints[edge.waypointStartIndex + mergeSkipIndex];
                    float3 dir3 = float3.zero; // Tangent entering new lane
                    
                    if (mergeSkipIndex < endIndex) {
                        dir3 = math.normalize(Waypoints[edge.waypointStartIndex + mergeSkipIndex + 1] - p3);
                    } else if (mergeSkipIndex > startIndex) {
                        dir3 = math.normalize(p3 - Waypoints[edge.waypointStartIndex + mergeSkipIndex - 1]);
                    } else {
                        dir3 = dir0; // Fallback parallel
                    }

                    // Control points scaled to push the curve forward smoothly
                    float controlScale = mergeDistance * 0.45f;
                    float3 p1 = p0 + (dir0 * controlScale);
                    float3 p2 = p3 - (dir3 * controlScale);

                    // Generate smooth interpolated waypoints
                    int resolution = 12;
                    for (int step = 1; step < resolution; step++)
                    {
                        float t = step / (float)resolution;
                        float u = 1 - t;
                        float tt = t * t;
                        float3 p = (u * u * u) * p0;
                        p += 3 * (u * u) * t * p1;
                        p += 3 * u * tt * p2;
                        p += (tt * t) * p3;
                        rawPoints.Add(p);
                    }
                }
                
                // Jump the loop index forward so we resume tracking perfectly after the S-Curve
                startIndex = mergeSkipIndex;
                justMerged = false;
            }

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

    private void DisposeTemp(NativeHashMap<int, NodeRecord> records, NativeList<int> openList)
    {
        if (records.IsCreated) records.Dispose();
        if (openList.IsCreated) openList.Dispose();
    }
}