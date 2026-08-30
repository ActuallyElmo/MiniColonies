using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ManagedTrafficGraphAdapterResult
{
    public readonly List<TrafficNode> Nodes = new List<TrafficNode>();
    public readonly List<TrafficEdge> Edges = new List<TrafficEdge>();
}

public static class ManagedTrafficGraphAdapter
{
    public static bool TryBuild(
        TrafficGraphSnapshot graph,
        TrafficDiagnosticCollection diagnostics,
        out ManagedTrafficGraphAdapterResult result)
    {
        if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));
        result = null;
        if (graph == null)
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.MissingLaneReference,
                "Adapter cannot build from a null traffic graph.");
            return false;
        }

        result = new ManagedTrafficGraphAdapterResult();
        var laneSegmentStartNodes = new Dictionary<LaneSegmentId, TrafficNode>();
        var laneSegmentEndNodes = new Dictionary<LaneSegmentId, TrafficNode>();
        var laneDistanceNodes = new Dictionary<string, TrafficNode>();
        int nextEdgeId = 0;

        for (int i = 0; i < graph.LaneSegments.Count; i++)
        {
            LaneTraversalSegmentRecord segment = graph.LaneSegments[i];
            if (!graph.TryGetLane(segment.LaneId, out LaneRecord lane) ||
                !graph.TryGetGeometry(segment.GeometryId, out TrafficGeometryRecord geometry))
            {
                diagnostics.AddError(
                    TrafficDiagnosticCode.MissingLaneReference,
                    "Adapter cannot resolve a lane segment reference.");
                return false;
            }

            TrafficNode start = GetOrCreateLaneDistanceNode(
                graph,
                result,
                laneDistanceNodes,
                lane.Id,
                segment.StartDistanceUnits,
                geometry.Samples[0]);
            TrafficNode end = GetOrCreateLaneDistanceNode(
                graph,
                result,
                laneDistanceNodes,
                lane.Id,
                segment.EndDistanceUnits,
                geometry.Samples[geometry.Samples.Count - 1]);
            TrafficEdge edge = new TrafficEdge(
                start,
                end,
                lane.SpeedLimitUnitsPerSecond)
            {
                edgeId = nextEdgeId++,
                graphVersion = graph.Version,
                stableSectionId = lane.SectionId,
                stableLaneId = lane.Id,
                stableLaneSegmentId = segment.Id,
                requiredPermissions = lane.AllowedPermissions,
                requiredCapabilities = lane.AllowedCapabilities,
                kind = TrafficEdgeKind.RoadLane,
                laneIndex = lane.LaneOrdinal,
                totalLanes = lane.LaneCountInDirection,
                fromLaneIndex = lane.LaneOrdinal,
                toLaneIndex = lane.LaneOrdinal,
                fromDirectionBit = lane.StartLegDirectionBit,
                toDirectionBit = lane.EndLegDirectionBit,
                edgeColor = Color.white
            };
            CopyWaypoints(edge, geometry.Samples);

            start.outgoingEdges.Add(edge);
            edge.RecalculateLength();
            result.Edges.Add(edge);
            laneSegmentStartNodes.Add(segment.Id, start);
            laneSegmentEndNodes.Add(segment.Id, end);
        }

        for (int i = 0; i < graph.Movements.Count; i++)
        {
            MovementRecord movement = graph.Movements[i];
            if (!graph.TryGetLane(movement.SourceLaneId, out LaneRecord sourceLane) ||
                !graph.TryGetLane(movement.TargetLaneId, out LaneRecord targetLane) ||
                !graph.TryGetGeometry(movement.GeometryId, out TrafficGeometryRecord geometry) ||
                !graph.TryGetMovementOwner(movement.OwnerId, out MovementOwnerRecord owner) ||
                !laneSegmentEndNodes.TryGetValue(movement.SourceSegmentId, out TrafficNode start) ||
                !laneSegmentStartNodes.TryGetValue(movement.TargetSegmentId, out TrafficNode end))
            {
                diagnostics.AddError(
                    TrafficDiagnosticCode.MissingLaneReference,
                    "Adapter cannot resolve a movement reference.");
                return false;
            }

            TrafficEdge edge = new TrafficEdge(
                start,
                end,
                Mathf.Min(
                    sourceLane.SpeedLimitUnitsPerSecond,
                    targetLane.SpeedLimitUnitsPerSecond),
                movement.Kind == TrafficMovementKind.Intersection)
            {
                edgeId = nextEdgeId++,
                graphVersion = graph.Version,
                stableSectionId = sourceLane.SectionId,
                stableLaneId = sourceLane.Id,
                stableMovementId = movement.Id,
                stableMovementOwnerId = movement.OwnerId,
                requiredPermissions = movement.RequiredPermissions,
                requiredCapabilities = movement.RequiredCapabilities,
                kind = ConvertMovementKind(movement.Kind),
                isIntersection = movement.Kind == TrafficMovementKind.Intersection,
                isMergeEdge = movement.Kind == TrafficMovementKind.OptionalLaneChange ||
                              movement.Kind == TrafficMovementKind.MandatoryMerge,
                isUTurn = movement.Kind == TrafficMovementKind.RoadEndUTurn,
                laneIndex = sourceLane.LaneOrdinal,
                totalLanes = sourceLane.LaneCountInDirection,
                fromLaneIndex = sourceLane.LaneOrdinal,
                toLaneIndex = targetLane.LaneOrdinal,
                fromDirectionBit = movement.FromDirectionBit,
                toDirectionBit = movement.ToDirectionBit,
                turnType = movement.TurnType,
                conflictMask = movement.ApproachLegMask,
                hasControlledNodeCell =
                    owner.Kind == TrafficMovementOwnerKind.ControlledIntersection ||
                    owner.Kind == TrafficMovementOwnerKind.TransitionBoundary ||
                    owner.Kind == TrafficMovementOwnerKind.RoadEnd,
                controlledNodeCell = owner.PrimaryCell,
                transitionCell = owner.PrimaryCell,
                isRoadTypeTransition =
                    owner.Kind == TrafficMovementOwnerKind.TransitionBoundary,
                transitionPriority = movement.PolicyPriority,
                edgeColor = Color.yellow
            };
            CopyWaypoints(edge, geometry.Samples);

            start.outgoingEdges.Add(edge);
            edge.RecalculateLength();
            result.Edges.Add(edge);
        }

        return true;
    }

    private static TrafficNode GetOrCreateLaneDistanceNode(
        TrafficGraphSnapshot graph,
        ManagedTrafficGraphAdapterResult result,
        Dictionary<string, TrafficNode> nodesByLaneDistance,
        LaneId laneId,
        float distanceUnits,
        Vector3 position)
    {
        string key = $"{laneId.Value:X16}:{distanceUnits:0.###}";
        if (nodesByLaneDistance.TryGetValue(key, out TrafficNode node))
        {
            return node;
        }

        node = new TrafficNode(position)
        {
            graphVersion = graph.Version
        };
        nodesByLaneDistance.Add(key, node);
        result.Nodes.Add(node);
        return node;
    }

    private static TrafficEdgeKind ConvertMovementKind(TrafficMovementKind kind)
    {
        switch (kind)
        {
            case TrafficMovementKind.OptionalLaneChange:
                return TrafficEdgeKind.LaneChange;
            case TrafficMovementKind.MandatoryMerge:
            case TrafficMovementKind.LaneExpansion:
            case TrafficMovementKind.LaneContinuation:
                return TrafficEdgeKind.RoadTypeTransition;
            case TrafficMovementKind.Intersection:
                return TrafficEdgeKind.IntersectionMovement;
            case TrafficMovementKind.RoadEndUTurn:
                return TrafficEdgeKind.RoadEndUTurn;
            default:
                return TrafficEdgeKind.RoadLane;
        }
    }

    private static void CopyWaypoints(
        TrafficEdge edge,
        IReadOnlyList<Vector3> samples)
    {
        edge.waypoints.Clear();
        for (int i = 0; i < samples.Count; i++) edge.waypoints.Add(samples[i]);
    }
}
