# Phase 0 Research: Play and mix music from a song panel

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

## Context

spec 086 loads audio assets: a `Song`/`Music` asset is a probed **reference** on its file — the
manager holds its `Path` and nothing decoded (`audio/AudioAsset.fs`, `Residency.Reference`). The mixer
is already up: `Audio.Context` owns the `MIX_Mixer` (spec 086) and the eggshell is released in
`Assets.release`. This phase adds **music playback**: a song panel that plays several tracks at once,
each with a weight, a scrub and a loop toggle, over one master.

The managed `Mixer` binding already exposes the whole playback surface (verified — see plan
Technical Context), so this is a wiring-and-model problem, not a binding one.

## Decision 1 — Music only; one-shot sounds and cue ops are deferred

- **Decision**: this phase plays music decks driven from the panel. One-shot sounds and any
  cue-triggered playback are out of scope (spec FR-015).
- **Rationale**: the feature the user asked for is the song panel; sounds and cue ops are a later
  tranche. Keeping to music makes the model small (one kind of deck, no voice pool).
- **Alternatives considered**: a general playback subsystem with a sound pool now — speculative until
  sounds are actually wanted.

## Decision 2 — Each song is a streaming deck; no pool, no scheduler

- **Decision**: one **deck** per playing row — a streaming `MIX_Audio` (`LoadAudio(mixer, path,
  predecode = false)`, created from the asset's `Path`) plus its own `MIX_Track`.
- **Rationale**: music streams, so a deck holds no sample data — N decks cost almost nothing, and
  there is no reason for a shared voice pool or a "steal the oldest" policy (that is a *sound* problem).
  A streaming `MIX_Audio` carries one playback position, which is exactly one deck's semantics.
- **Alternatives considered**: a fixed track pool — unnecessary for a handful of music rows and it
  would blur "one row = one position".

## Decision 3 — Gain is `master × weight`, applied per track; no group/bus API

- **Decision**: every track's gain is `master × weight`; a weight or master change re-applies the
  product to each affected track's `SetTrackGain`. No `MIX_Group`.
- **Rationale**: two levels is what the user asked for (per-song balance + one master), and the product
  is trivially correct. Re-applying across a handful of tracks on a slider move is nothing. A music
  group would be the right tool only once a second bus (sounds) exists.
- **Alternatives considered**: a `MIX_Group` for the music bus — deferred until sounds arrive; the
  binding's group-gain path is not needed now.

## Decision 4 — Looping and finishing are native; no per-frame work

- **Decision**: loop on = `SetTrackLoops(track, -1)`; loop off = `SetTrackLoops(track, 0)` (play once).
  A finished track leaves SDL's `TrackPlaying` false at its end. There is **no** per-frame loop or
  finish handling.
- **Rationale**: with no in/out points (spec), the library loops the whole track itself. This is the
  payoff of dropping loop points — the one piece of per-frame logic that would otherwise be needed
  disappears.
- **Alternatives considered**: an app-enforced wrap — only needed for a non-zero loop start, which the
  spec excludes.

## Decision 5 — Transport: play resumes a paused row, else starts from the beginning

- **Decision**: three ops — **play**, **pause**, **stop**:
  - `play`: if the row is paused, **resume**; otherwise **seek to 0** and play.
  - `pause`: hold the position.
  - `stop`: halt and **seek to 0**.
- **Rationale**: this is the row-transport rule the spec's acceptance scenarios describe — a fresh row
  plays from its beginning (US1/AC1), pause then play resumes (US1/AC2), stop rewinds (US1/AC3) — and
  it makes "pressing play after a finished non-looping track" restart cleanly, with no special-casing
  of the end.
- **Alternatives considered**: play always from 0 — breaks resume; a separate resume control — the
  spec names only play/pause/stop.

## Decision 6 — The scrub is a slider over the duration, in seconds

- **Decision**: a row's scrub is a `sliderFloat` over `[0, duration]` in seconds. While playing, its
  value is set each frame from `GetTrackPlaybackPosition` (converted with `TrackFramesToMS`); while
  dragged (the slider returns true) the position is written back with `SetTrackPlaybackPosition`
  (`TrackMSToFrames`). `GetAudioDuration` gives the total.
- **Rationale**: a slider *is* the transport bar and needs no new ImGui binding (the binding has no
  draw-list API). Seconds read naturally and map directly onto the API.
- **Alternatives considered**: a custom draw-list strip — needs several new bindings; deferred, and
  the visual difference over a slider is small now that only the playhead is draggable (no start or
  loop markers).

## Decision 7 — The mix lives on `SharedState`; the rack too

- **Decision**: `SharedState` gains `mutable Mix : Music.Mix` (authored) and `mutable Rack : Music.Rack`
  (runtime), beside `Assets` and `Advents`.
- **Rationale**: music is game-wide — it should survive a screen change — and `SharedState` is where
  game-wide, multi-screen state already lives (`Advents` is the precedent). The mixer itself is already
  reachable there through `Assets.Audio`.
- **Alternatives considered**: `GameplayState` — the mix would reset with the screen; the panel is the
  gameplay screen's, but the *music* is not.

## Decision 8 — The authored mix is a `SetMix` state-level step

- **Decision**: add `SetMix of before: Music.Mix * after: Music.Mix` to `Command`, `placeOf` → `None`,
  and handle it in `History.apply`/`undo` (`shared.Mix <- next`) exactly as `Advent` and `SetScenario`
  are handled. A weight, master, loop or row-set change commits `SetMix`, one step **per completed
  adjustment** (a dragged slider commits on release, `isItemDeactivatedAfterEdit`).
- **Rationale**: the mix is not a zone's, so it is a state-level step like the flags and the scenario
  library; the machinery (undo/redo, the redo-clear, batching) already exists, so nothing new is built.
  One step per drag keeps the history readable and matches the Inspector's slider convention.
- **Alternatives considered**: a direct write (like the Dialogue panel) — wrong, the mix is authored
  data that must undo; a per-frame step — floods the history.

## Decision 9 — The rack reconciles after a mix change

- **Decision**: after applying or undoing a `SetMix`, `Music.reconcile shared` runs: a row that is no
  longer in the mix closes its deck (stop + free), and a surviving row re-applies its gain and loop
  setting to its live track.
- **Rationale**: the authored mix can change under a running deck (a row removed, a weight moved, an
  undo), so the runtime rack must be brought back in line — the same "reconcile after the value changes"
  shape `Zone.reconcileEnvironment` uses (spec 053).
- **Alternatives considered**: reconcile lazily in the panel's draw — would miss changes made while the
  panel is closed and would couple correctness to drawing.

## Decision 10 — A deck is created on first play and freed on removal / release

- **Decision**: a row's streaming audio is opened on its first play (not when added); a removed row
  closes its deck (FR-012); `Music.release shared` closes every deck and is called **before**
  `Assets.release` (which destroys the mixer, spec 086 FR-010; FR-014).
- **Rationale**: opening on play avoids opening files for rows the director never hears; freeing before
  the mixer respects the ownership spec 086 established (a sound belongs to its mixer).
- **Alternatives considered**: open on add — would hold a file handle per row for no benefit.

## Decision 11 — No new ImGui bindings

- **Decision**: the panel is built from `button`, `checkbox`, `sliderFloat`, `text`, `sameLine` and the
  existing combo — all already bound in `render/ImGui.fs`.
- **Rationale**: with start and loop points gone, every control is a standard widget; nothing needs the
  draw list or an invisible button.
- **Alternatives considered**: a custom timeline strip — see D6.

## Decision 12 — Test the pure logic headlessly; verify the sound by walkthrough

- **Decision**: `Music`'s pure parts — `gainOf master weight`, add/remove a row, set weight/loop/master,
  the play/pause/stop transition rule — are asserted with no device. The SDL deck calls are verified by
  the audible walkthrough.
- **Rationale**: audio output cannot be asserted headlessly; the logic that can be is small and worth
  pinning, mirroring spec 086's split.
- **Alternatives considered**: a fake deck-ops seam for the rack — more machinery than the logic
  warrants right now.
