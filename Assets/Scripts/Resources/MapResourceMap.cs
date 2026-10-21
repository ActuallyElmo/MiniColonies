using System.Collections.Generic;
using UnityEngine;

// Authoritative runtime state. Cells are whole logical tiles, regardless of how
// much of each tile is painted by resource terrain or covered by a decorative prefab.
public sealed class MapResourceSource
{
    public int Id { get; }
    public MapResourceDefinition Definition { get; }
    public IReadOnlyList<Vector2Int> Cells { get; }
    // One quantity per source; neither tile count nor painted surface area scales it.
    private long generatedUnits;
    private long remainingUnits;
    public long GeneratedUnits
    {
        get => generatedUnits;
        internal set
        {
            generatedUnits = Unlimited ? 0 : System.Math.Max(0, value);
            RemainingUnits = generatedUnits;
        }
    }
    public long RemainingUnits
    {
        get => remainingUnits;
        internal set
        {
            long clamped = Unlimited ? 0 : System.Math.Max(0, System.Math.Min(value, generatedUnits));
            if (remainingUnits == clamped) return;
            remainingUnits = clamped;
            QuantityChanged?.Invoke();
        }
    }
    public event System.Action QuantityChanged;
    public bool Unlimited { get; }
    public bool BlocksConstruction { get; }

    internal MapResourceSource(int id, MapResourceDefinition definition, IList<Vector2Int> cells)
    {
        Id = id;
        Definition = definition;
        Cells = new List<Vector2Int>(cells).AsReadOnly();
        Unlimited = definition.unlimited;
        BlocksConstruction = definition.BlocksConstruction;
    }
}

public sealed class MapResourceMap
{
    private readonly Dictionary<Vector2Int, MapResourceSource> byCell = new Dictionary<Vector2Int, MapResourceSource>();
    private readonly List<MapResourceSource> sources = new List<MapResourceSource>();
    private readonly List<string> warnings = new List<string>();
    public IReadOnlyList<MapResourceSource> Sources { get; }
    public IReadOnlyList<string> Warnings { get; }

    public MapResourceMap()
    {
        Sources = sources.AsReadOnly();
        Warnings = warnings.AsReadOnly();
    }

    // A tile has either no source or exactly one source. There is no partial resource
    // occupancy: construction, picking and footprint outlines must use this index.
    public bool TryGetSource(Vector2Int cell, out MapResourceSource source) => byCell.TryGetValue(cell, out source);
    public bool BlocksConstruction(Vector2Int cell) => byCell.TryGetValue(cell, out var source) && source.BlocksConstruction;

    internal MapResourceSource Add(MapResourceDefinition definition, IList<Vector2Int> cells)
    {
        var source = new MapResourceSource(sources.Count + 1, definition, cells);
        sources.Add(source);
        foreach (Vector2Int cell in cells) byCell.Add(cell, source);
        return source;
    }

    internal void Warn(string warning) => warnings.Add(warning);
}
