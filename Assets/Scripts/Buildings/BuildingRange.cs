using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class BuildingRange
{
    public const int MaximumRadius = 64;
    public static readonly Color DefaultColor = new Color(0.2f, 0.85f, 1f, 1f);
    public string name = "Coverage";
    [Range(0, MaximumRadius), Tooltip("Tile distance from the nearest footprint cell, including diagonals. Zero covers only the footprint.")]
    public int radius = 1;
    [ColorUsage(false), Tooltip("Outline color, shared by valid and invalid placement. Invisible serialized colors are restored to a visible color at runtime.")]
    public Color color = DefaultColor;
    [Tooltip("Empty: coverage only. Otherwise at least one listed tile type must occur in this range for valid placement. Separate ranges impose separate requirements.")]
    public List<WorldTileType> requiredTileTypes = new List<WorldTileType>();
    public bool RequiresMatchingTile => requiredTileTypes != null && requiredTileTypes.Count > 0;
    public bool HasValidRadius => radius >= 0 && radius <= MaximumRadius;

    public static Color ResolveOutlineColor(Color color)
    {
        if (!IsFinite(color.r) || !IsFinite(color.g) || !IsFinite(color.b) || !IsFinite(color.a)) return DefaultColor;
        // New serialized list entries can contain zero RGBA rather than the
        // C# initializer. Existing entries must remain visible without an asset rewrite.
        if (color.a <= 0f)
        {
            if (color.r == 0f && color.g == 0f && color.b == 0f) return DefaultColor;
            color.a = 1f;
        }
        return color;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

public static class BuildingRangeGeometry
{
    public static bool Contains(IReadOnlyList<Vector2Int> footprint, Vector2Int cell, int radius)
    {
        if (footprint == null || radius < 0 || radius > BuildingRange.MaximumRadius) return false;
        for (int i = 0; i < footprint.Count; i++)
            if (Math.Abs((long)cell.x - footprint[i].x) <= radius &&
                Math.Abs((long)cell.y - footprint[i].y) <= radius) return true;
        return false;
    }
}
