# Contract: Adventure Flags and the Advent Panel

The surfaces this feature adds or changes, and what explicitly does not change.

## `world/Advent.fs` — NEW

```fsharp
module Advent

type Advent =
    | OpeningComplete
    | MetAiko
    | MetAkane
    | MetShinji
    | MetYuki

val all  : Advent list
val name : Advent -> string
```

- The flags are a plain `Set<Advent>` and are manipulated with the standard `Set` functions
  (`Set.empty`, `Set.contains`, `Set.add`, `Set.remove`); the module adds only `all` and `name`.
- `all` returns the union's cases in declaration order (enumerated from the type), which is the
  deterministic iteration the panel and the suite rely on (FR-001, FR-008).
- `name` is an explicit match, so adding a case fails the build until it is named.

## `state/SharedState.fs` — CHANGED

```fsharp
type SharedState =
    { mutable Gpu : Gpu.Gpu
      Assets : Assets.AssetManager
      Cast : Casting.Cast
      ZoneRegistry : ZoneRegistry.Registry
      mutable Advents : Set<Advent>
      Physics : Physics.World }
```

(`SharedState.fs` opens `Advent`, so it writes `Set<Advent>` for the type.)

- `create` gains `Advents = Set.empty`; every other construction (the five test fixtures) gains the
  same field.

## `state/Command.fs` — CHANGED

- A new case on `Command`:

  ```fsharp
  | Advent of Previous : Set<Advent> * Next : Set<Advent>
  ```

- `Command.apply`/`undo` **refuse** it: it is added to the `invalidOp` arm that already holds
  `ZoneMove`/`ZoneAdd`/`ZoneDelete`/`SetCue`, because the zone-level runner takes no `SharedState`.
- `UndoStack.push`'s coalescing is unchanged (only `Transform` merges); an `Advent` step never merges.

## `state/History.fs` — CHANGED

- `apply` gains `| Advent (_, next) -> shared.Advents <- next`.
- `undo` gains `| Advent (previous, _) -> shared.Advents <- previous`.
- Both sit with `SetCue`'s arms, before the `| _ ->` fallthrough. `commit`, `pruneCueSteps`, and the
  two steppers are unchanged: an `Advent` step is not a cue step, so Play's pruning leaves it alone,
  exactly as it leaves a zone step alone.

## `editor/AdventPanel.fs` — NEW

- `commit : SharedState.SharedState -> byref<GameplayState.GameplayState> -> Set<Advent> -> unit` —
  the write path, one `Advent` step via `History.commit` (FR-007).
- `draw : SharedState.SharedState -> byref<GameplayState.GameplayState> -> unit` — opens the window
  named exactly `"Advent"` (the native dock line matches the string) and draws one checkbox per
  `Advent.all`, seeded with `Set.contains`; a toggle calls `commit` with `Set.add` or `Set.remove`
  (FR-004, FR-005, FR-006).

## `editor/Editor.fs` — CHANGED

- `panelNames` gains `"Advent"` (after `"Cast"`), so Ctrl+Tab reaches it.
- The draw order changes: `Inspector.draw` is submitted **first** among the right node's panels and the
  docking-init frame queues `PendingFocus <- Some "Inspector"`, so the Inspector is both the leftmost and the
  selected tab when the editor opens (a node's tab order is its windows' submission order; its selected tab
  defaults to the last submitted — revised 2026-10-03 from "call it last"). `AdventPanel.draw` is called after
  `CastPanel.draw`.

## `native/cimgui_sdl3.cpp` — CHANGED

One line, in `cimgui_setup_dock_layout`, beside the `"Cue"` line:

```cpp
ImGui::DockBuilderDockWindow("Advent", right);
```

`libcimgui.so` is rebuilt with `bash native/build.sh`; no new shim function is added. Until the rebuild
lands, the window floats.

## `fsharp-game.fsproj` — CHANGED

- `Compile Include="world/Advent.fs"` after `world/EnvironmentRender.fs`.
- `Compile Include="editor/AdventPanel.fs"` after `editor/CastPanel.fs`.

## `test/` — CHANGED

- `AdventTests.fs` (NEW) and one `Compile` entry in `test/fsharp-game.Tests.fsproj`.
- `CueTests.fs`, `CuePanelTests.fs`, `GameplayStateTests.fs`, `SelectionTests.fs`, `SpawningTests.fs`
  each gain `Advents = Set.empty` in their `SharedState` fixture.

## Explicitly unchanged

- **`world/Cue.fs` and `world/CueEngine.fs`** — no `AddAdvent`/`RemoveAdvent`/`Await`/`If` case is
  added, and the engine is untouched (FR-011; `specs/046`'s contract).
- **`state/EditorState.fs`** — no new field: with a closed union the panel needs no text buffer, so
  the first cut's `AdventNameBuffer` was removed on revision.
- **`state/History.fs`'s `pruneCueSteps`** — an `Advent` step is not a cue step and is not pruned.
- **Persistence** — nothing is written to disk (FR-010).
- **`world/Zone.fs`, `world/Systems.fs`, the render modules, and the screens** — untouched.
