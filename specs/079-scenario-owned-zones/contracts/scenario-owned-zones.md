# Contract: Scenario-Owned Zones

**Feature**: `specs/079-scenario-owned-zones` | **Date**: 2026-10-11

The interfaces this feature introduces, changes or retires, and the behaviour each must exhibit. Signatures are
F#-style; "raises" is named where a programming bug may, per the project's fail-loud rule.

## 1. A zone id is a `string` — everywhere

```fsharp
// state/GameplayState.fs
Zones      : Dictionary<string, Zone.Zone>
ActiveZone : string
let startZone : string = "start"     // replaces Place.start

// state/Command.fs — the first field of every content command, and the zone ops, are `string`
| Transform of ZoneId : string * Ref : EntityRef * Before : Transform * After : Transform
| ZoneMove of Previous : string * Next : string
| ZoneAdd of Previous : string * Name : string
| ZoneDelete of Previous : string * Name : string * Contents : ZoneContents
```

**Behaviour.** Identical to today, keyed by a name rather than a `Place`. Total: any string is a valid id; an id that
names no present zone is "absent" (an expected state, not an error). A command naming an absent zone still raises (the
present/undo invariant is unchanged from `specs/077`).

## 2. `ScenarioZone` carries staging

```fsharp
// world/ScenarioZone.fs
type ScenarioZone =
    { Staging : ZoneStaging
      Actors : ActorState array
      Waypoints : ScenarioWaypoint array
      Triggers : TriggerState array }

val capture : Zone -> ScenarioZone      // Staging = Zone.captureTemplate zone (None when no environment)
```

**Behaviour.** `capture` reads the live zone and nothing else (device-free). `Staging` is `None` for a zone with no
loaded environment. `ScenarioContents.Zones : Map<string, ScenarioZone>`.

## 3. `Command.ZonePresent` and `Command.ZoneStage`

```fsharp
// state/Command.fs
| ZonePresent of Name : string * Staging : ZoneStaging
| ZoneStage   of Name : string * Previous : ZoneStaging * Next : ZoneStaging
```

| Command | `placeOf` | apply | undo |
|---------|-----------|-------|------|
| `ZonePresent (name, staging)` | `None` | `GameplayState.presentZone shared &state name staging` | `GameplayState.deleteZone shared &state name` |
| `ZoneStage (name, previous, next)` | `None` | `GameplayState.stageZone shared &state name next` | `GameplayState.stageZone shared &state name previous` |

**Behaviour.** Both are **state-level**: the zone-level `Command.apply`/`Command.undo` must `invalidOp` if either
reaches them. `ZonePresent.apply` on an already-present zone `invalidOp`s (LIFO guarantees it cannot). `ZoneStage`
requires the named zone to be present (`invalidOp` otherwise — a bug).

## 4. `GameplayState.stageZone` — the one restager

```fsharp
// state/GameplayState.fs  (absorbs editor/Spawning.instantiateZone, which is deleted)
val stageZone :
    shared: SharedState.SharedState
    -> state: byref<GameplayState>
    -> name: string
    -> staging: ZoneStaging
    -> unit
```

**Preconditions.** `name` is present (`invalidOp` otherwise).

**Postconditions.**

- `Some template`: the zone's environment is the template's (streamed in if not resident), its placed props are the
  template's (old props freed, new placed), its light is the template's, its parts are the template's.
- `None`: the zone has no environment, no placed props, and `Texture.defaultLight`.
- The zone's bodies simulate **iff** it is the active zone: after the swap it is detached (`Zone.deactivatePhysics`)
  when `name <> state.ActiveZone`.
- The active place, camera and selection are unchanged; `Zone.Player` is not touched.

## 5. `ScenarioFork.materialise` — ensure, restage, append

```fsharp
// world/ScenarioFork.fs  (compiled after state/GameplayState.fs, before world/CueEngine.fs)
val materialise :
    shared: SharedState.SharedState
    -> state: byref<GameplayState>
    -> contents: ScenarioRegistry.ScenarioContents
    -> Command list option
```

**Preconditions.** `contents.Zones` keys are zone names; an `ActorState.AssetId` names a loadable actor asset; a
waypoint `Next = Some i` indexes within the zone's own `Waypoints` (a violation raises — authored-data corruption).

**Postconditions on `Some commands`.**

- Every recorded zone is present: a previously-absent one was created and staged from the scenario; a present one was
  **reused** (never duplicated) and its staging **replaced** by the scenario's (unless it already matched). No place
  was activated: `state.ActiveZone` is unchanged.
- Every recorded zone's narrative entities were appended (duplicates allowed). Bodies built into an inactive zone are
  detached.
- `commands` reverse it exactly: `ZonePresent` for created zones, `ZoneStage` for restaged ones, `*Create` for the
  entities. `commands` may be empty.

**Postconditions on `None`.** Nothing changed at all (no zone created/restaged, no entity/prop added); the reason was
logged on stderr.

## 6. Cue ops

```fsharp
// world/Cue.fs — RETIRED: AddZone, PreloadZone
// KEPT (now string-keyed):
| SetZone of place: string
| RemoveZone of place: string
| ForkScenario of scenario: Scenario     // unchanged
```

**Behaviour.** `SetZone` switches to a present zone (no-op if absent); `RemoveZone` makes a present, non-active zone
absent (no-op otherwise). `ForkScenario` is the runtime effect from `specs/078` (materialise, then `Fork contents.Cue`).

## 7. Editor surfaces

```fsharp
// editor/Outliner.fs
//   list = state.Zones keys (present zones), with the residency marker
//   "Add place" = a typed name committing ZoneAdd(active, name) (guarded: non-empty, not present)

// editor/CueTree.fs
//   the zone chooser is a typed name field (as before specs/061); `Place.all` is gone
//   `kinds` no longer offers "Add zone" / "Preload zone"

// editor/ZoneRegistryPanel.fs  — DELETED; editor/Editor.fs no longer draws it
```

**Behaviour.** The active zone's environment is still set from the Asset Browser (`SetEnvironment`), and its light in
the Inspector — both unchanged. `docs/EDITOR_DESIGN.md`'s D-023 (the Zone Registry panel) is recorded as retired.

## 8. Non-contract (explicitly out of scope — FR-015 / spec)

- Persistence of the scenario library (`docs/ROADMAP.md` phase 15).
- Odin's `InlineScenario`/`ClearScenario` and the camera-follow/interaction toggles.
- Any boot/loading-screen scene assembly (boot still creates the empty `startZone`).
