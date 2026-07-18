using System;

[Flags]
public enum VehicleCapabilityMask
{
    None = 0,
    GroundRoad = 1 << 0,
    HeavyVehicle = 1 << 1,
    All = GroundRoad | HeavyVehicle
}

[Flags]
public enum RoadPermissionMask
{
    None = 0,
    GeneralRoad = 1 << 0,
    ServiceRoad = 1 << 1,
    All = GeneralRoad | ServiceRoad
}

[Flags]
public enum RoadMovementPolicyMask
{
    None = 0,
    LaneContinuation = 1 << 0,
    LaneChange = 1 << 1,
    Merge = 1 << 2,
    Intersection = 1 << 3,
    RoadEndUTurn = 1 << 4,
    BuildingPort = 1 << 5,
    All = LaneContinuation | LaneChange | Merge | Intersection | RoadEndUTurn | BuildingPort
}

public enum RoadFlowDirectionality
{
    OneWay,
    TwoWay
}

public enum LaneOrderingConvention
{
    InsideToOutside
}

public sealed class RoadProfile
{
    public RoadProfileId Id { get; }
    public string SourceKey { get; }
    public int ForwardLaneCount { get; }
    public int ReverseLaneCount { get; }
    public RoadFlowDirectionality Directionality { get; }
    public LaneOrderingConvention LaneOrdering { get; }
    public float SpeedLimitUnitsPerSecond { get; }
    public RoadPermissionMask AllowedPermissions { get; }
    public VehicleCapabilityMask AllowedCapabilities { get; }
    public float RoadWidthUnits { get; }
    public float CurbWidthUnits { get; }
    public RoadMovementPolicyMask SupportedMovements { get; }

    public int TotalLaneCount => ForwardLaneCount + ReverseLaneCount;

    public RoadProfile(
        RoadProfileId id,
        string sourceKey,
        int forwardLaneCount,
        int reverseLaneCount,
        RoadFlowDirectionality directionality,
        LaneOrderingConvention laneOrdering,
        float speedLimitUnitsPerSecond,
        RoadPermissionMask allowedPermissions,
        VehicleCapabilityMask allowedCapabilities,
        float roadWidthUnits,
        float curbWidthUnits,
        RoadMovementPolicyMask supportedMovements)
    {
        Id = id;
        SourceKey = sourceKey ?? string.Empty;
        ForwardLaneCount = forwardLaneCount;
        ReverseLaneCount = reverseLaneCount;
        Directionality = directionality;
        LaneOrdering = laneOrdering;
        SpeedLimitUnitsPerSecond = speedLimitUnitsPerSecond;
        AllowedPermissions = allowedPermissions;
        AllowedCapabilities = allowedCapabilities;
        RoadWidthUnits = roadWidthUnits;
        CurbWidthUnits = curbWidthUnits;
        SupportedMovements = supportedMovements;
    }

    public bool Validate(TrafficDiagnosticCollection diagnostics)
    {
        if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));
        int initialDiagnosticCount = diagnostics.Count;

        TrafficDiagnosticSource source = TrafficDiagnosticSource.ForProfile(SourceKey, Id.ToString());
        if (!Id.IsValid)
        {
            diagnostics.AddError(TrafficDiagnosticCode.MissingProfileId, "Road profile has no stable ID.", source);
        }

        if (ForwardLaneCount <= 0 ||
            ReverseLaneCount < 0 ||
            !Enum.IsDefined(typeof(RoadFlowDirectionality), Directionality) ||
            (Directionality == RoadFlowDirectionality.OneWay && ReverseLaneCount != 0) ||
            (Directionality == RoadFlowDirectionality.TwoWay && ReverseLaneCount <= 0))
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.InvalidLaneCount,
                "Road lane counts do not agree with the compiled directionality.",
                source);
        }

        if (!Enum.IsDefined(typeof(RoadFlowDirectionality), Directionality))
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.InvalidRoadDirectionality,
                "Road directionality is not a known compiled value.",
                source);
        }

        if (!Enum.IsDefined(typeof(LaneOrderingConvention), LaneOrdering))
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.InvalidLaneOrdering,
                "Lane ordering convention is not a known compiled value.",
                source);
        }

        if (!IsPositiveFinite(SpeedLimitUnitsPerSecond))
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.InvalidSpeedLimit,
                "Road speed limit must be a positive finite value in world units per second.",
                source);
        }

        if (!IsPositiveFinite(RoadWidthUnits))
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.InvalidRoadWidth,
                "Road width must be a positive finite value in world units.",
                source);
        }

        if (!IsNonNegativeFinite(CurbWidthUnits))
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.InvalidCurbWidth,
                "Curb width must be a non-negative finite value in world units.",
                source);
        }

        if (AllowedPermissions == RoadPermissionMask.None ||
            (AllowedPermissions & ~RoadPermissionMask.All) != 0)
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.InvalidPermissionMask,
                "Road profile must allow at least one known permission.",
                source);
        }

        if (AllowedCapabilities == VehicleCapabilityMask.None ||
            (AllowedCapabilities & ~VehicleCapabilityMask.All) != 0)
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.InvalidCapabilityMask,
                "Road profile must allow at least one known vehicle capability.",
                source);
        }

        if (SupportedMovements == RoadMovementPolicyMask.None ||
            (SupportedMovements & ~RoadMovementPolicyMask.All) != 0)
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.InvalidMovementPolicy,
                "Road profile must support at least one known traffic movement.",
                source);
        }

        return !HasErrorsSince(diagnostics, initialDiagnosticCount);
    }

    public static bool TryCompile(
        RoadType sourceAsset,
        TrafficDiagnosticCollection diagnostics,
        out RoadProfile profile)
    {
        if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));
        profile = null;

        if (sourceAsset == null)
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.MissingProfileId,
                "Cannot compile a null RoadType asset.");
            return false;
        }

        bool hasPersistentKey = !string.IsNullOrWhiteSpace(sourceAsset.trafficProfileKey);
        string sourceKey = ResolveSourceKey(
            sourceAsset.trafficProfileKey,
            sourceAsset.roadName,
            sourceAsset.name,
            FormattableString.Invariant(
                $"ROAD:{sourceAsset.lanesPerWay}:{sourceAsset.isTwoWay}:{sourceAsset.speedLimit:R}:{sourceAsset.roadWidth:R}:{sourceAsset.curbWidth:R}:{(int)sourceAsset.allowedTrafficPermissions}:{(int)sourceAsset.allowedVehicleCapabilities}:{(int)sourceAsset.supportedTrafficMovements}"));
        TrafficDiagnosticSource source = TrafficDiagnosticSource.ForProfile(sourceKey);
        if (!hasPersistentKey)
        {
            diagnostics.AddWarning(
                TrafficDiagnosticCode.MissingPersistentProfileKey,
                "RoadType has no persistent traffic profile key; a deterministic compatibility key was derived from traffic data.",
                source);
        }

        RoadPermissionMask allowedPermissions = sourceAsset.allowedTrafficPermissions;
        if (allowedPermissions == RoadPermissionMask.None)
        {
            diagnostics.AddWarning(
                TrafficDiagnosticCode.CompatibilityDefaultApplied,
                $"{nameof(sourceAsset.allowedTrafficPermissions)} used compatibility default {RoadPermissionMask.All}.",
                source);
            allowedPermissions = RoadPermissionMask.All;
        }

        VehicleCapabilityMask allowedCapabilities = sourceAsset.allowedVehicleCapabilities;
        if (allowedCapabilities == VehicleCapabilityMask.None)
        {
            diagnostics.AddWarning(
                TrafficDiagnosticCode.CompatibilityDefaultApplied,
                $"{nameof(sourceAsset.allowedVehicleCapabilities)} used compatibility default {VehicleCapabilityMask.All}.",
                source);
            allowedCapabilities = VehicleCapabilityMask.All;
        }

        RoadMovementPolicyMask supportedMovements = sourceAsset.supportedTrafficMovements;
        if (supportedMovements == RoadMovementPolicyMask.None)
        {
            diagnostics.AddWarning(
                TrafficDiagnosticCode.CompatibilityDefaultApplied,
                $"{nameof(sourceAsset.supportedTrafficMovements)} used compatibility default {RoadMovementPolicyMask.All}.",
                source);
            supportedMovements = RoadMovementPolicyMask.All;
        }

        profile = new RoadProfile(
            RoadProfileId.FromStableKey(sourceKey),
            sourceKey,
            sourceAsset.lanesPerWay,
            sourceAsset.isTwoWay ? sourceAsset.lanesPerWay : 0,
            sourceAsset.isTwoWay ? RoadFlowDirectionality.TwoWay : RoadFlowDirectionality.OneWay,
            LaneOrderingConvention.InsideToOutside,
            sourceAsset.speedLimit,
            allowedPermissions,
            allowedCapabilities,
            sourceAsset.roadWidth,
            sourceAsset.curbWidth,
            supportedMovements);

        return profile.Validate(diagnostics);
    }

    internal static string ResolveSourceKey(
        string explicitKey,
        string displayName,
        string assetName,
        string deterministicFallback)
    {
        if (!string.IsNullOrWhiteSpace(explicitKey)) return explicitKey.Trim();
        if (!string.IsNullOrWhiteSpace(deterministicFallback)) return deterministicFallback.Trim();
        if (!string.IsNullOrWhiteSpace(displayName)) return displayName.Trim();
        if (!string.IsNullOrWhiteSpace(assetName)) return assetName.Trim();
        return string.Empty;
    }

    private static bool HasErrorsSince(
        TrafficDiagnosticCollection diagnostics,
        int initialDiagnosticCount)
    {
        for (int i = initialDiagnosticCount; i < diagnostics.Count; i++)
        {
            if (diagnostics[i].Severity == TrafficDiagnosticSeverity.Error) return true;
        }

        return false;
    }

    private static bool IsPositiveFinite(float value) =>
        value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsNonNegativeFinite(float value) =>
        value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
}
