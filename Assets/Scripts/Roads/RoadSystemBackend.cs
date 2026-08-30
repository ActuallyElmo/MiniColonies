using System.Collections.Generic;
using UnityEngine;
using System;

// Kept as a pure data class. (Later, for Burst, this can become a strict struct).
public class RoadCell
{
    public Vector2Int gridPosition { get; private set; }
    public int elevationLayer { get; private set; }
    public RoadType roadType { get; private set; }
    
    public int connections { get; private set; }
    public int outConnections { get; private set; }
    public int inConnections { get; private set; }
    
    public bool isPreview { get; private set; }

    public RoadCell(Vector2Int pos, int layer, RoadType type)
    {
        gridPosition = pos;
        elevationLayer = layer;
        roadType = type;
    }

    public void AddConnection(int directionBit) { connections |= directionBit; }
    public bool HasConnection(int directionBit) { return (connections & directionBit) != 0; }
    
    public void RemoveConnection(int directionBit) 
    { 
        connections &= ~directionBit; 
        outConnections &= ~directionBit;
        inConnections &= ~directionBit;
    }

    public void SetOutgoingConnection(int directionBit, bool enabled)
    {
        if (enabled) outConnections |= directionBit;
        else outConnections &= ~directionBit;
    }

    public void SetIncomingConnection(int directionBit, bool enabled)
    {
        if (enabled) inConnections |= directionBit;
        else inConnections &= ~directionBit;
    }

    public void SetDirectionalConnection(
        int directionBit,
        bool canExit,
        bool canEnter)
    {
        SetOutgoingConnection(directionBit, canExit);
        SetIncomingConnection(directionBit, canEnter);
    }

    public void SetRoadType(RoadType type) { roadType = type; }
    public void SetPreview(bool preview) { isPreview = preview; }

    public void RestoreAuthoringState(
        int restoredConnections,
        int restoredIncomingConnections,
        int restoredOutgoingConnections,
        RoadType restoredRoadType,
        bool restoredPreview)
    {
        connections = restoredConnections;
        inConnections = restoredIncomingConnections;
        outConnections = restoredOutgoingConnections;
        roadType = restoredRoadType;
        isPreview = restoredPreview;
    }
}

public class RoadSystemBackend : MonoBehaviour
{
    public static RoadSystemBackend Instance { get; private set; }

    [Header("Debugging")]
    public bool showDebugRoads = true;
    public Color debugNodeColor = Color.blue;
    public Color debugLineColor = Color.cyan;

    private readonly Dictionary<Vector2Int, RoadCell> _roads = new Dictionary<Vector2Int, RoadCell>();
    private readonly Dictionary<Vector2Int, IntersectionData> _intersections =
        new Dictionary<Vector2Int, IntersectionData>();

    public IReadOnlyDictionary<Vector2Int, RoadCell> Roads => _roads;
    public int AuthoringRevision { get; private set; }
    
    public event Action<HashSet<Vector2Int>> OnChunksDirty;
    public event Action<Vector2Int> OnRoadCellChanged;
    
    private HashSet<Vector2Int> _dirtyChunksThisFrame = new HashSet<Vector2Int>();
    private WorldManager _worldManager;

    // --- INTERSECTION MANAGEMENT ---
    public IReadOnlyDictionary<Vector2Int, IntersectionData> Intersections => _intersections;


    // --- TRANSACTION & PREVIEW SYSTEM ---
    public bool IsPreviewMode { get; private set; }

    private struct RoadCellSnapshot 
    {
        public bool existed;
        public int connections;
        public int inConnections;
        public int outConnections;
        public RoadType type;
        public bool wasPreview;
    }

    private Dictionary<Vector2Int, RoadCellSnapshot> _previewSnapshots = new Dictionary<Vector2Int, RoadCellSnapshot>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        _worldManager = WorldManager.Instance;
    }

    // Opens the transaction gate
    public void BeginPreview()
    {
        if (IsPreviewMode) return;
        IsPreviewMode = true;
        _previewSnapshots.Clear();
    }

    // Confirms all changes, turns temporary roads into permanent ones
    public void CommitPreview()
    {
        if (!IsPreviewMode) return;
        IsPreviewMode = false;

        foreach (var pos in _previewSnapshots.Keys)
        {
            if (_roads.TryGetValue(pos, out RoadCell cell) && cell.isPreview)
            {
                cell.SetPreview(false);
                MarkCellDirty(pos); // Mark dirty again to trigger the permanent mesh swap
            }
        }
        _previewSnapshots.Clear();
    }

    // Violently reverts all grid cells to the exact state they were in before the drag started
    public void CancelPreview()
    {
        if (!IsPreviewMode) return;
        IsPreviewMode = false;

        foreach (var kvp in _previewSnapshots)
        {
            Vector2Int pos = kvp.Key;
            RoadCellSnapshot snap = kvp.Value;

            if (!snap.existed)
            {
                _roads.Remove(pos);
            }
            else
            {
                if (_roads.TryGetValue(pos, out RoadCell cell))
                {
                    cell.RestoreAuthoringState(
                        snap.connections,
                        snap.inConnections,
                        snap.outConnections,
                        snap.type,
                        snap.wasPreview);
                }
            }
            MarkCellDirty(pos);
        }
        _previewSnapshots.Clear();
    }

    private void SaveSnapshot(Vector2Int pos)
    {
        if (!IsPreviewMode || _previewSnapshots.ContainsKey(pos)) return;

        if (_roads.TryGetValue(pos, out RoadCell cell))
        {
            _previewSnapshots[pos] = new RoadCellSnapshot {
                existed = true,
                connections = cell.connections,
                inConnections = cell.inConnections,
                outConnections = cell.outConnections,
                type = cell.roadType,
                wasPreview = cell.isPreview
            };
        }
        else
        {
            _previewSnapshots[pos] = new RoadCellSnapshot { existed = false };
        }
    }

    // --- ENDPOINTS ---

    public void PlaceRoadBlock(Vector2Int cell, RoadType type)
    {
        if (type == null ||
            _roads.ContainsKey(cell) ||
            !CanPlaceRoadTypeAt(cell, type))
        {
            return;
        }

        SaveSnapshot(cell); // SNAPSHOT
        
        int layer = _worldManager.GetCellLayer(cell.x, cell.y);
        RoadCell newCell = new RoadCell(cell, layer, type);
        
        if (IsPreviewMode) newCell.SetPreview(true); // FLAG TEMPORARY
        
        _roads.Add(cell, newCell);
        MarkCellDirty(cell);
    }

    public void RemoveRoadBlock(Vector2Int cell)
    {
        if (!_roads.TryGetValue(cell, out RoadCell roadToRemove)) return;

        SaveSnapshot(cell); // SNAPSHOT

        for (int i = 0; i < 8; i++)
        {
            int bit = 1 << i;
            if (roadToRemove.HasConnection(bit))
            {
                Vector2Int neighborPos = GetNeighborPosition(cell, bit);
                if (_roads.TryGetValue(neighborPos, out RoadCell neighbor))
                {
                    SaveSnapshot(neighborPos); // SNAPSHOT NEIGHBORS
                    int reverseBit = GetDirectionBit(neighborPos, cell);
                    neighbor.RemoveConnection(reverseBit);
                    MarkCellDirty(neighborPos); 
                    UpdateIntersectionState(neighbor);
                }
            }
        }
        
        MarkCellDirty(cell);
        _roads.Remove(cell);

        if (_intersections.TryGetValue(cell, out IntersectionData removedIntersection))
        {
            UnsubscribeFromIntersection(removedIntersection);
            _intersections.Remove(cell);
        }
    }

    public void UpgradeRoadBlock(Vector2Int cell, RoadType newType)
    {
        if (!CanUpgradeRoadBlock(cell, newType)) return;

        if (_roads.TryGetValue(cell, out RoadCell road))
        {
            if (road.roadType == newType)
            {
                if (newType != null && newType.UsesOneWayTraffic)
                {
                    ReverseOneWayRoadEdge(cell, newType);
                }

                return;
            }

            if (road.roadType != newType)
            {
                SaveSnapshot(cell); // SNAPSHOT
                road.SetRoadType(newType);
                NormalizeCellTrafficDirections(road);
                MarkCellDirty(cell);
                UpdateIntersectionState(road);
                MarkConnectedCellsDirtyForRoadTypeChange(road);
            }
        }
    }

    public bool CanUpgradeRoadBlock(Vector2Int cell, RoadType newType)
    {
        return newType != null &&
               _roads.TryGetValue(cell, out RoadCell road) &&
               road != null &&
               road.roadType != null &&
               road.roadType.IsWide == newType.IsWide &&
               CanPlaceRoadTypeAt(cell, newType);
    }

    private void MarkConnectedCellsDirtyForRoadTypeChange(RoadCell road)
    {
        if (road == null) return;

        for (int i = 0; i < 8; i++)
        {
            int bit = 1 << i;
            if (!road.HasConnection(bit)) continue;

            Vector2Int neighborPos = GetNeighborPosition(road.gridPosition, bit);
            if (!_roads.TryGetValue(neighborPos, out RoadCell neighbor)) continue;

            MarkCellDirty(neighborPos);
            UpdateIntersectionState(neighbor);
        }
    }

    public void ConnectCellsSafe(Vector2Int from, Vector2Int to, RoadType selectedRoadType)
    {
        if (selectedRoadType == null ||
            !CanPlaceRoadTypeAt(from, selectedRoadType) ||
            !CanPlaceRoadTypeAt(to, selectedRoadType))
        {
            return;
        }

        SaveSnapshot(from); // SNAPSHOT
        SaveSnapshot(to);   // SNAPSHOT

        if (!_roads.ContainsKey(from)) PlaceRoadBlock(from, selectedRoadType);
        if (!_roads.ContainsKey(to)) PlaceRoadBlock(to, selectedRoadType);

        RoadCell toCell = _roads[to];
        RoadCell fromCell = _roads[from];

        int toToFromBit = GetDirectionBit(to, from);
        int fromToToBit = GetDirectionBit(from, to);

        toCell.AddConnection(toToFromBit);
        fromCell.AddConnection(fromToToBit);

        bool isOneWay = selectedRoadType != null && selectedRoadType.UsesOneWayTraffic;

        if (isOneWay)
        {
            SetTrafficDirectionForConnection(
                fromCell,
                fromToToBit,
                true);
            SetTrafficDirectionForConnection(
                toCell,
                toToFromBit,
                false);

            NormalizeOneWayRoadEdgeAfterConnection(from, to, selectedRoadType);
        }
        else
        {
            SetTrafficDirectionForConnection(
                fromCell,
                fromToToBit,
                true);
            SetTrafficDirectionForConnection(
                toCell,
                toToFromBit,
                false);
        }

        MarkCellDirty(from);
        MarkCellDirty(to);

        UpdateIntersectionState(fromCell);
        UpdateIntersectionState(toCell);
    }

    public bool CanPlaceRoadTypeAt(Vector2Int cell, RoadType roadType)
    {
        if (roadType == null) return false;

        BuildingSystemBackend buildingSystem = BuildingSystemBackend.Instance;
        if (buildingSystem == null) return true;
        if (!buildingSystem.CanBuildRoadAt(cell)) return false;

        return !buildingSystem.TryGetPortAt(cell, out _) ||
               roadType.CanConnectToPort;
    }

    public bool TryGetOneWayRoadEdgeKey(
        Vector2Int cell,
        RoadType roadType,
        out string key)
    {
        key = null;
        if (!TryCollectOneWayRoadEdge(cell, roadType, out List<Vector2Int> edgeCells))
        {
            return false;
        }

        key = BuildRoadEdgeKey(edgeCells);
        return true;
    }

    private bool ReverseOneWayRoadEdge(Vector2Int cell, RoadType roadType)
    {
        if (!TryCollectOneWayRoadEdge(cell, roadType, out List<Vector2Int> edgeCells))
        {
            return false;
        }

        for (int i = 0; i < edgeCells.Count; i++)
        {
            SaveSnapshot(edgeCells[i]);
        }

        for (int i = 0; i < edgeCells.Count - 1; i++)
        {
            RoadCell a = _roads[edgeCells[i]];
            RoadCell b = _roads[edgeCells[i + 1]];
            int aToBBit = GetDirectionBit(a.gridPosition, b.gridPosition);
            int bToABit = GetDirectionBit(b.gridPosition, a.gridPosition);

            bool aCanExit = (a.outConnections & aToBBit) != 0;
            bool aCanEnter = (a.inConnections & aToBBit) != 0;
            bool bCanExit = (b.outConnections & bToABit) != 0;
            bool bCanEnter = (b.inConnections & bToABit) != 0;

            a.SetDirectionalConnection(aToBBit, bCanExit, bCanEnter);
            b.SetDirectionalConnection(bToABit, aCanExit, aCanEnter);
        }

        for (int i = 0; i < edgeCells.Count; i++)
        {
            RoadCell edgeCell = _roads[edgeCells[i]];
            MarkCellDirty(edgeCell.gridPosition);
            UpdateIntersectionState(edgeCell);
        }

        return true;
    }

    private bool TryCollectOneWayRoadEdge(
        Vector2Int seed,
        RoadType roadType,
        out List<Vector2Int> edgeCells)
    {
        edgeCells = null;
        if (roadType == null || roadType.UsesTwoWayTraffic) return false;
        if (!_roads.TryGetValue(seed, out RoadCell seedCell) ||
            seedCell.roadType != roadType)
        {
            return false;
        }

        List<int> candidateDirections = GetSameOneWayRoadDirections(seedCell, roadType);
        if (candidateDirections.Count == 0) return false;

        if (IsOneWayRoadEdgeBoundary(seedCell, roadType))
        {
            if (candidateDirections.Count != 1) return false;

            edgeCells = TraceOneWayRoadEdgeFromBoundary(
                seed,
                candidateDirections[0],
                roadType);
            return edgeCells.Count >= 2;
        }

        if (candidateDirections.Count != 2) return false;

        List<Vector2Int> firstHalf = TraceOneWayRoadEdgeFromBoundary(
            seed,
            candidateDirections[0],
            roadType);
        List<Vector2Int> secondHalf = TraceOneWayRoadEdgeFromBoundary(
            seed,
            candidateDirections[1],
            roadType);

        firstHalf.Reverse();
        firstHalf.RemoveAt(firstHalf.Count - 1);
        firstHalf.AddRange(secondHalf);
        edgeCells = firstHalf;
        return edgeCells.Count >= 2;
    }

    private bool TryCollectOneWayRoadEdgeThroughConnection(
        Vector2Int a,
        Vector2Int b,
        RoadType roadType,
        out List<Vector2Int> edgeCells)
    {
        edgeCells = null;
        if (roadType == null || roadType.UsesTwoWayTraffic) return false;
        if (!_roads.TryGetValue(a, out RoadCell aCell) ||
            !_roads.TryGetValue(b, out RoadCell bCell) ||
            aCell.roadType != roadType ||
            bCell.roadType != roadType)
        {
            return false;
        }

        int aToBBit = GetDirectionBit(a, b);
        int bToABit = GetDirectionBit(b, a);
        if (!aCell.HasConnection(aToBBit) || !bCell.HasConnection(bToABit))
        {
            return false;
        }

        List<Vector2Int> left = TraceOneWayRoadEdgeSide(a, b, roadType);
        List<Vector2Int> right = TraceOneWayRoadEdgeSide(b, a, roadType);
        left.Reverse();
        left.AddRange(right);
        edgeCells = left;
        return edgeCells.Count >= 2;
    }

    private List<Vector2Int> TraceOneWayRoadEdgeFromBoundary(
        Vector2Int start,
        int firstDirectionBit,
        RoadType roadType)
    {
        var result = new List<Vector2Int> { start };
        Vector2Int previous = start;
        Vector2Int current = GetNeighborPosition(start, firstDirectionBit);
        int safety = _roads.Count + 1;

        while (safety-- > 0 &&
               _roads.TryGetValue(current, out RoadCell currentCell) &&
               currentCell.roadType == roadType)
        {
            result.Add(current);
            if (IsOneWayRoadEdgeBoundary(currentCell, roadType)) break;

            List<int> directions = GetSameOneWayRoadDirections(currentCell, roadType);
            Vector2Int next = current;
            for (int i = 0; i < directions.Count; i++)
            {
                Vector2Int candidate = GetNeighborPosition(current, directions[i]);
                if (candidate != previous)
                {
                    next = candidate;
                    break;
                }
            }

            if (next == current) break;
            previous = current;
            current = next;
        }

        return result;
    }

    private List<Vector2Int> TraceOneWayRoadEdgeSide(
        Vector2Int start,
        Vector2Int blockedNeighbor,
        RoadType roadType)
    {
        var result = new List<Vector2Int> { start };
        Vector2Int previous = blockedNeighbor;
        Vector2Int current = start;
        int safety = _roads.Count + 1;

        while (safety-- > 0 &&
               _roads.TryGetValue(current, out RoadCell currentCell) &&
               !IsOneWayRoadEdgeBoundary(currentCell, roadType))
        {
            List<int> directions = GetSameOneWayRoadDirections(currentCell, roadType);
            Vector2Int next = current;
            for (int i = 0; i < directions.Count; i++)
            {
                Vector2Int candidate = GetNeighborPosition(current, directions[i]);
                if (candidate != previous)
                {
                    next = candidate;
                    break;
                }
            }

            if (next == current ||
                !_roads.TryGetValue(next, out RoadCell nextCell) ||
                nextCell.roadType != roadType)
            {
                break;
            }

            result.Add(next);
            previous = current;
            current = next;
        }

        return result;
    }

    private List<int> GetSameOneWayRoadDirections(RoadCell cell, RoadType roadType)
    {
        var directions = new List<int>();
        for (int i = 0; i < 8; i++)
        {
            int bit = 1 << i;
            if (!cell.HasConnection(bit)) continue;

            Vector2Int neighborPos = GetNeighborPosition(cell.gridPosition, bit);
            if (_roads.TryGetValue(neighborPos, out RoadCell neighbor) &&
                neighbor.roadType == roadType &&
                neighbor.roadType != null &&
                neighbor.roadType.UsesOneWayTraffic)
            {
                directions.Add(bit);
            }
        }

        directions.Sort();
        return directions;
    }

    private bool IsOneWayRoadEdgeBoundary(RoadCell cell, RoadType roadType)
    {
        if (cell == null || roadType == null) return true;
        if (GetConnectionCount(cell) != 2) return true;
        return GetSameOneWayRoadDirections(cell, roadType).Count != 2;
    }

    private string BuildRoadEdgeKey(IReadOnlyList<Vector2Int> edgeCells)
    {
        if (edgeCells == null || edgeCells.Count == 0) return string.Empty;

        Vector2Int start = edgeCells[0];
        Vector2Int end = edgeCells[edgeCells.Count - 1];
        if (CompareCells(start, end) > 0)
        {
            Vector2Int temporary = start;
            start = end;
            end = temporary;
        }

        return $"{start.x},{start.y}|{end.x},{end.y}";
    }

    private int CompareCells(Vector2Int left, Vector2Int right)
    {
        int x = left.x.CompareTo(right.x);
        return x != 0 ? x : left.y.CompareTo(right.y);
    }

    private void SetTrafficDirectionForConnection(
        RoadCell cell,
        int directionBit,
        bool followsPlacementDirection)
    {
        if (cell == null || cell.roadType == null) return;

        if (cell.roadType.UsesTwoWayTraffic)
        {
            cell.SetDirectionalConnection(directionBit, true, true);
            return;
        }

        cell.SetDirectionalConnection(
            directionBit,
            followsPlacementDirection,
            !followsPlacementDirection);
    }

    private void NormalizeOneWayRoadEdgeAfterConnection(
        Vector2Int from,
        Vector2Int to,
        RoadType roadType)
    {
        if (!TryCollectOneWayRoadEdgeThroughConnection(
                from,
                to,
                roadType,
                out List<Vector2Int> edgeCells))
        {
            return;
        }

        bool forward = TryChooseExistingEdgeDirection(
            edgeCells,
            from,
            to,
            out bool anchoredForward)
            ? anchoredForward
            : IsConnectionForwardInEdge(edgeCells, from, to);

        for (int i = 0; i < edgeCells.Count; i++)
        {
            SaveSnapshot(edgeCells[i]);
        }

        ApplyOneWayRoadEdgeDirection(edgeCells, forward);
    }

    private bool TryChooseExistingEdgeDirection(
        IReadOnlyList<Vector2Int> edgeCells,
        Vector2Int from,
        Vector2Int to,
        out bool forward)
    {
        forward = false;
        int anchorCellIndex = GetPreferredExistingAnchorIndex(edgeCells, from, to);
        bool hasVote = false;
        bool votedForward = false;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < edgeCells.Count - 1; i++)
        {
            RoadCell a = _roads[edgeCells[i]];
            RoadCell b = _roads[edgeCells[i + 1]];
            if (a.isPreview || b.isPreview) continue;
            if (!TryGetPairFlowDirection(a, b, out bool pairForward)) continue;

            int distance = anchorCellIndex >= 0
                ? Mathf.Min(
                    Mathf.Abs(anchorCellIndex - i),
                    Mathf.Abs(anchorCellIndex - (i + 1)))
                : 0;
            if (hasVote && distance >= bestDistance) continue;

            hasVote = true;
            votedForward = pairForward;
            bestDistance = distance;
        }

        if (!hasVote) return false;

        forward = votedForward;
        return true;
    }

    private int GetPreferredExistingAnchorIndex(
        IReadOnlyList<Vector2Int> edgeCells,
        Vector2Int from,
        Vector2Int to)
    {
        bool fromIsPreview = _roads.TryGetValue(from, out RoadCell fromCell) && fromCell.isPreview;
        bool toIsPreview = _roads.TryGetValue(to, out RoadCell toCell) && toCell.isPreview;

        if (fromIsPreview && !toIsPreview) return IndexOfCell(edgeCells, to);
        if (!fromIsPreview && toIsPreview) return IndexOfCell(edgeCells, from);
        return -1;
    }

    private int IndexOfCell(IReadOnlyList<Vector2Int> edgeCells, Vector2Int cell)
    {
        for (int i = 0; i < edgeCells.Count; i++)
        {
            if (edgeCells[i] == cell) return i;
        }

        return -1;
    }

    private bool TryGetPairFlowDirection(
        RoadCell a,
        RoadCell b,
        out bool forward)
    {
        forward = false;
        int aToBBit = GetDirectionBit(a.gridPosition, b.gridPosition);
        int bToABit = GetDirectionBit(b.gridPosition, a.gridPosition);
        bool canTravelForward =
            (a.outConnections & aToBBit) != 0 &&
            (b.inConnections & bToABit) != 0;
        bool canTravelBackward =
            (b.outConnections & bToABit) != 0 &&
            (a.inConnections & aToBBit) != 0;

        if (canTravelForward == canTravelBackward) return false;

        forward = canTravelForward;
        return true;
    }

    private bool IsConnectionForwardInEdge(
        IReadOnlyList<Vector2Int> edgeCells,
        Vector2Int from,
        Vector2Int to)
    {
        for (int i = 0; i < edgeCells.Count - 1; i++)
        {
            if (edgeCells[i] == from && edgeCells[i + 1] == to) return true;
            if (edgeCells[i] == to && edgeCells[i + 1] == from) return false;
        }

        return true;
    }

    private void ApplyOneWayRoadEdgeDirection(
        IReadOnlyList<Vector2Int> edgeCells,
        bool forward)
    {
        for (int i = 0; i < edgeCells.Count - 1; i++)
        {
            RoadCell a = _roads[edgeCells[i]];
            RoadCell b = _roads[edgeCells[i + 1]];
            int aToBBit = GetDirectionBit(a.gridPosition, b.gridPosition);
            int bToABit = GetDirectionBit(b.gridPosition, a.gridPosition);

            a.SetDirectionalConnection(aToBBit, forward, !forward);
            b.SetDirectionalConnection(bToABit, !forward, forward);
        }

        for (int i = 0; i < edgeCells.Count; i++)
        {
            RoadCell edgeCell = _roads[edgeCells[i]];
            MarkCellDirty(edgeCell.gridPosition);
            UpdateIntersectionState(edgeCell);
        }
    }

    private void NormalizeCellTrafficDirections(RoadCell cell)
    {
        if (cell == null || cell.roadType == null) return;

        for (int i = 0; i < 8; i++)
        {
            int bit = 1 << i;
            if (!cell.HasConnection(bit)) continue;

            if (cell.roadType.UsesTwoWayTraffic)
            {
                cell.SetDirectionalConnection(bit, true, true);
                continue;
            }

            bool canExit = (cell.outConnections & bit) != 0;
            bool canEnter = (cell.inConnections & bit) != 0;
            if (canExit == canEnter)
            {
                cell.SetDirectionalConnection(bit, true, false);
            }
        }
    }

    private void UpdateIntersectionState(RoadCell cell)
    {
        if (GetRoadNodeKind(cell) == RoadNodeKind.Intersection)
        {
            if (!_intersections.ContainsKey(cell.gridPosition))
            {
                RegisterIntersection(new IntersectionData(cell.gridPosition));
            }

            _intersections[cell.gridPosition].NodeKind = RoadNodeKind.Intersection;
        }
        else
        {
            if (_intersections.TryGetValue(cell.gridPosition, out IntersectionData removedIntersection))
            {
                UnsubscribeFromIntersection(removedIntersection);
                _intersections.Remove(cell.gridPosition);
            }
        }
    }

    // --- INTERNAL LOGIC ---

    private void MarkCellDirty(Vector2Int cell)
    {
        unchecked
        {
            AuthoringRevision++;
            if (AuthoringRevision <= 0) AuthoringRevision = 1;
        }

        OnRoadCellChanged?.Invoke(cell);
        if (_worldManager == null) return;

        Vector2Int chunkPos = new Vector2Int(
            Mathf.FloorToInt((float)cell.x / _worldManager.chunkSize), 
            Mathf.FloorToInt((float)cell.y / _worldManager.chunkSize)
        );
        _dirtyChunksThisFrame.Add(chunkPos);

        int localX = cell.x % _worldManager.chunkSize;
        int localZ = cell.y % _worldManager.chunkSize;
        if (localX == 0) _dirtyChunksThisFrame.Add(new Vector2Int(chunkPos.x - 1, chunkPos.y));
        if (localX == _worldManager.chunkSize - 1) _dirtyChunksThisFrame.Add(new Vector2Int(chunkPos.x + 1, chunkPos.y));
        if (localZ == 0) _dirtyChunksThisFrame.Add(new Vector2Int(chunkPos.x, chunkPos.y - 1));
        if (localZ == _worldManager.chunkSize - 1) _dirtyChunksThisFrame.Add(new Vector2Int(chunkPos.x, chunkPos.y + 1));
    }

    private void LateUpdate()
    {
        if (_dirtyChunksThisFrame.Count > 0)
        {
            OnChunksDirty?.Invoke(new HashSet<Vector2Int>(_dirtyChunksThisFrame));
            _dirtyChunksThisFrame.Clear();
        }
    }

    public int GetConnectionCount(RoadCell cell)
    {
        int count = 0;
        for (int i = 0; i < 8; i++) if (cell.HasConnection(1 << i)) count++;
        return count;
    }

    public IntersectionData GetEditableIntersection(Vector2Int cell)
    {
        if (!_roads.TryGetValue(cell, out RoadCell roadCell)) return null;
        if (GetRoadNodeKind(roadCell) != RoadNodeKind.Intersection) return null;

        if (!_intersections.TryGetValue(cell, out IntersectionData data))
        {
            data = new IntersectionData(cell);
            RegisterIntersection(data);
        }

        data.NodeKind = RoadNodeKind.Intersection;
        return data;
    }

    public bool SetLaneConnection(Vector2Int cell, int fromDir, int fromLane, int toDir, int toLane)
    {
        IntersectionData data = GetEditableIntersection(cell);
        if (data == null) return false;

        data.AddCustomRule(fromDir, fromLane, toDir, toLane);
        return true;
    }

    public bool ClearLaneConnections(Vector2Int cell)
    {
        IntersectionData data = GetEditableIntersection(cell);
        if (data == null) return false;

        data.ClearCustomRules();
        return true;
    }

    public bool SetIntersectionRuleType(Vector2Int cell, IntersectionRuleType ruleType)
    {
        IntersectionData data = GetEditableIntersection(cell);
        if (data == null) return false;
        data.RuleType = ruleType;
        return true;
    }

    public bool SetIntersectionPriorityDirections(
        Vector2Int cell,
        int priorityDirectionBitA,
        int priorityDirectionBitB)
    {
        IntersectionData data = GetEditableIntersection(cell);
        if (data == null) return false;
        data.SetPriorityDirections(priorityDirectionBitA, priorityDirectionBitB);
        return true;
    }

    public bool SetIntersectionTrafficLightCycle(Vector2Int cell, float cycleSeconds)
    {
        IntersectionData data = GetEditableIntersection(cell);
        if (data == null) return false;
        data.TrafficLightCycleSeconds = cycleSeconds;
        return true;
    }

    public RoadNodeKind GetRoadNodeKind(RoadCell cell)
    {
        int connectionCount = GetConnectionCount(cell);

        if (connectionCount == 1) return RoadNodeKind.RoadEnd;
        if (connectionCount >= 3) return RoadNodeKind.Intersection;

        if (connectionCount == 2)
        {
            if (IsSharpWideRoadTurn(cell))
            {
                return RoadNodeKind.Intersection;
            }

            for (int i = 0; i < 8; i++)
            {
                int bit = 1 << i;
                if (cell.HasConnection(bit) && HasRoadTypeOrDirectionalityChange(cell, bit))
                {
                    return RoadNodeKind.Transition;
                }
            }

            return RoadNodeKind.ThroughRoad;
        }

        return RoadNodeKind.ThroughRoad;
    }

    private bool HasRoadTypeOrDirectionalityChange(RoadCell cell, int directionBit)
    {
        Vector2Int neighborPos = GetNeighborPosition(cell.gridPosition, directionBit);
        if (!_roads.TryGetValue(neighborPos, out RoadCell neighbor)) return false;

        int neighborConnectionCount = GetConnectionCount(neighbor);
        if (neighborConnectionCount >= 3 || IsSharpWideRoadTurn(neighbor))
        {
            return false;
        }

        bool hasTypeChange =
            GetLaneCount(cell.roadType) != GetLaneCount(neighbor.roadType) ||
            IsTwoWay(cell.roadType) != IsTwoWay(neighbor.roadType);

        int reverseBit = GetDirectionBit(neighborPos, cell.gridPosition);
        bool canLeaveCell = (cell.outConnections & directionBit) != 0;
        bool canEnterCell = (cell.inConnections & directionBit) != 0;
        bool neighborCanEnter = (neighbor.inConnections & reverseBit) != 0;
        bool neighborCanLeave = (neighbor.outConnections & reverseBit) != 0;
        bool hasDirectionalityChange =
            canLeaveCell != neighborCanEnter ||
            canEnterCell != neighborCanLeave;

        if (!hasTypeChange && !hasDirectionalityChange)
        {
            return false;
        }

        if (neighborConnectionCount != 2)
        {
            return true;
        }

        return cell.gridPosition.x < neighborPos.x ||
               (cell.gridPosition.x == neighborPos.x &&
                cell.gridPosition.y < neighborPos.y);
    }

    private bool IsSharpWideRoadTurn(RoadCell cell)
    {
        if (cell == null ||
            cell.roadType == null ||
            !cell.roadType.IsWide ||
            GetConnectionCount(cell) != 2)
        {
            return false;
        }

        int firstDirectionBit = 0;
        int secondDirectionBit = 0;
        for (int i = 0; i < 8; i++)
        {
            int bit = 1 << i;
            if (!cell.HasConnection(bit)) continue;

            if (firstDirectionBit == 0) firstDirectionBit = bit;
            else
            {
                secondDirectionBit = bit;
                break;
            }
        }

        Vector2 firstDirection = (Vector2)GetNeighborPosition(
            Vector2Int.zero,
            firstDirectionBit);
        Vector2 secondDirection = (Vector2)GetNeighborPosition(
            Vector2Int.zero,
            secondDirectionBit);
        if (firstDirection.sqrMagnitude <= 0f ||
            secondDirection.sqrMagnitude <= 0f)
        {
            return false;
        }

        // Outward leg angles of 90 degrees or less are sharp. Treating these
        // as intersections gives them the larger cap and connector geometry.
        return Vector2.Dot(
                   firstDirection.normalized,
                   secondDirection.normalized) >= -0.0001f;
    }

    private int GetLaneCount(RoadType roadType)
    {
        if (roadType == null) return 0;
        return roadType.UsesTwoWayTraffic ? roadType.lanesPerWay * 2 : roadType.lanesPerWay;
    }

    private bool IsTwoWay(RoadType roadType)
    {
        return roadType != null && roadType.UsesTwoWayTraffic;
    }

    public int GetDirectionBit(Vector2Int from, Vector2Int to)
    {
        return RoadGridDirectionUtility.GetDirectionBit(from, to);
    }

    public Vector2Int GetNeighborPosition(Vector2Int pos, int bit)
    {
        return RoadGridDirectionUtility.GetNeighborPosition(pos, bit);
    }

    private void RegisterIntersection(IntersectionData data)
    {
        _intersections[data.GridPosition] = data;
        data.Changed -= HandleIntersectionChanged;
        data.Changed += HandleIntersectionChanged;
    }

    private void UnsubscribeFromIntersection(IntersectionData data)
    {
        if (data != null) data.Changed -= HandleIntersectionChanged;
    }

    private void HandleIntersectionChanged(IntersectionData data)
    {
        if (data != null && _intersections.ContainsKey(data.GridPosition))
        {
            MarkCellDirty(data.GridPosition);
        }
    }
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showDebugRoads || _worldManager == null) return;
        foreach (var kvp in _roads)
        {
            RoadCell cell = kvp.Value;
            float x = cell.gridPosition.x * _worldManager.cellSize + (_worldManager.cellSize * 0.5f);
            float z = cell.gridPosition.y * _worldManager.cellSize + (_worldManager.cellSize * 0.5f);
            float y = _worldManager.GetPhysicalHeight(cell.gridPosition.x + 0.5f, cell.gridPosition.y + 0.5f) * _worldManager.heightStep;
            
            Vector3 centerPos = new Vector3(x, y + 0.2f, z);
            Gizmos.color = debugNodeColor; Gizmos.DrawSphere(centerPos, _worldManager.cellSize * 0.2f);
            Gizmos.color = debugLineColor;

            for (int i = 0; i < 8; i++)
            {
                int bit = 1 << i;
                if (cell.HasConnection(bit))
                {
                    Vector2Int neighborPos = GetNeighborPosition(cell.gridPosition, bit);
                    float nx = neighborPos.x * _worldManager.cellSize + (_worldManager.cellSize * 0.5f);
                    float nz = neighborPos.y * _worldManager.cellSize + (_worldManager.cellSize * 0.5f);
                    float ny = _worldManager.GetPhysicalHeight(neighborPos.x + 0.5f, neighborPos.y + 0.5f) * _worldManager.heightStep;
                    Gizmos.DrawLine(centerPos, new Vector3(nx, ny + 0.2f, nz));
                }
            }
        }
    }
#endif
}
