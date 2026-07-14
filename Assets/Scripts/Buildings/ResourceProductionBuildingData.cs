using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewResourceProduction", menuName = "ColonySim/Buildings/Production")]
public class ResourceProductionBuildingData : BuildingData
{
    [Header("Production Requirements")]
    [Tooltip("Amount of population needed to run this building at 100% efficiency.")]
    public int popRequiredForMaxEfficiency;

    [Header("Resource Inputs and Outputs")]
    [Tooltip("Resources consumed per production cycle at max efficiency.")]
    public List<ResourceAmount> requiredAtMaxEfficiency = new List<ResourceAmount>();
    
    [Tooltip("Resources generated per production cycle at max efficiency.")]
    public List<ResourceAmount> producedAtMaxEfficiency = new List<ResourceAmount>();
}