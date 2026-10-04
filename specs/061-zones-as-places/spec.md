# Feature Specification: Zones as Places

**Feature Branch**: `061-zones-as-places`

**Created**: 2026-10-04

**Status**: Draft

**Input**: User description: "Lets get started with spec A" — the first step of a larger decision: cue and world addressing should be by closed, authored identity, not by free strings, so that cues can safely reach actors and zones.

## Origin

A runtime zone is identified by a **free string**. `GameplayState.Zones` is a
`Dictionary<string, Zone>`, `GameplayState.ActiveZone` is a `string`, the editor's Outliner creates a zone
by typing a name into a text field (`state/GameplayState.fs`, `editor/Outliner.fs`,
`state/EditorState.fs`'s `NewZoneName`), and the zone steps and the cue's zone op carry strings
(`Command.ZoneMove/ZoneAdd/ZoneDelete`, `Cue.SwitchZone`). Names like `"main"` and `"classroom"` are
conventional by habit only: nothing checks that a name corresponds to a zone the game declares.

Meanwhile the **authored** set of the game's zones already exists, as a closed union: `ZoneRegistry.Place`
(`world/ZoneRegistry.fs`), `ArtRoom of Period | Island`, "the game's zones and what each one can look
like". The registry is a sparse map from a place to the zone template registered there
(`specs/034`). So the runtime identity (a string) and the authored identity (a `Place`) are two different
things, and only the string is used at run time.

This feature makes a runtime zone's identity a `Place`. The runtime map is keyed by place, the active zone
is a place, and the editor creates zones by choosing a declared place rather than typing a name — so a zone
that is not a place the game declares cannot exist. This is the foundation of a larger decision: cues and
the world are addressed by closed, authored identity (a `Place` for zones, a `Roster.Character` for actors,
`specs/061`'s successors), so a cue can name *where* and *who* as values the compiler checks rather than as
strings that may typo.

It also draws the line that the registry is **config, not identity**: the `Place` union is the vocabulary,
and `ZoneRegistry` is the sparse store of what to instantiate a place *with*. A place may be present with
no registered template (its environment authored by hand); the registry is consulted only when a place must
be brought into the world from a saved config, which is a later feature.

## Clarifications

### Session 2026-10-04

- Q: Does the editor manage a place's presence, or is every declared place always present? → A: Presence is
  managed — "add" makes a declared place present, "remove" makes a present place absent; a place itself is
  never created or deleted (it is a compile-time constant).
- Q: What happens to a removed zone's environment? → A: If it is loaded on the GPU, removal must unload it;
  an absent place must not stay environment-resident.
- Q: When the active place is made absent, which remaining place becomes active? → A: None — the active place
  cannot be made absent; the author switches to another present place first. This also covers a lone present
  place (it is necessarily active, so it cannot be removed).
- Q: When Instantiate is used on a place, does it make that place active? → A: Yes — it makes the place
  present and active (adding it when absent), then loads its template, as one undoable step.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A zone is one of the game's declared places (Priority: P1)

The editor's **Zones** section lists the places that are currently **present**, with the active one marked. To
add a zone the author chooses a declared place that is not present; a place already present cannot be added
again, and no name can be typed that is not a declared place. The active zone is one of these places.

**Why this priority**: it is the whole identity change, and it is the foundation the rest builds on.

**Independent Test**: create a game state, assert its zones are keyed by places; add a declared place and
confirm it becomes present and active; confirm the same place cannot be added twice; confirm a place the
game does not declare cannot be added at all (it is not offered and cannot be named).

**Acceptance Scenarios**:

1. **Given** a fresh game state, **When** it is created, **Then** its zone map is keyed by places and the
   designated start place is active.
2. **Given** the Zones section, **When** the author opens the add control, **Then** only the declared places
   that are not already present are offered; choosing one makes it present and active.
3. **Given** a place already present, **Then** it cannot be added a second time.
4. **Given** a place with no registered template, **When** it is made present, **Then** it is a valid,
   addressable zone with no environment until one is loaded — registration is not required for presence.
5. **Given** the active place, **When** the author attempts to make it absent, **Then** the control is
   refused — the author must switch to another present place first; with a single present place this means it
   cannot be removed at all.
6. **Given** a declared place with no template yet, **When** the author makes it present, authors an
   environment and props, and activates **Register**, **Then** the setup is saved as that place's template —
   "set it up, then save it to a place" is preserved; only the place's identity was required up front.

---

### User Story 2 - Zone steps and the cue name a place (Priority: P2)

Switching zones, adding a zone and deleting a zone — the editor actions and the cue's zone op — name a
place. Undo and redo of a zone step still put the world back exactly.

**Why this priority**: it is the authoring surface and the cue vocabulary catching up with the identity;
without it the new identity is unreachable.

**Independent Test**: move between two present places and undo/redo, asserting the active place each time;
run a cue carrying a zone op that names a place and assert the active place moved.

**Acceptance Scenarios**:

1. **Given** two present places, **When** the author clicks one to switch, **Then** it becomes active as
   one step on the one history, and undo returns to the previous place.
2. **Given** the cue carries a zone op naming a present place, **When** the cue runs, **Then** that place
   becomes active.
3. **Given** a zone step, **When** it is undone and redone, **Then** the active place and the set of present
   places round-trip exactly as today.

---

### User Story 3 - Nothing else about a zone changes (Priority: P3)

Everything a zone already does is unchanged apart from the type of its identity: physics is attached to the
active zone's bodies, the environment-residency markers in the Zones list still read, zone contents still
capture and restore through the same commands, and the Inspector and Viewport still act on the active zone.

**Why this priority**: it bounds the change — this is an identity refactor, not a behaviour change.

**Independent Test**: the existing suite passes unchanged except where it named zones by string; the
environment-residency markers, capture/restore and active-zone reads behave as before.

**Acceptance Scenarios**:

1. **Given** the active place, **When** the game plays, **Then** the same systems run against the same zone
   as before.
2. **Given** a zone delete and undo, **Then** the zone's contents are restored as today.

---

### Edge Cases

- **A location the game does not declare**: cannot be created in the editor — the vocabulary is closed, so
  a new location is a **code edit** (a new place), after which it is made present, authored, and saved.
  Declare-first is deliberate.
- **A present place with no registered template**: valid and addressable; the registry is orthogonal
  config and its absence is not an error (`specs/034` already treats an unregistered place as normal).
- **A place already present**: not offered by the add control and refused if attempted.
- **The active place made absent**: refused — the active place cannot be made absent, so removal always
  removes an inactive place; the author switches first. This subsumes the last present place (it is always
  active).
- **Removing a zone whose environment is GPU-resident**: the environment must be unloaded too (FR-010); an
  environment shared with a still-present place is not unloaded.
- **An op (cue or otherwise) naming an absent place**: this feature does **not** change the current
  behaviour; making a cue's zone op a no-op when the place is not present is the next feature
  (see Assumptions). Here, a place that is not present is still a valid `Place` value, and the runtime
  decides what to do with it exactly as a string name that is not a key did today.
- **Renaming**: there is no rename — a zone's identity is its place and a place's name is derived from the
  union, so a zone cannot be renamed independently of the place it is.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A runtime zone MUST be identified by a value from the game's declared places (a closed union),
  not by a free string. There MUST be no way to create, address or store a runtime zone under a name that
  is not a declared place.
- **FR-002**: The gameplay state MUST key its **present** zones by place and MUST hold the active zone as a
  place.
- **FR-003**: At most one zone per place may be **present**; a place that is already present MUST NOT be
  offered for adding and MUST be refused if attempted. Presence is managed: the editor MUST let the author
  make a declared place present and make a present place absent — neither creates nor deletes a place.
- **FR-004**: The editor's Zones section MUST list **present** places and offer adding an **absent** declared
  place; it MUST NOT accept a free-text name. The list MUST still mark the active place and still show each
  zone's environment residency (loaded / loading / failed / none).
- **FR-005**: The zone steps (move, add, delete) and the cue's zone op MUST name a place, and undo/redo MUST
  preserve them exactly as the string-keyed steps did.
- **FR-006**: The gameplay state MUST start on a designated **start place** declared with the vocabulary.
- **FR-007**: A place MUST have a renderable name, derived from the union rather than stored beside it, for
  the editor and for diagnostic messages; the runtime zone MUST NOT carry an independent string name.
- **FR-008**: Every behaviour that already reads or writes a zone (physics attach/detach on switch, zone
  capture/restore, environment-residency markers, the Inspector/Viewport/Outliner's reads of the active zone,
  release on shutdown) MUST be unchanged apart from the identity type.
- **FR-009**: The Zone Registry panel MUST act on the place it names: registering captures the active
  place's zone as that place's template, and instantiating makes the named place present and **active**
  (adding it when it is absent) and loads its template into it — presented and undoable as one step
  (present + active + load).
- **FR-010**: Making a place **absent** MUST release that zone's resources and MUST unload its environment
  from the GPU — an absent place MUST NOT remain environment-resident. An environment still needed by a
  present place (shared between places) MUST NOT be unloaded on its account.
- **FR-011**: The active place MUST NOT be made absent by an editor action; the author MUST switch to
  another present place first, so a removal always removes an inactive place. A single present place is
  therefore unremovable.

### Key Entities *(include if data involved)*

- **Place**: one of the game's zones together with one of the variants it declares (`ArtRoom Period`,
  `Island`), a value from a closed union. The authored identity of a zone, and a compile-time constant — it
  is never created or deleted at run time. (Exists today in `ZoneRegistry`; this feature separates the
  vocabulary from the config store.)
- **Present zone** (converse: an **absent** place): a place that currently has a runtime zone in the gameplay
  state's map. Presence is what the editor's add/remove and the cue rule act on.
- **Active place**: which present place the game is simulating.
- **Environment residency**: whether a zone's environment is loaded on the GPU (the existing term,
  `specs/050`/`053`) — distinct from presence.
- **Registry template**: the optional saved config for a place, keyed by place (unchanged; orthogonal to
  presence).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: No free-string zone identity remains in the runtime model, the zone steps, or the cue's zone
  op; a zone is always one of the game's declared places.
- **SC-002**: An author can only create zones that the game declares — the editor offers exactly the
  absent declared places, and a present place cannot be duplicated.
- **SC-003**: Switching, adding and deleting zones, and undoing/redoing those steps, behave exactly as
  before apart from the identity type.
- **SC-004**: A place may be present with no registered template, and this is not an error.
- **SC-006**: Removing a present zone unloads its environment when nothing else needs it.
- **SC-005**: The project builds with no warnings and the suite is green, with no device opened.

## Assumptions

- **The registry is config, not identity.** The `Place` union is the vocabulary; `ZoneRegistry` remains the
  sparse store of what to instantiate a place with. Presence does not require registration.
- **This is identity only.** Bringing an absent place into the world from its registered template
  (*streaming*), and making a cue's zone op a **no-op** when the place is not present, are the **next**
  feature — not this one. This feature keeps the current behaviour when a named place is not present.
- **An actor-addressing feature follows**: with zones addressed by a closed place, actor cue ops name a
  `(place, character)` pair and resolve through that place's present zone; that is `specs/061`'s successor,
  not this feature.
- **Declare-first, no ad-hoc zones (decided).** Every runtime zone is a declared place; the editor has no
  scratch or ad-hoc zone outside the declared set. Adding a new location is a **code edit** (a new place),
  after which it is made present, authored, and saved with **Register**. This is deliberate for a bespoke
  engine: the closed vocabulary is what makes cue addressing checkable, and hard-coding a location is an
  accepted cost.
- **No rename.** A zone is its place; there is no independent zone name to rename.

## Decisions

- **Declare-first, no ad-hoc zones.** A runtime zone is always a declared place; a new location is a code
  edit. The authoring flow is: declare a place → make it present → author it → **Register** to save its
  template. Only a place's *identity* is required up front; setting up and saving its config is unchanged.
- **The Zone Registry panel acts on the place it names.** With a zone and its place the same thing,
  "Register" captures the active place's zone as that place's template, and "Instantiate" makes the named
  place present and active (adding it when absent) and loads its template into it, as one undoable step —
  no more saving one zone's content under another place. (Cue-*driven* streaming remains the follow-up; this
  is the editor control.)
- **A designated start place.** The gameplay state starts on a start place declared with the vocabulary,
  rather than the string `"main"` or an implicit first-declared place.
