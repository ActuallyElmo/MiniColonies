using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewResourceTransport", menuName = "ColonySim/Vehicles/ResourceTransport")]
public class ResourceTransportVehicleData : VehicleData
{
    [Header("Resource Transportation Settings")]
    public int vehicleCapacity;
    [Tooltip("Dynamic list of resources this vehicle can transport")]
    public List<ResourceType> transportableResources = new List<ResourceType>();
}