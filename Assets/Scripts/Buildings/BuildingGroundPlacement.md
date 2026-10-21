# Per-cell building tile rules

Tile types are derived from the existing world data. No ground-type assets or
resource-specific code are needed.

| Tile type | Source of truth | Shared icon |
| --- | --- | --- |
| Flat Ground | Terrain layer marked Flat Ground, away from edges | BuildingPlacementInput |
| Edge | Terrain slope/cliff classification | BuildingPlacementInput |
| Water Level | Terrain layer marked Water Level, away from edges | BuildingPlacementInput |
| Resource Node (resource name) | Logical resource source with Nodes shape | Resource catalog World Tile Icon |
| Resource Deposit (resource name) | Logical resource source with Deposit shape | Resource catalog World Tile Icon |

A tile has a terrain surface and may also contain a resource. This keeps Water
Level distinct from a generated Water Deposit, and allows an underground Oil
Deposit beneath ordinary Flat Ground. Borders, roads, and building occupancy
remain independent obstructions, not terrain types.

## Author a footprint

1. On `WorldManager`, expand **Layers** and select each layer's **Tile Type**:
   Flat Ground or Water Level. Edges are derived automatically from geometry.
2. Expand a building asset's **Footprint** cell. Add entries to either list:
   - **Optional Types** grants additional placement permissions while preserving
     normal flat-ground placement. It does not impose a requirement.
   - **Required Types (Any)** requires at least one listed type to match.
     Matching required types also grant the relevant permission. Every required
     footprint cell must satisfy its own list.
3. Select a type using the dropdown. Resource Nodes and Resource Deposits are
   populated automatically from all `MapResourceDefinition` assets, deduplicated
   by resource identity and shape. Multiple definitions at different depths can
   share one choice. New definitions appear the next time the dropdown opens.

| Example cell | Optional Types | Required Types (Any) |
| --- | --- | --- |
| Ordinary foundation | Empty | Empty |
| Foundation that can extend over water | Water Level | Empty |
| Dock cell that must reach water | Empty | Water Level |
| Copper extractor | Empty | Copper Ore Node |
| Extractor accepting either resource | Empty | Copper Ore Node, Carbon Node |
| Oil extractor permitted over water | Water Level | Oil Deposit |

Each matching permission removes only its own restriction. Resource permission
does not bypass water or edges; add those to Optional Types when needed. An
optional water permission cannot satisfy a required Oil Deposit. Required
types are alternatives, not cumulative requirements: putting Water Level and
Oil Deposit together under Required Types accepts either. Use the separate
optional list for the oil-on-water example above.

Water and edge permissions allow a cell to cross elevation boundaries, so a
dock's ordinary land cells can anchor the building while its special cells
reach water below. The existing elevation selection prefers a flat-ground
cursor cell, then the most represented flat-ground footprint layer. With no
such anchor it uses the rounded cursor height. Default road placement still
uses flat ground and its existing slope/coast safety checks. Underground/deep
deposits retain their default nonblocking behavior; nodes and overground
deposits need an explicit matching permission.

## Add a resource

Create the resource and set its icons in the existing Resource Editor, create a
map resource definition for its node/deposit appearance, and configure its
spawn settings on the world. That definition automatically supplies a footprint
tile choice using the same persistent resource ID and catalog icon. There is
no additional ground asset, registration list, or enum entry to maintain.

## Preview and compatibility

On `BuildingPlacementInput`, assign **Flat Ground Tile Icon**, **Edge Tile
Icon**, and **Water Level Tile Icon** once. In **Tools > Mini Colonies >
Resources > Resource Editor**, select a resource and assign **World Tile Icon
(optional)** for its node/deposit footprint markers. This is separate from the
regular **Icon (optional)** used by factory/UI controls. A missing world tile
icon falls back to the regular icon, then the catalog's Default Icon.

Each building **Footprint** cell also exposes **Footprint Icon Override**.
Assigning it replaces every terrain/resource rule icon in that cell with one
centered sprite. It also works on ordinary cells with no rules, uses the gap
filler, and follows footprint rotation. It changes presentation only; placement
requirements and port indicators remain active. Clear it to use shared icons
again. After editing preview icons, exit and re-enter placement to rebuild the
preview; no per-frame icon refresh is added.

**Tools > Mini Colonies > Configure Building Placement Preview** fills missing
Optional/Required Ground Fill Sprite references with `BuildingPlacement_11`
and `_12`. It preserves overrides and marks scenes dirty without saving pending
edits. Fillers share the hatch/outline atlas; icons may use other textures.
Icons appear in the center gap, with multiple types sharing a row. A cell with
required types uses the required filler. Combined port cells use a smaller
corner port icon. Placement remains usable while simulation is paused.

Old scenes retain their serialized `isBuildable` values: an unset surface type
resolves true to Flat Ground and false to Water Level. Explicit named types
take precedence. Existing ground assets remain supported only for legacy
footprint cells; the Inspector shows those existing references with a notice.
Adding a type to either new list switches that cell to the new rules without
deleting its legacy data. The old ground-asset creation menu is retired.
Placement does not reshape terrain or consume/extract resource sources.

## Validation

Run the focused **BuildingPlacementPreviewTests** EditMode fixture in Unity
6000.0.76f1. It covers typed and legacy rules, custom resource IDs, node/deposit
distinctions, optional versus required matching, terrain/resource coexistence,
rotation, shoreline elevation, obstructions, and preview icon cleanup.
Run **ResourceCatalogTests** for the separate world/UI icon precedence and
fallback regression.

For a visual smoke check, assign a required Copper Ore Node to a test building
cell, use the preview setup command, then enter Play Mode. Hover bare ground,
an Oil tile, and a Copper Ore node tile; only the matching placement should
succeed. Rotate with R and check that the cell/icon follows the footprint. Check
a shoreline footprint with ordinary land cells plus water/cliff permissions,
and a cell combining a port with a ground rule. Verify both gap fillers and
icons at the normal camera zoom, including while paused. Exit placement and
confirm the overlay disappears. Save authored scene changes normally after
review; the setup command does not save pending scene edits.

Assign different regular and World Tile Icons to a resource. Confirm its node
and deposit footprint markers use the world icon while factory controls retain
the regular icon. Assign a Footprint Icon Override to a cell with multiple
rules; confirm exactly one centered override appears, rotation follows the
cell, and placement remains valid/invalid on the same terrain as before. Clear
the override and re-enter placement to restore the shared rule icons.
