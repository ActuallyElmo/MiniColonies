using UnityEngine;
using System.Collections.Generic;

public abstract class VehicleData : ScriptableObject
{
    [Header("Base Vehicle Info")]
    public string vehicleName;
    public GameObject vehiclePrefab;
    public float maximumVehicleSpeed;
    public bool isLongVehicle;
    public bool isOffroadVehicle;
}
