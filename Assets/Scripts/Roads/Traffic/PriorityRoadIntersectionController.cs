using System.Collections.Generic;
using UnityEngine;

public sealed class PriorityRoadIntersectionController : IIntersectionController
{
    private readonly FreeForAllIntersectionController safetyController =
        new FreeForAllIntersectionController();
    private readonly HashSet<VehicleAI> activePriorityVehicles =
        new HashSet<VehicleAI>();
    private int priorityDirectionBitA;
    private int priorityDirectionBitB;
    private int tickIndex;
    private int lastPriorityRequestTick = int.MinValue;

    public Vector2Int Cell { get; private set; }
    public IntersectionRuleType RuleType => IntersectionRuleType.PriorityRoad;
    public int PriorityDirectionBitA => priorityDirectionBitA;
    public int PriorityDirectionBitB => priorityDirectionBitB;

    public void Initialize(
        Vector2Int cell,
        IntersectionData data,
        IReadOnlyList<TrafficEdge> movementEdges)
    {
        Cell = cell;
        priorityDirectionBitA = data != null ? data.PriorityDirectionBitA : 0;
        priorityDirectionBitB = data != null ? data.PriorityDirectionBitB : 0;
        activePriorityVehicles.Clear();
        tickIndex = 0;
        lastPriorityRequestTick = int.MinValue;
        safetyController.Initialize(cell, data, movementEdges);
    }

    public void Tick(float deltaTime)
    {
        tickIndex++;
        activePriorityVehicles.RemoveWhere(vehicle => vehicle == null);
        safetyController.Tick(deltaTime);
    }

    public bool CanEnter(
        VehicleAI vehicle,
        TrafficEdge fromEdge,
        TrafficEdge movementEdge,
        TrafficEdge toEdge) =>
        CanEnter(new TrafficMovementRequest(vehicle, fromEdge, movementEdge, toEdge));

    public bool CanEnter(TrafficMovementRequest request)
    {
        if (request.MovementEdge == null) return false;
        bool priority = IsPriorityDirection(request.MovementEdge.fromDirectionBit);
        if (priority)
        {
            lastPriorityRequestTick = tickIndex;
        }
        else if (activePriorityVehicles.Count > 0 ||
                 lastPriorityRequestTick >= tickIndex - 1)
        {
            return false;
        }

        return safetyController.CanEnter(request);
    }

    public void NotifyEntered(VehicleAI vehicle, TrafficEdge movementEdge)
    {
        if (vehicle != null && movementEdge != null &&
            IsPriorityDirection(movementEdge.fromDirectionBit))
        {
            activePriorityVehicles.Add(vehicle);
        }
        safetyController.NotifyEntered(vehicle, movementEdge);
    }

    public void NotifyExited(VehicleAI vehicle, TrafficEdge movementEdge)
    {
        if (vehicle != null) activePriorityVehicles.Remove(vehicle);
        safetyController.NotifyExited(vehicle, movementEdge);
    }

    public bool IsPriorityDirection(int directionBit)
    {
        // An unconfigured priority road behaves like free-for-all until the
        // player chooses its approaches, avoiding a permanently blocked road.
        if (priorityDirectionBitA == 0 && priorityDirectionBitB == 0) return true;
        return directionBit == priorityDirectionBitA ||
               directionBit == priorityDirectionBitB;
    }
}
