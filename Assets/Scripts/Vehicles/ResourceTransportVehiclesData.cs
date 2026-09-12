using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewResourceTransport", menuName = "ColonySim/Vehicles/ResourceTransport")]
public class ResourceTransportVehicleData : VehicleData
{
    [Header("Resource Transportation Settings")]
    [Min(1)]
    [Tooltip("The amount of parking space this vehicle occupies at an industrial building.")]
    public int vehicleSpaceUsed = 1;

    [Min(0)]
    public int vehicleCapacity;
    [Tooltip("Dynamic list of resources this vehicle can transport")]
    public List<ResourceType> transportableResources = new List<ResourceType>();
}
