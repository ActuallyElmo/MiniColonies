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
    private Coroutine _routeRecalculationCoroutine;
    private Coroutine _trafficBackendSubscriptionCoroutine;
    private bool _subscribedToTrafficReady;

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
                SpawnVehicles(vData, 20);
            }
        }

        bool wasAlreadySubscribed = _subscribedToTrafficReady;
        TrySubscribeToTrafficReady();
        if (wasAlreadySubscribed && HasPublishedTrafficNetwork())
        {
            HandleTrafficReady();
        }
    }

    private void SpawnVehicles(VehicleData vehicleData, int amount)
    {
        for(int i = 0; i < amount; i++)
        {
            GameObject vObj = Instantiate(vehicleData.vehiclePrefab, transform.position, Quaternion.identity, transform);
            VehicleAI vAI = vObj.AddComponent<VehicleAI>();
            vAI.Initialize(this, vehicleData);
            spawnedVehicles.Add(vAI);
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
        TrySubscribeToTrafficReady();
        if (!_subscribedToTrafficReady && _trafficBackendSubscriptionCoroutine == null)
        {
            _trafficBackendSubscriptionCoroutine = StartCoroutine(WaitForTrafficBackendSubscription());
        }
    }

    private void OnDisable()
    {
        if (_trafficBackendSubscriptionCoroutine != null)
        {
            StopCoroutine(_trafficBackendSubscriptionCoroutine);
            _trafficBackendSubscriptionCoroutine = null;
        }

        if (_subscribedToTrafficReady && TrafficSystemBackend.Instance != null)
        {
            TrafficSystemBackend.Instance.OnTrafficNetworkReady -= HandleTrafficReady;
        }

        _subscribedToTrafficReady = false;
    }

    public void OnNetworksUpdated()
    {
        _needsPathRecalculation = true; 
    }

    private void HandleTrafficReady()
    {
        if (!isActiveAndEnabled || data == null) return;

        //if (!_needsPathRecalculation) return;
        //_needsPathRecalculation = false;
        
        if (_routeRecalculationCoroutine != null)
        {
            StopCoroutine(_routeRecalculationCoroutine);
        }
        _routeRecalculationCoroutine = StartCoroutine(StaggeredRouteRecalculation());
    }

    private System.Collections.IEnumerator StaggeredRouteRecalculation()
    {
        // Stagger the requests so hundreds of buildings don't choke the frame
        int randomFrameDelay = Random.Range(0, 60);
        for (int i = 0; i < randomFrameDelay; i++) yield return null;

        if (data is PopHousingBuildingData)
        {
            RouteData bestRoute = FindBestFactoryRoute();

            if (bestRoute == null && spawnedVehicles.Count > 0)
            {
                Debug.LogWarning(
                    $"{DescribeBuildingForDispatch()} could not dispatch vehicles to factories. {DescribeFactoryRouteFailure()}");
            }
            
            foreach (VehicleAI vAI in spawnedVehicles)
            {
                if (bestRoute != null) 
                {
                    if (_needsPathRecalculation || vAI.targetBuilding != bestRoute.targetFactory)
                    {
                        vAI.Reroute(bestRoute.targetFactory, bestRoute.sharedNetwork); 
                        yield return new WaitForSeconds(1.5f); // Throttle departures to create a natural flow
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

    private IEnumerator WaitForTrafficBackendSubscription()
    {
        while (isActiveAndEnabled && TrafficSystemBackend.Instance == null)
        {
            yield return null;
        }

        _trafficBackendSubscriptionCoroutine = null;
        TrySubscribeToTrafficReady();
    }

    private bool TrySubscribeToTrafficReady()
    {
        if (_subscribedToTrafficReady || TrafficSystemBackend.Instance == null)
        {
            return _subscribedToTrafficReady;
        }

        TrafficSystemBackend.Instance.OnTrafficNetworkReady += HandleTrafficReady;
        _subscribedToTrafficReady = true;

        if (HasPublishedTrafficNetwork())
        {
            HandleTrafficReady();
        }

        return true;
    }

    private bool HasPublishedTrafficNetwork()
    {
        return TrafficSystemBackend.Instance != null &&
               TrafficSystemBackend.Instance.allEdges != null &&
               TrafficSystemBackend.Instance.allEdges.Count > 0;
    }

    private string DescribeFactoryRouteFailure()
    {
        int networkCount = validNetworks != null ? validNetworks.Count : 0;
        int networksWithExitPorts = 0;
        int factoriesOnSharedNetworks = 0;
        int factoriesWithEntryPorts = 0;

        if (validNetworks != null)
        {
            foreach (RoadNetwork network in validNetworks)
            {
                if (network == null) continue;

                if (HasPortOnNetwork(this, network, PortType.Exit))
                {
                    networksWithExitPorts++;
                }

                if (network.connectedBuildings == null) continue;

                foreach (Building building in network.connectedBuildings)
                {
                    if (building == null || !(building.data is ResourceProductionBuildingData))
                    {
                        continue;
                    }

                    factoriesOnSharedNetworks++;
                    if (building.validNetworks.Contains(network) &&
                        HasPortOnNetwork(building, network, PortType.Entry))
                    {
                        factoriesWithEntryPorts++;
                    }
                }
            }
        }

        return
            $"validNetworks={networkCount}, networksWithExitPorts={networksWithExitPorts}, factoriesOnSharedNetworks={factoriesOnSharedNetworks}, factoriesWithEntryPorts={factoriesWithEntryPorts}.";
    }

    private bool HasPortOnNetwork(Building building, RoadNetwork network, PortType desiredFlow)
    {
        if (building == null || network == null) return false;

        foreach (var kvp in building.globalPorts)
        {
            bool flowMatches = kvp.Value == desiredFlow || kvp.Value == PortType.Both;
            if (flowMatches &&
                building.portNetworks.TryGetValue(kvp.Key, out RoadNetwork portNetwork) &&
                portNetwork == network)
            {
                return true;
            }
        }

        return false;
    }

    private string DescribeBuildingForDispatch()
    {
        string buildingName = data != null ? data.buildingName : name;
        return $"Building {buildingName} at {originCell}";
    }
}
