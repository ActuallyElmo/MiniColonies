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
    public void RoadProfileNormalizesLegacyAllBitMasks()
    {
        RoadProfile profile = CompileRoadProfile(
            "legacy-road-mask",
            (RoadPermissionMask)(-1),
            (VehicleCapabilityMask)(-1),
            (RoadMovementPolicyMask)(-1));

        Assert.AreEqual(RoadPermissionMask.All, profile.AllowedPermissions);
        Assert.AreEqual(VehicleCapabilityMask.All, profile.AllowedCapabilities);
        Assert.AreEqual(RoadMovementPolicyMask.All, profile.SupportedMovements);
    }

    [Test]
    public void WideRoadProfileKeepsIntersectionGeometryStandard()
    {
        RoadProfile profile = CompileRoadProfile(
            "wide-profile-rule",
            RoadPermissionMask.GeneralRoad,
            VehicleCapabilityMask.GroundRoad,
            RoadMovementPolicyMask.All,
            isWide: true);

        Assert.IsTrue(profile.IsWide);
        Assert.Greater(
            RoadGeometryRules.GetNodePullbackFraction(profile.IsWide),
            RoadGeometryRules.GetNodePullbackFraction(false));
        Assert.AreEqual(
            RoadGeometryRules.GetIntersectionVisualOuterDiameter(
                profile.RoadWidthUnits,
                profile.CurbWidthUnits),
            RoadGeometryRules.GetIntersectionGeometryOuterDiameter(
                profile.RoadWidthUnits,
                profile.CurbWidthUnits));
        Assert.AreEqual(
            profile.RoadWidthUnits + profile.CurbWidthUnits,
            RoadGeometryRules.GetIntersectionGeometryOuterDiameter(
                profile.RoadWidthUnits,
                profile.CurbWidthUnits),
            0.001f);
    }

    [Test]
    public void WideRoadCornerCenterlineUsesLargerTurnRadius()
    {
        TrafficGeometryRecord standard = CompileCornerCenterline(false);
        TrafficGeometryRecord wide = CompileCornerCenterline(true);
        Vector3 authoredCorner = new Vector3(1.5f, 0f, 0.5f);

        float standardClosestDistance =
            GetClosestPlanarDistance(standard.Samples, authoredCorner);
        float wideClosestDistance =
            GetClosestPlanarDistance(wide.Samples, authoredCorner);

        Assert.Greater(wide.Samples.Count, standard.Samples.Count);
        Assert.Less(
            GetMaximumPlanarHeadingChange(wide.Samples),
            GetMaximumPlanarHeadingChange(standard.Samples),
            "Wide centerline subdivision should reduce the steering angle between consecutive segments.");
        Assert.Greater(
            wideClosestDistance,
            standardClosestDistance + 0.05f,
            "Wide centerline nodes should begin and end a 90-degree turn farther from the authored corner.");
    }

    [Test]
    public void WideRoadIntersectionMovementMatchesNormalRoadGeometry()
    {
        TrafficGeometryRecord standard = CompileIntersectionTurnGeometry(false);
        TrafficGeometryRecord wide = CompileIntersectionTurnGeometry(true);
        Assert.AreEqual(
            standard.Samples.Count,
            wide.Samples.Count,
            "Wide intersections should use the normal-road movement geometry.");
        for (int i = 0; i < standard.Samples.Count; i++)
        {
            Assert.Less(
                Vector3.Distance(standard.Samples[i], wide.Samples[i]),
                0.001f,
                "IsWide must not change geometry inside an intersection.");
        }
    }

    [Test]
    public void WideYIntersectionWithCurvedArmsMatchesNormalRoadGeometry()
    {
        TrafficGeometryRecord standard =
            CompileYIntersectionTurnGeometry(false);
        TrafficGeometryRecord wide =
            CompileYIntersectionTurnGeometry(true);

        AssertGeometrySamplesEqual(
            standard,
            wide,
            "Wide Y-intersections must use normal-road approach and movement geometry.");
    }

    [Test]
    public void WideRoadTypeTransitionMatchesNormalRoadGeometry()
    {
        TrafficGeometryRecord standard =
            CompileRoadTypeTransitionGeometry(false, false);
        TrafficGeometryRecord wideFirst =
            CompileRoadTypeTransitionGeometry(true, false);
        TrafficGeometryRecord wideSecond =
            CompileRoadTypeTransitionGeometry(false, true);

        AssertGeometrySamplesEqual(
            standard,
            wideFirst,
            "IsWide on the first road must not change transition geometry.");
        AssertGeometrySamplesEqual(
            standard,
            wideSecond,
            "IsWide on the second road must not change transition geometry.");
    }

    [Test]
    public void VehicleProfileNormalizesLegacyAllBitMasks()
    {
        VehicleTrafficProfile profile = CompileVehicleProfile(
            "legacy-vehicle-mask",
            (RoadPermissionMask)(-1),
            (VehicleCapabilityMask)(-1));

        Assert.AreEqual(RoadPermissionMask.All, profile.RoadPermissions);
        Assert.AreEqual(VehicleCapabilityMask.All, profile.Capabilities);
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
    public void PortTypeVehicleFilteringSeparatesPopAndResourceTransportation()
    {
        PopTransportVehicleData popVehicle =
            ScriptableObject.CreateInstance<PopTransportVehicleData>();
        ResourceTransportVehicleData resourceVehicle =
            ScriptableObject.CreateInstance<ResourceTransportVehicleData>();

        try
        {
            Assert.IsTrue(PortType.Entry.AllowsVehicleFlow(
                PortFlowDirection.Entry,
                popVehicle));
            Assert.IsTrue(PortType.Entry.AllowsVehicleFlow(
                PortFlowDirection.Entry,
                resourceVehicle));

            Assert.IsTrue(PortType.PopEntry.AllowsVehicleFlow(
                PortFlowDirection.Entry,
                popVehicle));
            Assert.IsFalse(PortType.PopEntry.AllowsVehicleFlow(
                PortFlowDirection.Entry,
                resourceVehicle));

            Assert.IsTrue(PortType.ResourceExit.AllowsVehicleFlow(
                PortFlowDirection.Exit,
                resourceVehicle));
            Assert.IsFalse(PortType.ResourceExit.AllowsVehicleFlow(
                PortFlowDirection.Exit,
                popVehicle));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(popVehicle);
            UnityEngine.Object.DestroyImmediate(resourceVehicle);
        }
    }

    [Test]
    public void CompilerBuildsReachableAnchorForPopSpecificBuildingPort()
    {
        RoadProfile profile = CompileRoadProfile(
            "pop-specific-port-road",
            RoadPermissionMask.GeneralRoad,
            VehicleCapabilityMask.GroundRoad,
            RoadMovementPolicyMask.All);
        BuildingPortAnchorId portId =
            BuildingPortAnchorId.FromStableKey("POP_SPECIFIC_PORT");

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
                    PortType.PopBoth)
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
            PortType.PopBoth,
            out BuildingPortAnchorRecord anchor));
        Assert.AreEqual(PortType.PopBoth, anchor.PortType);
        Assert.Greater(anchor.ArrivalAnchors.Count, 0);
        Assert.Greater(anchor.DepartureAnchors.Count, 0);
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
    public void CompilerKeepsRoadEndLaneEndpointsAtTileCenters()
    {
        RoadProfile profile = CompileRoadProfile(
            "centered-road-end",
            RoadPermissionMask.GeneralRoad,
            VehicleCapabilityMask.GroundRoad,
            RoadMovementPolicyMask.All);
        Vector2Int leftRoadEnd = Vector2Int.zero;
        Vector2Int rightRoadEnd = new Vector2Int(2, 0);

        RoadNetworkSnapshot snapshot = RoadNetworkSnapshot.CreateForCompilation(
            1,
            1,
            1f,
            1f,
            new[]
            {
                CreateRoadCell(leftRoadEnd, 4, RoadNodeKind.RoadEnd, profile),
                CreateRoadCell(
                    new Vector2Int(1, 0),
                    4 | 64,
                    RoadNodeKind.ThroughRoad,
                    profile),
                CreateRoadCell(rightRoadEnd, 64, RoadNodeKind.RoadEnd, profile)
            },
            new[] { profile });

        Assert.IsTrue(TrafficGraphCompiler.TryCompile(
            snapshot,
            new TrafficGraphVersion(1),
            out TrafficGraphSnapshot graph,
            out TrafficDiagnosticCollection diagnostics));
        Assert.IsFalse(diagnostics.HasErrors);

        int checkedEndpoints = 0;
        for (int i = 0; i < graph.Lanes.Count; i++)
        {
            LaneRecord lane = graph.Lanes[i];
            Assert.IsTrue(graph.TryGetGeometry(
                lane.GeometryId,
                out TrafficGeometryRecord geometry));

            if (lane.StartAnchorCell == leftRoadEnd)
            {
                Assert.AreEqual(0.5f, geometry.Samples[0].x, 0.001f);
                checkedEndpoints++;
            }

            if (lane.EndAnchorCell == leftRoadEnd)
            {
                Assert.AreEqual(
                    0.5f,
                    geometry.Samples[geometry.Samples.Count - 1].x,
                    0.001f);
                checkedEndpoints++;
            }

            if (lane.StartAnchorCell == rightRoadEnd)
            {
                Assert.AreEqual(2.5f, geometry.Samples[0].x, 0.001f);
                checkedEndpoints++;
            }

            if (lane.EndAnchorCell == rightRoadEnd)
            {
                Assert.AreEqual(
                    2.5f,
                    geometry.Samples[geometry.Samples.Count - 1].x,
                    0.001f);
                checkedEndpoints++;
            }
        }

        Assert.Greater(checkedEndpoints, 0);
    }

    [Test]
    public void CompilerPullsWideIntersectionLaneEndsBehindVisualCap()
    {
        RoadProfile profile = CompileRoadProfile(
            "wide-intersection-road",
            RoadPermissionMask.GeneralRoad,
            VehicleCapabilityMask.GroundRoad,
            RoadMovementPolicyMask.All,
            0.9f,
            0.3f,
            3);
        Vector2Int intersection = Vector2Int.zero;
        Vector3 intersectionCenter = new Vector3(0.5f, 0f, 0.5f);

        RoadNetworkSnapshot snapshot = RoadNetworkSnapshot.CreateForCompilation(
            1,
            1,
            1f,
            1f,
            new[]
            {
                CreateRoadCell(
                    new Vector2Int(-1, 0),
                    4,
                    RoadNodeKind.RoadEnd,
                    profile),
                CreateRoadCell(
                    intersection,
                    1 | 4 | 64 | 128,
                    RoadNodeKind.Intersection,
                    profile),
                CreateRoadCell(
                    new Vector2Int(1, 0),
                    64,
                    RoadNodeKind.RoadEnd,
                    profile),
                CreateRoadCell(
                    new Vector2Int(0, 1),
                    16,
                    RoadNodeKind.RoadEnd,
                    profile),
                CreateRoadCell(
                    new Vector2Int(-1, 1),
                    8,
                    RoadNodeKind.RoadEnd,
                    profile)
            },
            new[] { profile });

        Assert.IsTrue(TrafficGraphCompiler.TryCompile(
            snapshot,
            new TrafficGraphVersion(1),
            out TrafficGraphSnapshot graph,
            out TrafficDiagnosticCollection diagnostics));
        Assert.IsFalse(diagnostics.HasErrors);

        float expectedMinimumPullback = (profile.RoadWidthUnits + profile.CurbWidthUnits) * 0.5f;
        int checkedIncomingLanes = 0;
        var incomingEndpointsByLeg = new Dictionary<int, List<Vector3>>();
        for (int i = 0; i < graph.Lanes.Count; i++)
        {
            LaneRecord lane = graph.Lanes[i];
            if (lane.EndAnchorCell != intersection) continue;

            Assert.IsTrue(graph.TryGetGeometry(
                lane.GeometryId,
                out TrafficGeometryRecord geometry));
            Vector3 laneEnd = geometry.Samples[geometry.Samples.Count - 1];
            laneEnd.y = 0f;
            Assert.GreaterOrEqual(
                Vector3.Distance(laneEnd, intersectionCenter),
                expectedMinimumPullback);
            if (!incomingEndpointsByLeg.TryGetValue(
                    lane.EndLegDirectionBit,
                    out List<Vector3> endpoints))
            {
                endpoints = new List<Vector3>();
                incomingEndpointsByLeg.Add(lane.EndLegDirectionBit, endpoints);
            }
            endpoints.Add(laneEnd);
            checkedIncomingLanes++;
        }

        Assert.Greater(checkedIncomingLanes, 0);
        foreach (KeyValuePair<int, List<Vector3>> kvp in incomingEndpointsByLeg)
        {
            AssertStopLineIsOutsideIntersectionCell(
                kvp.Key,
                intersectionCenter,
                profile,
                new[] { 1, 4, 64, 128 },
                kvp.Value);
            AssertStopLineIsPerpendicularToLeg(
                kvp.Key,
                intersectionCenter,
                kvp.Value);
        }
    }

    [Test]
    public void CompilerKeepsStopLinesOnOwnSideBetweenAdjacentIntersections()
    {
        RoadProfile profile = CompileRoadProfile(
            "adjacent-wide-intersections",
            RoadPermissionMask.GeneralRoad,
            VehicleCapabilityMask.GroundRoad,
            RoadMovementPolicyMask.All,
            0.9f,
            0.3f,
            3);
        Vector2Int leftIntersection = Vector2Int.zero;
        Vector2Int rightIntersection = new Vector2Int(1, 0);
        float boundaryMidpointX = 1f;

        RoadNetworkSnapshot snapshot = RoadNetworkSnapshot.CreateForCompilation(
            1,
            1,
            1f,
            1f,
            new[]
            {
                CreateRoadCell(
                    new Vector2Int(-1, 0),
                    4,
                    RoadNodeKind.RoadEnd,
                    profile),
                CreateRoadCell(
                    leftIntersection,
                    1 | 4 | 64,
                    RoadNodeKind.Intersection,
                    profile),
                CreateRoadCell(
                    new Vector2Int(0, 1),
                    16,
                    RoadNodeKind.RoadEnd,
                    profile),
                CreateRoadCell(
                    rightIntersection,
                    1 | 4 | 64,
                    RoadNodeKind.Intersection,
                    profile),
                CreateRoadCell(
                    new Vector2Int(1, 1),
                    16,
                    RoadNodeKind.RoadEnd,
                    profile),
                CreateRoadCell(
                    new Vector2Int(2, 0),
                    64,
                    RoadNodeKind.RoadEnd,
                    profile)
            },
            new[] { profile });

        Assert.IsTrue(TrafficGraphCompiler.TryCompile(
            snapshot,
            new TrafficGraphVersion(1),
            out TrafficGraphSnapshot graph,
            out TrafficDiagnosticCollection diagnostics));
        Assert.IsFalse(diagnostics.HasErrors);

        int checkedLanes = 0;
        for (int i = 0; i < graph.Lanes.Count; i++)
        {
            LaneRecord lane = graph.Lanes[i];
            bool leftToRight =
                lane.StartAnchorCell == leftIntersection &&
                lane.EndAnchorCell == rightIntersection;
            bool rightToLeft =
                lane.StartAnchorCell == rightIntersection &&
                lane.EndAnchorCell == leftIntersection;
            if (!leftToRight && !rightToLeft) continue;

            Assert.IsTrue(graph.TryGetGeometry(
                lane.GeometryId,
                out TrafficGeometryRecord geometry));
            Vector3 start = geometry.Samples[0];
            Vector3 end = geometry.Samples[geometry.Samples.Count - 1];
            if (leftToRight)
            {
                Assert.Less(start.x, boundaryMidpointX);
                Assert.Greater(end.x, boundaryMidpointX);
            }
            else
            {
                Assert.Greater(start.x, boundaryMidpointX);
                Assert.Less(end.x, boundaryMidpointX);
            }

            checkedLanes++;
        }

        Assert.Greater(checkedLanes, 0);
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
        RoadMovementPolicyMask movements,
        float roadWidth = 0.5f,
        float curbWidth = 0.1f,
        int lanesPerWay = 1,
        bool isWide = false)
    {
        RoadType roadType = ScriptableObject.CreateInstance<RoadType>();
        roadType.trafficProfileKey = key;
        roadType.roadName = key;
        roadType.lanesPerWay = lanesPerWay;
        roadType.isTwoWay = true;
        roadType.speedLimit = 12f;
        roadType.IsWide = isWide;
        roadType.roadWidth = roadWidth;
        roadType.curbWidth = curbWidth;
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

    private static TrafficGeometryRecord CompileCornerCenterline(bool isWide)
    {
        RoadProfile profile = CompileRoadProfile(
            isWide ? "wide-corner" : "standard-corner",
            RoadPermissionMask.GeneralRoad,
            VehicleCapabilityMask.GroundRoad,
            RoadMovementPolicyMask.All,
            isWide: isWide);
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
                    1 | 64,
                    RoadNodeKind.ThroughRoad,
                    profile),
                CreateRoadCell(
                    new Vector2Int(1, 1),
                    16,
                    RoadNodeKind.RoadEnd,
                    profile)
            },
            new[] { profile });

        Assert.IsTrue(TrafficGraphCompiler.TryCompile(
            snapshot,
            new TrafficGraphVersion(1),
            out TrafficGraphSnapshot graph,
            out TrafficDiagnosticCollection diagnostics));
        Assert.IsFalse(diagnostics.HasErrors);
        Assert.AreEqual(1, graph.Sections.Count);
        Assert.IsTrue(graph.TryGetGeometry(
            graph.Sections[0].CenterlineGeometryId,
            out TrafficGeometryRecord geometry));
        return geometry;
    }

    private static TrafficGeometryRecord CompileIntersectionTurnGeometry(
        bool isWide)
    {
        RoadProfile profile = CompileRoadProfile(
            isWide ? "wide-intersection-turn" : "standard-intersection-turn",
            RoadPermissionMask.GeneralRoad,
            VehicleCapabilityMask.GroundRoad,
            RoadMovementPolicyMask.All,
            isWide: isWide);
        Vector2Int intersection = Vector2Int.zero;
        RoadNetworkSnapshot snapshot = RoadNetworkSnapshot.CreateForCompilation(
            1,
            1,
            1f,
            1f,
            new[]
            {
                CreateRoadCell(
                    new Vector2Int(-1, 0),
                    4,
                    RoadNodeKind.RoadEnd,
                    profile),
                CreateRoadCell(
                    intersection,
                    1 | 4 | 64,
                    RoadNodeKind.Intersection,
                    profile),
                CreateRoadCell(
                    new Vector2Int(1, 0),
                    64,
                    RoadNodeKind.RoadEnd,
                    profile),
                CreateRoadCell(
                    new Vector2Int(0, 1),
                    16,
                    RoadNodeKind.RoadEnd,
                    profile)
            },
            new[] { profile });

        Assert.IsTrue(TrafficGraphCompiler.TryCompile(
            snapshot,
            new TrafficGraphVersion(1),
            out TrafficGraphSnapshot graph,
            out TrafficDiagnosticCollection diagnostics));
        Assert.IsFalse(diagnostics.HasErrors);

        for (int i = 0; i < graph.Movements.Count; i++)
        {
            MovementRecord movement = graph.Movements[i];
            if (movement.Kind != TrafficMovementKind.Intersection ||
                movement.FromDirectionBit != 64 ||
                movement.ToDirectionBit != 1 ||
                movement.TurnType == TrafficTurnType.Straight ||
                movement.TurnType == TrafficTurnType.UTurn)
            {
                continue;
            }

            Assert.IsTrue(graph.TryGetGeometry(
                movement.GeometryId,
                out TrafficGeometryRecord geometry));
            return geometry;
        }

        Assert.Fail("Expected the T-intersection fixture to compile its west-to-north movement.");
        return null;
    }

    private static TrafficGeometryRecord CompileRoadTypeTransitionGeometry(
        bool firstIsWide,
        bool secondIsWide)
    {
        RoadProfile profileA = CompileRoadProfile(
            firstIsWide ? "wide-transition-a" : "standard-transition-a",
            RoadPermissionMask.GeneralRoad,
            VehicleCapabilityMask.GroundRoad,
            RoadMovementPolicyMask.All,
            isWide: firstIsWide);
        RoadProfile profileB = CompileRoadProfile(
            secondIsWide ? "wide-transition-b" : "standard-transition-b",
            RoadPermissionMask.GeneralRoad,
            VehicleCapabilityMask.GroundRoad,
            RoadMovementPolicyMask.All,
            isWide: secondIsWide);
        Vector2Int transition = Vector2Int.zero;
        RoadNetworkSnapshot snapshot = RoadNetworkSnapshot.CreateForCompilation(
            1,
            1,
            1f,
            1f,
            new[]
            {
                CreateRoadCell(
                    new Vector2Int(-1, 0),
                    4,
                    RoadNodeKind.RoadEnd,
                    profileA),
                CreateRoadCell(
                    transition,
                    4 | 64,
                    RoadNodeKind.ThroughRoad,
                    profileA),
                CreateRoadCell(
                    new Vector2Int(1, 0),
                    4 | 64,
                    RoadNodeKind.ThroughRoad,
                    profileB),
                CreateRoadCell(
                    new Vector2Int(2, 0),
                    64,
                    RoadNodeKind.RoadEnd,
                    profileB)
            },
            new[] { profileA, profileB });

        Assert.IsTrue(TrafficGraphCompiler.TryCompile(
            snapshot,
            new TrafficGraphVersion(1),
            out TrafficGraphSnapshot graph,
            out TrafficDiagnosticCollection diagnostics));
        Assert.IsFalse(diagnostics.HasErrors);

        for (int i = 0; i < graph.Movements.Count; i++)
        {
            MovementRecord movement = graph.Movements[i];
            if (movement.OwnerCell != transition ||
                movement.Kind != TrafficMovementKind.LaneContinuation ||
                movement.FromDirectionBit != 64 ||
                movement.ToDirectionBit != 4)
            {
                continue;
            }

            Assert.IsTrue(graph.TryGetGeometry(
                movement.GeometryId,
                out TrafficGeometryRecord geometry));
            return geometry;
        }

        Assert.Fail("Expected the mixed road-type fixture to compile its west-to-east transition movement.");
        return null;
    }

    private static TrafficGeometryRecord CompileYIntersectionTurnGeometry(
        bool isWide)
    {
        RoadProfile profile = CompileRoadProfile(
            isWide ? "wide-y-intersection" : "standard-y-intersection",
            RoadPermissionMask.GeneralRoad,
            VehicleCapabilityMask.GroundRoad,
            RoadMovementPolicyMask.All,
            isWide: isWide);
        Vector2Int intersection = Vector2Int.zero;
        RoadNetworkSnapshot snapshot = RoadNetworkSnapshot.CreateForCompilation(
            1,
            1,
            1f,
            1f,
            new[]
            {
                CreateRoadCell(
                    new Vector2Int(-2, -1),
                    4,
                    RoadNodeKind.RoadEnd,
                    profile),
                CreateRoadCell(
                    new Vector2Int(-1, -1),
                    64 | 2,
                    RoadNodeKind.ThroughRoad,
                    profile),
                CreateRoadCell(
                    intersection,
                    32 | 2 | 8,
                    RoadNodeKind.Intersection,
                    profile),
                CreateRoadCell(
                    new Vector2Int(1, 1),
                    32 | 4,
                    RoadNodeKind.ThroughRoad,
                    profile),
                CreateRoadCell(
                    new Vector2Int(2, 1),
                    64,
                    RoadNodeKind.RoadEnd,
                    profile),
                CreateRoadCell(
                    new Vector2Int(1, -1),
                    128 | 4,
                    RoadNodeKind.ThroughRoad,
                    profile),
                CreateRoadCell(
                    new Vector2Int(2, -1),
                    64,
                    RoadNodeKind.RoadEnd,
                    profile)
            },
            new[] { profile });

        Assert.IsTrue(TrafficGraphCompiler.TryCompile(
            snapshot,
            new TrafficGraphVersion(1),
            out TrafficGraphSnapshot graph,
            out TrafficDiagnosticCollection diagnostics));
        Assert.IsFalse(diagnostics.HasErrors);

        for (int i = 0; i < graph.Movements.Count; i++)
        {
            MovementRecord movement = graph.Movements[i];
            if (movement.Kind != TrafficMovementKind.Intersection ||
                movement.OwnerCell != intersection ||
                movement.FromDirectionBit != 32 ||
                movement.ToDirectionBit != 8)
            {
                continue;
            }

            Assert.IsTrue(graph.TryGetGeometry(
                movement.GeometryId,
                out TrafficGeometryRecord geometry));
            return geometry;
        }

        Assert.Fail("Expected the Y-intersection fixture to compile its southwest-to-southeast movement.");
        return null;
    }

    private static void AssertGeometrySamplesEqual(
        TrafficGeometryRecord expected,
        TrafficGeometryRecord actual,
        string message)
    {
        Assert.AreEqual(expected.Samples.Count, actual.Samples.Count, message);
        for (int i = 0; i < expected.Samples.Count; i++)
        {
            Assert.Less(
                Vector3.Distance(expected.Samples[i], actual.Samples[i]),
                0.001f,
                message);
        }
    }

    private static float GetClosestPlanarDistance(
        IReadOnlyList<Vector3> samples,
        Vector3 point)
    {
        float closest = float.MaxValue;
        for (int i = 0; i < samples.Count; i++)
        {
            Vector3 delta = samples[i] - point;
            delta.y = 0f;
            closest = Mathf.Min(closest, delta.magnitude);
        }

        return closest;
    }

    private static float GetMaximumPlanarHeadingChange(
        IReadOnlyList<Vector3> samples)
    {
        float maximumAngle = 0f;
        Vector2 previousDirection = Vector2.zero;
        for (int i = 1; i < samples.Count; i++)
        {
            Vector3 segment = samples[i] - samples[i - 1];
            Vector2 direction = new Vector2(segment.x, segment.z);
            if (direction.sqrMagnitude <= 0.000001f) continue;

            direction.Normalize();
            if (previousDirection.sqrMagnitude > 0f)
            {
                maximumAngle = Mathf.Max(
                    maximumAngle,
                    Vector2.Angle(previousDirection, direction));
            }

            previousDirection = direction;
        }

        return maximumAngle;
    }

    private static void AssertStopLineIsPerpendicularToLeg(
        int legDirectionBit,
        Vector3 intersectionCenter,
        IReadOnlyList<Vector3> endpoints)
    {
        if (endpoints.Count < 2) return;

        Vector2 legDirection2 = DirectionVector(legDirectionBit);
        Vector3 legDirection = new Vector3(
            legDirection2.x,
            0f,
            legDirection2.y);
        float projection = Vector3.Dot(
            endpoints[0] - intersectionCenter,
            legDirection);
        for (int i = 1; i < endpoints.Count; i++)
        {
            Assert.AreEqual(
                projection,
                Vector3.Dot(endpoints[i] - intersectionCenter, legDirection),
                0.001f);
        }
    }

    private static void AssertStopLineIsOutsideIntersectionCell(
        int legDirectionBit,
        Vector3 intersectionCenter,
        RoadProfile profile,
        IReadOnlyList<int> intersectionLegs,
        IReadOnlyList<Vector3> endpoints)
    {
        Vector2 legDirection2 = DirectionVector(legDirectionBit);
        Vector3 legDirection = new Vector3(
            legDirection2.x,
            0f,
            legDirection2.y);
        float expectedMinimumProjection =
            GetExpectedProjectedFootprintPullback(
                legDirectionBit,
                profile,
                intersectionLegs);

        for (int i = 0; i < endpoints.Count; i++)
        {
            Assert.GreaterOrEqual(
                Vector3.Dot(endpoints[i] - intersectionCenter, legDirection),
                expectedMinimumProjection);
        }
    }

    private static float GetExpectedProjectedFootprintPullback(
        int legDirectionBit,
        RoadProfile profile,
        IReadOnlyList<int> intersectionLegs)
    {
        Vector2 approach = DirectionVector(legDirectionBit);
        Vector2 approachNormal = Perpendicular(approach);
        float roadHalfWidth = profile.RoadWidthUnits * 0.5f;
        float capHalfWidth =
            (profile.RoadWidthUnits + profile.CurbWidthUnits) * 0.5f;
        float required = capHalfWidth;

        for (int i = 0; i < intersectionLegs.Count; i++)
        {
            int otherLeg = intersectionLegs[i];
            if (otherLeg == legDirectionBit ||
                otherLeg == OppositeDirectionBit(legDirectionBit))
            {
                continue;
            }

            Vector2 other = DirectionVector(otherLeg);
            Vector2 otherNormal = Perpendicular(other);
            float approachAcrossOther =
                Mathf.Abs(Vector2.Dot(approach, otherNormal));
            if (approachAcrossOther <= 0.0001f) continue;

            float laneSpreadAcrossOther =
                roadHalfWidth *
                Mathf.Abs(Vector2.Dot(approachNormal, otherNormal));
            required = Mathf.Max(
                required,
                (capHalfWidth + laneSpreadAcrossOther) /
                approachAcrossOther);
        }

        return Mathf.Min(0.95f, required + 0.05f);
    }

    private static Vector2 Perpendicular(Vector2 direction)
    {
        return new Vector2(-direction.y, direction.x);
    }

    private static int OppositeDirectionBit(int bit)
    {
        return bit <= 8 ? bit << 4 : bit >> 4;
    }

    private static Vector2 DirectionVector(int directionBit)
    {
        switch (directionBit)
        {
            case 1: return Vector2.up;
            case 2: return new Vector2(1f, 1f).normalized;
            case 4: return Vector2.right;
            case 8: return new Vector2(1f, -1f).normalized;
            case 16: return Vector2.down;
            case 32: return new Vector2(-1f, -1f).normalized;
            case 64: return Vector2.left;
            case 128: return new Vector2(-1f, 1f).normalized;
            default: return Vector2.zero;
        }
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
