using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Pure generation rules. Yield points bound work; the caller supplies the frame budget.
public static class MapResourceGenerator
{
    private static readonly Vector2Int[] Directions =
        { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

    public static int StableSeed(string seed)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in seed ?? string.Empty) hash = (hash ^ c) * 16777619;
            return (int)hash;
        }
    }

    public static IEnumerator Generate(int size, bool circular, string seed,
        IReadOnlyList<MapResourceSpawnSettings> settings, Func<Vector2Int, ResourceTerrainCell> sample,
        MapResourceMap output)
    {
        if (size <= 0 || settings == null || sample == null || output == null) yield break;
        var random = new System.Random(StableSeed(seed + "::map-resources"));
        var terrainCache = new Dictionary<Vector2Int, ResourceTerrainCell>();
        ResourceTerrainCell Terrain(Vector2Int cell)
        {
            if (!terrainCache.TryGetValue(cell, out var value))
            {
                value = sample(cell);
                terrainCache.Add(cell, value);
            }
            return value;
        }
        bool HasNodeClearance(Vector2Int cell)
        {
            var center = Terrain(cell);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var neighbor = cell + new Vector2Int(dx, dz);
                    if (neighbor.x < 0 || neighbor.y < 0 || neighbor.x >= size || neighbor.y >= size) return false;
                    var adjacent = Terrain(neighbor);
                    if (!adjacent.Playable || !adjacent.Buildable || adjacent.Transition ||
                        !MapResourceSpawnSettings.IsFinite(adjacent.Height) || Mathf.Abs(adjacent.Height - center.Height) > 0.001f) return false;
                }
            return true;
        }
        int work = 0;
        foreach (var rule in settings)
        {
            if (rule == null || !rule.enabled || rule.resource == null ||
                !MapResourceSpawnSettings.IsFinite(rule.sourcesPerThousandTiles) || rule.sourcesPerThousandTiles <= 0f) continue;
            int minSources = Mathf.Max(0, rule.sourceCount.x);
            int maxSources = Mathf.Max(minSources, rule.sourceCount.y);
            if (maxSources == 0) continue;
            var candidates = new List<Vector2Int>();
            var eligible = new HashSet<Vector2Int>();
            for (int x = 0; x < size; x++)
                for (int z = 0; z < size; z++)
                {
                    var cell = new Vector2Int(x, z);
                    if (!output.TryGetSource(cell, out _) && rule.Allows(cell, size, circular, Terrain(cell)) &&
                        (rule.resource.shape != MapResourceShape.Nodes || HasNodeClearance(cell)))
                    {
                        candidates.Add(cell);
                        eligible.Add(cell);
                    }
                    if (++work % 64 == 0) yield return null;
                }
            // A stochastic rounding keeps low densities meaningful on small maps.
            double desired = candidates.Count * (double)rule.sourcesPerThousandTiles / 1000d;
            int target = (int)Math.Min(maxSources, Math.Max(minSources, Math.Floor(desired + random.NextDouble())));
            target = Math.Min(target, candidates.Count);
            long units = rule.resource.unlimited ? 0 : Inclusive(random, Mathf.Max(0, rule.totalUnits.x),
                Mathf.Max(Mathf.Max(0, rule.totalUnits.x), rule.totalUnits.y));
            if (!rule.resource.unlimited) target = (int)Math.Min(target, units);
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var temp = candidates[i]; candidates[i] = candidates[j]; candidates[j] = temp;
                if (++work % 64 == 0) yield return null;
            }

            var generated = new List<MapResourceSource>();
            var frontier = new List<Vector2Int>();
            var seen = new HashSet<Vector2Int>();
            var patch = new List<Vector2Int>();
            var grown = new HashSet<Vector2Int>();
            bool deposit = rule.resource.shape == MapResourceShape.Deposit;
            int minTiles = deposit ? Mathf.Max(2, rule.depositTiles.x) : 1;
            int maxTiles = deposit ? Mathf.Max(minTiles, rule.depositTiles.y) :
                rule.nodeDistribution == ResourceNodeDistribution.Clustered ? Mathf.Max(1, rule.nodesPerCluster.y) : 1;
            int minCluster = Mathf.Clamp(rule.nodesPerCluster.x, 1, maxTiles);
            foreach (var origin in candidates)
            {
                if (generated.Count >= target) break;
                if (!eligible.Contains(origin))
                {
                    if (++work % 64 == 0) yield return null;
                    continue;
                }
                int requested = (int)Inclusive(random, deposit ? minTiles : minCluster, maxTiles);
                if (!deposit) requested = Math.Min(requested, target - generated.Count);
                requested = Math.Min(requested, eligible.Count);
                frontier.Clear(); seen.Clear(); patch.Clear(); grown.Clear();
                frontier.Add(origin); seen.Add(origin);
                var growth = deposit ? new MapResourceDepositGrowth(rule.resource, origin, requested, seed) : null;
                int newTipCount = 0;
                while (frontier.Count > 0 && patch.Count < requested)
                {
                    int index = deposit ? growth.SelectIndex(frontier, grown, newTipCount, random) : random.Next(frontier.Count);
                    var cell = frontier[index];
                    frontier[index] = frontier[frontier.Count - 1];
                    frontier.RemoveAt(frontier.Count - 1);
                    patch.Add(cell);
                    if (deposit) grown.Add(cell);
                    newTipCount = 0;
                    foreach (var direction in Directions)
                    {
                        var neighbor = cell + direction;
                        if (eligible.Contains(neighbor) && seen.Add(neighbor))
                        {
                            frontier.Add(neighbor);
                            newTipCount++;
                        }
                    }
                    if (++work % 64 == 0) yield return null;
                }
                if (patch.Count >= minTiles)
                {
                    if (deposit) generated.Add(output.Add(rule.resource, patch));
                    foreach (var cell in patch)
                    {
                        eligible.Remove(cell);
                        if (!deposit) generated.Add(output.Add(rule.resource, new[] { cell }));
                        if (++work % 64 == 0) yield return null;
                    }
                }
                else
                {
                    // An exhausted connected component cannot fit a deposit. Do not flood it again for every origin.
                    foreach (var cell in patch)
                    {
                        eligible.Remove(cell);
                        if (++work % 64 == 0) yield return null;
                    }
                }
                if (++work % 64 == 0) yield return null;
            }
            // Allocate the sampled map-wide budget across actual sources, retaining an exact total.
            long remaining = units;
            for (int i = 0; i < generated.Count; i++)
            {
                if (rule.resource.unlimited) break;
                int left = generated.Count - i;
                long amount = left == 1 ? remaining : Math.Max(1, (long)(remaining / (double)left * (0.5 + random.NextDouble())));
                amount = Math.Min(amount, remaining - left + 1);
                generated[i].GeneratedUnits = amount;
                remaining -= amount;
                if (++work % 64 == 0) yield return null;
            }
            if (generated.Count < minSources || (generated.Count == 0 && units > 0))
                output.Warn($"{rule.resource.displayName}: generated {generated.Count} sources (minimum {minSources}). Check allowed terrain, source/area limits, total units and earlier resource entries. No placement restrictions were relaxed.");
        }
    }

    private static long Inclusive(System.Random random, int min, int max) =>
        min + (long)Math.Floor(random.NextDouble() * ((long)max - min + 1));
}
