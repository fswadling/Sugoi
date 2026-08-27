module SharedState

// Cross-screen state owned by `ScreenManager`. Empty this milestone; populated
// by later phases (assets, cues, cast, zone registry) per docs/ARCHITECTURE.md.
// A class rather than a record because it is long-lived, shared, mutable state.

type SharedState() = class end
