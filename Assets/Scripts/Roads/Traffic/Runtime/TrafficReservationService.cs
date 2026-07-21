using System.Collections.Generic;

public sealed class TrafficReservationService
{
    private readonly Dictionary<TrafficEdge, List<TrafficEdgeReservation>>
        _reservationsByEdge = new Dictionary<TrafficEdge, List<TrafficEdgeReservation>>();

    public bool Reserve(
        TrafficEdge edge,
        VehicleAI vehicle,
        float minDistanceUnits,
        float maxDistanceUnits,
        int sequence,
        TrafficGraphVersion expectedGraphVersion = default)
    {
        if (edge == null || vehicle == null) return false;
        if (expectedGraphVersion.IsValid &&
            edge.graphVersion.IsValid &&
            edge.graphVersion != expectedGraphVersion)
        {
            return false;
        }

        List<TrafficEdgeReservation> reservations = GetOrCreateReservations(edge);
        TrafficEdgeReservation existing =
            reservations.Find(r => r != null && r.vehicle == vehicle);
        if (existing == null)
        {
            existing = new TrafficEdgeReservation { vehicle = vehicle };
            reservations.Add(existing);
        }

        existing.minDistance = minDistanceUnits < maxDistanceUnits
            ? minDistanceUnits
            : maxDistanceUnits;
        existing.maxDistance = maxDistanceUnits > minDistanceUnits
            ? maxDistanceUnits
            : minDistanceUnits;
        existing.sequence = sequence;
        SyncEdgeMirror(edge);
        return true;
    }

    public void Release(TrafficEdge edge, VehicleAI vehicle)
    {
        if (edge == null || vehicle == null) return;
        if (!_reservationsByEdge.TryGetValue(
                edge,
                out List<TrafficEdgeReservation> reservations))
        {
            return;
        }

        reservations.RemoveAll(r => r == null || r.vehicle == null || r.vehicle == vehicle);
        if (reservations.Count == 0)
        {
            _reservationsByEdge.Remove(edge);
        }

        SyncEdgeMirror(edge);
    }

    public IReadOnlyList<TrafficEdgeReservation> GetReservations(TrafficEdge edge)
    {
        return edge != null &&
               _reservationsByEdge.TryGetValue(
                   edge,
                   out List<TrafficEdgeReservation> reservations)
            ? reservations
            : System.Array.Empty<TrafficEdgeReservation>();
    }

    public int GetReservationCount(TrafficEdge edge)
    {
        return edge != null &&
               _reservationsByEdge.TryGetValue(
                   edge,
                   out List<TrafficEdgeReservation> reservations)
            ? reservations.Count
            : 0;
    }

    public bool HasReservations(TrafficEdge edge) => GetReservationCount(edge) > 0;

    public void Clear()
    {
        foreach (TrafficEdge edge in _reservationsByEdge.Keys)
        {
            if (edge != null && edge.reservations != null) edge.reservations.Clear();
        }

        _reservationsByEdge.Clear();
    }

    private List<TrafficEdgeReservation> GetOrCreateReservations(TrafficEdge edge)
    {
        if (!_reservationsByEdge.TryGetValue(
                edge,
                out List<TrafficEdgeReservation> reservations))
        {
            reservations = new List<TrafficEdgeReservation>();
            _reservationsByEdge.Add(edge, reservations);
        }

        return reservations;
    }

    private void SyncEdgeMirror(TrafficEdge edge)
    {
        if (edge == null || edge.reservations == null) return;
        edge.reservations.Clear();
        if (!_reservationsByEdge.TryGetValue(
                edge,
                out List<TrafficEdgeReservation> reservations))
        {
            return;
        }

        edge.reservations.AddRange(reservations);
    }
}
