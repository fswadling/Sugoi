# Phase 0 Research: The cue's edits ride the one undo history

Nine decisions. D1–D4 are the design, D5–D7 what the stories assert and how, D8–D9 the wording and the documents.

## Decision 1 — A cue edit is a command, not a second history

- **Decision**: an edit the Cue panel accepts is recorded as a `SetCue of Previous : Cue.Cue * Next : Cue.Cue` step
  on the one history spec 048 established. The cue's own history — `EditorState.CueHistory`, its module, and the
  `CueHistory` field — is deleted.
- **Rationale**: the reason for a second history was ownership. `specs/047` D-010 put it plainly: "a cue spans zones
  while the undo stacks are per zone, so the cue cannot ride a zone's stack". Spec 048 removed the premise — the
  history is one timeline on the gameplay state, and the moves between zones are steps on it — so a cue *can* ride
  it, and a second history is now only a second thing to keep in step. Every argument that once applied to the cue
  (`specs/047` FR-013, `specs/048` FR-014) is superseded by this, and recorded in [research D8](#decision-8--the-superseded-statements-and-the-wording).
- **Alternatives considered**: **keep `CueHistory` and mirror it into the one history** — rejected: two sources of
  truth for one timeline, which is what the feature exists to remove. **Make the cue a zone's value** — rejected:
  it is not a zone's (spec 046 FR-013), and a cue survives a zone switch.

## Decision 2 — A cue step carries two cue values, because a cue is immutable

- **Decision**: the step's payload is the cue before the edit and the cue after it — the values themselves, not a
  command that could re-derive them.
- **Rationale**: this is exactly what `CueHistory` stored (a cue is immutable, "so a snapshot is a reference"), and
  it is the same rule the command family already follows for a zone-level value (`SetLight of Previous * Next`,
  `SetPlayer of Previous * Next`). A removal, an addition, a kind change and a text change are all just "the cue
  was this and is now that", so one case covers every edit the panel can make and nothing has to know how a cue was
  edited.
- **Alternatives considered**: **a command per edit kind** (add/remove/retarget/…) — rejected: it would have to
  re-derive the "before" value from the "after" one on undo, which a snapshot needs no help with, and it would grow
  with the panel's control set.

## Decision 3 — The panel commits inline and keeps taking only the state

- **Decision**: `CueEditor.commit` writes `state.Cue` and pushes the step onto `state.Editor.Undo` itself, rather
  than routing through `History.commit` (which would make it take `SharedState`).
- **Rationale**: it is exactly what every other editor call site already does — `History`'s own header records that
  "an entity edit's existing call sites apply and push themselves, onto the same field", and that `commit` exists for
  the zone steps, which need the state. It also keeps `specs/047`'s property that the panel takes only the gameplay
  state: nothing it needs is in `SharedState`, and giving it one only to forward it to `commit` would be a parameter
  with no reader. `History.apply`'s `SetCue` arm is still needed — redo reaches it.
- **Alternatives considered**: **`CueEditor.draw shared &state`, routed through `History.commit`** — rejected as
  above: one more parameter on the panel and an inconsistency with the call sites that preceded it.
  **`CueEditor.commit` calling `History.commit` with a dummy `shared`** — rejected: a lie in a signature.

## Decision 4 — Play drops the cue's steps, from both sides, and nothing else

- **Decision**: when Play begins, every `SetCue` step is removed from `Undo.Applied` **and** `Undo.Redo`; every
  other step is left exactly as it was.
- **Rationale**: `specs/047` FR-013 required the cue's history to be dropped when play begins, because the cue
  advances while playing and a step recorded against it could no longer be applied cleanly. With one history, that
  requirement can only mean *the cue's steps* — clearing the history would throw away the zone edits, which is the
  property `specs/048` bought and this feature's US3 exists to protect. Filtering is sound because steps are
  independent: a cue step does not depend on a zone step or on another cue step, so removing it leaves the
  remaining timeline's order and meaning intact.
- **Alternatives considered**: **clear the whole history on Play** — rejected: it silently forfeits the point of
  `specs/048`. **Leave the cue steps and let undo rewind the cue** — rejected: the author would be editing a race
  they already ran; undo is not a rewind of play.

## Decision 5 — A cue step touches the cue and nothing else

- **Decision**: undoing or redoing a `SetCue` writes `state.Cue` and nothing else — no zone, no camera, no
  selection. The Cue panel's stored selection is left where it is; a selection that no longer names a node renders
  as no selection, which is the rule the panel already follows after any structural edit (`specs/047` D-003).
- **Rationale**: a cue is not a zone's, so a cue step has no zone to activate and no camera to re-frame — the
  contrast with `ZoneMove`, whose undo *does* re-frame the camera, is the point. The selection is the panel's own
  state rather than the cue's, the same reason `specs/048` D-6 leaves it alone when a move is undone, and it is
  never written into a cue (`specs/047`'s Key Entities).
- **Clarified**: the spec's Question 1 (2026-10-01), answered "only validate — a cue step carries no selection".

## Decision 6 — The panel's controls go, and so do the functions behind them

- **Decision**: the Cue panel draws no Undo/Redo control, and `CueEditor.undo`, `redo`, `canUndo` and `canRedo`
  are deleted rather than left unused. The only entry points are the existing `Ctrl+Z`/`Ctrl+Y` and the Edit menu,
  whose enabled state already reads the one history.
- **Rationale**: the request is explicit, and one key meaning one thing is the point. Leaving the functions behind
  would be dead code with a plausible-looking API, and the Edit menu's gating (not mid-drag, not typing, non-empty
  side) already covers every condition the panel's buttons used to check.
- **Alternatives considered**: **keep the buttons as synonyms of the menu** — rejected: two affordances for one
  undo is the confusion being removed.

## Decision 7 — What the suite asserts

- **Decision**: headlessly, with no device: a cue edit followed by an actor transform are undone in the reverse of
  the order made and redone in the order made (SC-001); the panel exposes no Undo/Redo control, asserted as "no
  such function on its public surface" (SC-002); a cue step and a zone step on the history, a Play and a return,
  leave no cue step and the zone step still undoable (SC-003); undo through the whole history returns the project
  to its starting state with the cue included (SC-004); and undoing a cue step leaves the active zone, the camera
  and the selection untouched (FR-006).
- **Rationale**: every scenario needs only a headless `GameplayState` and a cue value, which is how
  `test/CuePanelTests.fs` and `test/GameplayStateTests.fs` already build them. `CueHistory`'s own tests are replaced
  by the one-history equivalents rather than deleted, so the coverage that mattered — one edit, one step; nothing
  applied is a no-op — survives under the new mechanism.
- **Alternatives considered**: asserting the command list's shape — rejected: it pins the representation rather
  than the behaviour, and the behaviour is "the cue came back".

## Decision 8 — The superseded statements, and the wording

- **Decision**: **history** stays the canonical term (`specs/048` D-10). The statements this feature supersedes are
  recorded here, not patched into the specs that wrote them, because a spec is a historical artefact:

  | Superseded statement | Where it was written | What this feature does instead |
  |---|---|---|
  | "The cue editor MUST keep its own undo history for the edits it accepts, and MUST drop it when play begins" | `specs/047` FR-013 | The cue's edits are steps on the one history; a Play drops those steps, not a history of their own |
  | "After a play, the cue editor's undo history holds 0 steps" | `specs/047` SC-008 | Kept in effect: after a play the cue contributes 0 steps — while every zone step stays |
  | "the cue editor gives it its own history, the way the cast binding and the zone registry are direct writes" | `specs/047` D-010 | The cue is a command now; the cast binding and the zone registry are still direct writes, and are still the exception |
  | "The cue panel … presents the cue as a recursive tree list (D-013): … and the cue keeps its own undo history (D-010)" | `docs/EDITOR_DESIGN.md`, the workspace table | The panel keeps D-013's tree list; it keeps no history of its own |
  | "It takes only `state`: nothing it needs is in `SharedState`" | `specs/047`'s contract | Still true, and preserved deliberately (D3 above) |
  | "The cue's own history (spec 047) MUST be unchanged; moving the cue onto this history is a separate feature" | `specs/048` FR-014 | That separate feature is this one |

  **Not superseded**: `specs/047`'s editing rules (what is executing is read-only, a `Sequence` always takes an
  addition, a row per container, the text field commits once) and `specs/048`'s structure (one history, the moves
  as steps, the zone guards).
- **Rationale**: the rule this project settled on — specs are records, the living docs move — and it is what makes
  the record trustworthy.
- **Where the prior specs live**: `specs/047-cue-panel` and `specs/048-unified-undo-stack` are **not edited**.

## Decision 9 — The documents that move

- **Decision**: `docs/EDITOR_DESIGN.md` — the workspace table's Cue row, D-010 (the cue's undo), D-011 (the
  command/undo model, whose exceptions list names the cue indirectly), D-013 (the cue editor's interaction), and
  the module table's `CueEditor`/`CueTree` row — plus `docs/PROJECT_STRUCTURE.md`'s `CueEditor.fs` line,
  `docs/ROADMAP.md` (phase 9's cue-editor deliverable and the "Next step" summary, both of which call the
  panel's history its own), and the header comment in `editor/CueEditor.fs`, which describes a history the panel
  no longer has. **Deliberately not moved**: `docs/CUE_MODEL_DECISION.md` — it records the earlier model and is
  marked "superseded as the design; kept for the measurements and the route", so its cue-history clause is part
  of what it recorded rather than a description of current behaviour.
- **Rationale**: these describe current behaviour, so they move with it, unlike the specs above. `D-011`'s payload
  rule gains a worked example rather than a change: a cue step is a value snapshot, like `SetLight`'s.
- **Alternatives considered**: leaving the docs for a later pass — rejected: a doc that says the panel "keeps its
  own undo history" is wrong the moment this lands.
