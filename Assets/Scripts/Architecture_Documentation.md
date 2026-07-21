# MiniColonies Architecture Context Guide

This document describes the implemented `Assets/Scripts` architecture after the
traffic cleanup.

## Core Rule

Large simulation work should not stall the frame. Player input mutates compact
backend state immediately, while mesh, network, graph, and pathfinding work runs
through `SimulationTaskManager` as time-sliced tasks or Unity jobs.

## Main Systems

### Terrain
- `Terrain/WorldManager.cs`
- Owns terrain generation, cell height sampling, buildability, and world
  bootstrap.
- Provides `cellSize`, `chunkSize`, and height data used by roads, buildings,
  and traffic snapshots.

### Roads
- `Roads/RoadSystemBackend.cs`
- Owns the authored road grid, preview transactions, connection bitmasks,
  dirty chunk events, and editable intersection policy data.
- Emits `OnRoadCellChanged` for logical/network invalidation and
  `OnChunksDirty` for visual mesh rebuilds.

### Buildings
- `Buildings/BuildingSystemBackend.cs`
- Owns active buildings and occupancy.
- Building ports are captured into road/traffic snapshots so vehicles can route
  between exact port cells.

### Logical Road Networks
- `Roads/Network/RoadNetworkManager.cs`
- Maintains coarse road-network connectivity for building dispatch decisions.
- Rebuilds only after road placement commits, so drag placement does not
  repeatedly recalculate building connectivity.

### Traffic Graph
- `Roads/Traffic/TrafficSystemBackend.cs`
- Captures road/building authoring state into `RoadNetworkSnapshot`.
- Queues `TrafficGenerationTask`, which only advances `TrafficGraphCompiler`.
- Publishes the validated `TrafficGraphSnapshot` atomically.
- Builds the temporary managed `TrafficNode`/`TrafficEdge` adapter output used by
  the current conveyor and native pathfinding runtime.

### Vehicle Runtime
- `Roads/Traffic/ConveyorTrafficManager.cs`
- Advances active vehicles on a fixed simulation step.
- Owns route entry, following constraints, reservations, intersection admission,
  graph rebuild remapping, congestion snapshots, and presentation interpolation.
- This class is still the largest remaining traffic simplification target.

### Pathfinding
- `Vehicles/VehicleAI/NativeTrafficGraph.cs`
- Mirrors the currently published managed traffic adapter into native arrays for
  Burst pathfinding jobs.
- `VehiclePathfindingTask` returns both a managed edge route for the conveyor and
  stable graph IDs in `RouteCorridor`.

## End-to-End Flow

1. Road or building authoring state changes.
2. `RoadNetworkManager` rebuilds coarse building connectivity after commit.
3. `TrafficSystemBackend` captures an immutable road/building snapshot.
4. `TrafficGenerationTask` advances the staged compiler across frames.
5. `TrafficGraphValidator` rejects invalid topology before publication.
6. `TrafficSystemBackend` publishes the immutable graph and managed adapter.
7. `NativeTrafficGraph` rebuilds from the published adapter.
8. Buildings stagger vehicle route requests.
9. `VehiclePathfindingTask` routes through legal lanes and movements.
10. `ConveyorTrafficManager` advances vehicles on the published graph.

## Remaining Cleanup Targets

- Move conveyor runtime readers from `TrafficEdge.occupants` and
  `TrafficEdge.reservations` onto `TrafficRuntimeState` and
  `TrafficReservationService`.
- Replace managed-edge pathfinding with direct stable graph IDs, then remove the
  managed adapter and native graph bridge.
- Split `ConveyorTrafficManager` into smaller runtime services once it no longer
  has to preserve managed edge compatibility.
