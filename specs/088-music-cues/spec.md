# Feature Specification: Music Cues — play and shape music from the story

**Feature Branch**: `088-music-cues`

**Created**: 2026-10-14

**Status**: Draft

**Input**: User description: "Add cue ops for playing music: set up the palette, start and stop songs, and shape levels and cross-fade between songs, driven from the story."

## Origin

spec 087 added music **playback**: an authored mix (rows, weights, loop flags, master) and a Songs panel
that plays and mixes it. Nothing plays from the story yet — playback is driven by hand from the panel.
This feature is the **story half**: cue operations so a cue can **set up the palette** (add and remove
songs), **start and stop** music on it, and **shape and cross-fade** it as the run reaches it — the way
`SetZone`, `Exposit` and `Animate`/`CrossFade` already drive the world, the screen, the dialogue and an
actor's blends.

The scope is deliberately small, and every part of it follows a decision already made:

- **Music only.** One-shot sounds stay deferred (their own feature); this phase adds no `PlaySound`.
- **No start points and no loop points.** A song plays from its beginning, loops per its authored flag,
  and stops on command.
- **The mix is authored state, in the same way the adventure flags are** (spec 055). The editor edits it
  through `SetMix` steps on the one history; **cues change it directly at runtime**, where those changes
  are **not** on the undo stack — the `AddAdvent`/`RemoveAdvent` pattern. A cue adding a song, removing
  one, or moving a level is a runtime effect, not an authored edit.
- **Parity with the actor blend ops.** The sound-shaping ops mirror `Animate` (one target moved to a
  level over a duration) and `CrossFadeAnimation` (two songs swapped over a duration). Membership,
  transport and level each also have a **whole-palette** form, so a scene can start, end or duck the bed
  with one op.

## Clarifications

### Session 2026-10-14

- Q: When a cue names a song to play, must it be a row of the authored mix, or may it name any music
  asset? → A: Cues **manage the palette** — they add songs to it and remove them, and start/stop and
  cross-fade on it — with parity to the actor `Animate`/`CrossFadeAnimation` ops.
- Q: Is the palette authored (fixed, on the undo history) or runtime (cue-built)? → A: The mix is
  **authored state**, in the same way the adventure flags are: the editor edits it through commands (an
  undoable `SetMix` step), and **cues change it directly at runtime**, where those changes are **not** on
  the undo stack.
- Q: Which exact set of music cue ops? → A: Membership + per-song transport + shaping, **plus whole-palette
  ops** — a master level and a bed transport (`PlayBed`/`StopBed`) — so a scene can start, end or duck the
  whole bed with one op. Each axis has a song form and a bed form.
- Q: The existing `CrossFade` cue op (an actor animation cross-fade) now reads confusingly beside the new
  music cross-fade. What should the names be? → A: Rename the existing ops for parity — `CrossFade` →
  `CrossFadeAnimation` and `CrossMorph` → `CrossFadeMorph` — so the animation, morph and music cross-fades
  read alike. The single-target `Animate`/`Morph` keep their names.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A cue sets up the palette and starts and stops music (Priority: P1)

The director authors a cue that brings a song into the palette, plays it when a scene opens, stops it,
and takes it back out — and sees and hears each happen as the story reaches it.

**Why this priority**: without palette and transport ops there is nothing here; this is the whole point
of the feature and the slice everything else builds on.

**Independent Test**: build a cue containing an add, a play, a stop and a remove for one song, run the
story, and confirm each takes effect at its op.

**Acceptance Scenarios**:

1. **Given** a cue, **When** it reaches an **add** op naming a song not in the palette, **Then** the song
   becomes a row of the mix (a palette song).
2. **Given** a palette song, **When** a cue reaches a **play** op, **Then** the song plays from its
   beginning.
3. **Given** a playing song, **When** a cue reaches a **stop** op, **Then** it halts and its position
   returns to the start.
4. **Given** a palette song, **When** a cue reaches a **remove** op, **Then** it leaves the palette
   (stops and releases its playback resources).
5. **Given** any music op, **When** it is performed, **Then** it records **no** step on the undo history
   — the editor's own mix edits still do.
6. **Given** two palette songs, **When** a cue reaches a **play-the-bed** op, **Then** every palette song
   plays; a **stop-the-bed** op halts every song and returns each to the start.

---

### User Story 2 - A cue shapes a song's level, optionally over time (Priority: P2)

The director fades a song in or out from the story — over a duration for a smooth transition, or
instantly — and can return it to a level later.

**Why this priority**: a bed that appears and disappears abruptly reads as a bug; shaping the level is
what makes a music cue usable in practice, and it mirrors the actor `Animate` op. It ranks below
transport because without a play op there is nothing to shape.

**Independent Test**: a cue with a level op over a duration — the song's level moves to the target across
that duration; with duration 0 it moves at once.

**Acceptance Scenarios**:

1. **Given** a palette song, **When** a cue reaches a level op with a duration, **Then** the song's level
   moves steadily to the target across that duration.
2. **Given** a level op with duration 0, **When** it is reached, **Then** the level changes immediately.
3. **Given** a level op naming a song that is not yet playing, **When** the song is later played, **Then**
   it sounds at the level set earlier.
4. **Given** the level op, **When** the editor looks at the mix, **Then** the song's level is the value
   the cue left it at — the same authored state the Songs panel shows.
5. **Given** several palette songs, **When** a cue reaches a **master-level** op, **Then** every song's
   level moves together across the op's duration (or at once when it is zero).

---

### User Story 3 - A cue cross-fades between two songs (Priority: P3)

The director swaps one song for another from the story, with one op, over a duration.

**Why this priority**: a cross-fade is the reason the palette holds more than one song; it is a
convenience over shaping two songs by hand (US2), so it sits below the per-song case, but it is what a
director reaches for at every scene change.

**Independent Test**: with two palette songs, a cross-fade op lowers the first and raises the second
together over the duration.

**Acceptance Scenarios**:

1. **Given** two palette songs, **When** a cue reaches a cross-fade op, **Then** the first's level falls
   to silence and the second's rises to full across the op's duration.
2. **Given** a cross-fade op with duration 0, **When** it is reached, **Then** the swap happens
   immediately.
3. **Given** a cross-fade op naming a song that is not in the palette, **When** it is reached, **Then**
   the missing side is a no-op (reported) and the other side still moves.

---

### Edge Cases

- **A cue names a song that is not in the palette** for a play/stop/level/cross-fade op: a reported no-op
  (or, for a level op, a level recorded for when it joins), never a crash.
- **A song whose asset failed to load, or whose file has since gone** (spec 086/087): the op is a no-op;
  the run continues.
- **The audio subsystem is unavailable**: every music op is a no-op and the story still runs, as the
  Songs panel still draws (spec 087 FR-013).
- **A cue's change to the mix is not auto-reverted.** A song a cue added, removed or re-levelled stays
  that way in the mix after the cue ends or the run stops — like an advent a cue added — until the
  editor changes it. (It is never on the undo stack, so it is not restored by undo either.)
- **A second level or cross-fade ramp reached while one is in flight** on the same song: it continues
  from the current level to the new target — no jump — the rule the actor blends and the screen fades
  already follow.
- **An editor mix edit taken while a ramp is in flight**: it is a `SetMix` step that captures the mix's
  level at that instant; the ramp then continues toward its target.
- **A stop op on a song that is already stopped, or a play op on the one already playing**: a no-op or a
  restart, never an error.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A cue MUST be able to **add** a named song to the palette (a mix row); adding a song that
  is already a row is a no-op.
- **FR-002**: A cue MUST be able to **remove** a named song from the palette; it stops and its playback
  resources are released (spec 087 FR-012). Removing a song that is not a row is a no-op.
- **FR-003**: A cue MUST be able to **play** a named song, from its beginning.
- **FR-004**: A cue MUST be able to **stop** a named song: halt it and return its position to the start.
- **FR-005**: A cue MUST be able to **play every palette song** (the whole bed) and **stop the whole bed**
  (halt every song, returning each to its start).
- **FR-006**: A cue MUST be able to set a named song's **level**, over a duration (a ramp) or immediately
  when the duration is zero.
- **FR-007**: A cue MUST be able to set the **palette master level** (the whole bed's level), over a
  duration or immediately when it is zero.
- **FR-008**: A cue MUST be able to **cross-fade** between two named songs — the one lowered to silence
  and the other raised to full, together over the duration, or immediately when it is zero.
- **FR-009**: Every music cue op MUST be a **runtime effect that is never a step on the one undo
  history**: a cue's change to the mix is applied to the same authored mix the Songs panel edits and is
  visible there, but it is not an undoable `SetMix` step.
- **FR-010**: A music op naming a song whose asset is unavailable, or performed while the audio subsystem
  is unavailable, MUST be a **reported no-op**: nothing raises, and the run continues.
- **FR-011**: A level or cross-fade op naming a song that is not currently playing MUST still set that
  song's level, applied when the song next plays.
- **FR-012**: A level or cross-fade ramp reached while another is in flight on the same song MUST
  continue from the current level to the new target (no jump).
- **FR-013**: The music ops MUST be authorable from the cue editor — a song chooser, a level and a
  duration — alongside the existing ops.
- **FR-014**: The existing actor blend cross-fade ops MUST be renamed for parity — `CrossFade` →
  `CrossFadeAnimation` and `CrossMorph` → `CrossFadeMorph` — so the animation, morph and music cross-fades
  read alike, and the cue editor's labels MUST match.
- **FR-015**: This phase MUST NOT play one-shot sounds, and MUST NOT offer start points or loop points.
- **FR-016**: `dotnet build` MUST be clean and `dotnet test` MUST pass, with the existing suite
  unmodified.

### Key Entities *(include if feature involves data)*

- **Palette**: spec 087's mix — its rows are the songs in the palette. Authored state: the editor edits
  it via `SetMix` (undoable), and cues mutate it at runtime (never on the history).
- **Song**: a music asset in the palette, addressed by its id; carries a level.
- **Level**: a song's mix weight (0..1); the master is the palette's level. The value a level op moves.
- **Music cue op**: a leaf of the cue vocabulary naming a song (or two) with, for a shaping op, a target
  level and a duration. A no-progress op (add/remove/play/stop) or a progress-carrying op (a level or
  cross-fade ramp in flight).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A cue that adds, plays, stops and removes a song — or plays or stops the whole bed —
  produces exactly that on every reach, for 100% of songs.
- **SC-002**: A level or cross-fade with a duration reaches its target in that duration (± one frame); a
  duration of zero changes it in the same frame.
- **SC-003**: A music cue op records **0** steps on the undo history, while an equivalent editor action
  still records exactly **1** `SetMix` step.
- **SC-004**: An unavailable song, asset or audio subsystem leaves the story running with **0** crashes.
- **SC-005**: After a cue plays, the Songs panel shows the mix the cue left (rows, levels) — the same
  authored state, 100% consistent with what is sounding.
- **SC-006**: `dotnet build` is clean and `dotnet test` passes, with the existing suite unmodified.

## Assumptions

- **Music only.** One-shot sounds remain out of scope (a separate feature), as do start points, loop
  points, ducking/sidechain and spatialisation.
- **The mix is authored state mutated by both editors.** The editor mutates it through `SetMix` steps
  (undoable); cues mutate it directly at runtime (not undoable) — the `AddAdvent`/`RemoveAdvent` pattern
  (spec 055). There is no separate runtime level layer: a song's level *is* its mix weight, exactly as
  `Animate` moves an actor's blend weight.
- **Op-per-meaning.** Palette membership (add/remove), transport (play/stop) and shaping (level,
  cross-fade) are separate ops; a shaping op's `set` and `fade` are one op distinguished by its duration,
  so a duration of zero is the instant form. Each axis has a **song form and a whole-palette form**
  (`play`/`playBed`, `song level`/`master level`), so a cue reaches the whole bed as easily as one song.
- **The vocabulary is closed** (Constitution N-III): the cases are added in the one shared place the cue
  vocabulary lives, and the cue editor authors them.
- **The mix is global and screen-independent** (spec 087): a music cue started by the story keeps sounding
  across a screen change, like the panel's transport.
