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

    private readonly Queue<ISimulationTask> _taskQueue = new Queue<ISimulationTask>();
    private readonly Stopwatch _frameTimer = new Stopwatch();
    private readonly List<JobHandle> _activeJobHandles = new List<JobHandle>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void EnqueueTask(ISimulationTask task)
    {
        if (task == null)
        {
            UnityEngine.Debug.LogWarning(
                "Cannot enqueue a null simulation task.");
            return;
        }

        _taskQueue.Enqueue(task);
    }

    private void Update()
    {
        if (_taskQueue.Count == 0) return;

        _frameTimer.Restart();
        float frameBudgetMilliseconds = Mathf.Max(
            0.01f,
            maxMillisecondsPerFrame);

        while (_taskQueue.Count > 0 &&
               _frameTimer.Elapsed.TotalMilliseconds < frameBudgetMilliseconds)
        {
            ISimulationTask currentTask = _taskQueue.Peek();
            
            bool isFinished = currentTask.Process(
                _frameTimer,
                frameBudgetMilliseconds);

            if (isFinished) _taskQueue.Dequeue();
            else break; // Yields exactly when the decimal hits 5.0ms
        }

        _frameTimer.Stop();
    }

    public void RegisterJob(JobHandle handle)
    {
        _activeJobHandles.Add(handle);
    }

    public void CompleteActiveJobs()
    {
        foreach (JobHandle handle in _activeJobHandles)
        {
            handle.Complete();
        }

        _activeJobHandles.Clear();
    }

    private void LateUpdate()
    {
        for (int i = _activeJobHandles.Count - 1; i >= 0; i--)
        {
            if (!_activeJobHandles[i].IsCompleted) continue;

            _activeJobHandles[i].Complete();
            _activeJobHandles.RemoveAt(i);
        }
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        CompleteActiveJobs();
        foreach (ISimulationTask task in _taskQueue)
        {
            if (task is System.IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _taskQueue.Clear();
        Instance = null;
    }
}
