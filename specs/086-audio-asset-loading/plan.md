# Implementation Plan: Music and Sound Assets Load from the Asset Manager

**Branch**: `086-audio-asset-loading` | **Date**: 2026-10-14 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/086-audio-asset-loading/spec.md`

## Summary

Add music and sound as a first-class asset **kind** the existing asset manager loads, alongside
actors, props and environments. A manifest in `assets/audio/` declares a source file and a class
(**Sound** or **Music**). At startup, `Assets.scan` discovers every audio manifest, resolves its
file to an absolute path, and then:

- a **Sound** is decoded and held in memory (`MIX_LoadAudio` with `predecode = true`) — resident,
  ready, no further disk access;
- a **Music** track is left as a reference to its compressed file on disk (decoded audio is
  uncompressed and large: ≈35 MB for a three-minute theme), but is **probed** once with a decoder
  (`MIX_CreateAudioDecoder`) so a broken track is caught at startup rather than at playback.

The decoded-buffer/probe plumbing lives in a **new `audio/` cluster** (device state, no GPU). The
asset manager holds the audio assets and a reference to the mixer context, and does the discovery
and residency bookkeeping; the engine owns the mixer's lifetime through `SharedState`. If the audio
subsystem cannot start, no audio is loaded and the game runs with audio unavailable — the same
graceful handling a missing font gets (spec 080).

This is **only the loading half** of music and sound. Playback, mixing, transport, looping and
volume are a later feature and are out of scope (spec FR-011).

Unlike environments (spec 050/053), audio needs **no worker pool, no per-frame upload budget and no
byte-budgeted LRU**: sounds are small and music holds nothing, so both are decided once, eagerly, on
the render thread.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).

**Primary Dependencies**: **no new managed package.** `SDL3.Mixer` already ships in the referenced
`SDL3-CS` 3.4.14.1 (188 `MIX_*` bindings); the native `libSDL3_mixer.so` is already vendored in
`native/` with a `CopySdl3MixerLibrary` build target (added during the spike). Everything else is
BCL.

**Storage**: none persisted; audio source files live under `assets/audio/`, referenced by manifest.

**Testing**: the existing suite is the regression gate. The audio **manifest resolve/classify/
residency/release** logic is pure and asserted headlessly through an **injected mixer context**
(§ `Audio.Context`), so no device is opened; the SDL **decode/probe** calls are device-dependent and
are verified by the walkthrough in [quickstart.md](./quickstart.md) (the spec-053 pattern).

**Target Platform**: Linux x64 desktop, SDL3 + SDL_mixer.

**Project Type**: single-project native desktop game.

**Performance Goals**: startup pays for decoding sounds (short, few — a small, bounded cost) and for
probing music (open + discard a decoder, no decode). No per-frame cost is added: this phase does no
work after the scan.

**Constraints**: sounds resident in memory, music a reference on disk (FR-005/FR-006); audio
unavailable must not stop the game (FR-009); no worker pool, budget or LRU; no playback, transport,
loop or volume (FR-011); the existing suite stays green (FR-012).

**Scale/Scope**: one new cluster (`audio/`, two modules), additions to `world/Assets.fs`
(a dict, a context field, a scan pass, release), one line in `state/SharedState.fs`, test-support
and test updates, and a `docs/PROJECT_STRUCTURE.md` update for the new cluster. No shader, pipeline,
pass, format or GPU change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — two small modules, a two-field context, no new dependency. No pool/budget/LRU: eager decode and a one-shot probe are all this needs. |
| II. Fail Loudly | Pass — a missing/corrupt/undecodable file raises in the loader and is caught at the scan boundary, reported and marked `Failed` (never a silent default); a non-audio bug still raises. |
| III. Spec-First | Pass — `specs/086-audio-asset-loading/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — `AudioAsset`, `Residency` and the manifest are records/DUs; the context is a record of values and closures, no object graph or inheritance. |
| V. Minimise GC Pressure | Pass — loading is off the frame loop; no per-frame allocation is introduced (nothing runs per frame). |
| N-V. Testing is the Authoring Tool | Not engaged — narrative subsystem, untouched. |

## Project Structure

### Documentation (this feature)

```text
specs/086-audio-asset-loading/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── audio-loading.md # Phase 1 output — the surfaces that change
├── checklists/
│   └── requirements.md  # Spec quality checklist (already written)
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
audio/                    # NEW cluster — device/CPU, no GPU. Compiled after render/, before world/.
├── AudioAsset.fs         # NEW: `SoundHandle`, `Class` (Music | Sound), the manifest record
│                         #   (`[<SymbolicExpansion>]`: Id, Name, File, Kind), `Residency`
│                         #   (Resident | Reference | Failed), `AudioAsset`, and its accessors.
└── Audio.fs              # NEW: `Context` (the MIX_Mixer handle + `LoadSound`/`Probe`/`FreeSound`
                          #   closures), `create` (SDL audio + MIX_Init + mixer device, else
                          #   unavailable), `unavailable` (the degraded/test context), `release`.

world/
└── Assets.fs             # CHANGED: an `AudioAssets` dict and an `Audio : Audio.Context` field on
                          #   `AssetManager`; `scan` gains the context and a third `scanDir` pass
                          #   over `assets/audio/`, loading sounds / probing music; `release` frees
                          #   resident sounds then releases the context; small readout predicates.

state/
└── SharedState.fs        # CHANGED: `create` builds `Audio.create ()` and passes it to `Assets.scan`.

test/
├── TestSupport.fs        # CHANGED: `manager` gains `AudioAssets` + `Audio = Audio.unavailable`.
├── AssetsTests.fs        # CHANGED: the five `Assets.scan … 0n` calls gain `Audio.unavailable`.
└── AudioTests.fs         # NEW: the pure audio-load logic against a fake context (classify,
                          #   id/name, path resolution, residency, failure, release bookkeeping).

docs/
└── PROJECT_STRUCTURE.md  # CHANGED: the new `audio/` cluster and its compile position.
fsharp-game.fsproj        # CHANGED: two `<Compile>` entries after `render/`, before `world/`.
```

**Structure Decision**: audio is a **new cluster**, not part of `render/` (it touches no GPU) or
`world/` (it is device state, consumed by `world/`). It is compiled **after `render/` and before
`world/`**, so `world/Assets.fs` may name it while the dependency direction stays downward
(`world → audio → core`). The **model** (`AudioAsset.fs`) is compiled before the **device ops**
(`Audio.fs`), because `Residency` names `SoundHandle` — the same ordering `EnvironmentAsset.fs`
uses to precede its consumers.

## Complexity Tracking

No Constitution Check violations — no complexity tracking required. The one deliberate seam is the
**injected `Audio.Context`** (§ `Audio.Context`): it keeps `Assets` free of SDL, makes the audio
load logic testable headlessly (the spec-053 injectable-decode precedent), and lets the engine hand
the same mixer to playback later. It is a two-field record of closures, not an abstraction layer.

## Phase 0: Research

See [research.md](./research.md). It settles the cluster placement (D1), the manifest and
classification (D2), the injectable context and the degraded path (D3), the load/probe calls (D4),
the residency model (D5), the mixer's lifetime and ownership (D6), the audio-subsystem init that
must *not* be part of Engine's all-or-nothing `SDL.Init` (D7), the format stance (D8), failure
handling (D9), the test strategy (D10) and scope (D11).

## Phase 1: Design

- [data-model.md](./data-model.md) — `Class`, the manifest, `AudioAsset`, `Residency`, `Audio.Context`,
  the `AssetManager` additions, the lifecycle and the validation rules.
- [contracts/audio-loading.md](./contracts/audio-loading.md) — the surfaces that change (`Audio`,
  `AudioAsset`, `Assets`, `SharedState`) and what explicitly does not.
- [quickstart.md](./quickstart.md) — the headless validation and the walkthrough that shows a sound
  loads resident, a track is referenced, a broken file is reported, and the game runs with audio
  unavailable.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — two modules, a context record, one scan pass; no new dependency, no machinery beyond what the residency split needs. |
| II. Fail Loudly | Pass — loader raises, scan catches and marks `Failed`; the degraded path is an *expected* state, reported, not a silent default. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — records/DUs, a record of closures; no object graph. |
| V. Minimise GC Pressure | Pass — nothing added runs per frame. |
| N-V. Testing is the Authoring Tool | Not engaged. |
