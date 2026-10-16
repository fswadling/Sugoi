# Feature Specification: Scenarios as a String-Keyed Ordered Library

**Feature Branch**: `083-scenario-strings`

**Created**: 2026-10-13

**Status**: Draft

**Input**: User description: "Remove the hard-coded scenario discriminated union and replace it with a
string-keyed library. Right now the scenario set is a fixed union where each entry needs registering to. Instead
I'd like to be able to add and remove scenarios in the Scenario panel with a text box and Add button just like
with advents. I should also be able to delete scenarios, as well as register and fork them as we currently can
do. This leads to functional changes — there is no longer a fixed vocabulary, so cues that refer to scenarios
will refer to a string, and it is up to the author to ensure the strings are correct. Advents are the closest
match in terms of similar behaviour in cues currently. If possible, use an ordered map from Prime."

## Origin

`specs/070-scenario-library` made a scenario a **closed union** (`type Scenario = Intro`) — the vocabulary was
compiled in, and `world/ScenarioRegistry.fs` keyed the captured contents by it. `specs/076` and `specs/078`/`079`
then gave a scenario its narrative entities, its zones' staging and the **fork** that runs it into the scene,
without changing the vocabulary. This feature reverses the closed-union decision: a scenario becomes a plain
**string** and the library becomes an **ordered map keyed by scenario name** — the shape the reference projects
use, and the shape `specs/082-advent-strings` just adopted for milestones.

The motivation is authorship: the union is compiled in, so adding a scenario is a code change and the panel can
only ever offer the cases the build knows about. A string-keyed, author-editable library lets the author name,
add, delete and file scenarios in the editor.

This is the same move `specs/082-advent-strings` made for milestones (a set of strings, with the cue chooser
offering the current names plus a typed fallback) and `specs/074-retire-cast` made for the dialogue speaker
(a string chosen from discovered names with a typed fallback). A scenario differs from a milestone in one way:
it carries **contents** (its zones and its cue), so the library is a **map** keyed by name rather than a set of
names.

## Clarifications

### Session 2026-10-13

- Q: What is a scenario now — the closed union or a string? → A **plain string name**. The union, its
  enumeration (`all`) and its display name (`scenarioName`) are removed, and no game code enumerates a scenario
  vocabulary.
- Q: What shape is the library? → An **ordered map keyed by name** (Prime's ordered persistent map), iterating in
  the order names were added — the authored order, which is what makes the panel's list stable and readable. Each
  key's value is the scenario's recorded contents, or "nothing yet" for a name that has been added but not
  registered. F#'s `Map` would order alphabetically and lose the authored order; a plain dictionary would lose
  both the order and the before/after snapshot, so neither is used.
- Q: How does the panel add and delete scenarios? → A single-line **text field** and an **Add** control name a new
  scenario (the Advent panel's pattern), and each listed row carries a **Delete** control that removes the name
  and any contents filed under it. The old fixed list of declared cases is gone, because there is no vocabulary.
- Q: Is **Add** the same as **Register**? → **No.** Add creates a named scenario (an empty slot); **Register**
  files the current scene (its zones and the screen's cue) under a scenario. The existing Register control is
  kept, and Fork is kept.
- Q: How is **Delete** different from **Clear**? → **Clear** empties a scenario's registered contents but keeps
  its name (the row stays, showing "(none)"); **Delete** removes the name as well, so the row leaves the list.
  Both are kept: Clear is the existing control, Delete is the new one.
- Q: How do the cue ops name a scenario? → By **string**. The chooser offers the **current** scenario names
  (autocomplete) and also accepts a **typed** name that matches none, so an author may write an op naming a
  scenario before it has been added — the `specs/074`/`specs/082` rule.
- Q: Does typing a new name in a cue op's chooser add it to the library? → **No.** The typed name only names the
  op; the library changes only through the Scenario panel. A cue edit stays one `SetCue` step and never emits a
  scenario step.
- Q: What scenario does a freshly-created fork op carry? → An **empty name**, until the author picks or types one.
  With no vocabulary there is no meaningful default, and an empty name is visibly incomplete rather than silently
  targeting whichever scenario happens to be first.
- Q: Does the game start holding the former union case (`Intro`)? → **No.** The library starts **empty** and the
  author adds every scenario. `Intro` is not seeded: nothing persisted it, and seeding it would reinstate the
  built-in vocabulary this feature removes.
- Q: Is misspelling caught anywhere? → **No.** There is no vocabulary to validate against, and no cross-reference
  between a cue's string and the library: a `ForkScenario` naming an absent or unregistered scenario performs
  nothing and reports why, exactly as an unregistered scenario does today. A delete or rename never rewrites a
  cue. Spelling correctness is the author's responsibility (the accepted trade).
- Q: How is a name stored and matched? → It is **trimmed of surrounding whitespace** before it is stored or
  compared, and then matched **exactly (ordinal, case-sensitive)** — consistent with `specs/074`'s by-name
  addressing. A name that is blank after trimming is rejected; `"Intro"` and `"intro"` are different scenarios,
  while `" Intro "` is the same scenario as `"Intro"`.
- Q: Does the library persist, and can the chosen map be serialized? → **No persistence** (phase 15, unchanged
  from `specs/070`). The chosen ordered map is not supported by the project's symbolic-expression serializer,
  which is acceptable while nothing is saved; a future persistence feature serializes the scenarios as an ordered
  list of name-and-contents records and rebuilds the map in order.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The author names a scenario and files the current scene under it (Priority: P1)

The editor's **Scenario** panel has a text field and an **Add** control. The author types a name — "Intro" — and
presses Add; the name appears in the panel's list of scenarios. They then press **Register** on that row and the
current scene — every present zone with its staging, and the screen's cue — is filed under it. There is no fixed
vocabulary to pick from; the author decides the name. The library belongs to the gameplay world, so switching
zones leaves it in place.

**Why this priority**: it is the change. Naming a scenario by hand, rather than by editing a union in code, is the
whole point, and it is a viable feature on its own — a named scenario the author can later fork.

**Independent Test**: with a headless gameplay state and an empty library, add a typed name and assert the
library lists exactly that name; register and assert the name now has contents; switch zones and assert it is
still there. No window, no GPU.

**Acceptance Scenarios**:

1. **Given** an empty library and a typed name, **When** the author adds it, **Then** the library holds exactly
   that name in its list.
2. **Given** a named scenario, **When** the author registers, **Then** the library's entry for it holds every
   present zone and the screen's cue, and the row reports a non-empty capture.
3. **Given** a registered scenario, **When** the active zone is switched, **Then** it is still registered — the
   library outlives the zone.
4. **Given** the author adds several names in an order, **When** the panel lists them, **Then** they appear in
   the order they were added, the same order every time.
5. **Given** the text field is empty (or holds only whitespace), **When** the author tries to add, **Then**
   nothing is added.
6. **Given** a name already in the library, **When** the author adds it again, **Then** the library is unchanged
   and the list shows one row for it.

---

### User Story 2 - The author deletes a scenario, or clears its contents (Priority: P1)

The panel lists the current scenarios, each row with a **Delete** control and a **Clear** control. Delete removes
the name and anything filed under it, so the row leaves the list. Clear empties a registered scenario's contents
but keeps the name, so the row stays and reports "(none)".

**Why this priority**: it is the inverse of the MVP and makes the panel a real editor rather than an append-only
log. It ranks with US1 because a name typed by mistake is otherwise permanent.

**Independent Test**: with a library holding two registered scenarios, delete one and clear the other; assert the
first is gone entirely and the second remains named with no contents.

**Acceptance Scenarios**:

1. **Given** a library holding "A" and "B", **When** the author deletes "A", **Then** "A" leaves the list and the
   library, and "B" is untouched.
2. **Given** a registered scenario, **When** the author clears it, **Then** its contents are gone but its name
   remains and its row reports "(none)".
3. **Given** a library holding one scenario, **When** it is deleted, **Then** the library is empty and the list
   shows none.
4. **Given** a cue names a scenario that is then deleted, **When** the cue runs, **Then** it performs nothing and
   reports why — no error, and the cue is not rewritten.

---

### User Story 3 - The author forks a registered scenario into the scene (Priority: P2)

The panel's **Fork** control runs a registered scenario into the live scene: it ensures the scenario's zones by
name (creating and staging an absent one, restaging a present one), appends the scenario's narrative entities,
and forks the scenario's cue beside the running one — as **one** undoable step. The active place, the camera and
the selection are untouched.

**Why this priority**: it is the payoff of a registered scenario and already exists; it must keep working when the
library becomes name-keyed. It ranks below the add/delete MVP because it depends on a scenario existing.

**Independent Test**: register a scenario carrying an actor and a cue, fork it headlessly, and assert the actor is
appended and the running cue is the fork; undo and assert both are reversed.

**Acceptance Scenarios**:

1. **Given** a registered scenario, **When** the author forks it, **Then** its zones' entities are appended, its
   cue is forked beside the running one, and the active place, camera and selection are unchanged.
2. **Given** a scenario whose target zone lacks room, **When** the author forks it, **Then** nothing changes at
   all and a reason is reported.
3. **Given** a scenario is forked twice, **Then** its entities are appended twice (a fork is deliberately
   non-idempotent).
4. **Given** a scenario that is named but not registered, **When** the author forks it, **Then** Fork is
   unavailable and performs nothing.

---

### User Story 4 - A cue names a scenario as a string (Priority: P2)

The fork cue op names a scenario as a **string**. In the cue panel it offers the **current** scenario names as a
filterable list and also accepts a **typed** name that matches none, so an author may write an op naming a
scenario before it has been added to the library.

**Why this priority**: it is the consumer half. The library works once it exists, but the op is what makes the
scenarios useful; it ranks below the library because it depends on it.

**Independent Test**: build a cue whose only op is a fork naming a typed string that matches no scenario, and
assert it is authored without error and runs as a documented no-op; then register that name and assert the same
op now materialises the scenario.

**Acceptance Scenarios**:

1. **Given** the cue panel's kind list, **When** a node is re-kinded to "Fork scenario", **Then** it is made with
   an empty scenario name.
2. **Given** a fork op carrying a scenario, **When** its scenario chooser opens, **Then** it offers the current
   scenario names and narrows them as the author types.
3. **Given** the chooser, **When** the author types a name that matches no current scenario and accepts it,
   **Then** the op carries that typed name — not an error, and no new scenario is added to the library by the act
   of typing.
4. **Given** a fork op naming an absent or unregistered scenario, **When** it runs, **Then** it performs nothing
   and reports why, and the cue advances past it.

---

### User Story 5 - Each change is one undoable step on the one history (Priority: P3)

Every add, delete, clear and register is a single step on the one undo history, reachable from `Ctrl+Z` and the
Edit menu exactly as a scene edit is. Undo restores the exact prior library; redo re-applies it. A cue's scenario
edit is also one step, as today.

**Why this priority**: it is the editor-correctness layer over the library. Without it the panel still works; with
it a mistyped scenario is one keypress from gone, on the same timeline as the zone moves and cue edits around it.

**Independent Test**: add a name, undo, assert the library is back to its prior contents and the redo side holds
one step; redo and assert the name is back.

**Acceptance Scenarios**:

1. **Given** an empty library, **When** the author adds a name, **Then** the history holds exactly one step
   carrying the empty library before and the one-name library after.
2. **Given** a name was just added, **When** the author undoes, **Then** the library is empty and the step is on
   the redo side; **When** the author redoes, **Then** the name is back.
3. **Given** a library edit and a zone move made after it, **When** the author undoes twice, **Then** the zone
   move reverses first and the library edit second, reaching the library from before both.

---

### Edge Cases

- **A name that is blank after trimming**: not addable — the Add control does nothing.
- **A duplicate name**: adding it changes nothing; the list shows one row. Its registered contents, if any, are
  not cleared by the attempt.
- **Spacing and case**: surrounding whitespace is trimmed, so `" Intro "` and `"Intro"` are the same scenario; a
  name is then matched **exactly** (ordinal, case-sensitive), as the `specs/074` speaker is, so "Intro" and
  "intro" are different scenarios.
- **A named-but-unregistered scenario**: a legal state — the row shows "(none)" and Fork is unavailable until it
  is registered.
- **An empty name on a cue op**: a freshly-authored fork op carries no name until the author picks or types one;
  its label reads the op with no name suffix ("Fork scenario", not "Fork scenario: ").
- **A cue names a scenario that is not in the library**: legal and not an error — the author may name it before
  adding it. The fork performs nothing and reports why.
- **Deleting a scenario a cue forks**: the cue keeps running; the op performs nothing when reached. Nothing
  detects the dangling reference — the accepted cost of having no vocabulary.
- **Renaming a scenario**: there is no rename; it is a delete and an add, and no cue string is rewritten.
- **The former `Intro` scenario**: not built in; an author re-adds and re-registers any they want by hand, since
  nothing persisted it.
- **A large registration**: the library is hand-authored and small; whole-library before/after snapshots stay
  cheap because the library is persistent (a snapshot shares its structure), and registration is off the play path.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The game MUST represent a scenario as a plain **string name** and the library as an **ordered map
  keyed by name** that iterates deterministically in the order names were added, and whose value may be absent
  until the scenario is registered.
- **FR-002**: The library MUST live on the gameplay world, project state that survives a zone switch, and MUST
  start **empty** (the former `Intro` case is not seeded).
- **FR-003**: The editor MUST present a **Scenario** panel with a single-line text field to name a new scenario
  and an **Add** control that records the typed name.
- **FR-004**: The panel MUST list the current scenarios, one row each, each with a **Delete** control that removes
  the name and any contents filed under it.
- **FR-005**: The panel MUST keep its existing **Register** control (file the current scene under a scenario) and
  its **Clear** control (empty a scenario's contents but keep its name), alongside Add, Delete and Fork.
- **FR-006**: The panel MUST keep its existing **Fork** control, which materialises a registered scenario into the
  scene as one undoable step.
- **FR-007**: A typed name MUST be trimmed of surrounding whitespace before it is stored or matched; a name that
  is blank after trimming MUST be rejected; adding a name already present MUST be a no-op; a name MUST be matched
  exactly (ordinal, case-sensitive).
- **FR-008**: The cue op that names a scenario (`ForkScenario`) MUST carry a plain string, and no closed scenario
  type MUST remain in the cue vocabulary. A freshly-created fork op MUST carry an empty scenario name until the
  author chooses or types one.
- **FR-009**: The cue panel's scenario chooser MUST offer the current scenario names (filterable) and MUST also
  accept a **typed** name matching none, so an op may name a scenario before it is added. Accepting a typed name
  MUST NOT add it to the library.
- **FR-010**: Each accepted add, delete, clear and register MUST be **one undoable step** — a state-level command
  carrying the library before the change and after it — applied and reversed by the one history (the library is
  not a zone's).
- **FR-011**: Undoing a step MUST restore the exact before library, and redoing MUST restore the exact after
  library, including the authored order.
- **FR-012**: The closed scenario union and its vocabulary (its enumeration and display name) MUST be removed, and
  no game code MUST enumerate a scenario vocabulary.
- **FR-013**: The feature MUST NOT validate a cue's scenario string against the library, and a delete MUST NOT
  rewrite or warn about cue strings — spelling correctness is the author's responsibility.
- **FR-014**: Forking MUST behave as before apart from the name: it ensures each recorded zone by name (creating
  and staging an absent one, restaging a present one), appends the narrative entities, forks the cue beside the
  running one, is non-idempotent for entities, and refuses, with a report and no change at all, when a target zone
  lacks room. Nothing it does MUST change the active place, the camera or the selection.
- **FR-015**: A fork op naming an absent or unregistered scenario, or one whose target zone lacks room, MUST
  perform nothing and report why, leaving the run to advance past it (the existing behaviour).
- **FR-016**: The library MUST NOT be persisted, and nothing about it is written to disk (phase 15, unchanged
  from `specs/070`). *(Superseded by `specs/095-scenario-persistence`, which persists the library automatically as
  `project.manifest`.)*
- **FR-017**: The project MUST build with **0 warnings** and the headless suite MUST pass, with the new library,
  panel controls and cue case among them.

### Key Entities *(include if feature involves data)*

- **A scenario name**: a plain string identifier chosen by the author ("Intro", "Boss Fight").
- **The scenario library**: an ordered map keyed by scenario name, holding each scenario's recorded contents —
  the scenarios a cue forks; project state written by the one history, on the gameplay world.
- **A scenario's contents**: the zones it is made of (each a ref-free capture of narrative entities and staging)
  together with the cue to run.
- **The state-level command**: the undo step — the library before and after an add, delete, clear or register.
- **The Scenario panel**: the text field, the Add/Delete controls, and the Register/Clear/Fork write paths.
- **The fork cue op** (`ForkScenario`): now names its scenario as a string.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The author names a scenario by typing its name and adding it, registers a scene under it, and it
  remains registered across a zone switch. (FR-001, FR-002, FR-003, FR-005)
- **SC-002**: The author deletes a scenario and it leaves the list and the library, or clears it and its name
  remains with no contents. (FR-004, FR-005)
- **SC-003**: Adding a name already present, or a blank name, changes nothing, in 0 cases. (FR-007)
- **SC-004**: A library change is one undo step; `Ctrl+Z` restores the library from before it and `Ctrl+Y`
  re-applies it, on the same history as a zone move. (FR-010, FR-011)
- **SC-005**: A cue names its scenario from the current names or a typed new one — 0 fixed scenario vocabularies
  remain in the editor. (FR-008, FR-009, FR-012)
- **SC-006**: A cue naming a scenario not in the library is authored without error and runs to its documented
  behaviour (it performs nothing and reports why). (FR-013, FR-015)
- **SC-007**: Forking a registered scenario materialises its entities and forks its cue as one undoable step, and
  a fork that cannot fit changes nothing. (FR-006, FR-014)
- **SC-008**: The project builds with **0 warnings** and the full headless suite passes. (FR-017)

## Assumptions

- **Ordering is insertion order.** The library iterates in the order names were added (the chosen ordered map
  preserves it; a plain sorted map would not), so the panel's list and the suite's assertions are stable and
  authored.
- **No persistence.** The library is session state; a future save serializes an ordered list of name-and-contents
  records and rebuilds the map, because the chosen ordered map is not supported by the symbolic-expression
  serializer.
- **No integrity checking.** There is no vocabulary and no cross-reference between cues and the library: a
  misspelling is not caught, a fork of a missing scenario performs nothing, and a delete does not reach the cues
  that named it — all accepted, and the reason a scenario is a string.
- **No automatic migration.** The former `Intro` case is not seeded; nothing persisted it, so an author
  re-creates any scenarios they want.
- **Ownership is unchanged.** The library stays on the gameplay world (not shared state): as before, only the
  gameplay screen and its editor read it.
- **The dock layout is unaffected**: the panel's window keeps its name, so the native right-dock needs no change.
- **Documentation** (`docs/ARCHITECTURE.md`, `docs/EDITOR_DESIGN.md`, `docs/PROJECT_STRUCTURE.md`,
  `docs/ROADMAP.md`, `docs/CUE_MODEL_DECISION.md`) is corrected to state the string-keyed library rather than the
  union.
