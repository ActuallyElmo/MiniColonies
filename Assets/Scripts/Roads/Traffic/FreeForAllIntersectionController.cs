using System.Collections.Generic;
using UnityEngine;

public class FreeForAllIntersectionController : IIntersectionController
{
    private class ReservedMovement
    {
        public TrafficEdge MovementEdge;
        public int LastSeenTick;
    }

    private const float MovementPathClearance = 0.12f;
    private const float ApproachBufferMinUnits = 0.65f;
    private const float ApproachBufferVehicleLengthMultiplier = 3f;
    private readonly Dictionary<VehicleAI, TrafficEdge> _activeMovements = new Dictionary<VehicleAI, TrafficEdge>();
    private readonly Dictionary<VehicleAI, ReservedMovement> _reservedMovements =
        new Dictionary<VehicleAI, ReservedMovement>();
    private readonly List<VehicleAI> _staleReservedVehicles = new List<VehicleAI>();
    private int _tickIndex;

    public Vector2Int Cell { get; private set; }
    public IntersectionRuleType RuleType => IntersectionRuleType.FreeForAll;

    public void Initialize(Vector2Int cell, IntersectionData data, IReadOnlyList<TrafficEdge> movementEdges)
    {
        Cell = cell;
        _activeMovements.Clear();
        _reservedMovements.Clear();
        _staleReservedVehicles.Clear();
        _tickIndex = 0;
    }

    public void Tick(float deltaTime)
    {
        _tickIndex++;
        _staleReservedVehicles.Clear();
        foreach (KeyValuePair<VehicleAI, ReservedMovement> reserved in _reservedMovements)
        {
            VehicleAI vehicle = reserved.Key;
            ReservedMovement movement = reserved.Value;
            if (vehicle == null ||
                movement == null ||
                movement.MovementEdge == null ||
                movement.LastSeenTick < _tickIndex - 1 ||
                vehicle.currentEdge == movement.MovementEdge)
            {
                _staleReservedVehicles.Add(vehicle);
            }
        }

        foreach (VehicleAI staleVehicle in _staleReservedVehicles)
        {
            _reservedMovements.Remove(staleVehicle);
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
        if (vehicle == null || fromEdge == null || movementEdge == null) return false;

        if (!IsInsideApproachBuffer(vehicle, fromEdge))
        {
            _reservedMovements.Remove(vehicle);
            return true;
        }

        if (!IsLeadApproachVehicle(vehicle, fromEdge))
        {
            _reservedMovements.Remove(vehicle);
            return false;
        }

        if (HasMatchingReservation(vehicle, movementEdge))
        {
            RefreshReservation(vehicle);
            return true;
        }

        foreach (KeyValuePair<VehicleAI, TrafficEdge> active in _activeMovements)
        {
            if (active.Key == null || active.Key == vehicle || active.Value == null) continue;
            if (active.Value == movementEdge) continue;
            if (PathsConflict(movementEdge, active.Value)) return false;
        }

        foreach (KeyValuePair<VehicleAI, ReservedMovement> reserved in _reservedMovements)
        {
            if (reserved.Key == null ||
                reserved.Key == vehicle ||
                reserved.Value == null ||
                reserved.Value.MovementEdge == null ||
                reserved.Value.MovementEdge == movementEdge)
            {
                continue;
            }

            if (PathsConflict(movementEdge, reserved.Value.MovementEdge))
            {
                return false;
            }
        }

        if (IsInsideApproachBuffer(vehicle, fromEdge))
        {
            _reservedMovements[vehicle] = new ReservedMovement
            {
                MovementEdge = movementEdge,
                LastSeenTick = _tickIndex
            };
        }

        return true;
    }

    public void NotifyEntered(VehicleAI vehicle, TrafficEdge movementEdge)
    {
        if (vehicle != null && movementEdge != null)
        {
            _reservedMovements.Remove(vehicle);
            _activeMovements[vehicle] = movementEdge;
        }
    }

    public void NotifyExited(VehicleAI vehicle, TrafficEdge movementEdge)
    {
        if (vehicle != null)
        {
            _activeMovements.Remove(vehicle);
            _reservedMovements.Remove(vehicle);
        }
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

    private bool HasMatchingReservation(VehicleAI vehicle, TrafficEdge movementEdge)
    {
        return vehicle != null &&
               movementEdge != null &&
               _reservedMovements.TryGetValue(
                   vehicle,
                   out ReservedMovement reserved) &&
               reserved != null &&
               reserved.MovementEdge == movementEdge;
    }

    private void RefreshReservation(VehicleAI vehicle)
    {
        if (vehicle == null) return;
        if (_reservedMovements.TryGetValue(
                vehicle,
                out ReservedMovement reserved) &&
            reserved != null)
        {
            reserved.LastSeenTick = _tickIndex;
        }
    }

    private bool IsInsideApproachBuffer(VehicleAI vehicle, TrafficEdge fromEdge)
    {
        if (vehicle == null || fromEdge == null) return false;

        float bufferDistance = Mathf.Max(
            ApproachBufferMinUnits,
            vehicle.GetVehicleLengthUnits() *
            ApproachBufferVehicleLengthMultiplier);
        float distanceToEntry = Mathf.Max(
            0f,
            fromEdge.totalLength - vehicle.conveyorDistanceOnEdge);
        return distanceToEntry <= bufferDistance;
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
