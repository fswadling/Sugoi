# Feature Specification: Trigger Cue

**Feature Branch**: `058-trigger-cue`

**Created**: 2026-10-03

**Status**: Draft

**Input**: User description: "Add a cue editor to the trigger inspector so that when a trigger is triggered, the cue is forked into the running cue."

## Origin

`specs/029-triggers` gave a zone trigger volumes that detect the player, and it **deliberately deferred firing a cue**:
its `FR-010` says a trigger must run no cue, and the two fire hooks (the rising edge on entry, and the interact key)
were to arrive with the cue engine. `specs/046-cue-core` then delivered the engine, and it says in its own assumptions
that "trigger → cue wiring is not here … wiring a placed trigger to a cue is phase 10". `specs/047-cue-panel` built the
one shared cue control (`editor/CueTree.fs`) and said explicitly that "a trigger's cue (phase 10) and a scenario's cue
(phase 11) draw it". `docs/EDITOR_DESIGN.md` names this feature's surface: "A trigger's cue is edited inline in the
Inspector" (phase 10, "trigger→cue wiring inline in the Inspector").

This feature closes that loop. A trigger owns an authored cue; the Inspector shows it as the same tree the Cue panel
uses; and when the trigger fires, that cue is **forked into the screen's running cue** — it runs beside whatever is
already running rather than replacing it (`specs/046` FR-009, the mechanism the cue engine was built around).

The reference (`odin-game`) reached the same design from a scenario indirection it removed: a trigger used to name a
scenario, and `docs/TRIGGERS_CUE_PLAN.md` replaces that with a cue **owned by the trigger** and forked when it fires.
This port has no scenario library yet (`specs/046` lists the scenario library as phase 11), so it adopts the owned cue
directly, with no scenario indirection to remove first.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A trigger fires its cue when the player enters (Priority: P1)

A non-interactable trigger carries a cue. While playing, when the player enters the trigger's volume, that cue runs —
forked into whatever cue is already running, so it runs beside it rather than replacing it. Leaving and entering again
fires it again. A trigger whose cue is empty fires nothing, a disabled trigger never fires, and with no live player
nothing fires.

**Why this priority**: it is the capability the whole feature exists for. A cue editor without a fire is a dead end,
and the fire is testable headlessly before any editor exists.

**Independent Test**: put a cue on a trigger in code, place the player just outside and then just inside the volume,
step the play chain, and assert the screen's cue gained the trigger's cue running beside it — no window, no GPU.

**Acceptance Scenarios**:

1. **Given** a trigger with a cue and no player inside, **When** the player enters the volume, **Then** the trigger's
   cue is forked into the screen's running cue (it runs beside it) and the trigger reports the player inside.
2. **Given** the player inside such a trigger, **When** the game updates again with them still inside, **Then** the cue
   is not forked a second time — one entry, one fire.
3. **Given** a cue already part-way through a wait, **When** the trigger fires, **Then** the running cue keeps its
   remaining time (the fork disturbs nothing) and the trigger's cue runs beside it.
4. **Given** a trigger whose cue is empty, **When** the player enters, **Then** nothing is forked.
5. **Given** a disabled trigger, **When** the player enters its volume, **Then** nothing is forked.
6. **Given** the player leaves and enters again, **When** the game updates, **Then** the trigger's cue is forked again,
   so the same trigger can be re-fired.
7. **Given** no live designated player, **When** the game updates, **Then** no trigger fires anything.

---

### User Story 2 - A trigger's cue is edited inline in the Inspector (Priority: P1)

Selecting a trigger in the Outliner shows its cue in the Inspector as the same tree the Cue panel draws: a row per
node, children indented beneath their parent, and a property editor for the selected node. The author adds, removes,
re-kinds and retunes nodes, and each accepted edit is one undoable step on the one history — the same control, the same
rules, and the same undo as the screen's cue.

**Why this priority**: the authoring half. A trigger's cue must be authorable where the trigger is, or the fire can
only be exercised from code.

**Independent Test**: select a trigger, add an op to its cue through the control's pure functions, and assert the
trigger's authored cue changed as one step that undo restores; assert the rows, the lock rule and the ids are the
control's own.

**Acceptance Scenarios**:

1. **Given** a selected trigger, **When** the Inspector draws, **Then** its cue is shown as the shared tree control
   with the trigger's own selection, and a trigger with an empty cue shows `Fin`.
2. **Given** a selected trigger whose cue node is selected, **When** the author re-kinds it, retunes a number, edits
   its text, adds a child, or removes a node, **Then** the trigger's authored cue changes as exactly one `TriggerSet`
   step, redoable, and the tree shows the change next frame.
3. **Given** an edit to a trigger's authored cue that is already forked into the running cue, **When** the edit is
   committed, **Then** the running copy is undisturbed (a cue value is immutable; a run holds its own).
4. **Given** the active selection changes from one trigger to another, **When** the Inspector draws, **Then** the inline
   editor's node selection and pending text follow the newly selected trigger, and text typed for one trigger is never
   applied to another.
5. **Given** a trigger's cue is being edited, **When** the Cue panel edits the screen's cue in the same frame,
   **Then** the two editors do not share a selection or a text buffer.

---

### User Story 3 - An interactable trigger fires on interaction (Priority: P2)

An interactable trigger does not fire on entry. Instead, while the player is inside it, its prompt is shown (as
`specs/029` US3 already does) and pressing the interact key runs its cue — forked into the running cue, exactly as an
entry fire is. The trigger that fires is the tracked nearest interactable one.

**Why this priority**: it completes the model and matches the reference, but a trigger that fires on entry (US1) is
useful without it. It ranks below US1 and US2 because it needs both the fire and the authored cue to exist.

**Independent Test**: make a trigger interactable with a prompt and a cue, put the player inside, press the interact
key, and assert the cue was forked; assert entering alone did not fire it, and that with nothing tracked the key fires
nothing.

**Acceptance Scenarios**:

1. **Given** an interactable trigger with a cue and the player inside it, **When** the player enters, **Then** its cue
   is not forked (entry alone does not fire an interactable trigger).
2. **Given** the same state, **When** the interact key is pressed, **Then** the tracked trigger's cue is forked into
   the running cue.
3. **Given** the player inside two interactable triggers, **When** the interact key is pressed, **Then** the nearer
   one's cue is the one forked.
4. **Given** no tracked interactable trigger (the player is inside none, or none has a live cue), **When** the interact
   key is pressed, **Then** nothing is forked.
5. **Given** the interact key is held, **When** several frames pass, **Then** the cue is forked once per press, not
   once per frame.

---

### Edge Cases

- **A trigger fired while its authored cue is empty (`Fin`)**: nothing to run, so nothing is forked — not an error, and
  the same as a trigger with no cue.
- **A trigger fired with nothing running**: the arrived cue simply becomes the screen's cue (`specs/046`: `Fin` forked
  into is the cue).
- **A trigger that fires again after the first run finished**: it forks its authored cue afresh; a run consumes
  nothing of the value it came from, so the same trigger is repeatable.
- **Editing a trigger's cue while a copy of it is running**: the run holds its own values and is not disturbed by the
  edit; the edit changes what a *future* fire forks.
- **Deleting a trigger while its cue is selected in the inline editor**: the selection is stale and clears; no cue value
  owns a resource, so nothing needs releasing.
- **Duplicating a trigger**: the copy carries the same authored cue; because a cue is immutable the two may share it.
- **Switching zones**: a trigger's cue is zone data like the trigger, and the screen's cue is the screen's — a zone
  switch leaves the running cue alone and preserves each zone's trigger cues.
- **Entering play while the player already stands inside a trigger**: the entry is a fresh rising edge only if they were
  not already marked inside; a trigger left marked inside across a pause does not re-fire.
- **Two triggers containing the player at once**: both may fire, and both cues run beside the running one.
- **A trigger disabled while the player is inside it**: it clears and fires nothing; re-enabling re-detects them and
  can fire again.
- **An interactable trigger whose prompt is empty**: still interactable and still tracked (the prompt being empty only
  means nothing is shown) — the key still fires its cue.
- **Editing a trigger's cue while playing**: the editor is passive during play, so nothing edits a cue mid-run; the
  value already forked is the one that runs.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A trigger MUST carry its own **authored cue** as part of its authored state, in the same place its shape
  and prompt live; it MUST be zone data like the rest of a trigger, and MUST default to an empty cue.
- **FR-002**: Firing MUST fork the trigger's cue into the screen's running cue — it runs **beside** what is already
  running, never replacing it, and MUST NOT disturb anything already in flight (its timers included). With nothing
  running, the arrived cue becomes the cue (`specs/046` FR-009).
- **FR-003**: A **non-interactable**, enabled trigger MUST fire once when the player **enters** its volume — on the
  rising edge of "player inside" — and MUST NOT fire again while they remain inside. Leaving and re-entering MUST fire
  again.
- **FR-004**: An **interactable** trigger MUST NOT fire on entry; instead its cue MUST fire when the interact key is
  pressed while it is the tracked interactable trigger (the nearest one the player is inside, `specs/029` US3/FR-008).
  The key MUST fire on the press edge, not once per frame while held.
- **FR-005**: A trigger whose authored cue is empty, a disabled trigger, and a trigger with no live designated player
  MUST fire nothing. None of these is an error.
- **FR-006**: Firing MUST be exercisable **headlessly**: the system that detects the player and forks MUST run over a
  zone, a player position and a cue with no window, GPU or device (`specs/046` FR-016).
- **FR-007**: The editor MUST show the **selected trigger's cue** in the Inspector as the shared cue control draws it —
  the same rows, indentation, locking rule and node editor as the Cue panel (`specs/047`), under its own ImGui id
  prefix and with its own selection so the two editors cannot collide.
- **FR-008**: Every accepted edit to a trigger's cue MUST be exactly one `TriggerSet` step on the one undo history,
  redoable, and a trigger's cue MUST ride the existing trigger commands: duplicating a trigger carries its cue, and
  deleting one removes it (`specs/003`/`specs/008`; `specs/029` FR-003).
- **FR-009**: Editing a trigger's **authored** cue MUST NOT disturb a copy of it already forked into the running cue: a
  cue value is immutable and a run holds its own values (`specs/046` FR-001).
- **FR-010**: The inline editor's node selection and its text-field scaffolding MUST follow the selected trigger: text
  typed for one trigger MUST never be applied to another, and the selection MUST clear when the trigger it names is
  gone (the Inspector's existing buffer rule, `docs/EDITOR_DESIGN.md`).
- **FR-011**: The inline editor's cue text MUST be committed through the same field semantics as the Cue panel: one
  edit committed on submit or on leaving the field, and a camera op's waypoint chosen from the active zone's live
  waypoints (`specs/047` FR-006/FR-007).
- **FR-012**: A trigger's cue MUST take part in no physics and name no asset; carrying a cue MUST NOT change a trigger's
  detection, its lack of a collider, or any other entity (`specs/029` FR-011).

### Key Entities *(include if feature involves data)*

- **Trigger's authored cue** (new field on the trigger's authored state, `world/Entity.fs`): the cue a trigger forks
  when it fires. An immutable cue value like the screen's, defaulting to an empty cue; authored, undoable zone data.
- **Screen cue** (existing, `state/GameplayState.fs`): where a fired cue is forked in and runs, beside whatever is
  already running (`specs/046`).
- **Fork** (existing, the cue engine, `world/CueEngine.fs`): the operation that runs an arriving cue beside the running
  one without disturbing it; this feature gives it its first caller from the game.
- **Trigger system** (existing, `world/Systems.fs`): already reports the rising edge and the tracked interactable
  trigger; it is where an entry fire is decided (`specs/029`).
- **Inline cue editor** (new, in the Inspector, `editor/Inspector.fs`, over the existing control
  `editor/CueTree.fs`): the selected trigger's cue, drawn and edited with the trigger's own transient selection and
  text field.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Entering a trigger with a cue forks it into the running cue, and the running cue's remaining time is
  unchanged (0 ms disturbed) — `specs/046` SC-005.
- **SC-002**: One entry produces exactly one fork: standing still inside across many updates never forks again (0
  repeated entry fires), and leaving and re-entering forks once more (100%).
- **SC-003**: A disabled trigger, a trigger with an empty cue, and any update with no live player fork nothing
  (0 occurrences).
- **SC-004**: An interactable trigger is never fired by entry, and fires exactly once per interact press of the tracked
  trigger (0 entry fires, 100% of presses).
- **SC-005**: A developer can author a trigger's cue entirely in the Inspector, and every accepted edit is one undo
  step that redo restores (100%).
- **SC-006**: The trigger fire is demonstrable headlessly, `dotnet build` is clean and `dotnet test` passes with tests
  covering the stories above requiring no GPU.
- **SC-007**: Existing behaviour is unchanged: the trigger detection suite and the cue suite pass, and no other entity
  kind is affected.

## Assumptions

- **A trigger owns its cue, with no scenario indirection.** The reference removed a trigger → scenario → cue hop and
  put the cue on the trigger; this port adopts the owned cue directly because it has no scenario library yet
  (`specs/046`, phase 11). A scenario op inside a trigger's cue would be phase 11's, not this feature's.
- **`firing` means the cue engine's `fork`, not a splice.** The cue model has no join and no splice; an arriving cue
  *runs beside* the running one (`specs/046` FR-009 / the model decision). A trigger's cue is therefore never inserted
  into the running cue and never rewrites it.
- **The cue is the same one type.** There is no separate authored cue kept beside a run (`specs/046`). The trigger
  stores a cue value; forking shares it and the run consumes nothing of it, so the same trigger is repeatable.
- **Entry is the rising edge the trigger system already computes** (`specs/029` FR-007): "entered" is "inside now and
  not inside last update", and remaining inside is not an entry.
- **The interact key is `E`.** No play-mode interact key exists yet; this feature binds `E` for interaction (it is
  unused in play mode — `E` is an editor camera/nudge key only while editing, and edit and play are exclusive). The
  binding is recorded so review can veto it.
- **Only the rising edge and the interact key fire.** Neither a per-frame inside test nor any other trigger condition
  runs a cue; those remain `specs/029`'s detection alone.
- **The fire happens in the play chain, before the cue advances that frame**, following the trigger system's place in
  the chain and the reference's ordering, so a fired cue begins on the following tick.
- **Interaction is always enabled while playing.** There is no cue op that suspends interaction in this cut (the
  reference has one), so no gate is added; play mode itself is the gate (`specs/046` FR-002).
- **No persistence.** Zones and their triggers are in-memory (`specs/029`); nothing about a trigger's cue is written to
  disk (phase 15).
- **The documentation this feature corrects** is expected to move with it: `docs/ARCHITECTURE.md`'s trigger/cue
  relationship, `docs/EDITOR_DESIGN.md` (D-026, the inline trigger cue), and `docs/ROADMAP.md` phase 10.
