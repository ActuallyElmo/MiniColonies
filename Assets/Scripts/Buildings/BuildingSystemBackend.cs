using System;
using System.Collections.Generic;
using UnityEngine;

public class BuildingSystemBackend : MonoBehaviour
{
    public static BuildingSystemBackend Instance { get; private set; }

    // Pure Data Collections
    private Dictionary<Vector2Int, Building> _gridOccupancy = new Dictionary<Vector2Int, Building>();
    private List<Building> _activeBuildings = new List<Building>();
    // Reused scratch space for ambiguous placement height selection.
    private int[] _placementLayerCounts = Array.Empty<int>();
    private readonly HashSet<BuildingRangeEvaluation> _rangeWork = new HashSet<BuildingRangeEvaluation>();
    private readonly System.Diagnostics.Stopwatch _rangeFallbackTimer = new System.Diagnostics.Stopwatch();
    private int _rangeFallbackFrame = -1;
    public int AuthoringRevision { get; private set; }

    // Events for other systems (like RoadNetworkManager) to react to
    public event Action<Building> OnBuildingPlaced;
    public event Action<Building> OnBuildingRemoved;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnDisable()
    {
        // Cancellation invokes completion handlers, so iterate a stable copy.
        var pending = new List<BuildingRangeEvaluation>(_rangeWork);
        foreach (BuildingRangeEvaluation work in pending) work.Cancel();
        _rangeWork.Clear();
        StopAllCoroutines();
    }

    public BuildingRangeEvaluation RequestPlacementRanges(WorldManager world, BuildingData data,
        Vector2Int origin, IReadOnlyList<BuildingTile> footprint)
    {
        var cells = new Vector2Int[footprint != null ? footprint.Count : 0];
        for (int i = 0; i < cells.Length; i++) cells[i] = origin + (footprint[i] != null ? footprint[i].localPosition : Vector2Int.zero);
        return ScheduleRanges(new BuildingRangeEvaluation(this, world, data, cells, null));
    }

    // The returned snapshot includes unique active buildings touched by each
    // coverage area, excluding its owner. Effects can consume this same query.
    public BuildingRangeEvaluation RequestBuildingRanges(WorldManager world, Building building)
    {
        var cells = new Vector2Int[building != null ? building.occupiedCells.Count : 0];
        if (building != null) building.occupiedCells.CopyTo(cells);
        return ScheduleRanges(new BuildingRangeEvaluation(this, world, building != null ? building.data : null, cells, building));
    }

    private BuildingRangeEvaluation ScheduleRanges(BuildingRangeEvaluation work)
    {
        _rangeWork.Add(work);
        work.Finished += HandleRangeFinished;
        if (Application.isPlaying)
        {
            if (SimulationTaskManager.Instance != null) SimulationTaskManager.Instance.EnqueueTask(work);
            else StartCoroutine(ProcessRanges(work));
        }
        return work;
    }

    private void HandleRangeFinished(BuildingRangeEvaluation work) => _rangeWork.Remove(work);

    private System.Collections.IEnumerator ProcessRanges(BuildingRangeEvaluation work)
    {
        // A missing task manager still gets a bounded real-frame fallback.
        yield return null;
        while (!work.IsComplete)
        {
            if (_rangeFallbackFrame != Time.frameCount)
            {
                _rangeFallbackFrame = Time.frameCount;
                _rangeFallbackTimer.Restart();
            }
            work.Process(_rangeFallbackTimer, 1f);
            if (!work.IsComplete) yield return null;
        }
    }

    public bool CanPlaceBuildingWithRanges(WorldManager world, BuildingData data, Vector2Int origin,
        IReadOnlyList<BuildingTile> footprint, bool[] cellValidity, BuildingRangeEvaluation ranges, out int requiredLayer)
    {
        bool footprintValid = CanPlaceBuilding(world, origin, footprint, cellValidity, out requiredLayer);
        return footprintValid && AreRangeRequirementsSatisfied(world, data, origin, footprint, ranges);
    }

    public bool AreRangeRequirementsSatisfied(WorldManager world, BuildingData data, Vector2Int origin,
        IReadOnlyList<BuildingTile> footprint, BuildingRangeEvaluation ranges)
    {
        if (!isActiveAndEnabled || data == null) return false;
        if (data.ranges == null || data.ranges.Count == 0) return true;
        bool requiresMatchingTile = false;
        foreach (BuildingRange range in data.ranges)
        {
            if (range == null || !range.HasValidRadius) return false;
            requiresMatchingTile |= range.RequiresMatchingTile;
        }
        if (!requiresMatchingTile) return true;
        return ranges != null && ranges.MatchesPlacement(world, data, origin, footprint) &&
            ranges.IsComplete && !ranges.IsCancelled && ranges.AllRequirementsMet;
    }

    // --- DATA ENDPOINTS ---

    public bool IsCellOccupied(Vector2Int cell) 
    { 
        return _gridOccupancy.ContainsKey(cell); 
    }

    public bool TryGetBuildingAt(Vector2Int cell, out Building building)
    {
        if (_gridOccupancy.TryGetValue(cell, out building) && building != null && building.isActiveAndEnabled)
            return true;
        building = null;
        return false;
    }

    // The preview and placement click use the same rules. Fill every result even
    // after a failure so the preview can highlight only the obstructed cells.
    public bool CanPlaceBuilding(
        WorldManager world,
        Vector2Int origin,
        IReadOnlyList<BuildingTile> footprint,
        bool[] cellValidity,
        out int requiredLayer)
    {
        requiredLayer = world != null ? world.GetCellLayer(origin.x, origin.y) : 0;
        if (cellValidity != null) Array.Clear(cellValidity, 0, cellValidity.Length);
        if (world == null || footprint == null || footprint.Count == 0) return false;
        if (world.IsGenerating) return false;
        if (cellValidity != null && cellValidity.Length < footprint.Count)
            throw new ArgumentException("A validity result is required for each footprint cell.", nameof(cellValidity));

        requiredLayer = GetPlacementLayer(world, origin, footprint, requiredLayer);

        bool allValid = true;
        for (int i = 0; i < footprint.Count; i++)
        {
            BuildingTile tile = footprint[i];
            bool valid = false;
            if (tile != null)
            {
                Vector2Int position = origin + tile.localPosition;
                valid = BuildingGroundPlacement.Allows(tile, world.GetTileInfo(position), requiredLayer) &&
                        !IsCellOccupied(position) &&
                        (RoadSystemBackend.Instance == null ||
                         !RoadSystemBackend.Instance.Roads.ContainsKey(position));
            }

            if (cellValidity != null) cellValidity[i] = valid;
            allValid &= valid;
        }
        return allValid;
    }

    private int GetPlacementLayer(
        WorldManager world, Vector2Int origin, IReadOnlyList<BuildingTile> footprint, int originLayer)
    {
        // A flat cursor cell expresses the player's intent. A transition has no
        // reliable layer, so infer it from actual flat terrain under the footprint.
        if (!world.IsExtrusionCell(origin.x, origin.y) && world.IsLayerBuildable(originLayer) &&
            !world.IsTransition(origin.x, origin.y)) return originLayer;

        int layerCount = world.layers.Count;
        if (_placementLayerCounts.Length != layerCount) _placementLayerCounts = new int[layerCount];
        else Array.Clear(_placementLayerCounts, 0, layerCount);

        foreach (BuildingTile tile in footprint)
        {
            if (tile == null) continue;
            Vector2Int position = origin + tile.localPosition;
            if (world.IsExtrusionCell(position.x, position.y)) continue;
            int layer = world.GetCellLayer(position.x, position.y);
            if (world.IsLayerBuildable(layer) && !world.IsTransition(position.x, position.y))
                _placementLayerCounts[layer]++;
        }

        int selectedLayer = originLayer;
        int largestCount = 0;
        float closestHeight = float.PositiveInfinity;
        float cursorHeight = world.GetPhysicalHeight(origin.x + 0.5f, origin.y + 0.5f);
        for (int layer = 0; layer < layerCount; layer++)
        {
            int count = _placementLayerCounts[layer];
            if (count == 0) continue;
            float distance = Mathf.Abs(layer - cursorHeight);
            // Ascending layer order also makes exact height ties deterministic.
            if (count > largestCount || (count == largestCount && distance < closestHeight))
            {
                selectedLayer = layer;
                largestCount = count;
                closestHeight = distance;
            }
        }
        return selectedLayer;
    }

    public bool TryGetPortAt(Vector2Int cell, out PortType type)
    {
        if (_gridOccupancy.TryGetValue(cell, out Building building))
        {
            if (building != null &&
                building.globalPorts.TryGetValue(cell, out type) &&
                type != PortType.None)
            {
                return true;
            }
        }
        type = PortType.None;
        return false;
    }

    public bool CanBuildRoadAt(Vector2Int cell)
    {
        return !_gridOccupancy.ContainsKey(cell) || TryGetPortAt(cell, out _);
    }

    public IReadOnlyList<Building> GetActiveBuildings()
    {
        return _activeBuildings;
    }

    public void RequestPopTransportDispatch()
    {
        for (int i = 0; i < _activeBuildings.Count; i++)
        {
            Building building = _activeBuildings[i];
            if (building != null)
            {
                building.RequestPopTransportDispatch();
            }
        }
    }

    public void RequestResourceTransportDispatch()
    {
        for (int i = 0; i < _activeBuildings.Count; i++)
            if (_activeBuildings[i] != null) _activeBuildings[i].RequestResourceTransportDispatch();
    }

    // --- MUTATION ENDPOINTS ---

    public void RegisterBuilding(Building building, List<BuildingTile> activeFootprint)
    {
        if (building == null || activeFootprint == null || _activeBuildings.Contains(building))
        {
            return;
        }

        foreach (var tile in activeFootprint)
        {
            Vector2Int globalPos = building.originCell + tile.localPosition;
            _gridOccupancy[globalPos] = building;
        }
        
        _activeBuildings.Add(building);
        IncrementAuthoringRevision();
        OnBuildingPlaced?.Invoke(building);
        RequestResourceTransportDispatch();
    }

    public bool NotifyBuildingAuthoringChanged(Building building)
    {
        if (building == null || !_activeBuildings.Contains(building)) return false;
        IncrementAuthoringRevision();
        return true;
    }

    public bool TryDemolishBuilding(Vector2Int cell)
    {
        if (_gridOccupancy.TryGetValue(cell, out Building buildingToRemove))
        {
            // 1. Clean up occupied cells and potential road ports
            foreach (Vector2Int occupiedCell in buildingToRemove.occupiedCells)
            {
                if (buildingToRemove.globalPorts.ContainsKey(occupiedCell))
                {
                    if (RoadSystemBackend.Instance != null && RoadSystemBackend.Instance.Roads.ContainsKey(occupiedCell))
                    {
                        RoadSystemBackend.Instance.RemoveRoadBlock(occupiedCell); 
                    }
                }
                _gridOccupancy.Remove(occupiedCell);
            }

            // 2. Remove from active list
            _activeBuildings.Remove(buildingToRemove);
            IncrementAuthoringRevision();
            
            // 3. Notify systems
            OnBuildingRemoved?.Invoke(buildingToRemove);

            // Return any resource cargo carried toward the demolished factory.
            for (int i = 0; i < _activeBuildings.Count; i++)
            {
                Building owner = _activeBuildings[i];
                if (owner == null) continue;
                var vehicles = owner.OwnedVehicles;
                for (int v = 0; v < vehicles.Count; v++)
                    if (vehicles[v] != null && vehicles[v].targetBuilding == buildingToRemove)
                        vehicles[v].RecallAndDeactivate();
            }

            // 4. Destroy the Unity Object
            Destroy(buildingToRemove.gameObject);
            return true;
        }
        return false;
    }

    private void IncrementAuthoringRevision()
    {
        unchecked
        {
            AuthoringRevision++;
            if (AuthoringRevision <= 0) AuthoringRevision = 1;
        }
    }
}
