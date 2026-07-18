# Deferred Traffic Extensions

This file records planned extension areas that are intentionally outside the active R0-R14 migration unless a revision task explicitly activates them.

Do not implement one of these features merely because its extension point is being created. Activation requires stable prerequisites, an assigned task, tests, and any unresolved product decision.

## F1 - Discretionary Lane Behavior

Examples:

- congestion-driven lane changes,
- overtaking slower vehicles,
- preferred cruising lanes,
- driver aggressiveness variation,
- post-expansion lane distribution.

Prerequisites:

- R7 longitudinal controller,
- R9 mandatory tactical lane planning,
- R10 stable reservations,
- soak tests with no lane oscillation or emergency clamps.

Extension boundary:

- add tactical scoring policies and profile data,
- do not modify strategic legality or invent runtime connections.

## F2 - Advanced Intersection Controllers

Examples:

- traffic lights,
- priority roads,
- rule-of-way,
- emergency preemption,
- timed or demand-responsive phases.

Prerequisites:

- stable movement IDs,
- immutable controlled-node policy records,
- R10 controller request/grant contract,
- deterministic conflict and queue fixtures.

Extension boundary:

- implement controller policies over compiled movements,
- do not mutate graph topology, occupancy collections, or vehicle transforms.

Exact generated traffic-light phase patterns remain a future tuning decision.

## F3 - Player Editing

Examples:

- intersection lane mapping,
- transition lane mapping,
- controller selection,
- priority direction configuration,
- traffic overlay and editing UI.

Prerequisites:

- D18 authored policy persistence,
- compiler validation diagnostics,
- stable endpoint IDs suitable for UI,
- query and command APIs that do not expose mutable compiler collections.

Transition editing may change compatible lane mappings but must not allow illegal traffic direction.

## F4 - Heterogeneous Lane And Vehicle Widths

Width fields may exist in profiles now, but D4 keeps compatibility width-independent during the current migration.

Activating variable widths requires:

- a new resolved decision,
- lane capacity and compatibility rules,
- geometry clearance validation,
- intersection and merge conflict-envelope changes,
- asset migration and mixed-width test matrices.

Do not activate width behavior through an isolated conditional.

## F5 - Routing And Simulation Scale

Examples:

- congestion-aware strategic rerouting,
- hierarchical or regional pathfinding,
- region-scoped graph compilation,
- distant-vehicle simulation level of detail,
- multiple task queues or priorities.

Prerequisites:

- R13 measurements from representative colony sizes,
- stable graph partitions and IDs,
- deterministic equivalence rules between simulation detail levels.

Optimization must preserve legality and behavioral contracts.

## F6 - Recovery And Player Diagnostics

Examples:

- traffic jam overlays,
- deadlock explanation UI,
- invalid authored-rule warnings,
- recovery history and telemetry.

The 30-second recovery behavior in D17 remains active regardless of UI work. Presentation choices for these diagnostics are still a future tuning question.
