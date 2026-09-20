# Feature Specification: A Registered Zone's Placed Props

**Feature Branch**: `035-zone-template-props`

**Created**: 2026-09-20

**Status**: Draft

**Input**: User description: "Next id like to add props to zone templates" — with three decisions
settled in the same conversation: "when instantiating a zone from the zone registry, the environment
asset is being replaced, so the props array should be replaced"; one instantiation stays **one** undo
step; and a place is still **not** registerable without an environment loaded.

**Origin**: `specs/034-zone-registry` deferred this in as many words. Its FR-015 reads "An entry MUST
record the environment and its parts and nothing else: placed props, waypoints, triggers and every
other zone content are out of scope", and its research Decision 13 records *how* they were meant to
arrive: "they join as siblings of the parts". `docs/ARCHITECTURE.md`'s `Zone_Registry` entry has the
same shape, naming a `prop_template` beside the environment asset and the settings. This feature lands
the first of those siblings and leaves the rest where they are.

## User Scenarios & Testing *(mandatory)*

Two parties use this, and they are the same two as `specs/034-zone-registry`: the **author**, who sets
a zone out with props and then files it away under one of the game's places; and the **scene**, which
receives an ordinary zone — a loaded environment and a set of placed props — and knows nothing about
the registry.

### User Story 1 - A registered zone carries its props (Priority: P1)

The author sets a room out — the environment loaded, its parts arranged, and props of their own placed
around it: a lectern, a stack of books, a chair pushed back. She registers the room against a place,
and the entry takes the props with it, in the state she left them: the one she moved, the one she
renamed, the one she deleted, and the one she duplicated.

**Why this priority**: a zone's set dressing is as much a part of "what this place looks like" as the
environment it stands in, and a registration that dropped it would be a registration the author has to
redo by hand every time she uses it. It is also the half that makes the entry's shape complete, which
is what Story 2 reads.

**Independent Test**: load an environment, place several props, move and rename one, delete another and
duplicate a third, register the place, and inspect what was recorded: every remaining prop is there
with its asset, name and placement, the deleted one is absent, and the duplicated one appears twice.

**Acceptance Scenarios**:

1. **Given** an active zone with props placed in it, **When** the author registers a place, **Then**
   the entry records those props alongside the environment asset, its settings and its parts.
2. **Given** props that have been moved, renamed, hidden, deleted or duplicated, **Then** the entry
   records exactly that arrangement — a prop the author deleted is absent, a prop she duplicated is
   present twice, each drawing the prop asset it was placed from.
3. **Given** a zone with no props placed, **Then** the entry records no props, and that is a complete
   registration rather than a partial one.
4. **Given** a registration, **Then** nothing in the scene changes, no undo step is recorded, and the
   zone's own props are untouched.
5. **Given** a registration taken before this feature existed — one whose entry has no props — **Then**
   it is an entry that records no props, which is the same state as scenario 3, and nothing has to
   migrate.

---

### User Story 2 - Instantiating a zone replaces its props too (Priority: P1)

The author instantiates a registered place into a zone that is set out differently — other props, in
other places. The zone afterwards *is* the registered one: the registered environment and its parts,
and the registered props and no others. One Ctrl+Z puts back everything that was there.

**Why this priority**: the two halves are one capability — an entry that records props nobody restores
is a larger file, not a working feature. It is P1 for the same reason Story 1 is: without it nothing
the author does with a registration changes.

**Independent Test**: register a zone with props, change the zone into something else entirely —
a different environment and a different set of props — instantiate the registered place, and confirm
the props are exactly the registered ones; then undo once and confirm both the environment and the
props that were there come back.

**Acceptance Scenarios**:

1. **Given** a registered place, **When** it is instantiated into a zone holding different props,
   **Then** the zone's props afterwards are exactly the entry's, and none of the props it held before
   remain.
2. **Given** an instantiation, **When** the author undoes it once, **Then** the zone holds its previous
   environment *and* its previous props, at the placements and with the names they had.
3. **Given** an instantiation, **Then** it is one step whatever the number of props, and the number of
   undo steps does not grow with them.
4. **Given** an entry recording no props, **When** it is instantiated, **Then** the zone's props are
   cleared rather than left alone, because the entry is the whole of what the zone's props become.
5. **Given** an instantiation, **Then** the entry is unchanged, no other place is affected, and the
   zone's actors, waypoints and triggers are left alone.

---

### Edge Cases

- A prop is not a part, and the two are recorded differently: a part is a piece of the environment
  asset, and a prop is an asset of its own standing in the zone. A prop has no existence outside the
  zone that holds it, where a part has no existence outside its environment.
- A zone may hold props and no environment — that is a scene the author can build — but it cannot be
  *registered*, because registering requires an environment (FR-007). A place's entry always names an
  environment asset, and nothing here changes that.
- A prop the author duplicated draws the same asset twice; the entry records both, and instantiating
  places both.
- An entry whose props name a prop asset that is no longer loaded is a failure to report rather than
  something to skip: the entry names an asset the game no longer has, which is a broken registration.
- Instantiating into a zone with no environment: the same as `specs/034-zone-registry` — the
  environment is loaded and the props are placed, since there is nothing to put back.
- Two places registered from one zone are independent here as they are there: re-registering or
  instantiating one leaves the other's props alone.
- The order props come back in is not part of the record: what a registration is for is which props
  stand where, not the order an author happened to place them in.
- Undoing an instantiation restores the previous props *at their old identities*, so a prop the author
  had selected or was dragging before the instantiation is the same prop afterwards rather than a copy
  of it.
- Props placed after an instantiation are the author's, not the entry's: the entry is a copy, and
  editing the zone afterwards does not reach it (`specs/034-zone-registry` FR-009, unchanged).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An entry MUST record the active zone's placed props alongside the environment asset, the
  environment's settings and its parts.
- **FR-002**: Registering MUST record the props as they stand at that moment, including props that have
  been moved, renamed, hidden, deleted or duplicated.
- **FR-003**: An entry MUST keep its props and its parts distinguishable: each prop records the prop
  asset it draws, and a part records a piece of the environment asset. Neither MUST be recorded as the
  other.
- **FR-004**: Instantiating an entry MUST replace the active zone's placed props with the entry's: the
  zone's props afterwards MUST be exactly those the entry records, and none of the props it held before
  MUST remain.
- **FR-005**: One instantiation MUST be one edit, however many props it carries: a single reversal MUST
  restore the zone's previous environment settings, environment parts and placed props exactly.
- **FR-006**: Instantiating MUST NOT alter the entry, MUST NOT alter any other place, and MUST NOT
  touch the zone's actors, waypoints or triggers — the deferral `specs/034-zone-registry` FR-015
  records stays a deferral for everything but props.
- **FR-007**: A place MUST NOT be registerable without an environment loaded, unchanged from
  `specs/034-zone-registry` FR-006. An entry always names an environment asset.
- **FR-008**: Registration MUST remain a direct write to the registry that records no undo step and
  changes nothing in the scene (`specs/034-zone-registry` FR-009, FR-010, unchanged).
- **FR-009**: Every other behaviour MUST be unchanged: the panel's tree and its two controls, the
  environment's parts, the Asset Browser, the Outliner, the Inspector, and the undo granularity of
  every existing gesture. (Preserves `specs/034-zone-registry` FR-001 to FR-014.)

### Key Entities *(include if feature involves data)*

- **Entry**: what a registered place holds, growing one field — the environment asset, the
  environment's settings, the environment's parts, and now the zone's placed props.
- **Placed prop**: an asset of its own standing in a zone, with a name, a placement, and the prop asset
  it draws. Distinct from a **part**, which is a piece of the environment asset the zone has loaded:
  what `specs/034-zone-registry` called the parts, and what this feature adds beside them.
- **Zone's previous contents**: what an instantiation has to put back — the environment it replaced and
  the props it removed — held by the one edit that replaced them.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A zone with *n* props registers *n* props, over arrangements that include deletions and
  duplicates: props the author placed are recorded, props the author deleted are not (100%).
- **SC-002**: A place registered and then instantiated reproduces its props exactly — same count, same
  assets, same names, same placements (100%).
- **SC-003**: The zone's props after an instantiation are the entry's and nothing else: 0 leftover
  props, including when the entry records none.
- **SC-004**: One instantiation is one undoable step whatever the prop count, and undoing it restores
  the zone's previous environment and previous props exactly (100%).
- **SC-005**: Registering still writes no undo step (0 steps) and changes nothing in the scene
  (0 edits).
- **SC-006**: No existing behaviour changes: the suite from `specs/034-zone-registry` passes with every
  assertion it already made unmodified (100%).

## Assumptions

- **The props are replaced, not added to.** Settled in conversation, and the reason given is the right
  one: instantiating already replaces the environment asset, and the props are the same piece of world
  as that environment — set dressing inside a room that no longer stands there. An additive
  instantiation would also mean that instantiating twice silently doubles every prop, and that the
  claim "the zone is the registered one" would stop being true the second time.
- **One instantiation is one undo step, and that is a requirement rather than a nicety.** Undoing an
  instantiation by hand means re-placing every prop it brought in and re-placing every prop it removed;
  a step per prop would make the gesture the author wants to reverse the most the one that is hardest
  to reverse. This is the constraint that shapes the design more than anything else in the feature.
- **A template is not the whole zone.** It is the environment, its parts, and the zone's placed props.
  Actors, waypoints and triggers stay out, and instantiating leaves them alone: an actor is *who is in
  the room*, which is story, where the environment parts and the set dressing are what the room looks
  like. If a place is ever meant to bring its occupants with it, that is a separate decision with a
  separate question behind it (what happens to the actors already standing there).
- **An environment is still required to register**, so an entry's environment asset stays non-optional
  and a zone with props and no environment cannot be filed away. A props-only place is a coherent idea
  and is deliberately not this feature; the cost of admitting it is a second, thinner kind of entry
  with its own rules, exactly as `specs/034-zone-registry`'s first Assumption argues about a bare
  asset.
- **Props keep the placements they were captured with**, in the zone's own space. Nothing re-anchors
  them to the environment or to the place's name: a registration is a copy of a room, and a copy of a
  room puts the chair back where the chair was.
- **The panel's rows need not change.** A row shows the place's name and the registered asset's name;
  whether it should also show how many props an entry carries is a nicety this feature does not need,
  and the count is visible by instantiating. If it is wanted, it is additive.
- **Waypoints, triggers, splat maps and persistence stay deferred**, as siblings that will join the
  same entry. Nothing here decides their shape, and nothing here decides a file format.
- **An entry whose props name an asset the game no longer has fails loudly** rather than being skipped
  or reduced, in keeping with the rest of the registry: only discovered assets can be placed, so an id
  that names none is a broken registration rather than a state to render around.
