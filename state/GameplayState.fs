module GameplayState

// Per-screen gameplay state. A struct; owns the editor's data (camera + editor
// state + editing flag) and the active zone. Shared per-asset data (the asset
// manager) and the physics world live on `SharedState`, not here.

open System.Numerics

[<Struct>]
type GameplayState =
    { mutable Camera : Camera.Camera
      mutable DebugDraw : DebugDraw.DebugDraw
      mutable GizmoDraw : DebugDraw.DebugDraw
      MeshPipeline : Gpu.Pipeline
      StaticPipeline : Gpu.Pipeline
      Zone : Zone.Zone
      mutable Editor : EditorState.EditorState
      mutable Editing : bool }

let create (debugDraw: DebugDraw.DebugDraw) (gizmoDraw: DebugDraw.DebugDraw) (meshPipeline: Gpu.Pipeline) (staticPipeline: Gpu.Pipeline) : GameplayState =
    { Camera = Camera.create (Vector3(0.0f, 3.0f, 6.0f)) Vector3.Zero
      DebugDraw = debugDraw
      GizmoDraw = gizmoDraw
      MeshPipeline = meshPipeline
      StaticPipeline = staticPipeline
      Zone = Zone.create ()
      Editor = EditorState.create ()
      Editing = true }
