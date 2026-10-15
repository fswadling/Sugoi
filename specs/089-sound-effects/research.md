# Phase 0 — Research: Play One-Shot Sounds and Audition Them from a Sound Panel

Decisions settled before design. Each is a choice with its rationale and the alternatives weighed.

## D1 — Where the sound playback lives: a new `audio/Sound.fs`

**Decision**: add `audio/Sound.fs` (module `Sound`), the counterpart to `audio/Music.fs`. It holds the
voice pool and its ops. It names `Audio` (the track ops) and `AudioAsset` (`SoundHandle`) only — never
`Assets` or `SharedState` — so the panel resolves each sound's handle and passes it in, exactly as
`SongPanel` resolves a music asset's path and hands it to `Music.play`.

**Rationale**: `Music.fs` set the pattern — the model and its device ops together, device-free to
construct, the callers resolving assets. Sounds need the same seam so the suite can compose a pool
against `Audio.unavailable`.

**Alternatives considered**: putting the ops in `audio/Audio.fs` (rejected — `Audio` is the thin device
seam over `SDL3.Mixer`; the pool is a model over that seam, like `Music` is); a `world/Sound.fs`
(rejected — no zone/asset knowledge is needed, and `world/` is compiled after `audio/`).

## D2 — The voice pool: a small, fixed array of reusable tracks

**Decision**: a `Voices` record holds a fixed array of playback tracks (the **capacity**, 16),
a parallel array of the asset id each track is playing (`""` when idle or finished), and a
round-robin index. The tracks are created once, when the pool is made (`Mixer.CreateTrack` per track).

**Rationale**: the spec requires overlap up to a bound with the oldest cut (FR-003, clarified). A fixed
array reused round-robin satisfies that with no per-play allocation, no stopped-callback from the audio
thread, and no per-frame polling — the simplest model that meets the requirement (Minimalism I,
Minimise GC Pressure V).

**Alternatives considered**: one track per play, destroyed when it stops (rejected — needs a
stopped-callback or per-frame polling to free, and allocates/synchronises on the hot path); a
`Dictionary` of tracks per asset (rejected — unbounded and unnecessary; a one-shot is
indistinguishable per instance); an unbounded list of tracks (rejected — the spec asks for a bound).

## D3 — "Oldest is stopped": round-robin reuse *is* the rule

**Decision**: `play` takes the next voice in round-robin order and moves the index on. Because the
index only advances, the voice it takes is the least-recently-used — the one started longest ago —
which is exactly "the oldest is stopped to make room" (FR-003).

**Rationale**: the least-recently-used voice is the oldest-started voice; no timestamps, no scan for
the oldest, no per-voice state beyond which asset it plays. Setting a new sound on a still-playing
track and playing it restarts that track, so a reuse both silences the old instance and starts the new
one.

**Alternatives considered**: scanning for the oldest by timestamp (rejected — more state, same result);
scanning for a free/stopped voice first (rejected — needs `MIX_TrackPlaying` per play and is not what
"the oldest is cut" says; round-robin already reuses a finished voice when one is next).

## D4 — No new SDL binding: reuse `Audio`'s track ops

**Decision**: the pool composes `Audio.createTrack`, `Audio.setAudio`, `Audio.play`, `Audio.stopTrack`
and `Audio.destroyTrack` — all already in `audio/Audio.fs` (added for the music decks, spec 087).
`audio/Audio.fs` is unchanged.

**Rationale**: a one-shot is a track with a resident buffer set and played — the same three calls a
music deck uses. Nothing new is needed from `SDL3.Mixer`; verifying that avoids a speculative binding.

**Alternatives considered**: `MIX_SetTrackStoppedCallback` (rejected — D2 needs no callback);
`MIX_PlayTag`/groups (rejected — deferred, FR-010); `MIX_SetTrackGain` per play (rejected — no volume
this phase; sounds audition at full level).

## D5 — The state home: `SharedState.Voices`, beside `Rack`

**Decision**: `SharedState` gains `Voices : Sound.Voices`. It is created in `SharedState.create` from
the audio context and freed in `Engine.fs` before `Assets.release`.

**Rationale**: `Rack` is on `SharedState` because a game-wide runtime audio resource outlives a screen
and must be freed before the mixer; the voice pool has the same shape. The one undo history writes
state owned by `SharedState` (specs/048, 049); a runtime pool that nothing authors belongs with `Rack`.

**Alternatives considered**: `GameplayState` (rejected — sounds are the game's, not one screen's, and
`Rack` already set the game-wide precedent); `EditorState` (rejected — it is transient UI state, and
the pool is a device resource that must be freed on shutdown, not copied per input event); a
module-level singleton (rejected — the project forbids module-level editor/state singletons).

## D6 — Stop semantics: per asset id, and stop all

**Decision**: `stop voices assetId` stops every voice whose recorded asset id matches and clears it;
`stopAll voices` stops every voice and clears the record. The record is the asset id, not the track, so
a row's Stop targets exactly that sound (FR-004) and Stop all targets everything (FR-005).

**Rationale**: the panel is per-sound (spec 087's rows), so the natural Stop is "silence this sound"
(clarified Q2). Recording the asset id per voice is the whole of what that needs.

**Alternatives considered**: tracking only the track (rejected — cannot target a row's sound);
tracking a monotonically increasing play id and stopping by it (rejected — the panel stops by asset,
not by instance).

## D7 — The panel's readout: `Assets.soundAssets`, by name

**Decision**: `world/Assets.fs` gains `soundAssets : AssetManager -> AudioAsset.AudioAsset list`, the
assets whose `Class = Sound`, sorted by their `Name`. The panel shows each asset's `Name` and acts on
its `Id`.

**Rationale**: the panel needs three things per row — the id (to play), the name (to show, FR-004) and
the residency (to mark resident/failed, FR-005) — so returning the assets is more useful than the
ids-only `musicNames` (spec 087). The asset carries its name (spec 086 FR-002), so the row reads
human-friendly.

**Alternatives considered**: `soundNames` returning ids, mirroring `musicNames` (rejected — the spec
asks for the name, and the row needs the residency too, so the panel would re-look-up both).

## D8 — Naming: module `Sound` beside the `AudioAsset.Sound` case

**Decision**: the module is `Sound` (`audio/Sound.fs`), mirroring `Music` (`audio/Music.fs`) beside the
`AudioAsset.Music` case. The panel references `AudioAsset.*` qualified and does not `open AudioAsset`,
so `Sound.play` is unambiguously the module.

**Rationale**: `module Music` already coexists with the `AudioAsset.Music` union case in this codebase
(`world/Assets.fs` uses `AudioAsset.Music` while `Music.fs` is a module), so the pattern is proven; a
qualified reference removes even the reader's ambiguity.

**Alternatives considered**: `SoundFx` (rejected — `Sound` mirrors `Music` and the classification name);
naming the pool `Voices` as the module (rejected — the module is the sound feature, the pool is its
state).

## D9 — Testing: the pool against `Audio.unavailable`, the walkthrough for sound

**Decision**: `test/SoundTests.fs` composes a `Voices` against `Audio.unavailable` (a `0n` mixer, so
`createTrack` yields `0n` tracks and every op is a guarded no-op) and asserts the pool's observable
state — the recorded asset id per voice and the round-robin index. The `soundAssets` readout is tested
against a temp root and a recording fake `Audio.Context`, as `test/AudioTests.fs` does. The audible
result is the walkthrough in [quickstart.md](./quickstart.md).

**Rationale**: the pool's decisions (which voice, which asset it now plays, what Stop clears) are pure
data moves, so they are assertable with no device — the spec-086/087 split.

**Alternatives considered**: asserting on `MIX_` state (rejected — needs a device); an audible test
(rejected — the suite is headless).
