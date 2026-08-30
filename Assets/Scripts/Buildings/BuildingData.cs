using System.Collections.Generic;
using UnityEngine;

public enum PortFlowDirection { Entry, Exit }

public enum PortType
{
    None = 0,
    [InspectorName("All Vehicles Entry")] Entry = 1,
    [InspectorName("All Vehicles Exit")] Exit = 2,
    [InspectorName("All Vehicles Both")] Both = 3,
    [InspectorName("Pop Transport Entry")] PopEntry = 4,
    [InspectorName("Pop Transport Exit")] PopExit = 5,
    [InspectorName("Pop Transport Both")] PopBoth = 6,
    [InspectorName("Resource Transport Entry")] ResourceEntry = 7,
    [InspectorName("Resource Transport Exit")] ResourceExit = 8,
    [InspectorName("Resource Transport Both")] ResourceBoth = 9
}

public static class PortTypeUtility
{
    public static bool AllowsFlow(this PortType portType, PortFlowDirection flowDirection)
    {
        switch (flowDirection)
        {
            case PortFlowDirection.Entry:
                return portType == PortType.Entry ||
                       portType == PortType.Both ||
                       portType == PortType.PopEntry ||
                       portType == PortType.PopBoth ||
                       portType == PortType.ResourceEntry ||
                       portType == PortType.ResourceBoth;
            case PortFlowDirection.Exit:
                return portType == PortType.Exit ||
                       portType == PortType.Both ||
                       portType == PortType.PopExit ||
                       portType == PortType.PopBoth ||
                       portType == PortType.ResourceExit ||
                       portType == PortType.ResourceBoth;
            default:
                return false;
        }
    }

    public static bool AllowsVehicle(this PortType portType, VehicleData vehicleData)
    {
        if (portType == PortType.None) return false;

        bool isPopPort =
            portType == PortType.PopEntry ||
            portType == PortType.PopExit ||
            portType == PortType.PopBoth;
        bool isResourcePort =
            portType == PortType.ResourceEntry ||
            portType == PortType.ResourceExit ||
            portType == PortType.ResourceBoth;

        if (!isPopPort && !isResourcePort) return true;
        if (vehicleData is PopTransportVehicleData) return isPopPort;
        if (vehicleData is ResourceTransportVehicleData) return isResourcePort;
        return false;
    }

    public static bool AllowsVehicleFlow(
        this PortType portType,
        PortFlowDirection flowDirection,
        VehicleData vehicleData)
    {
        return portType.AllowsFlow(flowDirection) &&
               portType.AllowsVehicle(vehicleData);
    }
}

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
