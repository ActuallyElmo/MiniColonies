using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class BuildingPlacementPreviewTests
{
    private GameObject managers;
    private WorldManager world;
    private BuildingSystemBackend buildings;
    private RoadSystemBackend roads;
    private BuildingData data;
    private Texture2D texture;
    private Sprite outline;
    private Sprite hatch;
    private readonly List<Mesh> meshes = new List<Mesh>();
    private readonly List<Object> groundAssets = new List<Object>();
    private readonly Vector2Int origin = new Vector2Int(4, 4);

    [SetUp]
    public void SetUp()
    {
        managers = new GameObject("Building placement preview tests");
        world = managers.AddComponent<WorldManager>();
        world.worldSize = 16;
        world.extrusion = 0;
        world.makeCircular = false;
        world.layers = new List<TerrainLayer>
        {
            new TerrainLayer { isBuildable = true },
            new TerrainLayer { isBuildable = true }
        };
        buildings = managers.AddComponent<BuildingSystemBackend>();
        roads = managers.AddComponent<RoadSystemBackend>();
        Invoke(buildings, "Awake");
        Invoke(roads, "Awake");
        data = ScriptableObject.CreateInstance<BuildingData>();
        data.footprint = new List<BuildingTile>
        {
            new BuildingTile { localPosition = Vector2Int.zero },
            new BuildingTile { localPosition = Vector2Int.right },
            new BuildingTile { localPosition = Vector2Int.right * 2 }
        };
        texture = new Texture2D(32, 32);
        outline = Sprite.Create(texture, new Rect(0, 16, 16, 16), Vector2.one * 0.5f);
        hatch = Sprite.Create(texture, new Rect(16, 0, 16, 16), Vector2.one * 0.5f);
    }

    [TearDown]
    public void TearDown()
    {
        // Disabling the input also releases its runtime material and cell meshes.
        Object.DestroyImmediate(managers);
        foreach (Mesh mesh in meshes) Object.DestroyImmediate(mesh);
        meshes.Clear();
        foreach (Object asset in groundAssets) Object.DestroyImmediate(asset);
        groundAssets.Clear();
        Object.DestroyImmediate(data);
        Object.DestroyImmediate(outline);
        Object.DestroyImmediate(hatch);
        Object.DestroyImmediate(texture);
    }

    [Test]
    public void ClearFootprintIsValidAndFillsEveryCellResult()
    {
        bool[] valid = new bool[3];
        Assert.IsTrue(Evaluate(origin, data.footprint, valid));
        CollectionAssert.AreEqual(new[] { true, true, true }, valid);
    }

    [Test]
    public void BuildingLookupUsesOccupiedCellsAndRejectsDisabledOrDestroyedObjects()
    {
        var instance = new GameObject("Hovered building");
        try
        {
            var building = instance.AddComponent<Building>();
            Occupancy().Add(origin, building);
            Assert.IsTrue(buildings.TryGetBuildingAt(origin, out Building found));
            Assert.AreSame(building, found);
            Assert.IsFalse(buildings.TryGetBuildingAt(origin + Vector2Int.up, out found));
            Assert.IsNull(found);
            building.enabled = false;
            Assert.IsFalse(buildings.TryGetBuildingAt(origin, out found));
            Assert.IsNull(found);
            Object.DestroyImmediate(instance);
            Assert.IsFalse(buildings.TryGetBuildingAt(origin, out found));
            Assert.IsNull(found);
        }
        finally
        {
            if (instance != null) Object.DestroyImmediate(instance);
        }
    }

    [Test]
    public void ObstructionsOnlyInvalidateTheirOwnCellsAndAllFailuresAreReported()
    {
        Occupancy().Add(origin, null);
        RoadCells().Add(origin + Vector2Int.right * 2, new RoadCell(origin + Vector2Int.right * 2, 0, null));
        bool[] valid = new bool[3];
        Assert.IsFalse(Evaluate(origin, data.footprint, valid));
        CollectionAssert.AreEqual(new[] { false, true, false }, valid);

        Occupancy().Clear();
        RoadCells().Clear();
        Assert.IsTrue(Evaluate(origin, data.footprint, valid));
        CollectionAssert.AreEqual(new[] { true, true, true }, valid);
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    [TestCase(3, true)]
    public void RotationChecksTheCellsUnderTheRotatedPreview(int rotation, bool expected)
    {
        Occupancy().Add(origin + Vector2Int.right, null);
        bool[] valid = new bool[3];
        Assert.AreEqual(expected, Evaluate(origin, data.GetRotatedFootprint(rotation), valid));
        CollectionAssert.AreEqual(new[] { true, expected, true }, valid);
    }

    [Test]
    public void CrossingWorldBorderOnlyInvalidatesOutsideCells()
    {
        bool[] valid = new bool[3];
        Assert.IsFalse(Evaluate(new Vector2Int(14, 4), data.footprint, valid));
        CollectionAssert.AreEqual(new[] { true, false, false }, valid);
    }

    [Test]
    public void UnbuildableTerrainRejectsPlacement()
    {
        world.layers[0].isBuildable = false;
        bool[] valid = { true, true, true };
        Assert.IsFalse(Evaluate(origin, data.footprint, valid));
        CollectionAssert.AreEqual(new[] { false, false, false }, valid);
    }

    [TestCase(1f)]
    [TestCase(0.5f)]
    public void DifferentElevationOrSlopeOnlyInvalidatesAffectedPart(float height)
    {
        world.visualHeights = new float[17, 17];
        // Keep the first two cells flat, with a plateau/slope starting at the last.
        for (int x = 6; x <= 16; x++)
            for (int z = 0; z <= 16; z++) world.visualHeights[x, z] = height;
        bool[] valid = new bool[3];
        Assert.IsFalse(Evaluate(origin, data.footprint, valid));
        Assert.IsTrue(valid[0]);
        Assert.IsFalse(valid[2]);
    }

    [TestCase(0.75f, 0, 0)]
    [TestCase(0.75f, 0, 1)]
    [TestCase(0.75f, 0, 2)]
    [TestCase(0.75f, 0, 3)]
    [TestCase(0.25f, 1, 0)]
    [TestCase(0.25f, 1, 1)]
    [TestCase(0.25f, 1, 2)]
    [TestCase(0.25f, 1, 3)]
    public void TransitionOriginUsesFlatFootprintLevelInEveryRotation(
        float transitionHeight, int flatLayer, int rotation)
    {
        SetTransitionBand(transitionHeight, flatLayer, rotation);
        Assert.IsTrue(world.IsTransition(origin.x, origin.y));
        Assert.AreNotEqual(flatLayer, world.GetCellLayer(origin.x, origin.y),
            "The rounded origin layer reproduces the previous all-red failure.");

        bool[] valid = new bool[3];
        Assert.IsFalse(buildings.CanPlaceBuilding(
            world, origin, data.GetRotatedFootprint(rotation), valid, out int requiredLayer));
        Assert.AreEqual(flatLayer, requiredLayer);
        CollectionAssert.AreEqual(new[] { false, true, true }, valid);
    }

    [Test]
    public void TransitionChoosesMostRepresentedFlatLayerEvenWhenCursorRoundsToOtherLayer()
    {
        SetTerraceEdge(0.75f);
        data.footprint = Footprint(-3, -2, -1, 0, 1, 2);
        Assert.IsTrue(world.IsTransition(origin.x, origin.y));
        Assert.AreEqual(1, world.GetCellLayer(origin.x, origin.y));
        bool[] valid = new bool[6];
        Assert.IsFalse(buildings.CanPlaceBuilding(world, origin, data.footprint, valid, out int layer));
        Assert.AreEqual(0, layer);
        CollectionAssert.AreEqual(new[] { true, true, true, false, false, false }, valid);

        // Obstructions affect validity, but cannot change the desired height.
        Occupancy().Add(origin + Vector2Int.left, null);
        Occupancy().Add(origin + Vector2Int.left * 2, null);
        Vector2Int roadPosition = origin + Vector2Int.left * 3;
        RoadCells().Add(roadPosition, new RoadCell(roadPosition, 0, null));
        Assert.IsFalse(buildings.CanPlaceBuilding(world, origin, data.footprint, valid, out layer));
        Assert.AreEqual(0, layer);
        CollectionAssert.AreEqual(new[] { false, false, false, false, false, false }, valid);
    }

    [TestCase(0.25f, 0)]
    [TestCase(0.5f, 0)]
    [TestCase(0.75f, 1)]
    public void EqualFlatCoverageUsesClosestHeightThenLowerLayerIndependentOfFootprintOrder(
        float transitionHeight, int expectedLayer)
    {
        SetTerraceEdge(transitionHeight);
        data.footprint = Footprint(-1, 0, 1);
        bool[] valid = new bool[3];
        Assert.IsFalse(buildings.CanPlaceBuilding(world, origin, data.footprint, valid, out int layer));
        Assert.AreEqual(expectedLayer, layer);
        CollectionAssert.AreEqual(new[] { expectedLayer == 0, false, expectedLayer == 1 }, valid);

        data.footprint.Reverse();
        Assert.IsFalse(buildings.CanPlaceBuilding(world, origin, data.footprint, valid, out layer));
        Assert.AreEqual(expectedLayer, layer);
        CollectionAssert.AreEqual(new[] { expectedLayer == 1, false, expectedLayer == 0 }, valid);
    }

    [TestCase(0)]
    [TestCase(1)]
    public void FlatCursorCellRetainsItsLayerEvenWhenOtherLayerCoversMoreCells(int layer)
    {
        SetTerraceEdge(layer);
        data.footprint = layer == 0 ? Footprint(0, 1, 2) : Footprint(0, -1, -2);
        Assert.IsFalse(world.IsTransition(origin.x, origin.y));
        bool[] valid = new bool[3];
        Assert.IsFalse(buildings.CanPlaceBuilding(world, origin, data.footprint, valid, out int selectedLayer));
        Assert.AreEqual(layer, selectedLayer);
        CollectionAssert.AreEqual(new[] { true, false, false }, valid);
    }

    [Test]
    public void AmbiguousOriginIgnoresUnbuildableFlatLayers()
    {
        SetTerraceEdge(0.25f);
        world.layers[0].isBuildable = false;
        data.footprint = Footprint(-3, -2, -1, 0, 1);
        bool[] valid = new bool[5];
        Assert.IsFalse(buildings.CanPlaceBuilding(world, origin, data.footprint, valid, out int layer));
        Assert.AreEqual(1, layer);
        CollectionAssert.AreEqual(new[] { false, false, false, false, true }, valid);
    }

    [Test]
    public void FootprintEntirelyOnTransitionRemainsInvalid()
    {
        SetTransitionBand(0.5f, 0.5f, 0);
        bool[] valid = { true, true, true };
        Assert.IsFalse(Evaluate(origin, data.footprint, valid));
        CollectionAssert.AreEqual(new[] { false, false, false }, valid);
    }

    [Test]
    public void MissingWorldOrEmptyFootprintCannotBePlacedAndClearsStaleResults()
    {
        bool[] valid = { true, true, true };
        Assert.IsFalse(buildings.CanPlaceBuilding(null, origin, data.footprint, valid, out _));
        CollectionAssert.AreEqual(new[] { false, false, false }, valid);
        Assert.IsFalse(Evaluate(origin, new List<BuildingTile>(), valid));
    }

    [Test]
    public void IrregularOutlineIncludesConcaveEdgesAndOmitsInternalSeams()
    {
        var footprint = new HashSet<Vector2Int>
        {
            Vector2Int.zero, Vector2Int.right, Vector2Int.up
        };
        Assert.AreEqual(BuildingPreviewEdges.Left | BuildingPreviewEdges.Bottom,
            BuildingPlacementPreviewGeometry.GetExposedEdges(Vector2Int.zero, footprint));
        Assert.AreEqual(BuildingPreviewEdges.Right | BuildingPreviewEdges.Bottom | BuildingPreviewEdges.Top,
            BuildingPlacementPreviewGeometry.GetExposedEdges(Vector2Int.right, footprint));
        Assert.AreEqual(BuildingPreviewEdges.Left | BuildingPreviewEdges.Right | BuildingPreviewEdges.Top,
            BuildingPlacementPreviewGeometry.GetExposedEdges(Vector2Int.up, footprint));
    }

    [Test]
    public void SingleCellAndInteriorCellHaveTheCorrectOutline()
    {
        var single = new HashSet<Vector2Int> { Vector2Int.zero };
        Mesh isolated = CreateMesh(Vector2Int.zero, single);
        Assert.AreEqual(20, isolated.vertexCount, "Hatch and four exposed edge strips.");
        Assert.AreEqual(30, isolated.triangles.Length);
        Assert.AreEqual(new Vector3(2f, 0f, 2f), isolated.bounds.size);

        var interior = new HashSet<Vector2Int>
        {
            Vector2Int.zero, Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up
        };
        Mesh center = CreateMesh(Vector2Int.zero, interior);
        Assert.AreEqual(4, center.vertexCount, "No lines around interior cells.");
        Assert.AreEqual(6, center.triangles.Length);
    }

    [Test]
    public void HatchCoordinatesContinueAcrossAdjacentCellsWithoutStretching()
    {
        var footprint = new HashSet<Vector2Int> { Vector2Int.zero, Vector2Int.right };
        var firstUvs = new List<Vector3>();
        var secondUvs = new List<Vector3>();
        CreateMesh(Vector2Int.zero, footprint).GetUVs(0, firstUvs);
        CreateMesh(Vector2Int.right, footprint).GetUVs(0, secondUvs);
        Assert.AreEqual(firstUvs[2], secondUvs[1]);
        Assert.AreEqual(firstUvs[3], secondUvs[0]);
        Assert.AreEqual(1f, firstUvs[0].z, "Hatch samples must use repeating atlas coordinates.");
        Assert.AreEqual(0f, firstUvs[4].z, "Outline samples must use direct atlas coordinates.");
    }

    [Test]
    public void PreviewTintsOnlyBlockedCellsAndRestoresColorsWhenTheyClear()
    {
        var input = managers.AddComponent<BuildingPlacementInput>();
        input.worldManager = world;
        input.placementOutlineSprite = outline;
        input.placementHatchSprite = hatch;
        Invoke(input, "CreatePreview", data);
        MeshRenderer[] renderers = input.GetComponentsInChildren<MeshRenderer>();
        Assert.AreEqual(3, renderers.Length);
        Assert.AreSame(renderers[0].sharedMaterial, renderers[1].sharedMaterial);
        Assert.IsEmpty(input.GetComponentsInChildren<Collider>());

        Occupancy().Add(origin + Vector2Int.right, null);
        Assert.IsFalse((bool)Invoke(input, "UpdatePreviewValidity", origin, 0));
        AssertTint(renderers[0], input.validCellColor, input.outlineColor);
        AssertTint(renderers[1], input.invalidCellColor, Color.red);
        AssertTint(renderers[2], input.validCellColor, input.outlineColor);

        Occupancy().Clear();
        Assert.IsTrue((bool)Invoke(input, "UpdatePreviewValidity", origin, 0));
        AssertTint(renderers[1], input.validCellColor, input.outlineColor);
        Mesh mesh = renderers[0].GetComponent<MeshFilter>().sharedMesh;
        Material material = renderers[0].sharedMaterial;
        input.CancelPlacementPreview();
        Assert.IsTrue(mesh == null);
        Assert.IsTrue(material == null);
        Assert.IsEmpty(input.GetComponentsInChildren<MeshRenderer>());
    }

    [TestCase(0.75f, 0)]
    [TestCase(0.25f, 1)]
    public void PreviewOnTransitionOnlyTintsEdgeAndUsesSelectedElevation(float transitionHeight, int layer)
    {
        SetTransitionBand(transitionHeight, layer, 0);
        world.heightStep = 2.5f;
        var input = managers.AddComponent<BuildingPlacementInput>();
        input.worldManager = world;
        input.placementOutlineSprite = outline;
        input.placementHatchSprite = hatch;
        Invoke(input, "CreatePreview", data);
        Assert.IsFalse((bool)Invoke(input, "UpdatePreviewValidity", origin, 0));
        MeshRenderer[] renderers = input.GetComponentsInChildren<MeshRenderer>();
        Assert.AreEqual(layer * world.heightStep, renderers[0].transform.parent.position.y);
        AssertTint(renderers[0], input.invalidCellColor, Color.red);
        AssertTint(renderers[1], input.validCellColor, input.outlineColor);
        AssertTint(renderers[2], input.validCellColor, input.outlineColor);

        // Moving to flat terrain restores the transition tile and keeps height/tint in sync.
        world.visualHeights = null;
        Assert.IsTrue((bool)Invoke(input, "UpdatePreviewValidity", origin, 0));
        Assert.AreEqual(0f, renderers[0].transform.parent.position.y);
        AssertTint(renderers[0], input.validCellColor, input.outlineColor);
    }

    [TestCase(false, MapResourceShape.Nodes)]
    [TestCase(true, MapResourceShape.Nodes)]
    [TestCase(false, MapResourceShape.Deposit)]
    [TestCase(true, MapResourceShape.Deposit)]
    public void ResourcePermissionAllowsOnlyMatchingResourceAndRequirementRejectsEmptyGround(bool required, MapResourceShape shape)
    {
        BuildingGroundType copper = Ground(BuildingGroundKind.MapResource);
        copper.resourceType = ResourceType.CopperOre;
        data.footprint[1].allowedGrounds.Add(copper);
        data.footprint[1].requireGroundMatch = required;
        bool[] valid = new bool[3];
        Assert.AreEqual(!required, Evaluate(origin, data.footprint, valid));

        AddSource(ResourceType.CopperOre, origin + Vector2Int.right, MapResourceDepth.Overground, shape);
        Assert.IsTrue(Evaluate(origin, data.footprint, valid));
        // A resource permission on one cell cannot permit a different cell.
        AddSource(ResourceType.CopperOre, origin);
        Assert.IsFalse(Evaluate(origin, data.footprint, valid));
        CollectionAssert.AreEqual(new[] { false, true, true }, valid);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void RequiredResourceFollowsCellThroughRotationAndRetainsPortData(int rotation)
    {
        BuildingGroundType copper = Ground(BuildingGroundKind.MapResource);
        copper.resourceType = ResourceType.CopperOre;
        data.footprint[1].allowedGrounds.Add(copper);
        data.footprint[1].requireGroundMatch = true;
        data.footprint[1].isPort = true;
        data.footprint[1].portType = PortType.ResourceBoth;
        var footprint = data.GetRotatedFootprint(rotation);
        Assert.IsFalse(Evaluate(origin, footprint, new bool[3]));
        AddSource(ResourceType.CopperOre, origin + footprint[1].localPosition);
        Assert.IsTrue(Evaluate(origin, footprint, new bool[3]));
        Assert.IsTrue(footprint[1].requireGroundMatch);
        Assert.AreEqual(PortType.ResourceBoth, footprint[1].portType);
        Assert.IsTrue(footprint[1].isPort);
        Assert.AreSame(copper, footprint[1].allowedGrounds[0]);
        footprint[1].allowedGrounds.Clear();
        Assert.AreEqual(1, data.footprint[1].allowedGrounds.Count);
    }

    [Test]
    public void RequiredGroundAcceptsAnyListedTypeButRejectsUnlistedResourceAndMissingRules()
    {
        BuildingGroundType copper = Ground(BuildingGroundKind.MapResource);
        copper.resourceType = ResourceType.CopperOre;
        BuildingGroundType oil = Ground(BuildingGroundKind.MapResource);
        oil.resourceType = ResourceType.Oil;
        BuildingTile tile = data.footprint[0];
        tile.requireGroundMatch = true;
        tile.allowedGrounds.Add(null);
        Assert.IsFalse(Evaluate(origin, data.footprint, new bool[3]));
        tile.allowedGrounds.Add(copper);
        tile.allowedGrounds.Add(oil);
        AddSource(ResourceType.Oil, origin);
        Assert.IsTrue(Evaluate(origin, data.footprint, new bool[3]));
        oil.resourceType = ResourceType.Water;
        Assert.IsFalse(Evaluate(origin, data.footprint, new bool[3]));
    }

    [Test]
    public void RequiredUndergroundResourceUsesLogicalSourceEvenWithoutConstructionBlocker()
    {
        BuildingGroundType oil = Ground(BuildingGroundKind.MapResource);
        oil.resourceType = ResourceType.Oil;
        data.footprint[0].allowedGrounds.Add(oil);
        data.footprint[0].requireGroundMatch = true;
        Assert.IsFalse(Evaluate(origin, data.footprint, new bool[3]));
        AddSource(ResourceType.Oil, origin, MapResourceDepth.Deep);
        Assert.IsTrue(Evaluate(origin, data.footprint, new bool[3]));
    }

    [Test]
    public void MatchingGroundCannotOverrideRoadBuildingOrWorldBoundary()
    {
        BuildingGroundType layer = Ground(BuildingGroundKind.TerrainLayer);
        foreach (BuildingTile tile in data.footprint) tile.allowedGrounds.Add(layer);
        world.layers[0].isBuildable = false;
        Assert.IsTrue(Evaluate(origin, data.footprint, new bool[3]));
        Occupancy().Add(origin, null);
        RoadCells().Add(origin + Vector2Int.right, new RoadCell(origin + Vector2Int.right, 0, null));
        bool[] valid = new bool[3];
        Assert.IsFalse(Evaluate(origin, data.footprint, valid));
        CollectionAssert.AreEqual(new[] { false, false, true }, valid);
        Assert.IsFalse(Evaluate(new Vector2Int(14, 4), data.footprint, valid));
        CollectionAssert.AreEqual(new[] { true, false, false }, valid);
    }

    [Test]
    public void WaterAndCliffPermissionsBridgeElevationsButDoNotAllowBlockingResources()
    {
        SetTerraceEdge(0.5f);
        world.layers[0].isBuildable = false;
        data.footprint = Footprint(-1, 0, 1);
        BuildingGroundType water = Ground(BuildingGroundKind.TerrainLayer);
        BuildingGroundType cliff = Ground(BuildingGroundKind.Cliff);
        data.footprint[0].allowedGrounds.Add(water);
        data.footprint[0].requireGroundMatch = true;
        data.footprint[1].allowedGrounds.Add(cliff);
        data.footprint[1].requireGroundMatch = true;
        Assert.IsTrue(buildings.CanPlaceBuilding(world, origin, data.footprint, new bool[3], out int layer));
        Assert.AreEqual(1, layer, "Ordinary land anchors a shoreline building above its water cell.");
        AddSource(ResourceType.Oil, origin);
        Assert.IsFalse(Evaluate(origin, data.footprint, new bool[3]));
        BuildingGroundType oil = Ground(BuildingGroundKind.MapResource);
        oil.resourceType = ResourceType.Oil;
        data.footprint[1].allowedGrounds.Add(oil);
        Assert.IsTrue(Evaluate(origin, data.footprint, new bool[3]));
        data.footprint[1].allowedGrounds.Remove(cliff);
        Assert.IsFalse(Evaluate(origin, data.footprint, new bool[3]), "Resource permission cannot bypass cliffs.");
    }

    [Test]
    public void RequiredWaterFailsWhenMovedOntoDryLandAndOptionalWaterStillAllowsDryLand()
    {
        BuildingGroundType water = Ground(BuildingGroundKind.TerrainLayer);
        water.terrainLayer = 1;
        data.footprint[0].allowedGrounds.Add(water);
        data.footprint[0].requireGroundMatch = true;
        Assert.IsFalse(Evaluate(origin, data.footprint, new bool[3]));
        data.footprint[0].requireGroundMatch = false;
        Assert.IsTrue(Evaluate(origin, data.footprint, new bool[3]));
        water.terrainLayer = -1;
        data.footprint[0].requireGroundMatch = true;
        Assert.IsFalse(Evaluate(origin, data.footprint, new bool[3]));
    }

    [Test]
    public void ResourcePermissionDoesNotBypassUnbuildableTerrainAndGenerationRejectsPlacement()
    {
        BuildingGroundType copper = Ground(BuildingGroundKind.MapResource);
        copper.resourceType = ResourceType.CopperOre;
        foreach (BuildingTile tile in data.footprint) tile.allowedGrounds.Add(copper);
        AddSource(ResourceType.CopperOre, origin);
        Assert.IsTrue(Evaluate(origin, data.footprint, new bool[3]));
        world.layers[0].isBuildable = false;
        Assert.IsFalse(Evaluate(origin, data.footprint, new bool[3]));
        world.layers[0].isBuildable = true;
        typeof(WorldManager).GetField("<IsGenerating>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(world, true);
        bool[] valid = { true, true, true };
        Assert.IsFalse(Evaluate(origin, data.footprint, valid));
        CollectionAssert.AreEqual(new[] { false, false, false }, valid);
        typeof(WorldManager).GetField("<IsGenerating>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(world, false);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GroundPreviewUsesFullCellFillSharedIconAndReleasesMeshes(bool required)
    {
        BuildingGroundType ground = Ground(BuildingGroundKind.TerrainLayer);
        ground.icon = outline;
        data.footprint[1].allowedGrounds.Add(ground);
        data.footprint[1].requireGroundMatch = required;
        var input = managers.AddComponent<BuildingPlacementInput>();
        input.worldManager = world;
        input.placementOutlineSprite = outline;
        input.placementHatchSprite = hatch;
        input.optionalGroundFillSprite = outline;
        input.requiredGroundFillSprite = hatch;
        Invoke(input, "CreatePreview", data);
        Transform cell = input.transform.Find("BuildingPreview_" + data.buildingName + "/PreviewCell_" + Vector2Int.right);
        var uvs = new List<Vector3>();
        cell.GetComponent<MeshFilter>().sharedMesh.GetUVs(0, uvs);
        Assert.AreEqual(2f, uvs[0].z, "The center gap must not repeat with the hatch period.");
        Assert.That(uvs[0].y, Is.GreaterThanOrEqualTo(required ? 0f : 0.5f));
        Assert.That(uvs[0].y, Is.LessThan(required ? 0.5f : 1f));
        Transform icon = input.transform.Find("BuildingPreview_" + data.buildingName + "/GroundIcon_" + ground.name);
        Assert.IsNotNull(icon);
        Assert.AreEqual(world.cellSize, icon.localPosition.x);
        Assert.AreEqual(0f, icon.localPosition.z);
        Mesh iconMesh = icon.GetComponent<MeshFilter>().sharedMesh;
        Assert.AreEqual(Vector3.zero, iconMesh.bounds.center, "Sprite pivots cannot move the centered icon.");
        var block = new MaterialPropertyBlock();
        icon.GetComponent<MeshRenderer>().GetPropertyBlock(block);
        Assert.AreSame(texture, block.GetTexture("_MainTex"));
        input.CancelPlacementPreview();
        Assert.IsTrue(iconMesh == null);
        Assert.IsEmpty(input.GetComponentsInChildren<MeshRenderer>());
    }

    [TestCase(WorldTileKind.ResourceNode, MapResourceShape.Nodes)]
    [TestCase(WorldTileKind.ResourceDeposit, MapResourceShape.Deposit)]
    public void TileRulesUseCustomResourceIdentityAndDistinguishNodesFromDeposits(WorldTileKind kind, MapResourceShape shape)
    {
        ResourceType custom = (ResourceType)2048;
        data.footprint[1].requiredTileTypes.Add(new WorldTileType { kind = kind, resource = custom });
        Assert.IsFalse(Evaluate(origin, data.footprint, new bool[3]));
        AddSource(custom, origin + Vector2Int.right, MapResourceDepth.Overground, shape);
        Assert.IsTrue(Evaluate(origin, data.footprint, new bool[3]), "No ground asset or enum member is needed for a new resource.");
        data.footprint[1].requiredTileTypes[0] = new WorldTileType
        {
            kind = kind == WorldTileKind.ResourceNode ? WorldTileKind.ResourceDeposit : WorldTileKind.ResourceNode,
            resource = custom
        };
        Assert.IsFalse(Evaluate(origin, data.footprint, new bool[3]));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TypedResourcePermissionKeepsOptionalAndRequiredBehavior(bool required)
    {
        var rule = new WorldTileType { kind = WorldTileKind.ResourceDeposit, resource = ResourceType.Oil };
        List<WorldTileType> rules = required ? data.footprint[0].requiredTileTypes : data.footprint[0].allowedTileTypes;
        rules.Add(rule);
        Assert.AreEqual(!required, Evaluate(origin, data.footprint, new bool[3]));
        AddSource(ResourceType.Oil, origin, MapResourceDepth.Deep);
        Assert.IsTrue(Evaluate(origin, data.footprint, new bool[3]));
        rules[0] = new WorldTileType { kind = WorldTileKind.ResourceDeposit, resource = ResourceType.CopperOre };
        Assert.AreEqual(!required, Evaluate(origin, data.footprint, new bool[3]), "Unmatched optional permission cannot block otherwise ordinary terrain.");
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void TypedRulesRotateAndKeepIndependentAuthoringLists(int rotation)
    {
        var rule = new WorldTileType { kind = WorldTileKind.ResourceNode, resource = ResourceType.CopperOre };
        data.footprint[1].requiredTileTypes.Add(rule);
        data.footprint[1].allowedTileTypes.Add(new WorldTileType { kind = WorldTileKind.FlatGround });
        var rotated = data.GetRotatedFootprint(rotation);
        Assert.IsFalse(Evaluate(origin, rotated, new bool[3]));
        AddSource(ResourceType.CopperOre, origin + rotated[1].localPosition, MapResourceDepth.Overground, MapResourceShape.Nodes);
        Assert.IsTrue(Evaluate(origin, rotated, new bool[3]));
        Assert.AreEqual(rule, rotated[1].requiredTileTypes[0]);
        rotated[1].allowedTileTypes.Clear();
        Assert.AreEqual(1, data.footprint[1].allowedTileTypes.Count);
        rotated[1].requiredTileTypes.Clear();
        Assert.AreEqual(1, data.footprint[1].requiredTileTypes.Count);
    }

    [Test]
    public void TileInfoDerivesTerrainAndResourceIndependentlyAndUsesExplicitSurfaceType()
    {
        world.layers[0].surfaceType = TerrainSurfaceType.WaterLevel;
        Assert.IsFalse(world.IsLayerBuildable(0), "Named type overrides the retained legacy boolean.");
        WorldTileInfo cell = world.GetTileInfo(origin);
        Assert.IsTrue(cell.Matches(new WorldTileType { kind = WorldTileKind.WaterLevel }));
        Assert.IsFalse(cell.Matches(new WorldTileType { kind = WorldTileKind.FlatGround }));
        AddSource(ResourceType.Water, origin);
        cell = world.GetTileInfo(origin);
        Assert.IsTrue(cell.Matches(new WorldTileType { kind = WorldTileKind.WaterLevel }));
        Assert.IsTrue(cell.Matches(new WorldTileType { kind = WorldTileKind.ResourceDeposit, resource = ResourceType.Water }));
        Assert.IsFalse(world.GetTileInfo(Vector2Int.zero).Playable);
        world.layers[0].isBuildable = false;
        world.layers[0].surfaceType = TerrainSurfaceType.FlatGround;
        Assert.IsTrue(world.IsLayerBuildable(0));
    }

    [Test]
    public void TypedShorelineRulesAllowWaterAndEdgesAndRetainRoadAndOccupancySafety()
    {
        SetTerraceEdge(0.5f);
        world.layers[0].surfaceType = TerrainSurfaceType.WaterLevel;
        world.layers[1].surfaceType = TerrainSurfaceType.FlatGround;
        data.footprint = Footprint(-1, 0, 1);
        data.footprint[0].requiredTileTypes.Add(new WorldTileType { kind = WorldTileKind.WaterLevel });
        data.footprint[1].requiredTileTypes.Add(new WorldTileType { kind = WorldTileKind.Edge });
        Assert.IsTrue(buildings.CanPlaceBuilding(world, origin, data.footprint, new bool[3], out int layer));
        Assert.AreEqual(1, layer);
        Assert.IsTrue(world.GetTileInfo(origin).Matches(new WorldTileType { kind = WorldTileKind.Edge }));
        Assert.IsFalse(world.GetTileInfo(origin).Matches(new WorldTileType { kind = WorldTileKind.FlatGround }));
        Occupancy().Add(origin + Vector2Int.left, null);
        RoadCells().Add(origin, new RoadCell(origin, 0, null));
        bool[] valid = new bool[3];
        Assert.IsFalse(Evaluate(origin, data.footprint, valid));
        CollectionAssert.AreEqual(new[] { false, false, true }, valid);
    }

    [Test]
    public void TypedResourceCannotBypassWaterAndNewRulesTakePrecedenceOverLegacyAssets()
    {
        AddSource(ResourceType.Oil, origin);
        data.footprint[0].requiredTileTypes.Add(new WorldTileType { kind = WorldTileKind.ResourceDeposit, resource = ResourceType.Oil });
        Assert.IsTrue(Evaluate(origin, data.footprint, new bool[3]));
        world.layers[0].surfaceType = TerrainSurfaceType.WaterLevel;
        Assert.IsFalse(Evaluate(origin, data.footprint, new bool[3]));
        foreach (BuildingTile tile in data.footprint)
            tile.allowedTileTypes.Add(new WorldTileType { kind = WorldTileKind.WaterLevel });
        Assert.IsTrue(Evaluate(origin, data.footprint, new bool[3]));
        Assert.IsFalse(Evaluate(origin + Vector2Int.up, data.footprint, new bool[3]),
            "Optional water cannot satisfy the separate required Oil Deposit rule.");
        // A retained legacy permission must not silently broaden edited rules.
        data.footprint[0].allowedGrounds.Add(Ground(BuildingGroundKind.TerrainLayer));
        BuildingGroundType legacyOil = Ground(BuildingGroundKind.MapResource);
        legacyOil.resourceType = ResourceType.Oil;
        data.footprint[0].allowedGrounds.Add(legacyOil);
        data.footprint[0].allowedTileTypes.Clear();
        data.footprint[0].requiredTileTypes.Clear();
        data.footprint[0].allowedTileTypes.Add(new WorldTileType { kind = WorldTileKind.FlatGround });
        Assert.IsFalse(Evaluate(origin, data.footprint, new bool[3]));
    }

    [Test]
    public void RequiredTileAlternativesMatchAnyAndUnknownTypesCannotMatch()
    {
        data.footprint[0].requiredTileTypes.Add(new WorldTileType { kind = (WorldTileKind)99 });
        Assert.IsFalse(Evaluate(origin, data.footprint, new bool[3]));
        data.footprint[0].requiredTileTypes.Add(new WorldTileType { kind = WorldTileKind.ResourceDeposit, resource = ResourceType.Oil });
        data.footprint[0].requiredTileTypes.Add(new WorldTileType { kind = WorldTileKind.ResourceNode, resource = ResourceType.CopperOre });
        AddSource(ResourceType.CopperOre, origin, MapResourceDepth.Overground, MapResourceShape.Nodes);
        Assert.IsTrue(Evaluate(origin, data.footprint, new bool[3]));
        data.footprint[0].requiredTileTypes.RemoveAt(2);
        Assert.IsFalse(Evaluate(origin, data.footprint, new bool[3]));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TypedGroundPreviewUsesSharedTerrainIconAndAppropriateFillerWithoutGroundAsset(bool required)
    {
        List<WorldTileType> rules = required ? data.footprint[1].requiredTileTypes : data.footprint[1].allowedTileTypes;
        rules.Add(new WorldTileType { kind = WorldTileKind.FlatGround });
        var input = managers.AddComponent<BuildingPlacementInput>();
        input.worldManager = world;
        input.placementOutlineSprite = outline;
        input.placementHatchSprite = hatch;
        input.optionalGroundFillSprite = outline;
        input.requiredGroundFillSprite = hatch;
        input.flatGroundTileIcon = hatch;
        Invoke(input, "CreatePreview", data);
        Transform icon = input.transform.Find("BuildingPreview_" + data.buildingName + "/GroundIcon_Flat Ground");
        Assert.IsNotNull(icon);
        Assert.AreEqual(world.cellSize, icon.localPosition.x);
        Transform cell = input.transform.Find("BuildingPreview_" + data.buildingName + "/PreviewCell_" + Vector2Int.right);
        var uvs = new List<Vector3>();
        cell.GetComponent<MeshFilter>().sharedMesh.GetUVs(0, uvs);
        Assert.That(uvs[0].y, Is.GreaterThanOrEqualTo(required ? 0f : 0.5f));
        Assert.That(uvs[0].y, Is.LessThan(required ? 0.5f : 1f));
        Mesh mesh = icon.GetComponent<MeshFilter>().sharedMesh;
        input.CancelPlacementPreview();
        Assert.IsTrue(mesh == null);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void FootprintOverrideReplacesAllRuleIconsPreservesValidityAndRestoresDefaultsWhenCleared(int mode)
    {
        BuildingTile tile = data.footprint[1];
        if (mode == 0)
        {
            tile.allowedTileTypes.Add(new WorldTileType { kind = WorldTileKind.FlatGround });
            tile.requiredTileTypes.Add(new WorldTileType { kind = WorldTileKind.WaterLevel });
        }
        else if (mode == 1)
        {
            BuildingGroundType cliff = Ground(BuildingGroundKind.Cliff);
            BuildingGroundType water = Ground(BuildingGroundKind.TerrainLayer);
            water.terrainLayer = 1;
            cliff.icon = hatch;
            water.icon = hatch;
            tile.allowedGrounds.Add(cliff);
            tile.allowedGrounds.Add(water);
            tile.requireGroundMatch = true;
        }
        bool beforeOverride = Evaluate(origin, data.footprint, new bool[3]);
        tile.footprintIconOverride = outline;
        Assert.AreEqual(beforeOverride, Evaluate(origin, data.footprint, new bool[3]));
        Assert.AreSame(outline, data.GetRotatedFootprint(1)[1].footprintIconOverride);

        var input = managers.AddComponent<BuildingPlacementInput>();
        input.worldManager = world;
        input.placementOutlineSprite = outline;
        input.placementHatchSprite = hatch;
        input.optionalGroundFillSprite = outline;
        input.requiredGroundFillSprite = outline;
        input.flatGroundTileIcon = hatch;
        input.waterLevelTileIcon = hatch;
        Invoke(input, "CreatePreview", data);
        string root = "BuildingPreview_" + data.buildingName;
        Transform icon = input.transform.Find(root + "/GroundIcon_Override");
        Assert.IsNotNull(icon);
        Assert.AreEqual(4, input.GetComponentsInChildren<MeshRenderer>().Length, "Three cells plus exactly one override icon.");
        Assert.AreEqual(world.cellSize, icon.localPosition.x);
        Assert.AreEqual(0f, icon.localPosition.z);
        Mesh iconMesh = icon.GetComponent<MeshFilter>().sharedMesh;
        var uvs = new List<Vector3>();
        iconMesh.GetUVs(0, uvs);
        foreach (Vector3 uv in uvs) Assert.GreaterOrEqual(uv.y, 0.5f, "The override sprite occupies the top half of the atlas.");
        Transform cell = input.transform.Find(root + "/PreviewCell_" + Vector2Int.right);
        cell.GetComponent<MeshFilter>().sharedMesh.GetUVs(0, uvs);
        Assert.AreEqual(2f, uvs[0].z, "An icon-only cell still needs the gap filler.");
        input.CancelPlacementPreview();
        Assert.IsTrue(iconMesh == null);

        tile.footprintIconOverride = null;
        Invoke(input, "CreatePreview", data);
        Assert.IsNull(input.transform.Find(root + "/GroundIcon_Override"));
        Assert.AreEqual(mode == 2 ? 3 : 5, input.GetComponentsInChildren<MeshRenderer>().Length);
        Assert.AreEqual(beforeOverride, Evaluate(origin, data.footprint, new bool[3]));
    }

    [TestCase(WorldTileKind.ResourceNode)]
    [TestCase(WorldTileKind.ResourceDeposit)]
    public void ResourceTilePreviewUsesTheWorldTileIconInsteadOfTheUiIcon(WorldTileKind kind)
    {
        var catalog = ScriptableObject.CreateInstance<ResourceCatalog>();
        groundAssets.Add(catalog);
        ResourceDefinition resource = catalog.AddResource("Custom world tile");
        resource.icon = hatch;
        resource.worldTileIcon = outline;
        FieldInfo current = typeof(ResourceCatalog).GetField("current", BindingFlags.Static | BindingFlags.NonPublic);
        object previous = current.GetValue(null);
        try
        {
            current.SetValue(null, catalog);
            data.footprint[1].allowedTileTypes.Add(new WorldTileType { kind = kind, resource = resource.resource });
            var input = managers.AddComponent<BuildingPlacementInput>();
            input.worldManager = world;
            input.placementOutlineSprite = outline;
            input.placementHatchSprite = hatch;
            Invoke(input, "CreatePreview", data);
            MeshRenderer[] renderers = input.GetComponentsInChildren<MeshRenderer>();
            Assert.AreEqual(4, renderers.Length);
            Transform icon = input.transform.Find("BuildingPreview_" + data.buildingName + "/GroundIcon_" + data.footprint[1].allowedTileTypes[0]);
            var uvs = new List<Vector3>();
            icon.GetComponent<MeshFilter>().sharedMesh.GetUVs(0, uvs);
            foreach (Vector3 uv in uvs) Assert.GreaterOrEqual(uv.y, 0.5f);
            Assert.AreSame(hatch, catalog.GetResourceIcon(resource.resource));
        }
        finally { current.SetValue(null, previous); }
    }

    [Test]
    public void RangeFailureTintsTheFootprintAndMatchingRangeRestoresPerCellObstructionFeedback()
    {
        data.ranges.Add(new BuildingRange
        {
            radius = 1,
            requiredTileTypes = new List<WorldTileType>
            {
                new WorldTileType { kind = WorldTileKind.ResourceDeposit, resource = ResourceType.Concrete }
            }
        });
        Vector2Int matchingOrigin = origin + Vector2Int.up * 4;
        AddSource(ResourceType.Concrete, matchingOrigin + Vector2Int.up);
        var input = managers.AddComponent<BuildingPlacementInput>();
        input.worldManager = world;
        input.placementOutlineSprite = outline;
        input.placementHatchSprite = hatch;
        Invoke(input, "CreatePreview", data);
        MeshRenderer[] renderers = input.GetComponentsInChildren<MeshRenderer>();
        Assert.IsFalse((bool)Invoke(input, "UpdatePreviewValidity", origin, 0));
        foreach (MeshRenderer renderer in renderers) AssertTint(renderer, input.invalidCellColor, Color.red);
        var display = (BuildingRangeDisplay)typeof(BuildingPlacementInput)
            .GetField("_rangeDisplay", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(input);
        FinishRange(display.Evaluation);
        Assert.IsFalse((bool)Invoke(input, "UpdatePreviewValidity", origin, 0));
        foreach (MeshRenderer renderer in renderers) AssertTint(renderer, input.invalidCellColor, Color.red);

        Assert.IsFalse((bool)Invoke(input, "UpdatePreviewValidity", matchingOrigin, 0));
        FinishRange(display.Evaluation);
        Assert.IsTrue((bool)Invoke(input, "UpdatePreviewValidity", matchingOrigin, 0));
        foreach (MeshRenderer renderer in renderers) AssertTint(renderer, input.validCellColor, input.outlineColor);
        Occupancy().Add(matchingOrigin + Vector2Int.right, null);
        Assert.IsFalse((bool)Invoke(input, "UpdatePreviewValidity", matchingOrigin, 0));
        AssertTint(renderers[0], input.validCellColor, input.outlineColor);
        AssertTint(renderers[1], input.invalidCellColor, Color.red);
        AssertTint(renderers[2], input.validCellColor, input.outlineColor);
    }

    private static void FinishRange(BuildingRangeEvaluation work)
    {
        for (int i = 0; i < 1000 && !work.IsComplete; i++) work.Advance(16);
        Assert.IsTrue(work.IsComplete);
        Assert.IsFalse(work.IsCancelled);
    }

    private BuildingGroundType Ground(BuildingGroundKind kind)
    {
        var ground = ScriptableObject.CreateInstance<BuildingGroundType>();
        ground.kind = kind;
        groundAssets.Add(ground);
        return ground;
    }

    private void AddSource(ResourceType type, Vector2Int cell, MapResourceDepth depth = MapResourceDepth.Overground,
        MapResourceShape shape = MapResourceShape.Deposit)
    {
        var definition = ScriptableObject.CreateInstance<MapResourceDefinition>();
        groundAssets.Add(definition);
        definition.resourceType = type;
        definition.shape = shape;
        definition.depth = depth;
        typeof(MapResourceMap).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(world.ResourceMap, new object[] { definition, new[] { cell } });
    }

    private void SetTransitionBand(float transitionHeight, float flatLayer, int rotation)
    {
        bool alongX = rotation % 2 == 0;
        world.visualHeights = new float[17, 17];
        for (int x = 0; x <= 16; x++)
            for (int z = 0; z <= 16; z++)
                world.visualHeights[x, z] = (alongX ? x == origin.x : z == origin.y)
                    ? transitionHeight : flatLayer;
    }

    private void SetTerraceEdge(float transitionHeight)
    {
        world.visualHeights = new float[17, 17];
        for (int x = 0; x <= 16; x++)
            for (int z = 0; z <= 16; z++)
                world.visualHeights[x, z] = x < origin.x ? 0f : x > origin.x ? 1f : transitionHeight;
    }

    private static List<BuildingTile> Footprint(params int[] offsets)
    {
        var footprint = new List<BuildingTile>();
        foreach (int offset in offsets)
            footprint.Add(new BuildingTile { localPosition = Vector2Int.right * offset });
        return footprint;
    }

    private bool Evaluate(Vector2Int position, List<BuildingTile> footprint, bool[] valid) =>
        buildings.CanPlaceBuilding(world, position, footprint, valid, out _);

    private Dictionary<Vector2Int, Building> Occupancy() =>
        (Dictionary<Vector2Int, Building>)typeof(BuildingSystemBackend)
            .GetField("_gridOccupancy", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(buildings);

    private Dictionary<Vector2Int, RoadCell> RoadCells() =>
        (Dictionary<Vector2Int, RoadCell>)roads.Roads;

    private Mesh CreateMesh(Vector2Int cell, HashSet<Vector2Int> footprint)
    {
        Mesh mesh = BuildingPlacementPreviewGeometry.CreateCellMesh(cell, footprint, 2f, outline);
        meshes.Add(mesh);
        return mesh;
    }

    private static void AssertTint(MeshRenderer renderer, Color hatchColor, Color outlineColor)
    {
        var colors = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(colors);
        Assert.AreEqual(hatchColor, colors.GetColor("_Color"));
        Assert.AreEqual(outlineColor, colors.GetColor("_OutlineColor"));
    }

    private static object Invoke(object target, string method, params object[] arguments) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, arguments);
}
