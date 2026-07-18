using System.Collections.Generic;
using UnityEngine;
using System;

[Serializable]
public struct LaneConnectionRule
{
    public int FromDirectionBit;
    public int FromLaneIndex;
    
    public int ToDirectionBit;
    public int ToLaneIndex;

    public LaneConnectionRule(int fromDir, int fromLane, int toDir, int toLane)
    {
        FromDirectionBit = fromDir;
        FromLaneIndex = fromLane;
        ToDirectionBit = toDir;
        ToLaneIndex = toLane;
    }
}

[Serializable]
public class IntersectionData
{
    public Vector2Int GridPosition { get; private set; }

    private RoadNodeKind _nodeKind = RoadNodeKind.Intersection;
    private IntersectionRuleType _ruleType = IntersectionRuleType.FreeForAll;
    private int _priorityDirectionBitA;
    private int _priorityDirectionBitB;
    private float _trafficLightCycleSeconds = 30f;

    public RoadNodeKind NodeKind
    {
        get => _nodeKind;
        set
        {
            if (_nodeKind == value) return;
            _nodeKind = value;
            NotifyChanged();
        }
    }

    public IntersectionRuleType RuleType
    {
        get => _ruleType;
        set
        {
            if (_ruleType == value) return;
            _ruleType = value;
            NotifyChanged();
        }
    }
    
    // Custom Rules set by the player. If empty, the intersection relies on defaults.
    public List<LaneConnectionRule> CustomRules { get; } = new List<LaneConnectionRule>();

    // We can also cache the generated default rules here if we want to visualize them
    // without the player having explicitly set them.
    public List<LaneConnectionRule> DefaultRules { get; } = new List<LaneConnectionRule>();
    public List<LaneConnectionRule> DisabledRules { get; } = new List<LaneConnectionRule>();

    public int InvalidCustomRuleCount { get; set; }

    public int PriorityDirectionBitA
    {
        get => _priorityDirectionBitA;
        set
        {
            if (_priorityDirectionBitA == value) return;
            _priorityDirectionBitA = value;
            NotifyChanged();
        }
    }

    public int PriorityDirectionBitB
    {
        get => _priorityDirectionBitB;
        set
        {
            if (_priorityDirectionBitB == value) return;
            _priorityDirectionBitB = value;
            NotifyChanged();
        }
    }

    public float TrafficLightCycleSeconds
    {
        get => _trafficLightCycleSeconds;
        set
        {
            if (_trafficLightCycleSeconds.Equals(value)) return;
            _trafficLightCycleSeconds = value;
            NotifyChanged();
        }
    }

    public event Action<IntersectionData> Changed;

    public IntersectionData(Vector2Int gridPosition)
    {
        GridPosition = gridPosition;
    }

    public void AddCustomRule(int fromDir, int fromLane, int toDir, int toLane)
    {
        for (int i = 0; i < CustomRules.Count; i++)
        {
            LaneConnectionRule existing = CustomRules[i];
            if (existing.FromDirectionBit != fromDir || existing.FromLaneIndex != fromLane)
            {
                continue;
            }

            if (existing.ToDirectionBit == toDir && existing.ToLaneIndex == toLane) return;
            CustomRules[i] = new LaneConnectionRule(fromDir, fromLane, toDir, toLane);
            NotifyChanged();
            return;
        }

        CustomRules.Add(new LaneConnectionRule(fromDir, fromLane, toDir, toLane));
        NotifyChanged();
    }

    public void RemoveCustomRule(int fromDir, int fromLane)
    {
        int removed = CustomRules.RemoveAll(
            rule => rule.FromDirectionBit == fromDir && rule.FromLaneIndex == fromLane);
        if (removed > 0) NotifyChanged();
    }

    public void ClearCustomRules()
    {
        if (CustomRules.Count == 0) return;
        CustomRules.Clear();
        NotifyChanged();
    }

    public void AddDisabledRule(int fromDir, int fromLane, int toDir, int toLane)
    {
        var rule = new LaneConnectionRule(fromDir, fromLane, toDir, toLane);
        if (DisabledRules.Contains(rule)) return;
        DisabledRules.Add(rule);
        NotifyChanged();
    }

    public void ClearDisabledRules()
    {
        if (DisabledRules.Count == 0) return;
        DisabledRules.Clear();
        NotifyChanged();
    }

    public void SetPriorityDirections(int directionBitA, int directionBitB)
    {
        if (_priorityDirectionBitA == directionBitA &&
            _priorityDirectionBitB == directionBitB)
        {
            return;
        }

        _priorityDirectionBitA = directionBitA;
        _priorityDirectionBitB = directionBitB;
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        Changed?.Invoke(this);
    }
}
