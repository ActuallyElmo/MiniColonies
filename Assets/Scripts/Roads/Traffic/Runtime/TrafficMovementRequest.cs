public readonly struct TrafficMovementRequest
{
    public readonly VehicleAI Vehicle;
    public readonly VehicleSimulationId VehicleId;
    public readonly TrafficGraphVersion GraphVersion;
    public readonly MovementId MovementId;
    public readonly TrafficEdge FromEdge;
    public readonly TrafficEdge MovementEdge;
    public readonly TrafficEdge ToEdge;

    public TrafficMovementRequest(
        VehicleAI vehicle,
        TrafficEdge fromEdge,
        TrafficEdge movementEdge,
        TrafficEdge toEdge)
    {
        Vehicle = vehicle;
        VehicleId = vehicle != null
            ? vehicle.EnsureSimulationId()
            : VehicleSimulationId.Invalid;
        GraphVersion = movementEdge != null
            ? movementEdge.graphVersion
            : TrafficGraphVersion.Invalid;
        MovementId = movementEdge != null
            ? movementEdge.stableMovementId
            : MovementId.Invalid;
        FromEdge = fromEdge;
        MovementEdge = movementEdge;
        ToEdge = toEdge;
    }
}
