# Quickstart: Entity Rename

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- For the interactive scenarios: a display, and a zone containing at least one
  actor, one prop, and one waypoint (parts come from a loaded environment).

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj   # includes ZoneTests + SelectionTests
```

The data path is covered headlessly — `Zone.setName` across kinds,
`Selection.renameRef` (including undo/redo), and the `Rename` command through
`UndoStack`. The field's focus/commit interaction needs an ImGui frame and is
covered by the scenarios below.

## Validation scenarios

Automated (headless):

1. **A name is written for every stored kind (FR-001, FR-008)** — `Zone.setName`
   sets the name of an actor, a prop, a part, and a waypoint, and leaves each
   entity's transform, physics attachment, and binding untouched.
2. **A rename is one undo step (FR-002)** — `Selection.renameRef` pushes exactly
   one command; undo restores the previous name and redo reapplies the new one.
3. **An empty name is refused (FR-004)** — `Selection.renameRef` with `""` (or
   whitespace only) raises, and the entity's name is unchanged.
4. **Unchanged and dead refs are inert (FR-003, FR-009)** — renaming to the name
   the entity already has pushes no command; renaming a deleted or stale ref
   changes nothing and creates nothing.
5. **Duplicates are allowed (FR-007, SC-006)** — two entities can share a name,
   and each is still resolved separately by its own ref.
6. **Nothing else changes (FR-008, SC-005)** — renaming does not move, re-shape,
   or re-render the entity, and repeated renames add no bodies.

Interactive (editor; needs a display):

7. **The field appears (FR-001)** — select each of an actor, a prop, a part, and a
   waypoint: the Inspector's name row is a text field showing that entity's name.
8. **Commit with Enter (FR-003, FR-010)** — type a new name and press Enter: the
   Inspector field shows it at once and the entity's Outliner row shows it by the
   next frame.
9. **Commit by clicking away (FR-003)** — type a new name and click elsewhere: the
   same result as Enter.
10. **Undo and redo (FR-002)** — Ctrl+Z reverts the name in both the field and the
    Outliner row; Ctrl+Y reapplies it.
11. **Leaving without typing commits nothing (FR-003)** — click into the field and
    click away: no undo step is added and the name is unchanged.
12. **An empty name is not stored (FR-004, SC-002)** — clear the field and press
    Enter, then click away: the entity keeps its previous name and the field shows
    it again.
13. **No leakage between entities (FR-005, SC-003)** — rename A, then select B and
    rename it: each keeps exactly the name typed for it. Then type a new name for
    B *without* committing and click A's row: B gets the typed name, and A's field
    shows A's own name.
14. **Typing is not a shortcut (FR-012)** — with the field focused, type a name
    containing `f`, `3`, `q`, and spaces, and press Delete and Backspace: the
    camera does not move, the tool does not change, snapping does not toggle, and
    the entity is not deleted or duplicated.
15. **Renaming is a label change (FR-008, SC-005)** — rename an actor and confirm
    it still collides and renders exactly as before, at the same position,
    rotation, and scale; watch the stats line across many renames for no growth.
16. **Survives zone switching (FR-006)** — rename an entity, switch zones and back:
    the entity is listed under its new name.
17. **Survives delete and undo (FR-009)** — rename an entity, delete it, undo the
    deletion: it returns with the renamed value and is still selectable.

## Expected outcome

- Every entity the Inspector can show can be renamed there, with the change
  committed on Enter or on leaving the field, as exactly one undoable step.
- An empty name is never stored, an uncommitted edit changes nothing, and text
  typed for one entity is never applied to another.
- A name is a label: duplicates are allowed, nothing resolves an entity by name,
  and renaming changes nothing else about the entity.
- `dotnet build` is clean and `dotnet test` passes (existing suite plus the
  `Zone.setName` cases and the new `SelectionTests`).
