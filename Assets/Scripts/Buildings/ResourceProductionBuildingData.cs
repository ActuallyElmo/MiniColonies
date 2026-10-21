using UnityEngine;
using System.Collections.Generic;

public enum FactoryShiftDuration
{
    FourHours = 4,
    SixHours = 6,
    EightHours = 8
}

public enum ProductionResourceDirection
{
    Input,
    Output
}

[System.Serializable]
public struct ProductionResourceEntry
{
    // Kept for serialized legacy inputs and concrete outputs.
    public ResourceType resourceType;

    [HideInInspector] public string inputId;
    public string inputName;
    [Tooltip("Enabled: accept resources matching every required type. Disabled: require the specific Resource Type item.")]
    public bool useResourceTypes;
    public List<string> requiredTypes;

    [Min(0)]
    public int storageCapacity;

    [Min(0)]
    public int amountAtMaximumEfficiency;

    public ProductionResourceDirection direction;
}

[CreateAssetMenu(fileName = "NewResourceProduction", menuName = "ColonySim/Buildings/Production")]
public class ResourceProductionBuildingData : BuildingData
{
    [Header("Industrial Vehicle Storage")]
    [Min(0)]
    [Tooltip("Total parking space available for industrial resource vehicles owned by this building.")]
    public int industrialVehiclesStorageSpace = 4;

    [Header("Schedule")]
    [Tooltip("The fixed length of every shift for this building type.")]
    public FactoryShiftDuration shiftDuration =
        FactoryShiftDuration.EightHours;

    public int ShiftDurationHours
    {
        get
        {
            int durationHours = (int)shiftDuration;
            return durationHours == 4 ||
                   durationHours == 6 ||
                   durationHours == 8
                ? durationHours
                : 8;
        }
    }

    [Header("Production Requirements")]
    [Tooltip("Singular role shown in this factory type's UI, for example engineer, scientist or miner.")]
    public string workerRoleName = "factory worker";

    [Tooltip("Plural role shown in this factory type's UI, for example engineers, scientists or miners.")]
    public string workerRoleNamePlural = "factory workers";

    public string WorkerRoleName => string.IsNullOrWhiteSpace(workerRoleName)
        ? "factory worker"
        : workerRoleName.Trim();

    public string WorkerRoleNamePlural => string.IsNullOrWhiteSpace(workerRoleNamePlural)
        ? "factory workers"
        : workerRoleNamePlural.Trim();

    [Tooltip("Amount of population needed to run this building at 100% efficiency.")]
    public int popRequiredForMaxEfficiency;

    [Header("Resource Inputs and Outputs")]
    [Tooltip("Every resource stored by this building, its capacity, its amount per cycle at maximum efficiency, and whether it is consumed or produced.")]
    public List<ProductionResourceEntry> resources =
        new List<ProductionResourceEntry>();

    public string GetInputId(int resourceIndex)
    {
        ProductionResourceEntry entry = resources[resourceIndex];
        return string.IsNullOrEmpty(entry.inputId) ? "legacy-" + resourceIndex : entry.inputId;
    }

    public int FindInputIndex(string inputId)
    {
        if (resources == null || string.IsNullOrEmpty(inputId)) return -1;
        for (int i = 0; i < resources.Count; i++)
            if (resources[i].direction == ProductionResourceDirection.Input && GetInputId(i) == inputId) return i;
        return -1;
    }

    public static bool UsesTypes(ProductionResourceEntry entry) => entry.useResourceTypes;

    public static bool AcceptsInput(ProductionResourceEntry entry, ResourceType resource, ResourceCatalog catalog)
    {
        if (entry.direction != ProductionResourceDirection.Input ||
            !ResourceCatalog.IsKnownResource(resource, catalog)) return false;
        return UsesTypes(entry)
            ? catalog != null && catalog.HasAllTypes(resource, entry.requiredTypes)
            : entry.resourceType == resource;
    }

    public void GetAllowedInputResources(int resourceIndex, ResourceCatalog catalog, List<ResourceType> results)
    {
        if (results == null) throw new System.ArgumentNullException(nameof(results));
        results.Clear();
        if (resources == null || resourceIndex < 0 || resourceIndex >= resources.Count) return;
        ProductionResourceEntry entry = resources[resourceIndex];
        if (entry.direction != ProductionResourceDirection.Input) return;
        if (UsesTypes(entry))
        {
            if (catalog != null) catalog.GetResourcesMatchingTypes(entry.requiredTypes, results);
        }
        else if (ResourceCatalog.IsKnownResource(entry.resourceType, catalog)) results.Add(entry.resourceType);
    }

    public string GetInputName(int resourceIndex)
    {
        ProductionResourceEntry entry = resources[resourceIndex];
        if (!UsesTypes(entry)) return string.IsNullOrWhiteSpace(entry.inputName)
            ? ResourceCatalog.GetDisplayName(entry.resourceType) : entry.inputName.Trim();
        if (entry.requiredTypes == null || entry.requiredTypes.Count == 0) return "Choose a type";
        var names = new List<string>();
        for (int i = 0; i < entry.requiredTypes.Count; i++)
        {
            string displayName = ResourceCatalog.Current?.FindType(entry.requiredTypes[i])?.displayName;
            names.Add(!string.IsNullOrWhiteSpace(displayName) ? displayName.Trim() :
                !string.IsNullOrWhiteSpace(entry.requiredTypes[i]) ? entry.requiredTypes[i] : "Unknown type");
        }
        return string.Join(" + ", names);
    }

    public int GetResourceCapacity(ResourceType resource, ResourceCatalog catalog)
    {
        int capacity = 0;
        if (resources == null) return capacity;
        for (int i = 0; i < resources.Count; i++)
        {
            ProductionResourceEntry entry = resources[i];
            bool matches = entry.direction == ProductionResourceDirection.Input
                ? AcceptsInput(entry, resource, catalog)
                : entry.direction == ProductionResourceDirection.Output && entry.resourceType == resource;
            // Overlapping rules describe the same per-resource bin; they do not add capacity.
            if (matches) capacity = Mathf.Max(capacity, entry.storageCapacity);
        }
        return capacity;
    }

    private void OnValidate()
    {
        workerRoleName = WorkerRoleName;
        workerRoleNamePlural = WorkerRoleNamePlural;

        industrialVehiclesStorageSpace = Mathf.Max(
            0,
            industrialVehiclesStorageSpace);

        if ((int)shiftDuration != 4 &&
            (int)shiftDuration != 6 &&
            (int)shiftDuration != 8)
        {
            shiftDuration = FactoryShiftDuration.EightHours;
        }

        popRequiredForMaxEfficiency = Mathf.Max(
            0,
            popRequiredForMaxEfficiency);

        if (resources == null)
        {
            resources = new List<ProductionResourceEntry>();
            return;
        }

        var inputIds = new HashSet<string>();
        for (int i = 0; i < resources.Count; i++)
        {
            ProductionResourceEntry resource = resources[i];
            resource.amountAtMaximumEfficiency = Mathf.Max(
                0,
                resource.amountAtMaximumEfficiency);
            resource.storageCapacity = Mathf.Max(
                resource.amountAtMaximumEfficiency,
                resource.storageCapacity);
            if (resource.direction != ProductionResourceDirection.Input &&
                resource.direction != ProductionResourceDirection.Output)
            {
                resource.direction = ProductionResourceDirection.Input;
            }

            if (resource.direction == ProductionResourceDirection.Input &&
                (string.IsNullOrEmpty(resource.inputId) || !inputIds.Add(resource.inputId)))
            {
                resource.inputId = System.Guid.NewGuid().ToString("N");
                inputIds.Add(resource.inputId);
            }

            resources[i] = resource;
        }
    }
}
