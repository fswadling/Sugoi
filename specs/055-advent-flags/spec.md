# Feature Specification: Adventure Flags and the Advent Panel

**Feature Branch**: `055-advent-flags`

**Created**: 2026-10-03

**Status**: Implemented

**Input**: User description: "Next, lets add the advents and advents panel in fsharp game. Look at odin-game for reference." (revised: milestones are a typed union rather than a string set; the panel docks right.)

## Origin

This is phase 10's remaining data surface — `docs/ROADMAP.md`'s "story primitives". Waypoints
(`specs/026`, `specs/028`), triggers (`specs/029`) and the cast (`specs/032`, `specs/033`) are
already in; the named **adventure flag set** and its panel are what is left. The reference is
`odin-game/src/advent.odin` (the set) plus the "Advents" panel inside `odin-game/src/cue_editor.odin`,
and the closer analogue is `ocaml-game`'s M7 (`lib/core/advent.ml`, `lib/editor/advent_panel.ml`),
which this feature ports.

An *advent* is a story milestone — "the opening is complete", "the player has met Akane". Unlike the
references, whose milestones are free strings, a milestone here is a **closed union**
(`world/Advent.fs`), and the flags are the set of milestones that have happened. A cue or trigger
will branch on membership; the set is the fact those branches read. This feature delivers the union,
the set and the editor panel that toggles it. It deliberately does **not** extend the cue vocabulary:
the advent cue ops (`AddAdvent`, `RemoveAdvent`, `Await`, `If`) and trigger → cue wiring are the next
increment in this phase (`specs/046`'s contract already records the advent ops as deferred).

## Clarifications

### Session 2026-10-03

- Q: What kind of value is a milestone — a free string, as in `odin-game`/`ocaml-game`, or something
  typed? → A: A **closed union**, `type Advent = OpeningComplete | MetAiko | MetAkane | MetShinji |
  MetYuki`. This is the reason `cast/Roster.fs` gives for its cast: a milestone cannot be misspelled,
  matching on one is exhaustive, and nothing about a milestone's identity is decided by a string
  comparison. The game's own declaration grows by adding a case, which the build reports every match
  of.
- Q: What is the flag set's shape? → A: An **immutable ordered** `Set<Advent>`. F#'s `Set` orders
  union values by their case tag, i.e. declaration order, so iteration is deterministic (the panel's
  rows and the suite's assertions both rely on it), and a change produces a new set, which is what
  makes an edit a before/after snapshot.
- Q: Where does the set live — `SharedState` or `GameplayState`? → A: **`SharedState`**, as a
  `mutable` field: project state that belongs to no zone and must outlive a zone switch, as the cast
  binding and the zone registry do (`docs/ARCHITECTURE.md`, `docs/EDITOR_DESIGN.md` D-011).
- Q: Is an edit a direct write or an undoable step? → A: An **undoable step on the one history**
  (`Command.Advent`), handled by `state/History.fs`. The set is not a zone's, so the step rides the
  one history as a state-level step, as the cue's `SetCue` does (`specs/048`, `specs/049`).
- Q: How does the panel edit flags? → A **checklist**: one checkbox per milestone, enumerated from
  the union, ticked while the milestone is set. Ticking sets it and unticking clears it. There is no
  free-text field, because the cases are fixed — which removes the blank-name and duplicate-name
  rules the string version needed.
- Q: Where does the panel dock? → The **right** column, beside the **Inspector** (and the Cue),
  rather than the left, because what it edits is a set of story facts rather than a place in the
  scene. (Revised from the first cut's left dock.)
- Q: Does the flag set persist? → A: **No.** Persistence is phase 15, and nothing loaded names a
  milestone; the set is session state, like the cast binding and the zone registry.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The author records a story milestone by ticking it (Priority: P1)

The editor has an **Advent** panel listing every story milestone. The author ticks one to record that
it has happened. The flag is game state: it belongs to no zone, so switching zones leaves it set.

**Why this priority**: it is the whole point of the feature — the named flag the story will branch on
— and it is a viable MVP on its own.

**Independent Test**: with a headless gameplay state, commit a milestone and assert the shared
state's set contains it; switch zones and assert it is still there. No window, no GPU.

**Acceptance Scenarios**:

1. **Given** an empty flag set, **When** the author ticks `OpeningComplete`, **Then** the set contains
   exactly that milestone and its box reads ticked.
2. **Given** a milestone is set, **When** the active zone is switched, **Then** it is still set — the
   set outlives the zone.
3. **Given** the milestone union, **When** the panel lists it, **Then** every case appears, in
   declaration order, so a milestone cannot be missed.

---

### User Story 2 - The author clears a milestone (Priority: P2)

The author unticks a milestone's checkbox, and it leaves the set.

**Why this priority**: it is the inverse of the MVP and makes the panel a real editor rather than an
append-only log, but a flag set that can only grow is still usable.

**Independent Test**: with a set holding two milestones, clear one and assert only the other remains.

**Acceptance Scenarios**:

1. **Given** a set holding `OpeningComplete` and `MetAkane`, **When** the author unticks
   `OpeningComplete`, **Then** the set contains only `MetAkane`.
2. **Given** the set holds one milestone, **When** it is cleared, **Then** the set is empty and its box
   reads unticked.

---

### User Story 3 - Each toggle is one undoable step on the one history (Priority: P3)

Every tick and untick is a single step on the one undo history (`specs/048`), reachable from `Ctrl+Z`
and the Edit menu exactly as a scene edit is. Undo restores the exact prior set; redo re-applies it.

**Why this priority**: it is the editor-correctness layer over the MVP. Without it the panel still
works; with it, a flag set by mistake is one keypress from gone, on the same timeline as the zone
moves and cue edits around it.

**Independent Test**: set a milestone, undo, assert the set is back to its prior contents and the
redo side holds one step; redo and assert the milestone is back.

**Acceptance Scenarios**:

1. **Given** an empty set, **When** the author ticks a milestone, **Then** the history holds exactly
   one `Advent` step carrying the empty set before and the one-milestone set after.
2. **Given** a milestone was just set, **When** the author undoes, **Then** the set is empty and the
   step is on the redo side; **When** the author redoes, **Then** the milestone is back.
3. **Given** a flag edit and a zone move made after it, **When** the author undoes twice, **Then** the
   zone move reverses first and the flag edit second, reaching the set from before both.

---

### Edge Cases

- **A new milestone case**: it appears in the panel as an unchecked row with no other edit, because
  the panel enumerates the union's cases rather than listing them.
- **Toggling the same box twice**: two steps — a set, then a clear; undo walks back through both.
- **An edit before a zone switch**: the step is on the one history, so it remains reachable after the
  move — the shape `specs/048` guarantees for a step that names no zone.
- **Nothing persisted**: quitting and relaunching starts from the empty set, because no save exists
  yet (phase 15).
- **Many milestones**: the union is hand-authored and small; whole-set before/after snapshots stay
  cheap.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The game MUST represent story milestones as a **closed union** (`world/Advent.fs`) and
  the flags as a set of milestones, iterating deterministically (ordered by declaration).
- **FR-002**: The flag set MUST live on `SharedState` (`mutable Advents`), project state that belongs
  to no zone and survives a zone switch.
- **FR-003**: The editor MUST present an **Advent** panel, docked as a tab beside the **Inspector** in
  the right column.
- **FR-004**: The panel MUST enumerate **every** case of the milestone union, one checkbox each, so a
  milestone cannot be missing from it.
- **FR-005**: Ticking a milestone MUST set it and unticking it MUST clear it.
- **FR-006**: Each box MUST read the milestone's current membership in the flag set.
- **FR-007**: Each accepted toggle MUST be **one undoable step** on the one history: a
  `Command.Advent` carrying the set **before** the change and **after** it, applied and reversed by
  `state/History.fs` (the set is not a zone's; the step is state-level, like `SetCue`).
- **FR-008**: The milestone union MUST be declared once, in `world/Advent.fs`; the panel MUST NOT list
  the cases again (it enumerates them), so a new case needs no panel edit.
- **FR-009**: Undoing a step MUST restore the exact `before` set and redoing MUST restore the exact
  `after` set.
- **FR-010**: The flag set MUST NOT be persisted (phase 15); nothing about it is written to disk.
- **FR-011**: The feature MUST NOT change the cue's vocabulary: no `AddAdvent`/`RemoveAdvent`/
  `Await`/`If` case is added here (`specs/046`'s contract keeps those deferred).

### Key Entities *(include if feature involves data)*

- **`Advent`** (`world/Advent.fs`): the milestone union — a closed set of cases — plus `all` and
  `name`. The flags are a plain `Set<Advent>`, manipulated with the standard `Set` functions. It
  depends on FSharp.Core (`Set`, and reflection for `all`) and nothing else, so it loads on its own.
- **`SharedState.Advents`** (`state/SharedState.fs`): the mutable field holding the flag set, added
  beside `Cast` and `ZoneRegistry`.
- **`Command.Advent`** (`state/Command.fs`): the state-level step — `Previous`/`Next`, the two set
  values.
- **`AdventPanel`** (`editor/AdventPanel.fs`): the panel — the milestone checklist and the write path.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The Advent panel lists every milestone, and the author ticks one to record it; it
  remains set across a zone switch. (FR-002, FR-003, FR-004, FR-005, FR-006)
- **SC-002**: The author clears a milestone by unticking it, and the panel reflects it. (FR-005,
  FR-006)
- **SC-003**: A toggle is one undo step; `Ctrl+Z` restores the set from before the edit and `Ctrl+Y`
  re-applies it, on the same history as a zone move. (FR-007, FR-009)
- **SC-004**: Adding a case to the union makes it appear in the panel with no other edit. (FR-008)
- **SC-005**: Both projects build with **0 warnings** and the full headless suite passes, with the new
  union, set and history cases among them.

## Assumptions

- **The milestone set is the game's declaration.** The starter cases (`OpeningComplete`, `MetAiko`,
  `MetAkane`, `MetShinji`, `MetYuki`) are grounded in the existing roster and are expected to grow;
  adding a case is one edit in `world/Advent.fs`.
- **No persistence.** The flag set is session state; phase 15 adds saving, and no loaded data names a
  milestone, so nothing needs migrating.
- **No consumer yet.** The advent cue ops and trigger → cue wiring are the next increment of phase 10;
  the flag set is delivered first because it is the data those consumers read.
- **The set is small.** It is hand-authored and read at a branch, so a whole-set before/after snapshot
  is the natural payload.
- **The dock layout is native.** `native/cimgui_sdl3.cpp`'s `cimgui_setup_dock_layout` docks the
  `"Advent"` window to the right node and `libcimgui.so` is rebuilt with `native/build.sh`; the window
  floats at a default position until that rebuild lands, because layout persistence is deliberately
  disabled (`docs/RENDERING_NOTES.md`).
