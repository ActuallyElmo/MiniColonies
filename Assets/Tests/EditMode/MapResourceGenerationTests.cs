using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class MapResourceGenerationTests
{
    private readonly List<Object> temporary = new List<Object>();
    private const int Size = 24;

    [TearDown]
    public void TearDown()
    {
        for (int i = temporary.Count - 1; i >= 0; i--)
            if (temporary[i] != null) Object.DestroyImmediate(temporary[i]);
        temporary.Clear();
    }

    [Test]
    public void FixedSeedReproducesCellsSourcesAndQuantities()
    {
        var rule = Rule(MapResourceShape.Deposit, 5);
        rule.depositTiles = new Vector2Int(4, 9);
        rule.totalUnits = new Vector2Int(100, 200);
        var first = Generate(new[] { rule });
        var second = Generate(new[] { rule });
        Assert.AreEqual(5, first.Sources.Count);
        for (int i = 0; i < first.Sources.Count; i++)
        {
            Assert.AreEqual(first.Sources[i].Id, second.Sources[i].Id);
            Assert.AreEqual(first.Sources[i].GeneratedUnits, second.Sources[i].GeneratedUnits);
            CollectionAssert.AreEqual(first.Sources[i].Cells, second.Sources[i].Cells);
        }
        var other = Generate(new[] { rule }, seed: "Different seed");
        Assert.IsFalse(first.Sources.SelectMany(s => s.Cells).SequenceEqual(other.Sources.SelectMany(s => s.Cells)));
    }

    [Test]
    public void ResourceDefaultsChoosePerResourceTileGrowth()
    {
        var rule = Rule(MapResourceShape.Deposit, 1);
        rule.resource.resourceType = ResourceType.Oil;
        Assert.AreEqual(ResourceDepositGrowth.Vein, MapResourceDepositGrowth.ResolvePattern(rule.resource));
        rule.resource.resourceType = ResourceType.Water;
        Assert.AreEqual(ResourceDepositGrowth.Organic, MapResourceDepositGrowth.ResolvePattern(rule.resource));
        rule.resource.resourceType = ResourceType.Concrete;
        Assert.AreEqual(ResourceDepositGrowth.Compact, MapResourceDepositGrowth.ResolvePattern(rule.resource));
    }

    [TestCase(ResourceDepositGrowth.Compact)]
    [TestCase(ResourceDepositGrowth.Organic)]
    [TestCase(ResourceDepositGrowth.Vein)]
    [TestCase(ResourceDepositGrowth.Branching)]
    public void DepositTileGrowthPreservesConnectedStrictFootprintsAndBudgets(ResourceDepositGrowth pattern)
    {
        var rule = Rule(MapResourceShape.Deposit, 4);
        rule.resource.depositGrowth = pattern;
        rule.resource.depositElongation = 8f;
        rule.resource.depositIrregularity = 1f;
        rule.totalUnits = new Vector2Int(101, 101);
        rule.restrictMapPosition = true;
        rule.mapRegion = new Rect(0.25f, 0.25f, 0.5f, 0.5f);
        rule.restrictHeight = true;
        rule.heightRange = new Vector2(3f, 3f);
        Func<Vector2Int, ResourceTerrainCell> sample = cell => new ResourceTerrainCell(true,
            cell.x == 12 ? 0f : 3f, occupied: cell.y == 11);
        var first = Generate(new[] { rule }, sample);
        var repeated = Generate(new[] { rule }, sample);
        Assert.AreEqual(4, first.Sources.Count);
        Assert.AreEqual(101, first.Sources.Sum(source => source.GeneratedUnits));
        var occupied = new HashSet<Vector2Int>();
        for (int i = 0; i < first.Sources.Count; i++)
        {
            var source = first.Sources[i];
            Assert.AreEqual(6, source.Cells.Count);
            AssertConnected(source.Cells);
            CollectionAssert.AreEqual(source.Cells, repeated.Sources[i].Cells);
            Assert.AreEqual(source.GeneratedUnits, repeated.Sources[i].GeneratedUnits);
            foreach (var cell in source.Cells)
            {
                Assert.IsTrue(occupied.Add(cell));
                Assert.IsTrue(rule.Allows(cell, Size, false, sample(cell)));
            }
        }
    }

    [Test]
    public void VeinTileGrowthElongatesAndRotatesTheFootprintWithoutMovingItsOrigin()
    {
        var rule = Rule(MapResourceShape.Deposit, 1);
        rule.depositTiles = new Vector2Int(32, 32);
        rule.resource.depositGrowth = ResourceDepositGrowth.Vein;
        rule.resource.depositElongation = 12f;
        rule.resource.randomizeDepositDirection = false;
        rule.resource.depositGrowthAngle = 0f;
        var horizontal = Generate(new[] { rule }).Sources[0];
        rule.resource.depositGrowthAngle = 90f;
        var vertical = Generate(new[] { rule }).Sources[0];
        Assert.AreEqual(horizontal.Cells[0], vertical.Cells[0]);
        Assert.AreEqual(32, horizontal.Cells.Count);
        Assert.AreEqual(32, vertical.Cells.Count);
        Assert.GreaterOrEqual(Span(horizontal.Cells, true), Span(horizontal.Cells, false) * 2);
        Assert.GreaterOrEqual(Span(vertical.Cells, false), Span(vertical.Cells, true) * 2);
        AssertConnected(horizontal.Cells);
        AssertConnected(vertical.Cells);

        int Span(IReadOnlyList<Vector2Int> cells, bool x) => x
            ? cells.Max(cell => cell.x) - cells.Min(cell => cell.x) + 1
            : cells.Max(cell => cell.y) - cells.Min(cell => cell.y) + 1;
    }

    [Test]
    public void BranchingAndOrganicTileGrowthProduceDifferentFootprintsFromCompactGrowth()
    {
        var rule = Rule(MapResourceShape.Deposit, 1);
        rule.depositTiles = new Vector2Int(32, 32);
        rule.resource.depositIrregularity = 1f;
        int compactEdges = 0, branchingEdges = 0;
        bool organicDiffers = false;
        for (int i = 0; i < 8; i++)
        {
            string seed = "Tile shape comparison " + i;
            rule.resource.depositGrowth = ResourceDepositGrowth.Compact;
            var compact = Generate(new[] { rule }, seed: seed).Sources[0];
            rule.resource.depositGrowth = ResourceDepositGrowth.Branching;
            var branching = Generate(new[] { rule }, seed: seed).Sources[0];
            rule.resource.depositGrowth = ResourceDepositGrowth.Organic;
            var organic = Generate(new[] { rule }, seed: seed).Sources[0];
            Assert.AreEqual(compact.Cells[0], branching.Cells[0]);
            Assert.AreEqual(compact.Cells[0], organic.Cells[0]);
            Assert.AreEqual(32, branching.Cells.Count);
            AssertConnected(branching.Cells);
            AssertConnected(organic.Cells);
            compactEdges += Perimeter(compact.Cells);
            branchingEdges += Perimeter(branching.Cells);
            organicDiffers |= !new HashSet<Vector2Int>(compact.Cells).SetEquals(organic.Cells);
        }
        Assert.Greater(branchingEdges, compactEdges * 1.25f, "Branching must create arms rather than another compact blob.");
        Assert.IsTrue(organicDiffers, "Organic irregularity must change occupied tiles, not just their paint.");

        int Perimeter(IReadOnlyList<Vector2Int> cells)
        {
            var footprint = new HashSet<Vector2Int>(cells);
            int edges = 0;
            foreach (var cell in cells)
                foreach (var direction in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                    if (!footprint.Contains(cell + direction)) edges++;
            return edges;
        }
    }

    [Test]
    public void GrowthValuesAreClampedAndNodeDistributionIgnoresDepositShape()
    {
        var rule = Rule(MapResourceShape.Deposit, 1);
        rule.resource.depositGrowth = ResourceDepositGrowth.Vein;
        rule.resource.depositCompactness = float.NaN;
        rule.resource.depositElongation = float.PositiveInfinity;
        rule.resource.depositGrowthAngle = float.NaN;
        rule.resource.depositIrregularity = float.NaN;
        rule.resource.depositBranchChance = float.NaN;
        Assert.AreEqual(1, Generate(new[] { rule }).Sources.Count);
        rule.resource.depositGrowth = (ResourceDepositGrowth)int.MaxValue;
        Assert.AreEqual(ResourceDepositGrowth.Compact, MapResourceDepositGrowth.ResolvePattern(rule.resource));
        rule.resource.shape = MapResourceShape.Nodes;
        rule.sourceCount = new Vector2Int(8, 8);
        var before = Generate(new[] { rule });
        rule.resource.depositGrowth = ResourceDepositGrowth.Branching;
        var after = Generate(new[] { rule });
        CollectionAssert.AreEqual(before.Sources.SelectMany(source => source.Cells), after.Sources.SelectMany(source => source.Cells));
    }

    [Test]
    public void AppearanceEditsCannotChangeGeneratedTileStateOrSupply()
    {
        var nodes = Rule(MapResourceShape.Nodes, 8);
        var deposit = Rule(MapResourceShape.Deposit, 3);
        deposit.resource.depth = MapResourceDepth.Underground;
        deposit.resource.resourceType = ResourceType.Oil;
        var rules = new[] { deposit, nodes };
        var baseline = Generate(rules);
        var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
        var prefab = new GameObject("Resource appearance invariant test prefab");
        temporary.Add(material);
        temporary.Add(prefab);
        foreach (MapResourceTerrainTransition style in new[] { MapResourceTerrainTransition.Polygonal, MapResourceTerrainTransition.Smooth })
        {
            foreach (var rule in rules)
            {
                rule.resource.terrainMaterial = style == MapResourceTerrainTransition.Polygonal ? material : null;
                rule.resource.terrainTransitionStyle = style;
                rule.resource.terrainTint = Color.magenta;
                rule.resource.terrainBlendWidth = 0.5f;
                rule.resource.terrainBlendAmount = 1f;
                rule.resource.terrainBlendRandomness = 1f;
                rule.resource.terrainBlendNoiseSize = 0.1f;
                rule.resource.terrainEdgeRoundness = 0.5f;
                rule.resource.prefabVariants.Add(prefab);
                rule.resource.depositPrefabChance = 1f;
                rule.resource.prefabScaleRange = new Vector2(0.2f, 3f);
                rule.resource.randomYaw = false;
                rule.resource.prefabHeightOffset = 2f;
            }
            var regenerated = Generate(rules);
            Assert.AreEqual(baseline.Sources.Count, regenerated.Sources.Count);
            for (int i = 0; i < baseline.Sources.Count; i++)
            {
                var expected = baseline.Sources[i];
                var actual = regenerated.Sources[i];
                Assert.AreEqual(expected.Id, actual.Id);
                CollectionAssert.AreEqual(expected.Cells, actual.Cells);
                Assert.AreEqual(expected.GeneratedUnits, actual.GeneratedUnits);
                Assert.AreEqual(expected.Unlimited, actual.Unlimited);
                Assert.AreEqual(expected.BlocksConstruction, actual.BlocksConstruction);
            }
            for (int x = 0; x < Size; x++)
                for (int z = 0; z < Size; z++)
                {
                    var cell = new Vector2Int(x, z);
                    Assert.AreEqual(baseline.TryGetSource(cell, out var before), regenerated.TryGetSource(cell, out var after));
                    if (before != null) Assert.AreEqual(before.Id, after.Id);
                    Assert.AreEqual(baseline.BlocksConstruction(cell), regenerated.BlocksConstruction(cell));
                }
        }
    }

    [Test]
    public void DepositsAreConnectedAndAllCellsResolveToOneSourceWithoutOverlaps()
    {
        var first = Rule(MapResourceShape.Deposit, 4);
        var second = Rule(MapResourceShape.Nodes, 12);
        var map = Generate(new[] { first, second });
        Assert.AreEqual(16, map.Sources.Count);
        var occupied = new HashSet<Vector2Int>();
        foreach (var source in map.Sources)
        {
            foreach (var cell in source.Cells)
            {
                Assert.IsTrue(occupied.Add(cell));
                Assert.IsTrue(map.TryGetSource(cell, out var found));
                Assert.AreSame(source, found);
            }
            if (source.Definition.shape == MapResourceShape.Deposit)
            {
                Assert.AreEqual(6, source.Cells.Count);
                AssertConnected(source.Cells);
            }
            else Assert.AreEqual(1, source.Cells.Count);
        }
    }

    [Test]
    public void ClusteredNodesShareAnAreaButRetainIndependentSources()
    {
        var rule = Rule(MapResourceShape.Nodes, 8);
        rule.nodeDistribution = ResourceNodeDistribution.Clustered;
        rule.nodesPerCluster = new Vector2Int(8, 8);
        var map = Generate(new[] { rule });
        Assert.AreEqual(8, map.Sources.Count);
        AssertConnected(map.Sources.SelectMany(s => s.Cells).ToList());
        Assert.IsTrue(map.Sources.All(s => s.Cells.Count == 1 && s.GeneratedUnits > 0));
    }

    [Test]
    public void SampledUnitsAreAMapWideBudgetRatherThanAnAmountPerTile()
    {
        var rule = Rule(MapResourceShape.Deposit, 4);
        rule.totalUnits = new Vector2Int(101, 101);
        var map = Generate(new[] { rule });
        Assert.AreEqual(101, map.Sources.Sum(s => s.GeneratedUnits));
        Assert.IsTrue(map.Sources.All(s => !s.Unlimited && s.GeneratedUnits > 0));
        rule.resource.unlimited = true;
        rule.totalUnits = Vector2Int.zero;
        var unlimited = Generate(new[] { rule });
        Assert.AreEqual(4, unlimited.Sources.Count);
        Assert.IsTrue(unlimited.Sources.All(s => s.Unlimited && s.GeneratedUnits == 0));
    }

    [Test]
    public void EveryDepositCellRespectsAllEnabledLocationRulesAndExistingOccupancy()
    {
        var rule = Rule(MapResourceShape.Deposit, 2);
        rule.restrictHeight = true;
        rule.heightRange = new Vector2(3f, 3f);
        rule.restrictMapPosition = true;
        rule.mapRegion = new Rect(0.25f, 0.25f, 0.5f, 0.5f);
        rule.restrictDistanceFromCenter = true;
        rule.centerDistanceRange = new Vector2(0f, 0.65f);
        Func<Vector2Int, ResourceTerrainCell> sample = cell => new ResourceTerrainCell(
            cell.x > 0 && cell.y > 0 && cell.x < Size - 1 && cell.y < Size - 1,
            cell.x < Size / 2 ? 0f : 3f, cell.y == 12, cell.x == 15);
        var map = Generate(new[] { rule }, sample, circular: true);
        Assert.AreEqual(2, map.Sources.Count);
        foreach (var cell in map.Sources.SelectMany(s => s.Cells))
        {
            Assert.IsTrue(rule.Allows(cell, Size, true, sample(cell)));
            Assert.That(cell.x, Is.InRange(12, 17));
            Assert.AreNotEqual(12, cell.y);
            Assert.AreNotEqual(15, cell.x);
        }
    }

    [Test]
    public void ImpossibleLocationsAndUndersizedIslandsNeverRelaxRestrictions()
    {
        var rule = Rule(MapResourceShape.Deposit, 2);
        var map = Generate(new[] { rule }, cell => new ResourceTerrainCell(cell.x % 3 == 0 && cell.y % 3 == 0, 0f));
        Assert.IsEmpty(map.Sources);
        Assert.AreEqual(1, map.Warnings.Count);
        rule.restrictHeight = true;
        rule.heightRange = new Vector2(5f, 6f);
        map = Generate(new[] { rule });
        Assert.IsEmpty(map.Sources);
        Assert.AreEqual(1, map.Warnings.Count);
    }

    [Test]
    public void DensityUsesEligibleAreaAndClampsToSourceLimits()
    {
        var rule = Rule(MapResourceShape.Nodes, 1);
        rule.sourceCount = new Vector2Int(1, 20);
        rule.sourcesPerThousandTiles = 10f;
        var map = Generate(new[] { rule });
        // Node clearance leaves 20 * 20 eligible tiles at ten sources / 1000.
        Assert.AreEqual(4, map.Sources.Count);
        rule.sourceCount = new Vector2Int(8, 10);
        Assert.AreEqual(8, Generate(new[] { rule }).Sources.Count);
        rule.sourcesPerThousandTiles = 10000f;
        Assert.AreEqual(10, Generate(new[] { rule }).Sources.Count);
    }

    [Test]
    public void InvalidOrDisabledSettingsAreSafeAndFiniteSourcesNeverGetZeroUnits()
    {
        var rule = Rule(MapResourceShape.Nodes, 10);
        rule.totalUnits = new Vector2Int(3, 3);
        var map = Generate(new[] { rule });
        Assert.AreEqual(3, map.Sources.Count);
        Assert.IsTrue(map.Sources.All(s => s.GeneratedUnits == 1));
        Assert.AreEqual(1, map.Warnings.Count);
        rule.sourcesPerThousandTiles = float.NaN;
        Assert.IsEmpty(Generate(new[] { rule }).Sources);
        rule.sourcesPerThousandTiles = 1f;
        rule.restrictHeight = true;
        rule.heightRange = new Vector2(float.NaN, 1f);
        Assert.IsEmpty(Generate(new[] { rule }).Sources);
        rule.enabled = false;
        Assert.IsEmpty(Generate(new[] { rule, null, new MapResourceSpawnSettings() }).Sources);
    }

    [TestCase(ResourceNodeDistribution.Scattered)]
    [TestCase(ResourceNodeDistribution.Clustered)]
    public void NodesRequireBuildableFlatGroundAndClearanceEvenWhenFlatPreferenceIsDisabled(ResourceNodeDistribution distribution)
    {
        var rule = Rule(MapResourceShape.Nodes, 200);
        rule.nodeDistribution = distribution;
        rule.requireFlatTerrain = false;
        rule.sourceCount = new Vector2Int(1, 200);
        rule.sourcesPerThousandTiles = 10000f;
        Func<Vector2Int, ResourceTerrainCell> sample = cell => new ResourceTerrainCell(
            cell.x > 0 && cell.y > 0 && cell.x < Size - 1 && cell.y < Size - 1,
            cell.x >= 12 ? 1f : 0f, transition: cell.y == 8, buildable: cell.y != 16);
        var map = Generate(new[] { rule }, sample);
        Assert.IsNotEmpty(map.Sources);
        foreach (var cell in map.Sources.SelectMany(source => source.Cells))
        {
            Assert.That(cell.x, Is.InRange(2, Size - 3));
            Assert.That(cell.y, Is.InRange(2, Size - 3));
            Assert.IsFalse(cell.y >= 7 && cell.y <= 9, "Stay clear of transitions.");
            Assert.IsFalse(cell.y >= 15 && cell.y <= 17, "Stay clear of unbuildable ground.");
            Assert.IsFalse(cell.x == 11 || cell.x == 12, "Stay clear of height discontinuities.");
        }
    }

    [Test]
    public void DepositsCanStillUseUnbuildableTerrainWhenTheirLocationRulesAllowIt()
    {
        var rule = Rule(MapResourceShape.Deposit, 2);
        rule.requireFlatTerrain = false;
        var map = Generate(new[] { rule }, cell => new ResourceTerrainCell(true, 0f, buildable: false));
        Assert.AreEqual(2, map.Sources.Count);
    }

    [Test]
    public void ResourceBlendMatchesTerrainAtBordersAndRemainsContinuousBetweenSameTypeTiles()
    {
        var rule = Rule(MapResourceShape.Nodes, 1);
        var map = new MapResourceMap();
        map.Add(rule.resource, new[] { new Vector2Int(4, 4) });
        map.Add(rule.resource, new[] { new Vector2Int(5, 4) });
        int mask = MapResourceTerrainAppearance.BoundaryMask(map, new Vector2Int(4, 4), rule.resource);
        Assert.AreEqual(0f, MapResourceTerrainAppearance.BlendWeight(0f, 0.5f, mask, 0.35f));
        Assert.AreEqual(1f, MapResourceTerrainAppearance.BlendWeight(0.5f, 0.5f, mask, 0.35f));
        Assert.AreEqual(1f, MapResourceTerrainAppearance.BlendWeight(1f, 0.5f, mask, 0.35f));
        float near = MapResourceTerrainAppearance.BlendWeight(0.1f, 0.5f, mask, 0.35f);
        float farther = MapResourceTerrainAppearance.BlendWeight(0.2f, 0.5f, mask, 0.35f);
        Assert.That(near, Is.GreaterThan(0f).And.LessThan(farther));
        Assert.That(farther, Is.LessThan(1f));
        Assert.AreEqual(1f, MapResourceTerrainAppearance.BlendWeight(0f, 0.5f, mask, 0f));
        // The adjoining tile computes the same weight along the shared edge, including its corners.
        int adjacent = MapResourceTerrainAppearance.BoundaryMask(map, new Vector2Int(5, 4), rule.resource);
        for (int i = 0; i <= 10; i++)
            Assert.AreEqual(MapResourceTerrainAppearance.BlendWeight(1f, i / 10f, mask, 0.35f, 0.6f, 0.3f),
                MapResourceTerrainAppearance.BlendWeight(0f, i / 10f, adjacent, 0.35f, 0.6f, 0.3f));
    }

    [Test]
    public void BlendAmountControlsFadeStrengthWithoutChangingTheResourceInterior()
    {
        var rule = Rule(MapResourceShape.Nodes, 1);
        var map = new MapResourceMap();
        map.Add(rule.resource, new[] { Vector2Int.zero });
        int mask = MapResourceTerrainAppearance.BoundaryMask(map, Vector2Int.zero, rule.resource);
        float full = MapResourceTerrainAppearance.BlendWeight(0.1f, 0.5f, mask, 0.35f, 1f);
        Assert.That(full, Is.GreaterThan(0f).And.LessThan(1f));
        Assert.AreEqual(1f, MapResourceTerrainAppearance.BlendWeight(0.1f, 0.5f, mask, 0.35f, 0f));
        Assert.That(MapResourceTerrainAppearance.BlendWeight(0.1f, 0.5f, mask, 0.35f, 0.5f),
            Is.EqualTo((1f + full) * 0.5f).Within(0.0001f));
        Assert.AreEqual(1f, MapResourceTerrainAppearance.BlendWeight(0.5f, 0.5f, mask, 0.35f, 1f));
        Assert.AreEqual(full, MapResourceTerrainAppearance.BlendWeight(0.1f, 0.5f, mask, 0.35f, float.NaN));
        Assert.AreEqual(full, MapResourceTerrainAppearance.BlendWeight(0.1f, 0.5f, mask, 0.35f, 2f));
    }

    [Test]
    public void BlendNoiseIsRepeatableDependsOnPositionAndSeedAndDoesNotConsumeGlobalRandom()
    {
        var state = UnityEngine.Random.state;
        try
        {
            var samples = new List<float>();
            bool seedChanged = false;
            for (int i = 0; i < 24; i++)
            {
                float x = 10.13f + i * 0.17f;
                float value = MapResourceTerrainAppearance.BlendVariation(x, 6.45f, 12345, 1f, 0.8f);
                samples.Add(value);
                // Interleaved sampling must not change a repeated result.
                float other = MapResourceTerrainAppearance.BlendVariation(x, 6.45f, -23456, 1f, 0.8f);
                seedChanged |= Mathf.Abs(value - other) > 0.0001f;
                Assert.AreEqual(value, MapResourceTerrainAppearance.BlendVariation(x, 6.45f, 12345, 1f, 0.8f));
                Assert.That(value, Is.InRange(-1f, 1f));
            }
            Assert.Greater(samples.Max() - samples.Min(), 0.05f);
            Assert.IsTrue(seedChanged);
            Assert.AreEqual(state, UnityEngine.Random.state);
        }
        finally { UnityEngine.Random.state = state; }
    }

    [Test]
    public void RandomizedBlendingKeepsBordersInteriorsAndSharedChunkEdgesConsistent()
    {
        var rule = Rule(MapResourceShape.Nodes, 1);
        var map = new MapResourceMap();
        var left = new Vector2Int(9, 10);
        var right = new Vector2Int(10, 10);
        map.Add(rule.resource, new[] { left });
        map.Add(rule.resource, new[] { right });
        int leftMask = MapResourceTerrainAppearance.BoundaryMask(map, left, rule.resource);
        int rightMask = MapResourceTerrainAppearance.BoundaryMask(map, right, rule.resource);
        for (int i = 0; i <= 16; i++)
        {
            float z = i / 16f;
            float noise = MapResourceTerrainAppearance.BlendVariation(10f, 10f + z, 56789, 1f, 0.5f);
            Assert.That(MapResourceTerrainAppearance.BlendWeight(1f, z, leftMask, 0.35f, 1f, 0.2f, noise),
                Is.EqualTo(MapResourceTerrainAppearance.BlendWeight(0f, z, rightMask, 0.35f, 1f, 0.2f, noise)).Within(0.00001f));
        }
        foreach (float noise in new[] { -1f, -0.5f, 0f, 0.5f, 1f })
        {
            Assert.AreEqual(0f, MapResourceTerrainAppearance.BlendWeight(0f, 0.5f, leftMask, 0.35f, 1f, 0f, noise));
            Assert.AreEqual(1f, MapResourceTerrainAppearance.BlendWeight(0.5f, 0.5f, leftMask, 0.35f, 1f, 0f, noise));
            Assert.AreEqual(1f, MapResourceTerrainAppearance.BlendWeight(0.1f, 0.5f, leftMask, 0.35f, 0f, 0f, noise));
        }
        Assert.Less(MapResourceTerrainAppearance.BlendWeight(0.1f, 0.5f, leftMask, 0.35f, 1f, 0f, -0.5f),
            MapResourceTerrainAppearance.BlendWeight(0.1f, 0.5f, leftMask, 0.35f, 1f, 0f, 0.5f));
    }

    [Test]
    public void ZeroOrInvalidNoiseRestoresUniformBlendingAndInvalidPatchSizeIsSafe()
    {
        Assert.AreEqual(0f, MapResourceTerrainAppearance.BlendVariation(10.13f, 6.45f, 12345, 0f, 1f));
        Assert.AreEqual(0f, MapResourceTerrainAppearance.BlendVariation(10.13f, 6.45f, 12345, float.NaN, 1f));
        float expected = MapResourceTerrainAppearance.BlendVariation(10.13f, 6.45f, 12345, 0.65f, 1f);
        Assert.AreEqual(expected, MapResourceTerrainAppearance.BlendVariation(10.13f, 6.45f, 12345, 0.65f, float.NaN));
        float minimumSize = MapResourceTerrainAppearance.BlendVariation(10.13f, 6.45f, 12345, 0.65f, 0.1f);
        Assert.AreEqual(minimumSize, MapResourceTerrainAppearance.BlendVariation(10.13f, 6.45f, 12345, 0.65f, -1f));
        const int allBoundaries = 511 & ~(1 << 4);
        float uniform = MapResourceTerrainAppearance.BlendWeight(0.1f, 0.5f, allBoundaries, 0.35f);
        Assert.AreEqual(uniform, MapResourceTerrainAppearance.BlendWeight(0.1f, 0.5f, allBoundaries, 0.35f, 1f, 0f,
            MapResourceTerrainAppearance.BlendVariation(0.1f, 0.5f, 12345, 0f, 1f)));
        Assert.AreEqual(uniform, MapResourceTerrainAppearance.BlendWeight(0.1f, 0.5f, allBoundaries, 0.35f, 1f, 0f, float.NaN));
    }

    [Test]
    public void PolygonCornersAreBroadRepeatableCutsThatPreserveTheTileCenter()
    {
        var rule = Rule(MapResourceShape.Nodes, 1);
        var first = new Vector3[MapResourceTerrainAppearance.MaximumPolygonCuts];
        var repeated = new Vector3[MapResourceTerrainAppearance.MaximumPolygonCuts];
        var other = new Vector3[MapResourceTerrainAppearance.MaximumPolygonCuts];
        var randomState = UnityEngine.Random.state;
        try
        {
            int count = MapResourceTerrainAppearance.BuildPolygonCuts(new Vector2Int(5, 7), 511, 13579, rule.resource, first);
            Assert.AreEqual(8, count);
            MapResourceTerrainAppearance.BuildPolygonCuts(new Vector2Int(5, 7), 511, 13579, rule.resource, repeated);
            MapResourceTerrainAppearance.BuildPolygonCuts(new Vector2Int(5, 7), 511, -24680, rule.resource, other);
            CollectionAssert.AreEqual(first, repeated);
            Assert.IsFalse(first.SequenceEqual(other));
            for (int i = 0; i < count; i++)
            {
                Vector3 plane = first[i];
                Assert.Greater(plane.x * 0.5f + plane.y * 0.5f + plane.z, 0f);
                if (i < 4) continue;
                Assert.That(1f / Mathf.Abs(plane.x), Is.InRange(0.2f, 0.48001f));
                Assert.That(1f / Mathf.Abs(plane.y), Is.InRange(0.2f, 0.48001f));
            }
            Assert.AreEqual(randomState, UnityEngine.Random.state);
        }
        finally { UnityEngine.Random.state = randomState; }
    }

    [Test]
    public void PolygonCutsRespectSharedEdgesDisabledControlsAndInvalidValues()
    {
        var rule = Rule(MapResourceShape.Nodes, 1);
        var cuts = new Vector3[MapResourceTerrainAppearance.MaximumPolygonCuts];
        Assert.AreEqual(0, MapResourceTerrainAppearance.BuildPolygonCuts(Vector2Int.zero, 0, 123, rule.resource, cuts));
        // Only the left edge is exposed: shared top/right/bottom edges must stay intact.
        Assert.AreEqual(0, MapResourceTerrainAppearance.BuildPolygonCuts(Vector2Int.zero, 1 << 1, 123, rule.resource, cuts));
        rule.resource.terrainBlendAmount = 0f;
        rule.resource.terrainEdgeRoundness = 0f;
        Assert.AreEqual(0, MapResourceTerrainAppearance.BuildPolygonCuts(Vector2Int.zero, 511, 123, rule.resource, cuts));
        rule.resource.terrainBlendAmount = float.NaN;
        rule.resource.terrainBlendWidth = float.NaN;
        rule.resource.terrainEdgeRoundness = float.NaN;
        rule.resource.terrainBlendRandomness = float.NaN;
        rule.resource.terrainBlendNoiseSize = float.NaN;
        Assert.AreEqual(8, MapResourceTerrainAppearance.BuildPolygonCuts(Vector2Int.zero, 511, 123, rule.resource, cuts));
        foreach (Vector3 plane in cuts)
            Assert.IsTrue(MapResourceSpawnSettings.IsFinite(plane.x) &&
                MapResourceSpawnSettings.IsFinite(plane.y) && MapResourceSpawnSettings.IsFinite(plane.z));
    }

    [Test]
    public void PolygonalStraightBordersRecedeWithinTilesAndMeetAcrossTileBoundaries()
    {
        var rule = Rule(MapResourceShape.Deposit, 1);
        var map = new MapResourceMap();
        var cells = new List<Vector2Int>();
        for (int x = 4; x <= 6; x++)
            for (int z = 4; z <= 10; z++) cells.Add(new Vector2Int(x, z));
        map.Add(rule.resource, cells);
        var first = new Vector3[MapResourceTerrainAppearance.MaximumPolygonCuts];
        var next = new Vector3[MapResourceTerrainAppearance.MaximumPolygonCuts];
        var cell = new Vector2Int(4, 6);
        int mask = MapResourceTerrainAppearance.BoundaryMask(map, cell, rule.resource);
        int nextMask = MapResourceTerrainAppearance.BoundaryMask(map, cell + Vector2Int.up, rule.resource);
        Assert.AreEqual(1, MapResourceTerrainAppearance.BuildPolygonCuts(cell, mask, 123, rule.resource, first));
        Assert.AreEqual(1, MapResourceTerrainAppearance.BuildPolygonCuts(cell + Vector2Int.up, nextMask, 123, rule.resource, next));
        float start = -first[0].z, end = -first[0].y - first[0].z;
        Assert.That(start, Is.InRange(0.01f, 0.45f));
        Assert.That(end, Is.InRange(0.01f, 0.45f));
        Assert.Greater(Mathf.Abs(start - end), 0.001f, "Outer edges must tilt within tiles instead of following the grid.");
        Assert.That(end, Is.EqualTo(-next[0].z).Within(0.000001f), "Adjacent cells must share the exact contour endpoint.");

        rule.resource.terrainBlendWidth = 0f;
        Assert.AreEqual(0, MapResourceTerrainAppearance.BuildPolygonCuts(cell, mask, 123, rule.resource, first));
    }

    [Test]
    public void PolygonalCoverageVariesBetweenNodesWithoutLosingTheirCenters()
    {
        var rule = Rule(MapResourceShape.Nodes, 1);
        var cuts = new Vector3[MapResourceTerrainAppearance.MaximumPolygonCuts];
        var geometry = new MapResourcePatchGeometry();
        float minimum = 1f, maximum = 0f;
        for (int i = 0; i < 32; i++)
        {
            int count = MapResourceTerrainAppearance.BuildPolygonCuts(new Vector2Int(i, 5), 511, 123, rule.resource, cuts);
            for (int cut = 0; cut < count; cut++)
                Assert.Greater(cuts[cut].x * 0.5f + cuts[cut].y * 0.5f + cuts[cut].z, 0f);
            geometry.Clear();
            geometry.AppendTriangle(Vector3.zero, Vector3.forward, Vector3.right + Vector3.forward, cuts, count);
            geometry.AppendTriangle(Vector3.zero, Vector3.right + Vector3.forward, Vector3.right, cuts, count);
            float area = 0f;
            for (int t = 0; t < geometry.Triangles.Count; t += 3)
            {
                int a = geometry.Triangles[t], b = geometry.Triangles[t + 1], c = geometry.Triangles[t + 2];
                if (geometry.UsesResourceTint[a]) area += Vector3.Cross(geometry.Vertices[b] - geometry.Vertices[a],
                    geometry.Vertices[c] - geometry.Vertices[a]).y * 0.5f;
            }
            Assert.That(area, Is.InRange(0.1f, 0.9f), "Each node must retain a visible footprint inside its tile.");
            minimum = Mathf.Min(minimum, area);
            maximum = Mathf.Max(maximum, area);
        }
        Assert.Greater(maximum - minimum, 0.07f, "Nodes must have visibly different coverage, not repeated tile-sized shapes.");
    }

    [Test]
    public void PolygonalInsetsRemainContinuousAtConcaveJunctions()
    {
        var rule = Rule(MapResourceShape.Deposit, 1);
        var map = new MapResourceMap();
        var cells = new[] { new Vector2Int(4, 6), new Vector2Int(5, 6), new Vector2Int(5, 7) };
        map.Add(rule.resource, cells);
        var first = new Vector3[MapResourceTerrainAppearance.MaximumPolygonCuts];
        var second = new Vector3[MapResourceTerrainAppearance.MaximumPolygonCuts];
        foreach (Vector2Int cell in cells)
            foreach (Vector2Int direction in new[] { Vector2Int.right, Vector2Int.up })
            {
                Vector2Int neighbor = cell + direction;
                if (!map.TryGetSource(neighbor, out _)) continue;
                int a = MapResourceTerrainAppearance.BuildPolygonCuts(cell,
                    MapResourceTerrainAppearance.BoundaryMask(map, cell, rule.resource), 123, rule.resource, first);
                int b = MapResourceTerrainAppearance.BuildPolygonCuts(neighbor,
                    MapResourceTerrainAppearance.BoundaryMask(map, neighbor, rule.resource), 123, rule.resource, second);
                for (int i = 0; i <= 100; i++)
                {
                    float along = i / 100f;
                    Vector2 point = direction == Vector2Int.right ? new Vector2(1f, along) : new Vector2(along, 1f);
                    Vector2 opposite = point - (Vector2)direction;
                    Assert.AreEqual(Inside(first, a, point), Inside(second, b, opposite),
                        "Occupied neighbors must show the same color along their shared edge, including concave turns.");
                }
            }

        bool Inside(Vector3[] planes, int count, Vector2 point)
        {
            for (int i = 0; i < count; i++)
                if (planes[i].x * point.x + planes[i].y * point.y + planes[i].z < -0.000001f) return false;
            return true;
        }
    }

    [Test]
    public void DecorativePrefabOriginsStayOnSmallerPolygonalPatches()
    {
        var rule = Rule(MapResourceShape.Nodes, 1);
        rule.resource.terrainBlendWidth = 0.5f;
        var cuts = new Vector3[MapResourceTerrainAppearance.MaximumPolygonCuts];
        int count = MapResourceTerrainAppearance.BuildPolygonCuts(Vector2Int.zero, 511, 123, rule.resource, cuts);
        bool moved = false;
        for (int x = 0; x <= 10; x++)
            for (int z = 0; z <= 10; z++)
            {
                var original = new Vector2(x / 10f, z / 10f);
                Vector2 constrained = MapResourcePatchGeometry.ConstrainPoint(original, cuts, count);
                moved |= (constrained - original).sqrMagnitude > 0.000001f;
                for (int i = 0; i < count; i++)
                    Assert.GreaterOrEqual(cuts[i].x * constrained.x + cuts[i].y * constrained.y + cuts[i].z, -0.000001f);
            }
        Assert.IsTrue(moved);
        Assert.AreEqual(new Vector2(0.5f, 0.5f), MapResourcePatchGeometry.ConstrainPoint(new Vector2(0.5f, 0.5f), cuts, count));
        Assert.AreEqual(Vector2.zero, MapResourcePatchGeometry.ConstrainPoint(Vector2.zero, cuts, 0));
    }

    [Test]
    public void PolygonClippingPreservesTerrainAreaHeightsAndContourAcrossSubdivisions()
    {
        var rule = Rule(MapResourceShape.Nodes, 1);
        var cuts = new Vector3[MapResourceTerrainAppearance.MaximumPolygonCuts];
        int cutCount = MapResourceTerrainAppearance.BuildPolygonCuts(Vector2Int.zero, 511, 123, rule.resource, cuts);
        float coarseArea = ResourceArea(1);
        Assert.That(ResourceArea(7), Is.EqualTo(coarseArea).Within(0.00001f),
            "The contour must not snap to terrain triangles or change with mesh resolution.");
        Assert.That(coarseArea, Is.InRange(0.1f, 0.9f));

        float ResourceArea(int subdivisions)
        {
            var geometry = new MapResourcePatchGeometry();
            for (int x = 0; x < subdivisions; x++)
                for (int z = 0; z < subdivisions; z++)
                {
                    Vector3 bl = Point(x, z), br = Point(x + 1, z);
                    Vector3 tl = Point(x, z + 1), tr = Point(x + 1, z + 1);
                    geometry.AppendTriangle(bl, tl, tr, cuts, cutCount);
                    geometry.AppendTriangle(bl, tr, br, cuts, cutCount);
                }
            float totalArea = 0f, resourceArea = 0f;
            for (int i = 0; i < geometry.Triangles.Count; i += 3)
            {
                int a = geometry.Triangles[i], b = geometry.Triangles[i + 1], c = geometry.Triangles[i + 2];
                Vector3 pa = geometry.Vertices[a], pb = geometry.Vertices[b], pc = geometry.Vertices[c];
                float area = Vector3.Cross(pb - pa, pc - pa).y * 0.5f;
                Assert.Greater(area, 0f, "Clipping must preserve upward winding and omit degenerate triangles.");
                totalArea += area;
                bool resource = geometry.UsesResourceTint[a];
                Assert.AreEqual(resource, geometry.UsesResourceTint[b]);
                Assert.AreEqual(resource, geometry.UsesResourceTint[c]);
                Vector3 center = (pa + pb + pc) / 3f;
                bool inside = cuts.All(p => p.x * center.x + p.y * center.z + p.z >= -0.00001f);
                if (resource) Assert.IsTrue(inside, "Resource faces must stay inside all contour cuts.");
                else Assert.IsTrue(cuts.Any(p => p.x * center.x + p.y * center.z + p.z <= 0.00001f),
                    "Map faces must remain outside the resource contour.");
                if (resource) resourceArea += area;
            }
            Assert.That(totalArea, Is.EqualTo(1f).Within(0.00001f), "No holes, overlaps or missing terrain.");
            foreach (Vector3 p in geometry.Vertices)
            {
                Assert.That(p.x, Is.InRange(0f, 1f));
                Assert.That(p.z, Is.InRange(0f, 1f));
                Assert.That(p.y, Is.EqualTo(2f * p.x + 3f * p.z).Within(0.00001f));
            }
            return resourceArea;

            Vector3 Point(int x, int z)
            {
                float u = (float)x / subdivisions, v = (float)z / subdivisions;
                return new Vector3(u, 2f * u + 3f * v, v);
            }
        }
    }

    [Test]
    public void PolygonalMeshCutsAcrossTheGridWithPureColorsAndSmoothModeRemainsAvailable()
    {
        var rule = Rule(MapResourceShape.Nodes, 1);
        var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
        temporary.Add(material);
        rule.resource.terrainMaterial = material;
        rule.resource.terrainTint = Color.red;
        rule.resource.terrainTransitionStyle = MapResourceTerrainTransition.Polygonal;
        var map = new MapResourceMap();
        map.Add(rule.resource, new[] { new Vector2Int(2, 2) });
        var manager = new GameObject("Polygonal resource mesh test");
        temporary.Add(manager);
        var world = manager.AddComponent<WorldManager>();
        world.worldSize = 8;
        world.extrusion = 0;
        world.makeCircular = false;
        world.meshSubdivisions = 12;
        world.terrainMaterial = material;
        Color ground = new Color(0.2f, 0.2f, 0.2f);
        Color resource = new Color(0.5f, 0f, 0f);
        world.layers.Add(new TerrainLayer { layerColor = ground });
        world.layers.Add(new TerrainLayer { layerColor = new Color(0.4f, 0.4f, 0.4f) });
        typeof(WorldManager).GetProperty(nameof(WorldManager.ResourceMap)).SetValue(world, map);
        var chunkObject = new GameObject("Polygon mesh test chunk");
        chunkObject.transform.SetParent(manager.transform, false);
        var chunk = chunkObject.AddComponent<ChunkRenderer>();
        chunk.Initialize(world, 0, 0, 4, material);
        chunk.GenerateMesh();
        Mesh mesh = chunkObject.GetComponent<MeshFilter>().sharedMesh;
        var vertices = mesh.vertices;
        var colors = mesh.colors;
        var triangles = mesh.triangles;
        int groundPieces = 0, resourcePieces = 0;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            Vector3 center = (vertices[a] + vertices[b] + vertices[c]) / 3f;
            if (center.x <= 2f || center.x >= 3f || center.z <= 2f || center.z >= 3f) continue;
            Assert.AreEqual(colors[a], colors[b], "Each piece must have a flat color across its vertices.");
            Assert.AreEqual(colors[a], colors[c]);
            Assert.IsTrue(colors[a] == ground || colors[a] == resource, "Do not mix terrain/resource colors inside a piece.");
            if (colors[a] == ground) groundPieces++;
            else resourcePieces++;
        }
        Assert.Greater(groundPieces, 0);
        Assert.Greater(resourcePieces, 0);
        Assert.IsTrue(vertices.Any(p => p.x > 2f && p.x < 3f && p.z > 2f && p.z < 3f &&
            (Mathf.Abs(p.x * 12f - Mathf.Round(p.x * 12f)) > 0.001f ||
             Mathf.Abs(p.z * 12f - Mathf.Round(p.z * 12f)) > 0.001f)),
            "Polygon edges must cut through mesh triangles instead of making a sawtooth along them.");

        rule.resource.terrainTransitionStyle = MapResourceTerrainTransition.Smooth;
        rule.resource.terrainBlendRandomness = 0f;
        chunk.GenerateMesh();
        vertices = mesh.vertices;
        colors = mesh.colors;
        bool intermediate = false;
        for (int i = 0; i < vertices.Length; i++)
            if (vertices[i].x >= 2f && vertices[i].x <= 3f && vertices[i].z >= 2f && vertices[i].z <= 3f &&
                colors[i].g > 0.001f && colors[i].g < 0.199f && colors[i].r > colors[i].g + 0.001f)
                intermediate = true;
        Assert.IsTrue(intermediate, "Smooth mode still supports the previous continuous fade.");
    }

    [Test]
    public void RoundnessClipsConvexCornersIndependentlyOfBlendingAndKeepsSharedEdgesContinuous()
    {
        var rule = Rule(MapResourceShape.Nodes, 1);
        var map = new MapResourceMap();
        map.Add(rule.resource, new[] { Vector2Int.zero });
        int mask = MapResourceTerrainAppearance.BoundaryMask(map, Vector2Int.zero, rule.resource);
        Assert.AreEqual(1f, MapResourceTerrainAppearance.BlendWeight(0.05f, 0.05f, mask, 0f, 0f, 0f));
        Assert.AreEqual(0f, MapResourceTerrainAppearance.BlendWeight(0.05f, 0.05f, mask, 0f, 0f, 0.3f));
        Assert.AreEqual(1f, MapResourceTerrainAppearance.BlendWeight(0.15f, 0.15f, mask, 0f, 0f, 0.3f));
        Assert.AreEqual(1f, MapResourceTerrainAppearance.BlendWeight(0.5f, 0.5f, mask, 0f, 0f, 0.5f));
        map.Add(rule.resource, new[] { Vector2Int.right });
        mask = MapResourceTerrainAppearance.BoundaryMask(map, Vector2Int.zero, rule.resource);
        Assert.AreEqual(1f, MapResourceTerrainAppearance.BlendWeight(1f, 0.05f, mask, 0f, 0f, 0.3f),
            "A corner shared with the same resource must not be cut away.");
    }

    [Test]
    public void ResourceTintRetainsTheMapHeightBandBrightnessRatio()
    {
        var tint = new Color(0.8f, 0.4f, 0.2f);
        Color low = MapResourceTerrainAppearance.ShadeTint(new Color(0.1f, 0.2f, 0.3f), tint, 0.6f);
        Color high = MapResourceTerrainAppearance.ShadeTint(new Color(0.2f, 0.4f, 0.6f), tint, 0.6f);
        Assert.That(high.r, Is.EqualTo(tint.r).Within(0.0001f));
        Assert.That(high.r / low.r, Is.EqualTo(2f).Within(0.0001f));
        Assert.That(high.g / low.g, Is.EqualTo(2f).Within(0.0001f));
        Assert.That(high.b / low.b, Is.EqualTo(2f).Within(0.0001f));
    }

    [Test]
    public void SingleTileResourceRetainsItsTintAtTheLowestMeshSetting()
    {
        var rule = Rule(MapResourceShape.Nodes, 1);
        var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
        temporary.Add(material);
        rule.resource.terrainMaterial = material;
        rule.resource.terrainTint = Color.red;
        var map = new MapResourceMap();
        map.Add(rule.resource, new[] { new Vector2Int(2, 2) });
        var manager = new GameObject("Resource blend mesh test");
        temporary.Add(manager);
        var world = manager.AddComponent<WorldManager>();
        world.worldSize = 8;
        world.extrusion = 0;
        world.makeCircular = false;
        world.meshSubdivisions = 1;
        world.terrainMaterial = material;
        world.layers.Add(new TerrainLayer { layerColor = new Color(0.2f, 0.2f, 0.2f) });
        world.layers.Add(new TerrainLayer { layerColor = new Color(0.4f, 0.4f, 0.4f) });
        typeof(WorldManager).GetProperty(nameof(WorldManager.ResourceMap)).SetValue(world, map);
        var chunkObject = new GameObject("Blend test chunk");
        chunkObject.transform.SetParent(manager.transform, false);
        var chunk = chunkObject.AddComponent<ChunkRenderer>();
        chunk.Initialize(world, 0, 0, 4, material);
        chunk.GenerateMesh();
        Mesh mesh = chunkObject.GetComponent<MeshFilter>().sharedMesh;
        Assert.IsTrue(mesh.colors.Any(color => color.r > color.g + 0.1f),
            "Resource tint must survive even though all four tile corners fade to the map color.");
        Assert.IsTrue(mesh.colors.Any(color => Mathf.Approximately(color.r, 0.2f) &&
            Mathf.Approximately(color.g, 0.2f)), "Unoccupied ground retains its authored color.");
    }

    [Test]
    public void ResourceOutlineUsesSpritePixelsAndOmitsSharedDepositEdges()
    {
        var texture = new Texture2D(32, 32);
        temporary.Add(texture);
        var sprite = Sprite.Create(texture, new Rect(0f, 16f, 16f, 16f), Vector2.one * 0.5f);
        temporary.Add(sprite);
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        MapResourceOutlineGeometry.AppendCell(new Vector2Int(4, 7), 4, sprite, vertices, uvs, indices);
        Assert.AreEqual(4 * 4 * 6, indices.Count);
        for (int i = 0; i < vertices.Count; i++)
        {
            Assert.That(vertices[i].x, Is.InRange(4f, 5f));
            Assert.That(vertices[i].z, Is.InRange(7f, 8f));
            Assert.That(uvs[i].x, Is.InRange(0.5f / 32f, 15.5f / 32f));
            Assert.AreEqual(31.5f / 32f, uvs[i].y);
        }
        for (int i = 0; i < indices.Count; i += 3)
        {
            Vector3 center = (vertices[indices[i]] + vertices[indices[i + 1]] + vertices[indices[i + 2]]) / 3f;
            Assert.IsTrue(center.x <= 4.0625f || center.x >= 4.9375f || center.z <= 7.0625f || center.z >= 7.9375f);
        }
        vertices.Clear(); uvs.Clear(); indices.Clear();
        var footprint = new HashSet<Vector2Int> { new Vector2Int(4, 7), new Vector2Int(5, 7) };
        foreach (var cell in footprint)
            MapResourceOutlineGeometry.AppendCell(cell, 4, sprite, vertices, uvs, indices, footprint);
        Assert.AreEqual(6 * 4 * 6, indices.Count, "Two adjoining cells have six exposed tile edges.");
        for (int i = 0; i < indices.Count; i += 3)
        {
            Vector3 center = (vertices[indices[i]] + vertices[indices[i + 1]] + vertices[indices[i + 2]]) / 3f;
            Assert.IsTrue(center.x <= 4.0625f || center.x >= 5.9375f || center.z <= 7.0625f || center.z >= 7.9375f,
                "No outline strip may remain on the shared interior edge.");
        }
    }

    [Test]
    public void DepositPerimeterIncludesHolesAndEmitsNothingForFullyInteriorTiles()
    {
        var texture = new Texture2D(32, 32);
        temporary.Add(texture);
        var sprite = Sprite.Create(texture, new Rect(0f, 16f, 16f, 16f), Vector2.one * 0.5f);
        temporary.Add(sprite);
        var footprint = new HashSet<Vector2Int>();
        for (int x = 0; x < 3; x++)
            for (int z = 0; z < 3; z++)
                if (x != 1 || z != 1) footprint.Add(new Vector2Int(x, z));
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        foreach (var cell in footprint)
            MapResourceOutlineGeometry.AppendCell(cell, 2, sprite, vertices, uvs, indices, footprint);
        Assert.AreEqual((12 + 4) * 2 * 6, indices.Count, "Outer perimeter plus four edges around the hole.");
        footprint.Add(Vector2Int.one);
        vertices.Clear(); uvs.Clear(); indices.Clear();
        MapResourceOutlineGeometry.AppendCell(Vector2Int.one, 2, sprite, vertices, uvs, indices, footprint);
        Assert.IsEmpty(indices);
        Assert.IsEmpty(vertices);
    }

    [Test]
    public void OutlineKeepsSquareCornersWhenTerrainUsesRoundedCorners()
    {
        var rule = Rule(MapResourceShape.Deposit, 1);
        rule.resource.terrainEdgeRoundness = 0.3f;
        var map = new MapResourceMap();
        map.Add(rule.resource, new[] { Vector2Int.zero });
        int mask = MapResourceTerrainAppearance.BoundaryMask(map, Vector2Int.zero, rule.resource);
        Assert.AreEqual(0f, MapResourceTerrainAppearance.BlendWeight(0.05f, 0.05f, mask, 0f, 0f,
            rule.resource.terrainEdgeRoundness), "Terrain corners retain their configured rounding.");
        var texture = new Texture2D(32, 32);
        temporary.Add(texture);
        var sprite = Sprite.Create(texture, new Rect(0f, 16f, 16f, 16f), Vector2.one * 0.5f);
        temporary.Add(sprite);
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        MapResourceOutlineGeometry.AppendCell(Vector2Int.zero, 8, sprite, vertices, uvs, indices,
            new HashSet<Vector2Int> { Vector2Int.zero });
        Assert.IsNotEmpty(indices);
        foreach (var corner in new[] { Vector3.zero, Vector3.right, Vector3.forward, Vector3.right + Vector3.forward })
            CollectionAssert.Contains(vertices, corner, "Highlights must retain each square footprint corner.");
        foreach (var vertex in vertices)
        {
            Assert.That(vertex.x, Is.InRange(-0.0001f, 1.0001f));
            Assert.That(vertex.z, Is.InRange(-0.0001f, 1.0001f));
        }
    }

    [TestCase(MapResourceShape.Nodes, MapResourceDepth.Overground, true)]
    [TestCase(MapResourceShape.Nodes, MapResourceDepth.Deep, true)]
    [TestCase(MapResourceShape.Deposit, MapResourceDepth.Overground, true)]
    [TestCase(MapResourceShape.Deposit, MapResourceDepth.Underground, false)]
    [TestCase(MapResourceShape.Deposit, MapResourceDepth.Deep, false)]
    public void BuildingAndRoadPlacementUseSourceConstructionRules(MapResourceShape shape, MapResourceDepth depth, bool blocked)
    {
        var rule = Rule(shape, 1);
        rule.resource.depth = depth;
        var map = Generate(new[] { rule });
        var managers = new GameObject("Resource placement test");
        temporary.Add(managers);
        var world = managers.AddComponent<WorldManager>();
        world.worldSize = Size;
        world.extrusion = 0;
        world.makeCircular = false;
        world.layers.Add(new TerrainLayer());
        typeof(WorldManager).GetProperty(nameof(WorldManager.ResourceMap)).SetValue(world, map);
        var buildings = managers.AddComponent<BuildingSystemBackend>();
        var roads = managers.AddComponent<RoadSystemBackend>();
        foreach (var component in new MonoBehaviour[] { world, buildings, roads })
            component.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
        var roadType = ScriptableObject.CreateInstance<RoadType>();
        temporary.Add(roadType);
        Vector2Int cell = map.Sources[0].Cells[0];
        bool[] validity = new bool[1];
        Assert.AreEqual(!blocked, world.IsBuildableCell(cell.x, cell.y));
        Assert.AreEqual(!blocked, buildings.CanPlaceBuilding(world, cell,
            new[] { new BuildingTile { localPosition = Vector2Int.zero } }, validity, out _));
        Assert.AreEqual(!blocked, validity[0]);
        Assert.AreEqual(!blocked, roads.CanPlaceRoadTypeAt(cell, roadType));
        world.ClearWorld();
        Assert.IsEmpty(world.ResourceMap.Sources);
        Assert.IsTrue(world.IsBuildableCell(cell.x, cell.y));
    }

    private MapResourceSpawnSettings Rule(MapResourceShape shape, int count)
    {
        var definition = ScriptableObject.CreateInstance<MapResourceDefinition>();
        temporary.Add(definition);
        definition.displayName = shape.ToString();
        definition.shape = shape;
        return new MapResourceSpawnSettings
        {
            resource = definition, sourceCount = new Vector2Int(count, count),
            depositTiles = new Vector2Int(6, 6), totalUnits = new Vector2Int(1000, 1000)
        };
    }

    private static MapResourceMap Generate(IReadOnlyList<MapResourceSpawnSettings> rules,
        Func<Vector2Int, ResourceTerrainCell> sample = null, string seed = "Resource test", bool circular = false)
    {
        if (sample == null) sample = cell => new ResourceTerrainCell(
            cell.x > 0 && cell.y > 0 && cell.x < Size - 1 && cell.y < Size - 1, 0f);
        var map = new MapResourceMap();
        var work = MapResourceGenerator.Generate(Size, circular, seed, rules, sample, map);
        int yields = 0;
        while (work.MoveNext()) Assert.Less(++yields, 100000, "Generation must remain bounded.");
        return map;
    }

    private static void AssertConnected(IReadOnlyList<Vector2Int> cells)
    {
        var remaining = new HashSet<Vector2Int>(cells);
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(cells[0]); remaining.Remove(cells[0]);
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var neighbor in new[] { cell + Vector2Int.right, cell + Vector2Int.up, cell + Vector2Int.left, cell + Vector2Int.down })
                if (remaining.Remove(neighbor)) queue.Enqueue(neighbor);
        }
        Assert.IsEmpty(remaining);
    }
}
