# Data Model: Music Cues — play and shape music from the story

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Research**: [research.md](./research.md)

No new module and no new persistent type. The change is **nine cases and three `…State` cases** added to the
existing `Cue` union (`world/Cue.fs`), two cases **renamed** for parity, the perform/step wiring in
`world/CueEngine.fs`, and the authoring tables in `editor/CueTree.fs`/`editor/CueEditor.fs`. `Music`,
`SharedState` and `Assets` are **unchanged** — the engine composes what spec 087 shipped.

## The `Cue` cases (`world/Cue.fs`) — CHANGED

The nine authored ops, in the union beside the actor and camera ops, under a spec-088 comment banner:

```fsharp
    // The music cues (spec 088): set up the palette (membership), start and stop it (transport), and shape
    // it (level, master, cross-fade) as the run reaches them. The mix is the authored state `SharedState`
    // holds (spec 087); a cue mutates it directly and never lands on the one history (FR-009). Membership,
    // transport and the bed are no-progress leaves; the shaping ops carry their numbers in a `…State`, the
    // `FadeOut`/`Animate` convention.
    | AddMusic of song: string
    | RemoveMusic of song: string
    | PlayMusic of song: string
    | StopMusic of song: string
    | PlayBed
    | StopBed
    | SetMusicLevel of song: string * level: float32 * duration: float
    | SetMusicMaster of level: float32 * duration: float
    | CrossFadeMusic of fromSong: string * toSong: string * duration: float
    // The in-flight forms: the captured level(s), the duration and the deadline — so a ramp that begins
    // part-way is correct and a retarget continues from the current level (FR-012).
    | MusicLevelState of song: string * initial: float32 * target: float32 * duration: float * endsAt: float
    | MusicMasterState of initial: float32 * target: float32 * duration: float * endsAt: float
    | CrossFadeMusicState of fromSong: string * toSong: string * initialFrom: float32 * initialTo: float32 * duration: float * endsAt: float
```

`fromSong`/`toSong` (not bare `from`/`to`) follow the blend cases' `fromClip`/`toClip` naming — `from`/`to` are
reserved in query expressions.

### The renamed cases (FR-014)

```fsharp
    | CrossFadeAnimation of place: string * actor: string * fromClip: string * toClip: string * weight: float32 * duration: float
    | CrossFadeMorph     of place: string * actor: string * fromMorph: string * toMorph: string * weight: float32 * duration: float
```

Pure rename of `CrossFade`/`CrossMorph`. `Animate`, `Morph` and `BlendState` are unchanged.

## The write path (`world/CueEngine.fs`) — CHANGED

The op performs on `shared.Mix`/`shared.Rack`, in place, and returns its next cue — the `AddAdvent` shape.
The engine gains small private helpers beside `noOp`:

```fsharp
/// A row's playable music asset, or `None` — an id with no manifest, or a `Failed` asset, is not playable
/// (FR-010). The same test the Songs panel's `playRow` makes; the manager's dictionary is read directly, so
/// `Assets` is unchanged and `Assets.audioAsset`'s key-not-found is avoided.
let private tryMusicAsset (shared: SharedState.SharedState) (song: string) : AudioAsset.AudioAsset option

/// Play one palette row: resolve its asset and `Music.play` (opens the deck, applies the gain, resume-else-
/// restart, spec 087 D5). Not a row → reported no-op; not playable → reported no-op (FR-010).
let private playMusic (shared: SharedState.SharedState) (song: string) : unit

/// The reason a music op performed nothing — the cue's `noOp` family, named for a song rather than an actor.
let private noOpMusic (op: string) (song: string) (reason: string) : unit
```

Per-case perform/step:

| Case | Reached | Performs |
|---|---|---|
| `AddMusic song` | no-progress leaf | `shared.Mix <- Music.addRow song shared.Mix`; `Fin` |
| `RemoveMusic song` | no-progress leaf | `shared.Mix <- Music.removeRow song shared.Mix`; `Music.reconcile …`; `Fin` |
| `PlayMusic song` | no-progress leaf | `playMusic shared song`; `Fin` |
| `StopMusic song` | no-progress leaf | row? → `Music.stop rack song`; else reported no-op; `Fin` |
| `PlayBed` | no-progress leaf | for each row, `playMusic shared row.Asset`; `Fin` |
| `StopBed` | no-progress leaf | for each row, `Music.stop rack row.Asset`; `Fin` |
| `SetMusicLevel (song, level, dur)` | capture `initial` from the row, `endsAt = now + dur` | duration 0 or row absent → write once; else hold `MusicLevelState` |
| `SetMusicMaster (level, dur)` | capture `initial = mix.Master` | as above with `Music.setMaster` |
| `CrossFadeMusic (from, to, dur)` | capture both row weights (absent → 0) | both absent → report twice, `Fin`; else hold `CrossFadeMusicState` |
| `MusicLevelState (…)` | tick | `stepMusicLevel`: write `setWeight song (lerp initial target)`, reconcile; `Fin` at `endsAt` (exact target) |
| `MusicMasterState (…)` | tick | `stepMusicMaster`: write `setMaster (lerp initial target)`, reconcile; `Fin` at `endsAt` |
| `CrossFadeMusicState (…)` | tick | write `setWeight from (initialFrom * (1-t))` and `setWeight to (initialTo + (1-initialTo) * t)`, reconcile; at `endsAt` write 0 and 1 exactly, `Fin` |

The ramp arithmetic is the existing `stepFade`/`stepBlend` formula (research D5):
`t = (now - (endsAt - duration)) / duration`, and the endpoints land exactly at `now >= endsAt`.

`Music.setWeight` is a no-op on an absent row, so the cross-fade's "missing side" needs no branch in the step
— the present side moves and the absent one is simply untouched (research D5).

## The `…State` and the walk (`world/Cue.fs` invariant)

- A shaping op returns its `…State` at the step it is reached (or `Fin` when the duration is 0), and the
  `…State` returns `Fin` at its deadline, else `ValueNone` (still in flight, nothing to rebuild) — exactly the
  `FadeOut`/`FadeOutState` pair.
- The `…State` cases carry only what the run needs: the captured level(s), the duration and the deadline. No
  handle, no deck — the mix is addressed by the song's id, which is the key the `Rack` uses.

## The editor (`editor/CueTree.fs`, `editor/CueEditor.fs`) — CHANGED

### `world`/`editor` tables that must gain the cases (all exhaustive matches)

- `CueTree.executing` — the three `…State` cases are executing; the nine authored ops are not.
- `CueTree.children` — every music case is a leaf (`[]`).
- `CueTree.kinds` — a new group: `"Music", [ "Add music"; "Remove music"; "Play music"; "Stop music";
  "Play bed"; "Stop bed"; "Music level"; "Music master"; "Cross fade music" ]`.
- `CueTree.kindName` — each authored op → its label; each `…State` → the op it came from (the view is what is
  left to run), e.g. `MusicLevelState _ -> "Music level"`.
- `CueTree.defaultOfKind` — `"Add music" -> AddMusic ""`, …, `"Cross fade music" -> CrossFadeMusic("", "", 1.0)`.
- `CueTree.label` — `AddMusic song -> adventLabel "Add music" song` (and the verb family), `PlayBed -> "Play bed"`,
  `CrossFadeMusic (f, t, _) -> sprintf "Cross fade music: %s -> %s" f t`, `SetMusicLevel (song, _, _) ->
  sprintf "Music level: %s" song`, `SetMusicMaster _ -> "Music master"`; each `…State` renders as its op.
- `CueTree.placeOf` — every music case → `ValueNone`.
- `CueTree.withSong : song: string -> cue: Cue -> Cue` (NEW) — the chooser's write, like `withTrigger`.
  Writes the first song field for every music op; `CrossFadeMusic` gets a second writer (by position, like
  `withText2`).
- `CueTree.drawEditor` — the number block: `SetMusicLevel (song, level, duration)` and
  `MusicLevelState (song, _, target, duration, _)` → a `Level` (0..1) and a `Seconds` `dragNumber` (writing the
  authored form); `SetMusicMaster`/`MusicMasterState` → a `Level` and a `Seconds`;
  `CrossFadeMusic (_, _, duration)` → a `Seconds`.
- `CueEditor.drawNodeText` — a song chooser (`drawSongCombo`, NEW) over `Assets.musicNames` for the song-bearing
  ops (`AddMusic`/`RemoveMusic`/`PlayMusic`/`StopMusic`/`SetMusicLevel`), and two for `CrossFadeMusic`.

- **`Assets.musicNames`** (spec 087) is the chooser's item list — already exists, unchanged.
- **No place selector**: `placeOf` is `ValueNone`, so `drawEditor` draws no place combo for a music op.

## Validation rules

- **VR-001 (membership)**: `AddMusic` of an existing row and `RemoveMusic` of an absent one are quiet no-ops —
  `Music.addRow`/`removeRow` are already idempotent (FR-001/FR-002).
- **VR-002 (palette required)**: a transport/level/cross-fade op naming a song that is not a palette row is a
  **reported no-op** (`PlayMusic` guards membership before `Music.play`, which would raise) — FR-010, the edge
  case. A level op names a row that is in the palette but not playing is **not** a no-op: the level is written
  and applied when the song next plays (FR-011).
- **VR-003 (asset playable)**: a song whose asset has no manifest, or is `Failed`/non-`Reference`, is a
  reported no-op on a transport op; the run continues (FR-010).
- **VR-004 (runtime, not a step)**: every music op mutates `shared.Mix`/`shared.Rack` and **records no step**;
  only the editor's `SetMix` does (FR-009, SC-003).
- **VR-005 (endpoints)**: a ramp reaches `target` exactly at `now >= endsAt`; a duration ≤ 0 is over before it
  begins, landing on the target in the same frame (SC-002).
- **VR-006 (retarget)**: a second ramp reached on the same row while one is in flight captures the *current*
  level as its initial, so it continues without a jump (FR-012).
- **VR-007 (cross-fade partial)**: a cross-fade with one side absent reports that side and still moves the
  other (US3/AC3); with both absent it reports twice and is `Fin`.
- **VR-008 (release order)**: a cue never frees the mixer; `Music.release` still runs before `Assets.release`
  at shutdown (spec 087 FR-014) — a removed row's deck is freed by `reconcile`.
- **VR-009 (no undo restore)**: because no music op is a step, a cue's mix change is not restored by undo — it
  persists like an advent a cue added (edge case; the `AddAdvent` precedent).
