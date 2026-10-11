# Data Model: Commands Carry the Zone They Act On

**Feature**: `specs/077-commands-carry-zone` | **Date**: 2026-10-11

No new entities. One field is added to an existing union, one helper is added, and two rules change (how a step finds
its zone, and the delete-restore path). Field names and types match the code so `/speckit-tasks` can act directly.

## As-is (`state/Command.fs`)

```fsharp
type Command =
    | Transform of Ref : EntityRef * Before : Transform * After : Transform
    | Rename of Ref : EntityRef * Before : string * After : string
    | ActorCreate of Ref : EntityRef * Actor : ActorState
    | ActorDelete of Ref : EntityRef * Actor : ActorState
    | PropCreate  of Ref : EntityRef * Prop : PropState
    | PropDelete  of Ref : EntityRef * Prop : PropState
    | PartCreate  of Ref : EntityRef * Part : PropState
    | PartDelete  of Ref : EntityRef * Part : PropState
    | WaypointCreate of Ref : EntityRef * Waypoint : Waypoint
    | WaypointDelete of Ref : EntityRef * Waypoint : Waypoint
    | TriggerCreate of Ref : EntityRef * Trigger : TriggerState
    | TriggerDelete of Ref : EntityRef * Trigger : TriggerState
    | Link of Ref : EntityRef * Before : EntityRef option * After : EntityRef option
    | TriggerSet of Ref : EntityRef * Before : TriggerState * After : TriggerState
    | LinkSpawn of From : EntityRef * Before : EntityRef option * Ref : EntityRef * Waypoint : Waypoint
    | SetEnvironment of Previous : Environment option * Next : Environment option
    | SetLight of Previous : LightSettings * Next : LightSettings
    | SetPlayer of Previous : Handle voption * Next : Handle voption
    | Batch of Commands : Command list
    | ZoneMove of Previous : Place * Next : Place
    | ZoneAdd of Previous : Place * Name : Place
    | ZoneDelete of Previous : Place * Name : Place * Contents : ZoneContents
    | SetCue of Previous : Cue.Cue * Next : Cue.Cue
    | Advent of Previous : Set<Advent> * Next : Set<Advent>
    | SetScenario of Name : Scenario * Previous : ScenarioContents option * Next : ScenarioContents option
```

`Command.apply`/`undo` take `(zone: Zone) (physics) (assets) cmd`; `History` passes the **active** zone to every
content command.

## To-be

```fsharp
type Command =
    // ... every zone-content case gains `Place` as its first field, e.g.:
    | Transform of Place : Place * Ref : EntityRef * Before : Transform * After : Transform
    | Rename of Place : Place * Ref : EntityRef * Before : string * After : string
    | ActorCreate of Place : Place * Ref : EntityRef * Actor : ActorState
    | ActorDelete of Place : Place * Ref : EntityRef * Actor : ActorState
    | PropCreate  of Place : Place * Ref : EntityRef * Prop : PropState
    | PropDelete  of Place : Place * Ref : EntityRef * Prop : PropState
    | PartCreate  of Place : Place * Ref : EntityRef * Part : PropState
    | PartDelete  of Place : Place * Ref : EntityRef * Part : PropState
    | WaypointCreate of Place : Place * Ref : EntityRef * Waypoint : Waypoint
    | WaypointDelete of Place : Place * Ref : EntityRef * Waypoint : Waypoint
    | TriggerCreate of Place : Place * Ref : EntityRef * Trigger : TriggerState
    | TriggerDelete of Place : Place * Ref : EntityRef * Trigger : TriggerState
    | Link of Place : Place * Ref : EntityRef * Before : EntityRef option * After : EntityRef option
    | TriggerSet of Place : Place * Ref : EntityRef * Before : TriggerState * After : TriggerState
    | LinkSpawn of Place : Place * From : EntityRef * Before : EntityRef option * Ref : EntityRef * Waypoint : Waypoint
    | SetEnvironment of Place : Place * Previous : Environment option * Next : Environment option
    | SetLight of Place : Place * Previous : LightSettings * Next : LightSettings
    | SetPlayer of Place : Place * Previous : Handle voption * Next : Handle voption
    // unchanged: Batch, ZoneMove, ZoneAdd, ZoneDelete, SetCue, Advent, SetScenario
    | Batch of Commands : Command list
    | ZoneMove of Previous : Place * Next : Place
    // ...
```

```fsharp
/// The place a content command acts on, or `None` for a state-level step or a `Batch` (whose sub-commands each
/// name their own). The state-level runner resolves `Some place` to its zone before handing the command to the
/// zone-level runner.
let placeOf (cmd: Command) : Place option
```

### Changed resolution rule (`state/History.fs`)

```fsharp
let rec apply shared (state: byref<GameplayState>) cmd =
    match cmd with
    | ZoneMove (_, next) -> ...
    | Batch commands -> for c in commands do apply shared &state c
    | SetCue (_, next) -> ...
    | // ... the other state-level cases ...
    | _ ->
        let place = Command.placeOf cmd |> Option.defaultWith (fun () -> failwith "…")   // a content command
        match state.Zones.TryGetValue place with
        | true, zone -> Command.apply zone state.Physics shared.Assets cmd
        | false, _ -> failwithf "a step names an absent place '%s'" (Place.placeName place)
```

`undo` mirrors it (backwards for `Batch`).

### Changed delete-restore path (`state/History.fs`, `ZoneDelete` undo)

```fsharp
| ZoneDelete (previous, name, contents) ->
    state.Zones.[name] <- Zone.create ()
    Zone.occupyZone state.Zones.[name] state.Physics shared.Assets contents
    Zone.deactivatePhysics state.Zones.[name] state.Physics   // the zone is not active: detach what was built
    // no switch: `previous` is already the active place
```

### Changed coalescing rule (`state/Command.fs`, `UndoStack.push`)

```fsharp
match prev, newCmd with
| Transform (pa, a, before, _), Transform (pb, b, _, after) when a = b && pa = pb ->
    Some (Transform (pa, a, before, after))
| _ -> None
```

## Validation rules

- **FR-002**: apply/undo of a content command act on the zone at `cmd`'s place, never the active place.
- **FR-004**: no content command's apply/undo changes `state.ActiveZone` or the camera/selection.
- **FR-005**: a `Batch` may mix places; forward order applies each, reverse reverses each.
- **Fail loud**: an absent named place raises; a mismatched part epoch raises (unchanged).
- **FR-011**: for a single-place session, the number of steps and their round-trips are unchanged.

## Unchanged

- `world/Zone.fs` (`occupyZone`/`deactivatePhysics` are reused as they are; no new function expected).
- The state-level steps and `Batch`'s shape.
- Part refs stay epoch-scoped (`EntityRef.Epoch`).
- The screen-level `SetCue`/`Advent`/`SetScenario` and their pruning.
