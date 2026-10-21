using System;
using System.Collections.Generic;
using UnityEngine;

// Selects the next eligible tile in a single source's connected frontier.
// It changes logical footprint shape, never source origins or decorative paint.
public sealed class MapResourceDepositGrowth
{
    private readonly ResourceDepositGrowth pattern;
    private readonly Vector2Int origin;
    private readonly int samples, shapeSeed;
    private readonly double directionX, directionZ, elongation, irregularity, noiseSize, branchChance;

    public static ResourceDepositGrowth ResolvePattern(MapResourceDefinition definition)
    {
        if (definition == null) return ResourceDepositGrowth.Compact;
        return ResolvePattern(definition.depositGrowth, definition.resourceType);
    }

    public static ResourceDepositGrowth ResolvePattern(ResourceDepositGrowth growth, ResourceType resourceType)
    {
        switch (growth)
        {
            case ResourceDepositGrowth.Compact:
            case ResourceDepositGrowth.Organic:
            case ResourceDepositGrowth.Vein:
            case ResourceDepositGrowth.Branching: return growth;
            case ResourceDepositGrowth.ResourceDefault:
                switch (resourceType)
                {
                    case ResourceType.Water: return ResourceDepositGrowth.Organic;
                    case ResourceType.Oil: return ResourceDepositGrowth.Vein;
                    default: return ResourceDepositGrowth.Compact;
                }
            default: return ResourceDepositGrowth.Compact;
        }
    }

    public MapResourceDepositGrowth(MapResourceDefinition definition, Vector2Int origin, int tiles, string seed)
    {
        this.origin = origin;
        pattern = ResolvePattern(definition);
        samples = 1 + Mathf.RoundToInt(Safe(definition.depositCompactness, 0f, 1f, 1f) * 7f);
        elongation = Safe(definition.depositElongation, 1f, 12f, 4f);
        irregularity = Safe(definition.depositIrregularity, 0f, 1f, 0.65f);
        branchChance = Safe(definition.depositBranchChance, 0f, 1f, 0.2f);
        noiseSize = Math.Max(2.0, Math.Sqrt(Math.Max(1, tiles)) * 0.65);
        shapeSeed = unchecked(MapResourceGenerator.StableSeed(seed + "::deposit-growth") ^
            ((int)definition.resourceType * 397) ^ (origin.x * 73856093) ^ (origin.y * 19349663));
        double angle = Safe(definition.depositGrowthAngle, -180f, 180f, 0f);
        if (definition.randomizeDepositDirection) angle += Noise(0, 0) * 360.0;
        double radians = angle * Math.PI / 180.0;
        directionX = Math.Cos(radians); directionZ = Math.Sin(radians);
    }

    public int SelectIndex(List<Vector2Int> frontier, ISet<Vector2Int> grown, int newTipCount, System.Random random)
    {
        int index = random.Next(frontier.Count);
        if (pattern == ResourceDepositGrowth.Branching)
        {
            bool extendTip = newTipCount > 0 && random.NextDouble() >= branchChance;
            int start = frontier.Count - newTipCount;
            if (extendTip) index = start + random.Next(newTipCount);
            int neighbors = NeighborCount(frontier[index], grown);
            // New tip candidates are at most four; starting another arm samples at most eight.
            int choices = extendTip ? newTipCount : 8;
            for (int i = 0; i < choices; i++)
            {
                int candidate = extendTip ? start + i : random.Next(frontier.Count);
                int adjacent = NeighborCount(frontier[candidate], grown);
                if (adjacent >= neighbors) continue;
                index = candidate; neighbors = adjacent;
            }
            return index;
        }
        double best = Score(frontier[index]);
        for (int choice = 1; choice < samples && choice < frontier.Count; choice++)
        {
            int candidate = random.Next(frontier.Count);
            double score = Score(frontier[candidate]);
            if (score >= best) continue;
            index = candidate; best = score;
        }
        return index;
    }

    private double Score(Vector2Int cell)
    {
        double dx = (double)cell.x - origin.x, dz = (double)cell.y - origin.y;
        if (pattern == ResourceDepositGrowth.Vein)
        {
            double along = (dx * directionX + dz * directionZ) / elongation;
            double across = -dx * directionZ + dz * directionX;
            return along * along + across * across;
        }
        double distance = dx * dx + dz * dz;
        if (pattern != ResourceDepositGrowth.Organic || irregularity <= 0.0) return distance;
        double x = dx / noiseSize, z = dz / noiseSize;
        int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
        double u = x - ix, v = z - iz;
        u *= u * (3.0 - 2.0 * u); v *= v * (3.0 - 2.0 * v);
        double bottom = Noise(ix, iz) * (1.0 - u) + Noise(ix + 1, iz) * u;
        double top = Noise(ix, iz + 1) * (1.0 - u) + Noise(ix + 1, iz + 1) * u;
        double noise = bottom * (1.0 - v) + top * v;
        return distance * (1.0 + irregularity * (noise * 2.0 - 1.0) * 0.85);
    }

    private double Noise(int x, int z)
    {
        unchecked
        {
            uint hash = (uint)shapeSeed ^ ((uint)x * 0x9e3779b9u) ^ ((uint)z * 0x85ebca6bu);
            hash ^= hash >> 16; hash *= 0x7feb352du;
            hash ^= hash >> 15; hash *= 0x846ca68bu;
            hash ^= hash >> 16;
            return (hash + 0.5) / 4294967296.0;
        }
    }

    private static int NeighborCount(Vector2Int cell, ISet<Vector2Int> grown) =>
        (grown.Contains(cell + Vector2Int.left) ? 1 : 0) + (grown.Contains(cell + Vector2Int.right) ? 1 : 0) +
        (grown.Contains(cell + Vector2Int.up) ? 1 : 0) + (grown.Contains(cell + Vector2Int.down) ? 1 : 0);

    private static float Safe(float value, float min, float max, float fallback) =>
        MapResourceSpawnSettings.IsFinite(value) ? Mathf.Clamp(value, min, max) : fallback;
}
