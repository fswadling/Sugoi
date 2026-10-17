module Project

// Automatically persist the scenario library as s-syntax (spec 095): the authored value — every scenario's
// name, whether it is registered, and a registered scenario's zones, entities and cue — is written to
// `project.manifest` at the launch working directory and read back at startup. There is no save command and
// no load command; a change to the library is written, and startup reads it.
//
// Prime's symbolic serializer maps records, unions, options, lists, arrays, maps and its own deque directly,
// but it cannot carry `System.Numerics.Vector3`/`Quaternion` (written as unreadable text) nor its own ordered
// `OMap` (unsupported). The record tree below therefore mirrors the authored records, flattening the vectors
// to scalars and the library to an ordered list, and `encode`/`decode` convert at that one boundary. The cue
// and each zone's `Map` are carried as-is.
//
// Listed after `world/ScenarioRegistry.fs`, whose library it files, and after every `world/` type the tree
// mirrors (`Entity`, `ZoneTemplate`, `ScenarioZone`, `Cue`). Its two entry points take the library by value,
// so the module needs nothing from the state layer.

open System.IO
open System.Numerics
open Prime
open Entity
open ScenarioZone
open ScenarioRegistry
open ZoneTemplate

// ── the serializable record tree ──

[<SymbolicExpansion>]
type Vec3Dto = { X : float32; Y : float32; Z : float32 }

[<SymbolicExpansion>]
type QuatDto = { X : float32; Y : float32; Z : float32; W : float32 }

[<SymbolicExpansion>]
type LightDto = { Pitch : float32; Yaw : float32; Color : Vec3Dto; Ambient : float32 }

[<SymbolicExpansion>]
type SkydomeDto = { Enabled : bool; Zenith : Vec3Dto; Horizon : Vec3Dto }

[<SymbolicExpansion>]
type EntityDto =
    { Name : string
      Position : Vec3Dto
      Rotation : QuatDto
      Scale : Vec3Dto
      Enabled : bool
      Visible : bool
      Selectable : bool
      Draggable : bool }

[<SymbolicExpansion>]
type ActorPhysicsDto =
    { Mobility : ActorMobility
      Mass : float32
      Friction : float32
      Restitution : float32
      GravityFactor : float32 }

[<SymbolicExpansion>]
type ActorDto = { Base : EntityDto; AssetId : string; ShowCollider : bool; Physics : ActorPhysicsDto option }

[<SymbolicExpansion>]
type PropDto = { Base : EntityDto; AssetId : string option; SubmeshIndex : int; SubmeshCount : int }

[<SymbolicExpansion>]
type TriggerDto = { Base : EntityDto; Radius : float32; Height : float32; Interactable : bool; Prompt : string; Cue : Cue.Cue }

[<SymbolicExpansion>]
type WaypointDto = { Base : EntityDto; Next : int option }

[<SymbolicExpansion>]
type TemplateDto =
    { AssetId : string
      Base : EntityDto
      Orientation : QuatDto
      Light : LightDto
      Skydome : SkydomeDto
      Parts : PropDto array
      Props : PropDto array }

[<SymbolicExpansion>]
type StagingDto =
    { Environment : TemplateDto option
      Light : LightDto
      Skydome : SkydomeDto
      Props : PropDto array }

[<SymbolicExpansion>]
type ZoneDto =
    { Staging : StagingDto
      Actors : ActorDto array
      Waypoints : WaypointDto array
      Triggers : TriggerDto array }

[<SymbolicExpansion>]
type ContentsDto = { Zones : Map<string, ZoneDto>; Cue : Cue.Cue }

[<SymbolicExpansion>]
type EntryDto = { Name : string; Contents : ContentsDto option }

/// The file root: the format version and the library as an ordered list of entries.
[<SymbolicExpansion>]
type Project = { Version : int; Scenarios : EntryDto array }

// ── the file ──

/// The project file's name, at the launch working directory (FR-007).
[<Literal>]
let projectPath = "project.manifest"

/// The current format version (FR-010).
[<Literal>]
let projectVersion = 1

// ── the boundary conversions ──

let private toVec3 (v: Vector3) : Vec3Dto = { X = v.X; Y = v.Y; Z = v.Z }
let private ofVec3 (v: Vec3Dto) : Vector3 = Vector3(v.X, v.Y, v.Z)

let private toQuat (q: Quaternion) : QuatDto = { X = q.X; Y = q.Y; Z = q.Z; W = q.W }
let private ofQuat (q: QuatDto) : Quaternion = Quaternion(q.X, q.Y, q.Z, q.W)

let private toLightDto (l: Texture.LightSettings) : LightDto =
    { Pitch = l.Pitch; Yaw = l.Yaw; Color = toVec3 l.Color; Ambient = l.Ambient }

let private ofLightDto (l: LightDto) : Texture.LightSettings =
    { Pitch = l.Pitch; Yaw = l.Yaw; Color = ofVec3 l.Color; Ambient = l.Ambient }

let private toSkydomeDto (s: Skydome.Settings) : SkydomeDto =
    { Enabled = s.Enabled; Zenith = toVec3 s.Zenith; Horizon = toVec3 s.Horizon }

let private ofSkydomeDto (s: SkydomeDto) : Skydome.Settings =
    { Enabled = s.Enabled; Zenith = ofVec3 s.Zenith; Horizon = ofVec3 s.Horizon }

let private toEntityDto (e: Entity) : EntityDto =
    { Name = e.Name
      Position = toVec3 e.Transform.Position
      Rotation = toQuat e.Transform.Rotation
      Scale = toVec3 e.Transform.Scale
      Enabled = e.Enabled
      Visible = e.Visible
      Selectable = e.Selectable
      Draggable = e.Draggable }

let private ofEntityDto (e: EntityDto) : Entity =
    { Name = e.Name
      Transform =
        { Position = ofVec3 e.Position
          Rotation = ofQuat e.Rotation
          Scale = ofVec3 e.Scale }
      Enabled = e.Enabled
      Visible = e.Visible
      Selectable = e.Selectable
      Draggable = e.Draggable }

let private toPropDto (p: PropState) : PropDto =
    { Base = toEntityDto p.Base; AssetId = p.AssetId; SubmeshIndex = p.SubmeshIndex; SubmeshCount = p.SubmeshCount }

let private ofPropDto (p: PropDto) : PropState =
    { Base = ofEntityDto p.Base; AssetId = p.AssetId; SubmeshIndex = p.SubmeshIndex; SubmeshCount = p.SubmeshCount }

let private toTemplateDto (t: ZoneTemplate) : TemplateDto =
    { AssetId = t.AssetId
      Base = toEntityDto t.Base
      Orientation = toQuat t.Orientation
      Light = toLightDto t.Light
      Skydome = toSkydomeDto t.Skydome
      Parts = Array.map toPropDto t.Parts
      Props = Array.map toPropDto t.Props }

let private ofTemplateDto (t: TemplateDto) : ZoneTemplate =
    { AssetId = t.AssetId
      Base = ofEntityDto t.Base
      Orientation = ofQuat t.Orientation
      Light = ofLightDto t.Light
      Skydome = ofSkydomeDto t.Skydome
      Parts = Array.map ofPropDto t.Parts
      Props = Array.map ofPropDto t.Props }

let private toStagingDto (s: ZoneStaging) : StagingDto =
    { Environment = Option.map toTemplateDto s.Environment
      Light = toLightDto s.Light
      Skydome = toSkydomeDto s.Skydome
      Props = Array.map toPropDto s.Props }

let private ofStagingDto (s: StagingDto) : ZoneStaging =
    { Environment = Option.map ofTemplateDto s.Environment
      Light = ofLightDto s.Light
      Skydome = ofSkydomeDto s.Skydome
      Props = Array.map ofPropDto s.Props }

let private toActorDto (a: ActorState) : ActorDto =
    { Base = toEntityDto a.Base
      AssetId = a.AssetId
      ShowCollider = a.ShowCollider
      Physics = Some { Mobility = a.Physics.Mobility
                       Mass = a.Physics.Mass
                       Friction = a.Physics.Friction
                       Restitution = a.Physics.Restitution
                       GravityFactor = a.Physics.GravityFactor } }

let private ofActorDto (a: ActorDto) : ActorState =
    let physics =
        match a.Physics with
        | None -> dynamicPhysics
        | Some p ->
            if not (p.Mass >= minMass && p.Mass <= maxMass) then invalidArg "Physics.Mass" (sprintf "%g is outside [%g, %g]" p.Mass minMass maxMass)
            if not (p.Friction >= minFriction && p.Friction <= maxFriction) then invalidArg "Physics.Friction" (sprintf "%g is outside [%g, %g]" p.Friction minFriction maxFriction)
            if not (p.Restitution >= minRestitution && p.Restitution <= maxRestitution) then invalidArg "Physics.Restitution" (sprintf "%g is outside [%g, %g]" p.Restitution minRestitution maxRestitution)
            if not (p.GravityFactor >= minGravityFactor && p.GravityFactor <= maxGravityFactor) then invalidArg "Physics.GravityFactor" (sprintf "%g is outside [%g, %g]" p.GravityFactor minGravityFactor maxGravityFactor)
            { Mobility = p.Mobility; Mass = p.Mass; Friction = p.Friction; Restitution = p.Restitution; GravityFactor = p.GravityFactor }
    { Base = ofEntityDto a.Base; AssetId = a.AssetId; ShowCollider = a.ShowCollider; Physics = physics }

let private toWaypointDto (w: ScenarioWaypoint) : WaypointDto =
    { Base = toEntityDto w.Base; Next = w.Next }

let private ofWaypointDto (w: WaypointDto) : ScenarioWaypoint =
    { Base = ofEntityDto w.Base; Next = w.Next }

let private toTriggerDto (t: TriggerState) : TriggerDto =
    { Base = toEntityDto t.Base; Radius = t.Radius; Height = t.Height; Interactable = t.Interactable; Prompt = t.Prompt; Cue = t.Cue }

let private ofTriggerDto (t: TriggerDto) : TriggerState =
    { Base = ofEntityDto t.Base; Radius = t.Radius; Height = t.Height; Interactable = t.Interactable; Prompt = t.Prompt; Cue = t.Cue }

let private toZoneDto (z: ScenarioZone) : ZoneDto =
    { Staging = toStagingDto z.Staging
      Actors = Array.map toActorDto z.Actors
      Waypoints = Array.map toWaypointDto z.Waypoints
      Triggers = Array.map toTriggerDto z.Triggers }

let private ofZoneDto (z: ZoneDto) : ScenarioZone =
    { Staging = ofStagingDto z.Staging
      Actors = Array.map ofActorDto z.Actors
      Waypoints = Array.map ofWaypointDto z.Waypoints
      Triggers = Array.map ofTriggerDto z.Triggers }

let private toContentsDto (c: ScenarioContents) : ContentsDto =
    { Zones = Map.map (fun _ z -> toZoneDto z) c.Zones; Cue = c.Cue }

let private ofContentsDto (c: ContentsDto) : ScenarioContents =
    { Zones = Map.map (fun _ z -> ofZoneDto z) c.Zones; Cue = c.Cue }

// The library is ordered, so it becomes an ordered list of entries (a `Map` would sort by name and lose the
// authored order); `ofProject` rebuilds the `OMap` in that order (FR-004).
let private toProject (registry: Registry) : Project =
    { Version = projectVersion
      Scenarios =
        registry
        |> OMap.toSeq
        |> Seq.map (fun (name, contents) -> { Name = name; Contents = Option.map toContentsDto contents })
        |> Seq.toArray }

let private ofProject (project: Project) : Registry =
    if project.Version <> projectVersion then failwithf "project: unsupported version %d (expected %d)" project.Version projectVersion else
    project.Scenarios
    |> Seq.map (fun entry -> entry.Name, Option.map ofContentsDto entry.Contents)
    |> OMap.ofSeq1

// ── the codec ──

/// The library as s-syntax text (FR-002).
let encode (registry: Registry) : string =
    PrettyPrinter.prettyPrintValue (toProject registry) PrettyPrinter.defaultPrinter

/// Text back to a library. Raises on malformed text or an unsupported version — both are the loader's to report.
let decode (text: string) : Registry =
    scvalue<Project> text |> ofProject

// ── the file read and write ──

/// The file's text, or `None` when it is absent (the expected first run, not an error).
let private readText (path: string) : string option =
    if File.Exists path then Some (File.ReadAllText path) else None

/// Text back to a library, or the failure's message when it cannot be read. The caller decides the policy.
let private tryDecode (text: string) : Result<Registry, string> =
    try Ok (decode text) with ex -> Error ex.Message

/// Write `text` atomically: a temporary file, then a rename over the target, so an interrupted write cannot
/// corrupt the last good file (FR-006). Reports a failure, leaving the last good file; true when it wrote.
let private writeAtomic (path: string) (text: string) : bool =
    let temporary = path + ".tmp"
    try
        do File.WriteAllText(temporary, text)
        do File.Move(temporary, path, true)
        true
    with ex ->
        do Diagnostics.reportf "project: '%s' could not be written (%s); keeping the previous file" path ex.Message
        if File.Exists temporary then
            try File.Delete temporary with _ -> ()
        false

/// Decode with the **startup** policy: a good file's library, or a reported empty library for a damaged one (FR-009).
let private decodeStartup (path: string) (text: string) : Registry =
    match tryDecode text with
    | Ok registry -> registry
    | Error message ->
        do Diagnostics.reportf "project: '%s' could not be read (%s); starting with an empty library" path message
        ScenarioRegistry.create ()

/// Read a project file. A missing file is the empty library — the expected first run, not an error (FR-008). A
/// file that cannot be read or parsed, or whose version is not understood, is reported and yields the empty
/// library, leaving the file untouched (FR-009).
let loadFrom (path: string) : Registry =
    match readText path with
    | None -> ScenarioRegistry.create ()
    | Some text -> decodeStartup path text

/// Write the library atomically (FR-006). A failure is reported and the write skipped, leaving the last good file.
let saveTo (path: string) (registry: Registry) : unit =
    ignore (writeAtomic path (encode registry))

// ── the store and the watch (spec 096) ──

// The library as last written (or loaded): the reference the change comparison is against.
let mutable private lastWritten : Registry = ScenarioRegistry.create ()

// The library of the last write **attempt**, successful or not. `lastWritten` records only success, so
// this is what tells `persistFrame` that a failed attempt is the same edit rather than a new one.
let mutable private lastAttempted : Registry = lastWritten

// The file's text as last written or read: the reference an external change is compared against. It closes the
// feedback loop — the game's own saves read back identical, so they are never reloaded (FR-002).
let mutable private lastText : string = ""

// The frame time banked toward the next poll, so the file is read once per interval, not once per frame.
let mutable private accumulator : float = 0.0

// A failed write is retried, but not once per frame: the edit is still pending and `lastWritten` must not
// advance until it lands (FR-003, research D6).
let mutable private writeFailed : bool = false
let mutable private retryAccumulator : float = 0.0

// The poll interval: at most one read of the file per this many seconds (FR-001).
let private pollSeconds = 0.25

// The retry interval after a failed write: at most one attempt (and one report) per this many seconds.
let private retrySeconds = 0.5

/// Load the project file once, remembering it (FR-007).
let load () : Registry =
    let text = readText projectPath
    let registry =
        match text with
        | None -> ScenarioRegistry.create ()
        | Some text -> decodeStartup projectPath text
    match text with Some text -> lastText <- text | None -> ()
    do lastWritten <- registry
    do lastAttempted <- registry
    writeFailed <- false
    retryAccumulator <- 0.0
    registry

/// Write the project file: one immediate attempt when `registry` is not the value last written, and a
/// retry of a previously failed attempt. The library is immutable, so every change — an add, clear,
/// register or delete, and the undo or redo of any of them — yields a new instance, so the comparison
/// is one reference check and an unchanged library writes nothing (FR-015). `lastWritten` and the
/// file's text advance only when the write succeeded, so a failed write is never mistaken for the
/// file's content (FR-006).
let persist (registry: Registry) : unit =
    if obj.ReferenceEquals (registry, lastWritten) && not writeFailed then () else

    let text = encode registry
    lastAttempted <- registry
    if writeAtomic projectPath text then
        do lastText <- text
        do lastWritten <- registry
        writeFailed <- false
        retryAccumulator <- 0.0
    else
        writeFailed <- true

/// Persist from the frame loop (FR-003): a new edit attempts immediately, and a failed attempt is
/// retried at most once per `retrySeconds`, so a persistent failure neither loses the edit nor writes
/// and reports every frame.
let persistFrame (dt: float) (registry: Registry) : unit =
    if not (obj.ReferenceEquals (registry, lastAttempted)) then
        do persist registry
    elif writeFailed then
        retryAccumulator <- retryAccumulator + dt
        if retryAccumulator >= retrySeconds then
            retryAccumulator <- 0.0
            do persist registry

/// Whether the poll may run: only while the author is **editing**, and only when live reload is **on**. A pure
/// predicate, so the gate the frame applies is itself testable (FR-008).
let pollEnabled (editing: bool) (liveReload: bool) : bool = editing && liveReload

/// Read the file at most once per interval; when an **external** change is found and decodes, return the reloaded
/// library. `None` means nothing to do — not yet due, unchanged (the game's own write, or a no-op rewrite), absent,
/// or undecodable; the last is reported and retried next interval, leaving the running library alone
/// (FR-001–FR-005, FR-007).
let pollExternal (dt: float) : Registry option =
    accumulator <- accumulator + dt
    if accumulator < pollSeconds then None else

    do accumulator <- 0.0
    match readText projectPath with
    | None -> None
    | Some text ->
    if text = lastText then None else

    match tryDecode text with
    | Ok registry ->
        do lastText <- text
        do lastWritten <- registry
        do lastAttempted <- registry
        writeFailed <- false
        retryAccumulator <- 0.0
        Some registry
    | Error message ->
        do Diagnostics.reportf "project: '%s' changed but could not be read (%s); keeping the running library" projectPath message
        None
