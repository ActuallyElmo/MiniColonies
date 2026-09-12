using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class FactoryScheduleTests
{
    private GameObject _buildingObject;
    private GameObject _timeObject;
    private Building _building;
    private ResourceProductionBuildingData _buildingData;
    private readonly List<Object> _temporaryObjects = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        _buildingData =
            ScriptableObject.CreateInstance<ResourceProductionBuildingData>();
        _buildingData.popRequiredForMaxEfficiency = 12;
        _buildingData.resources = new List<ProductionResourceEntry>
        {
            new ProductionResourceEntry
            {
                resourceType = ResourceType.IronOre,
                storageCapacity = 25,
                amountAtMaximumEfficiency = 4,
                direction = ProductionResourceDirection.Input
            },
            new ProductionResourceEntry
            {
                resourceType = ResourceType.Wood,
                storageCapacity = 20,
                amountAtMaximumEfficiency = 2,
                direction = ProductionResourceDirection.Input
            },
            new ProductionResourceEntry
            {
                resourceType = ResourceType.Steel,
                storageCapacity = 30,
                amountAtMaximumEfficiency = 3,
                direction = ProductionResourceDirection.Output
            }
        };

        _buildingObject = new GameObject("FactoryScheduleTests");
        _building = _buildingObject.AddComponent<Building>();
        _building.Initialize(
            _buildingData,
            Vector2Int.zero,
            0,
            new List<BuildingTile>());
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < _temporaryObjects.Count; i++)
        {
            if (_temporaryObjects[i] != null)
            {
                Object.DestroyImmediate(_temporaryObjects[i]);
            }
        }
        _temporaryObjects.Clear();
        Object.DestroyImmediate(_buildingObject);
        if (_timeObject != null)
        {
            Object.DestroyImmediate(_timeObject);
        }
        Object.DestroyImmediate(_buildingData);
    }

    [Test]
    public void NewFactoryStartsOpenWithNoShifts()
    {
        Assert.IsTrue(_building.IsFactoryOpen);
        Assert.AreEqual(8, _building.FactoryShiftDurationHours);
        Assert.IsEmpty(_building.FactoryShifts);
    }

    [Test]
    public void NewFactoryStartsWithInputStorageFullAndOutputStorageEmpty()
    {
        Assert.AreEqual(
            25,
            _building.GetFactoryStoredAmount(ResourceType.IronOre));
        Assert.AreEqual(
            20,
            _building.GetFactoryStoredAmount(ResourceType.Wood));
        Assert.AreEqual(
            0,
            _building.GetFactoryStoredAmount(ResourceType.Steel));
    }

    [Test]
    public void CompletedShiftOverfillsOutputAndBlocksTheNextShift()
    {
        _buildingData.popRequiredForMaxEfficiency = 0;
        _buildingData.resources = new List<ProductionResourceEntry>
        {
            new ProductionResourceEntry
            {
                resourceType = ResourceType.Steel,
                storageCapacity = 2,
                amountAtMaximumEfficiency = 3,
                direction = ProductionResourceDirection.Output
            }
        };

        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift first));
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift second));
        Assert.IsTrue(_building.SetFactoryShiftActive(first, true));
        Assert.IsTrue(_building.SetFactoryShiftActive(second, true));

        _timeObject = new GameObject("FactoryRuntimeTestClock");
        GameTimeManager time = _timeObject.AddComponent<GameTimeManager>();
        _building.Initialize(
            _buildingData,
            Vector2Int.zero,
            0,
            new List<BuildingTile>());

        Assert.AreSame(first, _building.CurrentFactoryShift);
        for (int hour = 0;
             hour < _building.FactoryShiftDurationHours;
             hour++)
        {
            time.AdvanceTime(time.RealSecondsPerGameHour);
        }

        Assert.AreEqual(8, time.CurrentHour);
        Assert.AreEqual(3, _building.GetFactoryStoredAmount(ResourceType.Steel));
        Assert.IsNull(_building.CurrentFactoryShift);

        Assert.IsFalse(_building.TryStoreFactoryResource(
            ResourceType.Steel,
            1,
            out int additionallyStored));
        Assert.AreEqual(0, additionallyStored);
    }

    [Test]
    public void PreparingShiftOnlyRequestsItsStorageShortfall()
    {
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift shift));
        Assert.IsTrue(_building.TrySetFactoryShiftStartHour(shift, 1));
        Assert.IsTrue(_building.SetFactoryShiftDesiredResource(
            shift,
            ResourceType.IronOre,
            4));
        Assert.IsTrue(_building.SetFactoryShiftActive(shift, true));

        _timeObject = new GameObject("FactoryDemandTestClock");
        _timeObject.AddComponent<GameTimeManager>();
        _building.Initialize(
            _buildingData,
            Vector2Int.zero,
            0,
            new List<BuildingTile>());

        Assert.AreSame(shift, _building.PreparingFactoryShift);
        Assert.AreEqual(
            0,
            _building.GetRemainingFactoryInputDemand(
                ResourceType.IronOre));

        Assert.IsTrue(_building.TryTakeFactoryResource(
            ResourceType.IronOre,
            23,
            out int taken));
        Assert.AreEqual(23, taken);
        Assert.AreEqual(
            2,
            _building.GetRemainingFactoryInputDemand(
                ResourceType.IronOre));

        Assert.IsTrue(_building.TryStoreFactoryResource(
            ResourceType.IronOre,
            2,
            out int stored));
        Assert.AreEqual(2, stored);
        Assert.AreEqual(
            0,
            _building.GetRemainingFactoryInputDemand(
                ResourceType.IronOre));

        var demands = new List<ResourceAmount>();
        _building.GetRemainingFactoryInputDemands(demands);
        Assert.IsEmpty(demands);
    }

    [Test]
    public void ShiftProcessesStoredInputsWithoutPreparationDemand()
    {
        _buildingData.popRequiredForMaxEfficiency = 0;
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift shift));
        Assert.AreEqual(
            0,
            shift.GetDesiredResourceAmount(ResourceType.IronOre));
        Assert.AreEqual(
            0,
            shift.GetDesiredResourceAmount(ResourceType.Wood));
        Assert.IsTrue(_building.SetFactoryShiftActive(shift, true));

        _timeObject = new GameObject("FactoryStoredInputTestClock");
        _timeObject.AddComponent<GameTimeManager>();
        _building.Initialize(
            _buildingData,
            Vector2Int.zero,
            0,
            new List<BuildingTile>());

        Assert.AreSame(shift, _building.CurrentFactoryShift);
        Assert.AreEqual(
            21,
            _building.GetFactoryStoredAmount(ResourceType.IronOre));
        Assert.AreEqual(
            18,
            _building.GetFactoryStoredAmount(ResourceType.Wood));
    }

    [Test]
    public void AddedShiftUsesFactoryDurationAndDynamicRequirements()
    {
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift shift));

        Assert.AreEqual(0, shift.StartHour);
        Assert.IsFalse(shift.IsActive);
        Assert.AreEqual(8, shift.GetEndHour(
            _building.FactoryShiftDurationHours));
        Assert.AreEqual(1, shift.PreparationHours);
        Assert.AreEqual(0, shift.DesiredPops);
        Assert.AreEqual(2, shift.DesiredResources.Count);
        Assert.AreEqual(0,
            shift.GetDesiredResourceAmount(ResourceType.IronOre));
        Assert.AreEqual(0,
            shift.GetDesiredResourceAmount(ResourceType.Steel));
    }

    [Test]
    public void DuplicatedShiftCopiesRequirementsButStartsInactive()
    {
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift source));
        Assert.IsTrue(_building.SetFactoryShiftPreparationHours(source, 4));
        Assert.IsTrue(_building.SetFactoryShiftDesiredPops(source, 7));
        Assert.IsTrue(_building.SetFactoryShiftDesiredResource(
            source,
            ResourceType.IronOre,
            13));
        Assert.IsTrue(_building.SetFactoryShiftDesiredResource(
            source,
            ResourceType.Wood,
            9));
        Assert.IsTrue(_building.SetFactoryShiftActive(source, true));

        Assert.IsTrue(_building.TryDuplicateFactoryShift(
            source,
            out FactoryShift duplicate));

        Assert.AreEqual(8, duplicate.StartHour);
        Assert.IsFalse(duplicate.IsActive);
        Assert.AreEqual(source.PreparationHours, duplicate.PreparationHours);
        Assert.AreEqual(source.DesiredPops, duplicate.DesiredPops);
        Assert.AreEqual(
            source.GetDesiredResourceAmount(ResourceType.IronOre),
            duplicate.GetDesiredResourceAmount(ResourceType.IronOre));
        Assert.AreEqual(
            source.GetDesiredResourceAmount(ResourceType.Wood),
            duplicate.GetDesiredResourceAmount(ResourceType.Wood));
    }

    [Test]
    public void DuplicateFailsWithoutChangingAFullSchedule()
    {
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift source));
        Assert.IsTrue(_building.TryAddFactoryShift(out _));
        Assert.IsTrue(_building.TryAddFactoryShift(out _));

        Assert.IsFalse(_building.TryDuplicateFactoryShift(source, out _));
        Assert.AreEqual(3, _building.FactoryShifts.Count);
    }

    [Test]
    public void PreparationIsAClampedDurationBeforeTheShift()
    {
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift shift));

        Assert.IsTrue(_building.SetFactoryShiftPreparationHours(shift, 0));
        Assert.AreEqual(1, shift.PreparationHours);
        Assert.AreEqual(23, shift.GetPreparationStartHour());

        Assert.IsTrue(_building.SetFactoryShiftPreparationHours(shift, 99));
        Assert.AreEqual(
            _building.FactoryShiftDurationHours,
            shift.PreparationHours);
        Assert.AreEqual(16, shift.GetPreparationStartHour());

        Assert.IsTrue(_building.TrySetFactoryShiftStartHour(shift, 12));
        Assert.AreEqual(4, shift.GetPreparationStartHour());
    }

    [Test]
    public void ShiftTimesWrapAtMidnight()
    {
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift shift));
        Assert.IsTrue(_building.TrySetFactoryShiftStartHour(shift, 20));

        Assert.AreEqual(4, shift.GetEndHour(8));
        Assert.IsTrue(_building.IsFactoryScheduledAtHour(23));
        Assert.IsTrue(_building.IsFactoryScheduledAtHour(2));
        Assert.IsFalse(_building.IsFactoryScheduledAtHour(4));
    }

    [Test]
    public void ShiftsCannotOverlap()
    {
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift first));
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift second));

        Assert.AreEqual(0, first.StartHour);
        Assert.AreEqual(8, second.StartHour);
        Assert.IsFalse(_building.TrySetFactoryShiftStartHour(second, 7));
        Assert.AreEqual(8, second.StartHour);
    }

    [Test]
    public void DragMovementPushesEveryCollidingShiftAsAChain()
    {
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift first));
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift second));
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift third));

        Assert.IsTrue(_building.TryMoveFactoryShiftChain(first, 1));

        Assert.AreEqual(1, first.StartHour);
        Assert.AreEqual(9, second.StartHour);
        Assert.AreEqual(17, third.StartHour);
        Assert.AreEqual(0, first.GetPreparationStartHour());
    }

    [Test]
    public void BackwardDragPushesAFullScheduleAcrossMidnight()
    {
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift first));
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift second));
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift third));

        Assert.IsTrue(_building.TryMoveFactoryShiftChain(first, -1));

        Assert.AreEqual(23, first.StartHour);
        Assert.AreEqual(7, second.StartHour);
        Assert.AreEqual(15, third.StartHour);
    }

    [Test]
    public void ActiveShiftCannotBeMoved()
    {
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift shift));
        Assert.IsTrue(_building.SetFactoryShiftActive(shift, true));

        Assert.IsFalse(_building.TryMoveFactoryShiftChain(shift, 1));
        Assert.IsFalse(_building.TrySetFactoryShiftStartHour(shift, 1));
        Assert.AreEqual(0, shift.StartHour);
    }

    [Test]
    public void InactiveShiftCannotPushAnActiveShift()
    {
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift first));
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift second));
        Assert.IsTrue(_building.SetFactoryShiftActive(second, true));

        Assert.IsFalse(_building.TryMoveFactoryShiftChain(first, 1));

        Assert.AreEqual(0, first.StartHour);
        Assert.AreEqual(8, second.StartHour);
    }

    [Test]
    public void ActiveShiftRequirementsRemainEditable()
    {
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift shift));
        Assert.IsTrue(_building.SetFactoryShiftActive(shift, true));

        Assert.IsTrue(_building.SetFactoryShiftPreparationHours(shift, 2));
        Assert.IsTrue(_building.SetFactoryShiftDesiredPops(shift, 6));
        Assert.IsTrue(_building.SetFactoryShiftDesiredResource(
            shift,
            ResourceType.IronOre,
            3));

        Assert.AreEqual(2, shift.PreparationHours);
        Assert.AreEqual(6, shift.DesiredPops);
        Assert.AreEqual(
            3,
            shift.GetDesiredResourceAmount(ResourceType.IronOre));
    }

    [Test]
    public void DragOnlyPushesAShiftAfterClosingTheGap()
    {
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift first));
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift second));
        Assert.IsTrue(_building.TrySetFactoryShiftStartHour(second, 10));

        Assert.IsTrue(_building.TryMoveFactoryShiftChain(first, 3));

        Assert.AreEqual(3, first.StartHour);
        Assert.AreEqual(11, second.StartHour);
    }

    [Test]
    public void ShiftDurationComesFromTheBuildingType()
    {
        _buildingData.shiftDuration = FactoryShiftDuration.FourHours;

        Assert.AreEqual(4, _building.FactoryShiftDurationHours);

        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift shift));
        Assert.AreEqual(4, shift.GetEndHour(
            _building.FactoryShiftDurationHours));
    }

    [Test]
    public void DesiredAmountsAreNonNegativeAndWorkersRespectCapacity()
    {
        _building.TryAddFactoryShift(out FactoryShift shift);

        Assert.IsTrue(_building.SetFactoryShiftDesiredPops(shift, 99));
        Assert.AreEqual(12, shift.DesiredPops);
        Assert.IsTrue(_building.SetFactoryShiftDesiredPops(shift, -1));
        Assert.AreEqual(0, shift.DesiredPops);

        Assert.IsTrue(_building.SetFactoryShiftDesiredResource(
            shift,
            ResourceType.IronOre,
            -5));
        Assert.AreEqual(0,
            shift.GetDesiredResourceAmount(ResourceType.IronOre));
        Assert.IsFalse(_building.SetFactoryShiftDesiredResource(
            shift,
            ResourceType.CopperOre,
            1));
        Assert.IsFalse(_building.SetFactoryShiftDesiredResource(
            shift,
            ResourceType.Steel,
            1));

        Assert.IsTrue(_building.SetFactoryShiftDesiredResource(
            shift,
            ResourceType.IronOre,
            999));
        Assert.AreEqual(25,
            shift.GetDesiredResourceAmount(ResourceType.IronOre));
    }

    [Test]
    public void ShiftEfficiencyAndRequiredInputsScaleWithPreparedPops()
    {
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift shift));
        Assert.IsTrue(_building.SetFactoryShiftDesiredPops(shift, 6));

        Assert.AreEqual(0.5f, _building.GetFactoryShiftEfficiency(shift));
        Assert.AreEqual(
            2,
            _building.GetFactoryShiftRequiredResourceAmount(
                shift,
                ResourceType.IronOre));
        Assert.AreEqual(
            1,
            _building.GetFactoryShiftRequiredResourceAmount(
                shift,
                ResourceType.Wood));
        Assert.AreEqual(
            0,
            _building.GetFactoryShiftRequiredResourceAmount(
                shift,
                ResourceType.Steel));
    }

    [Test]
    public void FactoryReportsPerWorkerPerHourResourceThroughput()
    {
        Assert.AreEqual(
            4f / (12f * 8f),
            _building.GetFactoryResourceThroughputPerWorkerPerHour(
                ResourceType.IronOre,
                ProductionResourceDirection.Input),
            0.0001f);
        Assert.AreEqual(
            3f / (12f * 8f),
            _building.GetFactoryResourceThroughputPerWorkerPerHour(
                ResourceType.Steel,
                ProductionResourceDirection.Output),
            0.0001f);
    }

    [Test]
    public void LateWorkersOnlyIncreaseProductionForFutureHours()
    {
        _buildingData.popRequiredForMaxEfficiency = 10;
        _buildingData.resources = new List<ProductionResourceEntry>
        {
            new ProductionResourceEntry
            {
                resourceType = ResourceType.Steel,
                storageCapacity = 1000,
                amountAtMaximumEfficiency = 80,
                direction = ProductionResourceDirection.Output
            }
        };

        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift shift));
        Assert.IsTrue(_building.TrySetFactoryShiftStartHour(shift, 1));
        Assert.IsTrue(_building.SetFactoryShiftDesiredPops(shift, 10));
        Assert.IsTrue(_building.SetFactoryShiftActive(shift, true));

        _timeObject = new GameObject("FactoryHourlyEfficiencyTestClock");
        GameTimeManager time = _timeObject.AddComponent<GameTimeManager>();
        _building.Initialize(
            _buildingData,
            Vector2Int.zero,
            0,
            new List<BuildingTile>());

        VehicleAI firstVehicle = CreateTestPopVehicle("FirstWorkers");
        VehicleAI lateVehicle = CreateTestPopVehicle("LateWorkers");
        Assert.IsTrue(_building.TryReserveIncomingPops(
            firstVehicle,
            5,
            out _));
        Assert.IsTrue(_building.TryReserveIncomingPops(
            lateVehicle,
            5,
            out _));
        _building.MarkIncomingPopsDelivered(firstVehicle);

        time.AdvanceTime(time.RealSecondsPerGameHour);
        Assert.AreEqual(5, _building.CurrentWorkingPops);
        time.AdvanceTime(time.RealSecondsPerGameHour);

        _building.MarkIncomingPopsDelivered(lateVehicle);
        time.AdvanceTime(time.RealSecondsPerGameHour);
        Assert.AreEqual(10, _building.CurrentWorkingPops);
        time.AdvanceTime(time.RealSecondsPerGameHour);

        Assert.AreEqual(
            20,
            GetAmount(
                _building.CurrentShiftPendingOutputs,
                ResourceType.Steel));
    }

    [Test]
    public void ReadingTransportDemandDoesNotPublishRuntimeChanges()
    {
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift shift));
        Assert.IsTrue(_building.TrySetFactoryShiftStartHour(shift, 1));
        Assert.IsTrue(_building.SetFactoryShiftDesiredPops(shift, 5));
        Assert.IsTrue(_building.SetFactoryShiftActive(shift, true));

        _timeObject = new GameObject("FactoryDemandReadTestClock");
        _timeObject.AddComponent<GameTimeManager>();
        _building.Initialize(
            _buildingData,
            Vector2Int.zero,
            0,
            new List<BuildingTile>());

        int runtimeChangeCount = 0;
        _building.FactoryRuntimeChanged += _ => runtimeChangeCount++;

        _building.GetRemainingFactoryInputDemand(ResourceType.IronOre);
        _building.GetRemainingFactoryInputDemands(new List<ResourceAmount>());
        _building.GetRemainingUnassignedPopCapacity();

        Assert.AreEqual(0, runtimeChangeCount);
    }

    [Test]
    public void FirstWorkerArrivalPublishesOneRuntimeChange()
    {
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift shift));
        Assert.IsTrue(_building.TrySetFactoryShiftStartHour(shift, 1));
        Assert.IsTrue(_building.SetFactoryShiftDesiredPops(shift, 5));
        Assert.IsTrue(_building.SetFactoryShiftActive(shift, true));

        _timeObject = new GameObject("FirstWorkerArrivalTestClock");
        _timeObject.AddComponent<GameTimeManager>();
        _building.Initialize(
            _buildingData,
            Vector2Int.zero,
            0,
            new List<BuildingTile>());

        VehicleAI vehicle = CreateTestPopVehicle("FirstArrivalVehicle");
        Assert.IsTrue(_building.TryReserveIncomingPops(vehicle, 5, out _));

        int runtimeChangeCount = 0;
        _building.FactoryRuntimeChanged += _ => runtimeChangeCount++;
        _building.MarkIncomingPopsDelivered(vehicle);

        Assert.AreEqual(1, runtimeChangeCount);
    }

    private VehicleAI CreateTestPopVehicle(string objectName)
    {
        var vehicleObject = new GameObject(objectName);
        _temporaryObjects.Add(vehicleObject);
        return vehicleObject.AddComponent<VehicleAI>();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ClosingHousingImmediatelyReleasesWorkersAndParksVehicles(bool delivered)
    {
        Assert.IsTrue(_building.TryAddFactoryShift(out FactoryShift shift));
        Assert.IsTrue(_building.TrySetFactoryShiftStartHour(shift, 1));
        Assert.IsTrue(_building.SetFactoryShiftDesiredPops(shift, 5));
        Assert.IsTrue(_building.SetFactoryShiftActive(shift, true));

        var housingData = ScriptableObject.CreateInstance<PopHousingBuildingData>();
        _temporaryObjects.Add(housingData);
        housingData.autoFillVehicleSpaceForTesting = false;
        housingData.popTransportVehiclesStorageSpace = 1;
        var popData = ScriptableObject.CreateInstance<PopTransportVehicleData>();
        _temporaryObjects.Add(popData);
        popData.vehicleSpaceUsed = 1;
        popData.amountOfPopsTransported = 5;
        var prefab = new GameObject("HousingRecallVehiclePrefab");
        prefab.SetActive(false);
        prefab.AddComponent<VehicleAI>();
        _temporaryObjects.Add(prefab);
        popData.vehiclePrefab = prefab;
        housingData.typesOfPopTransportVehiclesAllowed.Add(popData);
        var houseObject = new GameObject("HousingRecallTest");
        houseObject.SetActive(false);
        _temporaryObjects.Add(houseObject);
        Building house = houseObject.AddComponent<Building>();
        house.Initialize(housingData, Vector2Int.zero, 0, new List<BuildingTile>());
        Assert.IsTrue(house.TryAssignVehicle(popData, out VehicleAI vehicle));
        Assert.IsTrue(_building.TryReserveIncomingPops(vehicle, 5, out int reserved));
        vehicle.AssignPopDelivery(_building, reserved);
        vehicle.targetBuilding = _building;
        vehicle.currentState = delivered ? VehicleAI.VehicleState.AtDestination : VehicleAI.VehicleState.Outbound;
        vehicle.gameObject.SetActive(true);
        if (delivered) _building.MarkIncomingPopsDelivered(vehicle);

        Assert.IsTrue(house.SetHousingOpen(false));

        Assert.IsFalse(vehicle.gameObject.activeSelf);
        Assert.AreEqual(VehicleAI.VehicleState.Idle, vehicle.currentState);
        Assert.AreEqual(0, vehicle.AssignedPopCount);
        Assert.AreEqual(0, _building.GetAssignedPopCount());
        Assert.IsNull(vehicle.targetBuilding);
        Assert.IsTrue(vehicle.CanReceivePopTransportAssignment);
        Assert.IsFalse(house.TryDispatchPopTransportVehicle(vehicle));
        Assert.AreEqual(1, house.GetOwnedVehicleCount());
        Assert.IsTrue(house.SetHousingOpen(true));
        Assert.IsTrue(house.IsHousingOpen);
    }

    [Test]
    public void ClosedAppearancePreservesSharedMaterialAndRestoresRendererOverrides()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        Assert.IsNotNull(shader);
        var material = new Material(shader);
        _temporaryObjects.Add(material);
        material.SetColor("_BaseColor", Color.white);
        var model = new GameObject("ClosedAppearanceModel");
        model.transform.SetParent(_building.transform);
        var renderer = model.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        var original = new MaterialPropertyBlock();
        Color openColor = new Color(0.8f, 0.6f, 0.4f, 1f);
        original.SetColor("_BaseColor", openColor);
        renderer.SetPropertyBlock(original, 0);

        Assert.IsTrue(_building.SetFactoryOpen(false));
        var actual = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(actual, 0);
        Assert.Less(actual.GetColor("_BaseColor").r, openColor.r);
        Assert.AreEqual(Color.white, material.GetColor("_BaseColor"));

        Assert.IsTrue(_building.SetFactoryOpen(true));
        renderer.GetPropertyBlock(actual, 0);
        Assert.AreEqual(openColor, actual.GetColor("_BaseColor"));
    }

    private static int GetAmount(
        IReadOnlyList<ResourceAmount> resources,
        ResourceType resourceType)
    {
        for (int i = 0; i < resources.Count; i++)
        {
            if (resources[i].resourceType == resourceType)
            {
                return resources[i].amount;
            }
        }

        return 0;
    }
}
