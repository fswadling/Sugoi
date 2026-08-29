module SharedState

// Cross-screen state owned by ScreenManager: the asset list (and later cues,
// cast, zone registry). A struct so it can be stored inline.

[<Struct>]
type SharedState =
    { Assets : Assets.Asset array }

let create () : SharedState =
    { Assets = Assets.scan "assets" }
