using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class TrafficGraphValidatorTests
{
    [Test]
    public void ValidFixturePublishesWithoutErrors()
    {
        TrafficGraphSnapshot graph = CreateGraph();
        var diagnostics = new TrafficDiagnosticCollection();

        Assert.IsTrue(TrafficGraphValidator.Validate(graph, diagnostics));
        Assert.IsFalse(diagnostics.HasErrors);
    }

    [Test]
    public void MissingConnectorTargetFailsValidation()
    {
        TrafficGraphSnapshot graph = CreateGraph(useInvalidTargetLane: true);
        var diagnostics = new TrafficDiagnosticCollection();

        Assert.IsFalse(TrafficGraphValidator.Validate(graph, diagnostics));
        Assert.IsTrue(diagnostics.Contains(TrafficDiagnosticCode.MissingLaneReference));
    }

    [Test]
    public void EmptyGeometryFailsValidation()
    {
        TrafficGraphSnapshot graph = CreateGraph(useInvalidMovementGeometry: true);
        var diagnostics = new TrafficDiagnosticCollection();

        Assert.IsFalse(TrafficGraphValidator.Validate(graph, diagnostics));
        Assert.IsTrue(diagnostics.Contains(TrafficDiagnosticCode.MissingLaneReference));
    }

    [Test]
    public void ExactBuildingPortAnchorIsReachable()
    {
        TrafficGraphSnapshot graph = CreateGraph(includeEntryPort: true);
        BuildingPortAnchorId portId = BuildingPortAnchorId.FromStableKey("PORT");

        Assert.IsTrue(TrafficGraphValidator.TryGetReachablePortAnchor(
            graph,
            portId,
            PortType.Entry,
            out BuildingPortAnchorRecord anchor));
        Assert.AreEqual(new Vector2Int(1, 0), anchor.PortCell);
    }

    [Test]
    public void CompilerBuildsReachableAnchorForMidSectionBuildingPort()
    {
        RoadProfile profile = CompileRoadProfile(
            "mid-section-port-road",
            RoadPermissionMask.GeneralRoad,
            VehicleCapabilityMask.GroundRoad,
            RoadMovementPolicyMask.All);
        BuildingPortAnchorId portId =
            BuildingPortAnchorId.FromStableKey("MID_PORT");

        RoadNetworkSnapshot snapshot = RoadNetworkSnapshot.CreateForCompilation(
            1,
            1,
            1f,
            1f,
            new[]
            {
                CreateRoadCell(
                    new Vector2Int(0, 0),
                    4,
                    RoadNodeKind.RoadEnd,
                    profile),
                CreateRoadCell(
                    new Vector2Int(1, 0),
                    4 | 64,
                    RoadNodeKind.ThroughRoad,
                    profile),
                CreateRoadCell(
                    new Vector2Int(2, 0),
                    64,
                    RoadNodeKind.RoadEnd,
                    profile)
            },
            new[] { profile },
            new[]
            {
                new BuildingPortRecord(
                    portId,
                    new Vector2Int(1, 1),
                    new Vector2Int(1, 0),
                    PortType.Both)
            });

        Assert.IsTrue(TrafficGraphCompiler.TryCompile(
            snapshot,
            new TrafficGraphVersion(1),
            out TrafficGraphSnapshot graph,
            out TrafficDiagnosticCollection diagnostics));
        Assert.IsFalse(diagnostics.HasErrors);
        Assert.IsTrue(TrafficGraphValidator.TryGetReachablePortAnchor(
            graph,
            portId,
            PortType.Both,
            out BuildingPortAnchorRecord anchor));
        Assert.AreEqual(new Vector2Int(1, 0), anchor.PortCell);
        Assert.Greater(anchor.ArrivalAnchors.Count, 0);
        Assert.Greater(anchor.DepartureAnchors.Count, 0);
        Assert.IsTrue(HasInteriorAnchor(anchor.ArrivalAnchors));
        Assert.IsTrue(HasInteriorAnchor(anchor.DepartureAnchors));
    }

    [Test]
    public void CompilerAllowsDisconnectedBuildingPortUntilRoadConnectsIt()
    {
        RoadProfile profile = CompileRoadProfile(
            "disconnected-port-road",
            RoadPermissionMask.GeneralRoad,
            VehicleCapabilityMask.GroundRoad,
            RoadMovementPolicyMask.All);
        BuildingPortAnchorId portId =
            BuildingPortAnchorId.FromStableKey("DISCONNECTED_PORT");

        RoadNetworkSnapshot snapshot = RoadNetworkSnapshot.CreateForCompilation(
            1,
            1,
            1f,
            1f,
            new[]
            {
                CreateRoadCell(
                    new Vector2Int(0, 0),
                    4,
                    RoadNodeKind.RoadEnd,
                    profile),
                CreateRoadCell(
                    new Vector2Int(1, 0),
                    4 | 64,
                    RoadNodeKind.ThroughRoad,
                    profile),
                CreateRoadCell(
                    new Vector2Int(2, 0),
                    64,
                    RoadNodeKind.RoadEnd,
                    profile)
            },
            new[] { profile },
            new[]
            {
                new BuildingPortRecord(
                    portId,
                    new Vector2Int(5, 5),
                    new Vector2Int(5, 4),
                    PortType.Both)
            });

        Assert.IsTrue(TrafficGraphCompiler.TryCompile(
            snapshot,
            new TrafficGraphVersion(1),
            out TrafficGraphSnapshot graph,
            out TrafficDiagnosticCollection diagnostics));
        Assert.IsFalse(diagnostics.HasErrors);
        Assert.IsTrue(diagnostics.Contains(TrafficDiagnosticCode.UnreachableBuildingPort));
        Assert.IsFalse(TrafficGraphValidator.TryGetReachablePortAnchor(
            graph,
            portId,
            PortType.Both,
            out _));
    }

    [Test]
    public void VehicleVariationUsesPermissionMaskWithoutConcreteBranch()
    {
        RoadProfile serviceRoad = CompileRoadProfile(
            "service-road",
            RoadPermissionMask.ServiceRoad,
            VehicleCapabilityMask.HeavyVehicle,
            RoadMovementPolicyMask.All);
        RoadProfile generalRoad = CompileRoadProfile(
            "general-road",
            RoadPermissionMask.GeneralRoad,
            VehicleCapabilityMask.GroundRoad,
            RoadMovementPolicyMask.All);
        VehicleTrafficProfile serviceTruck = CompileVehicleProfile(
            "service-truck",
            RoadPermissionMask.ServiceRoad,
            VehicleCapabilityMask.HeavyVehicle);

        var validDiagnostics = new TrafficDiagnosticCollection();
        Assert.IsTrue(TrafficProfileLegality.ValidateVehicleCanUseRoad(
            serviceRoad,
            serviceTruck,
            validDiagnostics));
        Assert.IsFalse(validDiagnostics.HasErrors);

        var invalidDiagnostics = new TrafficDiagnosticCollection();
        Assert.IsFalse(TrafficProfileLegality.ValidateVehicleCanUseRoad(
            generalRoad,
            serviceTruck,
            invalidDiagnostics));
        Assert.IsTrue(invalidDiagnostics.Contains(
            TrafficDiagnosticCode.IllegalProfileCombination));
    }

    [Test]
    public void RoadVariationUsesMovementPolicyMaskWithoutNameBranch()
    {
        RoadProfile throughOnly = CompileRoadProfile(
            "through-only",
            RoadPermissionMask.All,
            VehicleCapabilityMask.All,
            RoadMovementPolicyMask.LaneContinuation |
            RoadMovementPolicyMask.BuildingPort);
        RoadProfile intersectionCapable = CompileRoadProfile(
            "intersection-capable",
            RoadPermissionMask.All,
            VehicleCapabilityMask.All,
            RoadMovementPolicyMask.LaneContinuation |
            RoadMovementPolicyMask.Intersection |
            RoadMovementPolicyMask.BuildingPort);

        var invalidDiagnostics = new TrafficDiagnosticCollection();
        Assert.IsFalse(TrafficProfileLegality.ValidateRoadSupportsMovement(
            throughOnly,
            RoadMovementPolicyMask.Intersection,
            invalidDiagnostics));
        Assert.IsTrue(invalidDiagnostics.Contains(
            TrafficDiagnosticCode.IllegalProfileCombination));

        var validDiagnostics = new TrafficDiagnosticCollection();
        Assert.IsTrue(TrafficProfileLegality.ValidateRoadSupportsMovement(
            intersectionCapable,
            RoadMovementPolicyMask.Intersection,
            validDiagnostics));
        Assert.IsFalse(validDiagnostics.HasErrors);
    }

    private static TrafficGraphSnapshot CreateGraph(
        bool useInvalidTargetLane = false,
        bool useInvalidMovementGeometry = false,
        bool includeEntryPort = false)
    {
        var version = new TrafficGraphVersion(1);
        RoadSectionId sectionId = RoadSectionId.FromStableKey("SECTION");
        LaneId laneId = LaneId.FromStableKey("LANE");
        LaneId targetLaneId = useInvalidTargetLane
            ? LaneId.FromStableKey("MISSING_LANE")
            : laneId;
        MovementId movementId = MovementId.FromStableKey("MOVE");
        ControlledNodeId ownerId = ControlledNodeId.FromStableKey("OWNER");
        TrafficGeometryId centerlineGeometryId =
            TrafficGeometryId.FromStableKey("CENTERLINE_GEOMETRY");
        TrafficGeometryId laneGeometryId =
            TrafficGeometryId.FromStableKey("LANE_GEOMETRY");
        TrafficGeometryId movementGeometryId =
            TrafficGeometryId.FromStableKey("MOVEMENT_GEOMETRY");
        LaneSegmentId laneSegmentId = LaneSegmentId.FromStableKey("LANE_SEGMENT");

        var section = new RoadSectionRecord(
            sectionId,
            new Vector2Int(0, 0),
            new Vector2Int(1, 0),
            1,
            16,
            RoadProfileId.FromStableKey("PROFILE"),
            centerlineGeometryId,
            RoadPermissionMask.GeneralRoad,
            VehicleCapabilityMask.GroundRoad,
            new[] { laneId },
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) });

        var lane = new LaneRecord(
            laneId,
            sectionId,
            TrafficLaneFlowDirection.SectionStartToEnd,
            0,
            1,
            new Vector2Int(0, 0),
            new Vector2Int(1, 0),
            1,
            16,
            RoadPermissionMask.GeneralRoad,
            VehicleCapabilityMask.GroundRoad,
            1f,
            laneGeometryId,
            new[] { movementId },
            new[] { laneSegmentId });

        var movement = new MovementRecord(
            movementId,
            laneId,
            targetLaneId,
            TrafficMovementKind.RoadEndUTurn,
            ownerId,
            new Vector2Int(1, 0),
            16,
            1,
            TrafficTurnType.UTurn,
            RoadPermissionMask.GeneralRoad,
            VehicleCapabilityMask.GroundRoad,
            movementGeometryId,
            laneSegmentId,
            laneSegmentId,
            1f,
            0f,
            true,
            0,
            17);

        var owner = new MovementOwnerRecord(
            ownerId,
            TrafficMovementOwnerKind.RoadEnd,
            new Vector2Int(1, 0),
            sectionId,
            new[] { new Vector2Int(1, 0) });

        var segment = new LaneTraversalSegmentRecord(
            laneSegmentId,
            laneId,
            0,
            0f,
            1f,
            laneGeometryId);

        var geometry = new List<TrafficGeometryRecord>
        {
            new TrafficGeometryRecord(
                centerlineGeometryId,
                new[] { Vector3.zero, Vector3.right }),
            new TrafficGeometryRecord(
                laneGeometryId,
                new[] { Vector3.zero, Vector3.right }),
            new TrafficGeometryRecord(
                movementGeometryId,
                useInvalidMovementGeometry
                    ? new[] { Vector3.right }
                    : new[] { Vector3.right, Vector3.zero })
        };

        var ports = new List<BuildingPortAnchorRecord>();
        if (includeEntryPort)
        {
            ports.Add(new BuildingPortAnchorRecord(
                BuildingPortAnchorId.FromStableKey("PORT"),
                new Vector2Int(2, 0),
                new Vector2Int(1, 0),
                PortType.Entry,
                new[] { new LanePositionAnchorRecord(laneId, 1f) },
                new LanePositionAnchorRecord[0]));
        }

        return new TrafficGraphSnapshot(
            version,
            1,
            1,
            new[] { section },
            new[] { lane },
            new[] { movement },
            new ControlledNodeRecord[0],
            new[] { owner },
            new[] { segment },
            geometry,
            ports,
            new MovementRejectionRecord[0]);
    }

    private static RoadCellRecord CreateRoadCell(
        Vector2Int cell,
        int connections,
        RoadNodeKind nodeKind,
        RoadProfile profile)
    {
        return new RoadCellRecord(
            cell,
            0,
            connections,
            connections,
            connections,
            profile.Id,
            nodeKind,
            new Vector3(cell.x + 0.5f, 0f, cell.y + 0.5f));
    }

    private static bool HasInteriorAnchor(
        IReadOnlyList<LanePositionAnchorRecord> anchors)
    {
        for (int i = 0; i < anchors.Count; i++)
        {
            if (anchors[i].DistanceUnits > 0.01f)
            {
                return true;
            }
        }

        return false;
    }

    private static RoadProfile CompileRoadProfile(
        string key,
        RoadPermissionMask permissions,
        VehicleCapabilityMask capabilities,
        RoadMovementPolicyMask movements)
    {
        RoadType roadType = ScriptableObject.CreateInstance<RoadType>();
        roadType.trafficProfileKey = key;
        roadType.roadName = key;
        roadType.lanesPerWay = 1;
        roadType.isTwoWay = true;
        roadType.speedLimit = 12f;
        roadType.roadWidth = 0.5f;
        roadType.curbWidth = 0.1f;
        roadType.allowedTrafficPermissions = permissions;
        roadType.allowedVehicleCapabilities = capabilities;
        roadType.supportedTrafficMovements = movements;

        var diagnostics = new TrafficDiagnosticCollection();
        Assert.IsTrue(roadType.TryCompileTrafficProfile(
            diagnostics,
            out RoadProfile profile));
        UnityEngine.Object.DestroyImmediate(roadType);
        return profile;
    }

    private static VehicleTrafficProfile CompileVehicleProfile(
        string key,
        RoadPermissionMask permissions,
        VehicleCapabilityMask capabilities)
    {
        PopTransportVehicleData vehicleData =
            ScriptableObject.CreateInstance<PopTransportVehicleData>();
        vehicleData.trafficProfileKey = key;
        vehicleData.vehicleName = key;
        vehicleData.maximumVehicleSpeed = 8f;
        vehicleData.vehicleLengthUnits = 2.5f;
        vehicleData.minimumFollowingGapUnits = 0.5f;
        vehicleData.accelerationUnitsPerSecondSquared = 6f;
        vehicleData.decelerationUnitsPerSecondSquared = 8f;
        vehicleData.emergencyDecelerationUnitsPerSecondSquared = 12f;
        vehicleData.desiredTimeHeadwaySeconds = 1.5f;
        vehicleData.maximumJerkUnitsPerSecondCubed = 20f;
        vehicleData.trafficRoadPermissions = permissions;
        vehicleData.trafficCapabilities = capabilities;

        var diagnostics = new TrafficDiagnosticCollection();
        Assert.IsTrue(vehicleData.TryCompileTrafficProfile(
            diagnostics,
            out VehicleTrafficProfile profile));
        UnityEngine.Object.DestroyImmediate(vehicleData);
        return profile;
    }
}
