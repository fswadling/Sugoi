# Quickstart: Commands Carry the Zone They Act On

**Feature**: `specs/077-commands-carry-zone` | **Date**: 2026-10-11

Validation guide. It proves a step acts on the place it was recorded in, that a gesture may span places, and that
single-place authoring is unchanged. See `data-model.md` for the shapes and `contracts/command-resolution.md` for the
API.

## Prerequisites

- .NET 10 SDK, at the repository root.
- Headless validation needs no window, GPU or asset files (the command/history tests are already device-free).
- The manual walkthrough needs a desktop (SDL3/ImGui) and the usual `assets/`.

## Headless validation (primary)

```sh
dotnet build          # expect: no warnings
dotnet test           # expect: green
```

What the suite must show (US1/US2/US3):

1. **A step acts on its place.** Build a two-place state. Record an entity edit in place A (its command names A).
   Switch to place B. Undo once: assert A's entity is restored, B is still `ActiveZone`, and B's camera and
   `Editor.Selection` are unchanged. Redo: assert A's edit is back and B is still active.
2. **A gesture may span places.** Build a `Batch` whose sub-commands name A and B. Apply it; assert both places
   changed. Undo once; assert both are back and `ActiveZone` never changed. Redo; assert the whole gesture returns.
3. **Delete-restore does not switch.** Delete a non-active place; undo; assert the place and its entities are present
   and `ActiveZone`, camera and selection are exactly as before.
4. **Single-place authoring is unchanged.** Re-run the existing command/edit tests (`SelectionTests`,
   `SpawningTests`, `CuePanelTests`, `GameplayStateTests`): each edit is one step and round-trips; consecutive
   transforms to one entity still merge.
5. **Coalescing is per (place, ref).** Two `Transform`s for the same ref index in two different places do **not**
   merge into one step.

These run with no device.

## Manual editor walkthrough

1. `dotnet run`; ensure **editing** is on.
2. In the Outliner, **Add** a second place (so there are two present places).
3. In the active place, move an actor with the gizmo, then rename it.
4. Click the other place in the Outliner to make it active.
5. Press **Ctrl+Z** twice: the rename and the move are taken back **in the first place**, while the second place stays
   on screen — its camera does not jump and the selection is not cleared.
6. Press **Ctrl+Y** twice: the edits return, still with the second place active.
7. Delete the first place (it is not active), then **Ctrl+Z**: the place comes back with its entities, and the place
   you are standing in, its camera and selection are untouched (no flicker).

## Expected outcomes

- An edit recorded in one place undoes in that place, whatever place is active, without disturbing the view.
- A single batch naming several places applies and reverses as one step.
- A delete-restore no longer switches the active place, even briefly.
- A single-place session is indistinguishable from before; build warning-free; suite green.

## Notes

- The scenario **fork** is the first consumer of a multi-place batch, but it is a later increment and is not built
  here; this guide exercises the capability with a hand-built batch.
- `docs/EDITOR_DESIGN.md` (D-011) and `docs/ARCHITECTURE.md` are updated so no document still says a command carries
  no zone.
