using System;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public struct TrafficNodeViewStyle
{
    private const int CurrentSerializedVersion = 2;

    [SerializeField, HideInInspector]
    private int serializedVersion;

    [Header("Lane lines")]
    public Color lineColor;
    [Min(0.001f), Tooltip("Full line width in world units.")]
    public float lineWidth;
    [FormerlySerializedAs("innerLineColor")]
    [InspectorName("Start-to-End Inner Color")]
    [Tooltip("Base inner color for lanes travelling from a section's start toward its end.")]
    public Color startToEndInnerColor;
    [InspectorName("End-to-Start Inner Color")]
    [Tooltip("Base inner color for lanes travelling from a section's end toward its start.")]
    public Color endToStartInnerColor;
    [Min(0.0001f), Tooltip("Width of the colored stripe inside the main line, in world units.")]
    public float innerLineWidth;
    [Range(0f, 1f), Tooltip("How strongly each branch varies from its direction's base color.")]
    public float branchColorVariation;
    [Min(0f), Tooltip("Distance above the lane geometry in world units.")]
    public float heightOffset;

    [Header("Direction arrows")]
    public bool showArrows;
    public Color arrowColor;
    [Min(0.001f)] public float arrowLength;
    [Min(0.001f)] public float arrowWidth;
    [Min(0.05f), Tooltip("Distance between arrows along a lane in world units.")]
    public float arrowSpacing;

    public static TrafficNodeViewStyle Default => new TrafficNodeViewStyle
    {
        serializedVersion = CurrentSerializedVersion,
        lineColor = new Color(0.15f, 0.9f, 1f),
        lineWidth = 0.07f,
        startToEndInnerColor = new Color(1f, 0.4f, 0.08f),
        endToStartInnerColor = new Color(0.65f, 0.22f, 1f),
        innerLineWidth = 0.035f,
        branchColorVariation = 0.85f,
        heightOffset = 0.12f,
        showArrows = true,
        arrowColor = new Color(0.15f, 0.9f, 1f),
        arrowLength = 0.38f,
        arrowWidth = 0.32f,
        arrowSpacing = 1.5f
    };

    public TrafficNodeViewStyle Validated()
    {
        var result = this;
        if (result.serializedVersion < 1)
        {
            result.startToEndInnerColor = Default.startToEndInnerColor;
            result.endToStartInnerColor = Default.endToStartInnerColor;
            result.innerLineWidth = Mathf.Min(
                Default.innerLineWidth,
                Mathf.Max(0.0001f, result.lineWidth * 0.5f));
            result.branchColorVariation = Default.branchColorVariation;
        }
        else if (result.serializedVersion < 2)
        {
            // Version 1 exposed one base inner color. Keep it for the first
            // direction and initialize the opposite way half a hue turn away.
            result.endToStartInnerColor = ShiftHue(
                result.startToEndInnerColor,
                0.5f);
        }
        result.serializedVersion = CurrentSerializedVersion;
        result.lineWidth = ClampFinite(result.lineWidth, 0.001f, 10f, Default.lineWidth);
        result.innerLineWidth = ClampFinite(
            result.innerLineWidth,
            0.0001f,
            result.lineWidth * 0.9f,
            Mathf.Min(Default.innerLineWidth, result.lineWidth * 0.5f));
        result.branchColorVariation = ClampFinite(
            result.branchColorVariation, 0f, 1f, Default.branchColorVariation);
        result.heightOffset = ClampFinite(result.heightOffset, 0f, 10f, Default.heightOffset);
        result.arrowLength = ClampFinite(result.arrowLength, 0.001f, 10f, Default.arrowLength);
        result.arrowWidth = ClampFinite(result.arrowWidth, 0.001f, 10f, Default.arrowWidth);
        result.arrowSpacing = ClampFinite(result.arrowSpacing, 0.05f, 1000f, Default.arrowSpacing);
        result.lineColor = ValidateColor(result.lineColor, Default.lineColor);
        result.startToEndInnerColor = ValidateColor(
            result.startToEndInnerColor,
            Default.startToEndInnerColor);
        result.endToStartInnerColor = ValidateColor(
            result.endToStartInnerColor,
            Default.endToStartInnerColor);
        result.arrowColor = ValidateColor(result.arrowColor, Default.arrowColor);
        return result;
    }

    private static float ClampFinite(float value, float min, float max, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

    private static Color ValidateColor(Color value, Color fallback) => new Color(
        ClampFinite(value.r, 0f, 1f, fallback.r), ClampFinite(value.g, 0f, 1f, fallback.g),
        ClampFinite(value.b, 0f, 1f, fallback.b), ClampFinite(value.a, 0f, 1f, fallback.a));

    private static Color ShiftHue(Color color, float offset)
    {
        Color.RGBToHSV(color, out float hue, out float saturation, out float value);
        Color result = Color.HSVToRGB(
            Mathf.Repeat(hue + offset, 1f),
            saturation,
            value);
        result.a = color.a;
        return result;
    }
}
