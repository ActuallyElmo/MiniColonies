public enum IntersectionBuilderStage
{
    Overview,
    Intersection,
    Entry,
    Drawing
}

/// <summary>Navigation only. Connections remain owned by RoadSystemBackend.</summary>
public sealed class IntersectionBuilderSession
{
    public IntersectionBuilderStage Stage { get; private set; }
    public ControlledNodeId Intersection { get; private set; }
    public LaneId Entry { get; private set; }

    public bool Focus(TrafficGraphSnapshot graph, ControlledNodeId intersection)
    {
        if (graph == null || !graph.TryGetControlledNode(intersection, out var node) ||
            node.NodeKind != RoadNodeKind.Intersection) return false;
        Intersection = intersection;
        Entry = LaneId.Invalid;
        Stage = IntersectionBuilderStage.Intersection;
        return true;
    }

    public bool SelectEntry(TrafficGraphSnapshot graph, LaneId entry)
    {
        if (Stage == IntersectionBuilderStage.Overview || Stage == IntersectionBuilderStage.Drawing ||
            graph == null || !graph.TryGetControlledNode(Intersection, out var node) ||
            !IntersectionConnectionEditing.Contains(node.IncomingLaneIds, entry)) return false;
        Stage = Stage == IntersectionBuilderStage.Entry && Entry == entry
            ? IntersectionBuilderStage.Drawing : IntersectionBuilderStage.Entry;
        Entry = entry;
        return true;
    }

    public void CompleteConnection()
    {
        if (Stage == IntersectionBuilderStage.Drawing) Stage = IntersectionBuilderStage.Entry;
    }

    public MovementRecord FindEditableConnection(TrafficGraphSnapshot graph, LaneId exit)
    {
        if (Stage != IntersectionBuilderStage.Entry || graph == null ||
            !graph.TryGetControlledNode(Intersection, out var node)) return null;
        for (int i = 0; i < node.MovementIds.Count; i++)
        {
            if (graph.TryGetMovement(node.MovementIds[i], out var movement) &&
                movement.Kind == TrafficMovementKind.Intersection &&
                movement.SourceLaneId == Entry && movement.TargetLaneId == exit) return movement;
        }
        return null;
    }

    public void Back()
    {
        switch (Stage)
        {
            case IntersectionBuilderStage.Drawing:
                Stage = IntersectionBuilderStage.Entry;
                break;
            case IntersectionBuilderStage.Entry:
                Entry = LaneId.Invalid;
                Stage = IntersectionBuilderStage.Intersection;
                break;
            default:
                Reset();
                break;
        }
    }

    public void Reconcile(TrafficGraphSnapshot graph)
    {
        if (Stage == IntersectionBuilderStage.Overview) return;
        if (graph == null || !graph.TryGetControlledNode(Intersection, out var node))
        {
            Reset();
            return;
        }
        // Never continue a mouse gesture across a topology publication.
        CompleteConnection();
        if (Entry.IsValid && !IntersectionConnectionEditing.Contains(node.IncomingLaneIds, Entry))
        {
            Entry = LaneId.Invalid;
            Stage = IntersectionBuilderStage.Intersection;
        }
    }

    public void Reset()
    {
        Stage = IntersectionBuilderStage.Overview;
        Intersection = ControlledNodeId.Invalid;
        Entry = LaneId.Invalid;
    }
}
