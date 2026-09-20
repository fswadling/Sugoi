# Feature Specification: Entity Command Families

**Feature Branch**: `030-entity-command-families`

**Created**: 2026-09-20

**Status**: Draft

**Input**: User description: "Rework the editor's entity create/destroy/copy commands so that each entity kind records its own full authored state, instead of a shared base entity. Redo of a waypoint with links — and redo of any kind of entity — must restore it exactly, with no authored property silently reset."

**Origin**: the waypoint-chain storage decision in `specs/028-waypoint-chains`
(research Decision 1) and the diagnosis that followed it. The editor's shared
create/destroy/copy commands record a **base entity plus an asset binding** — the
part every kind has in common — so any property a kind carries *beyond* that pair
has nowhere to be recorded. That diagnosis is confirmed three times over in the
current code: a trigger's authored shape needed its own separate command set
(`specs/029-triggers`), a waypoint's link needed a zone-level lookup table rather
than living on the waypoint (`specs/028-waypoint-chains`), and an actor's
"show collider" state is **silently reset** by a restore today. This feature
replaces the shared record with one command family per entity kind, each carrying
that kind's own authored state, so the loss cannot recur.

## Clarifications

### Session 2026-09-20

- Q: Should toggling an actor's collider overlay be an undoable edit, or stay a non-recorded view toggle? → A: The overlay is recorded actor state, so a recorded delete, duplicate, or restore carries it and it round-trips; but toggling it is not an undo step, because it is a visualisation preference rather than a scene edit.

- Q: Should every entity kind get its own create/destroy/copy family, or only the kinds whose state exceeds the shared base record? → A: Every kind gets its own family, and the shared create/destroy/copy record is removed, so there is exactly one mechanism and no path records less than a kind's full state.

- Q: When an entity is duplicated, which of its properties should the copy inherit? → A: All of them exactly, including references to other entities; only runtime state is excluded. A duplicated waypoint therefore inherits its link (reversing 028's assumption), and a copied reference whose target is later deleted simply goes stale.

- Q: Where should a waypoint's link live? → A: On the waypoint itself, as part of its authored state, the way the reference project keeps it; the zone's separate link table is removed, so a link is carried by whichever operation records the waypoint and lives and dies with it.

- Q: Should a kind's own property edits move into that kind's family too, or do only create/destroy/copy move? → A: Kind-specific property edits join their kind's family, while edits of the shared base (transform, rename) stay shared because every kind has them; the actor overlay is outside this, since it is deliberately not an undoable edit.

- Q: Should a kind with no create, delete, or restore path be a hard build failure (by promoting F#'s non-exhaustive-match warning project-wide), or a compiler-reported warning? → A: A compiler-reported warning. Removing the catch-alls means the compiler names the unhandled kind, but the project does not promote FS0025 to an error, so no build-wide flag is added. A new *property* still fails the build, because record construction is checked exactly.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Undo and redo restore an entity exactly (Priority: P1)

A developer edits an entity — renames it, moves it, and changes the
kind-specific properties the inspector exposes for it — and then deletes it.
Undo brings it back exactly as it was: every property, not only the name,
transform, and position that every kind shares. Redo removes it again, and undoing
once more returns the same entity. Repeating the cycle never drifts.

**Why this priority**: this is the invariant the whole undo model already claims
to guarantee, and it is false today. An actor's "show collider" state does not
survive its own deletion and does not survive being redone, because the deletion
never records it. Every other outcome in this feature is a consequence of making
this one hold for every kind, so it is the MVP.

**Independent Test**: for each entity kind, set every property that kind exposes
to a non-default value, delete the entity, undo, and confirm every property is
unchanged; then redo and undo again and confirm the same.

**Acceptance Scenarios**:

1. **Given** an actor with its collider overlay enabled, **When** the developer
   deletes the actor, **Then** undoing restores it with the overlay still enabled.
2. **Given** an actor with a non-default name and transform, **When** the
   developer deletes it and undoes, **Then** its name and transform are unchanged.
3. **Given** a trigger with a non-default shape, prompt, and interactable flag,
   **When** the developer deletes it and undoes, **Then** all of them are
   unchanged.
4. **Given** any entity, **When** the developer deletes it, then undoes, then
   redoes, then undoes, **Then** at every step the entity is present or absent as
   expected and its properties are identical each time it is present.
5. **Given** an entity, **When** the developer duplicates it and then undoes the
   duplication, **Then** the original is untouched and the copy is gone; redoing
   restores a copy carrying the original's authored properties.
6. **Given** an actor, **When** the developer toggles its collider overlay and
   then undoes, **Then** the undo reverses the preceding edit rather than the
   toggle: toggling the overlay is deliberately not an undo step.

---

### User Story 2 - A waypoint's link travels with the waypoint (Priority: P2)

A developer builds a chain of waypoints, deletes one from the middle of it, and
undoes the deletion: the chain through that waypoint is whole again, in order.
Duplicating a linked waypoint produces a copy that links to the same next, so a
chain can be branched at a successor. Linking, re-linking, and unlinking are each
one undoable step. A link belongs to the waypoint that holds it, so it is carried
by whatever operation records that waypoint and dies with it. Most of this already
behaves correctly today; storing the link on the waypoint and letting a copy carry
it are the deliberate changes, and the rest is a regression to avoid.

**Why this priority**: this is the motivating case — it is what exposed the
shared-record problem — and it is the behaviour most at risk from the change. It
sits below US1 only because it is otherwise already correct, so it is mostly a
regression to avoid rather than a capability to add.

**Independent Test**: build a three-waypoint chain, delete the middle waypoint,
undo, and confirm the chain reads in its original order; duplicate a linked
waypoint and confirm the copy links to the same next, then delete that next and
confirm the copy's link simply stops resolving.

**Acceptance Scenarios**:

1. **Given** a chain of three waypoints, **When** the middle one is deleted,
   **Then** following the chain stops at the one before it, and **When** the
   deletion is undone, **Then** the chain of three reads in its original order
   again.
2. **Given** a waypoint that links to another, **When** it is duplicated, **Then**
   the copy links to the same next, and **When** that next is deleted, **Then** the
   copy's link stops resolving rather than pointing at another waypoint.
3. **Given** a waypoint that links to another, **When** it is deleted and the
   deletion undone, **Then** both the link it held and the link that pointed at it
   are unchanged.
4. **Given** a deleted linked waypoint, **When** a new waypoint occupies the slot
   it used, **Then** no link resolves to the newcomer.
5. **Given** a waypoint whose link is changed or broken, **When** the developer
   undoes, **Then** the previous link is restored exactly, as one step.

---

### User Story 3 - A new kind or property cannot silently lose state (Priority: P3)

A developer adds an entity kind, or adds a property to an existing kind. The
solution refuses to build until they have said how that kind's or property's state
is recorded when the entity is created, destroyed, copied, and restored. It is
therefore not possible to add a kind that is only partially restorable, or a
property that a restore silently drops, which is how the current losses arose.

**Why this priority**: it is what stops the invariant decaying after this feature
ships, and it is the reason to fix the shape once rather than per kind. It is
P3 because it pays off on the *next* kind rather than on this one.

**Independent Test**: add a property to an entity kind without recording it, and
confirm the solution fails to build rather than compiling and resetting the
property on restore.

**Acceptance Scenarios**:

1. **Given** an entity kind with authored properties, **When** any operation that
   creates, removes, or copies it runs, **Then** all of that kind's authored
   properties are part of what is recorded.
2. **Given** a kind whose authored state is not handled by a create, remove, or
   copy path, **When** the solution is built, **Then** the compiler reports it as an
   unhandled case.
3. **Given** an entity kind, **When** it is created or destroyed, **Then** there
   is no path that records less than that kind's full authored state.

---

### Edge Cases

- Repeated create/undo/redo cycles must not accumulate rendering or physics
  resources: a removal releases them and a restore rebuilds them, so resource
  counts return to their baseline.
- Toggling the collider overlay pushes no undo step, so an undo straight after a
  toggle reverses the preceding edit. The overlay's value is still carried by the
  next recorded operation that removes or copies the actor, so it is restored by
  that operation's undo and not lost.
- A property that is *runtime* state — recomputed while playing rather than
  authored — must not be restored from a recorded command. A trigger's
  "triggered" flag is the current example.
- Swapping a zone's environment keeps its current behaviour, including how a part
  edit recorded against one environment behaves when another is loaded.
- Undo history remains per zone: switching zones must not apply one zone's
  recorded operations to another.
- A kind whose authored state is exactly the shared set (a placed prop) must
  round-trip as before, with no behavioural change.
- A transform drag or held nudge must still collapse into a single undo step.
- Waypoint chains may loop and may share a successor, and nothing walks a chain
  (`specs/028-waypoint-chains`). Duplicating a linked waypoint is one way such a
  share arises, so no rule may assume chains are trees.
- A zone keeps no link state of its own: a deleted waypoint takes its link with it,
  so nothing resembling an orphaned entry can accumulate as waypoints are created,
  linked, and deleted, and no surviving operation can be affected by a deleted
  waypoint's old link.
- An entity deleted while a linked or referenced by another entity leaves no
  dangling reference that resolves to the wrong entity.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Undoing the deletion of an entity MUST restore every property of
  that entity that the editor can set for its kind — its authored state — and not
  only the properties every kind shares.
- **FR-002**: Redoing the creation, or the copy, of an entity MUST restore that
  entity's full authored state. No authored property may be replaced by a default
  as a result of a restore.
- **FR-003**: An actor's collider-overlay state MUST be part of what a recorded
  operation carries, so it round-trips through deletion and undo, and through
  duplication and redo, and is never silently reset by a restore. Changing the
  overlay MUST NOT push an undo step of its own — it is an editor visualisation
  preference, not a scene edit.
- **FR-004**: A trigger's authored shape, interactable flag, prompt, and enabled
  state MUST round-trip exactly as they do today.
- **FR-005**: A waypoint's link MUST survive the deletion and undo of the
  waypoint; MUST never resolve to a waypoint other than the one it was made to;
  and MUST remain scoped to the waypoint's own zone. A duplicate inherits the
  link like every other property (FR-013). (Preserves `specs/028-waypoint-chains`
  FR-002, FR-010, and FR-012; deliberately reverses 028's duplicate assumption.)
- **FR-006**: Each entity kind MUST have its own handling for being created,
  removed, and copied, and that handling MUST record the kind's full authored
  state. There MUST be no kind-agnostic path that creates, removes, or copies an
  entity while recording less than its kind's authored state. Adding an authored
  property MUST fail to build until its create/remove/copy/restore handling exists;
  adding an entity kind MUST leave an unhandled case that the compiler reports rather
  than a silent no-op.
- **FR-007**: Recorded operations MUST NOT carry live rendering or physics
  resources. A restore MUST rebuild those resources rather than reuse recorded
  ones.
- **FR-008**: Runtime state — state recomputed while playing rather than authored
  — MUST NOT be restored from a recorded operation.
- **FR-009**: Every existing editor operation MUST keep its current behaviour and
  its current undo granularity: selection, transform, nudge, rename, duplicate,
  delete, link/unlink, trigger property edits, environment swap, and zone switch.
  In particular, a continuous transform gesture MUST remain one undo step.
- **FR-010**: Each entity kind MUST keep its own separate storage and processing.
  The change MUST NOT introduce a single shared entity representation, and MUST
  NOT add a per-operation check of which kind an entity is on any per-frame path.
- **FR-011**: An entity's authored state MUST remain in-memory zone data; how
  zones are persisted is unchanged and out of scope.
- **FR-012**: The feature MUST NOT change physics, rendering, or gameplay
  behaviour. It is an editor-model change.
- **FR-013**: A duplicate MUST carry every property of the original — including
  references to other entities — so that a copied waypoint links to the same
  successor as its original. Runtime state MUST NOT be inherited.
- **FR-014**: A waypoint's link MUST be part of the waypoint's own authored state,
  so it is carried by whichever operation records the waypoint — a deletion, a
  copy, or a restore — and lives and dies with it. A waypoint has at most one
  link: it either points at one other waypoint or ends a chain. The zone MUST NOT
  keep a separate structure recording which waypoint points at which. A link whose target
  is gone is left as it is and simply stops resolving; nothing rewrites or clears
  it.
- **FR-015**: A kind-specific property whose change is an undoable edit MUST be
  recorded through that kind's own family of operations — not by a command of its
  own outside the family, and not by a command shared with kinds that lack the
  property. A property every kind shares (its name, its transform) may stay shared.
  A kind-specific property that is deliberately not an undoable edit (the actor's
  overlay, FR-003) is outside this rule.

### Key Entities *(include if feature involves data)*

- **Entity kind**: one of the game's entity types — actor, placed prop,
  environment part, waypoint, trigger — each with its own properties and its own
  separate storage. The set of kinds is small and stable, and this feature does
  not merge them.
- **Authored state**: the properties of an entity of a given kind that the editor
  can set, and that must therefore survive undo, redo, duplication, and deletion.
  The shared part (name, transform, editor flags) plus the kind's own properties. A
  waypoint's link is authored state under this definition.
- **Runtime state**: properties of an entity that are recomputed while playing and
  are never authored. Never recorded, never restored.
- **Undo step**: one user action maps to exactly one undoable and redoable step,
  with continuous gestures collapsed into one.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For every entity kind, setting all of that kind's editable
  properties to non-default values, deleting the entity, and undoing restores all
  of them, with zero properties reset (100%).
- **SC-002**: Repeating delete → undo → redo → undo 50 times leaves the entity's
  properties identical at every step where it is present, with no failure or drift
  (100%).
- **SC-003**: Repeated create/undo cycles do not accumulate rendering or physics
  resources: after the cycles, live resource counts match the pre-cycle baseline.
- **SC-004**: Adding an authored property to an entity kind without handling its
  round-trip fails the build, rather than compiling and silently resetting the
  property on restore; adding an entity kind without its handling is reported by the
  compiler as an unhandled case (100%).
- **SC-005**: The existing test suite passes, and every existing editor gesture
  still undoes as exactly one step (100%).
- **SC-006**: No gameplay, physics, or rendering behaviour changes: the existing
  behaviour tests pass unmodified, and the project's frame-rate target is
  unaffected by the change.
- **SC-007**: A waypoint chain deleted mid-way and undone is restored in full,
  100% of the time, including links that ran through the deleted waypoint.
- **SC-008**: Per-waypoint link state never outlives its waypoint: after repeated
  create → link → delete cycles, no residual link state remains in the zone and no
  operation can be affected by a deleted waypoint's old link (0 orphans).

## Assumptions

- **The "user" is the developer authoring in the editor**, as in the other editor
  specs; the outcomes above are editor-visible behaviour.
- **Separate storage per entity kind is deliberate and stays.** Entities of
  different kinds live in their own collections rather than one collection of a
  single tagged entity type, precisely so that the per-frame systems never check
  which kind an entity is. This feature works *within* that decision rather than
  revisiting it, which is why per-kind command handling is the right shape here:
  it matches how the entity model is already built.
- **The authoring surface does not change.** The Outliner, Inspector, viewport,
  and their gestures keep their current behaviour; only what a mutation records
  changes.
- **An entity's authored state is split from its runtime state per kind**, and
  that split is the kind's own business. A trigger's "triggered" flag is runtime;
  everything the inspector exposes for it is authored.
- **The collider overlay is recorded actor state, but not an undoable edit.** It
  belongs to the actor, so whatever operation removes or copies the actor carries
  it and a restore returns it; but toggling it is a visualisation preference, not a
  scene edit, so it pushes no undo step of its own. This is a deliberate exception
  to the editor's "undo everywhere" principle, recorded here so that a missing undo
  step for the toggle is not mistaken for the silent loss this feature exists to
  fix.
- **Undo history stays one stack per zone**, as recorded in `docs/ARCHITECTURE.md`;
  ordering across a zone switch is unchanged.
- **A copy carries every property of the original, references included.** A
  copied reference whose target is later deleted simply goes stale — a state the
  model already treats as valid, since it stops resolving and can never alias
  whoever takes the slot — so there is no reason to exclude references from the
  rule. Only runtime state is excluded.
- **This supersedes the link-storage decision in `specs/028-waypoint-chains`
  (research Decision 1).** That spec put a waypoint's link in a zone-level table
  because the shared record could not carry it; with per-kind records it belongs
  on the waypoint, and the zone's link table is removed entirely. 028's observable
  requirements — a link survives delete/undo,
  never aliases another waypoint, and never crosses zones — are preserved
  unchanged, and this spec's US2 is their regression test. **One of 028's
  assumptions is deliberately reversed:** a duplicate now inherits the link, so
  copying a linked waypoint branches the chain at the same successor instead of
  starting a new one. 028 rejected that on the grounds that it silently creates a
  second chain through the same successor, but a shared successor is a state 028
  already allows, and a copied reference is safe. 028's own artifacts are left
  unchanged as historical records; where they and this spec disagree, this spec
  governs.
- **This generalises the approach already taken for triggers, and removes the
  shared record.** `029-triggers` needed a kind-specific command set because a
  trigger's payload is richer than the shared record; after this feature that is no
  longer a special case but the shape every kind uses — for its creation, removal,
  copying, and its own property edits alike. The shared create/destroy/copy record
  is deleted rather than retained, so a new kind cannot be added against a path that
  records less than its full state.
- **A placed prop matching the shared record today is a coincidence of its current
  fields**, not a property of props. Props and environment parts therefore get their
  own family like every other kind rather than keeping the shared path.
- **Scope is all entity kinds together.** Doing this for one kind would leave two
  mechanisms in place and postpone the same work; the plan may still phase the
  rollout.
- **In-memory only.** Undo history does not survive a restart, so there is no
  migration of recorded operations.
- **No new entity kinds** are introduced by this feature, and no changes are made
  to `docs/ARCHITECTURE.md`'s data model beyond what FR-001…FR-015 require.
