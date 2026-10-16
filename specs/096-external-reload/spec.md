# Feature Specification: Live Reload of the Project File

**Feature Branch**: `096-external-reload`

**Created**: 2026-10-16

**Status**: Draft

**Input**: User description: "Another feature that would be really cool is if there was a listener while the
program runs for changes to the manifest file that happen externally — say an agent makes changes, such that the
changes get automatically picked up without restarting the program. Could that work in a platform agnostic way?"
Two decisions taken on the sketch: **poll the file's content** rather than subscribe to `FileSystemWatcher`, and the
reload is **not** an undo step.

## Origin

`specs/095-scenario-persistence` writes the scenario library to `project.manifest` automatically and reads it **once
at startup**. The file is human-readable and hand-editable by design, so it can be changed from *outside* the game —
by the author in a text editor, or by a coding agent — but today such a change is invisible until the game restarts.
This feature closes that gap: while the game runs, an external change to the file is picked up and installed without a
restart.

The file holds **only the scenario library**, so this is precisely "watch my scenes change live" — an external edit
updates the scenarios, not the live scene or the running cue (which `specs/095` does not persist).

## Clarifications

### Session 2026-10-16

- Q: What is watched? → **The project file** (`project.manifest`) at the launch working directory — the one artifact
  `specs/095` persists. Nothing else is watched, because nothing else is written.
- Q: How is a change detected — events or polling? → A **periodic content poll**. The file's text is read and compared
  to the text last written or read, once per interval. This is deliberately preferred over `FileSystemWatcher`: it is
  plain `System.IO` (no per-OS quirks), it detects every save style uniformly — an in-place write *and* a
  temp-file-then-rename ("atomic save"), which fires a `Renamed` event a file-bound watcher would miss — and it has no
  event storms to debounce.
- Q: How often? → At a **bounded interval** of about **250 ms**, gated by a frame-time accumulator, so it is **one
  read per interval, not one per frame** (a per-frame read of the file, which can reach hundreds of KB, would be tens
  of MB/s).
- Q: How is the game's own write distinguished from an external one? → By **comparing the file's text to the text last
  written or read**. An identical text is not a change, so the game's own saves (and a no-op internal rewrite) never
  trigger a reload — the feedback loop is closed by the comparison itself.
- Q: What if the poll catches a *partial* or damaged external write? → It is **reported** and the running library is
  **left unchanged** — a live library is never emptied because an agent wrote half a file — and the poll **retries**
  on the next interval, so a transient partial write self-heals once the writer finishes. (This deliberately differs
  from the startup load, where a damaged file yields an *empty* library: at startup there is nothing to preserve, but
  at runtime there is.)
- Q: What if the file is deleted externally? → The running library is **left as it is**; the reload does not clear it.
  If the file reappears with content, it is picked up then.
- Q: Is the reload a step on the undo history? → **No.** It is like the startup load — an arrival of authored state,
  not an author action in this session — so it pushes no step. (Making it undoable is possible but complicates the
  history for little gain.)
- Q: What happens when an external edit and a game edit collide? → **Last writer wins; there is no merge.** Whichever
  writes the file last is what the poll sees, and the running library is reconciled to it.
- Q: Can live reload be turned off? → **Yes** — a **Live reload** toggle in the existing Miscellaneous panel
  (default on), so the author can suspend it while hand-editing. Switching it off suspends *reloading only*; the
  game's own automatic saving is unaffected.
- Q: Where is the poll drained? → At the end of the gameplay screen's frame update, beside the existing write, gated
  by the interval accumulator **and by the author editing** (it does not run while playing).
- Q: Does live reload run in **play** mode? → **No — edit mode only.** The poll reads a file that can grow to
  hundreds of KB, and actual play must keep the hot path clear, so the drain is gated on the author **editing**. An
  external edit made while playing (or on the main menu) is picked up on the first edit-mode frame — when the author
  returns to the editor — and the Scenario panel is the natural place to see it.
- Q: Does this need an OS-specific API? → **No.** A content poll uses only `System.IO`, so the feature is
  platform-agnostic by construction; no inotify / FSEvents / ReadDirectoryChangesW dependency is introduced.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An external edit appears without a restart (Priority: P1)

While the game is running, an agent (or the author in a text editor) edits `project.manifest` — renaming a scenario,
registering another, changing a cue. Within about a second the **Scenario** panel reflects the new contents, with no
restart and no action by the author.

**Why this priority**: it is the feature. The file is authored data that is *meant* to be edited outside the game;
picking those edits up live is the whole point and is valuable on its own.

**Independent Test**: with a headless gameplay state loaded from a temp project file, rewrite the file externally,
run the poll's interval, and assert the reloaded library equals the new file's.

**Acceptance Scenarios**:

1. **Given** a running game with a loaded library, **When** the file is rewritten with a new scenario, **Then** a
   poll returns the new library and it is installed.
2. **Given** the same, **When** the file is rewritten with content identical to what was last written or read,
   **Then** no reload occurs (0 changes).
3. **Given** the same, **When** the file is replaced by a temp-file-then-rename save, **Then** the new content is
   picked up (the poll does not care *how* the bytes landed).

---

### User Story 2 - The reload never fights the game or empties a live library (Priority: P2)

The user keeps editing scenes in the game while an agent edits the file. The game's own saves never trigger a
spurious reload, and a damaged, partial, or deleted file never wipes the library the author is working with.

**Why this priority**: a live reload that reloads its own writes, or that empties the library when an agent writes
half a file, is worse than no feature — it makes the editor untrustworthy.

**Independent Test**: after the game writes the file, assert the poll reports no change; then write a malformed file
and assert the poll reports the problem, returns nothing, and leaves the running library unchanged; delete the file
and assert the same.

**Acceptance Scenarios**:

1. **Given** the game has just written the file, **When** the poll runs, **Then** it reports no change (the text
   matches what was written).
2. **Given** an externally written file that cannot be decoded, **When** the poll runs, **Then** it is reported, no
   reload is installed, and the running library is unchanged; a later valid write is still picked up.
3. **Given** the file is deleted externally, **When** the poll runs, **Then** the running library is unchanged.

---

### User Story 3 - The author can suspend live reload (Priority: P3)

While hand-editing the file, the author turns **Live reload** off in the Miscellaneous panel so the game stops
picking up their in-progress edits; turning it back on resumes. Automatic saving is unaffected either way.

**Why this priority**: a safety valve for the hand-editing author; secondary to the feature itself.

**Independent Test**: with the toggle off, rewrite the file and assert no reload; turn it on and assert the next poll
picks the change up.

**Acceptance Scenarios**:

1. **Given** Live reload is off, **When** the file changes externally, **Then** nothing is reloaded.
2. **Given** Live reload is turned back on, **When** the next interval elapses, **Then** the pending external change
   is picked up.

---

### Edge Cases

- A **temp-file-then-rename** external save is detected the same as an in-place write (a content poll, not an event).
- A **partial** external write decodes as damaged → reported, library unchanged, and the finished file is picked up
  by the following poll.
- The file is **deleted** → the library is left as it is; a later re-creation is picked up.
- **Coarse filesystem timestamps** (1-second granularity) do not matter, because detection compares *content*, not a
  timestamp.
- The game and an external writer both write within the same interval → last writer wins; the next poll reconciles.
- **Our own write** in the same interval as a poll → recognised by the text comparison, so it is not reloaded.
- An external change made while **playing** (or on the main menu) is picked up on the first frame the author is
  editing again, because the poll runs only in edit mode.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: While the author is **editing** the gameplay screen (not while playing), the game MUST poll the project
  file's **content** at a bounded interval of about **250 ms**, gated so it reads **once per interval, not once per
  frame**.
- **FR-002**: A change MUST be recognised by **comparing the file's text** to the text last written or read by the
  game, so the game's own saves and any identical rewrite are **not** treated as changes.
- **FR-003**: On a recognised external change that decodes, the reloaded library MUST be **installed** into the
  running gameplay state (the scenario library), with no restart and no author action.
- **FR-004**: An external file that cannot be read or decoded (including a **partial** write) MUST be **reported**
  and MUST **leave the running library unchanged**; the poll MUST retry on the next interval, so a transient partial
  write is picked up once the writer finishes.
- **FR-005**: A file **deleted** externally MUST leave the running library unchanged (the reload MUST NOT clear it).
- **FR-006**: The reload MUST **not** be a step on the undo history, and MUST NOT alter the live scene, the active
  place, the camera or the running cue. (Because the poll runs only while editing, no reload can occur during play.)
- **FR-007**: Detection MUST be **content-based**, so it is correct regardless of filesystem timestamp granularity or
  save style (an in-place write and a temp-file-then-rename save are both detected).
- **FR-008**: A **Live reload** toggle in the Miscellaneous panel (default **on**) MUST suspend reloading when off,
  without affecting the game's automatic saving; turning it back on MUST pick up a pending change.
- **FR-009**: The feature MUST use **no OS-specific API** — it is a content poll over `System.IO` — so it is
  platform-agnostic.
- **FR-010**: An external change affects **only the scenario library** (the whole of what the file holds); nothing
  else is reloaded or altered. A collision between an external edit and a game edit is **last-writer-wins**, with no
  merge.
- **FR-011**: The project MUST build with **0 warnings** and the headless suite MUST pass, with reload tests among
  them (external change installed; identical rewrite ignored; game's own write ignored; damaged/partial reported with
  the library unchanged; deletion leaves the library; the toggle suspends; the interval gate does not re-read).
- **FR-012**: Documentation MUST note live reload: the Persistence section of `docs/ARCHITECTURE.md` and the editor's
  Miscellaneous panel description in `docs/EDITOR_DESIGN.md`.

### Key Entities *(include if feature involves data)*

- **The project file**: the s-syntax `project.manifest` of `specs/095` — the one watched artifact.
- **The last-written text**: the file content the game last wrote or read, the reference the change comparison is
  against (it closes the feedback loop).
- **The reload poll**: the interval-gated read-and-compare that yields the reloaded library, or nothing (no change /
  mid-write / deleted).
- **The Live reload toggle**: the editor's switch that suspends reloading.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An external edit to `project.manifest` is reflected in the editor within about a quarter of a second,
  with 0 restarts and 0 author actions. (FR-001, FR-002, FR-003)
- **SC-002**: In 0 cases does the game's own write cause a reload (the feedback loop is closed). (FR-002)
- **SC-003**: In 0 cases does a damaged, partial, or deleted external file empty or alter the running library; each is
  reported where a report is due, and a subsequent valid write is still picked up. (FR-004, FR-005)
- **SC-004**: Both save styles — in-place write and temp-file-then-rename — are detected, on any of the supported
  file systems. (FR-007, FR-009)
- **SC-005**: The reload adds nothing to the undo history and changes nothing but the scenario library. (FR-006,
  FR-010)
- **SC-006**: With Live reload off, 0 external changes are applied; turning it on applies the pending one. (FR-008)
- **SC-007**: The project builds with **0 warnings** and the full headless suite passes. (FR-011)

## Assumptions

- **Polling, by content, while editing.** A ~250 ms content poll running only in edit mode is the chosen mechanism
  (see Clarifications): it is portable, it needs no debounce, and it detects atomic renames that a file-bound event
  watcher misses. Running it only while editing keeps actual play's hot path clear of file I/O — the reason for the
  edit-mode gate rather than a longer interval.
- **The interval is not user-configurable** in this feature; the toggle is the only control.
- **No merge, last-writer-wins.** There is no three-way merge between the game's library and an external one; the
  file is a single authored document and whichever writer wrote last is authoritative. This is the accepted trade.
- **Only the file is watched**, and the file is only the scenario library — so this cannot reload the live scene, the
  camera or the running cue, which are deliberately not persisted (`specs/095`).
- **The toggle is editor-only state**, not persisted and not a step on the undo history (the `specs/059`/Misc-panel
  pattern).
- **Ownership is unchanged.** The library stays on the gameplay world; the poll and the toggle write it there and add
  no new owner.
- **Documentation** (`docs/ARCHITECTURE.md` Persistence, `docs/EDITOR_DESIGN.md` Miscellaneous panel) is updated to
  note live reload.
