# Phase 1 — Data Model: Play One-Shot Sounds and Audition Them from a Sound Panel

Nothing here is authored or persisted: the whole feature is a runtime playback pool plus a read-only
view over the sound assets (spec FR-006). The model is small and value-shaped (Constitution IV).

## `Sound.Voices` — the fixed playback pool

The bounded set of simultaneous one-shots. One pool is made per run and held on `SharedState`.

```fsharp
/// The one-shot playback pool: a fixed set of tracks reused round-robin.
type Voices =
    { Tracks : Audio.TrackHandle array   // the voices; created once, `0n` when audio is unavailable
      Playing : string array             // the asset id each voice is playing, or "" when idle/finished
      mutable Next : int }               // the round-robin cursor (the next voice to use)
```

- **Capacity**: a fixed constant, `Sound.capacity`, 16. Not authored, not exposed, not persisted
  (FR-003).
- **`Tracks`**: `capacity` playback tracks, created by `newVoices` via `Audio.createTrack`. With an
  unavailable mixer every handle is `0n` and the pool is inert.
- **`Playing`**: parallel to `Tracks`; entry `i` is the asset id voice `i` was last asked to play, or
  `""` when it is idle or its one-shot has finished. This is what a per-sound Stop matches against; the
  pool never needs to ask SDL whether a voice is still sounding.
- **`Next`**: the round-robin cursor. Because `play` only advances it, the voice it selects is the
  least-recently-used, i.e. the oldest-started — the spec's "oldest is stopped to make room" (FR-003,
  research D3).

### Operations

| Op | Signature | Effect | Requirement |
|---|---|---|---|
| `newVoices` | `Audio.Context -> Voices` | Create the tracks (one per capacity slot) and an all-`""` record; cursor at 0. | FR-002 |
| `play` | `Voices -> string -> AudioAsset.SoundHandle -> unit` | Take the next voice (round-robin), set its audio to the resident handle, play it from the top, record the asset id, advance the cursor. | FR-001, FR-003 |
| `stop` | `Voices -> string -> unit` | Stop every voice whose recorded id matches; clear those records. | FR-004 |
| `stopAll` | `Voices -> unit` | Stop every voice; clear every record. | FR-005 |
| `release` | `Audio.Context -> Voices -> unit` | Destroy every track; clear every record. | FR-009 |

All ops are guarded inside `Audio` for a `0n` handle, so an unavailable mixer makes each a quiet no-op
(FR-008) and a fresh pool against `Audio.unavailable` is assertable headlessly (research D9).

## `SharedState` — the pool's home

```fsharp
type SharedState =
    { ...
      mutable Mix : Music.Mix
      mutable Rack : Music.Rack
      Voices : Sound.Voices }   // NEW — the one-shot pool; game-wide, freed before the mixer
```

- Because the pool is a fixed set of tracks owned by one context, it is not `mutable` (nothing
  reassigns it) — it is created once in `SharedState.create` with the audio context (spec FR-009's
  owner). It sits beside `Rack` for the same reason: a game-wide runtime audio resource that must be
  freed before `Assets.release` (research D5).
- **Every `SharedState` literal fixture** in `test/` gains `Voices = Sound.newVoices Audio.unavailable`
  (the device-free pool), as the `Mix`/`Rack` fields already required (spec 087).

## Asset readout — `Assets.soundAssets`

```fsharp
/// The discovered sound-class assets (spec 086's `Kind = Sound`), sorted by name — the panel's rows.
let soundAssets (manager: AssetManager) : AudioAsset.AudioAsset list
```

- Only `Kind = Sound` (FR-004); sorted by `Name` so the list is stable and human-ordered.
- Each returned `AudioAsset` carries everything a row needs: `Id` (to play), `Name` (to show), and
  `Residency` — `Resident` (playable) or `Failed` (listed and marked, FR-007). A failed asset is still
  returned, so it is visible to the author.

## Validation rules

- **Play resolves a handle only where the unit is present.** The panel calls `AudioAsset.sound` only
  when `AudioAsset.isResident` holds; a failed or absent asset is a no-op (FR-007) — never a read over
  a freed handle (the `AudioAsset` accessor's contract).
- **Stop matches by recorded id.** A row's Stop silences only voices whose record equals that asset id;
  it never touches another sound (FR-004, SC-007).
- **The pool is bounded.** No path grows the pool: the tracks are created once and only the cursor and
  the records change (FR-003).
- **Nothing is authored.** No op writes `Editor.Undo`, the mix, a zone, or any command; the pool is the
  whole of a play's effect (FR-006, SC-004).

## State transitions

A voice moves `idle → sounding` on `play`, and `sounding → idle` when its one-shot finishes (no record
change happens on its own — the pool does not poll) or when a Stop/Stop-all/reuse clears it. A `Playing`
entry therefore means "last asked to play this asset", which is precisely what a Stop needs; whether
the sound is *currently* audible is deliberately not tracked (the panel shows no playing indicator,
clarified Q2).
