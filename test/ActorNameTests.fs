module ActorNameTests

// Actors are addressed by name (spec 072): the cast role and the runtime cast index are gone, so resolution is
// a name scan over the present places — the same pattern `tryWaypointByName` / `tryTriggerByName` already use.
// Headless: an actor is spawned through the real `Spawning` / `Zone` path against a fabricated actor asset
// whose GPU handles are defaulted, so the scan is asserted with no device.

open System
open System.Collections.Generic
open System.Numerics
open Xunit
open Gltf
open Entity
open Place
open TestSupport

let private classroomPlace : Place = Place.ArtRoom Period.Evening
let private atriumPlace : Place = Place.Island

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
      Advents = Set.empty }

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

// ── GameplayState.tryActorByName — the resolver ──

[<Fact>]
let ``the resolver finds a named actor across the present places, not just the active one`` () =
    let shared = sharedState (headlessAssets ())
    let mutable state = newState ()
    GameplayState.addZone shared &state classroomPlace
    let ref = spawn shared &state "Aiko"
    Assert.Equal(classroomPlace, state.ActiveZone)
    Assert.Equal(Some (classroomPlace, ref), (match GameplayState.tryActorByName state "Aiko" with Some (p, _, r) -> Some (p, r) | None -> None))

    // the answer does not depend on the active place: it is a scan, not an index
    GameplayState.addZone shared &state atriumPlace
    Assert.Equal(atriumPlace, state.ActiveZone)
    Assert.Equal(Some (classroomPlace, ref), (match GameplayState.tryActorByName state "Aiko" with Some (p, _, r) -> Some (p, r) | None -> None))

[<Fact>]
let ``a duplicate name resolves to the first actor, silently`` () =
    let shared = sharedState (headlessAssets ())
    let mutable state = newState ()
    let first = spawn shared &state "Aiko"
    let second = spawn shared &state "Aiko"
    Assert.NotEqual(first, second)
    Assert.Equal(Some first, (match GameplayState.tryActorByName state "Aiko" with Some (_, _, r) -> Some r | None -> None))
