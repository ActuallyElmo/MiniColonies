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
        if (disableAllPathfinding || requester == null)
        {
            callback?.Invoke(null);
            return;
        }

        // NO LONGER capturing startPos here! We let the task fetch it dynamically when it runs.
        VehiclePathfindingTask task = new VehiclePathfindingTask(requester, targetPos, callback);
        SimulationTaskManager.Instance.EnqueueTask(task);
    }
}