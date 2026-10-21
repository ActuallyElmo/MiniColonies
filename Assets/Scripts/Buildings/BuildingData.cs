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

    [HideInInspector]
    public List<BuildingGroundType> allowedGrounds = new List<BuildingGroundType>();

    [Tooltip("Additional tile permissions. Resource nodes/deposits are listed automatically from map definitions. Empty uses normal flat-ground placement.")]
    public List<WorldTileType> allowedTileTypes = new List<WorldTileType>();

    [Tooltip("At least one of these types must match. Matching required types also grant permission. Optional terrain permissions cannot satisfy a required resource.")]
    public List<WorldTileType> requiredTileTypes = new List<WorldTileType>();

    [Tooltip("Replaces all terrain/resource rule icons in this footprint cell with one centered icon. Does not change placement rules or port indicators. Empty uses the shared tile icons.")]
    public Sprite footprintIconOverride;

    [HideInInspector]
    public bool requireGroundMatch;

    public int OptionalTileTypeCount => allowedTileTypes != null ? allowedTileTypes.Count : 0;
    public int RequiredTileTypeCount => requiredTileTypes != null ? requiredTileTypes.Count : 0;
    public int TileTypeCount => OptionalTileTypeCount + RequiredTileTypeCount;
    public bool HasTileRules => TileTypeCount > 0;
    public bool RequiresTileMatch => HasTileRules ? RequiredTileTypeCount > 0 : requireGroundMatch;

    public WorldTileType GetTileType(int index) => index < OptionalTileTypeCount
        ? allowedTileTypes[index] : requiredTileTypes[index - OptionalTileTypeCount];
}

[CreateAssetMenu(fileName = "NewBuilding", menuName = "ColonySim/Building")]
public class BuildingData : ScriptableObject
{
    public string buildingName = "New Building";
    public GameObject prefab;
    public List<BuildingTile> footprint = new List<BuildingTile>();
    [Tooltip("Named coverage areas measured from the footprint. Each range can optionally require a nearby tile type for placement.")]
    public List<BuildingRange> ranges = new List<BuildingRange>();

    public List<BuildingTile> GetRotatedFootprint(int rotationSteps)
    {
        List<BuildingTile> rotated = new List<BuildingTile>();
        if (footprint == null) return rotated;
        int steps = ((rotationSteps % 4) + 4) % 4;
        foreach (var tile in footprint)
        {
            if (tile == null) { rotated.Add(null); continue; }
            Vector2Int newPos = tile.localPosition;
            
            // Apply 90-degree clockwise rotation math (x' = y, y' = -x)
            for (int i = 0; i < steps; i++)
            {
                newPos = new Vector2Int(newPos.y, -newPos.x);
            }
            
            rotated.Add(new BuildingTile { 
                localPosition = newPos, 
                isPort = tile.isPort, 
                portType = tile.portType,
                requireGroundMatch = tile.requireGroundMatch,
                footprintIconOverride = tile.footprintIconOverride,
                allowedTileTypes = tile.allowedTileTypes != null
                    ? new List<WorldTileType>(tile.allowedTileTypes) : new List<WorldTileType>(),
                requiredTileTypes = tile.requiredTileTypes != null
                    ? new List<WorldTileType>(tile.requiredTileTypes) : new List<WorldTileType>(),
                allowedGrounds = tile.allowedGrounds != null
                    ? new List<BuildingGroundType>(tile.allowedGrounds) : new List<BuildingGroundType>()
            });
        }
        return rotated;
    }
}
