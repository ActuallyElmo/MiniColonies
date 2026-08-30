using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewPopHousing", menuName = "ColonySim/Buildings/PopHousing")]
public class PopHousingBuildingData : BuildingData
{
    [Header("Pop Housing Specifics")]
    [Tooltip("Total vehicle space available (e.g., house = 4, skyscraper = 16)")]
    public int popTransportVehiclesStorageSpace;
    
    [Tooltip("List of specific pop vehicles allowed to park here.")]
    public List<PopTransportVehicleData> typesOfPopTransportVehiclesAllowed = new List<PopTransportVehicleData>();

    [Header("Initial Testing Ownership")]
    [Tooltip(
        "Keeps the current prototype behavior by filling this house when it is " +
        "placed. Disable this when vehicle assignment is fully player-driven.")]
    public bool autoFillVehicleSpaceForTesting = true;

    [Tooltip(
        "Vehicle used by the prototype auto-fill. When empty, the first allowed " +
        "vehicle type is used.")]
    public PopTransportVehicleData testingAutoFillVehicle;
}
