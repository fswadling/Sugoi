module Zone

// The world container for one playable area: actors plus the environment (which
// owns props). Waypoints and triggers land in a later phase. Heap-allocated (not
// a struct); its entity collections are fixed-size inline slot maps, mutated in
// place.

open Entity
open SlotMap

type Zone =
    { Name : string
      mutable Actors : SlotMap<Actor>
      mutable Environment : Environment }

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
      Environment =
        { Base = environmentBase
          AssetId = ""
          Renderer = Unchecked.defaultof<EnvironmentRender.EnvironmentRender>
          Props = SlotMap.create<Prop> 512u } }

// ── Entity kind-dispatch ──
//
// The editor addresses entities by `EntityRef { Kind; Index }` and needs to
// read/write/spawn/remove them without a generation check. These helpers are the
// single seam for that; `editor/Command.fs` wraps them in undoable commands.

// The base Entity behind a ref (Actor/Prop), or None for other kinds / absent.
let entityAt (zone: Zone) (ref: EntityRef) : Entity option =
    match ref.Kind with
    | EntityKind.Actor -> Option.map (fun (a: Actor) -> a.Base) (SlotMap.tryGetByIndex zone.Actors ref.Index)
    | EntityKind.Prop -> Option.map (fun (p: Prop) -> p.Base) (SlotMap.tryGetByIndex zone.Environment.Props ref.Index)
    | _ -> None

// The entity's asset id behind a ref (empty for non-actor/prop kinds or absent).
let assetIdAt (zone: Zone) (ref: EntityRef) : string =
    match ref.Kind with
    | EntityKind.Actor -> Option.map (fun (a: Actor) -> a.AssetId) (SlotMap.tryGetByIndex zone.Actors ref.Index) |> Option.defaultValue ""
    | EntityKind.Prop -> Option.map (fun (p: Prop) -> p.AssetId) (SlotMap.tryGetByIndex zone.Environment.Props ref.Index) |> Option.defaultValue ""
    | _ -> ""

// Write a base Entity back at the ref's index (Actor/Prop). No-op for other kinds.
// The existing AssetId/Renderer are preserved (a base-entity write such as a
// transform must not drop the model binding).
let setEntity (zone: Zone) (ref: EntityRef) (entity: Entity) : unit =
    match ref.Kind with
    | EntityKind.Actor ->
        match SlotMap.tryGetByIndex zone.Actors ref.Index with
        | Some a ->
            zone.Actors <- SlotMap.setByIndex zone.Actors ref.Index { Base = entity; AssetId = a.AssetId; Renderer = a.Renderer }
        | None -> ()
    | EntityKind.Prop ->
        match SlotMap.tryGetByIndex zone.Environment.Props ref.Index with
        | Some p ->
            zone.Environment.Props <- SlotMap.setByIndex zone.Environment.Props ref.Index { Base = entity; AssetId = p.AssetId; Renderer = p.Renderer }
        | None -> ()
    | _ -> ()

// Write an actor or prop (base + assetId) at the ref's index. Used by command
// apply/undo to restore/create an entity where the renderer is created eagerly.
let setActorWithAsset (zone: Zone) (assets: Assets.AssetManager) (ref: EntityRef) (entity: Entity) (assetId: string) : unit =
    match ref.Kind with
    | EntityKind.Actor ->
        let renderer = Assets.createActorRender assets assetId
        zone.Actors <- SlotMap.setByIndex zone.Actors ref.Index { Base = entity; AssetId = assetId; Renderer = renderer }
    | EntityKind.Prop ->
        let renderer =
            if assetId = "" then Unchecked.defaultof<EnvironmentRender.EnvironmentRender>
            else Assets.createPropRender assets assetId
        zone.Environment.Props <- SlotMap.setByIndex zone.Environment.Props ref.Index { Base = entity; AssetId = assetId; Renderer = renderer }
    | _ -> ()

// Remove the entity at the ref's index (Actor/Prop). No-op for other kinds.
let removeEntity (zone: Zone) (ref: EntityRef) : unit =
    match ref.Kind with
    | EntityKind.Actor ->
        zone.Actors <- SlotMap.removeByIndex zone.Actors ref.Index (Some Actor.releaseRenderer)
    | EntityKind.Prop ->
        zone.Environment.Props <- SlotMap.removeByIndex zone.Environment.Props ref.Index (Some Prop.releaseRenderer)
    | _ -> ()

// Spawn a new actor/prop and return the new ref. The renderer is created eagerly
// (matching odin's `create_actor_entity`/`create_prop_entity` + load path).
let spawnActor (zone: Zone) (assets: Assets.AssetManager) (entity: Entity) (assetId: string) : EntityRef =
    let renderer = Assets.createActorRender assets assetId
    let actors, handle = SlotMap.add zone.Actors { Base = entity; AssetId = assetId; Renderer = renderer }
    zone.Actors <- actors
    { Kind = EntityKind.Actor; Index = handle.Index }

let spawnProp (zone: Zone) (assets: Assets.AssetManager) (entity: Entity) (assetId: string) : EntityRef =
    // An empty asset id means "no model" (headless command tests): leave the
    // renderer uninitialised. A real spawn resolves and uploads a shared asset.
    let renderer =
        if assetId = "" then Unchecked.defaultof<EnvironmentRender.EnvironmentRender>
        else Assets.createPropRender assets assetId
    let props, handle = SlotMap.add zone.Environment.Props { Base = entity; AssetId = assetId; Renderer = renderer }
    zone.Environment.Props <- props
    { Kind = EntityKind.Prop; Index = handle.Index }

// Load an environment asset into the zone (a zone has exactly one environment).
// Unloads any existing environment and installs a fresh props slot map. Not
// addressable by `EntityRef` and not undoable this phase (a zone-level load).
let setEnvironment (zone: Zone) (assets: Assets.AssetManager) (assetId: string) : unit =
    do Environment.releaseRenderer zone.Environment
    let asset = assets.Environments.[assetId]
    let renderer = Assets.createEnvironmentRender assets assetId
    let baseEntity : Entity =
        { Name = asset.Name
          Transform = transformIdentity
          Enabled = true
          Visible = true
          Selectable = true
          Draggable = true }
    zone.Environment <-
        { Base = baseEntity
          AssetId = assetId
          Renderer = renderer
          Props = SlotMap.create<Prop> 512u }
