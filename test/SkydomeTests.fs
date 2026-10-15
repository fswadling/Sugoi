module SkydomeTests

// A zone's authored sky (spec 092): the value's defaults and the editor step that changes it. The render
// (the dome behind the scene), the Inspector's controls and the staging round-trip need a GPU or a live
// editor; the value and the step are checkable with no window, as the rest of the suite is. The staging
// round-trip itself is asserted in `ScenarioForkTests`.

open System
open System.Collections.Generic
open System.Numerics
open Xunit
open Command
open Entity

// A device-free shared state: a sky edit touches no asset and no body.
let private sharedState () : SharedState.SharedState =
    { Gpu = Unchecked.defaultof<Gpu.Gpu>
      Assets = TestSupport.manager IntPtr.Zero (Dictionary()) (Dictionary()) (Dictionary())
      Advents = Advent.empty
      Mix = Music.empty
      Rack = Music.newRack ()
      Voices = Sound.newVoices Audio.unavailable }

let private gameplayState (zone: Zone.Zone) : GameplayState.GameplayState =
    let zones = Dictionary<string, Zone.Zone>()
    zones.[GameplayState.startZone] <- zone
    { Camera = Camera.create Vector3.Zero Vector3.Zero
      DebugDraw = Unchecked.defaultof<DebugDraw.DebugDraw>
      GizmoDraw = Unchecked.defaultof<DebugDraw.DebugDraw>
      Ui = Unchecked.defaultof<Ui.Ui>
      Skydome = Unchecked.defaultof<Skydome.Skydome>
      Dialogue = Dialogue.empty
      Zones = zones
      Scenarios = ScenarioRegistry.create ()
      Physics = Physics.create ()
      ActiveZone = GameplayState.startZone
      Editor = EditorState.create ()
      Editing = true
      Follow = Systems.createFollow ()
      InteractableTrigger = ValueNone
      Cue = Cue.Fin
      PlayTime = 0.0
      Fade = 0.0f }

// ── the value (FR-001, FR-005) ──

[<Fact>]
let ``the default sky is off`` () =
    // Off by default, so an unedited zone draws nothing behind the scene (the frame is unchanged).
    Assert.False Skydome.defaultSettings.Enabled

[<Fact>]
let ``the default sky carries the reference colours as a horizon-to-zenith pair`` () =
    // The colours are `odin-game`'s, converted to the port's linear space: the sky overhead is a deeper,
    // less-red blue than at the horizon.
    let sky = Skydome.defaultSettings
    Assert.True(sky.Zenith.X < sky.Horizon.X)
    Assert.True(sky.Zenith.Y < sky.Horizon.Y)
    Assert.True(sky.Zenith.Z > 0.0f)

[<Fact>]
let ``a fresh zone carries the default sky`` () =
    let zone = Zone.create ()
    Assert.Equal(Skydome.defaultSettings, zone.Skydome)

// ── the editor step (FR-010, SC-004) ──

[<Fact>]
let ``setSkydome records one step and undo and redo round-trip the sky`` () =
    let shared = sharedState ()
    let zone = Zone.create ()
    let mutable state = gameplayState zone
    let before = zone.Skydome

    do Selection.setSkydome shared &state (fun sky -> { sky with Enabled = true; Horizon = Vector3(0.2f, 0.4f, 0.6f) })

    Assert.True(zone.Skydome.Enabled)
    Assert.Equal(Vector3(0.2f, 0.4f, 0.6f), zone.Skydome.Horizon)
    Assert.Equal(1, List.length state.Editor.Undo.Applied)

    do History.undoHistory shared &state
    Assert.Equal(before, zone.Skydome)

    do History.redoHistory shared &state
    Assert.True(zone.Skydome.Enabled)

[<Fact>]
let ``a no-op setSkydome records nothing`` () =
    let shared = sharedState ()
    let zone = Zone.create ()
    let mutable state = gameplayState zone

    do Selection.setSkydome shared &state id

    Assert.True(List.isEmpty state.Editor.Undo.Applied)
