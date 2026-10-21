using System;
using UnityEngine;
using TMPro;

[Serializable]
public struct IntersectionBuilderStyle
{
    [SerializeField, HideInInspector] private int serializedVersion;
    [Header("Marker images")]
    public Sprite intersectionSprite;
    public Sprite entrySprite;
    public Sprite exitSprite;
    [Tooltip("Sprite used by exits connected to the selected entry. Falls back to Exit Sprite when unassigned.")]
    public Sprite connectedExitSprite;
    [Header("Intersection control icons")]
    public Sprite freeForAllTypeSprite;
    public Sprite fifoTypeSprite;
    public Sprite priorityRoadTypeSprite;
    public Sprite trafficLightTypeSprite;
    [Tooltip("Smart-light control icon. Falls back to the normal traffic-light icon.")]
    public Sprite smartTrafficLightTypeSprite;
    [Tooltip("Tint of the existing intersection icon beneath its control-type icon.")]
    public Color intersectionIconColor;
    public Color intersectionTypeIconColor;
    [Range(0.1f, 1f)] public float intersectionTypeIconScale;
    [Min(0.01f), InspectorName("Intersection Icon Radius")] public float intersectionCircleRadius;
    [Min(0.01f), InspectorName("Entry Icon Radius")] public float laneCircleRadius;
    [Min(0.01f)] public float exitIconRadius;
    // Retained for serialized compatibility with the original procedural markers.
    [HideInInspector]
    public float circleThicknessRatio;

    [Header("Lines (world units)")]
    [Min(0.001f)] public float laneLineWidth;
    [Min(0.001f)] public float connectionLineWidth;
    [Min(0.001f)] public float previewLineWidth;
    [Min(0f)] public float heightOffset;
    [Min(0.001f)] public float arrowLength;
    [Min(0.001f)] public float arrowWidth;
    [Tooltip("Arrows on intersection paths, hover highlights, and previews. Road lane arrows are unchanged.")]
    public bool showIntersectionArrows;

    [Header("Path editing")]
    [Min(0.001f)] public float backgroundPathWidth;
    [Tooltip("Color and transparency of paths belonging to other entries.")]
    public Color backgroundPathColor;
    [Tooltip("Color and transparency of background paths from the configured priority-road approaches.")]
    public Color priorityRoadBackgroundPathColor;
    public Color hoveredPathColor;
    // Preview curvature is now owned by TrafficMovementCurve, like compiled turns.
    [HideInInspector]
    public float previewCurveStrength;

    [Header("Colors")]
    public Color entryColor;
    [Tooltip("Tint used while the pointer is over an intersection, entry, or exit icon.")]
    public Color hoveredMarkerColor;
    public Color selectedColor;
    public Color connectedColor;
    [Tooltip("Tint used by an exit that can be connected to the selected entry.")]
    public Color availableExitColor;
    public Color unavailableColor;
    public Color incomingLaneColor;
    public Color outgoingLaneColor;

    [Header("Traffic light indices")]
    [Tooltip("Background for the numbered lane controls. Falls back to the intersection sprite, then a circle.")]
    public Sprite trafficLightIndexBackgroundSprite;
    [Tooltip("Optional font override. Uses the intersection panel's game font when unassigned.")]
    public TMP_FontAsset trafficLightIndexFont;
    [Tooltip("Background color by index: 0 uses the start of the gradient, 9 uses the end.")]
    public Gradient trafficLightIndexColorGradient;
    public Color trafficLightIndexTextColor;
    [Range(0f, 1f)]
    [Tooltip("Amount to lighten the current index background while hovered.")]
    public float trafficLightIndexHoverLightness;

    [Header("Traffic light indicators")]
    [Tooltip("Sprite used by the red/green lane signals. Falls back to the traffic-light type, entry, then intersection sprite.")]
    public Sprite trafficLightSignalSprite;
    [Min(0.01f)] public float trafficLightIconRadius;
    [Range(-10f, 10f), InspectorName("Traffic Light Icon Offset")]
    [Tooltip("Offset along the incoming road in world units. Negative moves toward the intersection center; positive moves outward.")]
    public float trafficLightIconOutwardOffset;
    [Min(0f)] public float trafficLightIconHeightOffset;
    [Min(0.01f)] public float trafficLightPopDuration;
    [Range(0f, 2f)]
    [Tooltip("Overshoot applied as traffic-light icons pop into their new state.")]
    public float trafficLightPopOvershoot;
    public Color trafficLightRedColor;
    public Color trafficLightGreenColor;

    public Sprite GetRuleTypeSprite(IntersectionRuleType ruleType)
    {
        switch (ruleType)
        {
            case IntersectionRuleType.FIFO: return fifoTypeSprite;
            case IntersectionRuleType.PriorityRoad: return priorityRoadTypeSprite;
            case IntersectionRuleType.TrafficLight: return trafficLightTypeSprite;
            case IntersectionRuleType.SmartTrafficLight:
                return smartTrafficLightTypeSprite != null ? smartTrafficLightTypeSprite : trafficLightTypeSprite;
            default: return freeForAllTypeSprite;
        }
    }

    public Sprite GetTrafficLightSignalSprite() =>
        trafficLightSignalSprite != null ? trafficLightSignalSprite :
        trafficLightTypeSprite != null ? trafficLightTypeSprite :
        entrySprite != null ? entrySprite : intersectionSprite;

    public Color GetTrafficLightIndexColor(int index, bool hovered = false)
    {
        Color color = trafficLightIndexColorGradient != null
            ? trafficLightIndexColorGradient.Evaluate(Mathf.Clamp(index, 0, 9) / 9f)
            : selectedColor;
        color = ValidColor(color, new Color(1f, 0.8f, 0.2f));
        return hovered ? Color.Lerp(color, new Color(1f, 1f, 1f, color.a),
            ValidHoverLightness(trafficLightIndexHoverLightness)) : color;
    }

    private static Gradient CreateIndexGradient()
    {
        var gradient = new Gradient();
        gradient.SetKeys(new[]
        {
            new GradientColorKey(new Color(0.2f, 0.65f, 0.8f), 0f),
            new GradientColorKey(new Color(1f, 0.8f, 0.2f), 1f)
        }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return gradient;
    }

    private static float ValidHoverLightness(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? 0.25f : Mathf.Clamp01(value);

    public static IntersectionBuilderStyle Default => new IntersectionBuilderStyle
    {
        serializedVersion = 10,
        trafficLightIndexColorGradient = CreateIndexGradient(),
        trafficLightIndexTextColor = Color.black,
        trafficLightIndexHoverLightness = 0.25f,
        intersectionCircleRadius = 0.3f,
        laneCircleRadius = 0.11f,
        exitIconRadius = 0.11f,
        showIntersectionArrows = true,
        circleThicknessRatio = 0.25f,
        laneLineWidth = 0.045f,
        connectionLineWidth = 0.06f,
        previewLineWidth = 0.05f,
        heightOffset = 0.16f,
        arrowLength = 0.2f,
        arrowWidth = 0.15f,
        backgroundPathWidth = 0.02f,
        backgroundPathColor = new Color(0.65f, 0.8f, 0.9f, 0.2f),
        priorityRoadBackgroundPathColor = new Color(0.3f, 1f, 0.5f, 0.45f),
        hoveredPathColor = new Color(1f, 0.35f, 0.15f),
        previewCurveStrength = 0.4f,
        entryColor = new Color(0.2f, 0.85f, 1f),
        hoveredMarkerColor = Color.white,
        selectedColor = new Color(1f, 0.8f, 0.2f),
        connectedColor = new Color(0.3f, 1f, 0.5f),
        availableExitColor = new Color(1f, 0.8f, 0.2f),
        unavailableColor = Color.gray,
        incomingLaneColor = new Color(0.2f, 0.65f, 0.8f),
        outgoingLaneColor = new Color(0.6f, 0.65f, 0.7f),
        intersectionIconColor = new Color(0.2f, 0.85f, 1f),
        intersectionTypeIconColor = Color.white,
        intersectionTypeIconScale = 0.55f,
        trafficLightIconRadius = 0.09f,
        trafficLightIconOutwardOffset = 0.08f,
        trafficLightIconHeightOffset = 0.2f,
        trafficLightPopDuration = 0.3f,
        trafficLightPopOvershoot = 1f,
        trafficLightRedColor = new Color(1f, 0.16f, 0.12f),
        trafficLightGreenColor = new Color(0.2f, 1f, 0.32f)
    };

    public IntersectionBuilderStyle Validated()
    {
        var result = this;
        if (result.serializedVersion < 1)
        {
            result.backgroundPathWidth = Default.backgroundPathWidth;
            result.backgroundPathColor = Default.backgroundPathColor;
            result.hoveredPathColor = Default.hoveredPathColor;
            result.previewCurveStrength = Default.previewCurveStrength;
            result.serializedVersion = 1;
        }
        if (result.serializedVersion < 2)
        {
            result.exitIconRadius = laneCircleRadius;
            result.showIntersectionArrows = true;
            result.serializedVersion = 2;
        }
        if (result.serializedVersion < 3)
        {
            result.hoveredMarkerColor = Default.hoveredMarkerColor;
            result.serializedVersion = 3;
        }
        if (result.serializedVersion < 4)
        {
            // Available exits previously shared the selected-entry color.
            result.availableExitColor = result.selectedColor == default
                ? Default.availableExitColor
                : result.selectedColor;
            result.serializedVersion = 4;
        }
        if (result.serializedVersion < 5)
        {
            result.connectedExitSprite = result.exitSprite;
            result.serializedVersion = 5;
        }
        if (result.serializedVersion < 6)
        {
            result.intersectionIconColor = result.entryColor == default
                ? Default.intersectionIconColor
                : result.entryColor;
            result.intersectionTypeIconColor = Default.intersectionTypeIconColor;
            result.intersectionTypeIconScale = Default.intersectionTypeIconScale;
            result.trafficLightIconRadius = Default.trafficLightIconRadius;
            result.trafficLightIconOutwardOffset = Default.trafficLightIconOutwardOffset;
            result.trafficLightIconHeightOffset = Default.trafficLightIconHeightOffset;
            result.trafficLightPopDuration = Default.trafficLightPopDuration;
            result.trafficLightRedColor = Default.trafficLightRedColor;
            result.trafficLightGreenColor = Default.trafficLightGreenColor;
            result.serializedVersion = 6;
        }
        if (result.serializedVersion < 7)
        {
            result.trafficLightSignalSprite = result.trafficLightTypeSprite != null
                ? result.trafficLightTypeSprite
                : result.entrySprite;
            result.serializedVersion = 7;
        }
        if (result.serializedVersion < 8)
        {
            result.trafficLightPopOvershoot = Default.trafficLightPopOvershoot;
            result.serializedVersion = 8;
        }
        if (result.serializedVersion < 9)
        {
            result.trafficLightIndexColorGradient = CreateIndexGradient();
            result.trafficLightIndexTextColor = Color.black;
            result.trafficLightIndexHoverLightness = 0.25f;
            result.serializedVersion = 9;
        }
        if (result.serializedVersion < 10)
        {
            result.priorityRoadBackgroundPathColor = Default.priorityRoadBackgroundPathColor;
            result.serializedVersion = 10;
        }
        if (result.trafficLightIndexColorGradient == null)
            result.trafficLightIndexColorGradient = CreateIndexGradient();
        result.trafficLightIndexTextColor = ValidColor(result.trafficLightIndexTextColor, Color.black);
        result.trafficLightIndexHoverLightness = ValidHoverLightness(result.trafficLightIndexHoverLightness);
        result.intersectionCircleRadius = AtLeast(intersectionCircleRadius, 0.01f, Default.intersectionCircleRadius);
        result.laneCircleRadius = AtLeast(laneCircleRadius, 0.01f, Default.laneCircleRadius);
        result.exitIconRadius = AtLeast(result.exitIconRadius, 0.01f, Default.exitIconRadius);
        result.intersectionTypeIconScale = Mathf.Clamp(
            AtLeast(result.intersectionTypeIconScale, 0.1f, Default.intersectionTypeIconScale),
            0.1f,
            1f);
        result.circleThicknessRatio = Mathf.Min(1f, AtLeast(circleThicknessRatio, 0.05f, Default.circleThicknessRatio));
        result.laneLineWidth = AtLeast(laneLineWidth, 0.001f, Default.laneLineWidth);
        result.connectionLineWidth = AtLeast(connectionLineWidth, 0.001f, Default.connectionLineWidth);
        result.previewLineWidth = AtLeast(previewLineWidth, 0.001f, Default.previewLineWidth);
        result.heightOffset = AtLeast(heightOffset, 0f, Default.heightOffset);
        result.arrowLength = AtLeast(arrowLength, 0.001f, Default.arrowLength);
        result.arrowWidth = AtLeast(arrowWidth, 0.001f, Default.arrowWidth);
        result.backgroundPathWidth = Mathf.Min(result.connectionLineWidth,
            AtLeast(result.backgroundPathWidth, 0.001f, Default.backgroundPathWidth));
        result.previewCurveStrength = Mathf.Min(1f,
            AtLeast(result.previewCurveStrength, 0.05f, Default.previewCurveStrength));
        result.trafficLightIconRadius = AtLeast(
            result.trafficLightIconRadius, 0.01f, Default.trafficLightIconRadius);
        result.trafficLightIconOutwardOffset = AtLeast(
            result.trafficLightIconOutwardOffset, -10f, Default.trafficLightIconOutwardOffset);
        result.trafficLightIconHeightOffset = AtLeast(
            result.trafficLightIconHeightOffset, 0f, Default.trafficLightIconHeightOffset);
        result.trafficLightPopDuration = AtLeast(
            result.trafficLightPopDuration, 0.01f, Default.trafficLightPopDuration);
        result.trafficLightPopOvershoot = Mathf.Clamp(
            float.IsNaN(result.trafficLightPopOvershoot) ||
            float.IsInfinity(result.trafficLightPopOvershoot)
                ? Default.trafficLightPopOvershoot
                : result.trafficLightPopOvershoot,
            0f,
            2f);
        result.backgroundPathColor = ValidColor(result.backgroundPathColor, Default.backgroundPathColor);
        result.priorityRoadBackgroundPathColor = ValidColor(result.priorityRoadBackgroundPathColor,
            Default.priorityRoadBackgroundPathColor);
        result.hoveredPathColor = ValidColor(result.hoveredPathColor, Default.hoveredPathColor);
        return result;
    }

    private static Color ValidColor(Color value, Color fallback) => new Color(
        Mathf.Min(1f, AtLeast(value.r, 0f, fallback.r)), Mathf.Min(1f, AtLeast(value.g, 0f, fallback.g)),
        Mathf.Min(1f, AtLeast(value.b, 0f, fallback.b)), Mathf.Min(1f, AtLeast(value.a, 0f, fallback.a)));

    private static float AtLeast(float value, float minimum, float fallback) =>
        // Match the overlay renderer's maximum dimensions so picking and drawing agree.
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, minimum, 10f);
}
