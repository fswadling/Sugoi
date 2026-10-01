# Feature Specification: The cue's edits ride the one undo history

**Feature Branch**: `049-cue-on-one-history`

**Created**: 2026-10-01

**Status**: Draft

**Input**: User description: "Integrate the cue editor's independent undo/redo with the
gameplay-screen-level undo stack, and remove the cue-specific undo/redo buttons."

## Clarifications

### Session 2026-10-01

- Q: After a cue edit is undone or redone, should the Cue panel put its selected node back where it was, or only make sure the highlight never points at the wrong node? → A: Only validate — undo restores the cue and nothing else; the selection is left where it is, and one that no longer names a node renders as no selection (today's rule). A cue step does not carry the selection.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One undo covers the cue and the scene (Priority: P1)

A developer reworks a cue in the Cue panel, then moves an actor in the viewport, then presses undo
twice. Both edits come back off the same history in the order they were made — the actor's move
first, the cue edit second — exactly as if they had been edits of the same kind.

**Why this priority**: it is the point. Today the cue's edits live in a history of their own that
the undo key cannot reach, so the editor has two undos and the author has to remember which one a
key or button belongs to. Nothing else here matters if that is still true.

**Independent Test**: edit the cue, transform an actor, undo twice — both revert, the actor first
and the cue second; redo twice re-applies both in the order they were made.

**Acceptance Scenarios**:

1. **Given** a cue edit and then an actor transform, **When** the user undoes twice, **Then** the
   transform is reverted first and the cue edit second, and the cue is the value it had before it.
2. **Given** the same sequence, **When** the user redoes twice, **Then** both are re-applied in the
   order they were made.
3. **Given** only cue edits on the history, **When** the user undoes, **Then** the cue reverts and
   nothing in the scene changes.
4. **Given** a move between zones recorded between two cue edits, **When** the user undoes, **Then**
   the move is stepped back before the earlier cue edit is reached, and that edit reverts too.

---

### User Story 2 - The cue panel has no undo of its own (Priority: P2)

The Cue panel shows the cue and nothing else: its own Undo and Redo controls are gone, because there
is one undo and it is already on the keyboard and in the Edit menu.

**Why this priority**: it is what makes the first story true *in the interface* rather than only
beneath it — two controls inviting two mental models is the thing being removed. It is separable:
the controls could go while the histories were still two, which is why it is its own story.

**Independent Test**: open the Cue panel and confirm it offers no Undo or Redo control; press the
undo key with the panel focused and confirm a cue edit reverts.

**Acceptance Scenarios**:

1. **Given** the Cue panel, **When** it is drawn, **Then** it draws no Undo or Redo control.
2. **Given** a cue edit, **When** the user presses the undo key with the Cue panel focused,
   **Then** the edit reverts.
3. **Given** the Edit menu, **When** the history has something on one side, **Then** the
   corresponding menu item is enabled — including when the last edit was a cue edit.

---

### User Story 3 - Play drops only what went stale (Priority: P3)

The author plays the game with a cue edit and a zone edit on the history, then comes back to
editing. The cue's steps are gone — the cue has advanced, so they could not be applied cleanly — but
the zone edit is still there to take back.

**Why this priority**: it protects what the previous feature bought. Dropping the cue's history
wholesale on Play was harmless while the two histories were separate; with one history it would take
the zone edits with it. It is last because it is a property to *not break*, not a new capability.

**Independent Test**: edit the cue and a zone's light, play, return to editing, then undo — the zone
edit reverts and there is no cue step left on the history.

**Acceptance Scenarios**:

1. **Given** a cue edit and a zone edit, **When** Play begins and the editor is returned to,
   **Then** the cue contributes no steps and the zone edit still undoes.
2. **Given** no cue edits, **When** Play begins and the editor is returned to, **Then** the history
   is exactly as it was.

---

### Edge Cases

- Undoing with an empty history, or redoing with an empty redo side, is a no-op.
- Undoing a cue step touches no zone: the active zone, the camera and the selection are unchanged.
- The cue is `Fin` before the edit; its first edit is still one step.
- Removing a node and undoing brings it back — the step carries the whole cue value, so a removal is
  as reversible as an addition.
- Adding a node to a running `Sequence` is one step (the one edit a running cue accepts).
- Typing in the node's text field is not an undo: the shortcut yields while a text field wants
  input, and the field commits once when it is submitted or left.
- Undo/redo does not fire while a gizmo drag is in progress.
- A new edit after an undo clears the redo side — including when that edit is a cue edit.
- A cue edit to a node that is executing is still not offered; what is executing stays read-only.
- A cue step that followed a zone move is reached only after the move is stepped back (US1/AC4).
- Undoing or redoing a cue step leaves the panel's selection where it is; a selection that no longer
  names a node renders as no selection, exactly as it already does after any structural edit. The
  highlight is not part of what a step carries.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An edit the Cue panel accepts MUST be recorded as **one** step on the one history the
  editor already keeps (spec 048), not in a history of its own.
- **FR-002**: Undo and redo — the existing keys and the Edit menu — MUST take back and re-apply cue
  edits together with zone edits, in the order they were made.
- **FR-003**: The Cue panel MUST NOT offer Undo or Redo controls of its own.
- **FR-004**: A cue step MUST carry the cue value before the edit and after it; undo restores the
  earlier value and redo the later one.
- **FR-005**: When Play begins, every step whose payload is a cue MUST be removed from **both** sides
  of the history, because the cue advances while playing and such a step could no longer be applied
  cleanly. Steps over zone state MUST be left alone.
- **FR-006**: Undoing or redoing a cue step MUST NOT change the active zone, the camera or the Cue
  panel's selection — a cue is not a zone's and not a zone's edit. A selection that no longer names a
  node renders as no selection; a step carries no selection.
- **FR-007**: The Cue panel's editing rules MUST be unchanged: a node that is executing cannot be
  retuned, replaced or removed; a running `Sequence` still takes an addition; a row per container;
  the node-text field commits once.
- **FR-008**: The cue's own history type and the field that holds it MUST be removed; the cue's
  selection and its text-field fields MUST stay.
- **FR-009**: Undo and redo MUST NOT fire while a text field wants input or a drag is in progress.
- **FR-010**: After a Play, the cue MUST contribute no steps to the history, and zone steps recorded
  before Play MUST remain undoable.

### Key Entities *(include if feature involves data)*

- **The one history** (spec 048): the ordered steps the editor can take back, one timeline for the
  gameplay state. This feature makes a cue edit reach it.
- **A cue step**: the cue as it was before one accepted edit, and as it is after — two immutable cue
  values (spec 046), so a step is a snapshot and not a copy of a run.
- **The cue**: unchanged — one type that is both the vocabulary and its run, shrinking to `Fin`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A cue edit and an actor transform are both taken back by two undos, in the reverse of
  the order they were made, with the cue and the actor both restored — **0** edits stranded by being
  a cue's rather than a zone's.
- **SC-002**: The Cue panel offers **0** Undo/Redo controls.
- **SC-003**: With one cue edit and one zone edit on the history, a Play and return leaves **0** cue
  steps and **1** undoable zone step.
- **SC-004**: Undoing through the entire history returns the project to its starting state, cue
  included.
- **SC-005**: `dotnet build` is clean (warnings are errors) and `dotnet test` is green, including the
  cue-panel suite adapted to the one history.

## Assumptions

- **Play prunes the cue's steps, not the whole history.** Spec 047 asked for the cue's history to be
  dropped when play begins; with one history, the only reading that keeps spec 048's property is that
  the cue's *entries* go and every other step stays. Stated here rather than left to the
  implementation, because the alternative — clearing the history — silently undoes the previous
  feature's purpose.
- **The cue panel's selection and text-field state stay.** They are interface scaffolding — which
  node is highlighted, the buffer being typed into — not a history.
- **A cue step is only ever recorded while editing.** Play freezes the cue (spec 046) and the panel
  is editor-only, so there is no "edit the cue while it runs" case to decide.
- **The cast binding and the zone registry stay direct writes.** They remain outside the command
  system; whether they *should* become steps is the open question spec 048 recorded, and it is not
  decided here.
- **Triggers wired to cues are out of scope** (roadmap phase 10). When a trigger's cue arrives it
  will use the same step, but nothing about triggers is built here.
- **Prior specs are historical artefacts and are not edited** — the rule this project settled on.
  The requirements this feature supersedes — spec 047's own-history requirement and its "0 steps
  after a play" criterion, and spec 048's "the cue keeps its own history" — are recorded in this
  feature's own artifacts instead.
