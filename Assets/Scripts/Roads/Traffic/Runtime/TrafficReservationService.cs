public sealed class TrafficReservationService
{
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

        edge.SetReservation(
            vehicle,
            minDistanceUnits,
            maxDistanceUnits,
            sequence);
        return true;
    }

    public void Release(TrafficEdge edge, VehicleAI vehicle)
    {
        if (edge == null || vehicle == null) return;
        edge.RemoveReservation(vehicle);
    }
}
