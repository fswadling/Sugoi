module ProjectTests

// The project file (spec 095), asserted headlessly: the round trip, the file handling (absent / damaged /
// unknown version / atomic write / failed write), and the change-driven persist. Nothing here opens a window,
// a GPU or a device — the codec is pure and the file I/O is `System.IO` over a temp directory. The suite is
// serialized (`xunit.runner.json`), so the persist test may set the current directory to a temp dir.

open System
open System.Collections.Generic
open System.IO
open System.Numerics
open Xunit
open Prime
open Cue
open Dialogue
open Entity
open ScenarioZone
open ScenarioRegistry
open ZoneTemplate
open TestSupport

// The pre-feature record tree: an actor with no physics field. Encoding this and decoding it with the
// current `Project` proves the optional field is backward-compatible. Kept in a nested module so its
// `Zones`/`Cue` labels do not collide with `ScenarioContents` in the rest of this file.
module LegacyDtos =
    [<SymbolicExpansion>]
    type ActorDto =
        { Base : Project.EntityDto
          AssetId : string
          ShowCollider : bool }

    [<SymbolicExpansion>]
    type ZoneDto =
        { Staging : Project.StagingDto
          Actors : ActorDto array
          Waypoints : Project.WaypointDto array
          Triggers : Project.TriggerDto array }

    [<SymbolicExpansion>]
    type ContentsDto =
        { Zones : Map<string, ZoneDto>
          Cue : Cue.Cue }

    [<SymbolicExpansion>]
    type EntryDto =
        { Name : string
          Contents : ContentsDto option }

    [<SymbolicExpansion>]
    type Project =
        { Version : int
          Scenarios : EntryDto array }

// ── fixtures ──

// An authored zone: a light and prop in its staging, an actor, a chained waypoint pair, and a trigger whose cue
// does something — every shape the file must carry.
let private authoredZone () : ScenarioZone =
    { Staging =
        { Environment = None
          Light = { Pitch = 30.0f; Yaw = 45.0f; Color = Vector3(1.0f, 0.5f, 0.25f); Ambient = 0.2f }
          Skydome = { Enabled = true; Zenith = Vector3(0.1f, 0.2f, 0.3f); Horizon = Vector3(0.4f, 0.5f, 0.6f) }
          Props = [| { Base = entity "crate"; AssetId = Some "crate"; SubmeshIndex = 0; SubmeshCount = 1 } |] }
      Actors = [| { Base = entity "aiko"; AssetId = "aiko"; ShowCollider = false; Physics = dynamicPhysics } |]
      Waypoints = [| { Base = entity "a"; Next = Some 1 }; { Base = entity "b"; Next = None } |]
      Triggers = [| { Base = entity "area"; Radius = 1.5f; Height = 2.0f; Interactable = true; Prompt = "Look"; Cue = Exposit("hi", Kind.Dialogue) } |] }

/// A library with a registered scenario (two zones, a cue) and a named-but-unregistered one.
let private library () : Registry =
    let contents =
        { Zones = Map.ofList [ "start", authoredZone (); "island", { authoredZone () with Actors = [||] } ]
          Cue = Sequence(Wait 1.0, FDeque.ofList [ Exposit("hello", Kind.Dialogue) ]) }
    setContents "Intro" (Some contents) (add "Draft" (create ()))

/// A zone whose one trigger carries `cue`, so a cue alone can be put through the file.
let private zoneWithCue (cue: Cue) : ScenarioZone =
    { authoredZone () with
        Triggers = [| { Base = entity "area"; Radius = 1.0f; Height = 1.0f; Interactable = true; Prompt = ""; Cue = cue } |] }

/// Encode and decode `cue` as a registered scenario's trigger cue.
let private cueRoundTrip (cue: Cue) : Cue =
    let contents = { Zones = Map.ofList [ "start", zoneWithCue cue ]; Cue = Fin }
    let registry = setContents "Intro" (Some contents) (create ())
    let reloaded = Project.decode (Project.encode registry)
    (Map.find "start" (Option.get (tryContents "Intro" reloaded)).Zones).Triggers.[0].Cue

let private tempPath (stem: string) : string =
    Path.Combine(Path.GetTempPath(), sprintf "sugoi-%s-%s.manifest" stem (Guid.NewGuid().ToString "N"))

// ── the round trip (FR-003, FR-004, FR-011) ──

[<Fact>]
let ``a library round-trips through encode and decode, equal scenario for scenario`` () =
    let registry = library ()
    let reloaded = Project.decode (Project.encode registry)
    // The names, their order and every authored field: the reloaded library re-encodes to the saved text.
    Assert.Equal<string list>(names registry, names reloaded)
    Assert.Equal(Project.encode registry, Project.encode reloaded)

    // The named-but-unregistered scenario survives as unregistered.
    Assert.True((tryContents "Draft" reloaded).IsNone)

    // The meaningful shapes: a chained waypoint pair with a real chain end, and a trigger's authored cue.
    let main = Map.find "start" (Option.get (tryContents "Intro" reloaded)).Zones
    Assert.Equal(2, Array.length main.Waypoints)
    Assert.Equal(Some 1, main.Waypoints.[0].Next)
    Assert.Equal(None, main.Waypoints.[1].Next)
    Assert.Equal(1, Array.length main.Triggers)
    Assert.Equal(Exposit("hi", Kind.Dialogue), main.Triggers.[0].Cue)

[<Fact>]
let ``the physics setting round-trips through the project file`` () =
    let physics = { dynamicPhysics with Mass = 123.0f; Friction = 1.5f; Restitution = 0.8f; GravityFactor = 2.0f }
    let zone = { authoredZone () with Actors = [| { Base = entity "npc"; AssetId = "aiko"; ShowCollider = false; Physics = physics } |] }
    let contents = { Zones = Map.ofList [ "start", zone ]; Cue = Fin }
    let registry = setContents "Intro" (Some contents) (create ())
    let text = Project.encode registry
    Assert.Contains("[Physics", text)
    let reloaded = Project.decode text
    let actor = (Map.find "start" (Option.get (tryContents "Intro" reloaded)).Zones).Actors.[0]
    Assert.Equal(123.0f, actor.Physics.Mass)
    Assert.Equal(1.5f, actor.Physics.Friction)
    Assert.Equal(0.8f, actor.Physics.Restitution)
    Assert.Equal(2.0f, actor.Physics.GravityFactor)
    Assert.Equal(Dynamic, actor.Physics.Mobility)

[<Fact>]
let ``an actor record without a physics field decodes as dynamic`` () =
    let entityDto : Project.EntityDto =
        { Name = "aiko"; Position = { X = 0.0f; Y = 0.0f; Z = 0.0f }; Rotation = { X = 0.0f; Y = 0.0f; Z = 0.0f; W = 1.0f }
          Scale = { X = 1.0f; Y = 1.0f; Z = 1.0f }; Enabled = true; Visible = true; Selectable = true; Draggable = true }
    let staging : Project.StagingDto =
        { Environment = None
          Light = { Pitch = 0.0f; Yaw = 0.0f; Color = { X = 1.0f; Y = 1.0f; Z = 1.0f }; Ambient = 0.1f }
          Skydome = { Enabled = false; Zenith = { X = 0.0f; Y = 0.0f; Z = 1.0f }; Horizon = { X = 1.0f; Y = 1.0f; Z = 1.0f } }
          Props = [||] }
    let zone : LegacyDtos.ZoneDto =
        { Staging = staging
          Actors = [| { Base = entityDto; AssetId = "aiko"; ShowCollider = false } |]
          Waypoints = [||]
          Triggers = [||] }
    let legacy : LegacyDtos.Project =
        { Version = 1
          Scenarios = [| { Name = "Legacy"; Contents = Some { Zones = Map.ofList [ "start", zone ]; Cue = Fin } } |] }

    let text = PrettyPrinter.prettyPrintValue legacy PrettyPrinter.defaultPrinter
    let reloaded = Project.decode text
    let actor = (Map.find "start" (Option.get (tryContents "Legacy" reloaded)).Zones).Actors.[0]
    Assert.Equal(Dynamic, actor.Physics.Mobility)
    Assert.Equal(dynamicPhysics, actor.Physics)

[<Fact>]
let ``an out-of-range physics value is rejected on decode`` () =
    let zone = { authoredZone () with Actors = [| { Base = entity "npc"; AssetId = "aiko"; ShowCollider = false; Physics = { dynamicPhysics with Mass = 1000.0f } } |] }
    let contents = { Zones = Map.ofList [ "start", zone ]; Cue = Fin }
    let registry = setContents "Intro" (Some contents) (create ())
    Assert.Throws<ArgumentException>(fun () -> Project.decode (Project.encode registry) |> ignore) |> ignore

[<Fact>]
let ``every dialogue ending shape round-trips, and the old exposit stays cue-managed`` () =
    let cases =
        [ Exposit("hi", Kind.Dialogue)
          ExpositWith("hi", Kind.Dialogue, Set.empty, ValueNone)
          ExpositWith("hi", Kind.Dialogue, Set.singleton ByPlayer, ValueNone)
          ExpositWith("hi", Kind.Thought, Set.singleton ByTimeout, ValueSome 3.5)
          ExpositWith("hi", Kind.Dialogue, Set.ofList [ ByPlayer; ByTimeout ], ValueSome 3.5)
          ExpositWait("hi", Kind.Dialogue, Set.empty, ValueNone)
          ExpositWait("hi", Kind.Dialogue, Set.singleton ByPlayer, ValueNone)
          ExpositWait("hi", Kind.Dialogue, Set.ofList [ ByPlayer; ByTimeout ], ValueSome 3.5)
          ExpositWaitState("hi", Kind.Dialogue, Set.empty, ValueNone)
          ExpositWaitState("hi", Kind.Dialogue, Set.ofList [ ByPlayer; ByTimeout ], ValueSome 3.5)
          CloseExposition ]

    for cue in cases do
        Assert.Equal(cue, cueRoundTrip cue)

    // the two-argument form persisted by earlier projects is the empty set, exactly
    match cueRoundTrip (Exposit("hi", Kind.Dialogue)) with
    | Exposit (text, Kind.Dialogue) -> Assert.Equal("hi", text)
    | other -> Assert.Fail(sprintf "the old Exposit decoded as %A" other)

[<Fact>]
let ``a captured scene round-trips`` () =
    let zones = Dictionary<string, Zone.Zone>()
    let main = Zone.create ()
    do ignore (Zone.spawnWaypoint main (entity "start"))
    do ignore (main.Actors.Add(actor "pupil"))
    zones.[GameplayState.startZone] <- main

    let registry = setContents "Intro" (Some (ScenarioRegistry.capture zones Fin)) (create ())

    Assert.Equal(Project.encode registry, Project.encode (Project.decode (Project.encode registry)))

[<Fact>]
let ``an empty library and exact real numbers round-trip`` () =
    let empty = create ()
    Assert.Equal(Project.encode empty, Project.encode (Project.decode (Project.encode empty)))

    let reloaded = Project.decode (Project.encode (library ()))
    let light = (Map.find "start" (Option.get (tryContents "Intro" reloaded)).Zones).Staging.Light
    Assert.Equal(0.2f, light.Ambient)
    Assert.Equal(Vector3(1.0f, 0.5f, 0.25f), light.Color)

// ── a missing file (FR-008) ──

[<Fact>]
let ``a missing file yields the empty library, reports nothing, and is not created`` () =
    let path = tempPath "absent"
    let before = Diagnostics.errorCount ()
    let registry = Project.loadFrom path
    Assert.Empty(names registry)
    Assert.Equal(before, Diagnostics.errorCount ())
    Assert.False(File.Exists path)

// ── change-driven persist (FR-005, FR-012, FR-013, FR-015, SC-007) ──

[<Fact>]
let ``persist writes on a change and nothing when unchanged, and touches nothing but the library`` () =
    withTempDir (fun _ ->
        // the first persist writes; the same instance again writes nothing
        let registry = library ()
        do Project.persist registry
        Assert.True(File.Exists "project.manifest")
        let written = File.GetLastWriteTimeUtc "project.manifest"
        do Project.persist registry
        Assert.Equal(written, File.GetLastWriteTimeUtc "project.manifest")

        // Content-based, so a write cannot hide behind an unchanged timestamp: plant a sentinel and
        // confirm an unchanged library leaves the file byte-for-byte alone (F-80).
        do File.WriteAllText("project.manifest", "sentinel")
        do Project.persist registry
        Assert.Equal("sentinel", File.ReadAllText "project.manifest")

        // a change (a new instance) is written
        do Project.persist (remove "Draft" registry)
        Assert.DoesNotContain("Draft", File.ReadAllText "project.manifest")

        // an undo restores the prior instance, and the file holds the scenario again
        do Project.persist registry
        Assert.Contains("Draft", File.ReadAllText "project.manifest")

        // a write and a load touch nothing but the library: the undo history and the zones are untouched
        let mutable state = headlessState ()
        let undoBefore = state.Editor.Undo
        let zonesBefore = state.Zones.Count
        do Project.persist state.Scenarios
        state.Scenarios <- Project.loadFrom "project.manifest"
        Assert.Equal(undoBefore, state.Editor.Undo)
        Assert.Equal(zonesBefore, state.Zones.Count))

[<Fact>]
let ``a failed write is not recorded as written and is retried after the interval`` () =
    // F-03: a failure must leave the edit pending (so a retry can land it) without attempting or
    // reporting once per frame. The target is made unwritable by placing a directory where the file
    // must go; removing it restores the path.
    withTempDir (fun _ ->
        do Directory.CreateDirectory "project.manifest" |> ignore
        let registry = library ()
        let before = Diagnostics.errorCount ()
        do Project.persist registry
        Assert.True(Diagnostics.errorCount () > before)
        do Directory.Delete("project.manifest", true)
        Assert.False(File.Exists "project.manifest")

        // below the 0.5 s retry interval: no attempt, so no new report and no file
        let before = Diagnostics.errorCount ()
        do Project.persistFrame 0.2 registry
        Assert.Equal(before, Diagnostics.errorCount ())
        Assert.False(File.Exists "project.manifest")

        // past the interval: the pending edit is retried and lands
        do Project.persistFrame 0.4 registry
        Assert.True(File.Exists "project.manifest")
        Assert.Equal<string list>(names registry, names (Project.loadFrom "project.manifest")))

// ── a damaged file (FR-009, FR-010) ──

[<Fact>]
let ``a malformed file is reported, yields the empty library, and is left unchanged`` () =
    let path = tempPath "malformed"
    do File.WriteAllText(path, "this is not a project file")
    try
        let before = Diagnostics.errorCount ()
        Assert.Empty(names (Project.loadFrom path))
        Assert.True(Diagnostics.errorCount () > before)
        Assert.Equal("this is not a project file", File.ReadAllText path)
    finally
        do File.Delete path

[<Fact>]
let ``an unknown version is treated as a damaged file`` () =
    let path = tempPath "version"
    do File.WriteAllText(path, "[[Version 99] [Scenarios []]]")
    try
        let before = Diagnostics.errorCount ()
        Assert.Empty(names (Project.loadFrom path))
        Assert.True(Diagnostics.errorCount () > before)
        Assert.Equal("[[Version 99] [Scenarios []]]", File.ReadAllText path)
    finally
        do File.Delete path

// ── the atomic write (FR-006, SC-006) ──

[<Fact>]
let ``saveTo writes atomically, leaves no temporary, and reloads`` () =
    let path = tempPath "save"
    try
        do Project.saveTo path (library ())
        Assert.True(File.Exists path)
        Assert.False(File.Exists(path + ".tmp"))
        Assert.Equal<string list>(names (library ()), names (Project.loadFrom path))
    finally
        do File.Delete path

[<Fact>]
let ``a failed write is reported and leaves the existing good file`` () =
    let dir = Path.Combine(Path.GetTempPath(), sprintf "sugoi-fail-%s" (Guid.NewGuid().ToString "N"))
    do Directory.CreateDirectory dir |> ignore
    try
        let path = Path.Combine(dir, "project.manifest")
        do Project.saveTo path (library ())
        let good = File.ReadAllText path

        // a target whose parent directory does not exist: the write cannot complete
        let before = Diagnostics.errorCount ()
        do Project.saveTo (Path.Combine(dir, "missing-subdir", "project.manifest")) (library ())

        Assert.True(Diagnostics.errorCount () > before)
        Assert.Equal(good, File.ReadAllText path)
    finally
        do Directory.Delete(dir, true)

// ── live reload: an external change (spec 096 FR-001..FR-003, FR-007, SC-004) ──

[<Fact>]
let ``an external edit is picked up, a rename-over save too, and the game's own write is not`` () =
    withTempDir (fun _ ->
        // the game writes the library, so the file holds a baseline
        let registry = library ()
        do Project.persist registry
        Assert.True((Project.pollExternal 1.0).IsNone) // our own write is not a change

        // an external rewrite is picked up
        let changed = setContents "Intro" None registry
        do File.WriteAllText("project.manifest", Project.encode changed)
        match Project.pollExternal 1.0 with
        | Some reloaded -> Assert.Equal(Project.encode changed, Project.encode reloaded)
        | _ -> Assert.Fail "the external rewrite was not picked up"

        // a temp-file-then-rename save is picked up the same way (a content poll, not an event)
        do File.WriteAllText("project.manifest.tmp", Project.encode registry)
        do File.Move("project.manifest.tmp", "project.manifest", true)
        match Project.pollExternal 1.0 with
        | Some reloaded -> Assert.Equal(Project.encode registry, Project.encode reloaded)
        | _ -> Assert.Fail "the rename-over save was not picked up"

        // an identical rewrite is not a change
        Assert.True((Project.pollExternal 1.0).IsNone)

        // installing a reload touches nothing but the library
        let mutable state = headlessState ()
        let undoBefore = state.Editor.Undo
        let zonesBefore = state.Zones.Count
        state.Scenarios <- registry
        Assert.Equal(undoBefore, state.Editor.Undo)
        Assert.Equal(zonesBefore, state.Zones.Count))

// ── live reload: a damaged or deleted file keeps the running library (spec 096 FR-004, FR-005, SC-003) ──

[<Fact>]
let ``a damaged or deleted external file leaves the running library alone`` () =
    withTempDir (fun _ ->
        do File.WriteAllText("project.manifest", Project.encode (library ()))
        do ignore (Project.load ()) // establish the baseline

        // a malformed write is reported and installs nothing, and the file is left alone
        do File.WriteAllText("project.manifest", "not a project")
        let before = Diagnostics.errorCount ()
        Assert.True((Project.pollExternal 1.0).IsNone)
        Assert.True(Diagnostics.errorCount () > before)
        Assert.Equal("not a project", File.ReadAllText "project.manifest")

        // a later valid write is still picked up (the retry self-heals)
        do File.WriteAllText("project.manifest", Project.encode (setContents "Intro" None (library ())))
        Assert.True((Project.pollExternal 1.0).IsSome)

        // a deletion installs nothing
        do File.Delete "project.manifest"
        Assert.True((Project.pollExternal 1.0).IsNone))

// ── the gate and the interval (spec 096 FR-001, FR-008, FR-011) ──

[<Fact>]
let ``the poll gate is pure, defaults to on, and the interval suppresses a second immediate read`` () =
    withTempDir (fun _ ->
        Assert.True((EditorState.create ()).LiveReload)
        Assert.True(Project.pollEnabled true true)
        Assert.False(Project.pollEnabled true false)
        Assert.False(Project.pollEnabled false true)
        Assert.False(Project.pollEnabled false false)

        do File.WriteAllText("project.manifest", Project.encode (library ()))
        do ignore (Project.load ())
        // read once to zero the interval accumulator
        do File.WriteAllText("project.manifest", Project.encode (setContents "Intro" None (library ())))
        do ignore (Project.pollExternal 1.0)

        // a fresh external change, just below and then past the interval
        do File.WriteAllText("project.manifest", Project.encode (library ()))
        Assert.True((Project.pollExternal 0.1).IsNone)
        Assert.True((Project.pollExternal 0.1).IsNone)
        Assert.True((Project.pollExternal 0.1).IsSome))
