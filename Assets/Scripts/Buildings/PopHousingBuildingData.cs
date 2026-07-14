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
}