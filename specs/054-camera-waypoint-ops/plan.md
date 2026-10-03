# Implementation Plan: Camera Cue Ops by Waypoint

**Branch**: `054-camera-waypoint-ops` | **Date**: 2026-10-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/054-camera-waypoint-ops/spec.md`

## Summary

Correct the cue vocabulary's camera leaf: remove `MoveCamera`/`MoveCameraState` and the `CameraPose` type, and add
two waypoint-addressed ops — `SetCamera` (a snap from a named waypoint; no progress of its own) and
`FlyCamera`/`FlyCameraState` (a flight along a named waypoint chain at a speed; the shape whose progress is more than
a deadline). A camera in this game is placed by waypoints, and a waypoint *name* is what the editor authors without
the shared control reading the zone registry (`specs/047`, FR-006) — the panel, which already holds the gameplay
state, offers the active zone's waypoints in a combo, so a chosen name always resolves.

The work is confined to the cue's own surfaces plus one small addition to `world/Zone.fs`:

- `world/Cue.fs` — remove three cases, add `SetCamera of waypoint: string` and
  `FlyCamera of path: string * speed: float` with `FlyCameraState of path: string * points: Vector3[]
  * rotations: Quaternion[] * duration: float * endsAt: float`.
- `world/Zone.fs` — add `tryWaypointByName` (first live waypoint of that name, slot order) and `waypointChain`
  (the `Next` links, bounded so a cycle terminates). Nothing in the app walks a chain yet; this feature is the first
  caller.
- `world/CueEngine.fs` — resolve names in the active zone where the walk reaches the op; a flight resolves its chain
  once, into a spline of positions and facings, and its in-flight value is pinned to that path. Add the camera-look
  helper and remove the `CameraPose` helpers. Both ops remain plain data on `state.Camera`, so they are assertable
  headlessly.
- `core/Maths.fs` — NEW: the spline geometry (Catmull-Rom position, per-segment slerp facing, sampled arc length),
  pure and device-free, so the curve is testable on its own and the engine keeps only the camera's own concern.
- `editor/CueTree.fs` — the kind list, labels, lock rule, text fields and the flight's speed drag. `editor/CueEditor.fs`
  gains the camera op's waypoint combo: the panel reads the active zone's live waypoints and draws the combo, while
  `CueTree` stays zone-agnostic (`specs/047`, FR-006; FR-019).
- `test/CueTests.fs`, `test/CuePanelTests.fs` — the two camera ops' acceptance scenarios and the panel's tables.

One new module (`core/Maths.fs`, the spline geometry), one `Compile` entry, no new field on `GameplayState`, and no
new dependency. A flight's *begin* allocates its resolved path once; every in-flight tick allocates nothing (it
writes `state.Camera` and reports `ValueNone`).

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`), `TreatWarningsAsErrors`-clean build

**Primary Dependencies**: Prime (for `FDeque` — already referenced), `System.Numerics` (`Vector3`, `Quaternion`).
SDL3, Dear ImGui and Jolt Physics are untouched.

**Storage**: none — cues are not persisted (phase 15); removing `MoveCamera` leaves no saved data to migrate

**Testing**: xUnit via `dotnet test` (headless). The suite builds a `GameplayState` the way
`test/GameplayStateTests.fs` does and places waypoints in its active zone with `Zone.spawnWaypoint`; both ops are
asserted on `state.Camera` with no window, GPU or device (`specs/046`, FR-016)

**Target Platform**: Linux desktop (SDL_gpu/Vulkan)

**Project Type**: desktop game with an in-engine editor (single project; one `.fsproj` per the app and its tests)

**Performance Goals**: 60 fps. A flight in progress must cost the loop **0 bytes per tick** (`specs/046`, FR-012;
Constitution V); resolving a path allocates once, when the op begins, and that cost does not grow with the cue's
length

**Constraints**: the constitution's five principles; a cue is a value and a step returns the next value without
mutating it; `Cue.fs` names no zone/screen/asset/device type, so it stays loadable on its own; `CueEngine.fs` stays
*after* `state/GameplayState.fs` (it reads the active zone and writes the camera); the camera ops do not touch the
follow camera (FR-018); the ops must not change the shape of the run or the walk (`specs/046`, FR-017)

**Scale/Scope**: two modules changed (`world/Cue.fs`, `world/CueEngine.fs`), one small addition to `world/Zone.fs`,
one new module (`core/Maths.fs`), two editor files changed (`editor/CueTree.fs`'s tables and `editor/CueEditor.fs`'s
waypoint combo), two test files extended, three code comments corrected. No new field, no doc change (no `docs/*.md`
names `MoveCamera`); one fsproj entry is added.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — **net fewer** cases than before if the flight is counted as one op plus its own state: `MoveCamera`+`MoveCameraState`+`CameraPose` leave; `SetCamera`, `FlyCamera`+`FlyCameraState` arrive. No new field, no new mechanism: the ops perform their effect where the walk reaches them, as `Exposit`/`SwitchZone` already do. The geometry is one small, pure `core/Maths.fs` module (reusable and testable on its own); `Zone` gains two small helpers this feature is the first caller of. The editor change is one combo in the panel that already draws this node (FR-019), and it keeps the shared control free of any zone dependency rather than widening it. The rest of the op family stays deferred (research D1). |
| II. Fail Loudly | Pass — a name that resolves to no waypoint, an empty name, a chain of fewer than two waypoints, or a non-positive speed **raises where the op is reached**, matching `GameplayState.switchZone` for an unknown zone. No silent skip and no default. |
| III. Spec-First | Pass — `specs/054-camera-waypoint-ops/spec.md` is written, clarified and drives this plan. |
| IV. Data-Oriented Architecture | Pass — still one discriminated union, no inheritance, no object graph. The flight's in-flight value carries `Vector3[]`/`Quaternion[]` (contiguous arrays, the constitution's preferred hot-path shape) and is otherwise immutable; the only mutation is the op's effect on `state.Camera`, written where the walk reaches it. |
| V. Minimise GC Pressure | Pass, and it is a tested property: an in-flight tick writes the camera and reports `ValueNone` (a struct), so it allocates nothing; only the op's *begin* allocates its resolved path, a bounded amount independent of the cue's length. `test/CueTests.fs` measures the 0 bytes exactly. |
| N-V. Testing is the Authoring Tool | Not engaged as written (the narrative subsystem is untouched), but its substance holds: both ops are plain data on the camera, so the suite exercises them headlessly by placing waypoints in a zone — no device. |

## Project Structure

### Documentation (this feature)

```text
specs/054-camera-waypoint-ops/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── camera-ops.md    # Phase 1 output — the surfaces that change
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
core/
└── Maths.fs         # NEW: the spline geometry — Catmull-Rom position, per-segment slerp facing, and
                     #   sampled arc length, as pure functions over `Vector3`/`Quaternion`. Names no
                     #   zone, screen, asset or device, so it is exercisable on its own.
world/
├── Cue.fs           # CHANGED: remove `MoveCamera`, `MoveCameraState` and `CameraPose`; add
│                    #   `SetCamera of waypoint: string` and
│                    #   `FlyCamera of path: string * speed: float` /
│                    #   `FlyCameraState of path: string * points: Vector3[] * rotations: Quaternion[]
│                    #     * duration: float * endsAt: float`. Still needs `Prime` and `System.Numerics`
│                    #   only, so it still names no zone, screen, asset or device.
├── CueEngine.fs     # CHANGED: handle the two ops where the walk reaches them — resolve a name in the
│                    #   active zone, snap (`SetCamera`) or resolve the chain once and fly (`FlyCamera`);
│                    #   the flight's in-flight case writes the camera each tick and reports `ValueNone`.
│                    #   Take the spline from `core/Maths.fs` and keep only the camera-look helper
│                    #   (position + facing * distance). Still compiled *after* `state/GameplayState.fs`.
├── Zone.fs          # CHANGED: `tryWaypointByName : Zone -> string -> EntityRef option` (first live
│                    #   waypoint of that name, in slot order) and `waypointChain : Zone -> EntityRef ->
│                    #   EntityRef list` (walk `Next`, bounded by the waypoint count so a cycle terminates).
└── Systems.fs       # UNCHANGED: the follow camera is untouched (FR-018).

editor/
├── CueTree.fs       # CHANGED: the kind list ("Set camera", "Fly camera" in place of "Move camera"), the
│                    #   row labels, the lock rule (`FlyCameraState` is executing), which nodes carry text
│                    #   (`SetCamera`'s waypoint, `FlyCamera`'s path) and the `FlyCamera` speed drag.
└── CueEditor.fs     # CHANGED: the panel draws a camera op's waypoint as a combo over the active zone's live
                     #   waypoints, reading the zone itself so `CueTree` names none (`specs/047`, FR-006).

test/
├── CueTests.fs      # CHANGED: the camera scenarios — snap from a waypoint, fly along a chain, the
│                    #   malformed cases, the pinned flight, and the 0-byte in-flight tick.
└── CuePanelTests.fs # CHANGED: the two camera kinds in the panel's tables (labels, defaults, text, lock).

fsharp-game.fsproj   # CHANGED: one `Compile` entry for `core/Maths.fs`, after `core/Input.fs`.
```

**Structure Decision**: the work stays inside the cue's existing modules. `world/Cue.fs` keeps the vocabulary and
names nothing but `Prime` and `System.Numerics`; the two camera ops carry only values (`string`, `float`, and arrays
of `Vector3`/`Quaternion`), so nothing new leaks into the type. The resolution and the geometry live in
`world/CueEngine.fs`, which already owns the ops' effects and is compiled after `state/GameplayState.fs` — it reads
`GameplayState.activeZone` to resolve a name and writes `state.Camera`. The spline geometry goes in
`core/Maths.fs`, because a curve through points belongs to neither the cue nor the camera and is worth testing on its
own. The chain helpers go in `world/Zone.fs` beside `waypointNext`, because a chain is the zone's data and the app
now walks it. One `Compile` entry is added for the new module.

## Phase 0: Research

See [research.md](./research.md). It settles why `MoveCamera` is replaced by two waypoint ops (D1), how a name
resolves and how a chain is walked (D2), what the flight's in-flight value carries and why it is pinned (D3), the
geometry of a flight and the camera's look (D4), where a flight begins and the degenerate case (D5), how failure is
reported (D6), the follow camera's non-interaction (D7), and the editor's tables plus the panel's waypoint combo
(D8).

## Phase 1: Design

- [data-model.md](./data-model.md) — the changed `Cue` type, the `FlyCameraState` fields, the `Zone` helpers, the
  flight's state transitions, and validation rules.
- [contracts/camera-ops.md](./contracts/camera-ops.md) — every surface that changes and what explicitly does not.
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite and a scratch tick that shows a snap
  and a flight.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one small, pure `core/Maths.fs` module and two `Zone` helpers, each justified by use; no new field. |
| II. Fail Loudly | Pass — every malformed case raises where the op is reached; no default, no silent drop. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — one DU unchanged in kind; the flight's path is contiguous arrays; the only mutation is the op's effect on `state.Camera`. |
| V. Minimise GC Pressure | Pass, and tested: the in-flight tick's 0 bytes are asserted in `test/CueTests.fs`; only the op's begin allocates. |
| N-V. Testing is the Authoring Tool | Adopted in substance — both ops are asserted headlessly against a zone built in the suite. |
