using System;
using System.Threading.Tasks;

public enum TrafficGraphCompilerStage
{
    Normalize,
    Classify,
    GenerateLanes,
    GenerateLegalMovements,
    BuildMovementGeometry,
    Validate,
    Publish,
    Complete
}

/// <summary>
/// Pure, incremental compiler. Runtime calls schedule one stage at a time as
/// background work and poll completion, while TryCompile keeps a synchronous
/// path for deterministic tests and tools.
/// </summary>
public sealed class TrafficGraphCompiler
{
    private readonly TrafficGraphCompilationContext _context;
    private Task _activeStageTask;

    public TrafficGraphCompilerStage Stage { get; private set; }
    public TrafficGraphSnapshot Result { get; private set; }
    public TrafficDiagnosticCollection Diagnostics => _context.Diagnostics;
    public bool IsComplete => Stage == TrafficGraphCompilerStage.Complete;
    public bool Succeeded => IsComplete && Result != null && !Diagnostics.HasErrors;

    public TrafficGraphCompiler(
        RoadNetworkSnapshot source,
        TrafficGraphVersion targetVersion)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (!targetVersion.IsValid)
        {
            throw new ArgumentException(
                "A valid target graph version is required.",
                nameof(targetVersion));
        }

        _context = new TrafficGraphCompilationContext(source, targetVersion);
        Stage = TrafficGraphCompilerStage.Normalize;
    }

    public bool ProcessNextStage()
    {
        return ProcessNextStage(false);
    }

    private bool ProcessNextStage(bool runSynchronously)
    {
        if (IsComplete) return true;

        if (_activeStageTask != null)
        {
            if (!_activeStageTask.IsCompleted) return false;

            if (_activeStageTask.IsFaulted)
            {
                Exception failure = _activeStageTask.Exception.GetBaseException();
                _context.Diagnostics.AddError(
                    TrafficDiagnosticCode.CompilerStageFailed,
                    failure.Message);
                Stage = TrafficGraphCompilerStage.Complete;
                _activeStageTask = null;
                return true;
            }

            _activeStageTask.GetAwaiter().GetResult();
            _activeStageTask = null;
            AdvanceAfterCurrentStage();
            return IsComplete;
        }

        if (runSynchronously)
        {
            RunCurrentStage();
            AdvanceAfterCurrentStage();
            return IsComplete;
        }

        _activeStageTask = Task.Run(RunCurrentStage);
        return false;
    }

    private void RunCurrentStage()
    {
        switch (Stage)
        {
            case TrafficGraphCompilerStage.Normalize:
                TrafficGraphCompilerStages.Normalize(_context);
                break;
            case TrafficGraphCompilerStage.Classify:
                TrafficGraphCompilerStages.Classify(_context);
                break;
            case TrafficGraphCompilerStage.GenerateLanes:
                TrafficGraphCompilerStages.GenerateLanes(_context);
                break;
            case TrafficGraphCompilerStage.GenerateLegalMovements:
                TrafficGraphCompilerStages.GenerateLegalMovements(_context);
                break;
            case TrafficGraphCompilerStage.BuildMovementGeometry:
                TrafficGraphCompilerStages.BuildMovementGeometry(_context);
                break;
            case TrafficGraphCompilerStage.Validate:
                TrafficGraphCompilerStages.Validate(_context);
                break;
            case TrafficGraphCompilerStage.Publish:
                if (!_context.Diagnostics.HasErrors)
                {
                    TrafficGraphSnapshot candidate =
                        TrafficGraphCompilerStages.Publish(_context);
                    if (TrafficGraphValidator.Validate(
                            candidate,
                            _context.Diagnostics))
                    {
                        Result = candidate;
                    }
                }
                break;
        }
    }

    private void AdvanceAfterCurrentStage()
    {
        switch (Stage)
        {
            case TrafficGraphCompilerStage.Normalize:
                Stage = TrafficGraphCompilerStage.Classify;
                break;
            case TrafficGraphCompilerStage.Classify:
                Stage = TrafficGraphCompilerStage.GenerateLanes;
                break;
            case TrafficGraphCompilerStage.GenerateLanes:
                Stage = TrafficGraphCompilerStage.GenerateLegalMovements;
                break;
            case TrafficGraphCompilerStage.GenerateLegalMovements:
                Stage = TrafficGraphCompilerStage.BuildMovementGeometry;
                break;
            case TrafficGraphCompilerStage.BuildMovementGeometry:
                Stage = TrafficGraphCompilerStage.Validate;
                break;
            case TrafficGraphCompilerStage.Validate:
                Stage = TrafficGraphCompilerStage.Publish;
                break;
            case TrafficGraphCompilerStage.Publish:
                Stage = TrafficGraphCompilerStage.Complete;
                break;
        }
    }

    public static bool TryCompile(
        RoadNetworkSnapshot source,
        TrafficGraphVersion targetVersion,
        out TrafficGraphSnapshot graph,
        out TrafficDiagnosticCollection diagnostics)
    {
        var compiler = new TrafficGraphCompiler(source, targetVersion);
        while (!compiler.ProcessNextStage(true))
        {
        }

        graph = compiler.Result;
        diagnostics = compiler.Diagnostics;
        return compiler.Succeeded;
    }
}
