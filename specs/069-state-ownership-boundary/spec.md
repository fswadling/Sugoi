# Feature Specification: State Ownership Boundary

**Feature Branch**: `069-state-ownership-boundary`

**Created**: 2026-10-07

**Status**: Draft

**Input**: User description: "Do we have a clear distinction between what is supposed to go into `SharedState` and what goes into `GameplayState`? ... `ZoneRegistry` and `Physics` feel more like they belong in `GameplayState`."

## Origin

The two state containers grew without a stated rule for what goes where.

`SharedState` (`state/SharedState.fs`) is documented as *"cross-screen state owned by
ScreenManager"*, but its members are not one kind of thing. `Gpu`, `Assets` and `Physics` are
grouped as engine services; `Cast` (the cast binding), `ZoneRegistry` and `Advents` are grouped
as *"project state that belongs to no zone"*. The comment justifying the latter says: *"the undo
stacks are per zone, so anything that must outlive every zone lives here (spec 034)."* That
reason is **dead**: `specs/048-unified-undo-stack` replaced the per-zone stacks with one history
on the gameplay state, and `docs/EDITOR_DESIGN.md` (D-022) records outright that *"that reason is
gone"*. So at least two members are placed by a comment that no longer holds.

A review of the actual readers shows the members split cleanly along a different line than
"cross-screen":

- `Physics` (`Physics.World`) is read only on the gameplay path — the play chain
  (`screen/GameplayScreen.fs`), the editor's nudge/pick/drag (`editor/Editor.fs`), spawning
  (`editor/Spawning.fs`), selection (`editor/Selection.fs`), the asset browser, every `Zone.*`
  operation, and the shutdown sweep in `Engine.fs` (which touches it only to release the
  gameplay zones). Nothing else reads it. It is shared across **zones**, not across screens.
- `ZoneRegistry` (the sparse `Place → ZoneTemplate` store, `specs/034`) is read only by the Zone
  Registry panel (`editor/ZoneRegistryPanel.fs`) and by the cue's zone ops through
  `GameplayState.presentZone`/`populateZone`. Nothing outside the gameplay screen and its editor
  layer reads it.
- `Cast` (the slot → actor-asset binding) and `Advents` (the named story flags, `specs/055`) are
  read only on the gameplay path **today**, but they are the game's cast and progress
  data — the kind of thing a journal or inventory screen would present.

`GameplayState` already owns the runtime zones — the authored scene — and is the container that
will be persisted (zones are the payload a save writes). So persistence does not separate the two
containers: authored, persisted content already lives on the gameplay state. Its `Cast` field is
the *runtime* cast index (`specs/063`), a different thing from `SharedState`'s cast binding.

This feature gives the boundary a rule and applies it.

## The rule

A member's home is decided by **who may read it**, not by when it is authored and not by whether
it is persisted.

- **`SharedState`** holds state the engine itself, or more than one screen, may read — state that
  is meaningful independently of the gameplay world.
- **`GameplayState`** holds state that only the gameplay screen and its editor layer read — the
  gameplay world and the configuration of that world.

Applied, this moves `ZoneRegistry` and `Physics` onto the gameplay state and leaves `Gpu`,
`Assets`, `Cast` and `Advents` on `SharedState`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Each state container states and obeys one rule (Priority: P1)

A developer opening either state module can read one sentence that says what belongs there, and
every field in the module obeys it. `SharedState` holds only what the engine or another screen
may read; `GameplayState` holds the gameplay world and its configuration.

**Why this priority**: it is the whole point of the change — a rule that makes every future
field's placement a decision rather than a precedent hunt, and that retires the obsolete
per-zone-undo justification.

**Independent Test**: read the two module headers; confirm each states the readership rule;
confirm every field on `SharedState` has a reader outside the gameplay screen/editor or is read
by the engine, and that no field whose only readers are gameplay-scoped remains.

**Acceptance Scenarios**:

1. **Given** the `SharedState` module, **When** its header is read, **Then** it states that it
   holds engine/any-screen state and no longer cites per-zone undo stacks or `spec 034` as the
   reason a member lives there.
2. **Given** any field on `SharedState`, **When** its readers are traced, **Then** it is read by
   the engine or by more than one screen (present or anticipated).

---

### User Story 2 - The zone registry lives beside the zones it registers (Priority: P1)

The registry is the recipe table for the gameplay world's zones, so it moves onto the gameplay
state, next to `Zones`. The Zone Registry panel still registers and instantiates, the cue's zone
ops still bring a registered place into the world, and nothing else about them changes.

**Why this priority**: it is the clearest case — every reader of the registry is the gameplay
screen or its editor layer — and it is the member whose current placement rests on the dead
comment.

**Independent Test**: from a fresh gameplay state, register the active zone against a place, then
drive a cue that instantiates that place, and assert the registration is read and the place is
brought in exactly as before; assert the same via the panel.

**Acceptance Scenarios**:

1. **Given** a gameplay state, **When** the panel registers the active zone against a place,
   **Then** the registration is stored on the gameplay state and read back by the panel.
2. **Given** a registered place, **When** a cue instantiates it, **Then** the place is brought
   into the world exactly as before, reading the recipe from the gameplay state.
3. **Given** the module list, **When** it is compiled, **Then** no new ordering constraint appears
   (the registry already compiles below the gameplay state).

---

### User Story 3 - Physics lives beside the zones it simulates (Priority: P2)

The single physics world is the simulation the gameplay zones' bodies attach to, so it moves onto
the gameplay state, next to `Zones`. Play, the editor's manipulation, and spawning still step and
read the same one world; a zone switch still detaches the outgoing zone's bodies and attaches the
incoming zone's.

**Why this priority**: it is the same readership finding as the registry, but it is the member
whose "service" appearance most plausibly resists the move, so it is stated separately.

**Independent Test**: run the play chain and assert the world is stepped once per play frame;
switch zones and assert the outgoing zone's bodies are detached and the incoming zone's attached,
exactly as before; assert the shutdown sweep releases the gameplay zones through the same world.

**Acceptance Scenarios**:

1. **Given** a playing session, **When** a frame advances, **Then** one world is stepped, read
   from the gameplay state.
2. **Given** two present places, **When** the active place changes, **Then** the outgoing zone's
   bodies are detached and the incoming zone's attached, as before.
3. **Given** the engine's shutdown, **When** zones are released, **Then** they are released
   through the gameplay state's world.

---

### User Story 4 - Cast and advent flags stay shared (Priority: P3)

A member moves only if its only readers are gameplay-scoped. The cast binding and the advent flags
are the game's cast and progress data, which a journal or inventory screen would read, so they
stay on `SharedState` — even though today the gameplay screen is their only reader.

**Why this priority**: it bounds the change and records why two members that *look* like the
moved ones are deliberately kept, so a later reader does not "finish the job" by moving them too.

**Independent Test**: read `SharedState` and confirm the cast binding and the advent flags are
present, with a comment stating they are shared because a non-gameplay screen may read them.

**Acceptance Scenarios**:

1. **Given** the `SharedState` module, **When** its fields are listed, **Then** the cast binding
   and the advent flags remain, and their rationale is a cross-screen reader rather than zone
   survival.

---

### Edge Cases

- **`GameplayState` is a struct; `SharedState` is a class.** The relocation changes the field's
  container, not its type or lifetime: both are constructed once at startup and held for the
  process, so the single-world and single-registry invariants hold. The registry field need not be
  mutable — its writers mutate the inner dictionary.
- **Screen lifecycle.** `GameplayState` is not cleared on a screen change today
  (`GameplayScreen.activate` re-syncs only the camera aspect; the menu leaves it intact), so
  authored project data is safe on it. If a future feature ever resets the gameplay state on
  entry (a "new game" path), this relocation must be revisited — recorded in Assumptions.
- **The reads that take the world as a value.** Every `Zone.*` operation takes the world as a
  parameter (a zone never holds it, so there is one world for every zone); this is unchanged, only
  the expression naming it changes (`shared.Physics` → the gameplay state's field).
- **A future place- or scenario-selection screen.** The registry's content is "which places have
  an authored zone". If such a screen is ever built, it should be served by the scenario library
  (the documented cross-screen level concept), not by moving the per-place registry back — this is
  a scope bet, not a certainty (recorded in Decisions).
- **A future physics-inspection screen.** Likewise, a *separate* physics debug screen would make
  the world cross-screen; a physics overlay as a mode of the gameplay screen does not.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `SharedState` MUST hold only state the engine or more than one screen may read;
  `GameplayState` MUST hold state that only the gameplay screen and its editor layer read. Each
  module MUST state this rule in its header.
- **FR-002**: The zone registry MUST live on `GameplayState`, co-located with the `Zones` it
  registers.
- **FR-003**: The physics world MUST live on `GameplayState`, co-located with the `Zones` whose
  bodies it holds.
- **FR-004**: The cast binding and the advent flags MUST remain on `SharedState`, justified by a
  non-gameplay reader (a journal or inventory screen), not by outliving a zone.
- **FR-005**: The GPU context and the asset manager MUST remain on `SharedState` — the engine's
  render loop reads the former, and any presenting screen may read the latter.
- **FR-006**: Every reader of the registry (the Zone Registry panel, and the cue's zone ops
  through `presentZone`/`populateZone`) MUST read it from the gameplay state; their observable
  behaviour MUST be unchanged.
- **FR-007**: Every reader of the physics world (the play chain, the editor's nudge/pick/drag,
  spawning, selection, the asset browser, and the shutdown release) MUST read it from the gameplay
  state; their observable behaviour MUST be unchanged.
- **FR-008**: There MUST remain exactly one physics world, shared by every zone: switching the
  active zone MUST detach the outgoing zone's bodies and attach the incoming zone's, and no `Zone`
  MUST hold the world (operations continue to receive it as a parameter).
- **FR-009**: The change MUST NOT introduce a module cycle or a new `.fsproj` ordering
  constraint, and MUST NOT change any `Zone.*` signature.
- **FR-010**: The obsolete rationale — per-zone undo stacks and `spec 034` as the reason a member
  lives on `SharedState` — MUST be removed from the code and from the docs, replaced by the
  readership rule. `docs/PROJECT_STRUCTURE.md`, `docs/ARCHITECTURE.md` (§State management) and the
  affected `docs/EDITOR_DESIGN.md` note MUST agree with the code.
- **FR-011**: The gameplay state MUST NOT be cleared on a screen change (existing behaviour):
  relocated project data MUST survive a trip to the menu and back. This MUST be recorded as an
  assumption so the relocation is revisited if that ever changes.
- **FR-012**: The project MUST build with no new warnings and the existing suite MUST stay green,
  with no device opened.

### Key Entities *(include if data involved)*

- **`SharedState`**: engine/any-screen state — here, the GPU context, the asset manager, the cast
  binding, and the advent flags.
- **`GameplayState`**: the gameplay world and its configuration — here, the zones, the zone
  registry, and the physics world, beside the camera, cue run, UI layer, dialogue, editor state
  and fade.
- **Zone registry**: the sparse `Place → ZoneTemplate` store — the recipe table for the gameplay
  world's zones (moves).
- **Physics world**: the single Jolt world every zone's bodies attach to (moves).
- **Cast binding / advent flags**: the game's cast configuration and story-progress facts (stay).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: `SharedState` contains exactly the GPU context, the asset manager, the cast binding
  and the advent flags — no member whose only readers are the gameplay screen or its editor
  layer.
- **SC-002**: The zone registry and the physics world are read from the gameplay state at every
  call site; no `shared.ZoneRegistry` or `shared.Physics` reference remains.
- **SC-003**: Observable gameplay is unchanged — the existing suite is green with no device
  opened, and the project builds warning-free.
- **SC-004**: Exactly one physics world exists, and a zone switch detaches the outgoing zone's
  bodies and attaches the incoming zone's exactly as before.
- **SC-005**: Both state headers, `PROJECT_STRUCTURE.md`, `ARCHITECTURE.md` §State management and
  the `EDITOR_DESIGN.md` note state the readership rule, and no document still cites per-zone undo
  stacks as the reason a member lives on `SharedState`.

## Assumptions

- **No screen apart from gameplay (and its editor layer) reads the registry or physics, now or in
  the foreseen set.** A future place- or scenario-selection screen would be served by the scenario
  library (the documented cross-screen level concept), not the per-place registry. A future
  *separate* physics-inspection screen would be the one case that reverses this; a physics overlay
  as a mode of the gameplay screen does not.
- **`GameplayState` is app-lifetime.** It is not cleared on a screen change today, which is what
  makes it safe for authored project data. If a future feature resets it on entry, the relocated
  members must be revisited.
- **Persistence is orthogonal to ownership.** Both containers hold persisted and unpersisted data;
  the zones (authored, persisted, on `GameplayState`) are the proof, and are why the move is
  expressed as a readership rule rather than an authored/persisted one.

## Decisions

- **Readership is the test, not authorship or persistence.** A member lives where its readers are:
  `SharedState` for engine/any-screen state, `GameplayState` for gameplay-world state.
- **The cast binding and the advent flags stay shared** because a screen other than the world (a
  journal or inventory) may read them — the same class of anticipated reader, not because they
  outlive a zone.
- **The dead rationale is retired, not restated.** The per-zone undo stacks (and the `spec 034`
  comment built on them) are gone; the headers and docs are corrected to the readership rule.
- **The move is a relocation, not a redesign.** One physics world, one registry, one history,
  unchanged behaviour; only the container and the naming of the field change. No module cycle and
  no `Zone.*` signature changes are introduced.
