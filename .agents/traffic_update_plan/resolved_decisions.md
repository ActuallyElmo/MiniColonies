# Traffic Update Resolved Decisions

These decisions came from the user after reviewing the initial traffic update plan. Treat them as implementation constraints unless the user explicitly changes them later.

## D1 - Lane Drop Merge Style

Lane drops should use zipper merge by default when multiple source lanes are queued in parallel.

The design should still allow a later configurable merge rule where one lane can be prioritised over others. Do not hard-code zipper merge in a way that prevents future priority-lane behavior.

## D2 - Two-Way To One-Way Transition Handling

When a two-way road connects to a one-way road, only lanes whose travel direction legally enters the one-way road should continue through the transition.

Any lane that would travel against the one-way road becomes a dead end. It should use road-end behavior, such as generated U-turns where legal, rather than an illegal through edge.

## D3 - Player Editing Of Road Type Transitions

Road type transitions should eventually be editable in a way similar to intersections.

Do not implement transition editing in the first pass. However, transition metadata should be designed so a future UI can expose lane mapping. Future transition editing must not allow players to change the legal way/direction of traffic.

## D4 - Lane And Vehicle Widths

There should not be variable lane widths or vehicle widths for this phase.

Road type transitions should be based on changes in lane count and legal directionality. Vehicle length may vary, but vehicle width should not affect lane compatibility or transition generation.

## D5 - Lane Expansion Behavior

When a road expands to more lanes, vehicles should keep their natural continuing lane and also have optional lane-change connectors into the newly available lanes.

This mirrors lane merging structurally, except widening transitions are optional lane changes while narrowing transitions force vehicles to merge.

## D6 - Multi-Lane Road-End U-Turns

Each incoming lane at a road end should get a U-turn to one compatible opposing-side outgoing lane if possible.

If there are fewer opposing-side lanes than incoming lanes, pair as many as possible and route extras through transition/merge behavior before the U-turn rather than creating illegal overlapping arcs.

## D7 - Traffic Light Scope

Traffic light rules should not be implemented in the first traffic update pass.

They are a deeper task and should remain as planned extension points after the core conveyor movement, transitions, and FIFO intersection behavior are stable.

## D8 - Editing UI Scope

Player-facing editing and other UI actions will be implemented later.

The first implementation should still expose accessible backend points and stable metadata for future editing of intersections and road type transitions.

## D9 - Intersection Movement Edges And Admission

Intersection movements should behave as normal capacity-constrained traffic edges once a vehicle enters them.

Vehicles advance until their front bumper reaches the intersection entry node, then either transition atomically onto their desired movement edge or wait at that boundary. Admission requires both entry space on the movement edge and permission from the intersection controller.

Do not require a vehicle to reserve downstream road space or prove that it can fully clear the intersection before entering. If downstream traffic blocks a vehicle, that vehicle should queue on its intersection movement edge using the same length and following-gap rules as other traffic edges.

Intersection controllers must inspect current movement occupancy. New admissions are closed while a vehicle is traffic-blocked on any movement edge. Free-for-all controllers may otherwise admit non-conflicting movements concurrently and may admit following vehicles onto the same movement when normal edge-entry spacing permits.

Rear-clearance tracking may keep a movement logically active briefly after a vehicle's pivot transitions to the outgoing edge, so conflicting traffic cannot clip the departing vehicle's rear.

Intersection entry nodes must be pulled back far enough to remain outside the physical overlap of connected roads. The pullback should account for road width and crossing angle, especially for wide roads meeting diagonal legs, rather than using one fixed distance for every approach.

Vehicles should approach intersection entry nodes through a braking-speed envelope. Only the lead vehicle on each approach may participate in controller arbitration, and only after it enters a decision zone sized from its maximum speed and braking distance. Free-for-all controllers should retain deterministic order between eligible conflicting movements long enough for denied vehicles to brake smoothly, without allowing distant or queued vehicles to reserve priority.

## D10 - Transition Ownership And Short Lane Changes

Each road-type boundary must have exactly one transition-node owner. Two adjacent two-connection cells on opposite sides of the same lane-count or directionality change must not both become transition nodes. A road cell adjacent to a true intersection must leave lane mapping to the intersection instead of creating an additional road-type transition.

Multi-lane road segments should generate at least one lane-change opportunity whenever their usable length can support a smooth connector. Short segments may scale the normal lane-change length and endpoint margins down to configured minimums rather than omitting lane changes completely.

Lane-change reservations must represent a moving vehicle-length interval projected onto the target lane, extended forward by the lane-changing vehicle's braking distance. They must not reserve the entire parallel target section or unnecessary space behind the merge position, because traffic outside the actual merge envelope should continue normally.

## D11 - Continuous Following Across Dense Traffic Nodes

Road-section boundaries created for lane-change stations are transparent to car-following behavior. A vehicle must evaluate spacing continuously along its route rather than braking independently for each graph edge.

A moving lead vehicle contributes its current speed to the follower's braking curve. The follower should converge toward that speed at the configured gap, while stopped vehicles and reservations still produce a zero-speed target.

Lane-change and road-transition reservations are requested inside the approaching vehicle's braking horizon. A vehicle with available merge space should enter the connector continuously instead of stopping at the node and acquiring the reservation afterward.

Driver reaction delay remains applicable to a newly released intersection stop, but it does not add a second artificial pause after a connector has already been cleared and reserved.

## D12 - Layered Traffic Architecture

The lane graph and conveyor simulation remain the foundation, but no single exact edge route should own strategic navigation, tactical lane choice, car-following, and presentation.

The revised architecture separates:

- immutable compiled traffic topology and geometry,
- mutable runtime occupancy, reservations, and controller state,
- strategic road-corridor routing,
- short-horizon tactical lane selection,
- longitudinal speed control,
- Unity presentation.

Existing behavior should migrate through compatibility adapters instead of being replaced in one rewrite.

## D13 - Legality Is Not A Pathfinding Cost

Illegal movements must be absent or explicitly disabled in the compiled graph. Large penalties must not be used to represent forbidden turns, invalid lane transitions, incompatible vehicle classes, or wrong-way travel.

Pathfinding costs express preference only after legality and vehicle compatibility have been established.

## D14 - Stable Identity And Versioned Graphs

Runtime systems must refer to roads, lanes, and movements through stable value identifiers rather than long-lived managed object references.

Every published traffic graph is immutable and has a graph version. Mutable occupancy, reservations, queues, and controller state live in a separate runtime store keyed by stable identifiers. Graph rebuild handling must explicitly remap or reroute active vehicles.

## D15 - Strategic Routes And Tactical Lane Choice

Strategic routing selects a road corridor and required downstream movements. Tactical lane planning selects exact lanes and lane-change maneuvers only within a bounded lookahead horizon.

Mandatory lane changes needed to follow the route are distinct from optional lane changes for traffic distribution or overtaking. Tactical decisions require hysteresis and deterministic tie-breaking so vehicles do not oscillate between lanes.

## D16 - Data-Driven Road And Vehicle Variations

New road and vehicle variations must enter traffic behavior through profiles, capabilities, permissions, and policy interfaces. Core traffic loops must not branch on concrete vehicle subclasses, prefab names, road asset names, or special grid coordinates.

Vehicle dimensions and dynamics belong to a traffic profile. Road lane layout, allowed vehicle capabilities, directionality, and speed policy belong to a road profile. Geometry and visuals do not decide movement legality.

D4 remains in force for the current phase: width fields may be captured for future-safe profiles and visual envelopes, but variable lane or vehicle width must not yet change lane compatibility unless a later resolved decision activates that behavior.

## D17 - Traffic Stall Recovery

A vehicle that remains traffic-blocked without meaningful movement for 30 seconds should leave active traffic and respawn or reset through its home-building flow.

This is gameplay recovery, not a correctness mechanism. Recovery must emit a structured diagnostic containing the blocking reason, graph version, vehicle ID, lane/movement ID, and elapsed stationary time. Agents must still repair repeatable topology, reservation, or controller defects that trigger recovery.

Normal waiting at a building, inactive dispatch state, or an intentional non-traffic pause must not advance this timer.

## D18 - Authored Movement Policy Persistence

Player-authored intersection mappings and future transition policy settings belong to authoring state and survive traffic graph rebuilds.

The compiler validates authored mappings against the current normalized lane endpoints. Valid mappings compile into immutable movement records. Stale or invalid mappings are disabled with structured diagnostics and must not crash generation or silently connect a nearby lane.

Clearing authored mappings restores deterministic generated defaults. Regular intersections do not generate U-turn movements unless an explicit future policy enables them.
