using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

public enum TrafficDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public enum TrafficDiagnosticCode
{
    // Values are a persisted diagnostics contract. Add new codes only at the end.
    None = 0,
    CompatibilityDefaultApplied = 1,
    InvalidStableId = 2,
    MissingProfileId = 3,
    InvalidLaneCount = 4,
    InvalidRoadDirectionality = 5,
    InvalidLaneOrdering = 6,
    InvalidSpeedLimit = 7,
    InvalidRoadWidth = 8,
    InvalidCurbWidth = 9,
    InvalidVehicleLength = 10,
    InvalidVehicleWidth = 11,
    InvalidVehicleSpeed = 12,
    InvalidAcceleration = 13,
    InvalidServiceDeceleration = 14,
    InvalidEmergencyDeceleration = 15,
    InvalidTimeHeadway = 16,
    InvalidStandstillGap = 17,
    InvalidJerk = 18,
    InvalidCapabilityMask = 19,
    InvalidPermissionMask = 20,
    InvalidMovementPolicy = 21,
    MissingRoadSystem = 22,
    MissingWorldGeometry = 23,
    MissingRoadProfile = 24,
    DuplicateStableId = 25,
    MissingRoadNeighbor = 26,
    SnapshotSourceChanged = 27,
    MissingLaneReference = 28,
    IllegalDirectionMovement = 29,
    UnmappedIncomingLane = 30,
    InvalidTransitionOwner = 31,
    InvalidLegDirection = 32,
    UnreachableBuildingPort = 33,
    ReservationConflict = 34,
    TacticalPlanUnavailable = 35,
    EmergencySafetyClamp = 36,
    GraphVersionMismatch = 37,
    MissingPersistentProfileKey = 38,
    CompilerStageFailed = 39,
    SnapshotAdapterComparison = 40,
    IllegalProfileCombination = 41
}

public readonly struct TrafficDiagnosticSource : IEquatable<TrafficDiagnosticSource>
{
    public static readonly TrafficDiagnosticSource None = default;

    public TrafficGraphVersion GraphVersion { get; }
    public string StableId { get; }
    public string ProfileKey { get; }
    public Vector2Int SourceCell { get; }
    public bool HasSourceCell { get; }
    public VehicleSimulationId VehicleId { get; }

    public TrafficDiagnosticSource(
        TrafficGraphVersion graphVersion,
        string stableId,
        string profileKey,
        Vector2Int sourceCell,
        bool hasSourceCell,
        VehicleSimulationId vehicleId)
    {
        GraphVersion = graphVersion;
        StableId = stableId ?? string.Empty;
        ProfileKey = profileKey ?? string.Empty;
        SourceCell = sourceCell;
        HasSourceCell = hasSourceCell;
        VehicleId = vehicleId;
    }

    public static TrafficDiagnosticSource ForProfile(string profileKey, string stableId = null)
    {
        return new TrafficDiagnosticSource(
            TrafficGraphVersion.Invalid,
            stableId,
            profileKey,
            Vector2Int.zero,
            false,
            VehicleSimulationId.Invalid);
    }

    public static TrafficDiagnosticSource ForCell(
        TrafficGraphVersion graphVersion,
        Vector2Int sourceCell,
        string stableId = null)
    {
        return new TrafficDiagnosticSource(
            graphVersion,
            stableId,
            string.Empty,
            sourceCell,
            true,
            VehicleSimulationId.Invalid);
    }

    public bool Equals(TrafficDiagnosticSource other)
    {
        return GraphVersion == other.GraphVersion &&
               string.Equals(StableId, other.StableId, StringComparison.Ordinal) &&
               string.Equals(ProfileKey, other.ProfileKey, StringComparison.Ordinal) &&
               SourceCell == other.SourceCell &&
               HasSourceCell == other.HasSourceCell &&
               VehicleId == other.VehicleId;
    }

    public override bool Equals(object obj) => obj is TrafficDiagnosticSource other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = GraphVersion.GetHashCode();
            hash = (hash * 397) ^ (StableId != null ? StableId.GetHashCode() : 0);
            hash = (hash * 397) ^ (ProfileKey != null ? ProfileKey.GetHashCode() : 0);
            hash = (hash * 397) ^ SourceCell.GetHashCode();
            hash = (hash * 397) ^ HasSourceCell.GetHashCode();
            hash = (hash * 397) ^ VehicleId.GetHashCode();
            return hash;
        }
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (GraphVersion.IsValid) parts.Add(GraphVersion.ToString());
        if (!string.IsNullOrEmpty(StableId)) parts.Add(StableId);
        if (!string.IsNullOrEmpty(ProfileKey)) parts.Add($"Profile={ProfileKey}");
        if (HasSourceCell) parts.Add($"Cell={SourceCell}");
        if (VehicleId.IsValid) parts.Add(VehicleId.ToString());
        return parts.Count == 0 ? "Unspecified source" : string.Join(", ", parts);
    }

    public static bool operator ==(TrafficDiagnosticSource left, TrafficDiagnosticSource right) => left.Equals(right);
    public static bool operator !=(TrafficDiagnosticSource left, TrafficDiagnosticSource right) => !left.Equals(right);
}

public sealed class TrafficDiagnostic
{
    public TrafficDiagnosticSeverity Severity { get; }
    public TrafficDiagnosticCode Code { get; }
    public string Message { get; }
    public TrafficDiagnosticSource Source { get; }

    public TrafficDiagnostic(
        TrafficDiagnosticSeverity severity,
        TrafficDiagnosticCode code,
        string message,
        TrafficDiagnosticSource source = default)
    {
        Severity = severity;
        Code = code;
        Message = message ?? string.Empty;
        Source = source;
    }

    public override string ToString()
    {
        return $"[{Severity}] {Code}: {Message} ({Source})";
    }
}

public sealed class TrafficDiagnosticCollection : IReadOnlyList<TrafficDiagnostic>
{
    private readonly List<TrafficDiagnostic> _items = new List<TrafficDiagnostic>();
    private readonly ReadOnlyCollection<TrafficDiagnostic> _readOnlyItems;

    public TrafficDiagnosticCollection()
    {
        _readOnlyItems = _items.AsReadOnly();
    }

    public int Count => _items.Count;
    public TrafficDiagnostic this[int index] => _items[index];
    public IReadOnlyList<TrafficDiagnostic> Items => _readOnlyItems;
    public bool HasErrors => ContainsSeverity(TrafficDiagnosticSeverity.Error);

    public void Add(TrafficDiagnostic diagnostic)
    {
        if (diagnostic == null) throw new ArgumentNullException(nameof(diagnostic));
        _items.Add(diagnostic);
    }

    public void Add(
        TrafficDiagnosticSeverity severity,
        TrafficDiagnosticCode code,
        string message,
        TrafficDiagnosticSource source = default)
    {
        Add(new TrafficDiagnostic(severity, code, message, source));
    }

    public void AddInfo(
        TrafficDiagnosticCode code,
        string message,
        TrafficDiagnosticSource source = default)
    {
        Add(TrafficDiagnosticSeverity.Info, code, message, source);
    }

    public void AddWarning(
        TrafficDiagnosticCode code,
        string message,
        TrafficDiagnosticSource source = default)
    {
        Add(TrafficDiagnosticSeverity.Warning, code, message, source);
    }

    public void AddError(
        TrafficDiagnosticCode code,
        string message,
        TrafficDiagnosticSource source = default)
    {
        Add(TrafficDiagnosticSeverity.Error, code, message, source);
    }

    public bool Contains(TrafficDiagnosticCode code)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Code == code) return true;
        }

        return false;
    }

    public int CountByCode(TrafficDiagnosticCode code)
    {
        int count = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Code == code) count++;
        }

        return count;
    }

    public bool ContainsSeverity(TrafficDiagnosticSeverity severity)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Severity == severity) return true;
        }

        return false;
    }

    public IEnumerator<TrafficDiagnostic> GetEnumerator() => _readOnlyItems.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
