using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presents one vehicle owned by the housing building currently shown in the
/// housing information panel.
/// </summary>
[DisallowMultipleComponent]
public sealed class HousingVehicleRowUI : MonoBehaviour
{
    [Header("Vehicle Row Fields")]
    [SerializeField] private Image vehicleIcon;
    [SerializeField] private TMP_Text vehicleNameText;
    [SerializeField] private TMP_Text vehicleStatusText;
    [SerializeField] private TMP_Text vehicleCapacityText;
    [SerializeField] private TMP_Text vehicleOccupiedSpaceText;
    [SerializeField] private Button removeVehicleButton;

    public VehicleAI Vehicle { get; private set; }

    private void Awake()
    {
        PrepareForHousingListLayout();
        ResolveVehicleIcon();
        ApplyPanelFont();
    }

    private void ResolveVehicleIcon()
    {
        if (vehicleIcon != null)
        {
            return;
        }

        Transform iconTransform = transform.Find("VehicleIcon");
        if (iconTransform != null)
        {
            vehicleIcon = iconTransform.GetComponent<Image>();
            return;
        }

        GameObject iconObject = new GameObject(
            "VehicleIcon",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(LayoutElement));
        iconObject.transform.SetParent(transform, false);
        vehicleIcon = iconObject.GetComponent<Image>();
        vehicleIcon.color = new Color32(110, 150, 205, 255);
        vehicleIcon.raycastTarget = false;
        vehicleIcon.preserveAspect = true;
        LayoutElement layout = iconObject.GetComponent<LayoutElement>();
        layout.preferredWidth = 20f;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 1f;
    }

    /// <summary>
    /// Gives older row instances a real layout height. The authored prefab's
    /// LayoutElement is left untouched so editor changes remain authoritative.
    /// </summary>
    public void PrepareForHousingListLayout()
    {
        RectTransform rowRect = transform as RectTransform;
        if (rowRect == null)
        {
            return;
        }

        LayoutElement layout = GetComponent<LayoutElement>();
        if (layout == null)
        {
            layout = gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 28f;
            layout.preferredHeight = 28f;
            layout.flexibleHeight = 0f;
            layout.flexibleWidth = 1f;
        }
    }

    private void ApplyPanelFont()
    {
        HousingBuildingInfoPanel panel =
            GetComponentInParent<HousingBuildingInfoPanel>(true);
        TMP_FontAsset font = panel != null ? panel.PanelFont : null;
        if (font == null)
        {
            return;
        }

        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null)
            {
                texts[i].font = font;
            }
        }
    }

    /// <summary>
    /// Connects this row to a vehicle. The row does not own the vehicle and
    /// must never create or destroy it.
    /// </summary>
    public void Bind(VehicleAI vehicle)
    {
        Vehicle = vehicle;
        ResolveRemoveButton();
        removeVehicleButton?.onClick.RemoveListener(RemoveVehicle);
        removeVehicleButton?.onClick.AddListener(RemoveVehicle);
        if (removeVehicleButton != null)
        {
            removeVehicleButton.interactable = Vehicle != null;
        }

        ResolveStaticTexts();
        PopulateStaticFields();
        Refresh();
    }

    private void OnDestroy()
    {
        removeVehicleButton?.onClick.RemoveListener(RemoveVehicle);
    }

    /// <summary>
    /// Refreshes values that can change while the panel is open.
    /// HousingBuildingInfoPanel calls this on its live refresh interval.
    /// </summary>
    public bool Refresh()
    {
        if (Vehicle == null)
        {
            if (removeVehicleButton != null)
            {
                removeVehicleButton.interactable = false;
            }

            bool unavailableNameChanged =
                SetText(vehicleNameText, "Unavailable vehicle");
            bool unavailableStatusChanged =
                SetText(vehicleStatusText, "Unavailable");
            return unavailableNameChanged || unavailableStatusChanged;
        }

        bool nameChanged = SetText(
            vehicleNameText,
            GetVehicleName(Vehicle));
        bool statusChanged = SetText(
            vehicleStatusText,
            $"{GetVehicleStatus(Vehicle)}");
        return nameChanged || statusChanged;
    }

    private void RemoveVehicle()
    {
        VehicleAI vehicleToRemove = Vehicle;
        Building owner = vehicleToRemove != null
            ? vehicleToRemove.homeBuilding
            : null;

        if (vehicleToRemove == null || owner == null)
        {
            return;
        }

        if (removeVehicleButton != null)
        {
            removeVehicleButton.interactable = false;
        }

        if (!owner.TryRemoveOwnedVehicle(vehicleToRemove) &&
            removeVehicleButton != null)
        {
            removeVehicleButton.interactable = true;
        }
    }

    private void ResolveRemoveButton()
    {
        if (removeVehicleButton != null)
        {
            return;
        }

        Button[] buttons = GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button candidate = buttons[i];
            if (candidate != null &&
                candidate.name.IndexOf(
                    "remove",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                removeVehicleButton = candidate;
                return;
            }
        }
    }

    private static string GetVehicleName(VehicleAI vehicle)
    {
        VehicleData vehicleData = vehicle.vehicleData;
        if (vehicleData == null)
        {
            return vehicle.name;
        }

        return string.IsNullOrWhiteSpace(vehicleData.vehicleName)
            ? vehicleData.name
            : vehicleData.vehicleName;
    }

    private void PopulateStaticFields()
    {
        if (vehicleIcon != null)
        {
            PopTransportVehicleData iconData = Vehicle != null
                ? Vehicle.vehicleData as PopTransportVehicleData
                : null;
            vehicleIcon.sprite = iconData != null
                ? iconData.vehicleIcon
                : null;
            vehicleIcon.preserveAspect = true;
        }

        if (Vehicle != null &&
            Vehicle.vehicleData is PopTransportVehicleData popVehicleData)
        {
            SetText(
                vehicleCapacityText,
                $"{Mathf.Max(0, popVehicleData.amountOfPopsTransported)}");
            SetText(
                vehicleOccupiedSpaceText,
                $"{Mathf.Max(0, popVehicleData.vehicleSpaceUsed)}");
            return;
        }

        SetText(vehicleCapacityText, "Unavailable");
        SetText(vehicleOccupiedSpaceText, "Unavailable");
    }

    private void ResolveStaticTexts()
    {
        if (vehicleCapacityText != null &&
            vehicleOccupiedSpaceText != null)
        {
            return;
        }

        TMP_Text[] rowTexts = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < rowTexts.Length; i++)
        {
            TMP_Text candidate = rowTexts[i];
            if (candidate == null) continue;

            bool belongsToOccupiedSpace =
                HierarchyNameContains(candidate.transform, "occupied");

            if (vehicleOccupiedSpaceText == null &&
                belongsToOccupiedSpace)
            {
                vehicleOccupiedSpaceText = candidate;
            }
            else if (vehicleCapacityText == null &&
                     !belongsToOccupiedSpace &&
                     HierarchyNameContains(candidate.transform, "capacity"))
            {
                vehicleCapacityText = candidate;
            }
        }
    }

    private static bool HierarchyNameContains(
        Transform transformToCheck,
        string value)
    {
        for (Transform current = transformToCheck;
             current != null;
             current = current.parent)
        {
            if (current.name.IndexOf(
                    value,
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetVehicleStatus(VehicleAI vehicle)
    {
        switch (vehicle.currentState)
        {
            case VehicleAI.VehicleState.Outbound:
                return vehicle.targetBuilding != null
                    ? $"On route"
                    : "No target building";

            case VehicleAI.VehicleState.Inbound:
                return "Returning";

            case VehicleAI.VehicleState.AtDestination:
                return "Arrived";

            default:
                return "Parked";
        }
    }

    private static string GetBuildingName(Building building)
    {
        if (building.data != null &&
            !string.IsNullOrWhiteSpace(building.data.buildingName))
        {
            return building.data.buildingName;
        }

        return building.name;
    }

    private static bool SetText(TMP_Text target, string value)
    {
        if (target == null || target.text == value)
        {
            return false;
        }

        target.text = value;
        return true;
    }
}
