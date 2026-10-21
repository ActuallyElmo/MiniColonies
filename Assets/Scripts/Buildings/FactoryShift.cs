using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public struct FactoryShiftInput
{
    public string inputId;
    public bool hasSelection;
    public ResourceType resourceType;
    public int desiredAmount;
}

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

    [SerializeField]
    private List<FactoryShiftInput> inputSelections = new List<FactoryShiftInput>();

    public IReadOnlyList<FactoryShiftInput> InputSelections
    {
        get { EnsureResourceList(); return inputSelections; }
    }

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

    internal void SynchronizeInputSlots(ResourceProductionBuildingData data, ResourceCatalog catalog, bool initializeAmounts = false)
    {
        EnsureResourceList();
        var synchronized = new List<FactoryShiftInput>();
        var allowed = new List<ResourceType>();
        if (data.resources != null)
        {
            for (int i = 0; i < data.resources.Count; i++)
            {
                ProductionResourceEntry entry = data.resources[i];
                if (entry.direction != ProductionResourceDirection.Input) continue;
                string id = data.GetInputId(i);
                bool existed = TryGetInput(id, out FactoryShiftInput selection);
                if (!existed)
                {
                    data.GetAllowedInputResources(i, catalog, allowed);
                    selection = new FactoryShiftInput { inputId = id };
                    if (allowed.Count > 0)
                    {
                        selection.hasSelection = true;
                        selection.resourceType = allowed[0];
                        bool hasLegacyAmount = false;
                        // Migrate serialized schedules by retaining an already chosen material.
                        for (int j = 0; j < desiredResources.Count; j++)
                            if (allowed.Contains(desiredResources[j].resourceType))
                            {
                                selection.resourceType = desiredResources[j].resourceType;
                                hasLegacyAmount = true;
                                break;
                            }
                        selection.desiredAmount = initializeAmounts || !hasLegacyAmount
                            ? entry.amountAtMaximumEfficiency : GetDesiredResourceAmount(selection.resourceType);
                    }
                }
                else if (!ResourceProductionBuildingData.UsesTypes(entry) && !isActive)
                {
                    // A fixed input follows the factory definition, including when
                    // the designer changes an inactive slot from categories to an item.
                    selection.resourceType = entry.resourceType;
                    selection.hasSelection = ResourceCatalog.IsKnownResource(entry.resourceType, catalog);
                }
                else if (selection.hasSelection && !ResourceProductionBuildingData.AcceptsInput(entry, selection.resourceType, catalog))
                {
                    // Do not silently switch a running recipe to a different material.
                    selection.hasSelection = false;
                }
                selection.desiredAmount = Mathf.Clamp(selection.desiredAmount, 0, Mathf.Max(0, entry.storageCapacity));
                synchronized.Add(selection);
            }
        }
        inputSelections = synchronized;
        RebuildDesiredResources();
    }

    public bool TryGetInput(string inputId, out FactoryShiftInput selection)
    {
        EnsureResourceList();
        for (int i = 0; i < inputSelections.Count; i++)
            if (inputSelections[i].inputId == inputId) { selection = inputSelections[i]; return true; }
        selection = default;
        return false;
    }

    internal void SetInputResource(string inputId, ResourceType resource)
    {
        for (int i = 0; i < inputSelections.Count; i++)
        {
            FactoryShiftInput selection = inputSelections[i];
            if (selection.inputId != inputId) continue;
            selection.resourceType = resource;
            selection.hasSelection = true;
            inputSelections[i] = selection;
            RebuildDesiredResources();
            return;
        }
    }

    internal void SetInputDesiredAmount(string inputId, int amount)
    {
        for (int i = 0; i < inputSelections.Count; i++)
        {
            FactoryShiftInput selection = inputSelections[i];
            if (selection.inputId != inputId) continue;
            selection.desiredAmount = Mathf.Max(0, amount);
            inputSelections[i] = selection;
            RebuildDesiredResources();
            return;
        }
    }

    internal void CopyInputSelectionsFrom(FactoryShift source)
    {
        inputSelections = new List<FactoryShiftInput>(source.InputSelections);
        RebuildDesiredResources();
    }

    private void RebuildDesiredResources()
    {
        desiredResources.Clear();
        for (int i = 0; i < inputSelections.Count; i++)
        {
            FactoryShiftInput selection = inputSelections[i];
            if (!selection.hasSelection) continue;
            int existing = GetDesiredResourceAmount(selection.resourceType);
            SetDesiredResourceAmount(selection.resourceType,
                (int)Math.Min(int.MaxValue, (long)existing + selection.desiredAmount));
        }
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
        if (inputSelections == null) inputSelections = new List<FactoryShiftInput>();
    }
}
