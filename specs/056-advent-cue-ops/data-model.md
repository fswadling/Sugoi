# Data Model: Advent Cue Ops

## `world/Cue.fs` — changed

```fsharp
// added to the existing `Cue` union; `open Advent`
| AddAdvent of advent: Advent                              // no progress of its own
| RemoveAdvent of advent: Advent                           // no progress of its own
| Await of advent: Advent                                  // blocks until the flag is set
| AwaitState of advent: Advent                             //   — in flight: the begun form (FR-006)
| If of advent: Advent * thenCue: Cue * elseCue: Cue       // choose a branch
```

- **`AddAdvent`/`RemoveAdvent` are the no-progress shape** (`specs/046` FR-007): each holds nothing in flight, so
  neither has a `…State` sibling.
- **`Await`/`AwaitState` are a condition**: the authored form is `Await m`; the begun form is `AwaitState m`. The
  state case carries the same `Advent` — it is the **begun-marker exception** to the `…State` rule (research D3),
  there so the panel can tell a running await from a queued one.
- **`If` is a two-branch control-flow op**: `children (If (_, t, e)) = [ t; e ]`, `thenCue` first.
- **The milestone is the closed `Advent` union**, not a string (research D1), so an unknown milestone is
  unrepresentable.
- **`Cue.fs` widens its dependency note by one pure module** (`Advent`); it still names no zone, screen, asset or
  device.

## `world/CueEngine.fs` — changed

```fsharp
| AddAdvent m -> shared.Advents <- Set.add m shared.Advents; ValueSome Fin
| RemoveAdvent m -> shared.Advents <- Set.remove m shared.Advents; ValueSome Fin
| Await m -> if Set.contains m shared.Advents then ValueSome Fin else ValueSome (AwaitState m)
| AwaitState m -> if Set.contains m shared.Advents then ValueSome Fin else ValueNone
| If (m, thenCue, elseCue) -> advance shared &state now (if Set.contains m shared.Advents then thenCue else elseCue)
```

- **The write ops effect `SharedState.Advents`** where the walk reaches them — it is mutable project state the walk
  already carries (as `SwitchZone` reuses `shared`), so no new parameter and no new field.
- **`Await`'s begin** either finishes at once (already set) or becomes `AwaitState`; its in-flight step re-checks and
  reports `ValueNone` while waiting (0 bytes).
- **`If` is a tail call to `advance` on the chosen branch**, so the branch runs in the `If`'s place; a `Fin` branch
  costs no time. No `IfState`.
- **`advance`'s signature and `Cue voption` answer are unchanged.** `open Advent`.

## `editor/CueTree.fs` — changed

| Surface | Change |
|---|---|
| `kinds` | adds `"Add advent"`, `"Remove advent"`, `"Await"`, `"If"` |
| `kindName` | `AddAdvent _ -> "Add advent"`; `RemoveAdvent _ -> "Remove advent"`; `Await _ \| AwaitState _ -> "Await"`; `If _ -> "If"` |
| `defaultOfKind` | each made with `List.head Advent.all` as the milestone; `If` with two `Fin` branches |
| `label` | `"Add: " + name m`, `"Remove: " + name m`, `"Await: " + name m`, `"If: " + name m` |
| `children` | `If (_, t, e) -> [ t; e ]`; the other four have none |
| `rows` / `drawTree` | an `If`'s two branches are each headed by a `then`/`else` label row — a `Row` whose `Branch` is the name and which carries no node — shown as text and not selectable; the branch is indented beneath its label |
| `executing` | `AwaitState _` is executing; `Await _`, `AddAdvent _`, `RemoveAdvent _`, `If _` are not |
| `drawEditor` | a milestone chooser over `Advent.all` for all four (`If` also shows its branch rows through the tree) |
| `textOf`/`withText` | unchanged — none of the four carries text |

## State transitions (an await)

| From | Event | To |
|---|---|---|
| `Await m` | reached, `m` set | `Fin` (over before it begins) |
| `Await m` | reached, `m` clear | `AwaitState m` |
| `AwaitState m` | a step, `m` still clear | the same value; `ValueNone` |
| `AwaitState m` | a step, `m` now set | `Fin` |
| `Await m`/`AwaitState m` | `m` cleared again | unchanged — membership is re-read, clearing is not remembered |

## Validation rules

| Rule | Where it is enforced |
|---|---|
| A milestone cannot be unknown | the type — `Advent` is a closed union (`specs/055`) |
| `AddAdvent` of a set flag, `RemoveAdvent` of a clear one | not an error: an idempotent no-op (research D2) |
| A cue writing a flag is not an undoable edit | the kind of step: it happens in `advance`, and no `Command` is made (FR-011) |
| `Await`/`If` anywhere in a cue | legal — they are ordinary ops; a `Fork` outside a `Sequence`'s front is still the only malformed shape |

## Deliberately not modelled

- **A trigger setting a flag** — trigger→cue wiring is phase 10; `Await` is satisfied by *anything* that sets the flag,
  including the panel today.
- **A scenario reading flags** — phase 11.
- **Persisting the flags** — phase 15.
