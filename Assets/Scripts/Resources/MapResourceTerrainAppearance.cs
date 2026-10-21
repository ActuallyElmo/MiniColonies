using UnityEngine;

public static class MapResourceTerrainAppearance
{
    public const int MaximumPolygonCuts = 8;
    private const int BottomLeftBoundary = (1 << 1) | (1 << 3);
    private const int BottomRightBoundary = (1 << 7) | (1 << 3);
    private const int TopLeftBoundary = (1 << 1) | (1 << 5);
    private const int TopRightBoundary = (1 << 7) | (1 << 5);

    // Appearance only: adjacent sources with the same definition may share paint.
    // This never merges their logical source IDs or replaces source.Cells membership.
    // Cache the eight neighboring boundary tests once per tile, not per mesh vertex.
    public static int BoundaryMask(MapResourceMap map, Vector2Int cell, MapResourceDefinition definition)
    {
        int mask = 0;
        for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
                if (!map.TryGetSource(cell + new Vector2Int(x, z), out var source) || source.Definition != definition)
                    mask |= 1 << ((x + 1) * 3 + z + 1);
        return mask;
    }

    public static float BlendWeight(float localX, float localZ, int boundaryMask, float width,
        float amount = 1f, float roundness = 0f, float variation = 0f)
    {
        width = MapResourceSpawnSettings.IsFinite(width) ? Mathf.Clamp(width, 0f, 0.5f) : 0.35f;
        amount = MapResourceSpawnSettings.IsFinite(amount) ? Mathf.Clamp01(amount) : 1f;
        roundness = MapResourceSpawnSettings.IsFinite(roundness) ? Mathf.Clamp(roundness, 0f, 0.5f) : 0.2f;
        float distanceSquared = width * width;
        for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                if ((boundaryMask & (1 << ((x + 1) * 3 + z + 1))) == 0) continue;
                float dx = Mathf.Max(Mathf.Max(x - localX, 0f), localX - (x + 1));
                float dz = Mathf.Max(Mathf.Max(z - localZ, 0f), localZ - (z + 1));
                distanceSquared = Mathf.Min(distanceSquared, dx * dx + dz * dz);
            }
        float distance = Mathf.Sqrt(distanceSquared);
        // Round only convex outer corners, entirely inside the occupied footprint.
        // Missing diagonal neighbors continue to supply the existing concave-corner fade.
        if (roundness > 0f)
        {
            if (localX < roundness && localZ < roundness && (boundaryMask & BottomLeftBoundary) == BottomLeftBoundary)
                distance = Mathf.Min(distance, CornerDistance(localX, localZ, roundness));
            if (1f - localX < roundness && localZ < roundness && (boundaryMask & BottomRightBoundary) == BottomRightBoundary)
                distance = Mathf.Min(distance, CornerDistance(1f - localX, localZ, roundness));
            if (localX < roundness && 1f - localZ < roundness && (boundaryMask & TopLeftBoundary) == TopLeftBoundary)
                distance = Mathf.Min(distance, CornerDistance(localX, 1f - localZ, roundness));
            if (1f - localX < roundness && 1f - localZ < roundness && (boundaryMask & TopRightBoundary) == TopRightBoundary)
                distance = Mathf.Min(distance, CornerDistance(1f - localX, 1f - localZ, roundness));
        }
        if (distance < 0f) return 0f;
        float fade = width > 0f ? Mathf.SmoothStep(0f, 1f, distance / width) : 1f;
        variation = MapResourceSpawnSettings.IsFinite(variation) ? Mathf.Clamp(variation, -1f, 1f) : 0f;
        // Irregularity affects only the transition band. Border and interior colors stay anchored.
        fade = Mathf.Clamp01(fade + variation * (4f * fade * (1f - fade)));
        return Mathf.Lerp(1f, fade, amount);
    }

    public static float BlendVariation(float gridX, float gridZ, int seed, float strength, float size)
    {
        strength = MapResourceSpawnSettings.IsFinite(strength) ? Mathf.Clamp01(strength) : 0f;
        if (strength <= 0f || !MapResourceSpawnSettings.IsFinite(gridX) || !MapResourceSpawnSettings.IsFinite(gridZ)) return 0f;
        size = MapResourceSpawnSettings.IsFinite(size) ? Mathf.Max(0.1f, size) : 1f;
        uint bits = unchecked((uint)seed);
        float offsetX = (bits & 0xffff) * 0.03125f;
        float offsetZ = (bits >> 16) * 0.03125f;
        float x = gridX / size, z = gridZ / size;
        // World-grid sampling keeps adjacent tiles/chunks identical and never consumes gameplay RNG.
        float broad = Mathf.PerlinNoise(x + offsetX, z + offsetZ);
        float detail = Mathf.PerlinNoise(x * 2.13f + offsetX + 37.17f, z * 2.13f + offsetZ + 91.53f);
        return Mathf.Clamp((broad * 0.7f + detail * 0.3f) * 2f - 1f, -1f, 1f) * strength;
    }

    // Half planes in tile coordinates: plane.x * x + plane.y * z + plane.z >= 0.
    // Exposed sides move inward by unequal amounts, with one broad cut at convex corners.
    // Shared world-space endpoints keep the contour continuous between cells and chunks.
    public static int BuildPolygonCuts(Vector2Int cell, int boundaryMask, int seed,
        MapResourceDefinition definition, Vector3[] cuts)
    {
        float width = MapResourceSpawnSettings.IsFinite(definition.terrainBlendWidth)
            ? Mathf.Clamp(definition.terrainBlendWidth, 0f, 0.5f) : 0.35f;
        float amount = MapResourceSpawnSettings.IsFinite(definition.terrainBlendAmount)
            ? Mathf.Clamp01(definition.terrainBlendAmount) : 1f;
        float roundness = MapResourceSpawnSettings.IsFinite(definition.terrainEdgeRoundness)
            ? Mathf.Clamp(definition.terrainEdgeRoundness, 0f, 0.5f) : 0.2f;
        float inset = Mathf.Min(0.95f, Mathf.Max(width * amount * 2.5f, roundness * 1.6f));
        if (inset <= 0f) return 0;
        int cardinal = (1 << 1) | (1 << 3) | (1 << 5) | (1 << 7);
        // Four large corner cuts would erase an isolated node. Keep a generous solid core.
        float maximumRadius = (boundaryMask & cardinal) == cardinal ? 0.48f : 0.95f;
        inset = Mathf.Min(inset, maximumRadius);
        float maximumEdgeInset = (boundaryMask & cardinal) == cardinal ? 0.34f : 0.45f;
        float edgeInset = width * amount * 0.85f;
        int count = 0;
        if (edgeInset > 0f)
        {
            float bl = CornerInset(0, 0), br = CornerInset(1, 0);
            float tl = CornerInset(0, 1), tr = CornerInset(1, 1);
            if ((boundaryMask & (1 << 1)) != 0 && bl + tl > 0f)
                cuts[count++] = new Vector3(1f, bl - tl, -bl);
            if ((boundaryMask & (1 << 7)) != 0 && br + tr > 0f)
                cuts[count++] = new Vector3(-1f, br - tr, 1f - br);
            if ((boundaryMask & (1 << 3)) != 0 && bl + br > 0f)
                cuts[count++] = new Vector3(bl - br, 1f, -bl);
            if ((boundaryMask & (1 << 5)) != 0 && tl + tr > 0f)
                cuts[count++] = new Vector3(tl - tr, -1f, 1f - tl);
        }
        AddCorner(0, 0, BottomLeftBoundary);
        AddCorner(1, 0, BottomRightBoundary);
        AddCorner(0, 1, TopLeftBoundary);
        AddCorner(1, 1, TopRightBoundary);
        return count;

        float CornerInset(int x, int z)
        {
            int dx = x == 0 ? -1 : 1, dz = z == 0 ? -1 : 1;
            int occupied = 1;
            if ((boundaryMask & (1 << ((dx + 1) * 3 + 1))) == 0) occupied++;
            if ((boundaryMask & (1 << (3 + dz + 1))) == 0) occupied++;
            if ((boundaryMask & (1 << ((dx + 1) * 3 + dz + 1))) == 0) occupied++;
            // A concave junction must reach the original vertex, or its fully covered
            // neighbor would show a color seam along their shared edge.
            if (occupied >= 3) return 0f;
            float variation = PolygonVariation(cell.x + x, cell.y + z, seed ^ 0x3c6ef372,
                definition.terrainBlendRandomness, definition.terrainBlendNoiseSize);
            return Mathf.Clamp(edgeInset * (1f + variation * 1.2f), 0f, maximumEdgeInset);
        }

        void AddCorner(int x, int z, int mask)
        {
            if ((boundaryMask & mask) != mask) return;
            float rx = Radius(seed), rz = Radius(seed ^ 0x6a09e667);
            // Endpoints stay on exposed edges. No cut reaches a shared edge or the tile center.
            float nx = (x == 0 ? 1f : -1f) / rx;
            float nz = (z == 0 ? 1f : -1f) / rz;
            cuts[count++] = new Vector3(nx, nz, -1f - nx * x - nz * z);

            float Radius(int axisSeed)
            {
                float noise = PolygonVariation(cell.x + x, cell.y + z, axisSeed,
                    definition.terrainBlendRandomness, definition.terrainBlendNoiseSize);
                return Mathf.Clamp(inset * (1f + noise * 0.8f), 0.01f, maximumRadius);
            }
        }
    }

    private static float PolygonVariation(int x, int z, int seed, float strength, float size)
    {
        strength = MapResourceSpawnSettings.IsFinite(strength) ? Mathf.Clamp01(strength) : 0f;
        if (strength <= 0f) return 0f;
        uint hash;
        unchecked
        {
            hash = (uint)seed ^ ((uint)x * 0x9e3779b9u) ^ ((uint)z * 0x85ebca6bu);
            hash ^= hash >> 16; hash *= 0x7feb352du;
            hash ^= hash >> 15; hash *= 0x846ca68bu;
            hash ^= hash >> 16;
        }
        // Corner jitter supplies visible variation even at integer grid samples;
        // coherent noise relates nearby corners without adding fine edge fragments.
        float jitter = ((hash & 0xffffff) / 16777215f * 2f - 1f) * strength;
        return jitter * 0.65f + BlendVariation(x, z, seed, strength, size) * 0.35f;
    }

    private static float CornerDistance(float x, float z, float radius)
    {
        float dx = x - radius, dz = z - radius;
        return radius - Mathf.Sqrt(dx * dx + dz * dz);
    }

    public static Color ShadeTint(Color terrainBand, Color tint, float paletteBrightness)
    {
        // Preserve the exact relative brightness of the map's authored height bands.
        float shade = paletteBrightness > 0f ? terrainBand.maxColorComponent / paletteBrightness : 0f;
        return new Color(tint.r * shade, tint.g * shade, tint.b * shade, terrainBand.a);
    }
}
