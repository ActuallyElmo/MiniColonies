using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class ResourceCatalogTests
{
    private ResourceCatalog catalog;

    [SetUp]
    public void SetUp()
    {
        catalog = ScriptableObject.CreateInstance<ResourceCatalog>();
        ResourceCatalogDefaults.PopulateEmpty(catalog);
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(catalog);

    [Test]
    public void StarterCatalogHasAtLeastFiftyAdditionalResourcesAndCoversEveryIdentity()
    {
        var errors = new List<string>();
        catalog.GetValidationErrors(errors);
        Assert.IsEmpty(errors);
        Assert.GreaterOrEqual(catalog.resources.Count - 8, 50);
        CollectionAssert.AreEquivalent(Enum.GetValues(typeof(ResourceType)), catalog.resources.Select(r => r.resource));
        Assert.IsTrue(catalog.resources.Any(r => r.availableFrom == ResourceOrigin.World));
        Assert.IsTrue(catalog.resources.Any(r => r.availableFrom == ResourceOrigin.Factory));
        Assert.IsTrue(catalog.resources.Any(r => r.availableFrom == (ResourceOrigin.World | ResourceOrigin.Factory)));
    }

    [TestCase(ResourceType.GoldOre)]
    [TestCase(ResourceType.IronOre)]
    [TestCase(ResourceType.NickelOre)]
    [TestCase(ResourceType.AluminumOre)]
    [TestCase(ResourceType.CopperOre)]
    [TestCase(ResourceType.ZincOre)]
    public void OresShareAMetalOreType(ResourceType resource)
    {
        Assert.IsTrue(catalog.HasType(resource, "metal-ore"));
        Assert.IsFalse(catalog.HasType(resource, "metal"));
    }

    [Test]
    public void ResourcesSupportOverlappingTypesWithoutConflatingTheirIdentity()
    {
        Assert.IsTrue(catalog.HasType(ResourceType.Coal, "fuel"));
        Assert.IsTrue(catalog.HasType(ResourceType.Wood, "fuel"));
        Assert.IsTrue(catalog.HasType(ResourceType.Oil, "fuel"));
        Assert.IsTrue(catalog.HasType(ResourceType.Oil, "liquid"));
        Assert.IsFalse(catalog.HasType(ResourceType.Oil, "coolant"));
        Assert.IsTrue(catalog.HasType(ResourceType.Water, "liquid"));
        Assert.IsTrue(catalog.HasType(ResourceType.Water, "coolant"));
        Assert.IsFalse(catalog.HasType(ResourceType.Water, "fuel"));
    }

    [Test]
    public void TypeFiltersRequireEveryTypeAndRejectEmptyOrBrokenFilters()
    {
        Assert.IsTrue(catalog.HasAllTypes(ResourceType.Oil, new[] { "fuel", "liquid" }));
        Assert.IsFalse(catalog.HasAllTypes(ResourceType.Coal, new[] { "fuel", "liquid" }));
        Assert.IsFalse(catalog.HasAllTypes(ResourceType.Water, new[] { "fuel", "liquid" }));
        Assert.IsFalse(catalog.HasAllTypes(ResourceType.Oil, Array.Empty<string>()));
        Assert.IsFalse(catalog.HasAllTypes(ResourceType.Oil, null));
        Assert.IsFalse(catalog.HasAllTypes(ResourceType.Oil, new[] { "fuel", "missing" }));
        var results = new List<ResourceType>();
        catalog.GetResourcesMatchingTypes(new[] { "fuel", "liquid" }, results);
        CollectionAssert.AreEquivalent(new[] { ResourceType.Oil, ResourceType.Gasoline, ResourceType.Diesel }, results);
    }

    [Test]
    public void ResourceIconsUseAnOverrideOrTheSharedPlaceholder()
    {
        var texture = new Texture2D(2, 2);
        var placeholder = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
        var specific = Sprite.Create(texture, new Rect(1, 1, 1, 1), Vector2.zero);
        try
        {
            catalog.fallbackIcon = placeholder;
            catalog.Find(ResourceType.Gold).icon = specific;
            Assert.AreSame(specific, catalog.GetResourceIcon(ResourceType.Gold));
            Assert.AreSame(placeholder, catalog.GetResourceIcon(ResourceType.Zinc));
            catalog.Find(ResourceType.Gold).icon = null;
            Assert.AreSame(placeholder, catalog.GetResourceIcon(ResourceType.Gold));
        }
        finally
        {
            Object.DestroyImmediate(specific);
            Object.DestroyImmediate(placeholder);
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void WorldTileIconIsIndependentOfUiIconAndFallsBackThroughUiToPlaceholder()
    {
        var texture = new Texture2D(3, 1);
        var placeholder = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
        var uiIcon = Sprite.Create(texture, new Rect(1, 0, 1, 1), Vector2.zero);
        var worldIcon = Sprite.Create(texture, new Rect(2, 0, 1, 1), Vector2.zero);
        try
        {
            catalog.fallbackIcon = placeholder;
            ResourceDefinition resource = catalog.AddResource("Custom map resource");
            resource.icon = uiIcon;
            resource.worldTileIcon = worldIcon;
            Assert.AreSame(worldIcon, catalog.GetResourceWorldTileIcon(resource.resource));
            Assert.AreSame(uiIcon, catalog.GetResourceIcon(resource.resource), "World tile styling cannot change factory/UI icons.");
            resource.worldTileIcon = null;
            Assert.AreSame(uiIcon, catalog.GetResourceWorldTileIcon(resource.resource));
            resource.icon = null;
            Assert.AreSame(placeholder, catalog.GetResourceWorldTileIcon(resource.resource));
            Assert.AreSame(placeholder, catalog.GetResourceWorldTileIcon((ResourceType)(-1)));
            catalog.fallbackIcon = null;
            Assert.IsNull(catalog.GetResourceWorldTileIcon(resource.resource));
        }
        finally
        {
            Object.DestroyImmediate(worldIcon);
            Object.DestroyImmediate(uiIcon);
            Object.DestroyImmediate(placeholder);
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void TypeIconsUseTheirOwnOverridesAndTheSharedPlaceholder()
    {
        var texture = new Texture2D(2, 2);
        var placeholder = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
        var metalIcon = Sprite.Create(texture, new Rect(1, 1, 1, 1), Vector2.zero);
        try
        {
            catalog.fallbackIcon = placeholder;
            catalog.FindType("metal").icon = metalIcon;
            Assert.AreSame(metalIcon, catalog.GetTypeIcon("metal"));
            Assert.AreSame(placeholder, catalog.GetTypeIcon("fuel"));
            Assert.AreSame(placeholder, catalog.GetTypeIcon("missing"));
            Assert.AreSame(placeholder, catalog.GetTypeIcon(null));
            Assert.AreSame(placeholder, catalog.GetResourceIcon(ResourceType.Gold),
                "A type icon must not replace the chosen item's icon.");
            catalog.FindType("metal").icon = null;
            Assert.AreSame(placeholder, catalog.GetTypeIcon("metal"));
        }
        finally
        {
            Object.DestroyImmediate(metalIcon);
            Object.DestroyImmediate(placeholder);
            Object.DestroyImmediate(texture);
        }
    }

    [Test]
    public void CustomTypeAndMembershipChangesAreImmediatelyQueryable()
    {
        catalog.types.Add(new ResourceCategory { id = "custom-conductor", displayName = "Conductor" });
        catalog.Find(ResourceType.Copper).types.Add("custom-conductor");
        catalog.Find(ResourceType.Gold).types.Add("custom-conductor");
        var results = new List<ResourceType> { ResourceType.Wood };

        catalog.GetResourcesWithType("custom-conductor", results);
        CollectionAssert.AreEqual(new[] { ResourceType.Copper, ResourceType.Gold }, results);

        catalog.Find(ResourceType.Copper).types.Remove("custom-conductor");
        Assert.IsFalse(catalog.HasType(ResourceType.Copper, "custom-conductor"));
        catalog.GetResourcesWithType("custom-conductor", results);
        CollectionAssert.AreEqual(new[] { ResourceType.Gold }, results);
        catalog.types.RemoveAt(catalog.types.Count - 1);
        Assert.IsFalse(catalog.HasType(ResourceType.Gold, "custom-conductor"));
    }

    [Test]
    public void MissingOrMalformedDataFailsSafelyAndReportsAuthoringErrors()
    {
        catalog.types.Add(new ResourceCategory { id = "fuel", displayName = "Duplicate fuel" });
        catalog.types.Add(null);
        catalog.resources.Add(null);
        catalog.resources.Add(new ResourceDefinition { resource = (ResourceType)(-1), availableFrom = (ResourceOrigin)8 });
        catalog.resources.Add(catalog.Find(ResourceType.Oil));
        catalog.Find(ResourceType.Water).types.Add("missing");
        catalog.Find(ResourceType.Water).types.Add("liquid");
        catalog.Find(ResourceType.Wood).types = null;
        var errors = new List<string>();

        catalog.GetValidationErrors(errors);
        Assert.IsTrue(errors.Any(e => e.Contains("Duplicate type ID")));
        Assert.IsTrue(errors.Any(e => e.Contains("Duplicate resource")));
        Assert.IsTrue(errors.Any(e => e.Contains("negative identity")));
        Assert.IsTrue(errors.Any(e => e.Contains("unknown type")));
        Assert.IsTrue(errors.Any(e => e.Contains("duplicate type")));
        Assert.IsTrue(errors.Any(e => e.Contains("origins")));
        Assert.IsNull(catalog.Find((ResourceType)999));
        Assert.IsFalse(catalog.HasType(ResourceType.Water, "missing"));
        Assert.IsFalse(catalog.HasType(ResourceType.Wood, "fuel"));
        Assert.IsFalse(catalog.HasType(ResourceType.Oil, null));

        var results = new List<ResourceType>();
        catalog.GetResourcesWithType("fuel", results);
        Assert.AreEqual(1, results.Count(r => r == ResourceType.Oil));
        catalog.GetResourcesWithType("missing", results);
        Assert.IsEmpty(results);
        catalog.resources = null;
        catalog.types = null;
        Assert.IsFalse(catalog.HasType(ResourceType.Oil, "fuel"));
        catalog.GetResourcesWithType("fuel", results);
        Assert.IsEmpty(results);
        Assert.AreEqual("Iron Ore", catalog.GetResourceName(ResourceType.IronOre));
        Assert.AreEqual("Unknown resource", catalog.GetResourceName((ResourceType)999));
    }

    [Test]
    public void DefaultPopulationRefusesToOverwriteAuthoredEdits()
    {
        catalog.Find(ResourceType.Water).displayName = "Fresh water";
        catalog.Find(ResourceType.Water).types.Clear();
        Assert.Throws<InvalidOperationException>(() => ResourceCatalogDefaults.PopulateEmpty(catalog));
        Assert.AreEqual("Fresh water", catalog.GetResourceName(ResourceType.Water));
        Assert.IsEmpty(catalog.Find(ResourceType.Water).types);
    }

    [Test]
    public void CatalogMetadataAndCustomTypesSurviveSerialization()
    {
        catalog.types.Add(new ResourceCategory { id = "custom", displayName = "Custom type" });
        catalog.Find(ResourceType.Water).types.Add("custom");
        catalog.Find(ResourceType.Water).displayName = "Fresh water";
        var restored = ScriptableObject.CreateInstance<ResourceCatalog>();
        try
        {
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(catalog), restored);
            Assert.AreEqual("Fresh water", restored.GetResourceName(ResourceType.Water));
            Assert.IsTrue(restored.HasType(ResourceType.Water, "custom"));
            Assert.IsTrue(restored.HasType(ResourceType.Water, "coolant"));
        }
        finally { Object.DestroyImmediate(restored); }
    }

    [Test]
    public void NewResourcesAndTypesNeedNoEnumEntriesAndKeepTheirIdsWhenRenamed()
    {
        ResourceCategory type = catalog.AddType("Conductor");
        ResourceDefinition resource = catalog.AddResource("Graphite");
        resource.types.Add(type.id);
        resource.types.Add("mineral");
        ResourceType id = resource.resource;
        string typeId = type.id;
        Assert.IsFalse(Enum.IsDefined(typeof(ResourceType), id));
        Assert.IsTrue(ResourceCatalog.IsKnownResource(id, catalog));
        Assert.IsFalse(ResourceCatalog.IsKnownResource((ResourceType)(-1), catalog));
        Assert.IsFalse(ResourceCatalog.IsKnownResource((ResourceType)int.MaxValue, catalog));
        Assert.IsTrue(ResourceCatalog.IsKnownResource(ResourceType.Wood, null));
        Assert.IsFalse(ResourceCatalog.IsKnownResource(id, null));
        Assert.IsTrue(catalog.HasAllTypes(id, new[] { typeId, "mineral" }));
        var matches = new List<ResourceType>();
        catalog.GetResourcesMatchingTypes(new[] { typeId }, matches);
        CollectionAssert.AreEqual(new[] { id }, matches);

        resource.displayName = "Industrial graphite";
        type.displayName = "Electrical conductor";
        Assert.AreEqual("Industrial graphite", catalog.GetResourceName(id));
        Assert.AreSame(resource, catalog.Find(id));
        Assert.AreSame(type, catalog.FindType(typeId));
        Assert.IsTrue(catalog.HasType(id, typeId));
        var errors = new List<string>();
        catalog.GetValidationErrors(errors);
        Assert.IsEmpty(errors);
    }

    [Test]
    public void ResourceIdsSurviveReorderingSerializationAndNeverReuseRetiredIds()
    {
        ResourceDefinition first = catalog.AddResource("First custom resource");
        ResourceDefinition second = catalog.AddResource("Second custom resource");
        ResourceCategory type = catalog.AddType("Custom category");
        first.types.Add(type.id);
        catalog.resources.Reverse();
        catalog.resources.Remove(second);
        catalog.types.Remove(type);
        var restored = ScriptableObject.CreateInstance<ResourceCatalog>();
        var map = ScriptableObject.CreateInstance<MapResourceDefinition>();
        try
        {
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(catalog), restored);
            Assert.AreEqual(first.displayName, restored.Find(first.resource).displayName);
            Assert.IsNull(restored.Find(second.resource));
            ResourceDefinition next = restored.AddResource("Third custom resource");
            Assert.Greater((int)next.resource, (int)second.resource);
            Assert.AreNotEqual(type.id, restored.AddType("Another category").id);
            ResourceAmount amount = JsonUtility.FromJson<ResourceAmount>(JsonUtility.ToJson(
                new ResourceAmount { resourceType = first.resource, amount = 17 }));
            Assert.AreEqual(first.resource, amount.resourceType);
            Assert.AreEqual(17, amount.amount);
            map.resourceType = first.resource;
            string mapJson = JsonUtility.ToJson(map);
            map.resourceType = ResourceType.Wood;
            JsonUtility.FromJsonOverwrite(mapJson, map);
            Assert.AreEqual(first.resource, map.resourceType);
        }
        finally
        {
            Object.DestroyImmediate(map);
            Object.DestroyImmediate(restored);
        }
    }

    [Test]
    public void CreationRejectsBlankNamesAndAvoidsImportedIdCollisions()
    {
        int resourceCount = catalog.resources.Count;
        int typeCount = catalog.types.Count;
        Assert.Throws<ArgumentException>(() => catalog.AddResource(" "));
        Assert.Throws<ArgumentException>(() => catalog.AddType(null));
        Assert.AreEqual(resourceCount, catalog.resources.Count);
        Assert.AreEqual(typeCount, catalog.types.Count);
        catalog.resources.Add(new ResourceDefinition { resource = (ResourceType)7000, displayName = "Imported" });
        Assert.AreEqual((ResourceType)7001, catalog.AddResource("New").resource);
        catalog.types.Add(new ResourceCategory { id = "custom-type-1", displayName = "Imported type" });
        Assert.AreEqual("custom-type-2", catalog.AddType("New type").id);
        catalog.resources.Add(new ResourceDefinition { resource = (ResourceType)int.MaxValue });
        Assert.Throws<InvalidOperationException>(() => catalog.AddResource("Exhausted"));
    }

    [Test]
    public void ExistingSerializedResourceAmountsKeepTheirMeanings()
    {
        var legacy = new[] { ResourceType.IronOre, ResourceType.CopperOre, ResourceType.Wood,
            ResourceType.Steel, ResourceType.Oil, ResourceType.Water, ResourceType.Carbon, ResourceType.Concrete };
        for (int i = 0; i < legacy.Length; i++)
        {
            ResourceAmount restored = JsonUtility.FromJson<ResourceAmount>("{\"resourceType\":" + i + ",\"amount\":12}");
            Assert.AreEqual(legacy[i], restored.resourceType);
            Assert.AreEqual(12, restored.amount);
        }
    }

    [Test]
    public void InstalledCatalogIsAvailableInPlayersAndContainsEveryStarterIdentity()
    {
        ResourceCatalog installed = Resources.Load<ResourceCatalog>(ResourceCatalog.DefaultResourcePath);
        Assert.IsNotNull(installed, "The default catalog must be shipped under Assets/Resources.");
        var errors = new List<string>();
        installed.GetValidationErrors(errors);
        Assert.IsEmpty(errors);
        CollectionAssert.IsSubsetOf(Enum.GetValues(typeof(ResourceType)), installed.resources.Select(r => r.resource).ToArray());
        foreach (ResourceDefinition definition in installed.resources) Assert.IsNotNull(installed.GetResourceIcon(definition.resource));
        foreach (ResourceCategory type in installed.types) Assert.IsNotNull(installed.GetTypeIcon(type.id));
    }
}
