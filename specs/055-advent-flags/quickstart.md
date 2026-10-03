# Quickstart: Adventure Flags and the Advent Panel

## Prerequisites

- .NET 10 SDK, and `native/libcimgui.so` built (`bash native/build.sh`) for the interactive panel.
- A display for the interactive part; the headless part needs none.

## 1. Build and run the headless suite

```sh
dotnet build fsharp-game.fsproj
dotnet build test/fsharp-game.Tests.fsproj
dotnet test test/fsharp-game.Tests.fsproj
```

Expected: both projects build with **0 warnings** and every test passes, including
`test/AdventTests.fs`.

To run only this feature's cases:

```sh
dotnet test test/fsharp-game.Tests.fsproj --filter "FullyQualifiedName~AdventTests"
```

## 2. The union and the set, headlessly (a scratch F# script)

```fsharp
#r "bin/Debug/net10.0/fsharp-game.dll"
open Advent

all |> List.map name
// ["OpeningComplete"; "MetAiko"; "MetAkane"; "MetShinji"; "MetYuki"] — declaration order

let a = Set.add OpeningComplete Set.empty
Set.contains OpeningComplete a                 // true
Set.contains OpeningComplete (Set.empty : Set<Advent>) // false — immutable
Set.contains OpeningComplete (Set.remove OpeningComplete a) // false
```

## 3. The write path and its undo step (`test/AdventTests.fs`)

The suite asserts, against a real `SharedState`/`GameplayState`:

- committing `Set.add OpeningComplete Set.empty` sets `shared.Advents` and pushes exactly
  one `Advent` step whose `Previous` is empty and `Next` is that set;
- `History.undoHistory` restores the empty set and moves the step to the redo side;
  `History.redoHistory` re-applies it;
- an edit before a zone move is reached only after the move is undone — the `specs/048` shape;
- a zone switch leaves the set unchanged.

## 4. The panel, interactively

1. Launch the game, enter the editor (backtick), and find the **Advent** tab in the **right** column
   (beside **Inspector** and **Cue**). The **Inspector** is the selected tab at startup. If the Advent
   floats instead, the native library has not been rebuilt — run `bash native/build.sh` and relaunch.
2. Tick **OpeningComplete**: the milestone is recorded.
3. Untick it: it is cleared.
4. **Ctrl+Z** restores the tick; **Ctrl+Y** clears it again — the Edit menu mirrors both, on the same
   history as any zone edit around it.
5. Tick a milestone, switch zones (Outliner), and confirm it is still ticked — the set is not a zone's.
6. Relaunch: every box is unticked, because nothing is persisted (FR-010).

## 5. Adding a milestone

Add a case to `type Advent` in `world/Advent.fs` (and name it in the `name` match). The panel lists it
as a new unchecked row with no panel edit (FR-008), and the build reports any other match that must
account for it.

## 6. Expected outcomes

- SC-001/002: every milestone is listed; ticking records one and it survives a zone switch; unticking
  clears it.
- SC-003: each toggle is one history step, undone by Ctrl+Z and redone by Ctrl+Y, beside zone steps.
- SC-004: a new union case appears in the panel with no other edit.
- SC-005: both projects build with 0 warnings and the suite passes.
