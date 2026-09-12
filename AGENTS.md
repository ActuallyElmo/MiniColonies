# MiniColonies repository instructions

## Project context

- This is a Unity project. Use Unity `6000.0.76f1`, as recorded in
  `ProjectSettings/ProjectVersion.txt`. Do not open or resave the project with a
  different Unity version unless the user explicitly asks for an upgrade.
- Runtime code is under `Assets/Scripts`, editor-only tooling is under
  `Assets/Editor`, and automated tests are under `Assets/Tests`.
- `Assets/Scenes/SampleScene.unity` is the scene enabled in Build Settings.
- Read `Assets/Scripts/Architecture_Documentation.md` before changing terrain,
  roads, buildings, traffic, vehicle routing, or simulation scheduling.
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

## Introducing or changing features

1. Establish the observable behavior and affected systems before editing. Note
   important boundary cases, state transitions, persistence needs, and failure
   behavior.
2. Find the current owner of the state or rule. Extend that owner where
   practical; do not create a parallel manager, cache, or source of truth for
   convenience.
3. Implement the smallest end-to-end slice, including domain behavior, Unity
   integration, and UI feedback where the feature requires them.
4. Add or update tests at the same time as the behavior. Prefer regression tests
   that fail for the old behavior and pass for the new behavior.
5. Check serialized references, prefab/scene wiring, lifecycle cleanup, and
   compatibility with existing assets. Scene-dependent changes require an
   Editor or Play Mode smoke check, not only a C# unit test.
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
fixture first, and then run the full EditMode suite for changes that affect
shared systems.

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

After any C# change:

- Confirm Unity script compilation has no errors.
- Run the narrowest relevant fixture while iterating.
- Run the full EditMode suite before completion when the change affects shared
  simulation, buildings, roads, traffic, routing, time, or input behavior.
- Run an appropriate Play Mode or manual scene smoke check for scene/UI/input/
  lifecycle changes.
- Report the commands or Test Runner scope used, the result, and any validation
  that could not be performed.

## Review priorities

When reviewing changes, prioritize functional regressions, broken serialized
references, main-thread stalls, per-frame allocations, lifecycle leaks,
nondeterministic simulation behavior, and missing behavioral tests. Leave
purely stylistic issues to existing formatting unless they obscure a real
problem.
