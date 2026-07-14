using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class RoadNetwork
{
    public int id;
    public Color debugColor;
    
    // Every road cell that physically connects to this network
    public HashSet<Vector2Int> roadCells = new HashSet<Vector2Int>();
    
    // Buildings that have successfully satisfied BOTH Entry and Exit requirements for this network
    public HashSet<Building> connectedBuildings = new HashSet<Building>();
}