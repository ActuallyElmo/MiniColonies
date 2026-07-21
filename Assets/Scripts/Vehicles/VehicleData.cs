using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Serialization;

public abstract class VehicleData : ScriptableObject
{
    [Header("Traffic Profile")]
    [Tooltip("Persistent authoring key used to derive the compiled vehicle traffic profile ID. Falls back to vehicleName or asset name for existing assets.")]
    public string trafficProfileKey;
    public VehicleCapabilityMask trafficCapabilities = VehicleCapabilityMask.GroundRoad;
    public RoadPermissionMask trafficRoadPermissions = RoadPermissionMask.GeneralRoad;

    [Header("Base Vehicle Info")]
    public string vehicleName;
    public GameObject vehiclePrefab;
    public float maximumVehicleSpeed;
    public bool isLongVehicle;
    public bool isOffroadVehicle;

    [Header("Traffic Movement")]
    [FormerlySerializedAs("vehicleLengthMeters")]
    public float vehicleLengthUnits = 1.0f;
    [FormerlySerializedAs("minimumFollowingGapMeters")]
    public float minimumFollowingGapUnits = 0.5f;
    [FormerlySerializedAs("acceleration")]
    public float accelerationUnitsPerSecondSquared = 12f;
    [FormerlySerializedAs("deceleration")]
    public float decelerationUnitsPerSecondSquared = 15f;
    public float emergencyDecelerationUnitsPerSecondSquared = 30f;
    [Tooltip("Extra following time added on top of minimumFollowingGapUnits while moving. Lower values keep tiny vehicles closer together.")]
    public float desiredTimeHeadwaySeconds = 0.15f;
    [Tooltip("Delay before a queued vehicle reacts to the vehicle ahead accelerating from a stop.")]
    public float driverReactionTimeSeconds = 0.25f;
    public float maximumJerkUnitsPerSecondCubed = 30f;

    public bool TryCompileTrafficProfile(
        TrafficDiagnosticCollection diagnostics,
        out VehicleTrafficProfile profile)
    {
        return VehicleTrafficProfile.TryCompile(this, diagnostics, out profile);
    }
}
