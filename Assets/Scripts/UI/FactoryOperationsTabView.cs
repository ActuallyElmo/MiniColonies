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
            SetText(workerEfficiencyText, "Worker efficiency: --");
            SetText(shiftProgressText, "Shift progress: --");
            return;
        }

        RebuildPerformance(productionData);
        RebuildStorage(productionData);
        RefreshPreparation();
        RefreshCurrentShift(productionData);
        RebuildLayout();
    }

    private void RebuildStorage(ResourceProductionBuildingData productionData)
    {
        if (productionData.resources == null) return;

        RebuildStorageDirection(
            productionData,
            ProductionResourceDirection.Input,
            "Incoming Resources");
        RebuildStorageDirection(
            productionData,
            ProductionResourceDirection.Output,
            "Outgoing Resources");
    }

    private void RebuildStorageDirection(
        ResourceProductionBuildingData productionData,
        ProductionResourceDirection direction,
        string header)
    {
        var displayed = new HashSet<ResourceType>();
        bool headerSpawned = false;
        for (int i = 0; i < productionData.resources.Count; i++)
        {
            ProductionResourceEntry resource = productionData.resources[i];
            if (resource.direction != direction) continue;
            if (!displayed.Add(resource.resourceType)) continue;

            if (!headerSpawned)
            {
                SpawnHeader(storageRowsRoot, header);
                headerSpawned = true;
            }
            SpawnRow(
                storageRowsRoot,
                Nicify(resource.resourceType.ToString()),
                $"{_building.GetFactoryStoredAmount(resource.resourceType)}" +
                $" / {_building.GetFactoryResourceCapacity(resource.resourceType)}");
        }
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
            float throughput = _building
                .GetFactoryResourceThroughputPerWorkerPerHour(
                    resource.resourceType,
                    resource.direction);
            SpawnRow(
                throughputRowsRoot,
                Nicify(resource.resourceType.ToString()),
                $"{throughput:0.####} / worker / hr");
        }
    }

    private void RefreshPreparation()
    {
        FactoryShift shift = _building.PreparingFactoryShift;
        if (shift == null)
        {
            SetText(preparationStateText, "No shift is being prepared");
            SetText(preparedPopsText, "Pops: --");
            return;
        }

        SetText(
            preparationStateText,
            $"Incoming shift  {FormatHour(shift.StartHour)}–" +
            $"{FormatHour(shift.GetEndHour(_building.FactoryShiftDurationHours))}");
        SetText(
            preparedPopsText,
            $"Pops arrived {_building.PreparedPops}/{shift.DesiredPops}" +
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
            SetText(currentWorkersText, "Workers: --");
            SetText(currentEfficiencyText, string.Empty);
            SetText(workerEfficiencyText, "Worker efficiency: --");
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
            $"Working {_building.CurrentWorkingPops}/{shift.DesiredPops}" +
            lateSuffix);
        SetText(currentEfficiencyText, string.Empty);
        SetText(
            workerEfficiencyText,
            $"Worker efficiency: {_building.CurrentFactoryEfficiency:P0}");
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

        if (productionData.resources == null) return;

        IReadOnlyList<ResourceAmount> remaining = shift != null
            ? _building.CurrentShiftInputsRemaining
            : null;
        IReadOnlyList<ResourceAmount> outputs = shift != null
            ? _building.CurrentShiftPendingOutputs
            : null;
        var displayedInputs = new HashSet<ResourceType>();
        var displayedOutputs = new HashSet<ResourceType>();

        for (int i = 0; i < productionData.resources.Count; i++)
        {
            ProductionResourceEntry resource = productionData.resources[i];
            bool isInput;
            if (resource.direction == ProductionResourceDirection.Input)
            {
                isInput = true;
            }
            else if (resource.direction == ProductionResourceDirection.Output)
            {
                isInput = false;
            }
            else
            {
                continue;
            }

            HashSet<ResourceType> displayed = isInput
                ? displayedInputs
                : displayedOutputs;
            if (!displayed.Add(resource.resourceType)) continue;

            IReadOnlyList<ResourceAmount> amounts = isInput
                ? remaining
                : outputs;
            string amount = shift == null
                ? "-"
                : isInput
                    ? $"{GetAmount(amounts, resource.resourceType)} unprocessed"
                    : $"{GetAmount(amounts, resource.resourceType)} pending";
            SpawnRow(
                isInput ? currentInputRowsRoot : pendingOutputRowsRoot,
                Nicify(resource.resourceType.ToString()),
                amount);
        }
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

    private static int GetAmount(
        IReadOnlyList<ResourceAmount> resources,
        ResourceType resourceType)
    {
        if (resources == null) return 0;
        for (int i = 0; i < resources.Count; i++)
        {
            if (resources[i].resourceType == resourceType)
            {
                return resources[i].amount;
            }
        }

        return 0;
    }

    private void SpawnRow(RectTransform parent, string label, string amount)
    {
        if (parent == null || resourceRowPrefab == null) return;

        FactoryStatusResourceRowView row = Instantiate(
            resourceRowPrefab,
            parent,
            false);
        row.gameObject.SetActive(true);
        row.Bind(label, amount);
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

    private static string Nicify(string name)
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
