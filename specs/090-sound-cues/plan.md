# Implementation Plan: Sound Cues — play a sound from the story

**Branch**: `090-sound-cues` | **Date**: 2026-10-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/090-sound-cues/spec.md`

## Summary

Add one cue op — `PlaySound` — so a cue plays a named sound effect at the point the run reaches it.
It is a **no-progress leaf** (a one-shot is not held, so it begins and finishes in the same step), it
resolves the named sound's resident buffer and drives spec 089's voice pool (`Sound.play`), and it is a
**runtime effect that is never a step on the one history** (the music-op rule, spec 088).

There is no palette, level, loop, cross-fade or stop — a sound is a one-shot, so `PlaySound` is the
whole family. The change is entirely additive: one DU case, its engine handling beside the music ops,
and the editor's authoring of it (a sound chooser), plus the exhaustive-match updates the closed
vocabulary demands (Constitution N-III). **No new module, no new dependency, no `SharedState` change.**

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).

**Primary Dependencies**: **none new.** The op composes existing modules: `audio/Sound.fs` (`play`),
`AudioAsset` (`isResident`, `sound`), and `Assets.AudioAssets`. The editor reuses the cue editor's
existing pick-only combo (`CueTree.filteredCombo`).

**Storage**: none. Nothing is authored or persisted by this phase; the op is a runtime effect.

**Testing**: the engine handling is verified **headlessly** — build a `SharedState` whose assets carry
one resident sound, `tick` a cue holding a `PlaySound` op, and assert the pool recorded the id
(`Sound.play` records each voice's asset id, a pure data move that needs no device) and the cue reached
`Fin`. The label/`kindName`/`defaultOfKind`/`withSong`/`placeOf` behaviour is asserted the way
`test/MusicCueTests.fs` asserts the music ops. The audible result is the walkthrough in
[quickstart.md](./quickstart.md).

**Target Platform**: Linux x64 desktop, SDL3 + SDL_mixer.

**Project Type**: single-project native desktop game.

**Performance Goals**: nothing runs per frame beyond the existing cue tick; the op is a single
`Sound.play` (an index bump and two SDL calls) at the step it is reached.

**Constraints**: one-shot only (no palette/level/loop/points/cross-fade/ducking/spatialisation/stop —
spec FR-008/FR-009); a runtime effect, never on the history (FR-004); absent/failed asset or unavailable
audio is a reported no-op (FR-005).

**Scale/Scope**: one DU case in `world/Cue.fs`, its handling in `world/CueEngine.fs`, the
exhaustive-match updates and the chooser in `editor/CueTree.fs` / `editor/CueEditor.fs`, and tests in
`test/CueTests.fs` / `test/CuePanelTests.fs`. No `.fsproj`, shader, pipeline or native change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one op, one engine helper, one chooser; reuses `Sound.play` and the cue editor's combo; no palette, no state case, no new module. |
| II. Fail Loudly | Pass — an unknown/failed sound or unavailable audio is an *expected* no-op, reported (the `noOpMusic` pattern); nothing raises. |
| III. Spec-First | Pass — `specs/090-sound-cues/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — a DU leaf carrying a string; no new state, no object graph. |
| V. Minimise GC Pressure | Pass — nothing added runs per frame beyond a step that plays once. |
| N-III. Closed Vocabulary | Pass — the case is added in the one shared place (`world/Cue.fs`) and every exhaustive match is updated; the editor derives its row from the vocabulary. |
| N-V. Testing is the Authoring Tool | Not engaged — narrative subsystem, untouched. |

## Complexity Tracking

No Constitution Check violations — no complexity tracking required. The one deliberate choice is the
**chooser reuse**: the cue editor's music-song combo is generalised to take its candidate list, so a
sound op reuses it rather than adding a near-duplicate control.

## Project Structure

### Documentation (this feature)

```text
specs/090-sound-cues/
├── plan.md                 # This file
├── research.md             # Phase 0 output
├── data-model.md           # Phase 1 output
├── quickstart.md           # Phase 1 output
├── contracts/
│   └── sound-cue-op.md     # Phase 1 output — the surfaces that change
├── checklists/
│   └── requirements.md     # Spec quality checklist (already written)
└── tasks.md                # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
world/
├── Cue.fs               # CHANGED: `| PlaySound of sound: string` — a no-progress leaf, beside the music
│                        #   cues; the module doc's no-progress list gains it.
└── CueEngine.fs         # CHANGED: `trySoundAsset` (resident sound, else None) + `playSound` (report a
                         #   no-op when absent/failed; else `Sound.play shared.Voices sound (AudioAsset.sound asset)`),
                         #   and the `| PlaySound sound ->` case in `advance` (ValueSome Fin).

editor/
├── CueTree.fs           # CHANGED: `PlaySound` added to `executing` (false), `label` ("Play sound"),
│                        #   `children` ([]), `kindName`, `defaultOfKind` ("Play sound" -> PlaySound ""),
│                        #   `withSong` (the one-name setter), and `drawEditor`'s no-numbers list.
│                        #   The label/kindName strings are the chooser's keys.
├── CueEditor.fs         # CHANGED: generalise `drawSongCombo` to `drawPickCombo` (takes its candidate
│                        #   list + label); a `| PlaySound sound ->` case in `drawNodeText` draws it over
│                        #   the discovered sound ids (`Assets.soundAssets` -> ids).

test/
├── CueTests.fs          # CHANGED: `describe` gains `PlaySound`; new headless tests (play records the id,
│                        #   the cue reaches Fin, 0 history steps, an unknown/failed sound is a no-op).
└── CuePanelTests.fs     # CHANGED: `describe` gains `PlaySound`.
```

**Structure Decision**: the op lives in the **closed vocabulary** (`world/Cue.fs`) as a no-progress
leaf, exactly where the music cues are; its effect is performed in `world/CueEngine.fs` beside the
music ops (the engine is compiled after the state so the op can write the pool where the walk reaches
it, research D3). The editor's exhaustive matches and its authoring live in the cue-panel cluster
(`CueTree`/`CueEditor`), which already derives every op's row, label and chooser from the vocabulary.
Nothing else moves.

## Phase 0: Research

See [research.md](./research.md). It settles the op's shape (D1), the engine resolution and the
`Sound.play` reuse (D2), the report style (D3), the chooser reuse (D4), the closed-vocabulary edits
(D5), and the test strategy (D6).

## Phase 1: Design

- [data-model.md](./data-model.md) — the `PlaySound` case, why it carries no `…State`, and the
  validation rules.
- [contracts/sound-cue-op.md](./contracts/sound-cue-op.md) — the surfaces that change (`Cue`,
  `CueEngine`, `CueTree`, `CueEditor`, the tests) and what explicitly does not.
- [quickstart.md](./quickstart.md) — the headless tests and the audible walkthrough (author a cue, hear
  the sound at its op).

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one op, one helper, one reused chooser; no palette, no state. |
| II. Fail Loudly | Pass — expected unavailability is a reported no-op. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — a DU leaf. |
| V. Minimise GC Pressure | Pass — nothing per frame. |
| N-III. Closed Vocabulary | Pass — one shared place, every exhaustive match updated. |
| N-V. Testing is the Authoring Tool | Not engaged. |
