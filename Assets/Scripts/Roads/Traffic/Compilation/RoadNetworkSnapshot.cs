using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

public readonly struct RoadCellRecord : IEquatable<RoadCellRecord>
{
    public Vector2Int GridPosition { get; }
    public int ElevationLayer { get; }
    public int PhysicalConnections { get; }
    public int LegalOutgoingDirections { get; }
    public int LegalIncomingDirections { get; }
    public RoadProfileId RoadProfileId { get; }
    public RoadNodeKind NodeKind { get; }
    public Vector3 WorldCenter { get; }

    public RoadCellRecord(
        Vector2Int gridPosition,
        int elevationLayer,
        int physicalConnections,
        int legalOutgoingDirections,
        int legalIncomingDirections,
        RoadProfileId roadProfileId,
        RoadNodeKind nodeKind,
        Vector3 worldCenter)
    {
        GridPosition = gridPosition;
        ElevationLayer = elevationLayer;
        PhysicalConnections = physicalConnections;
        LegalOutgoingDirections = legalOutgoingDirections;
        LegalIncomingDirections = legalIncomingDirections;
        RoadProfileId = roadProfileId;
        NodeKind = nodeKind;
        WorldCenter = worldCenter;
    }

    public bool HasPhysicalConnection(int directionBit) =>
        (PhysicalConnections & directionBit) != 0;

    public bool CanExit(int directionBit) =>
        (LegalOutgoingDirections & directionBit) != 0;

    public bool CanEnter(int directionBit) =>
        (LegalIncomingDirections & directionBit) != 0;

    public bool Equals(RoadCellRecord other)
    {
        return GridPosition == other.GridPosition &&
               ElevationLayer == other.ElevationLayer &&
               PhysicalConnections == other.PhysicalConnections &&
               LegalOutgoingDirections == other.LegalOutgoingDirections &&
               LegalIncomingDirections == other.LegalIncomingDirections &&
               RoadProfileId == other.RoadProfileId &&
               NodeKind == other.NodeKind &&
               WorldCenter == other.WorldCenter;
    }

    public override bool Equals(object obj) => obj is RoadCellRecord other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = GridPosition.GetHashCode();
            hash = (hash * 397) ^ ElevationLayer;
            hash = (hash * 397) ^ PhysicalConnections;
            hash = (hash * 397) ^ LegalOutgoingDirections;
            hash = (hash * 397) ^ LegalIncomingDirections;
            hash = (hash * 397) ^ RoadProfileId.GetHashCode();
            hash = (hash * 397) ^ (int)NodeKind;
            hash = (hash * 397) ^ WorldCenter.GetHashCode();
            return hash;
        }
    }
}

public readonly struct BuildingPortRecord : IEquatable<BuildingPortRecord>
{
    public BuildingPortAnchorId Id { get; }
    public Vector2Int BuildingOriginCell { get; }
    public Vector2Int PortCell { get; }
    public PortType PortType { get; }

    public BuildingPortRecord(
        BuildingPortAnchorId id,
        Vector2Int buildingOriginCell,
        Vector2Int portCell,
        PortType portType)
    {
        Id = id;
        BuildingOriginCell = buildingOriginCell;
        PortCell = portCell;
        PortType = portType;
    }

    public bool Equals(BuildingPortRecord other)
    {
        return Id == other.Id &&
               BuildingOriginCell == other.BuildingOriginCell &&
               PortCell == other.PortCell &&
               PortType == other.PortType;
    }

    public override bool Equals(object obj) => obj is BuildingPortRecord other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
}

public readonly struct LaneConnectionRuleRecord : IEquatable<LaneConnectionRuleRecord>
{
    public int FromDirectionBit { get; }
    public int FromLaneIndex { get; }
    public int ToDirectionBit { get; }
    public int ToLaneIndex { get; }

    public LaneConnectionRuleRecord(
        int fromDirectionBit,
        int fromLaneIndex,
        int toDirectionBit,
        int toLaneIndex)
    {
        FromDirectionBit = fromDirectionBit;
        FromLaneIndex = fromLaneIndex;
        ToDirectionBit = toDirectionBit;
        ToLaneIndex = toLaneIndex;
    }

    public bool Equals(LaneConnectionRuleRecord other)
    {
        return FromDirectionBit == other.FromDirectionBit &&
               FromLaneIndex == other.FromLaneIndex &&
               ToDirectionBit == other.ToDirectionBit &&
               ToLaneIndex == other.ToLaneIndex;
    }

    public override bool Equals(object obj) => obj is LaneConnectionRuleRecord other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = FromDirectionBit;
            hash = (hash * 397) ^ FromLaneIndex;
            hash = (hash * 397) ^ ToDirectionBit;
            hash = (hash * 397) ^ ToLaneIndex;
            return hash;
        }
    }
}

public sealed class IntersectionPolicyRecord
{
    private readonly LaneConnectionRuleRecord[] _customRules;
    private readonly LaneConnectionRuleRecord[] _disabledRules;
    private readonly ReadOnlyCollection<LaneConnectionRuleRecord> _readOnlyCustomRules;
    private readonly ReadOnlyCollection<LaneConnectionRuleRecord> _readOnlyDisabledRules;

    public ControlledNodeId Id { get; }
    public Vector2Int GridPosition { get; }
    public RoadNodeKind NodeKind { get; }
    public IntersectionRuleType RuleType { get; }
    public int PriorityDirectionBitA { get; }
    public int PriorityDirectionBitB { get; }
    public float TrafficLightCycleSeconds { get; }
    public IReadOnlyList<LaneConnectionRuleRecord> CustomRules => _readOnlyCustomRules;
    public IReadOnlyList<LaneConnectionRuleRecord> DisabledRules => _readOnlyDisabledRules;

    public IntersectionPolicyRecord(
        ControlledNodeId id,
        Vector2Int gridPosition,
        RoadNodeKind nodeKind,
        IntersectionRuleType ruleType,
        int priorityDirectionBitA,
        int priorityDirectionBitB,
        float trafficLightCycleSeconds,
        IReadOnlyList<LaneConnectionRuleRecord> customRules,
        IReadOnlyList<LaneConnectionRuleRecord> disabledRules)
    {
        Id = id;
        GridPosition = gridPosition;
        NodeKind = nodeKind;
        RuleType = ruleType;
        PriorityDirectionBitA = priorityDirectionBitA;
        PriorityDirectionBitB = priorityDirectionBitB;
        TrafficLightCycleSeconds = trafficLightCycleSeconds;
        _customRules = CopyRules(customRules);
        _disabledRules = CopyRules(disabledRules);
        _readOnlyCustomRules = Array.AsReadOnly(_customRules);
        _readOnlyDisabledRules = Array.AsReadOnly(_disabledRules);
    }

    private static LaneConnectionRuleRecord[] CopyRules(
        IReadOnlyList<LaneConnectionRuleRecord> source)
    {
        if (source == null || source.Count == 0) return Array.Empty<LaneConnectionRuleRecord>();

        var copy = new LaneConnectionRuleRecord[source.Count];
        for (int i = 0; i < source.Count; i++) copy[i] = source[i];
        return copy;
    }
}

public sealed class RoadNetworkSnapshot
{
    private readonly RoadCellRecord[] _cells;
    private readonly RoadProfile[] _roadProfiles;
    private readonly BuildingPortRecord[] _buildingPorts;
    private readonly IntersectionPolicyRecord[] _intersectionPolicies;
    private readonly ReadOnlyCollection<RoadCellRecord> _readOnlyCells;
    private readonly ReadOnlyCollection<RoadProfile> _readOnlyRoadProfiles;
    private readonly ReadOnlyCollection<BuildingPortRecord> _readOnlyBuildingPorts;
    private readonly ReadOnlyCollection<IntersectionPolicyRecord> _readOnlyIntersectionPolicies;
    private readonly Dictionary<Vector2Int, int> _cellIndexByPosition;
    private readonly Dictionary<RoadProfileId, int> _profileIndexById;
    private readonly Dictionary<Vector2Int, int> _policyIndexByPosition;

    public int Revision => RoadAuthoringRevision;
    public int RoadAuthoringRevision { get; }
    public int BuildingAuthoringRevision { get; }
    public float CellSizeUnits { get; }
    public float HeightStepUnits { get; }
    public IReadOnlyList<RoadCellRecord> Cells => _readOnlyCells;
    public IReadOnlyList<RoadProfile> RoadProfiles => _readOnlyRoadProfiles;
    public IReadOnlyList<BuildingPortRecord> BuildingPorts => _readOnlyBuildingPorts;
    public IReadOnlyList<IntersectionPolicyRecord> IntersectionPolicies =>
        _readOnlyIntersectionPolicies;

    internal RoadNetworkSnapshot(
        int roadAuthoringRevision,
        int buildingAuthoringRevision,
        float cellSizeUnits,
        float heightStepUnits,
        IReadOnlyList<RoadCellRecord> cells,
        IReadOnlyList<RoadProfile> roadProfiles,
        IReadOnlyList<BuildingPortRecord> buildingPorts,
        IReadOnlyList<IntersectionPolicyRecord> intersectionPolicies)
    {
        RoadAuthoringRevision = roadAuthoringRevision;
        BuildingAuthoringRevision = buildingAuthoringRevision;
        CellSizeUnits = cellSizeUnits;
        HeightStepUnits = heightStepUnits;
        _cells = Copy(cells);
        _roadProfiles = Copy(roadProfiles);
        _buildingPorts = Copy(buildingPorts);
        _intersectionPolicies = Copy(intersectionPolicies);
        _readOnlyCells = Array.AsReadOnly(_cells);
        _readOnlyRoadProfiles = Array.AsReadOnly(_roadProfiles);
        _readOnlyBuildingPorts = Array.AsReadOnly(_buildingPorts);
        _readOnlyIntersectionPolicies = Array.AsReadOnly(_intersectionPolicies);

        _cellIndexByPosition = new Dictionary<Vector2Int, int>(_cells.Length);
        for (int i = 0; i < _cells.Length; i++) _cellIndexByPosition.Add(_cells[i].GridPosition, i);

        _profileIndexById = new Dictionary<RoadProfileId, int>(_roadProfiles.Length);
        for (int i = 0; i < _roadProfiles.Length; i++) _profileIndexById.Add(_roadProfiles[i].Id, i);

        _policyIndexByPosition = new Dictionary<Vector2Int, int>(_intersectionPolicies.Length);
        for (int i = 0; i < _intersectionPolicies.Length; i++)
        {
            _policyIndexByPosition.Add(_intersectionPolicies[i].GridPosition, i);
        }
    }

    /// <summary>
    /// Pure-data construction seam for deterministic compiler fixtures and
    /// non-scene tooling. Production capture remains owned by
    /// RoadNetworkSnapshotBuilder.
    /// </summary>
    public static RoadNetworkSnapshot CreateForCompilation(
        int roadAuthoringRevision,
        int buildingAuthoringRevision,
        float cellSizeUnits,
        float heightStepUnits,
        IReadOnlyList<RoadCellRecord> cells,
        IReadOnlyList<RoadProfile> roadProfiles,
        IReadOnlyList<BuildingPortRecord> buildingPorts = null,
        IReadOnlyList<IntersectionPolicyRecord> intersectionPolicies = null)
    {
        return new RoadNetworkSnapshot(
            roadAuthoringRevision,
            buildingAuthoringRevision,
            cellSizeUnits,
            heightStepUnits,
            cells,
            roadProfiles,
            buildingPorts,
            intersectionPolicies);
    }

    public bool TryGetCell(Vector2Int position, out RoadCellRecord cell)
    {
        if (_cellIndexByPosition.TryGetValue(position, out int index))
        {
            cell = _cells[index];
            return true;
        }

        cell = default;
        return false;
    }

    public bool TryGetRoadProfile(RoadProfileId id, out RoadProfile profile)
    {
        if (_profileIndexById.TryGetValue(id, out int index))
        {
            profile = _roadProfiles[index];
            return true;
        }

        profile = null;
        return false;
    }

    public bool TryGetIntersectionPolicy(
        Vector2Int position,
        out IntersectionPolicyRecord policy)
    {
        if (_policyIndexByPosition.TryGetValue(position, out int index))
        {
            policy = _intersectionPolicies[index];
            return true;
        }

        policy = null;
        return false;
    }

    public bool MatchesCurrentSources(
        RoadSystemBackend roadSystem,
        BuildingSystemBackend buildingSystem)
    {
        bool buildingRevisionMatches = buildingSystem != null
            ? buildingSystem.AuthoringRevision == BuildingAuthoringRevision
            : BuildingAuthoringRevision == 0;

        return roadSystem != null &&
               roadSystem.AuthoringRevision == RoadAuthoringRevision &&
               buildingRevisionMatches;
    }

    private static T[] Copy<T>(IReadOnlyList<T> source)
    {
        if (source == null || source.Count == 0) return Array.Empty<T>();

        var copy = new T[source.Count];
        for (int i = 0; i < source.Count; i++) copy[i] = source[i];
        return copy;
    }
}

public static class RoadGridDirectionUtility
{
    public static int GetDirectionBit(Vector2Int from, Vector2Int to)
    {
        int dx = to.x - from.x;
        int dy = to.y - from.y;
        if (dx == 0 && dy == 1) return 1;
        if (dx == 1 && dy == 1) return 2;
        if (dx == 1 && dy == 0) return 4;
        if (dx == 1 && dy == -1) return 8;
        if (dx == 0 && dy == -1) return 16;
        if (dx == -1 && dy == -1) return 32;
        if (dx == -1 && dy == 0) return 64;
        if (dx == -1 && dy == 1) return 128;
        return 0;
    }

    public static Vector2Int GetNeighborPosition(Vector2Int position, int directionBit)
    {
        switch (directionBit)
        {
            case 1: return new Vector2Int(position.x, position.y + 1);
            case 2: return new Vector2Int(position.x + 1, position.y + 1);
            case 4: return new Vector2Int(position.x + 1, position.y);
            case 8: return new Vector2Int(position.x + 1, position.y - 1);
            case 16: return new Vector2Int(position.x, position.y - 1);
            case 32: return new Vector2Int(position.x - 1, position.y - 1);
            case 64: return new Vector2Int(position.x - 1, position.y);
            case 128: return new Vector2Int(position.x - 1, position.y + 1);
            default: return position;
        }
    }
}
