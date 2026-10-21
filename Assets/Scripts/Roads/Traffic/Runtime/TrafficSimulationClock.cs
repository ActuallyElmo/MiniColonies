using UnityEngine;

public sealed class TrafficSimulationClock
{
    private readonly float _fixedDeltaSeconds;
    private readonly int _maxStepsPerFrame;
    private double _accumulator;

    public TrafficSimulationClock(
        float fixedDeltaSeconds = 0.05f,
        int maxStepsPerFrame = 4)
    {
        _fixedDeltaSeconds = Mathf.Max(0.001f, fixedDeltaSeconds);
        _maxStepsPerFrame = Mathf.Max(1, maxStepsPerFrame);
    }

    public float FixedDeltaSeconds => _fixedDeltaSeconds;
    public float InterpolationAlpha => Mathf.Clamp01((float)(_accumulator / _fixedDeltaSeconds));

    public int Accumulate(float frameDeltaSeconds)
    {
        if (float.IsNaN(frameDeltaSeconds) || float.IsInfinity(frameDeltaSeconds))
            return 0;
        _accumulator += Mathf.Max(0f, frameDeltaSeconds);
        int steps = 0;
        while (_accumulator >= _fixedDeltaSeconds &&
               steps < _maxStepsPerFrame)
        {
            _accumulator -= _fixedDeltaSeconds;
            steps++;
        }

        if (steps == _maxStepsPerFrame)
        {
            _accumulator = System.Math.Min(_accumulator, _fixedDeltaSeconds);
        }

        return steps;
    }
}
