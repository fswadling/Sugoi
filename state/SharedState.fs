module SharedState

// Cross-screen state owned by ScreenManager: the asset manager (which also owns
// the GPU device) and the physics world. One physics world is shared by every
// zone (zones keep only their body→entity map), so there is a single Jolt
// PhysicsSystem + job system for the whole game. A reference type so screens
// can write shared values back (e.g. loaded assets) instead of threading
// struct copies.

type SharedState =
    { Assets : Assets.AssetManager
      Physics : Physics.World }

let create (device: Gpu.Device) : SharedState =
    { Assets = Assets.scan "assets" device
      Physics = Physics.create () }
