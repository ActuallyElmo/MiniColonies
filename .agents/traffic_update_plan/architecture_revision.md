# Scalable Traffic Architecture Revision

Status: canonical target architecture as of July 18, 2026.

Audience: implementation agents modifying roads, traffic generation, routing, vehicle movement, intersections, debugging, or performance.

## Objective

Evolve the current lane-graph and conveyor prototype into a system that:

- stays deterministic and debuggable as vehicle count grows,
- supports additional road layouts and vehicle variations through data,
- treats topology errors as compiler/validation failures rather than runtime driving edge cases,
- produces smooth following, braking, merging, and lane-changing behavior,
- survives road graph rebuilds without dangling managed references,
- remains runnable throughout migration.

This is not a switch to NavMesh movement, free steering, or physics-driven traffic. Vehicles remain constrained to compiled lane geometry. The revision changes ownership and decision boundaries around that geometry.

## Why The Current Shape Needs Revision

The current implementation has useful foundations:

- `RoadSystemBackend` owns road-grid authoring state.
- `TrafficGenerationTask` creates lane and connector geometry.
- `NativeTrafficGraph` provides a job-compatible graph.
- `ConveyorTrafficManager` centralizes traffic movement and conflict handling.
- `VehicleAI` carries building dispatch and arrival behavior.

The scaling risk is that the same edge model is being asked to be all of the following:

- compiled topology,
- mutable occupancy container,
- exact whole-trip route,
- tactical lane decision,
- collision constraint,
- connector reservation target,
- visual spline.

That coupling turns new road and vehicle variations into conditionals across generation, pathfinding, and movement. It also makes topology mistakes appear as abrupt stops, stuck vehicles, or failed lane changes.

## Target Data Flow

```text
Road and building authoring state
        |
        v
RoadNetworkSnapshot
        |
        v
Traffic graph compiler
  normalize -> classify -> connect -> build geometry -> validate
        |
        v
Immutable TrafficGraphSnapshot (version N)
        |
        +--------------------+
        |                    |
        v                    v
Strategic router       TrafficRuntimeState
        |              occupancy/reservations/controllers
        v                    |
RouteCorridor                |
        |                    |
        v                    |
TacticalLanePlanner <--------+
        |
        v
LongitudinalController -> fixed-step integration
        |
        v
Vehicle simulation pose -> Unity presentation interpolation
```

## Component Boundaries

### 1. Authoring State

Owners:

- `RoadSystemBackend`
- `BuildingSystemBackend`
- persistent intersection and transition configuration
- `RoadType` and `VehicleData` assets during compatibility migration

Responsibilities:

- Store what exists in the colony.
- Store player configuration.
- Emit revisions and dirty regions.
- Produce a complete immutable input snapshot for compilation.

Authoring state does not store live vehicles, reservations, or traffic queues.

### 2. `RoadNetworkSnapshot`

Introduce a pure-data snapshot captured before traffic compilation.

Minimum records:

```csharp
public sealed class RoadNetworkSnapshot
{
    public int Revision { get; }
    public IReadOnlyList<RoadCellRecord> Cells { get; }
    public IReadOnlyList<BuildingPortRecord> BuildingPorts { get; }
    public IReadOnlyList<IntersectionPolicyRecord> IntersectionPolicies { get; }
}
```

The implementation owns private backing buffers and exposes read-only access. Callers must not receive a mutable array or list that can alter a captured snapshot.

`RoadCellRecord` must include:

- stable cell ID or grid coordinate,
- physical neighbor connections,
- legal incoming and outgoing directions,
- road profile ID,
- elevation/geometry samples required by lane generation,
- persisted node policy reference where relevant.

The compiler must not return to live scene state to fill missing semantics.

### 3. Compiled Profiles

#### `RoadProfile`

`RoadType` remains the Unity authoring asset initially. Compile it into a runtime profile containing:

- stable profile ID,
- number of lanes in each legal direction,
- lane ordering convention,
- speed policy,
- allowed vehicle capability mask,
- lane width and shoulder/curb geometry parameters,
- supported movement policies,
- optional future tags such as bus-only or emergency access.

The initial revision may preserve equal lane widths, but the profile boundary must allow future lane-specific definitions without changing routing contracts.

Per D4, width is future-safe profile data during this phase and does not yet alter vehicle/lane compatibility.

#### `VehicleTrafficProfile`

Compile traffic-relevant fields from `VehicleData`:

- stable profile ID,
- length and width envelope,
- desired speed and maximum speed,
- comfortable acceleration,
- comfortable service deceleration,
- emergency deceleration,
- desired time headway,
- minimum standstill gap,
- maximum jerk,
- capability/permission mask,
- optional deterministic driver-behavior parameters.

Freight, passenger, emergency, off-road, or future vehicle roles must not be identified by subclass checks inside traffic loops.

### 4. `TrafficGraphSnapshot`

This is the immutable, published result of traffic compilation.

Recommended records:

```csharp
public sealed class TrafficGraphSnapshot
{
    public int Version { get; }
    public IReadOnlyList<RoadSectionRecord> Sections { get; }
    public IReadOnlyList<LaneRecord> Lanes { get; }
    public IReadOnlyList<MovementRecord> Movements { get; }
    public IReadOnlyList<ControlledNodeRecord> ControlledNodes { get; }
    public IReadOnlyList<BuildingPortAnchorRecord> PortAnchors { get; }
}
```

As with the road snapshot, backing storage is private and cannot be mutated through published APIs.

Stable identifier types:

- `RoadSectionId`
- `LaneId`
- `MovementId`
- `ControlledNodeId`
- `BuildingPortAnchorId`
- `VehicleSimulationId`

Use value types with explicit invalid values. Do not exchange raw list indices across graph versions without the version that owns them.

#### `RoadSectionRecord`

Represents a continuous road corridor between meaningful topology boundaries. It owns:

- start and end anchor IDs,
- road profile ID,
- ordered lane IDs per direction,
- physical cells/tile coverage,
- stable geometry key,
- legal capability mask.

#### `LaneRecord`

Represents continuous directed travel along a road section:

- stable lane ID,
- owning section ID,
- flow direction/leg ID,
- lane ordinal in a documented inside-to-outside convention,
- permissions,
- speed policy,
- polyline or spline sample range,
- next legal movement IDs.

Lane identity is semantic. It must not be reconstructed from waypoint proximity.

#### `MovementRecord`

Represents travel from one lane to another:

- stable movement ID,
- source lane ID,
- target lane ID,
- movement kind,
- controlled-node or transition owner ID,
- turn or merge semantics,
- required permission mask,
- reservation/conflict policy ID,
- base preference cost,
- geometry sample range,
- diagnostic source metadata.

Movement kinds should distinguish at least:

- lane continuation,
- optional lane change,
- mandatory lane merge,
- lane expansion choice,
- intersection movement,
- road-end U-turn,
- building-port entry or exit.

### 5. Traffic Graph Compiler

Compilation is a staged pipeline. Each stage consumes only the prior stage's output.

#### Stage A - Normalize

- Capture immediate neighbor legs and legal directionality.
- Resolve one transition owner per road-type boundary.
- Assign stable topology keys before creating geometry.
- Normalize lane ordinal conventions.
- Record source cells and authoring profile IDs for diagnostics.

#### Stage B - Classify

Classify semantic nodes explicitly:

- through boundary,
- road end,
- road-type transition,
- controlled intersection,
- building-port anchor.

Classification must be based on normalized snapshot data, not managed object type, proximity, or visual geometry.

#### Stage C - Generate Lanes

- Build directed lane records for road sections.
- Preserve exact road profile and source cell coverage.
- Generate geometry independently for each lane.
- Do not add lane changes yet.

#### Stage D - Generate Legal Movements

Use policy services:

- `ITransitionMappingPolicy`
- `IIntersectionMovementPolicy`
- `IRoadEndPolicy`
- `IBuildingPortConnectionPolicy`

Policies return legal semantic mappings and rejection reasons. They do not mutate runtime occupancy.

Generate required continuity and mandatory merges first. Generate optional lane changes as a separate pass so optional behavior cannot accidentally repair missing topology.

#### Stage E - Build Movement Geometry

Create smooth curves between already selected source and target lanes. A geometry builder cannot substitute a different target when curve construction fails.

#### Stage F - Validate

Run structural, legality, geometry, and reachability validators. Error-level results prevent publication.

#### Stage G - Publish

Publish the complete graph atomically with a new version. Build native arrays and indexes from the same snapshot.

### 6. Runtime State

Introduce `TrafficRuntimeState` as the sole owner of mutable traffic simulation data.

It contains:

- vehicle simulation records,
- ordered occupancy per active lane or movement,
- interval reservations,
- controller state per controlled node,
- deterministic request queues,
- graph version,
- active-lane index,
- read-only congestion snapshot generation.

Do not store `VehicleAI` references inside immutable lane records. During migration, map `VehicleSimulationId` to presentation objects in a separate registry.

Suggested service boundaries:

```csharp
public interface ITrafficOccupancyService
{
    bool TryInsert(VehicleSimulationId vehicle, LaneId lane, float distance, out TrafficRejectReason reason);
    bool TryTransfer(VehicleSimulationId vehicle, MovementId movement, out TrafficRejectReason reason);
    TrafficLeaderInfo GetLeader(VehicleSimulationId vehicle, float lookaheadDistance);
}

public interface ITrafficReservationService
{
    ReservationResult TryAcquire(in ReservationRequest request);
    void Release(ReservationId reservation);
}
```

Only these services mutate their owned collections.

### 7. Strategic Routing

The strategic router answers:

> Which road corridor and controlled movements can legally reach the destination?

`RouteCorridor` should contain:

- graph version,
- start and destination port anchor IDs,
- ordered road section IDs,
- required controlled movement IDs or movement groups,
- acceptable lane set at each upcoming decision boundary,
- reroute reason/version metadata.

Strategic A* evaluates:

- legality for the vehicle capability mask,
- distance/travel time,
- optional congestion snapshot,
- controlled movement delay,
- stable preference penalties.

It should not prescribe every optional lane change for the entire trip.

### 8. Tactical Lane Planning

The tactical planner answers:

> Which lane and maneuver should this vehicle use over the next bounded horizon?

Inputs:

- route corridor,
- current lane and distance,
- acceptable lanes for upcoming required movements,
- nearby occupancy snapshot,
- available lane-change and merge movements,
- vehicle traffic profile,
- existing reservation or cooldown.

Outputs:

- desired lane,
- optional movement request,
- reason: mandatory route preparation, forced merge, congestion avoidance, lane distribution, or keep lane,
- decision validity horizon,
- failure/replan reason.

Required behavior:

- prepare for required turns early enough to avoid last-second illegal changes,
- distinguish mandatory from discretionary changes,
- reserve target conflict space before entering a connector,
- check predicted front and rear gaps,
- use hysteresis and cooldowns,
- replan when a request remains infeasible,
- never invent an uncompiled connection.

The first tactical version should implement mandatory route preparation and forced merges. Congestion-driven discretionary lane changes come later.

### 9. Longitudinal Control

Create a pure controller that computes requested acceleration from constraints.

```csharp
public readonly struct LongitudinalContext
{
    public readonly float Speed;
    public readonly float DesiredFreeSpeed;
    public readonly float LeaderDistance;
    public readonly float LeaderSpeed;
    public readonly float ConstraintDistance;
    public readonly float ConstraintTargetSpeed;
    public readonly VehicleTrafficProfile Profile;
}

public interface ILongitudinalController
{
    float ComputeAcceleration(in LongitudinalContext context, float previousAcceleration, float deltaTime);
}
```

The controller should combine:

- free-road desired speed,
- time-headway following,
- relative speed to a moving leader,
- upcoming lower speed limit,
- denied reservation or stop line,
- route endpoint.

Clamp jerk after combining constraints. Keep an emergency overlap-prevention clamp in integration, with diagnostics whenever it activates.

### 10. Intersections And Merges

Controllers and merge policies grant use of conflict space; they do not directly move vehicles.

Each request includes:

- stable vehicle ID,
- graph version,
- source lane,
- requested movement,
- predicted arrival tick,
- required conflict interval,
- deterministic request sequence.

Controller output is a grant, denial reason, or retry condition. Longitudinal control converts denial into a smooth approach to the boundary.

Lane reductions use a merge policy, not an intersection controller. Zipper merge remains the default for parallel queued lanes, with a policy ID that permits future priority-lane behavior.

### 11. Simulation And Presentation

Traffic simulation runs on a fixed tick. Each vehicle simulation record contains:

- graph version,
- current lane or movement ID,
- distance along record,
- speed,
- acceleration,
- tactical state,
- strategic route handle,
- active reservation handle,
- stable deterministic ID.

`VehicleAI` remains responsible for dispatch, arrival, cargo/passenger behavior, and binding a visual object to simulation state. It must not own car-following, right-of-way, or direct occupancy mutation.

Presentation samples lane/movement geometry from the graph and interpolates between previous and current simulation poses. Visual rotation smoothing cannot alter route progress.

### 12. Graph Rebuilds

Graph rebuild policy:

1. Capture road revision R.
2. Compile and validate graph version N+1 from R.
3. Reject the result if authoring revision changed before publish.
4. Publish N+1 atomically.
5. Remap active vehicles by stable lane/movement keys where exact compatibility exists.
6. Reroute vehicles that cannot be safely remapped.
7. Keep a vehicle inactive at a building port or enter explicit recovery if neither action is legal.

Never leave active runtime records pointing at managed objects from graph N.

### 13. Diagnostics

Introduce structured diagnostics:

```csharp
public enum TrafficDiagnosticCode
{
    MissingLaneReference,
    IllegalDirectionMovement,
    UnmappedIncomingLane,
    InvalidTransitionOwner,
    InvalidLegDirection,
    UnreachableBuildingPort,
    ReservationConflict,
    TacticalPlanUnavailable,
    EmergencySafetyClamp,
    GraphVersionMismatch
}
```

Diagnostics include graph version, stable IDs, source cell/profile, vehicle ID where applicable, and severity. Debug visuals read this data rather than duplicating inference logic.

## Extension Rules

### Adding A Vehicle Variation

Expected changes:

- create or extend vehicle traffic profile data,
- assign capability/permission mask,
- add profile validation,
- add a scenario test.

Unexpected changes that require architecture review:

- editing the central simulation tick,
- adding a concrete vehicle subclass check to pathfinding,
- adding a vehicle-name exception to a connector,
- changing graph topology to accommodate a visual prefab.

### Adding A Road Variation

Expected changes:

- define authoring and compiled road profile data,
- register a topology or movement policy if semantics are new,
- add compiler-validator cases,
- add reachability and geometry scenarios.

Unexpected changes that require architecture review:

- coordinate-specific mapping,
- special casing the road asset name in pathfinding,
- treating road width or mesh shape as implicit traffic legality,
- bypassing the traffic compiler with hand-created runtime edges.

### Adding An Intersection Rule

Expected changes:

- implement the controller policy contract,
- define serializable authoring data,
- compile it into a controlled-node policy record,
- add deterministic conflict and queue tests.

The new controller cannot mutate lane graph topology or vehicle transforms.

## Performance Direction

Correctness gates optimization, but the architecture must allow:

- flat native graph buffers built from immutable snapshots,
- spatial indexing for lane/port lookup,
- active-lane rather than all-lane runtime iteration,
- zero managed allocations in the steady simulation tick,
- congestion snapshots at a lower frequency than movement ticks,
- hierarchical routing for larger colonies,
- region-scoped graph rebuilds,
- simulation level-of-detail for distant vehicles.

Do not introduce ECS merely to solve ownership ambiguity. Data boundaries and stable IDs come first; storage can migrate later without changing behavior contracts.

### Implemented Wave 5 Runtime Hooks

As of July 18, 2026 the implementation names for the rebuild, variation, and scale hooks are:

- `TrafficSystemBackend.TryApplyNewNetwork` publishes the current `TrafficGraphSnapshot`, managed adapter output, stable-edge lookup tables, spatial index, intersection controllers, and native graph from one accepted source snapshot; compiled immutable graphs are rejected before runtime mutation if they are stale or invalid.
- `ConveyorTrafficManager.HandleTrafficGraphRebuilt` releases old runtime graph references, attempts exact route remap by stable lane-segment or movement IDs, and reroutes vehicles that cannot be safely remapped.
- `TrafficSpatialIndex` owns closest-lane lookup buckets and exact port-cell endpoint indexes used by route start/end selection.
- `TrafficPerformanceSnapshot` and `TrafficCongestionSnapshot` expose graph-size, route, tick, active-traffic, reservation-contention, and congestion data without putting mutable runtime collections into immutable graph records.
- `TrafficProfileLegality` demonstrates the extension boundary for vehicle/road permission, capability, and movement-policy combinations.

Compatibility notes:

- `ManagedTrafficGraphAdapter` is still the bridge from immutable graph records into current `TrafficNode`/`TrafficEdge` runtime objects.
- `TrafficEdge.occupants`, `TrafficEdge.reservations`, and `TrafficEdge.exitController` remain compatibility mirrors/links while `ConveyorTrafficManager`, `TrafficRuntimeState`, and controller services are migrated.
- The legacy managed generation payload remains available only as transient comparison/build input until Play Mode topology, rebuild, runtime, and soak tests sign off deleting the remaining bridge fields.

## Migration Strategy

The revision is incremental:

1. Characterize current behavior and add graph diagnostics.
2. Introduce IDs, profiles, snapshots, and validators without changing movement.
3. Compile the new immutable graph and adapt it to current `TrafficEdge` objects.
4. Move occupancy and reservations into runtime state.
5. Replace abrupt stopping logic with the longitudinal controller.
6. Introduce strategic corridors and mandatory tactical lane planning.
7. Move transitions and intersection admissions to stable movement/reservation contracts.
8. Add rebuild remapping, spatial indexes, discretionary lane behavior, and performance work.
9. Remove compatibility structures only after parity and soak tests.

The executable version of this sequence is in `revision_task_list.md`.

## Definition Of Architectural Success

The revision is successful when:

- a graph error fails validation with a specific diagnostic before vehicles spawn,
- 4-to-2, 2-to-4, one-way, dead-end, and intersection cases use general policies,
- adding a normal road or vehicle variation is data work plus tests,
- vehicles choose lanes locally without rewriting their whole strategic route,
- moving leaders produce smooth speed convergence and stop constraints produce jerk-limited braking,
- runtime simulation contains no long-lived references to an obsolete graph,
- traffic behavior is deterministic under the same graph, inputs, seed, and fixed tick,
- steady-state simulation cost scales with active traffic rather than total graph size per vehicle.
