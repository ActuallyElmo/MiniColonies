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
    public ResourceType resourceType;

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
    [Tooltip("Amount of population needed to run this building at 100% efficiency.")]
    public int popRequiredForMaxEfficiency;

    [Header("Resource Inputs and Outputs")]
    [Tooltip("Every resource stored by this building, its capacity, its amount per cycle at maximum efficiency, and whether it is consumed or produced.")]
    public List<ProductionResourceEntry> resources =
        new List<ProductionResourceEntry>();

    private void OnValidate()
    {
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

            resources[i] = resource;
        }
    }
}
