using System;
using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public class VehiclePathfindingTask : ISimulationTask
{
    private enum TaskState { Init, Wait, Complete }
    private TaskState _state = TaskState.Init;

    private VehicleAI _requester;
    private Vector3 _targetPos;
    private Action<Queue<Vector3>> _onComplete;

    // Native Data
    private NativeArray<int> _startEdgeIndices;
    private NativeArray<int> _endEdgeIndices;
    private NativeList<float3> _resultWaypoints;
    private JobHandle _jobHandle;

    public VehiclePathfindingTask(VehicleAI requester, Vector3 target, Action<Queue<Vector3>> onComplete)
    {
        _requester = requester;
        _targetPos = target;
        _onComplete = onComplete;
    }

    public bool Process(Stopwatch timer, float maxMillisecondsPerFrame)
    {
        if (_state == TaskState.Init)
        {
            NativeTrafficGraph graph = NativeTrafficGraph.Instance;

            // Fail out if graph isn't built or vehicle died
            if (graph == null || !graph.IsReady || _requester == null || !_requester.gameObject.activeInHierarchy)
            {
                _onComplete?.Invoke(null);
                return true; 
            }

            // CRITICAL FIX: Fetch the starting position dynamically AT THE EXACT MOMENT the task executes.
            // Because this task sat in the queue while the traffic graph generated, the vehicle 
            // kept driving. Capturing this now ensures we don't use a stale, out-of-bounds position!
            Vector3 dynamicStartPos = _requester.GetPathingOrigin();

            // Find closest lanes on the Main Thread (Fast spatial check)
            List<TrafficEdge> startEdges = TrafficSystemBackend.Instance.GetClosestLanes(dynamicStartPos, 3f, 2);
            List<TrafficEdge> endEdges = TrafficSystemBackend.Instance.GetClosestLanes(_targetPos, 3f, 2);

            if (startEdges.Count == 0 || endEdges.Count == 0)
            {
                _onComplete?.Invoke(null);
                return true;
            }

            // Map Objects to Native Indices
            _startEdgeIndices = new NativeArray<int>(startEdges.Count, Allocator.TempJob);
            for (int i = 0; i < startEdges.Count; i++) _startEdgeIndices[i] = graph.EdgeToIndex[startEdges[i]];

            _endEdgeIndices = new NativeArray<int>(endEdges.Count, Allocator.TempJob);
            for (int i = 0; i < endEdges.Count; i++) _endEdgeIndices[i] = graph.EdgeToIndex[endEdges[i]];

            _resultWaypoints = new NativeList<float3>(Allocator.TempJob);

            // Schedule the Burst Job
            VehiclePathfindingJob job = new VehiclePathfindingJob
            {
                Nodes = graph.Nodes,
                Edges = graph.Edges,
                Waypoints = graph.Waypoints,
                NodeOutConnections = graph.NodeOutConnections,
                StartEdgeIndices = _startEdgeIndices,
                EndEdgeIndices = _endEdgeIndices,
                StartPos = dynamicStartPos, // Feed the job the dynamic position
                TargetPos = _targetPos,
                ResultWaypoints = _resultWaypoints
            };

            _jobHandle = job.Schedule();
            
            SimulationTaskManager.Instance.RegisterJob(_jobHandle);
            _state = TaskState.Wait;
        }

        if (_state == TaskState.Wait)
        {
            if (!_jobHandle.IsCompleted) return false;
            _state = TaskState.Complete;
        }

        if (_state == TaskState.Complete)
        {
            _jobHandle.Complete();

            Queue<Vector3> finalPath = null;
            if (_resultWaypoints.Length > 0)
            {
                finalPath = new Queue<Vector3>(_resultWaypoints.Length);
                for (int i = 0; i < _resultWaypoints.Length; i++)
                {
                    finalPath.Enqueue(_resultWaypoints[i]);
                }
            }

            _startEdgeIndices.Dispose();
            _endEdgeIndices.Dispose();
            _resultWaypoints.Dispose();

            if (_requester != null && _requester.gameObject.activeInHierarchy)
            {
                _onComplete?.Invoke(finalPath);
            }

            return true; 
        }

        return false;
    }
}