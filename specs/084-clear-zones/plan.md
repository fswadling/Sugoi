# Implementation Plan: Clear the World Back to a Fresh Start

**Branch**: `084-clear-zones` | **Date**: 2026-10-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/084-clear-zones/spec.md`

## Summary

Add a **Clear World** action that removes every zone from the live world and installs a single fresh **start** zone,
and resets the **running cue** to empty, as **one** undoable step. The world is the unit of work, so its half is a
**state-level** command: a new `Command.ZonesClear` carries the previous world (each zone's name and its
`ZoneContents`, plus the active place), and `state/History.fs` carries it exactly as it carries `ZoneDelete`. The cue
half reuses the existing state-level `SetCue` step, committed in the same `Batch` so one clear is one step (and
`pruneCueSteps` still reaches into a batch on Play, keeping the zone step undoable). Apply and undo are the same
operation with different arguments — a **world swap** — so one helper, `GameplayState.replaceZones`, retires every
present zone (bodies destroyed, renderers and batch freed, environments unloaded once unreferenced) and rebuilds the
given set through `Zone.occupyZone`, deactivating all but the active place. The action joins the **Edit menu**
("Clear World", `Ctrl+Shift+C`) and `Editor.handleShortcuts`; the snapshot is taken **on invoke**, never per frame.
The camera, the scenario library, the fade and the dialogue are untouched. Nothing is added to or removed from the
build: one command case, one state helper, one `Zone` value, one `Input` key, and two editor lines.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: no new packages — SDL3-CS (the new `C` scancode), the existing ImGui binding (the menu
item), Jolt (`Physics.releaseBody`, reached through `Zone.release`). Modules touched: `core/Input.fs`,
`world/Zone.fs`, `state/Command.fs`, `state/GameplayState.fs`, `state/History.fs`, `editor/Editor.fs`.

**Storage**: N/A (nothing persisted; the world is session state — phase 15)

**Testing**: xUnit, headless (`dotnet test`). `test/GameplayStateTests.fs` drives the clear and its undo/redo through
`History` using the file's existing device-free fixture (`Gpu = Unchecked.defaultof`, `TestSupport.manager
IntPtr.Zero`, `GameplayState.create …`); `test/InputTests.fs` adds `Key.C` to the key-uniqueness assertion. No device.

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor)

**Performance Goals**: the clear is a cold editor action taken once per click (and once per undo/redo), never on a
frame path. `Zone.captureZone` allocates, so the snapshot is taken **inside the invoke path**, not each frame; the
menu item draws from nothing captured, so `editor/Editor.fs` adds no per-frame allocation.

**Constraints**: warning-free build; green headless suite; no device opened; **no `.fsproj` change** (no file added or
removed — every edit extends a module already in the compile list).

**Scale/Scope**: game source — 6 files edited (`core/Input.fs`, `world/Zone.fs`, `state/Command.fs`,
`state/GameplayState.fs`, `state/History.fs`, `editor/Editor.fs`); tests — 2 edited (`test/GameplayStateTests.fs`,
`test/InputTests.fs`); docs — `docs/EDITOR_DESIGN.md`. No file added; no file removed.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. One command case, one world-swap helper, one `Zone` value, one `Input` key and two editor
  lines. The teardown is **reused** (`Zone.release`, `Assets.releaseEnvironment`, `Zone.occupyZone`) — no new runtime
  teardown is introduced. No new module, no new file, no new dependency.
- **II. Fail Loudly** — PASS. A clear's target state is always constructible, so there is no expected-failure path to
  swallow; `GameplayState.replaceZones` `invalidOp`s if the named active place is not among the zones it installs — a
  broken invariant, not a condition. Undo/redo are total (the payload carries the whole world).
- **III. Spec-First** — PASS. This feature is `specs/084-clear-zones`.
- **IV. Data-Oriented Architecture** — PASS. The step's payload is plain data — a list of `(string * ZoneContents)`
  and a string — reusing the existing `ZoneContents` record; no object graph, no wrapper type.
- **V. Minimise GC Pressure** — PASS. The clear and its snapshot are off every frame path (taken on invoke and on
  undo/redo); the menu draws without capturing anything.
- **Narrative subsystem (N-I…N-V)** — N/A. The narrative engine touches no zone.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/084-clear-zones/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── clear-world.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
core/Input.fs               # add the `C` key: `| C` in the `Key` union, `| C -> 1u <<< 22` in `Key.mask`, and
                            #   `int SDL.Scancode.C, Key.mask Key.C` in `keyMap`
world/Zone.fs               # add `emptyContents : ZoneContents` beside `captureZone`/`occupyZone`
state/Command.fs            # add `ZonesClear of Previous : (string * ZoneContents) list * PreviousActive : string`;
                            #   add it to `placeOf`'s `None` arm and both `apply`/`undo` invalidOp arms
state/GameplayState.fs      # add `replaceZones shared state zones active` — retire every present zone + unload each
                            #   distinct environment, rebuild the set via `occupyZone`, deactivate all but the active
                            #   place, set the active place, drop the selection/drag baseline
state/History.fs            # `ZonesClear` apply/undo through `GameplayState.replaceZones`; add the public
                            #   `clearZones` write path (capture the zones + the running cue, commit `Batch
                            #   [ ZonesClear …; SetCue (before, Cue.Fin) ]` when the cue is not already `Fin`)
editor/Editor.fs            # Edit menu item "Clear World" ("Ctrl+Shift+C", `idle`-gated); the shortcut in
                            #   `handleShortcuts` (edit mode, `Drag = None`)

test/GameplayStateTests.fs  # the clear headlessly: whole-world teardown, fresh start, one-step undo/redo, the active
                            #   place restored, environments unloaded, the step refused by the zone-level runner
test/InputTests.fs          # `Key.C` added to the distinct-bit assertion
docs/EDITOR_DESIGN.md       # the Edit menu/shortcut description, the D-011 command list, the module map
```

**Structure Decision**: single project, F#'s file-order model. **No `<Compile>` line changes** — every edit extends a
module already listed. The command type stays in `state/Command.fs` (above the state) while its runner lives in
`state/History.fs` (after `GameplayState`, because it swaps the whole world), the same split every state-level step
uses (`ZoneDelete`, `SetCue`, `SetScenario`); the command type cannot move without a cyclic edge, since `EditorState`
names `UndoStack`, which names `Command`. The world-swap helper belongs to `GameplayState` (it owns the zones and the
physics world); the public capture-and-commit path belongs to `History` (compiled after `GameplayState`, so it can
commit), mirroring the panel `commit` functions that keep a write path drivable headlessly.

## Complexity Tracking

> No Constitution Check violations.
