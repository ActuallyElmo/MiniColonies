using UnityEngine;

[CreateAssetMenu(fileName = "NewPopTransport", menuName = "ColonySim/Vehicles/PopTransport")]
public class PopTransportVehicleData : VehicleData
{
    [Header("Pop Transportation Settings")]
    [Tooltip("The amount of space this vehicle takes up in a housing building.")]
    public int vehicleSpaceUsed;
    
    [Tooltip("The amount of pops this vehicle can transport to a workplace.")]
    public int amountOfPopsTransported;
}
