using System.Diagnostics;

public class TrafficGenerationTask : ISimulationTask
{
    private readonly RoadNetworkSnapshot _roadSnapshot;
    private readonly TrafficGraphCompiler _graphCompiler;

    public TrafficGenerationTask(
        RoadNetworkSnapshot roadSnapshot,
        TrafficGraphVersion targetGraphVersion)
    {
        _roadSnapshot = roadSnapshot;
        _graphCompiler = new TrafficGraphCompiler(
            roadSnapshot,
            targetGraphVersion);
    }

    public bool Process(Stopwatch timer, float maxMillisecondsPerFrame)
    {
        if (!_graphCompiler.ProcessNextStage()) return false;

        if (TrafficSystemBackend.Instance != null)
        {
            TrafficSystemBackend.Instance.TryApplyNewNetwork(
                _roadSnapshot,
                _graphCompiler.Result,
                _graphCompiler.Diagnostics);
        }

        return true;
    }
}
