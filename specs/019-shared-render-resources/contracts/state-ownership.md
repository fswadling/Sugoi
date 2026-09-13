# Contract: State Ownership

Public surface changed by this feature. Only `state/`, `screen/`, and `Engine.fs`
change; `world/` and `render/` keep their contracts.

## Shared state — `state/SharedState.fs`

```fsharp
// Owns the device-derived render resources, created once after the device and
// released before it. The Gpu field is mutable so the depth target can be
// recreated on resize through this single owner.
type SharedState =
    { mutable Gpu : Gpu.Gpu
      Assets : Assets.AssetManager
      Physics : Physics.World }

// The GPU context carries the device, so no separate device argument.
val create : gpu:Gpu.Gpu -> SharedState
```

- `Gpu.Gpu` bundles: `Device`, `SwapchainFormat`, `DepthFormat`, `MeshPipeline`,
  `StaticPipeline`, `DepthTexture`, `DepthW`, `DepthH`.
- Exactly one `SharedState` exists; it is the only owner of the pipelines and the
  depth target.

## Screen state — `state/GameplayState.fs`

```fsharp
[<Struct>]
type GameplayState =
    { mutable Camera : Camera.Camera
      mutable DebugDraw : DebugDraw.DebugDraw
      mutable GizmoDraw : DebugDraw.DebugDraw
      Batch : EnvironmentBatch.EnvironmentBatch
      Zone : Zone.Zone
      mutable Editor : EditorState.EditorState
      mutable Editing : bool
      mutable Player : SlotMap.Handle option
      mutable Follow : Systems.CameraFollow }

// Takes shared state for the device the per-screen buffers need; receives no
// pipelines.
val create :
    shared:SharedState.SharedState ->
    debugDraw:DebugDraw.DebugDraw ->
    gizmoDraw:DebugDraw.DebugDraw -> GameplayState
```

- **Removed from the type**: `MeshPipeline`, `StaticPipeline`.
- Per-screen render buffers (`DebugDraw`, `GizmoDraw`, `Batch`) and the camera
  remain.

> **Superseded (`020-zone-owned-batch`):** the static-scene `Batch` moves to `Zone`,
> and `GameplayState.create` no longer takes shared state (it becomes
> `create debugDraw gizmoDraw`). The pipeline removal described here still holds.

## Screen manager — `screen/ScreenManager.fs`

```fsharp
// Builds the shared state around the GPU context and constructs the gameplay
// screen from it.
val init :
    gpu:Gpu.Gpu ->
    debugDraw:DebugDraw.DebugDraw ->
    gizmoDraw:DebugDraw.DebugDraw -> ScreenManager

// Passes shared state through to the gameplay screen's renderScene.
val renderScene : manager:inref<ScreenManager> -> pass:ScenePass -> unit
```

## Gameplay screen — `screen/GameplayScreen.fs`

```fsharp
// Reads device resources from shared state instead of holding copies.
val renderScene :
    shared:SharedState.SharedState ->
    state:inref<GameplayState.GameplayState> ->
    pass:ScenePass -> unit
```

- Uses `shared.Gpu.StaticPipeline` for `EnvironmentBatch.render` and
  `shared.Gpu.MeshPipeline` for the actor draws; the scene pass and depth target
  still come from `Engine` via `ScenePass`.

## Run loop — `Engine.fs`

```fsharp
// Creates the GPU context after the device, hands it to shared state (via
// ScreenManager.init), and reaches it through shared state thereafter.
let mutable gpu = Gpu.create device swapchainFormat     // bootstrap only — consumed at init
let debugDraw = DebugDraw.create device swapchainFormat gpu.DepthFormat false
let gizmoDraw = DebugDraw.create device swapchainFormat gpu.DepthFormat true
let mutable manager = ScreenManager.init gpu debugDraw gizmoDraw

// depth recreate / info / release go through the shared owner:
//   Gpu.ensureDepthTexture &shared.Gpu w h
//   Gpu.depthTargetInfo shared.Gpu
//   Gpu.destroy shared.Gpu
// where `shared = manager.Shared`
```

- The local `gpu` is a **bootstrap value**: it is read for `DepthFormat` before
  `ScreenManager.init` and handed over at init. It MUST NOT be used afterward —
  all later access (depth recreate, depth info, release) goes through
  `manager.Shared.Gpu`, or a resized depth target would be written to a stale copy.

## Rules

- No device-level render resource (pipeline, format, or depth target) may appear
  as a field of `GameplayState`.
- Each pipeline MUST be owned by exactly one value (`SharedState.Gpu`); no copies.
- Per-screen buffers MUST stay on `GameplayState` and get the device from shared
  state rather than a threaded argument.
- Creation/release order is unchanged: the GPU context is created after the device
  and released before it.
- `world/`, `render/`, physics, and commands are unchanged.
