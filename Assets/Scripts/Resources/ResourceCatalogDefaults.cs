using System;
using System.Collections.Generic;

/// <summary>Starter content only. Authored catalog assets own all live metadata and memberships.</summary>
public static class ResourceCatalogDefaults
{
    // Stable ID | display name. Types are data, so designers can add more without code changes.
    private const string TypeRows = @"
metal-ore|Metal ore
metal|Metal
alloy|Alloy
fuel|Fuel
liquid|Liquid
gas|Gas
coolant|Coolant
mineral|Mineral
construction|Construction material
organic|Organic material
agricultural|Agricultural material
chemical|Chemical
polymer|Polymer
textile|Textile
food|Food
component|Component
manufactured|Manufactured goods
nuclear|Nuclear material";

    // Identity | display name | origins (World, Factory or both) | comma-separated type IDs.
    private const string ResourceRows = @"
IronOre|Iron ore|World|metal-ore
CopperOre|Copper ore|World|metal-ore
Wood|Wood|World|fuel,organic
Steel|Steel|Factory|metal,alloy,construction
Oil|Oil|World|fuel,liquid
Water|Water|World, Factory|liquid,coolant
Carbon|Carbon|World, Factory|mineral,chemical
Concrete|Concrete|Factory|construction
GoldOre|Gold ore|World|metal-ore
NickelOre|Nickel ore|World|metal-ore
AluminumOre|Aluminum ore|World|metal-ore
ZincOre|Zinc ore|World|metal-ore
Coal|Coal|World|fuel,mineral
Stone|Stone|World|mineral,construction
Limestone|Limestone|World|mineral,construction
Sand|Sand|World|mineral,construction
Clay|Clay|World|mineral,construction
Salt|Salt|World, Factory|mineral,chemical
Sulfur|Sulfur|World, Factory|mineral,chemical
PhosphateRock|Phosphate rock|World|mineral,chemical
UraniumOre|Uranium ore|World|metal-ore,nuclear
LithiumOre|Lithium ore|World|metal-ore
TinOre|Tin ore|World|metal-ore
LeadOre|Lead ore|World|metal-ore
SilverOre|Silver ore|World|metal-ore
ChromiumOre|Chromium ore|World|metal-ore
TitaniumOre|Titanium ore|World|metal-ore
Quartz|Quartz|World|mineral
NaturalGas|Natural gas|World|fuel,gas
RubberLatex|Rubber latex|World|liquid,organic,polymer
Cotton|Cotton|World|agricultural,organic,textile
Grain|Grain|World|agricultural,organic,food
Iron|Iron|Factory|metal,construction
Copper|Copper|Factory|metal
Gold|Gold|Factory|metal
Nickel|Nickel|Factory|metal
Aluminum|Aluminum|Factory|metal,construction
Zinc|Zinc|Factory|metal
Tin|Tin|Factory|metal
Lead|Lead|Factory|metal
Silver|Silver|Factory|metal
Titanium|Titanium|Factory|metal
Bronze|Bronze|Factory|metal,alloy
Brass|Brass|Factory|metal,alloy
StainlessSteel|Stainless steel|Factory|metal,alloy,construction
Charcoal|Charcoal|Factory|fuel
Coke|Coke|Factory|fuel
Gasoline|Gasoline|Factory|fuel,liquid
Diesel|Diesel|Factory|fuel,liquid
Lubricant|Lubricant|Factory|liquid,chemical
Plastic|Plastic|Factory|polymer,chemical
Rubber|Rubber|Factory|polymer
Glass|Glass|Factory|construction
Cement|Cement|Factory|construction
Bricks|Bricks|Factory|construction
Lumber|Lumber|Factory|construction,organic,fuel
Paper|Paper|Factory|organic,manufactured
Chemicals|Chemicals|Factory|chemical
Fertilizer|Fertilizer|Factory|chemical,agricultural
Textiles|Textiles|Factory|textile,manufactured
Food|Food|Factory|food,manufactured
ElectronicComponents|Electronic components|Factory|component,manufactured
Machinery|Machinery|Factory|manufactured
Batteries|Batteries|Factory|component,manufactured";

    public static void PopulateEmpty(ResourceCatalog catalog)
    {
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));
        if ((catalog.types != null && catalog.types.Count != 0) ||
            (catalog.resources != null && catalog.resources.Count != 0))
            throw new InvalidOperationException("Defaults can only populate an empty catalog; authored edits must be preserved.");

        catalog.types = new List<ResourceCategory>();
        foreach (string row in TypeRows.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = row.Split('|');
            catalog.types.Add(new ResourceCategory { id = fields[0], displayName = fields[1] });
        }
        catalog.resources = new List<ResourceDefinition>();
        foreach (string row in ResourceRows.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = row.Split('|');
            catalog.resources.Add(new ResourceDefinition
            {
                resource = (ResourceType)Enum.Parse(typeof(ResourceType), fields[0]),
                displayName = fields[1],
                availableFrom = (ResourceOrigin)Enum.Parse(typeof(ResourceOrigin), fields[2]),
                types = new List<string>(fields[3].Split(','))
            });
        }
    }
}
