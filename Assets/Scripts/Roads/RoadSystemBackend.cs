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

    public void AddOutgoingConnection(int directionBit) { outConnections |= directionBit; }
    public void AddIncomingConnection(int directionBit) { inConnections |= directionBit; }
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
        if (type == null || _roads.ContainsKey(cell)) return;

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
        if (_roads.TryGetValue(cell, out RoadCell road))
        {
            if (road.roadType != newType)
            {
                SaveSnapshot(cell); // SNAPSHOT
                road.SetRoadType(newType);
                MarkCellDirty(cell);
            }
        }
    }

    public void ConnectCellsSafe(Vector2Int from, Vector2Int to, RoadType selectedRoadType)
    {
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

        bool isOneWay = !selectedRoadType.isTwoWay;

        if (isOneWay)
        {
            bool forward = true;
            if (GetConnectionCount(fromCell) >= 1 && !fromCell.roadType.isTwoWay)
            {
                if (fromCell.inConnections == 0 && fromCell.outConnections > 0) forward = false;
            }

            if (forward)
            {
                fromCell.AddOutgoingConnection(fromToToBit);
                toCell.AddIncomingConnection(toToFromBit);
                if (fromCell.roadType.isTwoWay) fromCell.AddIncomingConnection(fromToToBit);
                if (toCell.roadType.isTwoWay) toCell.AddOutgoingConnection(toToFromBit);
            }
            else
            {
                fromCell.AddIncomingConnection(fromToToBit);
                toCell.AddOutgoingConnection(toToFromBit);
                if (fromCell.roadType.isTwoWay) fromCell.AddOutgoingConnection(fromToToBit);
                if (toCell.roadType.isTwoWay) toCell.AddIncomingConnection(toToFromBit);
            }
        }
        else
        {
            fromCell.AddOutgoingConnection(fromToToBit);
            fromCell.AddIncomingConnection(fromToToBit);
            toCell.AddOutgoingConnection(toToFromBit);
            toCell.AddIncomingConnection(toToFromBit);
        }

        MarkCellDirty(from);
        MarkCellDirty(to);

        UpdateIntersectionState(fromCell);
        UpdateIntersectionState(toCell);
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
        data.InvalidCustomRuleCount = 0;
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
        if (neighborConnectionCount >= 3)
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

    private int GetLaneCount(RoadType roadType)
    {
        if (roadType == null) return 0;
        return roadType.isTwoWay ? roadType.lanesPerWay * 2 : roadType.lanesPerWay;
    }

    private bool IsTwoWay(RoadType roadType)
    {
        return roadType != null && roadType.isTwoWay;
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
