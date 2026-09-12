using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays and configures one resource vehicle owned by a production
/// building. Resource dispatch itself is handled by the later logistics phase.
/// </summary>
[DisallowMultipleComponent]
public sealed class IndustrialVehicleRowUI : MonoBehaviour
{
    [SerializeField] private Image vehicleIcon;
    [SerializeField] private TMP_Text vehicleNameText;
    [SerializeField] private TMP_Text vehicleStatusText;
    [SerializeField] private TMP_Text vehicleCapacityText;
    [SerializeField] private TMP_Dropdown carriedResourceDropdown;
    [SerializeField] private Button removeVehicleButton;

    private readonly List<ResourceType> compatibleOutputs =
        new List<ResourceType>();
    private Building owner;

    public VehicleAI Vehicle { get; private set; }

    public void Bind(Building building, VehicleAI vehicle)
    {
        owner = building;
        Vehicle = vehicle;

        MatchDropdownFontToRow();

        if (removeVehicleButton != null)
        {
            removeVehicleButton.onClick.RemoveAllListeners();
            removeVehicleButton.onClick.AddListener(RemoveVehicle);
            removeVehicleButton.interactable = Vehicle != null;
        }

        ConfigureStaticFields();
        ConfigureOutputDropdown();
        Refresh();
    }

    private void MatchDropdownFontToRow()
    {
        if (carriedResourceDropdown == null || vehicleNameText == null ||
            vehicleNameText.font == null)
        {
            return;
        }

        TMP_Text[] dropdownTexts = carriedResourceDropdown
            .GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < dropdownTexts.Length; i++)
        {
            if (dropdownTexts[i].font != vehicleNameText.font)
            {
                dropdownTexts[i].font = vehicleNameText.font;
            }
        }
    }

    public bool Refresh()
    {
        if (Vehicle == null)
        {
            bool nameChanged = SetText(vehicleNameText, "Unavailable vehicle");
            bool statusChanged = SetText(vehicleStatusText, "Unavailable");
            if (removeVehicleButton != null)
            {
                removeVehicleButton.interactable = false;
            }

            return nameChanged || statusChanged;
        }

        return SetText(vehicleStatusText, GetVehicleStatus(Vehicle));
    }

    private void ConfigureStaticFields()
    {
        ResourceTransportVehicleData vehicleData = Vehicle != null
            ? Vehicle.vehicleData as ResourceTransportVehicleData
            : null;

        if (vehicleIcon != null)
        {
            vehicleIcon.sprite = vehicleData != null
                ? vehicleData.vehicleIcon
                : null;
            vehicleIcon.preserveAspect = true;
        }

        string vehicleName = Vehicle != null ? Vehicle.name : "Unavailable";
        if (vehicleData != null)
        {
            vehicleName = string.IsNullOrWhiteSpace(vehicleData.vehicleName)
                ? vehicleData.name
                : vehicleData.vehicleName;
        }

        SetText(vehicleNameText, vehicleName);
        SetText(
            vehicleCapacityText,
            vehicleData != null
                ? $"{Mathf.Max(0, vehicleData.vehicleCapacity)} Units"
                : "-- Units");
    }

    private void ConfigureOutputDropdown()
    {
        if (carriedResourceDropdown == null) return;

        carriedResourceDropdown.onValueChanged.RemoveAllListeners();
        carriedResourceDropdown.ClearOptions();
        compatibleOutputs.Clear();

        ResourceTransportVehicleData vehicleData = Vehicle != null
            ? Vehicle.vehicleData as ResourceTransportVehicleData
            : null;
        owner?.GetCompatibleFactoryOutputs(vehicleData, compatibleOutputs);

        var labels = new List<string> { "None" };
        for (int i = 0; i < compatibleOutputs.Count; i++)
        {
            labels.Add(NicifyName(compatibleOutputs[i].ToString()));
        }

        if (compatibleOutputs.Count == 0)
        {
            carriedResourceDropdown.AddOptions(labels);
            carriedResourceDropdown.SetValueWithoutNotify(0);
            carriedResourceDropdown.interactable = false;
            carriedResourceDropdown.RefreshShownValue();
            return;
        }

        carriedResourceDropdown.AddOptions(labels);
        carriedResourceDropdown.interactable = true;
        int selectedIndex = Vehicle != null && Vehicle.HasAssignedResourceType
            ? compatibleOutputs.IndexOf(Vehicle.AssignedResourceType) + 1
            : 0;
        carriedResourceDropdown.SetValueWithoutNotify(
            Mathf.Max(0, selectedIndex));
        carriedResourceDropdown.RefreshShownValue();
        carriedResourceDropdown.onValueChanged.AddListener(
            HandleOutputSelectionChanged);
    }

    private void HandleOutputSelectionChanged(int index)
    {
        if (owner == null || Vehicle == null || index < 0)
        {
            return;
        }

        if (index == 0)
        {
            owner.TryClearResourceVehicleOutput(Vehicle);
            return;
        }

        int outputIndex = index - 1;
        if (outputIndex >= compatibleOutputs.Count) return;

        owner.TrySetResourceVehicleOutput(
            Vehicle,
            compatibleOutputs[outputIndex]);
    }

    private void RemoveVehicle()
    {
        if (owner != null && Vehicle != null)
        {
            owner.TryRemoveOwnedVehicle(Vehicle);
        }
    }

    private static string GetVehicleStatus(VehicleAI vehicle)
    {
        switch (vehicle.currentState)
        {
            case VehicleAI.VehicleState.Outbound:
                return vehicle.targetBuilding != null
                    ? "Delivering"
                    : "No destination";
            case VehicleAI.VehicleState.Inbound:
                return "Returning";
            case VehicleAI.VehicleState.AtDestination:
                return "Arrived";
            default:
                return "Parked";
        }
    }

    private static bool SetText(TMP_Text text, string value)
    {
        if (text == null || text.text == value) return false;
        text.text = value;
        return true;
    }

    private static string NicifyName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var result = new System.Text.StringBuilder(name.Length + 4);
        result.Append(name[0]);
        for (int i = 1; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && !char.IsWhiteSpace(name[i - 1]))
            {
                result.Append(' ');
            }

            result.Append(name[i]);
        }

        return result.ToString();
    }
}
