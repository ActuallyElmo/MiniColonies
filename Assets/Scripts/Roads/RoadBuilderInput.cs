using UnityEngine;
using UnityEngine.EventSystems;

public class RoadBuilderInput : MonoBehaviour
{
    [Tooltip("How close to the center (0.0 to 0.5) the mouse must be to place a road.")]
    public float centerSnapThreshold = 0.35f;

    private bool _isDragging = false;
    private bool _madeChangesDuringDrag = false; // Tracks if a commit is needed
    private Vector2Int _lastDragCell;

    private void Update()
    {
        HandleInput();
    }

    private void HandleInput()
    {
        if (PlayerActionManager.Instance == null || RoadSystemBackend.Instance == null || WorldManager.Instance == null) return;

        PlayerMode currentMode = PlayerActionManager.Instance.currentMode;

        if (currentMode != PlayerMode.BuildRoad && currentMode != PlayerMode.Delete && currentMode != PlayerMode.UpgradeRoad) 
        {
            _isDragging = false;
            _madeChangesDuringDrag = false;
            return;
        }

        // 1. Detect Mouse Down
        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Vector2Int? cell = GetCellUnderMouse(out Vector3 hitPoint);
            
            if (cell.HasValue && IsCloseToCenter(cell.Value, hitPoint))
            {
                RoadSystemBackend.Instance.BeginPreview(); // OPEN TRANSACTION
                
                _isDragging = true;
                _lastDragCell = cell.Value;
                ProcessActionAtCell(cell.Value, currentMode);
                _madeChangesDuringDrag = true; 
            }
        }

        // 2. Handle Dragging
        if (_isDragging)
        {
            // --- NEW: RIGHT CLICK TO CANCEL ---
            if (Input.GetMouseButtonDown(1)) 
            {
                RoadSystemBackend.Instance.CancelPreview(); // ROLLBACK TRANSACTION
                _isDragging = false;
                _madeChangesDuringDrag = false;
                return;
            }

            Vector2Int? currentCell = GetCellUnderMouse(out Vector3 hitPoint);
            
            if (currentCell.HasValue && currentCell.Value != _lastDragCell)
            {
                if (currentMode == PlayerMode.BuildRoad)
                {
                    if (IsAdjacent(_lastDragCell, currentCell.Value) && IsCloseToCenter(currentCell.Value, hitPoint))
                    {
                        if (IsValidPlacement(currentCell.Value) && CanConnect(_lastDragCell, currentCell.Value))
                        {
                            RoadSystemBackend.Instance.ConnectCellsSafe(_lastDragCell, currentCell.Value, PlayerActionManager.Instance.SelectedRoadType);
                            _lastDragCell = currentCell.Value;
                            _madeChangesDuringDrag = true; 
                        }
                    }
                }
                else
                {
                    if (IsCloseToCenter(currentCell.Value, hitPoint))
                    {
                        ProcessActionAtCell(currentCell.Value, currentMode);
                        _lastDragCell = currentCell.Value;
                        _madeChangesDuringDrag = true; 
                    }
                }
            }
        }

        // 3. Detect Mouse Up (The Commit Phase)
        if (Input.GetMouseButtonUp(0))
        {
            if (_isDragging && _madeChangesDuringDrag)
            {
                RoadSystemBackend.Instance.CommitPreview(); // COMMIT TRANSACTION

                if (RoadNetworkManager.Instance != null)
                {
                    RoadNetworkManager.Instance.CommitNetworkChanges();
                }
            }

            _isDragging = false;
            _madeChangesDuringDrag = false;
        }
    }

    private void ProcessActionAtCell(Vector2Int cell, PlayerMode mode)
    {
        RoadType selectedType = PlayerActionManager.Instance.SelectedRoadType;

        switch (mode)
        {
            case PlayerMode.BuildRoad:
                if (IsValidPlacement(cell)) 
                    RoadSystemBackend.Instance.PlaceRoadBlock(cell, selectedType);
                break;

            case PlayerMode.Delete:
                RoadSystemBackend.Instance.RemoveRoadBlock(cell);
                break;

            case PlayerMode.UpgradeRoad:
                if (selectedType != null)
                    RoadSystemBackend.Instance.UpgradeRoadBlock(cell, selectedType);
                break;
        }
    }

    // --- VALIDATION & RAYCASTING ---
    // (Everything below here remains exactly the same as your current file)

    private Vector2Int? GetCellUnderMouse(out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        int layerMask = 1 << LayerMask.NameToLayer("Terrain");

        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, layerMask))
        {
            hitPoint = hit.point;
            int x = Mathf.FloorToInt(hit.point.x / WorldManager.Instance.cellSize);
            int z = Mathf.FloorToInt(hit.point.z / WorldManager.Instance.cellSize);
            return new Vector2Int(x, z);
        }
        return null;
    }

    private bool IsCloseToCenter(Vector2Int cell, Vector3 hitPoint)
    {
        float cellSize = WorldManager.Instance.cellSize;
        float cellCenterWorldX = cell.x * cellSize + (cellSize * 0.5f);
        float cellCenterWorldZ = cell.y * cellSize + (cellSize * 0.5f);
        
        Vector2 center2D = new Vector2(cellCenterWorldX, cellCenterWorldZ);
        Vector2 hit2D = new Vector2(hitPoint.x, hitPoint.z);
        
        return Vector2.Distance(center2D, hit2D) <= (cellSize * centerSnapThreshold);
    }

    private bool IsAdjacent(Vector2Int a, Vector2Int b)
    {
        int dx = Mathf.Abs(a.x - b.x);
        int dy = Mathf.Abs(a.y - b.y);
        return (dx <= 1 && dy <= 1) && (dx + dy > 0);
    }

    private bool IsValidPlacement(Vector2Int cell)
    {
        if (WorldManager.Instance.IsExtrusionCell(cell.x, cell.y)) return false;
        if (!WorldManager.Instance.IsBuildableCell(cell.x, cell.y)) return false;

        if (BuildingSystemBackend.Instance != null && BuildingSystemBackend.Instance.IsCellOccupied(cell))
        {
            if (!BuildingSystemBackend.Instance.TryGetPortAt(cell, out PortType portType)) return false; 
        }

        return true; 
    }

    private bool CanConnect(Vector2Int from, Vector2Int to)
    {
        if (!WorldManager.Instance.IsBuildableCell(from.x, from.y) || !WorldManager.Instance.IsBuildableCell(to.x, to.y)) return false;
        
        bool fromIsTransition = WorldManager.Instance.IsTransition(from.x, from.y);
        bool toIsTransition = WorldManager.Instance.IsTransition(to.x, to.y);

        if (fromIsTransition && toIsTransition) return false; 

        int fromLayer = WorldManager.Instance.GetCellLayer(from.x, from.y);
        int toLayer = WorldManager.Instance.GetCellLayer(to.x, to.y);

        if (!fromIsTransition && !toIsTransition && fromLayer != toLayer) return false;

        bool fromIsPort = BuildingSystemBackend.Instance != null && BuildingSystemBackend.Instance.TryGetPortAt(from, out _);
        bool toIsPort = BuildingSystemBackend.Instance != null && BuildingSystemBackend.Instance.TryGetPortAt(to, out _);

        if (fromIsPort && toIsPort) return false;

        if (fromIsPort && RoadSystemBackend.Instance.Roads.TryGetValue(from, out RoadCell fromCell))
        {
            int newDirBit = RoadSystemBackend.Instance.GetDirectionBit(from, to);
            if (RoadSystemBackend.Instance.GetConnectionCount(fromCell) >= 1 && !fromCell.HasConnection(newDirBit))
                return false;
        }

        if (toIsPort && RoadSystemBackend.Instance.Roads.TryGetValue(to, out RoadCell toCell))
        {
            int newDirBit = RoadSystemBackend.Instance.GetDirectionBit(to, from);
            if (RoadSystemBackend.Instance.GetConnectionCount(toCell) >= 1 && !toCell.HasConnection(newDirBit))
                return false;
        }

        if (fromIsTransition || toIsTransition)
        {
            Vector2Int transitionCell = fromIsTransition ? from : to;
            Vector2Int flatCell = fromIsTransition ? to : from;

            if (RoadSystemBackend.Instance.Roads.TryGetValue(transitionCell, out RoadCell transitionRoad))
            {
                if (RoadSystemBackend.Instance.GetConnectionCount(transitionRoad) >= 2) return false;

                if (RoadSystemBackend.Instance.GetConnectionCount(transitionRoad) == 1)
                {
                    int existingDirBit = transitionRoad.connections; 
                    int newDirBit = RoadSystemBackend.Instance.GetDirectionBit(transitionCell, flatCell);

                    if (!IsOppositeDirection(existingDirBit, newDirBit)) return false;

                    Vector2Int otherFlatPos = RoadSystemBackend.Instance.GetNeighborPosition(transitionCell, existingDirBit);
                    int otherFlatLayer = WorldManager.Instance.GetCellLayer(otherFlatPos.x, otherFlatPos.y);
                    int newFlatLayer = WorldManager.Instance.GetCellLayer(flatCell.x, flatCell.y);

                    if (otherFlatLayer == newFlatLayer) return false; 
                }
            }
        }
        return true; 
    }

    private bool IsOppositeDirection(int bitA, int bitB)
    {
        switch (bitA)
        {
            case 1: return bitB == 16;  case 2: return bitB == 32;
            case 4: return bitB == 64;  case 8: return bitB == 128;
            case 16: return bitB == 1;  case 32: return bitB == 2;
            case 64: return bitB == 4;  case 128: return bitB == 8;
            default: return false;
        }
    }
}