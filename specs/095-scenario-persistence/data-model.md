# Data Model: Automatic Persistence of the Scenario Library

## What is persisted

The scenario library — `ScenarioRegistry.Registry` (`world/ScenarioRegistry.fs`) — and nothing else. The authored
value is a tree of immutable records:

```fsharp
type Registry = OMap<string, ScenarioContents option>                 // ordered by insertion
type ScenarioContents = { Zones : Map<string, ScenarioZone>; Cue : Cue.Cue }
type ScenarioZone =
    { Staging : ZoneStaging
      Actors : ActorState array
      Waypoints : ScenarioWaypoint array
      Triggers : TriggerState array }
type ScenarioWaypoint = { Base : Entity; Next : int option }
type ZoneStaging =
    { Environment : ZoneTemplate option
      Light : Texture.LightSettings
      Skydome : Skydome.Settings
      Props : PropState array }
type ZoneTemplate =
    { AssetId : string
      Base : Entity
      Orientation : Quaternion
      Light : Texture.LightSettings
      Skydome : Skydome.Settings
      Parts : PropState array
      Props : PropState array }
```

`ActorState`, `PropState`, `TriggerState` and `Entity` come from `world/Entity.fs`; the cue from `world/Cue.fs`. The
live scene, the active place, the camera, the running cue, the advents and the mix are **not** part of it.

## The serializable record tree — `world/Project.fs`

A mirror of the authored tree. Every record is `[<SymbolicExpansion>]`. Two flattenings only — `Vector3`/`Quaternion`
to scalar fields, and the library's ordered map to an ordered list — because those are the two things Prime's
symbolic converter cannot carry (research D1). The cue and the per-zone `Map` are reused **directly**.

```fsharp
[<SymbolicExpansion>] type Vec3Dto = { X : float32; Y : float32; Z : float32 }
[<SymbolicExpansion>] type QuatDto = { X : float32; Y : float32; Z : float32; W : float32 }
[<SymbolicExpansion>] type LightDto  = { Pitch : float32; Yaw : float32; Color : Vec3Dto; Ambient : float32 }
[<SymbolicExpansion>] type SkydomeDto = { Enabled : bool; Zenith : Vec3Dto; Horizon : Vec3Dto }
[<SymbolicExpansion>] type EntityDto =
    { Name : string
      Position : Vec3Dto; Rotation : QuatDto; Scale : Vec3Dto        // Entity.Transform, flattened
      Enabled : bool; Visible : bool; Selectable : bool; Draggable : bool }
[<SymbolicExpansion>] type ActorDto   = { Base : EntityDto; AssetId : string; ShowCollider : bool }
[<SymbolicExpansion>] type PropDto    = { Base : EntityDto; AssetId : string option; SubmeshIndex : int; SubmeshCount : int }
[<SymbolicExpansion>] type TriggerDto = { Base : EntityDto; Radius : float32; Height : float32; Interactable : bool; Prompt : string; Cue : Cue.Cue }
[<SymbolicExpansion>] type WaypointDto = { Base : EntityDto; Next : int option }
[<SymbolicExpansion>] type TemplateDto =
    { AssetId : string; Base : EntityDto; Orientation : QuatDto
      Light : LightDto; Skydome : SkydomeDto; Parts : PropDto array; Props : PropDto array }
[<SymbolicExpansion>] type StagingDto =
    { Environment : TemplateDto option; Light : LightDto; Skydome : SkydomeDto; Props : PropDto array }
[<SymbolicExpansion>] type ZoneDto =
    { Staging : StagingDto; Actors : ActorDto array; Waypoints : WaypointDto array; Triggers : TriggerDto array }
[<SymbolicExpansion>] type ContentsDto = { Zones : Map<string, ZoneDto>; Cue : Cue.Cue }
[<SymbolicExpansion>] type EntryDto    = { Name : string; Contents : ContentsDto option }
[<SymbolicExpansion>] type Project      = { Version : int; Scenarios : EntryDto array }   // the file root
```

Conversion is a pair of small total functions per mirrored record — `toEntityDto`/`ofEntityDto` and the `Vec3`/`Quat`
pair carry the bulk (an `Entity` appears inside five records), the rest are one-for-one:

```text
toVec3/ofVec3, toQuat/ofQuat, toLightDto/ofLightDto, toSkydomeDto/ofSkydomeDto,
toEntityDto/ofEntityDto, toActorDto/ofActorDto, toPropDto/ofPropDto, toTriggerDto/ofTriggerDto,
toWaypointDto/ofWaypointDto, toTemplateDto/ofTemplateDto, toStagingDto/ofStagingDto,
toZoneDto/ofZoneDto, toContentsDto/ofContentsDto, toEntryDto/ofEntryDto
```

The library ↔ entries boundary is the one place the ordered map meets the list:

```text
toProject  registry = { Version = projectVersion
                        Scenarios = OMap.toSeq registry |> Seq.map (fun (name, contents) -> { Name = name; Contents = Option.map toContentsDto contents }) |> Seq.toArray }
ofProject  project  = OMap.ofSeq1 [ for entry in project.Scenarios -> entry.Name, Option.map ofContentsDto entry.Contents ]
```

## The module's surface — `world/Project.fs`

| Function | Signature | Meaning |
|----------|-----------|---------|
| `projectPath` | `string` | the file name, `"project.manifest"`, at the working-directory root |
| `projectVersion` | `int` (literal) | the current format version (1) |
| `encode` | `Registry -> string` | the library as s-syntax text (`prettyPrintValue … defaultPrinter`) |
| `decode` | `string -> Registry` | text back to a library; raises on malformed input (the loader catches) |
| `loadFrom` | `string -> Registry` | read a path: absent → empty (no report); damaged/unknown version → report + empty |
| `saveTo` | `string -> Registry -> unit` | encode, then write atomically (`.tmp` + rename); report and skip on an encode or write failure, leaving the last good file |
| `load` | `unit -> Registry` | `loadFrom projectPath`, remembering the result as the last written |
| `persist` | `Registry -> unit` | call `saveTo projectPath` **iff** the argument is not the last-written instance; then remember it |

The last-written library is held in the module (the `Diagnostics` pattern of a process-global that is engine state,
not gameplay state); it is the reference the `persist` comparison is against. No `GameplayState` field is added.

```text
// module-private
let mutable private lastWritten : Registry = ScenarioRegistry.create ()
```

## The file

`project.manifest`, one s-syntax document with named fields:

```text
[[Version 1]
 [Scenarios
  [[Intro
    [Some
     [[Zones
       [[art-room
         [[Staging [[Environment [Some [[AssetId "art-room"] [Base [...]] [Orientation [...]] [Light [...]] [Skydome [...]] [Parts [...]] [Props [...]]]]] [Light [...]] [Skydome [...]] [Props [...]]]]
          [Actors [[[Base [[Name "Aiko"] [Position [0 0 0]] ...]] [AssetId "Akiko"] [ShowCollider false]]]]
          [Waypoints [[[Base [...]] [Next [Some 1]]]]]
          [Triggers [[[Base [...]] [Radius 2] [Height 3] [Interactable true] [Prompt "Look"] [Cue [Exposit [Look-here Dialogue]]]]]]]]]]
      [Cue [Sequence [Wait 1] [[Exposit [Hello Dialogue]] Fin]]]]]]
   [Draft [None]]]]]
```

- `Scenarios` is the ordered list of entries; a `[None]` entry is a scenario **named but not registered**, so both
  states round-trip.
- `Next [None]` is a waypoint chain end; `[Some n]` continues at the zone's waypoint `n`.
- `Cue` is written directly by the converter (the union and its `FDeque` round-trip); a scenario's and a trigger's
  cue are the same shape.

## Validation rules

- **Faithful round-trip**: `decode (encode r) = r` for any library the author can build — every field, including the
  authored order, a named-but-unregistered scenario, a chain-ending waypoint and an empty cue (FR-003, FR-011).
- **Encode is total over authored data**: it raises only on a run-form cue (a `…State` op), which is not authored
  data; `persist` reports and skips the write rather than emitting a partial file (research D10, FR-006).
- **Change-driven, once per change**: `persist` compares by reference; the library is immutable, so any change —
  add, clear, register, delete, undo, redo — is a new instance and is written exactly once (FR-005).
- **Atomic**: `saveTo` never writes the target in place; the target is always either the old file or the complete new
  one (FR-006).
- **Absent is not an error; damaged is reported and non-destructive**: `loadFrom` distinguishes the two; a damaged
  file is left on disk unchanged (FR-008, FR-009, FR-010).
- **Nothing else is touched**: a load installs `state.Scenarios` and nothing else; a save reads `state.Scenarios`
  and nothing else (FR-013).
