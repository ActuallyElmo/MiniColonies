# Traffic Performance Profile Notes

Status: Wave 5 instrumentation handoff, July 18, 2026.

## Implemented Counters

`TrafficSystemBackend.CurrentPerformanceSnapshot` now exposes:

- graph version, node count, edge count,
- indexed lane count and indexed lane segment count,
- closest-lane query candidate segment count and distance-test count,
- last route calculation time in milliseconds,
- last fixed traffic tick time in milliseconds,
- active vehicles, active lanes, reserved edges, and reservation contention events,
- the latest lower-frequency `TrafficCongestionSnapshot`.

`TrafficSpatialIndex` is the owner of closest-lane and port-cell endpoint lookup work. Route start/end selection should not scan every graph edge except during bootstrap fallback before an index is ready.

`ConveyorTrafficManager` continues to tick `_activeEdges`, not every lane in the graph, and publishes congestion snapshots every `0.5` simulation seconds rather than every movement tick.

## Initial Budgets For Representative Colonies

These are sign-off targets for Play Mode profiling, not behavior-changing constraints:

- closest-lane lookup should inspect bucket-local candidate segments instead of all road-lane segments;
- fixed traffic tick time should scale with active vehicles and active lanes, not total graph lanes;
- route calculation time should be tracked separately from fixed tick time;
- reservation contention should be visible as a counter before deeper policy tuning;
- steady fixed-tick code paths should not introduce recurring managed allocations beyond compatibility mirrors already listed in `architecture_revision.md`.

## Next Bottleneck To Measure

The next profiling pass should compare:

1. route job time against graph edge count,
2. closest-lane candidate segment counts against road density,
3. fixed tick time against active vehicle count,
4. reservation contention against transition/intersection queue density.

If those curves remain separated, later optimization should target regional compilation and simulation LOD. ECS or a larger storage rewrite should stay deferred until these counters show storage layout, rather than graph ownership, is the limiting factor.
