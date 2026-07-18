# Traffic Revision Handoff

This directory is the canonical handoff for MiniColonies traffic work.

## Read In This Order

1. `resolved_decisions.md`
2. `architecture_rules.md`
3. `architecture_revision.md`
4. `revision_task_list.md`
5. `testing_checklist.md`
6. `future_extensions.md` when planning work beyond the active migration.
7. `current_behavior_baseline.md` for the completed R0 prototype evidence.

## Active Task

R0 baseline inventory and characterization is complete.

R1 ground-road contract implementation is complete.

R2 immutable road input snapshots are implemented for the compatibility traffic path. Automated tests were intentionally omitted at the user's request; user verification is pending.

Do not start R3 or behavioral fixes until that verification is accepted.

## Architecture Summary

- Preserve lane-constrained traffic and the conveyor concept.
- Compile roads into an immutable, validated, versioned traffic graph.
- Keep mutable occupancy, reservations, queues, and controllers in a separate runtime state store.
- Route strategically through road corridors and choose exact lanes tactically within a bounded horizon.
- Use one jerk-limited longitudinal controller for following and approaching constraints.
- Extend roads and vehicles through profiles, capabilities, and policies.
- Treat illegal movement structurally; never hide it behind pathfinding cost.
- Keep the project runnable through compatibility adapters during migration.

## Agent Completion Rule

An agent is not done when the code merely compiles. It must satisfy its task's semantic tests, structured diagnostics, compatibility requirement, and ground-rule checklist.
