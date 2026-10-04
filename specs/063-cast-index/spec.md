# Feature Specification: Cast Index

**Feature Branch**: `063-cast-index`

**Created**: 2026-10-04

**Status**: Draft

**Input**: User description: "Before the actor cue ops, settle where the cast index lives: the runtime cast should be keyed by `Roster.Character`, held on `GameplayState`, with one cast member unique across all zones — an actor records which character it plays, and the state indexes it."

## Origin

`specs/061-zones-as-places` made a cue address a zone by closed, authored identity and named **actor
addressing** as its other successor. The cast already exists as closed unions — `Roster.Character` (who) and
`Casting.Look` (which look) — and `Casting.tryAsset (character, look)` binds a pair to an actor asset. What is
missing is the **runtime** half: nothing records *which actor in the scene plays which character*. Actors are
anonymous in their zone, so a cue (or a system) has no way to name one.

An earlier attempt keyed the runtime cast by `Casting.Slot` (a character *and* a look) and recorded it on the
actor. That was wrong twice: a `Slot` lets one character exist twice in different looks, and the scene has one
actor per character, not one per look. This feature settles the design before any cue op is built on it:

- an actor **records the character it plays** as part of its authored state — so the binding rides
  `ActorCreate`/`ActorDelete` and a zone's capture/restore for free; and
- the **gameplay state holds an index** keyed by `Roster.Character`, so a character resolves to its actor in
  one lookup and is unique across all zones.

The cue ops that address actors through this index are the **next** feature, not this one.

**Why an index on `GameplayState` and not a scan.** The undo/redo stack is the gameplay state's
(`state.Editor.Undo`), and undo/redo run at that level (`History.undoHistory`/`redoHistory` take the state).
Every place an actor comes into or leaves existence — a create, a delete, an undo/redo of either, a zone
delete, and a zone-delete undone — is reachable with the state, so the index can be kept exact where the
changes are made rather than searched for on every use.

## Clarifications

### Session 2026-10-04

- Q: When a spawn names a character already in the scene, does it perform nothing, replace the member, or
  refuse? → A: **Perform nothing** — the existing member is left exactly as it is, so the operation is safe to
  repeat. When the existing member is in a **different zone** from the one being spawned into, the operation
  records a diagnostic naming the character and the zone it is already in: one character in two places is the
  authoring mistake worth surfacing (the same-zone case is a benign repeat and is silent).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An actor knows which character it plays (Priority: P1)

An actor that is a cast member records the `Roster.Character` it plays. The gameplay state can answer "which
actor plays Aiko?" in one lookup, and the editor can spawn a cast member the way it already does and have it
become addressable.

**Why this priority**: it is the whole point — the runtime half of the cast, and the foundation every later
actor op reads.

**Independent Test**: spawn a cast member, resolve its character through the state, and assert it is that
actor; spawn a second, non-cast actor and assert it resolves for no character.

**Acceptance Scenarios**:

1. **Given** the cast panel bound a character and look to an asset, **When** the author spawns that member,
   **Then** the actor records the character it plays and the state resolves the character to it.
2. **Given** an actor spawned from the Asset Browser (no cast entry), **When** the state is asked for any
   character, **Then** it does not resolve to that actor.
3. **Given** a cast member in a zone, **When** it is moved or its transform changes, **Then** resolving its
   character still finds it.

---

### User Story 2 - One character exists at most once across all zones (Priority: P2)

A character is in the scene **at most once, across every present zone**. Attempting to place a character that
is already present does not put a second actor in the world.

**Why this priority**: it is the invariant that makes "the actor for Aiko" well-defined; without it a cue
naming a character is ambiguous.

**Independent Test**: spawn a character, attempt to spawn it again (in the same and in another zone), and
assert still exactly one actor plays it.

**Acceptance Scenarios**:

1. **Given** a character already in the scene, **When** a second spawn of it is attempted, **Then** no second
   actor plays it (one character, one actor).
2. **Given** a character present in one zone, **When** the game moves to another zone and a spawn is attempted
   there, **Then** the attempt still sees the character as already present.

---

### User Story 3 - The binding survives undo/redo and zone delete/restore (Priority: P3)

The cast binding is authored state, so removing an actor and undoing it, or deleting a zone and undoing that,
leave the index exactly as it was — no stale entry, no missing one.

**Why this priority**: an index that drifts on undo is worse than no index; it bounds the change and is what
makes the invariant trustworthy.

**Independent Test**: spawn, undo, redo, and assert the character resolves exactly when the actor exists;
delete a zone holding a member and undo, and assert it resolves again; redo and assert it does not.

**Acceptance Scenarios**:

1. **Given** a cast member, **When** its creation is undone, **Then** the character no longer resolves; when
   redone, it resolves to the same actor.
2. **Given** a cast member in an inactive zone, **When** that zone is deleted, **Then** the character no
   longer resolves; when the delete is undone, it resolves again.
3. **Given** a cast member, **When** its delete is undone, **Then** it resolves again at the same actor.

---

### Edge Cases

- **An actor with no cast entry**: a non-cast actor records nothing and indexes none; it is unaffected.
- **A character the game has no art for**: the editor's cast spawn still only offers a bound `(character,
  look)`; an unbound pair is not placeable.
- **A character already present, spawned again**: performs nothing (one character, one actor). If the
  existing member is in another zone, the repeated spawn records a diagnostic — placing one character in two
  places is the mistake the log names.
- **A cast member whose zone is deleted and restored**: the index is rebuilt from the restored actors, so the
  actor resolves again at the same ref.
- **The active zone changing**: resolving a character does not depend on which zone is active; the index is
  global. (Which zone a *cue op* acts in is the next feature's question.)
- **A direct slot mutation that bypasses the actor ops**: an actor added to a zone's slot map outside the
  create path would not be indexed; the create path is the only supported way to add one.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An actor MUST be able to record the `Roster.Character` it plays, and MUST be able to record none
  (a non-cast actor). This record MUST be part of the actor's **authored** state, so it is carried by the
  actor create/delete commands and by a zone's capture/restore.
- **FR-002**: The gameplay state MUST hold a **cast index** keyed by `Roster.Character` that resolves a
  character to the actor playing it (which zone it is in, and its ref).
- **FR-003**: The index MUST enforce that a character is present **at most once across all zones**.
- **FR-004**: The index MUST be kept exact wherever an actor comes into or leaves existence: a create, a
  delete, an undo or redo of either, a zone delete (which removes that zone's actors), and a zone delete
  undone (which restores them).
- **FR-005**: The editor's cast panel spawn MUST record the character on the actor it spawns, so a member
  placed by hand is addressable exactly as one placed by a cue will be.
- **FR-006**: Resolving a character MUST NOT depend on which zone is active; the index is global.
- **FR-007**: A spawn that names a character already in the scene MUST perform nothing; the existing member
  is left exactly as it is. When the existing actor is in a **different zone** from the one being spawned
  into, the operation MUST record a diagnostic naming the character and the zone it is already in.
- **FR-008**: This feature MUST add **no cue ops**; addressing actors from a cue is the next feature. It adds
  the runtime cast only.

### Key Entities *(include if data involved)*

- **Character**: a value from `Roster.Character`, the closed roster — who can exist in this story.
- **Cast role**: the character an actor plays, part of the actor's authored state; `None` for a non-cast
  actor.
- **Cast binding**: `Casting.tryAsset (character, look)`, the game's sparse map from a member-and-look to an
  actor asset; its absence is "no art for that look".
- **Cast index**: the gameplay state's map from a character to the actor playing it (zone and ref); one
  character at most, across all zones.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A character resolves to the actor that plays it in one lookup, with no search of the zones.
- **SC-002**: A character is never present twice across all zones, however many spawns are attempted.
- **SC-003**: After any create, delete, undo, redo, zone delete or zone-delete undo, the index is exactly
  consistent with the actors that exist.
- **SC-004**: The project builds with no warnings and the suite is green, with no device opened.

## Assumptions

- **The runtime cast is keyed by `Roster.Character`, not `Casting.Slot`.** A look chooses which art a spawn
  uses; it is not part of the runtime identity, and the scene holds one actor per character. (`Casting.Slot`
  remains the *binding* key — the game may hold art for several looks.)
- **The index lives on `GameplayState`.** The undo/redo layer is the gameplay state's, and every actor
  existence change is reachable there, so the index can be maintained rather than searched; a zone-level
  index would be per-zone and could not answer the global-uniqueness question.
- **The cast role is set at spawn and is not edited afterwards.** The index does not have to track a role
  being changed on a live actor; changing which character an actor plays is a despawn and a spawn.
- **The roster and the look set are unchanged** — no new characters or looks are declared here.
- **The cue ops that address actors are the next feature** (`specs/064-...`), and the narrative engine is not
  touched by this change. That feature intends to combine spawning a member and moving it into a single **set
  actor** op rather than separate spawn/warp ops; recorded here only so the decision is not lost, as this
  feature adds no cue ops.
