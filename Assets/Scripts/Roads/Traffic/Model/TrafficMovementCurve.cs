using System.Collections.Generic;
using UnityEngine;

/// <summary>Shared turn geometry for graph compilation and connection previews.</summary>
public static class TrafficMovementCurve
{
    public const int SampleCount = 12;

    public static Vector3 Evaluate(Vector3 start, Vector3 end, Vector3 sourceDirection,
        Vector3 targetDirection, float t)
    {
        float controlDistance = Mathf.Max(0.1f, Vector3.Distance(start, end) * 0.4f);
        Vector3 controlA = start + sourceDirection * controlDistance;
        Vector3 controlB = end - targetDirection * controlDistance;
        float remaining = 1f - t;
        return remaining * remaining * remaining * start +
            3f * remaining * remaining * t * controlA +
            3f * remaining * t * t * controlB + t * t * t * end;
    }

    public static void AppendSamples(List<Vector3> destination, Vector3 start, Vector3 end,
        Vector3 sourceDirection, Vector3 targetDirection)
    {
        for (int i = 0; i < SampleCount; i++)
            destination.Add(Evaluate(start, end, sourceDirection, targetDirection, (float)i / (SampleCount - 1)));
    }

    public static Vector3 SampleAtDistance(IReadOnlyList<Vector3> samples, float distance,
        out Vector3 direction)
    {
        direction = Vector3.forward;
        if (samples == null || samples.Count == 0) return Vector3.zero;
        float totalLength = 0f;
        for (int i = 1; i < samples.Count; i++) totalLength += Vector3.Distance(samples[i - 1], samples[i]);
        float remaining = Mathf.Clamp(distance, 0f, totalLength);
        for (int i = 1; i < samples.Count; i++)
        {
            Vector3 segment = samples[i] - samples[i - 1];
            float length = segment.magnitude;
            if (length <= 0.000001f) continue;
            direction = segment / length;
            if (remaining <= length) return samples[i - 1] + direction * remaining;
            remaining -= length;
        }
        // Keep the last nonzero tangent even when the polyline ends in duplicates.
        return samples[samples.Count - 1];
    }
}
