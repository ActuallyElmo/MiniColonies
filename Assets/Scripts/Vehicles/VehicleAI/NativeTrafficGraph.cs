using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

public struct NativeTrafficNode
{
    public float3 position;
    public int edgeStartIndex;
    public int edgeCount;
}

public struct NativeTrafficEdge
{
    public int startNodeIndex;
    public int endNodeIndex;
    public float cost;
    public bool isIntersection;
    
    public bool isUTurn;
    public float uTurnPenalty;

    public bool isMergeEdge;
    public float laneChangePenalty; 
    
    public int waypointStartIndex;
    public int waypointCount;
}

public class NativeTrafficGraph : MonoBehaviour
{
    public static NativeTrafficGraph Instance { get; private set; }

    [Header("Native Graph Buffers")]
    public NativeArray<NativeTrafficNode> Nodes;
    public NativeArray<NativeTrafficEdge> Edges;
    public NativeArray<float3> Waypoints;
    public NativeArray<int> NodeOutConnections; // Moved to the top for visibility!

    // Fast lookups for the Main Thread to map objects to Native indices
    public Dictionary<TrafficNode, int> NodeToIndex = new Dictionary<TrafficNode, int>();
    public Dictionary<TrafficEdge, int> EdgeToIndex = new Dictionary<TrafficEdge, int>();

    public bool IsReady { get; private set; } = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void OnDestroy()
    {
        DisposeGraph();
    }

    public void DisposeGraph()
    {
        IsReady = false;
        
        if (Nodes.IsCreated) Nodes.Dispose();
        if (Edges.IsCreated) Edges.Dispose();
        if (Waypoints.IsCreated) Waypoints.Dispose();
        
        // FIXED: The memory leak was happening because this was missing during OnDestroy
        if (NodeOutConnections.IsCreated) NodeOutConnections.Dispose();
        
        NodeToIndex.Clear();
        EdgeToIndex.Clear();
    }

    // Call this immediately after TrafficSystemBackend applies a new network
    public void RebuildGraph(List<TrafficNode> allNodes, List<TrafficEdge> allEdges)
    {
        DisposeGraph(); // This now correctly cleans up ALL native memory before rebuilding

        if (allNodes.Count == 0 || allEdges.Count == 0) return;

        Nodes = new NativeArray<NativeTrafficNode>(allNodes.Count, Allocator.Persistent);
        Edges = new NativeArray<NativeTrafficEdge>(allEdges.Count, Allocator.Persistent);

        // Calculate total waypoints for the flat buffer
        int totalWaypoints = 0;
        foreach (var edge in allEdges) totalWaypoints += edge.waypoints.Count;
        Waypoints = new NativeArray<float3>(totalWaypoints, Allocator.Persistent);

        // Map Object -> Index
        for (int i = 0; i < allNodes.Count; i++) NodeToIndex[allNodes[i]] = i;
        for (int i = 0; i < allEdges.Count; i++) EdgeToIndex[allEdges[i]] = i;

        int currentWaypointOffset = 0;

        // 1. Build Edges and Waypoints
        for (int i = 0; i < allEdges.Count; i++)
        {
            TrafficEdge edge = allEdges[i];
            
            for (int w = 0; w < edge.waypoints.Count; w++)
            {
                Waypoints[currentWaypointOffset + w] = edge.waypoints[w];
            }

            Edges[i] = new NativeTrafficEdge
            {
                startNodeIndex = NodeToIndex[edge.startNode],
                endNodeIndex = NodeToIndex[edge.endNode],
                cost = edge.isIntersection ? Vector3.Distance(edge.startNode.position, edge.endNode.position) * 1f 
                                           : Vector3.Distance(edge.startNode.position, edge.endNode.position),
                isIntersection = edge.isIntersection,
                isMergeEdge = edge.isMergeEdge,
                laneChangePenalty = edge.isMergeEdge ? 0.5f : 0.0f,

                isUTurn = edge.isUTurn,
                uTurnPenalty = edge.isUTurn ? 0.3f : 0.0f,

                waypointStartIndex = currentWaypointOffset,
                waypointCount = edge.waypoints.Count
            };

            currentWaypointOffset += edge.waypoints.Count;
        }

        // 2. Build Nodes (linking their outgoing edges sequentially)
        // Note: Because outgoingEdges might not be sequential in the allEdges list, 
        // we need an intermediary array of all outgoing edge indices for Burst. 
        // For simplicity and speed, we will flatten edge pointers directly into the node.
        
        // Let's create a secondary flat array for Node -> Outgoing Edge Indices
        List<int> flatOutgoingEdges = new List<int>();

        for (int i = 0; i < allNodes.Count; i++)
        {
            TrafficNode node = allNodes[i];
            int startIndex = flatOutgoingEdges.Count;
            
            foreach (var outEdge in node.outgoingEdges)
            {
                flatOutgoingEdges.Add(EdgeToIndex[outEdge]);
            }

            Nodes[i] = new NativeTrafficNode
            {
                position = node.position,
                edgeStartIndex = startIndex,
                edgeCount = node.outgoingEdges.Count
            };
        }

        // We must also pass the flat connection mapping to Burst
        // (Store it as a persistent array in the class)
        NodeOutConnections = new NativeArray<int>(flatOutgoingEdges.ToArray(), Allocator.Persistent);

        IsReady = true;
    }
}