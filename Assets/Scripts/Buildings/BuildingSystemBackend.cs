using System;
using System.Collections.Generic;
using UnityEngine;

public class BuildingSystemBackend : MonoBehaviour
{
    public static BuildingSystemBackend Instance { get; private set; }

    // Pure Data Collections
    private Dictionary<Vector2Int, Building> _gridOccupancy = new Dictionary<Vector2Int, Building>();
    private List<Building> _activeBuildings = new List<Building>();

    // Events for other systems (like RoadNetworkManager) to react to
    public event Action<Building> OnBuildingPlaced;
    public event Action<Building> OnBuildingRemoved;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // --- DATA ENDPOINTS ---

    public bool IsCellOccupied(Vector2Int cell) 
    { 
        return _gridOccupancy.ContainsKey(cell); 
    }

    public bool TryGetPortAt(Vector2Int cell, out PortType type)
    {
        if (_gridOccupancy.TryGetValue(cell, out Building building))
        {
            if (building.globalPorts.TryGetValue(cell, out type)) return true;
        }
        type = PortType.None;
        return false;
    }

    public List<Building> GetActiveBuildings() 
    {
        return _activeBuildings;
    }

    // --- MUTATION ENDPOINTS ---

    public void RegisterBuilding(Building building, List<BuildingTile> activeFootprint)
    {
        foreach (var tile in activeFootprint)
        {
            Vector2Int globalPos = building.originCell + tile.localPosition;
            _gridOccupancy[globalPos] = building;
        }
        
        _activeBuildings.Add(building);
        OnBuildingPlaced?.Invoke(building);
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
            
            // 3. Notify systems
            OnBuildingRemoved?.Invoke(buildingToRemove);

            // 4. Destroy the Unity Object
            Destroy(buildingToRemove.gameObject);
            return true;
        }
        return false;
    }
}