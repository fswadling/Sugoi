# Implementation Plan: Play One-Shot Sounds and Audition Them from a Sound Panel

**Branch**: `089-sound-effects` | **Date**: 2026-10-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/089-sound-effects/spec.md`

## Summary

Add sound **playback**: the ability to play a one-shot sound effect from its already-resident decoded
buffer, and an editor **sound panel** where the director browses the sound assets (spec 086's
`Kind = Sound`) and auditions each one — a **Play**/**Stop** control per row plus a global **Stop
all**.

Playback goes through a **fixed voice pool**: a small, fixed set of playback tracks reused
round-robin, so a sound may overlap an earlier instance up to the bound and the oldest is cut when the
pool is full (FR-003). The pool and its device ops live in a new `audio/Sound.fs` (the counterpart to
`audio/Music.fs`); it reuses the deck ops `audio/Audio.fs` already exposes and adds **no new SDL
binding**. The panel is `editor/SoundPanel.fs`.

The panel is a **pure audition surface**: it authors nothing, records no step on the one undo history,
and persists nothing (FR-006). Only `world/Assets.fs` gains one readout (the sound assets, by name) and
only `Engine.fs` gains one line (free the voices before the mixer).

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).

**Primary Dependencies**: **none new.** `SDL3.Mixer` (in the referenced `SDL3-CS` package) already
exposes everything needed, and `audio/Audio.fs` already wraps it: `createTrack` / `destroyTrack`
(`MIX_CreateTrack` / `MIX_DestroyTrack`), `setAudio` (`MIX_SetTrackAudio`), `play`
(`MIX_PlayTrack`), `stopTrack` (`MIX_StopTrack`). A one-shot is a resident `MIX_Audio` (spec 086)
set on a track and played. No new binding, no new managed package.

**Storage**: none. Nothing is authored or persisted; the voice pool is runtime-only
(`SharedState.Voices`, like `SharedState.Rack`).

**Testing**: the **pure** pool logic — round-robin voice selection (the oldest is reused), the
per-asset Stop, Stop all, and the readout that lists only sound-class assets sorted by name — is
asserted headlessly against `Audio.unavailable` (a `0n` mixer, so every device op is a no-op and no
device is opened). The audible result is verified by the walkthrough in
[quickstart.md](./quickstart.md) (the spec-087 split).

**Target Platform**: Linux x64 desktop, SDL3 + SDL_mixer.

**Project Type**: single-project native desktop game.

**Performance Goals**: a one-shot allocates nothing per play and does no per-frame work — it picks the
next voice (an integer index), sets its audio and plays it. The pool is fixed at 16 tracks created
once; nothing runs on the game loop.

**Constraints**: one-shot only (no loop, no cue ops, no start/end points — spec FR-010); the panel is
runtime-only and records no history (FR-006); every voice is freed before the mixer (FR-009); a failed
asset or an unavailable mixer is a no-op (FR-007/FR-008).

**Scale/Scope**: one new `audio/Sound.fs`, one new `editor/SoundPanel.fs`, one new `test/SoundTests.fs`,
one field on `state/SharedState.fs`, one `Assets` readout, one line in `Engine.fs`, one line each in
`editor/Editor.fs` and `native/cimgui_sdl3.cpp` (dock it), and the `SharedState` literals in the test
fixtures gain the new field. No shader, pipeline, or GPU change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one small model module, one panel, a fixed voice pool; reuse `Audio`'s existing track ops; no group/bus machinery, no per-sound metadata. |
| II. Fail Loudly | Pass — a bad `MIX_` handle is guarded inside `Audio`'s ops (a `0n` track is a no-op); an unavailable mixer or a failed asset is an *expected* no-op, reported, so the panel stays usable (FR-007/FR-008). |
| III. Spec-First | Pass — `specs/089-sound-effects/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — `Voices` is a record over two arrays and an index; no object graph, no per-play allocation. |
| V. Minimise GC Pressure | Pass — nothing runs per frame; a play is an index bump, two `MIX_` calls and one array write. |
| N-V. Testing is the Authoring Tool | Not engaged — narrative subsystem, untouched. |

## Complexity Tracking

No Constitution Check violations — no complexity tracking required. The one deliberate structure is
the **fixed voice pool** (a small array of reusable tracks reused round-robin), which is the simplest
model that satisfies "overlap up to a bound, oldest cut" (FR-003) without a per-play allocation, a
stopped-callback, or per-frame bookkeeping.

## Project Structure

### Documentation (this feature)

```text
specs/089-sound-effects/
├── plan.md                 # This file
├── research.md             # Phase 0 output
├── data-model.md           # Phase 1 output
├── quickstart.md           # Phase 1 output
├── contracts/
│   └── sound-panel.md       # Phase 1 output — the surfaces that change
├── checklists/
│   └── requirements.md      # Spec quality checklist (already written)
└── tasks.md                 # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
audio/
├── Audio.fs             # unchanged — its track ops (create/destroy/setAudio/play/stopTrack) are reused.
├── AudioAsset.fs        # unchanged — `SoundHandle`, `Class`, `isResident`, `sound` are reused.
├── Music.fs             # unchanged.
└── Sound.fs             # NEW: the fixed voice pool (`Voices`, the capacity) and its device-free-composable
                         #   ops — `newVoices`, `play` (round-robin, oldest reused), `stop` (per asset),
                         #   `stopAll`, `release`. Names `Audio`/`AudioAsset` only (never `Assets`/`SharedState`).

editor/
├── SoundPanel.fs        # NEW: the panel — one row per sound (Play/Stop, name, state) and one Stop all.
└── Editor.fs            # CHANGED: submit `SoundPanel.draw`; add "Sounds" to the docked-panel focus list.

state/
└── SharedState.fs       # CHANGED: `Voices : Sound.Voices` (runtime), game-wide like `Rack`, freed before
                         #   the mixer.

world/
└── Assets.fs            # CHANGED: `soundAssets : AssetManager -> AudioAsset.AudioAsset list` — the
                         #   sound-class assets, sorted by name (the panel's rows).

Engine.fs                # CHANGED: `Sound.release manager.Shared.Assets.Audio manager.Shared.Voices` before
                         #   `Assets.release` (voices freed before the mixer, spec 086's order).

native/
└── cimgui_sdl3.cpp      # CHANGED: `DockBuilderDockWindow("Sounds", bottom)` in `cimgui_setup_dock_layout`
                         #   (the layout is built in C++); rebuilt with `native/build.sh`.

test/
├── SoundTests.fs        # NEW: the pool logic (round-robin, per-asset stop, stop all, release) and the
│                        #   `soundAssets` readout — device-free.
└── (SharedState literals) # CHANGED: each fixture that builds a `SharedState` gains `Voices = Sound.newVoices Audio.unavailable`.
```

**Structure Decision**: the pool **model and its ops** live together in `audio/Sound.fs` (device-free
to construct, so the suite composes one against `Audio.unavailable`), compiled after `audio/Music.fs`
(it names `Audio`'s track handles and `AudioAsset`'s `SoundHandle`) and before `state/` (which holds
it). It names neither `Assets` nor `SharedState`, so the panel resolves each sound's handle — the
`Music`/`SongPanel` split. The panel is an `editor/` module beside `SongPanel`. The pool lives on
`SharedState` beside `Rack` for the same reason (a game-wide runtime audio resource, freed before the
mixer).

## Phase 0: Research

See [research.md](./research.md). It settles the module home (D1), the voice-pool model (D2), the
round-robin "oldest cut" rule (D3), reusing `Audio`'s ops with no new binding (D4), the state home
(D5), per-asset stop semantics (D6), the panel's readout (D7), the `Sound`/`AudioAsset.Sound` naming
(D8) and the test strategy (D9).

## Phase 1: Design

- [data-model.md](./data-model.md) — `Voices`, the capacity, the asset readout, the `SharedState`
  field, and the validation rules.
- [contracts/sound-panel.md](./contracts/sound-panel.md) — the surfaces that change (`Sound`,
  `SoundPanel`, `Assets`, `SharedState`, `Engine`, `Editor`, the native dock) and what explicitly does
  not.
- [quickstart.md](./quickstart.md) — the headless pool tests and the audible walkthrough (play, overlap,
  per-sound stop, stop all).

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — a model module and a panel; the pool reuses `Audio`'s track ops; no metadata, no groups. |
| II. Fail Loudly | Pass — unavailability is a reported no-op; `Audio`'s ops already guard a `0n` handle. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — `Voices` is arrays and an index; no object graph. |
| V. Minimise GC Pressure | Pass — a play allocates nothing and nothing runs per frame. |
| N-V. Testing is the Authoring Tool | Not engaged. |
