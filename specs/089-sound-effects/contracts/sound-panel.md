# Contract — the surfaces that change

The feature's interface is internal (a native desktop game has no external API), so this documents the
module boundaries that change, what each promises, and what explicitly does not move. Paths are
project-relative; compile order is `fsharp-game.fsproj` list order.

## New — `audio/Sound.fs` (module `Sound`)

The one-shot playback pool and its ops. Names `Audio` (track ops) and `AudioAsset` (`SoundHandle`) only
— never `Assets` or `SharedState`. Compiled after `audio/Music.fs`, before `world/`/`state/`.

```fsharp
/// The capacity of the voice pool — the bound on simultaneous one-shots (FR-003). Fixed, not authored.
[<Literal>]
let capacity : int = 16

type Voices =
    { Tracks : Audio.TrackHandle array
      Playing : string array
      mutable Next : int }

/// Create the pool: `capacity` playback tracks on the mixer's context (all `0n` when it is unavailable).
val newVoices : Audio.Context -> Voices

/// Play a resident sound one-shot on the next voice (round-robin; the oldest is reused). Records the id.
val play : Voices -> assetId:string -> AudioAsset.SoundHandle -> unit

/// Stop every voice currently recorded as playing `assetId` (FR-004).
val stop : Voices -> assetId:string -> unit

/// Stop every voice (FR-005).
val stopAll : Voices -> unit

/// Destroy every track (FR-009). Called before the mixer is released.
val release : Audio.Context -> Voices -> unit
```

**Promises**: a `play` allocates nothing and does no per-frame work; every op is safe on an unavailable
(`0n`) mixer; `play` never raises for a valid handle (the caller resolves residency first).

## New — `editor/SoundPanel.fs` (module `SoundPanel`)

```fsharp
/// Draw the Sounds panel: one row per sound asset (name, state, Play, Stop) and a Stop all.
val draw : SharedState.SharedState -> byref<GameplayState.GameplayState> -> unit
```

**Promises**: reads the assets via `Assets.soundAssets`; a row shows the name and a
resident/failed marker; **Play** plays a resident sound (a failed/absent one is a no-op, FR-007);
**Stop** silences that sound; **Stop all** silences everything; the panel records **no** history step
and writes no authored state (FR-006). Empty when there are no sounds. A bottom-docked tab (see the
native change).

The panel does **not** open `AudioAsset` (it references `AudioAsset.*` qualified) so `Sound.play` is
unambiguously the module (research D8).

## Changed — `world/Assets.fs`

```fsharp
/// The discovered sound-class assets (spec 086's `Kind = Sound`), sorted by name — the panel's rows.
val soundAssets : Assets.AssetManager -> AudioAsset.AudioAsset list
```

Only this readout is added; the scan, the sound/music residency split, and `musicNames` are untouched.

## Changed — `state/SharedState.fs`

```fsharp
type SharedState =
    { ... 
      mutable Rack : Music.Rack
      Voices : Sound.Voices }   // NEW
```

Created in `create` from the audio context (`Sound.newVoices audio`), beside `Rack`. Not mutable —
nothing reassigns it.

## Changed — `Engine.fs`

Free the voices before the mixer, beside the decks:

```fsharp
do Music.release manager.Shared.Assets.Audio manager.Shared.Rack
do Sound.release manager.Shared.Assets.Audio manager.Shared.Voices   // NEW
do Assets.release manager.Shared.Assets
```

## Changed — `editor/Editor.fs`

- Add `"Sounds"` to the docked-panel focus list (`panelNames`), so Ctrl+Tab reaches it.
- Submit `SoundPanel.draw shared &state` in `draw`, after `SongPanel.draw`.

## Changed — `native/cimgui_sdl3.cpp`

`DockBuilderDockWindow("Sounds", bottom)` in `cimgui_setup_dock_layout`, beside `"Songs"`, the `"Asset
Browser"` and `"Dialogue"`. Rebuilt with `native/build.sh`.

## Changed — `fsharp-game.fsproj`

Insert `audio/Sound.fs` after `audio/Music.fs`, `editor/SoundPanel.fs` after `editor/SongPanel.fs`, and
(in the test project) `test/SoundTests.fs` after `test/MusicTests.fs`.

## Explicitly unchanged

- `audio/Audio.fs` — the track ops (`createTrack`, `setAudio`, `play`, `stopTrack`, `destroyTrack`) are
  reused as-is; **no new SDL binding** (research D4).
- `audio/AudioAsset.fs`, `audio/Music.fs`, `world/Cue.fs`, `world/CueEngine.fs` — no cue ops this phase
  (FR-010).
- `state/Command.fs`, `state/History.fs` — the panel authors nothing, so there is no new command and no
  history runner (FR-006).
- Every shader, pipeline and GPU resource — no rendering change.
