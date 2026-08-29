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

```fsharp
// Begin a line batch for the given camera; then emit lines; then end.
begin : Camera -> unit
drawGrid : unit -> unit
end : unit -> unit
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

// Draw the docking workspace + panels, reading the gameplay state (which owns
// the camera + editor state) and shared state; returns true on File > Quit.
Editor.draw : GameplayState -> SharedState -> bool
```

## Rules

- `GameplayState` owns the `Camera`/`EditorState` (both immutable); `Editor`
  reads them without owning them (D-020/D-021).
- The viewport's 3D scene renders to the swapchain; ImGui panels dock on top.
- A missing/malformed asset manifest raises (fail loudly).
