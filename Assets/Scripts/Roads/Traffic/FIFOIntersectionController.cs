using System.Collections.Generic;
using UnityEngine;

public class FIFOIntersectionController : IIntersectionController
{
    private const float MinimumApproachDecisionDistance = 0.35f;
    private const float ApproachDecisionMargin = 0.2f;

    private struct Arrival
    {
        public VehicleAI Vehicle;
        public VehicleSimulationId VehicleId;
        public TrafficEdge MovementEdge;
        public MovementId MovementId;
        public int Sequence;
    }

    public Vector2Int Cell { get; private set; }
    public IntersectionRuleType RuleType => IntersectionRuleType.FIFO;

    private readonly List<Arrival> _arrivalQueue = new List<Arrival>();
    private readonly Dictionary<VehicleAI, TrafficEdge> _activeMovements = new Dictionary<VehicleAI, TrafficEdge>();
    private readonly List<TrafficEdge> _movementEdges = new List<TrafficEdge>();
    private int _nextSequence;

    public void Initialize(Vector2Int cell, IntersectionData data, IReadOnlyList<TrafficEdge> movementEdges)
    {
        Cell = cell;
        _arrivalQueue.Clear();
        _activeMovements.Clear();
        _movementEdges.Clear();
        if (movementEdges != null)
        {
            _movementEdges.AddRange(movementEdges);
        }
        _nextSequence = 0;
    }

    public void Tick(float deltaTime)
    {
    }

    public bool CanEnter(VehicleAI vehicle, TrafficEdge fromEdge, TrafficEdge movementEdge, TrafficEdge toEdge)
    {
        return CanEnter(new TrafficMovementRequest(
            vehicle,
            fromEdge,
            movementEdge,
            toEdge));
    }

    public bool CanEnter(TrafficMovementRequest request)
    {
        VehicleAI vehicle = request.Vehicle;
        TrafficEdge fromEdge = request.FromEdge;
        TrafficEdge movementEdge = request.MovementEdge;
        if (vehicle == null || movementEdge == null) return false;

        if (!IsLeadApproachVehicle(vehicle, fromEdge))
        {
            RemoveQueuedVehicle(vehicle);
            return false;
        }

        if (!IsInsideApproachDecisionZone(vehicle, fromEdge))
        {
            RemoveQueuedVehicle(vehicle);
            return _activeMovements.Count == 0;
        }

        EnqueueIfNeeded(request);
        SortQueue();

        if (HasBlockedVehicleInside()) return false;
        if (_arrivalQueue.Count == 0 || _arrivalQueue[0].Vehicle != vehicle) return false;
        return _activeMovements.Count == 0;
    }

    public void NotifyEntered(VehicleAI vehicle, TrafficEdge movementEdge)
    {
        RemoveQueuedVehicle(vehicle);
        if (vehicle != null && movementEdge != null)
        {
            _activeMovements[vehicle] = movementEdge;
        }
    }

    public void NotifyExited(VehicleAI vehicle, TrafficEdge movementEdge)
    {
        if (vehicle != null)
        {
            _activeMovements.Remove(vehicle);
            RemoveQueuedVehicle(vehicle);
        }
    }

    private void EnqueueIfNeeded(TrafficMovementRequest request)
    {
        for (int i = 0; i < _arrivalQueue.Count; i++)
        {
            if (_arrivalQueue[i].VehicleId == request.VehicleId) return;
        }

        _arrivalQueue.Add(new Arrival
        {
            Vehicle = request.Vehicle,
            VehicleId = request.VehicleId,
            MovementEdge = request.MovementEdge,
            MovementId = request.MovementId,
            Sequence = _nextSequence++
        });
    }

    private void SortQueue()
    {
        _arrivalQueue.RemoveAll(a => a.Vehicle == null);
        _arrivalQueue.Sort((a, b) =>
        {
            int sequenceCompare = a.Sequence.CompareTo(b.Sequence);
            if (sequenceCompare != 0) return sequenceCompare;

            int edgeCompare = a.MovementEdge.edgeId.CompareTo(b.MovementEdge.edgeId);
            if (edgeCompare != 0) return edgeCompare;
            int movementCompare = a.MovementId.CompareTo(b.MovementId);
            return movementCompare != 0
                ? movementCompare
                : a.VehicleId.CompareTo(b.VehicleId);
        });
    }

    private void RemoveQueuedVehicle(VehicleAI vehicle)
    {
        _arrivalQueue.RemoveAll(a => a.Vehicle == vehicle);
    }

    private bool HasBlockedVehicleInside()
    {
        foreach (TrafficEdge movementEdge in _movementEdges)
        {
            if (ConveyorTrafficManager.Instance != null &&
                ConveyorTrafficManager.Instance.HasBlockedOccupantOnEdge(movementEdge))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsLeadApproachVehicle(VehicleAI vehicle, TrafficEdge fromEdge)
    {
        return vehicle != null &&
               fromEdge != null &&
               ConveyorTrafficManager.Instance != null &&
               ConveyorTrafficManager.Instance.IsLeadVehicleOnEdge(
                   vehicle,
                   fromEdge);
    }

    private bool IsInsideApproachDecisionZone(VehicleAI vehicle, TrafficEdge fromEdge)
    {
        if (vehicle == null || fromEdge == null) return false;

        float maximumSpeed = Mathf.Min(
            vehicle.GetMaximumSpeedUnitsPerSecond(),
            fromEdge.speedLimit);
        float deceleration = Mathf.Max(
            0.1f,
            vehicle.GetDecelerationUnitsPerSecondSquared());
        float brakingDistance =
            maximumSpeed * maximumSpeed /
            (2f * deceleration);
        float decisionDistance = Mathf.Max(
            MinimumApproachDecisionDistance,
            brakingDistance + ApproachDecisionMargin);
        float distanceToEntry = Mathf.Max(
            0f,
            fromEdge.totalLength - vehicle.conveyorDistanceOnEdge);
        return distanceToEntry <= decisionDistance;
    }
}
