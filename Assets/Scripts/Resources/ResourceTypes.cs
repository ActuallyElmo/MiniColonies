public enum ResourceType
{
    IronOre,
    CopperOre,
    Wood,
    Steel
}

[System.Serializable]
public struct ResourceAmount
{
    public ResourceType resourceType;
    public int amount;
}
