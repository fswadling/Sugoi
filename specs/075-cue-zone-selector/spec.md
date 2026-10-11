# Feature Specification: An Explicit Zone on Every Entity Cue Op

**Feature Branch**: `075-cue-zone-selector`

**Created**: 2026-10-11

**Status**: Draft

**Input**: User description: "Several cue ops still do not take a zone param and implicitly treat the current zone as implicit. All cue ops that need to get an entity and update it should have the zone as one of the parameters. I want to select a zone in the various cue editors; it should default to the current zone, but I should be able to select another."

## Origin

A cue op that names an entity by a string does not say **which zone** that entity is in; it treats the
current zone as implicit. Two different rules hide behind that:

- The **actor ops** (`RemoveActor`, `Animate`, `CrossFade`, `Morph`, `CrossMorph`, `MoveActor`,
  `RotateActor`, `EnablePlayer`, `DisablePlayer`) resolve their actor by a **global scan of the present
  places** (`GameplayState.tryActorByName`), taking the first match anywhere (`specs/072`, `specs/073`).
- The **camera ops** (`SetCamera`, `FlyCamera`) resolve their waypoint in the **active zone** only.

Three more groups of ops already name a place themselves and are **not** part of this change: the zone ops
(`SetZone`, `AddZone`, `RemoveZone`, `PreloadZone`), `AddActor` (place + waypoint), and the trigger
toggles (`EnableTrigger`/`DisableTrigger`).

Making the zone explicit has two effects. First, an op's target becomes unambiguous — an author who wants
`Animate` to drive an actor in `Island` says so, rather than relying on that actor being the first one
found. Second, the cue editor gains the missing selector: today its actor chooser offers only the active
zone's actors and its waypoint chooser only the active zone's waypoints, so an op targeting another zone
cannot be authored against its contents. Once the op carries a **place**, each op's editor shows a zone
selector (defaulting to the current zone) that the op's entity choosers read from.

This supersedes `specs/072`/`specs/073`'s "resolve by a scan over the present places" for the actor ops
that name a zone: resolution becomes per the op's named place. The two physics-driven motion ops
(`MoveActor`/`RotateActor`) do **not** name a zone — they act on the **active zone** and are unchanged in
that respect. It also changes the camera ops from active-zone-only to their named place.

## Clarifications

### Session 2026-10-11

- Q: Does choosing a zone change what the cue does at run time, or only which entities the editor offers?
  → A: **Run-time meaning** — the zone becomes an explicit cue-op parameter, so an op names the zone it
  acts on. (This supersedes an earlier "editor-only" answer.)
- Q: Should the selector be one per editor or one per op? → A: **Per op** — each op carries and selects
  its own zone, so different ops in one cue can name different zones.
- Q: Which ops gain the zone — all entity ops, or actor ops only? → A: **Every op that names an actor or
  a waypoint** — the actor ops *and* the camera ops (`SetCamera`, `FlyCamera`).
- Q: Should a zone-naming motion op run only while its zone is active, or move an actor in a non-active
  zone? → A: **Active-zone-only** — physics is attached per active zone, so motion runs only while its
  zone is active.
- Q: Should the physics-driven ops carry a zone at all? → A: **No** — `MoveActor` and `RotateActor`
  drop the zone parameter and keep acting on the active zone's actor; every other entity op names its zone.

## User Scenarios & Testing *(mandatory)*

Three parties are affected: the **author**, who names the zone an op acts on; the **cue**, whose ops now
carry that zone; and the **run**, which resolves each op in its named zone.

### User Story 1 - An entity op names the zone it acts on (Priority: P1)

Every cue op that names an actor or a waypoint — except the two physics-driven motion ops — also names
the **place** that entity is in. The op resolves its entity in that place's zone — not by a scan of every
present place, and not the active zone. An op whose name matches nothing in its named place performs
nothing, exactly as a miss does today.

**Why this priority**: it is the change — an op's target is unambiguous, and an author can address an
entity in a zone other than the one they are standing in.

**Independent Test**: place two actors of the same name in two zones; author an `Animate` naming the
second zone and assert it drives the actor in the second zone and not the first; author one naming an
absent place and assert it does nothing.

**Acceptance Scenarios**:

1. **Given** an actor in `Island` and an actor in `ArtRoom Morning`, **When** an `Animate` names
   `Island`, **Then** it acts on the actor in `Island`.
2. **Given** an op naming a place that is not present, **When** it is reached, **Then** it performs
   nothing and the run continues.
3. **Given** an op naming a place that holds no entity of that name, **When** it is reached, **Then** it
   performs nothing.
4. **Given** `RemoveActor`, `Animate`, `CrossFade`, `Morph`, `CrossMorph`, `EnablePlayer`,
   `DisablePlayer`, `SetCamera` and `FlyCamera`, **Then** each names a place among its arguments; and
   **Given** `MoveActor`/`RotateActor`, **Then** neither names a place (they act on the active zone).

---

### User Story 2 - Each op's editor shows a zone selector that its entity choosers read (Priority: P1)

When an op is selected, its editor shows a **zone selector** over the closed place set (matching the
existing place combos). It defaults to the active zone, so an author who never touches it sees today's
behaviour. Choosing another zone makes **that
op's** entity choosers — the actor chooser, the waypoint chooser, and the clip/morph choosers (which read
the chosen actor's asset) — offer **that** zone's contents. Changing the zone is an edit to the cue and a
single step on the one undo history.

**Why this priority**: it is the visible fix — the chooser the author uses now follows the op's zone.

**Independent Test**: with two present zones holding different actors, select an actor op, choose the
other zone from its selector, assert its chooser offers exactly that zone's actors, and assert the change
is one undo step that undo reverses.

**Acceptance Scenarios**:

1. **Given** an op selected whose place is the active zone, **When** nothing is chosen from its selector,
   **Then** its entity choosers offer the active zone's contents, exactly as today.
2. **Given** two present zones with different actors, **When** the author sets an op's zone to the second,
   **Then** that op's actor chooser offers the second zone's live actors and not the first's.
3. **Given** an op whose zone is a zone holding the named actor, **When** the author picks a clip or morph,
   **Then** the chooser offers that actor's asset's names.
4. **Given** any change to an op's zone, **Then** it is recorded as one step and undo restores the
   previous zone.
5. **Given** two ops in one cue, **When** each names a different zone, **Then** each op's choosers read its
   own zone, side by side.

---

### User Story 3 - Defaults, in-flight ops, and the two editors (Priority: P2)

A newly-created entity op defaults its zone to the **active zone** (a node added to a queue is a `Wait`
until its kind is changed). An op that has already begun keeps the place it began in, so a zone switch
mid-run does not re-target it. The two cue editors — the Cue panel and the inline trigger-cue editor — remain
independent, as their node selections already are.

**Why this priority**: it makes the default the thing an author expects, and bounds the change for ops
already in flight.

**Independent Test**: add an `Animate` from the kind dropdown while in a zone and assert its place is that
zone; begin a blend, switch zones, and assert the in-flight blend keeps its place; change an op's zone in
one editor and assert the other editor's op is unaffected.

**Acceptance Scenarios**:

1. **Given** the author is in the active zone, **When** a new entity op is created, **Then** its zone is
   the active zone.
2. **Given** an op that has begun (a blend or a motion), **When** the active zone changes, **Then** the
   in-flight op keeps acting on the place it began in.
3. **Given** the Cue panel and the inline trigger editor both open, **When** an op's zone is changed in
   one, **Then** the other editor's ops and choosers are unchanged.

---

### Edge Cases

- **An op that names its own place already** (`SetZone`/`AddZone`/`RemoveZone`/`PreloadZone`, `AddActor`,
  `EnableTrigger`/`DisableTrigger`): unchanged; its place is its subject and its secondary chooser reads
  that place.
- **An op naming a place that is not present**: a no-op at run time (as an absent place is for `AddActor`
  and the trigger toggles).
- **Physics-driven motion**: `MoveActor`/`RotateActor` are the one pair of entity ops with **no** zone
  parameter — they act on the actor of that name in the **active zone** and run only while it is active
  (physics is attached per active zone).
- **A name matching several actors in the named zone**: the first live match in slot order wins,
  deterministically (unchanged).
- **Every op has a place**: the cue union changed, so a cue authored before this feature cannot exist and
  there is no legacy op to migrate.
- **While editing**: cues do not run; only Play advances the cue (unchanged).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each of these cue ops MUST carry an explicit **place** (zone) argument: `RemoveActor`,
  `Animate`, `CrossFade`, `Morph`, `CrossMorph`, `EnablePlayer`, `DisablePlayer`, `SetCamera` and
  `FlyCamera`. (`MoveActor` and `RotateActor` name a waypoint but carry no place — FR-009.)
- **FR-002**: Such an op MUST resolve its actor/waypoint in the **named place's zone** — not by a scan of
  the present places, and not in the active zone.
- **FR-003**: For an **actor op**, a name that matches no live entity in the named place, or a named place
  that is not present, MUST be a **no-op** that lets the run continue — the no-op-on-miss rule is unchanged.
  The **camera ops** are the exception: they keep their `specs/054` convention and **raise** on an
  unresolved name or an absent place.
- **FR-004**: Ops that already name a place (the zone ops, `AddActor`, the trigger toggles) MUST be
  unchanged.
- **FR-005**: Each such op's editor MUST present a **zone selector** over the closed place set (matching the
  existing place combos), and changing it MUST write the op's place as **one step** on the one undo history.
  The entity choosers populate from the chosen place's zone when it is present (FR-006).
- **FR-006**: An op's entity choosers — the actor chooser, the waypoint chooser and the clip/morph choosers
  — MUST read the op's **named place** rather than the active zone.
- **FR-007**: A newly-created entity op MUST default its place to the **active zone** — this covers choosing
  a kind from the dropdown, including for a node just added to a queue (Add Child adds a `Wait`, whose kind
  the author then changes).
- **FR-008**: An op that has already begun MUST keep the place it began in: the in-flight state carries
  the resolved place, so a later zone switch does not re-target an op in progress.
- **FR-009**: `MoveActor` and `RotateActor` MUST NOT carry a zone; they MUST act on the actor of the named
  name in the **active zone** and MUST continue to run only while that zone is active.
- **FR-010**: The two cue editors MUST remain independent: changing an op's zone in one MUST NOT change an
  op in the other, mirroring their independent node selections.
- **FR-011**: Every other behaviour MUST be unchanged: dialogue, zones, triggers and rendering produce
  identical output; the build is warning-free and the suite green, with no device.

### Key Entities *(include if data involved)*

- **Place (zone) argument**: the zone an entity-naming op acts on, carried in the op itself. Authored
  state, part of the cue; it round-trips and is undoable like any other op argument.
- **Entity name**: the actor or waypoint an op names, resolved within the op's place. A label, not an
  identity; the first live match in the place's zone wins.
- **Resolved slot (place + handle)**: what an in-flight op captures when it begins, so it keeps acting on
  the same entity for its whole duration.
- **The editor's zone selector**: the per-op control that writes the place argument and drives that op's
  entity choosers; default is the active zone.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With an actor of one name in each of two present zones, an op naming a zone acts on that
  zone's actor (100%) and never on the other's.
- **SC-002**: Every entity op's editor offers a zone selector, and a newly-created entity op's zone is the
  active zone (100%).
- **SC-003**: Changing an op's zone records exactly **1** undo step, and undo restores the previous zone.
- **SC-004**: An op that has begun keeps acting on its place across a zone switch (100%); a name that
  matches nothing in its place performs nothing (100%).
- **SC-005**: The project builds with no warnings and the suite is green, with no device opened.

## Assumptions

- **Resolution by named place replaces the global scan.** `specs/072`/`specs/073`'s "first live match over
  the present places" no longer applies to the actor ops; each resolves in its named place. The global
  resolver (`GameplayState.tryActorByName`) is removed if it has no remaining caller.
- **The physics ops keep the active zone.** `MoveActor`/`RotateActor` are physics-driven and physics is
  attached per active zone, so they do **not** name a zone: they act on the active zone's actor and are
  the two deliberate exceptions to "every entity op names its zone". Making motion run in a non-active
  zone is out of scope.
- **Camera ops read the named zone's waypoints.** This is a behaviour change from active-zone-only; the
  camera itself remains the screen's, unchanged.
- **The selector is the existing place control.** The op's zone is written through the same place combo the
  zone ops already use; there is no separate transient "viewed zone" state.
- **Superseded.** `specs/073-cue-actor-by-name`'s "the active place" clarification and `specs/072`'s global
  scan are withdrawn for the ops listed in FR-001. Their other requirements stand.
