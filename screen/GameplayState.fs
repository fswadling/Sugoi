module GameplayState

// Per-screen gameplay state. A struct; owns the editor's data (camera + editor
// state + editing flag) and grows the world in later phases.

open System.Numerics

[<Struct>]
type GameplayState =
    { Camera : Camera.Camera
      Editor : EditorState.EditorState
      Editing : bool }

let create () : GameplayState =
    { Camera = Camera.create (Vector3(5.0f, 5.0f, 5.0f)) Vector3.Zero
      Editor = EditorState.create ()
      Editing = true }
