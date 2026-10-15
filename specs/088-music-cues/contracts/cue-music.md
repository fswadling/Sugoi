# Contract: The music cue ops

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Data model**: [data-model.md](./data-model.md)

The surfaces that **change**, what they guarantee, and what explicitly **does not**.

## `world/Cue.fs` — CHANGED: the nine ops, three states, two renames

```fsharp
// membership + transport (no-progress leaves)
| AddMusic    of song: string
| RemoveMusic of song: string
| PlayMusic   of song: string
| StopMusic   of song: string
| PlayBed
| StopBed

// shaping (time-spanning: an authored op + its in-flight state)
| SetMusicLevel  of song: string * level: float32 * duration: float
| SetMusicMaster of level: float32 * duration: float
| CrossFadeMusic of fromSong: string * toSong: string * duration: float

| MusicLevelState     of song: string * initial: float32 * target: float32 * duration: float * endsAt: float
| MusicMasterState    of initial: float32 * target: float32 * duration: float * endsAt: float
| CrossFadeMusicState of fromSong: string * toSong: string * initialFrom: float32 * initialTo: float32 * duration: float * endsAt: float

// renamed for parity (FR-014)
| CrossFadeAnimation of place: string * actor: string * fromClip: string * toClip: string * weight: float32 * duration: float
| CrossFadeMorph     of place: string * actor: string * fromMorph: string * toMorph: string * weight: float32 * duration: float
```

- **Guarantee (FR-001/FR-002)**: `AddMusic` is a no-op when the song is already a row; `RemoveMusic` is a
  no-op when it is not — `Music.addRow`/`removeRow` are idempotent.
- **Guarantee (FR-003..FR-005)**: `PlayMusic`/`StopMusic` act on one row; `PlayBed`/`StopBed` on the whole row
  set. "Play" is the spec-087 row rule (resume a paused row, else seek 0 and play); "stop" halts and rewinds.
- **Guarantee (FR-006..FR-008)**: a level/master/cross-fade op ramps to its target over its duration, or in the
  same frame when the duration is ≤ 0, and lands exactly at the deadline.
- **Guarantee (FR-012)**: a ramp's captured initial is the level where it was reached, so a retarget continues
  from the current level without a jump.
- **Guarantee (FR-009)**: no music case is ever committed to the one history — a cue's mix change is a runtime
  effect (the `AddAdvent` precedent).
- `Fork`/`Sequence`/`Parallel`/`Fin` and `forkInto` are unchanged; the music cases are leaves the walk performs
  or holds like any other.

## `world/CueEngine.fs` — CHANGED: perform and step

```fsharp
// the same `advance` signature; no new public surface
let rec advance (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (now: float) (cue: Cue) : Cue voption
```

- Each music case performs on `shared.Mix`/`shared.Rack` in place and returns `Fin` (no-progress) or its
  `…State` (shaping), then the `…State` returns `Fin` at its deadline else `ValueNone` — the `FadeOut` shape.
- **Guarantee (FR-010)**: a song that is not a palette row, an unplayable/missing asset, or an unavailable
  mixer is a no-op and never raises — the reported no-op family (`noOpMusic`). `PlayMusic` checks membership
  *before* `Music.play`, which raises `invalidOp` on a non-row.
- **Guarantee (FR-011)**: a level/cross-fade on a palette row that is not playing writes the mix; the gain is
  applied by `Music.play` when the song next plays.
- The engine **composes** `Music` (`addRow`, `removeRow`, `play`, `stop`, `setWeight`, `setMaster`,
  `reconcile`); it adds no `Music` function.

## `editor/CueTree.fs` — CHANGED: the kind tables and the writers

```fsharp
val withSong  : song: string -> cue: Cue -> Cue                 // the chooser's write (like `withTrigger`)
```

- `kinds` gains a **Music** group of the nine labels; `kindName`, `defaultOfKind`, `label`, `executing`,
  `children` and `placeOf` gain the cases (and the two renamed ones); `drawEditor` draws the `Level`/`Seconds`
  numbers for the shaping ops.
- **Guarantee (FR-013)**: all nine ops are authorable from the existing panel — kinds, a song chooser, a level
  and a duration — with no new widget.

## `editor/CueEditor.fs` — CHANGED: the song chooser

- A `drawSongCombo` over `Assets.musicNames` (a pick-only `filteredCombo`, the `drawAssetCombo` pattern) for
  the song-bearing ops; two for `CrossFadeMusic` (the incoming and outgoing songs).
- **Guarantee (FR-014)**: the animation cross-fade cases and their labels read `Cross fade animation` /
  `Cross fade morph`; the `Animate`/`Morph` labels are unchanged.

## `audio/Music.fs` — UNCHANGED

- The signed surface the ops use is exactly spec 087's:
  `addRow`, `removeRow`, `setWeight`, `setMaster`, `play`, `pause`, `stop`, `reconcile` (`context`/`rack`/`mix`),
  and `rowOf`/`contains` for the palette test. No function is added, and no field of `Mix`/`MixRow` changes.

## `state/SharedState.fs` — UNCHANGED

- `mutable Mix : Music.Mix` and `mutable Rack : Music.Rack` already hold the state a cue mutates; a cue writes
  them in place, exactly as `AddAdvent` writes `shared.Advents`.

## `world/Assets.fs` — UNCHANGED

- `musicNames` (the chooser's list) and the manager's `AudioAssets` dictionary (read for a playable asset) both
  already exist. `audioAsset` is **not** used on the cue path — a cue reads `AudioAssets.TryGetValue` and
  `AudioAsset.isReference`, so an id with no manifest is a reported no-op rather than a key-not-found.

## `state/Command.fs` / `state/History.fs` — UNCHANGED

- No new `Command` case: a cue op is a runtime effect, not a step. `SetMix` and its `History` handling (spec
  087) remain the editor's authoring path, untouched.

## Explicitly NOT changed

- **One-shot sounds** and any `PlaySound` op (FR-015) — the palette is music only.
- **Start points, loop points, ducking/sidechain, spatialisation, DSP** — out of scope (FR-015).
- **The spec-086 load path**: `Audio.Context`, `create`/`unavailable`/`release`, `LoadSound`/`Probe`/
  `FreeSound`, the audio asset model and the scan.
- **The GPU, the render pass, the screens, the other panels and the undo machinery.**
- **`Engine.fs`'s shutdown order** (`Music.release` before `Assets.release`, spec 087 FR-014).

## Test surface

- **NEW `test/MusicCueTests.fs`** (device-free): each op's effect on `Mix`/`Rack`, the `…State` transitions
  against a `now` clock, the no-op rules (not a row, unplayable asset, unavailable mixer), the retarget rule,
  and the **0 steps** property (vs one `SetMix` for the editor action).
- **CHANGED `test/CueTests.fs` / `test/CuePanelTests.fs`**: the exhaustive `describe` matches and the label
  assertions gain the new and renamed cases; the existing assertions stay the regression gate (FR-016).
- **Audible**: the [quickstart.md](./quickstart.md) walkthrough is the only place a fade is *heard*.
