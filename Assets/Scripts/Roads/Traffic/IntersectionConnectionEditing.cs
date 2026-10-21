using System.Collections.Generic;
using UnityEngine;

/// <summary>Validates edits against the same immutable inputs used by the compiler.</summary>
public static class IntersectionConnectionEditing
{
    public static bool CanConnect(RoadNetworkSnapshot roads, TrafficGraphSnapshot graph,
        ControlledNodeId intersection, LaneId entry, LaneId exit)
    {
        if (roads == null || graph == null ||
            roads.RoadAuthoringRevision != graph.SourceRoadRevision ||
            roads.BuildingAuthoringRevision != graph.SourceBuildingRevision ||
            !graph.TryGetControlledNode(intersection, out var node) ||
            node.NodeKind != RoadNodeKind.Intersection ||
            !Contains(node.IncomingLaneIds, entry) || !Contains(node.OutgoingLaneIds, exit) ||
            !graph.TryGetLane(entry, out var source) || !graph.TryGetLane(exit, out var target))
            return false;

        return source.EndLegDirectionBit != target.StartLegDirectionBit &&
            (source.AllowedPermissions & target.AllowedPermissions) != RoadPermissionMask.None &&
            (source.AllowedCapabilities & target.AllowedCapabilities) != VehicleCapabilityMask.None &&
            SupportsIntersection(roads, graph, source) && SupportsIntersection(roads, graph, target);
    }

    public static bool TryCreateRules(RoadNetworkSnapshot roads, TrafficGraphSnapshot graph,
        ControlledNodeId intersection, LaneId entry, LaneId exit, bool connected,
        out List<LaneConnectionRule> rules)
    {
        rules = null;
        if (!CanConnect(roads, graph, intersection, entry, exit)) return false;
        graph.TryGetControlledNode(intersection, out var node);
        graph.TryGetLane(entry, out var source);
        graph.TryGetLane(exit, out var target);
        var result = new List<LaneConnectionRule>(node.MovementIds.Count + 1);
        bool exists = false;
        foreach (var id in node.MovementIds)
        {
            if (!graph.TryGetMovement(id, out var movement) ||
                movement.Kind != TrafficMovementKind.Intersection) continue;
            bool matches = movement.SourceLaneId == entry && movement.TargetLaneId == exit;
            exists |= matches;
            if (matches && !connected) continue;
            if (graph.TryGetLane(movement.SourceLaneId, out var from) &&
                graph.TryGetLane(movement.TargetLaneId, out var to))
                result.Add(ToRule(from, to));
        }
        if (exists == connected) return false;
        if (connected) result.Add(ToRule(source, target));
        rules = result;
        return true;
    }

    public static List<LaneConnectionSource> CollectIncomingSources(
        TrafficGraphSnapshot graph,
        ControlledNodeRecord node)
    {
        var result = new List<LaneConnectionSource>(
            node != null ? node.IncomingLaneIds.Count : 0);
        if (graph == null || node == null) return result;
        for (int i = 0; i < node.IncomingLaneIds.Count; i++)
        {
            if (!graph.TryGetLane(node.IncomingLaneIds[i], out LaneRecord lane))
                continue;
            var source = new LaneConnectionSource(
                lane.EndLegDirectionBit,
                lane.LaneOrdinal);
            if (!result.Contains(source)) result.Add(source);
        }
        return result;
    }

    private static LaneConnectionRule ToRule(LaneRecord source, LaneRecord target) =>
        new LaneConnectionRule(source.EndLegDirectionBit, source.LaneOrdinal,
            target.StartLegDirectionBit, target.LaneOrdinal);

    private static bool SupportsIntersection(RoadNetworkSnapshot roads,
        TrafficGraphSnapshot graph, LaneRecord lane) =>
        graph.TryGetSection(lane.SectionId, out var section) &&
        roads.TryGetRoadProfile(section.RoadProfileId, out var profile) &&
        (profile.SupportedMovements & RoadMovementPolicyMask.Intersection) != 0;

    public static bool Contains(IReadOnlyList<LaneId> lanes, LaneId lane)
    {
        for (int i = 0; i < lanes.Count; i++)
            if (lanes[i] == lane) return true;
        return false;
    }
}
