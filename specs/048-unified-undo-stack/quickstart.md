# Quickstart: One undo history on the gameplay state

## Prerequisites

- .NET 10 SDK (`dotnet --version` reports `10.x`).
- Nothing else: **no window, no GPU, no device**. The history is exercisable headlessly — a zone, an entity and a
  command need no device, and the suite builds its state the way `test/GameplayStateTests.fs` already does
  (`Unchecked.defaultof` for the GPU pieces, a device-free asset manager).
- `dotnet build` clean (warnings are errors in this repo).

## Automated coverage (headless)

`dotnet test` runs the extended `GameplayStateTests` alongside the rest. What it asserts, by story:

| Story / property | Assertion |
|---|---|
| 1 — one history across zones | An edit in A, a move to B and an edit in B are undone in reverse order — B's edit with B active, then the move (A active again), then A's edit — and redone in the order they were made; a move with no edit after it is stepped back on its own, with nothing else changed (0 edits lost by moving). |
| 1 — a command carries no zone | The step's undo touches the active zone only, which after the move steps is provably the zone it was recorded in; no step has to name a zone to be undone. |
| 2 — add | Adding a zone and undoing leaves the same set of zones and the same active zone (the zone active before the add); redoing adds it back and makes it active. |
| 3 — delete | A zone holding a placed prop, a waypoint, a trigger, the environment's part and a non-default light is deleted and undone: they come back **at the same refs** with the same light; redoing removes it again; deleting the *active* zone and undoing restores it **and** makes it active; and a zone's contents are asserted to carry its actors and its player designation. |
| 4 — whole-history | Undoing through the entire history returns the project to its starting zones and entities, with nothing lost or corrupted; a new edit after an undo clears the redo side. |
| preserved | Undo/redo is a no-op on an empty side and while dragging or typing; an add/delete of an invalid name or the last zone is still refused; the existing per-kind command tests are unchanged. |

**One clause is checked by hand, not headlessly.** Restoring an *actor* means building its renderer, and
`ActorRender.create` calls `SDL.CreateGPUBuffer` on the GPU device — which the device-free fixture does not have
(`test/ActorTests.fs` builds no `ActorAsset` either). The suite therefore asserts that a zone's contents **carry**
an actor and the player designation, and that `occupyZone` puts the designation back; that a restored actor appears
is walkthrough step 3, below.

## Interactive walkthrough

Open the game, press backtick to enter the editor, and use the **Zones** section at the top of the Outliner:

1. **Cross-zone undo.** In *main*, drag an actor. Add a zone `classroom`, and drag the prop you placed there.
   Press `Ctrl+Z` twice: the classroom edit reverts and `classroom` stays active; the second undo steps back to
   *main* and reverts the actor there. `Ctrl+Y` twice replays both, the active zone following.
2. **Add is undoable.** Type a name, click *Add Zone* (`atrium` exists and is active), then `Ctrl+Z`: `atrium` is
   gone and the previous zone is active again. `Ctrl+Y` brings it back.
3. **Delete is undoable.** Delete a zone that has content (`Del` beside its row), then `Ctrl+Z`: the zone returns
   with everything that was in it, and if it was the active zone it is active again.
4. **A move on its own is a step.** Click another zone and press `Ctrl+Z`: you are back in the zone you were in,
   and the camera re-frames there. Nothing else changed.

## Expected outcome

- `dotnet test` is green, including the new cross-zone, add, delete and whole-history cases.
- In the editor, one `Ctrl+Z` undoes the last thing done **wherever it was done**, and the view follows it; the
  cue panel is the one remaining surface with its own Undo/Redo buttons, because the cue is not yet on this
  history (the next feature).
- Deleting a zone and undoing restores it with its entities at the same identities, so a step that named one of
  them before the delete still resolves afterwards.
