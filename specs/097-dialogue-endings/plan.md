# Implementation Plan: Dialogue Endings

**Branch**: `097-dialogue-endings` | **Date**: 2026-10-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/097-dialogue-endings/spec.md`

## Summary

Give a shown dialogue line a set of **runtime** ending conditions — `ByPlayer`, `ByTimeout`, or neither — with the timeout's duration held as a `voption` on the line rather than inside the condition. Closing a line from a cue is a universal capability, not a member of the set: a close or replacement always works, and the empty set means the cue owns the line's end. Give the cue two display shapes: a non-waiting op that writes the line and continues, and a waiting op that holds the cue until the screen line is gone. Add an explicit close op. The runtime rule lives in one small system that reads the set, the timeout, the player's dismiss edge and play time; the timer uses the same `PlayTime` as the cue. The Cue panel, the trigger's inline cue editor and the Dialogue panel share the same ending controls, and a running waiting op is read-only in the cue tree.

The implementation keeps the existing two-argument `Exposit` as the empty-set, non-waiting form so persisted projects keep decoding; a general non-waiting form (`ExpositWith`) carries the set and timeout, and the editor presents both as one "Exposit" kind. No project-file version bump or symbolic migration is needed.

## Technical Context

**Language/Version**: F# 10 on .NET 10

**Primary Dependencies**: Prime 11.5.2 (symbolic serialization, `FDeque`), SDL3-CS 3.4.14.1, the project's cimgui/SDL3 ImGui bindings

**Storage**: `project.manifest` at the launch working directory for the scenario library and its cues (Prime s-syntax). The dialogue state itself is runtime-only and is never persisted.

**Testing**: xUnit (`dotnet test`), headless — no GPU, window or device for the cue/state/panel logic

**Target Platform**: Linux desktop (SDL3); the editor and game share one window

**Project Type**: Single F# desktop game/editor project

**Performance Goals**: 60 FPS; 0 bytes/frame allocated by the dialogue tick and by an idle `ExpositWaitState` cue

**Constraints**: F# compile order (`world/Dialogue.fs` → `world/Cue.fs` → `state/CueEngine.fs`); the existing two-argument `Exposit` must keep decoding; no per-frame allocation; the cue's `PlayTime` is the one clock the timer shares; the cue is immutable, so waiting is a run state, not a flag on the dialogue; a newly enabled timeout starts at 3.0 seconds; a clear produced by a parallel branch advanced after a waiting op is observed on the next play step, still within one play frame

**Scale/Scope**: One screen line at a time; two data-free runtime ending kinds in a `Set`, one `voption` timeout on the line; three new cue cases plus one run state; editor controls on three existing surfaces

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — the feature adds one data-free ending union, one set and one `voption` field, a small helper set, three cue cases plus a run state, and one system. It adds no module, no panel, and no generic timer machinery. |
| II. Fail Loudly | Pass — the existing `withText`/`withExpositKind` write paths still raise for a kind that cannot carry the field; the new `withEndings` does the same. An inconsistent `ByTimeout` with no duration is prevented by the editor and treated as no timeout by the runtime; a waiting line with no runtime condition is a deliberate authoring choice. |
| III. Spec-First | Pass — `specs/097-dialogue-endings/spec.md` is written, clarified (five questions), and drives this plan. |
| IV. Data-Oriented Architecture | Pass — the ending is a data-free union in a `Set`; the timeout is a struct `voption` on the line; the cue stays an immutable value; the dialogue stays a `[<Struct>]` with explicit mutation; waiting is carried by the cue's existing run-state shape. |
| V. Minimise GC Pressure | Pass — the ending set and timeout are authored data, allocated when a line is written, never per frame; the per-frame check is `Set.contains` over at most two elements plus a `voption` match; `ExpositWaitState` is allocated once when the op begins. |
| Narrative principles | Not engaged — the narrative engine and its event/fact vocabulary are untouched. |

Re-check after Phase 1: the design still passes all five gates; no violation needs tracking.

## Project Structure

### Documentation (this feature)

```text
specs/097-dialogue-endings/
├── plan.md                         # this file
├── research.md                     # Phase 0 decisions
├── data-model.md                   # Phase 1 types, rules and transitions
├── quickstart.md                   # Phase 1 validation guide
├── contracts/
│   └── dialogue-endings.md         # cue, state, system and panel contracts
├── checklists/
│   └── requirements.md             # spec quality checklist (already passing)
└── tasks.md                        # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
world/
├── Dialogue.fs                     # CHANGED: ByPlayer/ByTimeout, Endings : Set, Timeout : float voption,
│                                   #   ShownAt, the helpers, and the default hand-shown line
├── Cue.fs                          # CHANGED: ExpositWith, ExpositWait, ExpositWaitState, CloseExposition;
│                                   #   Exposit keeps its two-argument empty-set form
├── Systems.fs                      # CHANGED: updateDialogue replaces dismissDialogue —
│                                   #   player edge + timeout against PlayTime; cue closing is universal
└── Project.fs                      # UNCHANGED: the cue is carried as-is; the new cases serialize

state/
└── CueEngine.fs                    # CHANGED: exposit writes the set, timeout and ShownAt;
                                    #   advance performs the new display/wait/close cases

screen/
└── GameplayScreen.fs               # CHANGED: one play-time value feeds updateDialogue and the cue tick

editor/
├── CueTree.fs                      # CHANGED: kinds, labels, executing/children/text functions,
│                                   #   withEndings, and the shared endingControls
├── CueEditor.fs                    # VERIFY: the generic text field handles the widened
│                                   #   textOf/withText; likely no code change
└── DialoguePanel.fs                # CHANGED: player/timeout controls (timeout default 3.0),
                                    #   ShownAt on show, no undo/persistence

test/
├── DialogueTests.fs                # CHANGED: ending helpers + updateDialogue rules
├── CueTests.fs                     # CHANGED: new cases, waiting behaviour, allocation
├── CuePanelTests.fs                # CHANGED: labels/kinds/defaults/withEndings
└── ProjectTests.fs                 # CHANGED: cue round-trip with the new cases and old Exposit

project.manifest                    # CHANGED: the intro cutscene gains the explicit CloseExposition
```

**Structure Decision**: Single-project layout, matching `docs/PROJECT_STRUCTURE.md`. The change stays inside the existing `world/`, `state/`, `screen/` and `editor/` clusters, and no `.fsproj` compile-order change is expected: `Dialogue.fs` remains before `Cue.fs`, `Systems.fs` remains after both, and `CueEngine.fs` remains after `GameplayState.fs`.

## Complexity Tracking

No constitution violations. No new project, module, dependency or storage format; the design deliberately avoids a custom-equality set and a per-line token (see `research.md`).
