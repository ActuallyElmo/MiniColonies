using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Shared panel presenter for every ResourceProductionBuildingData asset.
/// Assign the text fields from the industrial panel hierarchy in the Inspector.
/// </summary>
public sealed class IndustrialBuildingInfoPanel : BuildingInfoPanel
{
    [Header("Industrial Fields")]
    [SerializeField] private TMP_Text buildingNameText;
    [SerializeField] private TMP_Text populationText;
    [SerializeField] private TMP_Text incomingPopulationText;
    [SerializeField] private TMP_Text efficiencyText;
    [SerializeField] private TMP_Text requiredResourcesText;
    [SerializeField] private TMP_Text producedResourcesText;

    protected override void OnBuildingInitialized(Building building)
    {
        if (!(building.data is ResourceProductionBuildingData productionData))
        {
            Debug.LogError(
                "An industrial panel received a non-industrial building.",
                this);
            return;
        }

        SetText(buildingNameText, productionData.buildingName);
        SetText(
            populationText,
            $"Workers: {building.DeliveredPops}/" +
            $"{building.GetTargetPopCount()}");
        SetText(
            incomingPopulationText,
            $"Incoming workers: {building.ReservedIncomingPops}");
        SetText(
            efficiencyText,
            $"Efficiency: {building.GetProductionEfficiency():P0}");
        SetText(
            requiredResourcesText,
            FormatResources(
                "Inputs",
                productionData.requiredAtMaxEfficiency));
        SetText(
            producedResourcesText,
            FormatResources(
                "Outputs",
                productionData.producedAtMaxEfficiency));
    }

    private static string FormatResources(
        string heading,
        IReadOnlyList<ResourceAmount> resources)
    {
        if (resources == null || resources.Count == 0)
        {
            return $"{heading}: None";
        }

        var text = new StringBuilder(heading).Append(":");
        for (int i = 0; i < resources.Count; i++)
        {
            ResourceAmount resource = resources[i];
            text.Append("\n")
                .Append(resource.resourceType)
                .Append(": ")
                .Append(resource.amount);
        }

        return text.ToString();
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null)
        {
            target.text = value;
        }
    }
}
