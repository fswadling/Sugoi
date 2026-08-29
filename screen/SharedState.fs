module SharedState

// Cross-screen state owned by ScreenManager: the asset list (and later cues,
// cast, zone registry). A reference type so screens can write shared values
// back (e.g. the selected scenario) instead of threading struct copies.

type SharedState =
    { Assets : Assets.Asset array }

let create () : SharedState =
    { Assets = Assets.scan "assets" }
