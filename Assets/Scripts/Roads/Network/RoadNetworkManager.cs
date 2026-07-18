using System.Collections.Generic;
using UnityEngine;

public class RoadNetworkManager : MonoBehaviour
{
    public static RoadNetworkManager Instance { get; private set; }
    
    public WorldManager worldManager;
    public List<RoadNetwork> activeNetworks = new List<RoadNetwork>();
    public Dictionary<Vector2Int, RoadNetwork> cellToNetwork = new Dictionary<Vector2Int, RoadNetwork>();

    // Accumulation Buffers
    private HashSet<Vector2Int> _dirtyRoadCells = new HashSet<Vector2Int>();
    private HashSet<Building> _dirtyBuildings = new HashSet<Building>();
    
    private bool _isTaskRunning = false;
    private bool _commitRequested = false; // NEW: The gatekeeper flag
    
    public int networkIdCounter = 0;

    public event System.Action OnNetworkReady;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        if (BuildingSystemBackend.Instance != null)
        {
            BuildingSystemBackend.Instance.OnBuildingPlaced += HandleBuildingChanged;
            BuildingSystemBackend.Instance.OnBuildingRemoved += HandleBuildingRemoved; 
        }

        if (RoadSystemBackend.Instance != null)
        {
            RoadSystemBackend.Instance.OnRoadCellChanged += HandleRoadChanged;
        }
    }

    private void OnDestroy()
    {
        if (BuildingSystemBackend.Instance != null)
        {
            BuildingSystemBackend.Instance.OnBuildingPlaced -= HandleBuildingChanged;
            BuildingSystemBackend.Instance.OnBuildingRemoved -= HandleBuildingRemoved;
        }

        if (RoadSystemBackend.Instance != null)
        {
            RoadSystemBackend.Instance.OnRoadCellChanged -= HandleRoadChanged;
        }
    }

    // --- NEW: THE COMMIT GATEWAY ---
    public void CommitNetworkChanges()
    {
        _commitRequested = true;
    }

    private void Update()
    {
        // CHANGE: We ONLY process the task if a Commit has been requested 
        // (i.e., the player let go of the mouse).
        if (_commitRequested && !_isTaskRunning && (_dirtyRoadCells.Count > 0 || _dirtyBuildings.Count > 0))
        {
            _isTaskRunning = true;
            _commitRequested = false; // Reset the flag
            
            HashSet<Vector2Int> roadSnapshot = new HashSet<Vector2Int>(_dirtyRoadCells);
            HashSet<Building> buildingSnapshot = new HashSet<Building>(_dirtyBuildings);
            
            Dictionary<Vector2Int, RoadCell> mapSnapshot =
                new Dictionary<Vector2Int, RoadCell>(RoadSystemBackend.Instance.Roads.Count);
            foreach (KeyValuePair<Vector2Int, RoadCell> pair in RoadSystemBackend.Instance.Roads)
            {
                mapSnapshot.Add(pair.Key, pair.Value);
            }

            _dirtyRoadCells.Clear();
            _dirtyBuildings.Clear();

            RoadNetworkGenerationTask task = new RoadNetworkGenerationTask(roadSnapshot, buildingSnapshot, mapSnapshot, this);
            SimulationTaskManager.Instance.EnqueueTask(task);
        }
    }

    // --- EVENT HANDLERS ---

    public void HandleRoadChanged(Vector2Int cell)
    {
        // This is still called instantly during the drag, 
        // silently accumulating the data without triggering the heavy graph rebuild!
        _dirtyRoadCells.Add(cell);
        
        List<Vector2Int> adjacent = GetAdjacentArea(cell);
        foreach(Vector2Int adj in adjacent)
        {
            if (BuildingSystemBackend.Instance != null && BuildingSystemBackend.Instance.IsCellOccupied(adj))
            {
                foreach (Building b in BuildingSystemBackend.Instance.GetActiveBuildings())
                {
                    if (b.globalPorts.ContainsKey(adj)) _dirtyBuildings.Add(b);
                }
            }
        }
    }

    public void HandleBuildingChanged(Building building)
    {
        if (building != null) 
        {
            _dirtyBuildings.Add(building);
            CommitNetworkChanges(); // Auto-commit since buildings are single-click placements
        }
    }

    public void OnTaskCompleted()
    {
        _isTaskRunning = false;
        OnNetworkReady?.Invoke();
    }

    public void HandleBuildingRemoved(Building building)
    {
        if (building == null) return;
        
        foreach (RoadNetwork net in activeNetworks)
        {
            net.connectedBuildings.Remove(building);
        }
        
        _dirtyBuildings.Remove(building);
        CommitNetworkChanges(); // Auto-commit to severe logical connections immediately
    }

    // Utility 
    private List<Vector2Int> GetAdjacentArea(Vector2Int pos)
    {
        return new List<Vector2Int>
        {
            pos, 
            new Vector2Int(pos.x, pos.y + 1),   new Vector2Int(pos.x + 1, pos.y + 1),
            new Vector2Int(pos.x + 1, pos.y),   new Vector2Int(pos.x + 1, pos.y - 1),
            new Vector2Int(pos.x, pos.y - 1),   new Vector2Int(pos.x - 1, pos.y - 1),
            new Vector2Int(pos.x - 1, pos.y),   new Vector2Int(pos.x - 1, pos.y + 1)
        };
    }
}
