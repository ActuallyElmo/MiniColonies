using System.Collections.Generic;
using UnityEngine;

public interface IIntersectionController
{
    Vector2Int Cell { get; }
    IntersectionRuleType RuleType { get; }
    void Initialize(Vector2Int cell, IntersectionData data, IReadOnlyList<TrafficEdge> movementEdges);
    void Tick(float deltaTime);
    bool CanEnter(TrafficMovementRequest request);
    bool CanEnter(VehicleAI vehicle, TrafficEdge fromEdge, TrafficEdge movementEdge, TrafficEdge toEdge);
    void NotifyEntered(VehicleAI vehicle, TrafficEdge movementEdge);
    void NotifyExited(VehicleAI vehicle, TrafficEdge movementEdge);
}
