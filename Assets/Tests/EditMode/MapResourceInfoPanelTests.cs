using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class MapResourceInfoPanelTests
{
    private readonly List<Object> temporary = new List<Object>();
    private WorldManager world;
    private PlayerActionManager actions;
    private PlayerViewModeManager views;

    [SetUp]
    public void SetUp()
    {
        var managers = Track(new GameObject("Resource selection tests"));
        world = managers.AddComponent<WorldManager>();
        actions = managers.AddComponent<PlayerActionManager>();
        views = managers.AddComponent<PlayerViewModeManager>();
        Invoke(world, "Awake");
        Invoke(actions, "Awake");
        Invoke(views, "Awake");
        Invoke(actions, "Start");
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = temporary.Count - 1; i >= 0; i--)
            if (temporary[i] != null) Object.DestroyImmediate(temporary[i]);
        temporary.Clear();
    }

    [TestCase(MapResourceShape.Nodes)]
    [TestCase(MapResourceShape.Deposit)]
    public void FiniteSourcesStartFullAndDepositTilesShareTheSameQuantity(MapResourceShape shape)
    {
        var source = Generate(shape);
        Assert.AreEqual(12500, source.GeneratedUnits);
        Assert.AreEqual(source.GeneratedUnits, source.RemainingUnits);
        foreach (var cell in source.Cells)
        {
            Assert.IsTrue(world.ResourceMap.TryGetSource(cell, out var same));
            Assert.AreSame(source, same);
        }
        Assert.AreEqual("12,500 / 12,500", MapResourceInfoPanel.FormatQuantity(source));
    }

    [Test]
    public void RemainingQuantityIsBoundedAndTotalSurvivesDepletion()
    {
        var source = Generate();
        SetRemaining(source, 2345);
        Assert.AreEqual("2,345 / 12,500", MapResourceInfoPanel.FormatQuantity(source));
        SetRemaining(source, -10);
        Assert.AreEqual("0 / 12,500", MapResourceInfoPanel.FormatQuantity(source));
        SetRemaining(source, long.MaxValue);
        Assert.AreEqual(12500, source.RemainingUnits);
    }

    [Test]
    public void UnlimitedSupplyHasExplicitPresentationInsteadOfZeroRemaining()
    {
        var source = Generate(unlimited: true);
        SetRemaining(source, 2345);
        Assert.AreEqual(0, source.GeneratedUnits);
        Assert.AreEqual(0, source.RemainingUnits);
        Assert.AreEqual("Unlimited", MapResourceInfoPanel.FormatQuantity(source));
    }

    [Test]
    public void SelectionReopensOnRepeatedClicksAndIsExclusiveWithBuildingSelection()
    {
        var source = Generate();
        var building = Track(new GameObject("Building")).AddComponent<Building>();
        int notifications = 0;
        actions.ResourceSelectionChanged += Count;
        try
        {
            actions.SelectBuilding(building);
            actions.SelectResource(source);
            Assert.IsNull(actions.SelectedBuilding);
            actions.SelectResource(source);
            Assert.AreEqual(2, notifications);
            actions.SelectBuilding(building);
            Assert.IsNull(actions.SelectedResource);
            Assert.AreSame(building, actions.SelectedBuilding);
        }
        finally { actions.ResourceSelectionChanged -= Count; }
        void Count(MapResourceSource selected) { if (selected != null) notifications++; }
    }

    [Test]
    public void ToolAndViewChangesClearSelectionAndRejectNewSelections()
    {
        var source = Generate();
        actions.SelectResource(source);
        actions.SetModeBuildRoad();
        Assert.IsNull(actions.SelectedResource);
        actions.SelectResource(source);
        Assert.IsNull(actions.SelectedResource);
        actions.SetModeNone();
        actions.SelectResource(source);
        views.SetOverviewView();
        Assert.IsNull(actions.SelectedResource);
        actions.SelectResource(source);
        Assert.IsNull(actions.SelectedResource);
    }

    [Test]
    public void WorldRegenerationClearsSelectionAndRejectsAStaleSourceWithTheSameId()
    {
        var old = Generate();
        actions.SelectResource(old);
        world.ClearWorld();
        Assert.IsNull(actions.SelectedResource);
        var replacement = Generate();
        Assert.AreEqual(old.Id, replacement.Id);
        actions.SelectResource(old);
        Assert.IsNull(actions.SelectedResource);
        actions.SelectResource(replacement);
        Assert.AreSame(replacement, actions.SelectedResource);
    }

    [TestCase(MapResourceShape.Deposit, "Deposit")]
    [TestCase(MapResourceShape.Nodes, "Node")]
    public void AuthoredControlsUpdateOnQuantityEventsAndCloseCanBeReopened(MapResourceShape shape, string expectedKind)
    {
        var source = Generate(shape);
        var root = Track(new GameObject("Panel", typeof(RectTransform)));
        root.SetActive(false);
        var panel = root.AddComponent<MapResourceInfoPanel>();
        var card = Child(root.transform, "Card");
        var name = Child(card, "Name").gameObject.AddComponent<TextMeshProUGUI>();
        var quantity = Child(card, "Quantity").gameObject.AddComponent<TextMeshProUGUI>();
        var kind = Child(card, "Kind").gameObject.AddComponent<TextMeshProUGUI>();
        var types = Child(card, "Types").gameObject.AddComponent<TextMeshProUGUI>();
        var quality = Child(card, "Quality").gameObject.AddComponent<TextMeshProUGUI>();
        var description = Child(card, "Description").gameObject.AddComponent<TextMeshProUGUI>();
        var catalog = Catalog(source.Definition.resourceType);
        SetField(panel, "playerActions", actions);
        SetField(panel, "card", card);
        SetField(panel, "resourceName", name);
        SetField(panel, "quantity", quantity);
        SetField(panel, "sourceKind", kind);
        SetField(panel, "resourceTypes", types);
        SetField(panel, "quality", quality);
        SetField(panel, "description", description);
        SetField(panel, "resourceCatalog", catalog);
        Invoke(panel, "OnEnable");
        try
        {
            actions.SelectResource(source);
            Assert.IsTrue(panel.IsVisible);
            Assert.AreEqual("Copper", name.text);
            Assert.AreEqual(expectedKind, kind.text);
            Assert.AreEqual("Metal, Liquid", types.text);
            Assert.AreEqual("—", quality.text);
            Assert.AreEqual("Catalog resource description.", description.text);
            SetRemaining(source, 2500);
            Assert.AreEqual("2,500 / 12,500", quantity.text);
            panel.Close();
            Assert.IsFalse(panel.IsVisible);
            Assert.AreSame(source, actions.SelectedResource);
            actions.SelectResource(source);
            Assert.IsTrue(panel.IsVisible);
            actions.SelectBuilding(null);
            Assert.IsFalse(panel.IsVisible);
            SetRemaining(source, 1000);
            Assert.AreEqual("2,500 / 12,500", quantity.text, "Closed panels release their source subscription.");
        }
        finally { Invoke(panel, "OnDisable"); }
    }

    [Test]
    public void CatalogMetadataUsesTypeDisplayNamesAndResourceDescription()
    {
        var resource = (ResourceType)2048;
        var catalog = Catalog(resource);
        catalog.Find(resource).types.Add("metal");
        catalog.Find(resource).types.Add("missing");
        Assert.AreEqual("Metal, Liquid", MapResourceInfoPanel.FormatTypes(resource, catalog));
        Assert.AreEqual("Catalog resource description.", MapResourceInfoPanel.FormatDescription(resource, catalog));
        Assert.AreEqual("Unclassified", MapResourceInfoPanel.FormatTypes(ResourceType.Oil, catalog));
        Assert.AreEqual("No description available.", MapResourceInfoPanel.FormatDescription(ResourceType.Oil, catalog));
        catalog.Find(resource).types = null;
        catalog.Find(resource).description = "  ";
        Assert.AreEqual("Unclassified", MapResourceInfoPanel.FormatTypes(resource, catalog));
        Assert.AreEqual("No description available.", MapResourceInfoPanel.FormatDescription(resource, catalog));
        Assert.AreEqual("Unclassified", MapResourceInfoPanel.FormatTypes(resource, null));
        Assert.AreEqual("No description available.", MapResourceInfoPanel.FormatDescription(resource, null));
    }

    private ResourceCatalog Catalog(ResourceType resource)
    {
        var catalog = Track(ScriptableObject.CreateInstance<ResourceCatalog>());
        catalog.types.Add(new ResourceCategory { id = "metal", displayName = "Metal" });
        catalog.types.Add(new ResourceCategory { id = "liquid", displayName = "Liquid" });
        catalog.resources.Add(new ResourceDefinition
        {
            resource = resource,
            types = new List<string> { "metal", "liquid" },
            description = " Catalog resource description. "
        });
        return catalog;
    }

    private MapResourceSource Generate(MapResourceShape shape = MapResourceShape.Deposit, bool unlimited = false)
    {
        var definition = Track(ScriptableObject.CreateInstance<MapResourceDefinition>());
        definition.displayName = "Copper";
        definition.shape = shape;
        definition.unlimited = unlimited;
        var rule = new MapResourceSpawnSettings
        {
            resource = definition,
            sourceCount = Vector2Int.one,
            sourcesPerThousandTiles = 100f,
            depositTiles = new Vector2Int(4, 4),
            totalUnits = new Vector2Int(12500, 12500)
        };
        var map = new MapResourceMap();
        var work = MapResourceGenerator.Generate(16, false, "Resource UI", new[] { rule },
            cell => new ResourceTerrainCell(true, 0f), map);
        int steps = 0;
        while (work.MoveNext()) Assert.Less(++steps, 10000);
        Assert.AreEqual(1, map.Sources.Count);
        typeof(WorldManager).GetProperty(nameof(WorldManager.ResourceMap)).SetValue(world, map);
        return map.Sources[0];
    }

    private T Track<T>(T obj) where T : Object { temporary.Add(obj); return obj; }
    private static void SetRemaining(MapResourceSource source, long units) =>
        typeof(MapResourceSource).GetProperty(nameof(MapResourceSource.RemainingUnits)).SetValue(source, units);
    private static void SetField(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    private static RectTransform Child(Transform parent, string name)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return obj.GetComponent<RectTransform>();
    }
}
