using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class TrafficGraphCompilationContext
{
    public readonly RoadNetworkSnapshot Source;
    public readonly TrafficGraphVersion Version;
    public readonly TrafficDiagnosticCollection Diagnostics =
        new TrafficDiagnosticCollection();
    public readonly Dictionary<Vector2Int, NormalizedRoadCell> Cells =
        new Dictionary<Vector2Int, NormalizedRoadCell>();
    public readonly List<SectionDraft> Sections = new List<SectionDraft>();
    public readonly List<LaneDraft> Lanes = new List<LaneDraft>();
    public readonly List<MovementDraft> Movements = new List<MovementDraft>();
    public readonly List<TrafficGeometryRecord> Geometry =
        new List<TrafficGeometryRecord>();
    public readonly List<MovementRejectionRecord> Rejections =
        new List<MovementRejectionRecord>();
    public readonly List<TransitionBoundaryDraft> TransitionBoundaries =
        new List<TransitionBoundaryDraft>();
    public readonly List<LaneSegmentDraft> LaneSegments =
        new List<LaneSegmentDraft>();
    public readonly List<MovementOwnerRecord> MovementOwners =
        new List<MovementOwnerRecord>();
    public readonly List<ControlledNodeRecord> ControlledNodes =
        new List<ControlledNodeRecord>();
    public readonly List<BuildingPortAnchorRecord> PortAnchors =
        new List<BuildingPortAnchorRecord>();
    public readonly HashSet<Vector2Int> PortBoundaryCells =
        new HashSet<Vector2Int>();

    public TrafficGraphCompilationContext(
        RoadNetworkSnapshot source,
        TrafficGraphVersion version)
    {
        Source = source;
        Version = version;
    }
}

internal sealed class TransitionBoundaryDraft
{
    public ControlledNodeId Id;
    public Vector2Int OwnerCell;
    public Vector2Int OtherCell;
    public RoadProfileId ProfileA;
    public RoadProfileId ProfileB;
}

internal sealed class NormalizedRoadCell
{
    public RoadCellRecord Source;
    public RoadNodeKind NodeKind;
    public readonly List<int> LegDirectionBits = new List<int>();
}

internal sealed class SectionDraft
{
    public RoadSectionId Id;
    public RoadProfile Profile;
    public Vector2Int StartCell;
    public Vector2Int EndCell;
    public int StartLegDirectionBit;
    public int EndLegDirectionBit;
    public readonly List<Vector2Int> SourceCells = new List<Vector2Int>();
    public readonly List<Vector3> Centerline = new List<Vector3>();
    public TrafficGeometryId CenterlineGeometryId;
    public readonly List<LaneId> LaneIds = new List<LaneId>();
}

internal sealed class LaneDraft
{
    public LaneId Id;
    public SectionDraft Section;
    public TrafficLaneFlowDirection FlowDirection;
    public int LaneOrdinal;
    public int LaneCountInDirection;
    public Vector2Int StartAnchorCell;
    public Vector2Int EndAnchorCell;
    public int StartLegDirectionBit;
    public int EndLegDirectionBit;
    public RoadPermissionMask AllowedPermissions;
    public VehicleCapabilityMask AllowedCapabilities;
    public float SpeedLimitUnitsPerSecond;
    public TrafficGeometryId GeometryId;
    public readonly List<Vector3> Samples = new List<Vector3>();
    public readonly List<MovementId> OutgoingMovementIds = new List<MovementId>();
    public readonly List<LaneSegmentId> TraversalSegmentIds =
        new List<LaneSegmentId>();
}

internal sealed class MovementDraft
{
    public MovementId Id;
    public LaneDraft Source;
    public LaneDraft Target;
    public TrafficMovementKind Kind;
    public ControlledNodeId OwnerId;
    public Vector2Int OwnerCell;
    public int FromDirectionBit;
    public int ToDirectionBit;
    public TrafficTurnType TurnType;
    public RoadPermissionMask RequiredPermissions;
    public VehicleCapabilityMask RequiredCapabilities;
    public bool IsMandatory;
    public int PolicyPriority;
    public int ApproachLegMask;
    public TrafficGeometryId GeometryId;
    public float SourceDistanceUnits;
    public float TargetDistanceUnits;
    public LaneSegmentId SourceSegmentId;
    public LaneSegmentId TargetSegmentId;
}

internal sealed class LaneSegmentDraft
{
    public LaneSegmentId Id;
    public LaneDraft Lane;
    public int Ordinal;
    public float StartDistanceUnits;
    public float EndDistanceUnits;
    public TrafficGeometryId GeometryId;
}

internal static class TrafficGraphCompilerStages
{
    private const float NodePullback = 0.35f;
    private const int MovementCurveSamples = 12;
    private const int LaneCenterlineSmoothingIterations = 3;
    private const float LaneChangeLengthUnits = 0.8f;
    private const float MinimumLaneChangeLengthUnits = 0.45f;
    private const float LaneChangeEndpointMarginUnits = 0.25f;
    private const float LaneChangeEndpointWindowSpacingUnits = 0.35f;
    private const float LaneChangeIntersectionEntryClearanceUnits = 3.5f;
    private const float MinimumLaneChangeEndpointMarginUnits = 0.1f;

    public static void Normalize(TrafficGraphCompilationContext context)
    {
        var cells = new List<RoadCellRecord>(context.Source.Cells);
        cells.Sort((left, right) =>
            CompareCells(left.GridPosition, right.GridPosition));

        for (int i = 0; i < cells.Count; i++)
        {
            RoadCellRecord source = cells[i];
            var normalized = new NormalizedRoadCell
            {
                Source = source,
                NodeKind = source.NodeKind
            };

            for (int directionIndex = 0; directionIndex < 8; directionIndex++)
            {
                int directionBit = 1 << directionIndex;
                if (!source.HasPhysicalConnection(directionBit)) continue;

                Vector2Int neighbor = RoadGridDirectionUtility.GetNeighborPosition(
                    source.GridPosition,
                    directionBit);
                if (context.Source.TryGetCell(neighbor, out _))
                {
                    normalized.LegDirectionBits.Add(directionBit);
                }
            }

            context.Cells.Add(source.GridPosition, normalized);
        }

        var transitionOwnerCells = new HashSet<Vector2Int>();
        foreach (NormalizedRoadCell cell in SortedCells(context))
        {
            for (int i = 0; i < cell.LegDirectionBits.Count; i++)
            {
                int directionBit = cell.LegDirectionBits[i];
                Vector2Int neighborPosition =
                    RoadGridDirectionUtility.GetNeighborPosition(
                        cell.Source.GridPosition,
                        directionBit);
                if (CompareCells(cell.Source.GridPosition, neighborPosition) >= 0 ||
                    !context.Cells.TryGetValue(
                        neighborPosition,
                        out NormalizedRoadCell neighbor) ||
                    cell.Source.RoadProfileId == neighbor.Source.RoadProfileId ||
                    cell.Source.NodeKind == RoadNodeKind.Intersection ||
                    neighbor.Source.NodeKind == RoadNodeKind.Intersection)
                {
                    continue;
                }

                Vector2Int owner = cell.Source.GridPosition;
                string stableKey =
                    $"TRANSITION_BOUNDARY:{owner.x},{owner.y}|{neighborPosition.x},{neighborPosition.y}/PROFILES:{cell.Source.RoadProfileId.Value:X16}>{neighbor.Source.RoadProfileId.Value:X16}";
                context.TransitionBoundaries.Add(
                    new TransitionBoundaryDraft
                    {
                        Id = ControlledNodeId.FromStableKey(stableKey),
                        OwnerCell = owner,
                        OtherCell = neighborPosition,
                        ProfileA = cell.Source.RoadProfileId,
                        ProfileB = neighbor.Source.RoadProfileId
                    });
                transitionOwnerCells.Add(owner);
            }
        }

        foreach (NormalizedRoadCell cell in SortedCells(context))
        {
            if (cell.Source.NodeKind == RoadNodeKind.Intersection ||
                cell.Source.NodeKind == RoadNodeKind.RoadEnd)
            {
                cell.NodeKind = cell.Source.NodeKind;
            }
            else
            {
                cell.NodeKind =
                    transitionOwnerCells.Contains(cell.Source.GridPosition)
                    ? RoadNodeKind.Transition
                    : RoadNodeKind.ThroughRoad;
            }
        }

        context.TransitionBoundaries.Sort(
            (left, right) => left.Id.CompareTo(right.Id));

        for (int i = 0; i < context.Source.BuildingPorts.Count; i++)
        {
            BuildingPortRecord port = context.Source.BuildingPorts[i];
            context.PortBoundaryCells.Add(port.PortCell);
        }

        foreach (NormalizedRoadCell cell in SortedCells(context))
        {
            if (!context.PortBoundaryCells.Contains(cell.Source.GridPosition) ||
                cell.NodeKind != RoadNodeKind.ThroughRoad)
            {
                continue;
            }

            cell.NodeKind = RoadNodeKind.Transition;
        }
    }

    public static void Classify(TrafficGraphCompilationContext context)
    {
        var visitedEdges = new HashSet<string>(StringComparer.Ordinal);
        List<NormalizedRoadCell> cells = SortedCells(context);

        for (int i = 0; i < cells.Count; i++)
        {
            NormalizedRoadCell start = cells[i];
            if (!IsBoundary(start)) continue;

            start.LegDirectionBits.Sort();
            for (int legIndex = 0;
                 legIndex < start.LegDirectionBits.Count;
                 legIndex++)
            {
                int directionBit = start.LegDirectionBits[legIndex];
                Vector2Int neighbor = RoadGridDirectionUtility.GetNeighborPosition(
                    start.Source.GridPosition,
                    directionBit);
                string firstEdgeKey =
                    GetUndirectedEdgeKey(start.Source.GridPosition, neighbor);
                if (visitedEdges.Contains(firstEdgeKey)) continue;

                List<Vector2Int> path = TraceSection(
                    context,
                    start.Source.GridPosition,
                    neighbor,
                    visitedEdges);
                AddSection(context, path);
            }
        }

        // Closed loops contain no semantic boundary. Start each remaining
        // component from its stable lowest cell and preserve it as one section.
        for (int i = 0; i < cells.Count; i++)
        {
            NormalizedRoadCell start = cells[i];
            start.LegDirectionBits.Sort();
            for (int legIndex = 0;
                 legIndex < start.LegDirectionBits.Count;
                 legIndex++)
            {
                Vector2Int neighbor = RoadGridDirectionUtility.GetNeighborPosition(
                    start.Source.GridPosition,
                    start.LegDirectionBits[legIndex]);
                string edgeKey =
                    GetUndirectedEdgeKey(start.Source.GridPosition, neighbor);
                if (visitedEdges.Contains(edgeKey)) continue;

                AddSection(
                    context,
                    TraceSection(
                        context,
                        start.Source.GridPosition,
                        neighbor,
                        visitedEdges));
            }
        }

        context.Sections.Sort((left, right) => left.Id.CompareTo(right.Id));
    }

    public static void GenerateLanes(TrafficGraphCompilationContext context)
    {
        for (int i = 0; i < context.Sections.Count; i++)
        {
            SectionDraft section = context.Sections[i];
            bool forwardLegal = IsPathLegal(context, section.SourceCells, false);
            bool reverseLegal = IsPathLegal(context, section.SourceCells, true);

            if (section.Profile.Directionality == RoadFlowDirectionality.OneWay)
            {
                if (forwardLegal)
                {
                    AddDirectionalLanes(
                        context,
                        section,
                        TrafficLaneFlowDirection.SectionStartToEnd,
                        section.Profile.ForwardLaneCount);
                }
                else if (reverseLegal)
                {
                    AddDirectionalLanes(
                        context,
                        section,
                        TrafficLaneFlowDirection.SectionEndToStart,
                        section.Profile.ForwardLaneCount);
                }
                else
                {
                    context.Diagnostics.AddError(
                        TrafficDiagnosticCode.IllegalDirectionMovement,
                        "A one-way road section has no legal flow direction.",
                        TrafficDiagnosticSource.ForCell(
                            context.Version,
                            section.StartCell,
                            section.Id.ToString()));
                }
            }
            else
            {
                if (!forwardLegal || !reverseLegal)
                {
                    context.Diagnostics.AddError(
                        TrafficDiagnosticCode.IllegalDirectionMovement,
                        "A two-way road section must have legal flow in both directions.",
                        TrafficDiagnosticSource.ForCell(
                            context.Version,
                            section.StartCell,
                            section.Id.ToString()));
                    continue;
                }

                if (forwardLegal)
                {
                    AddDirectionalLanes(
                        context,
                        section,
                        TrafficLaneFlowDirection.SectionStartToEnd,
                        section.Profile.ForwardLaneCount);
                }

                if (reverseLegal)
                {
                    AddDirectionalLanes(
                        context,
                        section,
                        TrafficLaneFlowDirection.SectionEndToStart,
                        section.Profile.ReverseLaneCount);
                }
            }
        }

        context.Lanes.Sort((left, right) => left.Id.CompareTo(right.Id));
    }

    public static void GenerateLegalMovements(
        TrafficGraphCompilationContext context)
    {
        var incoming = BuildLaneIndexByCell(context.Lanes, true);
        var outgoing = BuildLaneIndexByCell(context.Lanes, false);
        var cells = new List<Vector2Int>(incoming.Keys);
        foreach (Vector2Int cell in outgoing.Keys)
        {
            if (!incoming.ContainsKey(cell)) cells.Add(cell);
        }
        cells.Sort(CompareCells);

        for (int i = 0; i < cells.Count; i++)
        {
            Vector2Int cellPosition = cells[i];
            incoming.TryGetValue(cellPosition, out List<LaneDraft> incomingLanes);
            outgoing.TryGetValue(cellPosition, out List<LaneDraft> outgoingLanes);
            incomingLanes = incomingLanes ?? new List<LaneDraft>();
            outgoingLanes = outgoingLanes ?? new List<LaneDraft>();

            if (!context.Cells.TryGetValue(
                    cellPosition,
                    out NormalizedRoadCell cell))
            {
                continue;
            }

            switch (cell.NodeKind)
            {
                case RoadNodeKind.ThroughRoad:
                    // A boundary-free closed loop receives one stable synthetic
                    // section anchor; reconnect it as ordinary lane continuity.
                    GenerateTransitionMovements(
                        context,
                        cellPosition,
                        incomingLanes,
                        outgoingLanes);
                    break;
                case RoadNodeKind.Transition:
                    GenerateTransitionMovements(
                        context,
                        cellPosition,
                        incomingLanes,
                        outgoingLanes);
                    break;
                case RoadNodeKind.Intersection:
                    GenerateIntersectionMovements(
                        context,
                        cellPosition,
                        incomingLanes,
                        outgoingLanes);
                    break;
                case RoadNodeKind.RoadEnd:
                    GenerateRoadEndMovements(
                        context,
                        cellPosition,
                        incomingLanes,
                        outgoingLanes);
                    break;
            }
        }

        GenerateSectionLaneChanges(context);
        context.Movements.Sort((left, right) => left.Id.CompareTo(right.Id));
        context.Rejections.Sort(CompareRejections);
    }

    public static void BuildMovementGeometry(
        TrafficGraphCompilationContext context)
    {
        for (int i = 0; i < context.Movements.Count; i++)
        {
            MovementDraft movement = context.Movements[i];
            string geometryKey =
                $"MOVEMENT_GEOMETRY:{movement.Id.Value:X16}";
            movement.GeometryId = TrafficGeometryId.FromStableKey(geometryKey);

            List<Vector3> samples = BuildMovementCurve(
                movement);
            context.Geometry.Add(
                new TrafficGeometryRecord(movement.GeometryId, samples));
        }

        BuildLaneSegments(context);
        AssignMovementLaneSegments(context);
        context.ControlledNodes.AddRange(BuildControlledNodes(context));
        context.MovementOwners.AddRange(BuildMovementOwners(context));
        context.PortAnchors.AddRange(BuildPortAnchors(context));
        context.Geometry.Sort((left, right) => left.Id.CompareTo(right.Id));
    }

    public static void Validate(TrafficGraphCompilationContext context)
    {
        var sectionIds = new HashSet<RoadSectionId>();
        var laneIds = new HashSet<LaneId>();
        var movementIds = new HashSet<MovementId>();
        var geometryIds = new HashSet<TrafficGeometryId>();
        var controlledNodeIds = new HashSet<ControlledNodeId>();
        var ownerIds = new HashSet<ControlledNodeId>();
        var laneSegmentIds = new HashSet<LaneSegmentId>();
        var portAnchorIds = new HashSet<BuildingPortAnchorId>();

        for (int i = 0; i < context.Sections.Count; i++)
        {
            SectionDraft section = context.Sections[i];
            if (!section.Id.IsValid || !sectionIds.Add(section.Id))
            {
                AddDuplicateIdError(context, section.StartCell, section.Id.ToString());
            }

            if (!IsDirectionBit(section.StartLegDirectionBit) ||
                !IsDirectionBit(section.EndLegDirectionBit))
            {
                context.Diagnostics.AddError(
                    TrafficDiagnosticCode.InvalidLegDirection,
                    "Road section immediate leg direction is invalid.",
                    TrafficDiagnosticSource.ForCell(
                        context.Version,
                        section.StartCell,
                        section.Id.ToString()));
            }
        }

        for (int i = 0; i < context.Geometry.Count; i++)
        {
            TrafficGeometryRecord geometry = context.Geometry[i];
            if (!geometry.Id.IsValid || !geometryIds.Add(geometry.Id))
            {
                AddDuplicateIdError(context, Vector2Int.zero, geometry.Id.ToString());
            }

            if (geometry.Samples.Count < 2 ||
                geometry.LengthUnits <= 0f ||
                float.IsNaN(geometry.LengthUnits) ||
                float.IsInfinity(geometry.LengthUnits))
            {
                context.Diagnostics.AddError(
                    TrafficDiagnosticCode.MissingLaneReference,
                    "Drivable geometry must contain a positive finite curve.",
                    new TrafficDiagnosticSource(
                        context.Version,
                        geometry.Id.ToString(),
                        string.Empty,
                        Vector2Int.zero,
                        false,
                        VehicleSimulationId.Invalid));
            }
        }

        for (int i = 0; i < context.Lanes.Count; i++)
        {
            LaneDraft lane = context.Lanes[i];
            if (!lane.Id.IsValid || !laneIds.Add(lane.Id))
            {
                AddDuplicateIdError(context, lane.StartAnchorCell, lane.Id.ToString());
            }

            if (!sectionIds.Contains(lane.Section.Id) ||
                !geometryIds.Contains(lane.GeometryId))
            {
                context.Diagnostics.AddError(
                    TrafficDiagnosticCode.MissingLaneReference,
                    "Lane references a missing section or lane geometry record.",
                    TrafficDiagnosticSource.ForCell(
                        context.Version,
                        lane.StartAnchorCell,
                        lane.Id.ToString()));
            }

            if (!IsDirectionBit(lane.StartLegDirectionBit) ||
                !IsDirectionBit(lane.EndLegDirectionBit))
            {
                context.Diagnostics.AddError(
                    TrafficDiagnosticCode.InvalidLegDirection,
                    "Lane immediate approach metadata is invalid.",
                    TrafficDiagnosticSource.ForCell(
                        context.Version,
                        lane.StartAnchorCell,
                        lane.Id.ToString()));
            }
        }

        for (int i = 0; i < context.MovementOwners.Count; i++)
        {
            MovementOwnerRecord owner = context.MovementOwners[i];
            if (!owner.Id.IsValid || !ownerIds.Add(owner.Id))
            {
                AddDuplicateIdError(context, owner.PrimaryCell, owner.Id.ToString());
            }
        }

        for (int i = 0; i < context.ControlledNodes.Count; i++)
        {
            ControlledNodeRecord node = context.ControlledNodes[i];
            if (!node.Id.IsValid || !controlledNodeIds.Add(node.Id))
            {
                AddDuplicateIdError(context, node.GridPosition, node.Id.ToString());
            }
        }

        for (int i = 0; i < context.LaneSegments.Count; i++)
        {
            LaneSegmentDraft segment = context.LaneSegments[i];
            if (!segment.Id.IsValid || !laneSegmentIds.Add(segment.Id))
            {
                AddDuplicateIdError(
                    context,
                    segment.Lane != null
                        ? segment.Lane.StartAnchorCell
                        : Vector2Int.zero,
                    segment.Id.ToString());
            }

            if (segment.Lane == null ||
                !laneIds.Contains(segment.Lane.Id) ||
                !geometryIds.Contains(segment.GeometryId) ||
                segment.EndDistanceUnits <= segment.StartDistanceUnits)
            {
                context.Diagnostics.AddError(
                    TrafficDiagnosticCode.MissingLaneReference,
                    "Lane traversal segment references missing lane data or invalid distance bounds.",
                    TrafficDiagnosticSource.ForCell(
                        context.Version,
                        segment.Lane != null
                            ? segment.Lane.StartAnchorCell
                            : Vector2Int.zero,
                        segment.Id.ToString()));
            }
        }

        for (int i = 0; i < context.PortAnchors.Count; i++)
        {
            BuildingPortAnchorRecord anchor = context.PortAnchors[i];
            if (!anchor.Id.IsValid || !portAnchorIds.Add(anchor.Id))
            {
                AddDuplicateIdError(context, anchor.PortCell, anchor.Id.ToString());
            }
        }

        for (int i = 0; i < context.Movements.Count; i++)
        {
            MovementDraft movement = context.Movements[i];
            if (!movement.Id.IsValid || !movementIds.Add(movement.Id))
            {
                AddDuplicateIdError(
                    context,
                    movement.OwnerCell,
                    movement.Id.ToString());
            }

            if (!laneIds.Contains(movement.Source.Id) ||
                !laneIds.Contains(movement.Target.Id) ||
                !geometryIds.Contains(movement.GeometryId))
            {
                context.Diagnostics.AddError(
                    TrafficDiagnosticCode.MissingLaneReference,
                    "Movement references a missing lane or geometry record.",
                    TrafficDiagnosticSource.ForCell(
                        context.Version,
                        movement.OwnerCell,
                        movement.Id.ToString()));
            }

            if (!IsDirectionBit(movement.FromDirectionBit) ||
                !IsDirectionBit(movement.ToDirectionBit))
            {
                context.Diagnostics.AddError(
                    TrafficDiagnosticCode.InvalidLegDirection,
                    "Movement has invalid immediate leg metadata.",
                    TrafficDiagnosticSource.ForCell(
                        context.Version,
                        movement.OwnerCell,
                        movement.Id.ToString()));
            }

            if (!ownerIds.Contains(movement.OwnerId))
            {
                context.Diagnostics.AddError(
                    TrafficDiagnosticCode.InvalidTransitionOwner,
                    "Movement references a missing stable movement owner.",
                    TrafficDiagnosticSource.ForCell(
                        context.Version,
                        movement.OwnerCell,
                        movement.OwnerId.ToString()));
            }

            if (!laneSegmentIds.Contains(movement.SourceSegmentId) ||
                !laneSegmentIds.Contains(movement.TargetSegmentId))
            {
                context.Diagnostics.AddError(
                    TrafficDiagnosticCode.MissingLaneReference,
                    "Movement references a missing lane traversal segment.",
                    TrafficDiagnosticSource.ForCell(
                        context.Version,
                        movement.OwnerCell,
                        movement.Id.ToString()));
            }
        }
    }

    public static TrafficGraphSnapshot Publish(
        TrafficGraphCompilationContext context)
    {
        var sections = new List<RoadSectionRecord>(context.Sections.Count);
        for (int i = 0; i < context.Sections.Count; i++)
        {
            SectionDraft draft = context.Sections[i];
            draft.LaneIds.Sort();
            sections.Add(new RoadSectionRecord(
                draft.Id,
                draft.StartCell,
                draft.EndCell,
                draft.StartLegDirectionBit,
                draft.EndLegDirectionBit,
                draft.Profile.Id,
                draft.CenterlineGeometryId,
                draft.Profile.AllowedPermissions,
                draft.Profile.AllowedCapabilities,
                draft.LaneIds,
                draft.SourceCells));
        }

        var lanes = new List<LaneRecord>(context.Lanes.Count);
        for (int i = 0; i < context.Lanes.Count; i++)
        {
            LaneDraft draft = context.Lanes[i];
            draft.OutgoingMovementIds.Sort();
            draft.TraversalSegmentIds.Sort();
            lanes.Add(new LaneRecord(
                draft.Id,
                draft.Section.Id,
                draft.FlowDirection,
                draft.LaneOrdinal,
                draft.LaneCountInDirection,
                draft.StartAnchorCell,
                draft.EndAnchorCell,
                draft.StartLegDirectionBit,
                draft.EndLegDirectionBit,
                draft.AllowedPermissions,
                draft.AllowedCapabilities,
                draft.SpeedLimitUnitsPerSecond,
                draft.GeometryId,
                draft.OutgoingMovementIds,
                draft.TraversalSegmentIds));
        }

        var movements = new List<MovementRecord>(context.Movements.Count);
        for (int i = 0; i < context.Movements.Count; i++)
        {
            MovementDraft draft = context.Movements[i];
            movements.Add(new MovementRecord(
                draft.Id,
                draft.Source.Id,
                draft.Target.Id,
                draft.Kind,
                draft.OwnerId,
                draft.OwnerCell,
                draft.FromDirectionBit,
                draft.ToDirectionBit,
                draft.TurnType,
                draft.RequiredPermissions,
                draft.RequiredCapabilities,
                draft.GeometryId,
                draft.SourceSegmentId,
                draft.TargetSegmentId,
                draft.SourceDistanceUnits,
                draft.TargetDistanceUnits,
                draft.IsMandatory,
                draft.PolicyPriority,
                draft.ApproachLegMask));
        }

        return new TrafficGraphSnapshot(
            context.Version,
            context.Source.RoadAuthoringRevision,
            context.Source.BuildingAuthoringRevision,
            sections,
            lanes,
            movements,
            context.ControlledNodes,
            context.MovementOwners,
            BuildLaneSegmentRecords(context),
            context.Geometry,
            context.PortAnchors,
            context.Rejections);
    }

    private static List<Vector2Int> TraceSection(
        TrafficGraphCompilationContext context,
        Vector2Int start,
        Vector2Int firstNeighbor,
        HashSet<string> visitedEdges)
    {
        var path = new List<Vector2Int> { start };
        Vector2Int previous = start;
        Vector2Int current = firstNeighbor;
        int safety = context.Cells.Count + 1;

        while (safety-- > 0 && context.Cells.TryGetValue(
                   current,
                   out NormalizedRoadCell currentCell))
        {
            visitedEdges.Add(GetUndirectedEdgeKey(previous, current));
            path.Add(current);
            if (IsBoundary(currentCell) || current == start) break;

            Vector2Int next = current;
            currentCell.LegDirectionBits.Sort();
            for (int i = 0; i < currentCell.LegDirectionBits.Count; i++)
            {
                Vector2Int candidate =
                    RoadGridDirectionUtility.GetNeighborPosition(
                        current,
                        currentCell.LegDirectionBits[i]);
                if (candidate != previous)
                {
                    next = candidate;
                    break;
                }
            }

            if (next == current) break;
            previous = current;
            current = next;
        }

        return path;
    }

    private static void AddSection(
        TrafficGraphCompilationContext context,
        List<Vector2Int> path)
    {
        if (path == null || path.Count < 2) return;
        CanonicalizePath(path);

        RoadProfile profile = ResolveSectionProfile(context, path);
        if (profile == null)
        {
            context.Diagnostics.AddError(
                TrafficDiagnosticCode.MissingRoadProfile,
                "A normalized road section has no compiled road profile.",
                TrafficDiagnosticSource.ForCell(
                    context.Version,
                    path[0]));
            return;
        }

        string pathKey = BuildPathKey(path);
        var section = new SectionDraft
        {
            Id = RoadSectionId.FromStableKey(
                $"SECTION:{pathKey}/PROFILE:{profile.Id.Value:X16}"),
            Profile = profile,
            StartCell = path[0],
            EndCell = path[path.Count - 1],
            StartLegDirectionBit =
                RoadGridDirectionUtility.GetDirectionBit(path[0], path[1]),
            EndLegDirectionBit =
                RoadGridDirectionUtility.GetDirectionBit(
                    path[path.Count - 1],
                    path[path.Count - 2])
        };
        section.SourceCells.AddRange(path);

        for (int i = 0; i < path.Count; i++)
        {
            section.Centerline.Add(context.Cells[path[i]].Source.WorldCenter);
        }

        if (section.Centerline.Count >= 2 &&
            IsBoundary(context.Cells[section.StartCell]))
        {
            section.Centerline[0] = Vector3.Lerp(
                section.Centerline[0],
                section.Centerline[1],
                NodePullback);
        }

        int last = section.Centerline.Count - 1;
        if (section.Centerline.Count >= 2 &&
            IsBoundary(context.Cells[section.EndCell]))
        {
            section.Centerline[last] = Vector3.Lerp(
                section.Centerline[last],
                section.Centerline[last - 1],
                NodePullback);
        }

        ApplyChaikinInPlace(
            section.Centerline,
            LaneCenterlineSmoothingIterations);

        section.CenterlineGeometryId = TrafficGeometryId.FromStableKey(
            $"SECTION_CENTERLINE:{section.Id.Value:X16}");
        context.Geometry.Add(
            new TrafficGeometryRecord(
                section.CenterlineGeometryId,
                section.Centerline));
        context.Sections.Add(section);
    }

    private static void AddDirectionalLanes(
        TrafficGraphCompilationContext context,
        SectionDraft section,
        TrafficLaneFlowDirection flowDirection,
        int laneCount)
    {
        if (laneCount <= 0) return;

        bool reverse =
            flowDirection == TrafficLaneFlowDirection.SectionEndToStart;
        for (int ordinal = 0; ordinal < laneCount; ordinal++)
        {
            string stableKey =
                $"LANE:SECTION:{section.Id.Value:X16}/FLOW:{(int)flowDirection}/ORDINAL:{ordinal}";
            var lane = new LaneDraft
            {
                Id = LaneId.FromStableKey(stableKey),
                Section = section,
                FlowDirection = flowDirection,
                LaneOrdinal = ordinal,
                LaneCountInDirection = laneCount,
                StartAnchorCell = reverse ? section.EndCell : section.StartCell,
                EndAnchorCell = reverse ? section.StartCell : section.EndCell,
                StartLegDirectionBit = reverse
                    ? section.EndLegDirectionBit
                    : section.StartLegDirectionBit,
                EndLegDirectionBit = reverse
                    ? section.StartLegDirectionBit
                    : section.EndLegDirectionBit,
                AllowedPermissions = section.Profile.AllowedPermissions,
                AllowedCapabilities = section.Profile.AllowedCapabilities,
                SpeedLimitUnitsPerSecond =
                    section.Profile.SpeedLimitUnitsPerSecond,
                GeometryId = TrafficGeometryId.FromStableKey(
                    $"LANE_GEOMETRY:{stableKey}")
            };

            lane.Samples.AddRange(
                BuildLaneSamples(
                    section,
                    reverse,
                    ordinal,
                    laneCount));
            context.Geometry.Add(
                new TrafficGeometryRecord(lane.GeometryId, lane.Samples));
            context.Lanes.Add(lane);
            section.LaneIds.Add(lane.Id);
        }
    }

    private static List<Vector3> BuildLaneSamples(
        SectionDraft section,
        bool reverse,
        int ordinal,
        int laneCount)
    {
        var source = new List<Vector3>(section.Centerline);
        if (reverse) source.Reverse();

        float laneWidth =
            section.Profile.RoadWidthUnits /
            Mathf.Max(1, section.Profile.TotalLaneCount);
        bool twoWay =
            section.Profile.Directionality == RoadFlowDirectionality.TwoWay;
        float lateralOffset = twoWay
            ? laneWidth * (ordinal + 0.5f)
            : laneWidth * (ordinal - (laneCount - 1) * 0.5f);

        var result = new List<Vector3>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            Vector3 tangent;
            if (i == 0) tangent = source[1] - source[0];
            else if (i == source.Count - 1)
                tangent = source[i] - source[i - 1];
            else tangent = source[i + 1] - source[i - 1];

            tangent.y = 0f;
            Vector3 right = tangent.sqrMagnitude > 0.000001f
                ? Vector3.Cross(Vector3.up, tangent.normalized)
                : Vector3.right;
            result.Add(source[i] + right * lateralOffset);
        }

        return result;
    }

    private static void GenerateTransitionMovements(
        TrafficGraphCompilationContext context,
        Vector2Int cell,
        List<LaneDraft> incoming,
        List<LaneDraft> outgoing)
    {
        List<List<LaneDraft>> incomingLegs = GroupByEndLeg(incoming);
        List<List<LaneDraft>> outgoingLegs = GroupByStartLeg(outgoing);

        for (int legIndex = 0; legIndex < incomingLegs.Count; legIndex++)
        {
            List<LaneDraft> sourceLanes = incomingLegs[legIndex];
            List<LaneDraft> targetLanes =
                FindOppositeOutgoingLeg(sourceLanes[0], outgoingLegs);

            if (targetLanes == null || targetLanes.Count == 0)
            {
                GenerateRoadEndMovements(
                    context,
                    cell,
                    sourceLanes,
                    outgoing);
                continue;
            }

            sourceLanes.Sort(CompareLaneOrdinal);
            targetLanes.Sort(CompareLaneOrdinal);
            int sourceCount = sourceLanes.Count;
            int targetCount = targetLanes.Count;
            TrafficMovementKind primaryKind = sourceCount == targetCount
                ? TrafficMovementKind.LaneContinuation
                : sourceCount > targetCount
                    ? TrafficMovementKind.MandatoryMerge
                    : TrafficMovementKind.LaneExpansion;

            for (int laneIndex = 0; laneIndex < sourceCount; laneIndex++)
            {
                int targetIndex = Mathf.Min(
                    targetCount - 1,
                    Mathf.FloorToInt(
                        (float)laneIndex * targetCount / sourceCount));
                AddMovement(
                    context,
                    cell,
                    sourceLanes[laneIndex],
                    targetLanes[targetIndex],
                    primaryKind,
                    TrafficTurnType.Straight,
                    true,
                    Mathf.Abs(laneIndex - targetIndex));

                if (targetCount <= sourceCount) continue;

                int left = targetIndex - 1;
                int right = targetIndex + 1;
                if (left >= 0)
                {
                    AddMovement(
                        context,
                        cell,
                        sourceLanes[laneIndex],
                        targetLanes[left],
                        TrafficMovementKind.OptionalLaneChange,
                        TrafficTurnType.Straight,
                        false,
                        10 + Mathf.Abs(laneIndex - left));
                }
                if (right < targetCount)
                {
                    AddMovement(
                        context,
                        cell,
                        sourceLanes[laneIndex],
                        targetLanes[right],
                        TrafficMovementKind.OptionalLaneChange,
                        TrafficTurnType.Straight,
                        false,
                        10 + Mathf.Abs(laneIndex - right));
                }
            }
        }
    }

    private static void GenerateRoadEndMovements(
        TrafficGraphCompilationContext context,
        Vector2Int cell,
        List<LaneDraft> incoming,
        List<LaneDraft> outgoing)
    {
        List<List<LaneDraft>> incomingLegs = GroupByEndLeg(incoming);
        for (int legIndex = 0; legIndex < incomingLegs.Count; legIndex++)
        {
            List<LaneDraft> sourceLanes = incomingLegs[legIndex];
            List<LaneDraft> returnLanes = outgoing.FindAll(
                lane =>
                    lane.StartLegDirectionBit ==
                    sourceLanes[0].EndLegDirectionBit);
            sourceLanes.Sort(CompareLaneOrdinal);
            returnLanes.Sort(CompareLaneOrdinal);

            int mappedCount = Mathf.Min(sourceLanes.Count, returnLanes.Count);
            for (int i = 0; i < mappedCount; i++)
            {
                if ((sourceLanes[i].Section.Profile.SupportedMovements &
                     RoadMovementPolicyMask.RoadEndUTurn) == 0)
                {
                    Reject(
                        context,
                        cell,
                        sourceLanes[i],
                        returnLanes[i],
                        TrafficMovementKind.RoadEndUTurn,
                        TrafficMovementRejectionReason.UTurnNotAllowed);
                    continue;
                }

                AddMovement(
                    context,
                    cell,
                    sourceLanes[i],
                    returnLanes[i],
                    TrafficMovementKind.RoadEndUTurn,
                    TrafficTurnType.UTurn,
                    true,
                    i);
            }

            for (int i = mappedCount; i < sourceLanes.Count; i++)
            {
                Reject(
                    context,
                    cell,
                    sourceLanes[i],
                    null,
                    TrafficMovementKind.RoadEndUTurn,
                    TrafficMovementRejectionReason.NoLegalOutgoingLane);
            }
        }
    }

    private static void GenerateIntersectionMovements(
        TrafficGraphCompilationContext context,
        Vector2Int cell,
        List<LaneDraft> incoming,
        List<LaneDraft> outgoing)
    {
        context.Source.TryGetIntersectionPolicy(
            cell,
            out IntersectionPolicyRecord policy);
        if (policy != null && policy.CustomRules.Count > 0)
        {
            for (int i = 0; i < policy.CustomRules.Count; i++)
            {
                LaneConnectionRuleRecord rule = policy.CustomRules[i];
                LaneDraft source = incoming.Find(
                    lane =>
                        lane.EndLegDirectionBit == rule.FromDirectionBit &&
                        lane.LaneOrdinal == rule.FromLaneIndex);
                LaneDraft target = outgoing.Find(
                    lane =>
                        lane.StartLegDirectionBit == rule.ToDirectionBit &&
                        lane.LaneOrdinal == rule.ToLaneIndex);
                if (source == null || target == null ||
                    source.EndLegDirectionBit == target.StartLegDirectionBit)
                {
                    Reject(
                        context,
                        cell,
                        source,
                        target,
                        TrafficMovementKind.Intersection,
                        TrafficMovementRejectionReason.InvalidAuthoredLane);
                    continue;
                }

                AddMovement(
                    context,
                    cell,
                    source,
                    target,
                    TrafficMovementKind.Intersection,
                    ClassifyTurn(
                        source.EndLegDirectionBit,
                        target.StartLegDirectionBit),
                    true,
                    i);
            }

            return;
        }

        List<List<LaneDraft>> incomingLegs = GroupByEndLeg(incoming);
        List<List<LaneDraft>> outgoingLegs = GroupByStartLeg(outgoing);
        for (int inIndex = 0; inIndex < incomingLegs.Count; inIndex++)
        {
            List<LaneDraft> sourceLanes = incomingLegs[inIndex];
            sourceLanes.Sort(CompareLaneOrdinal);
            for (int outIndex = 0; outIndex < outgoingLegs.Count; outIndex++)
            {
                List<LaneDraft> targetLanes = outgoingLegs[outIndex];
                if (sourceLanes[0].EndLegDirectionBit ==
                    targetLanes[0].StartLegDirectionBit)
                {
                    continue;
                }

                targetLanes.Sort(CompareLaneOrdinal);
                for (int laneIndex = 0;
                     laneIndex < sourceLanes.Count;
                     laneIndex++)
                {
                    int targetIndex = Mathf.Min(
                        targetLanes.Count - 1,
                        Mathf.FloorToInt(
                            (float)laneIndex *
                            targetLanes.Count /
                            sourceLanes.Count));
                    LaneDraft source = sourceLanes[laneIndex];
                    LaneDraft target = targetLanes[targetIndex];
                    if (IsDisabled(policy, source, target))
                    {
                        Reject(
                            context,
                            cell,
                            source,
                            target,
                            TrafficMovementKind.Intersection,
                            TrafficMovementRejectionReason
                                .DisabledByAuthoredPolicy);
                        continue;
                    }

                    AddMovement(
                        context,
                        cell,
                        source,
                        target,
                        TrafficMovementKind.Intersection,
                        ClassifyTurn(
                            source.EndLegDirectionBit,
                            target.StartLegDirectionBit),
                        true,
                        laneIndex);
                }
            }
        }
    }

    private static void AddMovement(
        TrafficGraphCompilationContext context,
        Vector2Int ownerCell,
        LaneDraft source,
        LaneDraft target,
        TrafficMovementKind kind,
        TrafficTurnType turnType,
        bool mandatory,
        int priority)
    {
        if (source == null || target == null) return;

        RoadPermissionMask permissions =
            source.AllowedPermissions & target.AllowedPermissions;
        VehicleCapabilityMask capabilities =
            source.AllowedCapabilities & target.AllowedCapabilities;
        if (permissions == RoadPermissionMask.None ||
            capabilities == VehicleCapabilityMask.None)
        {
            Reject(
                context,
                ownerCell,
                source,
                target,
                kind,
                TrafficMovementRejectionReason.IncompatiblePermissions);
            return;
        }

        if (!SupportsMovement(source.Section.Profile, kind) ||
            !SupportsMovement(target.Section.Profile, kind))
        {
            Reject(
                context,
                ownerCell,
                source,
                target,
                kind,
                TrafficMovementRejectionReason.UnsupportedByRoadProfile);
            return;
        }

        string key =
            $"MOVEMENT:{source.Id.Value:X16}->{target.Id.Value:X16}/KIND:{(int)kind}/OWNER:{ownerCell.x},{ownerCell.y}";
        MovementId id = MovementId.FromStableKey(key);
        if (context.Movements.Exists(existing => existing.Id == id)) return;

        var movement = new MovementDraft
        {
            Id = id,
            Source = source,
            Target = target,
            Kind = kind,
            OwnerId = ResolveMovementOwnerId(
                context,
                ownerCell,
                source,
                target,
                kind),
            OwnerCell = ownerCell,
            FromDirectionBit = source.EndLegDirectionBit,
            ToDirectionBit = target.StartLegDirectionBit,
            TurnType = turnType,
            RequiredPermissions = permissions,
            RequiredCapabilities = capabilities,
            SourceDistanceUnits =
                CalculatePolylineLength(source.Samples),
            TargetDistanceUnits = 0f,
            IsMandatory = mandatory,
            PolicyPriority = priority,
            ApproachLegMask =
                source.EndLegDirectionBit | target.StartLegDirectionBit
        };
        context.Movements.Add(movement);
        source.OutgoingMovementIds.Add(id);
    }

    private static void GenerateSectionLaneChanges(
        TrafficGraphCompilationContext context)
    {
        for (int sectionIndex = 0;
             sectionIndex < context.Sections.Count;
             sectionIndex++)
        {
            SectionDraft section = context.Sections[sectionIndex];
            var byFlow =
                new Dictionary<TrafficLaneFlowDirection, List<LaneDraft>>();
            for (int laneIndex = 0; laneIndex < context.Lanes.Count; laneIndex++)
            {
                LaneDraft lane = context.Lanes[laneIndex];
                if (lane.Section != section) continue;
                if (!byFlow.TryGetValue(
                        lane.FlowDirection,
                        out List<LaneDraft> group))
                {
                    group = new List<LaneDraft>();
                    byFlow.Add(lane.FlowDirection, group);
                }
                group.Add(lane);
            }

            foreach (List<LaneDraft> lanes in byFlow.Values)
            {
                if (lanes.Count < 2) continue;
                lanes.Sort(CompareLaneOrdinal);
                float usableLength = CalculatePolylineLength(lanes[0].Samples);
                List<Vector2> windows =
                    BuildSectionLaneChangeWindows(usableLength);
                if (windows.Count == 0) continue;

                for (int windowIndex = 0;
                     windowIndex < windows.Count;
                     windowIndex++)
                {
                    Vector2 window = windows[windowIndex];
                    for (int laneIndex = 0;
                         laneIndex < lanes.Count - 1;
                         laneIndex++)
                    {
                        AddLaneChangeMovement(
                            context,
                            section,
                            lanes[laneIndex],
                            lanes[laneIndex + 1],
                            window.x,
                            window.y);
                        AddLaneChangeMovement(
                            context,
                            section,
                            lanes[laneIndex + 1],
                            lanes[laneIndex],
                            window.x,
                            window.y);
                    }
                }
            }
        }
    }

    private static List<Vector2> BuildSectionLaneChangeWindows(
        float sectionLength)
    {
        var windows = new List<Vector2>();
        if (sectionLength <= 0f) return windows;

        float endpointMargin = Mathf.Min(
            LaneChangeEndpointMarginUnits,
            Mathf.Max(
                MinimumLaneChangeEndpointMarginUnits,
                sectionLength * 0.15f));
        float availableLength = sectionLength - endpointMargin * 2f;
        if (availableLength < MinimumLaneChangeLengthUnits)
        {
            return windows;
        }

        float laneChangeLength = Mathf.Min(
            LaneChangeLengthUnits,
            availableLength);
        float availableEnd = sectionLength - endpointMargin;

        AddSectionLaneChangeWindow(
            windows,
            endpointMargin,
            endpointMargin + laneChangeLength);

        float approachWindowEnd = Mathf.Max(
            endpointMargin + laneChangeLength,
            availableEnd - LaneChangeIntersectionEntryClearanceUnits);
        float beforeIntersectionStart = approachWindowEnd - laneChangeLength;
        if (beforeIntersectionStart >=
            endpointMargin +
            laneChangeLength +
            LaneChangeEndpointWindowSpacingUnits)
        {
            AddSectionLaneChangeWindow(
                windows,
                beforeIntersectionStart,
                approachWindowEnd);
        }

        return windows;
    }

    private static void AddSectionLaneChangeWindow(
        List<Vector2> windows,
        float sourceDistance,
        float targetDistance)
    {
        if (windows == null) return;
        if (targetDistance - sourceDistance < MinimumLaneChangeLengthUnits)
        {
            return;
        }

        windows.Add(new Vector2(sourceDistance, targetDistance));
    }

    private static void AddLaneChangeMovement(
        TrafficGraphCompilationContext context,
        SectionDraft section,
        LaneDraft source,
        LaneDraft target,
        float sourceDistance,
        float targetDistance)
    {
        if (!SupportsMovement(
                section.Profile,
                TrafficMovementKind.OptionalLaneChange))
        {
            Reject(
                context,
                section.StartCell,
                source,
                target,
                TrafficMovementKind.OptionalLaneChange,
                TrafficMovementRejectionReason.UnsupportedByRoadProfile);
            return;
        }

        RoadPermissionMask permissions =
            source.AllowedPermissions & target.AllowedPermissions;
        VehicleCapabilityMask capabilities =
            source.AllowedCapabilities & target.AllowedCapabilities;
        if (permissions == RoadPermissionMask.None ||
            capabilities == VehicleCapabilityMask.None)
        {
            Reject(
                context,
                section.StartCell,
                source,
                target,
                TrafficMovementKind.OptionalLaneChange,
                TrafficMovementRejectionReason.IncompatiblePermissions);
            return;
        }

        string key =
            $"LANE_CHANGE:{source.Id.Value:X16}->{target.Id.Value:X16}/SECTION:{section.Id.Value:X16}/RANGE:{sourceDistance:0.###}-{targetDistance:0.###}";
        MovementId id = MovementId.FromStableKey(key);
        var movement = new MovementDraft
        {
            Id = id,
            Source = source,
            Target = target,
            Kind = TrafficMovementKind.OptionalLaneChange,
            OwnerId = ControlledNodeId.FromStableKey(
                $"LANE_CHANGE_POLICY:{section.Id.Value:X16}"),
            OwnerCell = section.StartCell,
            FromDirectionBit = source.StartLegDirectionBit,
            ToDirectionBit = target.StartLegDirectionBit,
            TurnType = TrafficTurnType.Straight,
            RequiredPermissions = permissions,
            RequiredCapabilities = capabilities,
            SourceDistanceUnits = sourceDistance,
            TargetDistanceUnits = targetDistance,
            IsMandatory = false,
            PolicyPriority = 20 + Mathf.Abs(
                source.LaneOrdinal - target.LaneOrdinal),
            ApproachLegMask =
                source.StartLegDirectionBit | target.StartLegDirectionBit
        };
        context.Movements.Add(movement);
        source.OutgoingMovementIds.Add(id);
    }

    private static ControlledNodeId ResolveMovementOwnerId(
        TrafficGraphCompilationContext context,
        Vector2Int ownerCell,
        LaneDraft source,
        LaneDraft target,
        TrafficMovementKind kind)
    {
        if (kind == TrafficMovementKind.Intersection)
        {
            if (context.Source.TryGetIntersectionPolicy(
                    ownerCell,
                    out IntersectionPolicyRecord policy))
            {
                return policy.Id;
            }

            return ControlledNodeId.FromStableKey(
                $"CONTROLLED_NODE:{ownerCell.x},{ownerCell.y}");
        }

        if (kind == TrafficMovementKind.RoadEndUTurn)
        {
            return ControlledNodeId.FromStableKey(
                $"ROAD_END:{ownerCell.x},{ownerCell.y}");
        }

        for (int i = 0; i < context.TransitionBoundaries.Count; i++)
        {
            TransitionBoundaryDraft boundary =
                context.TransitionBoundaries[i];
            if (boundary.OwnerCell != ownerCell) continue;
            if (!MovementTouchesBoundaryOtherCell(source, target, boundary.OtherCell))
            {
                continue;
            }

            RoadProfileId sourceProfile = source.Section.Profile.Id;
            RoadProfileId targetProfile = target.Section.Profile.Id;
            bool profilesMatch =
                (boundary.ProfileA == sourceProfile &&
                 boundary.ProfileB == targetProfile) ||
                (boundary.ProfileA == targetProfile &&
                 boundary.ProfileB == sourceProfile);
            if (profilesMatch) return boundary.Id;
        }

        return ControlledNodeId.FromStableKey(
            $"LOOP_CONTINUITY:{source.Section.Id.Value:X16}/{ownerCell.x},{ownerCell.y}");
    }

    private static bool MovementTouchesBoundaryOtherCell(
        LaneDraft source,
        LaneDraft target,
        Vector2Int otherCell)
    {
        return SectionTouchesBoundaryOtherCell(source.Section, source.EndAnchorCell, otherCell) ||
               SectionTouchesBoundaryOtherCell(target.Section, target.StartAnchorCell, otherCell);
    }

    private static bool SectionTouchesBoundaryOtherCell(
        SectionDraft section,
        Vector2Int boundaryCell,
        Vector2Int otherCell)
    {
        if (section.SourceCells.Count < 2) return false;
        if (section.SourceCells[0] == boundaryCell)
        {
            return section.SourceCells[1] == otherCell;
        }

        int last = section.SourceCells.Count - 1;
        return section.SourceCells[last] == boundaryCell &&
               section.SourceCells[last - 1] == otherCell;
    }

    private static void Reject(
        TrafficGraphCompilationContext context,
        Vector2Int ownerCell,
        LaneDraft source,
        LaneDraft target,
        TrafficMovementKind kind,
        TrafficMovementRejectionReason reason)
    {
        context.Rejections.Add(
            new MovementRejectionRecord(
                ownerCell,
                source != null ? source.Id : LaneId.Invalid,
                target != null ? target.Id : LaneId.Invalid,
                kind,
                reason));
    }

    private static List<ControlledNodeRecord> BuildControlledNodes(
        TrafficGraphCompilationContext context)
    {
        var result = new List<ControlledNodeRecord>();
        foreach (NormalizedRoadCell cell in SortedCells(context))
        {
            if (cell.NodeKind != RoadNodeKind.Intersection) continue;

            Vector2Int position = cell.Source.GridPosition;
            var incoming = new List<LaneId>();
            var outgoing = new List<LaneId>();
            var movements = new List<MovementId>();
            for (int i = 0; i < context.Lanes.Count; i++)
            {
                LaneDraft lane = context.Lanes[i];
                if (lane.EndAnchorCell == position) incoming.Add(lane.Id);
                if (lane.StartAnchorCell == position) outgoing.Add(lane.Id);
            }
            for (int i = 0; i < context.Movements.Count; i++)
            {
                if (context.Movements[i].OwnerCell == position)
                {
                    movements.Add(context.Movements[i].Id);
                }
            }
            incoming.Sort();
            outgoing.Sort();
            movements.Sort();

            context.Source.TryGetIntersectionPolicy(
                position,
                out IntersectionPolicyRecord policy);
            ControlledNodeId id = policy != null
                ? policy.Id
                : ControlledNodeId.FromStableKey(
                    $"CONTROLLED_NODE:{position.x},{position.y}");
            result.Add(new ControlledNodeRecord(
                id,
                position,
                RoadNodeKind.Intersection,
                policy != null
                    ? policy.RuleType
                    : IntersectionRuleType.FreeForAll,
                policy != null ? policy.PriorityDirectionBitA : 0,
                policy != null ? policy.PriorityDirectionBitB : 0,
                policy != null ? policy.TrafficLightCycleSeconds : 0f,
                incoming,
                outgoing,
                movements));
        }

        result.Sort((left, right) => left.Id.CompareTo(right.Id));
        return result;
    }

    private static void BuildLaneSegments(TrafficGraphCompilationContext context)
    {
        for (int laneIndex = 0; laneIndex < context.Lanes.Count; laneIndex++)
        {
            LaneDraft lane = context.Lanes[laneIndex];
            float laneLength = CalculatePolylineLength(lane.Samples);
            var distances = new List<float> { 0f, laneLength };
            for (int movementIndex = 0;
                 movementIndex < context.Movements.Count;
                 movementIndex++)
            {
                MovementDraft movement = context.Movements[movementIndex];
                if (movement.Source == lane)
                {
                    AddUniqueDistance(distances, movement.SourceDistanceUnits, laneLength);
                }
                if (movement.Target == lane)
                {
                    AddUniqueDistance(distances, movement.TargetDistanceUnits, laneLength);
                }
            }

            AddPortAnchorSplitDistances(context, lane, laneLength, distances);

            distances.Sort();
            for (int segmentIndex = 0;
                 segmentIndex < distances.Count - 1;
                 segmentIndex++)
            {
                float start = distances[segmentIndex];
                float end = distances[segmentIndex + 1];
                if (end - start <= 0.0001f) continue;

                string key =
                    $"LANE_SEGMENT:{lane.Id.Value:X16}/ORDINAL:{segmentIndex}/RANGE:{start:0.###}-{end:0.###}";
                TrafficGeometryId geometryId = TrafficGeometryId.FromStableKey(
                    $"LANE_SEGMENT_GEOMETRY:{key}");
                List<Vector3> samples =
                    SlicePolylineSamples(lane.Samples, start, end);
                context.Geometry.Add(
                    new TrafficGeometryRecord(geometryId, samples));

                var segment = new LaneSegmentDraft
                {
                    Id = LaneSegmentId.FromStableKey(key),
                    Lane = lane,
                    Ordinal = segmentIndex,
                    StartDistanceUnits = start,
                    EndDistanceUnits = end,
                    GeometryId = geometryId
                };
                context.LaneSegments.Add(segment);
                lane.TraversalSegmentIds.Add(segment.Id);
            }
        }

        context.LaneSegments.Sort((left, right) => left.Id.CompareTo(right.Id));
    }

    private static void AssignMovementLaneSegments(
        TrafficGraphCompilationContext context)
    {
        for (int i = 0; i < context.Movements.Count; i++)
        {
            MovementDraft movement = context.Movements[i];
            movement.SourceSegmentId = FindLaneSegmentId(
                context,
                movement.Source,
                movement.SourceDistanceUnits,
                true);
            movement.TargetSegmentId = FindLaneSegmentId(
                context,
                movement.Target,
                movement.TargetDistanceUnits,
                false);
        }
    }

    private static List<LaneTraversalSegmentRecord> BuildLaneSegmentRecords(
        TrafficGraphCompilationContext context)
    {
        var result =
            new List<LaneTraversalSegmentRecord>(context.LaneSegments.Count);
        for (int i = 0; i < context.LaneSegments.Count; i++)
        {
            LaneSegmentDraft draft = context.LaneSegments[i];
            result.Add(new LaneTraversalSegmentRecord(
                draft.Id,
                draft.Lane.Id,
                draft.Ordinal,
                draft.StartDistanceUnits,
                draft.EndDistanceUnits,
                draft.GeometryId));
        }

        result.Sort((left, right) => left.Id.CompareTo(right.Id));
        return result;
    }

    private static List<MovementOwnerRecord> BuildMovementOwners(
        TrafficGraphCompilationContext context)
    {
        var result = new List<MovementOwnerRecord>();
        var movementOwnerIds = new HashSet<ControlledNodeId>();
        for (int i = 0; i < context.TransitionBoundaries.Count; i++)
        {
            TransitionBoundaryDraft boundary = context.TransitionBoundaries[i];
            var sourceCells = new List<Vector2Int>
            {
                boundary.OwnerCell,
                boundary.OtherCell
            };
            sourceCells.Sort(CompareCells);
            result.Add(new MovementOwnerRecord(
                boundary.Id,
                TrafficMovementOwnerKind.TransitionBoundary,
                boundary.OwnerCell,
                RoadSectionId.Invalid,
                sourceCells));
            movementOwnerIds.Add(boundary.Id);
        }

        for (int i = 0; i < context.Movements.Count; i++)
        {
            MovementDraft movement = context.Movements[i];
            if (movementOwnerIds.Contains(movement.OwnerId)) continue;

            TrafficMovementOwnerKind kind = ResolveMovementOwnerKind(
                context,
                movement);
            var sourceCells = new List<Vector2Int>();
            AddUniqueCell(sourceCells, movement.OwnerCell);
            if (movement.Source != null)
            {
                AddUniqueCell(sourceCells, movement.Source.Section.StartCell);
                AddUniqueCell(sourceCells, movement.Source.Section.EndCell);
            }
            if (movement.Target != null)
            {
                AddUniqueCell(sourceCells, movement.Target.Section.StartCell);
                AddUniqueCell(sourceCells, movement.Target.Section.EndCell);
            }
            sourceCells.Sort(CompareCells);

            result.Add(new MovementOwnerRecord(
                movement.OwnerId,
                kind,
                movement.OwnerCell,
                movement.Source != null
                    ? movement.Source.Section.Id
                    : RoadSectionId.Invalid,
                sourceCells));
            movementOwnerIds.Add(movement.OwnerId);
        }

        result.Sort((left, right) => left.Id.CompareTo(right.Id));
        return result;
    }

    private static List<BuildingPortAnchorRecord> BuildPortAnchors(
        TrafficGraphCompilationContext context)
    {
        var result =
            new List<BuildingPortAnchorRecord>(
                context.Source.BuildingPorts.Count);
        for (int i = 0; i < context.Source.BuildingPorts.Count; i++)
        {
            BuildingPortRecord port = context.Source.BuildingPorts[i];
            var entry = new List<LanePositionAnchorRecord>();
            var exit = new List<LanePositionAnchorRecord>();
            for (int laneIndex = 0;
                 laneIndex < context.Lanes.Count;
                 laneIndex++)
            {
                LaneDraft lane = context.Lanes[laneIndex];
                if (!TryGetLaneDistanceAtCell(
                        context,
                        lane,
                        port.PortCell,
                        out float distanceUnits))
                {
                    continue;
                }

                if (port.PortType == PortType.Entry ||
                    port.PortType == PortType.Both)
                {
                    AddUniqueLaneAnchor(
                        entry,
                        new LanePositionAnchorRecord(lane.Id, distanceUnits));
                }

                if (port.PortType == PortType.Exit ||
                    port.PortType == PortType.Both)
                {
                    AddUniqueLaneAnchor(
                        exit,
                        new LanePositionAnchorRecord(lane.Id, distanceUnits));
                }
            }
            entry.Sort(CompareLaneAnchors);
            exit.Sort(CompareLaneAnchors);

            if (entry.Count == 0 && exit.Count == 0)
            {
                context.Diagnostics.AddInfo(
                    TrafficDiagnosticCode.UnreachableBuildingPort,
                    "Building port is currently disconnected from the compiled road graph.",
                    TrafficDiagnosticSource.ForCell(
                        context.Version,
                        port.PortCell,
                        port.Id.ToString()));
            }

            result.Add(new BuildingPortAnchorRecord(
                port.Id,
                port.BuildingOriginCell,
                port.PortCell,
                port.PortType,
                entry,
                exit));
        }

        result.Sort((left, right) => left.Id.CompareTo(right.Id));
        return result;
    }

    private static void AddPortAnchorSplitDistances(
        TrafficGraphCompilationContext context,
        LaneDraft lane,
        float laneLength,
        List<float> distances)
    {
        for (int portIndex = 0;
             portIndex < context.Source.BuildingPorts.Count;
             portIndex++)
        {
            BuildingPortRecord port = context.Source.BuildingPorts[portIndex];
            if (TryGetLaneDistanceAtCell(
                    context,
                    lane,
                    port.PortCell,
                    out float distanceUnits))
            {
                AddUniqueDistance(distances, distanceUnits, laneLength);
            }
        }
    }

    private static bool TryGetLaneDistanceAtCell(
        TrafficGraphCompilationContext context,
        LaneDraft lane,
        Vector2Int cell,
        out float distanceUnits)
    {
        distanceUnits = 0f;
        if (lane == null ||
            lane.Section == null ||
            lane.Samples == null ||
            context == null ||
            lane.Section.SourceCells == null ||
            lane.Samples.Count == 0 ||
            lane.Section.SourceCells.Count == 0)
        {
            return false;
        }

        int sourceIndex = -1;
        for (int i = 0; i < lane.Section.SourceCells.Count; i++)
        {
            if (lane.Section.SourceCells[i] == cell)
            {
                sourceIndex = i;
                break;
            }
        }

        if (sourceIndex < 0) return false;

        if (!context.Cells.TryGetValue(cell, out NormalizedRoadCell sourceCell))
        {
            return false;
        }

        distanceUnits = FindClosestDistanceAlongPolyline(
            lane.Samples,
            sourceCell.Source.WorldCenter);
        return true;
    }

    private static float FindClosestDistanceAlongPolyline(
        IReadOnlyList<Vector3> samples,
        Vector3 worldPoint)
    {
        if (samples == null || samples.Count < 2) return 0f;

        float cumulative = 0f;
        float bestDistance = 0f;
        float bestSqrDistance = float.MaxValue;
        for (int i = 1; i < samples.Count; i++)
        {
            Vector3 segmentStart = samples[i - 1];
            Vector3 segmentEnd = samples[i];
            Vector3 segment = segmentEnd - segmentStart;
            float segmentLengthSqr = segment.sqrMagnitude;
            if (segmentLengthSqr <= 0.0001f) continue;

            float t = Mathf.Clamp01(
                Vector3.Dot(worldPoint - segmentStart, segment) /
                segmentLengthSqr);
            Vector3 projected = segmentStart + segment * t;
            float sqrDistance = (worldPoint - projected).sqrMagnitude;
            float segmentLength = Mathf.Sqrt(segmentLengthSqr);
            if (sqrDistance < bestSqrDistance)
            {
                bestSqrDistance = sqrDistance;
                bestDistance = cumulative + segmentLength * t;
            }

            cumulative += segmentLength;
        }

        return bestDistance;
    }

    private static void ApplyChaikinInPlace(
        List<Vector3> path,
        int iterations)
    {
        if (path == null || path.Count < 3 || iterations <= 0) return;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            var smoothed = new List<Vector3>(path.Count * 2);
            smoothed.Add(path[0]);
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector3 p0 = path[i];
                Vector3 p1 = path[i + 1];
                smoothed.Add(Vector3.Lerp(p0, p1, 0.25f));
                smoothed.Add(Vector3.Lerp(p0, p1, 0.75f));
            }
            smoothed.Add(path[path.Count - 1]);

            path.Clear();
            path.AddRange(smoothed);
        }
    }

    private static void AddUniqueLaneAnchor(
        List<LanePositionAnchorRecord> anchors,
        LanePositionAnchorRecord anchor)
    {
        for (int i = 0; i < anchors.Count; i++)
        {
            if (anchors[i].Equals(anchor)) return;
        }

        anchors.Add(anchor);
    }

    private static TrafficMovementOwnerKind ResolveMovementOwnerKind(
        TrafficGraphCompilationContext context,
        MovementDraft movement)
    {
        switch (movement.Kind)
        {
            case TrafficMovementKind.Intersection:
                return TrafficMovementOwnerKind.ControlledIntersection;
            case TrafficMovementKind.RoadEndUTurn:
                return TrafficMovementOwnerKind.RoadEnd;
            case TrafficMovementKind.OptionalLaneChange:
                return TrafficMovementOwnerKind.LaneChangePolicy;
        }

        for (int i = 0; i < context.TransitionBoundaries.Count; i++)
        {
            if (context.TransitionBoundaries[i].Id == movement.OwnerId)
            {
                return TrafficMovementOwnerKind.TransitionBoundary;
            }
        }

        return TrafficMovementOwnerKind.LoopContinuity;
    }

    private static LaneSegmentId FindLaneSegmentId(
        TrafficGraphCompilationContext context,
        LaneDraft lane,
        float distance,
        bool preferPreviousAtBoundary)
    {
        if (lane == null) return LaneSegmentId.Invalid;

        LaneSegmentDraft best = null;
        for (int i = 0; i < context.LaneSegments.Count; i++)
        {
            LaneSegmentDraft segment = context.LaneSegments[i];
            if (segment.Lane != lane) continue;

            if (!IsDistanceInSegment(
                    segment,
                    distance,
                    preferPreviousAtBoundary))
            {
                continue;
            }

            if (best == null ||
                (preferPreviousAtBoundary &&
                 segment.Ordinal > best.Ordinal) ||
                (!preferPreviousAtBoundary &&
                 segment.Ordinal < best.Ordinal))
            {
                best = segment;
            }
        }

        if (best == null) best = FindNearestLaneSegment(context, lane, distance);
        return best != null ? best.Id : LaneSegmentId.Invalid;
    }

    private static bool IsDistanceInSegment(
        LaneSegmentDraft segment,
        float distance,
        bool preferPreviousAtBoundary)
    {
        const float epsilon = 0.0001f;
        if (preferPreviousAtBoundary)
        {
            return distance > segment.StartDistanceUnits + epsilon &&
                   distance <= segment.EndDistanceUnits + epsilon;
        }

        return distance >= segment.StartDistanceUnits - epsilon &&
               distance < segment.EndDistanceUnits - epsilon;
    }

    private static LaneSegmentDraft FindNearestLaneSegment(
        TrafficGraphCompilationContext context,
        LaneDraft lane,
        float distance)
    {
        LaneSegmentDraft best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < context.LaneSegments.Count; i++)
        {
            LaneSegmentDraft segment = context.LaneSegments[i];
            if (segment.Lane != lane) continue;

            float clamped = Mathf.Clamp(
                distance,
                segment.StartDistanceUnits,
                segment.EndDistanceUnits);
            float delta = Mathf.Abs(clamped - distance);
            if (best != null && delta >= bestDistance) continue;

            best = segment;
            bestDistance = delta;
        }

        return best;
    }

    private static List<Vector3> SlicePolylineSamples(
        IReadOnlyList<Vector3> samples,
        float startDistance,
        float endDistance)
    {
        var result = new List<Vector3>();
        result.Add(SampleAtDistance(samples, startDistance, out _));

        float cumulative = 0f;
        for (int i = 1; i < samples.Count - 1; i++)
        {
            cumulative += Vector3.Distance(samples[i - 1], samples[i]);
            if (cumulative > startDistance + 0.0001f &&
                cumulative < endDistance - 0.0001f)
            {
                result.Add(samples[i]);
            }
        }

        result.Add(SampleAtDistance(samples, endDistance, out _));
        return result;
    }

    private static void AddUniqueDistance(
        List<float> distances,
        float distance,
        float laneLength)
    {
        float clamped = Mathf.Clamp(distance, 0f, laneLength);
        for (int i = 0; i < distances.Count; i++)
        {
            if (Mathf.Abs(distances[i] - clamped) <= 0.0001f) return;
        }
        distances.Add(clamped);
    }

    private static void AddUniqueCell(List<Vector2Int> cells, Vector2Int cell)
    {
        if (!cells.Contains(cell)) cells.Add(cell);
    }

    private static int CompareLaneAnchors(
        LanePositionAnchorRecord left,
        LanePositionAnchorRecord right)
    {
        int lane = left.LaneId.CompareTo(right.LaneId);
        return lane != 0
            ? lane
            : left.DistanceUnits.CompareTo(right.DistanceUnits);
    }

    private static Dictionary<Vector2Int, List<LaneDraft>>
        BuildLaneIndexByCell(
            IReadOnlyList<LaneDraft> lanes,
            bool incoming)
    {
        var result = new Dictionary<Vector2Int, List<LaneDraft>>();
        for (int i = 0; i < lanes.Count; i++)
        {
            LaneDraft lane = lanes[i];
            Vector2Int cell = incoming
                ? lane.EndAnchorCell
                : lane.StartAnchorCell;
            if (!result.TryGetValue(cell, out List<LaneDraft> list))
            {
                list = new List<LaneDraft>();
                result.Add(cell, list);
            }
            list.Add(lane);
        }

        return result;
    }

    private static List<List<LaneDraft>> GroupByEndLeg(
        List<LaneDraft> lanes) =>
        GroupByLeg(lanes, true);

    private static List<List<LaneDraft>> GroupByStartLeg(
        List<LaneDraft> lanes) =>
        GroupByLeg(lanes, false);

    private static List<List<LaneDraft>> GroupByLeg(
        List<LaneDraft> lanes,
        bool endLeg)
    {
        var byBit = new SortedDictionary<int, List<LaneDraft>>();
        for (int i = 0; i < lanes.Count; i++)
        {
            int bit = endLeg
                ? lanes[i].EndLegDirectionBit
                : lanes[i].StartLegDirectionBit;
            if (!byBit.TryGetValue(bit, out List<LaneDraft> group))
            {
                group = new List<LaneDraft>();
                byBit.Add(bit, group);
            }
            group.Add(lanes[i]);
        }

        return new List<List<LaneDraft>>(byBit.Values);
    }

    private static List<LaneDraft> FindOppositeOutgoingLeg(
        LaneDraft source,
        List<List<LaneDraft>> outgoingLegs)
    {
        List<LaneDraft> best = null;
        float bestDot = float.MaxValue;
        Vector2 sourceDirection = DirectionVector(source.EndLegDirectionBit);
        for (int i = 0; i < outgoingLegs.Count; i++)
        {
            List<LaneDraft> candidate = outgoingLegs[i];
            if (candidate.Count == 0 ||
                candidate[0].StartLegDirectionBit ==
                source.EndLegDirectionBit)
            {
                continue;
            }

            float dot = Vector2.Dot(
                sourceDirection,
                DirectionVector(candidate[0].StartLegDirectionBit));
            if (dot < bestDot)
            {
                bestDot = dot;
                best = candidate;
            }
        }

        return best;
    }

    private static List<Vector3> BuildMovementCurve(MovementDraft movement)
    {
        Vector3 start = SampleAtDistance(
            movement.Source.Samples,
            movement.SourceDistanceUnits,
            out Vector3 sourceDirection);
        Vector3 end = SampleAtDistance(
            movement.Target.Samples,
            movement.TargetDistanceUnits,
            out Vector3 targetDirection);
        float controlDistance = Mathf.Max(
            0.1f,
            Vector3.Distance(start, end) * 0.4f);
        Vector3 controlA = start + sourceDirection * controlDistance;
        Vector3 controlB = end - targetDirection * controlDistance;

        var result = new List<Vector3>(MovementCurveSamples);
        for (int i = 0; i < MovementCurveSamples; i++)
        {
            float t = (float)i / (MovementCurveSamples - 1);
            float oneMinusT = 1f - t;
            result.Add(
                oneMinusT * oneMinusT * oneMinusT * start +
                3f * oneMinusT * oneMinusT * t * controlA +
                3f * oneMinusT * t * t * controlB +
                t * t * t * end);
        }

        return result;
    }

    private static Vector3 SampleAtDistance(
        IReadOnlyList<Vector3> samples,
        float distance,
        out Vector3 direction)
    {
        float totalLength = CalculatePolylineLength(samples);
        float remaining = Mathf.Clamp(distance, 0f, totalLength);
        for (int i = 1; i < samples.Count; i++)
        {
            Vector3 segment = samples[i] - samples[i - 1];
            float segmentLength = segment.magnitude;
            if (segmentLength <= 0.000001f) continue;
            if (remaining <= segmentLength)
            {
                direction = segment / segmentLength;
                return samples[i - 1] +
                       direction * remaining;
            }
            remaining -= segmentLength;
        }

        direction = samples.Count > 1
            ? (samples[samples.Count - 1] -
               samples[samples.Count - 2]).normalized
            : Vector3.forward;
        return samples[samples.Count - 1];
    }

    private static float CalculatePolylineLength(
        IReadOnlyList<Vector3> samples)
    {
        float length = 0f;
        for (int i = 1; i < samples.Count; i++)
        {
            length += Vector3.Distance(samples[i - 1], samples[i]);
        }
        return length;
    }

    private static RoadProfile ResolveSectionProfile(
        TrafficGraphCompilationContext context,
        IReadOnlyList<Vector2Int> path)
    {
        for (int i = 1; i < path.Count; i++)
        {
            NormalizedRoadCell cell = context.Cells[path[i]];
            if (context.Source.TryGetRoadProfile(
                    cell.Source.RoadProfileId,
                    out RoadProfile profile))
            {
                return profile;
            }
        }

        context.Source.TryGetRoadProfile(
            context.Cells[path[0]].Source.RoadProfileId,
            out RoadProfile fallback);
        return fallback;
    }

    private static bool IsPathLegal(
        TrafficGraphCompilationContext context,
        IReadOnlyList<Vector2Int> path,
        bool reverse)
    {
        for (int step = 0; step < path.Count - 1; step++)
        {
            int fromIndex = reverse ? path.Count - 1 - step : step;
            int toIndex = reverse ? fromIndex - 1 : fromIndex + 1;
            RoadCellRecord from = context.Cells[path[fromIndex]].Source;
            RoadCellRecord to = context.Cells[path[toIndex]].Source;
            int directionBit = RoadGridDirectionUtility.GetDirectionBit(
                from.GridPosition,
                to.GridPosition);
            int oppositeBit = OppositeDirectionBit(directionBit);
            if (!IsDirectionBit(directionBit) ||
                !from.CanExit(directionBit) ||
                !to.CanEnter(oppositeBit))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SupportsMovement(
        RoadProfile profile,
        TrafficMovementKind kind)
    {
        RoadMovementPolicyMask required;
        switch (kind)
        {
            case TrafficMovementKind.LaneContinuation:
            case TrafficMovementKind.LaneExpansion:
                required = RoadMovementPolicyMask.LaneContinuation;
                break;
            case TrafficMovementKind.OptionalLaneChange:
                required = RoadMovementPolicyMask.LaneChange;
                break;
            case TrafficMovementKind.MandatoryMerge:
                required = RoadMovementPolicyMask.Merge;
                break;
            case TrafficMovementKind.Intersection:
                required = RoadMovementPolicyMask.Intersection;
                break;
            case TrafficMovementKind.RoadEndUTurn:
                required = RoadMovementPolicyMask.RoadEndUTurn;
                break;
            default:
                required = RoadMovementPolicyMask.BuildingPort;
                break;
        }

        return (profile.SupportedMovements & required) != 0;
    }

    private static bool IsDisabled(
        IntersectionPolicyRecord policy,
        LaneDraft source,
        LaneDraft target)
    {
        if (policy == null) return false;
        for (int i = 0; i < policy.DisabledRules.Count; i++)
        {
            LaneConnectionRuleRecord rule = policy.DisabledRules[i];
            if (rule.FromDirectionBit == source.EndLegDirectionBit &&
                rule.FromLaneIndex == source.LaneOrdinal &&
                rule.ToDirectionBit == target.StartLegDirectionBit &&
                rule.ToLaneIndex == target.LaneOrdinal)
            {
                return true;
            }
        }

        return false;
    }

    private static TrafficTurnType ClassifyTurn(
        int incomingLegBit,
        int outgoingLegBit)
    {
        Vector2 incomingTravel = -DirectionVector(incomingLegBit);
        Vector2 outgoingTravel = DirectionVector(outgoingLegBit);
        float angle = Vector2.SignedAngle(incomingTravel, outgoingTravel);
        float absoluteAngle = Mathf.Abs(angle);
        if (absoluteAngle < 45f) return TrafficTurnType.Straight;
        if (absoluteAngle > 135f) return TrafficTurnType.UTurn;
        return angle > 0f ? TrafficTurnType.Left : TrafficTurnType.Right;
    }

    private static Vector2 DirectionVector(int directionBit)
    {
        Vector2Int offset =
            RoadGridDirectionUtility.GetNeighborPosition(
                Vector2Int.zero,
                directionBit);
        return ((Vector2)offset).normalized;
    }

    private static List<NormalizedRoadCell> SortedCells(
        TrafficGraphCompilationContext context)
    {
        var result = new List<NormalizedRoadCell>(context.Cells.Values);
        result.Sort((left, right) =>
            CompareCells(
                left.Source.GridPosition,
                right.Source.GridPosition));
        return result;
    }

    private static bool IsBoundary(NormalizedRoadCell cell) =>
        cell.NodeKind != RoadNodeKind.ThroughRoad;

    private static int CompareLaneOrdinal(LaneDraft left, LaneDraft right)
    {
        int ordinal = left.LaneOrdinal.CompareTo(right.LaneOrdinal);
        return ordinal != 0 ? ordinal : left.Id.CompareTo(right.Id);
    }

    private static int CompareRejections(
        MovementRejectionRecord left,
        MovementRejectionRecord right)
    {
        int result = CompareCells(left.OwnerCell, right.OwnerCell);
        if (result != 0) return result;
        result = left.SourceLaneId.CompareTo(right.SourceLaneId);
        if (result != 0) return result;
        result = left.TargetLaneId.CompareTo(right.TargetLaneId);
        if (result != 0) return result;
        result = left.AttemptedKind.CompareTo(right.AttemptedKind);
        return result != 0 ? result : left.Reason.CompareTo(right.Reason);
    }

    private static int CompareCells(Vector2Int left, Vector2Int right)
    {
        int x = left.x.CompareTo(right.x);
        return x != 0 ? x : left.y.CompareTo(right.y);
    }

    private static string GetUndirectedEdgeKey(Vector2Int a, Vector2Int b)
    {
        if (CompareCells(a, b) > 0)
        {
            Vector2Int temporary = a;
            a = b;
            b = temporary;
        }

        return $"{a.x},{a.y}|{b.x},{b.y}";
    }

    private static string BuildPathKey(IReadOnlyList<Vector2Int> path)
    {
        var parts = new string[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            parts[i] = $"{path[i].x},{path[i].y}";
        }
        return string.Join(";", parts);
    }

    private static void CanonicalizePath(List<Vector2Int> path)
    {
        for (int i = 0; i < path.Count; i++)
        {
            int comparison = CompareCells(path[i], path[path.Count - 1 - i]);
            if (comparison < 0) return;
            if (comparison > 0)
            {
                path.Reverse();
                return;
            }
        }
    }

    private static bool IsDirectionBit(int bit) =>
        bit > 0 && bit <= 128 && (bit & (bit - 1)) == 0;

    private static int OppositeDirectionBit(int bit)
    {
        if (!IsDirectionBit(bit)) return 0;
        return bit <= 8 ? bit << 4 : bit >> 4;
    }

    private static void AddDuplicateIdError(
        TrafficGraphCompilationContext context,
        Vector2Int cell,
        string stableId)
    {
        context.Diagnostics.AddError(
            TrafficDiagnosticCode.DuplicateStableId,
            "Compiled traffic graph contains a duplicate or invalid stable ID.",
            TrafficDiagnosticSource.ForCell(
                context.Version,
                cell,
                stableId));
    }
}
