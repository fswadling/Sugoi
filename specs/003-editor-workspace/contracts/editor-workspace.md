# Contract: Editor Workspace

The public surfaces exposed by the editor workspace modules. Signatures are
illustrative F#, not the implementation.

## Camera (`render/Camera.fs`)

```fsharp
type Camera =
    { Position : Vector3; Target : Vector3; Up : Vector3
      Fov : float32; Aspect : float32; Near : float32; Far : float32 }

view : Camera -> Matrix4x4            // look-at
proj : Camera -> Matrix4x4            // perspective
viewProjection : Camera -> Matrix4x4  // view * proj

orbit : Camera -> yaw:float32 -> pitch:float32 -> Camera  // rotate around target
dolly : Camera -> factor:float32 -> Camera                // scale distance (clamped)
pan   : Camera -> delta:Vector3 -> Camera                 // move position + target
```

## DebugDraw (`render/DebugDraw.fs`)

A line-only SDL_gpu path used to draw the grid (and later gizmos/overlays in
2c+). It owns both a CPU line accumulator and the GPU line pipeline + buffers.

```fsharp
[<Struct>]
type Color = { R : float32; G : float32; B : float32; A : float32 }

// Opaque render-resource handle: GPU line pipeline + buffers + a pre-allocated
// CPU vertex accumulator (reused each frame to avoid allocation).
type DebugDraw

// Build the shader/pipeline (LineList) + vertex/staging/uniform buffers.
create : device:nativeint -> swapchainFormat:uint32 -> DebugDraw

// Reset the accumulator for a new frame.
clear : DebugDraw -> DebugDraw

// Append a line segment (two vertices).
line : DebugDraw -> a:Vector3 -> b:Vector3 -> Color -> DebugDraw

// Append the reference grid (minor + major lines on the XZ plane, y = 0).
drawGrid : DebugDraw -> DebugDraw

// Upload the accumulated vertices to the GPU (copy pass, before the render pass).
upload : DebugDraw -> commandBuffer:nativeint -> unit

// Bind the line pipeline, push the view-projection uniform, and draw the
// accumulated vertices (inside the render pass; the uniform push needs the
// command buffer).
draw : DebugDraw -> viewProjection:Matrix4x4 -> commandBuffer:nativeint -> renderPass:nativeint -> unit
```

## Assets (`core/Assets.fs`)

```fsharp
type Asset = { Id : string; Name : string; Kind : EntityKind }  // Actor/Environment/Prop

// Scan a root dir; raises on a malformed manifest.
scan : string -> Asset array
```

## Editor (`editor/*`)

```fsharp
type EditorState = { Selection : EntityRef option }

// Draw the docking workspace + panels, reading/updating the gameplay state
// (which owns the camera + editor state); File > Quit is a ScreenTransition.
Editor.draw : GameplayState -> SharedState -> struct (GameplayState * ScreenTransition)
```

## Rules

- `GameplayState` owns the `Camera`/`EditorState` (both immutable); `Editor`
  reads them without owning them (D-020/D-021).
- The viewport's 3D scene renders to the swapchain; ImGui panels dock on top.
- A missing/malformed asset manifest raises (fail loudly).
