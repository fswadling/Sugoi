module SharedState

// Shared state owned by ScreenManager: what the engine itself, or more than one screen, may read.
// The test is readership, not authorship — a member lives here only when something other than the
// gameplay screen and its editor reads it.
//
//   - the GPU context — the device and its derived render resources (pipelines, formats, depth
//     target) — which the engine's own render loop reads;
//   - the asset manager, which any presenting screen may read (a journal resolves portraits through
//     it);
//   - the named adventure flags, which a journal or inventory screen may present or gate on.
//
// The gameplay world and its configuration — the zones, the zone registry and the physics world —
// are the gameplay screen's and live on `GameplayState`, beside the screen's own runtime state.
//
// The GPU context is the single owner of the device-derived render resources; screens read them
// from here rather than holding copies. A reference type so screens can write shared values back
// (e.g. loaded assets) instead of threading struct copies.

open Advent

type SharedState =
    { mutable Gpu : Gpu.Gpu
      Assets : Assets.AssetManager
      // The named adventure flags (spec 055; a string set since spec 082): story milestones a screen other
      // than the world may present or gate on, for the same reason. Mutable because the one undo history
      // writes it in place — a flag edit is a state-level step, as the cue's `SetCue` is (specs/048, 049).
      mutable Advents : Advent.Set
      // The music bed (spec 087): the authored mix (rows, weights, loop flags, master) and the runtime
      // decks. Game-wide, so music plays across a screen change; the decks are reconciled on every
      // `SetMix` (specs/087 History) and freed in `Music.release` before the mixer (specs/087 Engine).
      mutable Mix : Music.Mix
      mutable Rack : Music.Rack }

let create (gpu: Gpu.Gpu) : SharedState =
    // The mixer is brought up once and handed to the asset manager, which owns it (freed in
    // `Assets.release`). If audio is unavailable, scanning still runs and audio assets load failed.
    let audio = Audio.create ()
    { Gpu = gpu
      Assets = Assets.scan "assets" gpu.Device audio
      Advents = Advent.empty
      Mix = Music.empty
      Rack = Music.newRack () }
