# Data Model: Adventure Flags and the Advent Panel

This feature adds one union, one state field, one undo command case and its two history arms, and
one editor panel. It changes no persisted entity and no loaded-unit shape. It does **not** touch the
cue: the cue vocabulary is unchanged (FR-011), which is why no `Cue` case appears below.

## `Advent` (`world/Advent.fs`) — NEW

```fsharp
module Advent

/// A story milestone. The game's own declaration.
type Advent =
    | OpeningComplete
    | MetAiko
    | MetAkane
    | MetShinji
    | MetYuki

val all  : Advent list
val name : Advent -> string
```

- The union is closed, so a milestone cannot be misspelled and a match is exhaustive.
- The flags are a plain `Set<Advent>` and are manipulated with the standard `Set` functions; F#'s `Set`
  orders union values by case tag, so iterating it is declaration order and deterministic.
- `all` enumerates the union's cases with `FSharpType.GetUnionCases` (as `Roster.characters` does), so
  a new case appears everywhere without a second list.
- `name` is an explicit match on the cases, so adding a case fails the build until it is named.
- Depends on FSharp.Core (`Set`, reflection) alone; names no zone, screen, asset or device, so it
  loads on its own.

## `SharedState` (`state/SharedState.fs`) — CHANGED

```fsharp
type SharedState =
    { mutable Gpu : Gpu.Gpu
      Assets : Assets.AssetManager
      Cast : Casting.Cast
      ZoneRegistry : ZoneRegistry.Registry
      mutable Advents : Set<Advent>   // added: story flags, project state that outlives every zone
      Physics : Physics.World }
```

- `create` seeds it with `Set.empty`.
- `mutable`, because the state is a reference type the one history writes in place.

## `Command` (`state/Command.fs`) — CHANGED

```fsharp
| Advent of Previous : Set<Advent> * Next : Set<Advent>
```

- A **state-level** step: it writes the shared state, not a zone, so it joins `ZoneMove`, `ZoneAdd`,
  `ZoneDelete` and `SetCue` in the group `Command.apply`/`undo` refuse with `invalidOp`.
- Placed with that group, at the end of the union, after `SetCue`.

## `History` (`state/History.fs`) — CHANGED

```fsharp
// apply
| Advent (_, next) -> shared.Advents <- next
// undo
| Advent (previous, _) -> shared.Advents <- previous
```

- The two arms sit with `SetCue`'s, before the `| _ ->` fallthrough to the zone-level runner.

## `AdventPanel` (`editor/AdventPanel.fs`) — NEW

`commit : SharedState -> byref<GameplayState> -> Set<Advent> -> unit`
`draw   : SharedState -> byref<GameplayState> -> unit`

- `commit` records `next` as one step: `before = shared.Advents`, then `History.commit … (Advent
  (before, next))`. The panel keeps no history of its own.
- `draw` opens the `"Advent"` window and, for every `Advent.all`, draws a checkbox seeded with
  `Set.contains advent shared.Advents`; on a toggle it calls `commit` with `Set.add` or `Set.remove`.

## Lifecycle / state transitions

- **Tick `m` (m ∉ set)**: `next = Set.add m current`; `History.apply` writes `shared.Advents <-
  next`; `UndoStack.push` records `Advent (current, next)` and clears the redo side.
- **Untick `m` (m ∈ set)**: `next = Set.remove m current`; same path.
- **Undo**: `History.undoHistory` pops the step and writes `Previous`; the step moves to the redo side.
- **Redo**: writes `Next` again; the step moves back to the applied side.
- **Zone switch**: unrelated — the set is on `SharedState`, so it is untouched (US1/AC2).
- **New case added to the union**: `Advent.all` gains it, so the panel draws it with no panel edit
  (FR-008).

## Validation rules

- **VR-001 (FR-001)**: `all` is the union's cases in declaration order; the flag set is a `Set<Advent>`
  whose iteration is deterministic.
- **VR-002 (FR-002)**: a zone switch does not change the set (the field is not zone-scoped).
- **VR-003 (FR-007)**: each accepted toggle pushes exactly one `Advent` step whose `Previous` is the
  set before and `Next` is the set after.
- **VR-004 (FR-009)**: undo restores `Previous` exactly and redo restores `Next` exactly.
- **VR-005 (FR-010)**: nothing writes the set to disk.
- **VR-006 (FR-011)**: no `Cue` case is added; `world/Cue.fs` is unchanged.
