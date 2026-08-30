using System;
using UnityEngine;

public class VehiclePathRequestManager : MonoBehaviour
{
    public static VehiclePathRequestManager Instance { get; private set; }

    [Header("Debug")]
    public bool disableAllPathfinding = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void RequestPath(
        VehicleAI requester,
        Vector3 targetPos,
        Action<TrafficRoute> routeCallback,
        bool snapStartToClosestEdgeEndpoint,
        Vector2Int startPortCell,
        bool hasStartPortCell,
        Vector2Int targetPortCell,
        bool hasTargetPortCell,
        RouteRerouteReason rerouteReason = RouteRerouteReason.InitialRequest,
        StrategicCongestionSnapshot congestionSnapshot = null)
    {
        if (disableAllPathfinding)
        {
            Debug.LogWarning(
                "Vehicle path request rejected because VehiclePathRequestManager.disableAllPathfinding is enabled.");
            routeCallback?.Invoke(null);
            return;
        }

        if (requester == null)
        {
            Debug.LogWarning("Vehicle path request rejected because the requester is null.");
            routeCallback?.Invoke(null);
            return;
        }

        if (SimulationTaskManager.Instance == null)
        {
            Debug.LogError(
                $"Vehicle path request for {requester.name} failed because SimulationTaskManager.Instance is missing.");
            routeCallback?.Invoke(null);
            return;
        }

        VehiclePathfindingTask task = new VehiclePathfindingTask(
            requester,
            targetPos,
            routeCallback,
            snapStartToClosestEdgeEndpoint,
            startPortCell,
            hasStartPortCell,
            targetPortCell,
            hasTargetPortCell,
            rerouteReason,
            congestionSnapshot);
        SimulationTaskManager.Instance.EnqueueTask(task);
    }
}
