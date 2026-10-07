module CastIndexTests

// The runtime cast (spec 063): an actor records the character it plays, and the gameplay state
// indexes it by character so a cue can name an actor by who it is. Headless: an actor is spawned
// through the real `Spawning`/`Zone` path against a fabricated actor asset whose GPU handles are
// defaulted (`SDL.CreateGPUBuffer` on a zero device returns no buffer and is never read on these
// paths), so resolution, uniqueness, undo/redo and zone delete are all asserted with no device.

open System
open System.Collections.Generic
open System.Numerics
open Xunit
open Gltf
open Physics
open Entity
open SlotMap
open Place
open Command
open TestSupport

let private mainPlace : Place = Place.start
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

// An actor asset in the loaded shape, its GPU mesh defaulted: `createActorRender` builds a
// per-instance renderer from it, and the zero-device buffers it creates are never read here.
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
      Cast = { Casting.Bindings = Dictionary() }
      Advents = Set.empty }

let private newState () : GameplayState.GameplayState =
    GameplayState.create (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<Ui.Ui>)

// Spawn the member that plays `character` into the active zone, through the editor path.
let private spawn (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (character: Roster.Character) : unit =
    do Spawning.spawnActor shared &state (Spawning.baseEntity (Roster.characterName character)) "aiko" (Some character)

// ── US1 — an actor knows which character it plays ──

[<Fact>]
let ``a spawned cast member resolves by its character, and a non-cast actor does not`` () =
    let shared = sharedState (headlessAssets ())
    let mutable state = newState ()

    do spawn shared &state Roster.Aiko
    let ref = state.Editor.Selection.Value
    Assert.Equal(Some (mainPlace, ref), GameplayState.tryCastActor state Roster.Aiko)
    // The role is authored state on the actor, not a second copy beside the index.
    Assert.Equal(Some Roster.Aiko, (GameplayState.activeZone state).Actors.Get(handleOf ref).State.Cast)

    // A non-cast actor records none and indexes none.
    do Spawning.spawnActor shared &state (Spawning.baseEntity "Extra") "aiko" None
    let extra = state.Editor.Selection.Value
    Assert.Equal(None, (GameplayState.activeZone state).Actors.Get(handleOf extra).State.Cast)
    Assert.Equal(None, GameplayState.tryCastActor state Roster.Akane)
    Assert.Equal(Some (mainPlace, ref), GameplayState.tryCastActor state Roster.Aiko)

[<Fact>]
let ``resolving a character does not depend on which zone is active`` () =
    let shared = sharedState (headlessAssets ())
    let mutable state = newState ()
    GameplayState.addZone shared &state classroomPlace
    do spawn shared &state Roster.Aiko
    let ref = state.Editor.Selection.Value
    Assert.Equal(classroomPlace, state.ActiveZone)
    Assert.Equal(Some (classroomPlace, ref), GameplayState.tryCastActor state Roster.Aiko)

    // Move to another zone: the index is global, so the answer does not change.
    GameplayState.addZone shared &state atriumPlace
    Assert.Equal(atriumPlace, state.ActiveZone)
    Assert.Equal(Some (classroomPlace, ref), GameplayState.tryCastActor state Roster.Aiko)

// ── US2 — one character at most, across all zones ──

[<Fact>]
let ``a duplicate spawn in the same zone performs nothing`` () =
    let shared = sharedState (headlessAssets ())
    let mutable state = newState ()
    do spawn shared &state Roster.Aiko
    let ref = state.Editor.Selection.Value
    let count = (GameplayState.activeZone state).Actors.Count

    do spawn shared &state Roster.Aiko

    Assert.Equal(count, (GameplayState.activeZone state).Actors.Count)
    Assert.Equal(Some (mainPlace, ref), GameplayState.tryCastActor state Roster.Aiko)

[<Fact>]
let ``a duplicate spawn in another zone performs nothing and reports where the member is`` () =
    let shared = sharedState (headlessAssets ())
    let mutable state = newState ()
    do spawn shared &state Roster.Aiko
    let ref = state.Editor.Selection.Value

    GameplayState.addZone shared &state classroomPlace
    let classroom = GameplayState.activeZone state
    Assert.Equal(0u, classroom.Actors.Count)

    // The cross-zone repeat is a no-op (and the diagnostic naming Aiko in the other zone is written to
    // stderr; the count and the index are what this asserts).
    do spawn shared &state Roster.Aiko

    Assert.Equal(0u, classroom.Actors.Count)
    Assert.Equal(Some (mainPlace, ref), GameplayState.tryCastActor state Roster.Aiko)

[<Fact>]
let ``the index refuses a second actor for the same character`` () =
    let shared = sharedState (headlessAssets ())
    let mutable state = newState ()

    GameplayState.addCast &state Roster.Aiko mainPlace (actorRef 0u 0u)
    let mutable raised = false
    try GameplayState.addCast &state Roster.Aiko atriumPlace (actorRef 1u 0u) with _ -> raised <- true
    Assert.True raised

[<Fact>]
let ``removeCastZone drops only that zone's members`` () =
    let shared = sharedState (headlessAssets ())
    let mutable state = newState ()

    GameplayState.addCast &state Roster.Aiko mainPlace (actorRef 0u 0u)
    GameplayState.addCast &state Roster.Akane atriumPlace (actorRef 1u 0u)
    GameplayState.removeCastZone &state mainPlace

    Assert.Equal(None, GameplayState.tryCastActor state Roster.Aiko)
    Assert.Equal(Some (atriumPlace, actorRef 1u 0u), GameplayState.tryCastActor state Roster.Akane)

// ── US3 — the binding survives undo/redo and zone delete/restore ──

[<Fact>]
let ``undoing a cast member's creation and redoing it keeps the index exact`` () =
    let shared = sharedState (headlessAssets ())
    let mutable state = newState ()
    do spawn shared &state Roster.Aiko
    let ref = state.Editor.Selection.Value

    do History.undoHistory shared &state
    Assert.Equal(None, GameplayState.tryCastActor state Roster.Aiko)
    Assert.False(Zone.isAlive (GameplayState.activeZone state) ref)

    do History.redoHistory shared &state
    Assert.Equal(Some (mainPlace, ref), GameplayState.tryCastActor state Roster.Aiko)
    Assert.True(Zone.isAlive (GameplayState.activeZone state) ref)

[<Fact>]
let ``deleting a cast member and undoing it keeps the index exact`` () =
    let shared = sharedState (headlessAssets ())
    let mutable state = newState ()
    do spawn shared &state Roster.Aiko
    let ref = state.Editor.Selection.Value

    do Selection.deleteRef shared &state ref
    Assert.Equal(None, GameplayState.tryCastActor state Roster.Aiko)
    Assert.False(Zone.isAlive (GameplayState.activeZone state) ref)

    do History.undoHistory shared &state
    Assert.Equal(Some (mainPlace, ref), GameplayState.tryCastActor state Roster.Aiko)
    Assert.True(Zone.isAlive (GameplayState.activeZone state) ref)

    do History.redoHistory shared &state
    Assert.Equal(None, GameplayState.tryCastActor state Roster.Aiko)

[<Fact>]
let ``deleting a zone holding a cast member and undoing it keeps the index exact`` () =
    let shared = sharedState (headlessAssets ())
    let mutable state = newState ()

    GameplayState.addZone shared &state classroomPlace
    do spawn shared &state Roster.Aiko
    let ref = state.Editor.Selection.Value
    Assert.Equal(classroomPlace, state.ActiveZone)

    // The active place cannot be made absent, so switch away first.
    GameplayState.switchZone shared &state mainPlace
    do History.commit shared &state (ZoneDelete (mainPlace, classroomPlace, Zone.captureZone state.Zones.[classroomPlace]))
    Assert.Equal(None, GameplayState.tryCastActor state Roster.Aiko)

    do History.undoHistory shared &state
    Assert.Equal(Some (classroomPlace, ref), GameplayState.tryCastActor state Roster.Aiko)
    Assert.True(Zone.isAlive state.Zones.[classroomPlace] ref)

    do History.redoHistory shared &state
    Assert.Equal(None, GameplayState.tryCastActor state Roster.Aiko)
    Assert.False(state.Zones.ContainsKey classroomPlace)
