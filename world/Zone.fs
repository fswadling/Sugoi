module Zone

// The world container for one playable area: actors plus the environment (which
// owns props — both placed prop assets and the parts imported from the
// environment's glTF). Waypoints and triggers land in a later phase. Heap-
// allocated (not a struct); its entity collections are fixed-size inline slot
// maps, mutated in place.

open System.Numerics
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
          Orientation = Quaternion.Identity
          Props = SlotMap.create<Prop> 512u } }

// ── Entity kind-dispatch ──
//
// The editor addresses entities by `EntityRef { Kind; Index }` and needs to
// read/write/spawn/remove them without a generation check. These helpers are the
// single seam for that; `state/Command.fs` wraps them in undoable commands.

// The base Entity behind a ref (Actor/Prop), or None for other kinds / absent.
let entityAt (zone: Zone) (ref: EntityRef) : Entity option =
    match ref.Kind with
    | EntityKind.Actor -> Option.map (fun (a: Actor) -> a.Base) (SlotMap.tryGetByIndex zone.Actors ref.Index)
    | EntityKind.Prop -> Option.map (fun (p: Prop) -> p.Base) (SlotMap.tryGetByIndex zone.Environment.Props ref.Index)
    | _ -> None

// The asset binding behind a ref (asset id +, for props, the submesh range).
// Empty asset id = environment part (props) or no model.
let bindingAt (zone: Zone) (ref: EntityRef) : AssetBinding =
    match ref.Kind with
    | EntityKind.Actor ->
        match SlotMap.tryGetByIndex zone.Actors ref.Index with
        | Some a -> { AssetId = a.AssetId; SubmeshIndex = 0; SubmeshCount = 0 }
        | None -> { AssetId = ""; SubmeshIndex = 0; SubmeshCount = 0 }
    | EntityKind.Prop ->
        match SlotMap.tryGetByIndex zone.Environment.Props ref.Index with
        | Some p -> { AssetId = p.AssetId; SubmeshIndex = p.SubmeshIndex; SubmeshCount = p.SubmeshCount }
        | None -> { AssetId = ""; SubmeshIndex = 0; SubmeshCount = 0 }
    | _ -> { AssetId = ""; SubmeshIndex = 0; SubmeshCount = 0 }

// The entity's asset id behind a ref (empty for environment parts / absent).
let assetIdAt (zone: Zone) (ref: EntityRef) : string =
    (bindingAt zone ref).AssetId

// Write a base Entity back at the ref's index (Actor/Prop). No-op for other kinds.
// The existing binding/Renderer are preserved (a base-entity write such as a
// transform must not drop the model binding).
let setEntity (zone: Zone) (ref: EntityRef) (entity: Entity) : unit =
    match ref.Kind with
    | EntityKind.Actor ->
        match SlotMap.tryGetByIndex zone.Actors ref.Index with
        | Some a ->
            zone.Actors <- SlotMap.setByIndex zone.Actors ref.Index { a with Base = entity }
        | None -> ()
    | EntityKind.Prop ->
        match SlotMap.tryGetByIndex zone.Environment.Props ref.Index with
        | Some p ->
            zone.Environment.Props <- SlotMap.setByIndex zone.Environment.Props ref.Index { p with Base = entity }
        | None -> ()
    | _ -> ()

// Build a prop renderer from a binding: a placed prop asset (non-empty id), an
// environment part (empty id, environment loaded, submesh range), or none
// (headless tests / no model).
let private propRenderer (zone: Zone) (assets: Assets.AssetManager) (binding: AssetBinding) : EnvironmentRender.EnvironmentRender =
    if binding.AssetId <> "" then
        Assets.createPropRender assets binding.AssetId
    elif zone.Environment.AssetId <> "" then
        Assets.createPartRender assets zone.Environment.AssetId binding.SubmeshIndex binding.SubmeshCount
    else
        Unchecked.defaultof<EnvironmentRender.EnvironmentRender>

// Write an actor or prop (base + binding) at the ref's index. Used by command
// apply/undo to restore/create an entity where the renderer is created eagerly.
let setActorWithAsset (zone: Zone) (assets: Assets.AssetManager) (ref: EntityRef) (entity: Entity) (binding: AssetBinding) : unit =
    match ref.Kind with
    | EntityKind.Actor ->
        let renderer = Assets.createActorRender assets binding.AssetId
        zone.Actors <- SlotMap.setByIndex zone.Actors ref.Index { Base = entity; AssetId = binding.AssetId; Renderer = renderer }
    | EntityKind.Prop ->
        let renderer = propRenderer zone assets binding
        zone.Environment.Props <-
            SlotMap.setByIndex zone.Environment.Props ref.Index
                { Base = entity; AssetId = binding.AssetId; SubmeshIndex = binding.SubmeshIndex; SubmeshCount = binding.SubmeshCount; Renderer = renderer }
    | _ -> ()

// Remove the entity at the ref's index (Actor/Prop). No-op for other kinds.
let removeEntity (zone: Zone) (ref: EntityRef) : unit =
    match ref.Kind with
    | EntityKind.Actor ->
        zone.Actors <- SlotMap.removeByIndex zone.Actors ref.Index (Some Actor.releaseRenderer)
    | EntityKind.Prop ->
        zone.Environment.Props <- SlotMap.removeByIndex zone.Environment.Props ref.Index (Some Prop.releaseRenderer)
    | _ -> ()

// Spawn a new actor and return the new ref (renderer created eagerly).
let spawnActor (zone: Zone) (assets: Assets.AssetManager) (entity: Entity) (assetId: string) : EntityRef =
    let renderer = Assets.createActorRender assets assetId
    let actors, handle = SlotMap.add zone.Actors { Base = entity; AssetId = assetId; Renderer = renderer }
    zone.Actors <- actors
    { Kind = EntityKind.Actor; Index = handle.Index }

// Spawn a prop from a full binding (placed asset or environment part).
let spawnPropBinding (zone: Zone) (assets: Assets.AssetManager) (entity: Entity) (binding: AssetBinding) : EntityRef =
    let renderer = propRenderer zone assets binding
    let props, handle =
        SlotMap.add zone.Environment.Props
            { Base = entity; AssetId = binding.AssetId; SubmeshIndex = binding.SubmeshIndex; SubmeshCount = binding.SubmeshCount; Renderer = renderer }
    zone.Environment.Props <- props
    { Kind = EntityKind.Prop; Index = handle.Index }

// Spawn a placed prop asset (whole model). An empty asset id means "no model"
// (headless command tests): leave the renderer uninitialised.
let spawnProp (zone: Zone) (assets: Assets.AssetManager) (entity: Entity) (assetId: string) : EntityRef =
    spawnPropBinding zone assets entity { AssetId = assetId; SubmeshIndex = 0; SubmeshCount = 0 }

// Load an environment asset into the zone (a zone has exactly one environment).
// Keeps placed props (non-empty asset id) and drops the previous environment's
// parts (empty asset id), then installs the new environment's orientation + asset
// id and spawns one prop per part (empty asset id, the part's submesh range, and
// the part's world transform decomposed into the prop's entity transform). An
// empty asset id clears the environment (parts removed, placed props kept).
let setEnvironment (zone: Zone) (assets: Assets.AssetManager) (assetId: string) : unit =
    let props = SlotMap.create<Prop> 512u
    let mutable props = props
    SlotMap.iteriAlive zone.Environment.Props (fun _ (p: Prop) ->
        if p.AssetId <> "" then
            let (m, _) = SlotMap.add props p
            props <- m)

    let emptyEntity : Entity =
        { Name = ""
          Transform = transformIdentity
          Enabled = true
          Visible = true
          Selectable = true
          Draggable = true }

    if assetId = "" then
        zone.Environment <-
            { Base = emptyEntity
              AssetId = ""
              Orientation = Quaternion.Identity
              Props = props }
    else

    let asset = assets.Environments.[assetId]
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
          Orientation = asset.Orientation
          Props = props }

    for part in asset.Parts do
        let mutable scale = Vector3.One
        let mutable rotation = Quaternion.Identity
        let mutable translation = Vector3.Zero
        do Matrix4x4.Decompose(part.World, &scale, &rotation, &translation) |> ignore
        let entity : Entity =
            { Name = if part.Name = "" then part.HierarchyPath else part.Name
              Transform = { Position = translation; Rotation = rotation; Scale = scale }
              Enabled = true
              Visible = true
              Selectable = true
              Draggable = true }
        let binding = { AssetId = ""; SubmeshIndex = part.SubmeshIndex; SubmeshCount = part.SubmeshCount }
        spawnPropBinding zone assets entity binding |> ignore
