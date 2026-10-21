using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class BuildingRangeTests
{
    private GameObject root;
    private WorldManager world;
    private BuildingSystemBackend backend;
    private BuildingData data;
    private readonly Vector2Int origin = new Vector2Int(6, 6);
    private readonly List<Object> assets = new List<Object>();

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Building range tests");
        world = root.AddComponent<WorldManager>();
        world.worldSize = 24;
        world.extrusion = 0;
        world.makeCircular = false;
        world.layers = new List<TerrainLayer>
        {
            new TerrainLayer { surfaceType = TerrainSurfaceType.FlatGround },
            new TerrainLayer { surfaceType = TerrainSurfaceType.WaterLevel }
        };
        backend = root.AddComponent<BuildingSystemBackend>();
        typeof(BuildingSystemBackend).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(backend, null);
        data = ScriptableObject.CreateInstance<BuildingData>();
        assets.Add(data);
        data.footprint.Add(new BuildingTile());
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        foreach (Object asset in assets) Object.DestroyImmediate(asset);
        assets.Clear();
    }

    [Test]
    public void DistanceFollowsEveryFootprintCellIncludesDiagonalsAndDoesNotUseBoundingBox()
    {
        var footprint = new[] { Vector2Int.zero, Vector2Int.right * 3, Vector2Int.up * 3 };
        Assert.IsTrue(BuildingRangeGeometry.Contains(footprint, new Vector2Int(4, 1), 1));
        Assert.IsTrue(BuildingRangeGeometry.Contains(footprint, new Vector2Int(-1, -1), 1));
        Assert.IsFalse(BuildingRangeGeometry.Contains(footprint, new Vector2Int(3, 3), 1));
        Assert.IsFalse(BuildingRangeGeometry.Contains(footprint, Vector2Int.one, 0));
        Assert.IsTrue(BuildingRangeGeometry.Contains(footprint, Vector2Int.zero, 0));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void WaterWithinOneAndConcreteWithinTwoAreSeparateRequirementsInEveryRotation(int rotation)
    {
        MakeShoreline();
        AddSource(ResourceType.Concrete, origin + Vector2Int.left * 2);
        AddRange(1, new WorldTileType { kind = WorldTileKind.WaterLevel });
        AddRange(2, new WorldTileType { kind = WorldTileKind.ResourceDeposit, resource = ResourceType.Concrete });
        var footprint = data.GetRotatedFootprint(rotation);
        BuildingRangeEvaluation work = backend.RequestPlacementRanges(world, data, origin, footprint);
        bool[] cells = new bool[1];
        Assert.IsFalse(backend.CanPlaceBuildingWithRanges(world, data, origin, footprint, cells, work, out _), "Required searches must finish before placement.");
        Assert.IsTrue(cells[0], "Range failure does not turn a clear footprint cell into an obstruction.");
        Finish(work);
        Assert.IsTrue(work.AllRequirementsMet);
        Assert.AreEqual(9, work.Results[0].Cells.Count);
        Assert.AreEqual(25, work.Results[1].Cells.Count);
        Assert.IsTrue(backend.CanPlaceBuildingWithRanges(world, data, origin, footprint, cells, work, out _));
    }

    [Test]
    public void OutsideRangeFailsAndRadiusEditInvalidatesTheOldResult()
    {
        AddSource(ResourceType.Concrete, origin + Vector2Int.right * 3);
        AddRange(2, new WorldTileType { kind = WorldTileKind.ResourceDeposit, resource = ResourceType.Concrete });
        BuildingRangeEvaluation first = Request();
        Finish(first);
        Assert.IsFalse(first.AllRequirementsMet);
        data.ranges[0].radius = 3;
        Assert.IsFalse(first.IsCurrent());
        BuildingRangeEvaluation second = Request();
        Finish(second);
        Assert.IsTrue(second.AllRequirementsMet);
    }

    [Test]
    public void RequiredTilesAreAlternativesAndUseLogicalResourceIdentityAndShapeAtAnyDepth()
    {
        ResourceType custom = (ResourceType)4096;
        AddSource(custom, origin + Vector2Int.one, MapResourceDepth.Deep);
        AddRange(1, new WorldTileType { kind = WorldTileKind.WaterLevel },
            new WorldTileType { kind = WorldTileKind.ResourceDeposit, resource = custom });
        BuildingRangeEvaluation work = Request();
        Finish(work);
        Assert.IsTrue(work.AllRequirementsMet);
        data.ranges[0].requiredTileTypes[1] = new WorldTileType { kind = WorldTileKind.ResourceNode, resource = custom };
        work = Request();
        Finish(work);
        Assert.IsFalse(work.AllRequirementsMet);
    }

    [Test]
    public void RangeRequirementDoesNotGrantPermissionToCoverAnObstructingResource()
    {
        AddSource(ResourceType.Concrete, origin);
        AddRange(1, new WorldTileType { kind = WorldTileKind.ResourceDeposit, resource = ResourceType.Concrete });
        BuildingRangeEvaluation work = Request();
        Finish(work);
        Assert.IsTrue(work.AllRequirementsMet);
        Assert.IsFalse(backend.CanPlaceBuildingWithRanges(world, data, origin, data.footprint, new bool[1], work, out _));
        data.footprint[0].allowedTileTypes.Add(new WorldTileType { kind = WorldTileKind.ResourceDeposit, resource = ResourceType.Concrete });
        Assert.IsTrue(backend.CanPlaceBuildingWithRanges(world, data, origin, data.footprint, new bool[1], work, out _));
    }

    [Test]
    public void CoverageOnlyAndUnconfiguredBuildingsDoNotWaitForAQuery()
    {
        Assert.IsTrue(backend.CanPlaceBuildingWithRanges(world, data, origin, data.footprint, new bool[1], null, out _));
        AddRange(4);
        Assert.IsTrue(backend.CanPlaceBuildingWithRanges(world, data, origin, data.footprint, new bool[1], null, out _));
    }

    [TestCase(-1)]
    [TestCase(65)]
    public void InvalidRadiiCannotValidatePlacement(int radius)
    {
        AddRange(radius);
        BuildingRangeEvaluation work = Request();
        Finish(work);
        Assert.IsFalse(work.AllRequirementsMet);
        Assert.IsFalse(backend.CanPlaceBuildingWithRanges(world, data, origin, data.footprint, new bool[1], work, out _));
    }

    [Test]
    public void RangeIsClippedToPlayableMapAndSupportsFootprintsCrossingTheBoundary()
    {
        AddRange(2);
        var footprint = new List<BuildingTile> { new BuildingTile { localPosition = Vector2Int.left } };
        BuildingRangeEvaluation work = backend.RequestPlacementRanges(world, data, Vector2Int.zero, footprint);
        Finish(work);
        Assert.AreEqual(2, work.Results[0].Cells.Count);
        foreach (Vector2Int cell in work.Results[0].Cells) Assert.IsFalse(world.IsExtrusionCell(cell.x, cell.y));
    }

    [Test]
    public void SearchesAdvanceInBoundedStepsAndCancellationNeverPublishesValidity()
    {
        AddRange(10);
        BuildingRangeEvaluation work = Request();
        Assert.IsFalse(work.Advance(0));
        Assert.IsEmpty(work.Results[0].Cells);
        Assert.IsFalse(work.Advance(1));
        Assert.LessOrEqual(work.Results[0].Cells.Count, 1);
        work.Cancel();
        Assert.IsTrue(work.Advance(100));
        Assert.IsTrue(work.IsCancelled);
        Assert.IsFalse(work.AllRequirementsMet);
    }

    [Test]
    public void MovingOrRotatingThePreviewAndRegeneratingTheWorldRejectOldResults()
    {
        AddRange(1, new WorldTileType { kind = WorldTileKind.FlatGround });
        data.footprint.Add(new BuildingTile { localPosition = Vector2Int.right });
        BuildingRangeEvaluation work = Request();
        Finish(work);
        Assert.IsTrue(work.MatchesPlacement(world, data, origin, data.footprint));
        Assert.IsFalse(work.MatchesPlacement(world, data, origin + Vector2Int.up, data.footprint));
        Assert.IsFalse(work.MatchesPlacement(world, data, origin, data.GetRotatedFootprint(1)));
        world.ClearWorld();
        Assert.IsFalse(work.IsCurrent());
    }

    [Test]
    public void BuildingQueryUsesAnyCoveredCellDeduplicatesTargetsAndExcludesItsOwner()
    {
        AddRange(1);
        Building source = Place(origin, Vector2Int.zero);
        Building touching = Place(origin + Vector2Int.right, Vector2Int.zero, Vector2Int.up, Vector2Int.right);
        Building far = Place(origin + Vector2Int.up * 3, Vector2Int.zero);
        Building disabled = Place(origin + Vector2Int.left, Vector2Int.zero);
        disabled.enabled = false;
        BuildingRangeEvaluation work = backend.RequestBuildingRanges(world, source);
        Finish(work);
        CollectionAssert.AreEqual(new[] { touching }, work.Results[0].Buildings);
        CollectionAssert.DoesNotContain(work.Results[0].Buildings, source);
        CollectionAssert.DoesNotContain(work.Results[0].Buildings, far);
        backend.NotifyBuildingAuthoringChanged(touching);
        Assert.IsFalse(work.IsCurrent());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ZeroAlphaRangeColorStaysVisibleWhetherOrNotItsRequirementMatches(bool matched)
    {
        AddRange(1, new WorldTileType { kind = WorldTileKind.ResourceDeposit, resource = ResourceType.Concrete });
        data.ranges[0].color = default;
        if (matched) AddSource(ResourceType.Concrete, origin + Vector2Int.up);
        BuildingRangeEvaluation work = Request();
        Finish(work);
        Assert.AreEqual(matched, work.AllRequirementsMet);
        Assert.AreEqual(BuildingRange.DefaultColor, work.Results[0].Color);
        Assert.Greater(work.Results[0].Color.a, 0f);
        Assert.AreEqual(default(Color), data.ranges[0].color, "Runtime fallback must not overwrite authored assets.");
        Assert.IsTrue(work.IsCurrent(), "Compare snapshot freshness against the original authored color.");
    }

    [Test]
    public void TransparentTintKeepsItsHueAndVisibleAuthoredColorsArePreserved()
    {
        AddRange(1);
        data.ranges[0].color = new Color(0.3f, 0.7f, 0.5f, 0f);
        BuildingRangeEvaluation work = Request();
        Finish(work);
        Assert.AreEqual(new Color(0.3f, 0.7f, 0.5f, 1f), work.Results[0].Color);
        data.ranges[0].color = new Color(0.3f, 0.7f, 0.5f, 0.4f);
        work = Request();
        Finish(work);
        Assert.AreEqual(data.ranges[0].color, work.Results[0].Color);
        data.ranges[0].color = new Color(float.NaN, 0f, 0f, 1f);
        work = Request();
        Finish(work);
        Assert.AreEqual(BuildingRange.DefaultColor, work.Results[0].Color);
    }

    private BuildingRangeEvaluation Request() => backend.RequestPlacementRanges(world, data, origin, data.footprint);

    private static void Finish(BuildingRangeEvaluation work)
    {
        for (int i = 0; i < 10000 && !work.IsComplete; i++) work.Advance(8);
        Assert.IsTrue(work.IsComplete);
        Assert.IsFalse(work.IsCancelled);
    }

    private void AddRange(int radius, params WorldTileType[] required)
    {
        data.ranges.Add(new BuildingRange { radius = radius, requiredTileTypes = new List<WorldTileType>(required) });
    }

    private void MakeShoreline()
    {
        world.visualHeights = new float[25, 25];
        for (int x = origin.x + 1; x < 25; x++)
            for (int z = 0; z < 25; z++) world.visualHeights[x, z] = 1f;
    }

    private void AddSource(ResourceType type, Vector2Int cell, MapResourceDepth depth = MapResourceDepth.Overground)
    {
        var definition = ScriptableObject.CreateInstance<MapResourceDefinition>();
        assets.Add(definition);
        definition.resourceType = type; definition.shape = MapResourceShape.Deposit; definition.depth = depth;
        typeof(MapResourceMap).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(world.ResourceMap, new object[] { definition, new[] { cell } });
    }

    private Building Place(Vector2Int at, params Vector2Int[] localCells)
    {
        var instance = new GameObject("Range target");
        instance.transform.SetParent(root.transform);
        Building building = instance.AddComponent<Building>();
        typeof(Building).GetProperty("data").SetValue(building, data);
        typeof(Building).GetProperty("originCell").SetValue(building, at);
        var footprint = new List<BuildingTile>();
        foreach (Vector2Int local in localCells)
        {
            footprint.Add(new BuildingTile { localPosition = local });
            building.occupiedCells.Add(at + local);
        }
        backend.RegisterBuilding(building, footprint);
        return building;
    }
}
