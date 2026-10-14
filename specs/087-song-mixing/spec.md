# Feature Specification: Play and Mix Music from a Song Panel

**Feature Branch**: `087-song-mixing`

**Created**: 2026-10-14

**Status**: Draft

**Input**: User description: "Play audio in the game and set it up in the editor. Start with music: play multiple tracks at once, each scrubbable, each with a weight and a global volume. The song panel is the focus of this tranche."

## Origin

spec 086 loads audio assets: a **sound** is decoded into memory, a **music** track is a probed
reference on its file. Nothing plays yet. This feature is the **music playback half**: an editor
**song panel** that plays and mixes several music tracks at once, so a scene can carry a music bed.

The scope is deliberately small, and every part of it was chosen in discussion:

- **Music only.** One-shot sounds and cue-triggered playback are deferred.
- **No start points and no loop points.** A song either **loops** or **stops when finished** — there
  is no in/out region and no start offset to set. This keeps the model to a bare toggle and lets the
  underlying library loop the track natively.
- **No cue ops.** Playback is driven from the panel, not from the story.
- **One global mix.** A single project-wide bed, not per-zone or per-scenario.

The panel is the feature: N songs, each with transport, a scrub control, a weight and a loop toggle,
over one master volume.

## Clarifications

### Session 2026-10-14

- Q: Should the mix have a single control that plays or stops every song at once, or should each row be
  played only on its own? → A: Both — a **bed transport** (play/stop the whole mix) alongside each
  row's own play/pause/stop.
- Q: When the bed is played, should every song start from the beginning, or resume from where each is? →
  A: Resume — bed play continues each row from its current position, and the bed's halt **holds** every
  position (a pause, not a rewind).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Play a song from the panel (Priority: P1)

The director opens the song panel and plays a music asset — hearing it, pausing it, stopping it —
the same way pressing play on any media player works.

**Why this priority**: without playback there is nothing to mix; this is the whole point of the feature.

**Independent Test**: add a music asset to the panel, press play and hear it; pause holds its position
and resume continues; stop halts it and returns to the start.

**Acceptance Scenarios**:

1. **Given** the song panel, **When** the director adds a music asset and presses play, **Then** the
   track plays from its beginning.
2. **Given** a playing track, **When** it is paused, **Then** its position is held; resuming continues
   from that position.
3. **Given** a playing track, **When** it is stopped, **Then** it halts and its position returns to the
   start.

---

### User Story 2 - Mix several songs at once (Priority: P2)

The director adds several songs, all playing at the same time, and balances them with a per-song weight
and one master volume.

**Why this priority**: a music bed is several layers mixed together; this is what makes it a *mix* rather
than a player. It ranks below single playback because without that there is nothing to layer.

**Independent Test**: add two songs; both play; moving one song's weight changes only that song; moving
the master changes both together.

**Acceptance Scenarios**:

1. **Given** two or more songs in the mix, **When** they are played, **Then** all of them sound at once.
2. **Given** a song's weight, **When** it is changed, **Then** only that song's level changes.
3. **Given** the master volume, **When** it is changed, **Then** every song's level changes together.
4. **Given** a song already in the mix, **When** it is added again, **Then** the mix still holds one row
   for it (no duplicate).
5. **Given** the whole mix, **When** the bed is played, **Then** every row plays again from its own
   current position; **When** the bed is halted, **Then** every row holds its position.

---

### User Story 3 - Scrub and loop (Priority: P3)

The director drags a song's scrub control to hear it from any point, and toggles whether the song loops
or stops when it reaches the end.

**Why this priority**: auditioning from a point and looping are what make a bed usable in practice, but
they sit on top of playback and mixing rather than enabling them.

**Independent Test**: drag the scrub mid-song and hear it continue from there; with loop on the song
repeats at the end; with loop off it stops at the end.

**Acceptance Scenarios**:

1. **Given** a song, **When** its scrub control is dragged, **Then** the position moves and playback
   continues from the new position.
2. **Given** a song with loop on, **When** it reaches the end, **Then** it starts again from the
   beginning.
3. **Given** a song with loop off, **When** it reaches the end, **Then** it stops.
4. **Given** a song that is playing, **Then** its scrub control reflects the current position as it
   advances.

---

### Edge Cases

- **A song whose asset failed to load** (spec 086) or whose file has since gone: adding or playing it
  is a no-op — reported, never a crash.
- **The audio subsystem is unavailable** (spec 086 FR-009): the panel still draws and the game runs;
  playing is a no-op.
- **A row is removed while it is playing**: it stops and its playback resources are released.
- **The panel is empty**: there are no rows and nothing is mixed — a valid, quiet state.
- **The scrub is dragged while paused**: the position moves and resuming plays from there.
- **The same song is added twice**: not offered — one row per song.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The song panel MUST list the project's music assets (spec 086's `Kind = Music`) and MUST
  let the director add one to the mix and remove one from it.
- **FR-002**: The mix MUST hold at most one row per song; adding a song already in the mix is a no-op.
- **FR-003**: Each row MUST offer transport: **play**, **pause** and **stop**.
- **FR-004**: The mix MUST offer a **bed transport** that starts every row at once — each resuming from
  its own current position — and halts every row at once while **holding** each position (a pause, not a
  rewind), alongside each row's own transport (FR-003).
- **FR-005**: Every row in the mix MUST be able to play at the same time as every other.
- **FR-006**: Each row MUST have a **weight**, adjustable while playing, that affects only that song's
  level.
- **FR-007**: The mix MUST have one **master** volume, adjustable while playing, that affects every
  song's level together.
- **FR-008**: Each row MUST have a **scrub** control spanning the song's duration; dragging it seeks,
  and it MUST reflect the current position while the song plays.
- **FR-009**: Each row MUST have a **loop** toggle: on, the song repeats from the beginning at its end;
  off, the song stops at its end. Toggling it MUST take effect on a playing row without restarting it.
- **FR-010**: The song set, each row's weight and loop flag, and the master MUST be **authored project
  state** — a change is a single undoable edit on the one history (spec 086's authoring rules), not a
  runtime-only write. A dragged control records **one step per completed adjustment**, not one per
  frame.
- **FR-011**: Transport state — whether a row is playing or paused, its position, and whether the bed is
  playing — MUST be runtime state: never authored, never a step on the undo history, never persisted.
- **FR-012**: Removing a row MUST stop it and release its playback resources.
- **FR-013**: A row whose asset is unavailable MUST be a no-op when played, and the panel MUST still
  draw and the game MUST still run.
- **FR-014**: Releasing the audio resources MUST free every row's playback resources before the mixer
  is released (spec 086's shutdown order).
- **FR-015**: This phase MUST NOT play one-shot sounds, MUST NOT offer a start point or loop points,
  and MUST NOT trigger playback from cues.
- **FR-016**: `dotnet build` MUST be clean and `dotnet test` MUST pass, with the existing suite
  unmodified.

### Key Entities *(include if feature involves data)*

- **Song**: a music asset (spec 086), addressed by its id.
- **Mix row**: one song in the mix — its asset, its **weight**, and its **loop** flag.
- **Mix**: the set of rows plus the **master** volume. Authored project state. The mix also has a
  **bed transport** — start or halt every row at once, resuming/holding each position — which is runtime
  like a deck's.
- **Deck**: the playback of one row — whether it is playing or paused, and its current position. Runtime.
- **Master**: the one volume over the whole mix.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A song added to the panel plays from the panel, pauses, resumes and stops, for 100% of
  music assets.
- **SC-002**: N songs play simultaneously, each heard at its weight × master — 0 songs audible above the
  master, and 0 affected by another row's weight.
- **SC-003**: Dragging a scrub moves the position; with loop on a song repeats; with loop off it stops
  at the end (100% of rows).
- **SC-004**: A weight, master, loop or song-set change is a single undoable step, and a transport
  change records no step (0 transport steps on the history).
- **SC-005**: An unavailable asset or an unavailable audio subsystem leaves the panel usable and the game
  running (0 crashes).
- **SC-006**: `dotnet build` is clean and `dotnet test` passes, with the existing suite unmodified.

## Assumptions

- **One global mix.** The mix is a single project-wide music bed, not per-zone or per-scenario.
- **Global and screen-independent.** Because the mix and its decks live on shared state, music that has
  started keeps sounding across a screen transition (for example gameplay → main menu); navigating never
  stops or restarts it. Deliberate (research D7): a music bed should play *over* transitions.
- **"Authored" means on the undo history.** Project persistence to disk is the planned
  `world/Project.fs` (project.json), which does not exist yet; until it lands, an authored mix is
  editable on the one history, exactly as zones are today.
- **Transport-driven.** The mix plays when the director starts it; there is no automatic playback on
  entering play mode.
- **Music only.** One-shot sounds, cue-triggered playback, start points, loop points, crossfade, ducking
  and spatialisation are out of scope for this phase.
- **Linear gains.** Weights and the master are linear in 0..1.
- **A new bottom-docked panel**, drawn from the gameplay screen's editor as a tab beside the Asset
  Browser and the Dialogue.
- **Multiple songs cost little.** Music streams from disk (spec 086), so several playing at once holds
  no sample data in memory — only a streaming handle per row.
- **A row's scrub spans its duration once the row has a deck.** A deck opens on the row's first play
  (research D10), so a freshly added row shows `(play to scrub)`: the duration is not known until the
  stream is opened. Once the row has been played (or played and paused) the scrub spans the song's
  duration and reflects the position (FR-008). This is the price of not opening files for rows the
  director never hears.
