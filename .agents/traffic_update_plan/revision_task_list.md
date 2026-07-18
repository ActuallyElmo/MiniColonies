# Scalable Traffic Revision Agent Queue

Status: active executable backlog as of July 18, 2026.

Use this file to assign work to agents. `architecture_rules.md` is mandatory for every task, and `architecture_revision.md` defines the target component boundaries. This is the only active traffic execution queue.

## Queue Rules

- Complete tasks in dependency order. Do not skip a gate because later behavior is more visible.
- One agent owns one task's integration result even if research or tests are delegated.
- Agents must inspect the current dirty working tree and preserve unrelated work.
- Every task must leave the project buildable and the existing traffic path available unless its removal is explicitly in scope.
- New behavior stays behind a feature flag or adapter until its parity criteria pass.
- A task that discovers a missing contract updates the architecture documents before broadening implementation.
- Do not combine a compiler, runtime-state, routing, and presentation change into one task.
- Run `dotnet build Assembly-CSharp.csproj --no-restore --nologo` after C# changes.
- Prefer EditMode tests for pure compiler/controller logic and focused PlayMode scenarios for runtime integration.
- Record structured diagnostic codes in tests; do not rely only on log text.

## Required Task Report Template

Each agent returns:

```text
Task:
Status:
Files changed:
Contracts added or changed:
Ground rules checked:
Tests/build:
Diagnostics added:
Compatibility path:
Deferred items:
Risks for next task:
```

## Wave 0 - Freeze And Characterize

### R0 - Baseline Inventory And Characterization Harness

Owner role: traffic integration agent

Dependencies: none

Status: completed on July 18, 2026. See `current_behavior_baseline.md`.

Primary files:

- `Assets/Scripts/Roads/RoadSystemBackend.cs`
- `Assets/Scripts/Roads/Traffic/TrafficSystemBackend.cs`
- `Assets/Scripts/Roads/Traffic/TrafficGenerationTask.cs`
- `Assets/Scripts/Roads/Traffic/TrafficNetworkInfo.cs`
- `Assets/Scripts/Roads/Traffic/ConveyorTrafficManager.cs`
- `Assets/Scripts/Vehicles/VehicleAI/VehiclePathfindingTask.cs`
- `Assets/Scripts/Vehicles/VehicleAI/VehiclePathfindingJob.cs`
- `Assets/Scripts/Vehicles/VehicleAI/VehicleAI.cs`
- new tests under `Assets/Tests/EditMode/Traffic` and `Assets/Tests/PlayMode/Traffic` where project test assembly structure permits
- `.agents/traffic_update_plan/current_behavior_baseline.md`

Work:

- Inventory which current traffic behaviors and architectural responsibilities are fully implemented, partially implemented, or absent.
- Document current feature flags and scene dependencies.
- Capture the current graph counts and route results for minimal deterministic road layouts.
- Add characterization helpers that can build road snapshots or graph fixtures without scene-wide manual setup.
- Add failing or quarantined tests that reproduce:
  - required lane change before an intersection,
  - smooth following of a moving leader,
  - smooth stop-line approach,
  - 4-lane to 2-lane transition reachability,
  - 2-lane to 4-lane transition reachability,
  - two-way to one-way legality,
  - building-port to building-port reachability.
- Record the far-segment-endpoint versus adjacent-direction-bit inconsistency as a baseline defect.
- Record current allocations and tick time only as a baseline; do not optimize yet.

Do not:

- change pathfinding choice,
- retune braking,
- redesign graph records,
- repair the characterized defects inside this task.

Done when:

- `current_behavior_baseline.md` names the current authoritative code path for generation, routing, movement, and recovery.
- Each known symptom has a deterministic reproduction or a documented reason it still requires a manual scene.
- Later agents can compare graph records and vehicle motion against the baseline.
- The project builds.

### Gate 0

Status: satisfied. R0 distinguishes topology failures from runtime-control failures. R1 is unblocked.

## Wave 1 - Stable Contracts

### R1 - Stable IDs, Profiles, And Diagnostic Contracts

Owner role: traffic data-contract agent

Dependencies: R0

Status: ground-road implementation completed on July 18, 2026. Automated tests omitted by user direction.

Primary files:

- new `Assets/Scripts/Roads/Traffic/Model/TrafficIds.cs`
- new `Assets/Scripts/Roads/Traffic/Model/RoadProfile.cs`
- new `Assets/Scripts/Roads/Traffic/Model/VehicleTrafficProfile.cs`
- new `Assets/Scripts/Roads/Traffic/Diagnostics/TrafficDiagnostic.cs`
- `Assets/Scripts/Roads/RoadType.cs`
- `Assets/Scripts/Vehicles/VehicleData.cs`

Work:

- Add value-type IDs for graph version, road section, lane, movement, controlled node, building port anchor, and simulation vehicle.
- Give every ID an explicit invalid value, equality, deterministic ordering, and debug representation.
- Define compiled road and vehicle traffic profiles.
- Add compatibility conversion from existing `RoadType` and `VehicleData` assets.
- Separate comfortable service deceleration from emergency deceleration.
- Add desired time headway and maximum jerk with safe serialized defaults.
- Standardize traffic field names and calculations on Unity world units and seconds.
- Define vehicle capability and road permission masks without concrete subclass checks.
- Define structured diagnostic severity, code, source context, and collection APIs.

Do not:

- migrate current movement yet,
- place occupant lists in profile or graph records,
- make IDs depend on Unity instance IDs,
- encode list index alone as stable identity.

Done when:

- IDs are deterministic for equal normalized source data.
- Existing road and vehicle assets compile into valid profiles without manual repair.
- Profile validation rejects impossible values with structured diagnostics.
- No current vehicle behavior changes when compatibility defaults are used.
- Unit tests cover ID equality/order and profile migration.

### R2 - Immutable Road Input Snapshot

Owner role: road snapshot agent

Dependencies: R1

Status: implementation completed on July 18, 2026. Automated tests omitted by user direction; user verification pending before R3.

Primary files:

- new `Assets/Scripts/Roads/Traffic/Compilation/RoadNetworkSnapshot.cs`
- new `Assets/Scripts/Roads/Traffic/Compilation/RoadNetworkSnapshotBuilder.cs`
- `Assets/Scripts/Roads/RoadSystemBackend.cs`
- `Assets/Scripts/Roads/Traffic/TrafficSystemBackend.cs`
- building port source files as required

Work:

- Capture road cells, physical connections, legal directionality, compiled road profile IDs, required geometry/elevation inputs, intersection policies, and exact building-port cells.
- Stamp the snapshot with the road authoring revision.
- Copy mutable source collections before queuing long-running work.
- Ensure every datum needed for topology classification is present.
- Make snapshot records independent from `RoadCell`, `RoadType`, `Building`, and other mutable managed objects.
- Reject publication work when the source revision changes.

Do not:

- query `RoadSystemBackend.Instance.Roads` from downstream compiler stages,
- retain scene object references in the snapshot,
- build lane geometry in the snapshot builder.

Done when:

- Identical authoring state produces equal normalized snapshot records.
- Mutating the live road dictionary after capture does not alter the snapshot.
- Building ports retain exact port-cell identity.
- A changed road revision invalidates an in-flight result.
- Snapshot creation has deterministic tests for one-way and multi-lane cells.

## Wave 2 - Compiler And Validation

### R3 - Immutable Traffic Graph Records And Compiler Pipeline

Owner role: traffic compiler agent

Dependencies: R1, R2

Status: completed on July 18, 2026. The compiler now publishes immutable section, lane, movement, movement-owner, lane-segment, geometry, controlled-node, and exact building-port anchor records. Reopened semantic gates were addressed: building port cells split compilation as exact boundaries; port anchors carry lane-distance positions and unreachable authored ports are error diagnostics; all movements reference stable movement owners and lane traversal segments; traversal segment geometry slices and preserves lane polyline samples; movement segment attachment uses lane-local ordinal boundary rules rather than hash order; duplicate section, lane, movement, geometry, controlled-node, owner, segment, and port IDs are validated before snapshot dictionary construction; transition owner IDs are per boundary edge and transition owners publish even when no movement is emitted; two-way sections cannot silently publish one legal direction; `ApproachLegMask` is explicit non-authoritative leg metadata; runtime compiler advancement schedules one pure stage at a time as background work while `TryCompile` remains synchronous for tools/tests. `dotnet build Assembly-CSharp.csproj --no-restore --nologo` passed with 0 warnings and 0 errors.

Primary files:

- new `Assets/Scripts/Roads/Traffic/Model/TrafficGraphSnapshot.cs`
- new `Assets/Scripts/Roads/Traffic/Compilation/TrafficGraphCompiler.cs`
- new compiler stages under `Assets/Scripts/Roads/Traffic/Compilation`
- compatibility changes in `Assets/Scripts/Roads/Traffic/TrafficGenerationTask.cs`
- compatibility changes in `Assets/Scripts/Roads/Traffic/TrafficSystemBackend.cs`

Work:

- Define immutable road-section, lane, movement, controlled-node, geometry, and port-anchor records.
- Split compilation into normalize, classify, lane generation, legal movement generation, movement geometry, validation, and publish stages.
- Carry immediate approach direction or normalized leg ID separately from far road-section endpoints.
- Resolve exactly one stable transition owner per road-type boundary.
- Preserve lane ordinal and direction conventions explicitly.
- Generate required lane continuity and mandatory transition mappings before optional lane-change movements.
- Store movement rejection reasons during policy evaluation.
- Keep generation time-sliced/job-compatible.

Required transition cases:

- same profile and directionality,
- equal lane count with changed speed/profile,
- lane reduction,
- lane expansion,
- two-way to one-way,
- one-way to two-way,
- adjacent transition and intersection,
- short section between controlled nodes,
- road-end fallback where legal.

Do not:

- generate an illegal movement and discourage it with cost,
- use waypoint proximity to decide lane identity,
- let geometry choose a different target lane,
- publish partial graph records.

Done when:

- Compiler output is deterministic for a normalized snapshot.
- All movement records reference stable existing lane IDs.
- Direction/leg metadata is valid for multi-cell road sections.
- Transition mappings are policy output rather than scattered index conditionals.
- The graph remains immutable after construction.

### R4 - Graph Validator And Topology Test Matrix

Owner role: traffic validation agent

Dependencies: R3

Status: completed on July 18, 2026. Added `TrafficGraphValidator` as a pure snapshot validation pass and wired it into the compiler publish stage so error diagnostics prevent `TrafficGraphCompiler.Result` from being assigned. Validator checks G8 invariants for unique IDs, referenced IDs, lane/section direction agreement, movement lane/owner/segment/geometry references, permission/capability compatibility, controlled-node references, lane continuity, transition mapping coverage, positive geometry, and exact building-port reachability. Added generated EditMode-style validator fixtures under `Assets/Tests/EditMode/Traffic`; `dotnet build Assembly-CSharp.csproj --no-restore --nologo` and `dotnet test Assembly-CSharp.csproj --no-build --nologo` passed.

Primary files:

- new `Assets/Scripts/Roads/Traffic/Validation/TrafficGraphValidator.cs`
- new validators under `Assets/Scripts/Roads/Traffic/Validation`
- new fixture builders under `Assets/Tests/EditMode/Traffic`
- `.agents/traffic_update_plan/testing_checklist.md`

Work:

- Implement every invariant in `architecture_rules.md` G8.
- Separate error, warning, and informational diagnostics.
- Block graph publication on error.
- Add reachability queries for exact building-port anchors.
- Add policy-specific validation for transitions, road ends, controlled movements, permissions, and connector geometry.
- Add small generated fixtures rather than relying solely on `SampleScene`.
- Update the manual checklist with validator diagnostic expectations.

Required tests:

- all source lanes mapped in 4-to-2,
- natural continuation exists in 2-to-4,
- opposing travel into one-way is absent,
- no duplicate transition owner,
- adjacent intersection owns its lane mapping,
- missing connector target fails,
- invalid direction/leg ID fails,
- negative/empty drivable geometry fails,
- exact building port is retained,
- disconnected legal network reports unreachable port pair,
- stable IDs remain stable when unrelated cells are added outside the fixture.

Do not:

- repair invalid graphs inside the validator,
- downgrade legality or missing-reference errors to warnings,
- test only graph counts without semantic assertions.

Done when:

- Known invalid fixtures cannot publish.
- Valid fixtures publish without error diagnostics.
- Each error includes graph/source context and a stable code.
- The original lane-count transition symptom is caught before pathfinding.

### R5 - Current Runtime Compatibility Adapter

Owner role: traffic integration agent

Dependencies: R3, R4

Status: completed on July 18, 2026. Added `ManagedTrafficGraphAdapter` and comparison diagnostics on `TrafficSystemBackend`. The adapter validates snapshots before conversion, builds managed `TrafficNode`/`TrafficEdge` objects from lane segment and movement geometry, shares lane boundary nodes by lane-distance identity, preserves bidirectional stable-ID lookups for lane segments and movements, and exposes graph version/stable IDs through managed edges, native pathfinding edge payloads, and hover diagnostics. Snapshot adapter output is not published when validation fails. As of R14, compiled immutable graphs always publish through this adapter; the legacy generated managed graph remains only as a build/comparison bridge while generation code is retired. `dotnet build Assembly-CSharp.csproj --no-restore --nologo` passed with 0 warnings and 0 errors.

Primary files:

- new `Assets/Scripts/Roads/Traffic/Compatibility/ManagedTrafficGraphAdapter.cs`
- `Assets/Scripts/Roads/Traffic/TrafficNetworkInfo.cs`
- `Assets/Scripts/Roads/Traffic/TrafficSystemBackend.cs`
- `Assets/Scripts/Vehicles/VehicleAI/NativeTrafficGraph.cs`
- debug files

Work:

- Convert a validated `TrafficGraphSnapshot` into current managed `TrafficNode` and `TrafficEdge` objects.
- Preserve a bidirectional lookup between stable IDs and compatibility objects.
- Build native pathfinding arrays from the same immutable snapshot or a provably equivalent adapter output.
- Add a feature flag selecting legacy generation or snapshot generation.
- Add a comparison mode that reports semantic differences without allowing both graphs to mutate runtime state.
- Update hover/debug output to display stable IDs and graph version.

Do not:

- add new behavior to compatibility objects,
- treat managed object identity as stable identity,
- publish adapter output when validation failed.

Done when:

- Existing waypoint/conveyor paths can run from snapshot-compiled graph data.
- Comparison fixtures show explained differences only.
- Debug output links managed edges back to stable lane/movement IDs.
- Disabling the new path restores the prior implementation during migration.

### Gate 1

Do not migrate movement ownership until snapshot generation, validation, and compatibility publication pass the topology matrix.

## Wave 3 - Runtime And Vehicle Dynamics

### R6 - Traffic Runtime State And Occupancy Services

Owner role: traffic runtime agent

Dependencies: R5

Status: completed on July 18, 2026. Added stable `VehicleSimulationId` assignment, `TrafficRuntimeState`, and `TrafficReservationService`. `ConveyorTrafficManager` now registers, unregisters, transfers, updates, and orders active vehicles through runtime services; edge occupant and reservation lists remain synchronized compatibility mirrors for existing readers. Ordering uses stable vehicle IDs instead of `GetInstanceID()`, the stuck recovery timer is the resolved 30 seconds, and rebuild/unregister paths clear service-owned occupancy. `dotnet build Assembly-CSharp.csproj --no-restore --nologo` passed with 0 warnings and 0 errors.

Primary files:

- new `Assets/Scripts/Roads/Traffic/Runtime/TrafficRuntimeState.cs`
- new `Assets/Scripts/Roads/Traffic/Runtime/TrafficOccupancyService.cs`
- new `Assets/Scripts/Roads/Traffic/Runtime/TrafficReservationService.cs`
- compatibility changes in `Assets/Scripts/Roads/Traffic/ConveyorTrafficManager.cs`
- compatibility changes in `Assets/Scripts/Roads/Traffic/TrafficNetworkInfo.cs`
- compatibility changes in `Assets/Scripts/Vehicles/VehicleAI/VehicleAI.cs`

Work:

- Add stable vehicle simulation IDs.
- Move authoritative occupants and reservations into runtime state keyed by stable IDs.
- Implement atomic insert, transfer, reserve, release, and unregister commands.
- Maintain deterministic ordered occupancy for active lanes and movements.
- Expose read-only leader and nearby-gap queries.
- Keep compatibility mirrors on `TrafficEdge` read-only or adapter-driven until removed.
- Remove `VehicleAI` and presentation-object identity from simulation ordering.

Do not:

- let multiple systems mutate occupant lists,
- mutate graph snapshot records,
- scan every graph lane per tick,
- expose mutable collections to controllers or vehicles.

Done when:

- Runtime state is the sole authority for occupancy and reservations in the new mode.
- Two simultaneous transfers into the same space resolve deterministically and atomically.
- Register/unregister/rebuild operations leave no orphan occupants or reservations.
- Steady-state runtime APIs allocate no managed garbage per vehicle tick.

### R7 - Longitudinal Controller And Fixed Simulation Step

Owner role: vehicle dynamics agent

Dependencies: R1, R6

Status: completed on July 18, 2026. Added `TrafficSimulationClock` fixed-step accumulation and `LongitudinalController` for acceleration/service-braking/emergency-braking/jerk-limited speed updates. `ConveyorTrafficManager.Update` now advances deterministic fixed traffic ticks instead of simulating directly on render `Time.deltaTime`; follower constraints include desired time headway plus relative leader speed already propagated through stop-distance speed targets. Vehicle profile accessors now expose emergency deceleration, desired headway, and jerk. `dotnet build Assembly-CSharp.csproj --no-restore --nologo` passed with 0 warnings and 0 errors.

Primary files:

- new `Assets/Scripts/Roads/Traffic/Runtime/LongitudinalController.cs`
- new `Assets/Scripts/Roads/Traffic/Runtime/TrafficSimulationClock.cs`
- `Assets/Scripts/Roads/Traffic/ConveyorTrafficManager.cs`
- `Assets/Scripts/Vehicles/VehicleData.cs`
- motion tests

Work:

- Introduce a fixed traffic simulation tick and presentation interpolation state.
- Build a single route-distance constraint context across transparent lane-section boundaries.
- Implement desired time-headway following with relative leader speed.
- Combine free speed, leader, stop line, reservation, speed-limit, and route-end constraints.
- Clamp comfortable acceleration, service braking, emergency braking, and jerk separately.
- Retain a final overlap-prevention clamp and emit `EmergencySafetyClamp` when used.
- Provide deterministic profile-based driver variation only if needed; default to no variation.

Do not:

- tune around topology failures,
- use a fixed following gap as the only moving-leader model,
- grant reservations from the longitudinal controller,
- make simulation outcomes depend on render `Update()` frequency.

Done when:

- A follower converges smoothly to a moving leader's speed at the desired headway.
- A vehicle begins braking early enough for a denied stop line without a final abrupt normal-mode clamp.
- Acceleration change respects jerk limits.
- Emergency clamps do not activate in valid baseline scenarios.
- Results are materially equal across different render frame rates.

## Wave 4 - Routing And Lane Behavior

### R8 - Strategic Route Corridor

Owner role: pathfinding agent

Dependencies: R5, R6

Status: completed on July 18, 2026. `RouteCorridor` now returns the immutable graph version, exact start/target building-port anchor IDs and cells, ordered road sections, stable lane segments, required non-discretionary movements, acceptable lane sets per section, reroute reason, and structured failure diagnostics while preserving the exact managed compatibility edge route in `TrafficRoute.ManagedEdges`. The path task resolves exact immutable building-port anchors without destination proximity fallback, compiles the requesting vehicle profile, and filters start/end candidates plus every Burst A* expansion by road permissions and vehicle capabilities. Native edges carry the legality masks and graph version; requests reject native, immutable, in-flight, and optional congestion-snapshot version mismatches explicitly. `StrategicCongestionSnapshot` supplies immutable graph-versioned penalties without exposing live runtime occupancy to Burst. `ConveyorTrafficManager` continues to reject stale route entry, and the previous cost-as-legality fallback remains absent. `dotnet build Assembly-CSharp.csproj --no-restore --nologo` passed with 0 warnings and 0 errors.

Primary files:

- new `Assets/Scripts/Vehicles/VehicleAI/RouteCorridor.cs`
- new strategic pathfinding job/task files
- compatibility changes in `Assets/Scripts/Vehicles/VehicleAI/VehiclePathfindingJob.cs`
- compatibility changes in `Assets/Scripts/Vehicles/VehicleAI/VehiclePathfindingTask.cs`
- `Assets/Scripts/Vehicles/VehicleAI/TrafficRoute.cs`
- `Assets/Scripts/Vehicles/VehicleAI/NativeTrafficGraph.cs`

Work:

- Route between exact building-port anchors on the immutable graph.
- Return road sections, required controlled movements, and acceptable downstream lane sets.
- Apply vehicle capability/permission legality before search expansion.
- Keep illegal movements absent.
- Accept an immutable congestion snapshot with a graph version.
- Keep an adapter capable of producing an exact compatibility edge route while tactical planning is introduced.
- Add explicit reroute reason and failure diagnostics.

Do not:

- select every discretionary lane change for the full journey,
- read live runtime occupancy in Burst,
- fall back to a proximity-selected destination lane when exact port identity exists,
- make congestion cost determine legality.

Done when:

- Strategic routes survive equivalent lane-layout changes when the road corridor remains legal.
- Different vehicle capability profiles receive only permitted corridors.
- Exact port-to-port tests pass across transitions and intersections.
- Route results reject graph-version mismatch.

### R9 - Tactical Lane Planner

Owner role: lane behavior agent

Dependencies: R7, R8

Status: completed on July 18, 2026. Added `TacticalLanePlanner`, tactical decision/reason records, and per-vehicle last decision state. `ConveyorTrafficManager` now evaluates a bounded next-edge horizon every fixed traffic tick and distinguishes keep-lane, required controlled movement preparation, forced merge preparation, wait/replan-capable decision kinds without creating runtime graph connections or making discretionary overtaking changes. `dotnet build Assembly-CSharp.csproj --no-restore --nologo` passed with 0 warnings and 0 errors.

Primary files:

- new `Assets/Scripts/Roads/Traffic/Runtime/TacticalLanePlanner.cs`
- new tactical state and decision records
- `Assets/Scripts/Roads/Traffic/ConveyorTrafficManager.cs`
- `Assets/Scripts/Vehicles/VehicleAI/VehicleAI.cs`
- tactical tests

Work:

- Implement bounded-horizon lane selection.
- First support:
  - keep lane,
  - mandatory preparation for a required downstream movement,
  - forced lane-drop merge,
  - retry and strategic replan when no maneuver remains feasible.
- Use predicted front and rear clearance through reservation requests.
- Add hysteresis, minimum commitment time, and cooldown.
- Produce explicit decision reasons and failure reasons.
- Keep discretionary congestion/overtaking changes behind a later feature flag.

Do not:

- create graph connections at runtime,
- jump directly between adjacent lane records,
- oscillate because another lane has a marginally lower instantaneous cost,
- force a late illegal maneuver to preserve the strategic route.

Done when:

- Vehicles prepare for required turns within the configured horizon.
- A blocked maneuver waits or replans without clipping or oscillation.
- Forced 4-to-2 merges select legal target lanes deterministically.
- Optional lane changes can be disabled without breaking route reachability.
- Single-lane roads require no special-case path.

### R10 - Stable Movement Reservations And Controller Policies

Owner role: conflict-control agent

Dependencies: R6, R9

Status: completed on July 18, 2026. Added stable `TrafficMovementRequest` records carrying vehicle simulation ID, graph version, and movement ID. `ConveyorTrafficManager` now submits controller admission through this stable request contract. FIFO and free-for-all controllers retain compatibility overloads but queue/tie-break with stable vehicle/movement IDs instead of `GetInstanceID()`. Reservation mutations flow through `TrafficReservationService`, and existing edge reservation lists remain compatibility mirrors. `dotnet build Assembly-CSharp.csproj --no-restore --nologo` passed with 0 warnings and 0 errors.

Primary files:

- new runtime movement-request records
- revised intersection controller contracts
- revised merge policy contracts
- compatibility changes in `IIntersectionController.cs`
- compatibility changes in FIFO/free-for-all controllers
- `Assets/Scripts/Roads/Traffic/ConveyorTrafficManager.cs`

Work:

- Move intersection and merge requests to stable movement IDs and graph versions.
- Define conflict envelopes and deterministic acquisition/release lifecycle.
- Keep lane-drop zipper policy separate from intersection controllers.
- Convert controller denials into longitudinal constraints.
- Preserve rear-clearance tracking through runtime reservations.
- Add structured denial and timeout diagnostics.

Do not:

- let controllers move vehicle transforms,
- let controllers edit graph topology,
- share mutable reservation collections with vehicles,
- use automatic teleport as deadlock resolution.

Done when:

- Conflicting requests never hold overlapping grants.
- Non-conflicting movements may proceed according to the configured policy.
- Zipper merge order is deterministic under parallel queues.
- Reservation release waits for the full vehicle envelope to clear.
- Rebuild or vehicle removal releases all grants.

### Gate 2

Do not add discretionary overtaking, traffic lights, emergency priority, or advanced driver personalities until mandatory tactical planning and smooth longitudinal control pass soak tests.

## Wave 5 - Rebuilds, Extensibility, And Scale

### R11 - Versioned Graph Rebuild And Active-Vehicle Remap

Owner role: traffic integration agent

Dependencies: R6, R8, R10

Status: completed on July 18, 2026. `TrafficSystemBackend` now rejects stale/invalid compiled immutable snapshots before mutating runtime state, completes outstanding simulation jobs before native buffer swaps, rebuilds stable lane-segment and movement lookup tables during atomic publication, and notifies `ConveyorTrafficManager` with the published graph version. `ConveyorTrafficManager.HandleTrafficGraphRebuilt` clears old occupants/reservations/controllers before remapping every active vehicle onto current managed graph objects by stable IDs. Vehicles whose route corridor cannot be exactly remapped are detached into a safe non-traffic state and sent through the graph-rebuild reroute flow, preserving the existing exact start/target port corridor metadata. Stale native/snapshot route jobs, route entry commands, and reservation writes reject graph-version mismatches. `dotnet build Assembly-CSharp.csproj --no-restore --nologo` passed with 0 warnings and 0 errors.

Primary files:

- graph publication service
- runtime remap service
- `Assets/Scripts/Roads/Traffic/TrafficSystemBackend.cs`
- `Assets/Scripts/Roads/Traffic/ConveyorTrafficManager.cs`
- `Assets/Scripts/Vehicles/VehicleAI/VehicleAI.cs`

Work:

- Publish graphs atomically.
- Remap vehicles through stable semantic keys when their lane/movement still exists.
- Reroute vehicles when exact remap is impossible.
- Preserve exact home/destination building-port identity.
- Define safe inactive/recovery state when neither remap nor reroute succeeds.
- Reject stale reservation and route commands by graph version.

Done when:

- No runtime record holds references into the old graph after publish.
- Unaffected vehicles continue without visible teleport where exact remap exists.
- Affected vehicles reroute or recover with structured reasons.
- Rapid consecutive road edits cannot publish an obsolete compilation.

### R12 - Data-Driven Variation Proof

Owner role: extensibility agent

Dependencies: R7, R8, R9, R10

Status: completed on July 18, 2026. Added representative profile-only variation support through expanded `VehicleCapabilityMask`/`RoadPermissionMask` flags (`HeavyVehicle`, `ServiceRoad`), plus `TrafficProfileLegality` validation for road/vehicle permission-capability overlap and required movement support. EditMode tests demonstrate a service/heavy vehicle permission difference and a road movement-policy difference without adding concrete vehicle, road asset-name, or coordinate branches to central traffic loops. `dotnet test Assembly-CSharp.csproj --no-build --nologo` exited 0, and `dotnet build Assembly-CSharp.csproj --no-restore --nologo` passed with 0 warnings and 0 errors.

Primary files:

- road and vehicle profiles
- policy registries
- tests and sample assets only as necessary

Work:

- Add one representative vehicle variation using only profile/capability data.
- Add one representative road variation using profile and compiler policy data.
- Demonstrate a vehicle permission difference without a concrete-class branch.
- Demonstrate a road movement difference without an asset-name or coordinate branch.
- Document the extension steps in `architecture_revision.md` if implementation exposes missing steps.

Done when:

- Both variations pass routing, movement, and validation tests.
- Central traffic loops do not change to recognize either concrete type.
- Invalid capability/road combinations fail with a legality diagnostic.

### R13 - Spatial Index, Congestion Snapshot, And Profiling

Owner role: traffic performance agent

Dependencies: R6, R8, R11

Status: completed on July 18, 2026. Added `TrafficSpatialIndex` and wired closest-lane plus exact port-edge lookups through indexed buckets, retaining all-edge scans only as bootstrap fallback. Added `TrafficPerformanceSnapshot`/`TrafficCongestionSnapshot` counters for graph/index size, closest-lane candidate work, route time, tick time, active vehicles, active lanes, reserved edges, and reservation contention. `ConveyorTrafficManager` continues to tick active edges instead of all lanes, publishes congestion snapshots at a lower cadence than fixed movement ticks, and exposes immutable graph-versioned `StrategicCongestionSnapshot` penalties to route jobs without live occupancy reads. `dotnet build Assembly-CSharp.csproj --no-restore --nologo` passed with 0 warnings and 0 errors.

Primary files:

- new lane/port spatial index
- congestion snapshot builder
- native graph/index buffers
- profiler counters

Work:

- Replace all-edge closest-lane scans with an indexed lookup.
- Generate congestion snapshots separately from the fixed movement tick.
- Iterate active lanes rather than every lane in steady-state movement.
- Measure allocations, compiler time, route time, tick time, active vehicles, active lanes, and reservation contention.
- Establish budgets from representative colony sizes before deeper optimization.
- Identify boundaries for later regional compilation and simulation LOD.

Do not:

- change behavior to hit a performance number,
- introduce ECS before profiling proves storage is the limiting factor,
- cache mutable managed object references across graph versions.

Done when:

- Closest-lane/port queries do not scale linearly with all graph edges.
- Steady traffic tick has zero managed allocations.
- Profiles distinguish graph-size cost from active-traffic cost.
- Performance results and next bottleneck are documented.

### R14 - Compatibility Removal And Architecture Audit

Owner role: senior integration agent

Dependencies: R11, R12, R13 and all required parity tests

Status: completed on July 18, 2026 as the migration-safe compatibility-removal and audit pass for this wave. Removed the obsolete dirty-cell-only active-rebuild branch and the obsolete `useSnapshotCompatibilityAdapter` publication mode: when a compiled immutable graph exists, managed runtime state now publishes only through `ManagedTrafficGraphAdapter`; legacy managed generation output is retained only as transient comparison/build input. The remaining compatibility consumers are intentional migration bridges: `TrafficRoute.ManagedEdges`, `TrafficEdge.occupants/reservations`, and managed `TrafficEdge` references used by conveyor presentation/controller compatibility. Immutable graph records contain no occupants, reservations, or controller instances, traffic loops use profile masks and stable IDs, and architecture/testing docs name the final class/file boundaries. Full Play Mode topology/runtime/soak recordings remain the next validation layer before deleting the remaining bridge fields. `dotnet build Assembly-CSharp.csproj --no-restore --nologo` passed with 0 warnings and 0 errors.

Primary files:

- legacy generation adapter
- compatibility `TrafficEdge` runtime fields
- legacy exact-edge route path
- obsolete feature flags
- architecture and test documents

Work:

- Audit every compatibility path and list remaining consumers.
- Remove only paths with passing replacement tests.
- Ensure immutable graph records contain no runtime occupants/controllers.
- Ensure traffic loops use profiles and stable IDs.
- Run a contradiction pass across all traffic documents.
- Update architecture documentation to reflect final class/file names.

Done when:

- Only one authoritative generation, routing, occupancy, and movement path remains.
- All ground rules have a code owner and test or review check.
- No central behavior loop contains concrete road/vehicle special cases.
- Full build, topology matrix, runtime scenarios, rebuild tests, and soak tests pass.

## Recommended Assignment Order

1. R0 alone.
2. R1, then R2.
3. R3, then R4.
4. R5 integration gate.
5. R6.
6. R7 and R8 may proceed as separate branches after agreeing on runtime/route read models; integrate separately.
7. R9 after both R7 and R8.
8. R10.
9. R11.
10. R12 and R13 can proceed independently once their dependencies are stable.
11. R14 last.

## First Agent Starter Brief

Assign R0 with this instruction:

> Characterize the existing traffic implementation without repairing it. Read all canonical files in `.agents/traffic_update_plan`, inspect the dirty working tree, map current code responsibilities to the revision architecture, and create deterministic fixtures for topology, routing, following, braking, and transitions. Produce `current_behavior_baseline.md`, tests or quarantined reproductions, and a build result. Do not change behavior. Return the required task report.

No other implementation task should start until that report identifies which failures are graph defects and which are runtime-control defects.
