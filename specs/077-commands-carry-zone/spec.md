# Feature Specification: Commands Carry the Zone They Act On

**Feature Branch**: `077-commands-carry-zone`

**Created**: 2026-10-11

**Status**: Draft

**Input**: User description: "Make each editor command carry the place it acts on, so a step is self-contained: undo and redo apply to the named zone without switching the active place, and one gesture may span several zones as a single undo step."

## Origin

Every editor mutation is a `Command` with `apply`/`undo`, pushed onto the one undo history (`docs/EDITOR_DESIGN.md`,
D-011; `specs/048-unified-undo-stack`). Today a command **names no zone**: an entity or zone-value command is applied
to whatever place is **active**, and undo/redo lean on the guarantee that the active place is the one the step was
recorded in. That guarantee is bought by recording every change of the active place as a `ZoneMove` step, so
last-in-first-out keeps the active place in step with the history — the rule the code states as *"no command carries
a zone, because recording the moves is what keeps the active zone the one the step belongs to."*

This has served single-zone authoring well. It has two costs that a **multi-zone gesture** now exposes — the
scenario **fork** (a later increment) is the first: it appends a scenario's entities across several places and forks
its cue, all as **one** undo step.

1. **A step cannot act on any place but the active one.** Applying or undoing an entity edit recorded in another
   place would require momentarily switching the active place — which re-frames the camera and drops the selection.
2. **`ZoneMove` exists on the history for that reason alone.** Switching the active place — a change of *view* —
   is recorded as an undoable *scene edit*, purely so the LIFO guarantee holds.

The fix is to make each command **carry the place it acts on**, so a step is self-contained: it says *what* to change
and *where*. Apply and undo then resolve the named place and act on **that** zone, with no switch and no reliance on
which place happens to be active. A `Batch` — one gesture of several mutations — can then span zones, which is
exactly what the fork needs.

This is a **model refactor of the command layer**, deliberately taken as its own increment before the fork: it
changes no command's *effect*, only *which zone* each acts on, and it supersedes the "no command carries a zone"
rule recorded in `docs/EDITOR_DESIGN.md` (D-011) and the rationale in `specs/048-unified-undo-stack`.

## Clarifications

### Session 2026-10-11

- Q: Now that a command names its own zone, should switching the active place stay an undoable step, or become a
  non-undoable change of view? → A: Keep it undoable, exactly as today — zone navigation stays an edit the author can
  undo, even though no command depends on it (FR-008).
- Q: Should this refactor also remove the *temporary* active-place switch that a zone delete's undo performs, or
  leave that path for now? → A: Remove it — a restore rebuilds into the inactive zone and detaches its bodies, so the
  active place, camera and selection are undisturbed (FR-014).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A step acts on the place it was recorded in (Priority: P1)

An author edits an entity in one place, moves to another, and undoes: the edit is reversed **in the place it was
made**, and the place they are now standing in — its camera and selection — is not disturbed.

**Why this priority**: it is the whole point of the refactor. Today this only works because the move is itself an
undo step and LIFO walks back through it; with place-carrying commands the edit reverses directly, however many
places the author has since visited.

**Independent Test**: make an entity edit in place A; switch to place B; undo once and assert the edit in A is
reversed, B is still active, and B's camera and selection are unchanged; redo and assert the edit is back.

**Acceptance Scenarios**:

1. **Given** an edit recorded in place A and the author now in place B, **When** the author undoes, **Then** the
   edit is reversed in A's zone and B remains the active place with its camera and selection untouched.
2. **Given** the same, **When** the author redoes, **Then** the edit is re-applied in A's zone and B remains active.
3. **Given** an edit recorded in the active place, **When** the author undoes, **Then** behaviour is exactly as
   today (the common case is unchanged).
4. **Given** a deleted place that is not active, **When** the author undoes the delete, **Then** the place and its
   entities are restored and the active place, camera and selection are untouched.

---

### User Story 2 - One gesture may span several places (Priority: P2)

A single action that touches more than one place — the scenario fork appends entities across several zones and forks
the cue — is **one** step on the history: one undo reverses the whole gesture, one redo re-applies it.

**Why this priority**: it is the capability the refactor exists to unlock. It is smaller than the core guarantee
(US1) only in that it is the specialised case of it, and no other feature needs it yet.

**Independent Test**: build a step whose sub-commands name two different places; apply it, then undo once and assert
both places are back exactly as they were.

**Acceptance Scenarios**:

1. **Given** a batch whose mutations name two or more places, **When** it is applied, **Then** every mutation lands
   in its named place's zone.
2. **Given** that batch on the history, **When** the author undoes once, **Then** every mutation — in every place —
   is reversed, as one step, with the active place unchanged.
3. **Given** an undone batch, **When** the author redoes, **Then** the whole gesture is re-applied at the recorded
   places.

---

### User Story 3 - The rule is recorded and the author's use is unchanged (Priority: P3)

The guiding documents describe the new rule (a command carries its place) instead of the old one, and everything an
author does in a single place behaves exactly as it did before.

**Why this priority**: leaving the old rule asserted is how the next reader re-derives the wrong model. It is lower
priority than the behaviour itself but part of finishing it.

**Independent Test**: build and run the suite (green, warning-free); read the command/history docs and confirm none
still states that a command carries no zone or relies on the active place; drive a normal single-zone editing session
and confirm each edit is still one step that round-trips.

**Acceptance Scenarios**:

1. **Given** the refactor, **When** the project is built and the suite is run, **Then** it builds with no warnings and
   the suite is green, with new tests asserting US1 and US2.
2. **Given** the guiding documents (`docs/EDITOR_DESIGN.md`, `docs/ARCHITECTURE.md`, the module headers),
   **When** the feature is complete, **Then** none still states that a command carries no zone or that a step relies
   on the active place (the historical `specs/048` is superseded, not rewritten).
3. **Given** a single-zone editing session, **When** the author edits, undoes and redoes, **Then** each edit is one
   step and round-trips exactly as before.

---

### Edge Cases

- **An edit in a place deleted later**: LIFO reaches the edit before the delete's restore, so the place is still
  present when the edit reverses — unchanged from today.
- **A batch that makes a place present and then edits it**: forward order makes it present before the edit runs;
  reverse order removes the edit before it takes the place away. (This is the fork's shape, exercised here as a
  batch.)
- **A content command whose named place is absent when it is reached**: a broken invariant, not a state to tolerate —
  it fails loudly.
- **A mixed batch** (state-level steps such as `ZoneAdd` alongside content steps): each sub-command is dispatched the
  way it is today; a place-naming content step resolves its own place.
- **Transform coalescing**: consecutive transforms to the same entity still merge into one step (unchanged).
- **A part command**: a part ref stays epoch-scoped to the zone's environment (unchanged); the command names the
  place whose environment the part belongs to.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every command that changes a zone's contents — an entity's transform, name, or kind-specific property;
  a create or delete of an actor, prop, part, waypoint or trigger; a waypoint link; the zone's environment, light or
  player designation — MUST carry the **place** it acts on.
- **FR-002**: Applying or undoing such a command MUST act on the zone at the **named** place, and MUST NOT depend on
  which place is active.
- **FR-003**: Building a command in the editor MUST capture the place that was active at that moment.
- **FR-004**: Undoing or redoing a content command MUST NOT change the active place and MUST NOT disturb the view
  (no camera re-frame, no selection change) beyond the edit itself.
- **FR-005**: A single `Batch` step MAY contain mutations naming several places; applied forwards and reversed
  backwards, it lands in and reverses from each named place's zone, as **one** step.
- **FR-006**: The one undo history and its last-in-first-out ordering MUST be preserved: a step recorded in place A
  MUST reverse correctly even when A is not the active place.
- **FR-007**: The command type MUST remain independent of the state container (it is compiled before it); resolving a
  command's place to a zone MUST happen in the state-level runner, not inside the command type.
- **FR-008**: Switching the active place MUST remain an undoable step on the one history, exactly as today — a
  deliberate choice (see Clarifications): zone navigation stays an edit the author can undo, even though no command
  now depends on it.
- **FR-009**: Every editor surface that builds a command (selection/transform/rename, spawning and duplication, the
  inspector's light/player/trigger edits, the asset browser's environment load, zone-registry instantiation) MUST
  name the active place at build time.
- **FR-010**: State-level steps that already name places (`ZoneMove`, `ZoneAdd`, `ZoneDelete`) and the state-level
  steps that are not a zone's (`SetCue`, `Advent`, `SetScenario`) MUST keep their present behaviour; the zone-level
  runner MUST still refuse a state-level step.
- **FR-011**: For an author working in a single place, behaviour MUST be unchanged: one edit is one step, undo/redo
  round-trip, transforms coalesce as they do today, and no edit gains or loses a step.
- **FR-012**: The guiding documents and the code's own comments that state the old rule — `docs/EDITOR_DESIGN.md`
  (D-011), `docs/ARCHITECTURE.md`, and the `Command`/`History` module headers — MUST be updated to say a command
  carries the place it acts on. The prior spec that established the old rule (`specs/048-unified-undo-stack`) is the
  historical record and is left as it stands, superseded by this feature (the project's convention, as `specs/076`
  left `specs/070`).
- **FR-013**: The tests MUST cover: an edit in one place undone while another is active; a batch spanning two places
  applied and reversed as one step; and the active place untouched by a content command's undo/redo.
- **FR-014**: Restoring a deleted zone MUST NOT change the active place, **even temporarily**: it rebuilds the
  zone's contents into the (inactive) zone and detaches their bodies, rather than switching to the zone, rebuilding
  and switching back. The author's camera and selection are undisturbed by the undo.

### Key Entities

- **Command**: the recorded, reversible editor mutation. It gains the **place** it acts on, so it is self-contained.
- **Place**: the authored identity of a runtime zone (`world/Place.fs`); the key a command now carries and the
  runner resolves.
- **Batch**: several mutations that were one gesture; now able to span places, so a multi-zone gesture is one step.
- **The one history**: unchanged in shape; only how a step finds its zone changes (the named place, not the active
  one).
- **`ZoneMove`**: the step that changes the active place. It keeps its behaviour; the refactor removes the *reason*
  it was load-bearing, not necessarily the step (see FR-008).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An entity edit made in one place, with a different place active, undoes in the place it was made, and
  the active place's camera and selection are unchanged by the undo.
- **SC-002**: A single batch naming two or more places applies and reverses as one step, leaving every place exactly
  as before the gesture.
- **SC-003**: Undoing or redoing a content command never changes the active place.
- **SC-004**: A single-zone authoring session is indistinguishable from before — same number of steps, same
  round-trips — and the project builds with no warnings and a green suite.
- **SC-005**: No guiding document or module header still states that a command carries no zone or that a step relies
  on the active place being the recorded one.
- **SC-006**: Restoring a deleted zone leaves the active place, the camera and the selection exactly as they were,
  with the zone and its entities present again.

## Assumptions

- **Behaviour-preserving except where stated.** A command changes only *which zone* it acts on, not *what it does*;
  the effect of every command type is unchanged.
- **The command type stays above the state container.** A command is defined before `GameplayState` and cannot name
  it; the state-level runner resolves the carried place to a zone, as it already carries out the state-level steps.
- **`ZoneMove` behaviour is unchanged.** Switching the active place stays an undoable step (FR-008): zone
  navigation is an edit the author can undo, kept even though no command now depends on it. Making it a
  non-undoable view change is a separate, later decision, not taken here.
- **A restored zone never switches.** The delete/restore path is brought into line with the rule — a restore builds
  into the inactive zone and detaches its bodies (FR-014), the same build-then-detach the code already uses when it
  populates a place that is not active.
- **`ZoneAdd` keeps its meaning** ("make present *and* active"). A step that makes a place present **without**
  entering it (which the fork needs) is a separate concern, introduced by that feature, not here.
- **Part refs stay epoch-scoped.** A part's ref is scoped to its environment's epoch; naming the place does not
  change that, and a part command is still a no-op or a loud failure against a mismatched environment, as today.
- **No persistence change.** Commands are not persisted; nothing on disk holds a command's shape.
- **The scenario fork is a later increment.** It is the motivation and the first consumer of a multi-place batch, but
  it is not built here; this feature only makes the command layer capable of it.
