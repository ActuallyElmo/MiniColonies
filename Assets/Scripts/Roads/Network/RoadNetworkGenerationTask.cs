using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;

public class RoadNetworkGenerationTask : ISimulationTask
{
    private enum TaskState { Init, GatherDestroy, BFSRebuild, ReevaluateBuildings, Complete }
    private TaskState _state = TaskState.Init;

    // Inputs & References
    private HashSet<Vector2Int> _dirtyCells;
    private HashSet<Building> _dirtyBuildings;
    private Dictionary<Vector2Int, RoadCell> _roadSnapshot;
    private RoadNetworkManager _manager;

    // --- REUSABLE CLASS-LEVEL COLLECTIONS (ZERO GC ALLOCATIONS IN LOOPS) ---
    private HashSet<RoadNetwork> _networksToDestroy = new HashSet<RoadNetwork>();
    private HashSet<Vector2Int> _cellsToRebuild = new HashSet<Vector2Int>();
    private Queue<Vector2Int> _bfsQueue = new Queue<Vector2Int>();
    
    // Building Reevaluation Reusables
    private Dictionary<RoadNetwork, bool> _hasEntry = new Dictionary<RoadNetwork, bool>();
    private Dictionary<RoadNetwork, bool> _hasExit = new Dictionary<RoadNetwork, bool>();

    // State Tracking Iterators
    private RoadNetwork _currentNewNetwork;
    private IEnumerator<Vector2Int> _cellRebuildEnumerator;
    private IEnumerator<Building> _buildingEnumerator;

    public RoadNetworkGenerationTask(
        HashSet<Vector2Int> dirtyCells, 
        HashSet<Building> dirtyBuildings, 
        Dictionary<Vector2Int, RoadCell> roadSnapshot,
        RoadNetworkManager manager)
    {
        _dirtyCells = dirtyCells;
        _dirtyBuildings = dirtyBuildings;
        _roadSnapshot = roadSnapshot;
        _manager = manager;
    }

    public bool Process(Stopwatch timer, float maxMillisecondsPerFrame)
    {
        if (_state == TaskState.Init)
        {
            _state = TaskState.GatherDestroy;
        }

        if (_state == TaskState.GatherDestroy)
        {
            // 1. Identify which networks are touched by dirty cells
            foreach (Vector2Int dirtyCell in _dirtyCells)
            {
                if (_roadSnapshot.ContainsKey(dirtyCell)) _cellsToRebuild.Add(dirtyCell);

                List<Vector2Int> searchArea = GetAdjacentArea(dirtyCell);
                foreach (Vector2Int searchCell in searchArea)
                {
                    if (_manager.cellToNetwork.TryGetValue(searchCell, out RoadNetwork net))
                    {
                        _networksToDestroy.Add(net);
                    }
                }
            }

            // 2. Tear down old networks
            foreach (RoadNetwork net in _networksToDestroy)
            {
                foreach (Vector2Int cell in net.roadCells)
                {
                    if (_roadSnapshot.ContainsKey(cell)) _cellsToRebuild.Add(cell);
                    _manager.cellToNetwork.Remove(cell);
                }
                
                foreach (Building b in net.connectedBuildings) _dirtyBuildings.Add(b);
                _manager.activeNetworks.Remove(net);
            }

            _state = TaskState.BFSRebuild;
        }

        if (_state == TaskState.BFSRebuild)
        {
            // Continue BFS chunking until no cells are left
            while (_cellsToRebuild.Count > 0)
            {
                // Start a new network if we don't have one active
                if (_currentNewNetwork == null)
                {
                    _cellRebuildEnumerator = _cellsToRebuild.GetEnumerator();
                    _cellRebuildEnumerator.MoveNext();
                    Vector2Int startCell = _cellRebuildEnumerator.Current;

                    _currentNewNetwork = new RoadNetwork
                    {
                        id = _manager.networkIdCounter++,
                        debugColor = Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.8f, 1f)
                    };

                    _bfsQueue.Enqueue(startCell);
                    _cellsToRebuild.Remove(startCell);
                }

                // Run BFS Queue
                while (_bfsQueue.Count > 0)
                {
                    Vector2Int currentPos = _bfsQueue.Dequeue();
                    _currentNewNetwork.roadCells.Add(currentPos);
                    _manager.cellToNetwork[currentPos] = _currentNewNetwork;

                    if (_roadSnapshot.TryGetValue(currentPos, out RoadCell currentCell))
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            int bit = 1 << i;
                            if (currentCell.HasConnection(bit))
                            {
                                Vector2Int neighborPos = GetNeighborPosition(currentPos, bit);
                                if (_cellsToRebuild.Contains(neighborPos))
                                {
                                    _cellsToRebuild.Remove(neighborPos);
                                    _bfsQueue.Enqueue(neighborPos);
                                }
                            }
                        }
                    }

                    // TIMEOUT CHECK: Pause task and resume next frame
                    if (timer.ElapsedMilliseconds >= maxMillisecondsPerFrame) return false;
                }

                // Network completely mapped. Finalize it and reset for the next loop.
                _manager.activeNetworks.Add(_currentNewNetwork);
                _currentNewNetwork = null; 
            }

            // Setup Building evaluation state
            _buildingEnumerator = _dirtyBuildings.GetEnumerator();
            _state = TaskState.ReevaluateBuildings;
        }

        if (_state == TaskState.ReevaluateBuildings)
        {
            while (_buildingEnumerator.MoveNext())
            {
                Building building = _buildingEnumerator.Current;
                if (building == null) continue;

                // Remove from all existing network connections
                foreach (RoadNetwork net in _manager.activeNetworks) net.connectedBuildings.Remove(building);

                building.portNetworks.Clear();
                building.validNetworks.Clear();

                // Clear reusable collections to prevent GC Spikes
                _hasEntry.Clear();
                _hasExit.Clear();

                // Map Ports to Networks
                foreach (var kvp in building.globalPorts)
                {
                    Vector2Int portPos = kvp.Key;
                    PortType type = kvp.Value;

                    // STRICT CONNECTION: We ONLY check if the exact port cell has a road network on it.
                    // We no longer check adjacent cells. The player MUST build a road on the port.
                    if (_manager.cellToNetwork.TryGetValue(portPos, out RoadNetwork net))
                    {
                        building.portNetworks[portPos] = net; 

                        if (!_hasEntry.ContainsKey(net)) _hasEntry[net] = false;
                        if (!_hasExit.ContainsKey(net)) _hasExit[net] = false;

                        if (type == PortType.Entry || type == PortType.Both) _hasEntry[net] = true;
                        if (type == PortType.Exit || type == PortType.Both) _hasExit[net] = true;
                    }
                }

                // Validate Complete Flow
                foreach (var net in _hasEntry.Keys)
                {
                    if (_hasEntry[net] && _hasExit[net])
                    {
                        building.validNetworks.Add(net);
                        net.connectedBuildings.Add(building);
                    }
                }
                
                building.OnNetworksUpdated();

                // TIMEOUT CHECK: Pause task and resume next frame
                if (timer.ElapsedMilliseconds >= maxMillisecondsPerFrame) return false;
            }

            _state = TaskState.Complete;
        }

        if (_state == TaskState.Complete)
        {
            _manager.OnTaskCompleted();
            return true; // Task is officially done!
        }

        return false;
    }

    // --- MATH HELPERS ---
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

    private Vector2Int GetNeighborPosition(Vector2Int pos, int bit)
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
}