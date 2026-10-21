using UnityEngine;

// Hover and click selection must resolve the same logical footprint, including
// unpainted resource margins and gaps between a building's visible meshes.
public static class WorldPlacementPicking
{
    public static void Resolve(WorldManager world, RaycastHit hit,
        out Building building, out MapResourceSource source)
    {
        building = null;
        source = null;
        if (hit.collider == null) return;
        building = hit.collider.GetComponentInParent<Building>();
        if (building != null && !building.isActiveAndEnabled) building = null;
        if (building != null || world == null || world.IsGenerating) return;

        var marker = hit.collider.GetComponentInParent<MapResourceMarker>();
        if (marker != null) source = marker.Source;
        else if (hit.collider.GetComponent<ChunkRenderer>() != null && world.cellSize > 0f)
        {
            var cell = new Vector2Int(Mathf.FloorToInt(hit.point.x / world.cellSize),
                Mathf.FloorToInt(hit.point.z / world.cellSize));
            if (BuildingSystemBackend.Instance != null)
                BuildingSystemBackend.Instance.TryGetBuildingAt(cell, out building);
            if (building == null) world.ResourceMap.TryGetSource(cell, out source);
        }
    }
}
