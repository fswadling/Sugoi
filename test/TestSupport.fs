module TestSupport

// Shared device-free fixtures. An environment asset is built in the **loaded** shape (its whole unit
// present, GPU handles defaulted) so the real `Zone`/physics/render paths run headlessly; the GPU device
// is never dereferenced on those paths. Everything here is a function rather than a module-level value,
// so no test fact can observe another's mutations.

open System
open System.Collections.Generic
open System.IO
open System.Numerics
open Xunit
open Prime
open Cue
open EnvironmentAsset

// ── environments and props ──

let bounds : Gltf.Bounds =
    { Min = Vector3.Zero; Max = Vector3.One }

// A submesh with data-only geometry and defaulted GPU handles (never read on these paths).
let loadedSubmesh (positions: float32 array) (indices: int array) : LoadedSubmesh =
    { LoadedSubmesh.Positions = positions
      Normals = Array.zeroCreate positions.Length
      Uvs = Array.zeroCreate (positions.Length / 3 * 2)
      Indices = indices
      MaterialIndex = -1
      Mesh = Unchecked.defaultof<StaticMesh.StaticMesh>
      Material = Unchecked.defaultof<Texture.Material> }

/// One triangle: the geometry every headless room/prop fixture uses.
let triangleSubmesh () : LoadedSubmesh =
    loadedSubmesh
        [| 0.0f; 0.0f; 0.0f;  1.0f; 0.0f; 0.0f;  0.0f; 1.0f; 0.0f |]
        [| 0; 1; 2 |]

/// An environment part: a named node covering `submeshCount` submeshes from `submeshIndex`.
let environmentPart (name: string) (submeshIndex: int) (submeshCount: int) : EnvironmentPart =
    { Name = name
      HierarchyPath = name
      World = Matrix4x4.Identity
      Bounds = bounds
      SubmeshIndex = submeshIndex
      SubmeshCount = submeshCount }

// An environment/prop asset in the loaded shape: its whole unit present, GPU handles defaulted.
let residentAsset (id: string) (submeshes: LoadedSubmesh array) (parts: EnvironmentPart array) : EnvironmentAsset =
    { Id = id
      Name = id
      ModelPath = ""
      Orientation = Quaternion.Identity
      Residency =
        Resident
            { EnvironmentLoaded.Submeshes = submeshes
              Parts = parts
              Bounds = bounds
              Materials = [||]
              SubmeshBytes = 0L
              TextureKeys = [||] } }

/// The one-triangle room, with a single `wall` part covering its only submesh.
let roomAsset () : EnvironmentAsset =
    residentAsset "room" [| triangleSubmesh () |] [| environmentPart "wall" 0 1 |]

/// The one-triangle crate, a whole-model prop.
let crateAsset () : EnvironmentAsset =
    residentAsset "crate" [| triangleSubmesh () |] [||]

// ── managers and shared state ──

// A device-free asset manager: safe because the prop/part paths never dereference Device.
let manager
    (device: Gpu.Device)
    (actors: Dictionary<string, ActorAsset.ActorAsset>)
    (props: Dictionary<string, EnvironmentAsset>)
    (environments: Dictionary<string, EnvironmentAsset>) : Assets.AssetManager =
    { Device = device
      Root = ""
      Actors = actors
      ActorsByName = Assets.indexByName actors
      Props = props
      Environments = environments
      Loader =
        lazy
            { Device = device
              White = Unchecked.defaultof<Texture.Texture>
              DefaultMaterial = Unchecked.defaultof<Texture.Material>
              Textures = Dictionary() }
      Cache = Lru.create ()
      ResidentBytes = 0L
      Pool = None
      Uploads = Dictionary<string, Assets.PendingUpload>()
      PinnedIds = HashSet<string>()
      AudioAssets = Dictionary<string, AudioAsset.AudioAsset>()
      Audio = Audio.unavailable
      ActorNamesCache = None
      MusicNamesCache = None
      SoundAssetsCache = None }

let private dictionaryOf (pairs: (string * 'T) list) : Dictionary<string, 'T> =
    let dictionary = Dictionary<string, 'T>()
    for (key, value) in pairs do dictionary.[key] <- value
    dictionary

/// A device-free manager with the given actors, props and environments.
let managerWith
    (actors: (string * ActorAsset.ActorAsset) list)
    (props: (string * EnvironmentAsset) list)
    (environments: (string * EnvironmentAsset) list) : Assets.AssetManager =
    manager IntPtr.Zero (dictionaryOf actors) (dictionaryOf props) (dictionaryOf environments)

/// A device-free manager with no assets at all.
let emptyManager () : Assets.AssetManager =
    manager IntPtr.Zero (Dictionary()) (Dictionary()) (Dictionary())

/// A headless `SharedState`: a device-free empty manager whose audio is `unavailable`, an empty mix, a
/// fresh rack and a device-free voice pool.
let headlessSharedWith (assets: Assets.AssetManager) : SharedState.SharedState =
    { Gpu = Unchecked.defaultof<Gpu.Gpu>
      Assets = assets
      Advents = OSet.ofSeq1 []
      Mix = Music.empty
      Rack = Music.newRack ()
      Voices = Sound.newVoices Audio.unavailable }

let headlessShared () : SharedState.SharedState =
    headlessSharedWith (emptyManager ())

// ── gameplay state ──

/// A headless gameplay state: `Unchecked.defaultof` for the GPU pieces, so every path that reads only
/// authored/runtime state runs with no device.
let headlessState () : GameplayState.GameplayState =
    GameplayState.create
        (Unchecked.defaultof<DebugDraw.DebugDraw>)
        (Unchecked.defaultof<DebugDraw.DebugDraw>)
        (Unchecked.defaultof<Ui.Ui>)
        (Unchecked.defaultof<Skydome.Skydome>)

/// A gameplay state whose start zone is `zone`, ready for the real `Zone`/`Spawning` paths.
let stateWithZone (zone: Zone.Zone) : GameplayState.GameplayState =
    let state = headlessState ()
    state.Zones.[GameplayState.startZone] <- zone
    state

// ── entity constructors ──

let entity (name: string) : Entity.Entity =
    Entity.baseEntity name Entity.transformIdentity

// An actor value with no body or renderer: enough for the authored/state paths, which never touch them.
let actor (name: string) : Entity.Actor =
    { State =
        { Base = entity name
          AssetId = ""
          ShowCollider = false
          Physics = Entity.dynamicPhysics }
      Physics = None
      Renderer = Unchecked.defaultof<ActorRender.ActorRender> }

// A placed prop (a whole model) with no body or renderer.
let prop (name: string) : Entity.Prop =
    { State =
        { Base = entity name
          AssetId = Some "crate"
          SubmeshIndex = 0
          SubmeshCount = 0 }
      Physics = None
      Renderer = Unchecked.defaultof<EnvironmentRender.EnvironmentRender> }

// An environment part value (a submesh range of the zone's loaded environment), with no body or renderer.
let part (name: string) : Entity.Prop =
    { State =
        { Base = entity name
          AssetId = None
          SubmeshIndex = 0
          SubmeshCount = 0 }
      Physics = None
      Renderer = Unchecked.defaultof<EnvironmentRender.EnvironmentRender> }

// ── actor assets ──

/// A zero model: enough for `ActorRender.create` to hold no clip and pose nothing.
let emptyModel () : Iqm.Model =
    { Meshes = [||]
      Positions = [||]
      Normals = None
      Uvs = None
      BlendIndices = None
      BlendWeights = None
      Triangles = [||]
      Joints = [||]
      Poses = [||]
      Anims = [||]
      FrameChannels = [||]
      NumFrameChannels = 0
      PoseChannelOffsets = [||] }

/// An actor asset in the loaded shape, its GPU mesh defaulted: `ActorRender.create` builds a per-instance
/// renderer from it, and the zero-device buffers it creates are never read in these tests.
let actorAsset (id: string) : ActorAsset.ActorAsset =
    { Id = id
      Name = id
      ModelPath = ""
      Bounds = bounds
      Mesh = Unchecked.defaultof<Mesh.Mesh>
      Model = Actor.createModel (emptyModel ())
      Morphs = None
      Portraits = None
      SwayChains = [||] }

// ── a populated classroom ──

/// A headless classroom rig: the `room` environment loaded into a fresh active zone, with one `crate`
/// prop placed. Returns the manager, the state and the active zone.
let populatedClassroom () : Assets.AssetManager * GameplayState.GameplayState * Zone.Zone =
    let assets = managerWith [] [ "crate", crateAsset () ] [ "room", roomAsset () ]
    let state = stateWithZone (Zone.create ())
    let zone = GameplayState.activeZone state
    do Zone.loadEnvironment zone state.Physics assets "room" |> ignore
    do Zone.spawnProp zone state.Physics assets (entity "crate") "crate" |> ignore
    assets, state, zone

// ── assertions ──

/// Assert that `action` allocates no managed bytes on this thread. `warmup` runs first so tiered JIT
/// and one-time static initialisation fall outside the measurement; `iterations` is the measured run.
let assertAllocatesNothing (warmup: int) (iterations: int) (action: unit -> unit) : unit =
    for _ in 1 .. warmup do action ()
    let before = GC.GetAllocatedBytesForCurrentThread()
    for _ in 1 .. iterations do action ()
    let after = GC.GetAllocatedBytesForCurrentThread()
    Assert.Equal(0L, after - before)

let assertNear (tolerance: float32) (expected: float32) (actual: float32) : unit =
    Assert.True(abs (actual - expected) <= tolerance, sprintf "expected %f, got %f" expected actual)

let assertVec3 (tolerance: float32) (expected: Vector3) (actual: Vector3) : unit =
    Assert.True((expected - actual).Length() <= tolerance, sprintf "expected %A, got %A" expected actual)

/// Run `body` with a fresh temp directory as the process's current directory, deleting it recursively
/// afterwards. The suite serialises collections (`xunit.runner.json`), so the process-global directory
/// change is safe. The directory is passed to `body` as well as being the current directory.
let withTempDir (body: string -> unit) : unit =
    let dir = Path.Combine(Path.GetTempPath(), sprintf "sugoi-test-%s" (Guid.NewGuid().ToString "N"))
    do Directory.CreateDirectory dir |> ignore
    let original = Directory.GetCurrentDirectory ()
    do Directory.SetCurrentDirectory dir
    try body dir
    finally
        do Directory.SetCurrentDirectory original
        do Directory.Delete(dir, true)

// ── cue authoring sugar (shared by the cue tests) ──

/// Authoring sugar: a list of ops as the cue they denote.
let seqOf (cues: Cue list) : Cue =
    match cues with
    | [] -> Fin
    | [ only ] -> only
    | head :: tail -> Sequence(head, FDeque.ofList tail)

/// A line's ending set and timeout as a test reads it: the author-facing summary, with the duration
/// appended when one is set so a timeout without `ByTimeout` is still visible.
let describeEndings (endings: Set<Dialogue.Ending>) (timeout: float voption) : string =
    let summary = Dialogue.summary endings timeout
    match timeout with
    | ValueSome seconds -> sprintf "%s@%g" summary seconds
    | ValueNone -> summary

/// The cue as a person (or the editor's panel) reads it. The in-flight `WaitState` renders as the `Wait`
/// it came from: the view is what is left to run, and its front is the running op.
let rec describe (cue: Cue) : string =
    match cue with
    | Fin -> "Fin"
    | Sequence (current, rest) ->
        let parts = Seq.append (Seq.singleton (describe current)) (FDeque.toSeq rest |> Seq.map describe)
        "Seq[" + String.concat ", " parts + "]"
    | Parallel (l, r) -> sprintf "Par(%s, %s)" (describe l) (describe r)
    | Fork c -> sprintf "Fork(%s)" (describe c)
    | Wait s | WaitState (s, _) -> sprintf "Wait %g" s
    | FadeOut d | FadeOutState (_, d, _) -> sprintf "FadeOut %g" d
    | FadeIn d | FadeInState (_, d, _) -> sprintf "FadeIn %g" d
    | Exposit (t, _) -> sprintf "Exposit(%s)" t
    | ExpositWith (t, _, e, tm) -> sprintf "ExpositWith(%s,%s)" t (describeEndings e tm)
    | ExpositWait (t, _, e, tm) | ExpositWaitState (t, _, e, tm) -> sprintf "ExpositWait(%s,%s)" t (describeEndings e tm)
    | CloseExposition -> "CloseExposition"
    | Print t -> sprintf "Print(%s)" t
    | SetZone z -> sprintf "Zone(%s)" (z)
    | RemoveZone z -> sprintf "RemoveZone(%s)" (z)
    | AddActor (p, a, asset, w) -> sprintf "AddActor(%s,%s,%s,%s)" (p) a asset w
    | RemoveActor (p, a) -> sprintf "RemoveActor(%s,%s)" (p) a
    | Animate (p, a, clip, _, d) -> sprintf "Animate(%s,%s,%s,%g)" (p) a clip d
    | CrossFadeAnimation (p, a, f, t, _, d) -> sprintf "CrossFadeAnimation(%s,%s,%s,%s,%g)" (p) a f t d
    | Morph (p, a, m, _, d) -> sprintf "Morph(%s,%s,%s,%g)" (p) a m d
    | CrossFadeMorph (p, a, f, t, _, d) -> sprintf "CrossFadeMorph(%s,%s,%s,%s,%g)" (p) a f t d
    | BlendState (a, _, _, morph, f, t, _, _, _, _, _) ->
        if f = t then sprintf "%s(%s,%s)" (if morph then "Morph" else "Animate") a f
        else sprintf "%s(%s,%s,%s)" (if morph then "CrossFadeMorph" else "CrossFadeAnimation") a f t
    | MoveActor (a, p, _, _, _) -> sprintf "MoveActor(%s,%s)" a p
    | MoveActorState (a, _, _, _, _, _, _, _, _, _, _, _) -> sprintf "MoveActor(%s)" a
    | RotateActor (a, w, d) -> sprintf "RotateActor(%s,%s,%g)" a w d
    | RotateActorState (a, _, _, _, _, _, _) -> sprintf "RotateActor(%s)" a
    | EnablePlayer (p, a) -> sprintf "EnablePlayer(%s,%s)" (p) a
    | DisablePlayer (p, a) -> sprintf "DisablePlayer(%s,%s)" (p) a
    | EnableTrigger (p, t) -> sprintf "EnableTrigger(%s,%s)" (p) t
    | DisableTrigger (p, t) -> sprintf "DisableTrigger(%s,%s)" (p) t
    | SetCamera (p, w) -> sprintf "SetCamera(%s,%s)" (p) w
    | FlyCamera (p, path, _) -> sprintf "FlyCamera(%s,%s)" (p) path
    | FlyCameraState (path, _, _, _, _) -> sprintf "FlyCamera(%s)" path
    | AddAdvent m -> sprintf "Add(%s)" m
    | RemoveAdvent m -> sprintf "Remove(%s)" m
    | Await m | AwaitState m -> sprintf "Await(%s)" m
    | If (m, t, e) -> sprintf "If(%s, %s, %s)" m (describe t) (describe e)
    | ForkScenario scenario -> sprintf "ForkScenario(%s)" scenario
    | AddMusic song -> sprintf "AddMusic(%s)" song
    | RemoveMusic song -> sprintf "RemoveMusic(%s)" song
    | PlayMusic song -> sprintf "PlayMusic(%s)" song
    | StopMusic song -> sprintf "StopMusic(%s)" song
    | PlayBed -> "PlayBed"
    | StopBed -> "StopBed"
    | SetMusicLevel (song, level, d) -> sprintf "SetMusicLevel(%s,%g,%g)" song level d
    | SetMusicMaster (level, d) -> sprintf "SetMusicMaster(%g,%g)" level d
    | MusicLevelState (song, _, target, d, _) -> sprintf "MusicLevelState(%s,%g,%g)" song target d
    | MusicMasterState (_, target, d, _) -> sprintf "MusicMasterState(%g,%g)" target d
    | CrossFadeMusic (fromSong, toSong, d) -> sprintf "CrossFadeMusic(%s,%s,%g)" fromSong toSong d
    | CrossFadeMusicState (fromSong, toSong, _, _, d, _) -> sprintf "CrossFadeMusicState(%s,%s,%g)" fromSong toSong d
    | PlaySound sound -> sprintf "PlaySound(%s)" sound
