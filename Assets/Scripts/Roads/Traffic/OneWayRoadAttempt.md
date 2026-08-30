# One-Way Road Attempt

One-way roads are intentionally disabled for now.

The first implementation proved that asymmetric road direction is larger than a
quick road-type addition. A road edge needs one coherent traffic direction
between boundaries, where a boundary is an intersection, a dead end, or a road
type/directionality transition. That direction also needs to survive preview
transactions, upgrade drags, road type changes, and traffic graph compilation.

## What Was Tried

- One-way roads used the placement direction for new segments.
- Same-type one-way upgrades reversed the whole collected road edge instead of
  one cell at a time.
- Newly built one-way segments were normalized to match an existing same-type
  continuation when connected directly into it.
- Multiple lanes per one-way road were compiled by giving the road profile
  forward lanes and no reverse lanes.
- Incoming one-way lanes with no legal continuation were represented as
  explicit terminal movement rejections so validation could distinguish them
  from compiler bugs.

## Failure Modes Found

- A one-way road reversed between two intersections could leave incoming lanes
  at an intersection with no continuation, producing `UnmappedIncomingLane`.
- Connecting a one-way road to a two-way multi-lane road could leak stale
  directional authoring bits into the two-way section, producing
  `IllegalDirectionMovement`.
- Opposing one-way continuations connected directly to each other need segment
  normalization across the whole edge, not just the newest connection.
- Edge collection is ambiguous around intersections, road type changes, and
  preview cells. The rules need to be designed as a first-class authoring model,
  not patched per compiler error.

## Current State

- `RoadType.OneWayRoadsEnabled` is `false`.
- Road assets with `isTwoWay == false` are treated as two-way by player
  selection, road authoring logic, snapshot direction normalization, and road
  profile compilation.
- `Assets/Roads/SmallOneWayRoad.asset` has been converted back to a two-way
  small road asset.
- The exploratory one-way tests are ignored while the feature is parked.

## Recommended Next Pass

Before re-enabling one-way roads, add an explicit road-edge direction model
instead of deriving behavior from per-cell incoming/outgoing bitmasks alone.
That model should own:

- edge identity between boundaries,
- direction reversal as one transaction,
- connection rules at intersections and type transitions,
- compatibility behavior when one-way and two-way roads meet,
- migration/normalization of legacy directional bits.

After that, re-enable `RoadType.OneWayRoadsEnabled` and turn the ignored
one-way tests back on as the starting regression suite.
