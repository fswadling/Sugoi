# Implementation Plan: Scenarios as a String-Keyed Ordered Library

**Branch**: `083-scenario-strings` | **Date**: 2026-10-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/083-scenario-strings/spec.md`

## Summary

Replace the hard-coded `Scenario` discriminated union with a plain **string name** and the scenario library
with an **ordered map keyed by name** — Prime's `OMap<string, ScenarioContents option>`, which iterates in the
order names were added and keeps an overwritten key's position. `world/Scenario.fs` (the vocabulary: `type
Scenario = Intro`, `scenarioName`, `all`) is **deleted**; `world/ScenarioRegistry.fs` becomes the library's whole
home, with `Registry = OMap<string, ScenarioContents option>` (a key present with `None` is a scenario named but
not registered yet). `GameplayState.Scenarios` becomes `mutable` and holds that persistent value; `Command.SetScenario`
carries the library **before and after** an edit (the map is persistent, so a snapshot is a reference pair), the same
shape `Command.SetCue` has. `Cue.ForkScenario` carries a `string` instead of a `Scenario`, and `world/Cue.fs` no
longer names `Scenario` at all.

The **Scenario panel** is rewritten: a single-line **text field** and an **Add** control name a new scenario, and
each row carries **Register**, **Clear**, **Fork** and **Delete** — Register files the current scene, Clear empties
a scenario's contents but keeps its name, Delete removes the name and its contents. A name is trimmed of surrounding
whitespace and matched **exactly (ordinal, case-sensitive)** (`specs/074`/`specs/082`'s rule). The cue panel's
scenario chooser offers the **current** scenario names and accepts a **typed** name that matches none
(`editor/CueTree.fs`'s existing `filteredNameCombo`); typing a new name does **not** add it to the library. There is
no vocabulary and no cross-reference check — spelling correctness is the author's responsibility, the deliberate
trade.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: Prime (`OMap`, already referenced — no new package), `world/ScenarioRegistry.fs` (the
library's home), `world/Cue.fs`, `world/ScenarioFork.fs` (unchanged), `state/GameplayState.fs`, `state/Command.fs`,
`state/History.fs`, `state/EditorState.fs` (a new text buffer), `editor/ScenarioPanel.fs` (rewritten),
`editor/CueTree.fs` (labels, defaults, chooser), `editor/CueEditor.fs` (threading the scenario names).

**Storage**: N/A (nothing persisted — phase 15, unchanged from `specs/070`)

**Testing**: xUnit, headless (`dotnet test`). `test/ScenarioTests.fs` rewritten for the string-keyed library;
`test/ScenarioForkTests.fs` migrated to string names; `test/CueTests.fs` and `test/CuePanelTests.fs` migrated
(`ForkScenario "Intro"`; `Scenario.scenarioName` gone from the `describe` helpers; the default op becomes `""`).
No device.

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor, narrative engine in-process)

**Performance Goals**: the library is off the play path (editor-only), and `ForkScenario` is a cold op reached once
when a cue op runs — no per-frame allocation concern. Whole-library before/after snapshots are cheap: `OMap` is
persistent, so a snapshot shares structure with the value it was taken from.

**Constraints**: warning-free build; green headless suite; no device opened; `world/Scenario.fs` is **deleted**, so
its `<Compile>` line is removed from `fsharp-game.fsproj` (no other entry reorders); the panel's window stays named
`"Scenario"`, so the native dock layout (`native/cimgui_sdl3.cpp`) is untouched.

**Scale/Scope**: game source — 1 module deleted (`world/Scenario.fs`), 1 module rewritten (`world/ScenarioRegistry.fs`),
1 panel rewritten (`editor/ScenarioPanel.fs`), ~8 edited (`world/Cue.fs`, `world/CueEngine.fs`, `state/GameplayState.fs`,
`state/Command.fs`, `state/History.fs`, `state/EditorState.fs`, `editor/CueTree.fs`, `editor/CueEditor.fs`), plus
`fsharp-game.fsproj`; tests — 1 rewritten, ~3 edited; docs — `ARCHITECTURE.md`, `EDITOR_DESIGN.md`,
`PROJECT_STRUCTURE.md`, `ROADMAP.md`, `CUE_MODEL_DECISION.md`. One file deleted; no file added.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. It removes the union, its reflection enumeration (`all`) and its display name
  (`scenarioName`), and replaces them with one ordered map and a handful of one-line operations. The panel keeps its
  controls and gains exactly one (Delete) and one text buffer; the library model collapses two things (the vocabulary
  and the library) into one. The ordered map comes from an existing dependency (Prime), so nothing is added to the
  project.
- **II. Fail Loudly** — PASS. Nothing unexpected is swallowed: a blank add is refused as a value, not a silent
  default, and a fork of a missing scenario is an **expected** authoring reality (`specs/074`'s stance), reported as
  a diagnostic no-op rather than raising. The deliberate removal of compile-time checking is the feature's premise —
  there is no vocabulary to validate against, and the author owns spelling.
- **III. Spec-First** — PASS. This feature is `specs/083-scenario-strings`.
- **IV. Data-Oriented Architecture** — PASS. A scenario is a string key in an immutable persistent map; the contents
  are plain records and immutable maps. No object graph, no inheritance, no wrapper type beyond the map alias.
- **V. Minimise GC Pressure** — PASS. The library is read and written only by the editor and the cold `ForkScenario`
  op; there is no per-frame path to keep allocation-free. The persistent map makes the whole-library undo snapshots
  structural rather than deep copies.
- **Narrative subsystem (N-I…N-V)** — N/A. The narrative engine names no scenario; the change does not reach it.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/083-scenario-strings/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── scenario-library.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
fsharp-game.fsproj            # DELETE the <Compile Include="world/Scenario.fs" /> line; correct the neighbouring
                             #   "closed union" comments for ScenarioRegistry.fs / Cue.fs
world/Scenario.fs            # DELETED: the closed union, `scenarioName`, `all` (the vocabulary) are gone
world/ScenarioRegistry.fs    # REWRITTEN: `type Registry = OMap<string, ScenarioContents option>` (Prime); the
                             #   library operations `create`/`names`/`exists`/`tryContents`/`add`/`remove`/
                             #   `setContents`/`normalise`; `ScenarioContents` and `capture` unchanged; `open Prime`,
                             #   drop `open Scenario`; header comment rewritten
world/Cue.fs                 # `ForkScenario of scenario: string`; drop `open Scenario`; header comment corrected
world/CueEngine.fs           # `ForkScenario` reads a string; `noOp "ForkScenario" scenario …`; drop the
                             #   `Scenario.scenarioName` call
state/GameplayState.fs       # `mutable Scenarios : ScenarioRegistry.Registry`; `create` keeps `ScenarioRegistry.create ()`
state/Command.fs             # `SetScenario of Previous : ScenarioRegistry.Registry * Next : ScenarioRegistry.Registry`
                             #   (whole-library pair, like `SetCue`); drop `open Scenario`; comment reworded
state/History.fs             # `| SetScenario (_, next) -> state.Scenarios <- next` (apply) and `(previous, _)` (undo);
                             #   comment reworded
state/EditorState.fs         # NEW field `ScenarioNameBuffer : byte[]` for the panel's text field
editor/ScenarioPanel.fs      # REWRITTEN: text field + Add, and a list of the names each with Register/Clear/Fork/Delete
editor/CueTree.fs            # `withScenario` takes a `string`; `label` renders the raw name (omitted when empty);
                             #   `defaultOfKind "Fork scenario" -> ForkScenario ""`; drop `open Scenario`
editor/CueEditor.fs          # `drawNodeText` gains `scenarios: string list`; its `ForkScenario` case becomes
                             #   `filteredNameCombo` over the current names (pick or type); both `draw` and
                             #   `drawTrigger` pass `ScenarioRegistry.names state.Scenarios`

test/ScenarioTests.fs        # REWRITTEN: the string-keyed library (order, add/delete, trim/blank/duplicate),
                             #   register/clear, one-step undo, zone survival, the zone-level refusal
test/ScenarioForkTests.fs    # migrated to string names (`Intro` -> "Intro"); the materialiser and fork assertions
test/CueTests.fs             # `ForkScenario "Intro"`; `Scenario.scenarioName` -> the raw string in `describe`
test/CuePanelTests.fs        # `ForkScenario ""` default; labels read the raw name; the scenario op description
docs/ARCHITECTURE.md         # the Scenario & Cue section and the `Scenario_Library` mermaid node
docs/EDITOR_DESIGN.md        # the Scenario panel description (a list editor, not a fixed set of rows)
docs/PROJECT_STRUCTURE.md    # the `Scenario.fs` / `ScenarioRegistry.fs` / `ScenarioPanel.fs` lines
docs/ROADMAP.md              # the phase-11 "scenario library" passage
docs/CUE_MODEL_DECISION.md   # the scenario-library-off-the-one-history passages
```

**Structure Decision**: single project, F#'s file-order model. `world/Scenario.fs` is **removed** from the
`.fsproj` list — once `Cue.ForkScenario` carries a string, nothing names the vocabulary, and the library (which
depends on `Cue` and `ScenarioZone`) must stay compiled *after* them, so it cannot be hosted in the file's former
slot. `world/ScenarioRegistry.fs` keeps its filename and compile position and becomes the library's whole home.
Every consumer that held a `Scenario` value (the cue op, the command, the panel, the cue chooser) holds a `string`
instead, and every consumer that held the `Registry` dictionary holds the `OMap` — the same persistent shape `Advent`'s
set has — so the undo step, the state field and the panel's write path keep their structure. The only new state is
one text buffer on the editor's transient state, beside the zone-name and advent-name fields it mirrors.

## Complexity Tracking

> No Constitution Check violations.
