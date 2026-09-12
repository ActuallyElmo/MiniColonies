using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// The editable requirements for one factory shift. The shift duration is
/// owned by the factory so every shift in a factory has the same length.
/// </summary>
[Serializable]
public sealed class FactoryShift
{
    [SerializeField]
    private bool isActive;

    [SerializeField, Range(0, GameTimeManager.HoursPerDay - 1)]
    private int startHour;

    [FormerlySerializedAs("preparationHour")]
    [SerializeField, Min(1)]
    private int preparationHours = 1;

    [SerializeField, Min(0)]
    private int desiredPops;

    [SerializeField]
    private List<ResourceAmount> desiredResources =
        new List<ResourceAmount>();

    public bool IsActive => isActive;
    public int StartHour => startHour;
    public int PreparationHours => preparationHours;
    public int DesiredPops => desiredPops;
    public IReadOnlyList<ResourceAmount> DesiredResources
    {
        get
        {
            EnsureResourceList();
            return desiredResources;
        }
    }

    public int GetEndHour(int durationHours)
    {
        return WrapHour(startHour + durationHours);
    }

    public int GetPreparationStartHour()
    {
        return WrapHour(startHour - preparationHours);
    }

    public int GetDesiredResourceAmount(ResourceType resourceType)
    {
        EnsureResourceList();
        for (int i = 0; i < desiredResources.Count; i++)
        {
            if (desiredResources[i].resourceType == resourceType)
            {
                return desiredResources[i].amount;
            }
        }

        return 0;
    }

    internal FactoryShift(
        int startHour,
        int preparationHours,
        IEnumerable<ResourceType> resourceTypes)
    {
        isActive = false;
        this.startHour = WrapHour(startHour);
        this.preparationHours = Mathf.Max(1, preparationHours);
        desiredPops = 0;
        desiredResources = new List<ResourceAmount>();
        EnsureResourceTypes(resourceTypes);
    }

    // Unity needs a parameterless constructor for serialization.
    private FactoryShift()
    {
    }

    internal void SetStartHour(int value)
    {
        startHour = WrapHour(value);
    }

    internal void SetActive(bool value)
    {
        isActive = value;
    }

    internal void SetPreparationHours(int value)
    {
        preparationHours = Mathf.Max(1, value);
    }

    internal void SetDesiredPops(int value)
    {
        desiredPops = Mathf.Max(0, value);
    }

    internal void SetDesiredResourceAmount(
        ResourceType resourceType,
        int value)
    {
        EnsureResourceList();
        int clampedValue = Mathf.Max(0, value);

        for (int i = 0; i < desiredResources.Count; i++)
        {
            ResourceAmount resource = desiredResources[i];
            if (resource.resourceType != resourceType) continue;

            resource.amount = clampedValue;
            desiredResources[i] = resource;
            return;
        }

        desiredResources.Add(new ResourceAmount
        {
            resourceType = resourceType,
            amount = clampedValue
        });
    }

    internal void EnsureResourceTypes(IEnumerable<ResourceType> resourceTypes)
    {
        EnsureResourceList();
        var synchronizedResources = new List<ResourceAmount>();
        if (resourceTypes == null)
        {
            desiredResources = synchronizedResources;
            return;
        }

        var addedResourceTypes = new HashSet<ResourceType>();
        foreach (ResourceType resourceType in resourceTypes)
        {
            if (!addedResourceTypes.Add(resourceType)) continue;

            int existingAmount = 0;
            for (int i = 0; i < desiredResources.Count; i++)
            {
                if (desiredResources[i].resourceType == resourceType)
                {
                    existingAmount = desiredResources[i].amount;
                    break;
                }
            }

            synchronizedResources.Add(new ResourceAmount
            {
                resourceType = resourceType,
                amount = Mathf.Max(0, existingAmount)
            });
        }

        desiredResources = synchronizedResources;
    }

    internal static int WrapHour(int hour)
    {
        int wrappedHour = hour % GameTimeManager.HoursPerDay;
        return wrappedHour < 0
            ? wrappedHour + GameTimeManager.HoursPerDay
            : wrappedHour;
    }

    private void EnsureResourceList()
    {
        if (desiredResources == null)
        {
            desiredResources = new List<ResourceAmount>();
        }
    }
}
