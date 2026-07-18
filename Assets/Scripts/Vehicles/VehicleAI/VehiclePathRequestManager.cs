using System;
using System.Collections.Generic;
using UnityEngine;

public class VehiclePathRequestManager : MonoBehaviour
{
    public static VehiclePathRequestManager Instance { get; private set; }

    [Header("Debug")]
    public bool disableAllPathfinding = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void RequestPath(VehicleAI requester, Vector3 targetPos, Action<Queue<Vector3>> callback)
    {
        RequestPath(requester, targetPos, callback, null);
    }

    public void RequestPath(VehicleAI requester, Vector3 targetPos, Action<Queue<Vector3>> callback, Action<TrafficRoute> routeCallback, bool snapStartToClosestEdgeEndpoint = false)
    {
        RequestPath(requester, targetPos, callback, routeCallback, snapStartToClosestEdgeEndpoint, Vector2Int.zero, false, Vector2Int.zero, false);
    }

    public void RequestPath(
        VehicleAI requester,
        Vector3 targetPos,
        Action<Queue<Vector3>> callback,
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
            callback?.Invoke(null);
            routeCallback?.Invoke(null);
            return;
        }

        if (requester == null)
        {
            Debug.LogWarning("Vehicle path request rejected because the requester is null.");
            callback?.Invoke(null);
            routeCallback?.Invoke(null);
            return;
        }

        if (SimulationTaskManager.Instance == null)
        {
            Debug.LogError(
                $"Vehicle path request for {requester.name} failed because SimulationTaskManager.Instance is missing.");
            callback?.Invoke(null);
            routeCallback?.Invoke(null);
            return;
        }

        // NO LONGER capturing startPos here! We let the task fetch it dynamically when it runs.
        VehiclePathfindingTask task = new VehiclePathfindingTask(requester, targetPos, callback, routeCallback, snapStartToClosestEdgeEndpoint, startPortCell, hasStartPortCell, targetPortCell, hasTargetPortCell, rerouteReason, congestionSnapshot);
        SimulationTaskManager.Instance.EnqueueTask(task);
    }
}
