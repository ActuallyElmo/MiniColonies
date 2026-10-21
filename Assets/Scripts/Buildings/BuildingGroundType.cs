using UnityEngine;

public enum BuildingGroundKind { TerrainLayer, Cliff, MapResource }

// Shared authoring data: reuse one asset everywhere a ground type is offered.
// Legacy asset references remain readable. New authoring uses WorldTileType values.
public sealed class BuildingGroundType : ScriptableObject
{
    public BuildingGroundKind kind;
    public Sprite icon;
    [Tooltip("WorldManager layer index. Use the water layer index for water-height placement.")]
    [Min(0)] public int terrainLayer;
    [Tooltip("Matches the logical resource identity at any depth, independently of its decorative prefab.")]
    public ResourceType resourceType;

    public Sprite Icon => icon != null ? icon :
        kind == BuildingGroundKind.MapResource ? ResourceCatalog.GetWorldTileIcon(resourceType) : null;

    public bool Matches(int layer, bool transition, MapResourceSource source)
    {
        switch (kind)
        {
            case BuildingGroundKind.TerrainLayer: return terrainLayer >= 0 && layer == terrainLayer;
            case BuildingGroundKind.Cliff: return transition;
            case BuildingGroundKind.MapResource:
                return source != null && source.Definition != null && source.Definition.resourceType == resourceType;
            default: return false;
        }
    }

    private void OnValidate() => terrainLayer = Mathf.Max(0, terrainLayer);
}

public static class BuildingGroundPlacement
{
    public static bool Allows(BuildingTile tile, WorldTileInfo cell, int placementLayer)
    {
        if (tile == null || !cell.Playable ||
            (cell.Surface != TerrainSurfaceType.FlatGround && cell.Surface != TerrainSurfaceType.WaterLevel)) return false;
        if (!tile.HasTileRules)
            return Allows(tile, cell.Layer, placementLayer, cell.Surface == TerrainSurfaceType.FlatGround, cell.IsEdge, cell.Source);

        bool requiredMatched = tile.RequiredTileTypeCount == 0, water = false, edge = false, resource = false;
        for (int i = 0; i < tile.TileTypeCount; i++)
        {
            WorldTileType type = tile.GetTileType(i);
            if (!cell.Matches(type)) continue;
            if (i >= tile.OptionalTileTypeCount) requiredMatched = true;
            water |= type.kind == WorldTileKind.WaterLevel;
            edge |= type.kind == WorldTileKind.Edge;
            resource |= type.IsResource;
        }
        return requiredMatched &&
            (cell.Surface == TerrainSurfaceType.FlatGround || water || edge) &&
            (!cell.IsEdge || edge) &&
            (cell.Layer == placementLayer || water || edge) &&
            (cell.Source == null || !cell.Source.BlocksConstruction || resource);
    }

    // Each matching permission removes only its own obstruction. Resource
    // permission alone must not allow a mine to float over water or a cliff.
    public static bool Allows(BuildingTile tile, int layer, int placementLayer,
        bool buildable, bool transition, MapResourceSource source)
    {
        if (tile == null) return false;
        bool matched = false, allowsLayer = false, allowsCliff = false, allowsResource = false;
        if (tile.allowedGrounds != null)
        {
            foreach (BuildingGroundType ground in tile.allowedGrounds)
            {
                if (ground == null || !ground.Matches(layer, transition, source)) continue;
                matched = true;
                switch (ground.kind)
                {
                    case BuildingGroundKind.TerrainLayer: allowsLayer = true; break;
                    case BuildingGroundKind.Cliff: allowsCliff = true; break;
                    case BuildingGroundKind.MapResource: allowsResource = true; break;
                }
            }
        }

        return (!tile.requireGroundMatch || matched) &&
               (buildable || allowsLayer || allowsCliff) &&
               (layer == placementLayer || allowsLayer || allowsCliff) &&
               (!transition || allowsCliff) &&
               (source == null || !source.BlocksConstruction || allowsResource);
    }
}
