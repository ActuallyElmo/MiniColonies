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
public struct LaneConnectionSource : IEquatable<LaneConnectionSource>
{
    public int DirectionBit;
    public int LaneIndex;

    public LaneConnectionSource(int directionBit, int laneIndex)
    {
        DirectionBit = directionBit;
        LaneIndex = laneIndex;
    }

    public bool Equals(LaneConnectionSource other) =>
        DirectionBit == other.DirectionBit && LaneIndex == other.LaneIndex;

    public override bool Equals(object obj) =>
        obj is LaneConnectionSource other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            return (DirectionBit * 397) ^ LaneIndex;
        }
    }
}

[Serializable]
public readonly struct LaneTrafficLightIndex
{
    public LaneConnectionSource Source { get; }
    public int Index { get; }

    public LaneTrafficLightIndex(int directionBit, int laneIndex, int index)
    {
        Source = new LaneConnectionSource(directionBit, laneIndex);
        Index = Mathf.Clamp(index, 0, 9);
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
    private float _trafficLightCycleSeconds =
        IntersectionControlUtility.DefaultLightDurationSeconds;
    private float _smartTrafficLightWaitSeconds = IntersectionControlUtility.DefaultSmartWaitSeconds;

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
    
    // Legacy custom rules replace defaults when nonempty. Explicit edits may also be empty.
    public List<LaneConnectionRule> CustomRules { get; } = new List<LaneConnectionRule>();

    // An explicitly empty set means the player deleted every connection.
    public bool UseExplicitConnections { get; private set; }

    // Records the entries present when the player last edited this policy. New
    // entries receive defaults, while an existing entry may remain empty.
    public List<LaneConnectionSource> ExplicitSourceLanes { get; } =
        new List<LaneConnectionSource>();
    public bool HasExplicitSourceLaneBaseline { get; private set; }

    public void ReplaceConnections(IReadOnlyList<LaneConnectionRule> rules)
    {
        ReplaceConnections(rules, null, false);
    }

    public void ReplaceConnections(
        IReadOnlyList<LaneConnectionRule> rules,
        IReadOnlyList<LaneConnectionSource> sourceLanes)
    {
        if (sourceLanes == null) throw new ArgumentNullException(nameof(sourceLanes));
        ReplaceConnections(rules, sourceLanes, true);
    }

    private void ReplaceConnections(
        IReadOnlyList<LaneConnectionRule> rules,
        IReadOnlyList<LaneConnectionSource> sourceLanes,
        bool hasSourceLaneBaseline)
    {
        if (rules == null) throw new ArgumentNullException(nameof(rules));
        var copy = new List<LaneConnectionRule>(rules.Count);
        for (int i = 0; i < rules.Count; i++)
            if (!copy.Contains(rules[i])) copy.Add(rules[i]);
        var sourceCopy = new List<LaneConnectionSource>(
            sourceLanes != null ? sourceLanes.Count : 0);
        if (sourceLanes != null)
        {
            for (int i = 0; i < sourceLanes.Count; i++)
                if (!sourceCopy.Contains(sourceLanes[i]))
                    sourceCopy.Add(sourceLanes[i]);
        }
        CustomRules.Clear();
        CustomRules.AddRange(copy);
        DisabledRules.Clear();
        ExplicitSourceLanes.Clear();
        ExplicitSourceLanes.AddRange(sourceCopy);
        HasExplicitSourceLaneBaseline = hasSourceLaneBaseline;
        UseExplicitConnections = true;
        NotifyChanged();
    }

    public List<LaneConnectionRule> DisabledRules { get; } = new List<LaneConnectionRule>();

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
            float validated = IntersectionControlUtility.ValidateLightDuration(value);
            if (_trafficLightCycleSeconds.Equals(validated)) return;
            _trafficLightCycleSeconds = validated;
            NotifyChanged();
        }
    }

    public float SmartTrafficLightWaitSeconds
    {
        get => _smartTrafficLightWaitSeconds;
        set
        {
            float validated = IntersectionControlUtility.ValidateSmartWait(value);
            if (_smartTrafficLightWaitSeconds.Equals(validated)) return;
            _smartTrafficLightWaitSeconds = validated;
            NotifyChanged();
        }
    }

    public event Action<IntersectionData> Changed;

    private readonly List<LaneTrafficLightIndex> _trafficLightIndices =
        new List<LaneTrafficLightIndex>();
    private readonly IReadOnlyList<LaneTrafficLightIndex> _readOnlyTrafficLightIndices;
    public IReadOnlyList<LaneTrafficLightIndex> TrafficLightIndices =>
        _readOnlyTrafficLightIndices;

    public bool SetTrafficLightIndex(int directionBit, int laneIndex, int index)
    {
        if (!IntersectionControlUtility.IsDirectionBit(directionBit) || laneIndex < 0 ||
            index < 0 || index > 9) return false;
        var value = new LaneTrafficLightIndex(directionBit, laneIndex, index);
        for (int i = 0; i < _trafficLightIndices.Count; i++)
        {
            if (!_trafficLightIndices[i].Source.Equals(value.Source)) continue;
            if (_trafficLightIndices[i].Index == index) return true;
            _trafficLightIndices[i] = value;
            NotifyChanged();
            return true;
        }
        _trafficLightIndices.Add(value);
        NotifyChanged();
        return true;
    }

    public IntersectionData(Vector2Int gridPosition)
    {
        GridPosition = gridPosition;
        _readOnlyTrafficLightIndices = _trafficLightIndices.AsReadOnly();
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
        if (CustomRules.Count == 0 && !UseExplicitConnections) return;
        CustomRules.Clear();
        ExplicitSourceLanes.Clear();
        HasExplicitSourceLaneBaseline = false;
        UseExplicitConnections = false;
        NotifyChanged();
    }

    public bool RestoreDefaultConnections()
    {
        if (CustomRules.Count == 0 && DisabledRules.Count == 0 &&
            !UseExplicitConnections && !HasExplicitSourceLaneBaseline &&
            ExplicitSourceLanes.Count == 0) return false;
        CustomRules.Clear();
        DisabledRules.Clear();
        ExplicitSourceLanes.Clear();
        HasExplicitSourceLaneBaseline = false;
        UseExplicitConnections = false;
        NotifyChanged();
        return true;
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
