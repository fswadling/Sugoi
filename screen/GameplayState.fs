module GameplayState

// Per-screen gameplay state. A struct; owns the editor's data (camera + editor
// state + editing flag) and grows the world in later phases.

open System.Numerics

[<Struct>]
type GameplayState =
    { Camera : Camera.Camera
      DebugDraw : DebugDraw.DebugDraw
      Editor : EditorState.EditorState
      Editing : bool
      DockingInitialized : bool
      PrevMouseX : float32
      PrevMouseY : float32
      PrevBacktick : bool }

let create (debugDraw: DebugDraw.DebugDraw) : GameplayState =
    { Camera = Camera.create (Vector3(0.0f, 3.0f, 6.0f)) Vector3.Zero
      DebugDraw = debugDraw
      Editor = EditorState.create ()
      Editing = true
      DockingInitialized = false
      PrevMouseX = 0.0f
      PrevMouseY = 0.0f
      PrevBacktick = false }
