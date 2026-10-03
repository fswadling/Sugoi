# Implementation Plan: Trigger Cue

**Branch**: `058-trigger-cue` | **Date**: 2026-10-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/058-trigger-cue/spec.md`

## Summary

Give a trigger an authored cue and make it run. A trigger's cue is a field on its authored state (`world/Entity.fs`),
so it rides the existing `TriggerSet` command and is undone, duplicated and deleted with the trigger — no new command.
The Inspector draws that cue inline using the one shared control (`editor/CueTree.fs`), and when a trigger fires —
on entry for a non-interactable trigger, on the interact key (`E`) for a tracked interactable one — the cue is forked
into the screen's running cue with `forkInto`, exactly as `specs/046` designed.

The only structural moves are compile-order: `world/Cue.fs` moves above `world/Entity.fs` (the trigger's state now
names a cue), `forkInto` moves from `world/CueEngine.fs` to `world/Cue.fs` (the trigger system is compiled before the
engine and must fork), and `editor/CueTree.fs`/`editor/CueEditor.fs` move above `editor/Inspector.fs` (the Inspector
draws the inline editor through them). All three are pure reorderings with no behaviour change.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`)

**Primary Dependencies**: Prime (`FDeque`), SDL3-CS, cimgui (`ImGui.fs`), JoltPhysicsSharp — all existing

**Storage**: In-memory zones only; no persistence (phase 15)

**Testing**: xUnit (`dotnet test`); the trigger fire and the cue control are asserted headlessly, with no GPU

**Target Platform**: Linux desktop (SDL3 + SDL_gpu + ImGui)

**Project Type**: Single desktop game/editor executable (`fsharp-game.fsproj`)

**Performance Goals**: 60 FPS; the trigger system's idle path allocates nothing; a fork happens only on a fire

**Constraints**: The play chain runs every frame, so the read/write path must stay allocation-free off the fire;
`TriggerState` is a `[<Struct>]` and must stay a valid undo payload (no resources)

**Scale/Scope**: One field on a trigger, one system signature change, one seam in the play chain, one inline editor
in the Inspector, and one control refactor — no new commands, no new panels

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. No new command (the cue rides `TriggerSet`), no new panel (the Inspector draws the existing
  control), and `forkInto` is *relocated*, not duplicated. One small system helper (`forkTriggerCue`) serves the two
  fire sites.
- **II. Fail Loudly** — PASS. An empty cue (`Fin`) is the expected "nothing to fire" case, matched rather than
  defaulted; no error is swallowed.
- **III. Spec-First** — PASS. This plan is `specs/058-trigger-cue`.
- **IV. Data-Oriented Architecture** — PASS. The cue is a value field on a struct authored record; no inheritance, no
  object graph. The reorder keeps dependencies one-directional.
- **V. Minimise GC Pressure** — PASS. `updateTriggers` still reads/writes slot payloads byref and allocates nothing on
  the idle path; a fork allocates one `Parallel` node only on a fire, which is expected and bounded.

*Post-design re-check*: still PASS — the design adds fields and relocates functions; it introduces no new allocation on
a per-frame no-op path and no new command family.

## Project Structure

### Documentation (this feature)

```text
specs/058-trigger-cue/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── trigger-cue.md   # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
world/
├── Cue.fs          # MOVED above Entity.fs; gains forkInto (moved from CueEngine.fs)
├── Entity.fs       # TriggerState gains `Cue : Cue.Cue`
├── Zone.fs         # defaultTrigger gains `Cue = Cue.Fin`
├── CueEngine.fs    # loses forkInto (moved to Cue.fs)
└── Systems.fs      # updateTriggers takes the screen cue byref and fires; new forkTriggerCue

state/
├── EditorState.fs  # + trigger inline-editor transient fields (selection, ref, text field)
└── Command.fs      # unchanged (TriggerSet already carries the whole TriggerState)

editor/
├── CueTree.fs      # MOVED above Inspector.fs; unchanged control
├── CueEditor.fs    # MOVED above Inspector.fs; helpers parameterised; + drawTrigger
├── Inspector.fs    # trigger section calls CueEditor.drawTrigger
└── Selection.fs    # unchanged (updateTriggerRef is the commit path)

screen/
└── GameplayScreen.fs  # interact key fires the tracked trigger's cue

fsharp-game.fsproj    # reorder the three files above
test/
├── SystemsTests.fs   # updated updateTriggers arity; entry-fire + interact-fire cases
└── CuePanelTests.fs  # inline trigger editor + forkTriggerCue cases
```

**Structure Decision**: Single project; the feature touches `world`, `state`, `editor` and `screen`, following the
existing domain clusters. The three compile-order moves are recorded in `docs/PROJECT_STRUCTURE.md`.

## Complexity Tracking

No constitution violations. No entries.
