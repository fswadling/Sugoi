# Data Model: Play and mix music from a song panel

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Research**: [research.md](./research.md)

Two modules are new-or-changed. `audio/Audio.fs` is the **SDL_mixer device surface** (it names SDL and
holds the raw handles); `audio/Music.fs` is the **music model + rack** (it names `Audio`, never
`Assets` or `SharedState`, so the callers in `editor/` and `state/` do the resolving).

## `MixRow` and `Mix` (`audio/Music.fs`) — authored

```fsharp
/// One song in the mix: its asset, its balance, and whether it loops.
type MixRow =
    { Asset : string
      Weight : float32      // 0..1, relative to the other rows
      Loop : bool }

/// The music bed: the rows plus one master volume. Authored project state (spec FR-010).
type Mix =
    { Rows : MixRow list
      Master : float32 }    // 0..1, over every row
```

### Pure edits (device-free, headless-testable)

```fsharp
val gainOf    : master:float32 -> weight:float32 -> float32   // master * weight
val empty     : Mix
val contains  : asset:string -> mix:Mix -> bool
val rowOf     : asset:string -> mix:Mix -> MixRow option
val addRow    : asset:string -> mix:Mix -> Mix                // no-op when already present (FR-002)
val removeRow : asset:string -> mix:Mix -> Mix
val setWeight : asset:string -> weight:float32 -> mix:Mix -> Mix
val setLoop   : asset:string -> loop:bool -> mix:Mix -> Mix
val setMaster : master:float32 -> mix:Mix -> Mix
```

- Every edit returns a new `Mix`; the editor commits the before/after pair as one `SetMix` step (D8).
- `gainOf` is the one arithmetic rule: a row's audible level is `Master × Weight` (FR-006/FR-007).

### The start rule (device-free, headless-testable)

```fsharp
/// What pressing play does to a row (D5): continue a held position, or start from the top.
type Start =
    | Resume
    | Restart

val startOf : paused:bool -> Start   // paused → Resume, otherwise → Restart
```

- The one branch in the transport rule; `play` reads the row's paused state and issues `Audio.resume`
  for `Resume`, or a seek-to-0 then `Audio.play` for `Restart`. Pause (hold) and stop (halt + seek 0)
  have no branch, so they need no pure part.
- Extracted so the rule is asserted with no device (FR-003, VR-004, research D5/D12).

## `Deck` and `Rack` (`audio/Music.fs`) — runtime

```fsharp
/// One playing row: a streaming audio and the track it plays on. Opaque SDL handles.
type Deck =
    { Stream : Audio.AudioHandle
      Track : Audio.TrackHandle }

/// The runtime decks, keyed by asset id. Never authored, never persisted (FR-011).
type Rack = System.Collections.Generic.Dictionary<string, Deck>
```

### Operations (device)

```fsharp
/// Open the row's deck if needed, then play: resume when paused, else seek to 0 first (D5).
val play      : context:Audio.Context -> mix:Mix -> rack:Rack -> asset:string -> path:string -> unit
val pause     : rack:Rack -> asset:string -> unit
val stop      : rack:Rack -> asset:string -> unit
val position  : rack:Rack -> asset:string -> float32     // seconds; 0 when no deck
val duration  : rack:Rack -> asset:string -> float32     // seconds; 0 when no deck
val isPlaying : rack:Rack -> asset:string -> bool
val isPaused  : rack:Rack -> asset:string -> bool
val seek      : rack:Rack -> asset:string -> float32 -> unit   // seconds

/// Bring the rack back in line with the authored mix: free a deck whose row is gone, re-apply a
/// surviving row's gain (master × weight) and loop setting. Called after every `SetMix` (D9).
val reconcile : context:Audio.Context -> rack:Rack -> mix:Mix -> unit

/// Free every deck. Called before the mixer is released (FR-014).
val release   : context:Audio.Context -> rack:Rack -> unit
```

- `play` opens the streaming audio from `path` on first use (D10); the caller resolves `path` from the
  asset (`Assets`).
- `reconcile` takes no paths: it only closes removed decks and re-applies gain/loop to live ones.

## `Audio` (`audio/Audio.fs`) — CHANGED: the raw device ops

```fsharp
type AudioHandle = nativeint   // a streaming MIX_Audio (predecode = false)
type TrackHandle = nativeint   // a MIX_Track

val openStream  : context:Context -> path:string -> AudioHandle   // LoadAudio(..., predecode = false)
val closeStream : context:Context -> handle:AudioHandle -> unit
val createTrack : context:Context -> TrackHandle
val destroyTrack : context:Context -> track:TrackHandle -> unit
val setAudio    : track:TrackHandle -> audio:AudioHandle -> unit
val resume      : track:TrackHandle -> unit
val play        : track:TrackHandle -> unit
val pauseTrack  : track:TrackHandle -> unit
val stopTrack   : track:TrackHandle -> unit          // halts; position is moved separately
val setGain     : track:TrackHandle -> gain:float32 -> unit
val setLoops    : track:TrackHandle -> loop:bool -> unit        // SetTrackLoops(-1 | 0)
val seek        : track:TrackHandle -> seconds:float32 -> unit
val position    : track:TrackHandle -> float32                  // seconds, from frames
val duration    : track:TrackHandle -> audio:AudioHandle -> float32   // seconds, from the audio's frames
val isPlaying   : track:TrackHandle -> bool
val isPaused    : track:TrackHandle -> bool
```

- Thin wrappers over the `SDL3.Mixer` functions the plan lists; they keep SDL names out of `Music`, the
  panel and `History`.
- `gain`/`seconds` are the units the panel uses; frames ↔ seconds convert with
  `TrackMSToFrames`/`TrackFramesToMS` inside these wrappers.

## `SharedState` (`state/SharedState.fs`) — CHANGED

```fsharp
type SharedState =
    { …
      mutable Mix : Music.Mix     // authored — one global bed (FR-001..FR-010)
      mutable Rack : Music.Rack } // runtime — the decks (FR-011)
```

- Game-wide, so music survives a screen change (D7); `create` seeds `Mix = Music.empty` and a fresh rack.

## `Command.SetMix` (`state/Command.fs`) — CHANGED

```fsharp
| SetMix of before: Music.Mix * after: Music.Mix
```

- `placeOf SetMix _` → `None` (a state-level step, like `Advent`/`SetScenario`).
- The zone-level `Command.apply`/`undo` treat it as a state-level step (their `invalidOp` branch).

## `History` (`state/History.fs`) — CHANGED

```fsharp
| SetMix (_, next) -> shared.Mix <- next; Music.reconcile shared.Assets.Audio shared.Rack shared.Mix
```
```fsharp
| SetMix (previous, _) -> shared.Mix <- previous; Music.reconcile shared.Assets.Audio shared.Rack shared.Mix
```

- Apply and undo both write the mix and reconcile the rack (D9).

## `Assets` (`world/Assets.fs`) — CHANGED

```fsharp
/// The discovered music assets' ids, sorted — the song panel's picker (Kind = Music, spec 086).
val musicNames : manager:AssetManager -> string list
```

## Lifecycle / state transitions

```text
add a song (authored)        → SetMix (add row)            → reconcile: nothing to open yet
press play (runtime)         → open deck (streaming) → seek 0 → play   → Deck in Rack
press pause / play           → pause / resume                          → Deck held
press stop                   → stop + seek 0                           → Deck held, at 0
loop toggle (authored)       → SetMix (set loop)           → reconcile: setLoops on the live track
weight / master (authored)   → SetMix (set weight/master)  → reconcile: setGain = master × weight
remove a song (authored)     → SetMix (remove row)         → reconcile: close the row's deck
undo / redo a SetMix         → apply/undo                  → reconcile
app shutdown                 → Music.release (free decks) then Assets.release (mixer)
```

## Validation rules

- **VR-001 (one row per song)**: `addRow` is a no-op when the asset is already a row (FR-002).
- **VR-002 (gain)**: a row's live track gain is `Master × Weight`, re-applied whenever either changes
  (FR-006/FR-007).
- **VR-003 (authored vs runtime)**: rows, weights, loop flags and master are the authored `Mix`; deck
  existence, playing/paused and position are the runtime `Rack` (FR-010/FR-011).
- **VR-004 (transport)**: play resumes a paused row else seeks to 0 then plays; pause holds; stop halts
  and seeks to 0 (D5).
- **VR-005 (no-op when unavailable)**: a row playing with no available asset (spec 086 `Failed`) or an
  `unavailable` mixer does nothing and reports; the panel still draws (FR-013).
- **VR-006 (release order)**: every deck is freed before the mixer (FR-014).
