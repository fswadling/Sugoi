# Feature Specification: Automatic Persistence of the Scenario Library

**Feature Branch**: `095-scenario-persistence`

**Created**: 2026-10-16

**Status**: Draft

**Input**: User description: "The next feature I'd like to add is the ability to persist the state of the project.
The way this should work is that I should be able to persist the scenario registry as s syntax using prime. This
should happen automatically and not require a separate save and load commands." Scope confirmed as the scenario
library only; the file lives at the project root; changes are persisted on change (no timer); a missing file is not
an error and a damaged one is reported and leaves an empty library.

## Origin

The scenario library (`world/ScenarioRegistry.fs`, a string-keyed ordered map of scenario name to recorded contents)
is authoring state the author expects to keep across sessions, but nothing writes it to disk. `specs/083` recorded
this as a deliberate deferral — FR-016 ("the library MUST NOT be persisted") — and named the shape the future feature
would take: "a future persistence feature serializes the scenarios as an ordered list of name-and-contents records
and rebuilds the map in order". `docs/ROADMAP.md` phase 15 and the Persistence section of `docs/ARCHITECTURE.md`
also anticipate a save/load of project state.

The reference lineage persisted this state as JSON with a DTO split between the runtime model and a serializable
project record. This project's convention (`AGENTS.md`, the asset manifests) is the opposite: human-authored data is
written in Prime's symbolic-expression syntax, which maps F# records and unions directly, carries named fields and
`;` comments, and needs no separate schema. This feature adopts that convention for project state.

Two of the three things `docs/ARCHITECTURE.md`'s Persistence section lists have since been retired: the cast
(`specs/074-retire-cast`) and the zone registry (`specs/079-scenario-owned-zones`). The scenario library is the only
surviving project state, and a scenario already owns its zones' staging and narrative entities, so the library is
self-contained and is the whole of what this feature persists.

## Clarifications

### Session 2026-10-16

- Q: What is persisted? → **The scenario library only** — every scenario's name, whether it is registered, and a
  registered scenario's zones, entities and cue. The live working scene, the active place, the camera, the running
  cue, the adventure flags and the music mix are **not** written; the retired cast and zone-registry do not return.
  The library is self-contained because a scenario owns its zones.
- Q: What form does the file take? → A single **human-readable s-syntax text file**, written with the project's
  symbolic-expression serializer (Prime's `scstring`) and read with its reader (`scvalue`), matching the asset
  manifests rather than JSON. Records are emitted with named fields (`[<SymbolicExpansion>]`) so the parser is
  robust to reordering, and `;` comments are permitted in the file. The asset-format value types the serializer
  cannot represent directly (`System.Numerics.Vector3`/`Quaternion`) are mirrored by small float-field records;
  the ordered map itself is written as an ordered list of name-and-record entries and rebuilt in order on load
  (the same shape `specs/083` predicted, and the reason: the serializer does not support the ordered map directly).
- Q: Where does the file live and what is it called? → At the **project root** — the working directory the game
  runs from, beside the `assets` directory that is scanned relative to it — as a single file (`project.manifest`).
  One file holds the whole library; there is no per-scenario file and no directory of files.
- Q: When is it written? → **On change, exactly once per change** — no continuous updates, no time-based debounce
  and no periodic timer. The library changes on **Register** (file the current scene under a scenario) and, equally,
  on **Add**, **Clear** and **Delete** (each a `SetScenario` step), and again when any of those steps is **undone or
  redone**. Persistence follows the library *value*: whenever it differs from what was last written, it is written.
- Q: Does undoing a change persist too? → **Yes.** Persistence tracks the library value, not a particular button.
  Deleting a scenario and then undoing the delete must leave the scenario present on disk, so a reload does not lose
  it — otherwise a deleted scenario that was restored by undo would vanish on the next launch.
- Q: When is it read? → **At startup**, before the editor can be used, so the author's scenarios are present from
  the first frame. The load is not a step on the undo history.
- Q: What if the file is absent? → **Not an error.** The library starts empty and nothing is written until the
  first change. A first-run game with no project file behaves exactly as the pre-feature build.
- Q: What if the file is present but damaged (unreadable, unparseable, or an unrecognised version)? → The problem is
  **reported to the error surface** and the library starts **empty**; the damaged file is **left on disk untouched**
  so it is not silently overwritten on startup (it is overwritten only by the author's next real change).
- Q: Can a write leave a half-written or corrupt file? → **No.** Writing is atomic: the content is written to a
  temporary file and then renamed over the project file, so an interrupted write cannot destroy the previous good
  file. (The `.tmp` + rename rule `docs/ARCHITECTURE.md` already states.)
- Q: Does the file carry a version? → **Yes**, a format version, so a later format change can be detected. An
  unrecognised version is treated as a damaged file.
- Q: What should the game do when the project file cannot be **written** (a read-only directory, a full disk, a failed
  rename)? → **Report it and keep running**, skipping that one write and leaving the last good file in place; because
  writes are change-driven, the next change retries. A file-system problem is surfaced, never fatal.
- Q: Which directory is the file resolved against? → **The current working directory** — the project root the author
  launches from, beside the `assets` directory the game already scans relative to it, not the executable's directory.
  All the project's data therefore sits together, and a launch from elsewhere is the same class of setup error as
  missing assets.
- Q: How is a scenario whose captured cue is still **mid-run** handled? → Register **refuses**. A run-form op (a
  `…State` case) is run progress — it carries a live slot handle and engine-space vectors — not authoring, so a cue
  holding one cannot be filed. Detection reuses the predicate the editor already has (`CueTree.executing`, spec 047);
  the **Register control is disabled with a tooltip** saying the cue is still running, so the author sees why before
  attempting it and the library is left unchanged. (The write path keeps a defensive report-and-skip, FR-006, in case
  the invariant is ever violated.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The author's scenarios are there again next time (Priority: P1)

The author names scenarios in the Scenario panel, registers the current scene under them, deletes the ones they do
not want, and closes the game. When they open it again, the Scenario panel lists exactly the scenarios they left,
each registered one still holding its zones, entities and cue, in the same order — without their having pressed
anything to save or load.

**Why this priority**: it is the feature. Authoring that vanishes on exit is not authoring; keeping the library
across sessions is the whole point and is valuable on its own.

**Independent Test**: with a headless gameplay state, build a library (add, register, delete), write it through the
save path to a temporary project file, read it back through the load path, and assert the reloaded library lists the
same scenarios in the same order with the same contents. No window, no GPU.

**Acceptance Scenarios**:

1. **Given** a library with two named scenarios, one registered and one not, **When** it is saved and the process
   is started again, **Then** the panel lists both in the same order, the registered one with its contents and the
   other still nameless-of-contents.
2. **Given** a registered scenario with several zones, each with actors, waypoint chains and triggers (one trigger
   carrying a cue), **When** it round-trips through save and load, **Then** every zone's staging and every entity
   is present, and each waypoint's chain link and each trigger's cue are preserved.
3. **Given** the author closes the game with no project file having ever existed, **When** they reopen it, **Then**
   the library is empty and no error is shown.

---

### User Story 2 - Every change to the library is captured, including undo (Priority: P2)

The author deletes a scenario, realizes it was a mistake, and presses Ctrl+Z. The scenario returns to the panel — and
it is also back on disk, so the next launch still has it. Likewise, adding, clearing or registering a scenario, and
undoing or redoing any of those, is reflected in the file.

**Why this priority**: a save that only follows one control (Register) would be wrong the moment the author undoes
anything — a restored scenario would silently disappear on the next launch. Capturing every change is what makes the
persistence trustworthy.

**Independent Test**: with a headless gameplay state, delete a scenario from a saved library, assert the file no
longer holds it; undo through the one history, assert the file holds it again; redo, assert it is gone again.

**Acceptance Scenarios**:

1. **Given** a saved library, **When** the author deletes a scenario, **Then** the file no longer holds that name.
2. **Given** the delete just described, **When** the author undoes it, **Then** the file holds the scenario again,
   with its contents.
3. **Given** the author clears a registered scenario, **When** the change is written, **Then** the file keeps the
   name and records it as having no contents.
4. **Given** an add that is refused because the name is blank or already present, **When** the author tries it,
   **Then** the library value is unchanged and no write occurs.

---

### User Story 3 - A missing or damaged project file is survivable (Priority: P3)

The author removes the project file, or it becomes unreadable (hand-edit gone wrong, truncated by a bad shutdown,
written by a newer version of the game). The game still starts, reports the problem if there is one, and lets the
author work with an empty library rather than crashing or destroying what remains.

**Why this priority**: the failure paths matter for a file the author can see and touch, but they are secondary to
the happy path and to change tracking.

**Independent Test**: start with no file and assert an empty library and no error; start with a deliberately
malformed file and assert the error surface reports it, the library is empty, and the malformed file is still on
disk unchanged.

**Acceptance Scenarios**:

1. **Given** no project file, **When** the game starts, **Then** the library is empty, no error is reported, and
   nothing is written until the first change.
2. **Given** a project file that cannot be parsed, **When** the game starts, **Then** the error surface reports it,
   the library is empty, and the file on disk is unchanged.
3. **Given** a project file whose version is not recognised, **When** the game starts, **Then** it is handled as a
   damaged file (reported, empty library, file left in place).
4. **Given** a change is being written, **When** the process is interrupted mid-write, **Then** the previous good
   file is still readable (the write went to a temporary file and was renamed over).

---

### Edge Cases

- A scenario that is **named but not registered** must survive the round trip and still read as unregistered.
- A **waypoint chain that ends** (`Next` absent) and one that continues must both round-trip without inventing or
  dropping a link.
- A **trigger whose cue does nothing** (`Fin`) and a scenario whose cue is empty must both round-trip.
- A name containing spaces or punctuation must round-trip exactly (a name is matched ordinally after trimming).
- An **empty library** (no scenarios) round-trips to an empty library; the file is still valid.
- Real numbers (positions, orientations, colours, radii) round-trip to the same values, not merely to "close".
- The **working scene** is not affected by a load: whatever zones are present when the game starts stay present.
- A scenario whose registered cue names a zone the current session never visits must still round-trip (nothing
  is resolved against the live world during a load).
- A cue that is **still mid-run** (a `…State` op present) cannot be filed by Register; the control is disabled with an
  explanatory tooltip and the library is unchanged. A cue that is merely *non-empty and unplayed* is not mid-run and
  files normally.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The scenario library MUST be written to disk and read back **automatically**; there MUST be no
  explicit save command and no explicit load command.
- **FR-002**: The persisted form MUST be **human-readable text** in the project's symbolic-expression syntax —
  records with named fields, `;` comments permitted — not JSON and not a binary format.
- **FR-003**: The persisted form MUST capture, for each scenario, its **name**, whether it is **registered**, and,
  when registered, each of its zones' **staging** and **narrative entities** — actors, waypoints (with their chains)
  and triggers (each with its authored cue) — together with the scenario's own **cue**.
- **FR-004**: The persisted form MUST preserve the **authored order** of scenario names (an ordered list of entries,
  rebuilt into the ordered library on load), so the panel's list is stable across a restart.
- **FR-005**: The library MUST be written **whenever its value changes** — an add, delete, clear or register, and an
  undo or redo of any of those — and **once per change**; there MUST be no time-based debounce and no periodic timer,
  and detecting the change MUST NOT require walking the library.
- **FR-006**: Writing MUST be **atomic**: content is written to a temporary file and then renamed over the project
  file, so an interrupted write cannot corrupt or truncate the previous good file. A write that fails (a read-only
  directory, a full disk, a failed rename) MUST be **reported** and **skipped**, leaving the last good file in place,
  and the next change MUST retry; a write failure MUST NOT itself be treated as fatal.
- **FR-007**: On startup, if the project file is present and readable, the game MUST load it and install the library
  **before the editor is usable**, so the author's scenarios are present from the first frame. The file MUST be
  resolved against the **launch working directory** (the current directory the game is started from), beside the
  `assets` directory the game already scans — not against the executable's directory.
- **FR-008**: A **missing** project file MUST NOT be an error: the library starts empty and nothing is written until
  the first change, so a first run behaves exactly as the pre-feature build.
- **FR-009**: A project file that cannot be read or parsed, or whose version is not understood, MUST be **reported
  to the error surface**, MUST leave the library **empty**, and MUST be **left on disk unchanged** (not overwritten
  or deleted) until the author's next real change.
- **FR-010**: The persisted file MUST carry a **format version**, so a later change to the format can be detected;
  an unrecognised version MUST be handled as a damaged file (FR-009).
- **FR-011**: Reloading a saved library MUST reconstruct a library **equal, scenario for scenario, to the one that
  was saved** — including a named-but-unregistered scenario, a waypoint chain that ends, and an empty cue.
- **FR-012**: Persistence MUST NOT be a step on the undo history: a load is not a step and a write is not a step, and
  the history's before/after snapshots are neither produced nor consumed by saving or loading.
- **FR-013**: Persistence MUST NOT write anything the library does not hold: the live working scene, the active
  place, the camera, the running cue, the adventure flags and the music mix MUST be untouched by a save and unchanged
  by a load.
- **FR-014**: Persistence MUST be limited to the scenario library; the **retired cast and zone-registry** MUST NOT be
  persisted or reintroduced by this feature.
- **FR-015**: A change that leaves the library value unchanged — a refused duplicate add, a blank name, a clear of
  an already-unregistered scenario — MUST NOT produce a write.
- **FR-016**: The project MUST build with **0 warnings** and the headless suite MUST pass, with round-trip,
  file-handling (missing / damaged / atomic / write-failure) and mid-run-guard tests among them.
- **FR-017**: Documentation MUST be corrected to match: the Persistence section of `docs/ARCHITECTURE.md` and
  `docs/ROADMAP.md` phase 15 MUST state the s-syntax project file and the library-only scope, in place of the
  JSON DTO and the retired cast and zone-registry.
- **FR-018**: A scenario whose captured cue holds a run-form op (a `…State` case) MUST NOT be filed: the Register
  control MUST be **disabled**, with a tooltip explaining that the cue is still running, and the library MUST be left
  unchanged. Detecting this MUST reuse the existing executing-cue predicate (spec 047) rather than introduce a second
  notion of "in progress". The write path keeps its defensive report-and-skip (FR-006) in case the invariant is ever
  violated.

### Key Entities *(include if feature involves data)*

- **The project file**: a single s-syntax text file at the project root holding the persisted scenario library, with
  a format version; written atomically, read at startup.
- **The scenario library**: the ordered, name-keyed map of scenario name to recorded contents (or "named but not
  registered") — the one thing persisted.
- **A scenario's recorded contents**: the zones it is made of (each a ref-free capture — staging and narrative
  entities, with waypoint chains by index) together with its cue.
- **The persisted record tree**: the serializable mirror of the library's authored value — records with named
  fields, with the value types the serializer cannot represent directly flattened to plain numbers, and the ordered
  library flattened to an ordered list of entries.
- **The load result**: the installed library, or the empty library with a report when the file is missing or damaged.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A library the author builds — adding, registering and deleting scenarios — is present, in the same
  order with the same contents, the next time the game starts, with 0 save or load actions by the author.
  (FR-001, FR-003, FR-004, FR-007)
- **SC-002**: Every add, delete, clear and register, and every undo or redo of one, is reflected on disk, so a
  reload after any of them yields the library as it stood — including a scenario restored by undo. (FR-005)
- **SC-003**: In 0 cases does a refused change (duplicate or blank add, clear of an unregistered scenario) produce a
  write. (FR-015)
- **SC-004**: A library round-trips through save and load **equal, scenario for scenario**, including a
  named-but-unregistered scenario, a chain-ending waypoint, and an empty cue. (FR-003, FR-011)
- **SC-005**: Starting with no project file yields an empty library with 0 errors and 0 writes until the first
  change; starting with a damaged file reports the problem, yields an empty library, and leaves the file unchanged.
  (FR-008, FR-009, FR-010)
- **SC-006**: An interrupted write never leaves an unreadable project file — the previous good file is still
  readable. (FR-006)
- **SC-007**: Saving and loading change nothing but the scenario library — the live scene, the active place, the
  camera, the running cue, the adventure flags and the music mix are untouched, and nothing is added to the undo
  history. (FR-012, FR-013)
- **SC-008**: A write that cannot complete (a read-only directory or a full disk) is reported, leaves the last good
  file readable, and loses 0 changes — the next change is written when the problem clears. (FR-006)
- **SC-009**: The project builds with **0 warnings** and the full headless suite passes. (FR-016)
- **SC-010**: A scenario cannot be registered from a cue that is still mid-run — Register is unavailable (with an
  explanation) and 0 registrations occur — while a cue that is only authored, including a non-`Fin` one that has never
  played, registers normally. (FR-018)

## Assumptions

- **The library is the only project state.** Because a scenario owns its zones' staging and entities
  (`specs/079-scenario-owned-zones`) and the cast and zone registry are retired (`specs/074`, `specs/079`), persisting
  the library persists all authoring state the game has. The working scene is deliberately not persisted.
- **Value-following, not control-following.** The library is an immutable value, so "did it change?" is settled by
  comparing the current value to the last-written one; this is what makes undo and redo persist without any special
  handling. No per-button hook exists.
- **The serializer's reach shapes the record tree.** Prime's symbolic serializer maps records, unions, options,
  lists, arrays, maps and its own deque directly, but not `System.Numerics` vectors/quaternions (they would be
  written as unreadable text) nor its own ordered map (unsupported). The record tree therefore flattens those two
  cases at the boundary and encodes everything else as-is. The authored cue round-trips directly, because it is a
  union the serializer handles and it never holds a run-form op — a cue that does is refused at Register (FR-018).
- **One file at the root.** A single `project.manifest` sits in the working directory the game is launched from,
  beside `assets`; the file is not itself an asset and is not found by the asset scan.
- **Human-editable by design.** The file is commented, named-field text the author may read and hand-edit; a
  hand-edit that breaks it is handled as a damaged file rather than being silently repaired.
- **No automatic migration from nothing.** There is no prior saved format; the first run creates the file on the
  first change. The former phase-15 JSON plan is superseded, not migrated.
- **Ownership is unchanged.** The library stays on the gameplay world; persistence reads and writes it there and adds
  no new owner.
