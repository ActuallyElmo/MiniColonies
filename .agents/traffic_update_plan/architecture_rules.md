# Traffic Architecture Ground Rules

This is the mandatory review checklist for every traffic implementation agent. These rules apply to roads, graph generation, pathfinding, lane behavior, intersections, vehicle movement, debugging, and future traffic variations.

If a task appears to require breaking a rule, stop and write an architecture amendment proposal. Do not hide the exception in a conditional.

## Authority

Use the traffic documents in this order:

1. `resolved_decisions.md` for product decisions.
2. This file for engineering constraints.
3. `architecture_revision.md` for component boundaries and data flow.
4. `revision_task_list.md` for active work.
5. `testing_checklist.md` and task-specific tests for acceptance.
6. `future_extensions.md` for deliberately deferred additions.

## Non-Negotiable Rules

### G1 - One Meaning Per Layer

- Authoring data describes what the player built and configured.
- The compiler produces immutable traffic topology and geometry.
- Runtime state owns occupancy, reservations, queues, and controller state.
- Strategic routing chooses a corridor and required movements.
- Tactical planning chooses a near-term lane and maneuver.
- Longitudinal control chooses acceleration.
- Presentation samples simulation state and moves Unity objects.

No layer may quietly take over another layer's decision.

### G2 - Snapshot Mutable Inputs

- Traffic generation reads a captured `RoadNetworkSnapshot`, not the live `RoadSystemBackend.Roads` dictionary during a long-running task.
- A compiler stage must not query scene singletons for additional topology after compilation begins.
- Snapshot data includes every road property needed to classify topology, lanes, legality, and geometry.
- If the road revision changes before publication, discard or restart the result instead of publishing a mixed-version graph.

### G3 - Publish Immutable Versioned Graphs

- A published `TrafficGraphSnapshot` is immutable.
- Each graph has a monotonically increasing version.
- Lanes, sections, connectors, controlled nodes, and building-port anchors have stable value IDs.
- Graph records contain data and IDs, not `VehicleAI`, `GameObject`, or mutable controller references.
- Rebuilds publish atomically; readers never observe a partially constructed graph.

### G4 - Keep Runtime State Out Of Graph Records

- Occupants, reservations, wait queues, signal phase, and temporary costs live in `TrafficRuntimeState`.
- Runtime collections are keyed by stable lane or movement ID.
- Pathfinding consumes an immutable congestion/cost snapshot rather than reading live occupant collections.
- Compatibility fields on `TrafficEdge` may exist during migration, but new behavior must go through runtime-state APIs.

### G5 - Legality Before Preference

- Wrong-way, disconnected, forbidden, or vehicle-incompatible movements are absent or disabled.
- A high A* cost is not a substitute for illegality.
- Costs may prefer continuing lanes, avoid optional lane changes, or account for congestion only among legal alternatives.
- Every rejected movement has a diagnostic reason code.

### G6 - Model Semantics Explicitly

- Node kind, lane direction, connector kind, lane ordinal, vehicle permissions, and controller ownership are stored explicitly.
- Do not infer semantics from connection count, waypoint shape, edge color, object name, or proximity once compilation has begun.
- Transition legs carry immediate approach direction or a normalized leg ID. Do not reconstruct direction with a helper that expects adjacent cells but receives a far segment endpoint.
- A lane-count change is a transition, not an intersection.

### G7 - Separate Topology From Geometry

- Topology decides what may connect.
- Geometry decides the curve used to travel through an already legal connection.
- A geometry failure cannot silently create, remove, or redirect topology.
- Connector geometry must preserve exact source and target lane IDs.
- Visual road width and materials do not determine lane compatibility.

### G8 - Validate Before Publish

A graph cannot publish when it contains an error-level invariant violation. At minimum validate:

- unique stable IDs,
- all referenced IDs exist,
- lane direction agrees with its section,
- every connector starts and ends on compatible lanes,
- every legal incoming lane continues or has explicit dead-end behavior,
- forbidden one-way movements do not exist,
- lane reductions map every source lane,
- lane expansions preserve at least one natural continuation,
- connector target-lane runtime references can be resolved,
- building-port anchors refer to the intended port cell and reachable lane endpoint,
- no accidental direction bit or leg ID is invalid,
- no empty or negative-length drivable geometry is published.

Warnings may publish only when they include a stable diagnostic code and do not compromise legality or reachability.

### G9 - Use Strategic And Tactical Planning

- A strategic route is not a permanent exact-lane script for the entire journey.
- It records the road corridor, required controlled movements, destination anchor, and legal lane sets.
- A tactical planner chooses exact lanes within a bounded horizon.
- Mandatory and optional lane changes use different reasons and costs.
- Tactical decisions use hysteresis, cooldowns, and deterministic tie-breakers.
- Failure to execute a tactical maneuver triggers re-planning or strategic rerouting, not an illegal last-second edge.

### G10 - One Longitudinal Controller

- Normal car-following, stop-line approach, reservation approach, and speed-limit approach use one longitudinal-control contract.
- The controller receives current speed, target speed, available route distance, leader speed, vehicle dynamics, and active constraints.
- It returns requested acceleration; it does not mutate graph state or grant reservations.
- Use desired time headway and relative speed, not only a fixed distance gap.
- Clamp acceleration, service braking, emergency braking, and jerk separately.
- A hard positional clamp remains a final collision-safety invariant, not normal driving behavior.

### G11 - Reservations Own Conflict Space

- Lane changes, forced merges, and controlled intersection movements request explicitly bounded space.
- A reservation identifies owner vehicle, graph version, movement ID, affected lane intervals, and lifecycle state.
- Acquisition and release are atomic and deterministic.
- Reservations cover only the predicted conflict envelope plus required safety margin.
- Rejected reservations return a reason and may not partially mutate state.

### G12 - Deterministic Fixed-Step Simulation

- Traffic decisions run on a fixed simulation step independent of render frame rate.
- Ordering and tie-breakers are stable: graph IDs first, then request sequence or stable vehicle simulation ID.
- Do not use `GetInstanceID()` as the long-term simulation identity.
- Randomized driver variation uses seeded, stored parameters; it cannot depend on iteration order.
- Presentation may interpolate between simulation states but cannot change simulation outcomes.

### G13 - Capability-Based Extensibility

- Traffic code consumes a `VehicleTrafficProfile`, not concrete vehicle subclasses.
- The profile contains physical envelope, dynamic limits, permissions, and behavior parameters.
- Traffic code consumes a compiled `RoadProfile`, not prefab or asset-name checks.
- New vehicle or road types should normally require new data plus policy registration, not edits to central movement loops.
- Exceptional behaviors such as emergency priority use an explicit capability or policy interface.

### G14 - No Silent Fallbacks

- Do not pick a nearby lane when an exact building port, lane, or movement identity is available.
- Do not convert an invalid movement into a U-turn, teleport, or arbitrary adjacent edge without an explicit policy.
- Recovery actions emit a structured diagnostic and retain the original failure reason.
- A timeout may recover gameplay but must not conceal a repeatable topology or controller bug.

### G15 - Bounded Work And Ownership

- Runtime work iterates active vehicles, active lanes, or indexed nearby records; it must not scan the entire graph per vehicle per tick.
- Closest-lane lookup uses a spatial index before performance sign-off.
- Graph compilation remains time-sliced or jobified and never runs as an unbounded input-handler call.
- One service owns each mutable collection. Other systems use commands or read-only views.
- Avoid per-vehicle, per-tick managed allocations.

### G16 - Migrate Through Adapters

- Keep the game runnable after every revision wave.
- New graph snapshots may feed the existing managed `TrafficNode`/`TrafficEdge` runtime through a temporary adapter.
- Old and new implementations may run in comparison mode, but only one is authoritative for state mutation.
- Remove a compatibility path only after parity tests pass and no active code depends on it.

### G17 - One Unit Convention

- Traffic simulation uses Unity world units and seconds.
- Distance fields use an `Units` suffix, speeds use `UnitsPerSecond`, acceleration uses `UnitsPerSecondSquared`, and jerk uses `UnitsPerSecondCubed`.
- Normalized 0..1 progress is derived presentation or sampling data, never authoritative spacing.
- Asset compatibility converters perform any legacy conversion once at the boundary.
- Do not mix grid cells, normalized edge progress, and world distance in one comparison.

## Required Agent Handoff

Every implementation agent must report:

- Revision task ID and scope completed.
- Files changed.
- Ground rules affected.
- New IDs, records, commands, events, or public contracts.
- Validation and tests run.
- Known warnings or deliberately deferred behavior.
- Confirmation that illegal movements are represented structurally, not through high cost.
- Confirmation that no new concrete road/vehicle type branch was added to a central loop.

## Pull Request Rejection Conditions

Reject or return work that:

- fixes a named map layout with coordinate-specific logic,
- adds a vehicle- or road-name conditional to routing or movement,
- reads live managed occupancy inside a pathfinding job,
- mutates a published graph,
- represents illegal travel with a large cost,
- lets presentation objects own simulation truth,
- adds a new connector without validator coverage,
- changes a public traffic contract without updating the revision documents,
- combines topology generation, tactical lane choice, and vehicle acceleration in one class,
- passes compilation but provides no topology or runtime behavior test.
