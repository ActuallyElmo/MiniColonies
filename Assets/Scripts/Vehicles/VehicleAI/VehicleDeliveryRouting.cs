using System.Collections.Generic;
using UnityEngine;

// One search per departure port, reused across all candidates in a dispatch decision.
// Instances are short lived so road rebuilds cannot leave stale cached distances.
public sealed class VehicleDeliveryRouting
{
    private readonly Dictionary<(Vector2Int cell, bool departure), List<TrafficEdge>> portEdges =
        new Dictionary<(Vector2Int cell, bool departure), List<TrafficEdge>>();
    private readonly VehicleData vehicle;
    private readonly VehicleTrafficProfile profile;
    private readonly Dictionary<Vector2Int, Dictionary<TrafficNode, float>> distances =
        new Dictionary<Vector2Int, Dictionary<TrafficNode, float>>();

    public VehicleDeliveryRouting(VehicleData vehicle)
    {
        this.vehicle = vehicle;
        if (vehicle != null)
            vehicle.TryCompileTrafficProfile(new TrafficDiagnosticCollection(), out profile);
    }

    // Priority 1/5/9 gives weights 0.5/1/2. Four extra ranks justify twice the trip.
    public static float Score(float roadDistance, int priority)
    {
        return Mathf.Max(0.001f, roadDistance) /
            Mathf.Pow(2f, (Mathf.Clamp(priority, 1, 9) - 5) / 4f);
    }

    public bool TryFindPorts(Building source, Building target, RoadNetwork network,
        out Vector2Int exit, out Vector2Int entry, out float distance)
    {
        exit = entry = default;
        distance = float.PositiveInfinity;
        if (source == null || target == null || network == null) return false;
        foreach (var from in source.globalPorts)
        {
            if (!from.Value.AllowsVehicleFlow(PortFlowDirection.Exit, vehicle) ||
                !source.portNetworks.TryGetValue(from.Key, out var fromNetwork) || fromNetwork != network) continue;
            foreach (var to in target.globalPorts)
            {
                if (!to.Value.AllowsVehicleFlow(PortFlowDirection.Entry, vehicle) ||
                    !target.portNetworks.TryGetValue(to.Key, out var toNetwork) || toNetwork != network) continue;
                float candidate = GetDistance(from.Key, to.Key);
                if (candidate < distance)
                {
                    distance = candidate;
                    exit = from.Key;
                    entry = to.Key;
                }
            }
        }
        return !float.IsPositiveInfinity(distance);
    }

    private bool IsLegal(TrafficEdge edge)
    {
        return edge != null && profile != null && edge.startNode != null && edge.endNode != null &&
            (edge.requiredPermissions == RoadPermissionMask.None ||
             (edge.requiredPermissions & profile.RoadPermissions) != RoadPermissionMask.None) &&
            (edge.requiredCapabilities == VehicleCapabilityMask.None ||
             (edge.requiredCapabilities & profile.Capabilities) != VehicleCapabilityMask.None);
    }

    private List<TrafficEdge> PortEdges(Vector2Int cell, bool departure)
    {
        if (portEdges.TryGetValue((cell, departure), out var cached)) return cached;
        var result = new List<TrafficEdge>();
        portEdges[(cell, departure)] = result;
        var backend = TrafficSystemBackend.Instance;
        var graph = backend != null ? backend.CurrentTrafficGraphSnapshot : null;
        if (graph == null || WorldManager.Instance == null) return result;
        var position = new Vector3((cell.x + 0.5f) * WorldManager.Instance.cellSize, 0f,
            (cell.y + 0.5f) * WorldManager.Instance.cellSize);
        List<TrafficEdge> edges;
        bool found = departure
            ? backend.TryGetDepartureEdgesFromPortCell(cell, position, out edges)
            : backend.TryGetArrivalEdgesToPortCell(cell, position, out edges);
        if (!found) return result;
        foreach (var port in graph.BuildingPortAnchors)
        {
            if (port.PortCell != cell || !port.PortType.AllowsVehicleFlow(
                departure ? PortFlowDirection.Exit : PortFlowDirection.Entry, vehicle)) continue;
            var anchors = departure ? port.DepartureAnchors : port.ArrivalAnchors;
            if (anchors.Count == 0) continue;
            foreach (var edge in edges)
            {
                if (!IsLegal(edge)) continue;
                foreach (var anchor in anchors)
                    if (edge.stableLaneId == anchor.LaneId) { result.Add(edge); break; }
            }
            break;
        }
        return result;
    }

    private bool InCell(TrafficNode node, Vector2Int cell)
    {
        float size = WorldManager.Instance.cellSize;
        return Mathf.FloorToInt(node.position.x / size) == cell.x &&
            Mathf.FloorToInt(node.position.z / size) == cell.y;
    }

    private float GetDistance(Vector2Int from, Vector2Int to)
    {
        if (!distances.TryGetValue(from, out var costs))
        {
            var seeds = new Dictionary<TrafficNode, float>();
            foreach (var edge in PortEdges(from, true))
            {
                float cost = InCell(edge.startNode, from) ? edge.totalLength : 0f;
                if (!seeds.TryGetValue(edge.endNode, out float old) || cost < old)
                    seeds[edge.endNode] = cost;
            }
            costs = Search(seeds, IsLegal);
            distances.Add(from, costs);
        }
        float best = float.PositiveInfinity;
        foreach (var edge in PortEdges(to, false))
        {
            // Arrival edges must actually be traversed; reaching their end on another lane is insufficient.
            if (costs.TryGetValue(edge.startNode, out float cost))
                best = Mathf.Min(best, cost + (InCell(edge.endNode, to) ? edge.totalLength : 0f));
            foreach (var start in PortEdges(from, true))
                if (start == edge)
                {
                    float startOffset = InCell(edge.startNode, from) ? 0f : edge.totalLength;
                    float endOffset = InCell(edge.endNode, to) ? edge.totalLength : 0f;
                    if (endOffset >= startOffset) best = Mathf.Min(best, endOffset - startOffset);
                }
        }
        return best;
    }

    public static Dictionary<TrafficNode, float> Search(
        Dictionary<TrafficNode, float> seeds, System.Predicate<TrafficEdge> legal)
    {
        var costs = new Dictionary<TrafficNode, float>(seeds);
        var queue = new SortedSet<(float cost, int order, TrafficNode node)>();
        int sequence = 0;
        foreach (var seed in seeds) queue.Add((seed.Value, sequence++, seed.Key));
        while (queue.Count > 0)
        {
            var current = queue.Min;
            queue.Remove(current);
            if (current.cost > costs[current.node]) continue;
            foreach (var edge in current.node.outgoingEdges)
            {
                if (!legal(edge)) continue;
                float next = current.cost + Mathf.Max(0f, edge.totalLength);
                if (costs.TryGetValue(edge.endNode, out float old) && old <= next) continue;
                costs[edge.endNode] = next;
                queue.Add((next, sequence++, edge.endNode));
            }
        }
        return costs;
    }
}
