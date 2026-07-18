using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

public readonly struct TrafficGeometryId :
    IEquatable<TrafficGeometryId>,
    IComparable<TrafficGeometryId>
{
    public static readonly TrafficGeometryId Invalid = default;

    public ulong Value { get; }
    public bool IsValid => Value != 0UL;

    public TrafficGeometryId(ulong value) => Value = value;

    public static TrafficGeometryId FromStableKey(string key) =>
        new TrafficGeometryId(
            TrafficStableHash.FromNormalizedKey(nameof(TrafficGeometryId), key));

    public int CompareTo(TrafficGeometryId other) => Value.CompareTo(other.Value);
    public bool Equals(TrafficGeometryId other) => Value == other.Value;
    public override bool Equals(object obj) =>
        obj is TrafficGeometryId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid
        ? $"TrafficGeometryId(0x{Value:X16})"
        : "TrafficGeometryId(Invalid)";

    public static bool operator ==(
        TrafficGeometryId left,
        TrafficGeometryId right) => left.Equals(right);

    public static bool operator !=(
        TrafficGeometryId left,
        TrafficGeometryId right) => !left.Equals(right);
}

public readonly struct LaneSegmentId :
    IEquatable<LaneSegmentId>,
    IComparable<LaneSegmentId>
{
    public static readonly LaneSegmentId Invalid = default;

    public ulong Value { get; }
    public bool IsValid => Value != 0UL;

    public LaneSegmentId(ulong value) => Value = value;

    public static LaneSegmentId FromStableKey(string key) =>
        new LaneSegmentId(
            TrafficStableHash.FromNormalizedKey(nameof(LaneSegmentId), key));

    public int CompareTo(LaneSegmentId other) => Value.CompareTo(other.Value);
    public bool Equals(LaneSegmentId other) => Value == other.Value;
    public override bool Equals(object obj) =>
        obj is LaneSegmentId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid
        ? $"LaneSegmentId(0x{Value:X16})"
        : "LaneSegmentId(Invalid)";
    public static bool operator ==(LaneSegmentId left, LaneSegmentId right) =>
        left.Equals(right);
    public static bool operator !=(LaneSegmentId left, LaneSegmentId right) =>
        !left.Equals(right);
}

public enum TrafficLaneFlowDirection
{
    SectionStartToEnd,
    SectionEndToStart
}

public enum TrafficMovementKind
{
    LaneContinuation,
    OptionalLaneChange,
    MandatoryMerge,
    LaneExpansion,
    Intersection,
    RoadEndUTurn,
    BuildingPortEntry,
    BuildingPortExit
}

public enum TrafficMovementRejectionReason
{
    None,
    NoLegalOutgoingLane,
    WrongWay,
    UnsupportedByRoadProfile,
    DisabledByAuthoredPolicy,
    InvalidAuthoredLane,
    UTurnNotAllowed,
    IncompatiblePermissions,
    MissingGeometry
}

public enum TrafficMovementOwnerKind
{
    ControlledIntersection,
    TransitionBoundary,
    RoadEnd,
    LaneChangePolicy,
    LoopContinuity
}

public sealed class MovementOwnerRecord
{
    private readonly Vector2Int[] _sourceCells;
    private readonly ReadOnlyCollection<Vector2Int> _readOnlySourceCells;

    public ControlledNodeId Id { get; }
    public TrafficMovementOwnerKind Kind { get; }
    public Vector2Int PrimaryCell { get; }
    public RoadSectionId SectionId { get; }
    public IReadOnlyList<Vector2Int> SourceCells => _readOnlySourceCells;

    public MovementOwnerRecord(
        ControlledNodeId id,
        TrafficMovementOwnerKind kind,
        Vector2Int primaryCell,
        RoadSectionId sectionId,
        IReadOnlyList<Vector2Int> sourceCells)
    {
        Id = id;
        Kind = kind;
        PrimaryCell = primaryCell;
        SectionId = sectionId;
        _sourceCells = TrafficGraphRecordCopy.Copy(sourceCells);
        _readOnlySourceCells = Array.AsReadOnly(_sourceCells);
    }
}

public readonly struct LanePositionAnchorRecord :
    IEquatable<LanePositionAnchorRecord>
{
    public LaneId LaneId { get; }
    public float DistanceUnits { get; }

    public LanePositionAnchorRecord(LaneId laneId, float distanceUnits)
    {
        LaneId = laneId;
        DistanceUnits = distanceUnits;
    }

    public bool Equals(LanePositionAnchorRecord other) =>
        LaneId == other.LaneId && DistanceUnits.Equals(other.DistanceUnits);
    public override bool Equals(object obj) =>
        obj is LanePositionAnchorRecord other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            return (LaneId.GetHashCode() * 397) ^
                   DistanceUnits.GetHashCode();
        }
    }
}

public sealed class LaneTraversalSegmentRecord
{
    public LaneSegmentId Id { get; }
    public LaneId LaneId { get; }
    public int SegmentOrdinal { get; }
    public float StartDistanceUnits { get; }
    public float EndDistanceUnits { get; }
    public TrafficGeometryId GeometryId { get; }

    public LaneTraversalSegmentRecord(
        LaneSegmentId id,
        LaneId laneId,
        int segmentOrdinal,
        float startDistanceUnits,
        float endDistanceUnits,
        TrafficGeometryId geometryId)
    {
        Id = id;
        LaneId = laneId;
        SegmentOrdinal = segmentOrdinal;
        StartDistanceUnits = startDistanceUnits;
        EndDistanceUnits = endDistanceUnits;
        GeometryId = geometryId;
    }
}

public sealed class TrafficGeometryRecord
{
    private readonly Vector3[] _samples;
    private readonly ReadOnlyCollection<Vector3> _readOnlySamples;

    public TrafficGeometryId Id { get; }
    public IReadOnlyList<Vector3> Samples => _readOnlySamples;
    public float LengthUnits { get; }

    public TrafficGeometryRecord(
        TrafficGeometryId id,
        IReadOnlyList<Vector3> samples)
    {
        Id = id;
        _samples = TrafficGraphRecordCopy.Copy(samples);
        _readOnlySamples = Array.AsReadOnly(_samples);
        LengthUnits = CalculateLength(_samples);
    }

    private static float CalculateLength(IReadOnlyList<Vector3> samples)
    {
        float length = 0f;
        for (int i = 1; i < samples.Count; i++)
        {
            length += Vector3.Distance(samples[i - 1], samples[i]);
        }

        return length;
    }
}

public sealed class RoadSectionRecord
{
    private readonly LaneId[] _laneIds;
    private readonly Vector2Int[] _sourceCells;
    private readonly ReadOnlyCollection<LaneId> _readOnlyLaneIds;
    private readonly ReadOnlyCollection<Vector2Int> _readOnlySourceCells;

    public RoadSectionId Id { get; }
    public Vector2Int StartAnchorCell { get; }
    public Vector2Int EndAnchorCell { get; }
    public int StartLegDirectionBit { get; }
    public int EndLegDirectionBit { get; }
    public RoadProfileId RoadProfileId { get; }
    public TrafficGeometryId CenterlineGeometryId { get; }
    public RoadPermissionMask AllowedPermissions { get; }
    public VehicleCapabilityMask AllowedCapabilities { get; }
    public IReadOnlyList<LaneId> LaneIds => _readOnlyLaneIds;
    public IReadOnlyList<Vector2Int> SourceCells => _readOnlySourceCells;

    public RoadSectionRecord(
        RoadSectionId id,
        Vector2Int startAnchorCell,
        Vector2Int endAnchorCell,
        int startLegDirectionBit,
        int endLegDirectionBit,
        RoadProfileId roadProfileId,
        TrafficGeometryId centerlineGeometryId,
        RoadPermissionMask allowedPermissions,
        VehicleCapabilityMask allowedCapabilities,
        IReadOnlyList<LaneId> laneIds,
        IReadOnlyList<Vector2Int> sourceCells)
    {
        Id = id;
        StartAnchorCell = startAnchorCell;
        EndAnchorCell = endAnchorCell;
        StartLegDirectionBit = startLegDirectionBit;
        EndLegDirectionBit = endLegDirectionBit;
        RoadProfileId = roadProfileId;
        CenterlineGeometryId = centerlineGeometryId;
        AllowedPermissions = allowedPermissions;
        AllowedCapabilities = allowedCapabilities;
        _laneIds = TrafficGraphRecordCopy.Copy(laneIds);
        _sourceCells = TrafficGraphRecordCopy.Copy(sourceCells);
        _readOnlyLaneIds = Array.AsReadOnly(_laneIds);
        _readOnlySourceCells = Array.AsReadOnly(_sourceCells);
    }
}

public sealed class LaneRecord
{
    private readonly MovementId[] _outgoingMovementIds;
    private readonly LaneSegmentId[] _traversalSegmentIds;
    private readonly ReadOnlyCollection<MovementId> _readOnlyOutgoingMovementIds;
    private readonly ReadOnlyCollection<LaneSegmentId> _readOnlyTraversalSegmentIds;

    public LaneId Id { get; }
    public RoadSectionId SectionId { get; }
    public TrafficLaneFlowDirection FlowDirection { get; }
    public int LaneOrdinal { get; }
    public int LaneCountInDirection { get; }
    public Vector2Int StartAnchorCell { get; }
    public Vector2Int EndAnchorCell { get; }
    public int StartLegDirectionBit { get; }
    public int EndLegDirectionBit { get; }
    public RoadPermissionMask AllowedPermissions { get; }
    public VehicleCapabilityMask AllowedCapabilities { get; }
    public float SpeedLimitUnitsPerSecond { get; }
    public TrafficGeometryId GeometryId { get; }
    public IReadOnlyList<MovementId> OutgoingMovementIds =>
        _readOnlyOutgoingMovementIds;
    public IReadOnlyList<LaneSegmentId> TraversalSegmentIds =>
        _readOnlyTraversalSegmentIds;

    public LaneRecord(
        LaneId id,
        RoadSectionId sectionId,
        TrafficLaneFlowDirection flowDirection,
        int laneOrdinal,
        int laneCountInDirection,
        Vector2Int startAnchorCell,
        Vector2Int endAnchorCell,
        int startLegDirectionBit,
        int endLegDirectionBit,
        RoadPermissionMask allowedPermissions,
        VehicleCapabilityMask allowedCapabilities,
        float speedLimitUnitsPerSecond,
        TrafficGeometryId geometryId,
        IReadOnlyList<MovementId> outgoingMovementIds,
        IReadOnlyList<LaneSegmentId> traversalSegmentIds)
    {
        Id = id;
        SectionId = sectionId;
        FlowDirection = flowDirection;
        LaneOrdinal = laneOrdinal;
        LaneCountInDirection = laneCountInDirection;
        StartAnchorCell = startAnchorCell;
        EndAnchorCell = endAnchorCell;
        StartLegDirectionBit = startLegDirectionBit;
        EndLegDirectionBit = endLegDirectionBit;
        AllowedPermissions = allowedPermissions;
        AllowedCapabilities = allowedCapabilities;
        SpeedLimitUnitsPerSecond = speedLimitUnitsPerSecond;
        GeometryId = geometryId;
        _outgoingMovementIds = TrafficGraphRecordCopy.Copy(outgoingMovementIds);
        _traversalSegmentIds =
            TrafficGraphRecordCopy.Copy(traversalSegmentIds);
        _readOnlyOutgoingMovementIds = Array.AsReadOnly(_outgoingMovementIds);
        _readOnlyTraversalSegmentIds =
            Array.AsReadOnly(_traversalSegmentIds);
    }
}

public sealed class MovementRecord
{
    public MovementId Id { get; }
    public LaneId SourceLaneId { get; }
    public LaneId TargetLaneId { get; }
    public TrafficMovementKind Kind { get; }
    public ControlledNodeId OwnerId { get; }
    public Vector2Int OwnerCell { get; }
    public int FromDirectionBit { get; }
    public int ToDirectionBit { get; }
    public TrafficTurnType TurnType { get; }
    public RoadPermissionMask RequiredPermissions { get; }
    public VehicleCapabilityMask RequiredCapabilities { get; }
    public TrafficGeometryId GeometryId { get; }
    public LaneSegmentId SourceSegmentId { get; }
    public LaneSegmentId TargetSegmentId { get; }
    public float SourceDistanceUnits { get; }
    public float TargetDistanceUnits { get; }
    public bool IsMandatory { get; }
    public int PolicyPriority { get; }
    public int ApproachLegMask { get; }

    public MovementRecord(
        MovementId id,
        LaneId sourceLaneId,
        LaneId targetLaneId,
        TrafficMovementKind kind,
        ControlledNodeId ownerId,
        Vector2Int ownerCell,
        int fromDirectionBit,
        int toDirectionBit,
        TrafficTurnType turnType,
        RoadPermissionMask requiredPermissions,
        VehicleCapabilityMask requiredCapabilities,
        TrafficGeometryId geometryId,
        LaneSegmentId sourceSegmentId,
        LaneSegmentId targetSegmentId,
        float sourceDistanceUnits,
        float targetDistanceUnits,
        bool isMandatory,
        int policyPriority,
        int approachLegMask)
    {
        Id = id;
        SourceLaneId = sourceLaneId;
        TargetLaneId = targetLaneId;
        Kind = kind;
        OwnerId = ownerId;
        OwnerCell = ownerCell;
        FromDirectionBit = fromDirectionBit;
        ToDirectionBit = toDirectionBit;
        TurnType = turnType;
        RequiredPermissions = requiredPermissions;
        RequiredCapabilities = requiredCapabilities;
        GeometryId = geometryId;
        SourceSegmentId = sourceSegmentId;
        TargetSegmentId = targetSegmentId;
        SourceDistanceUnits = sourceDistanceUnits;
        TargetDistanceUnits = targetDistanceUnits;
        IsMandatory = isMandatory;
        PolicyPriority = policyPriority;
        ApproachLegMask = approachLegMask;
    }
}

public sealed class ControlledNodeRecord
{
    private readonly LaneId[] _incomingLaneIds;
    private readonly LaneId[] _outgoingLaneIds;
    private readonly MovementId[] _movementIds;
    private readonly ReadOnlyCollection<LaneId> _readOnlyIncomingLaneIds;
    private readonly ReadOnlyCollection<LaneId> _readOnlyOutgoingLaneIds;
    private readonly ReadOnlyCollection<MovementId> _readOnlyMovementIds;

    public ControlledNodeId Id { get; }
    public Vector2Int GridPosition { get; }
    public RoadNodeKind NodeKind { get; }
    public IntersectionRuleType RuleType { get; }
    public int PriorityDirectionBitA { get; }
    public int PriorityDirectionBitB { get; }
    public float TrafficLightCycleSeconds { get; }
    public IReadOnlyList<LaneId> IncomingLaneIds => _readOnlyIncomingLaneIds;
    public IReadOnlyList<LaneId> OutgoingLaneIds => _readOnlyOutgoingLaneIds;
    public IReadOnlyList<MovementId> MovementIds => _readOnlyMovementIds;

    public ControlledNodeRecord(
        ControlledNodeId id,
        Vector2Int gridPosition,
        RoadNodeKind nodeKind,
        IntersectionRuleType ruleType,
        int priorityDirectionBitA,
        int priorityDirectionBitB,
        float trafficLightCycleSeconds,
        IReadOnlyList<LaneId> incomingLaneIds,
        IReadOnlyList<LaneId> outgoingLaneIds,
        IReadOnlyList<MovementId> movementIds)
    {
        Id = id;
        GridPosition = gridPosition;
        NodeKind = nodeKind;
        RuleType = ruleType;
        PriorityDirectionBitA = priorityDirectionBitA;
        PriorityDirectionBitB = priorityDirectionBitB;
        TrafficLightCycleSeconds = trafficLightCycleSeconds;
        _incomingLaneIds = TrafficGraphRecordCopy.Copy(incomingLaneIds);
        _outgoingLaneIds = TrafficGraphRecordCopy.Copy(outgoingLaneIds);
        _movementIds = TrafficGraphRecordCopy.Copy(movementIds);
        _readOnlyIncomingLaneIds = Array.AsReadOnly(_incomingLaneIds);
        _readOnlyOutgoingLaneIds = Array.AsReadOnly(_outgoingLaneIds);
        _readOnlyMovementIds = Array.AsReadOnly(_movementIds);
    }
}

public sealed class BuildingPortAnchorRecord
{
    private readonly LanePositionAnchorRecord[] _arrivalAnchors;
    private readonly LanePositionAnchorRecord[] _departureAnchors;
    private readonly ReadOnlyCollection<LanePositionAnchorRecord>
        _readOnlyArrivalAnchors;
    private readonly ReadOnlyCollection<LanePositionAnchorRecord>
        _readOnlyDepartureAnchors;

    public BuildingPortAnchorId Id { get; }
    public Vector2Int BuildingOriginCell { get; }
    public Vector2Int PortCell { get; }
    public PortType PortType { get; }
    public IReadOnlyList<LanePositionAnchorRecord> ArrivalAnchors =>
        _readOnlyArrivalAnchors;
    public IReadOnlyList<LanePositionAnchorRecord> DepartureAnchors =>
        _readOnlyDepartureAnchors;

    public BuildingPortAnchorRecord(
        BuildingPortAnchorId id,
        Vector2Int buildingOriginCell,
        Vector2Int portCell,
        PortType portType,
        IReadOnlyList<LanePositionAnchorRecord> arrivalAnchors,
        IReadOnlyList<LanePositionAnchorRecord> departureAnchors)
    {
        Id = id;
        BuildingOriginCell = buildingOriginCell;
        PortCell = portCell;
        PortType = portType;
        _arrivalAnchors = TrafficGraphRecordCopy.Copy(arrivalAnchors);
        _departureAnchors = TrafficGraphRecordCopy.Copy(departureAnchors);
        _readOnlyArrivalAnchors = Array.AsReadOnly(_arrivalAnchors);
        _readOnlyDepartureAnchors = Array.AsReadOnly(_departureAnchors);
    }
}

public sealed class MovementRejectionRecord
{
    public Vector2Int OwnerCell { get; }
    public LaneId SourceLaneId { get; }
    public LaneId TargetLaneId { get; }
    public TrafficMovementKind AttemptedKind { get; }
    public TrafficMovementRejectionReason Reason { get; }

    public MovementRejectionRecord(
        Vector2Int ownerCell,
        LaneId sourceLaneId,
        LaneId targetLaneId,
        TrafficMovementKind attemptedKind,
        TrafficMovementRejectionReason reason)
    {
        OwnerCell = ownerCell;
        SourceLaneId = sourceLaneId;
        TargetLaneId = targetLaneId;
        AttemptedKind = attemptedKind;
        Reason = reason;
    }
}

public sealed class TrafficGraphSnapshot
{
    private readonly RoadSectionRecord[] _sections;
    private readonly LaneRecord[] _lanes;
    private readonly MovementRecord[] _movements;
    private readonly ControlledNodeRecord[] _controlledNodes;
    private readonly MovementOwnerRecord[] _movementOwners;
    private readonly LaneTraversalSegmentRecord[] _laneSegments;
    private readonly TrafficGeometryRecord[] _geometry;
    private readonly BuildingPortAnchorRecord[] _buildingPortAnchors;
    private readonly MovementRejectionRecord[] _movementRejections;
    private readonly ReadOnlyCollection<RoadSectionRecord> _readOnlySections;
    private readonly ReadOnlyCollection<LaneRecord> _readOnlyLanes;
    private readonly ReadOnlyCollection<MovementRecord> _readOnlyMovements;
    private readonly ReadOnlyCollection<ControlledNodeRecord> _readOnlyControlledNodes;
    private readonly ReadOnlyCollection<MovementOwnerRecord> _readOnlyMovementOwners;
    private readonly ReadOnlyCollection<LaneTraversalSegmentRecord>
        _readOnlyLaneSegments;
    private readonly ReadOnlyCollection<TrafficGeometryRecord> _readOnlyGeometry;
    private readonly ReadOnlyCollection<BuildingPortAnchorRecord> _readOnlyBuildingPortAnchors;
    private readonly ReadOnlyCollection<MovementRejectionRecord> _readOnlyMovementRejections;
    private readonly Dictionary<LaneId, int> _laneIndexById;
    private readonly Dictionary<RoadSectionId, int> _sectionIndexById;
    private readonly Dictionary<MovementId, int> _movementIndexById;
    private readonly Dictionary<ControlledNodeId, int> _controlledNodeIndexById;
    private readonly Dictionary<ControlledNodeId, int> _movementOwnerIndexById;
    private readonly Dictionary<LaneSegmentId, int> _laneSegmentIndexById;
    private readonly Dictionary<BuildingPortAnchorId, int> _portAnchorIndexById;
    private readonly Dictionary<TrafficGeometryId, int> _geometryIndexById;

    public TrafficGraphVersion Version { get; }
    public int SourceRoadRevision { get; }
    public int SourceBuildingRevision { get; }
    public IReadOnlyList<RoadSectionRecord> Sections => _readOnlySections;
    public IReadOnlyList<LaneRecord> Lanes => _readOnlyLanes;
    public IReadOnlyList<MovementRecord> Movements => _readOnlyMovements;
    public IReadOnlyList<ControlledNodeRecord> ControlledNodes =>
        _readOnlyControlledNodes;
    public IReadOnlyList<MovementOwnerRecord> MovementOwners =>
        _readOnlyMovementOwners;
    public IReadOnlyList<LaneTraversalSegmentRecord> LaneSegments =>
        _readOnlyLaneSegments;
    public IReadOnlyList<TrafficGeometryRecord> Geometry => _readOnlyGeometry;
    public IReadOnlyList<BuildingPortAnchorRecord> BuildingPortAnchors =>
        _readOnlyBuildingPortAnchors;
    public IReadOnlyList<MovementRejectionRecord> MovementRejections =>
        _readOnlyMovementRejections;

    internal TrafficGraphSnapshot(
        TrafficGraphVersion version,
        int sourceRoadRevision,
        int sourceBuildingRevision,
        IReadOnlyList<RoadSectionRecord> sections,
        IReadOnlyList<LaneRecord> lanes,
        IReadOnlyList<MovementRecord> movements,
        IReadOnlyList<ControlledNodeRecord> controlledNodes,
        IReadOnlyList<MovementOwnerRecord> movementOwners,
        IReadOnlyList<LaneTraversalSegmentRecord> laneSegments,
        IReadOnlyList<TrafficGeometryRecord> geometry,
        IReadOnlyList<BuildingPortAnchorRecord> buildingPortAnchors,
        IReadOnlyList<MovementRejectionRecord> movementRejections)
    {
        Version = version;
        SourceRoadRevision = sourceRoadRevision;
        SourceBuildingRevision = sourceBuildingRevision;
        _sections = TrafficGraphRecordCopy.Copy(sections);
        _lanes = TrafficGraphRecordCopy.Copy(lanes);
        _movements = TrafficGraphRecordCopy.Copy(movements);
        _controlledNodes = TrafficGraphRecordCopy.Copy(controlledNodes);
        _movementOwners = TrafficGraphRecordCopy.Copy(movementOwners);
        _laneSegments = TrafficGraphRecordCopy.Copy(laneSegments);
        _geometry = TrafficGraphRecordCopy.Copy(geometry);
        _buildingPortAnchors = TrafficGraphRecordCopy.Copy(buildingPortAnchors);
        _movementRejections = TrafficGraphRecordCopy.Copy(movementRejections);
        _readOnlySections = Array.AsReadOnly(_sections);
        _readOnlyLanes = Array.AsReadOnly(_lanes);
        _readOnlyMovements = Array.AsReadOnly(_movements);
        _readOnlyControlledNodes = Array.AsReadOnly(_controlledNodes);
        _readOnlyMovementOwners = Array.AsReadOnly(_movementOwners);
        _readOnlyLaneSegments = Array.AsReadOnly(_laneSegments);
        _readOnlyGeometry = Array.AsReadOnly(_geometry);
        _readOnlyBuildingPortAnchors = Array.AsReadOnly(_buildingPortAnchors);
        _readOnlyMovementRejections = Array.AsReadOnly(_movementRejections);

        _laneIndexById = new Dictionary<LaneId, int>(_lanes.Length);
        for (int i = 0; i < _lanes.Length; i++) _laneIndexById.Add(_lanes[i].Id, i);

        _sectionIndexById = new Dictionary<RoadSectionId, int>(_sections.Length);
        for (int i = 0; i < _sections.Length; i++)
        {
            _sectionIndexById.Add(_sections[i].Id, i);
        }

        _movementIndexById = new Dictionary<MovementId, int>(_movements.Length);
        for (int i = 0; i < _movements.Length; i++)
        {
            _movementIndexById.Add(_movements[i].Id, i);
        }

        _controlledNodeIndexById =
            new Dictionary<ControlledNodeId, int>(_controlledNodes.Length);
        for (int i = 0; i < _controlledNodes.Length; i++)
        {
            _controlledNodeIndexById.Add(_controlledNodes[i].Id, i);
        }

        _movementOwnerIndexById =
            new Dictionary<ControlledNodeId, int>(_movementOwners.Length);
        for (int i = 0; i < _movementOwners.Length; i++)
        {
            _movementOwnerIndexById.Add(_movementOwners[i].Id, i);
        }

        _laneSegmentIndexById =
            new Dictionary<LaneSegmentId, int>(_laneSegments.Length);
        for (int i = 0; i < _laneSegments.Length; i++)
        {
            _laneSegmentIndexById.Add(_laneSegments[i].Id, i);
        }

        _portAnchorIndexById =
            new Dictionary<BuildingPortAnchorId, int>(_buildingPortAnchors.Length);
        for (int i = 0; i < _buildingPortAnchors.Length; i++)
        {
            _portAnchorIndexById.Add(_buildingPortAnchors[i].Id, i);
        }

        _geometryIndexById =
            new Dictionary<TrafficGeometryId, int>(_geometry.Length);
        for (int i = 0; i < _geometry.Length; i++)
        {
            _geometryIndexById.Add(_geometry[i].Id, i);
        }
    }

    public bool TryGetSection(RoadSectionId id, out RoadSectionRecord section)
    {
        if (_sectionIndexById.TryGetValue(id, out int index))
        {
            section = _sections[index];
            return true;
        }

        section = null;
        return false;
    }

    public bool TryGetMovement(MovementId id, out MovementRecord movement)
    {
        if (_movementIndexById.TryGetValue(id, out int index))
        {
            movement = _movements[index];
            return true;
        }

        movement = null;
        return false;
    }

    public bool TryGetControlledNode(
        ControlledNodeId id,
        out ControlledNodeRecord controlledNode)
    {
        if (_controlledNodeIndexById.TryGetValue(id, out int index))
        {
            controlledNode = _controlledNodes[index];
            return true;
        }

        controlledNode = null;
        return false;
    }

    public bool TryGetMovementOwner(
        ControlledNodeId id,
        out MovementOwnerRecord owner)
    {
        if (_movementOwnerIndexById.TryGetValue(id, out int index))
        {
            owner = _movementOwners[index];
            return true;
        }

        owner = null;
        return false;
    }

    public bool TryGetLaneSegment(
        LaneSegmentId id,
        out LaneTraversalSegmentRecord segment)
    {
        if (_laneSegmentIndexById.TryGetValue(id, out int index))
        {
            segment = _laneSegments[index];
            return true;
        }

        segment = null;
        return false;
    }

    public bool TryGetBuildingPortAnchor(
        BuildingPortAnchorId id,
        out BuildingPortAnchorRecord anchor)
    {
        if (_portAnchorIndexById.TryGetValue(id, out int index))
        {
            anchor = _buildingPortAnchors[index];
            return true;
        }

        anchor = null;
        return false;
    }

    public bool TryGetLane(LaneId id, out LaneRecord lane)
    {
        if (_laneIndexById.TryGetValue(id, out int index))
        {
            lane = _lanes[index];
            return true;
        }

        lane = null;
        return false;
    }

    public bool TryGetGeometry(
        TrafficGeometryId id,
        out TrafficGeometryRecord geometry)
    {
        if (_geometryIndexById.TryGetValue(id, out int index))
        {
            geometry = _geometry[index];
            return true;
        }

        geometry = null;
        return false;
    }
}

internal static class TrafficGraphRecordCopy
{
    public static T[] Copy<T>(IReadOnlyList<T> source)
    {
        if (source == null || source.Count == 0) return Array.Empty<T>();

        var copy = new T[source.Count];
        for (int i = 0; i < source.Count; i++) copy[i] = source[i];
        return copy;
    }
}
