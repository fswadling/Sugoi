# Phase 0 Research: Music Cues — play and shape music from the story

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

## Context

spec 087 built the music **playback** half: an authored `Mix` (`MixRow`/`Mix`, `audio/Music.fs`) and a
runtime `Rack` of streaming decks, held game-wide on `SharedState`, driven by hand from the Songs panel
through `Music`'s ops (`addRow`, `removeRow`, `play`, `stop`, `setWeight`, `setMaster`, `reconcile`). This
phase is the **story half**: cue ops that set up the palette, transport it, and shape it as the run reaches
them — with parity to the actor `Animate`/`CrossFade` ops (spec 066/075) and the screen fades (spec 060).

The cue walk (`world/CueEngine.fs`) already receives the `SharedState` (`shared`) as a plain reference, and
`shared.Mix`/`shared.Rack` are mutable fields — so a cue can perform a music op on the *same* authored mix the
Songs panel edits, exactly as `AddAdvent` writes `shared.Advents` (spec 055). No new `Music` API is needed;
the engine composes what spec 087 shipped.

## Decision 1 — Op shapes: no-progress leaves, and one `…State` per shaping op

- **Decision**: nine authored cases, split by whether they span time:
  - **No-progress leaves** (performed where reached, returning `Fin`): `AddMusic`, `RemoveMusic`, `PlayMusic`,
    `StopMusic`, `PlayBed`, `StopBed`.
  - **Time-spanning** (`…State` carrying the captured level(s), the duration and the deadline): `SetMusicLevel`
    / `MusicLevelState`, `SetMusicMaster` / `MusicMasterState`, `CrossFadeMusic` / `CrossFadeMusicState`.
- **Rationale**: this is the convention `world/Cue.fs` already states — a no-progress op holds no record; a
  leaf that spans time carries its numbers in its own `…State`. It is the `FadeOut`/`FadeOutState` and
  `Animate`/`BlendState` shape exactly: capture the level where the walk reaches the op, so a ramp that begins
  part-way is correct (FR-012).
- **Alternatives considered**: a single `Music` op with a sub-command union — a second vocabulary nested in the
  first, against "op-per-meaning"; carrying a `set`/`fade` flag — the duration already distinguishes them
  (a duration of zero is the instant form), so a flag would be redundant state.

## Decision 2 — The write path: a cue mutates `shared.Mix` directly, and records no step

- **Decision**: a membership/level/shaping op assigns `shared.Mix <- …` in place, then tells the rack, and
  returns `Fin` (or holds its `…State`). It **never** calls `History.commit`, so it lands **0** steps on the
  one undo history (FR-009) — the `AddAdvent`/`RemoveAdvent` pattern (`AddAdvent advent -> shared.Advents <-
  Advent.add advent shared.Advents`, spec 055).
- **Rationale**: the mix is authored *state*, and the editor's `SetMix` is the undoable authoring path
  (spec 087 D8); a cue is a runtime effect on that same state, not an authored edit — so it must not be
  undoable, or undo would rewind a cue's runtime work. One mix, one home, two writers with different history
  semantics (the clarification).
- **Alternatives considered**: routing a cue's change through `History.commit` — would put runtime playback on
  the undo stack (FR-009 forbids it, and a fade would flood it); a separate runtime mix overlaying the
  authored one — rejected by the spec (the level *is* the weight).

## Decision 3 — The rack is kept in line by `Music.reconcile`; no new `Music` surface

- **Decision**: after writing the mix, an op that can change a live deck calls
  `Music.reconcile shared.Assets.Audio shared.Rack shared.Mix` — the spec-087 reconciler that frees a deck
  whose row has left the mix and re-applies a survivor's `master × weight` gain and loop flag:
  - `RemoveMusic` → reconcile (frees the removed row's deck; FR-002).
  - `SetMusicLevel` / `SetMusicMaster` / `CrossFadeMusic` → reconcile each tick while the ramp runs (applies
    the moving gain to the live decks) and once at the end.
  - `AddMusic`, `PlayMusic`, `PlayBed` → no reconcile needed: `addRow` opens nothing (`play` does, spec 087
    D10), and `play` re-applies the row's gain itself; `StopMusic`/`StopBed` touch decks directly.
- **Rationale**: `reconcile` is exactly "bring the rack back in line with the authored mix" and already exists
  (spec 087 D9) — the engine composes it rather than growing `Music`. Applying gain on every ramp tick is the
  minimum needed to *hear* the fade move, and it mirrors an actor blend writing the renderer weight each tick.
- **Alternatives considered**: a new `Music.setRowGain (context) (rack) (asset) (gain)` — one function and one
  more public surface to keep in step with `reconcile`; not worth it for a handful of rows (Constitution I).

## Decision 4 — What a music op no-ops on, and what it reports

A music op is deliberately forgiving where the state it names is absent (Principle II: `Result`/`Option` for
expected absence, `invalidOp` for bugs) — but a silent no-op cannot be told from a working op, so the reason is
reported through the existing `noOp` family (the cue's convention).

- **Not a palette row** (for a transport/level/cross-fade op): **reported no-op**. Transport and shaping act
  on the palette; a song must be `AddMusic`-ed first. This keeps the one state — the mix — the only place a
  level lives (no "pending level" side table, which would be the separate runtime layer the spec rejects).
  - `PlayMusic` in particular must check membership *before* `Music.play`: `Music.play` raises `invalidOp`
    when the asset is not a row (its callers are the panel, which checks first), so the cue guards rather
    than relies on the fault.
- **Asset unavailable** (not in `AudioAssets`, or not a `Reference` — spec 086 `Failed`): **reported
  no-op**. Resolved without touching `Assets`: `shared.Assets.AudioAssets.TryGetValue` + `AudioAsset.isReference`
  (the same test the panel's `playRow` makes). This also avoids `Assets.audioAsset`'s
  `KeyNotFoundException` for an id no manifest declared.
- **Mixer unavailable** (`shared.Assets.Audio.Mixer = IntPtr.Zero`): a **silent** no-op, matching the Songs
  panel (spec 087 FR-013) — every deck-dependent op is already inert (`Music`'s ops guard on `Mixer = 0n`), and
  the story runs on. Nothing to report: the whole subsystem is down, which the asset scan already logged.
- **Not currently playing** (a palette row with no live deck), for a level/cross-fade op: **not** a no-op —
  the level is written to the mix row and applied when the song next plays (FR-011; `Music.play` re-applies the
  row's gain). `Music.setWeight` already writes a row independent of any deck.
- **A redundant transport** (stop a stopped song, play the one already playing): a quiet no-op or a restart,
  never an error — `Music.stop` on no deck and `Music.play`'s `startOf` rule already give this.
- **`AddMusic` of an existing row / `RemoveMusic` of an absent one**: a quiet no-op — `Music.addRow`/
  `removeRow` are already idempotent (FR-001/FR-002).

## Decision 5 — The transport, level and cross-fade forms

- **`PlayMusic`** wraps spec 087's row play: resolve the row's `Path`, then `Music.play` — which opens the deck
  on first use, applies the row's gain, and **resumes a paused row else seeks to 0 and plays** (spec 087 D5).
  "Plays from its beginning" (FR-003) holds for a stopped/never-played row, which is the authored intent.
- **`StopMusic`** wraps `Music.stop` — halt and seek to 0 (FR-004, spec 087 D5).
- **`PlayBed`** plays every palette row (each per the same row rule), **`StopBed`** stops every row. This is
  the panel's *Play all* / *Hold all* over the whole row set (FR-005); it is one op so a scene starts or ends
  the bed without enumerating songs.
- **`SetMusicLevel (song, level, duration)`** ramps one row's mix weight from its captured value to `level`
  (`Music.setWeight`), writing the mix each tick and reconciling; a duration of 0 lands immediately. The
  captured `initial` is what makes a retarget continue from the current level, not jump (FR-012) — the
  `stepFade`/`stepBlend` formula: `t = (now - (endsAt - duration)) / duration`.
- **`SetMusicMaster (level, duration)`** the same, over `Music.setMaster` (FR-007) — one op ducks or lifts the
  whole bed.
- **`CrossFadeMusic (from, to, duration)`** ramps `from`'s weight to 0 and `to`'s to 1 together over the
  duration (FR-008), the music counterpart of `CrossFadeAnimation` and the same arithmetic as `writeBlend`'s
  two-clip case. The `…State` captures both initial weights. **A missing side** (not a palette row) is a
  reported no-op **and the other side still moves** (US3/AC3): `Music.setWeight` is a no-op on an absent row,
  so the step writes both weights blindly and only the present side changes. If *both* sides are absent the op
  reports twice and is `Fin`.
  - **Scope note**: a cross-fade shapes **levels only** — it does not start the incoming song's playback, the
    parity the spec asks for (a shaping op is `Animate`/`CrossFade`'s analogue, and neither "plays" a clip —
    it sets a weight). An authoring pattern that wants the incoming track to sound pairs it with `PlayMusic`
    (quickstart: set the incoming row's level to 0, `PlayMusic` both, then `CrossFadeMusic`). Auto-playing the
    incoming side is deliberately **not** added (FR-015 keeps the phase minimal); it is a candidate for a later
    phase if the director wants it.

## Decision 6 — The editor authors the ops (kinds, songs, numbers)

- **Decision**: the nine cases join `editor/CueTree.fs`'s existing tables and `editor/CueEditor.fs`'s field
  routing, under a new **Music** category in the kind dropdown:
  `Add music`, `Remove music`, `Play music`, `Stop music`, `Play bed`, `Stop bed`, `Music level`,
  `Music master`, `Cross fade music`.
  - **Song chooser**: a `filteredCombo` over `Assets.musicNames` — the pick-only combo `drawAssetCombo`
    already uses for `AddActor`'s asset. A song is a discovered music asset; there is no "author it before it
    exists" case (unlike a speaker or a scenario name), so pick-only is the honest control.
  - **Numbers**: a `Level` (0..1) and a `Seconds` `dragNumber` for the level ops, a `Seconds` for the
    cross-fade — the `Animate`/`CrossFade` pattern. Each writes the authored form back.
  - **Writers**: `withSong` (the chooser's write, like `withTrigger`), plus `withSong2` for the cross-fade's
    second name or one `withSong` handling both fields by position.
  - **No place**: every music op is `placeOf → ValueNone` (music is global; the mix names no zone) and is a
    leaf in `children`.
- **Rationale**: the op must be authorable without bespoke editors (FR-013), and the panel's tables are
  exhaustive matches, so a new case *must* be entered there or the build warns — the tables are the registry.
  Reusing the asset combo and the drag-number idiom adds no new widget.
- **Alternatives considered**: a typed (name-then-asset) song field like the speaker chooser — unnecessary:
  unlike a speaker, a music op is meaningless until the asset exists, so a typed fallback would only author a
  permanent no-op.

## Decision 7 — The rename is a pure rename (no data migration)

- **Decision**: `CrossFade` → `CrossFadeAnimation`, `CrossMorph` → `CrossFadeMorph`, in the `Cue` cases and
  everywhere they are matched: `world/Cue.fs`, `world/CueEngine.fs` (`opName`), `editor/CueTree.fs` (the
  tables and the `"Cross fade"`/`"Cross morph"` labels), `editor/CueEditor.fs`, and the test `describe`
  matches. The kind labels become `Cross fade animation` / `Cross fade morph`, and `blendOpName` emits them, so
  `kindName` and the dropdown agree (a mismatch would make re-choosing the kind look like a change).
- **Rationale**: the new `CrossFadeMusic` collides in reading with the actor `CrossFade`; a suffix at the same
  call site (`CrossFadeAnimation`/`CrossFadeMusic`) reads alike and leaves `Animate`/`Morph` alone (FR-014).
- **No migration**: cue authors are not persisted yet (that lands with the planned `world/Project.fs`), so no
  on-disk name changes — this is a compile-time rename only. The suite is the regression gate.

## Decision 8 — Allocation: idle stays 0 bytes; a ramp writes the mix per tick

- **Decision**: an idle tick (no shaping op in flight) touches nothing and stays **0 bytes**, as the suite
  already asserts. A shaping op **in flight** writes `shared.Mix` each tick — a new `Mix` and its rows list —
  and calls `reconcile`; both allocate while the ramp runs and stop when it ends. A no-progress music op
  allocates only what `Music`'s own op does (an `addRow`/`removeRow` list; nothing for a transport).
- **Rationale**: the mix is an immutable authored value (spec 087), so moving a weight *is* a new value; there
  is no mutable field to poke. It is a short, deliberate effect off the idle path — the same "clarity first,
  then measure" stance the plan records (Constitution V). The alternative — a mutable runtime level overlaid on
  the authored mix — is the separate layer the spec rejects.
- **Alternatives considered**: a per-tick guard that writes only the changed row — `setWeight` already rebuilds
  only the rows list (sharing the other rows), which is as small as the model allows without a mutable mix.

## Decision 9 — Test split: a new music-cue suite, and the touch-up of the two cue `describe`s

- **Decision**:
  - **NEW `test/MusicCueTests.fs`**: the music ops headlessly, against the device-free `SharedState`
    (`TestSupport.manager IntPtr.Zero …`, so audio is `unavailable`) — the effects on `Mix`/`Rack`, the
    `…State` transitions against a `now` clock, the no-op rules, the "0 steps" property, and the retarget rule
    — the shape `test/MusicTests.fs` (spec 087) and `test/CueTests.fs` already use.
  - **CHANGED `test/CueTests.fs` / `test/CuePanelTests.fs`**: the two exhaustive `describe` matches gain the
    renamed cases and the nine new ones (so the build stays warning-free), and the label assertions follow the
    rename. No behaviour is loosened — the existing assertions stay as the regression gate (FR-016).
- **Rationale**: audio output cannot be asserted headlessly, so the *op effects on the mix* (which can be) are
  pinned here, and the audible fade is the [quickstart.md](./quickstart.md) walkthrough — spec 087's split,
  applied to the cue ops.
- **Alternatives considered**: folding the music ops into `CueTests.fs` — that file is already large and
  mostly actor/zone shape; a focused suite keeps the music op set readable and independent.
