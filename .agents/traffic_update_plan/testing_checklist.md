# Traffic Runtime Manual Test Checklist

## Architecture Revision Gates

- The road input snapshot remains unchanged when live road authoring data is mutated after capture.
- A compiler result built from a stale road revision is discarded before publication.
- Every published graph has a version and unique stable lane/movement IDs.
- Error-level graph diagnostics prevent publication.
- Illegal one-way, vehicle-incompatible, and disabled movements are absent or explicitly disabled rather than assigned a high cost.
- Runtime occupancy and reservations reference stable IDs and the current graph version.
- A graph rebuild leaves no active vehicle, route, reservation, or controller pointing to the old graph.
- A graph rebuild with unchanged stable lane/movement IDs remaps active conveyor vehicles without replacing their home/destination port corridor metadata.
- A graph rebuild with missing stable lane/movement IDs detaches the vehicle from old traffic state and requests a graph-rebuild reroute instead of retaining old edges.
- Presentation frame rate does not materially change fixed-step traffic outcomes.
- A representative new road profile and vehicle traffic profile require no concrete-type branch in the central traffic loops.
- `TrafficProfileLegality` rejects incompatible road/vehicle permission or capability combinations with `IllegalProfileCombination`.
- `TrafficSpatialIndex` services closest-lane and port-cell endpoint lookups before fallback scans are used.
- `TrafficPerformanceSnapshot` distinguishes graph/index size, route time, tick time, active vehicles, active lanes, reservation contention, and congestion snapshot metrics.

## Compiler Validation Matrix

- `TrafficGraphValidator.Validate` reports error diagnostics and prevents compiler publish for missing lane/movement/owner/segment references.
- Validator diagnostics include `DuplicateStableId`, `MissingLaneReference`, `IllegalDirectionMovement`, `InvalidLegDirection`, `UnmappedIncomingLane`, or `UnreachableBuildingPort` as stable codes instead of relying on log text.
- Exact building-port reachability is checked through `TryGetReachablePortAnchor` using the authored port ID and requested flow.
- Four-lane road into two-lane road: every legal source lane has a mandatory merge mapping.
- Two-lane road into four-lane road: every source lane has a natural continuation; optional expansion movements are separate.
- Two-way into one-way: opposing illegal movements do not exist.
- Adjacent road-type boundary cells produce exactly one transition owner.
- A transition next to an intersection does not create a second lane-mapping owner.
- Multi-cell approaches retain a valid immediate direction/leg ID.
- Every connector references existing source and target lanes.
- Empty or negative-length drivable geometry prevents publication.
- Exact building-port anchors remain distinguishable from nearby lanes.
- Unreachable port pairs produce a structured diagnostic rather than an arbitrary proximity fallback.

Use this checklist in Play Mode after traffic graph rebuilds successfully.

## Core Queueing

- Straight two-way road, two vehicles in the same lane: the follower stops behind the leader and never overlaps.
- Same setup with a long vehicle: the follower keeps the larger spacing.
- A vehicle that remains traffic-blocked without meaningful movement for 30 seconds recovers through its home-building flow and emits a structured diagnostic.
- Normal inactive/building wait time does not advance the traffic-stall recovery timer.
- Dispatch/return-home loop with `useConveyorMovement` disabled: legacy waypoint movement still completes.
- Dispatch/return-home loop with `useConveyorMovement` enabled and `ConveyorTrafficManager` in the scene: vehicle follows edge samples and completes both legs.

## Road Ends

- One-lane-per-way dead end: incoming vehicle takes a `RoadEndUTurn` edge onto the opposing outgoing lane.
- Multi-lane dead end: each incoming lane pairs to a compatible outgoing lane where possible.
- Road-end cells are not listed by `RoadSystemBackend.GetEditableIntersection`.

## Intersections

- Four-way one-lane intersection: intersection movement edges have `IntersectionMovement` kind and FIFO controllers.
- Two vehicles arriving together on conflicting movements: only one enters, and the other waits.
- A denied vehicle reaches the intersection entry node and remains stopped there without creeping toward the movement edge.
- A denied lead vehicle decelerates smoothly instead of having its speed clamped to zero on the final frame.
- A distant vehicle or a vehicle queued behind another approach vehicle cannot hold priority over a lead vehicle already near the intersection.
- Waiting vehicles keep their full body outside conflicting movement paths, with their front bumper at the entry boundary.
- A vehicle may enter without downstream-road clearance, then queues safely on its movement edge if the exit is blocked.
- A traffic-blocked vehicle on any movement edge closes the intersection to new admissions.
- Free-for-all allows non-conflicting movement edges concurrently.
- Free-for-all allows a following vehicle onto the same movement edge when normal entry spacing is available.
- Conflicting movement remains blocked until the departing vehicle's rear has cleared.
- Wide multi-lane intersections with diagonal legs place every approach node outside the visible road-overlap area.
- Clearing custom rules restores deterministic defaults.
- In compatibility mode, stale custom rules increment `IntersectionData.InvalidCustomRuleCount` and do not throw.
- In revision mode, stale authored mappings produce a structured compiler diagnostic and are not published as movements.

## Road Type Transitions

- Four-lane road into two-lane road: generated connectors are `RoadTypeTransition`, not intersection movements.
- Two-lane road into four-lane road: natural continuing lanes exist, with optional adjacent expansion connectors.
- A two-lane/four-lane boundary creates one transition node and remains routable between building ports on both sides.
- Two-way into one-way transition: only legally directed lanes continue through generated outgoing endpoints.
- Transition hover debug logs transition cell, lane mapping, and priority.

## Lane Changes

- Existing virtual lane-change edges are `LaneChange`.
- A lane-change or transition into an occupied target lane waits until front and rear clearance are available.
- A short four-lane segment between a building and intersection gets an adaptive lane-change connector when at least 0.6 units are usable.
- Through traffic does not stop at a lane-change station because of reservations outside the moving merge interval.
- A queue spanning several lane-change station nodes keeps the configured bumper gap without stopping and restarting at each graph edge.
- A follower approaching a moving lead vehicle converges toward the lead vehicle's speed instead of braking toward zero.
- A vehicle with clear target-lane space reserves before the lane-change node and enters the connector without a stop-then-go pause.
- A blocked lane change brakes smoothly before the connector and proceeds as soon as its ordered reservation becomes available.

## Rebuilds

- Rebuilding roads while conveyor vehicles are active unregisters old occupants.
- Active conveyor vehicles reroute from their current world position or recall cleanly if no route exists.
- No orphaned occupants remain on old `TrafficEdge` objects after rebuild.
- When all stable route edge IDs survive rebuild, the active route is remapped to the new graph version and current managed edge references.
- Rapid consecutive road edits reject stale compilation results before they replace a newer graph.

## Diagnostics

- Hover debugger colors road-type transitions distinctly.
- Edge hover logs edge id, kind, lane mapping, direction bits, occupant count, and controller type where present.
- Edge hover logs stable graph/lane-segment/movement IDs where available.
- `dotnet build Assembly-CSharp.csproj --no-restore` succeeds after generation/runtime changes.
