# MiniColonies Architecture Context Guide

This document describes the current `Assets/Scripts` architecture after the
traffic cleanup. It is meant to help a reader follow the active workflows
without chasing every helper class first.

## Core Rule

Large simulation work should not stall the frame. Player input mutates compact
backend state immediately, while mesh, road network, traffic graph, and
pathfinding work runs through `SimulationTaskManager` as time-sliced tasks or
Unity jobs.

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
- Rebuilds after road placement commits, so drag placement does not repeatedly
  recalculate building connectivity.

## Traffic Workflow

### 1. Authoring Snapshot
- `Roads/Traffic/Compilation/RoadNetworkSnapshotBuilder.cs`
- Reads `RoadSystemBackend` and `BuildingSystemBackend`.
- Compiles road profiles, road cells, building ports, and intersection policies
  into immutable `RoadNetworkSnapshot` data.

### 2. Graph Compilation
- `Roads/Traffic/Compilation/TrafficGraphCompiler.cs`
- Advances this stage order:
  `Normalize -> Classify -> GenerateLanes -> GenerateLegalMovements -> BuildMovementGeometry -> Publish`.
- `TrafficGraphCompilerStages.cs` still contains most of the compiler logic:
  section tracing, lane generation, movement generation, geometry slicing,
  building-port anchors, and snapshot publishing.

### 3. Validation
- `Roads/Traffic/Validation/TrafficGraphValidator.cs`
- The published `TrafficGraphSnapshot` is the single graph validation target.
- The compiler no longer runs a separate draft validator before publishing, and
  the managed adapter no longer revalidates the same graph.

### 4. Publication And Runtime Bridges
- `Roads/Traffic/TrafficSystemBackend.cs`
- Publishes the validated immutable graph atomically.
- Builds `ManagedTrafficGraphAdapter` output because the current conveyor and
  native pathfinding still operate on managed `TrafficNode`/`TrafficEdge`
  objects.
- Rebuilds `TrafficSpatialIndex`, intersection controllers, and
  `NativeTrafficGraph` after publication.

## Vehicle Routing Workflow

1. A building or vehicle requests a route.
2. `VehiclePathfindingTask` resolves start/end lane candidates and exact
   building-port anchors.
3. `NativeTrafficGraph` supplies Burst-friendly node, edge, and waypoint arrays.
4. `VehiclePathfindingJob` finds a legal path using permissions,
   capabilities, lane movements, and congestion penalties.
5. `TrafficRoute` returns the managed edge list needed by the conveyor.
6. `RouteCorridor` keeps stable graph IDs and route diagnostics for rebuilds
   and debug visibility.

## Vehicle Runtime Workflow

1. `ConveyorTrafficManager` owns active vehicle ticking.
2. `TrafficRuntimeState` owns authoritative edge occupancy.
3. `TrafficReservationService` owns authoritative connector reservations.
4. `LongitudinalController` handles speed/acceleration stepping.
5. `TacticalLanePlanner` chooses simple lane-change intent from the next route
   edge.
6. Intersection controllers decide admission for controlled movement edges.
7. The conveyor updates vehicle transforms and publishes performance counters.

## Current Debt

- `TrafficGraphCompilerStages.cs` is still too broad. It should be split by
  compiler stage once behavior is stable.
- `ConveyorTrafficManager.cs` is still too broad. Following, connector
  reservations, intersection exits, graph-remap handling, and transform
  interpolation can be separated.
- `ManagedTrafficGraphAdapter` is a temporary bridge. It should disappear once
  pathfinding and the conveyor run directly on stable graph IDs.
- `TrafficEdge.occupants` and `TrafficEdge.reservations` remain only as mirrors
  for compatibility/debug consumers. Runtime code should use
  `TrafficRuntimeState` and `TrafficReservationService`.
- Profile compatibility defaults in `RoadProfile` and `VehicleTrafficProfile`
  can shrink after road and vehicle assets have persistent traffic profile keys.
