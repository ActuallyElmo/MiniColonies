using System.Collections.Generic;
using UnityEngine;

// Structure needed for intersection routing connections
public class LaneEndpoint
{
    public TrafficNode Node;
    public int LocalLaneIndex;       
    public int TotalLanes;           
    public Vector3 Direction;
    public Vector2Int NeighborCell;
}

// A distinct point in space where a vehicle can be.
public class TrafficNode
{
    public Vector3 position;
    public List<TrafficEdge> outgoingEdges = new List<TrafficEdge>();

    public TrafficNode(Vector3 pos) { position = pos; }
}

// A directional link between two nodes, representing a drivable lane.
public class TrafficEdge
{
    public TrafficNode startNode;
    public TrafficNode endNode;
    
    // The exact path vehicles will follow along this edge
    public List<Vector3> waypoints = new List<Vector3>();
    
    public float speedLimit;
    public bool isIntersection;
    public Color edgeColor;

    public int laneIndex;       // 0 is Left-most, max is Right-most
    public int totalLanes;
    public bool isMergeEdge;    // True if this edge exists just to switch lanes
    public bool isUTurn;


    public TrafficEdge(TrafficNode start, TrafficNode end, float limit, bool intersection = false)
    {
        startNode = start;
        endNode = end;
        speedLimit = limit;
        isIntersection = intersection;
    }
}