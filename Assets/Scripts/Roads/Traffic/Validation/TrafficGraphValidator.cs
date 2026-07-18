using System;
using System.Collections.Generic;
using UnityEngine;

public static class TrafficGraphValidator
{
    private const float DistanceTolerance = 0.001f;

    public static bool Validate(
        TrafficGraphSnapshot graph,
        TrafficDiagnosticCollection diagnostics)
    {
        if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));
        int initialCount = diagnostics.Count;

        if (graph == null)
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.MissingLaneReference,
                "Cannot validate a null traffic graph.");
            return false;
        }

        ValidateGeometry(graph, diagnostics);
        ValidateSectionsAndLanes(graph, diagnostics);
        ValidateMovementOwners(graph, diagnostics);
        ValidateLaneSegments(graph, diagnostics);
        ValidateMovements(graph, diagnostics);
        ValidateControlledNodes(graph, diagnostics);
        ValidateBuildingPorts(graph, diagnostics);
        ValidateLaneContinuity(graph, diagnostics);
        ValidateTransitionMappings(graph, diagnostics);

        return !HasErrorsSince(diagnostics, initialCount);
    }

    public static bool TryGetReachablePortAnchor(
        TrafficGraphSnapshot graph,
        BuildingPortAnchorId portId,
        PortType desiredFlow,
        out BuildingPortAnchorRecord anchor)
    {
        anchor = null;
        if (graph == null ||
            !graph.TryGetBuildingPortAnchor(portId, out BuildingPortAnchorRecord candidate))
        {
            return false;
        }

        bool needsEntry = desiredFlow == PortType.Entry ||
                          desiredFlow == PortType.Both;
        bool needsExit = desiredFlow == PortType.Exit ||
                         desiredFlow == PortType.Both;
        bool hasEntry = !needsEntry || candidate.ArrivalAnchors.Count > 0;
        bool hasExit = !needsExit || candidate.DepartureAnchors.Count > 0;
        if (!hasEntry || !hasExit) return false;

        anchor = candidate;
        return true;
    }

    private static void ValidateGeometry(
        TrafficGraphSnapshot graph,
        TrafficDiagnosticCollection diagnostics)
    {
        var ids = new HashSet<TrafficGeometryId>();
        for (int i = 0; i < graph.Geometry.Count; i++)
        {
            TrafficGeometryRecord geometry = graph.Geometry[i];
            if (!geometry.Id.IsValid || !ids.Add(geometry.Id))
            {
                AddDuplicateId(diagnostics, graph.Version, geometry.Id.ToString());
            }

            if (geometry.Samples.Count < 2 ||
                geometry.LengthUnits <= 0f ||
                float.IsNaN(geometry.LengthUnits) ||
                float.IsInfinity(geometry.LengthUnits))
            {
                diagnostics.AddError(
                    TrafficDiagnosticCode.MissingLaneReference,
                    "Graph geometry must have at least two samples and positive finite length.",
                    Source(graph.Version, geometry.Id.ToString()));
            }
        }
    }

    private static void ValidateSectionsAndLanes(
        TrafficGraphSnapshot graph,
        TrafficDiagnosticCollection diagnostics)
    {
        var sectionIds = new HashSet<RoadSectionId>();
        for (int i = 0; i < graph.Sections.Count; i++)
        {
            RoadSectionRecord section = graph.Sections[i];
            if (!section.Id.IsValid || !sectionIds.Add(section.Id))
            {
                AddDuplicateId(diagnostics, graph.Version, section.Id.ToString(), section.StartAnchorCell);
            }

            if (!IsDirectionBit(section.StartLegDirectionBit) ||
                !IsDirectionBit(section.EndLegDirectionBit) ||
                !graph.TryGetGeometry(section.CenterlineGeometryId, out _))
            {
                diagnostics.AddError(
                    TrafficDiagnosticCode.InvalidLegDirection,
                    "Road section has invalid leg metadata or missing centerline geometry.",
                    Source(graph.Version, section.Id.ToString(), section.StartAnchorCell));
            }
        }

        var laneIds = new HashSet<LaneId>();
        for (int i = 0; i < graph.Lanes.Count; i++)
        {
            LaneRecord lane = graph.Lanes[i];
            if (!lane.Id.IsValid || !laneIds.Add(lane.Id))
            {
                AddDuplicateId(diagnostics, graph.Version, lane.Id.ToString(), lane.StartAnchorCell);
            }

            if (!graph.TryGetSection(lane.SectionId, out RoadSectionRecord section) ||
                !graph.TryGetGeometry(lane.GeometryId, out _) ||
                !IsDirectionBit(lane.StartLegDirectionBit) ||
                !IsDirectionBit(lane.EndLegDirectionBit))
            {
                diagnostics.AddError(
                    TrafficDiagnosticCode.MissingLaneReference,
                    "Lane references missing section or geometry, or has invalid leg metadata.",
                    Source(graph.Version, lane.Id.ToString(), lane.StartAnchorCell));
                continue;
            }

            bool forward = lane.FlowDirection ==
                           TrafficLaneFlowDirection.SectionStartToEnd;
            bool anchorsMatch = forward
                ? lane.StartAnchorCell == section.StartAnchorCell &&
                  lane.EndAnchorCell == section.EndAnchorCell &&
                  lane.StartLegDirectionBit == section.StartLegDirectionBit &&
                  lane.EndLegDirectionBit == section.EndLegDirectionBit
                : lane.StartAnchorCell == section.EndAnchorCell &&
                  lane.EndAnchorCell == section.StartAnchorCell &&
                  lane.StartLegDirectionBit == section.EndLegDirectionBit &&
                  lane.EndLegDirectionBit == section.StartLegDirectionBit;
            if (!anchorsMatch)
            {
                diagnostics.AddError(
                    TrafficDiagnosticCode.InvalidLegDirection,
                    "Lane direction does not agree with its road section anchors.",
                    Source(graph.Version, lane.Id.ToString(), lane.StartAnchorCell));
            }
        }
    }

    private static void ValidateMovementOwners(
        TrafficGraphSnapshot graph,
        TrafficDiagnosticCollection diagnostics)
    {
        var ids = new HashSet<ControlledNodeId>();
        for (int i = 0; i < graph.MovementOwners.Count; i++)
        {
            MovementOwnerRecord owner = graph.MovementOwners[i];
            if (!owner.Id.IsValid || !ids.Add(owner.Id))
            {
                AddDuplicateId(diagnostics, graph.Version, owner.Id.ToString(), owner.PrimaryCell);
            }
        }
    }

    private static void ValidateLaneSegments(
        TrafficGraphSnapshot graph,
        TrafficDiagnosticCollection diagnostics)
    {
        var ids = new HashSet<LaneSegmentId>();
        for (int i = 0; i < graph.LaneSegments.Count; i++)
        {
            LaneTraversalSegmentRecord segment = graph.LaneSegments[i];
            if (!segment.Id.IsValid || !ids.Add(segment.Id))
            {
                AddDuplicateId(diagnostics, graph.Version, segment.Id.ToString());
            }

            if (!graph.TryGetLane(segment.LaneId, out _) ||
                !graph.TryGetGeometry(segment.GeometryId, out TrafficGeometryRecord geometry) ||
                segment.EndDistanceUnits <= segment.StartDistanceUnits ||
                geometry.Samples.Count < 2)
            {
                diagnostics.AddError(
                    TrafficDiagnosticCode.MissingLaneReference,
                    "Lane traversal segment references missing lane/geometry or invalid distance bounds.",
                    Source(graph.Version, segment.Id.ToString()));
            }
        }
    }

    private static void ValidateMovements(
        TrafficGraphSnapshot graph,
        TrafficDiagnosticCollection diagnostics)
    {
        var ids = new HashSet<MovementId>();
        for (int i = 0; i < graph.Movements.Count; i++)
        {
            MovementRecord movement = graph.Movements[i];
            LaneRecord sourceLane = null;
            LaneRecord targetLane = null;
            TrafficGeometryRecord geometry = null;
            LaneTraversalSegmentRecord sourceSegment = null;
            LaneTraversalSegmentRecord targetSegment = null;
            if (!movement.Id.IsValid || !ids.Add(movement.Id))
            {
                AddDuplicateId(diagnostics, graph.Version, movement.Id.ToString(), movement.OwnerCell);
            }

            bool referencesExist =
                graph.TryGetLane(movement.SourceLaneId, out sourceLane) &&
                graph.TryGetLane(movement.TargetLaneId, out targetLane) &&
                graph.TryGetGeometry(movement.GeometryId, out geometry) &&
                graph.TryGetMovementOwner(movement.OwnerId, out _) &&
                graph.TryGetLaneSegment(movement.SourceSegmentId, out sourceSegment) &&
                graph.TryGetLaneSegment(movement.TargetSegmentId, out targetSegment);
            if (!referencesExist)
            {
                diagnostics.AddError(
                    TrafficDiagnosticCode.MissingLaneReference,
                    "Movement references missing lane, owner, segment, or geometry data.",
                    Source(graph.Version, movement.Id.ToString(), movement.OwnerCell));
                continue;
            }

            if (geometry.Samples.Count < 2 ||
                sourceSegment.LaneId != movement.SourceLaneId ||
                targetSegment.LaneId != movement.TargetLaneId ||
                !IsDirectionBit(movement.FromDirectionBit) ||
                !IsDirectionBit(movement.ToDirectionBit) ||
                (movement.RequiredPermissions & sourceLane.AllowedPermissions) != movement.RequiredPermissions ||
                (movement.RequiredPermissions & targetLane.AllowedPermissions) != movement.RequiredPermissions ||
                (movement.RequiredCapabilities & sourceLane.AllowedCapabilities) != movement.RequiredCapabilities ||
                (movement.RequiredCapabilities & targetLane.AllowedCapabilities) != movement.RequiredCapabilities)
            {
                diagnostics.AddError(
                    TrafficDiagnosticCode.IllegalDirectionMovement,
                    "Movement geometry, leg metadata, permissions, or capability references are invalid.",
                    Source(graph.Version, movement.Id.ToString(), movement.OwnerCell));
            }

            bool sourceDistanceInside =
                movement.SourceDistanceUnits >= sourceSegment.StartDistanceUnits - DistanceTolerance &&
                movement.SourceDistanceUnits <= sourceSegment.EndDistanceUnits + DistanceTolerance;
            bool targetDistanceInside =
                movement.TargetDistanceUnits >= targetSegment.StartDistanceUnits - DistanceTolerance &&
                movement.TargetDistanceUnits <= targetSegment.EndDistanceUnits + DistanceTolerance;
            if (!sourceDistanceInside || !targetDistanceInside)
            {
                diagnostics.AddError(
                    TrafficDiagnosticCode.MissingLaneReference,
                    "Movement connector distances are outside their traversal segments.",
                    Source(graph.Version, movement.Id.ToString(), movement.OwnerCell));
            }
        }
    }

    private static void ValidateControlledNodes(
        TrafficGraphSnapshot graph,
        TrafficDiagnosticCollection diagnostics)
    {
        var ids = new HashSet<ControlledNodeId>();
        for (int i = 0; i < graph.ControlledNodes.Count; i++)
        {
            ControlledNodeRecord node = graph.ControlledNodes[i];
            if (!node.Id.IsValid || !ids.Add(node.Id))
            {
                AddDuplicateId(diagnostics, graph.Version, node.Id.ToString(), node.GridPosition);
            }

            for (int laneIndex = 0; laneIndex < node.IncomingLaneIds.Count; laneIndex++)
            {
                if (!graph.TryGetLane(node.IncomingLaneIds[laneIndex], out _))
                {
                    diagnostics.AddError(
                        TrafficDiagnosticCode.MissingLaneReference,
                        "Controlled node references a missing incoming lane.",
                        Source(graph.Version, node.Id.ToString(), node.GridPosition));
                }
            }

            for (int movementIndex = 0; movementIndex < node.MovementIds.Count; movementIndex++)
            {
                if (!graph.TryGetMovement(node.MovementIds[movementIndex], out _))
                {
                    diagnostics.AddError(
                        TrafficDiagnosticCode.MissingLaneReference,
                        "Controlled node references a missing movement.",
                        Source(graph.Version, node.Id.ToString(), node.GridPosition));
                }
            }
        }
    }

    private static void ValidateBuildingPorts(
        TrafficGraphSnapshot graph,
        TrafficDiagnosticCollection diagnostics)
    {
        var ids = new HashSet<BuildingPortAnchorId>();
        for (int i = 0; i < graph.BuildingPortAnchors.Count; i++)
        {
            BuildingPortAnchorRecord port = graph.BuildingPortAnchors[i];
            if (!port.Id.IsValid || !ids.Add(port.Id))
            {
                AddDuplicateId(diagnostics, graph.Version, port.Id.ToString(), port.PortCell);
            }

            bool needsEntry = port.PortType == PortType.Entry ||
                              port.PortType == PortType.Both;
            bool needsExit = port.PortType == PortType.Exit ||
                             port.PortType == PortType.Both;
            bool disconnected =
                port.ArrivalAnchors.Count == 0 &&
                port.DepartureAnchors.Count == 0;
            if (disconnected)
            {
                continue;
            }

            if ((needsEntry && port.ArrivalAnchors.Count == 0) ||
                (needsExit && port.DepartureAnchors.Count == 0))
            {
                diagnostics.AddError(
                    TrafficDiagnosticCode.UnreachableBuildingPort,
                    "Connected building port lacks a reachable lane anchor for its authored flow.",
                    Source(graph.Version, port.Id.ToString(), port.PortCell));
            }

            ValidatePortAnchors(
                graph,
                diagnostics,
                port,
                port.ArrivalAnchors,
                true);
            ValidatePortAnchors(
                graph,
                diagnostics,
                port,
                port.DepartureAnchors,
                false);
        }
    }

    private static void ValidatePortAnchors(
        TrafficGraphSnapshot graph,
        TrafficDiagnosticCollection diagnostics,
        BuildingPortAnchorRecord port,
        IReadOnlyList<LanePositionAnchorRecord> anchors,
        bool arrival)
    {
        for (int i = 0; i < anchors.Count; i++)
        {
            LanePositionAnchorRecord anchor = anchors[i];
            if (!graph.TryGetLane(anchor.LaneId, out LaneRecord lane) ||
                !graph.TryGetGeometry(lane.GeometryId, out TrafficGeometryRecord geometry) ||
                !graph.TryGetSection(lane.SectionId, out RoadSectionRecord section))
            {
                diagnostics.AddError(
                    TrafficDiagnosticCode.UnreachableBuildingPort,
                    "Building port anchor references a missing lane, section, or geometry.",
                    Source(graph.Version, port.Id.ToString(), port.PortCell));
                continue;
            }

            if (!SectionContainsCell(section, port.PortCell) ||
                anchor.DistanceUnits < -DistanceTolerance ||
                anchor.DistanceUnits > geometry.LengthUnits + DistanceTolerance)
            {
                diagnostics.AddError(
                    TrafficDiagnosticCode.UnreachableBuildingPort,
                    "Building port anchor does not reference a lane position inside the intended port cell.",
                    Source(graph.Version, port.Id.ToString(), port.PortCell));
            }
        }
    }

    private static bool SectionContainsCell(
        RoadSectionRecord section,
        Vector2Int cell)
    {
        for (int i = 0; i < section.SourceCells.Count; i++)
        {
            if (section.SourceCells[i] == cell) return true;
        }

        return false;
    }

    private static void ValidateLaneContinuity(
        TrafficGraphSnapshot graph,
        TrafficDiagnosticCollection diagnostics)
    {
        var outgoingByLane = new HashSet<LaneId>();
        for (int i = 0; i < graph.Movements.Count; i++)
        {
            outgoingByLane.Add(graph.Movements[i].SourceLaneId);
        }

        for (int i = 0; i < graph.Lanes.Count; i++)
        {
            LaneRecord lane = graph.Lanes[i];
            if (outgoingByLane.Contains(lane.Id) ||
                HasArrivalPortAt(graph, lane.EndAnchorCell))
            {
                continue;
            }

            diagnostics.AddError(
                TrafficDiagnosticCode.UnmappedIncomingLane,
                "Legal incoming lane has no continuation, port arrival, or explicit dead-end behavior.",
                Source(graph.Version, lane.Id.ToString(), lane.EndAnchorCell));
        }
    }

    private static void ValidateTransitionMappings(
        TrafficGraphSnapshot graph,
        TrafficDiagnosticCollection diagnostics)
    {
        var mandatoryByOwner = new Dictionary<ControlledNodeId, HashSet<LaneId>>();
        for (int i = 0; i < graph.Movements.Count; i++)
        {
            MovementRecord movement = graph.Movements[i];
            if (movement.Kind != TrafficMovementKind.MandatoryMerge &&
                movement.Kind != TrafficMovementKind.LaneExpansion &&
                movement.Kind != TrafficMovementKind.LaneContinuation)
            {
                continue;
            }

            if (!mandatoryByOwner.TryGetValue(
                    movement.OwnerId,
                    out HashSet<LaneId> lanes))
            {
                lanes = new HashSet<LaneId>();
                mandatoryByOwner.Add(movement.OwnerId, lanes);
            }
            lanes.Add(movement.SourceLaneId);
        }

        for (int i = 0; i < graph.MovementOwners.Count; i++)
        {
            MovementOwnerRecord owner = graph.MovementOwners[i];
            if (owner.Kind != TrafficMovementOwnerKind.TransitionBoundary) continue;
            if (mandatoryByOwner.ContainsKey(owner.Id)) continue;

            diagnostics.AddWarning(
                TrafficDiagnosticCode.UnmappedIncomingLane,
                "Transition boundary has no lane continuation, merge, or expansion movement.",
                Source(graph.Version, owner.Id.ToString(), owner.PrimaryCell));
        }
    }

    private static bool HasArrivalPortAt(TrafficGraphSnapshot graph, Vector2Int cell)
    {
        for (int i = 0; i < graph.BuildingPortAnchors.Count; i++)
        {
            BuildingPortAnchorRecord port = graph.BuildingPortAnchors[i];
            if (port.PortCell == cell && port.ArrivalAnchors.Count > 0) return true;
        }

        return false;
    }

    private static bool HasErrorsSince(
        TrafficDiagnosticCollection diagnostics,
        int initialCount)
    {
        for (int i = initialCount; i < diagnostics.Count; i++)
        {
            if (diagnostics[i].Severity == TrafficDiagnosticSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDirectionBit(int bit) =>
        bit > 0 && bit <= 128 && (bit & (bit - 1)) == 0;

    private static void AddDuplicateId(
        TrafficDiagnosticCollection diagnostics,
        TrafficGraphVersion version,
        string stableId,
        Vector2Int cell = default)
    {
        diagnostics.AddError(
            TrafficDiagnosticCode.DuplicateStableId,
            "Graph validator found a duplicate or invalid stable ID.",
            cell == default
                ? Source(version, stableId)
                : Source(version, stableId, cell));
    }

    private static TrafficDiagnosticSource Source(
        TrafficGraphVersion version,
        string stableId)
    {
        return new TrafficDiagnosticSource(
            version,
            stableId,
            string.Empty,
            Vector2Int.zero,
            false,
            VehicleSimulationId.Invalid);
    }

    private static TrafficDiagnosticSource Source(
        TrafficGraphVersion version,
        string stableId,
        Vector2Int cell)
    {
        return TrafficDiagnosticSource.ForCell(version, cell, stableId);
    }
}
