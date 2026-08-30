module Zone

// The world container for one playable area: actors plus the environment (which
// owns props). Waypoints and triggers land in a later phase. Heap-allocated (not
// a struct); its entity collections are fixed-size inline slot maps.

open Entity
open SlotMap

type Zone =
    { Name : string
      Actors : SlotMap<Actor>
      Environment : Environment }

let create () : Zone =
    let environmentBase : Entity =
        { Name = ""
          Transform = transformIdentity
          Enabled = true
          Visible = true
          Selectable = true
          Draggable = true }
    { Name = ""
      Actors = SlotMap.create<Actor> 64u
      Environment = { Base = environmentBase; Props = SlotMap.create<Prop> 512u } }

// ── Entity kind-dispatch (TEMPORARY — moves to editor/Command.fs in phase 2d) ──
//
// The editor addresses entities by `EntityRef { Kind; Index }` and needs to
// read/write/spawn/remove them without a generation check. These helpers are the
// single seam for that; phase 2d (command/undo) will wrap them in undoable
// commands and relocate them to `editor/Command.fs`.

// The base Entity behind a ref (Actor/Prop), or None for other kinds / absent.
let entityAt (zone: Zone) (ref: EntityRef) : Entity option =
    match ref.Kind with
    | EntityKind.Actor -> Option.map (fun (a: Actor) -> a.Base) (SlotMap.tryGetByIndex zone.Actors ref.Index)
    | EntityKind.Prop -> Option.map (fun (p: Prop) -> p.Base) (SlotMap.tryGetByIndex zone.Environment.Props ref.Index)
    | _ -> None

// Write a base Entity back at the ref's index (Actor/Prop). No-op for other kinds.
let setEntity (zone: Zone) (ref: EntityRef) (entity: Entity) : Zone =
    match ref.Kind with
    | EntityKind.Actor ->
        { zone with Actors = SlotMap.setByIndex zone.Actors ref.Index { Base = entity } }
    | EntityKind.Prop ->
        { zone with Environment = { zone.Environment with Props = SlotMap.setByIndex zone.Environment.Props ref.Index { Base = entity } } }
    | _ -> zone

// Remove the entity at the ref's index (Actor/Prop). No-op for other kinds.
let removeEntity (zone: Zone) (ref: EntityRef) : Zone =
    match ref.Kind with
    | EntityKind.Actor ->
        { zone with Actors = SlotMap.removeByIndex zone.Actors ref.Index }
    | EntityKind.Prop ->
        { zone with Environment = { zone.Environment with Props = SlotMap.removeByIndex zone.Environment.Props ref.Index } }
    | _ -> zone

// Spawn a new actor/prop and return the zone plus the new ref.
let spawnActor (zone: Zone) (entity: Entity) : Zone * EntityRef =
    let actors, handle = SlotMap.add zone.Actors { Base = entity }
    { zone with Actors = actors }, { Kind = EntityKind.Actor; Index = handle.Index }

let spawnProp (zone: Zone) (entity: Entity) : Zone * EntityRef =
    let props, handle = SlotMap.add zone.Environment.Props { Base = entity }
    { zone with Environment = { zone.Environment with Props = props } }, { Kind = EntityKind.Prop; Index = handle.Index }
