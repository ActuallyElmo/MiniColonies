using System.Collections.Generic;
using UnityEngine;

public sealed class TrafficLightIntersectionController : IIntersectionController
{
    private readonly FreeForAllIntersectionController safetyController =
        new FreeForAllIntersectionController();
    private readonly List<int> phaseMasks = new List<int>();
    private readonly List<int> phaseIndices = new List<int>();
    private readonly Dictionary<LaneConnectionSource, int> laneIndices =
        new Dictionary<LaneConnectionSource, int>();
    private float elapsedSeconds;
    private int phaseIndex;
    private struct WaitingVehicle
    {
        public VehicleAI Vehicle;
        public LaneConnectionSource Source;
        public double Since;
        public long LastSeenTick;
    }

    private readonly List<WaitingVehicle> waitingVehicles = new List<WaitingVehicle>();
    private double simulationSeconds;
    private double lastSwitchSeconds;
    private long tickIndex;

    public Vector2Int Cell { get; private set; }
    public IntersectionRuleType RuleType { get; private set; } = IntersectionRuleType.TrafficLight;
    public float SmartWaitSeconds { get; private set; }
    public int ActiveDirectionMask => phaseMasks.Count > 0 ? phaseMasks[phaseIndex] : 0;
    public int ActiveSignalIndex => phaseIndices.Count > 0 ? phaseIndices[phaseIndex] : -1;
    public float LightDurationSeconds { get; private set; }
    public float PhaseProgress => LightDurationSeconds > 0f
        ? Mathf.Clamp01(elapsedSeconds / LightDurationSeconds)
        : 0f;

    public void Initialize(
        Vector2Int cell,
        IntersectionData data,
        IReadOnlyList<TrafficEdge> movementEdges)
    {
        Initialize(cell, data, movementEdges, null);
    }

    public void Initialize(Vector2Int cell, IntersectionData data,
        IReadOnlyList<TrafficEdge> movementEdges,
        IReadOnlyList<LaneConnectionSource> incomingLanes)
    {
        Cell = cell;
        RuleType = data != null && data.RuleType == IntersectionRuleType.SmartTrafficLight
            ? IntersectionRuleType.SmartTrafficLight : IntersectionRuleType.TrafficLight;
        SmartWaitSeconds = IntersectionControlUtility.ValidateSmartWait(data != null
            ? data.SmartTrafficLightWaitSeconds : IntersectionControlUtility.DefaultSmartWaitSeconds);
        waitingVehicles.Clear();
        simulationSeconds = 0d;
        lastSwitchSeconds = 0d;
        tickIndex = 0;
        LightDurationSeconds = IntersectionControlUtility.ValidateLightDuration(
            data != null
                ? data.TrafficLightCycleSeconds
                : IntersectionControlUtility.DefaultLightDurationSeconds);
        elapsedSeconds = 0f;
        phaseIndex = 0;
        BuildPhases(data, movementEdges, incomingLanes);
        safetyController.Initialize(cell, data, movementEdges);
    }

    public bool RestorePhaseFrom(TrafficLightIntersectionController previous)
    {
        if (previous == null || phaseMasks.Count == 0) return false;
        int restoredIndex = phaseIndices.IndexOf(previous.ActiveSignalIndex);
        if (restoredIndex < 0) return false;

        phaseIndex = restoredIndex;
        elapsedSeconds = previous.PhaseProgress * LightDurationSeconds;
        if (RuleType == IntersectionRuleType.SmartTrafficLight && previous.RuleType == RuleType)
        {
            simulationSeconds = previous.simulationSeconds;
            lastSwitchSeconds = previous.lastSwitchSeconds;
            tickIndex = previous.tickIndex;
            for (int i = 0; i < previous.waitingVehicles.Count; i++)
            {
                WaitingVehicle waiting = previous.waitingVehicles[i];
                if (waiting.Vehicle != null && laneIndices.ContainsKey(waiting.Source))
                    waitingVehicles.Add(waiting);
            }
        }
        return true;
    }

    public void Tick(float deltaTime)
    {
        safetyController.Tick(deltaTime);
        if (phaseIndices.Count <= 1 || deltaTime <= 0f ||
            float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
        if (RuleType == IntersectionRuleType.SmartTrafficLight)
        {
            TickSmartLights(deltaTime);
            return;
        }
        // Bounded work even after a large simulation time step.
        double elapsed = (double)elapsedSeconds + deltaTime;
        double advances = System.Math.Floor(elapsed / LightDurationSeconds);
        elapsedSeconds = (float)(elapsed % LightDurationSeconds);
        phaseIndex = (phaseIndex + (int)(advances % phaseIndices.Count)) % phaseIndices.Count;
    }

    public bool CanEnter(
        VehicleAI vehicle,
        TrafficEdge fromEdge,
        TrafficEdge movementEdge,
        TrafficEdge toEdge) =>
        CanEnter(new TrafficMovementRequest(vehicle, fromEdge, movementEdge, toEdge));

    public bool CanEnter(TrafficMovementRequest request)
    {
        return request.MovementEdge != null &&
               IsGreenForLane(request.MovementEdge.fromDirectionBit,
                   request.MovementEdge.fromLaneIndex) &&
               safetyController.CanEnter(request);
    }

    public void NotifyEntered(VehicleAI vehicle, TrafficEdge movementEdge)
    {
        RemoveWaitingVehicle(vehicle);
        safetyController.NotifyEntered(vehicle, movementEdge);
    }

    public void NotifyExited(VehicleAI vehicle, TrafficEdge movementEdge)
    {
        RemoveWaitingVehicle(vehicle);
        safetyController.NotifyExited(vehicle, movementEdge);
    }

    // The conveyor observes the stop line before clearance/admission checks, so
    // an occupied exit cannot hide demand. Repeated probes never restart a wait.
    public void ObserveApproach(VehicleAI vehicle, TrafficEdge movement, bool stoppedAtLine)
    {
        if (RuleType != IntersectionRuleType.SmartTrafficLight || vehicle == null) return;
        var source = movement != null
            ? new LaneConnectionSource(movement.fromDirectionBit, movement.fromLaneIndex)
            : default;
        if (!stoppedAtLine || movement == null || !laneIndices.TryGetValue(source, out int index) ||
            index == ActiveSignalIndex)
        {
            RemoveWaitingVehicle(vehicle);
            return;
        }
        for (int i = 0; i < waitingVehicles.Count; i++)
        {
            WaitingVehicle waiting = waitingVehicles[i];
            if (waiting.Vehicle != vehicle) continue;
            if (!waiting.Source.Equals(source)) waiting.Since = simulationSeconds;
            waiting.Source = source;
            waiting.LastSeenTick = tickIndex;
            waitingVehicles[i] = waiting;
            return;
        }
        waitingVehicles.Add(new WaitingVehicle
        {
            Vehicle = vehicle, Source = source, Since = simulationSeconds, LastSeenTick = tickIndex
        });
    }

    private void TickSmartLights(float deltaTime)
    {
        simulationSeconds += deltaTime;
        tickIndex++;
        int selected = -1;
        double oldest = double.PositiveInfinity;
        for (int i = waitingVehicles.Count - 1; i >= 0; i--)
        {
            WaitingVehicle waiting = waitingVehicles[i];
            if (waiting.Vehicle == null || !waiting.Vehicle.isActiveAndEnabled ||
                waiting.Vehicle.currentEdge == null || waiting.Vehicle.conveyorCurrentSpeed > 0.05f ||
                waiting.LastSeenTick < tickIndex - 1 ||
                !laneIndices.TryGetValue(waiting.Source, out int index) || index == ActiveSignalIndex)
            {
                waitingVehicles.RemoveAt(i);
                continue;
            }
            if (simulationSeconds - waiting.Since + 0.000001d < SmartWaitSeconds) continue;
            if (waiting.Since < oldest || (waiting.Since == oldest && index < selected))
            {
                oldest = waiting.Since;
                selected = index;
            }
        }
        // Give drivers time to react; overdue groups must not flip every 0.05s.
        // This minimum is simulation time, like the demand threshold.
        if (selected < 0 || simulationSeconds - lastSwitchSeconds + 0.000001d < 1d) return;
        phaseIndex = phaseIndices.IndexOf(selected);
        lastSwitchSeconds = simulationSeconds;
        for (int i = waitingVehicles.Count - 1; i >= 0; i--)
            if (laneIndices[waitingVehicles[i].Source] == selected) waitingVehicles.RemoveAt(i);
    }

    private void RemoveWaitingVehicle(VehicleAI vehicle)
    {
        for (int i = waitingVehicles.Count - 1; i >= 0; i--)
            if (waitingVehicles[i].Vehicle == vehicle) waitingVehicles.RemoveAt(i);
    }

    public bool IsGreenForDirection(int directionBit) =>
        (ActiveDirectionMask & directionBit) != 0;

    public bool TryGetSignalIndex(int directionBit, int laneIndex, out int index) =>
        laneIndices.TryGetValue(new LaneConnectionSource(directionBit, laneIndex), out index);

    public bool IsGreenForLane(int directionBit, int laneIndex) =>
        TryGetSignalIndex(directionBit, laneIndex, out int index) && index == ActiveSignalIndex;

    private void BuildPhases(IntersectionData data, IReadOnlyList<TrafficEdge> movementEdges,
        IReadOnlyList<LaneConnectionSource> incomingLanes)
    {
        phaseMasks.Clear();
        phaseIndices.Clear();
        laneIndices.Clear();
        var sources = new List<LaneConnectionSource>();
        if (incomingLanes != null) sources.AddRange(incomingLanes);
        else if (movementEdges != null)
        {
            for (int i = 0; i < movementEdges.Count; i++)
            {
                TrafficEdge edge = movementEdges[i];
                if (edge == null ||
                    !IntersectionControlUtility.IsDirectionBit(edge.fromDirectionBit)) continue;
                var source = new LaneConnectionSource(edge.fromDirectionBit, edge.fromLaneIndex);
                if (!sources.Contains(source)) sources.Add(source);
            }
        }
        int directionMask = 0;
        sources.RemoveAll(source => source.LaneIndex < 0 ||
            !IntersectionControlUtility.IsDirectionBit(source.DirectionBit));
        for (int i = 0; i < sources.Count; i++) directionMask |= sources[i].DirectionBit;
        for (int i = 0; i < sources.Count; i++)
        {
            LaneConnectionSource source = sources[i];
            int index = IntersectionControlUtility.GetTrafficLightIndex(source.DirectionBit,
                source.LaneIndex, directionMask, data?.TrafficLightIndices);
            laneIndices[source] = index;
            if (!phaseIndices.Contains(index)) phaseIndices.Add(index);
        }
        phaseIndices.Sort();
        for (int i = 0; i < phaseIndices.Count; i++) phaseMasks.Add(0);
        foreach (var pair in laneIndices)
            phaseMasks[phaseIndices.IndexOf(pair.Value)] |= pair.Key.DirectionBit;
    }
}
