# Implementation Plan: Trim Scenario Scope to Narrative Only

**Branch**: `076-scenario-narrative-only` | **Date**: 2026-10-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/076-scenario-narrative-only/spec.md`

## Summary

Narrow a scenario to its **narrative entities**. Today a captured zone (`world/ScenarioZone.fs`) carries the
zone's environment (asset, base, orientation, parts), its light, and its player designation, and the scenario's
contents (`world/ScenarioRegistry.fs`) carry the active place. None of those are narrative: the environment and
light belong to the **place** (the Zone Registry's `ZoneTemplate`), the player designation is per-zone state
(`Zone.Player`), and the active place is runtime state on the gameplay screen. Remove all four from the scenario
record, leaving a `ScenarioZone` of actors, waypoints and triggers (chains by index) plus the cue.

This is a **removal**, so the work is mostly deletion: trim two records, delete the `ScenarioEnvironment` type and
the two helpers that only fed it (environment parts and the player index), drop the `active` parameter from the
capture, update the one editor call site, update the scenario tests, and refresh the five guiding docs. No new
module, no `.fsproj` change, no command change, no persistence migration (the library is not persisted yet).

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: `world/ScenarioZone.fs` (the ref-free authored zone), `world/ScenarioRegistry.fs` (the
contents and the capture), `world/Zone.fs` (the live zone the capture reads), `world/Cue.fs` (the cue the contents
carry), `state/Command.fs` / `state/History.fs` (the `SetScenario` step, unchanged in kind), `editor/ScenarioPanel.fs`
(the one capture call site). All present; no new dependency.

**Storage**: N/A — the scenario library is not persisted (`docs/ROADMAP.md` phase 15), so no stored value holds the
broader shape and no migration is needed.

**Testing**: xUnit, headless (`dotnet test`). The capture is pure over the zones it is handed, so the trimmed record
is asserted with no device — the existing `test/ScenarioTests.fs` builds zones by hand and drives the step through
`History`.

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor)

**Performance Goals**: editor-only cold path — a registration runs on a click; the play loop is untouched. Trimming
the capture removes work (no environment read/re-encode) rather than adding any.

**Constraints**: no cue vocabulary, no fork (spec FR-011); the build is warning-free; the suite is green; no device
opened; no new source file and no `.fsproj` change.

**Scale/Scope**: 0 new source files; 4 source files touched (`world/ScenarioZone.fs`, `world/ScenarioRegistry.fs`,
`editor/ScenarioPanel.fs`, `test/ScenarioTests.fs`); 5 docs touched (`docs/ARCHITECTURE.md`, `docs/EDITOR_DESIGN.md`,
`docs/ZONE_MODEL_DECISION.md`, `docs/ROADMAP.md`, `docs/PROJECT_STRUCTURE.md`).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. The change *removes* a type (`ScenarioEnvironment`), two helpers, three record fields and
  one contents field. It is the strongest possible satisfaction of the principle: no abstraction is added.
- **II. Fail Loudly** — PASS. No behaviour changes except what is dropped. The one raise in the capture (a waypoint
  whose `Next` names no live waypoint) stays exactly as it is; the removal adds no fallback and no sentinel.
- **III. Spec-First** — PASS. This feature is `specs/076-scenario-narrative-only`.
- **IV. Data-Oriented Architecture** — PASS. Records and arrays; the scenario zone stays a ref-free value with three
  arrays, which is *more* data-oriented after the trim, not less.
- **V. Minimise GC Pressure** — PASS. The capture is a cold, editor-only path; the trim reduces the value built and
  the strings/arrays it copies (no environment parts array on every register).

The **Narrative Subsystem** principles do not apply: this feature touches the scenario/cue world layer
(`world/ScenarioZone.fs`, `world/ScenarioRegistry.fs`) and the editor, not `narrative/`.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/076-scenario-narrative-only/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── scenario-capture.md
├── checklists/
│   └── requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
world/ScenarioZone.fs       # CHANGED: ScenarioZone loses Environment/Light/Player; delete ScenarioEnvironment and
                            #   the captureEnvironmentParts / player-index helpers; capture reads only actors,
                            #   waypoints and triggers
world/ScenarioRegistry.fs   # CHANGED: ScenarioContents loses Active; capture drops its `active` parameter
editor/ScenarioPanel.fs     # CHANGED: the Register call becomes `capture state.Zones state.Cue`
test/ScenarioTests.fs       # CHANGED: drop the active-place and player-index assertions; assert the trimmed record

docs/ARCHITECTURE.md        # "Scenario & Cue": the scenario zone is actors/waypoints/triggers by index; no player,
                            #   environment or light; the contents carry zones + cue, no active place
docs/EDITOR_DESIGN.md       # Workspace: Register files the scene's narrative entities and the cue (not the active
                            #   place or the zone's staging)
docs/ZONE_MODEL_DECISION.md # the ScenarioZone shape, the four-value table, and the convergence prose drop the
                            #   environment/light/player
docs/ROADMAP.md             # phase 11 deliverables: the ref-free zone is entities + chains by index (no player)
docs/PROJECT_STRUCTURE.md   # the ScenarioZone.fs line
```

**Structure Decision**: No module moves and no new files. The trimmed `ScenarioZone` stays in
`world/ScenarioZone.fs` and `ScenarioContents` in `world/ScenarioRegistry.fs`, at their existing points in the
compile list (`world/ScenarioZone.fs` after `world/Zone.fs`, `world/ScenarioRegistry.fs` after it), so nothing
downstream of them is disturbed. `state/Command.fs`'s `SetScenario` case names `ScenarioContents` and is unchanged
in kind — only the type it names shrinks — and `state/History.fs`'s `SetScenario` runner calls `setContents`, also
unchanged. The one capture call site is the Scenario panel (`editor/ScenarioPanel.fs`), which already writes through
`History.commit`; its preview reads `contents.Zones.Count` and is unaffected.

## Complexity Tracking

> No Constitution Check violations.
