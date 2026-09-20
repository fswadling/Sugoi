# Feature Specification: The Zone Registry

**Feature Branch**: `034-zone-registry`

**Created**: 2026-09-20

**Status**: Draft

**Input**: User description: "I need a zone registry ... we will have a du of zones, where we register a
zone asset, as well as all of its parts and props. ... when we instantiate an environment asset
directly, it also adds in all of its parts as well. the editor can then move the individual parts
around and customise them. When the user is done, they can register the zone to the zone registry.
so what gets saved in the zone registry should be the environment asset, the overall environment
settings (there will be more in the future), the individual parts (in the state they are in when
saving to the registry). ... the zone registry should have sub entries just like cast - we should be
able to have something like ClassroomMorning and ClassroomEvening. Unlike cast though, the variants
of zones should not use a shared variant set - each environment can have a different set."

**Origin**: the roadmap's Phase 12, "author zone templates/settings", and the
`Zone_Registry` that `docs/ARCHITECTURE.md` already describes as a distinct authoring structure
whose entries carry an environment asset, settings and prop templates. This feature takes the first
two of those and the environment's parts, and leaves the prop templates and the splat maps where
they are. It also answers the gap `docs/EDITOR_DESIGN.md` records against D-011 — that the registry
is one of the project-scoped surfaces with no undo stack — by not giving it one, for the reason
D-022 gives about the cast binding.

## User Scenarios & Testing *(mandatory)*

Two parties use this: the **author**, who builds a zone in the scene and then files it away under one
of the game's named places; and the **scene**, which receives an ordinary environment when a place is
instantiated, and knows nothing about the registry.

### User Story 1 - The game's places, and what each one can look like (Priority: P1)

The author opens the Zone Registry panel and finds the game's places as a tree: one node per place,
and under each node one row per variant that place declares — Classroom offering Morning and
Evening, Corridor offering Day and Night. The tree is the vocabulary itself rather than a list kept
beside it, so a place or a variant the code declares appears without anyone remembering to add it,
and two places are free to declare entirely different sets.

**Why this priority**: it is the smallest slice that is worth anything — the map of where the game
can happen, and which of those places have been registered. It is also the structure the other two
stories are navigated through, so it is buildable and checkable before either of them exists.

**Independent Test**: open the panel and count the nodes and rows against the vocabulary: every
place appears once, each shows exactly the variants it declares, and a place whose variant set is
larger shows more rows than one whose set is smaller.

**Acceptance Scenarios**:

1. **Given** the declared vocabulary, **When** the panel opens, **Then** every place appears exactly
   once, and each shows one row per variant it declares.
2. **Given** two places declaring different variant sets, **Then** each shows its own set — nothing
   requires a place to declare a variant another place declares.
3. **Given** a place declaring no variants, **Then** it appears with a single row.
4. **Given** a place with nothing registered against it, **Then** it is drawn as unregistered rather
   than omitted.

---

### User Story 2 - Register the zone you have authored (Priority: P1)

The author loads an environment into the active zone, works on it — moves parts about, renames one,
hides another, deletes a third — and then picks the place it belongs to and files it away. The entry
records the asset that is loaded, the environment's own settings, and every part in the state it is
in at that moment.

**Why this priority**: this is the feature's value. Everything before it is navigation and
everything after it is the same data read back.

**Independent Test**: load an environment, customise its parts, register it, then edit the live zone
further and confirm the entry did not change.

**Acceptance Scenarios**:

1. **Given** an active zone with an environment loaded, **When** the author registers it against a
   place, **Then** the entry records that asset, that environment's settings, and its parts.
2. **Given** parts that have been moved, renamed, hidden, deleted or duplicated, **Then** the entry
   records exactly that arrangement — a part the author deleted is absent, a part the author
   duplicated is present twice.
3. **Given** an active zone with no environment loaded, **Then** registering is not offered.
4. **Given** an entry already at that place, **When** the author registers again, **Then** the entry
   is replaced rather than added to: there is one entry per place.
5. **Given** a registration, **Then** the live zone is unchanged — registering files a copy, it does
   not move or alter anything.
6. **Given** a registration, **When** the author edits the live zone afterwards, **Then** the entry
   is unchanged, and the two are independent from then on.
7. **Given** a registration, **Then** no undo step is recorded, and Ctrl+Z does not remove the entry.

---

### User Story 3 - Instantiate a registered zone (Priority: P2)

The author takes a registered place and puts it into the active zone: the environment it names is
loaded, its settings applied, and its parts placed where and how they were registered. Undoing puts
back whatever the zone held before.

**Why this priority**: it is the same data read rather than written, so it adds no new state — but it
is in scope rather than deferred, because nothing else proves that what a registration records is
enough to reconstruct the zone. A registration the author cannot reproduce is a registration that
quietly lost something. It is P2 only because the authoring value above does not depend on it.

**Independent Test**: register a customised zone, change the live zone into something else entirely,
then instantiate the registered place and confirm the previously registered environment comes back
part for part; then undo and confirm the intervening state returns.

**Acceptance Scenarios**:

1. **Given** a registered place, **When** it is instantiated, **Then** the active zone's environment
   is the asset the entry names, with the entry's settings and parts.
2. **Given** an instantiation, **When** the author undoes it, **Then** the zone holds exactly what it
   held before, in one step.
3. **Given** an instantiation, **Then** the entry is unchanged and no other place is affected.
4. **Given** an entry, **When** it is instantiated twice, **Then** the second instantiation produces
   the same environment as the first.

---

### Edge Cases

- A place with no entry is the ordinary state of a place nobody has registered yet, not an error.
- Two places may be registered from the same asset with different part arrangements, and are then
  two independent entries; re-registering one leaves the other alone.
- Two places may hold entries with nothing different between them — there is no rule that entries
  differ.
- A part the author duplicated draws the same piece of the asset twice, and both copies are
  recorded; nothing collapses them.
- An entry with no parts at all is legitimate: every part may have been deleted, or the asset may
  have no parts to begin with.
- The environment's own name is one of the settings and is recorded as it stands; it is not required
  to match the name of the place it is registered under.
- Registering reads the zone that is **active at the moment of the action**, so filing one zone
  while another is active files the active one.
- Deleting the zone an entry was registered from changes nothing: the entry does not reference the
  zone, only the asset and the arrangement.
- The asset behind a registered place can be re-exported. If its geometry is rearranged, the entry's
  record of its parts no longer describes the same pieces of the model; this is a failure to report
  rather than something to draw anyway (see Assumptions).
- Instantiating into a zone that has no environment loaded is the same act as loading one for the
  first time: there is nothing to put back, and undo clears the environment again.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The game's places MUST be a closed vocabulary in which a place is a value rather than a
  name, and each place MUST declare its own set of variants. Two places MUST be able to declare
  different sets, and a place MUST be able to declare none.
- **FR-002**: The panel MUST show every place in the vocabulary, and under each place every variant it
  declares, derived from the vocabulary rather than from a list maintained beside it, so that adding
  a place or a variant needs no change to the panel.
- **FR-003**: The registry MUST hold at most one entry per place. An absent entry means nothing is
  registered there, and that absence MUST NOT be an error.
- **FR-004**: An entry MUST record the environment asset to load, the environment's settings, and the
  state of each of the environment's parts.
- **FR-005**: The panel MUST offer, for each place, an action that registers the active zone's loaded
  environment against that place.
- **FR-006**: The register action MUST NOT be available when the active zone has no environment
  loaded.
- **FR-007**: Registering MUST record the parts as they stand at that moment, including parts that
  have been moved, renamed, hidden, deleted or duplicated.
- **FR-008**: Registering MUST replace any entry already at that place, and MUST NOT alter any other
  entry.
- **FR-009**: Registering MUST NOT change the scene, and MUST NOT record an undo step.
- **FR-010**: The registry MUST outlive every zone: switching the active zone, deleting a zone, or
  editing a zone MUST NOT alter any entry.
- **FR-011**: The panel MUST offer, for each registered place, an action that instantiates its entry
  into the active zone.
- **FR-012**: Instantiating MUST reproduce the entry's environment — the same asset, the same
  settings, and the same parts in the same state.
- **FR-013**: Instantiating MUST be a single scene edit that reverses in one step, restoring whatever
  the zone held before.
- **FR-014**: Instantiating MUST NOT alter the entry, and MUST NOT alter any other place.
- **FR-015**: An entry MUST record the environment and its parts and nothing else: placed props,
  waypoints, triggers and every other zone content are out of scope, and MUST NOT be part of an
  entry. (Deferral, not a gap: `docs/ARCHITECTURE.md`'s prop templates and splat maps join as
  siblings of the parts when they land.)
- **FR-016**: Every existing behaviour MUST be unchanged — loading an environment from the Asset
  Browser, the Outliner's zones, the Cast panel, the Inspector, and the undo granularity of every
  existing gesture. (Preserves `specs/032-cast-binding` FR-007 to FR-011 and `specs/033-cast-spawn`
  FR-008.)

### Key Entities *(include if feature involves data)*

- **Place**: one of the game's places together with one of the variants that place declares — a
  classroom in the morning. It is the unit an entry is registered under, and it is a value from a
  closed vocabulary rather than a string, so a place or a variant cannot be misspelled and cannot
  fail to be reported when the vocabulary grows.
- **Zone vocabulary**: the closed set of the game's places, each declaring its own variants. The
  variants are the place's own rather than a set shared across places: a corridor's set has nothing
  to do with a classroom's.
- **Zone entry**: what a registered place holds — the asset, the settings, and the parts. A copy
  taken at a moment rather than a link to the zone it was taken from.
- **Environment settings**: the loaded environment's own properties rather than a part's — its name,
  its placement and its orientation. More will join these; they are grouped so that adding one is a
  change in one place.
- **Part state**: one piece of the environment's geometry as the author left it — which piece of the
  asset it draws, its name, its placement, and whether it is shown.
- **Active zone's environment**: the source a registration reads — whatever environment the active
  zone has loaded at the moment the register action is taken.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every place in the vocabulary, and every variant each place declares, appears exactly
  once in the panel, with nothing missing and nothing invented (100%).
- **SC-002**: A place registered and then instantiated reproduces its environment exactly — same
  asset, same settings, same parts in the same state (100%).
- **SC-003**: A zone with *n* parts registers *n* parts, counted over arrangements that include
  deletions and duplicates: parts the author added are recorded, parts the author deleted are not
  (100%).
- **SC-004**: One instantiation is one undoable step, and undoing it restores the zone's previous
  environment exactly (100%).
- **SC-005**: Registering writes no undo step (0 steps), and leaves the scene unchanged (0 edits).
- **SC-006**: Editing, switching, or deleting a zone changes no entry (0 changes).
- **SC-007**: Two places registered from one authored zone are independent: re-registering,
  instantiating, or editing one changes nothing about the other (0 changes).
- **SC-008**: No existing behaviour changes: the existing suite passes unmodified (100%).

## Assumptions

- **Registration is the only way an entry comes into being.** There is no separate control that
  binds an asset to a place with no parts. For the cast binding, assigning was the only way an asset
  could reach a slot, because nothing else could supply one; a zone's asset arrives with the
  environment the author loads and customises, so the capture is the natural act, and a bare asset
  with no arrangement would be a second, thinner kind of entry with its own rules about how it
  differs. If pre-assigning an asset to a place turns out to be wanted, it is an addition rather
  than a rewrite.
- **An entry records each part's own piece of the asset, not its position in the asset's list.** The
  alternative — matching a registered part to the asset's parts by name — would let a re-export
  propagate, but could not represent a part the author deleted, which is one of the things a
  registration is for. The cost is stated plainly: an asset re-exported with its geometry
  rearranged invalidates what a previously registered place says about its parts, and that is
  reported rather than drawn.
- **Instantiation targets the active zone.** A zone holds one environment, so "which zone" is not a
  question this feature needs a rule for, and the Asset Browser's environment load is the precedent
  for replacing one.
- **The entry is not undoable and not zone-scoped.** It outlives every zone, and the undo stacks are
  per zone, swapped on a switch so an edit's undo applies to the zone it was recorded in, so a
  registration parked in one zone's stack would be unreachable from every other and discarded when
  that zone is deleted. Assigning a model to a cast slot is a direct write for the same reason
  (D-022); `docs/EDITOR_DESIGN.md`'s D-011 note that this surface has no stack is the gap being left
  open deliberately, along with the other project-scoped surfaces.
- **Instantiating is an ordinary scene edit** and goes through the command system, which already
  carries a whole environment for exactly this kind of replacement. So the two halves of this
  feature behave differently on purpose, as the two halves of the cast panel do.
- **The registry lives in memory.** Persisting it with the rest of the project is the persistence
  phase's work; nothing here decides a file format.
- **Props, waypoints, triggers and splat maps are deferred.** They are the `prop_template` and the
  rest of `docs/ARCHITECTURE.md`'s `Zone_Registry` entry, and they join the entry's shape later as
  siblings of the parts rather than as a redesign.
- **The panel docks as a tab beside the Outliner and Cast**, as `docs/EDITOR_DESIGN.md`'s Workspace
  table and its sub-editor list already specify, and is shown by default.
- **A place's name and the environment's name are unrelated.** Registering a classroom's asset under
  a place called something else is allowed; the two names answer different questions.
- **Nothing requires a registered entry to be unique or complete.** Two places can hold copies of one
  arrangement, and a place can be registered with a single part; the vocabulary says what places can
  be registered, not what a good one looks like.
