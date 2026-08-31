# Quickstart: Command / Undo

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK (installed).
- Sample assets under `assets/` (see `specs/006-selection-transform/quickstart.md`).

## Build & test

```sh
dotnet build
dotnet test   # includes test/CommandTests.fs
```

## Validation scenarios

1. **Spawn undo/redo** — from the Asset Browser, spawn an actor/prop; press `Ctrl+Z`
   (it disappears), then `Ctrl+Y` (it reappears at the same place).
2. **Delete undo/redo** — select an entity and press `Del`; `Ctrl+Z` restores it
   with its prior transform; `Ctrl+Y` deletes it again.
3. **Duplicate undo/redo** — select an entity and press `Ctrl+D`; `Ctrl+Z` removes
   the copy and leaves the original selected/original untouched.
4. **Transform coalescing** — select an entity, drag a translate/rotate/scale gizmo
   through many frames; a single `Ctrl+Z` returns it to its pre-drag transform.
5. **Nudge coalescing** — hold Shift and tap WASD/QE to nudge; release; a single
   `Ctrl+Z` reverts the whole nudge.
6. **Guard rails** — press `Ctrl+Z` while dragging: nothing happens. Focus a text
   field and press `Ctrl+Z`: nothing happens (typing preserved).
7. **Redo cleared** — undo, then make a new edit, then confirm `Ctrl+Y` no
   longer re-applies the pre-undo edit.
8. **Edit menu** — Edit → Undo/Redo mirror the shortcuts (and are greyed out while
   the relevant history is empty, mid-drag, or while a text field has focus).

## Expected outcome

- Spawn/delete/duplicate/transform are each undoable and redoable.
- A gizmo drag or held nudge reverts with exactly one undo.
- `dotnet test` passes.
