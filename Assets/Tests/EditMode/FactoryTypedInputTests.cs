using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class FactoryTypedInputTests
{
    private readonly List<Object> temporary = new List<Object>();
    private static readonly FieldInfo CurrentCatalog = typeof(ResourceCatalog).GetField("current", BindingFlags.Static | BindingFlags.NonPublic);
    private ResourceCatalog previousCatalog;
    private ResourceCatalog catalog;
    private ResourceProductionBuildingData data;
    private Building factory;

    [SetUp]
    public void SetUp()
    {
        previousCatalog = (ResourceCatalog)CurrentCatalog.GetValue(null);
        catalog = Track(ScriptableObject.CreateInstance<ResourceCatalog>());
        ResourceCatalogDefaults.PopulateEmpty(catalog);
        CurrentCatalog.SetValue(null, catalog);
        data = Track(ScriptableObject.CreateInstance<ResourceProductionBuildingData>());
        data.popRequiredForMaxEfficiency = 0;
        data.resources = new List<ProductionResourceEntry>
        {
            Input("metal", "metal", 8), Input("fuel", "fuel", 8),
            new ProductionResourceEntry
            {
                direction = ProductionResourceDirection.Output, resourceType = ResourceType.Steel,
                storageCapacity = 2000, amountAtMaximumEfficiency = 8
            }
        };
        factory = Track(new GameObject("Typed factory")).AddComponent<Building>();
        factory.Initialize(data, Vector2Int.zero, 0, new List<BuildingTile>());
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = temporary.Count - 1; i >= 0; i--)
            if (temporary[i] != null) Object.DestroyImmediate(temporary[i]);
        temporary.Clear();
        CurrentCatalog.SetValue(null, previousCatalog);
    }

    [Test]
    public void TypedSlotsRequireEveryTypeAndAnEmptyOrUnknownFilterCannotActivate()
    {
        ProductionResourceEntry entry = Input("metal", "fuel", 8);
        entry.requiredTypes.Add("liquid");
        data.resources[0] = entry;
        Assert.AreEqual("Fuel + Liquid", data.GetInputName(0));
        var allowed = new List<ResourceType>();
        data.GetAllowedInputResources(0, catalog, allowed);
        CollectionAssert.AreEquivalent(new[] { ResourceType.Oil, ResourceType.Gasoline, ResourceType.Diesel }, allowed);
        Assert.IsTrue(factory.TryAddFactoryShift(out FactoryShift shift));
        Assert.IsFalse(factory.TrySetFactoryShiftInputResource(shift, "metal", ResourceType.Coal));
        Assert.IsFalse(factory.TrySetFactoryShiftInputResource(shift, "metal", ResourceType.Water));
        Assert.IsTrue(factory.TrySetFactoryShiftInputResource(shift, "metal", ResourceType.Oil));

        entry.requiredTypes.Clear();
        data.resources[0] = entry;
        Assert.IsFalse(factory.SetFactoryShiftActive(shift, true));
        Assert.IsFalse(factory.HasValidFactoryShiftInputs(shift));
        entry.requiredTypes.Add("unknown-type");
        data.GetAllowedInputResources(0, catalog, allowed);
        Assert.IsEmpty(allowed);
        Assert.IsFalse(factory.TrySetFactoryShiftInputResource(shift, "metal", ResourceType.Oil));
    }

    [Test]
    public void ShiftChoicesStayIndependentWhileAnActiveShiftIsWaiting()
    {
        FactoryShift first = Shift(ResourceType.Gold, ResourceType.Coal);
        FactoryShift second = Shift(ResourceType.Zinc, ResourceType.Wood);
        Assert.AreEqual("Metal", data.GetInputName(0));
        Assert.AreEqual("Fuel", data.GetInputName(1));
        Assert.AreEqual(8, first.GetDesiredResourceAmount(ResourceType.Gold));
        Assert.AreEqual(0, first.GetDesiredResourceAmount(ResourceType.Zinc));
        Assert.AreEqual(8, second.GetDesiredResourceAmount(ResourceType.Zinc));
        Assert.AreEqual(8, first.GetDesiredResourceAmount(ResourceType.Coal));
        Assert.AreEqual(8, second.GetDesiredResourceAmount(ResourceType.Wood));
        var metals = new List<ResourceType>();
        data.GetAllowedInputResources(0, catalog, metals);
        CollectionAssert.AreEquivalent(catalog.resources.Where(resource => resource.types.Contains("metal")).Select(resource => resource.resource), metals);
        Assert.IsFalse(metals.Contains(ResourceType.Coal));
        Assert.IsTrue(factory.TrySetFactoryShiftStartHour(first, 16));
        Assert.IsTrue(factory.SetFactoryShiftActive(first, true));
        Clock();
        Assert.IsTrue(factory.CanEditFactoryShiftInputs(first));
        Assert.IsTrue(factory.TrySetFactoryShiftInputResource(first, "metal", ResourceType.Copper));
        Assert.IsTrue(first.TryGetInput("metal", out FactoryShiftInput input));
        Assert.AreEqual(ResourceType.Copper, input.resourceType);
        Assert.IsTrue(second.TryGetInput("metal", out FactoryShiftInput secondInput));
        Assert.AreEqual(ResourceType.Zinc, secondInput.resourceType);
        Assert.IsTrue(factory.SetFactoryShiftActive(first, false));
        Assert.IsTrue(factory.TrySetFactoryShiftInputResource(first, "metal", ResourceType.Zinc));
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    [TestCase(4f)]
    public void MaterialChoicesLockAtPreparationAndUnlockAfterTheShiftFinishes(float speed)
    {
        FactoryShift shift = Shift(ResourceType.Gold, ResourceType.Coal);
        Assert.IsTrue(factory.TrySetFactoryShiftStartHour(shift, 12));
        Assert.IsTrue(factory.SetFactoryShiftPreparationHours(shift, 2));
        Assert.IsTrue(factory.SetFactoryShiftActive(shift, true));
        GameTimeManager time = Clock();
        time.SimulationSpeed = speed;
        Assert.IsTrue(factory.CanEditFactoryShiftInputs(shift));
        Assert.IsTrue(factory.TrySetFactoryShiftInputResource(shift, "metal", ResourceType.Zinc));
        Assert.IsFalse(factory.TrySetFactoryShiftInputResource(shift, "metal", ResourceType.Coal));

        time.AdvanceTime(time.RealSecondsPerGameHour * 9 / speed);
        Assert.IsTrue(factory.TrySetFactoryShiftInputResource(shift, "metal", ResourceType.Gold));
        time.AdvanceTime(time.RealSecondsPerGameHour / speed);
        Assert.AreSame(shift, factory.PreparingFactoryShift);
        Assert.IsFalse(factory.CanEditFactoryShiftInputs(shift));
        Assert.IsFalse(factory.TrySetFactoryShiftInputResource(shift, "metal", ResourceType.Zinc));

        time.SimulationSpeed = 0;
        time.AdvanceTime(time.RealSecondsPerGameHour * 4);
        Assert.AreSame(shift, factory.PreparingFactoryShift);
        Assert.IsFalse(factory.CanEditFactoryShiftInputs(shift));
        time.SimulationSpeed = 4;
        time.AdvanceTime(time.RealSecondsPerGameHour * 2 / 4);
        Assert.AreSame(shift, factory.CurrentFactoryShift);
        Assert.IsFalse(factory.CanEditFactoryShiftInputs(shift));
        Assert.IsFalse(factory.TrySetFactoryShiftInputResource(shift, "metal", ResourceType.Zinc));
        Assert.IsTrue(shift.TryGetInput("metal", out FactoryShiftInput input));
        Assert.AreEqual(ResourceType.Gold, input.resourceType);

        time.AdvanceTime(time.RealSecondsPerGameHour * 8 / 4);
        Assert.IsNull(factory.CurrentFactoryShift);
        Assert.IsNull(factory.PreparingFactoryShift);
        Assert.IsTrue(shift.IsActive);
        Assert.IsTrue(factory.CanEditFactoryShiftInputs(shift));
        Assert.IsTrue(factory.TrySetFactoryShiftInputResource(shift, "metal", ResourceType.Zinc));
    }

    [Test]
    public void CapacityIsPerMaterialAndOverlappingTypeLimitsUseTheLargestBin()
    {
        Assert.IsEmpty(factory.FactoryStorage);
        Assert.IsTrue(factory.TryStoreFactoryResource(ResourceType.Gold, 2500, out int gold));
        Assert.AreEqual(2000, gold);
        Assert.IsTrue(factory.TryStoreFactoryResource(ResourceType.Zinc, 2500, out int zinc));
        Assert.AreEqual(2000, zinc);
        Assert.AreEqual(2000, factory.GetFactoryResourceCapacity(ResourceType.Gold));
        Assert.IsFalse(factory.TryStoreFactoryResource(ResourceType.Water, 1, out _));

        ProductionResourceEntry overlap = Input("alloy", "metal", 1);
        overlap.requiredTypes.Add("alloy");
        overlap.storageCapacity = 3000;
        data.resources.Add(overlap);
        Assert.AreEqual(3000, factory.GetFactoryResourceCapacity(ResourceType.Steel));
        Assert.AreEqual(2000, factory.GetFactoryResourceCapacity(ResourceType.Gold));
        Assert.IsFalse(factory.TryStoreFactoryResource(ResourceType.Gold, 1, out _));
    }

    [Test]
    public void SpecificItemInputsCanBeMixedWithPerShiftCategoryInputs()
    {
        ProductionResourceEntry fixedInput = data.resources[0];
        fixedInput.useResourceTypes = false;
        fixedInput.resourceType = ResourceType.Water;
        data.resources[0] = fixedInput;
        var allowed = new List<ResourceType>();
        data.GetAllowedInputResources(0, catalog, allowed);
        CollectionAssert.AreEqual(new[] { ResourceType.Water }, allowed);
        CollectionAssert.AreEqual(new[] { "metal" }, fixedInput.requiredTypes);
        FactoryShift first = Shift(ResourceType.Water, ResourceType.Coal);
        FactoryShift second = Shift(ResourceType.Water, ResourceType.Wood);
        Assert.IsFalse(factory.TrySetFactoryShiftInputResource(first, "metal", ResourceType.Gold));
        Assert.AreEqual(8, first.GetDesiredResourceAmount(ResourceType.Water));
        Assert.AreEqual(8, second.GetDesiredResourceAmount(ResourceType.Water));
        Assert.AreEqual(8, first.GetDesiredResourceAmount(ResourceType.Coal));
        Assert.AreEqual(8, second.GetDesiredResourceAmount(ResourceType.Wood));
        Assert.IsFalse(factory.TryStoreFactoryResource(ResourceType.Gold, 1, out _));
        Store(ResourceType.Water, 8);
        Store(ResourceType.Coal, 8);
        Assert.IsTrue(factory.SetFactoryShiftActive(first, true));
        GameTimeManager time = Clock();
        time.AdvanceTime(time.RealSecondsPerGameHour * 8);
        Assert.AreEqual(8, factory.GetFactoryStoredAmount(ResourceType.Steel));
        Assert.AreEqual(0, factory.GetFactoryStoredAmount(ResourceType.Water));
    }

    [Test]
    public void SwitchingInputModesRetainsBothConfigurationsAndUpdatesInactiveShifts()
    {
        FactoryShift shift = Shift(ResourceType.Gold, ResourceType.Coal);
        ProductionResourceEntry entry = data.resources[0];
        entry.useResourceTypes = false;
        entry.resourceType = ResourceType.Water;
        data.resources[0] = entry;
        Assert.AreEqual(1, factory.FactoryShifts.Count); // Synchronize the authored definition.
        Assert.IsTrue(shift.TryGetInput("metal", out FactoryShiftInput chosen));
        Assert.IsTrue(chosen.hasSelection);
        Assert.AreEqual(ResourceType.Water, chosen.resourceType);
        Assert.IsTrue(factory.HasValidFactoryShiftInputs(shift));
        Assert.IsFalse(factory.TrySetFactoryShiftInputResource(shift, "metal", ResourceType.Gold));

        entry.useResourceTypes = true;
        data.resources[0] = entry;
        Assert.IsTrue(factory.TrySetFactoryShiftInputResource(shift, "metal", ResourceType.Zinc));
        Assert.AreEqual(ResourceType.Water, data.resources[0].resourceType);
        CollectionAssert.AreEqual(new[] { "metal" }, data.resources[0].requiredTypes);
        Assert.AreEqual(8, shift.GetDesiredResourceAmount(ResourceType.Zinc));
    }

    [Test]
    public void StockSurvivesMaterialChoicesAndLaterFilterOrCapacityChanges()
    {
        FactoryShift shift = Shift(ResourceType.Gold, ResourceType.Coal);
        Assert.IsTrue(factory.TryStoreFactoryResource(ResourceType.Gold, 10, out _));
        Assert.IsTrue(factory.TrySetFactoryShiftInputResource(shift, "metal", ResourceType.Zinc));
        Assert.AreEqual(10, factory.GetFactoryStoredAmount(ResourceType.Gold));
        catalog.Find(ResourceType.Gold).types.Clear();
        Assert.AreEqual(0, factory.GetFactoryResourceCapacity(ResourceType.Gold));
        Assert.AreEqual(10, factory.GetFactoryStoredAmount(ResourceType.Gold));
        Assert.IsFalse(factory.TryStoreFactoryResource(ResourceType.Gold, 1, out _));
        Assert.IsTrue(factory.TryTakeFactoryResource(ResourceType.Gold, 10, out int taken));
        Assert.AreEqual(10, taken);
    }

    [Test]
    public void OutputBlockingUsesTheSamePerMaterialCapacityAsStorage()
    {
        ProductionResourceEntry output = data.resources[2];
        output.storageCapacity = 10;
        data.resources[2] = output;
        Store(ResourceType.Steel, 10);
        Assert.AreEqual(2000, factory.GetFactoryResourceCapacity(ResourceType.Steel));
        Assert.IsFalse(factory.IsFactoryOutputStorageBlocked);
        Store(ResourceType.Steel, 1990);
        Assert.IsTrue(factory.IsFactoryOutputStorageBlocked);
    }

    [Test]
    public void FractionalStaffingPreservesResourceRounding()
    {
        data.popRequiredForMaxEfficiency = 12;
        ProductionResourceEntry input = data.resources[0];
        input.amountAtMaximumEfficiency = 3;
        data.resources[0] = input;
        FactoryShift shift = Shift(ResourceType.Gold, ResourceType.Coal);
        Assert.IsTrue(factory.SetFactoryShiftDesiredPops(shift, 4));
        Assert.AreEqual(1, factory.GetFactoryShiftInputRequiredAmount(shift, "metal"));
        Assert.AreEqual(1, factory.GetFactoryShiftRequiredResourceAmount(shift, ResourceType.Gold));
    }

    [Test]
    public void CollectionAndReservationsRequestOnlyThePreparingShiftsChosenMaterials()
    {
        FactoryShift shift = Shift(ResourceType.Gold, ResourceType.Coal);
        Assert.IsTrue(factory.TrySetFactoryShiftStartHour(shift, 1));
        Assert.IsTrue(factory.SetFactoryShiftActive(shift, true));
        Clock();
        Assert.AreSame(shift, factory.PreparingFactoryShift);
        Assert.IsTrue(factory.TryStoreFactoryResource(ResourceType.Gold, 3, out _));
        var demands = new List<ResourceAmount>();
        factory.GetRemainingFactoryInputDemands(demands);
        CollectionAssert.AreEquivalent(new[] { ResourceType.Gold, ResourceType.Coal }, demands.Select(r => r.resourceType));
        Assert.AreEqual(5, factory.GetRemainingFactoryInputDemand(ResourceType.Gold));
        Assert.AreEqual(0, factory.GetRemainingFactoryInputDemand(ResourceType.Zinc));
        Assert.AreEqual(0, factory.GetRemainingFactoryInputDemand(ResourceType.Oil));
        VehicleAI first = Vehicle();
        VehicleAI second = Vehicle();
        Assert.IsTrue(factory.TryReserveIncomingResource(first, ResourceType.Gold, 4, out int reserved));
        Assert.AreEqual(4, reserved);
        Assert.IsTrue(factory.TryReserveIncomingResource(second, ResourceType.Gold, 4, out reserved));
        Assert.AreEqual(1, reserved);
        Assert.IsFalse(factory.TryReserveIncomingResource(Vehicle(), ResourceType.Zinc, 4, out _));
        Assert.IsTrue(factory.ReceiveIncomingResource(first, ResourceType.Gold, 4));
        Assert.IsTrue(factory.ReceiveIncomingResource(second, ResourceType.Gold, 1));
        Assert.AreEqual(8, factory.GetFactoryStoredAmount(ResourceType.Gold));
        Assert.AreEqual(0, factory.GetReservedIncomingResourceAmount(ResourceType.Gold));
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    [TestCase(4f)]
    public void ProductionUsesChosenMaterialsThroughPauseAndSpeedChanges(float speed)
    {
        FactoryShift shift = Shift(ResourceType.Gold, ResourceType.Coal);
        Store(ResourceType.Gold, 8);
        Store(ResourceType.Coal, 8);
        Store(ResourceType.Zinc, 10);
        Store(ResourceType.Oil, 10);
        Assert.IsTrue(factory.SetFactoryShiftActive(shift, true));
        GameTimeManager time = Clock();
        time.SimulationSpeed = speed;
        time.AdvanceTime(time.RealSecondsPerGameHour * 3 / speed);
        Assert.AreEqual(3, factory.CurrentShiftElapsedHours);
        Assert.AreEqual(3, factory.CurrentShiftPendingOutputs.Single(r => r.resourceType == ResourceType.Steel).amount);
        time.SimulationSpeed = 0;
        time.AdvanceTime(time.RealSecondsPerGameHour * 12);
        Assert.AreEqual(3, factory.CurrentShiftElapsedHours);
        time.SimulationSpeed = 4;
        time.AdvanceTime(time.RealSecondsPerGameHour * 5 / 4);
        Assert.IsNull(factory.CurrentFactoryShift);
        Assert.AreEqual(8, factory.GetFactoryStoredAmount(ResourceType.Steel));
        Assert.AreEqual(0, factory.GetFactoryStoredAmount(ResourceType.Gold));
        Assert.AreEqual(0, factory.GetFactoryStoredAmount(ResourceType.Coal));
        Assert.AreEqual(10, factory.GetFactoryStoredAmount(ResourceType.Zinc));
        Assert.AreEqual(10, factory.GetFactoryStoredAmount(ResourceType.Oil));
    }

    [Test]
    public void CatalogDefinedResourcesWorkAsTypedInputsSpecificInputsOutputsAndVehicleCargo()
    {
        ResourceCategory category = catalog.AddType("Conductor");
        ResourceDefinition ingredient = catalog.AddResource("Graphite");
        ingredient.types.Add(category.id);
        ResourceDefinition fuel = catalog.AddResource("Biofuel");
        fuel.types.Add("fuel");
        ResourceDefinition product = catalog.AddResource("Composite");
        data.resources[0] = Input("metal", category.id, 8);
        ProductionResourceEntry fixedInput = data.resources[1];
        fixedInput.useResourceTypes = false;
        fixedInput.resourceType = fuel.resource;
        data.resources[1] = fixedInput;
        ProductionResourceEntry output = data.resources[2];
        output.resourceType = product.resource;
        data.resources[2] = output;
        FactoryShift shift = Shift(ingredient.resource, fuel.resource);
        var allowed = new List<ResourceType>();
        data.GetAllowedInputResources(0, catalog, allowed);
        CollectionAssert.AreEqual(new[] { ingredient.resource }, allowed);
        data.GetAllowedInputResources(1, catalog, allowed);
        CollectionAssert.AreEqual(new[] { fuel.resource }, allowed);
        Assert.IsTrue(factory.HasValidFactoryShiftInputs(shift));
        Assert.AreEqual(2000, factory.GetFactoryResourceCapacity(ingredient.resource));
        Assert.AreEqual(2000, factory.GetFactoryResourceCapacity(fuel.resource));
        Store(ingredient.resource, 8);
        Store(fuel.resource, 8);
        FactoryShift restored = JsonUtility.FromJson<FactoryShift>(JsonUtility.ToJson(shift));
        restored.SynchronizeInputSlots(data, catalog);
        Assert.IsTrue(restored.TryGetInput("metal", out FactoryShiftInput restoredInput));
        Assert.AreEqual(ingredient.resource, restoredInput.resourceType);
        Assert.IsTrue(restoredInput.hasSelection);
        Assert.IsTrue(restored.TryGetInput("fuel", out restoredInput));
        Assert.AreEqual(fuel.resource, restoredInput.resourceType);
        Assert.IsTrue(restoredInput.hasSelection);

        var vehicleData = Track(ScriptableObject.CreateInstance<ResourceTransportVehicleData>());
        vehicleData.transportableResources.Add(product.resource);
        var compatible = new List<ResourceType>();
        factory.GetCompatibleFactoryOutputs(vehicleData, compatible);
        CollectionAssert.AreEqual(new[] { product.resource }, compatible);
        var vehicle = Vehicle();
        vehicle.SetAssignedResourceType(product.resource);
        Assert.IsTrue(vehicle.HasAssignedResourceType);
        Assert.AreEqual(product.resource, vehicle.AssignedResourceType);
        Assert.IsTrue(factory.SetFactoryShiftActive(shift, true));
        GameTimeManager time = Clock();
        time.AdvanceTime(time.RealSecondsPerGameHour * 8);
        Assert.AreEqual(8, factory.GetFactoryStoredAmount(product.resource));
        Assert.AreEqual(0, factory.GetFactoryStoredAmount(ingredient.resource));
        Assert.AreEqual(0, factory.GetFactoryStoredAmount(fuel.resource));
        Assert.IsTrue(factory.TryTakeFactoryResource(product.resource, 3, out int taken));
        Assert.AreEqual(3, taken);
        Assert.AreEqual(5, factory.GetFactoryStoredAmount(product.resource));
    }

    [TestCase(ResourceType.Gold, ResourceType.Coal)]
    [TestCase(ResourceType.Zinc, ResourceType.Wood)]
    public void SelectedStockProducesTheSameOutputWithoutAnyCollectionTarget(ResourceType metal, ResourceType fuel)
    {
        FactoryShift shift = Shift(metal, fuel);
        Assert.IsTrue(factory.SetFactoryShiftDesiredInput(shift, "metal", 0));
        Assert.IsTrue(factory.SetFactoryShiftDesiredInput(shift, "fuel", 0));
        ResourceType otherMetal = metal == ResourceType.Gold ? ResourceType.Zinc : ResourceType.Gold;
        ResourceType otherFuel = fuel == ResourceType.Coal ? ResourceType.Wood : ResourceType.Coal;
        Store(metal, 8);
        Store(fuel, 8);
        Store(otherMetal, 10);
        Store(otherFuel, 10);
        Assert.IsTrue(factory.SetFactoryShiftActive(shift, true));
        GameTimeManager time = Clock();
        time.AdvanceTime(time.RealSecondsPerGameHour * 8);
        Assert.AreEqual(8, factory.GetFactoryStoredAmount(ResourceType.Steel));
        Assert.AreEqual(0, factory.GetFactoryStoredAmount(metal));
        Assert.AreEqual(0, factory.GetFactoryStoredAmount(fuel));
        Assert.AreEqual(10, factory.GetFactoryStoredAmount(otherMetal));
        Assert.AreEqual(10, factory.GetFactoryStoredAmount(otherFuel));
    }

    [Test]
    public void MissingSelectedStockDoesNotSubstituteAnotherItemWithTheSameType()
    {
        FactoryShift shift = Shift(ResourceType.Gold, ResourceType.Coal);
        Assert.IsTrue(factory.SetFactoryShiftDesiredInput(shift, "metal", 0));
        Assert.IsTrue(factory.SetFactoryShiftDesiredInput(shift, "fuel", 0));
        Store(ResourceType.Zinc, 8);
        Store(ResourceType.Coal, 8);
        Assert.IsTrue(factory.SetFactoryShiftActive(shift, true));
        GameTimeManager time = Clock();
        time.AdvanceTime(time.RealSecondsPerGameHour * 8);
        Assert.AreEqual(0, factory.GetFactoryStoredAmount(ResourceType.Steel));
        Assert.AreEqual(8, factory.GetFactoryStoredAmount(ResourceType.Zinc));
        Assert.AreEqual(8, factory.GetFactoryStoredAmount(ResourceType.Coal));
    }

    [Test]
    public void SelectingTheSameMaterialInTwoSlotsCombinesConsumptionAndDemand()
    {
        data.resources[0] = Input("metal", "fuel", 8);
        data.resources[1] = Input("fuel", "liquid", 8);
        FactoryShift shift = Shift(ResourceType.Oil, ResourceType.Oil);
        Assert.AreEqual(16, shift.GetDesiredResourceAmount(ResourceType.Oil));
        Assert.AreEqual(16, factory.GetFactoryShiftRequiredResourceAmount(shift, ResourceType.Oil));
        Assert.AreEqual(2000, factory.GetFactoryResourceCapacity(ResourceType.Oil));
        Store(ResourceType.Oil, 8);
        Assert.IsTrue(factory.SetFactoryShiftActive(shift, true));
        GameTimeManager time = Clock();
        time.AdvanceTime(time.RealSecondsPerGameHour * 8);
        Assert.AreEqual(4, factory.GetFactoryStoredAmount(ResourceType.Steel));
        Assert.AreEqual(0, factory.GetFactoryStoredAmount(ResourceType.Oil));
    }

    [Test]
    public void DuplicatedAndSerializedChoicesRetainSlotIdentityAfterReordering()
    {
        FactoryShift shift = Shift(ResourceType.Gold, ResourceType.Coal);
        Assert.IsTrue(factory.SetFactoryShiftDesiredInput(shift, "metal", 15));
        Assert.IsTrue(factory.TryDuplicateFactoryShift(shift, out FactoryShift duplicate));
        Assert.IsTrue(factory.TrySetFactoryShiftInputResource(shift, "metal", ResourceType.Zinc));
        Assert.AreEqual(15, duplicate.GetDesiredResourceAmount(ResourceType.Gold));
        Assert.IsFalse(duplicate.IsActive);
        FactoryShift restored = JsonUtility.FromJson<FactoryShift>(JsonUtility.ToJson(duplicate));
        ProductionResourceEntry first = data.resources[0];
        data.resources[0] = data.resources[1];
        data.resources[1] = first;
        restored.SynchronizeInputSlots(data, catalog);
        Assert.IsTrue(restored.TryGetInput("metal", out FactoryShiftInput metal));
        Assert.AreEqual(ResourceType.Gold, metal.resourceType);
        Assert.AreEqual(15, metal.desiredAmount);
        Assert.IsTrue(restored.TryGetInput("fuel", out FactoryShiftInput fuel));
        Assert.AreEqual(ResourceType.Coal, fuel.resourceType);
    }

    [Test]
    public void LegacySerializedExactInputsKeepTheirChosenMaterialAndAmount()
    {
        data.resources[0] = new ProductionResourceEntry
        {
            resourceType = ResourceType.IronOre, direction = ProductionResourceDirection.Input,
            storageCapacity = 20, amountAtMaximumEfficiency = 8
        };
        FactoryShift restored = JsonUtility.FromJson<FactoryShift>(
            "{\"desiredResources\":[{\"resourceType\":0,\"amount\":6}]}");
        restored.SynchronizeInputSlots(data, catalog);
        Assert.AreEqual(6, restored.GetDesiredResourceAmount(ResourceType.IronOre));
        Assert.IsTrue(restored.TryGetInput(data.GetInputId(0), out FactoryShiftInput input));
        Assert.IsTrue(input.hasSelection);
        Assert.AreEqual(ResourceType.IronOre, input.resourceType);
    }

    [Test]
    public void InputDropdownBindsFilteredNamesIconsAndTheBackendSelectionCallback()
    {
        var texture = Track(new Texture2D(2, 2));
        catalog.fallbackIcon = Track(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero));
        Sprite zincIcon = Track(Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero));
        Sprite metalIcon = Track(Sprite.Create(texture, new Rect(1, 1, 1, 1), Vector2.zero));
        catalog.Find(ResourceType.Zinc).icon = zincIcon;
        catalog.FindType("metal").icon = metalIcon;
        var (row, label, dropdown, icon) = CreateInputRow();
        var input = new FactoryShiftInput { inputId = "metal", hasSelection = true, resourceType = ResourceType.Gold, desiredAmount = 8 };
        ResourceType chosen = ResourceType.Wood;
        row.BindInput(data.GetInputName(0), new[] { ResourceType.Gold, ResourceType.Zinc }, input, 8, true, true, value => chosen = value, null, null, new[] { "metal" });
        Assert.AreEqual("Metal", label.text);
        CollectionAssert.AreEqual(new[] { "Gold", "Zinc" }, dropdown.options.Select(option => option.text));
        Assert.AreSame(catalog.fallbackIcon, dropdown.captionImage.sprite);
        Assert.AreEqual("Gold", dropdown.captionText.text);
        Assert.IsTrue(dropdown.captionImage.enabled);
        Assert.IsTrue(icon.gameObject.activeSelf);
        Assert.AreSame(metalIcon, icon.sprite);
        Assert.AreSame(zincIcon, dropdown.options[1].image);
        Assert.IsTrue(dropdown.options.All(option => option.color == Color.white));
        dropdown.value = 1;
        Assert.AreEqual(ResourceType.Zinc, chosen);
        Assert.AreEqual("Metal", label.text);
        Assert.AreEqual("Zinc", dropdown.captionText.text);
        Assert.AreSame(zincIcon, dropdown.captionImage.sprite);
        input.resourceType = chosen;
        row.BindInput(data.GetInputName(0), new[] { ResourceType.Gold, ResourceType.Zinc }, input, 8, true, false, null, null, null, new[] { "metal" });
        Assert.IsFalse(dropdown.interactable);
        Assert.IsTrue(dropdown.gameObject.activeSelf);
        Assert.AreEqual("Zinc", dropdown.captionText.text);
        Assert.AreSame(zincIcon, dropdown.captionImage.sprite);
        Assert.AreSame(metalIcon, icon.sprite);
        row.SetInputSelectionInteractable(true);
        Assert.IsTrue(dropdown.interactable);
        row.SetInputSelectionInteractable(false);
        Assert.IsFalse(dropdown.interactable);
        Assert.AreEqual("Zinc", dropdown.captionText.text);
        Assert.AreSame(zincIcon, dropdown.captionImage.sprite);
    }

    [Test]
    public void SpecificInputShowsTheRequiredNameAndIconWithoutADropdown()
    {
        var texture = Track(new Texture2D(2, 2));
        catalog.fallbackIcon = Track(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero));
        var (row, label, dropdown, icon) = CreateInputRow();
        var input = new FactoryShiftInput { hasSelection = true, resourceType = ResourceType.Gold, desiredAmount = 8 };
        int selections = 0;
        row.BindInput("Metal", new[] { ResourceType.Gold, ResourceType.Zinc }, input, 8, true, true, _ => selections++, null, null);
        var rowRect = (RectTransform)row.transform;
        RectTransform amount = row.transform.Find("Amount") as RectTransform;
        LayoutRebuilder.ForceRebuildLayoutImmediate(rowRect);
        float typedAmountPosition = amount.anchoredPosition.x;
        row.BindInput("Custom fixed input", new[] { ResourceType.Gold }, input, 8, false, true, _ => selections++, null, null);
        LayoutRebuilder.ForceRebuildLayoutImmediate(rowRect);
        Assert.AreEqual(typedAmountPosition, amount.anchoredPosition.x, 0.01f,
            "The amount column must stay in place when the dropdown is hidden.");
        Assert.AreEqual("Gold", label.text);
        Assert.IsTrue(icon.gameObject.activeSelf);
        Assert.AreSame(catalog.fallbackIcon, icon.sprite);
        Assert.IsFalse(dropdown.gameObject.activeSelf);
        row.SetInputSelectionInteractable(true);
        Assert.IsFalse(dropdown.interactable);
        dropdown.onValueChanged.Invoke(0);
        Assert.AreEqual(0, selections, "A fixed row must remove the previous typed selection callback.");

        row.BindInput("Metal", new[] { ResourceType.Gold, ResourceType.Zinc }, input, 8, true, true, _ => selections++, null, null);
        Assert.IsTrue(dropdown.gameObject.activeSelf);
        Assert.IsTrue(icon.gameObject.activeSelf);
        Assert.AreEqual("Metal", label.text);
        Assert.AreEqual("Gold", dropdown.captionText.text);
        dropdown.value = 1;
        Assert.AreEqual(1, selections);
    }

    [Test]
    public void CombinedTypeInputsShowEveryTypeIconAndReuseTheAuthoredIconTemplate()
    {
        var texture = Track(new Texture2D(2, 2));
        catalog.fallbackIcon = Track(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero));
        Sprite fuelIcon = Track(Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero));
        catalog.FindType("fuel").icon = fuelIcon;
        var (row, label, dropdown, icon) = CreateInputRow();
        var input = new FactoryShiftInput { hasSelection = true, resourceType = ResourceType.Oil };
        var requiredTypes = new[] { "fuel", "liquid" };
        row.BindInput("Fuel + Liquid", new[] { ResourceType.Oil }, input, 8, true, true, null, null, null, requiredTypes);
        Image[] requirementIcons = row.GetComponentsInChildren<Image>()
            .Where(image => image.transform.parent == row.transform).ToArray();
        Assert.AreEqual(2, requirementIcons.Length);
        Assert.AreSame(fuelIcon, requirementIcons[0].sprite);
        Assert.AreSame(catalog.fallbackIcon, requirementIcons[1].sprite);
        Assert.AreEqual("Fuel + Liquid", label.text);
        Assert.AreEqual("Oil", dropdown.captionText.text);

        row.BindInput("Oil", new[] { ResourceType.Oil }, input, 8, false, true, null, null, null);
        Assert.IsFalse(requirementIcons[1].gameObject.activeSelf);
        Assert.AreSame(catalog.fallbackIcon, icon.sprite);
        row.BindInput("Fuel + Liquid", new[] { ResourceType.Oil }, input, 8, true, true, null, null, null, requiredTypes);
        CollectionAssert.AreEquivalent(requirementIcons, row.GetComponentsInChildren<Image>()
            .Where(image => image.transform.parent == row.transform));
    }

    [Test]
    public void StorageViewShowsOnlyPositiveInventoryWithItsIndividualCapacity()
    {
        Store(ResourceType.Gold, 10);
        Store(ResourceType.Zinc, 5);
        Assert.IsTrue(factory.TryTakeFactoryResource(ResourceType.Zinc, 5, out _));
        var viewObject = Track(new GameObject("Factory status", typeof(RectTransform)));
        var view = viewObject.AddComponent<FactoryOperationsTabView>();
        var root = Track(new GameObject("Storage rows", typeof(RectTransform)));
        root.transform.SetParent(viewObject.transform, false);
        var template = Track(new GameObject("Storage template", typeof(RectTransform)));
        var row = template.AddComponent<FactoryStatusResourceRowView>();
        var nameObject = Track(new GameObject("Name", typeof(RectTransform)));
        nameObject.transform.SetParent(template.transform, false);
        var amountObject = Track(new GameObject("Amount", typeof(RectTransform)));
        amountObject.transform.SetParent(template.transform, false);
        SetField(row, "resourceNameText", nameObject.AddComponent<TextMeshProUGUI>());
        SetField(row, "amountText", amountObject.AddComponent<TextMeshProUGUI>());
        template.SetActive(false);
        SetField(view, "storageRowsRoot", root.GetComponent<RectTransform>());
        SetField(view, "resourceRowPrefab", row);
        view.Bind(factory);
        Assert.AreEqual(1, root.transform.childCount);
        string[] labels = root.GetComponentsInChildren<TMP_Text>().Select(text => text.text).ToArray();
        CollectionAssert.AreEquivalent(new[] { "Gold", "10 / 2000" }, labels);
    }

    private (FactoryResourceAmountRowView row, TMP_Text label, TMP_Dropdown dropdown, Image icon) CreateInputRow()
    {
        var rowObject = Track(new GameObject("Input row", typeof(RectTransform)));
        var row = rowObject.AddComponent<FactoryResourceAmountRowView>();
        ((RectTransform)rowObject.transform).sizeDelta = new Vector2(400f, 18f);
        var group = rowObject.AddComponent<HorizontalLayoutGroup>();
        group.childControlWidth = true;
        group.childForceExpandWidth = false;
        group.spacing = 3f;
        var labelObject = new GameObject("Input requirement", typeof(RectTransform));
        labelObject.transform.SetParent(rowObject.transform, false);
        var label = labelObject.AddComponent<TextMeshProUGUI>();
        SetField(row, "resourceNameText", label);
        var labelLayout = labelObject.AddComponent<LayoutElement>();
        labelLayout.minWidth = 35f;
        labelLayout.preferredWidth = 55f;
        var iconObject = new GameObject("Resource icon", typeof(RectTransform));
        iconObject.transform.SetParent(rowObject.transform, false);
        var icon = iconObject.AddComponent<Image>();
        SetField(row, "resourceIcon", icon);
        var iconLayout = iconObject.AddComponent<LayoutElement>();
        iconLayout.minWidth = iconLayout.preferredWidth = 10f;
        var dropdownObject = new GameObject("Dropdown", typeof(RectTransform));
        dropdownObject.transform.SetParent(rowObject.transform, false);
        var dropdown = dropdownObject.AddComponent<TMP_Dropdown>();
        var dropdownLayout = dropdownObject.AddComponent<LayoutElement>();
        dropdownLayout.minWidth = 65f;
        dropdownLayout.preferredWidth = 90f;
        dropdownLayout.flexibleWidth = 1f;
        var captionObject = new GameObject("Label", typeof(RectTransform));
        captionObject.transform.SetParent(dropdownObject.transform, false);
        dropdown.captionText = captionObject.AddComponent<TextMeshProUGUI>();
        var captionIconObject = new GameObject("Caption icon", typeof(RectTransform));
        captionIconObject.transform.SetParent(dropdownObject.transform, false);
        dropdown.captionImage = captionIconObject.AddComponent<Image>();
        SetField(row, "inputResourceDropdown", dropdown);
        var spacerObject = new GameObject("SpecificInputSpacer", typeof(RectTransform), typeof(LayoutElement));
        spacerObject.transform.SetParent(rowObject.transform, false);
        var spacerLayout = spacerObject.GetComponent<LayoutElement>();
        spacerLayout.minWidth = dropdownLayout.minWidth;
        spacerLayout.preferredWidth = dropdownLayout.preferredWidth;
        spacerLayout.flexibleWidth = dropdownLayout.flexibleWidth;
        SetField(row, "specificInputSpacer", spacerObject);
        var amountObject = new GameObject("Amount", typeof(RectTransform), typeof(LayoutElement));
        amountObject.transform.SetParent(rowObject.transform, false);
        var amountLayout = amountObject.GetComponent<LayoutElement>();
        amountLayout.minWidth = amountLayout.preferredWidth = 48f;
        SetField(row, "amountText", amountObject.AddComponent<TextMeshProUGUI>());
        return (row, label, dropdown, icon);
    }

    private static void SetField(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static ProductionResourceEntry Input(string id, string type, int amount) => new ProductionResourceEntry
    {
        inputId = id, useResourceTypes = true, requiredTypes = new List<string> { type },
        direction = ProductionResourceDirection.Input, storageCapacity = 2000, amountAtMaximumEfficiency = amount
    };

    private FactoryShift Shift(ResourceType metal, ResourceType fuel)
    {
        Assert.IsTrue(factory.TryAddFactoryShift(out FactoryShift shift));
        Assert.IsTrue(factory.TrySetFactoryShiftInputResource(shift, "metal", metal));
        Assert.IsTrue(factory.TrySetFactoryShiftInputResource(shift, "fuel", fuel));
        return shift;
    }

    private void Store(ResourceType resource, int amount)
    {
        Assert.IsTrue(factory.TryStoreFactoryResource(resource, amount, out int stored));
        Assert.AreEqual(amount, stored);
    }

    private GameTimeManager Clock()
    {
        var time = Track(new GameObject("Typed factory clock")).AddComponent<GameTimeManager>();
        typeof(GameTimeManager).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(time, null);
        factory.Initialize(data, Vector2Int.zero, 0, new List<BuildingTile>());
        return time;
    }

    private VehicleAI Vehicle()
    {
        var go = Track(new GameObject("Incoming resource vehicle"));
        go.SetActive(false);
        return go.AddComponent<VehicleAI>();
    }

    private T Track<T>(T obj) where T : Object { temporary.Add(obj); return obj; }
}
