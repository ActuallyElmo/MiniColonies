using System.Collections.Generic;
using UnityEngine;

public static class IntersectionControlUtility
{
    public const float MinimumLightDurationSeconds = 5f;
    public const float MaximumLightDurationSeconds = 120f;
    public const float DefaultLightDurationSeconds = 30f;
    public const float MinimumSmartWaitSeconds = 1f;
    public const float MaximumSmartWaitSeconds = 120f;
    public const float DefaultSmartWaitSeconds = 10f;

    public static bool UsesTrafficLights(IntersectionRuleType ruleType) =>
        ruleType == IntersectionRuleType.TrafficLight ||
        ruleType == IntersectionRuleType.SmartTrafficLight;

    public static float ValidateSmartWait(float seconds) =>
        float.IsNaN(seconds) || float.IsInfinity(seconds) ? DefaultSmartWaitSeconds :
        Mathf.Clamp(seconds, MinimumSmartWaitSeconds, MaximumSmartWaitSeconds);

    public static int GetTrafficLightIndex(int directionBit, int laneIndex,
        int incomingDirectionMask, IReadOnlyList<LaneTrafficLightIndex> indices)
    {
        if (indices != null)
        {
            for (int i = 0; i < indices.Count; i++)
                if (indices[i].Source.DirectionBit == directionBit &&
                    indices[i].Source.LaneIndex == laneIndex) return indices[i].Index;
        }
        // Preserve the existing sorted groups of exact opposing approaches.
        int phase = 0;
        for (int bit = 1; bit <= 128; bit <<= 1)
        {
            if ((incomingDirectionMask & bit) == 0) continue;
            int group = bit | (incomingDirectionMask & OppositeDirectionBit(bit));
            if ((group & directionBit) != 0) return phase;
            incomingDirectionMask &= ~group;
            phase++;
        }
        return 0;
    }

    public static bool IsPlayerSelectable(IntersectionRuleType ruleType) =>
        ruleType == IntersectionRuleType.FreeForAll ||
        ruleType == IntersectionRuleType.FIFO ||
        ruleType == IntersectionRuleType.PriorityRoad ||
        UsesTrafficLights(ruleType);

    public static float ValidateLightDuration(float seconds)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds))
            return DefaultLightDurationSeconds;
        return Mathf.Clamp(seconds, MinimumLightDurationSeconds, MaximumLightDurationSeconds);
    }

    public static bool IsDirectionBit(int directionBit) =>
        directionBit > 0 && directionBit <= 128 &&
        (directionBit & (directionBit - 1)) == 0;

    public static int OppositeDirectionBit(int directionBit)
    {
        if (!IsDirectionBit(directionBit)) return 0;
        int index = 0;
        while ((1 << index) != directionBit) index++;
        return 1 << ((index + 4) & 7);
    }

    public static int FindMostOppositeDirection(
        int selectedDirection,
        IReadOnlyList<int> availableDirections)
    {
        if (!IsDirectionBit(selectedDirection) || availableDirections == null)
            return 0;

        int exact = OppositeDirectionBit(selectedDirection);
        for (int i = 0; i < availableDirections.Count; i++)
            if (availableDirections[i] == exact) return exact;

        Vector2 selected = DirectionVector(selectedDirection);
        int best = 0;
        float bestDot = float.PositiveInfinity;
        for (int i = 0; i < availableDirections.Count; i++)
        {
            int candidate = availableDirections[i];
            if (candidate == selectedDirection || !IsDirectionBit(candidate)) continue;
            float dot = Vector2.Dot(selected, DirectionVector(candidate));
            if (dot < bestDot)
            {
                bestDot = dot;
                best = candidate;
            }
        }
        return best;
    }

    public static Vector2 DirectionVector(int directionBit)
    {
        Vector2Int offset = RoadGridDirectionUtility.GetNeighborPosition(
            Vector2Int.zero,
            directionBit);
        return new Vector2(offset.x, offset.y).normalized;
    }
}
