using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class Building : MonoBehaviour
{
    public BuildingData data { get; private set; }
    public Vector2Int originCell { get; private set; }
    public int elevationLayer { get; private set; }

    [Header("Vehicle Management")]
    public List<VehicleData> ownedVehicles = new List<VehicleData>();

    private List<VehicleAI> spawnedVehicles = new List<VehicleAI>();

    [Header("Network Data")]
    public Dictionary<Vector2Int, RoadNetwork> portNetworks = new Dictionary<Vector2Int, RoadNetwork>();
    public HashSet<RoadNetwork> validNetworks = new HashSet<RoadNetwork>();

    public HashSet<Vector2Int> occupiedCells = new HashSet<Vector2Int>();
    public Dictionary<Vector2Int, PortType> globalPorts = new Dictionary<Vector2Int, PortType>();

    private List<GameObject> portIcons = new List<GameObject>();
    private bool _needsPathRecalculation = false;

    public class RouteData
    {
        public Building targetFactory;
        public RoadNetwork sharedNetwork;
    }

    public void Initialize(BuildingData buildingData, Vector2Int origin, int layer, List<BuildingTile> activeFootprint)
    {
        data = buildingData;
        originCell = origin;
        elevationLayer = layer;

        foreach (var tile in activeFootprint)
        {
            Vector2Int globalPos = origin + tile.localPosition;
            occupiedCells.Add(globalPos);

            if (tile.isPort)
            {
                globalPorts.Add(globalPos, tile.portType);
            }
        }

        // Spawn test vehicles based on BuildingData ---
        if (data is PopHousingBuildingData popData && popData.typesOfPopTransportVehiclesAllowed.Count > 0)
        {
            VehicleData vData = popData.typesOfPopTransportVehiclesAllowed[0]; 
            if (vData.vehiclePrefab != null)
            {
                StartCoroutine(SpawnVehicles(vData, 20));
            }
        }
    }

    IEnumerator SpawnVehicles(VehicleData vehicleData, int amount)
    {
        for(int i = 0; i < amount; i++)
        {
            GameObject vObj = Instantiate(vehicleData.vehiclePrefab, transform.position, Quaternion.identity);
            VehicleAI vAI = vObj.AddComponent<VehicleAI>();
            vAI.Initialize(this, vehicleData);
            spawnedVehicles.Add(vAI);

            yield return new WaitForSeconds(1f);
        }
    }

    public void RegisterPortIcon(GameObject icon)
    {
        portIcons.Add(icon);
        icon.SetActive(false); 
    }

    public void SetPortIconsVisibility(bool isVisible)
    {
        foreach (var icon in portIcons)
        {
            if (icon != null) icon.SetActive(isVisible);
        }
    }

    public Vector2Int GetClosestPort(PortType desiredFlow, RoadNetwork targetNetwork, Vector2Int referenceCell)
    {
        Vector2Int bestPort = originCell; 
        float minSqrDist = float.MaxValue;
        bool found = false;

        foreach (var kvp in globalPorts)
        {
            if (kvp.Value == desiredFlow || kvp.Value == PortType.Both)
            {
                if (portNetworks.TryGetValue(kvp.Key, out RoadNetwork net) && net == targetNetwork)
                {
                    float sqrDist = (kvp.Key - referenceCell).sqrMagnitude;
                    if (sqrDist < minSqrDist)
                    {
                        minSqrDist = sqrDist;
                        bestPort = kvp.Key;
                        found = true;
                    }
                }
            }
        }
        
        if (!found) Debug.LogWarning($"Building {data.buildingName} could not find a {desiredFlow} port on network!");
        return found ? bestPort : originCell;
    }

    // --- UPDATED AI EVENT LISTENERS ---
    
    private void OnEnable()
    {
        // Listen to the new Backend instead of the old monolithic manager
        if (TrafficSystemBackend.Instance != null) 
            TrafficSystemBackend.Instance.OnTrafficNetworkReady += HandleTrafficReady;
    }

    private void OnDisable()
    {
        if (TrafficSystemBackend.Instance != null) 
            TrafficSystemBackend.Instance.OnTrafficNetworkReady -= HandleTrafficReady;
    }

    public void OnNetworksUpdated()
    {
        _needsPathRecalculation = true; 
    }

    private void HandleTrafficReady()
    {
        //if (!_needsPathRecalculation) return;
        //_needsPathRecalculation = false;
        
        StartCoroutine(StaggeredRouteRecalculation());
    }

    private System.Collections.IEnumerator StaggeredRouteRecalculation()
    {
        // Stagger the requests so hundreds of buildings don't choke the frame
        int randomFrameDelay = Random.Range(0, 60);
        for (int i = 0; i < randomFrameDelay; i++) yield return null;

        if (data is PopHousingBuildingData)
        {
            RouteData bestRoute = FindBestFactoryRoute();
            
            foreach (VehicleAI vAI in spawnedVehicles)
            {
                if (bestRoute != null) 
                {
                    if (_needsPathRecalculation || vAI.targetBuilding != bestRoute.targetFactory)
                    {
                        vAI.Reroute(bestRoute.targetFactory, bestRoute.sharedNetwork); 
                    }
                }
                else 
                {
                    // No factory found on the network anymore, park the car
                    vAI.RecallAndDeactivate();
                }
            }
        }

        // Safely reset the local port flag after we finish checking
        _needsPathRecalculation = false;
    }

    private RouteData FindBestFactoryRoute()
    {
        RouteData bestRoute = null;
        float minSqrDistance = float.MaxValue;

        foreach (RoadNetwork network in validNetworks)
        {
            List<Vector2Int> validExits = new List<Vector2Int>();
            foreach (var kvp in globalPorts)
            {
                if ((kvp.Value == PortType.Exit || kvp.Value == PortType.Both) && 
                    portNetworks.TryGetValue(kvp.Key, out RoadNetwork net) && net == network)
                {
                    validExits.Add(kvp.Key);
                }
            }

            if (validExits.Count == 0) continue; 

            foreach (Building b in network.connectedBuildings)
            {
                if (b.data is ResourceProductionBuildingData && b.validNetworks.Contains(network))
                {
                    foreach (var kvp in b.globalPorts)
                    {
                        if ((kvp.Value == PortType.Entry || kvp.Value == PortType.Both) && 
                            b.portNetworks.TryGetValue(kvp.Key, out RoadNetwork bNet) && bNet == network)
                        {
                            foreach (Vector2Int exitPort in validExits)
                            {
                                float sqrDist = (kvp.Key - exitPort).sqrMagnitude;
                                if (sqrDist < minSqrDistance)
                                {
                                    minSqrDistance = sqrDist;
                                    bestRoute = new RouteData { targetFactory = b, sharedNetwork = network };
                                }
                            }
                        }
                    }
                }
            }
        }
        return bestRoute;
    }
}