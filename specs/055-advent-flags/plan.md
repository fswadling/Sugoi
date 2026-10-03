# Implementation Plan: Adventure Flags and the Advent Panel

**Branch**: `055-advent-flags` | **Date**: 2026-10-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/055-advent-flags/spec.md`

## Summary

Add the story milestones — `world/Advent.fs`, a **closed union** `Advent`, with the flags a plain
`Set<Advent>` — as a `mutable Advents` field on `SharedState`, and its authoring surface
`editor/AdventPanel.fs`, the **Advent** tab docked on the **right** beside the Inspector. Each tick
and untick is one state-level step on the one undo history (`Command.Advent`, carried by
`state/History.fs`, as `SetCue` is), so a flag set by mistake is one `Ctrl+Z` from gone and the flags
share the timeline of the zone moves and cue edits around them. `editor/Editor.fs` draws the Inspector
last, so it is the selected tab when the editor opens.

The work is small and lands entirely in existing clusters:

- `world/Advent.fs` — NEW: the milestone union and the two helpers `Set` cannot know — `all` (the
  cases, enumerated from the type) and `name` (their display names). Pure; depends on FSharp.Core
  alone, so it loads on its own.
- `state/SharedState.fs` — one field, `mutable Advents : Set<Advent>`, beside `Cast` and `ZoneRegistry`.
- `state/Command.fs` — one state-level case, `Advent of Previous * Next`; it joins the group the
  zone-level runner refuses (like `SetCue`), because it is not a zone's.
- `state/History.fs` — the two arms that write `shared.Advents` on apply/undo.
- `editor/AdventPanel.fs` — NEW: the milestone checklist and the write path.
- `editor/Editor.fs` — draw the panel; draw the Inspector last so it is the startup tab.
- `native/cimgui_sdl3.cpp` — one `DockBuilderDockWindow("Advent", right)` line + `native/build.sh`.
- `fsharp-game.fsproj` — two `Compile` entries (the module and the panel).
- The five `SharedState` record fixtures in `test/` gain `Advents = Set.empty`, and a new
  `test/AdventTests.fs` asserts the union, the set, the write path and the history.

No cue vocabulary changes (FR-011): the advent cue ops stay deferred. No new `EditorState` field: a
closed union needs no text buffer.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`), `TreatWarningsAsErrors`-clean build

**Primary Dependencies**: FSharp.Core's `Set` and reflection (already used); SDL3, Dear ImGui and Jolt
Physics are untouched. No new dependency.

**Storage**: none — the set is session state (FR-010); persistence is phase 15

**Testing**: xUnit via `dotnet test` (headless). A `GameplayState`/`SharedState` is built the way
`test/CuePanelTests.fs` does; the union, the set, the panel's write path and the history steppers are
asserted with no window, GPU or device

**Target Platform**: Linux desktop (SDL_gpu/Vulkan)

**Project Type**: desktop game with an in-engine editor (single project; one `.fsproj` per the app and
its tests)

**Performance Goals**: 60 fps; the panel draws off the hot path (editor mode only), and the checklist
allocates nothing per frame beyond the `Advent.all` list it walks (Constitution V)

**Constraints**: the constitution's five principles; `world/Advent.fs` names no zone, screen, asset or
device type (it takes only its own union and `string`), so it stays loadable on its own; the panel
writes through `History.commit`, so the step is applied and pushed in one place

**Scale/Scope**: one new `world/` module, one new `editor/` panel, one field, one command case, two
`History` arms, one `Editor` draw order, one native line, two fsproj entries, five test-fixture
updates, one new test file. No new dependency, no new `EditorState` field.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one tiny pure module (a union and a plain `Set`), one panel, one field, one command case. No abstraction is introduced for the cue ops that are not in this cut. The panel is a single loop over the union's cases. |
| II. Fail Loudly | Pass — the write path is total; there is no "unexpected" branch. A closed union makes a misspelt milestone a compile error, and `name`'s explicit match turns a new case into a build failure rather than a silent default. |
| III. Spec-First | Pass — `specs/055-advent-flags/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — a closed union and an immutable `Set<Advent>` on a record field, no inheritance, no object graph. |
| V. Minimise GC Pressure | Pass — the set is cold (toggled by hand, read at a branch). A toggle allocates one small persistent set, which is the snapshot the undo step needs; the panel's per-frame cost is the `Advent.all` list. |
| N-V. Testing is the Authoring Tool | Not engaged as written (the narrative subsystem is untouched), but its substance holds: the union, the set and the write path are pure data and are asserted headlessly. |

## Project Structure

### Documentation (this feature)

```text
specs/055-advent-flags/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── advent-flags.md  # Phase 1 output — the surfaces that change
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
world/
└── Advent.fs            # NEW: the milestone union and the two helpers `Set` cannot know — `all` (the
                         #   cases, enumerated from the type) and `name` (an explicit match). The flags
                         #   are a plain `Set<Advent>`. Loads on its own.

state/
├── SharedState.fs       # CHANGED: `mutable Advents : Set<Advent>` beside `Cast`/`ZoneRegistry`;
                         #   `create` seeds it with `Set.empty`.
├── Command.fs           # CHANGED: the state-level case `Advent of Previous : Set<Advent>
                         #   * Next : Set<Advent>`; it joins the zone-level runner's refuse group.
└── History.fs           # CHANGED: `Advent (_, next) -> shared.Advents <- next` on apply and
                         #   `Advent (previous, _) -> shared.Advents <- previous` on undo.

editor/
├── AdventPanel.fs       # NEW: the milestone checklist — one checkbox per `Advent.all`, seeded with
│                        #   `Set.contains`; a toggle is one `Advent` step via `History.commit` (FR-007).
└── Editor.fs            # CHANGED: draw `AdventPanel`; draw the Inspector LAST so it is the selected
                         #   tab when the editor opens (research D5).

native/
└── cimgui_sdl3.cpp      # CHANGED: `DockBuilderDockWindow("Advent", right)`, beside Inspector and Cue.

test/
├── AdventTests.fs       # NEW: the union's cases and order; the set; the write path; the history round
│                        #   trip, a flag edit beside a zone move, and the zone-level refusal.
├── CueTests.fs          # CHANGED: the `SharedState` fixture gains `Advents = Set.empty`.
├── CuePanelTests.fs     # CHANGED: ditto.
├── GameplayStateTests.fs# CHANGED: ditto.
├── SelectionTests.fs    # CHANGED: ditto.
├── SpawningTests.fs     # CHANGED: ditto.
└── fsharp-game.Tests.fsproj  # CHANGED: one `Compile` entry for `AdventTests.fs`.

fsharp-game.fsproj       # CHANGED: `Compile` for `world/Advent.fs` (after `world/EnvironmentRender.fs`)
                         #   and `editor/AdventPanel.fs` (after `editor/CastPanel.fs`).
```

**Structure Decision**: the union and its set go in `world/` because they are game data read by the
game, not UI state (`docs/PROJECT_STRUCTURE.md` already names `world/Advent.fs`); the module sits
beside the other pure data modules and is listed before `state/`, which names it. The panel goes in
`editor/` beside the other editing surfaces. The command is a new case on the existing `Command` union
handled by the existing one history (`state/History.fs`) — no new stack, because `specs/048` already
made the one history carry non-zone steps. The startup-tab behaviour is a draw-order choice in
`editor/Editor.fs`, not a new mechanism.

## Phase 0: Research

See [research.md](./research.md). It settles the union's shape (D1), the set's home on `SharedState`
(D2), its edit as a state-level step on the one history (D3), the panel's right dock and native line
(D4), the Inspector's startup selection (D5), the checklist interaction (D6), and the command's
whole-set payload (D7).

## Phase 1: Design

- [data-model.md](./data-model.md) — the `Advent` union and the flags `Set`, the `SharedState` field, the
  `Command.Advent` case, the `History` arms, and the validation rules.
- [contracts/advent-flags.md](./contracts/advent-flags.md) — every surface that changes and what
  explicitly does not.
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite and a scratch toggle.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — a union, a `Set` field, one panel, one field, one case; nothing speculative. |
| II. Fail Loudly | Pass — the closed union makes a bad milestone a compile error; the write path is total. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — a closed union and an immutable set on a record, no object graph. |
| V. Minimise GC Pressure | Pass — cold set; one small set and the `Advent.all` list per interaction. |
| N-V. Testing is the Authoring Tool | Adopted in substance — the union, set and write path are asserted headlessly. |
