using System.Collections.Generic;
using UnityEngine;

public enum RouteFailureReason
{
    None,
    GraphUnavailable,
    ExactStartPortUnavailable,
    ExactTargetPortUnavailable,
    VehicleProfileInvalid,
    GraphVersionMismatch,
    NoLegalCorridor
}

public enum RouteRerouteReason
{
    InitialRequest,
    GraphRebuild,
    TacticalReplan,
    ReservationFailure,
    Manual
}

public sealed class RouteLaneSet
{
    public RoadSectionId SectionId { get; set; }
    public MovementId RequiredExitMovementId { get; set; }
    public readonly List<LaneId> AcceptableLaneIds = new List<LaneId>();
}

public sealed class RouteDiagnostic
{
    public RouteFailureReason FailureReason { get; set; }
    public string Message { get; set; }
}

public sealed class StrategicCongestionSnapshot
{
    private readonly Dictionary<LaneSegmentId, float> _laneSegmentPenalties;
    private readonly Dictionary<MovementId, float> _movementPenalties;

    public TrafficGraphVersion GraphVersion { get; }

    public StrategicCongestionSnapshot(
        TrafficGraphVersion graphVersion,
        IReadOnlyDictionary<LaneSegmentId, float> laneSegmentPenalties = null,
        IReadOnlyDictionary<MovementId, float> movementPenalties = null)
    {
        GraphVersion = graphVersion;
        _laneSegmentPenalties = laneSegmentPenalties != null
            ? new Dictionary<LaneSegmentId, float>(laneSegmentPenalties)
            : new Dictionary<LaneSegmentId, float>();
        _movementPenalties = movementPenalties != null
            ? new Dictionary<MovementId, float>(movementPenalties)
            : new Dictionary<MovementId, float>();
    }

    public float GetPenalty(TrafficEdge edge)
    {
        if (edge == null) return 0f;
        if (edge.stableLaneSegmentId.IsValid &&
            _laneSegmentPenalties.TryGetValue(
                edge.stableLaneSegmentId,
                out float lanePenalty))
        {
            return Mathf.Max(0f, lanePenalty);
        }
        if (edge.stableMovementId.IsValid &&
            _movementPenalties.TryGetValue(
                edge.stableMovementId,
                out float movementPenalty))
        {
            return Mathf.Max(0f, movementPenalty);
        }
        return 0f;
    }
}

public sealed class RouteCorridor
{
    public TrafficGraphVersion GraphVersion { get; set; }
    public BuildingPortAnchorId StartPortAnchorId { get; set; }
    public Vector2Int StartPortCell { get; set; }
    public bool HasStartPortCell { get; set; }
    public BuildingPortAnchorId TargetPortAnchorId { get; set; }
    public Vector2Int TargetPortCell { get; set; }
    public bool HasTargetPortCell { get; set; }
    public RouteRerouteReason RerouteReason { get; set; }
    public readonly List<RoadSectionId> RoadSectionIds =
        new List<RoadSectionId>();
    public readonly List<LaneSegmentId> LaneSegmentIds =
        new List<LaneSegmentId>();
    public readonly List<MovementId> RequiredMovementIds =
        new List<MovementId>();
    public readonly List<RouteLaneSet> AcceptableLaneSets =
        new List<RouteLaneSet>();
    public readonly List<RouteDiagnostic> Diagnostics =
        new List<RouteDiagnostic>();
    public RouteFailureReason FailureReason { get; set; }

    public bool IsSuccess =>
        FailureReason == RouteFailureReason.None &&
        GraphVersion.IsValid &&
        LaneSegmentIds.Count > 0;

    public bool MatchesGraph(TrafficGraphVersion version) =>
        GraphVersion.IsValid && GraphVersion == version;
}
