# Implementation Plan: Automatic Persistence of the Scenario Library

**Branch**: `095-scenario-persistence` | **Date**: 2026-10-16 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/095-scenario-persistence/spec.md`

## Summary

Persist the scenario library automatically, in Prime's symbolic-expression syntax, with no save or load command.
A new module `world/Project.fs` — compiled after `world/ScenarioRegistry.fs`, because it names the whole authored
tree — holds a **serializable record tree** that mirrors the authored types, a pure **`encode`/`decode`** pair over
that tree, and a **`load`/`persist`** pair over one file. The tree exists because Prime's symbolic converter does
not encode everything the authored types contain: `System.Numerics.Vector3`/`Quaternion` are written as unreadable
text, and Prime's ordered map (`OMap`) is unsupported; the tree flattens those two to scalar fields and an ordered
list. Everything else — records, unions (the cue), options, lists, arrays, `Map`, and the cue's `FDeque` — encodes
as-is.

The library is read **once at startup** in `screen/ScreenManager.fs` (`Project.load ()`), not in
`GameplayState.create` — every headless fixture calls `create`, so a disk read there would make the suite touch the
filesystem. It is written from `screen/GameplayScreen.fs` at the end of each frame: `Project.persist` compares the
current library **by reference** to the last one written and writes only when it differs. The library is an
immutable value, so every change (add, clear, register, delete, and the undo or redo of any of them) yields a **new**
instance — one reference comparison detects all of them, which is why persistence follows the *value* rather than
any one control. Writes are **atomic** (`.tmp` + rename). A **missing** file is not an error (empty library,
nothing written until the first change); a **damaged** one is reported and leaves the library empty, the file
untouched. The file is not a `GameplayState` concern, so no state owner is added.

One refinement touches the editor: the **Register** control is disabled (with a tooltip) while the screen's cue is
still mid-run, so a run-form cue is never filed — detected with the editor's existing executing-cue predicate
(`CueTree.executing`, spec 047), the same notion the Cue panel already uses to lock a running node.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: Prime (`scstring`/`scvalue`/`prettyPrintValue`, already referenced — no new package),
`System.IO` (`File`, `Path`), `core/Diagnostics.fs` (the report surface), and — for the editor refinement —
`render/ImGui.fs` (one tooltip extern) with `editor/ScenarioPanel.fs` and `editor/CueTree.fs` (the executing-cue
predicate). The authored tree it mirrors lives in
`world/Entity.fs`, `world/ZoneTemplate.fs`, `world/ScenarioZone.fs`, `world/ScenarioRegistry.fs`, `world/Cue.fs`,
`render/Texture.fs` (`LightSettings`) and `render/Skydome.fs` (`Settings`).

**Storage**: one human-readable s-syntax file, `project.manifest`, in the working directory the game is launched
from (beside `assets/`); written atomically via `project.manifest.tmp` + rename.

**Testing**: xUnit, headless (`dotnet test`). A new `test/ProjectTests.fs` covers the pure round-trip and the file
handling (missing / damaged / atomic). No device; no save/load command.

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor, narrative engine in-process)

**Performance Goals**: off the play path. The per-frame cost is one reference comparison; a write happens only when
the library changed (once per author action). `encode`/`decode` run only at startup and on a change, never per frame.

**Constraints**: warning-free build; green headless suite; no device opened; the record tree is limited to the
shapes Prime's symbolic converter round-trips (verified against Prime 11.5.2); the live scene, active place, camera,
running cue, advents and mix are **not** persisted; the Register control disables (with a tooltip) while the screen's
cue is executing, so a run-form cue is never filed.

**Scale/Scope**: game source — 1 new module (`world/Project.fs`), 5 edited (`screen/ScreenManager.fs`,
`screen/GameplayScreen.fs`, `render/ImGui.fs`, `editor/ScenarioPanel.fs`, `fsharp-game.fsproj`); tests — 1 new
(`test/ProjectTests.fs`) + its `.fsproj` line; docs — `ARCHITECTURE.md`, `ROADMAP.md`. No file deleted.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. One module holds the whole feature; it adds no command, no menu item, no *new*
  control and no new state owner — only one ImGui tooltip extern (`render/ImGui.fs`) and a disabled state on the
  existing Register control. The record tree is mechanical, not speculative, and every field in it is one the spec
  requires to round-trip. It uses an existing dependency (Prime) rather than a new one, and a single reference
  comparison replaces any dirty-flag or hook scheme.
- **II. Fail Loudly** — PASS. A damaged or unreadable file is **reported** (`Diagnostics.reportf`) and yields an
  empty library rather than a silent default or a crash; a write failure is reported and skips the write rather than
  leaving a partial file. The one deliberately non-fatal case — a missing file — is the *expected* first-run path,
  which is exactly what an `option`/empty value is for.
- **III. Spec-First** — PASS. This feature is `specs/095-scenario-persistence`, deriving from `specs/083` FR-016's
  named deferral.
- **IV. Data-Oriented Architecture** — PASS. It is plain records with named fields over the authored records — the
  same data-oriented shape the manifests use — and adds no object graph or inheritance. The ordered map is flattened
  to an ordered list at the boundary only.
- **V. Minimise GC Pressure** — PASS. The whole path is cold: startup and author actions, never a per-frame
  allocation. The per-frame check is a reference comparison, which allocates nothing.
- **Narrative subsystem (N-I…N-V)** — N/A. The narrative engine holds no scenario library; the change does not reach
  it.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/095-scenario-persistence/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── checklists/
│   └── requirements.md
├── contracts/
│   └── project-file.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
fsharp-game.fsproj            # ADD <Compile Include="world/Project.fs" /> immediately after world/ScenarioRegistry.fs
world/Project.fs             # NEW: the serializable record tree, `encode`/`decode`, `loadFrom`/`saveTo`, and the
                             #   `load`/`persist` entry points with the last-written library (module-level `Store`)
screen/ScreenManager.fs      # `init` loads the library once: `gameplay.Scenarios <- Project.load ()`
screen/GameplayScreen.fs     # `update` writes on change at end of frame: `do Project.persist state.Scenarios`
render/ImGui.fs              # ADD one tooltip extern (`igSetItemTooltip`) so a control can explain a disabled state
test/fsharp-game.Tests.fsproj # ADD <Compile Include="ProjectTests.fs" /> (last)
test/ProjectTests.fs         # NEW: round-trip (equal, scenario for scenario), missing/damaged/version handling,
                             #   atomic write, the "no write when unchanged" property, and the mid-run Register guard
editor/ScenarioPanel.fs      # Register disabled + tooltip while the screen's cue is still mid-run (`CueTree.executing`)
docs/ARCHITECTURE.md         # the Persistence section: s-syntax project file, library-only scope (was JSON DTO)
docs/ROADMAP.md              # phase 15: the project-file deliverables and "done when"
```

**Structure Decision**: single project, F#'s file-order model. `world/Project.fs` must compile **after**
`world/ScenarioRegistry.fs` (it names `Registry`, `ScenarioContents`, `ScenarioZone`, `ZoneStaging`/`ZoneTemplate`,
`Entity`/`ActorState`/`PropState`/`TriggerState`, `Cue` and `Texture`/`Skydome`), so it is inserted directly after
it and before `world/Systems.fs`. It sits in `world/` (not `state/`) because its inputs are all `world/` values and
its two entry points take the library **by value** — `persist (registry: Registry)` needs no `GameplayState` type,
so the module needs nothing from `state/` and no state owner is added. The two call sites are the production seams
that tests never touch: `ScreenManager.init` (startup, once) and `GameplayScreen.update` (per frame). The Register
guard reuses the existing `CueTree.executing` predicate (no new module), and the tooltip needs only an extern in
`render/ImGui.fs` (the native library already exports it). Nothing else changes shape.

## Complexity Tracking

> No Constitution Check violations.
