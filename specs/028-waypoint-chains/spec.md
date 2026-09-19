# Feature Specification: Waypoint Chains

**Feature Branch**: `028-waypoint-chains`

**Created**: 2026-09-19

**Status**: Draft

**Input**: User description: "Link waypoints into chains. A waypoint records at most one next waypoint in its zone, so waypoints form paths. The editor can extend the selected waypoint with a new waypoint at the camera and link them, link the selected waypoint to an existing one, and break a link; each is one undoable step. The viewport draws a connector with a direction arrow for every link so a chain's order is readable, a link to a deleted waypoint simply stops the chain rather than dangling, and following a chain always terminates even if it loops."

**Origin**: roadmap phase 10 ("Story primitives"), the waypoint-chain slice, after
`specs/026-waypoints` gave a zone its waypoints. The shape of the feature follows
the reference project's waypoint paths
(`ocaml-game/lib/core/entity.ml`, `lib/core/zone.ml`, `lib/editor/outliner.ml`,
`lib/editor/viewport.ml`, and its `specs/014-waypoints-triggers-cast`), which
stores each waypoint's next as a slot index plus a generation and offers
"Add Next at Camera" as the authoring gesture.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Build a chain while placing waypoints (Priority: P1)

A developer selects a waypoint, moves the camera to where the character should go
next, and chooses *Add Next at Camera*. A new waypoint appears there, already
facing the way the camera looks, and the selected waypoint is recorded as
pointing to it. The new waypoint becomes the selection, so pressing the action
again walks the chain out one link at a time in front of the camera. One undo
removes the new waypoint and restores the chain end it extended.

**Why this priority**: this is the way a chain is actually authored, and it is
the whole of the reference project's chain-authoring surface. Everything else
here is a refinement of it.

**Independent Test**: select a waypoint, add three next-waypoints at three
camera positions, and confirm the chain follows the order they were placed and
each new waypoint faces the camera direction it was placed with; then undo once
and confirm the last waypoint is gone and the previous end is the chain's end
again.

**Acceptance Scenarios**:

1. **Given** a selected waypoint, **When** the developer chooses *Add Next at
   Camera*, **Then** a new waypoint is created at the camera position facing the
   camera direction, the selected waypoint links to it, and the new waypoint
   becomes the selection.
2. **Given** a chain built by repeated *Add Next at Camera*, **When** the
   developer follows the links, **Then** the order matches the order the
   waypoints were placed.
3. **Given** the action was used, **When** the developer undoes, **Then** the new
   waypoint is gone and the waypoint that linked to it no longer has a next, in a
   single undo step.
4. **Given** the action was undone, **When** the developer redoes, **Then** the
   new waypoint and its link return.
5. **Given** no waypoint is selected, **When** the developer views the waypoint
   section, **Then** the action is not offered.

---

### User Story 2 - Link and unlink waypoints that already exist (Priority: P2)

The developer has placed waypoints and now wants to order them. With one waypoint
selected, they choose another from the list to be its next; the chain now runs
through both. They can also break a link, leaving the waypoint it started from
with no next.

**Why this priority**: the reference project only ever grows a chain by placing a
new waypoint at the camera, so its author cannot join two waypoints that are
already positioned, and cannot break a chain at all. Linking what exists (and
undoing it) is what makes chains usable on real layouts, but the placing flow of
US1 is already useful without it.

**Independent Test**: place three waypoints, link the first to the second by
choosing it from the list, confirm the chain reads first-then-second, then break
the link and confirm the first has no next; undo both and confirm the links
return.

**Acceptance Scenarios**:

1. **Given** two waypoints with one selected, **When** the developer links the
   selected one to the other from the list, **Then** the selected waypoint's next
   is the other waypoint, as one undoable step.
2. **Given** a waypoint that points to another, **When** the developer unlinks
   it, **Then** it has no next, as one undoable step.
3. **Given** a waypoint is selected, **When** the developer views the list,
   **Then** it is not offered as its own next.
4. **Given** a waypoint whose next is another, **When** the developer links it to
   a third, **Then** its next is replaced by the third (one next, not two).
5. **Given** a link was changed or removed, **When** the developer undoes,
   **Then** the previous next (present or absent) is restored exactly.

---

### User Story 3 - See the chain in the viewport (Priority: P3)

Every link is drawn in the viewport as a connector between the two waypoints,
with a direction marker, so the order of a chain is readable without selecting
anything, and the connector follows the waypoints as they are moved.

**Why this priority**: the value of a chain is its order, and links are otherwise
invisible. Linking still works without it (US1/US2), but chains would then be
authored blind.

**Independent Test**: link three waypoints and confirm three connectors appear,
each showing which way the chain runs; move the middle waypoint and confirm its
two connectors follow; select a waypoint and confirm it is still distinguishable
from the connectors.

**Acceptance Scenarios**:

1. **Given** a waypoint that points to another, **When** the viewport renders,
   **Then** a connector is drawn between them with a direction marker showing
   which way the chain runs.
2. **Given** a linked waypoint, **When** the developer moves or rotates either
   end, **Then** the connector follows in the same frame.
3. **Given** a chain, **When** it is drawn, **Then** each link is drawn once, from
   the waypoint that holds the link.
4. **Given** a waypoint with no next, **When** the viewport renders, **Then** no
   connector is drawn for it.
5. **Given** an inactive zone, **When** another zone is active, **Then** that
   zone's connectors are not drawn.

---

### Edge Cases

- A waypoint's next is deleted: the chain simply stops there. Nothing dangles,
  nothing points at the wrong waypoint, and the chain is not silently rewritten.
- A deleted next-waypoint is restored by undo: the chain is whole again,
  including links that ran through the restored waypoint.
- The slot a deleted next-waypoint occupied is reused by a new waypoint: the old
  link does not resolve to the new occupant, so a chain cannot silently acquire a
  different next.
- Self-link: a waypoint cannot be made its own next; the editor does not offer it
  and the model refuses it.
- A chain that loops (A to B to A): allowed, because a loop is a legitimate route
  (a patrol). Nothing here walks a chain, so no behaviour in this feature depends on
  a chain ending; a walker that must terminate, or that must treat a loop as a loop,
  arrives with the systems that move a character along a chain.
- Two waypoints pointing at the same successor (a merge): allowed. A chain is a
  path, and nothing requires joins to be exclusive; a later feature that walks one
  must not assume a tree.
- A link whose target is in another zone cannot exist: the editor offers only the
  active zone's waypoints, and links are zone data.
- Zone switch: chains belong to their zone and survive the switch with the rest of
  its contents; the inactive zone's connectors are not drawn.
- Duplicating a linked waypoint: the copy is a new waypoint; whether it inherits
  the original's link is a decision recorded in the assumptions, not left to
  chance.
- Play mode: a chain is inert data — nothing in the gameplay systems reads it yet,
  and connectors are editor-only, so play mode is unaffected.
- Chains are unaffected by the name, transform, visibility, and selection work
  already done for waypoints: renaming or moving a waypoint does not change its
  links.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A waypoint MUST be able to record at most one next waypoint in the
  same zone. A waypoint with no next is the end of a chain.
- **FR-002**: A link MUST NOT resolve to a waypoint other than the one it was made
  to — not after that waypoint is deleted, and not if another waypoint later
  occupies where it was. Following a chain MUST stop cleanly at such a link
  rather than failing, looping forever, or continuing to some other waypoint.
- **FR-003**: The editor MUST offer extending the selected waypoint with a new
  waypoint placed at the camera, facing the camera's direction, linked as its
  next, and selected afterwards. It MUST be offered only when a waypoint is
  selected.
- **FR-004**: The editor MUST offer linking the selected waypoint to an existing
  waypoint of the active zone, and MUST NOT offer a waypoint as its own next.
- **FR-005**: The editor MUST offer breaking the selected waypoint's link.
- **FR-006**: Every link change — extending, linking, unlinking — MUST be exactly
  one undoable step and MUST be redoable, restoring or removing the waypoint
  involved as well as the link.
- **FR-007**: Undoing the deletion of a waypoint MUST restore the links that ran
  through it, so deleting and undoing a mid-chain waypoint leaves the chain as it
  was.
- **FR-008**: The viewport MUST draw one connector per link, showing the direction
  the chain runs, and MUST update it as the linked waypoints move. A waypoint with
  no next MUST NOT produce a connector.
- **FR-009**: The viewport connector and the link-resolution rule MUST be the only
  places a chain is read in this feature: nothing in the gameplay systems walks a
  chain, and no production code path may depend on a chain being acyclic.
- **FR-010**: Chains MUST be zone data: preserved across a zone switch, and only
  the active zone's links drawn.
- **FR-011**: The feature MUST NOT change the existing behaviour of waypoints or
  of any other entity: name, transform, selection, focus, duplicate, delete, and
  undo MUST keep working as they do now, and a chain MUST NOT affect physics,
  geometry rendering, or the gameplay systems.
- **FR-012**: The editor MUST NOT allow a link to cross zones.

### Key Entities *(include if feature involves data)*

- **Waypoint link**: the record that one waypoint's chain continues at another
  waypoint of the same zone. A waypoint has at most one, and its absence marks the
  end of a chain.
- **Chain (path)**: the sequence of waypoints obtained by following links from a
  waypoint until a link does not resolve. Chains may loop and may share
  successors.
- **Extend action**: the authoring gesture that places a new waypoint at the
  camera and links the selected waypoint to it, so a chain can be walked out in
  front of the camera one link at a time.
- **Connector**: the viewport drawing of one link — a line between the two
  waypoints with a marker showing which way the chain runs.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can build a chain of at least three linked waypoints
  entirely from the editor, and the chain's order matches the order the waypoints
  were placed (100%).
- **SC-002**: Deleting a waypoint that is in the middle of a chain leaves no
  dangling chain — following it stops cleanly — and undoing that deletion restores
  the chain in full (100%).
- **SC-003**: A link never resolves to a waypoint other than the one it was made
  to, across repeated delete/recreate cycles (0 aliasing).
- **SC-004**: Every link action (extend, link, unlink, and the undo of each) is
  exactly one undo step (100%).
- **SC-005**: Every link in the active zone is visible in the viewport with its
  direction discernible, and updates in the same frame the linked waypoints move
  (100%).
- **SC-006**: The gameplay loop keeps the project's target frame rate (60 FPS on
  the target platform) with chains present, and linking and re-linking does not
  accumulate physics bodies or GPU resources.
- **SC-007**: Existing waypoint behaviour is unchanged: the existing test suite
  passes, and no other entity kind is affected.

## Assumptions

- Nothing consumes a chain at runtime yet. As in the reference project, the only
  reader today is the editor (the viewport's connectors and the tests); the story
  systems that move a character along a chain arrive with their own spec, which
  will read the same links.
- **A chain may loop and may merge, and nothing walks one yet.** A loop is a
  legitimate route (a patrol) and two waypoints may share a successor. No code in
  this feature follows a chain, so a walker that must terminate — or that must
  treat a loop as a loop — arrives with the systems that move a character along a
  chain, and its spec owns that behaviour. This feature owes it only the link
  resolution rule (FR-002), which is what makes a loop detectable: every step
  either resolves to a waypoint that exists or ends the chain.
- Chains are authored from the Outliner's waypoint section, where waypoints were
  already added and listed; the viewport draws them but is not a link-editing
  surface (waypoints are still not click-pickable, as `specs/026-waypoints`
  records).
- Duplicating a linked waypoint produces a waypoint with no next: the link
  belongs to the original, and copying it would silently create a second chain
  through the same successor. The copy is a source of new chains, not a
  continuation of one.
- Links are zone-scoped and never cross zones.
- Chains are in-memory only, like the rest of a zone's contents; persistence is a
  later phase.
- The reference project's storage decision — the link held by the waypoint as a
  slot index with the target's generation, so a stale link is detected rather
  than followed — is the origin of FR-002. How this project expresses that is a
  design question for the plan, not a requirement.
