# Current Traffic Behavior Baseline

R0 status: complete on July 18, 2026.

This document characterizes the in-progress traffic prototype without repairing it. It is the comparison baseline for the scalable architecture migration.

## Snapshot

- Branch: `main`
- HEAD at characterization: `375faf12ecc4e0312eba283d947d2cd91e62be00`
- Workspace state: dirty, with pre-existing traffic, scene, asset, documentation, and visual-system changes.
- Unity version: `6000.0.76f1`
- Traffic scene: `Assets/Scenes/SampleScene.unity`
- Conveyor manager, traffic backend, and native graph components are present and enabled in `SampleScene`.
- The traffic hover debugger object exists but is inactive in the serialized scene.

Do not interpret the HEAD hash as containing the working-tree prototype. The baseline includes the current uncommitted files listed by `git status`.

## Verification

Command:

```powershell
dotnet build Assembly-CSharp.csproj --no-restore --nologo
```

Result on July 18, 2026:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

Repeatable static characterization:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".agents\traffic_update_plan\scripts\check_current_traffic_baseline.ps1"
```

The script reads source and scene data only. It does not modify the workspace.

## Implemented Data Flow

### Road Authoring And Classification

Owner: `RoadSystemBackend`

Implemented:

- road cells and connection bitmasks,
- explicit `RoadNodeKind` values,
- road-end, transition, through-road, and intersection classification,
- deterministic ownership of a two-cell road-type boundary through coordinate ordering,
- persistent intersection authoring data.

Current limitation:

- classification reads the live managed road dictionary,
- there is no immutable road input snapshot or road revision gate.

Revision mapping: R1-R3.

### Segment Extraction And Graph Generation

Owners:

- `TrafficSystemBackend`
- `TrafficGenerationTask`

Implemented:

- tracing from road ends, transitions, and intersections,
- generated directed road lanes,
- discrete lane-change stations,
- road-end U-turn movements,
- road-type transition movements,
- generated and authored intersection movements,
- edge IDs, kinds, lengths, metadata, and touched-cell lists.

Current limitations:

- compilation reads scene singletons and live managed state,
- topology, movement policy, geometry, and managed runtime-object construction are mixed,
- no validator blocks publication,
- edge IDs are build-local sequence numbers rather than stable semantic IDs.

Revision mapping: R2-R5.

### Pathfinding

Owners:

- `NativeTrafficGraph`
- `VehiclePathfindingTask`
- `VehiclePathfindingJob`

Implemented:

- managed graph flattening into native buffers,
- Burst A*,
- exact edge-index route output,
- exact departure and arrival building-port cell identity,
- start/end route distances on the first and final edge,
- static lane-change, transition, and U-turn preference costs.

Current limitations:

- a route commits the entire exact lane-edge sequence,
- no strategic road corridor or tactical lane horizon exists,
- no immutable congestion snapshot is consumed,
- closest-lane and port-edge fallback searches scan all managed edges,
- a high-cost intersection fallback remains in A* instead of expressing legality structurally.

Revision mapping: R8-R9 and R13.

### Runtime Movement

Owners:

- `ConveyorTrafficManager`
- `VehicleAI`
- intersection controller implementations

Implemented:

- edge-distance movement,
- managed edge occupant ordering,
- route-distance following across future edges,
- leader-speed-aware stopping envelopes,
- connector reservations with target-lane intervals,
- intersection controller admission,
- rear-clearance release tracking,
- graph-dirty-cell reroute handling,
- transform position and rotation updates,
- traffic-stall recovery.

Current limitations:

- mutable occupants, reservations, and controllers are attached to managed graph objects,
- `VehicleAI` and Unity instance identity participate in runtime ownership and tie-breaking,
- movement ticks from `Update()` using `Time.deltaTime`,
- random reaction delay uses unseeded `Random.Range`,
- there is no desired time-headway controller,
- there is no jerk limit,
- simulation and transform presentation are updated in the same manager,
- there is no alternate or feature-flagged movement implementation.

Revision mapping: R6-R7 and R10-R11.

## Baseline Findings

### B001 - Stall-Recovery Policy Drift

Classification: configuration/policy drift.

Observed:

- `ConveyorTrafficManager.TrafficStuckSecondsBeforeRespawn` is `20f`.
- Resolved decision D17 requires 30 seconds.

Implication:

- this is a known baseline mismatch,
- it should be corrected when the runtime profile/configuration contract is introduced,
- recovery must not be used to hide repeatable graph or reservation defects.

### B002 - Far Endpoint Used As Adjacent Direction

Classification: compiler/topology defect risk.

Deterministic static reproduction:

1. `BuildStraightLanes` registers `endCell` as the outgoing endpoint's `NeighborCell` and `startCell` as the incoming endpoint's `NeighborCell`.
2. These cells are the far ends of an entire traced section and may be more than one grid cell away.
3. Intersection and transition mapping later calls `GetDirectionBit(nodeCell, leg.NeighborCell)`.
4. `GetDirectionBit` recognizes only the eight immediately adjacent deltas and returns `0` otherwise.

Implication:

- multi-cell approaches can receive invalid direction metadata,
- authored direction-bit rules can fail to match existing legs,
- movement conflict metadata can be wrong even when connector geometry exists,
- a validator is currently absent, so the graph can publish silently.

This is the primary static topology defect associated with invalid paths through lane-count changes and intersections.

### B003 - Current Lane-Count Mapping

Classification: implemented topology policy requiring validation.

The current primary mapping formula produces:

- 4 incoming lanes to 2 outgoing lanes: `[0, 0, 1, 1]`
- 2 incoming lanes to 4 outgoing lanes: `[0, 2]`

For expansion, adjacent optional connectors are then generated around each primary target.

Implication:

- 4-to-2 has a primary mapping for every incoming lane,
- 2-to-4 has a natural target plus some optional adjacent choices,
- reachability can still fail because ownership, direction metadata, target-edge resolution, and graph publication are not validated together.

### B004 - Lane Changes Are Discrete Route Edges

Classification: architectural behavior gap.

Observed:

- lane-change movements are generated at fixed graph stations,
- nominal connector length is `1.5` world units,
- nominal opportunity spacing is `3` world units,
- A* chooses lane-change movements as part of the exact route,
- the runtime only attempts the next route movement and its reservation.

Missing:

- bounded-horizon tactical lane selection,
- mandatory versus discretionary lane-change reason,
- alternative maneuver evaluation after a prolonged denial,
- hysteresis and commitment state.

Implication:

- lane changing can work when the precomputed route contains a feasible connector,
- it cannot behave as a general tactical response to traffic or reconsider an unsuitable lane choice.

### B005 - Conveyor Is The Only Active Movement Path

Classification: migration constraint.

Observed:

- `VehicleAI.Update()` is empty,
- all dispatch and return paths request conveyor routes,
- no `useConveyorMovement` feature flag exists,
- failure to find `ConveyorTrafficManager` recalls the vehicle.

Implication:

- R5 must introduce compatibility selection before replacing graph publication,
- later agents cannot assume a waypoint fallback is currently available.

### B006 - Runtime State Lives On Managed Graph Objects

Classification: architecture/scalability debt.

Observed:

- `TrafficEdge` owns `List<VehicleAI> occupants`,
- `TrafficEdge` owns mutable reservations and controller references,
- controller dictionaries are keyed by `VehicleAI`,
- ordering uses `GetInstanceID()` as a tie-break,
- movement runs once per render `Update()`.

Implication:

- graph rebuilds require managed-reference cleanup,
- deterministic simulation identity is not independent from Unity objects,
- runtime state cannot outlive or cleanly swap graph versions,
- fixed-step equivalence is not guaranteed.

### B007 - Longitudinal Control Is Collision-Oriented

Classification: runtime-control gap.

Implemented:

- leader speed is passed into the following constraint,
- future route edges are scanned inside a braking-distance lookahead,
- stopping speed uses a kinematic square-root envelope,
- speed moves toward the envelope at acceleration/deceleration rate.

Missing:

- desired time headway,
- relative-speed comfort model,
- distinct comfortable and emergency deceleration,
- jerk limit,
- fixed-step integration.

Implication:

- the current controller can avoid overlap and follow a moving leader,
- it can still hold speed until a restrictive envelope and then change acceleration abruptly,
- tuning the fixed gap alone cannot provide broadly smooth behavior.

### B008 - Exact Port Identity Is Preserved, Lookup Still Scans

Classification: correct identity contract with scalability debt.

Implemented:

- actual start and target port cells travel through the full route request,
- departure prefers road lanes starting in the exact departure port cell,
- arrival prefers road lanes ending in the exact target port cell.

Current limitation:

- port-specific selection and closest-lane fallback scan `allEdges`.

Implication:

- preserve the exact identity behavior during R2 and R8,
- replace the scan with the R13 spatial index rather than returning to proximity-only routing.

### B009 - Exact Edge Route Without Dynamic Snapshot

Classification: routing architecture gap.

Observed:

- A* returns exact edge indices,
- `TrafficRoute` stores managed edge references,
- no occupancy, congestion, or expected-wait input exists in the native search job.

Implication:

- the pathfinder cannot separate strategic reachability from tactical lane choice,
- dynamic rerouting cannot be made deterministic until it consumes a versioned snapshot.

### B010 - Scene Dependency Is Present

Classification: verified setup.

Observed:

- `ConveyorTrafficManager`,
- `TrafficSystemBackend`,
- `NativeTrafficGraph`

are present and enabled in `SampleScene`.

The `TrafficHoverDebugger` object is serialized inactive, so manual diagnostics require enabling it.

### B011 - High-Cost Intersection Fallback

Classification: legality-contract violation in baseline code.

Observed:

- `VehiclePathfindingJob` contains a branch that adds `15.0f` for a described "sloppy intersection merge."

Implication:

- D13 requires legality to be structural rather than represented by a large cost,
- the branch appears uncommon or unreachable with current native metadata because intersection movements are not normally marked as merge edges,
- R3/R8 must remove the ambiguous fallback rather than making it more reachable.

## User-Reported Symptoms And Ownership

| Symptom | Baseline classification | Primary revision owner |
|---|---|---|
| Lane changing does not work reliably | Exact-route architecture plus missing tactical state | R8-R9 |
| Vehicles slow too late or stop abruptly | Longitudinal controller lacks time headway and jerk limiting | R7 |
| Lane-count changes produce invalid routes | Compiler metadata/validation risk, especially B002 | R3-R4 |
| Vehicles remain stuck | Runtime/controller or topology cause; recovery policy also drifted | R4, R7, R10 |

Do not assign all four symptoms to `ConveyorTrafficManager`. Topology failures must be fixed before runtime tuning.

## Manual Play Mode Characterization Matrix

These scenarios remain manual because the project currently has no traffic fixture builder or Unity test assembly that can construct road, building-port, graph, and runtime state without scene singletons and private compiler stages. Adding that pure-data seam is R1-R4 work; performing it inside R0 would change architecture rather than characterize it.

### M1 - Required Lane Change

Setup:

- Use `Interstate` for a two-lanes-per-way approach.
- Connect it to an intersection where the destination requires a movement unavailable from the vehicle's initial lane.
- Ensure at least one generated lane-change station exists before the intersection.
- Enable logical traffic connections and the hover debugger.

Record:

- chosen `TrafficRoute.EdgeIds`,
- lane-change edge ID and reservation interval,
- whether the vehicle waits, enters, or reaches the wrong movement,
- whether an alternative connector existed but was never reconsidered.

### M2 - Moving-Leader Following

Setup:

- Straight `TwoWaySimple` segment.
- Dispatch two vehicles in the same direction with the follower close enough to catch the leader.

Record:

- leader and follower speed over time,
- center clearance,
- frame rate,
- whether the safety clamp or a full stop occurs while the leader is still moving.

### M3 - Denied Stop-Line Approach

Setup:

- Two conflicting arrivals at a FIFO or free-for-all controlled intersection.
- Keep one movement denied long enough to observe its full approach.

Record:

- distance at first deceleration,
- speed and acceleration near the stop line,
- overshoot or final positional clamp,
- driver reaction delay after release.

### M4 - Four Lanes To Two Lanes

Setup:

- Connect `Interstate` to `TwoWaySimple` through a two-connection boundary.
- Place reachable building ports on both sides.

Record:

- transition owner cell count,
- every source-to-target lane mapping,
- direction bits,
- `reservedTargetEdge` resolution,
- port-to-port route result in both travel directions.

### M5 - Two Lanes To Four Lanes

Use the reverse of M4.

Record:

- primary continuation per source lane,
- optional expansion connectors,
- whether all destination movements remain reachable,
- whether a vehicle takes an unnecessary optional connector.

### M6 - Two-Way To One-Way

Setup:

- Connect `TwoWaySimple` to `SmallOneWay`.

Record:

- legal forward movement records,
- absence of wrong-way movement,
- explicit dead-end/U-turn behavior for the opposing lane where legal,
- route failure diagnostic when no legal continuation exists.

### M7 - Exact Building Ports

Setup:

- Place a building with more than one nearby lane or port candidate.
- Dispatch and complete the return trip.

Record:

- requested start and target port cells,
- selected first and final edge endpoints,
- start and end distance on the route,
- final vehicle position.

## Performance Baseline

No reliable Play Mode timing or allocation number is recorded in R0.

Reason:

- there is no automated traffic fixture or benchmark scene,
- command-line compilation cannot execute the scene simulation,
- measuring an arbitrary editor scene without controlled vehicle and graph counts would produce a misleading budget.

Static scaling observations:

- closest-lane and exact-port fallback queries scan all graph edges,
- runtime sorts occupants on every active edge each frame,
- pathfinding performs a linear search of its open list,
- graph publication rebuilds full native buffers,
- runtime and presentation are coupled in the same render-frame tick.

R13 owns representative performance fixtures and budgets after correctness and ownership gates. R0 establishes the absence of a valid performance baseline rather than inventing one.

## R0 Gate Result

R0 is complete because:

- the authoritative current code paths are identified,
- the project builds,
- each reported symptom is assigned to topology, routing/tactical behavior, or longitudinal/runtime control,
- deterministic static findings are captured by a repeatable script,
- manual-only scenarios include setup, evidence to record, and the reason automation would require later architectural seams,
- no traffic behavior was changed.

R1 is the next unblocked task.

## R0 Agent Report

```text
Task: R0 - Baseline Inventory And Characterization Harness
Status: complete
Files changed: current_behavior_baseline.md; scripts/check_current_traffic_baseline.ps1; status references in canonical traffic documents
Contracts added or changed: none
Ground rules checked: G1, G5, G6, G9, G10, G12, G14, G15, G16, G17
Tests/build: repeatable static baseline script; dotnet build succeeded with 0 warnings and 0 errors
Diagnostics added: baseline finding IDs B001-B011; no runtime code diagnostics added
Compatibility path: current conveyor prototype remains unchanged and is the sole active movement path
Deferred items: Play Mode recordings M1-M7; representative profiler baseline
Risks for next task: dirty working tree; no immutable IDs/profiles; no pure traffic fixture seam; no movement feature flag
```
