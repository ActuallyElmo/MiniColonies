using System;
using System.Collections.Generic;
using UnityEngine;

public enum MapResourceShape { Nodes, Deposit }
public enum MapResourceDepth { Overground, Underground, Deep }
public enum ResourceNodeDistribution { Scattered, Clustered }
public enum ResourceDepositGrowth { ResourceDefault = 0, Compact = 1, Organic = 2, Vein = 3, Branching = 4 }
public enum MapResourceTerrainTransition { Polygonal = 0, Smooth = 1 }

[CreateAssetMenu(menuName = "Mini Colonies/Map Resource", fileName = "MapResource")]
public sealed class MapResourceDefinition : ScriptableObject
{
    [Header("Resource Rules (whole tiles)")]
    public ResourceType resourceType;
    public string displayName = "Resource";
    public MapResourceShape shape;
    public MapResourceDepth depth;
    public bool unlimited;
    [Header("Deposit Tile Growth (logical footprint)")]
    [Tooltip("How tiles spread within ONE deposit. Resource Default: water = Organic, oil = Vein, others = Compact. Ignored for nodes.")]
    public ResourceDepositGrowth depositGrowth;
    [Range(0f, 1f), Tooltip("How strongly Compact/Organic/Vein growth follows its shape. Lower values produce looser, more irregular footprints.")]
    public float depositCompactness = 1f;
    [Range(0f, 1f), Tooltip("Strength of broad lobes in Organic deposits. Zero uses compact growth.")]
    public float depositIrregularity = 0.65f;
    [Range(1f, 12f), Tooltip("Preferred length-to-width ratio for Vein deposits. Terrain and tile count can limit the final ratio.")]
    public float depositElongation = 4f;
    [Range(0f, 1f), Tooltip("Chance for Branching growth to start another arm from its frontier instead of continuing its current tip.")]
    public float depositBranchChance = 0.2f;
    public bool randomizeDepositDirection = true;
    [Range(-180f, 180f), Tooltip("Vein direction from the X axis when Randomize Deposit Direction is disabled.")]
    public float depositGrowthAngle;
    [Header("Terrain Appearance (visual only)")]
    [Tooltip("Optional surface material at any depth. Painted coverage never changes tile ownership, supply or construction rules.")]
    public Material terrainMaterial;
    [Tooltip("Resource surface color, shaded with the map's height color bands.")]
    public Color terrainTint = Color.white;
    [Tooltip("Polygonal uses a solid patch with broad straight edges; Smooth uses a continuous color fade.")]
    public MapResourceTerrainTransition terrainTransitionStyle = MapResourceTerrainTransition.Polygonal;
    [Range(0f, 0.5f), Tooltip("Polygon edge inset and corner cut size, or the color transition width in Smooth mode, in tiles.")]
    public float terrainBlendWidth = 0.35f;
    [Range(0f, 1f), Tooltip("Amount of ordinary terrain near resource borders: 0 = continuous resource color, 1 = full transition.")]
    public float terrainBlendAmount = 1f;
    [Range(0f, 1f), Tooltip("Seeded variation in polygon coverage, edge positions and corner lengths. Zero keeps a uniform border.")]
    public float terrainBlendRandomness = 0.65f;
    [Min(0.1f), Tooltip("Scale of coherent border variation in tiles. Polygonal mode also varies individual boundary vertices.")]
    public float terrainBlendNoiseSize = 1f;
    [Range(0f, 0.5f), Tooltip("Outer corner shaping: straight bevels in Polygonal mode, rounded corners in Smooth mode. Occupancy stays tile-based.")]
    public float terrainEdgeRoundness = 0.2f;
    [HideInInspector] public int terrainAppearanceVersion;
    [Header("Decorative Prefabs (visual only)")]
    public List<GameObject> prefabVariants = new List<GameObject>();
    [Range(0f, 1f), Tooltip("Chance of a decorative prefab on each deposit tile. Nodes always try to spawn one.")]
    public float depositPrefabChance = 0.1f;
    public Vector2 prefabScaleRange = new Vector2(0.8f, 1.2f);
    public bool randomYaw = true;
    public float prefabHeightOffset;

    public bool BlocksConstruction => shape == MapResourceShape.Nodes || depth == MapResourceDepth.Overground;

    private void OnValidate()
    {
        depositCompactness = MapResourceSpawnSettings.IsFinite(depositCompactness) ? Mathf.Clamp01(depositCompactness) : 1f;
        depositIrregularity = MapResourceSpawnSettings.IsFinite(depositIrregularity) ? Mathf.Clamp01(depositIrregularity) : 0.65f;
        depositElongation = MapResourceSpawnSettings.IsFinite(depositElongation) ? Mathf.Clamp(depositElongation, 1f, 12f) : 4f;
        depositBranchChance = MapResourceSpawnSettings.IsFinite(depositBranchChance) ? Mathf.Clamp01(depositBranchChance) : 0.2f;
        depositGrowthAngle = MapResourceSpawnSettings.IsFinite(depositGrowthAngle) ? Mathf.Clamp(depositGrowthAngle, -180f, 180f) : 0f;
        terrainBlendWidth = MapResourceSpawnSettings.IsFinite(terrainBlendWidth) ? Mathf.Clamp(terrainBlendWidth, 0f, 0.5f) : 0.35f;
        terrainBlendAmount = MapResourceSpawnSettings.IsFinite(terrainBlendAmount) ? Mathf.Clamp01(terrainBlendAmount) : 1f;
        terrainBlendRandomness = MapResourceSpawnSettings.IsFinite(terrainBlendRandomness) ? Mathf.Clamp01(terrainBlendRandomness) : 0f;
        terrainBlendNoiseSize = MapResourceSpawnSettings.IsFinite(terrainBlendNoiseSize) ? Mathf.Max(0.1f, terrainBlendNoiseSize) : 1f;
        terrainEdgeRoundness = MapResourceSpawnSettings.IsFinite(terrainEdgeRoundness) ? Mathf.Clamp(terrainEdgeRoundness, 0f, 0.5f) : 0.2f;
        depositPrefabChance = Mathf.Clamp01(depositPrefabChance);
        prefabScaleRange.x = Mathf.Max(0.01f, prefabScaleRange.x);
        prefabScaleRange.y = Mathf.Max(prefabScaleRange.x, prefabScaleRange.y);
    }
}

[Serializable]
public sealed class MapResourceSpawnSettings
{
    public bool enabled = true;
    public MapResourceDefinition resource;
    [Tooltip("Minimum and maximum source count. Each node is a source; each entire deposit is one source. Geography can prevent the minimum.")]
    public Vector2Int sourceCount = new Vector2Int(2, 8);
    [Min(0f), Tooltip("Desired sources per 1,000 eligible tiles, clamped to Source Count. Zero disables this resource.")]
    public float sourcesPerThousandTiles = 2f;
    [Tooltip("Total units across ALL generated sources of this entry, sampled inclusively. Ignored for unlimited resources.")]
    public Vector2Int totalUnits = new Vector2Int(10000, 20000);
    [Tooltip("Occupied tiles per deposit; at least two. A deposit that cannot reach the minimum is discarded.")]
    public Vector2Int depositTiles = new Vector2Int(8, 30);
    [Header("Node Spread")]
    public ResourceNodeDistribution nodeDistribution;
    public Vector2Int nodesPerCluster = new Vector2Int(3, 8);
    [Header("Deposit Terrain Rules")]
    public bool requireFlatTerrain = true;
    [Header("Strict Location Rules (all generated tiles)")]
    public bool restrictHeight;
    [Tooltip("Inclusive physical terrain heights in world units.")]
    public Vector2 heightRange = new Vector2(0f, 10f);
    public bool restrictDistanceFromCenter;
    [Tooltip("Inclusive normalized distance: 0 = center, 1 = playable edge. Square maps use square rings.")]
    public Vector2 centerDistanceRange = new Vector2(0f, 1f);
    public bool restrictMapPosition;
    [Tooltip("Allowed normalized X/Z rectangle: (0,0) is the map origin; (1,1) is the opposite corner.")]
    public Rect mapRegion = new Rect(0f, 0f, 1f, 1f);

    public bool Allows(Vector2Int cell, int worldSize, bool circular, ResourceTerrainCell terrain)
    {
        if (!enabled || resource == null || !terrain.Playable || terrain.Occupied ||
            !IsFinite(terrain.Height) || (requireFlatTerrain && terrain.Transition) || worldSize <= 0) return false;
        // Node safety cannot be disabled by the optional flat-terrain preference.
        if (resource.shape == MapResourceShape.Nodes && (!terrain.Buildable || terrain.Transition)) return false;
        if (restrictHeight && (!IsFinite(heightRange.x) || !IsFinite(heightRange.y) ||
            terrain.Height < heightRange.x || terrain.Height > heightRange.y)) return false;
        float nx = (cell.x + 0.5f) / worldSize;
        float nz = (cell.y + 0.5f) / worldSize;
        if (restrictMapPosition && (!IsFinite(mapRegion.x) || !IsFinite(mapRegion.y) ||
            !IsFinite(mapRegion.width) || !IsFinite(mapRegion.height) ||
            !(nx >= mapRegion.xMin && nx <= mapRegion.xMax && nz >= mapRegion.yMin && nz <= mapRegion.yMax))) return false;
        if (restrictDistanceFromCenter)
        {
            float radius = Mathf.Max(0.5f, worldSize * 0.5f - 1f);
            float dx = Mathf.Abs(cell.x + 0.5f - worldSize * 0.5f);
            float dz = Mathf.Abs(cell.y + 0.5f - worldSize * 0.5f);
            float distance = (circular ? Mathf.Sqrt(dx * dx + dz * dz) : Mathf.Max(dx, dz)) / radius;
            if (!IsFinite(centerDistanceRange.x) || !IsFinite(centerDistanceRange.y) ||
                !(distance >= centerDistanceRange.x && distance <= centerDistanceRange.y)) return false;
        }
        return true;
    }

    internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

public readonly struct ResourceTerrainCell
{
    public readonly bool Playable, Transition, Occupied, Buildable;
    public readonly float Height;
    public ResourceTerrainCell(bool playable, float height, bool transition = false, bool occupied = false, bool buildable = true)
    {
        Playable = playable;
        Height = height;
        Transition = transition;
        Occupied = occupied;
        Buildable = buildable;
    }
}
