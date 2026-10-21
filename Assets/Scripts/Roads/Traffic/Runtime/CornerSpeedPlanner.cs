using System.Collections.Generic;
using UnityEngine;

/// <summary>Geometry-derived corner targets; acceleration remains owned by the conveyor.</summary>
public static class CornerSpeedPlanner
{
    public readonly struct Sample
    {
        public readonly float Distance;
        public readonly float Severity;
        public Sample(float distance, float severity) { Distance = distance; Severity = severity; }
    }

    private const float CornerClearance = 0.2f;
    private const int MaximumEdges = 8;
    private const int MaximumSamples = 64;

    // Called with length rebuilding, never from the per-vehicle simulation loop.
    public static void Rebuild(TrafficEdge edge)
    {
        edge.CornerSamples.Clear();
        edge.CornerEntryDirection = edge.CornerExitDirection = Vector3.zero;
        Vector3 previous = Vector3.zero;
        for (int i = 1; i < edge.waypoints.Count; i++)
        {
            Vector3 direction = edge.waypoints[i] - edge.waypoints[i - 1];
            direction.y = 0f; // Hills alone are not steering corners.
            if (direction.sqrMagnitude < 0.000001f) continue;
            if (previous.sqrMagnitude > 0f)
            {
                float severity = GetSeverity(previous, direction);
                if (severity > 0f)
                    edge.CornerSamples.Add(new Sample(edge.cumulativeWaypointDistances[i - 1], severity));
            }
            else edge.CornerEntryDirection = direction;
            previous = direction;
            edge.CornerExitDirection = direction;
        }
    }

    public static float GetSeverity(Vector3 incoming, Vector3 outgoing)
    {
        incoming.y = outgoing.y = 0f;
        float a = incoming.magnitude;
        float b = outgoing.magnitude;
        if (!IsFinite(a) || !IsFinite(b) || a < 0.001f || b < 0.001f) return 0f;
        float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(incoming, outgoing) / (a * b), -1f, 1f));
        // Curvature distinguishes a short turn from a broad bend. Cap the span
        // for unsmoothed polyline kinks so long straight approaches cannot hide them.
        float curvature = angle / Mathf.Min(0.5f, (a + b) * 0.5f);
        return Mathf.InverseLerp(0.5f, 3f, curvature);
    }

    public static float GetDesiredSpeed(IReadOnlyList<TrafficEdge> route, int edgeIndex,
        float distanceOnEdge, float freeSpeed, float deceleration, float maximumJerk,
        float maximumSlowdown = 0.3f)
    {
        if (route == null || edgeIndex < 0 || edgeIndex >= route.Count ||
            route[edgeIndex] == null || !IsFinite(freeSpeed) || freeSpeed <= 0f) return 0f;
        maximumSlowdown = IsFinite(maximumSlowdown) ? Mathf.Clamp(maximumSlowdown, 0f, 0.6f) : 0.3f;
        if (maximumSlowdown <= 0f) return freeSpeed;
        deceleration = IsFinite(deceleration) ? Mathf.Max(0.1f, deceleration) : 1f;
        maximumJerk = IsFinite(maximumJerk) ? Mathf.Max(0.1f, maximumJerk) : 1f;
        distanceOnEdge = IsFinite(distanceOnEdge) ? Mathf.Max(0f, distanceOnEdge) : 0f;
        float brakingMargin = freeSpeed * Mathf.Min(0.5f, deceleration / maximumJerk);
        float lookahead = Mathf.Min(32f, freeSpeed * freeSpeed / (2f * deceleration) + brakingMargin + 1f);
        float result = freeSpeed;
        int remainingSamples = MaximumSamples;
        float edgeStart = -distanceOnEdge;
        int first = edgeIndex;
        // Retain a corner target briefly after crossing a graph edge boundary.
        if (edgeIndex > 0 && distanceOnEdge < CornerClearance && route[edgeIndex - 1] != null)
        {
            first--;
            edgeStart -= route[first].totalLength;
        }
        int end = Mathf.Min(route.Count, edgeIndex + MaximumEdges);
        for (int i = first; i < end && edgeStart <= lookahead && remainingSamples > 0; i++)
        {
            TrafficEdge edge = route[i];
            if (edge == null) break;
            float baseSpeed = Mathf.Min(freeSpeed, Mathf.Max(0f, edge.speedLimit));
            int sampleIndex = FirstSampleAtOrAfter(edge.CornerSamples, -edgeStart - CornerClearance);
            for (; sampleIndex < edge.CornerSamples.Count && remainingSamples > 0; sampleIndex++)
            {
                Sample sample = edge.CornerSamples[sampleIndex];
                float distance = edgeStart + sample.Distance;
                if (distance > lookahead) break;
                remainingSamples--;
                result = Limit(result, baseSpeed, sample.Severity, distance,
                    deceleration, brakingMargin, maximumSlowdown);
            }
            float boundary = edgeStart + edge.totalLength;
            if (boundary >= -CornerClearance && boundary <= lookahead && i + 1 < route.Count &&
                route[i + 1] != null)
            {
                result = Limit(result, Mathf.Min(baseSpeed, route[i + 1].speedLimit),
                    GetSeverity(edge.CornerExitDirection, route[i + 1].CornerEntryDirection),
                    boundary, deceleration, brakingMargin, maximumSlowdown);
            }
            edgeStart = boundary;
        }
        return result;
    }

    private static float Limit(float current, float baseSpeed, float severity, float distance,
        float deceleration, float brakingMargin, float slowdown)
    {
        if (severity <= 0f) return current;
        float cornerSpeed = baseSpeed * (1f - slowdown * severity);
        return Mathf.Min(current, Mathf.Sqrt(cornerSpeed * cornerSpeed +
            2f * deceleration * Mathf.Max(0f, distance - brakingMargin)));
    }

    private static int FirstSampleAtOrAfter(List<Sample> samples, float distance)
    {
        int low = 0, high = samples.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (samples[middle].Distance < distance) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
