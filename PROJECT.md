# MiniColonies Traffic Architecture

This file is the project-level pointer for traffic work. It intentionally does not duplicate the executable architecture or backlog.

## Canonical Traffic Documents

Read `.agents/traffic_update_plan/README.md` first.

The authoritative set is:

- `.agents/traffic_update_plan/resolved_decisions.md`
- `.agents/traffic_update_plan/architecture_rules.md`
- `.agents/traffic_update_plan/architecture_revision.md`
- `.agents/traffic_update_plan/revision_task_list.md`
- `.agents/traffic_update_plan/testing_checklist.md`
- `.agents/traffic_update_plan/future_extensions.md`

Do not create a competing milestone list in this file.

## Current Status

The working tree contains an in-progress conveyor prototype:

- managed `TrafficNode` and `TrafficEdge` graph generation,
- native A* graph mirroring,
- edge-progress vehicle movement,
- managed edge occupancy and connector reservations,
- road-type transition generation,
- intersection controller prototypes.

These classes are compatibility inputs to the revision, not the final ownership model. Their presence does not mean the scalable migration tasks are complete.

R0 and the ground-road R1-R2 contracts are implemented. The compatibility traffic path now consumes an immutable road input snapshot; user verification is pending before R3. No later revision task should be assumed complete until its acceptance criteria are recorded in `revision_task_list.md`.

## Target Architecture

The system remains lane-constrained and deterministic, but responsibilities are separated:

1. Road and building authoring state is captured into an immutable input snapshot.
2. A staged compiler creates legal lanes and movements.
3. Validators reject invalid topology before publication.
4. An immutable, versioned traffic graph is published atomically.
5. Mutable occupancy, reservations, queues, and controller state live in a separate runtime store.
6. Strategic routing selects road corridors and required controlled movements.
7. Tactical planning selects exact near-term lanes and maneuvers.
8. One fixed-step longitudinal controller handles following and constraint approach.
9. Unity presentation interpolates simulation state without owning traffic decisions.

## Extension Model

- Road variations enter through compiled road profiles and movement policies.
- Vehicle variations enter through traffic profiles, capabilities, permissions, and dynamics.
- Intersection rules enter through controller policies operating on stable movement IDs.
- Illegal movement is absent or explicitly disabled; pathfinding cost expresses preference only.
- Compatibility adapters keep the existing prototype runnable while ownership migrates.

## Assignment Rule

Agents take tasks only from `.agents/traffic_update_plan/revision_task_list.md`. Any new cross-cutting behavior first needs:

- a named owner and task,
- applicable ground rules,
- data and API contracts,
- compiler/runtime validation,
- deterministic tests,
- compatibility and removal plan.
