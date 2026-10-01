# Feature Specification: Cue Panel

**Feature Branch**: `047-cue-panel`

**Created**: 2026-09-30

**Status**: Draft

**Input**: User description: "a dedicated panel docked in the left of the editor to show the running cue, using a
reusable cue control we can reuse in the trigger inspector when the time comes."

## Origin

Phase 9 of `docs/ROADMAP.md` — "Cue engine + editor" — is delivered as two features. `specs/046-cue-core` is the
**engine half**: the cue, the run that is the cue, and the state it produces. It names this feature as its
follow-on — the Cue panel, and the cue's own undo history — because the engine half
cannot show what it is doing: a cue set in code runs, and nothing displays it. This is that panel: it shows the
running cue and lets the author edit it, and the first thing it buys is **visibility**.

Three things shape it:

- **The cue *is* its run.** The screen holds a cue value that shrinks as it is stepped, down to `Fin`. So this is
  not a view of an authored cue kept beside a running one — it is a view of the running cue, and what it shows
  shrinks under it. That is the model's intent, not a defect.
- **The editor is where an author watches.** The workspace is drawn only while editing, and a cue only advances
  while playing, so the panel shows the cue *as of* the moment the editor was opened — frozen. To watch it move,
  the author plays and returns. (A live overlay during play is a different surface and not this feature.)
- **One control, reused.** A cue appears in several places: the screen's cue here, a trigger's cue in the Inspector
  (phase 10), a scenario's cue (phase 11). The reference project extracted exactly one shared recursive cue-tree
  control to serve four such places rather than four drifting copies (`ocaml-game`, `specs/020-cue-tree-component`).
  This feature builds that control, and the panel is its first caller. `docs/PROJECT_STRUCTURE.md` already reserves
  `editor/CueTree.fs` for it and `editor/CueEditor.fs` for the panel; `docs/EDITOR_DESIGN.md`'s workspace table
  already affords a "Cue" tab, which this feature puts beside the Inspector rather than in the left column with
  the other sub-editors — what it edits is a cue rather than a place in the scene.

## Clarifications

### Session 2026-09-30

- Q: Should the Cue panel only display the running cue, or should it also let the author edit it (change a node's
  kind or numbers, add or remove nodes, with the cue's own undo history)? → A: It must also allow editing — the
  panel displays the running cue *and* lets the author change it, with the cue's own undo history.
- Q: When the author edits the op that is *currently running* — an in-flight `…State` leaf, shown as `Wait n` —
  what should that edit do? → A: The running op is read-only. It can be selected and read, but not changed; only
  ops that have not yet started can be edited.
- Q: When the cue is part-way through a sequence, which rows should be locked and which should stay editable? →
  A: The whole running path is locked — the in-flight leaf and every container above it are read-only; only parts
  entirely ahead of the running op can be edited.
- Q: How should a sequence be drawn in the panel — with its own `Sequence` row and the ops indented under it, or
  with the sequence's ops listed directly so the running op is literally the top row? → A: One row per container.
  `Sequence`, `Parallel` and `Fork` each get a labelled row with their children indented under it (the D-013
  recursive tree list, and the reference's shape).
- Q: The cue keeps its own undo history (D-010), but the cue also shrinks as it plays — what should happen to that
  history when the author plays and comes back? → A: It is cleared when play begins. The history covers the current
  editing session only, so no step can refer to a cue that has since advanced.
- Q: *(revised after implementation)* Should a `Sequence` that is running be locked along with the rest of the
  running path? → A: No — that was over-restrictive. The in-flight leaf stays read-only and a node on the running
  path still cannot be retuned, replaced or removed, but **an addition is always offered**: a `Sequence` takes a
  child whether or not it is running, because an addition lands on its queue and a queue's tail is ahead of the
  running op. This supersedes the "whole running path is locked" answer above.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The running cue is visible (Priority: P1)

The author sets a cue (in code, for now), plays, and opens the editor. The Cue panel is docked in the right column
beside the Inspector. It shows the cue as it stands: a row per container, the op in flight as the first child of
its sequence, what is still to come behind it, and the ops already run simply absent — and `Fin` when there is
nothing left to run.

**Why this priority**: it is the whole point of the feature — the cue system becomes observable. Without it the
engine half can be tested but never *seen*.

**Independent Test**: put a cue whose first op waits on the screen's state, open the editor, and read the panel:
the sequence is listed with the running op first and the queue behind it. Play a while, return, and read it again:
it shows what is left.

**Acceptance Scenarios**:

1. **Given** a cue on the screen's state, **When** the editor is open, **Then** the panel lists it — a row per
   container, the running op first among its sequence's children, the unstarted ops behind it, the ops already run
   absent.
2. **Given** a cue that has run to its end, **When** the editor is open, **Then** the panel shows `Fin` (the
   screen's `cue_end`).
3. **Given** the panel has been seen, **When** the author plays a while and returns to the editor, **Then** the
   panel shows the cue as it now stands, not as it was.
4. **Given** a cue in flight, **When** the panel lists it, **Then** a leaf that is running is shown as the op it
   came from (`Wait 2`), never as an internal state.

---

### User Story 2 - One cue control, reused (Priority: P2)

The cue the panel shows is rendered by a single control, parameterised by the cue and by a caller-supplied id
prefix, and knowing nothing about where the cue is stored. The same control will render a trigger's cue in the
Inspector (phase 10) and a scenario's cue (phase 11), so a cue looks the same everywhere and the places it appears
cannot drift apart.

**Why this priority**: it is what keeps the panel from becoming the first of several near-copies — the exact cost
the reference paid and then undid. It ranks below Story 1 because a control with nothing to render is not yet the
feature.

**Independent Test**: search the editor source for the recursive cue rendering — it exists once; the panel calls
it; and a second caller can be written without changing it.

**Acceptance Scenarios**:

1. **Given** the editor source, **When** a developer looks for the recursive cue rendering, **Then** it exists
   once, in one shared module, and the panel is one of its callers.
2. **Given** two cues rendered in the same window, **When** both are visible, **Then** their widgets do not
   collide, because each caller supplies its own id prefix.
3. **Given** the control, **When** a caller passes it a cue, **Then** it renders it — the caller supplies the id
   prefix, owns the selection, and handles any committed edit; nothing about the cue's storage, or whether it is the
   screen's, is known to the control.

---

### User Story 3 - The author edits what is still to run (Priority: P3)

The author watches a cue run, opens the editor, and changes what has not happened yet: a queued op's kind or its
numbers, an op appended to the sequence that is running, or one removed. What is executing is never retuned,
replaced or removed — the op in flight and the containers above it — so an edit cannot disturb the run; the one
edit the running path accepts is an addition, because an addition lands on a queue and a queue's tail is ahead of
the running op. Every accepted change is a step the cue's own undo can take back, until a play begins.

**Why this priority**: it is the second thing the panel buys, after seeing the cue at all, and it is what makes the
panel an authoring tool rather than a read-out. It ranks below the display (Story 1) and the shared control
(Story 2) because both are needed before an edit has anywhere to land.

**Independent Test**: with a cue paused part-way through a wait, change a queued op's numbers in the panel, undo
it, and read the cue back — the change and its reversal are exact; then confirm that neither the running op nor the
containers holding it offer any way to be changed.

**Acceptance Scenarios**:

1. **Given** a cue with an op queued behind the running one, **When** the author changes that op's kind or numbers,
   **Then** the screen's cue holds the change.
2. **Given** an op that is running, **When** the panel renders it, **Then** no control on it offers a change; and
   **given** the sequence holding that op, **Then** the one control it offers is an addition to its queue.
3. **Given** an edit just made, **When** the author undoes it, **Then** the cue is exactly as it was before.
4. **Given** an edit made while editing, **When** the author plays and returns to the editor, **Then** there is
   nothing left to undo — the history began with the current editing session.

---

### Edge Cases

- **A `Fin` cue**: the panel shows the terminal — not an error and not an empty panel.
- **A cue that is a single op** rather than a sequence: it is shown at the root.
- **A leaf in flight**: `WaitState` and `MoveCameraState` are shown as `Wait n` and the camera move, never by their
  internal names — the view is *what is left to run*.
- **The cue shrinks while the panel is closed**: on return the panel shows what is left, with no row for an op
  that has run.
- **A selection whose node is gone** (an edit removed it): the panel renders without error, and the selection does
  not silently point at a different node.
- **A cue of thousands of nodes**: shown whole and scrolled, not truncated.
- **Two cues in one window** (once the trigger Inspector exists): their widgets do not collide.
- **The dock layout**: the panel joins the right column's tabs beside the Inspector, and the layout is remembered
  (the workspace builds its default layout once).
- **The running path**: the in-flight leaf (shown as `Wait n` or the camera move) and the containers above it
  cannot be retuned, replaced or removed — those controls are withheld — so what is executing is never disturbed;
  the ops queued behind the running op are editable.
- **Adding**: an addition is offered on **any** `Sequence`, running or not — it lands on a queue, whose tail is
  ahead of the running op (FR-011). That is the one edit the running path accepts.
- **Editing**: the cue is frozen while the editor is open, so the panel does not change under the author's cursor.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The editor MUST have a Cue panel, docked in the right column as a tab beside the Inspector, and
  reachable by the panel-cycling shortcut the other docked panels use.
- **FR-002**: The panel MUST show the screen's cue as a tree: a row for every container (`Sequence`, `Parallel`,
  `Fork`) with its children indented beneath it, the running op the first child of its sequence, the unstarted ops
  behind it, and the ops already run simply absent; and `Fin` shown when nothing is running.
- **FR-003**: The panel MUST show the screen's *own* cue — the value that is executing — not a copy, and not a
  parallel structure kept in step with it.
- **FR-004**: A leaf in flight MUST be shown as the op it came from (`Wait n` for a `WaitState`, the camera move
  for a `MoveCameraState`), so the view is what is left to run and never an internal state's name.
- **FR-005**: Opening the panel, reading it, and selecting a node in it MUST NOT change the cue or the state that
  holds it.
- **FR-006**: The cue display MUST be a single shared control, parameterised by the cue, by a caller-supplied id
  prefix that disambiguates co-visible instances, and by the caller's own selection and commit callback — and by
  nothing that ties it to the panel or to where the cue is stored — so a trigger's cue (phase 10) and a scenario's
  cue (phase 11) can reuse it.
- **FR-007**: The control's node labels, its child structure, and **which nodes are read-only** in FR-011's sense
  — not retuned, replaced or removed — MUST come from a pure function, asserted headlessly — the house pattern,
  where a panel's layout is pure and tested and the drawing is thin over it (`DialogueRender`, `PromptRender`).
- **FR-008**: The panel MUST belong to the editor workspace: shown while editing, not while playing, and shown
  again on return to editing.
- **FR-009**: The panel MUST list a cue of any size without truncation — a long cue is scrolled, not cut.
- **FR-010**: `dotnet build` MUST be clean and `dotnet test` MUST pass.
- **FR-011**: A node on the running path — the in-flight leaf (a cue shown in its `…State` form, namely
  `WaitState` or `MoveCameraState`) and every container above it, from the root down — MUST NOT be retuned,
  replaced or removed. They can be selected and read, but the panel MUST NOT offer a way to change what is
  executing. The running path **does** accept additions, though: a `Sequence` takes a child whether or not it is
  running, because an addition lands on its queue and a queue's tail is ahead of the running op.
- **FR-012**: The panel MUST let the author edit the cue: change a node's kind and the numbers that kind carries,
  add a child to a `Sequence`, and remove a node — subject to what FR-011 protects. A `Parallel`'s two sides are
  the type's, not the author's — no node is added to one. Each accepted change MUST be written to the screen's cue.
- **FR-013**: The cue editor MUST keep its own undo history for the edits it accepts, and MUST drop it when play
  begins, so no step can refer to a cue that has since advanced.

### Key Entities *(include if feature involves data)*

- **The cue** (`world/Cue.fs`): what the control renders — the control flow, the leaves, `Fin`, and the in-flight
  `…State` cases.
- **The screen's cue** (`GameplayState.Cue`): what the panel shows — the cue that is executing.
- **The cue control** (`editor/CueTree.fs`, new): the one shared renderer. A cue, a caller id prefix, and the
  caller's selection; its labels and child structure are pure, its drawing is thin.
- **The Cue panel** (`editor/CueEditor.fs`, new): the docked panel that shows the screen's cue through the control.
- **The panel's selection**: which node is highlighted — identified by its position (a path) in the cue, since a
  cue value carries no ids; the panel's own state, never written into the cue.
- **The cue's undo history**: the steps the editor's accepted edits can be taken back through — the editor's own,
  not a zone's, and dropped when play begins so no step outlives the cue it refers to.
- **The id prefix**: a caller-supplied string that keeps two co-visible cue controls' widgets apart.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With a cue whose first op is a wait, the panel shows its sequence as a row with the running op as its
  first child and the ops to come behind it — 0 rows for an op that has already run.
- **SC-002**: After the cue has been played and the editor reopened, the panel shows the cue as it now stands —
  0 stale rows.
- **SC-003**: Exactly one implementation of the recursive cue rendering exists (0 copies).
- **SC-004**: The control's labels and child structure are asserted headlessly, `dotnet build` is clean and
  `dotnet test` passes.
- **SC-005**: Looking at the cue changes nothing: 0 changes to the cue from opening the panel or selecting a node.
- **SC-006**: With a cue part-way through a wait, the ops queued behind it are editable, the running leaf offers 0
  controls that change it, and the running `Sequence` offers exactly its 1 addition.
- **SC-007**: An edit to a queued op changes the screen's cue exactly once, and undoing it restores the cue exactly.
- **SC-008**: After a play, the cue editor's undo history holds 0 steps.

## Assumptions

- **The panel is the editor's.** It is shown while editing and not while playing (the backtick toggle), so what it
  shows is the cue frozen as of entering the edit; watching it advance is Play, then return. A live overlay during
  play is a different surface and is not this feature.
- **Editing is in scope.** The panel both shows the running cue and lets the author change it — a node's kind
  and numbers, adding and removing nodes — written back to the screen's cue through the control's commit callback
  (the reference's per-node editing, and `docs/EDITOR_DESIGN.md` D-013's tree-list interaction). What is protected
  is what is executing: the in-flight leaf and the containers above it cannot be retuned, replaced or removed, so
  an edit never disturbs the run — while an **addition** to a sequence's queue is always offered, running or not,
  since a queue's tail lies ahead of the running op. The cue keeps its own undo history (D-010), scoped to the
  editing session — dropped when play
  begins, so a step can never refer to a cue that has since advanced. The interaction's exact shape is settled by
  this session's clarifications (see **Clarifications**). Reordering nodes, which `D-013`'s tree-list interaction
  also names, is **not** part of the clarified set — change, add and remove are.
- **A cue for the panel to show is set in code for now** (spec 046's assumption, unchanged). Phase 10's triggers
  and phase 11's scenarios will set them; until then the panel shows `Fin` on a fresh run.
- **The control is a leaf editor module.** It depends on the cue and the ImGui binding, and never on
  `GameplayState`, `Command`, `EditorState` or a zone — so it can be reused where the cue is stored differently.
- The panel docks in the **right column** as a tab beside the Inspector rather than in the left column with the
  other sub-editors: what it edits is a cue rather than a place in the scene, and the right column is where the
  editing surfaces are. The Dialogue panel's bottom dock is the documented exception, for width, and does not
  apply here.
- `docs/EDITOR_DESIGN.md`'s workspace table already affords a "Cue" tab and D-013 describes the
  cue editor as a recursive tree list; `docs/PROJECT_STRUCTURE.md` already reserves `editor/CueEditor.fs` and
  `editor/CueTree.fs`. Those docs move with this feature where they say otherwise (the "top-level cue" language
  predates the model in which the screen's cue *is* the run).
- **No new dependency.** SDL3 and the ImGui binding, as the other panels use them.
