using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared panel presenter for every PopHousingBuildingData asset.
/// Assign the text fields from the housing panel hierarchy in the Inspector.
/// </summary>
public sealed class HousingBuildingInfoPanel : BuildingInfoPanel
{
    [Header("Housing Fields")]
    [SerializeField] private TMP_Text buildingNameText;
    [SerializeField] private TMP_Text vehicleStorageText;
    [SerializeField] private TMP_Text ownedVehicleCountText;
    [SerializeField] private TMP_Text totalPopsSentText;
    [SerializeField] private TMP_Text allowedVehicleTypesText;

    [Header("Owned Vehicle List")]
    [Tooltip(
        "Optional. When omitted, the panel finds its first child ScrollRect.")]
    [SerializeField] private ScrollRect ownedVehiclesScrollRect;

    [Tooltip(
        "Optional override for the row parent. When omitted, the ScrollRect's " +
        "Content transform is used.")]
    [SerializeField] private RectTransform vehicleListContent;

    [Tooltip("Prefab instantiated once for every vehicle the house owns.")]
    [SerializeField] private GameObject vehicleRowPrefab;

    [SerializeField] private bool resetVehicleScrollPositionOnOpen = true;

    [Header("Live Refresh")]
    [SerializeField, Min(0.05f)] private float refreshInterval = 0.25f;
    private float nextRefreshTime;
    private readonly List<HousingVehicleRowUI> vehicleRows =
        new List<HousingVehicleRowUI>();
    private bool reportedMissingVehicleListConfiguration;

    private void Update()
    {
        if (CurrentBuilding == null ||
            Time.unscaledTime < nextRefreshTime ||
            !(CurrentBuilding.data is PopHousingBuildingData housingData))
        {
            return;
        }

        nextRefreshTime = Time.unscaledTime + refreshInterval;
        RefreshFields(CurrentBuilding, housingData);
        RefreshVehicleRows();
    }

    protected override void OnBuildingInitialized(Building building)
    {
        if (!(building.data is PopHousingBuildingData housingData))
        {
            Debug.LogError(
                "A housing panel received a non-housing building.",
                this);
            return;
        }

        building.VehicleOwnershipChanged += HandleVehicleOwnershipChanged;
        nextRefreshTime = Time.unscaledTime + refreshInterval;
        RefreshFields(building, housingData);
        RebuildVehicleRows(
            building,
            resetVehicleScrollPositionOnOpen);
    }

    protected override void OnBuildingCleared(Building previousBuilding)
    {
        if (previousBuilding != null)
        {
            previousBuilding.VehicleOwnershipChanged -=
                HandleVehicleOwnershipChanged;
        }

        ClearVehicleRows();
    }

    private void HandleVehicleOwnershipChanged(Building building)
    {
        if (building == CurrentBuilding &&
            building.data is PopHousingBuildingData housingData)
        {
            RefreshFields(building, housingData);
            RebuildVehicleRows(building, false);
        }
    }

    private void RebuildVehicleRows(
        Building building,
        bool resetScrollPosition)
    {
        if (!TryResolveVehicleListConfiguration())
        {
            ClearVehicleRows();
            return;
        }

        ClearVehicleRows();

        IReadOnlyList<VehicleAI> ownedVehicles = building.OwnedVehicles;
        for (int i = 0; i < ownedVehicles.Count; i++)
        {
            VehicleAI vehicle = ownedVehicles[i];
            if (vehicle == null) continue;

            GameObject rowObject = Instantiate(
                vehicleRowPrefab,
                vehicleListContent,
                false);

            if (!rowObject.TryGetComponent(
                    out HousingVehicleRowUI row))
            {
                Debug.LogError(
                    $"{vehicleRowPrefab.name} needs a " +
                    $"{nameof(HousingVehicleRowUI)} component.",
                    vehicleRowPrefab);
                Destroy(rowObject);
                continue;
            }

            row.Bind(vehicle);
            vehicleRows.Add(row);
        }

        RefreshVehicleListLayout(resetScrollPosition);
    }

    private void RefreshVehicleRows()
    {
        bool anyValueChanged = false;
        for (int i = vehicleRows.Count - 1; i >= 0; i--)
        {
            HousingVehicleRowUI row = vehicleRows[i];
            if (row == null)
            {
                vehicleRows.RemoveAt(i);
                continue;
            }

            anyValueChanged |= row.Refresh();
        }

        if (anyValueChanged)
        {
            RefreshVehicleListLayout(false);
        }
    }

    private void RefreshVehicleListLayout(bool resetScrollPosition)
    {
        if (ownedVehiclesScrollRect == null ||
            vehicleListContent == null)
        {
            return;
        }

        float previousScrollPosition =
            ownedVehiclesScrollRect.verticalNormalizedPosition;

        LayoutRebuilder.ForceRebuildLayoutImmediate(vehicleListContent);
        Canvas.ForceUpdateCanvases();

        RectTransform viewport = ownedVehiclesScrollRect.viewport;
        bool requiresScrolling =
            viewport != null &&
            vehicleListContent.rect.height >
                viewport.rect.height + 0.5f;

        Scrollbar verticalScrollbar =
            ownedVehiclesScrollRect.verticalScrollbar;
        if (verticalScrollbar != null &&
            verticalScrollbar.gameObject.activeSelf != requiresScrolling)
        {
            verticalScrollbar.gameObject.SetActive(requiresScrolling);
            Canvas.ForceUpdateCanvases();
        }

        ownedVehiclesScrollRect.vertical = requiresScrolling;
        ownedVehiclesScrollRect.StopMovement();

        ownedVehiclesScrollRect.verticalNormalizedPosition =
            !requiresScrolling || resetScrollPosition
                ? 1f
                : Mathf.Clamp01(previousScrollPosition);
    }

    private void ClearVehicleRows()
    {
        if (vehicleListContent != null)
        {
            HousingVehicleRowUI[] rowsInContent =
                vehicleListContent.GetComponentsInChildren
                    <HousingVehicleRowUI>(true);

            for (int i = 0; i < rowsInContent.Length; i++)
            {
                HousingVehicleRowUI row = rowsInContent[i];
                if (row == null) continue;

                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
        }
        else
        {
            for (int i = 0; i < vehicleRows.Count; i++)
            {
                HousingVehicleRowUI row = vehicleRows[i];
                if (row == null) continue;

                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
        }

        vehicleRows.Clear();
    }

    private bool TryResolveVehicleListConfiguration()
    {
        if (ownedVehiclesScrollRect == null)
        {
            ownedVehiclesScrollRect =
                GetComponentInChildren<ScrollRect>(true);
        }

        if (vehicleListContent == null &&
            ownedVehiclesScrollRect != null)
        {
            vehicleListContent = ownedVehiclesScrollRect.content;
        }

        if (ownedVehiclesScrollRect != null &&
            vehicleListContent != null &&
            vehicleRowPrefab != null)
        {
            ownedVehiclesScrollRect.movementType =
                ScrollRect.MovementType.Clamped;
            ownedVehiclesScrollRect.inertia = false;
            // The panel controls scrollbar visibility itself. Expanding the
            // viewport when the bar appears changes every row's width and can
            // produce a visible horizontal jump.
            ownedVehiclesScrollRect.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.AutoHide;
            reportedMissingVehicleListConfiguration = false;
            return true;
        }

        if (!reportedMissingVehicleListConfiguration)
        {
            Debug.LogWarning(
                "The housing vehicle list needs a Content RectTransform and " +
                "a HousingVehicleRowUI prefab. Assign them on the housing " +
                "panel, or put a configured ScrollRect below the panel.",
                this);
            reportedMissingVehicleListConfiguration = true;
        }

        return false;
    }

    private void RefreshFields(
        Building building,
        PopHousingBuildingData housingData)
    {
        int capacity = building.GetVehicleSpaceCapacity();
        int usedSpace = building.GetUsedVehicleSpace();
        int totalVehicles = building.GetOwnedVehicleCount();
        int totalPopsSent =
            building.GetTotalPopsSentByOwnedVehicles();
        int totalPopCapacity =
            building.GetTotalPopCapacityOfOwnedVehicles();
        int sentVehicles = building.GetSentPopTransportVehicleCount();
        int availableVehicles = Mathf.Max(0, totalVehicles - sentVehicles);

        string vehicleSpaceSummary = $"{usedSpace}/{capacity}";
        string ownedVehicleSummary = $"{totalVehicles}";
        string totalPopsSentSummary =
            $"{totalPopsSent}/{totalPopCapacity}";
        string vehicleStatusSummary =
            $"{availableVehicles}\n" +
            $"{sentVehicles}";

        bool useCombinedFallback =
            vehicleStorageText == null &&
            ownedVehicleCountText == null &&
            totalPopsSentText == null;

        if (useCombinedFallback)
        {
            SetText(
                buildingNameText,
                $"{housingData.buildingName}\n\n" +
                $"{vehicleSpaceSummary}\n" +
                $"{ownedVehicleSummary}\n" +
                $"{totalPopsSentSummary}\n" +
                vehicleStatusSummary);
        }
        else
        {
            SetText(buildingNameText, housingData.buildingName);
            SetText(vehicleStorageText, vehicleSpaceSummary);
            SetText(ownedVehicleCountText, ownedVehicleSummary);
            SetText(totalPopsSentText, totalPopsSentSummary);
        }

        SetText(
            allowedVehicleTypesText,
            FormatAllowedVehicles(housingData));
    }

    private static string FormatAllowedVehicles(
        PopHousingBuildingData housingData)
    {
        if (housingData.typesOfPopTransportVehiclesAllowed == null ||
            housingData.typesOfPopTransportVehiclesAllowed.Count == 0)
        {
            return "Allowed vehicles: None";
        }

        var text = new StringBuilder("Allowed vehicles:");
        for (int i = 0;
             i < housingData.typesOfPopTransportVehiclesAllowed.Count;
             i++)
        {
            PopTransportVehicleData vehicle =
                housingData.typesOfPopTransportVehiclesAllowed[i];
            if (vehicle == null) continue;

            string vehicleName = string.IsNullOrWhiteSpace(vehicle.vehicleName)
                ? vehicle.name
                : vehicle.vehicleName;
            text.Append("\n").Append(vehicleName);
        }

        return text.ToString();
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null && target.text != value)
        {
            target.text = value;
        }
    }
}
