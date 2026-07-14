using System.Collections.Generic;
using UnityEngine;
using System;

// Kept as a pure data class. (Later, for Burst, this can become a strict struct).
public class RoadCell
{
    public Vector2Int gridPosition;
    public int elevationLayer;
    public RoadType roadType; 
    
    public int connections = 0;
    public int outConnections = 0; 
    public int inConnections = 0;  
    
    public bool isPreview = false;

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
}

public class RoadSystemBackend : MonoBehaviour
{
    public static RoadSystemBackend Instance { get; private set; }

    [Header("Debugging")]
    public bool showDebugRoads = true;
    public Color debugNodeColor = Color.blue;
    public Color debugLineColor = Color.cyan;

    public Dictionary<Vector2Int, RoadCell> Roads { get; private set; } = new Dictionary<Vector2Int, RoadCell>();
    
    public event Action<HashSet<Vector2Int>> OnChunksDirty;
    public event Action<Vector2Int> OnRoadCellChanged;
    
    private HashSet<Vector2Int> _dirtyChunksThisFrame = new HashSet<Vector2Int>();
    private WorldManager _worldManager;

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
            if (Roads.TryGetValue(pos, out RoadCell cell) && cell.isPreview)
            {
                cell.isPreview = false;
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
                Roads.Remove(pos);
            }
            else
            {
                if (Roads.TryGetValue(pos, out RoadCell cell))
                {
                    cell.connections = snap.connections;
                    cell.inConnections = snap.inConnections;
                    cell.outConnections = snap.outConnections;
                    cell.roadType = snap.type;
                    cell.isPreview = snap.wasPreview;
                }
            }
            MarkCellDirty(pos);
        }
        _previewSnapshots.Clear();
    }

    private void SaveSnapshot(Vector2Int pos)
    {
        if (!IsPreviewMode || _previewSnapshots.ContainsKey(pos)) return;

        if (Roads.TryGetValue(pos, out RoadCell cell))
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
        if (type == null || Roads.ContainsKey(cell)) return;

        SaveSnapshot(cell); // SNAPSHOT
        
        int layer = _worldManager.GetCellLayer(cell.x, cell.y);
        RoadCell newCell = new RoadCell(cell, layer, type);
        
        if (IsPreviewMode) newCell.isPreview = true; // FLAG TEMPORARY
        
        Roads.Add(cell, newCell);
        MarkCellDirty(cell);
    }

    public void RemoveRoadBlock(Vector2Int cell)
    {
        if (!Roads.TryGetValue(cell, out RoadCell roadToRemove)) return;

        SaveSnapshot(cell); // SNAPSHOT

        for (int i = 0; i < 8; i++)
        {
            int bit = 1 << i;
            if (roadToRemove.HasConnection(bit))
            {
                Vector2Int neighborPos = GetNeighborPosition(cell, bit);
                if (Roads.TryGetValue(neighborPos, out RoadCell neighbor))
                {
                    SaveSnapshot(neighborPos); // SNAPSHOT NEIGHBORS
                    int reverseBit = GetDirectionBit(neighborPos, cell);
                    neighbor.RemoveConnection(reverseBit);
                    MarkCellDirty(neighborPos); 
                }
            }
        }
        
        MarkCellDirty(cell);
        Roads.Remove(cell);
    }

    public void UpgradeRoadBlock(Vector2Int cell, RoadType newType)
    {
        if (Roads.TryGetValue(cell, out RoadCell road))
        {
            if (road.roadType != newType)
            {
                SaveSnapshot(cell); // SNAPSHOT
                road.roadType = newType;
                MarkCellDirty(cell);
            }
        }
    }

    public void ConnectCellsSafe(Vector2Int from, Vector2Int to, RoadType selectedRoadType)
    {
        SaveSnapshot(from); // SNAPSHOT
        SaveSnapshot(to);   // SNAPSHOT

        if (!Roads.ContainsKey(from)) PlaceRoadBlock(from, selectedRoadType);
        if (!Roads.ContainsKey(to)) PlaceRoadBlock(to, selectedRoadType);

        RoadCell toCell = Roads[to];
        RoadCell fromCell = Roads[from];

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
                fromCell.outConnections |= fromToToBit;
                toCell.inConnections |= toToFromBit;
                if (fromCell.roadType.isTwoWay) fromCell.inConnections |= fromToToBit;
                if (toCell.roadType.isTwoWay) toCell.outConnections |= toToFromBit;
            }
            else
            {
                fromCell.inConnections |= fromToToBit;
                toCell.outConnections |= toToFromBit;
                if (fromCell.roadType.isTwoWay) fromCell.outConnections |= fromToToBit;
                if (toCell.roadType.isTwoWay) toCell.inConnections |= toToFromBit;
            }
        }
        else
        {
            fromCell.outConnections |= fromToToBit;
            fromCell.inConnections |= fromToToBit;
            toCell.outConnections |= toToFromBit;
            toCell.inConnections |= toToFromBit;
        }

        MarkCellDirty(from);
        MarkCellDirty(to);
    }

    // --- INTERNAL LOGIC ---

    private void MarkCellDirty(Vector2Int cell)
    {
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

    public int GetDirectionBit(Vector2Int from, Vector2Int to)
    {
        int dx = to.x - from.x;
        int dy = to.y - from.y;
        if (dx == 0 && dy == 1) return 1;    if (dx == 1 && dy == 1) return 2;
        if (dx == 1 && dy == 0) return 4;    if (dx == 1 && dy == -1) return 8;
        if (dx == 0 && dy == -1) return 16;  if (dx == -1 && dy == -1) return 32;
        if (dx == -1 && dy == 0) return 64;  if (dx == -1 && dy == 1) return 128;
        return 0;
    }

    public Vector2Int GetNeighborPosition(Vector2Int pos, int bit)
    {
        switch (bit)
        {
            case 1: return new Vector2Int(pos.x, pos.y + 1);    case 2: return new Vector2Int(pos.x + 1, pos.y + 1);
            case 4: return new Vector2Int(pos.x + 1, pos.y);    case 8: return new Vector2Int(pos.x + 1, pos.y - 1);
            case 16: return new Vector2Int(pos.x, pos.y - 1);   case 32: return new Vector2Int(pos.x - 1, pos.y - 1);
            case 64: return new Vector2Int(pos.x - 1, pos.y);   case 128: return new Vector2Int(pos.x - 1, pos.y + 1);
            default: return pos;
        }
    }
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showDebugRoads || Roads == null || _worldManager == null) return;
        foreach (var kvp in Roads)
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