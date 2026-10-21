# MiniColonies Architecture Context Guide

This document describes the current `Assets/Scripts` architecture after the
traffic cleanup. It is meant to help a reader follow the active workflows
without chasing every helper class first.

## Core Rule

Large simulation work should not stall the frame. Player input mutates compact
backend state immediately, while mesh, road network, traffic graph, and
pathfinding work runs through `SimulationTaskManager` as time-sliced tasks or
Unity jobs.

## Main Systems

### Resources and Types
- `Resources/ResourceTypes.cs` retains the persistent `ResourceType` identities
  used by factory inputs/outputs, storage, vehicle cargo and map definitions.
  Existing integer values retain their meanings and serialization format; enum
  names are legacy aliases, not the list of allowed resources. `ResourceCatalog`
  allocates new permanent integer identities (starting at 1024) without adding
  enum entries or rewriting assets. Its serialized allocation counters prevent
  retired identities from being reused; imported IDs also advance allocation.
  `IsKnownResource` validates against the catalog, retaining legacy enum fallback
  only when no catalog is available. Never validate catalog IDs with `Enum.IsDefined`
  or enumerate the catalog with `Enum.GetValues`.
- `Resources/ResourceCatalog.cs` owns shared display names, intended World/Factory
  origins and many-to-many type membership. The default editable asset is
  `Assets/Resources/ResourceCatalog.asset`, loaded through `ResourceCatalog.Current`
  in players as well as the Editor. Factories and vehicle dropdowns use its names.
  Map definitions keep their own authored source labels, terrain and spawn rules;
  their Inspector also shows the shared catalog's types.
- Types are catalog data with stable, case-sensitive string IDs (for example
  `metal-ore`, `fuel`, `liquid`, `coolant`). Designers can add types and change
  memberships without code changes. Renaming an ID requires updating memberships
  and future recipe references. `HasType` and `GetResourcesWithType` provide
  deterministic queries for factory rules. `HasAllTypes` and
  `GetResourcesMatchingTypes` implement AND filters and reject empty or unknown
  requirements. Invalid/missing entries fail
  safely; the catalog Inspector reports duplicate IDs/identities and broken links.
- `Tools/Mini Colonies/Resources/Resource Editor` (also `Edit Resource Catalog`)
  opens the searchable resource authoring window. Double-clicking the catalog
  asset or its Inspector's Edit / Add buttons opens the same window. The Resources
  and Resource Types tabs each have an Add button and editable name/icon/details.
  Resource memberships are type checkboxes; the type tab links to its assigned
  resources. Reference IDs are generated automatically and stay fixed when names
  change. Changes support Unity Undo and the window's Save button saves only that
  catalog. New resources default to both origin flags; origins remain metadata.
  `Editor/ResourceTypeDrawer` lists catalog resources, including custom identities,
  in factory input/output, vehicle capability and map-resource Inspector selectors.
  Missing references remain visible and are not silently replaced. Its `...` button
  opens the editor. No source generation, asset migration or new clock is needed.
- `ResourceCatalogDefaults` supplies 64 starter resources (56 additional resources)
  across 18 types. `Editor/ResourceCatalogSetup` creates a missing default asset
  through Unity APIs after reload and exposes
  `Tools/Mini Colonies/Resources/Edit Resource Catalog`. Existing assets, including
  intentionally emptied catalogs, are preserved. Only the catalog asset is saved;
  scenes and unrelated pending edits are untouched.
- Focused custom-resource checks are `ResourceCatalogTests` and
  `FactoryTypedInputTests`. In the Editor, create a resource and type, assign a
  membership/icon, rename both, undo/redo an edit, and save/reopen the catalog.
  Verify the new resource appears in factory, map-resource and vehicle selectors;
  use it as a typed input and a specific input and verify storage/production.
  Existing factories, map definitions and vehicle capability references must
  retain their original identities. This smoke check covers Editor window wiring;
  deterministic rules and integer serialization are covered by the fixtures.
- Every resource has an editable `icon` sprite override. `fallbackIcon` on the
  catalog supplies one shared placeholder when an override is missing;
  `GetResourceIcon` / `GetIcon` are the common lookup boundary. Factory storage,
  processing rows and material dropdowns use those sprites. The default catalog
  references the existing placeholder sheet; no runtime sprites are generated.
- Resource definitions also expose a separate `worldTileIcon` in the Resource
  Editor. `GetResourceWorldTileIcon` / `GetWorldTileIcon` resolve this override,
  then the regular resource icon, then the catalog placeholder. Node/deposit
  footprint markers (including legacy ground fallbacks) use this lookup while
  factory/UI icons retain their regular lookup.
- Resource categories also have editable `icon` overrides, resolved through
  `GetTypeIcon` with the same placeholder. Typed factory inputs show their required
  category icons beside the label; AND filters show each category's icon using
  pooled instances of the authored row icon. The dropdown keeps the chosen item's
  own name and icon, independently of its categories.
- Origins remain descriptive metadata: no deposits, factories or harvesting
  are installed automatically. Runtime inventory quantities remain in backend
  owners. Vehicle cargo is still a concrete resource identity. This catalog
  introduces no clock, polling, or simulation speed changes.

### Simulation Time
- `Managers/GameTimeManager.cs` owns the selected Pause/x0.5/x1/x2/x4 speed,
  simulation elapsed seconds, frame delta, and calendar. It updates before
  simulation consumers and never writes Unity's `Time.timeScale` or fixed delta.
  Day length remains the duration at x1; changing speed preserves partial hours
  and emits every crossed hour/day event, keeping factory shifts and production
  on the same calendar. The existing `useUnscaledTime` setting selects the base
  time source for the whole simulation.
- Runtime input is capped at 0.2 base seconds per frame before multiplying by
  speed. Traffic retains 0.05-second steps with a bounded 16-step budget, enough
  for that shared cap at x4. Long stalls slow all simulation consumers together
  instead of advancing the calendar beyond the traffic catch-up budget.
- Conveyor driver reaction deadlines use elapsed fixed-step simulation time.
  Vehicle loading/unloading, entry retries, housing departure spacing, and debug
  vehicle approaches use the shared simulation clock. Pending gameplay waits
  respond to live speed changes. Return-route recovery retains real-time retries
  because graph rebuilding/pathfinding and task budgets still run per frame.
- Camera controls, UI refresh/debounce, placement, mesh/network work and cosmetic
  signal pop animations retain their existing timing. Gameplay speed is session
  state with an authored starting value; no disk-save system is introduced.
- `UI/GameClockUI` binds the editable top-left `GameClock` hierarchy to
  `GameTimeManager.OnHourChanged`, `OnDayChanged`, and `OnSimulationSpeedChanged`,
  without per-frame polling. It displays the hour in 24-hour format (`00:00` to
  `23:00`) with a smaller `Day N` cycle label to its left, an underbar, and five direct-selection buttons ordered
  Pause/x0.5/x1/x2/x4. Each button has editable placeholder Image marks, a speed
  caption and a selected indicator driven by backend state. Pausing freezes
  the hour and day displays along with gameplay; cosmetic/UI timing remains unchanged.
- `Editor/SimulationSpeedButtonSceneBuilder` installs this hierarchy in Edit
  Mode through `Tools/Mini Colonies/Install Game Clock` (the old menu remains
  an alias). Initial installation deactivates the legacy `SimulationSpeedButton`
  object without deleting its authored content. Repeated installation preserves
  the new layout and styles. Existing clocks gain a missing authored day label
  once, reserving space to the left of the hour; later installs preserve edits.
  Previously clean scenes are saved; pending scene
  edits are left unsaved. Runtime code only binds the authored controls.
- Pause uses a zero simulation multiplier, preserving partial hours, pending
  waits and traffic accumulator state. Resuming through any speed button does
  not catch up paused wall time. The legacy step button remains supported on
  other authored UI: left lowers speed, right raises it, between Pause and x4.

### Terrain
- `Terrain/WorldManager.cs`
- Owns terrain generation, cell height sampling, tile classification, and world
  bootstrap.
- `TerrainLayer.surfaceType` classifies layers as Flat Ground or Water Level.
  Edge cells derive from the existing slope/cliff test. The legacy serialized
  `isBuildable` field remains hidden: unset types resolve true to Flat Ground
  and false to Water Level, while explicit types take precedence. The layer
  Inspector presents named types and supports Undo without resaving scenes.
  `IsLayerBuildable` remains a compatibility query for flat-ground policy in
  road placement and resource-generation safety; it no longer owns a separate
  classification. `WorldManager.GetTileInfo` derives terrain, edge state, layer,
  playable bounds, and resource source on demand without a second grid/cache.
- Provides `cellSize`, `chunkSize`, and height data used by roads, buildings,
  and traffic snapshots.
- `WorldManager.ResourceMap` owns generated resource sources and their tile index.
  Each node owns a source; every four-connected deposit tile resolves to the same
  source. Definition assets describe resource identity, node/deposit shape,
  overground/underground/deep placement, finite/unlimited supply, optional terrain
  material and decorative prefab variants. Runtime quantities never live on assets.
  Tile ownership is discrete: each tile resolves to no source or exactly one
  source, including unpainted portions of that tile. Source cells, supply and
  construction restrictions are authoritative; material coverage has no gameplay
  meaning. Appearance changes cannot resize a logical deposit, reduce its units,
  or free construction space. Terrain picking and square hover perimeters use
  the logical footprint. Adjacent sources may share a visual surface without
  merging their identities or quantities. Rendering helpers, including
  `MapResourcePatchGeometry.UsesResourceTint`, classify paint only.
  `Editor/MapResourceDefinitionEditor` filters definition fields by node/deposit
  shape, resolved deposit growth mode, assigned material and prefab variants.
  Runtime and Inspector share the same pure growth-mode resolver. The
  `MapResourceSpawnSettingsDrawer` filters each world's resource entries by their
  referenced definition, finite supply, node clustering and strict-rule toggles.
  It supports default UI Toolkit Inspectors through an IMGUI container as well
  as IMGUI custom Inspectors. Both edit existing serialized properties with
  normal Undo/prefab override behavior; hiding a field never resets its value.
- `MapResourceGenerator` samples a deterministic source count from eligible-area
  density clamped by count limits. Enabled height, center-distance and normalized
  map-region restrictions are strict for every tile, including cluster/deposit
  growth. `MapResourceDepositGrowth` selects tiles from each source's connected
  frontier using its resource definition's Compact, Organic, Vein or Branching
  settings. Compactness, broad organic irregularity, vein elongation/orientation,
  and branch chance change the logical footprint while preserving tile counts,
  connectivity and strict eligibility. Resource Default gives water Organic,
  oil Vein and other types Compact growth. Shape noise and random vein direction
  are seeded per resource/origin, independently of terrain paint. Selection is
  bounded to eight candidate samples or four new tip neighbors; the existing
  tile yield budget remains in force. A temporary grown-cell set supports branch
  spacing and is discarded with generation. Node growth is unchanged.
  Earlier spawn entries reserve tiles first; sources never overlap.
  Origins are selected from the seeded shuffle of eligible tiles. Per-resource
  growth settings shape each deposit's tiles after its origin is chosen; there
  is no regional clustering or belt system for deposit spawn locations.
  Existing roads/buildings and the unplayable border are excluded. Geographic
  shortages produce warnings instead of relaxing restrictions. Finite entries
  sample a map-wide unit budget and distribute it among actual sources; unlimited
  sources use an explicit flag. There is no harvesting, depletion, regeneration,
  terrain carving or disk persistence in this workflow.
  Nodes additionally require buildable, non-transition terrain and a one-tile
  surrounding ring of playable, buildable, non-transition terrain at the same
  elevation. This mandatory clearance excludes cliffs, coasts and map edges even
  when the optional flat-terrain preference is disabled. Terrain samples are
  cached only for the duration of a generation pass.
- `PlayerActionManager` also owns `SelectedResource` and its selection event,
  mutually exclusive with building selection. Click and hover share
  `WorldPlacementPicking`: prefab markers resolve source identity, and terrain
  hits query logical building occupancy before resource tiles. Selection rejects
  sources from replaced maps and clears on world regeneration, tool/view changes
  or deselection. `WorldPlacementHover` keeps the selected source's solid outline
  independently of hover feedback.
- `UI/MapResourceInfoPanel` binds an authored `ResourceInfoPanel/Card` hierarchy
  on the overlay Canvas, using the existing game font and dark card style. It
  displays a compact header with the resource name and `Node` / `Deposit` suffix,
  catalog type names underneath, a `Remaining` row with the source's
  `RemainingUnits / GeneratedUnits`, a quality placeholder (`—`), and the catalog
  resource description (with a placeholder when blank). The card has no supply bar
  or divider; unlimited sources
  display `Unlimited`. Finite sources initialize remaining units to their generated
  total. Remaining units are bounded by that total and publish quantity changes;
  the panel subscribes only to its displayed source, with no quantity polling or
  duplicate inventory. Extraction gameplay and quality tags remain future work.
  Closing releases the display subscription while leaving selection with its
  owner; repeated clicks reopen the card. Background dragging uses UI coordinates
  and stays within the Canvas. This presentation has no simulation timer and
  remains usable while paused. `Editor/MapResourceInfoPanelSceneBuilder` installs
  missing panels after import/on scene opening or through `Tools/Mini Colonies`.
  A versioned one-time migration compacts older cards, reuses their serialized
  name/kind/quantity references, adds metadata controls and removes the old bar
  and divider. Later runs preserve designer edits and leave scene saving explicit.
  Scene build processing supplies missing wiring without runtime UI generation.
- Resource generation precedes chunk construction and publishes its map once
  complete. Generation, decorative prefab placement and hover mesh work use real
  frame budgets, independent of simulation pause/speed. Regeneration cancels
  pending generation and clears old resource state and feedback. Chunk submeshes
  replace occupied terrain faces with optional resource materials and world-grid
  UVs. Resource surfaces share the map material's shader and use vertex tints
  retaining the relative brightness of its authored height bands. A per-definition
  blend width and amount control the border shaping. In Smooth mode they fade
  resource tints into map colors inside exposed edges and concave corners; a
  separate radius rounds exposed convex corners independently of fade strength. Adjacent tiles of
  the same resource keep continuous colors,
  including across chunks. This changes appearance without expanding occupancy.
  `Polygonal` is the default resource transition style (serialized value zero,
  retaining compatibility with the former Fragmented setting). Exposed sides
  have seeded, unequal insets at their world-grid endpoints, so boundary tiles
  can have smaller, shifted footprints instead of filling to the grid lines.
  Convex corners add broad straight bevels. Insets match across neighboring cells
  and taper to zero at concave junctions, keeping shared edges continuous.
  `MapResourcePatchGeometry`
  clips the existing terrain triangles exactly against those lines, emitting solid
  map/resource colors on opposite sides. Boundaries cut across the mesh grid;
  they no longer select entire triangles or introduce noisy chips and holes.
  Intersections interpolate the original terrain plane, preserving height and
  collider coverage. Cuts stay inside occupied tiles, agree on shared edges, and
  retain a generous core on isolated nodes. `Smooth` retains the previous vertex
  fade and coherent noise as an explicit per-resource option. Randomness and noise
  size vary edge positions and corner cuts using world-grid coordinates, seed and resource identity; rebuilding
  chunks is repeatable and does not consume gameplay RNG or alter source placement.
  All transition work is baked during mesh generation, not animated.
  Prefabs use an independent seeded stream, optional yaw/scale variation,
  and per-deposit-tile scatter chance. Polygonal prefab origins are constrained
  to the visible patch using the same cuts, without consuming additional RNG.
- Nodes and overground deposits block both buildings and roads through the
  authoritative placement checks; underground/deep deposits permit construction.
  Terrain material does not determine buildability. `Visuals/WorldPlacementHover`
  handles building and resource hover together; resource names still use the
  existing owned tooltip. `MapResourceHover` remains a compatibility subclass,
  retaining scene script GUIDs and inherited serialized appearance fields.
- `Editor/MapResourceSetup` creates editable starter definitions, materials and
  node prefab variants under `Assets/MapResources`, then configures loaded worlds
  once after reload or through `Tools/Mini Colonies/Configure Map Resources`.
  Defaults are water and concrete overground, oil underground, copper and carbon
  nodes; water is unlimited and restricted to low terrain. Later runs preserve
  designer edits and deliberately emptied spawn lists. Scene changes remain
  unsaved for normal review; build scene processing also supplies missing wiring.
  The terrain appearance migration preserves existing resource colors in
  `terrainTint`, keeps material GUIDs, and separates original node prefab materials
  before adopting the map shader. Existing placeholder prefab edits and custom
  material overrides are preserved. The installer also fills a missing outline
  sprite reference from the scene's building-placement component or sprite atlas,
  and assigns the new `BuildingPlacement_10` inner-corner sprite when missing.

### World Placement Highlighting
- `Visuals/WorldPlacementHighlight` renders any supplied collection of logical
  world-grid cells through `Show(cells, material, edge, innerCorner, color, heightOffset)`.
  It owns only presentation: resources remain in `WorldManager.ResourceMap`,
  buildings remain in `BuildingSystemBackend`, and placed/rotated footprints
  come from `Building.occupiedCells`. Call `Show` when a footprint changes and
  `Clear` when hover feedback ends. `ShowSelection` / `ClearSelection` independently
  control a second, solid outline; `ClearAll` clears both. A new request replaces
  only its own channel. Both channels share the real frame-time build allowance.
  A per-renderer `_SolidOutline` property bypasses the hatch for selection without
  changing the shared material or physical outline width. Regeneration and disable
  cancel both channels, including work that has not yet published meshes.
- `WorldPlacementOutlineGeometry` omits shared edges, retains holes, keeps
  independent touching sources separate, and samples the edge sprite's white
  top row. Concave joins use the new inner-corner sprite's white top-right pixel
  without overlapping strips. Corners remain square regardless of terrain paint.
  The renderer samples terrain heights, emits bounded mesh batches, and uses
  real frame budgets so feedback stays responsive during simulation pause.
  Regeneration and disable cancel pending work and release all owned meshes.
  These overlays are procedural because their geometry comes from world data.
- The shared highlight shader clips a diagonal dash mask to that perimeter.
  Its material exposes dash length, gap length (both in outline pixels), and
  signed animation steps per second. Defaults are three lit pixels, three empty
  pixels and eight steps per second, matching the placement hatch's diagonal
  pixel style. Base pixel density comes from the edge sprite width divided by
  cell size. The material's `Pixels across outline` multiplies that density by
  one through four (default three), adding smaller pixels within the original
  fixed-width strip. It changes only shader sampling, so density edits apply
  immediately without rebuilding geometry. The existing `_OutlineWidthPixels`
  material key is retained to preserve authored values. World-grid coordinates keep
  the pattern aligned across terrain triangles, corners and mesh batches.
  Spatial samples and animation phases are rounded down to whole pixels;
  there is no smooth scrolling or filled interior. One shader-global update per
  visible frame reads `Time.unscaledTime`, independently of simulation speed or
  pause. The shader animates existing meshes without CPU geometry rebuilding.
  Zero speed freezes the pattern; zero gap gives the original solid outline.
  `Motion mode` defaults to Diagonal: both axes contribute equally to a single
  hatch phase per tick. Positive speed moves southwest to northeast (+X, +Z),
  using an `x + z` hatch so the diagonal translation crosses the stripes;
  negative speed reverses it. Splitting the phase between the two axes prevents the
  former two-phase jump, which skipped alternate states in the six-phase pattern.
  Original mode uses the earlier single-axis offset. Both modes now visit every
  phase at the same tick rate; density never changes the mesh's physical width.
- `WorldPlacementHover` performs one nearest-hit pick after camera movement in
  Normal view with no active tool and no UI under the pointer. Building colliders
  resolve to their parent building; terrain hits first query building occupancy
  (including model gaps), then resource tiles. Resource prefab markers retain
  source identity. Unchanged targets reuse the current geometry. Building
  authoring revisions invalidate its cached presentation. Hover owns its tooltip
  and clears feedback on target/view/tool changes or world regeneration.
  Building selection reads `PlayerActionManager.SelectedBuilding` and its existing
  selection/mode events. A selected building stays solid while another building
  or resource is hovered with an animated outline. Moving the pointer over UI
  clears hover while preserving selection. Hovering the selected building keeps
  only the solid outline. Selecting
  another building, clearing selection, changing tools/views, disabling a target,
  or demolishing it clears the appropriate feedback. Re-enabling the presenter
  restores the current selection from its owner. `PlayerActionManager.SelectBuilding`
  is the shared selection entry point for world clicks and explicit callers; it
  keeps repeated-click panel reopening and clears selection on building removal.
- `Editor/MapResourceSetup` wires the shared hover and renderer in loaded scenes
  and builds, preserving existing material, sprites, tint, height offset and
  pending scene edits. Legacy component and geometry entry points delegate to
  the shared implementation; no second resource outline renderer remains.

### Roads
- `Roads/RoadSystemBackend.cs`
- Owns the authored road grid, preview transactions, connection bitmasks,
  dirty chunk events, and editable intersection policy data.
- Emits `OnRoadCellChanged` for logical/network invalidation and
  `OnChunksDirty` for visual mesh rebuilds.

### Buildings
- `Buildings/BuildingSystemBackend.cs`
- Owns active buildings and occupancy.
- `ResourceProductionBuildingData` owns singular/plural worker role names per
  factory type (for example engineer/engineers). Factory schedule and status views
  bind those names into existing authored labels; missing or blank names fall
  back to factory worker/factory workers without changing population rules.
- Production entries now describe named input slots with `requiredTypes` (AND),
  a per-material storage capacity and per-shift maximum consumption; outputs
  remain concrete resources. The custom factory Inspector adds typed inputs by
  default. Each input has an explicit `Specific item` / `Resource types` mode,
  backed by the existing serialized `useResourceTypes` field. Specific inputs
  require their configured resource; category inputs select catalog categories
  by display name, with optional additional AND requirements. Both kinds can be
  mixed in the same factory. Switching modes retains the saved item and category
  requirements, and inactive shifts synchronize fixed inputs to their required
  item. Fixed item rows show the required resource's name and icon without a
  dropdown. Empty/unknown typed filters accept no
  materials. Overlapping capacity rules use the largest matching capacity for
  each resource; they never pool or add capacities across resources or rules.
- Each input's stable `inputId` survives reordering. `FactoryShift.inputSelections`
  owns one chosen material and delivery target per slot. Its old serialized
  `desiredResources` field is migrated on first synchronization and maintained
  only as an aggregated compatibility view. Building setters validate choices;
  materials can change while a shift is waiting, including when it is active.
  Preparation and production lock choices until those phases finish or are
  cancelled. The lock follows the backend's phase state on existing calendar-hour
  events and pauses with simulation time. The industrial panel refreshes existing
  dropdown availability on `FactoryRuntimeChanged`, closing an open selector when
  it locks without rebuilding the rows. Invalid slots prevent activation. Duplicating a shift copies its
  selections independently and leaves the copy inactive.
- Player-facing input rows show only the category requirement (for example
  `Metal`) beside the per-shift material dropdown, whose caption shows the chosen
  material's name and icon. Selecting Gold for
  one shift and Zinc for another changes each shift's collection/processing
  material while leaving the factory's Metal requirement unchanged. Numbered
  input prefixes are confined to the factory Inspector.
  Fixed inputs hide the dropdown and activate an authored layout spacer of the
  same width/flexibility, preserving the amount column. Amount text is left aligned
  in both modes; the selector installer applies this once and retains later edits.
- Collection demand and reservations use only the preparing shift's selected
  concrete materials. On shift start, `Building` snapshots and combines selected
  input consumption by resource (including the same material chosen in multiple
  slots). Allocation and proportional shortage coverage use that snapshot on
  the existing game-hour events, preserving simulation speed and pause behavior.
  Every matching material uses the input slot's same quantities and produces the
  same configured outputs. Collection targets do not limit consumption of existing
  stock: production uses available stock of the selected concrete item only and
  never substitutes a different item merely because it shares the required types.
- `Building.FactoryStorage` retains delivered stock across selection, filter and
  capacity changes. Existing stock is never discarded to synchronize metadata.
  New typed-input factories start empty; legacy exact-input factories retain
  their existing starter stock. Status UI shows only positive stored quantities
  and each material's individual limit, including any retained overfill.
- `Editor/FactoryInputSelectorSetup` upgrades the authored schedule row and vehicle
  dropdown prefabs with missing selectors/icons through Unity APIs after script
  reload. `Tools/Mini Colonies/Resources/Install Factory Input Selectors` repeats
  this operation. Existing controls and styling are retained, open Prefab Stages
  are skipped, and no scenes are saved. The original scene builders also install
  these controls when rebuilding. Runtime code only binds authored controls.
- Material dropdowns use the neighboring factory buttons' flat background,
  interaction colors and the resource label's font, size and text color, including
  popup items and scrollbars. The setup applies this once using an authored style
  version on the resource row; later prefab styling is preserved.
- Focused checks: `FactoryTypedInputTests`, `FactoryScheduleTests`, and
  `ResourceCatalogTests`. For an Editor smoke check, refresh scripts, verify the
  schedule prefab has its authored input dropdown, configure metal and fuel slots,
  check their category icons and the shared left-aligned amount column for typed
  and fixed inputs,
  choose materials on an active waiting shift and verify the selected name/icon remain
  visible in each dropdown. Verify a specific-item row has a name/icon and no
  dropdown. Leave the shift active and verify that collection
  requests only those choices, other stored materials remain visible with their
  own limits, and the choices lock at preparation start, remain locked during
  production and unlock after completion even if the shift stays active. No broad Play Mode run is
  required for the deterministic production rules.
- New slots initially choose the first matching catalog material. Factory designs
  should normally avoid duplicate ingredient slots; if multiple slots select the
  same material, their demand and consumption combine in one shared storage bin.
  Overlapping capacity rules use the largest limit. Stock left by a recipe change
  remains stored without automatic disposal.
  Categories are flat tags: metal ore does not automatically match metal unless
  the resource explicitly belongs to both categories.
- Building ports are captured into road/traffic snapshots so vehicles can route
  between exact port cells.
- `BuildingSystemBackend.CanPlaceBuilding` evaluates every rotated footprint
  cell against terrain, building occupancy, and authored roads. The same result
  gates placement and supplies per-cell preview validity.
  A flat, buildable cursor cell selects the desired elevation. On transitions or
  other ambiguous origin cells, the most represented flat, buildable footprint
  layer wins; ties favor the layer closest to the cursor cell's physical height,
  then the lower layer. Roads and occupancy do not influence that choice.
  The preview and placed prefab both use the selected elevation. Unconfigured
  cells retain the normal flat-terrain restrictions.
- `BuildingTile.allowedTileTypes` (Optional Types) and `requiredTileTypes`
  (Required Types, Any) store `WorldTileType` value identities: Flat Ground,
  Edge, Water Level, or a concrete resource Node/Deposit. No new ground assets
  or resource enum entries are needed. Resource matching reads the logical
  `WorldManager.ResourceMap` source, including its shape, at any depth; it never
  samples terrain paint or decorative prefab coverage. Resource icons come from
  `ResourceCatalog`; the three terrain icons are shared Inspector fields on
  `BuildingPlacementInput`.
- `Editor/WorldTileTypeDrawer` discovers resource choices from map-definition
  assets when its dropdown opens, deduplicating by stable resource ID and shape.
  This automatically includes new resource definitions without a second registry
  or per-repaint asset scans. `BuildingTileDrawer` exposes the two lists beside
  each footprint coordinate and port settings, in both Inspector rendering paths.
- `BuildingGroundPlacement.Allows` owns the deterministic per-cell ground rule.
  Optional types extend normal placement; a nonempty required list must match
  at least one entry. Required matches grant permission too; optional matches
  cannot satisfy required filters. For example, optional Water Level with required
  Oil Deposit still rejects bare water. Matching water allows its elevation,
  an edge allows transitions and their elevation, and a matching resource permits
  only that source's construction blocker. A resource on water or an edge
  therefore needs both relevant permissions. Borders, roads, building
  occupancy, and in-progress world generation always prevent placement.
  Rotation preserves cell rules and port data. Normal flat cells still select
  the building elevation through the existing algorithm; entirely special
  footprints without a flat buildable anchor use the rounded cursor height.
- Existing `BuildingGroundType` assets and the serialized `allowedGrounds` /
  `requireGroundMatch` fields remain readable as a legacy fallback when neither
  new list contains types. Editing either new list replaces that cell's legacy
  policy without deleting old references. The old creation menu is retired.
- `BuildingPlacementInput` renders a continuous footprint silhouette and hatch
  fill using `BuildingPlacement.png`; only invalid cells turn red. Geometry and
  rotated footprints are cached until the selection or rotation changes, and
  one shared unlit material uses per-cell color overrides. Preview meshes and
  material are released when placement ends or the input is disabled.
  Ground cells sample a complete, non-repeating gap filler (separate optional
  and required sprite references), with their shared ground icons centered in
  the gap; alternatives appear together. Combined port cells use a smaller
  corner port icon. These procedural world overlays derive directly from the
  footprint, while their styles and sprites remain Inspector-authored. Icon
  texture overrides support separate sprite sheets and compensate sprite pivots.
  Each `BuildingTile.footprintIconOverride` replaces all of that cell's terrain
  and resource rule icons with one centered sprite, including cells with no
  placement rules. Such cells also use the gap filler. Overrides survive
  rotation and do not modify validity or port indicators. Clearing the override
  restores shared icons when the preview is rebuilt. `BuildingTileDrawer`
  exposes the sprite beside the optional/required type lists.
  Placement and preview remain usable while simulation is paused and introduce
  no simulation timers. See `Buildings/BuildingGroundPlacement.md` for authoring
  and the focused validation workflow.
- `Editor/BuildingPlacementPreviewSetup` assigns missing hatch/outline/gap sprite
  references in loaded scenes and build scene processing, preserving authored
  overrides. Its `Tools/Mini Colonies` command can repeat the setup explicitly;
  loaded scenes are marked dirty for normal saving without overwriting pending edits.
- `BuildingData.ranges` owns named radii (0–64 tiles), colors, and optional
  required tile-type alternatives. Distance is Chebyshev distance from the nearest
  actual footprint cell, including the footprint itself, clipped to playable
  terrain. Separate ranges impose separate placement requirements. No elevation
  or line-of-sight restriction is added. Coverage-only ranges do not gate placement.
- `BuildingSystemBackend.RequestPlacementRanges` / `RequestBuildingRanges` create
  `BuildingRangeEvaluation` snapshots. Their deterministic `Advance` method scans
  bounded cell batches; `SimulationTaskManager` schedules production work with
  the shared real-frame budget. A missing manager uses a shared 1 ms/frame
  coroutine allowance. Backend disable cancels outstanding requests. Queries
  record unique active buildings whose footprint touches the covered area,
  excluding the source, for future effects without installing a specific effect.
- `CanPlaceBuildingWithRanges` combines normal footprint validation with current,
  completed required-range results. Preview and click use this same gate. Pending
  or stale required results cannot authorize placement. Snapshots capture building
  authoring revision, world resource/height identity, `WorldManager.PlacementRevision`,
  range configuration, and actual footprint cells. Generation, elevation and
  Inspector edits advance the world's placement revision. Existing footprint-only
  `CanPlaceBuilding` remains the compatibility endpoint for cell rules.
- `BuildingRangeDisplay` binds those backend results to independent keyed channels
  on `WorldPlacementHighlight`. Preview, hover and selection can show ranges
  without clearing each other's feedback. Channels reuse the existing animated
  dotted shader, terrain sampling and real-frame mesh budget; no per-frame area
  scans or cosmetic geometry rebuilds are added. Ranges retain their authored
  color regardless of requirement status; transparent/nonfinite colors receive
  a visible fallback without modifying serialized assets. Failed or pending
  requirements tint the footprint red through the backend's shared
  `AreRangeRequirementsSatisfied` predicate, while the owned tooltip names missing
  types. Valid ranges preserve the existing per-cell obstruction feedback.
  All presenters cancel
  pending work and clear their channels when their context changes or disables.
  Authoring and focused validation are documented in `Buildings/BuildingRanges.md`.

### Logical Road Networks
- `Roads/Network/RoadNetworkManager.cs`
- Maintains coarse road-network connectivity for building dispatch decisions.
- Rebuilds after road placement commits, so drag placement does not repeatedly
  recalculate building connectivity.

## Traffic Workflow

### 1. Authoring Snapshot
- `Roads/Traffic/Compilation/RoadNetworkSnapshotBuilder.cs`
- Reads `RoadSystemBackend` and `BuildingSystemBackend`.
- Compiles road profiles, road cells, building ports, and intersection policies
  into immutable `RoadNetworkSnapshot` data.

### 2. Graph Compilation
- `Roads/Traffic/Compilation/TrafficGraphCompiler.cs`
- Advances this stage order:
  `Normalize -> Classify -> GenerateLanes -> GenerateLegalMovements -> BuildMovementGeometry -> Publish`.
- `TrafficGraphCompilerStages.cs` still contains most of the compiler logic:
  section tracing, lane generation, movement generation, geometry slicing,
  building-port anchors, and snapshot publishing.

### 3. Validation
- `Roads/Traffic/Validation/TrafficGraphValidator.cs`
- The published `TrafficGraphSnapshot` is the single graph validation target.
- The compiler no longer runs a separate draft validator before publishing, and
  the managed adapter no longer revalidates the same graph.

### 4. Publication And Runtime Bridges
- `Roads/Traffic/TrafficSystemBackend.cs`
- Publishes the validated immutable graph atomically.
- Builds `ManagedTrafficGraphAdapter` output because the current conveyor and
  native pathfinding still operate on managed `TrafficNode`/`TrafficEdge`
  objects.
- Rebuilds `TrafficSpatialIndex`, intersection controllers, and
  `NativeTrafficGraph` after publication.

## Traffic Overview Visualization

- `PlayerViewModeManager` owns the traffic node option within Traffic view.
  Leaving Traffic resets the option; it does not alter simulation permissions,
  camera mode, or graph state.
- `PlayerViewModeUI` creates a Traffic nodes On/Off button beneath the existing
  traffic view button for existing scenes and only shows it in Traffic view.
- `Visuals/TrafficNodeViewOverlay` reads lane and movement geometry from the
  validated `CurrentTrafficGraphSnapshot`, with direction arrows following
  sample order. Each lane has a common outer line and a narrower inner stripe;
  the stripe color is derived deterministically from the road section and flow
  direction, so opposing ways and branches between network nodes are distinct.
  Intersection movements, mandatory lane merges, their corresponding lane
  expansions, and road-end U-turns are rendered as source-to-target color
  blends. Ordinary lane-change movements remain hidden to keep the network
  readable.
- The overlay listens for graph publication and option changes, builds bounded
  mesh chunks over multiple frames, and releases visuals on exit or disable.
  Once built, its meshes remain unchanged until one of those events occurs.
- The view manager Inspector exposes `Traffic Node Appearance`: the outer
  color, separate start-to-end and end-to-start inner base colors, outer and
  inner widths, branch color variation, height offset, arrow visibility, arrow
  color, length, width, and spacing in world units. Each branch receives a
  deterministic hue, saturation, and brightness variation around the base color
  for its direction. Inspector edits explicitly refresh a visible overlay in
  Play Mode; unchanged geometry does not rebuild per frame.

## Intersection Builder

- `PlayerViewModeManager.Start` installs `Player/IntersectionBuilderTool` beside
  the traffic overlay, so existing scenes gain the tool without scene edits.
  Intersection icons are visible in Traffic view independently of the Traffic
  nodes toggle. Opening an intersection switches Traffic nodes off in the view
  manager, updating the button too, and shows all lanes reaching that intersection.
  Leaving focus keeps the toggle off.
- `IntersectionBuilderSession` owns navigation only: Overview -> Intersection ->
  Entry -> Drawing. Right click retreats one step; graph publication cancels a
  drawing gesture and retains selections whose stable IDs still exist.
- The tool reads entry/exit endpoints and actual movement geometry from the
  validated graph. Click an entry to show its connections, click a path to remove
  it (clicking its connected exit does the same), or click the selected entry
  again then an exit to add a connection. Invalid
  exits, including same-leg U-turns unsupported by the compiler, use the
  unavailable color (gray by default).
- Instructions and edit feedback use the existing screen-corner `TooltipManager`
  through its owned `ShowForView` API. Gameplay tooltips remain restricted to
  Normal mode. The builder hides only its own tooltip on view exit or disable;
  it does not create a separate instruction canvas.
- `PlayerViewModeManager` serializes `Intersection Builder Appearance` for the
  runtime-created tool: separate intersection, entry, exit, and connected-exit
  sprites plus five control-type sprites, icon radii, line widths, height offset,
  arrows, and colors. A
  missing connected-exit sprite falls back to the regular exit sprite. Markers use world-space
  `SpriteRenderer` components, preserve their source aspect ratio, and scale their
  largest side to the configured diameter. Picking uses the projected world-space
  radius with a minimum eight-pixel target. Intersection, entry, and exit markers
  use the Inspector-configured hover tint while pointed at, then restore their
  overview, selected, connected, available, or unavailable tint when the pointer
  leaves. Available unconnected exits have their own Inspector color. Hovering a
  selected entry's path also applies the hover tint to that path's exit marker.
  Inspector edits and Undo explicitly rebuild visuals in Play Mode. Old radius
  settings are retained; obsolete outline and preview-strength fields stay hidden
  for serialized compatibility. `showIntersectionArrows` controls only intersection
  paths, highlights, and previews, leaving road lane arrows unchanged.
- Every intersection movement remains visible while editing. Other entries'
  paths use `backgroundPathWidth` and `backgroundPathColor` (including alpha).
  In Priority road mode, background paths from both configured priority
  approaches use `priorityRoadBackgroundPathColor`, so the priority pair remains
  visible without hover. Selected-entry and hovered paths retain their existing
  foreground colors.
  Only the selected entry's paths are clickable. Hover uses the same target as
  deletion, with connected exit markers taking precedence over path hits, and
  draws that path in `hoveredPathColor`. Its mesh changes only when the hovered
  movement changes; picking checks only the focused intersection during hover.
- `TrafficMovementCurve` owns the Bezier calculation, twelve-sample resolution,
  and polyline tangent sampling shared by the compiler and drawing preview.
  Duplicate endpoint samples retain a nonzero lane tangent. Hovering a valid exit
  previews the same samples that will be published for that connection; the
  snapped preview stays visible during compilation. Free cursor previews use
  the same curve rule with an estimated outgoing direction. Endpoint data and
  snapped samples are cached for the current gesture/target, keeping mouse updates
  bounded without rescanning road geometry every frame.
- `RoadSystemBackend.TrySetIntersectionConnection` checks current source revisions
  and `IntersectionConnectionEditing` validates lane membership, directions,
  permissions, and supported movements. An edit captures the intersection's
  current connections and incoming-lane baseline into `IntersectionData`, then
  changes only the requested pair. Explicitly cleared entries remain empty;
  incoming lanes added by later road construction receive their normal default
  movements. Legacy policies retain their existing automatic defaults. Edits
  persist in backend state across view changes and graph rebuilds; this does not
  introduce a save-to-disk system.
- One authoring notification and `CommitNetworkChanges` schedule the existing
  network/traffic rebuild. Further edits against stale snapshots are rejected
  with feedback until publication. Vehicles continue using the validated graph
  until its replacement is ready; the tool never edits runtime occupancy.
- Tool meshes are built in bounded chunks across frames and released on exit or
  disable. Entry changes keep the published visual root visible while its
  replacement is built, then swap the roots atomically to avoid a blank frame.
  Only the small mouse preview is rebuilt each frame while drawing; hover
  geometry is reused until its target changes.
- `UI/IntersectionControlPanel` is an authored hierarchy on the existing overlay
  Canvas, anchored at the screen's right edge with a configurable 20-unit inset
  and centered vertically. Anchors handle resizing without per-frame camera
  projection. `Editor/IntersectionControlPanelSceneBuilder` installs and wires the
  hierarchy once after scripts reload in Edit Mode, with a matching
  `Tools/Mini Colonies` menu command for manual installation. Runtime code only
  binds backend state and toggles the authored controls, so layout and appearance
  edits survive Play Mode. The panel component exposes `Right Screen Margin`;
  the old intersection distance remains hidden for serialized compatibility. Its
  building-panel-style card offers Free for all, First in/first out, Priority
  road, Traffic lights, and Smart traffic lights. Players can drag the empty card
  background within the screen bounds, including while paused; child controls
  retain their input behavior. Refreshing the focused intersection preserves the
  dragged position, while reopening or focusing another intersection restores
  the configured right-edge inset. The selected row expands its details. Priority road
  enters a world selection step for a pair of incoming approaches; Traffic
  lights exposes a debounced 5-120 second phase-duration slider. Changes mutate
  `IntersectionData` through `RoadSystemBackend` and schedule the normal atomic
  road/traffic rebuild without changing the intersection's lane connections.
- The authored panel also contains `ConnectionActions/DeleteAllConnections` and
  `ConnectionActions/RestoreDefaultConnections` buttons. The Editor installer
  adds missing controls to existing panels without replacing their authored
  hierarchy; runtime code only binds the serialized button references. Delete
  clears every incoming lane using an explicit empty connection policy and its
  current source-lane baseline. Restore removes custom and disabled rules and
  that baseline, allowing the compiler to regenerate normal default connections.
  Both actions retain the traffic mode, priority directions, duration, and lane
  signal indices. `RoadSystemBackend` validates current snapshot revisions and
  publishes one authoring change per reset. The builder cancels the active drawing
  or priority-selection gesture, keeps the intersection focused, and disables
  the reset buttons until the replacement traffic graph is published.
- Free for all remains the default. `PriorityRoadIntersectionController` gives
  configured approaches precedence while retaining the conflict-safe reservation
  checks. Each incoming lane has its own traffic light and an authored 0-9
  signal index. Defaults retain the existing groups of exact opposing approaches.
  `TrafficLightIntersectionController` cycles the indices present on incoming
  lanes in ascending numeric order, skipping gaps and wrapping to the lowest.
  Lanes with the same index receive green together; admission still uses the
  existing conflict checks. `IntersectionData` owns overrides keyed by approach
  direction and lane ordinal, retained across view changes, control-mode changes,
  and graph rebuilds. New lanes receive defaults; absent lanes do not create phases.
  Snapshot capture copies these values into immutable policy/node records, and
  controllers consume that published policy. Runtime phase state remains owned
  by `TrafficSystemBackend`, with notifications keyed by active index so phases
  splitting lanes on the same road still update their indicators.
- Smart traffic lights use the same lane groups, admission safety checks, and
  red/green overlay. `IntersectionData.SmartTrafficLightWaitSeconds` owns a
  separate 1-120 simulation-second threshold (default 10), captured in immutable
  authoring and graph snapshots. The authored panel's waiting slider updates it
  through `RoadSystemBackend`; its 0.3-second input debounce remains real time.
  The Editor installer adds the missing mode and slider to existing panels while
  preserving their authored controls and never saving unrelated pending scene edits.
  The conveyor reports stopped vehicles at the stop line before testing exit
  clearance. `TrafficLightIntersectionController` retains the current green until
  a red group's observed vehicle reaches the threshold, selecting the longest
  wait and breaking equal-time ties by signal index. Empty groups receive no turns.
  Each green lasts at least one simulation second to allow driver reaction and
  prevent rapid switching between overdue groups; conflict checks still gate entry.
  Waits advance only through the conveyor's fixed simulation steps, freeze on
  pause, and immediately follow speed changes. Departed or unobserved requests
  are pruned. Serving a group clears its red waits. Controller rebuilding retains
  active index and actual pending wait times for surviving lanes within smart mode,
  so threshold edits do not rescale elapsed waits. Switching modes clears demand.
  The conveyor excludes smart red-light waits from its 30-second stall recovery;
  vehicles still blocked on green remain eligible for recovery.
- Focused traffic-light intersections show a numbered circle beside every incoming
  lane entry. Left click increases its index and right click decreases it, clamped
  to 0-9. Both buttons are consumed by that control; right click elsewhere keeps
  the existing back navigation. Edits validate lane membership and source revisions,
  then use the normal backend notification and atomic graph rebuild. The controls
  and their text are released with the builder's visual root. Connection rules
  remain independently editable.
- `Intersection Builder Appearance` exposes the index background sprite, font
  override, text color, gradient, and hover lightening amount. Index colors sample
  the gradient at `index / 9`; hover lightens that color while retaining its alpha
  and restores it on exit. Labels inherit the authored intersection panel's game
  font when no override is assigned. Unassigned backgrounds fall back to the
  intersection sprite, then a runtime circle released on disable. Explicit
  appearance refreshes rebuild the controls even when gradient keys are edited
  in place. Index backgrounds compensate for their sprite pivot, and labels
  center their visible digit geometry in the background plane rather than using
  font line metrics or a separate height offset.
- `IntersectionTrafficLightOverlay` listens for controller phase changes and
  renders one small red/green icon per incoming lane in every player view,
  including Normal. Signals have their own Inspector sprite and fall back to the
  traffic-light type, entry, then intersection marker sprite when it is missing.
  `Traffic Light Icon Offset` is signed in world units: negative moves along the
  incoming road toward the intersection center, positive moves outward, and zero
  keeps the lane endpoint position. The serialized offset field and existing
  positive values are retained.
  Graph publication reuses matching signal objects, and rebuilt traffic-light
  controllers restore their active index and relative progress when that index
  remains present (otherwise they start at the lowest), so unrelated road and
  movement edits do not reset the lights. Indicators scale out and pop back in
  with an Inspector-configurable overshoot on phase changes; newly created
  signals use the same pop-in. Overview intersection markers keep the authored
  base icon beneath a smaller icon for the current control type.
- While choosing a priority road, hovering an incoming marker highlights every
  intersection movement belonging to that approach and the opposite approach
  that will be paired with it.
- `CameraController` owns smooth focus and overview restoration, including the
  camera rig offset. Its Inspector exposes `intersectionZoom` (default 4) and
  `intersectionFocusSmoothTime` (default 0.3 seconds). These settings are on
  SampleScene's CameraPivot; zoom edits also apply while focused. Focus uses unscaled time
  and temporarily holds movement/scroll input while editing. After leaving the
  intersection, camera movement or zoom input cancels the return animation and
  resumes normal controls from the currently displayed position and scale.

## Vehicle Routing Workflow

1. A building or vehicle requests a route.
2. `VehiclePathfindingTask` resolves start/end lane candidates and exact
   building-port anchors.
3. `NativeTrafficGraph` supplies Burst-friendly node, edge, and waypoint arrays.
4. `VehiclePathfindingJob` finds a legal path using permissions,
   capabilities, lane movements, and congestion penalties.
5. `TrafficRoute` returns the managed edge list needed by the conveyor.
6. `RouteCorridor` keeps stable graph IDs and route diagnostics for rebuilds
   and debug visibility.

## Vehicle Runtime Workflow

1. `ConveyorTrafficManager` owns active vehicle ticking.
2. `TrafficRuntimeState` owns authoritative edge occupancy.
3. `TrafficReservationService` owns authoritative connector reservations.
4. `LongitudinalController` handles speed/acceleration stepping.
5. `TacticalLanePlanner` chooses simple lane-change intent from the next route
   edge.
6. Intersection controllers decide admission for controlled movement edges.
7. The conveyor updates vehicle transforms and publishes performance counters.

`CornerSpeedPlanner` derives turn severity from the horizontal lane/movement
polyline when `TrafficEdge.RecalculateLength` rebuilds geometry. This is derived
geometry data, not vehicle state. During fixed simulation steps the conveyor
looks ahead along the route, including edge boundaries, and lowers its desired
speed for tight bends. Broad bends and hills retain cruising speed. The default
maximum reduction is 30%, editable through the conveyor's `Maximum Corner
Slowdown` Inspector field (zero disables it). Braking distance includes a small
jerk allowance; the existing longitudinal controller performs the actual braking
and acceleration. Queries allocate nothing and inspect at most eight upcoming
edges and 64 cached corner samples. Cornering follows simulation speed and
pause without a new timer or changing road/pathfinding costs.

The conveyor consumes a blocked vehicle's restart reaction delay as soon as
there is room to move, including when a more distant leader remains on its
route. A vehicle already moving clears the old blocked flag rather than taking
a second reaction pause at an intersection boundary. Pending restart delays
also hold the edge handoff; controller admission and clearance checks still
decide whether entry is safe.

## Debug Pop Vehicle Tool

- `UI/PlayerViewModeUI` creates a `Debug car spawns` toggle in its own row below
  the view controls, without covering the Traffic button. The toggle accepts
  world clicks only in Normal view and ignores clicks over UI.
- `Vehicles/DebugPopVehicleSpawner` owns the debug-only click workflow. A
  clicked vehicle carries no assigned population and approaches the nearest legal
  road-lane departure node. `VehicleAI` then tries factory destinations in distance
  order, considering only compatible entrances with published arrival anchors.
  Disconnected entrances are skipped; if one candidate has no legal directed
  route, the normal asynchronous pathfinder tries the next entrance/factory.
- The road trip uses the regular asynchronous path request, conveyor occupancy,
  and reservation systems. `VehicleAI` marks the vehicle as a one-way debug
  trip so it is destroyed after reaching the factory instead of entering the
  normal pop-delivery and return-home workflow. If no destination is reachable,
  the car remains visible with owned tooltip feedback and retries on
  `OnTrafficNetworkReady`, without per-frame retry polling. In-flight queries
  discard results from replaced graphs; disabling/destroying the vehicle cancels
  its coroutine, invalidates callbacks and unsubscribes. Graph rebuilding and
  debug stall recovery use the same selection flow rather than despawning the car.
  Path-search waits run in real frames because graph/pathfinding work proceeds
  while paused; the approach, driving and congested-lane entry waits remain on
  simulation time.

## Current Debt

- `TrafficGraphCompilerStages.cs` is still too broad. It should be split by
  compiler stage once behavior is stable.
- `ConveyorTrafficManager.cs` is still too broad. Following, connector
  reservations, intersection exits, graph-remap handling, and transform
  interpolation can be separated.
- `ManagedTrafficGraphAdapter` is a temporary bridge. It should disappear once
  pathfinding and the conveyor run directly on stable graph IDs.
- `TrafficEdge.occupants` and `TrafficEdge.reservations` remain only as mirrors
  for compatibility/debug consumers. Runtime code should use
  `TrafficRuntimeState` and `TrafficReservationService`.
- Profile compatibility defaults in `RoadProfile` and `VehicleTrafficProfile`
  can shrink after road and vehicle assets have persistent traffic profile keys.
