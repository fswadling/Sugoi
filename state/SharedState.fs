module SharedState

// Cross-screen state owned by ScreenManager: the GPU context (the device and its
// derived render resources — pipelines, formats, depth target), the asset manager,
// and the physics world. One physics world is shared by every zone (zones keep only
// their body→entity map), so there is a single Jolt PhysicsSystem + job system for
// the whole game. The GPU context is the single owner of the device-derived render
// resources; screens read them from here rather than holding copies. A reference
// type so screens can write shared values back (e.g. loaded assets) instead of
// threading struct copies.

type SharedState =
    { mutable Gpu : Gpu.Gpu
      Assets : Assets.AssetManager
      Physics : Physics.World }

let create (gpu: Gpu.Gpu) : SharedState =
    { Gpu = gpu
      Assets = Assets.scan "assets" gpu.Device
      Physics = Physics.create () }
