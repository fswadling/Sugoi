# Feature Specification: Advents as an Ordered String Set

**Feature Branch**: `082-advent-strings`

**Created**: 2026-10-13

**Status**: Draft

**Input**: User description: "Switch advents from a hard-coded discriminated union to an ordered set of
strings. Use the ordered set available in the Prime library. Change the Advents panel to expose a text box and a
list of the current advents, with the ability to add and remove them. A functional change is that we can no longer
rely on there being a defined vocabulary to build a dropdown from for the various cues that expect an advent — they
will have to take a string, and it is up to the author to check the spelling is correct."

## Origin

`specs/055` made a milestone a **closed union** (`type Advent = OpeningComplete | MetAiko | …`) and rejected the
free-string shape the reference projects (`odin-game`, `ocaml-game`) use, on the grounds that a milestone could
not be misspelled and matching on one was exhaustive. `specs/056` then carried that union into four cue ops
(`AddAdvent`, `RemoveAdvent`, `Await`, `If`). This feature reverses that decision: a milestone becomes a plain
**string**, and the flag set becomes an **ordered set of strings** — the shape the references actually use. The
motivation is authorship: the union is compiled in, so adding a milestone is a code change and the panel can only
ever offer the cases the build knows about; a string set lets the author name milestones in the editor.

This is the same move `specs/074-retire-cast` made for the dialogue speaker, which stopped being the closed
`Character` union and became a string chosen from discovered actor names with a typed fallback. The panel and the
cue chooser follow that precedent.

## Clarifications

### Session 2026-10-13

- Q: What is a milestone now — the closed union or a string? → A: A **plain string**. The union, its enumeration
  (`all`) and its display name (`name`) are removed, and no game code enumerates the vocabulary.
- Q: What is the flag set's shape? → A: An **ordered set of strings** (Prime's `OSet<string>`), iterating in the
  order names were added — the authored order, which is what makes the panel's list stable and readable. F#'s
  `Set<string>` would order alphabetically and lose the authored order, so it is not used.
- Q: How does the panel edit the set? → A single-line **text field** to name a new advent, an **Add** control, and
  a **list** of the current advents with a **Remove** control per row. The old one-checkbox-per-case checklist is
  removed, because there are no fixed cases to list.
- Q: Is a change still an undoable step? → **Yes**, unchanged: a `Command.Advent` carrying the set before and
  after, written by `state/History.fs` as a state-level step, so an add and a remove are both one step on the one
  history.
- Q: How do the cue ops name a milestone? → By **string**. The chooser offers the **current** advent names
  (autocomplete) and also accepts a **typed** name that matches none, so an author may name an advent before it is
  added — the `specs/074` speaker chooser's rule.
- Q: Does typing a new name in a cue op's chooser add it to the set? → **No.** The typed name only names the op; the
  set changes only through the Advent panel. A cue milestone edit stays one `SetCue` step and never emits an advent
  step.
- Q: What milestone does a freshly-created advent cue op carry? → An **empty name**, until the author picks or types
  one. With no vocabulary there is no meaningful default, and an empty name is visibly incomplete rather than
  silently targeting whichever advent happens to be first.
- Q: Does the game start holding the five former union cases? → **No.** The set starts **empty** and the author adds
  every name. The old cases are not seeded: nothing persisted them, seeding them would reinstate the built-in
  vocabulary this feature removes, and a cue that names one still works because it sets membership by string.
- Q: Is misspelling caught anywhere? → **No.** There is no vocabulary to validate against, and no cross-reference
  between a cue's string and the set: a misspelled `Await` simply waits, and an `If` takes its else branch. A
  removal or rename never rewrites a cue. Spelling correctness is the author's responsibility (the accepted trade).
- Q: How is a name stored and matched? → It is **trimmed of surrounding whitespace** before it is stored or
  compared, and then matched **exactly (ordinal, case-sensitive)** — consistent with `specs/074`'s by-name
  addressing. A name that is blank after trimming is rejected; `"MetAiko"` and `"metAiko"` are different advents,
  while `" MetAiko "` is the same advent as `"MetAiko"`.
- Q: Does the flag set persist, and can the chosen set type be serialized? → **No persistence** (phase 15,
  unchanged from `specs/055`). The chosen ordered set is not supported by the project's symbolic-expression
  serializer, which is acceptable while nothing is saved; a future persistence feature serializes the names as a
  plain string list and rebuilds the set in order.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The author names a new advent and records it (Priority: P1)

The editor's **Advent** panel has a text field and an **Add** control. The author types a name — "TheOpeningIsOver"
— presses Add, and the name appears in the panel's list of advents and is now a story fact a cue can branch on.
There is no fixed vocabulary to pick from; the author decides the name. The set belongs to no zone, so switching
zones leaves it in place.

**Why this priority**: it is the change. Naming a milestone by hand, rather than by editing a union in code, is the
whole point, and it is a viable feature on its own — a named fact the story can branch on.

**Independent Test**: with a headless gameplay state and an empty set, add a typed name and assert the shared
state's set contains exactly that name; switch zones and assert it is still there. No window, no GPU.

**Acceptance Scenarios**:

1. **Given** an empty advent set and a typed name, **When** the author adds it, **Then** the set contains exactly
   that name and the name appears in the panel's list.
2. **Given** an advent is recorded, **When** the active zone is switched, **Then** it is still recorded — the set
   outlives the zone.
3. **Given** the author adds several names in an order, **When** the panel lists them, **Then** they appear in the
   order they were added, the same order every time.
4. **Given** the text field is empty (or holds only whitespace), **When** the author tries to add, **Then** nothing
   is added.
5. **Given** a name already in the set, **When** the author adds it again, **Then** the set is unchanged and the
   list shows one row for it.

---

### User Story 2 - The author removes an advent (Priority: P1)

The panel lists the current advents, each row with a **Remove** control. The author removes one and it leaves the
set and the list.

**Why this priority**: it is the inverse of the MVP and makes the panel a real editor rather than an append-only
log. It ranks with US1 because a name typed by mistake is otherwise permanent.

**Independent Test**: with a set holding two names, remove one and assert only the other remains.

**Acceptance Scenarios**:

1. **Given** a set holding "A" and "B", **When** the author removes "A", **Then** the set contains only "B" and the
   list no longer shows "A".
2. **Given** the set holds one name, **When** it is removed, **Then** the set is empty and the list shows none.

---

### User Story 3 - A cue names an advent as a string (Priority: P2)

The four advent cue ops — add, remove, await and if — each name a milestone as a **string**. In the cue panel each
offers the **current** advent names as a filterable list and also accepts a **typed** name that matches none, so an
author may write an op naming an advent before it has been added to the set. A running await is still read-only.

**Why this priority**: it is the consumer half. The engine works once the set exists, but the ops are what make the
milestones useful; it ranks below the set because it depends on it.

**Independent Test**: build a cue whose only op is an add naming a typed string, run it headlessly, and assert the
set now contains that name and the cue is finished in the same step; then assert a cue naming a string that matches
no current advent is still authored without error.

**Acceptance Scenarios**:

1. **Given** the cue panel's kind list, **When** a node is re-kinded to "Add advent"/"Remove advent"/"Await"/"If",
   **Then** it is made with an empty milestone name, and "If" is made with two `Fin` children.
2. **Given** a cue op carrying a milestone, **When** its milestone chooser opens, **Then** it offers the current
   advent names and narrows them as the author types.
3. **Given** the chooser, **When** the author types a name that matches no current advent and accepts it, **Then**
   the op carries that typed name — not an error, and no new advent is added to the set by the act of typing.
4. **Given** a running await, **When** its row is drawn, **Then** it is labelled as the op it came from and is
   read-only.

---

### User Story 4 - Each change is one undoable step on the one history (Priority: P2)

Every add and every remove is a single step on the one undo history, reachable from `Ctrl+Z` and the Edit menu
exactly as a scene edit is. Undo restores the exact prior set; redo re-applies it. A cue's milestone edit is also
one step, as today.

**Why this priority**: it is the editor-correctness layer over the set. Without it the panel still works; with it a
mistyped advent is one keypress from gone, on the same timeline as the zone moves and cue edits around it.

**Independent Test**: add a name, undo, assert the set is back to its prior contents and the redo side holds one
step; redo and assert the name is back.

**Acceptance Scenarios**:

1. **Given** an empty set, **When** the author adds a name, **Then** the history holds exactly one `Advent` step
   carrying the empty set before and the one-name set after.
2. **Given** a name was just added, **When** the author undoes, **Then** the set is empty and the step is on the
   redo side; **When** the author redoes, **Then** the name is back.
3. **Given** an advent edit and a zone move made after it, **When** the author undoes twice, **Then** the zone move
   reverses first and the advent edit second, reaching the set from before both.

---

### Edge Cases

- **A name that is blank after trimming**: not addable — the Add control does nothing.
- **A duplicate name**: adding it changes nothing (the set is a set); the list shows one row.
- **Spacing and case**: surrounding whitespace is trimmed, so `" MetAiko "` and `"MetAiko"` are the same advent;
  a name is then matched **exactly** (ordinal, case-sensitive), as the `specs/074` speaker is, so "MetAiko" and
  "metAiko" are different advents.
- **An empty name on a cue op**: a freshly-authored advent op carries no name until the author picks or types one;
  its label reads the op's verb with no name suffix (`Add`, not `Add: `).
- **A cue names an advent that is not in the set**: legal and not an error — the author may name it before adding
  it. An `Await` for it simply waits; an `If` takes its else branch.
- **Removing an advent a cue awaits**: the cue keeps waiting (membership is re-checked each step; removal is not
  remembered). Nothing detects the dangling reference — the accepted cost of having no vocabulary.
- **Renaming an advent**: there is no rename; it is a remove and an add, and no cue string is rewritten.
- **The starter milestones**: the five names the union used to hold are no longer built in; an author re-adds any
  they want by hand, since nothing persisted them.
- **Many advents**: the set is hand-authored and small; whole-set before/after snapshots stay cheap, as today.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The game MUST represent a story milestone as a plain **string** and the flags as an **ordered set of
  strings** that iterates deterministically in the order names were added.
- **FR-002**: The flag set MUST live on shared, zone-independent state (`SharedState.Advents`), project state that
  survives a zone switch, and MUST start **empty** (the five former union cases are not seeded).
- **FR-003**: The editor MUST present an **Advent** panel, docked beside the Inspector in the right column, with a
  single-line text field to name a new advent and an **Add** control that records the typed name.
- **FR-004**: The panel MUST list the current advents, one row each, each with a **Remove** control that clears that
  name from the set.
- **FR-005**: A typed name MUST be trimmed of surrounding whitespace before it is stored or matched; a name that is
  blank after trimming MUST be rejected; adding a name already present MUST be a no-op; a name MUST be matched
  exactly (ordinal, case-sensitive).
- **FR-006**: The cue ops that name a milestone (`AddAdvent`, `RemoveAdvent`, `Await`, `If`) MUST carry a plain
  string, and no closed milestone type MUST remain in the cue vocabulary. A freshly-created advent op MUST carry an
  empty milestone name until the author chooses or types one.
- **FR-007**: The cue panel's milestone chooser MUST offer the current advent names (filterable) and MUST also
  accept a **typed** name matching none, so an op may name an advent before it is added. Accepting a typed name MUST
  NOT add it to the set.
- **FR-008**: Each accepted add and remove MUST be **one undoable step** — a `Command.Advent` carrying the set
  before the change and after it — applied and reversed by the one history (the set is not a zone's; the step is
  state-level).
- **FR-009**: Undoing a step MUST restore the exact before set, and redoing MUST restore the exact after set,
  including the authored order.
- **FR-010**: The flag set MUST NOT be persisted, and nothing about it is written to disk (phase 15, unchanged from
  `specs/055`).
- **FR-011**: The closed milestone union and its vocabulary (its enumeration and display name) MUST be removed, and
  no game code MUST enumerate a milestone vocabulary.
- **FR-012**: The feature MUST NOT validate a cue's milestone string against the set, and a remove MUST NOT rewrite
  or warn about cue strings — spelling correctness is the author's responsibility.
- **FR-013**: The four cue ops MUST behave as before apart from their milestone being a string: an add/remove is a
  no-op when redundant, an await completes exactly when its name is in the set (re-checked each step), and an `If`
  runs the then-branch when its name is in the set and the else-branch otherwise.
- **FR-014**: The project MUST build with **0 warnings** and the headless suite MUST pass, with the new set, panel,
  and cue cases among them.

### Key Entities *(include if feature involves data)*

- **A milestone**: now a plain string identifier for a story fact ("the opening is complete").
- **The advent set** (`SharedState.Advents`): an ordered set of milestone strings — the facts a cue reads; mutable
  project state written by the one history.
- **`Command.Advent`**: the state-level undo step — the set before and after an add or remove.
- **The Advent panel**: the text field, the list, and the Add/Remove write path.
- **The four cue ops** (`AddAdvent`, `RemoveAdvent`, `Await`, `If`): each now names its milestone as a string.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The author records a story milestone by typing its name and adding it, and it remains recorded across
  a zone switch. (FR-001, FR-002, FR-003)
- **SC-002**: The author removes a milestone from the list, and the set and the list reflect it. (FR-004)
- **SC-003**: Adding a name already present, or a blank name, changes nothing, in 0 cases. (FR-005)
- **SC-004**: A milestone addition or removal is one undo step; `Ctrl+Z` restores the set from before it and
  `Ctrl+Y` re-applies it, on the same history as a zone move. (FR-008, FR-009)
- **SC-005**: A cue names its milestone from the current names or a typed new one — 0 fixed milestone vocabularies
  remain in the editor. (FR-006, FR-007, FR-011)
- **SC-006**: A cue naming an advent not in the set is authored without error and runs to its documented behaviour
  (an await waits; an if takes its else branch). (FR-012, FR-013)
- **SC-007**: The project builds with **0 warnings** and the full headless suite passes. (FR-014)

## Assumptions

- **Ordering is insertion order.** The set iterates in the order names were added (the chosen ordered set preserves
  it; a plain sorted set would not), so the panel's list and the suite's assertions are stable and authored.
- **No persistence.** The set is session state; a future save serializes the names as an ordered string list and
  rebuilds the set, because the chosen ordered set is not supported by the symbolic-expression serializer.
- **No integrity checking.** There is no vocabulary and no cross-reference between cues and the set: a misspelling
  is not caught, an await can wait forever, and a remove does not reach the cues that named it — all accepted, and
  the reason the milestone is a string.
- **No automatic migration.** The union's five starter cases are not seeded; nothing persisted them, so an author
  re-creates any they want.
- **No consumer beyond the four cue ops and the panel.** The narrative subsystem names no milestone, so the change
  does not reach it.
- **The dock layout is unaffected**: the panel's window keeps its name, so the native right-dock needs no change.
- **Documentation** (`docs/ARCHITECTURE.md`, `docs/EDITOR_DESIGN.md`, `docs/PROJECT_STRUCTURE.md`,
  `docs/ROADMAP.md`, `docs/CUE_MODEL_DECISION.md`) is corrected to state the string-set shape rather than the union.
