# Implementation Plan: Play and Mix Music from a Song Panel

**Branch**: `087-song-mixing` | **Date**: 2026-10-14 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/087-song-mixing/spec.md`

## Summary

Add music **playback**: an editor **song panel** where the director adds music assets (spec 086's
`Kind = Music`) to a global **mix**, each playing as its own streaming **deck**, with per-row transport
(play/pause/stop), a scrub bar, a **weight**, and a **loop** toggle, over one **master** volume.

The authored mix (the row set, each weight and loop flag, and the master) is a **`SetMix` step on the
one undo history** — a state-level step exactly like `Advent`/`SetScenario` (spec 055/070) — so it is
undoable and, once `world/Project.fs` lands, persisted. The transport (playing, paused, position) is
**runtime only**, held with the deck, never authored.

The playback goes through `audio/Audio.fs` (which already owns the mixer from spec 086); the mix model
and the deck rack live in a new `audio/Music.fs`; the panel is `editor/SongPanel.fs`. **No new managed
package and no new ImGui bindings** are needed — every control is a slider, checkbox or button already
bound, and every SDL_mixer call is already exposed.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).

**Primary Dependencies**: **none new.** `SDL3.Mixer` (in the referenced `SDL3-CS` package) already
exposes everything needed: `LoadAudio(mixer, path, predecode = false)` (streaming), `CreateTrack`,
`SetTrackAudio`, `PlayTrack` / `PauseTrack` / `ResumeTrack` / `StopTrack`, `SetTrackGain`,
`SetTrackLoops`, `SetTrackPlaybackPosition` / `GetTrackPlaybackPosition`, `GetAudioDuration`,
`TrackPlaying` / `TrackPaused`, `TrackMSToFrames` / `TrackFramesToMS`, `DestroyTrack` / `DestroyAudio`.

**Storage**: none persisted yet; the mix is in-memory authored state (see Assumptions in the spec).

**Testing**: the **pure** mix logic — gain composition (`master × weight`), add/remove a row, the
play/pause/stop rule, loop toggling — is asserted headlessly. The SDL deck calls need a device, so the
audible result is verified by the walkthrough in [quickstart.md](./quickstart.md) (the spec-086 split).

**Target Platform**: Linux x64 desktop, SDL3 + SDL_mixer.

**Project Type**: single-project native desktop game.

**Performance Goals**: several music decks stream from disk, so N songs hold no sample data in memory
(one streaming handle per row). No per-frame work is added: SDL mixes on its own thread and the panel
reads live state only when drawn.

**Constraints**: music only (no one-shot sounds, no cue ops, no start/loop points — spec FR-015); the
authored mix is one undoable step and the transport is never authored (FR-010/FR-011); every deck is
freed before the mixer (FR-014); a missing asset or unavailable mixer is a no-op (FR-013).

**Scale/Scope**: one new `audio/Music.fs`, one new `editor/SongPanel.fs`, additions to `audio/Audio.fs`,
two fields on `state/SharedState.fs`, one `Command` case + its `History` runner, one `Assets` helper,
and one line in `Engine.fs` (release the decks before the mixer). No shader, pipeline, or GPU change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one small model module, one panel, deck ops added to the existing `Audio`; no dependency, no pool, no group/bus machinery (gain is `master × weight`). |
| II. Fail Loudly | Pass — deck ops raise on a bad handle; a missing asset or unavailable mixer is an *expected* no-op, reported, so the panel stays usable (FR-013). |
| III. Spec-First | Pass — `specs/087-song-mixing/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — `Mix`/`MixRow`/`Deck` are records, the mix an immutable value set on the history; no object graph. |
| V. Minimise GC Pressure | Pass — nothing runs per frame; the panel reads live deck state only while drawn. |
| N-V. Testing is the Authoring Tool | Not engaged — narrative subsystem, untouched. |

## Complexity Tracking

No Constitution Check violations — no complexity tracking required. The one deliberate choice is the
**authored-mix-on-the-history** model (`SetMix`), which reuses the existing state-level-step machinery
rather than adding a parallel "project settings" store.

## Project Structure

### Documentation (this feature)

```text
specs/087-song-mixing/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── song-panel.md    # Phase 1 output — the surfaces that change
├── checklists/
│   └── requirements.md  # Spec quality checklist (already written)
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
audio/
├── Audio.fs             # CHANGED: the deck device ops over the mixer — open a streaming deck from a
│                        #   path, play/pause/resume/stop, set gain, set loops, seek, position,
│                        #   duration, playing/paused, close a deck.
├── AudioAsset.fs        # unchanged.
└── Music.fs             # NEW: the authored `Mix` / `MixRow` model and its pure edits (add/remove a
                         #   row, set weight/loop/master, `gainOf`), the runtime `Rack` / `Deck`, and
                         #   `reconcile` / `release` over the decks.

editor/
├── SongPanel.fs         # NEW: the panel — a row per song (transport, scrub, weight, loop), the bed
│                        #   transport, the master slider, add/remove.
└── Editor.fs            # CHANGED: submit `SongPanel.draw`; the panel docks at the bottom (native layout).

state/
├── SharedState.fs       # CHANGED: `mutable Mix : Music.Mix` (authored) and `mutable Rack : Music.Rack`
│                        #   (runtime), game-wide so music survives a screen change.
├── Command.fs           # CHANGED: a `SetMix of before: Music.Mix * after: Music.Mix` state-level case
│                        #   (`placeOf` → None; the zone-level runner refuses it).
└── History.fs           # CHANGED: apply/undo `SetMix` writes `shared.Mix` then `Music.reconcile shared`.

world/
└── Assets.fs            # CHANGED: `musicNames : AssetManager -> string list` (the panel's picker).

Engine.fs                # CHANGED: `Music.release manager.Shared` before `Assets.release` (decks
                         #   freed before the mixer, spec 086's order).

native/
└── cimgui_sdl3.cpp      # CHANGED: `DockBuilderDockWindow("Songs", bottom)` in `cimgui_setup_dock_layout`
                         #   (the layout is built in C++); rebuilt with `native/build.sh`.

test/
├── TestSupport.fs       # CHANGED: `SharedState` fixtures gain `Mix`/`Rack`.
└── MusicTests.fs        # NEW: the pure mix/gain/transport logic, device-free.
```

**Structure Decision**: the mix **model** (`audio/Music.fs`) is device-free and compiled after
`Audio.fs` (it names `Audio`'s deck handles) and before `state/` (which holds it); the **device ops**
stay in `Audio.fs` beside the mixer they act on; the **panel** is an `editor/` module beside
`MiscPanel`/`ScenarioPanel`. The `Command` **type** stays in `state/Command.fs`; only its runner moves
to `state/History.fs`, which is the established split for a state-level step.

## Phase 0: Research

See [research.md](./research.md). It settles the deck model (D2), the gain model (D3), the loop/finish
behaviour (D4), the transport rule (D5), the scrub mapping (D6), the state home (D7), the authored
`SetMix` step and its rack reconcile (D8/D9), deck lifetime (D10), the no-new-bindings stance (D11) and
the test strategy (D12).

## Phase 1: Design

- [data-model.md](./data-model.md) — `MixRow`, `Mix`, `Deck`, `Rack`, the `SharedState` fields, the
  `SetMix` command, and the validation rules.
- [contracts/song-panel.md](./contracts/song-panel.md) — the surfaces that change (`Audio`, `Music`,
  `SongPanel`, `Assets`, `SharedState`, `Command`, `History`, `Engine`) and what explicitly does not.
- [quickstart.md](./quickstart.md) — the headless mix tests and the audible walkthrough (play several
  songs, weight and master, scrub, loop).

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — a model module and a panel; deck ops reuse `Audio`'s mixer; no groups, no pool. |
| II. Fail Loudly | Pass — expected unavailability is a reported no-op; bad handles still raise. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — records; the mix is an immutable set on the history. |
| V. Minimise GC Pressure | Pass — nothing added runs per frame. |
| N-V. Testing is the Authoring Tool | Not engaged. |
