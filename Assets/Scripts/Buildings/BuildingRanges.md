# Building ranges

Expand **Ranges** on any building asset. Add one named entry for each coverage
area or placement requirement:

| Example | Radius | Required Tiles (Any) |
| --- | --- | --- |
| Nearby buildings affected by a service | 5 | Empty |
| Water supply | 1 | Water Level |
| Concrete supply | 2 | Concrete Deposit |

Use the existing tile dropdown for terrain, resource nodes, and deposits.
Resource choices update automatically from map definitions. Water Level means
water-height terrain; Water Deposit means a generated Water resource source.

A radius is the distance from the nearest footprint cell, including diagonals.
Radius 1 adds one surrounding tile in every direction. The footprint itself is
included, and radius 0 covers only the footprint. Ranges follow irregular and
rotated footprints, are clipped to the playable map, and ignore elevation and
line of sight. Radius is limited to 0–64 tiles; malformed values fail placement
safely instead of creating unlimited searches.

Required tile entries in one range are alternatives: at least one matching tile
must occur anywhere in that area. Separate ranges must all pass, so Water supply
at radius 1 plus Concrete supply at radius 2 requires both. An empty required
list means coverage only and never waits for a search before allowing placement.
Tile presence can match beneath another building. A range requirement does not
grant permission to build on a blocking resource or terrain: footprint rules
remain independent.

## Player feedback

Ranges appear during placement and while hovering or selecting a placed
building. Each uses its authored color and the existing highlight material's
moving dotted outline. The line always retains its normal color, including when
a requirement fails. Transparent serialized colors receive a visible fallback
without rewriting the asset; a zero RGBA entry uses the default cyan. A failed
or pending requirement turns the placement footprint red, and a failed search
shows an owned tooltip identifying the missing tile types. Required placement
is held while a new search is pending; an old cursor position's result cannot
authorize a placement. Coverage-only queries run without blocking placement.

The shared material controls dash length, gap, pixel density, and movement
speed. **Tools > Mini Colonies > Configure Building Placement Preview** fills
missing range material and inner-corner sprite references on the scene's
`BuildingPlacementInput`. Existing overrides and unrelated scene edits are
preserved. Hover/selection use their existing highlight appearance. These
outlines are procedural world geometry derived from backend coverage, not a
new runtime UI hierarchy.

Range searches and outline meshes use real frame budgets and continue during
simulation pause. The shader's existing real-time clock animates the dots
without rebuilding geometry. Moving, rotating, changing tools/views, losing the
target, or disabling the presenter cancels its pending work and owned outlines.

## Using coverage for effects

Call `BuildingSystemBackend.RequestBuildingRanges(world, sourceBuilding)`.
The returned `BuildingRangeEvaluation` runs through `SimulationTaskManager`
(or a shared one-millisecond-per-frame fallback if that manager is absent).
Subscribe to `Finished`, check `IsComplete`, `!IsCancelled`, and `IsCurrent()`,
then read the corresponding `Results` entry:

- `Cells` contains the covered playable cells.
- `Buildings` contains unique active buildings touching any covered cell,
  excluding the source building. Centers do not determine membership.
- `RequirementMet` describes that range's optional tile-presence requirement.

These are snapshots. Building-authoring changes, world regeneration/terrain
edits, range-definition edits, and changed source footprints invalidate them.
Effects should request fresh snapshots when their inputs change and cancel
requests they no longer need. No specific buff, production multiplier,
harvesting, or ongoing effect is installed by the range feature itself.

The backend owns query results and placement acceptance. Presenters render
those results; they do not maintain a second affected-building registry.

## Validation

Run **BuildingRangeTests** in the Unity 6000.0.76f1 EditMode Test Runner. The
fixture drives work in explicit cell-count steps without elapsed-time waits.
It covers distance boundaries, combined requirements, custom resource IDs,
resource shapes/depths, invalid radii, playable bounds, pending/stale results,
cancellation, and deduplicated building membership. The existing
**BuildingPlacementPreviewTests** fixture covers footprint compatibility.

For lifecycle verification, run only
**WorldPlacementHighlightPlayModeTests.RangeChannelsAnimateIndependentlyAndCancelWithoutAffectingSelection**
and **WorldPlacementHighlightPlayModeTests.RangeDisplayKeepsTheSameVisibleColorWhenARequirementBecomesSatisfied**.
They exercise independent channels, range-only animation, cancellation, and
stable visible range color when requirements change. Existing tests also cover
simulation speeds and shared shader behavior.

For a manual smoke check, configure a test building with Water supply and
Concrete supply as above. Confirm each missing requirement separately blocks
placement, matching both permits placement, and a valid range cannot bypass an
invalid footprint. Move and rotate the ghost, then leave placement while a
large query is pending. Confirm no old outline appears later. Hover/select a
placed building, including while paused, and verify its ranges clear on
deselection, demolition, tool/view changes, and world regeneration.
