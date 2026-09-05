module SharedState

// Cross-screen state owned by ScreenManager: the asset manager (which also owns
// the GPU device). A reference type so screens can write shared values back
// (e.g. loaded assets) instead of threading struct copies.

type SharedState =
    { Assets : Assets.AssetManager }

let create (device: Gpu.Device) : SharedState =
    { Assets = Assets.scan "assets" device }
