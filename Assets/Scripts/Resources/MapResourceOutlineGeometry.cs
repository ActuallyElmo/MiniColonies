using System.Collections.Generic;
using UnityEngine;

// Compatibility entry point; new overlays use the shared geometry directly.
public static class MapResourceOutlineGeometry
{
    public static void AppendCell(Vector2Int cell, int subdivisions, Sprite outlineEdge,
        List<Vector3> vertices, List<Vector2> uvs, List<int> indices,
        ISet<Vector2Int> footprint = null)
    {
        WorldPlacementOutlineGeometry.AppendCell(cell, subdivisions, outlineEdge, vertices, uvs, indices, footprint);
    }
}