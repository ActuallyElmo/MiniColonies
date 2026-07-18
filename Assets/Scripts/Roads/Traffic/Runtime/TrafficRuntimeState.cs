using System.Collections.Generic;

public sealed class TrafficOccupantRecord
{
    public VehicleSimulationId VehicleId;
    public VehicleAI Vehicle;
    public TrafficEdge Edge;
    public float DistanceUnits;
    public float SpeedUnitsPerSecond;
    public int Sequence;
}

public sealed class TrafficRuntimeState
{
    private readonly Dictionary<VehicleSimulationId, TrafficOccupantRecord>
        _occupantsByVehicle = new Dictionary<VehicleSimulationId, TrafficOccupantRecord>();
    private readonly Dictionary<TrafficEdge, List<TrafficOccupantRecord>>
        _occupantsByEdge = new Dictionary<TrafficEdge, List<TrafficOccupantRecord>>();
    private int _nextSequence;

    public bool Register(
        VehicleAI vehicle,
        TrafficEdge edge,
        float distanceUnits)
    {
        if (vehicle == null || edge == null) return false;
        VehicleSimulationId id = vehicle.EnsureSimulationId();
        Unregister(vehicle);

        var record = new TrafficOccupantRecord
        {
            VehicleId = id,
            Vehicle = vehicle,
            Edge = edge,
            DistanceUnits = distanceUnits,
            SpeedUnitsPerSecond = vehicle.conveyorCurrentSpeed,
            Sequence = _nextSequence++
        };
        _occupantsByVehicle[id] = record;
        GetOrCreateEdgeList(edge).Add(record);
        SortEdge(edge);
        SyncEdgeMirror(edge);
        return true;
    }

    public void Unregister(VehicleAI vehicle)
    {
        if (vehicle == null || !vehicle.SimulationId.IsValid) return;
        if (!_occupantsByVehicle.TryGetValue(
                vehicle.SimulationId,
                out TrafficOccupantRecord record))
        {
            return;
        }

        _occupantsByVehicle.Remove(vehicle.SimulationId);
        if (_occupantsByEdge.TryGetValue(record.Edge, out List<TrafficOccupantRecord> list))
        {
            list.Remove(record);
            SyncEdgeMirror(record.Edge);
            if (list.Count == 0) _occupantsByEdge.Remove(record.Edge);
        }
    }

    public bool Transfer(
        VehicleAI vehicle,
        TrafficEdge targetEdge,
        float distanceUnits)
    {
        if (vehicle == null || targetEdge == null) return false;
        if (!_occupantsByVehicle.TryGetValue(
                vehicle.EnsureSimulationId(),
                out TrafficOccupantRecord record))
        {
            return Register(vehicle, targetEdge, distanceUnits);
        }

        TrafficEdge previous = record.Edge;
        if (_occupantsByEdge.TryGetValue(previous, out List<TrafficOccupantRecord> oldList))
        {
            oldList.Remove(record);
            SyncEdgeMirror(previous);
            if (oldList.Count == 0) _occupantsByEdge.Remove(previous);
        }

        record.Edge = targetEdge;
        record.DistanceUnits = distanceUnits;
        record.SpeedUnitsPerSecond = vehicle.conveyorCurrentSpeed;
        GetOrCreateEdgeList(targetEdge).Add(record);
        SortEdge(targetEdge);
        SyncEdgeMirror(targetEdge);
        return true;
    }

    public void UpdateVehicle(VehicleAI vehicle)
    {
        if (vehicle == null || !vehicle.SimulationId.IsValid) return;
        if (!_occupantsByVehicle.TryGetValue(
                vehicle.SimulationId,
                out TrafficOccupantRecord record))
        {
            return;
        }

        record.DistanceUnits = vehicle.conveyorDistanceOnEdge;
        record.SpeedUnitsPerSecond = vehicle.conveyorCurrentSpeed;
        SortEdge(record.Edge);
        SyncEdgeMirror(record.Edge);
    }

    public VehicleAI GetVehicleAhead(VehicleAI vehicle, TrafficEdge edge)
    {
        if (vehicle == null || edge == null ||
            !_occupantsByEdge.TryGetValue(edge, out List<TrafficOccupantRecord> list))
        {
            return null;
        }

        float distance = vehicle.conveyorDistanceOnEdge;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            TrafficOccupantRecord candidate = list[i];
            if (candidate.Vehicle == null || candidate.Vehicle == vehicle) continue;
            if (candidate.DistanceUnits > distance) return candidate.Vehicle;
        }

        return null;
    }

    public IReadOnlyList<TrafficOccupantRecord> GetOccupants(TrafficEdge edge)
    {
        return edge != null && _occupantsByEdge.TryGetValue(edge, out List<TrafficOccupantRecord> list)
            ? list
            : System.Array.Empty<TrafficOccupantRecord>();
    }

    public void Clear()
    {
        foreach (TrafficEdge edge in _occupantsByEdge.Keys)
        {
            if (edge != null && edge.occupants != null) edge.occupants.Clear();
        }
        _occupantsByEdge.Clear();
        _occupantsByVehicle.Clear();
    }

    private List<TrafficOccupantRecord> GetOrCreateEdgeList(TrafficEdge edge)
    {
        if (!_occupantsByEdge.TryGetValue(edge, out List<TrafficOccupantRecord> list))
        {
            list = new List<TrafficOccupantRecord>();
            _occupantsByEdge.Add(edge, list);
        }

        return list;
    }

    private void SortEdge(TrafficEdge edge)
    {
        if (edge == null ||
            !_occupantsByEdge.TryGetValue(edge, out List<TrafficOccupantRecord> list))
        {
            return;
        }

        list.Sort((left, right) =>
        {
            int distance = right.DistanceUnits.CompareTo(left.DistanceUnits);
            return distance != 0
                ? distance
                : left.VehicleId.CompareTo(right.VehicleId);
        });
    }

    private void SyncEdgeMirror(TrafficEdge edge)
    {
        if (edge == null) return;
        edge.occupants.Clear();
        if (!_occupantsByEdge.TryGetValue(edge, out List<TrafficOccupantRecord> list))
        {
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Vehicle != null) edge.occupants.Add(list[i].Vehicle);
        }
    }
}
