# Map resource authoring

Use Unity **6000.0.76f1**. After script import, the one-time installer configures
loaded scenes containing a `WorldManager`. You can also run
**Tools > Mini Colonies > Configure Map Resources**. Review and save the scene
normally; the installer does not save scenes or overwrite existing resource assets.

Starter assets are created through Unity Editor APIs under `Assets/MapResources`:

| Resource | Shape | Depth | Supply | Placeholder appearance |
| --- | --- | --- | --- | --- |
| Oil | Deposit | Underground | Finite | Dark terrain |
| Water | Deposit | Overground | Unlimited | Blue terrain, restricted to low heights |
| Copper | Nodes | Overground | Finite | Copper terrain and two rock prefab variants |
| Carbon | Nodes | Overground | Finite | Dark terrain and two rock prefab variants |
| Concrete | Deposit | Overground | Finite | Pale terrain |

Copper uses the existing `CopperOre` production resource identity. Existing enum
values are preserved. Change any starter definition's shape, depth, supply,
material or prefabs in its Inspector. Create more definitions through
**Create > Mini Colonies > Map Resource**. New economic resource identities still
need an entry in the existing `ResourceType` enum.

Starter copper, carbon and concrete entries exclude the lowest water layer.
Their initial height ranges follow the world's height step and layer count;
these ranges remain authored values after setup, so review them if topology changes.

## Tile state and natural appearance

`WorldManager.ResourceMap` is authoritative. Every tile either has no resource
source or belongs entirely to one source. A deposit links multiple whole tiles
to one source; a node has one tile and its own source. There is no fractional
resource occupancy derived from the visible surface.

| Concern | Authority |
| --- | --- |
| Resource presence and source identity | Tile index in `MapResourceMap` |
| Deposit footprint | `MapResourceSource.Cells` |
| Finite/unlimited supply | Source state; one generated unit total per source |
| Construction restrictions | Source rules plus the existing terrain/building/road rules |
| Hover on terrain and square perimeter | Logical tiles, including unpainted margins |
| Natural boundary, tint, blending, material and prefab placement | Presentation derived from tile state |

For example, a concrete patch may paint only 25% of an edge tile. The entire tile
still belongs to that concrete source and blocks construction. An underground oil
tile still contains oil even when its paint is small or absent; construction is
allowed according to its depth and the normal terrain rules. Changing appearance
does not change source IDs, occupied cells, unit budgets or construction results.

Adjacent sources of the same definition may visually join, but their identities,
quantities and hover footprints remain independent. Terrain meshes/colliders still
cover the full terrain surface: polygon clipping divides paint, not physical ground.
Square hover outlines deliberately show the precise logical footprint rather than
following the decorative contour.

## Resource definitions

The resource asset Inspector shows settings for its selected **Shape**. Nodes
hide deposit depth, tile-growth parameters and deposit prefab chance. Deposits
show only the parameters used by their resolved growth mode; **Resource Default**
also displays the effective mode. A fixed vein angle appears only when random
direction is disabled. Terrain appearance controls require a terrain material;
prefab tuning requires at least one assigned prefab variant. Switching modes
only changes which controls are shown; hidden authored values are retained.

In the world's **Map Resources** list, each entry is labeled with its resource
name and shape. Deposits show tile-count and flat-terrain preferences; nodes
show node distribution and, when clustered, nodes per cluster. Finite unit
ranges are hidden for unlimited resources. Strict location ranges appear only
when their restriction is enabled. Assign a resource to an empty entry first.
Mixed selections show common controls while ambiguous type/mode controls stay hidden.

- Nodes occupy one tile each and always block buildings and roads. Nearby nodes
  remain separate sources even when generated in a cluster.
  Nodes must be on flat, buildable ground, surrounded by one tile of flat,
  playable, buildable terrain at the same elevation. They cannot spawn along map
  edges, cliffs or unbuildable shores, even if `Require Flat Terrain` is disabled.
- Deposits occupy a connected set of at least two tiles. All those tiles refer
  to one source and share its generated quantity. Overground deposits block
  buildings and roads. Underground and deep deposits allow construction.
- `Unlimited` explicitly marks an unlimited source. Its numeric generated units
  are zero, not a finite zero-supply source.
- **Deposit Tile Growth**, on the **resource definition asset**, controls how
  tiles spread inside each individual deposit. It changes the logical footprint,
  not the distribution of separate deposits across the map or the painted contour.

  | Growth mode | Tile footprint |
  | --- | --- |
  | Resource Default | Water uses Organic, oil uses Vein, other types use Compact. |
  | Compact | Dense patches growing outward from their origin. |
  | Organic | Uneven patches with broad lobes and indentations. |
  | Vein | Long, narrow footprints following a preferred direction. |
  | Branching | Narrow arms; growth can continue a tip or start another arm. |

  `Deposit Compactness` controls how strongly Compact/Organic/Vein growth favors
  the selected shape (1 = strongest shape bias, 0 = loose random frontier growth).
  `Deposit Irregularity` controls Organic lobes; 0 gives compact growth.
  `Deposit Elongation` sets the Vein length-to-width preference (1-12).
  `Randomize Deposit Direction` gives each vein its own seeded orientation;
  disable it to use `Deposit Growth Angle` (0 follows X, 90 follows Z).
  `Deposit Branch Chance` controls how often Branching growth selects another
  frontier arm instead of extending its newest tip.

  All modes remain four-connected, respect the world's `Deposit Tiles` size
  range, share one source/quantity, and use only eligible tiles. Terrain and
  strict location filters can constrain the preferred shape. Nodes ignore these
  settings. Regenerate after changing them. Material/terrain blending remains a
  separate, appearance-only layer derived from the resulting whole tiles.
- `Terrain Material` optionally replaces the existing terrain faces; clearing it
  restores normal terrain appearance on the next generation. Height and terrain
  buildability are unchanged. Materials receive UVs in world-grid tile units.
  The installer gives resource surface materials the map terrain shader, copying
  its shader settings while preserving material GUIDs. `Terrain Tint` sets each
  resource's surface color; it retains the map palette's relative height-band
  brightness. Existing material colors migrate into this field once. Node prefab
  materials are kept separate so their placeholder colors survive this change.
- `Terrain Transition Style` defaults to **Polygonal**: solid patches with broad,
  irregular straight edges and sharply separated terrain/resource colors. Edges
  cut through mesh triangles instead of snapping to their sides, avoiding tiny
  sawtooth chips and holes. Existing Fragmented settings become Polygonal without
  changing their serialized value. **Smooth** keeps the continuous color fade.
- `Terrain Blend Width` controls polygon edge insets and corner cuts (default
  0.35, maximum 0.5). Exposed sides recede by unequal amounts, giving boundary
  tiles and nodes smaller, offset footprints. Adjacent boundary tiles share exact
  endpoints; concave junctions taper back to the original corner for continuity.
  In Smooth mode it measures the color fade width inside exposed
  edges, in tiles. Zero disables this contribution while retaining the configured
  corner shaping. Shared resource edges remain continuous, including across chunks.
- `Terrain Blend Amount` controls how much ordinary terrain appears near the
  resource border: 0 disables the width-based edge insets and corner cuts; higher
  values shrink border coverage further. In Smooth mode this controls fade strength.
  It does not reduce the resource color in the patch interior.
- `Terrain Blend Randomness` controls border irregularity (default 0.65).
  Increase toward 1 for more varied tile coverage, edge positions and corner
  lengths; 0 gives uniform side insets and bevels. In Smooth mode it varies the fade.
- `Terrain Blend Noise Size` controls the spatial scale of corner variation in
  tiles (default 1, minimum 0.1). Polygonal mode combines coherent variation with
  seeded jitter at individual boundary vertices, without adding fine breakup.
  Variation uses the map seed and resource identity,
  remains consistent across chunks, and stays fixed until regeneration.
- `Terrain Edge Roundness` shapes exposed convex corners inward (default 0.2,
  maximum 0.5). Polygonal mode uses straight bevels; Smooth uses rounded corners
  with a radius measured in tiles. This control works independently of blend
  amount/width. Set both corner shaping and blend width/amount to zero for square
  terrain borders. Hover outlines always keep square footprint corners.
  Isolated nodes retain a solid center even with maximum cuts. Polygonal contours
  are independent of mesh subdivisions; Smooth curve detail follows subdivisions.
  Deposit growth favors compact, connected patches while respecting strict placement.
  Blending changes no tile ownership, location restrictions or construction rules.
  Maps containing resources use at least two mesh subdivisions so single-tile
  patches retain an interior color sample even at the lowest mesh setting.
- `Prefab Variants` accepts authored prefabs. Each node chooses a non-null
  variant. Deposits independently roll `Deposit Prefab Chance` on every occupied
  tile and choose a variant when successful. Empty lists are valid for both.
  Placement is jittered within the tile; polygonal terrain constrains the prefab
  origin to its visible patch. It supports optional random yaw, scale range
  and a height offset. Variants should have their origin at ground level; add
  colliders if their visible geometry should be hoverable above the terrain.

## World spawn settings

Edit `WorldManager > Map Resources`. Entries run in list order and reserve tiles
before later entries, including underground and deep resources; this initial
implementation permits one resource source per tile. Existing roads/buildings
and the unplayable/extruded border are excluded.

- `Source Count` is the minimum/maximum count. Nodes count individually; a whole
  deposit counts once. `Sources Per Thousand Tiles` chooses the target count
  from eligible map area, with stochastic rounding, clamped to those limits.
  Zero density or a disabled entry generates nothing, even with a positive minimum.
- `Total Units` samples an inclusive **map-wide budget for this entry**, then
  distributes that exact quantity among successfully generated sources. It is
  not multiplied by tile or source count. Each finite source receives at least
  one unit, so very small budgets can lower source count. Unlimited supply ignores
  this range. If no source fits, no units are generated and a warning is reported.
- `Deposit Tiles` controls area per deposit. Unfit patches smaller than the
  minimum are discarded. Node distribution can be scattered or clustered;
  `Nodes Per Cluster` controls the attempted connected cluster size. Available
  space and the remaining source count can truncate a node cluster.
- `Require Flat Terrain` excludes transitions. Enabled height, center-distance,
  and map-position restrictions must all hold for **every occupied tile**.
  Height is physical world height, not layer index. Center distance is normalized
  from 0 at the center to 1 at the playable edge (circular rings on circular maps,
  square rings on square maps). Map position is a normalized X/Z rectangle.
  Use center-distance ranges such as 0–0.4 for inner placement or 0.7–1 for outer
  placement. Inverted/non-finite location ranges reject placement rather than
  silently widening allowed locations.

Counts can fall below minimum when geography, area requirements, earlier entries,
or finite budgets prevent placement. Generation reports this instead of breaking
location restrictions. Disable `Randomize Seed On Play` to reproduce a map. Visual
randomization uses a separate seed stream from source placement and quantities.
Regenerate after changing settings or definitions.

In Normal view with no tool active, click a resource prefab or any logical tile
of a deposit to open its compact resource info card. It shows the name with a
`Node` / `Deposit` suffix, catalog type names underneath, a `Remaining` row with
remaining / generated total, a `Quality` placeholder, and the catalog resource
description (or `No description available.`). There is no supply bar or divider.
Unlimited sources display
`Unlimited`. Each finite source starts with `RemainingUnits == GeneratedUnits`;
both quantities belong to `WorldManager.ResourceMap`, never to the UI or assets.
There is no extraction gameplay, regrowth, terrain carving or save-to-disk system.
Quality displays `—` until resources have a quality rule.

The editable hierarchy is `Canvas/ResourceInfoPanel/Card`. It uses the same game
font and dark card colors as the building and intersection panels. The one-time
Editor installer runs after import and on scene opening, or through
**Tools > Mini Colonies > Install Resource Info Panel**. A one-time layout upgrade
compacts older cards, moves their existing name/kind/quantity controls, removes
the old divider and supply bar, and adds the metadata controls. Later runs preserve
designer edits. Scene saving stays explicit so unrelated pending edits stay unsaved.
Build scene processing also supplies missing wiring. Drag the card background to
move it; its close button hides presentation and clicking the same source reopens
it. Right click or an empty world click clears selection. Building selection,
tool/view changes and world regeneration also clear resource selection. Selection
and dragging remain usable while paused; no simulation timer is involved.

## Verification

Run **MapResourceInfoPanelTests** for source quantities, unlimited supply, selection
exclusivity, repeated-click reopening, tool/view/regeneration invalidation and
event-driven display updates. In SampleScene, verify that clicking node prefabs
and terrain tiles opens the same styled card; all tiles of a deposit show its
shared total. Check closing, background dragging, selection outlines, building
selection, empty/right clicks and tool/view transitions, including while paused.
Run the installer twice and confirm one editable panel with designer edits intact.

Run the focused **MapResourceGenerationTests** EditMode fixture in Unity's Test
Runner. It covers repeatable placement/quantities, connected shared deposits,
appearance-independent tile identity, footprint, quantity and construction state,
per-resource tile-growth defaults, connected and repeatable footprints for all
growth modes, vein elongation/direction, branching perimeter, organic variation,
invalid growth values and node independence,
independent clustered nodes, non-overlap, strict locations, impossible terrain,
density/count limits, finite/unlimited budgets and actual building/road placement
checks for each depth.
It also covers mandatory node clearance, height-band shading, continuous boundary
weights, independent fade strength/roundness controls, sprite-based perimeter
outlines without shared edges, hole boundaries and fully interior tiles.
Seeded blend-noise coverage checks repeatability, spatial variation, different
seeds, shared edges, anchored colors, the zero-randomness fallback and RNG isolation.
Polygonal-transition checks verify broad repeatable cuts, solid node centers,
varying node coverage, receding straight borders, shared boundary endpoints,
continuity at concave junctions, prefab origins inside the patch,
disabled/invalid controls, exact terrain area and interpolated heights, unchanged
contours across mesh resolutions, and actual mesh edges crossing the grid with
flat colors on both sides. The mesh regression also verifies that Smooth remains available.

For an Editor/Play Mode smoke check:

1. Run Configure Map Resources twice and confirm existing asset/settings edits
   survive and there is one `MapResourceHover` component on the world. Save the
   intended scene changes normally.
2. Enter Play Mode in SampleScene. In Normal view with no construction tool
   active, hover node prefabs and deposit terrain. Confirm only the name appears,
   and each hovered source is outlined using the building-placement sprite. A
   deposit has one boundary around its footprint, including holes and concave
   corners, with no internal tile grid, even across chunks. A node retains its
   single-cell boundary. Moving away, over UI, or to
   another view clears feedback. Outline sprite and color are editable on the
   world's `MapResourceHover` component.
3. Try a building footprint and road through each resource depth. Nodes and
   overground deposits reject placement; underground/deep deposits permit it
   where terrain otherwise permits construction. Invalid footprint cells turn red.
4. Add a prefab variant to a deposit and set scatter chance to 1. Regenerate and
   confirm decorations appear only on its tiles. Remove its material and confirm
   normal terrain faces remain while hover/source behavior still works.
5. Pause and change speeds while hovering and regenerating. Feedback and
   generation remain responsive. Regenerate repeatedly and clear the world
   during generation; old prefabs, highlights and sources must disappear.
6. Check resource patches at several elevations. Their colors should keep the
   map's stepped height shading. In Smooth mode, check the continuous color fade;
   in Polygonal mode, check solid colors and broad straight edges. Inspect
   adjacent same-type tiles and chunk seams. Compare
   blend amounts 0, 0.5 and 1 at a fixed blend width, then compare corner roundness
   0 with 0.3. Regenerate after changes. Confirm hover perimeter corners stay square
   while terrain corners change. Confirm nodes stay away from cliffs and unbuildable
   terrain, while deposits such as water can still occupy their allowed terrain.
7. Use Polygonal style and compare blend randomness 0 with 0.65 and 1, then try
   noise sizes 0.5 and 2. Reduce Blend Width for smaller corner cuts. Regenerate
   with a fixed seed twice; patches should repeat without forced chunk seams.
   Compare mesh subdivisions 2 and 12: contour shape should stay the same, with
   broad clean edges and no tiny chips or accidental holes. Switch to Smooth to
   compare the fade. Hover outlines should retain square corners.
8. Disable Randomize Seed On Play. Select a resource definition asset under
   Assets/MapResources and change **Deposit Tile Growth** between Compact,
   Organic, Vein and Branching. Regenerate after each change. Use at least 32
   Deposit Tiles to compare footprints clearly. Compare Vein elongation 1 with
   8; disable random direction and compare angles 0 and 90. Compare Organic
   irregularity 0 with 1, and Branch Chance 0 with 0.5. Hover outlines should
   follow the actual whole-tile shapes; terrain paint remains natural inside them.
   Resource Default gives water Organic, oil Vein and concrete Compact growth.
   Deposit origins are randomly selected from eligible tiles. Strict limits
   remain enforced.
9. In Edit Mode, inspect a node asset and a deposit asset, then compare their
   entries in WorldManager's Map Resources list. Confirm unrelated settings are
   hidden. Change a deposit's growth mode and verify only its controls appear;
   toggle random vein direction, node clustering, unlimited supply and location
   restrictions to check their dependent fields. Switch back or undo the changes
   and confirm previous values return. Reorder/collapse the world entries and
   verify the fields remain under the correct resource with no overlap.
