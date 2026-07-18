using System;

public static class TrafficStableHash
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static ulong FromNormalizedKey(string typeName, string sourceKey)
    {
        string normalizedType = Normalize(typeName);
        string normalizedKey = Normalize(sourceKey);

        ulong hash = OffsetBasis;
        AddString(ref hash, normalizedType);
        AddByte(ref hash, (byte)'|');
        AddString(ref hash, normalizedKey);
        return hash == 0UL ? 1UL : hash;
    }

    public static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
    }

    private static void AddString(ref ulong hash, string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            AddByte(ref hash, (byte)(character & 0xff));
            AddByte(ref hash, (byte)(character >> 8));
        }
    }

    private static void AddByte(ref ulong hash, byte value)
    {
        hash ^= value;
        hash *= Prime;
    }
}

public readonly struct TrafficGraphVersion :
    IEquatable<TrafficGraphVersion>,
    IComparable<TrafficGraphVersion>
{
    public static readonly TrafficGraphVersion Invalid = default;

    public int Value { get; }
    public bool IsValid => Value > 0;

    public TrafficGraphVersion(int value)
    {
        Value = value > 0 ? value : 0;
    }

    public TrafficGraphVersion Next()
    {
        if (Value == int.MaxValue)
        {
            throw new InvalidOperationException("Traffic graph version overflow.");
        }

        return new TrafficGraphVersion(IsValid ? Value + 1 : 1);
    }

    public int CompareTo(TrafficGraphVersion other) => Value.CompareTo(other.Value);
    public bool Equals(TrafficGraphVersion other) => Value == other.Value;
    public override bool Equals(object obj) => obj is TrafficGraphVersion other && Equals(other);
    public override int GetHashCode() => Value;
    public override string ToString() => IsValid ? $"TrafficGraphVersion({Value})" : "TrafficGraphVersion(Invalid)";

    public static bool operator ==(TrafficGraphVersion left, TrafficGraphVersion right) => left.Equals(right);
    public static bool operator !=(TrafficGraphVersion left, TrafficGraphVersion right) => !left.Equals(right);
    public static bool operator <(TrafficGraphVersion left, TrafficGraphVersion right) => left.Value < right.Value;
    public static bool operator >(TrafficGraphVersion left, TrafficGraphVersion right) => left.Value > right.Value;
    public static bool operator <=(TrafficGraphVersion left, TrafficGraphVersion right) => left.Value <= right.Value;
    public static bool operator >=(TrafficGraphVersion left, TrafficGraphVersion right) => left.Value >= right.Value;
}

public readonly struct RoadProfileId : IEquatable<RoadProfileId>, IComparable<RoadProfileId>
{
    public static readonly RoadProfileId Invalid = default;
    public ulong Value { get; }
    public bool IsValid => Value != 0UL;

    public RoadProfileId(ulong value) => Value = value;
    public static RoadProfileId FromStableKey(string key) =>
        new RoadProfileId(TrafficStableHash.FromNormalizedKey(nameof(RoadProfileId), key));

    public int CompareTo(RoadProfileId other) => Value.CompareTo(other.Value);
    public bool Equals(RoadProfileId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is RoadProfileId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid ? $"RoadProfileId(0x{Value:X16})" : "RoadProfileId(Invalid)";
    public static bool operator ==(RoadProfileId left, RoadProfileId right) => left.Equals(right);
    public static bool operator !=(RoadProfileId left, RoadProfileId right) => !left.Equals(right);
    public static bool operator <(RoadProfileId left, RoadProfileId right) => left.Value < right.Value;
    public static bool operator >(RoadProfileId left, RoadProfileId right) => left.Value > right.Value;
}

public readonly struct VehicleTrafficProfileId :
    IEquatable<VehicleTrafficProfileId>,
    IComparable<VehicleTrafficProfileId>
{
    public static readonly VehicleTrafficProfileId Invalid = default;
    public ulong Value { get; }
    public bool IsValid => Value != 0UL;

    public VehicleTrafficProfileId(ulong value) => Value = value;
    public static VehicleTrafficProfileId FromStableKey(string key) =>
        new VehicleTrafficProfileId(TrafficStableHash.FromNormalizedKey(nameof(VehicleTrafficProfileId), key));

    public int CompareTo(VehicleTrafficProfileId other) => Value.CompareTo(other.Value);
    public bool Equals(VehicleTrafficProfileId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is VehicleTrafficProfileId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid
        ? $"VehicleTrafficProfileId(0x{Value:X16})"
        : "VehicleTrafficProfileId(Invalid)";
    public static bool operator ==(VehicleTrafficProfileId left, VehicleTrafficProfileId right) => left.Equals(right);
    public static bool operator !=(VehicleTrafficProfileId left, VehicleTrafficProfileId right) => !left.Equals(right);
    public static bool operator <(VehicleTrafficProfileId left, VehicleTrafficProfileId right) => left.Value < right.Value;
    public static bool operator >(VehicleTrafficProfileId left, VehicleTrafficProfileId right) => left.Value > right.Value;
}

public readonly struct RoadSectionId : IEquatable<RoadSectionId>, IComparable<RoadSectionId>
{
    public static readonly RoadSectionId Invalid = default;
    public ulong Value { get; }
    public bool IsValid => Value != 0UL;

    public RoadSectionId(ulong value) => Value = value;
    public static RoadSectionId FromStableKey(string key) =>
        new RoadSectionId(TrafficStableHash.FromNormalizedKey(nameof(RoadSectionId), key));

    public int CompareTo(RoadSectionId other) => Value.CompareTo(other.Value);
    public bool Equals(RoadSectionId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is RoadSectionId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid ? $"RoadSectionId(0x{Value:X16})" : "RoadSectionId(Invalid)";
    public static bool operator ==(RoadSectionId left, RoadSectionId right) => left.Equals(right);
    public static bool operator !=(RoadSectionId left, RoadSectionId right) => !left.Equals(right);
    public static bool operator <(RoadSectionId left, RoadSectionId right) => left.Value < right.Value;
    public static bool operator >(RoadSectionId left, RoadSectionId right) => left.Value > right.Value;
}

public readonly struct LaneId : IEquatable<LaneId>, IComparable<LaneId>
{
    public static readonly LaneId Invalid = default;
    public ulong Value { get; }
    public bool IsValid => Value != 0UL;

    public LaneId(ulong value) => Value = value;
    public static LaneId FromStableKey(string key) =>
        new LaneId(TrafficStableHash.FromNormalizedKey(nameof(LaneId), key));

    public int CompareTo(LaneId other) => Value.CompareTo(other.Value);
    public bool Equals(LaneId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is LaneId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid ? $"LaneId(0x{Value:X16})" : "LaneId(Invalid)";
    public static bool operator ==(LaneId left, LaneId right) => left.Equals(right);
    public static bool operator !=(LaneId left, LaneId right) => !left.Equals(right);
    public static bool operator <(LaneId left, LaneId right) => left.Value < right.Value;
    public static bool operator >(LaneId left, LaneId right) => left.Value > right.Value;
}

public readonly struct MovementId : IEquatable<MovementId>, IComparable<MovementId>
{
    public static readonly MovementId Invalid = default;
    public ulong Value { get; }
    public bool IsValid => Value != 0UL;

    public MovementId(ulong value) => Value = value;
    public static MovementId FromStableKey(string key) =>
        new MovementId(TrafficStableHash.FromNormalizedKey(nameof(MovementId), key));

    public int CompareTo(MovementId other) => Value.CompareTo(other.Value);
    public bool Equals(MovementId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is MovementId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid ? $"MovementId(0x{Value:X16})" : "MovementId(Invalid)";
    public static bool operator ==(MovementId left, MovementId right) => left.Equals(right);
    public static bool operator !=(MovementId left, MovementId right) => !left.Equals(right);
    public static bool operator <(MovementId left, MovementId right) => left.Value < right.Value;
    public static bool operator >(MovementId left, MovementId right) => left.Value > right.Value;
}

public readonly struct ControlledNodeId : IEquatable<ControlledNodeId>, IComparable<ControlledNodeId>
{
    public static readonly ControlledNodeId Invalid = default;
    public ulong Value { get; }
    public bool IsValid => Value != 0UL;

    public ControlledNodeId(ulong value) => Value = value;
    public static ControlledNodeId FromStableKey(string key) =>
        new ControlledNodeId(TrafficStableHash.FromNormalizedKey(nameof(ControlledNodeId), key));

    public int CompareTo(ControlledNodeId other) => Value.CompareTo(other.Value);
    public bool Equals(ControlledNodeId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is ControlledNodeId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid
        ? $"ControlledNodeId(0x{Value:X16})"
        : "ControlledNodeId(Invalid)";
    public static bool operator ==(ControlledNodeId left, ControlledNodeId right) => left.Equals(right);
    public static bool operator !=(ControlledNodeId left, ControlledNodeId right) => !left.Equals(right);
    public static bool operator <(ControlledNodeId left, ControlledNodeId right) => left.Value < right.Value;
    public static bool operator >(ControlledNodeId left, ControlledNodeId right) => left.Value > right.Value;
}

public readonly struct BuildingPortAnchorId :
    IEquatable<BuildingPortAnchorId>,
    IComparable<BuildingPortAnchorId>
{
    public static readonly BuildingPortAnchorId Invalid = default;
    public ulong Value { get; }
    public bool IsValid => Value != 0UL;

    public BuildingPortAnchorId(ulong value) => Value = value;
    public static BuildingPortAnchorId FromStableKey(string key) =>
        new BuildingPortAnchorId(TrafficStableHash.FromNormalizedKey(nameof(BuildingPortAnchorId), key));

    public int CompareTo(BuildingPortAnchorId other) => Value.CompareTo(other.Value);
    public bool Equals(BuildingPortAnchorId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is BuildingPortAnchorId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid
        ? $"BuildingPortAnchorId(0x{Value:X16})"
        : "BuildingPortAnchorId(Invalid)";
    public static bool operator ==(BuildingPortAnchorId left, BuildingPortAnchorId right) => left.Equals(right);
    public static bool operator !=(BuildingPortAnchorId left, BuildingPortAnchorId right) => !left.Equals(right);
    public static bool operator <(BuildingPortAnchorId left, BuildingPortAnchorId right) => left.Value < right.Value;
    public static bool operator >(BuildingPortAnchorId left, BuildingPortAnchorId right) => left.Value > right.Value;
}

public readonly struct VehicleSimulationId :
    IEquatable<VehicleSimulationId>,
    IComparable<VehicleSimulationId>
{
    public static readonly VehicleSimulationId Invalid = default;
    public ulong Value { get; }
    public bool IsValid => Value != 0UL;

    public VehicleSimulationId(ulong value) => Value = value;
    public static VehicleSimulationId FromStableKey(string key) =>
        new VehicleSimulationId(TrafficStableHash.FromNormalizedKey(nameof(VehicleSimulationId), key));

    public int CompareTo(VehicleSimulationId other) => Value.CompareTo(other.Value);
    public bool Equals(VehicleSimulationId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is VehicleSimulationId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => IsValid
        ? $"VehicleSimulationId(0x{Value:X16})"
        : "VehicleSimulationId(Invalid)";
    public static bool operator ==(VehicleSimulationId left, VehicleSimulationId right) => left.Equals(right);
    public static bool operator !=(VehicleSimulationId left, VehicleSimulationId right) => !left.Equals(right);
    public static bool operator <(VehicleSimulationId left, VehicleSimulationId right) => left.Value < right.Value;
    public static bool operator >(VehicleSimulationId left, VehicleSimulationId right) => left.Value > right.Value;
}
