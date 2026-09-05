module Screen

// Screen kinds, navigation requests, and the per-frame values shared by all
// screens. New screens are added as `ScreenKind` cases here and handled in
// `ScreenManager`.

open SDL3

type ScreenKind =
    | MainMenu
    | Gameplay

// A navigation request produced by a screen and applied by the manager at the
// end of the frame (so the current frame still draws fully).
type ScreenTransition =
    | None
    | Next of ScreenKind
    | Quit

module Transition =

    // Merge the frame's update and draw requests: the later one (draw — e.g.
    // a clicked button) wins; `None` yields to the earlier request.
    let orElse (fallback: ScreenTransition) (t: ScreenTransition) : ScreenTransition =
        match t with
        | ScreenTransition.None -> fallback
        | t -> t

// Everything the engine surfaces to screens each frame, built once in Engine:
// the frame time, the current window size (read after ImGui's backend NewFrame,
// which sets io.DisplaySize), and the gathered input. New fields extend this
// record, never a signature.
[<Struct>]
type Frame =
    { Dt : float32
      WindowW : float32
      WindowH : float32
      Input : Input.Input }

// What a screen needs to record its 3D scene pass. Bundles the GPU details so
// they cross one explicit seam instead of riding loose through the manager.
[<Struct>]
type ScenePass =
    { Cmd : Gpu.CommandBuffer
      ColorTargets : SDL.GPUColorTargetInfo array
      DepthTarget : SDL.GPUDepthStencilTargetInfo }
