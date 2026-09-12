using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class IndustrialVehicleOwnershipTests
{
    private GameObject factoryObject;
    private Building factory;
    private ResourceProductionBuildingData factoryData;
    private ResourceTransportVehicleData vehicleData;
    private GameObject vehiclePrefab;

    [SetUp]
    public void SetUp()
    {
        factoryData =
            ScriptableObject.CreateInstance<ResourceProductionBuildingData>();
        factoryData.resources = new List<ProductionResourceEntry>
        {
            new ProductionResourceEntry
            {
                resourceType = ResourceType.IronOre,
                direction = ProductionResourceDirection.Input
            },
            new ProductionResourceEntry
            {
                resourceType = ResourceType.Steel,
                direction = ProductionResourceDirection.Output
            },
            new ProductionResourceEntry
            {
                resourceType = ResourceType.Wood,
                direction = ProductionResourceDirection.Output
            }
        };

        factoryObject = new GameObject("IndustrialVehicleTestFactory");
        factory = factoryObject.AddComponent<Building>();
        factory.Initialize(
            factoryData,
            Vector2Int.zero,
            0,
            new List<BuildingTile>());

        vehiclePrefab = new GameObject("IndustrialVehicleTestPrefab");
        vehiclePrefab.AddComponent<VehicleAI>();
        vehicleData =
            ScriptableObject.CreateInstance<ResourceTransportVehicleData>();
        vehicleData.vehiclePrefab = vehiclePrefab;
        vehicleData.transportableResources = new List<ResourceType>
        {
            ResourceType.Steel,
            ResourceType.CopperOre
        };
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(factoryObject);
        Object.DestroyImmediate(vehiclePrefab);
        Object.DestroyImmediate(vehicleData);
        Object.DestroyImmediate(factoryData);
    }

    [Test]
    public void CompatibleOutputsAreBuildingVehicleIntersectionOnly()
    {
        var outputs = new List<ResourceType>();

        factory.GetCompatibleFactoryOutputs(vehicleData, outputs);

        CollectionAssert.AreEqual(
            new[] { ResourceType.Steel },
            outputs);
    }

    [Test]
    public void ProductionBuildingRejectsPopulationVehicles()
    {
        var popVehicle =
            ScriptableObject.CreateInstance<PopTransportVehicleData>();
        popVehicle.vehiclePrefab = vehiclePrefab;

        Assert.AreEqual(
            Building.VehicleAssignmentResult.UnsupportedBuilding,
            factory.GetVehicleAssignmentResult(popVehicle));

        Object.DestroyImmediate(popVehicle);
    }

    [Test]
    public void NonProductionBuildingRejectsIndustrialVehicles()
    {
        var housingData =
            ScriptableObject.CreateInstance<PopHousingBuildingData>();
        housingData.autoFillVehicleSpaceForTesting = false;
        var housingObject = new GameObject("IndustrialVehicleTestHousing");
        Building housing = housingObject.AddComponent<Building>();
        housing.Initialize(
            housingData,
            Vector2Int.zero,
            0,
            new List<BuildingTile>());

        Assert.AreEqual(
            Building.VehicleAssignmentResult.UnsupportedBuilding,
            housing.GetVehicleAssignmentResult(vehicleData));

        Object.DestroyImmediate(housingObject);
        Object.DestroyImmediate(housingData);
    }

    [Test]
    public void AssignmentDefaultsToCarryingNoOutput()
    {
        Assert.IsTrue(factory.TryAssignVehicle(vehicleData, out VehicleAI vehicle));

        Assert.IsNotNull(vehicle);
        Assert.IsFalse(vehicle.HasAssignedResourceType);
        Assert.AreEqual(1, factory.OwnedVehicles.Count);
        Assert.AreSame(vehicle, factory.OwnedVehicles[0]);
    }

    [Test]
    public void AssignedOutputCanOnlyChangeToACompatibleOutput()
    {
        vehicleData.transportableResources.Add(ResourceType.Wood);
        Assert.IsTrue(factory.TryAssignVehicle(vehicleData, out VehicleAI vehicle));

        Assert.IsTrue(factory.TrySetResourceVehicleOutput(
            vehicle,
            ResourceType.Wood));
        Assert.AreEqual(ResourceType.Wood, vehicle.AssignedResourceType);

        Assert.IsFalse(factory.TrySetResourceVehicleOutput(
            vehicle,
            ResourceType.IronOre));
        Assert.AreEqual(ResourceType.Wood, vehicle.AssignedResourceType);

        Assert.IsFalse(factory.TrySetResourceVehicleOutput(
            vehicle,
            ResourceType.CopperOre));
        Assert.AreEqual(ResourceType.Wood, vehicle.AssignedResourceType);

        Assert.IsTrue(factory.TryClearResourceVehicleOutput(vehicle));
        Assert.IsFalse(vehicle.HasAssignedResourceType);
    }

    [Test]
    public void CompatibleVehicleIsRejectedWhenFactoryParkingIsFull()
    {
        factoryData.industrialVehiclesStorageSpace = 1;
        vehicleData.vehicleSpaceUsed = 1;
        Assert.IsTrue(factory.TryAssignVehicle(vehicleData, out _));

        Assert.AreEqual(
            Building.VehicleAssignmentResult.InsufficientSpace,
            factory.GetVehicleAssignmentResult(vehicleData));
        Assert.IsFalse(factory.TryAssignVehicle(vehicleData, out _));
    }
}
