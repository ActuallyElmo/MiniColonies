using System;
using UnityEngine;

public enum TerrainSurfaceType { Legacy = 0, FlatGround = 1, WaterLevel = 2 }
public enum WorldTileKind { FlatGround = 0, Edge = 1, WaterLevel = 2, ResourceNode = 3, ResourceDeposit = 4 }

// Value identity, not an asset: new resources use their existing persistent ID.
[Serializable]
public struct WorldTileType : IEquatable<WorldTileType>
{
    public WorldTileKind kind;
    public ResourceType resource;

    public bool IsResource => kind == WorldTileKind.ResourceNode || kind == WorldTileKind.ResourceDeposit;
    public bool Equals(WorldTileType other) => kind == other.kind && (!IsResource || resource == other.resource);
    public override bool Equals(object obj) => obj is WorldTileType other && Equals(other);
    public override int GetHashCode() => ((int)kind * 397) ^ (IsResource ? (int)resource : 0);
    private string ResourceName => ResourceCatalog.Current != null && ResourceCatalog.Current.Find(resource) == null
        ? "Missing Resource #" + (int)resource : ResourceCatalog.GetDisplayName(resource);
    public override string ToString()
    {
        switch (kind)
        {
            case WorldTileKind.FlatGround: return "Flat Ground";
            case WorldTileKind.Edge: return "Edge";
            case WorldTileKind.WaterLevel: return "Water Level";
            case WorldTileKind.ResourceNode: return ResourceName + " Node";
            case WorldTileKind.ResourceDeposit: return ResourceName + " Deposit";
            default: return "Unknown tile type";
        }
    }
}

// Derived on demand from WorldManager terrain and ResourceMap; never a second grid.
public readonly struct WorldTileInfo
{
    public readonly bool Playable;
    public readonly int Layer;
    public readonly TerrainSurfaceType Surface;
    public readonly bool IsEdge;
    public readonly MapResourceSource Source;

    public WorldTileInfo(bool playable, int layer, TerrainSurfaceType surface, bool isEdge, MapResourceSource source)
    {
        Playable = playable;
        Layer = layer;
        Surface = surface;
        IsEdge = isEdge;
        Source = source;
    }

    public bool Matches(WorldTileType type)
    {
        if (!Playable) return false;
        switch (type.kind)
        {
            case WorldTileKind.FlatGround: return Surface == TerrainSurfaceType.FlatGround && !IsEdge;
            case WorldTileKind.WaterLevel: return Surface == TerrainSurfaceType.WaterLevel && !IsEdge;
            case WorldTileKind.Edge: return IsEdge;
            case WorldTileKind.ResourceNode:
            case WorldTileKind.ResourceDeposit:
                return (int)type.resource >= 0 && Source != null && Source.Definition != null && Source.Definition.resourceType == type.resource &&
                    Source.Definition.shape == (type.kind == WorldTileKind.ResourceNode ? MapResourceShape.Nodes : MapResourceShape.Deposit);
            default: return false;
        }
    }
}
