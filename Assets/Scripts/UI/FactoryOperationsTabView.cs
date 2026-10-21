using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Read-only view of factory storage and the live preparation/production
/// state. All values come from Building so closing the panel never pauses or
/// resets the simulation.
/// </summary>
public sealed class FactoryOperationsTabView : MonoBehaviour
{
    [Header("Factory Performance")]
    [SerializeField] private TMP_Text workerEfficiencyText;
    [SerializeField] private TMP_Text shiftProgressText;
    [SerializeField] private TMP_Text throughputLabel;
    [SerializeField] private RectTransform throughputRowsRoot;

    [Header("Storage")]
    [SerializeField] private RectTransform storageRowsRoot;

    [Header("Next Shift Preparation")]
    [SerializeField] private TMP_Text preparationStateText;
    [SerializeField] private TMP_Text preparedPopsText;

    [Header("Current Shift")]
    [SerializeField] private TMP_Text currentShiftStateText;
    [SerializeField] private TMP_Text currentWorkersText;
    [SerializeField] private TMP_Text currentEfficiencyText;
    [SerializeField] private RectTransform currentInputRowsRoot;
    [SerializeField] private RectTransform pendingOutputRowsRoot;

    [Header("Rows")]
    [SerializeField] private FactoryStatusResourceRowView resourceRowPrefab;

    private readonly List<FactoryStatusResourceRowView> _spawnedRows =
        new List<FactoryStatusResourceRowView>();
    private Building _building;
    private bool _refreshPending;
    private TMP_Text _currentInputsLabel;

    public void Bind(Building building)
    {
        if (_building != null)
        {
            _building.FactoryRuntimeChanged -= HandleRuntimeChanged;
        }

        _building = building;
        if (_building != null)
        {
            _building.FactoryRuntimeChanged += HandleRuntimeChanged;
        }

        _refreshPending = false;
        Refresh();
    }

    private void LateUpdate()
    {
        if (!_refreshPending) return;

        _refreshPending = false;
        Refresh();
    }

    private void OnDestroy()
    {
        if (_building != null)
        {
            _building.FactoryRuntimeChanged -= HandleRuntimeChanged;
        }
    }

    private void HandleRuntimeChanged(Building building)
    {
        // Runtime changes can arrive in bursts while dispatching vehicles.
        // Rebuilding once at the end of the frame keeps the layout stable.
        if (building == _building) _refreshPending = true;
    }

    public void Refresh()
    {
        ClearRows();
        if (_building == null ||
            !(_building.data is ResourceProductionBuildingData productionData))
        {
            SetText(preparationStateText, "No factory selected");
            SetText(preparedPopsText, string.Empty);
            SetText(currentShiftStateText, "No factory selected");
            SetText(currentWorkersText, string.Empty);
            SetText(currentEfficiencyText, string.Empty);
            SetText(workerEfficiencyText, "Staffing efficiency: --");
            SetText(throughputLabel, string.Empty);
            SetText(shiftProgressText, "Shift progress: --");
            return;
        }

        // Bind the existing authored label for scenes created before this reference existed.
        if (throughputLabel == null && throughputRowsRoot != null)
        {
            throughputLabel = throughputRowsRoot.parent
                ?.Find("ThroughputLabel")?.GetComponent<TMP_Text>();
        }
        SetText(
            throughputLabel,
            $"Throughput per {productionData.WorkerRoleName} / hour " +
            "(100% efficiency)");
        RebuildPerformance(productionData);
        RebuildStorage();
        RefreshPreparation(productionData);
        RefreshCurrentShift(productionData);
        RebuildLayout();
    }

    private void RebuildStorage()
    {
        IReadOnlyList<ResourceAmount> stored = _building.FactoryStorage;
        var displayed = new HashSet<ResourceType>();
        for (int i = 0; i < stored.Count; i++)
        {
            ResourceAmount resource = stored[i];
            if (resource.amount <= 0) continue;
            if (!displayed.Add(resource.resourceType)) continue;
            SpawnRow(
                storageRowsRoot,
                resource.resourceType,
                $"{resource.amount}" +
                $" / {_building.GetFactoryResourceCapacity(resource.resourceType)}");
        }
        if (displayed.Count == 0) SpawnHeader(storageRowsRoot, "Storage empty");
    }

    private void RebuildPerformance(
        ResourceProductionBuildingData productionData)
    {
        if (productionData.resources == null) return;

        RebuildPerformanceDirection(
            productionData,
            ProductionResourceDirection.Input,
            "Incoming Resources");
        RebuildPerformanceDirection(
            productionData,
            ProductionResourceDirection.Output,
            "Outgoing Resources");
    }

    private void RebuildPerformanceDirection(
        ResourceProductionBuildingData productionData,
        ProductionResourceDirection direction,
        string header)
    {
        bool headerSpawned = false;
        for (int i = 0; i < productionData.resources.Count; i++)
        {
            ProductionResourceEntry resource = productionData.resources[i];
            if (resource.direction != direction) continue;
            if (!headerSpawned)
            {
                SpawnHeader(throughputRowsRoot, header);
                headerSpawned = true;
            }
            int workers = _building.GetTargetPopCount();
            int hours = _building.FactoryShiftDurationHours;
            float throughput = workers <= 0 || hours <= 0 ? 0f :
                Mathf.Max(0, resource.amountAtMaximumEfficiency) / (float)(workers * hours);
            bool typedInput = direction == ProductionResourceDirection.Input;
            SpawnRow(
                throughputRowsRoot,
                typedInput ? productionData.GetInputName(i) : ResourceCatalog.GetDisplayName(resource.resourceType),
                $"{throughput:0.####} / {productionData.WorkerRoleName} / hr",
                typedInput && ResourceProductionBuildingData.UsesTypes(resource)
                    ? ResourceCatalog.Current?.fallbackIcon : ResourceCatalog.GetIcon(resource.resourceType));
        }
    }

    private void RefreshPreparation(ResourceProductionBuildingData productionData)
    {
        FactoryShift shift = _building.PreparingFactoryShift;
        if (shift == null)
        {
            SetText(preparationStateText, "No shift is being prepared");
            SetText(
                preparedPopsText,
                $"Prepared {productionData.WorkerRoleNamePlural}: --");
            return;
        }

        SetText(
            preparationStateText,
            $"Incoming shift  {FormatHour(shift.StartHour)}–" +
            $"{FormatHour(shift.GetEndHour(_building.FactoryShiftDurationHours))}");
        SetText(
            preparedPopsText,
            $"Arrived {productionData.WorkerRoleNamePlural}: " +
            $"{_building.PreparedPops}/{shift.DesiredPops}" +
            $"   •   incoming {_building.PreparedIncomingPops}");
    }

    private void RefreshCurrentShift(
        ResourceProductionBuildingData productionData)
    {
        FactoryShift shift = _building.CurrentFactoryShift;
        if (shift == null)
        {
            SetText(
                currentShiftStateText,
                _building.IsFactoryOutputStorageBlocked
                    ? "Output storage full — next shift blocked"
                    : "No shift currently running");
            SetText(
                currentWorkersText,
                $"Working {productionData.WorkerRoleNamePlural}: --");
            SetText(currentEfficiencyText, string.Empty);
            SetText(
                workerEfficiencyText,
                $"Efficiency ({productionData.WorkerRoleNamePlural}): --");
            SetText(shiftProgressText, "Shift progress: --");
            RebuildCurrentShiftResources(productionData, null);
            return;
        }

        SetText(
            currentShiftStateText,
            $"Current shift  {FormatHour(shift.StartHour)}–" +
            $"{FormatHour(shift.GetEndHour(_building.FactoryShiftDurationHours))}");
        string lateSuffix = _building.CurrentLatePops > 0
            ? $"   •   {_building.CurrentLatePops} available next hour"
            : string.Empty;
        SetText(
            currentWorkersText,
            $"Working {productionData.WorkerRoleNamePlural}: " +
            $"{_building.CurrentWorkingPops}/{shift.DesiredPops}" +
            lateSuffix);
        SetText(currentEfficiencyText, string.Empty);
        SetText(
            workerEfficiencyText,
            $"Efficiency ({productionData.WorkerRoleNamePlural}): " +
            $"{_building.CurrentFactoryEfficiency:P0}");
        SetText(
            shiftProgressText,
            $"Shift progress: hour {_building.CurrentShiftElapsedHours + 1}/" +
            $"{_building.FactoryShiftDurationHours}");

        RebuildCurrentShiftResources(productionData, shift);
    }

    private void RebuildCurrentShiftResources(
        ResourceProductionBuildingData productionData,
        FactoryShift shift)
    {
        bool hasInputs = HasResourcesInDirection(
            productionData,
            ProductionResourceDirection.Input);
        if (_currentInputsLabel == null && currentInputRowsRoot != null)
        {
            _currentInputsLabel = currentInputRowsRoot.parent
                ?.Find("InputsLabel")
                ?.GetComponent<TMP_Text>();
        }
        if (_currentInputsLabel != null)
        {
            _currentInputsLabel.gameObject.SetActive(hasInputs);
        }
        if (currentInputRowsRoot != null)
        {
            currentInputRowsRoot.gameObject.SetActive(hasInputs);
        }

        IReadOnlyList<ResourceAmount> remaining = shift != null
            ? _building.CurrentShiftInputsRemaining
            : null;
        IReadOnlyList<ResourceAmount> outputs = shift != null
            ? _building.CurrentShiftPendingOutputs
            : null;
        if (remaining != null)
        {
            for (int i = 0; i < remaining.Count; i++)
                SpawnRow(currentInputRowsRoot, remaining[i].resourceType, $"{remaining[i].amount} unprocessed");
        }
        if (outputs != null)
            for (int i = 0; i < outputs.Count; i++)
                SpawnRow(pendingOutputRowsRoot, outputs[i].resourceType, $"{outputs[i].amount} pending");
    }

    private static bool HasResourcesInDirection(
        ResourceProductionBuildingData productionData,
        ProductionResourceDirection direction)
    {
        if (productionData?.resources == null) return false;

        for (int i = 0; i < productionData.resources.Count; i++)
        {
            if (productionData.resources[i].direction == direction)
            {
                return true;
            }
        }

        return false;
    }

    private void SpawnRow(RectTransform parent, ResourceType resource, string amount) =>
        SpawnRow(parent, ResourceCatalog.GetDisplayName(resource), amount, ResourceCatalog.GetIcon(resource));

    private void SpawnRow(RectTransform parent, string label, string amount, Sprite icon = null)
    {
        if (parent == null || resourceRowPrefab == null) return;

        FactoryStatusResourceRowView row = Instantiate(
            resourceRowPrefab,
            parent,
            false);
        row.gameObject.SetActive(true);
        row.Bind(label, amount, icon);
        _spawnedRows.Add(row);
    }

    private void SpawnHeader(RectTransform parent, string title)
    {
        if (parent == null || resourceRowPrefab == null) return;

        FactoryStatusResourceRowView row = Instantiate(
            resourceRowPrefab,
            parent,
            false);
        row.gameObject.SetActive(true);
        row.BindHeader(title);
        _spawnedRows.Add(row);
    }

    private void ClearRows()
    {
        for (int i = 0; i < _spawnedRows.Count; i++)
        {
            if (_spawnedRows[i] != null)
            {
                // Destroy is deferred until the end of the frame. Disable the
                // old row immediately so it cannot affect this frame's layout.
                _spawnedRows[i].gameObject.SetActive(false);
                Destroy(_spawnedRows[i].gameObject);
            }
        }
        _spawnedRows.Clear();
    }

    private void RebuildLayout()
    {
        Canvas.ForceUpdateCanvases();
        RebuildRoot(throughputRowsRoot);
        RebuildRoot(storageRowsRoot);
        RebuildRoot(currentInputRowsRoot);
        RebuildRoot(pendingOutputRowsRoot);
        if (transform is RectTransform rect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        }
    }

    private static void RebuildRoot(RectTransform root)
    {
        if (root != null) LayoutRebuilder.ForceRebuildLayoutImmediate(root);
    }

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null) text.text = value;
    }

    private static string FormatHour(int hour)
    {
        return $"{FactoryShift.WrapHour(hour):00}:00";
    }

}
