using System.Collections.Generic;
using UnityEngine;

public sealed class TrafficSpatialIndex
{
    private readonly struct EdgeDistance
    {
        public readonly TrafficEdge Edge;
        public readonly float Distance;

        public EdgeDistance(TrafficEdge edge, float distance)
        {
            Edge = edge;
            Distance = distance;
        }
    }

    private readonly Dictionary<Vector2Int, List<TrafficEdge>> _laneBuckets =
        new Dictionary<Vector2Int, List<TrafficEdge>>();
    private readonly Dictionary<Vector2Int, List<TrafficEdge>> _departureEdgesByPortCell =
        new Dictionary<Vector2Int, List<TrafficEdge>>();
    private readonly Dictionary<Vector2Int, List<TrafficEdge>> _arrivalEdgesByPortCell =
        new Dictionary<Vector2Int, List<TrafficEdge>>();
    private readonly List<TrafficEdge> _candidateEdges = new List<TrafficEdge>();
    private readonly HashSet<TrafficEdge> _candidateSet = new HashSet<TrafficEdge>();
    private readonly List<EdgeDistance> _distances = new List<EdgeDistance>();
    private float _bucketSize = 1f;

    public bool IsBuilt { get; private set; }
    public bool IsReady => IsBuilt;
    public int IndexedLaneCount { get; private set; }
    public int IndexedSegmentCount { get; private set; }
    public int LastCandidateSegmentCount { get; private set; }
    public int LastDistanceTestCount { get; private set; }
    public int BucketCount => _laneBuckets.Count;

    public void Rebuild(IReadOnlyList<TrafficEdge> edges)
    {
        float bucketSize = WorldManager.Instance != null
            ? WorldManager.Instance.cellSize
            : 1f;
        Rebuild(edges, bucketSize);
    }

    public void Rebuild(IReadOnlyList<TrafficEdge> edges, float bucketSize)
    {
        Clear();
        _bucketSize = Mathf.Max(0.25f, bucketSize);

        if (edges == null)
        {
            return;
        }

        for (int i = 0; i < edges.Count; i++)
        {
            TrafficEdge edge = edges[i];
            if (edge == null || edge.kind != TrafficEdgeKind.RoadLane)
            {
                continue;
            }

            IndexedLaneCount++;
            IndexLaneSegments(edge);
            IndexPortEndpoint(edge);
        }

        IsBuilt = true;
    }

    public void Clear()
    {
        _laneBuckets.Clear();
        _departureEdgesByPortCell.Clear();
        _arrivalEdgesByPortCell.Clear();
        _candidateEdges.Clear();
        _candidateSet.Clear();
        _distances.Clear();
        IndexedLaneCount = 0;
        IndexedSegmentCount = 0;
        LastCandidateSegmentCount = 0;
        LastDistanceTestCount = 0;
        IsBuilt = false;
    }

    public TrafficEdge GetClosestLane(Vector3 worldPoint, float searchRadius)
    {
        return QueryClosestLane(worldPoint, searchRadius);
    }

    public List<TrafficEdge> GetClosestLanes(
        Vector3 worldPoint,
        float searchRadius,
        int maxResults)
    {
        var results = new List<TrafficEdge>();
        QueryClosestLanes(worldPoint, searchRadius, maxResults, results);
        return results;
    }

    public TrafficEdge QueryClosestLane(Vector3 worldPoint, float searchRadius)
    {
        QueryClosestLanes(worldPoint, searchRadius, 1, _candidateEdges);
        return _candidateEdges.Count > 0 ? _candidateEdges[0] : null;
    }

    public void QueryClosestLanes(
        Vector3 worldPoint,
        float searchRadius,
        int maxResults,
        List<TrafficEdge> results)
    {
        if (results == null) return;
        results.Clear();
        if (!IsBuilt || maxResults <= 0 || searchRadius < 0f) return;

        _candidateEdges.Clear();
        _candidateSet.Clear();
        _distances.Clear();

        int bucketRadius = Mathf.CeilToInt(searchRadius / _bucketSize);
        Vector2Int center = ToBucket(worldPoint);
        for (int y = center.y - bucketRadius; y <= center.y + bucketRadius; y++)
        {
            for (int x = center.x - bucketRadius; x <= center.x + bucketRadius; x++)
            {
                if (!_laneBuckets.TryGetValue(new Vector2Int(x, y), out List<TrafficEdge> bucket))
                {
                    continue;
                }

                for (int i = 0; i < bucket.Count; i++)
                {
                    TrafficEdge edge = bucket[i];
                    if (edge != null && _candidateSet.Add(edge))
                    {
                        _candidateEdges.Add(edge);
                    }
                }
            }
        }

        float searchRadiusSqr = searchRadius * searchRadius;
        for (int i = 0; i < _candidateEdges.Count; i++)
        {
            TrafficEdge edge = _candidateEdges[i];
            LastDistanceTestCount++;
            float sqrDistance = GetMinSqrDistanceToEdge(worldPoint, edge);
            if (sqrDistance <= searchRadiusSqr)
            {
                _distances.Add(new EdgeDistance(edge, Mathf.Sqrt(sqrDistance)));
            }
        }
        LastCandidateSegmentCount = _candidateEdges.Count;

        _distances.Sort((left, right) =>
        {
            int distanceCompare = left.Distance.CompareTo(right.Distance);
            return distanceCompare != 0
                ? distanceCompare
                : left.Edge.edgeId.CompareTo(right.Edge.edgeId);
        });

        int count = Mathf.Min(maxResults, _distances.Count);
        for (int i = 0; i < count; i++)
        {
            results.Add(_distances[i].Edge);
        }
    }

    public void GetDepartureEdgesForPortCell(Vector2Int portCell, List<TrafficEdge> results)
    {
        CopyPortEdges(_departureEdgesByPortCell, portCell, results);
    }

    public void GetArrivalEdgesForPortCell(Vector2Int portCell, List<TrafficEdge> results)
    {
        CopyPortEdges(_arrivalEdgesByPortCell, portCell, results);
    }

    public bool TryGetDepartureEdgesFromPortCell(
        Vector2Int portCell,
        Vector3 startPos,
        out List<TrafficEdge> edges)
    {
        return TryGetSortedPortEdges(
            _departureEdgesByPortCell,
            portCell,
            startPos,
            true,
            out edges);
    }

    public bool TryGetArrivalEdgesToPortCell(
        Vector2Int portCell,
        Vector3 targetPos,
        out List<TrafficEdge> edges)
    {
        return TryGetSortedPortEdges(
            _arrivalEdgesByPortCell,
            portCell,
            targetPos,
            false,
            out edges);
    }

    private void CopyPortEdges(
        Dictionary<Vector2Int, List<TrafficEdge>> source,
        Vector2Int portCell,
        List<TrafficEdge> results)
    {
        if (results == null) return;
        results.Clear();
        if (!source.TryGetValue(portCell, out List<TrafficEdge> edges)) return;

        for (int i = 0; i < edges.Count; i++)
        {
            if (edges[i] != null) results.Add(edges[i]);
        }
    }

    private void IndexLaneSegments(TrafficEdge edge)
    {
        if (edge.waypoints == null || edge.waypoints.Count == 0)
        {
            return;
        }

        if (edge.waypoints.Count == 1)
        {
            IndexedSegmentCount++;
            AddToBucket(ToBucket(edge.waypoints[0]), edge);
            return;
        }

        for (int i = 0; i < edge.waypoints.Count - 1; i++)
        {
            IndexedSegmentCount++;
            Vector3 a = edge.waypoints[i];
            Vector3 b = edge.waypoints[i + 1];
            Vector2Int min = ToBucket(new Vector3(
                Mathf.Min(a.x, b.x),
                0f,
                Mathf.Min(a.z, b.z)));
            Vector2Int max = ToBucket(new Vector3(
                Mathf.Max(a.x, b.x),
                0f,
                Mathf.Max(a.z, b.z)));

            for (int y = min.y; y <= max.y; y++)
            {
                for (int x = min.x; x <= max.x; x++)
                {
                    AddToBucket(new Vector2Int(x, y), edge);
                }
            }
        }
    }

    private void IndexPortEndpoint(TrafficEdge edge)
    {
        if (edge.startNode != null)
        {
            AddPortEdge(_departureEdgesByPortCell, WorldToCell(edge.startNode.position), edge);
        }

        if (edge.endNode != null)
        {
            AddPortEdge(_arrivalEdgesByPortCell, WorldToCell(edge.endNode.position), edge);
        }
    }

    private void AddToBucket(Vector2Int bucketKey, TrafficEdge edge)
    {
        if (!_laneBuckets.TryGetValue(bucketKey, out List<TrafficEdge> edges))
        {
            edges = new List<TrafficEdge>();
            _laneBuckets.Add(bucketKey, edges);
        }

        if (!edges.Contains(edge)) edges.Add(edge);
    }

    private void AddPortEdge(
        Dictionary<Vector2Int, List<TrafficEdge>> index,
        Vector2Int portCell,
        TrafficEdge edge)
    {
        if (!index.TryGetValue(portCell, out List<TrafficEdge> edges))
        {
            edges = new List<TrafficEdge>();
            index.Add(portCell, edges);
        }

        edges.Add(edge);
    }

    private Vector2Int ToBucket(Vector3 worldPoint)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPoint.x / _bucketSize),
            Mathf.FloorToInt(worldPoint.z / _bucketSize));
    }

    private static Vector2Int WorldToCell(Vector3 worldPoint)
    {
        float cellSize = WorldManager.Instance != null
            ? WorldManager.Instance.cellSize
            : 1f;
        return new Vector2Int(
            Mathf.FloorToInt(worldPoint.x / cellSize),
            Mathf.FloorToInt(worldPoint.z / cellSize));
    }

    private static float GetMinSqrDistanceToEdge(Vector3 point, TrafficEdge edge)
    {
        if (edge == null || edge.waypoints == null || edge.waypoints.Count == 0)
        {
            return float.MaxValue;
        }

        if (edge.waypoints.Count == 1)
        {
            return Vector3.SqrMagnitude(point - edge.waypoints[0]);
        }

        float minSqrDistance = float.MaxValue;
        for (int i = 0; i < edge.waypoints.Count - 1; i++)
        {
            float sqrDistance = DistanceToLineSegmentSqr(
                point,
                edge.waypoints[i],
                edge.waypoints[i + 1]);
            if (sqrDistance < minSqrDistance)
            {
                minSqrDistance = sqrDistance;
            }
        }

        return minSqrDistance;
    }

    public static float DistanceToLineSegment(
        Vector3 point,
        Vector3 lineStart,
        Vector3 lineEnd)
    {
        return Mathf.Sqrt(DistanceToLineSegmentSqr(point, lineStart, lineEnd));
    }

    private bool TryGetSortedPortEdges(
        Dictionary<Vector2Int, List<TrafficEdge>> source,
        Vector2Int portCell,
        Vector3 worldPoint,
        bool sortByStart,
        out List<TrafficEdge> edges)
    {
        edges = new List<TrafficEdge>();
        if (!source.TryGetValue(portCell, out List<TrafficEdge> indexed))
        {
            return false;
        }

        for (int i = 0; i < indexed.Count; i++)
        {
            if (indexed[i] != null) edges.Add(indexed[i]);
        }

        edges.Sort((left, right) =>
        {
            float leftDistance = GetEndpointDistanceSqr(left, worldPoint, sortByStart);
            float rightDistance = GetEndpointDistanceSqr(right, worldPoint, sortByStart);
            int distanceCompare = leftDistance.CompareTo(rightDistance);
            return distanceCompare != 0
                ? distanceCompare
                : left.edgeId.CompareTo(right.edgeId);
        });

        return edges.Count > 0;
    }

    private static float GetEndpointDistanceSqr(
        TrafficEdge edge,
        Vector3 worldPoint,
        bool useStart)
    {
        if (edge == null) return float.MaxValue;
        TrafficNode node = useStart ? edge.startNode : edge.endNode;
        return node != null
            ? Vector3.SqrMagnitude(worldPoint - node.position)
            : float.MaxValue;
    }

    private static float DistanceToLineSegmentSqr(
        Vector3 point,
        Vector3 lineStart,
        Vector3 lineEnd)
    {
        Vector3 segment = lineEnd - lineStart;
        float lengthSqr = segment.sqrMagnitude;
        if (lengthSqr <= 0.0001f)
        {
            return Vector3.SqrMagnitude(point - lineStart);
        }

        float t = Mathf.Clamp01(Vector3.Dot(point - lineStart, segment) / lengthSqr);
        Vector3 projection = lineStart + segment * t;
        return Vector3.SqrMagnitude(point - projection);
    }
}
