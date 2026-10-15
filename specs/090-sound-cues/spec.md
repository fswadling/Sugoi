# Feature Specification: Sound Cues — play a sound from the story

**Feature Branch**: `090-sound-cues`

**Created**: 2026-10-15

**Status**: Draft

**Input**: User description: "next lets add a cue to play a sound"

## Origin

spec 089 gave sounds a playback half: a fixed voice pool (`audio/Sound.fs`) that plays a resident sound
one-shot, and a Sounds panel that auditions them. Nothing plays a sound from the **story** yet —
playback is driven by hand from the panel. This feature is the **story half**: a cue operation so a cue
plays a named sound at the point the run reaches it, exactly as `PlayMusic` (spec 088) plays a song and
`Exposit`/`Animate` drive the dialogue and an actor's blends.

The scope is deliberately small, and every part of it follows a decision already made:

- **One-shot only.** A sound sounds once and stops (spec 089). There is no palette, no level, no loop
  and no cross-fade — those are music's (spec 087/088) because music *is* a bed. A sound cue is one op:
  play it.
- **The runtime it drives already exists.** `PlaySound` composes `audio/Sound.fs` as-is over the voice
  pool on `SharedState`; nothing about playback changes.
- **A runtime effect, never an authored edit.** Like every music cue op (spec 088), reaching `PlaySound`
  is not a step on the one undo history — the `AddAdvent` pattern (spec 055).
- **Op-per-meaning, and the vocabulary is closed** (Constitution N-III): the case is added in the one
  shared place the cue vocabulary lives, and the cue editor authors it.

## Clarifications

### Session 2026-10-15

- Q: Is this tranche only `PlaySound`, or does it also carry a stop form (a cue that silences a named
  sound, or every sound)? → A: **`PlaySound` only.** A sound is a one-shot, so a cue plays it and that is
  all; the Sounds panel's Stop and Stop all (spec 089) remain the manual way to silence a sound.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A cue plays a sound at the point it is reached (Priority: P1)

The director authors a cue that plays a sound effect — a door, a footstep, a chime — and hears it at
the moment the run reaches that op, with the story carrying straight on.

**Why this priority**: without the play op there is nothing here; this is the whole point of the feature.

**Independent Test**: build a cue containing a play-sound op naming a discovered sound, run the story,
and confirm the sound sounds once at that op and the cue continues without pausing.

**Acceptance Scenarios**:

1. **Given** a cue, **When** it reaches a **play-sound** op naming a sound, **Then** the sound sounds
   once from its beginning.
2. **Given** a play-sound op, **When** it is performed, **Then** the cue carries on at once — it does
   **not** wait for the sound to finish.
3. **Given** a play-sound op reached again (a re-run, or a loop), **Then** the sound sounds again as a
   fresh one-shot, overlapping an earlier instance if it is still sounding (spec 089).
4. **Given** any sound op, **When** it is performed, **Then** it records **no** step on the undo history.

---

### User Story 2 - The director authors the sound op in the cue editor (Priority: P2)

The director adds a play-sound op to a cue in the cue editor and chooses which discovered sound it
names, alongside the existing ops.

**Why this priority**: an op a cue can carry but the editor cannot author is half a feature; it sits
below playback because without the op there is nothing to author.

**Independent Test**: open the cue editor, add a play-sound op, choose a sound from the chooser, and
confirm the authored cue carries the chosen sound and the op reads back by the id of the sound it names.

**Acceptance Scenarios**:

1. **Given** the cue editor, **When** the director adds a play-sound op, **Then** it is offered beside
   the existing ops and offers a chooser of the discovered sounds.
2. **Given** an authored play-sound op, **When** the cue is shown again, **Then** it reads back with the
   id of the sound it names.

---

### User Story 3 - A broken or silent audio system never takes the story down (Priority: P3)

A sound whose asset failed to load, or a machine with no working audio output, leaves the cue running:
the op is a quiet no-op, exactly as an unavailable music op already is (spec 088).

**Why this priority**: it is the guard, not the feature. It ranks last because it is a failure path, but
the project's user-facing policy (spec 080) makes it required rather than optional.

**Independent Test**: run a cue with a play-sound op naming a failed sound and with audio unavailable,
and confirm the op does nothing, the cue continues, and the game runs.

**Acceptance Scenarios**:

1. **Given** a play-sound op naming a sound whose asset failed (or does not exist), **When** it is
   reached, **Then** it is a reported no-op and the cue continues.
2. **Given** the audio subsystem is unavailable (spec 086 FR-009), **When** a play-sound op is reached,
   **Then** it is a no-op and the run continues.

---

### Edge Cases

- **The op names an id no manifest declared**: a reported no-op (`its asset is unavailable`), never a
  crash — the music ops' rule (spec 088).
- **The op names a music asset rather than a sound**: not playable as a one-shot — a reported no-op (the
  asset is not a sound).
- **The sound's asset failed to load** (spec 086): listed but not resident, so the op is a no-op.
- **The audio subsystem is unavailable** (spec 086 FR-009): every sound op is a no-op and the story runs.
- **Several play-sound ops reached in quick succession**: each is a fresh one-shot; they overlap up to
  the pool's bound, the oldest cut (spec 089) — nothing queues and nothing blocks.
- **The op is reached while the Sounds panel has sounded the same effect**: the cue's play is another
  instance on the shared pool; it is not a separate channel.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A cue MUST be able to **play a named sound** — a sound asset (spec 086's `Kind = Sound`)
  addressed by its id — as a one-shot at the point the run reaches the op.
- **FR-002**: The sound MUST play from the **resident, decoded buffer** the asset manager already holds
  (spec 086 FR-005), through the voice pool (spec 089); playing MUST NOT decode again or touch disk.
- **FR-003**: The op MUST be **non-blocking**: reaching it plays the sound and the cue continues at once,
  without waiting for the sound to finish.
- **FR-004**: Reaching a play-sound op MUST be a **runtime effect that is never a step on the one undo
  history** (the music-op rule, spec 088 FR-009).
- **FR-005**: A play-sound op naming a sound whose asset is absent or failed, or performed while the
  audio subsystem is unavailable, MUST be a **reported no-op**: nothing raises and the run continues.
- **FR-006**: The op MUST be authorable from the cue editor — a chooser naming one of the discovered
  sounds — alongside the existing ops.
- **FR-007**: Repeated reaches MUST follow spec 089's one-shot semantics: the sound sounds once and
  stops, and concurrent instances overlap up to the pool's bound, the oldest cut.
- **FR-008**: This phase MUST NOT add a sound palette, a per-sound or per-cue volume, looping, start/end
  points, a cross-fade, ducking or spatialisation for sounds (the deferred audio families).
- **FR-009**: This phase MUST NOT add a **stop** form — a cue plays a sound and nothing more; the Sounds
  panel's Stop and Stop all (spec 089) remain the manual way to silence a sound.
- **FR-010**: `dotnet build` MUST be clean and `dotnet test` MUST pass, with existing assertions
  unmodified (adapting a fixture for a new value is expected, as previous audio features show).

### Key Entities *(include if feature involves data)*

- **Sound**: a sound asset (spec 086), addressed by its id and already decoded and resident — the
  material a one-shot plays.
- **Sound cue op**: a leaf of the cue vocabulary naming a sound; a no-progress op — it begins and
  finishes in the same step, since a one-shot is not held.
- **Voice pool**: spec 089's runtime — the fixed set of playback voices the op drives; unchanged here.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A cue that reaches a play-sound op sounds the named sound, for 100% of resident sounds.
- **SC-002**: The op records **0** steps on the undo history; the cue continues in the same frame it is
  performed (no waiting).
- **SC-003**: An absent or failed sound, or an unavailable audio subsystem, leaves the run going with
  **0** crashes.
- **SC-004**: The cue editor offers the op and authors it, and an authored op reads back by the id of the
  sound it names (100% of the discovered sounds are choosable).
- **SC-005**: `dotnet build` is clean and `dotnet test` passes, with existing assertions unmodified.

## Assumptions

- **One-shot only, no palette.** Unlike music (spec 087/088), a sound has no membership, level or
  transport — spec 089's model is a bare pool. `PlaySound` is the whole family.
- **Non-blocking.** A sound effect is fire-and-forget; a cue does not wait for it (a sound that must be
  waited on is a music track or a `Wait`).
- **Addressed by asset id**, like a music cue names a song by its id; the cue editor's chooser lists the
  discovered sounds.
- **It composes the existing runtime.** `audio/Sound.fs` and the voice pool are used as they are; this
  phase adds no playback behaviour.
- **A new cue vocab case and its engine handling**, in the one shared place the vocabulary lives
  (Constitution N-III), plus the cue editor's authoring of it.
- **Deferred:** a sound palette, per-sound volume, looping, start/end points, cross-fade, ducking and
  spatialisation; a cue has no **stop** form (the Sounds panel's Stop and Stop all are the manual way to
  silence a sound).
