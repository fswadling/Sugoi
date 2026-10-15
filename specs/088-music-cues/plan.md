# Implementation Plan: Music Cues — play and shape music from the story

**Branch**: `088-music-cues` | **Date**: 2026-10-14 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/088-music-cues/spec.md`

## Summary

Add music **cue ops**: a cue can set up the palette (`AddMusic`/`RemoveMusic`), transport it
(`PlayMusic`/`StopMusic`, and whole-bed `PlayBed`/`StopBed`), and shape it —
`SetMusicLevel song level duration`, `SetMusicMaster level duration`, and `CrossFadeMusic from to duration`
— all with the in-flight `…State` shape the screen fades and actor blends already use.

The mix stays spec 087's **authored state**: the editor edits it through `SetMix` steps (undoable), and a
cue mutates the `same` mix **directly at runtime** (never on the undo stack) — the `AddAdvent`/`RemoveAdvent`
pattern. The engine composes the **existing** `audio/Music.fs` ops (`addRow`, `removeRow`, `play`, `stop`,
`setWeight`, `setMaster`, `reconcile`); **no new `Music` API** is needed. This phase also **renames** the
actor cross-fade ops for parity (`CrossFade` → `CrossFadeAnimation`, `CrossMorph` → `CrossFadeMorph`).

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).

**Primary Dependencies**: **none new.** The cue vocabulary (`world/Cue.fs`), the engine
(`world/CueEngine.fs`), the editor (`editor/CueTree.fs`, `editor/CueEditor.fs`); reuses `audio/Music.fs`
(unchanged), `world/Assets.fs` (`musicNames`, `audioAsset`), and `state/SharedState.fs` (`Mix`, `Rack`,
`Assets.Audio`).

**Storage**: none — cues are not persisted yet (that lands with the future `world/Project.fs`), so the
rename needs no data migration.

**Testing**: headless, like every cue op. The op *effects* are asserted against a device-free `SharedState`
(`Audio.unavailable`), and the `…State` transitions with a `now` clock, as `CueTests` already does. The
audible result (hearing a fade) is the [quickstart.md](./quickstart.md) walkthrough.

**Target Platform**: Linux x64 desktop, SDL3 + SDL_mixer.

**Performance Goals**: an idle tick (no op in flight) stays **0 bytes**, as the suite already asserts. A
shaping op in flight writes `shared.Mix` each tick, which allocates one `Mix` (and its rows list) per tick
while it runs — a short, deliberate effect, off the idle path; the alternative (a separate runtime level
layer) was rejected by the spec's model (`## Assumptions`).

**Constraints**: music only (no one-shot sounds, no start/loop points — spec FR-014); every op is a
runtime effect that never lands on the one history (FR-009); a song outside the palette, an unavailable
asset, or an unavailable mixer is a reported no-op (FR-010); the existing suite stays green (FR-016).

**Scale/Scope**: `world/Cue.fs` (+9 cases, +3 states, 2 renamed), `world/CueEngine.fs` (perform + step),
`editor/CueTree.fs` and `editor/CueEditor.fs` (author the new ops), and tests. `audio/Music.fs`,
`state/SharedState.fs` and `world/Assets.fs` are **unchanged**.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — no new module; the ops reuse `Music`'s existing surface, and the shaping ops reuse the fade/blend shape. Two renames remove a naming collision. |
| II. Fail Loudly | Pass — a programming bug still raises (`Music.play`'s missing-row `invalidOp`); state an op cannot act on is a *reported* no-op via the existing `noOp` family. |
| III. Spec-First | Pass — `specs/088-music-cues/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — the ops are cases of the existing `Cue` DU; the in-flight forms are records-as-cases, as `BlendState`/`FadeOutState` are. |
| V. Minimise GC Pressure | Pass, with a recorded trade-off — an idle tick stays 0 bytes; a ramp writes the mix per tick (one small record per tick while it runs), acceptable for a short deliberate effect ("clarity first, then measure"). |
| N-V. Testing is the Authoring Tool | Not engaged — narrative subsystem, untouched. |

## Complexity Tracking

No Constitution Check violations — no complexity tracking required. The one deliberate choice is
**mutating the authored mix from a cue** (so a fade's level *is* the mix weight), which the spec chose over
a parallel runtime level layer.

## Project Structure

### Documentation (this feature)

```text
specs/088-music-cues/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── cue-music.md     # Phase 1 output — the surfaces that change
├── checklists/
│   └── requirements.md  # Spec quality checklist (already written)
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
world/
├── Cue.fs               # CHANGED: the music cases (add/remove/play/stop/playBed/stopBed, setLevel,
│                        #   setMaster, crossFade) + their in-flight `…State` forms; the rename of
│                        #   `CrossFade`/`CrossMorph`.
└── CueEngine.fs         # CHANGED: perform each music op on `shared.Mix`/`shared.Rack` (reusing `Music`),
│                        #   step the shaping states against `now`, and report every no-op.

editor/
├── CueTree.fs           # CHANGED: the kind tables, labels, defaults and per-op field routing for the new
│                        #   ops; the renamed cross-fade labels.
└── CueEditor.fs         # CHANGED: the field editors — a song chooser over `Assets.musicNames`, a level and
                         #   a duration for the shaping ops.

audio/Music.fs           # unchanged — reused as-is.
state/SharedState.fs     # unchanged — `Mix`/`Rack` already hold the state a cue mutates.
world/Assets.fs          # unchanged — `musicNames`/`audioAsset` already resolve a song.

test/
├── CueTests.fs          # CHANGED: the renamed cases; the music ops alongside the existing ones.
└── MusicCueTests.fs     # NEW: the music ops headlessly — effects on `Mix`/`Rack`, the `…State`
                         #   transitions, the no-op rules, and 0 undo steps.
```

**Structure Decision**: the ops live in the cue vocabulary and engine where every cue op already lives; the
engine composes `Music` rather than adding to it (the spec's `Music` surface is enough); the editor gains
the cases in its existing tables. No new module, and no change to `Music`, `SharedState` or `Assets`.

## Phase 0: Research

See [research.md](./research.md). It settles the op shapes (D1), the write path (D2/D3), the no-op rules
(D4), the cross-fade and bed forms (D5), the editor wiring (D6), the rename (D7), the allocation stance
(D8) and the test split (D9).

## Phase 1: Design

- [data-model.md](./data-model.md) — the new `Cue` cases and `…State` forms, the write path, and the
  validation rules (the no-op conditions, the ramp retarget rule).
- [contracts/cue-music.md](./contracts/cue-music.md) — the surfaces that change (`Cue`, `CueEngine`,
  `CueTree`, `CueEditor`) and what explicitly does not (`Music`, `SharedState`, `Assets`).
- [quickstart.md](./quickstart.md) — the headless suite and the audible walkthrough (a cue fades a song in,
  cross-fades to another, and stops the bed).

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — cases in the existing DU; `Music` untouched. |
| II. Fail Loudly | Pass — reported no-ops for expected absence; `invalidOp` only for bugs. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — DU cases and record states, no new object graph. |
| V. Minimise GC Pressure | Pass — idle ticks 0 bytes; ramps allocate a small record per tick, accepted. |
| N-V. Testing is the Authoring Tool | Not engaged. |
