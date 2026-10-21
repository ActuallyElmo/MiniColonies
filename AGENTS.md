# MiniColonies repository instructions

## Project context

- This is a Unity project. Use Unity `6000.0.76f1`, as recorded in
  `ProjectSettings/ProjectVersion.txt`. Do not open or resave the project with a
  different Unity version unless the user explicitly asks for an upgrade.
- Runtime code is under `Assets/Scripts`, editor-only tooling is under
  `Assets/Editor`, and automated tests are under `Assets/Tests`.
- `Assets/Scenes/SampleScene.unity` is the scene enabled in Build Settings.
- Read `Assets/Scripts/Architecture_Documentation.md` before changing terrain,
  roads, buildings, traffic, vehicle routing, simulation timing, or scheduling.
- The project uses URP, the new Input System, uGUI, TextMesh Pro, AI Navigation,
  Burst, and the Unity Test Framework. Check `Packages/manifest.json` before
  assuming another package is available.

## Working in the repository

- Inspect the existing implementation, related tests, and current Git diff
  before editing. Preserve user changes and keep unrelated work out of the
  patch.
- Follow the style of neighboring C# code. The generated projects currently
  target C# 9 and .NET Standard 2.1. Do not introduce language features that
  Unity's configured compiler cannot compile.
- Do not edit generated or machine-local content in `Library`, `Temp`, `Logs`,
  `Obj`, `Build`, `Builds`, or `UserSettings`.
- Do not edit generated `.csproj` or `.slnx` files. Unity regenerates them.
- Treat `Packages/manifest.json` and `Packages/packages-lock.json` as a pair.
  Add or upgrade packages only when the feature requires it, and call out the
  package change explicitly.
- Keep each Unity asset together with its `.meta` file. When moving, renaming,
  or deleting an asset, make the same operation on its `.meta` file. Never
  replace an existing GUID merely to clean up metadata.
- Prefer Unity Editor APIs for changes to scenes, prefabs, materials,
  ScriptableObjects, and other serialized assets. Avoid broad hand edits to
  Unity YAML; if a text edit is necessary, preserve file IDs, GUID references,
  and serialization structure and inspect the resulting diff carefully.
- Existing scene-building utilities are exposed under `Tools/Mini Colonies` in
  the Unity Editor. Extend or use these tools for repeatable UI hierarchy and
  prefab generation when appropriate instead of relying on fragile manual YAML
  edits.

## Architecture and runtime rules

- Keep authoritative simulation state in backend/domain systems. UI and input
  components should call those systems and render their state rather than own a
  second copy of it.
- Large simulation work must not stall the frame. Preserve the project's model:
  input mutates compact backend state, while mesh generation, network rebuilds,
  graph compilation, and pathfinding use `SimulationTaskManager`, time-sliced
  tasks, or Unity jobs.
- Preserve atomic publication of validated traffic graph snapshots. Runtime
  traffic occupancy belongs to `TrafficRuntimeState`, and connector
  reservations belong to `TrafficReservationService`; compatibility mirrors
  are not authoritative state.
- Prefer deterministic, side-effect-light C# methods for game rules. Keep
  `MonoBehaviour` lifecycle methods thin so rules can be covered by EditMode
  tests without loading a scene.
- Avoid adding per-frame polling when an existing event or explicit refresh can
  express the change. If polling is necessary, keep work bounded and allocation
  free in `Update`, `LateUpdate`, and `FixedUpdate`.
- Pair event subscriptions with unsubscriptions and release coroutines, jobs,
  native containers, singleton references, and temporary objects in the
  appropriate disable/destroy path.
- Preserve serialized data compatibility. Do not rename or change the type of a
  serialized field without a migration plan; use `FormerlySerializedAs` when a
  field rename is intentional.
- Validate designer-authored values at a clear boundary such as `OnValidate`,
  initialization, or a domain setter. Runtime behavior must still handle invalid
  data safely rather than depending only on Inspector validation.

## Simulation time and speed

- For every addition or change involving time, explicitly decide whether it
  follows simulation speed or real time before implementing it. Check movement,
  timers, coroutine waits, cooldowns, deadlines, and animations, including work
  already in progress when the player changes speed.
- `GameTimeManager` owns Pause/x0.5/x1/x2/x4 simulation speed and the shared clock.
  Pause is a zero simulation multiplier; gameplay timers must stop and resume
  without counting paused wall time, while UI and building tools stay usable.
  Gameplay movement and delays must use `SimulationDeltaTime`,
  `SimulationTimeSeconds`, or `WaitForSimulationSeconds` as appropriate.
  Calendar-driven rules such as factory shifts must use the existing game-hour
  events. Do not add a parallel clock or multiply an already scaled delta again.
- Vehicles, traffic-light phases, production/shift progression, loading and
  unloading, and gameplay departure/reaction delays follow simulation time.
  Traffic rules already driven by the conveyor must consume its fixed-step
  delta and simulation deadlines rather than Unity wall-clock time.
- UI feedback, camera controls, placement tools, cosmetic animations, and
  infrastructure work budgets normally retain real-time/frame-based behavior.
  A UI display of simulation state still reads that state from its owner.
  Document non-obvious choices, such as real-time retries for asynchronous
  network rebuilding. Do not change Unity's `Time.timeScale` or
  `Time.fixedDeltaTime` to implement simulation speed.
- Preserve bounded fixed-step catch-up and the shared frame-time limit so the
  calendar, traffic, and other simulation consumers remain coordinated after
  slow frames. For changed timing rules, add focused coverage for supported
  speeds and speed changes during pending work, following the validation scope
  limits below.

## UI authoring

- Most UI must be authored as editable objects in a scene or saved prefab
  assets. Layout, styling, text, and serialized references must be available to
  edit in the Unity Editor outside Play Mode. Do not build ordinary panels,
  buttons, or complete UI hierarchies from scratch in runtime code.
- Dynamic UI, such as variable-length lists, should instantiate or pool authored
  prefabs. Runtime scripts should bind data and events, update displayed state,
  and control visibility rather than recreate the authored design.
- Use or extend the `Tools/Mini Colonies` Editor utilities for repeatable scene
  and prefab creation. Installation must preserve existing designer edits and
  references, avoid duplicate controls, and never silently save over unrelated
  pending scene edits. Keep assets and their `.meta` files together.
- Use procedural runtime UI only when the behavior genuinely requires it, such
  as data-derived world overlays or geometry, and explain the exception in the
  change summary. Existing runtime-generated UI is not a precedent for making
  new ordinary UI that way; avoid unrelated rewrites of existing UI.

## Introducing or changing features

1. Establish the observable behavior and affected systems before editing. Note
   important boundary cases, state transitions, persistence needs, and failure
   behavior. For timed behavior, identify the time source; for UI, identify the
   editable scene hierarchy or prefab that will own its presentation.
2. Find the current owner of the state or rule. Extend that owner where
   practical; do not create a parallel manager, cache, or source of truth for
   convenience.
3. Implement the smallest end-to-end slice, including domain behavior, Unity
   integration, and UI feedback where the feature requires them.
4. Add or update tests at the same time as the behavior. Prefer regression tests
   that fail for the old behavior and pass for the new behavior.
5. Check serialized references, prefab/scene wiring, lifecycle cleanup, and
   compatibility with existing assets. Require an Editor or Play Mode smoke
   check only when the change directly edits a scene/prefab or its correctness
   cannot be established without Unity lifecycle or frame progression.
6. Update `Assets/Scripts/Architecture_Documentation.md` when ownership,
   authoritative data flow, or a major workflow changes.

Avoid speculative frameworks and broad cleanup during feature work. Extract a
new abstraction only when the requested behavior needs it or existing code has
more than one concrete use for it.

## Testing strategy

- The current automated suite uses NUnit EditMode tests in
  `Assets/Tests/EditMode`. Put deterministic game rules, validation, routing,
  scheduling, and state-transition tests there.
- Add PlayMode tests under `Assets/Tests/PlayMode` when correctness depends on
  frame progression, coroutines, physics, scene loading, or real Unity lifecycle
  ordering. Do not force such behavior into an EditMode test merely to make the
  test faster.
- For a bug fix, add a focused regression test whenever the behavior can be
  exercised reliably. For a new feature, cover the main success path, meaningful
  invalid input, and important transitions or invariants. Avoid tests that only
  duplicate trivial getters or Unity framework behavior.
- Tests must clean up every `GameObject`, `ScriptableObject`, material, native
  allocation, event subscription, and singleton they create. Use teardown and
  `DestroyImmediate` for EditMode objects, following the existing tests.
- Keep tests deterministic: drive time explicitly, avoid real-time waits, avoid
  depending on test order, and do not rely on assets or the active scene unless
  the test is intentionally an integration test.

### Running tests

Use Unity's Test Runner as the authoritative test runner. Do not treat
`dotnet test` or a generated solution build as a substitute for Unity script
compilation and tests.

In the Editor, use **Window > General > Test Runner**, run the relevant EditMode
fixture first. A focused fixture is the default completion check.

For a Windows batch run, set `$unityEditor` to the Unity executable for exactly
`6000.0.76f1`, then run:

```powershell
$projectPath = (Get-Location).Path
$resultsPath = Join-Path $projectPath "Logs\EditModeTests.xml"
$logPath = Join-Path $projectPath "Logs\EditModeTests.log"
& $unityEditor -batchmode -projectPath $projectPath -runTests -testPlatform EditMode -testResults $resultsPath -logFile $logPath
```

For a focused batch run, add `-testFilter "FullyQualifiedTestName"`. Do not run
a second Unity process against the project while the Editor has it open. If the
matching Unity executable is unavailable, report that tests were not run rather
than silently using another editor version.

### Validation time and scope limits

- Keep automated validation proportional to the change. Prefer script
  compilation plus the narrowest relevant EditMode fixture.
- Do not copy the project to a temporary directory to work around an open Unity
  Editor, test discovery, licensing, or locking. Do not launch a second Unity
  instance when this project is already open.
- Do not relocate tests, create temporary test assemblies, or modify project
  structure solely to make an automated validation command run. Report the
  discovery or environment limitation instead.
- A focused automated test command has a default time budget of two minutes.
  If it stalls, stop it, inspect the available log once, and report the blocked
  validation. Do not repeatedly retry the same command or escalate to broader
  suites as a workaround.
- Run the full EditMode suite only when the user explicitly requests it, for a
  release/pre-merge validation task, or when a broad foundational change has no
  reliable focused fixture. A file touching traffic, roads, buildings, time,
  input, or another shared system does not by itself require the full suite.
- Run Play Mode tests only when behavior depends on frame progression,
  coroutines, physics, scene loading, or real lifecycle ordering and a focused
  EditMode test cannot cover the risk. Prefer one focused Play Mode fixture or a
  concise manual smoke-check instruction over a broad Play Mode run.
- Do not add integration tests for wiring that is already exercised by an
  existing scene smoke check. Add tests for durable game rules and regressions,
  not to maximize test count for a small change.

After any C# change:

- Confirm Unity script compilation has no errors.
- Run the narrowest relevant fixture while iterating.
- Apply the validation time and scope limits above before considering a full
  suite or Play Mode run.
- Report the commands or Test Runner scope used, the result, and any validation
  that could not be performed.

## Review priorities

When reviewing changes, prioritize functional regressions, broken serialized
references, main-thread stalls, per-frame allocations, lifecycle leaks,
nondeterministic simulation behavior, and missing behavioral tests. Leave
purely stylistic issues to existing formatting unless they obscure a real
problem.
