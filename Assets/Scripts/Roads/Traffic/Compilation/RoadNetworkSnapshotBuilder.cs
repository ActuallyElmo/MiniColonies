using System;
using System.Collections.Generic;
using UnityEngine;

public static class RoadNetworkSnapshotBuilder
{
    private readonly struct RoadCellSource
    {
        public readonly Vector2Int GridPosition;
        public readonly int ElevationLayer;
        public readonly int PhysicalConnections;
        public readonly int LegalOutgoingDirections;
        public readonly int LegalIncomingDirections;
        public readonly RoadType RoadType;
        public readonly RoadNodeKind NodeKind;

        public RoadCellSource(RoadCell cell, RoadNodeKind nodeKind)
        {
            GridPosition = cell.gridPosition;
            ElevationLayer = cell.elevationLayer;
            PhysicalConnections = cell.connections;
            LegalOutgoingDirections = cell.outConnections;
            LegalIncomingDirections = cell.inConnections;
            RoadType = cell.roadType;
            NodeKind = nodeKind;
        }
    }

    private sealed class IntersectionPolicySource
    {
        public Vector2Int GridPosition;
        public RoadNodeKind NodeKind;
        public IntersectionRuleType RuleType;
        public int PriorityDirectionBitA;
        public int PriorityDirectionBitB;
        public float TrafficLightCycleSeconds;
        public LaneConnectionRuleRecord[] CustomRules;
        public LaneConnectionRuleRecord[] DisabledRules;
    }

    private readonly struct BuildingPortSource
    {
        public readonly Vector2Int BuildingOriginCell;
        public readonly Vector2Int PortCell;
        public readonly PortType PortType;

        public BuildingPortSource(
            Vector2Int buildingOriginCell,
            Vector2Int portCell,
            PortType portType)
        {
            BuildingOriginCell = buildingOriginCell;
            PortCell = portCell;
            PortType = portType;
        }
    }

    public static bool TryBuild(
        RoadSystemBackend roadSystem,
        BuildingSystemBackend buildingSystem,
        TrafficDiagnosticCollection diagnostics,
        out RoadNetworkSnapshot snapshot)
    {
        if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));
        snapshot = null;

        if (roadSystem == null)
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.MissingRoadSystem,
                "Cannot capture a road snapshot without RoadSystemBackend.");
            return false;
        }

        WorldManager world = WorldManager.Instance;
        if (world == null)
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.MissingWorldGeometry,
                "Cannot capture road geometry without WorldManager.");
            return false;
        }

        int roadRevision = roadSystem.AuthoringRevision;
        int buildingRevision = buildingSystem != null ? buildingSystem.AuthoringRevision : 0;

        List<RoadCellSource> roadSources = CaptureRoadSources(roadSystem);
        List<IntersectionPolicySource> policySources = CapturePolicySources(roadSystem);
        List<BuildingPortSource> portSources = CaptureBuildingPortSources(buildingSystem);

        if (roadSystem.AuthoringRevision != roadRevision ||
            (buildingSystem != null && buildingSystem.AuthoringRevision != buildingRevision))
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.SnapshotSourceChanged,
                "Road or building authoring state changed while snapshot inputs were being captured.");
            return false;
        }

        var profilesByAsset = new Dictionary<RoadType, RoadProfile>();
        var profilesById = new Dictionary<RoadProfileId, RoadProfile>();
        var cells = new List<RoadCellRecord>(roadSources.Count);

        for (int i = 0; i < roadSources.Count; i++)
        {
            RoadCellSource source = roadSources[i];
            if (!TryGetCompiledProfile(
                    source.RoadType,
                    profilesByAsset,
                    profilesById,
                    diagnostics,
                    out RoadProfile profile))
            {
                continue;
            }

            float worldY =
                world.GetPhysicalHeight(
                    source.GridPosition.x + 0.5f,
                    source.GridPosition.y + 0.5f) *
                world.heightStep;
            Vector3 worldCenter = new Vector3(
                source.GridPosition.x * world.cellSize + world.cellSize * 0.5f,
                worldY,
                source.GridPosition.y * world.cellSize + world.cellSize * 0.5f);

            cells.Add(new RoadCellRecord(
                source.GridPosition,
                source.ElevationLayer,
                source.PhysicalConnections,
                source.LegalOutgoingDirections,
                source.LegalIncomingDirections,
                profile.Id,
                source.NodeKind,
                worldCenter));
        }

        ValidatePhysicalNeighbors(cells, diagnostics);

        var profiles = new List<RoadProfile>(profilesById.Values);
        profiles.Sort((left, right) => left.Id.CompareTo(right.Id));

        var ports = new List<BuildingPortRecord>(portSources.Count);
        for (int i = 0; i < portSources.Count; i++)
        {
            BuildingPortSource source = portSources[i];
            string stableKey = FormattableString.Invariant(
                $"BUILDING:{source.BuildingOriginCell.x},{source.BuildingOriginCell.y}/PORT:{source.PortCell.x},{source.PortCell.y}/{source.PortType}");
            ports.Add(new BuildingPortRecord(
                BuildingPortAnchorId.FromStableKey(stableKey),
                source.BuildingOriginCell,
                source.PortCell,
                source.PortType));
        }

        var policies = new List<IntersectionPolicyRecord>(policySources.Count);
        for (int i = 0; i < policySources.Count; i++)
        {
            IntersectionPolicySource source = policySources[i];
            string stableKey = FormattableString.Invariant(
                $"CONTROLLED_NODE:{source.GridPosition.x},{source.GridPosition.y}");
            policies.Add(new IntersectionPolicyRecord(
                ControlledNodeId.FromStableKey(stableKey),
                source.GridPosition,
                source.NodeKind,
                source.RuleType,
                source.PriorityDirectionBitA,
                source.PriorityDirectionBitB,
                source.TrafficLightCycleSeconds,
                source.CustomRules,
                source.DisabledRules));
        }

        if (diagnostics.HasErrors) return false;

        if (roadSystem.AuthoringRevision != roadRevision ||
            (buildingSystem != null && buildingSystem.AuthoringRevision != buildingRevision))
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.SnapshotSourceChanged,
                "Road or building authoring state changed before snapshot construction completed.");
            return false;
        }

        snapshot = new RoadNetworkSnapshot(
            roadRevision,
            buildingRevision,
            world.cellSize,
            world.heightStep,
            cells,
            profiles,
            ports,
            policies);
        return true;
    }

    private static List<RoadCellSource> CaptureRoadSources(RoadSystemBackend roadSystem)
    {
        var positions = new List<Vector2Int>(roadSystem.Roads.Keys);
        positions.Sort(CompareCells);

        var sources = new List<RoadCellSource>(positions.Count);
        for (int i = 0; i < positions.Count; i++)
        {
            if (roadSystem.Roads.TryGetValue(positions[i], out RoadCell cell) && cell != null)
            {
                sources.Add(new RoadCellSource(cell, roadSystem.GetRoadNodeKind(cell)));
            }
        }

        return sources;
    }

    private static List<IntersectionPolicySource> CapturePolicySources(
        RoadSystemBackend roadSystem)
    {
        var positions = new List<Vector2Int>(roadSystem.Intersections.Keys);
        positions.Sort(CompareCells);

        var sources = new List<IntersectionPolicySource>(positions.Count);
        for (int i = 0; i < positions.Count; i++)
        {
            if (!roadSystem.Intersections.TryGetValue(
                    positions[i],
                    out IntersectionData source) ||
                source == null)
            {
                continue;
            }

            sources.Add(new IntersectionPolicySource
            {
                GridPosition = source.GridPosition,
                NodeKind = source.NodeKind,
                RuleType = source.RuleType,
                PriorityDirectionBitA = source.PriorityDirectionBitA,
                PriorityDirectionBitB = source.PriorityDirectionBitB,
                TrafficLightCycleSeconds = source.TrafficLightCycleSeconds,
                CustomRules = CopyRules(source.CustomRules),
                DisabledRules = CopyRules(source.DisabledRules)
            });
        }

        return sources;
    }

    private static List<BuildingPortSource> CaptureBuildingPortSources(
        BuildingSystemBackend buildingSystem)
    {
        var sources = new List<BuildingPortSource>();
        if (buildingSystem == null) return sources;

        List<Building> buildings = new List<Building>(buildingSystem.GetActiveBuildings());
        buildings.Sort((left, right) =>
        {
            if (left == null) return right == null ? 0 : 1;
            if (right == null) return -1;
            return CompareCells(left.originCell, right.originCell);
        });

        for (int i = 0; i < buildings.Count; i++)
        {
            Building building = buildings[i];
            if (building == null) continue;

            var portCells = new List<Vector2Int>(building.globalPorts.Keys);
            portCells.Sort(CompareCells);
            for (int portIndex = 0; portIndex < portCells.Count; portIndex++)
            {
                Vector2Int portCell = portCells[portIndex];
                if (building.globalPorts.TryGetValue(portCell, out PortType portType))
                {
                    sources.Add(new BuildingPortSource(
                        building.originCell,
                        portCell,
                        portType));
                }
            }
        }

        return sources;
    }

    private static bool TryGetCompiledProfile(
        RoadType roadType,
        Dictionary<RoadType, RoadProfile> profilesByAsset,
        Dictionary<RoadProfileId, RoadProfile> profilesById,
        TrafficDiagnosticCollection diagnostics,
        out RoadProfile profile)
    {
        if (roadType == null)
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.MissingRoadProfile,
                "A captured road cell has no RoadType.");
            profile = null;
            return false;
        }

        if (profilesByAsset.TryGetValue(roadType, out profile)) return true;
        if (!roadType.TryCompileTrafficProfile(diagnostics, out profile)) return false;

        if (profilesById.TryGetValue(profile.Id, out RoadProfile existing) &&
            !ProfilesMatch(existing, profile))
        {
            diagnostics.AddError(
                TrafficDiagnosticCode.DuplicateStableId,
                $"Road profile ID collision between '{existing.SourceKey}' and '{profile.SourceKey}'.",
                TrafficDiagnosticSource.ForProfile(profile.SourceKey, profile.Id.ToString()));
            return false;
        }

        profilesByAsset.Add(roadType, profile);
        profilesById[profile.Id] = profile;
        return true;
    }

    private static void ValidatePhysicalNeighbors(
        IReadOnlyList<RoadCellRecord> cells,
        TrafficDiagnosticCollection diagnostics)
    {
        var byPosition = new HashSet<Vector2Int>();
        for (int i = 0; i < cells.Count; i++) byPosition.Add(cells[i].GridPosition);

        for (int i = 0; i < cells.Count; i++)
        {
            RoadCellRecord cell = cells[i];
            for (int directionIndex = 0; directionIndex < 8; directionIndex++)
            {
                int directionBit = 1 << directionIndex;
                if (!cell.HasPhysicalConnection(directionBit)) continue;

                Vector2Int neighbor =
                    RoadGridDirectionUtility.GetNeighborPosition(cell.GridPosition, directionBit);
                if (!byPosition.Contains(neighbor))
                {
                    diagnostics.AddError(
                        TrafficDiagnosticCode.MissingRoadNeighbor,
                        $"Road cell {cell.GridPosition} references missing physical neighbor {neighbor}.",
                        TrafficDiagnosticSource.ForCell(
                            TrafficGraphVersion.Invalid,
                            cell.GridPosition,
                            cell.RoadProfileId.ToString()));
                }
            }
        }
    }

    private static LaneConnectionRuleRecord[] CopyRules(IReadOnlyList<LaneConnectionRule> source)
    {
        if (source == null || source.Count == 0) return Array.Empty<LaneConnectionRuleRecord>();

        var rules = new LaneConnectionRuleRecord[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            LaneConnectionRule rule = source[i];
            rules[i] = new LaneConnectionRuleRecord(
                rule.FromDirectionBit,
                rule.FromLaneIndex,
                rule.ToDirectionBit,
                rule.ToLaneIndex);
        }

        Array.Sort(rules, CompareRules);
        return rules;
    }

    private static int CompareCells(Vector2Int left, Vector2Int right)
    {
        int xComparison = left.x.CompareTo(right.x);
        return xComparison != 0 ? xComparison : left.y.CompareTo(right.y);
    }

    private static int CompareRules(
        LaneConnectionRuleRecord left,
        LaneConnectionRuleRecord right)
    {
        int result = left.FromDirectionBit.CompareTo(right.FromDirectionBit);
        if (result != 0) return result;
        result = left.FromLaneIndex.CompareTo(right.FromLaneIndex);
        if (result != 0) return result;
        result = left.ToDirectionBit.CompareTo(right.ToDirectionBit);
        return result != 0 ? result : left.ToLaneIndex.CompareTo(right.ToLaneIndex);
    }

    private static bool ProfilesMatch(RoadProfile left, RoadProfile right)
    {
        return left.ForwardLaneCount == right.ForwardLaneCount &&
               left.ReverseLaneCount == right.ReverseLaneCount &&
               left.Directionality == right.Directionality &&
               left.LaneOrdering == right.LaneOrdering &&
               left.SpeedLimitUnitsPerSecond.Equals(right.SpeedLimitUnitsPerSecond) &&
               left.AllowedPermissions == right.AllowedPermissions &&
               left.AllowedCapabilities == right.AllowedCapabilities &&
               left.RoadWidthUnits.Equals(right.RoadWidthUnits) &&
               left.CurbWidthUnits.Equals(right.CurbWidthUnits) &&
               left.SupportedMovements == right.SupportedMovements;
    }
}
