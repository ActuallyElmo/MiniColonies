using System;

public sealed class VehicleTrafficProfile
{
    public const float CompatibilityMaximumSpeedUnitsPerSecond = 1f;
    public const float CompatibilityVehicleLengthUnits = 1f;
    public const float CompatibilityVehicleWidthUnits = 0.2f;
    public const float CompatibilityAccelerationUnitsPerSecondSquared = 12f;
    public const float CompatibilityServiceDecelerationUnitsPerSecondSquared = 15f;
    public const float CompatibilityEmergencyDecelerationUnitsPerSecondSquared = 30f;
    public const float CompatibilityTimeHeadwaySeconds = 0.15f;
    public const float CompatibilityDriverReactionTimeSeconds = 0.25f;
    public const float CompatibilityStandstillGapUnits = 0.5f;
    public const float CompatibilityMaximumJerkUnitsPerSecondCubed = 30f;

    public VehicleTrafficProfileId Id { get; }
    public string SourceKey { get; }
    public float LengthUnits { get; }
    public float WidthUnits { get; }
    public float DesiredSpeedUnitsPerSecond { get; }
    public float MaximumSpeedUnitsPerSecond { get; }
    public float ComfortableAccelerationUnitsPerSecondSquared { get; }
    public float ComfortableServiceDecelerationUnitsPerSecondSquared { get; }
    public float EmergencyDecelerationUnitsPerSecondSquared { get; }
    public float DesiredTimeHeadwaySeconds { get; }
    public float DriverReactionTimeSeconds { get; }
    public float MinimumStandstillGapUnits { get; }
    public float MaximumJerkUnitsPerSecondCubed { get; }
    public VehicleCapabilityMask Capabilities { get; }
    public RoadPermissionMask RoadPermissions { get; }
    public uint DeterministicBehaviorSeed { get; }

    public VehicleTrafficProfile(
        VehicleTrafficProfileId id,
        string sourceKey,
        float lengthUnits,
        float widthUnits,
        float desiredSpeedUnitsPerSecond,
        float maximumSpeedUnitsPerSecond,
        float comfortableAccelerationUnitsPerSecondSquared,
        float comfortableServiceDecelerationUnitsPerSecondSquared,
        float emergencyDecelerationUnitsPerSecondSquared,
        float desiredTimeHeadwaySeconds,
        float driverReactionTimeSeconds,
        float minimumStandstillGapUnits,
        float maximumJerkUnitsPerSecondCubed,
        VehicleCapabilityMask capabilities,
        RoadPermissionMask roadPermissions,
        uint deterministicBehaviorSeed = 0)
    {
        Id = id;
        SourceKey = sourceKey ?? string.Empty;
        LengthUnits = lengthUnits;
        WidthUnits = widthUnits;
        DesiredSpeedUnitsPerSecond = desiredSpeedUnitsPerSecond;
        MaximumSpeedUnitsPerSecond = maximumSpeedUnitsPerSecond;
        ComfortableAccelerationUnitsPerSecondSquared = comfortableAccelerationUnitsPerSecondSquared;
        ComfortableServiceDecelerationUnitsPerSecondSquared =
            comfortableServiceDecelerationUnitsPerSecondSquared;
        EmergencyDecelerationUnitsPerSecondSquared = emergencyDecelerationUnitsPerSecondSquared;
        DesiredTimeHeadwaySeconds = desiredTimeHeadwaySeconds;
        DriverReactionTimeSeconds = driverReactionTimeSeconds;
        MinimumStandstillGapUnits = minimumStandstillGapUnits;
        MaximumJerkUnitsPerSecondCubed = maximumJerkUnitsPerSecondCubed;
        Capabilities = capabilities;
        RoadPermissions = roadPermissions;
        DeterministicBehaviorSeed = deterministicBehaviorSeed;
    }

    public bool Validate(TrafficDiagnosticCollection diagnostics)
    {
        if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));
        int initialDiagnosticCount = diagnostics.Count;

        TrafficDiagnosticSource source = TrafficDiagnosticSource.ForProfile(SourceKey, Id.ToString());
        if (!Id.IsValid)
        {
            diagnostics.AddError(TrafficDiagnosticCode.MissingProfileId, "Vehicle profile has no stable ID.", source);
        }

        ValidatePositive(
            LengthUnits,
            TrafficDiagnosticCode.InvalidVehicleLength,
            "Vehicle length must be a positive finite value in world units.",
            source,
            diagnostics);
        ValidatePositive(
            WidthUnits,
            TrafficDiagnosticCode.InvalidVehicleWidth,
            "Vehicle width must be a positive finite value in world units.",
            source,
            diagnostics);
        ValidatePositive(
            DesiredSpeedUnitsPerSecond,
            TrafficDiagnosticCode.InvalidVehicleSpeed,
            "Desired speed must be a positive finite value in world units per second.",
            source,
            diagnostics);
        ValidatePositive(
            MaximumSpeedUnitsPerSecond,
            TrafficDiagnosticCode.InvalidVehicleSpeed,
            "Maximum speed must be a positive finite value in world units per second.",
            source,
            diagnostics);

        if (IsPositiveFinite(DesiredSpeedUnitsPerSecond) &&
            IsPositiveFinite(MaximumSpeedUnitsPerSecond) &&
            DesiredSpeedUnitsPerSecond > MaximumSpeedUnitsPerSecond)
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.InvalidVehicleSpeed,
                "Desired speed cannot exceed maximum speed.",
                source);
        }

        ValidatePositive(
            ComfortableAccelerationUnitsPerSecondSquared,
            TrafficDiagnosticCode.InvalidAcceleration,
            "Comfortable acceleration must be a positive finite value in world units per second squared.",
            source,
            diagnostics);
        ValidatePositive(
            ComfortableServiceDecelerationUnitsPerSecondSquared,
            TrafficDiagnosticCode.InvalidServiceDeceleration,
            "Service deceleration must be a positive finite value in world units per second squared.",
            source,
            diagnostics);
        ValidatePositive(
            EmergencyDecelerationUnitsPerSecondSquared,
            TrafficDiagnosticCode.InvalidEmergencyDeceleration,
            "Emergency deceleration must be a positive finite value in world units per second squared.",
            source,
            diagnostics);

        if (IsPositiveFinite(ComfortableServiceDecelerationUnitsPerSecondSquared) &&
            IsPositiveFinite(EmergencyDecelerationUnitsPerSecondSquared) &&
            EmergencyDecelerationUnitsPerSecondSquared < ComfortableServiceDecelerationUnitsPerSecondSquared)
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.InvalidEmergencyDeceleration,
                "Emergency deceleration cannot be weaker than comfortable service deceleration.",
                source);
        }

        ValidatePositive(
            DesiredTimeHeadwaySeconds,
            TrafficDiagnosticCode.InvalidTimeHeadway,
            "Desired time headway must be a positive finite value in seconds.",
            source,
            diagnostics);
        ValidateNonNegative(
            DriverReactionTimeSeconds,
            TrafficDiagnosticCode.InvalidDriverReactionTime,
            "Driver reaction time must be a non-negative finite value in seconds.",
            source,
            diagnostics);
        ValidateNonNegative(
            MinimumStandstillGapUnits,
            TrafficDiagnosticCode.InvalidStandstillGap,
            "Minimum standstill gap must be a non-negative finite value in world units.",
            source,
            diagnostics);
        ValidatePositive(
            MaximumJerkUnitsPerSecondCubed,
            TrafficDiagnosticCode.InvalidJerk,
            "Maximum jerk must be a positive finite value in world units per second cubed.",
            source,
            diagnostics);

        if (Capabilities == VehicleCapabilityMask.None ||
            (Capabilities & ~VehicleCapabilityMask.All) != 0)
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.InvalidCapabilityMask,
                "Vehicle profile must contain at least one known capability.",
                source);
        }

        if (RoadPermissions == RoadPermissionMask.None ||
            (RoadPermissions & ~RoadPermissionMask.All) != 0)
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.InvalidPermissionMask,
                "Vehicle profile must contain at least one known road permission.",
                source);
        }

        return !HasErrorsSince(diagnostics, initialDiagnosticCount);
    }

    public static bool TryCompile(
        VehicleData sourceAsset,
        TrafficDiagnosticCollection diagnostics,
        out VehicleTrafficProfile profile)
    {
        if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));
        profile = null;

        if (sourceAsset == null)
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.MissingProfileId,
                "Cannot compile a null VehicleData asset.");
            return false;
        }

        bool hasPersistentKey = !string.IsNullOrWhiteSpace(sourceAsset.trafficProfileKey);
        string sourceKey = RoadProfile.ResolveSourceKey(
            sourceAsset.trafficProfileKey,
            sourceAsset.vehicleName,
            sourceAsset.name,
            FormattableString.Invariant(
                $"GROUND_VEHICLE:{sourceAsset.maximumVehicleSpeed:R}:{sourceAsset.isLongVehicle}:{sourceAsset.isOffroadVehicle}:{sourceAsset.vehicleLengthUnits:R}:{sourceAsset.minimumFollowingGapUnits:R}:{sourceAsset.accelerationUnitsPerSecondSquared:R}:{sourceAsset.decelerationUnitsPerSecondSquared:R}:{sourceAsset.emergencyDecelerationUnitsPerSecondSquared:R}:{sourceAsset.desiredTimeHeadwaySeconds:R}:{sourceAsset.driverReactionTimeSeconds:R}:{sourceAsset.maximumJerkUnitsPerSecondCubed:R}:{(int)sourceAsset.trafficCapabilities}:{(int)sourceAsset.trafficRoadPermissions}"));
        TrafficDiagnosticSource source = TrafficDiagnosticSource.ForProfile(sourceKey);
        if (!hasPersistentKey)
        {
            diagnostics.AddWarning(
                TrafficDiagnosticCode.MissingPersistentProfileKey,
                "VehicleData has no persistent traffic profile key; a deterministic compatibility key was derived from traffic data.",
                source);
        }

        float maximumSpeed = ApplyPositiveCompatibilityDefault(
            sourceAsset.maximumVehicleSpeed,
            CompatibilityMaximumSpeedUnitsPerSecond,
            nameof(sourceAsset.maximumVehicleSpeed),
            diagnostics,
            source);
        float length = ApplyPositiveCompatibilityDefault(
            sourceAsset.vehicleLengthUnits,
            sourceAsset.isLongVehicle ? 2.5f : CompatibilityVehicleLengthUnits,
            nameof(sourceAsset.vehicleLengthUnits),
            diagnostics,
            source);
        float acceleration = ApplyPositiveCompatibilityDefault(
            sourceAsset.accelerationUnitsPerSecondSquared,
            CompatibilityAccelerationUnitsPerSecondSquared,
            nameof(sourceAsset.accelerationUnitsPerSecondSquared),
            diagnostics,
            source);
        float serviceDeceleration = ApplyPositiveCompatibilityDefault(
            sourceAsset.decelerationUnitsPerSecondSquared,
            CompatibilityServiceDecelerationUnitsPerSecondSquared,
            nameof(sourceAsset.decelerationUnitsPerSecondSquared),
            diagnostics,
            source);
        float emergencyDeceleration = ApplyPositiveCompatibilityDefault(
            sourceAsset.emergencyDecelerationUnitsPerSecondSquared,
            Math.Max(CompatibilityEmergencyDecelerationUnitsPerSecondSquared, serviceDeceleration),
            nameof(sourceAsset.emergencyDecelerationUnitsPerSecondSquared),
            diagnostics,
            source);
        float timeHeadway = ApplyPositiveCompatibilityDefault(
            sourceAsset.desiredTimeHeadwaySeconds,
            CompatibilityTimeHeadwaySeconds,
            nameof(sourceAsset.desiredTimeHeadwaySeconds),
            diagnostics,
            source);
        float driverReactionTime = ApplyNonNegativeCompatibilityDefault(
            sourceAsset.driverReactionTimeSeconds,
            CompatibilityDriverReactionTimeSeconds,
            nameof(sourceAsset.driverReactionTimeSeconds),
            diagnostics,
            source);
        float maximumJerk = ApplyPositiveCompatibilityDefault(
            sourceAsset.maximumJerkUnitsPerSecondCubed,
            CompatibilityMaximumJerkUnitsPerSecondCubed,
            nameof(sourceAsset.maximumJerkUnitsPerSecondCubed),
            diagnostics,
            source);

        RoadPermissionMask permissions = sourceAsset.trafficRoadPermissions;
        VehicleCapabilityMask capabilities = sourceAsset.trafficCapabilities;
        if (capabilities == VehicleCapabilityMask.None)
        {
            diagnostics.AddWarning(
                TrafficDiagnosticCode.CompatibilityDefaultApplied,
                $"{nameof(sourceAsset.trafficCapabilities)} used compatibility default {VehicleCapabilityMask.GroundRoad}.",
                source);
            capabilities = VehicleCapabilityMask.GroundRoad;
        }

        if (permissions == RoadPermissionMask.None)
        {
            diagnostics.AddWarning(
                TrafficDiagnosticCode.CompatibilityDefaultApplied,
                $"{nameof(sourceAsset.trafficRoadPermissions)} used compatibility default {RoadPermissionMask.GeneralRoad}.",
                source);
            permissions = RoadPermissionMask.GeneralRoad;
        }

        profile = new VehicleTrafficProfile(
            VehicleTrafficProfileId.FromStableKey(sourceKey),
            sourceKey,
            length,
            CompatibilityVehicleWidthUnits,
            maximumSpeed,
            maximumSpeed,
            acceleration,
            serviceDeceleration,
            emergencyDeceleration,
            timeHeadway,
            driverReactionTime,
            sourceAsset.minimumFollowingGapUnits,
            maximumJerk,
            capabilities,
            permissions,
            (uint)(VehicleTrafficProfileId.FromStableKey(sourceKey).Value & uint.MaxValue));

        return profile.Validate(diagnostics);
    }

    private static float ApplyPositiveCompatibilityDefault(
        float value,
        float fallback,
        string fieldName,
        TrafficDiagnosticCollection diagnostics,
        TrafficDiagnosticSource source)
    {
        if (value > 0f && !float.IsNaN(value) && !float.IsInfinity(value)) return value;
        if (value < 0f || float.IsNaN(value) || float.IsInfinity(value)) return value;

        diagnostics.AddWarning(
            TrafficDiagnosticCode.CompatibilityDefaultApplied,
            $"{fieldName} used compatibility default {fallback:R}.",
            source);
        return fallback;
    }

    private static float ApplyNonNegativeCompatibilityDefault(
        float value,
        float fallback,
        string fieldName,
        TrafficDiagnosticCollection diagnostics,
        TrafficDiagnosticSource source)
    {
        if (value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value)) return value;
        if (value < 0f || float.IsNaN(value) || float.IsInfinity(value)) return value;

        diagnostics.AddWarning(
            TrafficDiagnosticCode.CompatibilityDefaultApplied,
            $"{fieldName} used compatibility default {fallback:R}.",
            source);
        return fallback;
    }

    private static void ValidatePositive(
        float value,
        TrafficDiagnosticCode code,
        string message,
        TrafficDiagnosticSource source,
        TrafficDiagnosticCollection diagnostics)
    {
        if (!IsPositiveFinite(value)) diagnostics.AddError(code, message, source);
    }

    private static void ValidateNonNegative(
        float value,
        TrafficDiagnosticCode code,
        string message,
        TrafficDiagnosticSource source,
        TrafficDiagnosticCollection diagnostics)
    {
        if (value < 0f || float.IsNaN(value) || float.IsInfinity(value))
        {
            diagnostics.AddError(code, message, source);
        }
    }

    private static bool IsPositiveFinite(float value) =>
        value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

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
}
