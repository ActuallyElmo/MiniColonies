using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

// Immutable authoring snapshot plus privately accumulated query results.
public sealed class BuildingRangeResult
{
    public string Name { get; }
    public int Radius { get; }
    public Color Color { get; }
    public bool ValidConfiguration { get; }
    public bool RequirementMet { get; internal set; }
    public IReadOnlyList<Vector2Int> Cells { get; }
    public IReadOnlyList<Building> Buildings { get; }
    internal readonly WorldTileType[] required;
    private readonly Color authoredColor;
    internal readonly List<Vector2Int> cells = new List<Vector2Int>();
    internal readonly List<Building> buildings = new List<Building>();
    internal readonly HashSet<Building> uniqueBuildings = new HashSet<Building>();

    internal BuildingRangeResult(BuildingRange definition)
    {
        Name = definition != null ? definition.name : "Missing range";
        Radius = definition != null ? definition.radius : -1;
        authoredColor = definition != null ? definition.color : UnityEngine.Color.white;
        Color = BuildingRange.ResolveOutlineColor(authoredColor);
        ValidConfiguration = definition != null && Radius >= 0 && Radius <= BuildingRange.MaximumRadius;
        required = definition?.requiredTileTypes != null ? definition.requiredTileTypes.ToArray() : Array.Empty<WorldTileType>();
        RequirementMet = ValidConfiguration && required.Length == 0;
        Cells = cells.AsReadOnly();
        Buildings = buildings.AsReadOnly();
    }

    internal bool Matches(BuildingRange definition)
    {
        if (definition == null) return !ValidConfiguration && Radius == -1;
        if (definition.name != Name || definition.radius != Radius || !definition.color.Equals(authoredColor)) return false;
        int count = definition.requiredTileTypes != null ? definition.requiredTileTypes.Count : 0;
        if (count != required.Length) return false;
        for (int i = 0; i < count; i++) if (!required[i].Equals(definition.requiredTileTypes[i])) return false;
        return true;
    }

}

// Infrastructure work runs in real frame budgets, including while gameplay is paused.
// Advance is deterministic and can be driven directly by EditMode tests.
public sealed class BuildingRangeEvaluation : ISimulationTask, IDisposable
{
    private readonly BuildingSystemBackend backend;
    private readonly WorldManager world;
    private readonly BuildingData data;
    private readonly Building source;
    private readonly bool hasSource;
    private readonly Vector2Int[] footprint;
    private readonly MapResourceMap resourceMap;
    private readonly float[,] heights;
    private readonly int authoringRevision, worldRevision;
    private readonly int worldSize;
    private readonly bool circular;
    private readonly List<BuildingRangeResult> results = new List<BuildingRangeResult>();
    private int rangeIndex, x, z, minX, minZ, maxX, maxZ;
    private bool rangeStarted;
    public IReadOnlyList<BuildingRangeResult> Results { get; }
    public bool IsComplete { get; private set; }
    public bool IsCancelled { get; private set; }
    public bool AllRequirementsMet { get; private set; }
    public event Action<BuildingRangeEvaluation> Finished;

    internal BuildingRangeEvaluation(BuildingSystemBackend backend, WorldManager world, BuildingData data,
        Vector2Int[] footprint, Building source)
    {
        this.backend = backend; this.world = world; this.data = data; this.footprint = footprint; this.source = source;
        hasSource = source != null;
        resourceMap = world != null ? world.ResourceMap : null;
        heights = world != null ? world.visualHeights : null;
        authoringRevision = backend != null ? backend.AuthoringRevision : -1;
        worldRevision = world != null ? world.PlacementRevision : -1;
        worldSize = world != null ? world.worldSize : 0;
        circular = world != null && world.makeCircular;
        if (data?.ranges != null)
            foreach (BuildingRange range in data.ranges) results.Add(new BuildingRangeResult(range));
        Results = results.AsReadOnly();
    }

    public bool IsCurrent()
    {
        if (IsCancelled || backend == null || !backend.isActiveAndEnabled || world == null || world.IsGenerating || data == null ||
            worldSize <= 2 || world.worldSize != worldSize || world.makeCircular != circular ||
            backend.AuthoringRevision != authoringRevision || world.PlacementRevision != worldRevision ||
            !ReferenceEquals(world.ResourceMap, resourceMap) || !ReferenceEquals(world.visualHeights, heights)) return false;
        int count = data.ranges != null ? data.ranges.Count : 0;
        if (count != results.Count) return false;
        for (int i = 0; i < count; i++) if (!results[i].Matches(data.ranges[i])) return false;
        if (hasSource)
        {
            if (source == null || !source.isActiveAndEnabled || source.data != data || source.occupiedCells.Count != footprint.Length) return false;
            foreach (Vector2Int cell in footprint) if (!source.occupiedCells.Contains(cell)) return false;
        }
        return true;
    }

    public bool MatchesPlacement(WorldManager candidateWorld, BuildingData candidateData, Vector2Int origin,
        IReadOnlyList<BuildingTile> candidateFootprint)
    {
        if (candidateWorld != world || candidateData != data || candidateFootprint == null ||
            footprint.Length != candidateFootprint.Count || !IsCurrent()) return false;
        for (int i = 0; i < footprint.Length; i++)
            if (candidateFootprint[i] == null || origin + candidateFootprint[i].localPosition != footprint[i]) return false;
        return true;
    }

    public bool Process(Stopwatch timer, float maxMillisecondsPerFrame)
    {
        while (!IsComplete && timer.Elapsed.TotalMilliseconds < maxMillisecondsPerFrame) Advance(1);
        return IsComplete;
    }

    public bool Advance(int maximumCells)
    {
        if (IsComplete) return true;
        if (!IsCurrent() || footprint.Length == 0) { Cancel(); return true; }
        for (int step = 0; step < Mathf.Max(0, maximumCells) && !IsComplete; step++)
        {
            if (rangeIndex >= results.Count) { Complete(); break; }
            BuildingRangeResult result = results[rangeIndex];
            if (!rangeStarted)
            {
                if (!result.ValidConfiguration) { rangeIndex++; continue; }
                SetBounds(result.Radius);
                rangeStarted = true;
                if (minX > maxX || minZ > maxZ) { rangeIndex++; rangeStarted = false; continue; }
            }
            var cell = new Vector2Int(x, z);
            if (!world.IsExtrusionCell(x, z) && BuildingRangeGeometry.Contains(footprint, cell, result.Radius))
            {
                result.cells.Add(cell);
                if (!result.RequirementMet)
                {
                    WorldTileInfo tile = world.GetTileInfo(cell);
                    foreach (WorldTileType required in result.required)
                        if (tile.Matches(required)) { result.RequirementMet = true; break; }
                }
                if (backend.TryGetBuildingAt(cell, out Building building) && building != source && result.uniqueBuildings.Add(building))
                    result.buildings.Add(building);
            }
            if (++x > maxX)
            {
                x = minX;
                if (++z > maxZ) { rangeIndex++; rangeStarted = false; }
            }
        }
        if (!IsComplete && rangeIndex >= results.Count) Complete();
        return IsComplete;
    }

    private void SetBounds(int radius)
    {
        long left = long.MaxValue, bottom = long.MaxValue, right = long.MinValue, top = long.MinValue;
        foreach (Vector2Int cell in footprint)
        {
            left = Math.Min(left, (long)cell.x - radius); right = Math.Max(right, (long)cell.x + radius);
            bottom = Math.Min(bottom, (long)cell.y - radius); top = Math.Max(top, (long)cell.y + radius);
        }
        minX = (int)Math.Max(0L, Math.Min((long)world.worldSize, left));
        minZ = (int)Math.Max(0L, Math.Min((long)world.worldSize, bottom));
        maxX = (int)Math.Min((long)world.worldSize - 1, Math.Max(-1L, right));
        maxZ = (int)Math.Min((long)world.worldSize - 1, Math.Max(-1L, top));
        x = minX; z = minZ;
    }

    private void Complete()
    {
        AllRequirementsMet = true;
        foreach (BuildingRangeResult result in results) AllRequirementsMet &= result.ValidConfiguration && result.RequirementMet;
        IsComplete = true;
        NotifyFinished();
    }

    public void Cancel()
    {
        if (IsComplete) return;
        IsCancelled = true; IsComplete = true;
        NotifyFinished();
    }

    private void NotifyFinished()
    {
        Action<BuildingRangeEvaluation> callback = Finished;
        Finished = null;
        callback?.Invoke(this);
    }

    public void Dispose() => Cancel();
}
