# MiniColonies Architecture Context Guide

This document is the current handoff map for the `Assets/Scripts` architecture as of July 18, 2026. It describes the implemented project structure and points traffic work to its canonical migration documents.

## What This Architecture Is Optimizing For

The project is structured around one primary rule: large-scale simulation work must not stall the frame.

That rule shows up in three design choices:

1. Authoritative game state lives in lightweight backend data stores.
2. Expensive rebuilds run through `SimulationTaskManager` via `ISimulationTask`.
3. Cross-system reactions are event-driven and staged, rather than immediate deep call chains.

The result is a pipeline where player input mutates small pieces of state immediately, while graph rebuilds, lane generation, mesh generation, and pathfinding are deferred into queued tasks and jobs.

## Current High-Level Layout

### Terrain
- `Terrain/WorldManager.cs`
- Owns terrain generation, chunk spawning, buildability checks, cell heights, and async world bootstrap.
- This is the source of truth for `cellSize`, `chunkSize`, terrain height sampling, and transition/slope constraints.

### Roads
- `Roads/RoadSystemBackend.cs`
- Owns the road grid as `Dictionary<Vector2Int, RoadCell>`.
- Handles road preview transactions, cell connection bitmasks, dirty chunk tracking, and intersection registration.
- Emits:
  - `OnRoadCellChanged` for logical network rebuilds.
  - `OnChunksDirty` for visual mesh rebuilds.

### Buildings
- `Buildings/BuildingSystemBackend.cs`
- Owns building occupancy and the active building list.
- Emits placement/removal events so network logic can recompute affected buildings without tight coupling.

### Logical Road Networks
- `Roads/Network/RoadNetworkManager.cs`
- Accumulates dirty road cells and dirty buildings.
- Does not rebuild immediately during drag placement.
- Waits for an explicit commit, snapshots the road map, then queues `RoadNetworkGenerationTask`.

### Traffic Graph
- `Roads/Traffic/TrafficSystemBackend.cs`
- Listens for `RoadNetworkManager.OnNetworkReady`.
- Extracts road segments from the road backend, then queues `TrafficGenerationTask`.
- Swaps in the completed `TrafficNode`/`TrafficEdge` graph in one apply step and rebuilds `NativeTrafficGraph`.

### Native Pathfinding Representation
- `Vehicles/VehicleAI/NativeTrafficGraph.cs`
- Flattens managed traffic nodes, edges, and waypoint lists into persistent native arrays for Burst jobs.
- This is the pathfinding-facing representation, not the authoring/source-of-truth layer.

### Vehicles
- `Vehicles/VehicleAI/VehicleAI.cs`
- `Vehicles/VehicleAI/VehiclePathRequestManager.cs`
- Vehicles request paths through the task system rather than pathfinding synchronously in `Update()`.

## Core Async Backbone

### `SimulationTaskManager`
- File: `Managers/SimulationTaskManager.cs`
- Owns a FIFO queue of `ISimulationTask`.
- Gives the main thread a millisecond budget each frame (`maxMillisecondsPerFrame`, currently 5ms in the manager).
- Tracks scheduled `JobHandle`s and completes remaining handles on destroy.

### `ISimulationTask`
```csharp
public interface ISimulationTask
{
    bool Process(Stopwatch timer, float maxMillisecondsPerFrame);
}
```

Contract:
- Return `true` only when the task is fully done.
- Return `false` whenever the task must yield and resume next frame.
- Long loops should check the timer internally.
- Jobs may be scheduled inside tasks, but managed object creation and Unity object mutation must remain on the main thread.

## End-to-End Data Flow

### Road editing flow
1. `RoadBuilderInput` opens preview mode on mouse down.
2. `RoadSystemBackend` mutates preview cells during drag.
3. `RoadSystemBackend.LateUpdate()` batches dirty chunks and fires `OnChunksDirty`.
4. `RoadVisualSystem` queues `RoadMeshGenerationTask`.
5. On mouse up, preview is committed.
6. `RoadNetworkManager.CommitNetworkChanges()` allows the logical rebuild to start.
7. `RoadNetworkManager` snapshots dirty state and queues `RoadNetworkGenerationTask`.
8. `TrafficSystemBackend` reacts to `OnNetworkReady` and queues `TrafficGenerationTask`.
9. `TrafficSystemBackend.ApplyNewNetwork()` swaps the graph and triggers `NativeTrafficGraph.RebuildGraph()`.
10. Vehicles can now request new paths against the rebuilt native graph.

### Building flow
1. `BuildingSystemBackend` registers or removes buildings.
2. `RoadNetworkManager` marks buildings dirty.
3. `RoadNetworkGenerationTask` remaps building ports to rebuilt road networks.
4. `Building.OnNetworksUpdated()` marks route recalculation needed.
5. When traffic is ready, buildings stagger vehicle reroutes over time to avoid bursty path requests.

## Important Tasks and Why They Exist

### `RoadMeshGenerationTask`
- Reads a road snapshot and dirty chunk set.
- Traces logical paths, smooths them, groups segments by chunk and `MeshKey`, schedules chunk mesh jobs, then applies finished meshes back to `RoadVisualSystem`.
- Purpose: keep road visual rebuilds chunked, isolated, and job-friendly.

### `RoadNetworkGenerationTask`
- Destroys affected logical networks, rebuilds them via BFS, then reevaluates building connectivity.
- Purpose: logical connectivity is decoupled from road placement so drag placement does not repeatedly rebuild the whole network.

### `TrafficGenerationTask`
- Converts road segments into lane centerlines and intersection connections.
- Uses a Burst `TrafficMathJob` for the heavy geometric work, then hydrates managed `TrafficNode`/`TrafficEdge` objects on the main thread.
- Purpose: isolate expensive lane math from graph assembly and keep traffic graph rebuilds incremental across frames.

### `VehiclePathfindingTask`
- Resolves start/end candidate lanes on the main thread, then schedules `VehiclePathfindingJob`.
- Pulls the vehicle start position at execution time rather than request time to avoid stale-path bugs while queued.

## Traffic Implementation State

The working tree contains an in-progress conveyor prototype:

- `RoadSystemBackend` classifies road ends, through roads, transitions, and intersections.
- `IntersectionData` stores generated and authored lane mappings.
- `TrafficGenerationTask` builds road lanes, lane-change connectors, road-type transitions, road-end U-turns, and intersection movements.
- `NativeTrafficGraph` mirrors the managed graph for Burst pathfinding.
- `VehiclePathfindingTask` returns compatibility edge routes anchored to exact building-port cells.
- `ConveyorTrafficManager` currently owns edge-progress movement, managed occupancy, following constraints, connector reservations, intersection admission, and traffic-stall recovery.

This is the baseline to characterize and migrate. It is not the final ownership model. In particular, mutable occupant/controller state on managed `TrafficEdge` objects and whole-trip exact-lane routes are compatibility structures, not target contracts.

## Canonical Traffic Revision

Traffic architecture and task assignments live under `.agents/traffic_update_plan`.

Read:

1. `README.md`
2. `resolved_decisions.md`
3. `architecture_rules.md`
4. `architecture_revision.md`
5. `revision_task_list.md`
6. `testing_checklist.md`

`PROJECT.md` is only a project-level pointer to that canonical set.

R0 and the ground-road R1-R2 contracts are implemented. The compatibility traffic generator consumes an immutable road input snapshot; user verification is pending before R3. Do not infer later migration completion from the presence of prototype classes.

## Revised Traffic Data Flow

The target traffic pipeline is:

1. Capture road, building-port, and authored policy state into an immutable road snapshot.
2. Normalize and classify topology.
3. Compile stable road-section, lane, movement, controlled-node, geometry, and port-anchor records.
4. Validate legality, references, transition ownership, geometry, and port reachability.
5. Publish an immutable graph version atomically.
6. Store live occupancy, reservations, queues, and controller state separately by stable IDs.
7. Route strategically through road corridors and required movements.
8. Select exact lanes tactically within a bounded horizon.
9. Advance vehicles on a fixed simulation step through one longitudinal controller.
10. Interpolate Unity presentation from simulation poses.

Compatibility adapters keep the current managed graph and conveyor path runnable while these responsibilities migrate.

## Project-Wide Rules Future Agents Must Preserve

1. Do not reintroduce synchronous full-map rebuilds in response to per-cell edits.
2. Keep authoring backends as the source of player-built state.
3. Snapshot mutable inputs before long-running tasks.
4. Prefer event-driven invalidation over immediate cross-system rebuild calls.
5. Keep expensive work time-sliced, jobified, indexed, pooled, or region-scoped according to measured need.
6. Separate pure data generation from Unity object application.
7. Publish rebuilt data atomically and version it when live consumers can outlast a rebuild.
8. Keep simulation truth out of presentation objects.
9. Treat legality structurally; costs express preference only.
10. Use `.agents/traffic_update_plan/architecture_rules.md` for the complete traffic-specific ground rules.

## Known Scalability Pressure Points

Measure these after correctness gates:

- `TrafficSystemBackend.GetClosestLane` and `GetClosestLanes` scan all current traffic edges.
- `TrafficGenerationTask` creates many managed geometry lists during graph assembly.
- `NativeTrafficGraph.RebuildGraph()` rebuilds full persistent buffers.
- Current conveyor work mixes occupancy, reservations, controller admission, motion, and presentation updates.
- `RoadNetworkManager.HandleRoadChanged()` may become expensive as building count grows.
- `Building.FindBestFactoryRoute()` remains a managed search across connected buildings and ports.

The revision addresses these through immutable snapshots, stable IDs, separated runtime state, spatial indexes, fixed-step simulation, and later regional/LOD work. Do not optimize around an invalid topology or unclear ownership contract.
