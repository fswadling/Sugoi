module ActorNameTests

// Actors are addressed by name (spec 072) and, since spec 075, resolved per zone: a cue op names its place and
// resolves the actor there with a `Zone`-local name scan (`Zone.tryActorByName`, the pattern
// `tryWaypointByName` / `tryTriggerByName` already use). There is no scan over the present places. Headless: an
// actor is spawned through the real `Spawning` / `Zone` path against a fabricated actor asset whose GPU handles
// are defaulted, so the scan is asserted with no device.

open System
open System.Collections.Generic
open System.Numerics
open Xunit
open Gltf
open Entity
open TestSupport

// A zero model: enough for `ActorRender.create` to hold no clip and pose nothing.
let private emptyModel () : Iqm.Model =
    { Meshes = [||]
      Positions = [||]
      Normals = None
      Uvs = None
      Tangents = None
      BlendIndices = None
      BlendWeights = None
      Triangles = [||]
      Joints = [||]
      Poses = [||]
      Anims = [||]
      FrameChannels = [||]
      NumFrameChannels = 0
      PoseChannelOffsets = [||] }

// An actor asset in the loaded shape, its GPU mesh defaulted: `createActorRender` builds a per-instance
// renderer from it, and the zero-device buffers it creates are never read here.
let private actorAsset (id: string) : ActorAsset.ActorAsset =
    { Id = id
      Name = id
      ModelPath = ""
      Bounds = { Min = Vector3.Zero; Max = Vector3.One }
      Mesh = Unchecked.defaultof<Mesh.Mesh>
      Model = Actor.createModel (emptyModel ())
      Morphs = None
      Portraits = None }

let private headlessAssets () : Assets.AssetManager =
    let actors = Dictionary<string, ActorAsset.ActorAsset>()
    actors.["aiko"] <- actorAsset "aiko"
    TestSupport.manager IntPtr.Zero actors (Dictionary()) (Dictionary())

let private sharedState (assets: Assets.AssetManager) : SharedState.SharedState =
    { Gpu = Unchecked.defaultof<Gpu.Gpu>
      Assets = assets
      Advents = Advent.empty }

let private newState () : GameplayState.GameplayState =
    GameplayState.create (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<Ui.Ui>)

// Spawn an actor named `name` into the active zone, through the editor path — no cast argument: an actor
// carries no role, its name is its identity (spec 072 FR-001).
let private spawn (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (name: string) : EntityRef =
    do Spawning.spawnActor shared &state (Spawning.baseEntity name) "aiko"
    state.Editor.Selection.Value

// ── Zone.tryActorByName ──

[<Fact>]
let ``Zone.tryActorByName finds a named live actor by its name and misses an unknown one`` () =
    let shared = sharedState (headlessAssets ())
    let mutable state = newState ()
    let ref = spawn shared &state "Aiko"
    let zone = GameplayState.activeZone state

    // the actor's identity is its name; nothing else records who it is
    Assert.Equal("Aiko", zone.Actors.Get(handleOf ref).State.Base.Name)
    Assert.Equal(Some ref, Zone.tryActorByName zone "Aiko")
    Assert.Equal(None, Zone.tryActorByName zone "Akane")

[<Fact>]
let ``a duplicate name resolves to the first actor, silently`` () =
    let shared = sharedState (headlessAssets ())
    let mutable state = newState ()
    let first = spawn shared &state "Aiko"
    let second = spawn shared &state "Aiko"
    Assert.NotEqual(first, second)
    let zone = GameplayState.activeZone state
    Assert.Equal(Some first, Zone.tryActorByName zone "Aiko")
