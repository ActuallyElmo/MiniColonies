using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;
using Unity.Jobs;

public interface ISimulationTask
{
    bool Process(Stopwatch timer, float maxMillisecondsPerFrame);
}

public class SimulationTaskManager : MonoBehaviour
{
    public static SimulationTaskManager Instance { get; private set; }

    [Header("Main Thread Settings")]
    [Tooltip("Maximum milliseconds allowed per frame for main-thread tasks.")]
    public float maxMillisecondsPerFrame = 5f;

    private Queue<ISimulationTask> _taskQueue = new Queue<ISimulationTask>();
    private Stopwatch _frameTimer = new Stopwatch();
    private List<JobHandle> _activeJobHandles = new List<JobHandle>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void EnqueueTask(ISimulationTask task)
    {
        _taskQueue.Enqueue(task);
    }

    private void Update()
    {
        if (_taskQueue.Count == 0) return;

        _frameTimer.Restart();

        // FIX: Use .Elapsed.TotalMilliseconds for decimal precision!
        while (_taskQueue.Count > 0 && _frameTimer.Elapsed.TotalMilliseconds < maxMillisecondsPerFrame)
        {
            ISimulationTask currentTask = _taskQueue.Peek();
            
            bool isFinished = currentTask.Process(_frameTimer, maxMillisecondsPerFrame);

            if (isFinished) _taskQueue.Dequeue();
            else break; // Yields exactly when the decimal hits 5.0ms
        }

        _frameTimer.Stop();
    }

    public void RegisterJob(JobHandle handle)
    {
        _activeJobHandles.Add(handle);
    }

    private void LateUpdate()
    {
        for (int i = _activeJobHandles.Count - 1; i >= 0; i--)
        {
            if (_activeJobHandles[i].IsCompleted) _activeJobHandles.RemoveAt(i);
        }
    }

    private void OnDestroy()
    {
        foreach (var handle in _activeJobHandles) handle.Complete();
        _activeJobHandles.Clear();
    }
}