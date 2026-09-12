module SpawningTests

// Drive the real `Spawning`/`Zone` paths through a headless asset manager (the
// prop/part render path never touches the GPU device), so command pairing and kind
// dispatch are covered for real rather than mocked.

open Xunit
open System
open System.Collections.Generic
open System.Numerics
open Command
open Entity
open EnvironmentAsset
open Gltf
open Physics

let private entity (name: string) : Entity =
    { Name = name
      Transform = transformIdentity
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

// ── Headless asset fixture ──
// Only the fields the collider/render paths read are populated; a zero Device is
// safe because `EnvironmentRender.create` never dereferences it.

let private bounds (mn: Vector3) (mx: Vector3) : Gltf.Bounds =
    { Min = mn; Max = mx }

let private assetSubmesh (positions: float32 array) (indices: int array) : StaticSubmesh =
    { Mesh = Unchecked.defaultof<StaticMesh.StaticMesh>
      Material = Unchecked.defaultof<Texture.Material>
      Positions = positions
      Indices = indices }

let private assetPart (submeshIndex: int) (submeshCount: int) : EnvironmentPart =
    { Name = "wall"
      HierarchyPath = "wall"
      World = Matrix4x4.Identity
      Bounds = bounds Vector3.Zero Vector3.One
      SubmeshIndex = submeshIndex
      SubmeshCount = submeshCount }

// A one-triangle room whose single part covers submesh 0.
let private roomAsset () : EnvironmentAsset.EnvironmentAsset =
    { Id = "room"
      Name = "room"
      ModelPath = ""
      Orientation = Quaternion.Identity
      Bounds = bounds Vector3.Zero Vector3.One
      Submeshes = [| assetSubmesh [| 0.0f; 0.0f; 0.0f;  1.0f; 0.0f; 0.0f;  0.0f; 1.0f; 0.0f |] [| 0; 1; 2 |] |]
      Parts = [| assetPart 0 1 |] }

let private headlessAssets () : Assets.AssetManager =
    let environments = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    environments.["room"] <- roomAsset ()
    { Device = IntPtr.Zero
      Actors = Dictionary()
      Props = Dictionary()
      Environments = environments }

let private sharedState (assets: Assets.AssetManager) : SharedState.SharedState =
    { SharedState.Assets = assets; Physics = Physics.create () }

// A minimal gameplay state: the fake draws are never touched on these paths.
let private gameplayState (zone: Zone.Zone) : GameplayState.GameplayState =
    { Camera = Camera.create Vector3.Zero Vector3.Zero
      DebugDraw = Unchecked.defaultof<DebugDraw.DebugDraw>
      GizmoDraw = Unchecked.defaultof<DebugDraw.DebugDraw>
      MeshPipeline = Unchecked.defaultof<Gpu.Pipeline>
      StaticPipeline = Unchecked.defaultof<Gpu.Pipeline>
      Zone = zone
      Editor = EditorState.create ()
      Editing = true }

// ── Duplicate ──

[<Fact>]
let ``duplicating a part spawns a copy, selects it, and pushes the reversing command`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let zone = Zone.create ()
    let environment = Zone.loadEnvironment zone shared.Physics assets "room"
    let source = partRef environment.Epoch 0u 0u
    let mutable state = gameplayState zone

    Spawning.duplicate shared &state source (entity "wall-copy")

    Assert.Equal(2u, environment.Parts.Count)
    // The source is untouched and the copy is a distinct, live part ref.
    Assert.True(Zone.isAlive state.Zone source)
    match state.Editor.Selection with
    | Some copy ->
        Assert.NotEqual(source, copy)
        Assert.Equal(EntityKind.Part, copy.Kind)
        Assert.Equal(environment.Epoch, copy.Epoch)
        Assert.True(Zone.isAlive state.Zone copy)
        Assert.Equal("wall-copy", (Zone.tryEntityAt state.Zone copy).Value.Name)
    | None -> Assert.Fail("duplicate did not select the copy")

    // The paired command must name the copy, so undoing it removes exactly that.
    match state.Editor.Undo.Applied with
    | Duplicate (r, _, _) :: _ -> Assert.Equal(state.Editor.Selection.Value, r)
    | other -> Assert.Fail(sprintf "expected a Duplicate command, got %A" other)

[<Fact>]
let ``duplicating a kind with no entity fails loudly`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let zone = Zone.create ()
    let mutable state = gameplayState zone

    for kind in [ EntityKind.Environment; EntityKind.Waypoint; EntityKind.Trigger ] do
        let source : EntityRef = { Kind = kind; Index = 0u; Generation = 0u; Epoch = 0u }
        let mutable message = ""
        try
            Spawning.duplicate shared &state source (entity "ghost")
        with ex ->
            message <- ex.Message
        Assert.Contains("has no entity to duplicate", message)

// ── Restore onto an occupied slot ──

[<Fact>]
let ``restoring onto an occupied slot reports the reused slot instead of clobbering it`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let zone = Zone.create ()
    let environment = Zone.loadEnvironment zone shared.Physics assets "room"
    // The part's slot is live, so restoring a recorded ref that names it is a bug.
    let ref = partRef environment.Epoch 0u 0u
    let binding = Zone.bindingAt zone ref

    let mutable message = ""
    try
        Zone.setActorWithAsset zone shared.Physics assets ref (entity "restored") binding
    with ex ->
        message <- ex.Message

    Assert.Contains("slot 0 of ref", message)
    Assert.Contains("is occupied", message)
    Assert.Contains("reused by an untracked Add", message)
    Assert.Equal("wall", (Zone.tryEntityAt zone ref).Value.Name)
