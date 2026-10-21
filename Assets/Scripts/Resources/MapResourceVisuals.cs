using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class MapResourceVisuals
{
    public static IEnumerator Spawn(WorldManager world)
    {
        var random = new System.Random(MapResourceGenerator.StableSeed(world.seed + "::resource-visuals"));
        int blendSeed = MapResourceGenerator.StableSeed(world.seed + "::resource-blend");
        var cuts = new Vector3[MapResourceTerrainAppearance.MaximumPolygonCuts];
        Transform root = null;
        var variants = new List<GameObject>();
        foreach (var source in world.ResourceMap.Sources)
        {
            var definition = source.Definition;
            variants.Clear();
            if (definition.prefabVariants != null)
                foreach (var prefab in definition.prefabVariants)
                    if (prefab != null) variants.Add(prefab);
            if (variants.Count == 0)
            {
                yield return null;
                continue;
            }
            foreach (var cell in source.Cells)
            {
                bool spawn = definition.shape == MapResourceShape.Nodes ||
                             random.NextDouble() < Mathf.Clamp01(definition.depositPrefabChance);
                if (spawn)
                {
                    if (root == null)
                    {
                        root = new GameObject("Resource Prefabs").transform;
                        root.SetParent(world.transform, false);
                    }
                    var local = new Vector2(0.25f + (float)random.NextDouble() * 0.5f,
                        0.25f + (float)random.NextDouble() * 0.5f);
                    if (definition.terrainMaterial != null && definition.terrainTransitionStyle == MapResourceTerrainTransition.Polygonal)
                    {
                        int mask = MapResourceTerrainAppearance.BoundaryMask(world.ResourceMap, cell, definition);
                        int resourceSeed = unchecked(blendSeed ^ ((int)definition.resourceType * 397));
                        int count = MapResourceTerrainAppearance.BuildPolygonCuts(cell, mask, resourceSeed, definition, cuts);
                        local = MapResourcePatchGeometry.ConstrainPoint(local, cuts, count);
                    }
                    float x = cell.x + local.x, z = cell.y + local.y;
                    float heightOffset = MapResourceSpawnSettings.IsFinite(definition.prefabHeightOffset)
                        ? definition.prefabHeightOffset : 0f;
                    Vector3 position = new Vector3(x * world.cellSize,
                        world.GetPhysicalHeight(x, z) * world.heightStep + heightOffset, z * world.cellSize);
                    Quaternion rotation = Quaternion.Euler(0f, definition.randomYaw ? (float)random.NextDouble() * 360f : 0f, 0f);
                    var instance = Object.Instantiate(variants[random.Next(variants.Count)], position, rotation, root);
                    float min = MapResourceSpawnSettings.IsFinite(definition.prefabScaleRange.x)
                        ? Mathf.Max(0.01f, definition.prefabScaleRange.x) : 1f;
                    float max = MapResourceSpawnSettings.IsFinite(definition.prefabScaleRange.y)
                        ? Mathf.Max(min, definition.prefabScaleRange.y) : min;
                    instance.transform.localScale *= Mathf.Lerp(min, max, (float)random.NextDouble());
                    var marker = instance.GetComponent<MapResourceMarker>();
                    if (marker == null) marker = instance.AddComponent<MapResourceMarker>();
                    marker.Source = source;
                }
                // Instantiation and tile traversal are frame-budgeted by the caller.
                yield return null;
            }
        }
    }
}
