# Feature Specification: Play One-Shot Sounds and Audition Them from a Sound Panel

**Feature Branch**: `089-sound-effects`

**Created**: 2026-10-15

**Status**: Draft

**Input**: User description: "I want to do sounds. I need the ability to play sounds in the game, and a panel in the editor from which I can explore the sounds available in the asset manager and test them."

## Origin

spec 086 loads audio assets: a **sound** is decoded and held in memory, a **music** track stays a
probed reference to its file on disk. spec 087 gave music a playback half — a song panel and a global
mix — and spec 088 wired that mix to the cues. Nothing plays a **sound** yet: the decoded buffers sit
resident in the asset manager, unheard.

This feature is the **sound playback half**: the ability to play a one-shot sound effect, plus an
editor **sound panel** where the director browses the sounds the asset manager discovered and
auditions each one.

The scope is deliberately small, and every part of it was chosen in discussion:

- **One-shot sounds only.** A sound sounds once and stops. Looping, start points and regions are not
  part of a sound effect — a long, looping thing is *music*, and music already has its own panel
  (spec 087).
- **The panel is a tester.** It is a browsing and auditioning surface: it authors nothing, records
  nothing, and persists nothing. The mix's `SetMix` history (spec 087) is not extended.
- **No cue ops.** Playing a sound from the story is a later tranche — the same one-spec gap that
  separated the song panel (spec 087) from the music cue ops (spec 088).
- **No per-sound metadata.** The classification (sound vs music, spec 086) is all a sound carries;
  default gain, groups, tags and spatialisation are deferred.

## Clarifications

### Session 2026-10-15

- Q: Is cue-triggered sound playback (a sound cue op, the analogue of spec 088's music ops) in this
  tranche, or deferred to a following spec? → A: **Deferred.** This tranche is the playback facility
  and the panel only; a sound cue op is a following spec, exactly as the music cue ops (spec 088)
  followed the song panel (spec 087).
- Q: Should a one-shot be able to sound while a previous playback of the same sound is still
  sounding (overlapping instances — e.g. rapid footsteps), or is one instance per sound at a time
  enough? → A: **Overlap, bounded.** A one-shot may sound while an earlier instance is still
  sounding, up to a bounded set of simultaneous one-shots; when the set is full the oldest is stopped
  to make room.
- Q: What is the bound on how many one-shot sounds may sound at once, and is it a fixed constant or
  something the director can tune? → A: **A small fixed constant**, defined once in the playback
  code (16 voices). It is an implementation guard, not an authored value: not exposed, not
  persisted, and the oldest instance is stopped when it is reached.
- Q: What controls does each sound row in the panel offer — just a Play button with a global Stop all,
  or also a per-sound Stop and/or a "currently sounding" indicator? → A: **Play and Stop per row**,
  plus a global **Stop all**. A row's Stop silences any currently sounding instances of that sound
  only, leaving other sounds untouched.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Audition a discovered sound from the panel (Priority: P1)

The director opens the sound panel, sees every sound the asset manager discovered, and presses play
on one to hear it — the same way the song panel (spec 087) auditions music.

**Why this priority**: this is the surface the request is about — explore the available sounds and
test them. It is the smallest slice that delivers the value: a list of sounds and a way to hear each.

**Independent Test**: open the panel with at least one sound asset present, confirm each is listed by
name, press one's play control, and hear it sound once.

**Acceptance Scenarios**:

1. **Given** one or more sound assets, **When** the panel is drawn, **Then** each is listed by its
   name.
2. **Given** a listed sound, **When** the director presses its **Play** control, **Then** the sound
   sounds once from its beginning.
3. **Given** a sound with one or more instances sounding, **When** the director presses that sound's
   **Stop** control, **Then** every sounding instance of that sound is silenced and no other sound is
   affected.
4. **Given** one or more sounds sounding, **When** the director presses **Stop all**, **Then** every
   sounding one-shot is silenced.
5. **Given** no sound assets were discovered, **When** the panel is drawn, **Then** it shows an empty
   placeholder and the game runs.

---

### User Story 2 - One-shot playback semantics (Priority: P2)

A sound plays from its resident decoded buffer, sounds once, and stops on its own; playing it again
starts a new instance that can overlap the previous one.

**Why this priority**: it is what makes the panel's play control behave like a sound *effect* rather
than a track. It sits under User Story 1 because without the list and the control there is nothing to
play.

**Independent Test**: play a short sound and confirm it stops without any further input; play it
again immediately and confirm the second instance starts while the first is still sounding (up to the
bound).

**Acceptance Scenarios**:

1. **Given** a resident sound, **When** it is played, **Then** it sounds once and stops on its own
   with no further input.
2. **Given** a sound still sounding, **When** it is played again, **Then** a new instance sounds while
   the first is still playing (up to the bound of simultaneous one-shots).
3. **Given** the bound of simultaneous one-shots has been reached, **When** another sound is played,
   **Then** the oldest sounding instance is stopped to make room and the new one sounds.

---

### User Story 3 - A broken or silent audio system never takes the game down (Priority: P3)

A sound whose asset failed to load, or a machine with no working audio output, leaves the panel usable
and the game running: playing is a quiet no-op, exactly as an unavailable music asset already is
(spec 087).

**Why this priority**: it is the guard, not the feature. It ranks last because it is a failure path,
but the project's user-facing policy (spec 080) makes it required rather than optional.

**Independent Test**: run with a failed sound asset and with audio unavailable, and confirm the panel
still draws, playing does nothing, and the game runs.

**Acceptance Scenarios**:

1. **Given** a sound asset whose load failed, **When** it is listed and played, **Then** it is marked
   failed, nothing sounds, and no error aborts the game.
2. **Given** the audio subsystem is unavailable (spec 086 FR-009), **When** the panel is drawn and a
   sound is played, **Then** the panel still draws and playing is a no-op.

---

### Edge Cases

- **The panel is empty** (no sound assets discovered): it shows an empty placeholder — a valid, quiet
  state.
- **The audio subsystem is unavailable** (spec 086 FR-009): the panel still draws and playing is a
  no-op; also, no sound can be resident, so every sound is treated as failed.
- **A sound's asset failed to load** (spec 086): it is still listed (a row the author may fix later),
  marked failed, and playing it is a no-op.
- **Play is pressed repeatedly or very rapidly**: instances overlap up to the bound; beyond it the
  oldest is stopped. Nothing is queued.
- **Two sound assets share a name**: they are distinct rows, addressed by id (the audio set has no
  name index, spec 086).
- **A long sound effect**: it still sounds once and stops at its end; there is no loop and no scrub
  (a long, loopable thing is music — spec 087).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The game MUST be able to play a **one-shot sound** — a sound asset (spec 086's
  `Kind = Sound`) addressed by its id — so that it sounds once from its beginning and stops on its
  own, with no further input.
- **FR-002**: A one-shot sound MUST play from the **resident, decoded buffer** the asset manager
  already holds (spec 086 FR-005). Playing MUST NOT decode the file again and MUST NOT touch disk.
- **FR-003**: A sound MUST be able to sound while a previous playback — of the same sound or a
  different one — is still sounding, up to a **bounded number of simultaneous one-shots**; when the
  bound is reached, the oldest sounding instance is stopped to make room. The bound MUST be a small
  fixed constant (16 voices), not an authored or tunable value.
- **FR-004**: The **sound panel** MUST list every sound asset the asset manager discovered (spec
  086's `Kind = Sound`), by its name, and MUST offer a **Play control** and a **Stop control** per
  sound — Play sounds it; Stop silences any currently sounding instances of that sound only.
- **FR-005**: The sound panel MUST show each sound's state (resident, i.e. playable, or failed) and
  MUST offer a **Stop all** control that silences every currently sounding one-shot.
- **FR-006**: The sound panel MUST be a **runtime audition surface**: auditioning a sound MUST NOT
  author anything, MUST NOT record a step on the one undo history, and MUST NOT persist anything.
- **FR-007**: An absent or failed sound asset MUST be a no-op when played — reported, never a crash —
  and it MUST still be listed so the author can see it.
- **FR-008**: If the audio subsystem is unavailable (spec 086 FR-009), the panel MUST still draw and
  the game MUST still run; playing a sound MUST be a no-op.
- **FR-009**: Releasing audio MUST stop and free every one-shot playback resource before the mixer is
  released, alongside the songs' decks (spec 086/spec 087's shutdown order).
- **FR-010**: This phase MUST NOT trigger sound playback from cues, MUST NOT loop a sound, and MUST
  NOT offer start/end points, groups, tags, ducking, spatialisation or per-sound authored metadata.
- **FR-011**: `dotnet build` MUST be clean and `dotnet test` MUST pass, with every existing assertion
  unmodified — no test weakened, removed or re-ordered. (The `SharedState` fixtures gain the one new
  field, exactly as the `Mix`/`Rack` fields already required; that addition is expected.)

### Key Entities *(include if feature involves data)*

- **Sound**: a sound asset (spec 086), addressed by its id and already decoded and resident — the
  material a one-shot plays.
- **One-shot playback**: a single sounding of a sound — it starts and stops on its own. Runtime only:
  never authored, never a step on the undo history, never persisted.
- **Voice**: one slot of the bounded set of simultaneous one-shots, aware of which sound it is
  playing (so a row's Stop can target that sound); when the set is full the oldest voice is reused.
- **Sound panel**: the editor surface that lists the discovered sounds and auditions them. It authors
  nothing.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of discovered sound assets are listed in the panel, and 100% of resident ones sound
  when their play control is pressed.
- **SC-002**: A played sound sounds once and stops on its own, with 0 requiring a manual stop.
- **SC-003**: Up to the bound, N one-shots sound simultaneously (0 cut off before the bound); beyond
  the bound, the oldest is stopped (0 dropped-into-nothing, 0 crashes).
- **SC-004**: Auditioning records 0 steps on the undo history; the history and every authored value
  are unchanged by any panel action.
- **SC-005**: A failed sound asset, or an unavailable audio subsystem, leaves the game running and the
  panel usable (0 crashes).
- **SC-006**: `dotnet build` is clean and `dotnet test` passes, with every existing assertion
  unmodified (the `SharedState` fixtures gaining the one new field aside).
- **SC-007**: A row's **Stop** silences only that sound's instances; other sounds keep sounding
  (0 other sounds stopped by a per-row Stop).

## Assumptions

- **A new bottom-docked "Sounds" panel**, a tab beside the Songs panel, the Asset Browser and the
  Dialogue — the same bottom authoring strip (spec 087's placement). It is the browsing counterpart to
  the Songs panel.
- **The panel is a pure audition surface.** The request is to "explore and test" the sounds, so the
  panel holds no authored state and no history; the runtime playback it drives is the whole of its
  effect. (If a later tranche wants authored per-sound settings, that would be a `Set*` step like the
  mix's `SetMix` — not this phase.)
- **Playing uses the resident buffer, so it costs nothing extra.** A sound is already decoded
  (spec 086), so auditioning holds no additional sample data — only a bounded set of playback voices.
- **Bounded overlap, fixed.** A small, fixed set of one-shots sounds at once (16 voices) and the
  oldest is cut when the set is full, so a rapid-fire audition never grows unbounded. The bound is an
  implementation guard, not an authored value.
- **No panel volume this phase.** Sounds audition at full level; a per-sound or panel volume, and the
  groups/tags/ducking machinery, are deferred.
- **Deferred (the later audio families):** cue-triggered sound playback, looping a sound, start/end
  points, groups/tags/buses, ducking, crossfade, and spatialisation.
- **"Play sounds in the game" means the facility, not yet a scripted trigger.** This phase provides
  the ability to play a sound (used by the panel, and available to the game); *what* plays a sound in
  the story is the deferred cue-op tranche.
- **Persistence is not touched.** There is nothing authored to persist; `world/Project.fs` does not
  exist yet, and this feature adds nothing to it.
