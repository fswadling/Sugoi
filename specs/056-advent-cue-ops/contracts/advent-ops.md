# Contract: Advent Cue Ops

The surfaces this feature adds or changes, and what explicitly does not change.

## `world/Cue.fs` — CHANGED

```fsharp
open Advent

type Cue =
    // ... existing cases ...
    | AddAdvent of advent: Advent
    | RemoveAdvent of advent: Advent
    | Await of advent: Advent
    | AwaitState of advent: Advent
    | If of advent: Advent * thenCue: Cue * elseCue: Cue
```

**Contract**: the cue is still the run, and the four ops are ordinary cases of it. The events carry the closed
`Advent` union, so this module now `open`s `world/Advent.fs` — a pure module compiled earlier — and still names no
zone, screen, asset or device. `AddAdvent`/`RemoveAdvent` add no `…State`; `Await` gains `AwaitState` (the
begun-marker exception, research D3); `If` has two cue children (FR-001, FR-002, FR-006).

## `world/CueEngine.fs` — CHANGED

```fsharp
| AddAdvent advent -> shared.Advents <- Set.add advent shared.Advents; ValueSome Fin
| RemoveAdvent advent -> shared.Advents <- Set.remove advent shared.Advents; ValueSome Fin
| Await advent -> if Set.contains advent shared.Advents then ValueSome Fin else ValueSome (AwaitState advent)
| AwaitState advent -> if Set.contains advent shared.Advents then ValueSome Fin else ValueNone
| If (advent, thenCue, elseCue) ->
    advance shared &state now (if Set.contains advent shared.Advents then thenCue else elseCue)
```

**Contract**: `advance` is unchanged in kind — one cue in, the cue as it now stands out (or `ValueNone` while an await
waits). The write ops effect `SharedState.Advents` where the walk reaches them; this is the only new place the engine
writes shared *project* state, and it reuses the field the `Advent` undo step already writes. A waiting step writes
nothing and reports `ValueNone`, so it allocates nothing per step. `If` is a tail call to `advance` on the chosen
branch, so no `IfState` and no shape change to the walk (FR-003, FR-004, FR-005, FR-007, FR-008).

## `editor/CueTree.fs` — CHANGED

Adds the four kinds, a milestone chooser over the closed list `Advent.all`, an `If`'s two child rows each headed by a
`then`/`else` label row (an `If`'s branches would otherwise be indistinguishable), labels (`"Add: …"`, `"Remove: …"`,
`"Await: …"`, `"If: …"`), and the lock rule (`AwaitState` executing; the other three and the authored `Await` not).
The label is a `Row` with its `Branch` set; it carries no node, so it is shown but not selectable. `textOf`/`withText`
are unchanged.

**Contract**: the control still reads no game state — `Advent.all` is a closed list, so a chooser over it needs no
zone and no registry (unlike a zone/waypoint name). It now also names `Advent` (a pure module) (FR-010).

## `test/CueTests.fs`, `test/CuePanelTests.fs` — CHANGED

**Contract**: `CueTests` gains the four ops' scenarios and the 0-byte waiting step, asserted against a `SharedState`'s
flag set (FR-009, FR-012); `CuePanelTests` gains the four kinds in its tables. Neither opens a device.

## `docs/ROADMAP.md` — CHANGED

Phase 10's deliverable line names the advent cue ops as landed (with advents, `specs/055`).

## Explicitly unchanged

- **`world/Advent.fs`** — the closed union and its names; this feature consumes them.
- **`state/SharedState.fs`** — `Advents : Set<Advent>` already exists and is mutable; the ops write it in place.
- **`state/Command.fs`, `state/History.fs`** — a running cue's flag writes are not authored edits, so no command is
  added or changed. The panel's `Advent` step is untouched (FR-011).
- **`world/Systems.fs`, `screen/GameplayScreen.fs`** — the play chain is unchanged; the cue still ticks last.
- **`state/GameplayState.fs`** — no new field.
- **`fsharp-game.fsproj`** — `world/Advent.fs` is already compiled before `world/Cue.fs`; no new entry.
- **The other op families** — triggers, scenarios, fades, actor movement, animation blends stay deferred.
