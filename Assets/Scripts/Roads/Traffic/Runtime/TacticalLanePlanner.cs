public enum TacticalLaneDecisionKind
{
    KeepLane,
    PrepareRequiredMovement,
    ForcedMerge,
    WaitForReservation,
    RequestStrategicReplan
}

public enum TacticalLaneDecisionReason
{
    None,
    NoUpcomingMovement,
    RequiredControlledMovementAhead,
    LaneDropAhead,
    ReservationUnavailable,
    NoFeasibleManeuver
}

public readonly struct TacticalLaneDecision
{
    public readonly TacticalLaneDecisionKind Kind;
    public readonly TacticalLaneDecisionReason Reason;
    public readonly MovementId RequiredMovementId;

    public TacticalLaneDecision(
        TacticalLaneDecisionKind kind,
        TacticalLaneDecisionReason reason,
        MovementId requiredMovementId)
    {
        Kind = kind;
        Reason = reason;
        RequiredMovementId = requiredMovementId;
    }
}

public sealed class TacticalLanePlanner
{
    public TacticalLaneDecision Decide(
        VehicleAI vehicle,
        TrafficEdge nextRouteEdge,
        float distanceToNextEdgeUnits)
    {
        if (vehicle == null || nextRouteEdge == null)
        {
            return new TacticalLaneDecision(
                TacticalLaneDecisionKind.KeepLane,
                TacticalLaneDecisionReason.NoUpcomingMovement,
                MovementId.Invalid);
        }

        if (nextRouteEdge.kind == TrafficEdgeKind.RoadTypeTransition &&
            nextRouteEdge.isMergeEdge)
        {
            return new TacticalLaneDecision(
                TacticalLaneDecisionKind.ForcedMerge,
                TacticalLaneDecisionReason.LaneDropAhead,
                nextRouteEdge.stableMovementId);
        }

        if (nextRouteEdge.kind == TrafficEdgeKind.IntersectionMovement ||
            nextRouteEdge.kind == TrafficEdgeKind.RoadEndUTurn)
        {
            return new TacticalLaneDecision(
                TacticalLaneDecisionKind.PrepareRequiredMovement,
                TacticalLaneDecisionReason.RequiredControlledMovementAhead,
                nextRouteEdge.stableMovementId);
        }

        return new TacticalLaneDecision(
            TacticalLaneDecisionKind.KeepLane,
            TacticalLaneDecisionReason.None,
            nextRouteEdge.stableMovementId);
    }
}
