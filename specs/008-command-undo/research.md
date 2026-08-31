# Research: Command / Undo

## Decision 1 — Zones are mutated in place, not copied

- **Decision**: `Zone` becomes a mutable reference type; `setEntity`/`removeEntity`
  mutate the zone's `Actors`/`Environment.Props` in place (return `unit`), and
  `spawnActor`/`spawnProp` mutate in place and return the new `EntityRef`.
  `Command.apply`/`undo` take the zone and mutate it, returning `unit`.
- **Rationale**: mirrors `ocaml-game`'s mutable slot map/zone, avoids allocating a
  fresh `Zone` record on every edit, and keeps `GameplayState` (a struct) holding a
  single shared `Zone` reference. The `SlotMap` already mutates its slot array in
  place; this removes the last "replace the zone" copy at the record level.
- **Alternatives considered**: keep `Zone` immutable and have `apply`/`undo` return
  a new `Zone` (rejected — the user directed in-place mutation; it also means every
  command thread a zone return value through the editor).

## Decision 2 — Commands store values + `EntityRef`, not closures

- **Decision**: the `Command` type carries an `EntityRef` and the before/after
  values (`Spawn`/`Delete`/`Duplicate` carry the entity; `Transform` carries before/
  after transforms). `apply`/`undo` pattern-match and re-derive the mutation.
- **Rationale**: value-storing commands are inspectable, trivially coalescible, and
  survive entity deletion because the `EntityRef` is an index, not a pointer
  (matches D-011).
- **Alternatives considered**: store closures (rejected — not coalescible, and
  opaque to the undo stack).

## Decision 3 — The undo stack is a functional value on `EditorState`

- **Decision**: `Command.UndoStack` is an immutable record (`Applied`/`Redo` lists)
  held as a plain `Undo` field on the immutable `EditorState`. `push`/`undo`/`redo`
  return a new `UndoStack` (the editor reassigns `Editor.Undo`), while `undo`/`redo`
  still mutate the zone in place.
- **Rationale**: matches the functional style of the rest of the editor state —
  `EditorState` is an immutable record whose `Selection`/`Tool`/`Snap`/`Drag`/
  `NudgeBefore` are reassigned rather than mutated. F# lists are immutable and
  structurally shared, so a push is still a single allocation.
- **Alternatives considered**: a mutable record mutated in place (rejected — an
  inconsistent special case against the functional editor state).

## Decision 4 — `Command.fs` depends on `Zone` + `Entity`, not `GameplayState`

- **Decision**: `editor/Command.fs` opens `Entity` and `Zone`; `apply`/`undo` take a
  `Zone`. It is placed after `screen/SharedState.fs` and before
  `editor/EditorState.fs`.
- **Rationale**: avoids the `Command → GameplayState → EditorState → Command` cycle
  (D-011). Phase 2d has no non-entity commands, so `SharedState` is not yet needed;
  the insertion point leaves room to widen `apply`/`undo` to `Zone * SharedState`
  when cast/scenario/zone/cue commands arrive (phases 9–12).
- **Alternatives considered**: putting entity CRUD in `Command.fs` (rejected — the
  in-place CRUD is a `Zone` concern; `Command` only owns undo semantics).

## Decision 5 — Continuous transforms coalesce in `UndoStack.push`

- **Decision**: `push` merges a `Transform` onto the top of the applied stack when
  both target the same `EntityRef`, keeping the first command's `Before` and the
  latest `After`. A gizmo drag pushes one `Transform` on release (using `Drag.Start`
  as `Before`); a held nudge pushes one on release (using a `nudge_before` field).
- **Rationale**: a drag/nudge is many per-frame transforms that are one user intent;
  one command per gesture keeps undo meaningful (D-011).
- **Alternatives considered**: pushing every frame (rejected — floods the history).

## Decision 6 — `Duplicate` is its own command case

- **Decision**: keep `Duplicate` distinct from `Spawn` even though `apply`/`undo`
  behave identically.
- **Rationale**: preserves intent and matches `ocaml-game`; a future change (e.g.
  selecting the copy on redo) stays localized.
- **Alternatives considered**: reuse `Spawn` (rejected — loses intent).

## Decision 7 — Inspector field editing and the `Field` command are deferred

- **Decision**: this phase ships `Spawn`/`Delete`/`Duplicate`/`Transform` only. The
  generic `Field` command, the Inspector's editable widgets (`InputText`/`Checkbox`/
  `DragFloat` + their native shims), and the `Inspector.draw : unit → GameplayState`
  signature change are deferred to the phase that makes the Inspector editable.
- **Rationale**: the roadmap's phase-2d scope is structural edits + transform; the
  Inspector is read-only today and no ImGui edit-widget bindings exist yet
  (minimalism — don't bind widgets before they're needed).
- **Alternatives considered**: ship `Field` now (rejected — dead code with no widget
  to emit it).

## Decision 8 — Undo/redo are blocked mid-drag and during text input

- **Decision**: `Ctrl+Z`/`Ctrl+Y` and the Edit menu items fire only while editing,
  `Drag = None`, and no ImGui text field has focus.
- **Rationale**: mutating the zone under an active drag would desync `Drag.Start`;
  typing must stay reserved for the field.
- **Alternatives considered**: allow undo mid-drag (rejected — corrupts the
  in-progress gesture).
