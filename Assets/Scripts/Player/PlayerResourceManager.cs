using UnityEngine;
using System.Collections.Generic;

public enum ResourceType
{
    IronOre,
    CopperOre,
    Wood,
    Steel,
}

// A reusable struct to map a resource to a specific amount
[System.Serializable]
public struct ResourceAmount
{
    public ResourceType resourceType;
    public int amount;
}

public class PlayerResourceManager : MonoBehaviour
{
    public static PlayerResourceManager Instance { get; private set; }

    // Dictionary to hold our current stockpiles
    private Dictionary<ResourceType, int> resourceInventory = new Dictionary<ResourceType, int>();

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(this);
        else Instance = this;
    }

    public void AddResource(ResourceType type, int amount)
    {
        if (!resourceInventory.ContainsKey(type))
        {
            resourceInventory[type] = 0;
        }
        resourceInventory[type] += amount;
    }

    public bool ConsumeResource(ResourceType type, int amount)
    {
        if (resourceInventory.ContainsKey(type) && resourceInventory[type] >= amount)
        {
            resourceInventory[type] -= amount;
            return true;
        }
        // Not enough resources
        return false; 
    }

    public int GetResourceAmount(ResourceType type)
    {
        return resourceInventory.ContainsKey(type) ? resourceInventory[type] : 0;
    }
}