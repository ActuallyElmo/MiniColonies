public readonly struct TrafficCongestionSnapshot
{
    public readonly TrafficGraphVersion GraphVersion;
    public readonly int ActiveVehicles;
    public readonly int ActiveLanes;
    public readonly int ReservedEdges;
    public readonly int ReservationContentionEvents;
    public readonly float AverageSpeedUnitsPerSecond;

    public TrafficCongestionSnapshot(
        TrafficGraphVersion graphVersion,
        int activeVehicles,
        int activeLanes,
        int reservedEdges,
        int reservationContentionEvents,
        float averageSpeedUnitsPerSecond)
    {
        GraphVersion = graphVersion;
        ActiveVehicles = activeVehicles;
        ActiveLanes = activeLanes;
        ReservedEdges = reservedEdges;
        ReservationContentionEvents = reservationContentionEvents;
        AverageSpeedUnitsPerSecond = averageSpeedUnitsPerSecond;
    }
}

public sealed class TrafficPerformanceSnapshot
{
    public TrafficGraphVersion GraphVersion { get; set; }
    public int GraphNodeCount { get; set; }
    public int GraphEdgeCount { get; set; }
    public int IndexedLaneCount { get; set; }
    public int IndexedLaneSegmentCount { get; set; }
    public int ClosestLaneCandidateSegments { get; set; }
    public int ClosestLaneDistanceTests { get; set; }
    public float LastCompilerMilliseconds { get; set; }
    public float LastRouteMilliseconds { get; set; }
    public float LastTickMilliseconds { get; set; }
    public int ActiveVehicles { get; set; }
    public int ActiveLanes { get; set; }
    public int ReservedEdges { get; set; }
    public int ReservationContentionEvents { get; set; }
    public TrafficCongestionSnapshot CongestionSnapshot { get; set; }
}
