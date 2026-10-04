# Implementation Plan: Print & Trigger Cue Ops

**Branch**: `067-print-trigger-cue-ops` | **Date**: 2026-10-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/067-print-trigger-cue-ops/spec.md`

## Summary

Add the last two op-portable pieces of odin's cue family: the **trigger toggles** `EnableTrigger` /
`DisableTrigger`, each naming a **place** and a trigger name in it and flipping that trigger's enabled flag
(the cross-zone convention); and **`Print`**, a developer-facing line written to the console where the walk
reaches it. All three are no-progress leaves. A toggle resolves the trigger through a new
`Zone.tryTriggerByName` (the same first-match rule `tryWaypointByName` uses) and writes the flag through
`Zone.setTrigger`, leaving the trigger's runtime state to the next play tick; it performs nothing — and says so
on stderr — when the place is not present, the trigger is missing, or the flag already holds. No new module: the
ops go in `world/Cue.fs`, their effects in `world/CueEngine.fs`, the lookup in `world/Zone.fs`, and editor
entries in `editor/CueTree.fs` / `editor/CueEditor.fs`.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: the trigger payload (`TriggerState`, `Trigger.enabled` through `Entity.Enabled`),
`Zone.tryTriggerAt` / `Zone.setTrigger` and the name-lookup shape of `Zone.tryWaypointByName`, the cue engine
(`world/CueEngine.fs` `advance` / `tick`), the closed place set (`Place`), and the console (`printfn`) — all
present. `Zone.tryTriggerByName` is the one new helper.

**Storage**: N/A

**Testing**: xUnit, headless (`dotnet test`); a trigger is spawned with `Zone.spawnTrigger` and the effect
asserted through `Zone.tryTriggerAt`, then the play systems stepped to observe firing/tracking.

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor)

**Performance Goals**: no per-frame cost — every op here is a no-progress leaf performed where the walk reaches
it and gone the same tick; a toggle is one zone lookup and one slot write, `Print` one `printfn`.

**Constraints**: all runtime effects (no undo step); a no-op on a place that is not present, a missing trigger,
or a redundant toggle, reported on stderr; the build is warning-free; the suite is green.

**Scale/Scope**: 4 source files (`world/Cue.fs`, `world/CueEngine.fs`, `world/Zone.fs`, and the two editor
files `editor/CueTree.fs` + `editor/CueEditor.fs` — 5 counting both editors); 2 test files (`test/CueTests.fs`,
`test/CuePanelTests.fs`), optionally `test/ZoneTests.fs` for the lookup; no `.fsproj` change, no state file.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. Three leaf ops, one small name lookup (`Zone.tryTriggerByName`, mirroring the
  existing `tryWaypointByName`), and one diagnostic helper. No new state case, no new module, no `EditorState`
  field (`Print` reuses the cue text buffer; the trigger combo needs none).
- **II. Fail Loudly** — PASS. The cue layer no-ops (recorded), never raises on missing or redundant state; the
  lookup is an `option` because a missing trigger is an *expected* failure path (`result`/`option`, not an
  exception), exactly as `tryWaypointByName` is. No editor setter changes.
- **III. Spec-First** — PASS. This feature is `specs/067-print-trigger-cue-ops`.
- **IV. Data-Oriented Architecture** — PASS. The ops are union cases; a toggle mutates the trigger slot in
  place through the existing `Zone.setTrigger`; no object graph is introduced.
- **V. Minimise GC Pressure** — PASS. No `…State` (no per-tick work), so the idle path is untouched; the
  toggle allocates nothing beyond the diagnostic's `sprintf` on the no-op path (cold).

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/067-print-trigger-cue-ops/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── cue-print-trigger-ops.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
world/Cue.fs              # the ops: Print, EnableTrigger, DisableTrigger (three no-progress leaves)
world/Zone.fs             # tryTriggerByName: the first live trigger named `name`, mirroring tryWaypointByName
world/CueEngine.fs        # the arms: Print -> stdout; a toggle -> resolve place/trigger, no-op+log else, set the flag
editor/CueTree.fs         # kinds/groups, kindName, label, executing, children, defaultOfKind,
                          #   withPlace (the toggles), withTrigger (the name), textOf/withText (Print), drawEditor arms
editor/CueEditor.fs       # drawNodeText: the trigger-name combo over the named place's triggers (typed fallback)
test/CueTests.fs          # the effects and no-ops, through CueEngine.tick
test/CuePanelTests.fs     # labels, kinds, writes for the new ops
```

**Structure Decision**: No module moves and no compile-order change. `Zone.fs` precedes `CueEngine.fs`, so the
lookup it adds is reachable where the walk reaches the op; `Place`, `Entity` and `Cue` all precede both. The
ops live in the existing cue type and engine exactly as the zone, actor and motion ops do.

## Complexity Tracking

> No Constitution Check violations.
