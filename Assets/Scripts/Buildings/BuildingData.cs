using System.Collections.Generic;
using UnityEngine;

public enum PortType { None, Entry, Exit, Both }

[System.Serializable]
public class BuildingTile
{
    [Tooltip("Local grid position relative to the building's origin (0,0). Max 3,3 for a 4x4 building.")]
    public Vector2Int localPosition;
    
    [Tooltip("Does this specific tile connect to the road network?")]
    public bool isPort;
    
    [Tooltip("If it is a port, what kind of traffic is allowed?")]
    public PortType portType;
}

[CreateAssetMenu(fileName = "NewBuilding", menuName = "ColonySim/Building")]
public class BuildingData : ScriptableObject
{
    public string buildingName = "New Building";
    public GameObject prefab;
    public List<BuildingTile> footprint = new List<BuildingTile>();

    public List<BuildingTile> GetRotatedFootprint(int rotationSteps)
    {
        List<BuildingTile> rotated = new List<BuildingTile>();
        foreach (var tile in footprint)
        {
            Vector2Int newPos = tile.localPosition;
            
            // Apply 90-degree clockwise rotation math (x' = y, y' = -x)
            for (int i = 0; i < rotationSteps % 4; i++)
            {
                newPos = new Vector2Int(newPos.y, -newPos.x);
            }
            
            rotated.Add(new BuildingTile { 
                localPosition = newPos, 
                isPort = tile.isPort, 
                portType = tile.portType 
            });
        }
        return rotated;
    }
}