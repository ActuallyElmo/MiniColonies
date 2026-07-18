using System.Collections.Generic;
using UnityEngine;

public class TrafficRoute
{
    public List<int> EdgeIds = new List<int>();
    public List<TrafficEdge> ManagedEdges = new List<TrafficEdge>();
    public Queue<Vector3> DebugWaypoints = new Queue<Vector3>();
    public float StartDistanceOnFirstEdge;
    public float EndDistanceOnFinalEdge;
    public RouteCorridor Corridor;
    public RouteFailureReason FailureReason;
    public RouteRerouteReason RerouteReason;

    public bool MatchesGraph(TrafficGraphVersion version) =>
        Corridor != null && Corridor.MatchesGraph(version);
}
