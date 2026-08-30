using UnityEngine;

[CreateAssetMenu(fileName = "New Road Type", menuName = "ColonySim/Road Type")]
public class RoadType : ScriptableObject
{
    public const bool OneWayRoadsEnabled = false;

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

    [Tooltip("Uses larger-radius road nodes for corners, transitions, and intersections. Enable this for multi-lane highways and other roads that must not make tight 90-degree turns.")]
    public bool IsWide;

    [Tooltip("Allows this road type to be placed or selected as an upgrade on a building port cell.")]
    public bool CanConnectToPort = true;

    [Header("Dimensions (Max 1 Grid Cell)")]
    [Range(0.1f, 0.9f)] public float roadWidth = 0.5f;
    [Range(0f, 0.3f)] public float curbWidth = 0.15f;

    [Header("Visuals")]
    public Material roadMaterial;
    public Material curbMaterial;

    [Header("Road Markings")]
    [Tooltip("Material used for center dividers, lane dividers, and stop lines. Falls back to the curb material when empty.")]
    public Material roadMarkingMaterial;
    public bool showCenterDivider;
    [Range(0.005f, 0.08f)] public float centerDividerWidth = 0.025f;
    public bool showLaneDividers;
    [Range(0.005f, 0.05f)] public float laneDividerWidth = 0.012f;
    [Range(0.05f, 0.5f)] public float laneDividerDashLength = 0.18f;
    [Range(0.05f, 0.5f)] public float laneDividerGapLength = 0.14f;
    public bool showStopLinesAtIntersections;
    [Range(0.01f, 0.12f)] public float stopLineWidth = 0.035f;
    [Range(0.001f, 0.15f)] public float markingSurfaceOffset = 0.05f;

    public bool UsesOneWayTraffic => OneWayRoadsEnabled && !isTwoWay;
    public bool UsesTwoWayTraffic => !UsesOneWayTraffic;

    public bool TryCompileTrafficProfile(
        TrafficDiagnosticCollection diagnostics,
        out RoadProfile profile)
    {
        return RoadProfile.TryCompile(this, diagnostics, out profile);
    }
}

public static class RoadGeometryRules
{
    public const float StandardNodePullbackFraction = 0.35f;
    public const float WideNodePullbackFraction = 0.75f;
    public const float StandardCornerCutRatio = 0.25f;
    public const float WideCornerCutRatio = 0.4f;
    public const int WideAdditionalSmoothingIterations = 2;

    public static float GetNodePullbackFraction(bool isWide) =>
        isWide ? WideNodePullbackFraction : StandardNodePullbackFraction;

    public static float GetCornerCutRatio(bool isWide) =>
        isWide ? WideCornerCutRatio : StandardCornerCutRatio;

    public static int GetSmoothingIterations(
        int baseIterations,
        bool isWide) =>
        Mathf.Max(
            0,
            baseIterations +
            (isWide ? WideAdditionalSmoothingIterations : 0));

    public static float GetIntersectionVisualOuterDiameter(
        float roadWidth,
        float curbWidth) =>
        Mathf.Max(0f, roadWidth + curbWidth);

    public static float GetIntersectionGeometryOuterDiameter(
        float roadWidth,
        float curbWidth) =>
        Mathf.Max(0f, roadWidth + curbWidth);
}
