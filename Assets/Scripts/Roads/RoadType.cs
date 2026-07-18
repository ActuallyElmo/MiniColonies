using UnityEngine;

[CreateAssetMenu(fileName = "New Road Type", menuName = "ColonySim/Road Type")]
public class RoadType : ScriptableObject
{
    [Header("Traffic Profile")]
    [Tooltip("Persistent authoring key used to derive the compiled road profile ID. Falls back to roadName for existing assets.")]
    public string trafficProfileKey;
    public RoadPermissionMask allowedTrafficPermissions = RoadPermissionMask.All;
    public VehicleCapabilityMask allowedVehicleCapabilities = VehicleCapabilityMask.All;
    public RoadMovementPolicyMask supportedTrafficMovements = RoadMovementPolicyMask.All;

    [Header("Gameplay Settings (Prep for Vehicles)")]
    public string roadName = "Standard Road";
    public int lanesPerWay = 1;
    public bool isTwoWay = true;
    public float speedLimit = 50f;

    [Header("Dimensions (Max 1 Grid Cell)")]
    [Range(0.1f, 0.9f)] public float roadWidth = 0.5f;
    [Range(0f, 0.3f)] public float curbWidth = 0.15f;

    [Header("Visuals")]
    public Material roadMaterial;
    public Material curbMaterial;

    public bool TryCompileTrafficProfile(
        TrafficDiagnosticCollection diagnostics,
        out RoadProfile profile)
    {
        return RoadProfile.TryCompile(this, diagnostics, out profile);
    }
}
