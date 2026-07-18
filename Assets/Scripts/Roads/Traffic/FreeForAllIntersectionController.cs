using System.Collections.Generic;
using UnityEngine;

public class FreeForAllIntersectionController : IIntersectionController
{
    private class ApproachRequest
    {
        public TrafficEdge movementEdge;
        public MovementId movementId;
        public VehicleSimulationId vehicleId;
        public int sequence;
        public int lastSeenTick;
    }

    private const float MovementPathClearance = 0.12f;
    private const float MinimumApproachDecisionDistance = 0.35f;
    private const float ApproachDecisionMargin = 0.2f;
    private readonly Dictionary<VehicleAI, TrafficEdge> _activeMovements = new Dictionary<VehicleAI, TrafficEdge>();
    private readonly List<TrafficEdge> _movementEdges = new List<TrafficEdge>();
    private readonly Dictionary<VehicleAI, ApproachRequest> _approachRequests =
        new Dictionary<VehicleAI, ApproachRequest>();
    private readonly List<VehicleAI> _staleApproachVehicles = new List<VehicleAI>();
    private int _tickIndex;
    private int _nextApproachSequence;

    public Vector2Int Cell { get; private set; }
    public IntersectionRuleType RuleType => IntersectionRuleType.FreeForAll;

    public void Initialize(Vector2Int cell, IntersectionData data, IReadOnlyList<TrafficEdge> movementEdges)
    {
        Cell = cell;
        _activeMovements.Clear();
        _movementEdges.Clear();
        _approachRequests.Clear();
        _staleApproachVehicles.Clear();
        _tickIndex = 0;
        _nextApproachSequence = 0;
        if (movementEdges != null)
        {
            _movementEdges.AddRange(movementEdges);
        }
    }

    public void Tick(float deltaTime)
    {
        _tickIndex++;
        _staleApproachVehicles.Clear();
        foreach (KeyValuePair<VehicleAI, ApproachRequest> request in _approachRequests)
        {
            if (request.Key == null ||
                request.Value == null ||
                request.Value.lastSeenTick < _tickIndex - 1)
            {
                _staleApproachVehicles.Add(request.Key);
            }
        }

        foreach (VehicleAI staleVehicle in _staleApproachVehicles)
        {
            _approachRequests.Remove(staleVehicle);
        }
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
            _approachRequests.Remove(vehicle);
            return false;
        }

        bool isInsideDecisionZone = IsInsideApproachDecisionZone(vehicle, fromEdge);
        ApproachRequest queuedRequest = isInsideDecisionZone
            ? RegisterApproach(request)
            : null;
        if (!isInsideDecisionZone)
        {
            _approachRequests.Remove(vehicle);
        }

        if (HasBlockedVehicleInside()) return false;

        foreach (KeyValuePair<VehicleAI, TrafficEdge> active in _activeMovements)
        {
            if (active.Key == null || active.Key == vehicle || active.Value == null) continue;
            if (active.Value == movementEdge) continue;
            if (PathsConflict(movementEdge, active.Value)) return false;
        }

        foreach (KeyValuePair<VehicleAI, ApproachRequest> other in _approachRequests)
        {
            if (other.Key == null ||
                other.Key == vehicle ||
                other.Value == null ||
                other.Value.movementEdge == null ||
                other.Value.movementEdge == movementEdge)
            {
                continue;
            }

            if (queuedRequest != null &&
                other.Value.sequence >= queuedRequest.sequence)
            {
                continue;
            }

            if (PathsConflict(movementEdge, other.Value.movementEdge))
            {
                return false;
            }
        }

        return true;
    }

    public void NotifyEntered(VehicleAI vehicle, TrafficEdge movementEdge)
    {
        if (vehicle != null && movementEdge != null)
        {
            _approachRequests.Remove(vehicle);
            _activeMovements[vehicle] = movementEdge;
        }
    }

    public void NotifyExited(VehicleAI vehicle, TrafficEdge movementEdge)
    {
        if (vehicle != null)
        {
            _approachRequests.Remove(vehicle);
            _activeMovements.Remove(vehicle);
        }
    }

    private ApproachRequest RegisterApproach(TrafficMovementRequest movementRequest)
    {
        VehicleAI vehicle = movementRequest.Vehicle;
        TrafficEdge movementEdge = movementRequest.MovementEdge;
        if (!_approachRequests.TryGetValue(vehicle, out ApproachRequest request) ||
            request == null ||
            request.movementEdge != movementEdge)
        {
            request = new ApproachRequest
            {
                movementEdge = movementEdge,
                movementId = movementRequest.MovementId,
                vehicleId = movementRequest.VehicleId,
                sequence = _nextApproachSequence++
            };
            _approachRequests[vehicle] = request;
        }

        request.lastSeenTick = _tickIndex;
        return request;
    }

    private bool IsLeadApproachVehicle(VehicleAI vehicle, TrafficEdge fromEdge)
    {
        return vehicle != null &&
               fromEdge != null &&
               vehicle.currentEdge == fromEdge &&
               fromEdge.GetVehicleAhead(vehicle) == null;
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

    private bool HasBlockedVehicleInside()
    {
        foreach (TrafficEdge movementEdge in _movementEdges)
        {
            if (movementEdge == null || movementEdge.occupants == null) continue;

            foreach (VehicleAI occupant in movementEdge.occupants)
            {
                if (occupant == null ||
                    occupant.currentEdge != movementEdge ||
                    !occupant.isConveyorMoving)
                {
                    continue;
                }

                if (occupant.trafficWasBlocked)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool PathsConflict(TrafficEdge a, TrafficEdge b)
    {
        if (a == b) return true;
        if (a.waypoints == null || b.waypoints == null) return true;

        for (int i = 1; i < a.waypoints.Count; i++)
        {
            Vector2 a0 = ToXZ(a.waypoints[i - 1]);
            Vector2 a1 = ToXZ(a.waypoints[i]);

            for (int j = 1; j < b.waypoints.Count; j++)
            {
                Vector2 b0 = ToXZ(b.waypoints[j - 1]);
                Vector2 b1 = ToXZ(b.waypoints[j]);
                if (SegmentsIntersect(a0, a1, b0, b1) ||
                    SegmentDistance(a0, a1, b0, b1) < MovementPathClearance)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private Vector2 ToXZ(Vector3 point)
    {
        return new Vector2(point.x, point.z);
    }

    private bool SegmentsIntersect(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
    {
        const float epsilon = 0.0001f;
        float d1 = Cross(a1 - a0, b0 - a0);
        float d2 = Cross(a1 - a0, b1 - a0);
        float d3 = Cross(b1 - b0, a0 - b0);
        float d4 = Cross(b1 - b0, a1 - b0);

        if (((d1 > epsilon && d2 < -epsilon) || (d1 < -epsilon && d2 > epsilon)) &&
            ((d3 > epsilon && d4 < -epsilon) || (d3 < -epsilon && d4 > epsilon)))
        {
            return true;
        }

        return IsPointOnSegment(a0, b0, a1, epsilon) ||
               IsPointOnSegment(a0, b1, a1, epsilon) ||
               IsPointOnSegment(b0, a0, b1, epsilon) ||
               IsPointOnSegment(b0, a1, b1, epsilon);
    }

    private float SegmentDistance(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
    {
        return Mathf.Min(
            Mathf.Min(
                PointToSegmentDistance(a0, b0, b1),
                PointToSegmentDistance(a1, b0, b1)),
            Mathf.Min(
                PointToSegmentDistance(b0, a0, a1),
                PointToSegmentDistance(b1, a0, a1)));
    }

    private float PointToSegmentDistance(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= 0.000001f)
        {
            return Vector2.Distance(point, start);
        }

        float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
        return Vector2.Distance(point, start + segment * t);
    }

    private float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private bool IsPointOnSegment(Vector2 a, Vector2 p, Vector2 b, float epsilon)
    {
        if (Mathf.Abs(Cross(p - a, b - a)) > epsilon) return false;
        return p.x >= Mathf.Min(a.x, b.x) - epsilon &&
               p.x <= Mathf.Max(a.x, b.x) + epsilon &&
               p.y >= Mathf.Min(a.y, b.y) - epsilon &&
               p.y <= Mathf.Max(a.y, b.y) + epsilon;
    }
}
