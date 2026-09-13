module GameplayState

// Per-screen gameplay state. A struct; owns the screen's entities (the zone), its
// view (camera + per-screen render buffers), and its editor/play state. Device-level
// render resources (pipelines, formats, depth target) live on `SharedState`, not
// here — a screen's state describes the screen, not the hardware.

open System.Numerics

[<Struct>]
type GameplayState =
    { mutable Camera : Camera.Camera
      mutable DebugDraw : DebugDraw.DebugDraw
      mutable GizmoDraw : DebugDraw.DebugDraw
      Zone : Zone.Zone
      mutable Editor : EditorState.EditorState
      mutable Editing : bool
      // A value option so the handle stays inline in this struct instead of pointing
      // at a heap `Some`; the player systems read it every frame while playing.
      mutable Player : SlotMap.Handle voption
      mutable Follow : Systems.CameraFollow }

let create (debugDraw: DebugDraw.DebugDraw) (gizmoDraw: DebugDraw.DebugDraw) : GameplayState =
    { Camera = Camera.create (Vector3(0.0f, 3.0f, 6.0f)) Vector3.Zero
      DebugDraw = debugDraw
      GizmoDraw = gizmoDraw
      Zone = Zone.create ()
      Editor = EditorState.create ()
      Editing = true
      Player = ValueNone
      Follow = Systems.createFollow () }
