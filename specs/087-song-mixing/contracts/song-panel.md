# Contract: The song panel

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Data model**: [data-model.md](./data-model.md)

The surfaces that change, what they guarantee, and what explicitly does not.

## `audio/Music.fs` — NEW

```fsharp
type MixRow = { Asset : string; Weight : float32; Loop : bool }
type Mix     = { Rows : MixRow list; Master : float32 }

val gainOf    : master:float32 -> weight:float32 -> float32
val empty     : Mix
val contains  : asset:string -> mix:Mix -> bool
val addRow    : asset:string -> mix:Mix -> Mix
val removeRow : asset:string -> mix:Mix -> Mix
val setWeight : asset:string -> weight:float32 -> mix:Mix -> Mix
val setLoop   : asset:string -> loop:bool -> mix:Mix -> Mix
val setMaster : master:float32 -> mix:Mix -> Mix

/// The start rule (D5): play resumes a paused row, any other restarts from 0.
type Start = Resume | Restart
val startOf : paused:bool -> Start

type Deck = { Stream : Audio.AudioHandle; Track : Audio.TrackHandle }
type Rack = System.Collections.Generic.Dictionary<string, Deck>

val play      : context:Audio.Context -> mix:Mix -> rack:Rack -> asset:string -> path:string -> unit
val pause     : rack:Rack -> asset:string -> unit
val stop      : rack:Rack -> asset:string -> unit
val position  : rack:Rack -> asset:string -> float32
val duration  : rack:Rack -> asset:string -> float32
val isPlaying : rack:Rack -> asset:string -> bool
val isPaused  : rack:Rack -> asset:string -> bool
val seek      : rack:Rack -> asset:string -> float32 -> unit
val reconcile : context:Audio.Context -> rack:Rack -> mix:Mix -> unit
val release   : context:Audio.Context -> rack:Rack -> unit
```

- **Guarantee (FR-002)**: `addRow` is a no-op when the asset is already a row.
- **Guarantee (FR-006/FR-007)**: `gainOf` is `Master × Weight`; `reconcile` re-applies it to each live
  track, so changing one row's weight changes only that row and changing the master changes all.
- **Guarantee (FR-003, D5)**: `play` resumes a paused row, else seeks to 0 and plays; `pause` holds;
  `stop` halts and seeks to 0.
- **Guarantee (FR-009)**: `setLoop` + `reconcile` set the track's loops to repeat or play once.
- **Guarantee (FR-012/FR-014)**: `reconcile` frees a deck whose row has left the mix; `release` frees
  every deck — and is called **before** the mixer is released.
- **Guarantee (FR-013)**: with no available asset or an `unavailable` mixer, every op is a reported
  no-op; nothing raises, and the panel still draws.
- `Music` names `Audio` only — never `Assets` or `SharedState` — so the callers resolve paths.

## `audio/Audio.fs` — CHANGED

```fsharp
type AudioHandle = nativeint     // a streaming MIX_Audio (predecode = false)
type TrackHandle = nativeint     // a MIX_Track

val openStream  : context:Context -> path:string -> AudioHandle
val closeStream : context:Context -> handle:AudioHandle -> unit
val createTrack : context:Context -> TrackHandle
val destroyTrack : context:Context -> track:TrackHandle -> unit
val setAudio    : track:TrackHandle -> audio:AudioHandle -> unit
val play        : track:TrackHandle -> unit
val pauseTrack  : track:TrackHandle -> unit
val resume      : track:TrackHandle -> unit
val stopTrack   : track:TrackHandle -> unit
val setGain     : track:TrackHandle -> gain:float32 -> unit
val setLoops    : track:TrackHandle -> loop:bool -> unit
val seek        : track:TrackHandle -> seconds:float32 -> unit
val position    : track:TrackHandle -> float32
val duration    : track:TrackHandle -> audio:AudioHandle -> float32
val isPlaying   : track:TrackHandle -> bool
val isPaused    : track:TrackHandle -> bool
```

- Thin wrappers over the `SDL3.Mixer` calls in the plan's Technical Context; the existing `Context`,
  `create`, `unavailable`, `release`, `LoadSound`, `Probe`, `FreeSound` are unchanged.
- **Guarantee**: an `unavailable` context (`Mixer = 0n`) makes every op a no-op, never a crash (FR-013).

## `editor/SongPanel.fs` — NEW

```fsharp
val draw : shared:SharedState.SharedState -> state:byref<GameplayState.GameplayState> -> unit
```

- Draws a bottom-docked window (`ImGui.beginWindow "Songs"`), a tab beside the Asset Browser and the
  Dialogue, docked by `native/cimgui_sdl3.cpp`.
- **Bed transport** at the top: **Play all** (resume every row from its position) and **Hold all** (hold
  every row), plus the **master** slider — a change commits one `SetMix`.
- A **picker** over `Assets.musicNames` with an **Add** button (an existing combo); a **Remove** per row.
- A **row** per song: **Play** / **Pause** / **Stop** (runtime, no history), a **scrub** `sliderFloat` over
  `[0, duration]` (runtime), a **weight** slider and a **loop** checkbox (each one `SetMix` on release/click).
- **Guarantee (FR-010/FR-011)**: only weight, loop, master and the row set touch the history; transport
  and scrub never do.

## `state/SharedState.fs` — CHANGED

```fsharp
type SharedState =
    { …
      mutable Mix : Music.Mix
      mutable Rack : Music.Rack }
```

- `create` seeds `Mix = Music.empty` and an empty rack. Game-wide (D7).

## `state/Command.fs` — CHANGED

```fsharp
| SetMix of before: Music.Mix * after: Music.Mix
```

- `placeOf` → `None`; the zone-level `apply`/`undo` list it among the state-level steps they refuse.

## `state/History.fs` — CHANGED

- `apply (SetMix (_, next))` and `undo (SetMix (previous, _))` write `shared.Mix` and then call
  `Music.reconcile shared.Assets.Audio shared.Rack shared.Mix`.

## `world/Assets.fs` — CHANGED

```fsharp
val musicNames : manager:AssetManager -> string list   // Kind = Music ids, sorted
```

## `native/cimgui_sdl3.cpp` — CHANGED

- One line, in `cimgui_setup_dock_layout`: `ImGui::DockBuilderDockWindow("Songs", bottom);` — the panel
  docks at the bottom, a tab beside the Asset Browser and the Dialogue. The layout is built in C++, so
  `libcimgui.so` is rebuilt with `bash native/build.sh`.

## `Engine.fs` — CHANGED

- Shutdown: `Music.release manager.Shared.Assets.Audio manager.Shared.Rack` **before**
  `Assets.release manager.Shared.Assets` (decks freed before the mixer, FR-014).

## Explicitly NOT changed

- **spec 086's load path**: `Audio.Context`, `create`/`unavailable`/`release`, `LoadSound`/`Probe`/
  `FreeSound`, the audio asset model and the scan.
- **Sounds and cue ops**: one-shot sounds, `PlaySound`/`PlayMusic` cue ops, per-zone ambient music.
- **Start and loop points, crossfade, ducking, spatialisation, DSP**: out of scope (FR-015).
- **The GPU, the render pass, shaders, pipelines**: untouched.
- **The other panels** and the undo machinery: unchanged; `SongPanel` is submitted like the rest.

## Test surface

- **Device-free** (`test/MusicTests.fs`): `gainOf`, add/remove/contains, `setWeight`/`setLoop`/
  `setMaster`, and `startOf` (the play/pause/stop transition rule), all as pure functions.
- **Device-dependent**: the SDL deck calls are verified by the audible walkthrough in
  [quickstart.md](./quickstart.md).
- `test/TestSupport.fs`'s `SharedState` fixtures gain the new `Mix`/`Rack` fields; the existing suite
  stays the regression gate.
