# Contract: Scenario Fork

**Feature**: `specs/078-fork-scenario` | **Date**: 2026-10-11

The interfaces this feature introduces or changes, and the behaviour each must exhibit. Signatures are
F#-style; "raises" is named where a programming bug may, in line with the project's fail-loud rule.

## 1. `ScenarioFork.materialise` — the shared materialiser

```fsharp
// world/ScenarioFork.fs  (compiled after state/GameplayState.fs, before world/CueEngine.fs)
val materialise :
    shared: SharedState.SharedState
    -> state: byref<GameplayState.GameplayState>
    -> contents: ScenarioRegistry.ScenarioContents
    -> Command list option
```

**Preconditions**

- `contents.Zones` names declared places (`Place` is closed) — every key is a valid place.
- Each `ActorState.AssetId` names a loadable actor asset; each waypoint `Next = Some i` indexes within that zone's own `Waypoints` array. A violation is authored-data corruption → `raise` (a bug), as `Zone.loadTemplate` and `ScenarioZone.capture` already do.

**Postconditions on `Some commands`**

- Every absent place in `contents.Zones` is now present in `state.Zones`, populated from the Zone Registry's template when one is registered, else empty. **No** place was activated: `state.ActiveZone` is unchanged.
- Every scenario zone's actors, waypoints and triggers have been appended to their place's zone (duplicates allowed). Waypoint chains are re-linked among the appended waypoints.
- An appended actor in a non-active place has its body detached (`Physics.removeBody`, guarded by `Physics.isAdded`) — it does not simulate.
- `commands`, applied in order, reproduce exactly these mutations; `History.undo` of it removes the entities and any created place. `commands` may be empty.

**Postconditions on `None`**

- **Nothing** changed: no place created, no entity appended, `state.ActiveZone` unchanged. The reason has been reported on stderr.

**Invariants**

- Never calls `GameplayState.switchZone`/`selectZone`; never writes `state.Editor.Selection`, the camera, or `state.Cue`.
- Idempotence is **not** offered: a second call appends a second copy.

## 2. `Command.ZonePresent` — the presence step

```fsharp
// state/Command.fs
| ZonePresent of Name : Place

// state/Command.fs
val placeOf : Command -> Place option
//   ZonePresent _ -> None   (joins ZoneMove, ZoneAdd, ZoneDelete, SetCue, Advent, SetScenario)

// state/History.fs
//   apply: | ZonePresent (_, name) -> ... presentZone with the registry's template ...
//   undo:  | ZonePresent (_, name) -> GameplayState.deleteZone shared &state name
```

**Behaviour**

- **apply**: if `name` is already present → `invalidOp` (LIFO means it cannot be; the command is only ever minted for an absent place). Otherwise `GameplayState.presentZone shared &state name (ZoneRegistry.tryTemplate name state.ZoneRegistry)`.
- **undo**: `GameplayState.deleteZone shared &state name`. Valid because a `ZonePresent` never names the active place, and by LIFO the active place is not this one when its undo runs.
- The zone-level runner (`Command.apply`/`Command.undo`) must treat it as a state-level step and `invalidOp` if it reaches there.

## 3. `Cue.ForkScenario` — the runtime op

```fsharp
// world/Cue.fs
| ForkScenario of scenario: Scenario

// world/CueEngine.fs — the `advance` arm
| ForkScenario scenario ->
    // registered: materialise (discarding the reversal) then Fork the cue; else a diagnostic no-op
```

**Behaviour**

| State | Result of `advance` | Side effect |
|-------|---------------------|-------------|
| `tryContents scenario` → `None` | `ValueSome Fin` | `eprintfn "cue: ForkScenario(%s) performs nothing: it is not registered"` (the `noOp` convention) |
| registered, `materialise` → `None` | `ValueSome Fin` | the capacity reason was already logged by `materialise`; nothing forked |
| registered, `materialise` → `Some _` | `ValueSome (Fork contents.Cue)` | scene materialised |

- The op performs a **runtime effect**: the returned command list is discarded; nothing is pushed to the undo history (FR-006).
- Returning `Fork _` reuses `stepSequenceNext`'s existing "run beside the rest" handling; no new control flow. Like `Fork`, it is only meaningful at a `Sequence`'s current op.

## 4. `CueTree` / `CueEditor` — the op's controls

```fsharp
// editor/CueTree.fs
//   kinds: a "Scenarios" group, [ "Fork scenario" ]
//   kindName: | ForkScenario _ -> "Fork scenario"
//   defaultOfKind: | "Fork scenario" -> ForkScenario (List.head Scenario.all)
//   label:    | ForkScenario s -> "Fork scenario: " + Scenario.scenarioName s
//   children: | ForkScenario _ -> []
//   placeOf:  | ForkScenario _ -> ValueNone
val withScenario : scenario: Scenario -> cue: Cue -> Cue    // write the chooser commits

// editor/CueEditor.fs
//   a filtered combo over Scenario.all (by scenarioName), committing
//   (CueTree.withAt path (CueTree.withScenario s node) cue)
```

**Behaviour**

- The scenario is **chosen**, not typed, from the closed declared set (FR-011) — the `filteredCombo` idiom every other closed set uses (`Place.all`, `Advent.all`).
- `withScenario` on a node that is not a `ForkScenario` → `invalidOp` (a bug), like `withPlace`/`withTrigger`.
- Adding `ForkScenario` to the closed `Cue` union means every exhaustive match gains an arm; the compiler reports them.

## 5. `ScenarioPanel` — the editor fork control

```fsharp
// editor/ScenarioPanel.fs
val fork : SharedState.SharedState -> byref<GameplayState.GameplayState> -> Scenario -> unit
```

**Behaviour**

- Unregistered scenario → does nothing (the button is drawn disabled, FR-008).
- Registered → one step on the one history: the materialiser's returned commands plus, when the scenario's cue is not `Fin`, a `SetCue (before, forkInto contents.Cue before)`, wrapped in one `Batch`, applied by the materialiser/direct write and pushed **without** re-applying.
- Undo removes the entities and any created places and restores the cue; redo reapplies all of it (FR-007). The active place, camera and selection are unchanged (FR-004).
- A refused fork (no capacity) does nothing at all.
- Drawn per Scenario row, beside Register and Clear, both `sameLine`d and id-scoped by `pushId (scenarioName scenario)`.

## 6. `History.pruneCueSteps` — reaching into a batch

```fsharp
// state/History.fs
//   Before: keep (SetCue _) = false
//   After:  recurse into Batch, dropping a nested SetCue and keeping the rest;
//           a batch emptied by the drop is itself dropped.
```

**Behaviour**

- Entering Play removes every cue effect from both sides of the history — including the `SetCue` nested in a fork's `Batch` — while the `ZonePresent`/`*Create` commands in that batch stay undoable (FR-012).
- A non-`Batch`, non-`SetCue` step is kept exactly as before.

## 7. `SlotMap.Capacity`

```fsharp
// core/SlotMap.fs
member _.Capacity : uint32 = uint32 slotCount
```

**Behaviour**

- Returns the fixed capacity the map was constructed with. Free room = `Capacity - Count`. No behaviour changes elsewhere.

## 8. Non-contract (explicitly out of scope — FR-013)

- No `InlineScenario`, no `ClearScenario`.
- No camera-follow or interaction toggles.
- No persistence of the scenario library (ROADMAP phase 15).
- No change to what a scenario captures (`specs/076`) or to the command layer beyond `ZonePresent` (`specs/077`).
