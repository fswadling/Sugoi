# Quickstart: The cue's edits ride the one undo history

## Prerequisites

- .NET 10 SDK (`dotnet --version` reports `10.x`).
- Nothing else: **no window, no GPU, no device**. The whole feature is exercisable headlessly — a cue, a
  `SetCue` step and the running cue's write path need no device, and the suite builds its state the way
  `test/CuePanelTests.fs` and `test/GameplayStateTests.fs` already do (`Unchecked.defaultof` for the GPU
  pieces, a device-free asset manager).
- `dotnet build` clean (warnings are errors in this repo).

## Automated coverage (headless)

`dotnet test` runs the adapted `CuePanelTests` and the extended `GameplayStateTests` alongside the rest. What
it asserts, by story:

| Story / property | Assertion |
|---|---|
| 1 — the panel records on the one history | `CueEditor.commit` changes `state.Cue` once and leaves exactly **one** `SetCue` step on `state.Editor.Undo`; `History.undoHistory` restores the earlier cue and `History.redoHistory` re-applies the later one; an undo with nothing applied is a no-op and leaves the cue alone. |
| 1 — one undo covers the cue and the scene (SC-001) | A cue edit is followed by a `Transform` on an actor placed directly in the headless zone; two `History.undoHistory` calls revert the transform first and the cue second, and two `History.redoHistory` calls re-apply both in the order made. |
| 1 — a cue step touches the cue and nothing else (FR-006) | Undoing a cue step leaves the active zone, the camera and the panel's selection exactly as they were. |
| 2 — the panel has no undo of its own (SC-002) | The compiled `CueEditor` type exposes no `undo`, `redo`, `canUndo` or `canRedo`; the Edit menu and the shortcut keys are the only entry points, and their gating already reads the one history. |
| 3 — Play drops only what went stale (SC-003, FR-010) | With a `SetCue` step and a zone step on the history, `History.pruneCueSteps` leaves **0** cue steps on both sides and the zone step still undoable. |
| 4 — whole history (SC-004) | Undoing through the entire history returns the project to its starting state, cue included; a new edit after an undo clears the redo side, including when that edit is a cue edit. |

**No clause needs a by-hand check.** Unlike an actor's *restore* (which builds a renderer and needs the GPU,
spec 048's one manual clause), a cue step and a `Transform` over a body-less actor touch no resource — the
actor is placed with `zone.Actors.Add` and its renderer stays `Unchecked.defaultof`, which is how
`GameplayStateTests` already drives entity commands.

## Interactive walkthrough

Open the game, press backtick to enter the editor, and work in the **Cue** panel (right column, beside the
Inspector):

1. **One undo across the panel and the scene.** In the Cue panel, select a node and change its kind or its
   text. Then click an actor in the viewport and drag it. Press `Ctrl+Z` twice: the actor returns first, then
   the cue edit. `Ctrl+Y` twice replays both, in the order they were made. The Edit menu's Undo/Redo items are
   enabled and disabled in step with the keys.
2. **No second undo.** Confirm the panel has no Undo or Redo button — it never did anything the keyboard and
   the menu do not, and drawing them invited two mental models. With the panel focused, `Ctrl+Z` still reverts
   the last cue edit.
3. **Play keeps the zone edits.** Make a cue edit and a zone edit (say, the active zone's light), press
   backtick to play, then backtick to return. `Ctrl+Z` reverts the zone edit; there is no cue step left to
   take back, because the cue advanced while playing.
4. **A cue step reverts nothing else.** Undo a cue edit: the active zone, the camera and the panel's
   selection do not move, and a selection that no longer names a node simply shows as no selection.

## Expected outcome

- `dotnet test` is green, including the adapted cue-panel cases and the new prune and cross-edit cases.
- In the editor there is exactly **one** undo: `Ctrl+Z`/`Ctrl+Y` and the Edit menu take back and re-apply cue
  edits and scene edits together, in the order they were made, and the Cue panel draws no Undo or Redo
  control of its own.
- After a play, every zone edit made beforehand is still undoable, and no cue edit is.
