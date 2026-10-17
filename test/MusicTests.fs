module MusicTests

// The music mix logic (spec 087), asserted headlessly: the pure model (gain, add/remove, the setters and
// the start rule) and the `SetMix` step on the one history, against an empty deck rack so no device is
// opened. The SDL deck calls themselves — playing, scrubbing, looping — are heard in quickstart.md.

open System
open System.Collections.Generic
open Prime
open Xunit
open Command
open Music

// ── fixtures ──

let private newState () : GameplayState.GameplayState =
    TestSupport.headlessState ()

/// A headless `SharedState`: a device-free, empty asset manager (whose audio is `unavailable`), an empty
/// mix and a fresh rack — so a `SetMix` step reconciles nothing and no device is touched.
let private sharedState () : SharedState.SharedState =
    TestSupport.headlessShared ()

/// The panel's write path: one `SetMix` step on the one history.
let private setMix (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (next: Music.Mix) : unit =
    let before = shared.Mix
    do Undo.commit shared &state (SetMix (before, next))

let private raises (f: unit -> unit) : bool =
    try
        f ()
        false
    with _ ->
        true

// ══════ the model (US1) ══════

[<Fact>]
let ``a fresh mix is empty with a full master`` () =
    Assert.Equal(0, Music.empty.Rows.Length)
    Assert.Equal(1.0f, Music.empty.Master)

[<Fact>]
let ``addRow adds one row, and re-adding an existing song is a no-op`` () =
    let mix = Music.empty |> Music.addRow "dark1" |> Music.addRow "dark1"
    Assert.Equal(1, mix.Rows.Length)      // one row per song (FR-002)
    Assert.True(Music.contains "dark1" mix)

[<Fact>]
let ``a new row defaults to full weight and no loop`` () =
    let row = (Music.empty |> Music.addRow "dark1" |> Music.rowOf "dark1").Value
    Assert.Equal("dark1", row.Asset)
    Assert.Equal(1.0f, row.Weight)
    Assert.False(row.Loop)

[<Fact>]
let ``removeRow drops the row, and removing an absent song is a no-op`` () =
    let mix = Music.empty |> Music.addRow "a" |> Music.addRow "b"
    let dropped = Music.removeRow "a" mix
    Assert.Equal<string list>([ "b" ], dropped.Rows |> List.map (fun row -> row.Asset))
    Assert.Equal(2, (Music.removeRow "z" mix).Rows.Length)
    Assert.False(Music.contains "a" dropped)

[<Fact>]
let ``the start rule resumes a held row and restarts any other`` () =
    Assert.Equal(Music.Resume, Music.startOf true)     // FR-003: play resumes a paused row...
    Assert.Equal(Music.Restart, Music.startOf false)   // ...and restarts any other from 0

[<Fact>]
let ``a row with no deck reads as stopped, at zero`` () =
    let rack = Music.newRack ()
    Assert.False(Music.isPlaying rack "a")
    Assert.Equal(0.0f, Music.position rack "a")
    Assert.Equal(0.0f, Music.duration rack "a")

// ══════ gain and balance (US2) ══════

[<Fact>]
let ``gain is the master-weight product`` () =
    Assert.Equal(0.5f, Music.gainOf 1.0f 0.5f)     // FR-006/FR-007
    Assert.Equal(0.5f, Music.gainOf 0.5f 1.0f)
    Assert.Equal(0.25f, Music.gainOf 0.5f 0.5f)
    Assert.Equal(0.0f, Music.gainOf 0.0f 1.0f)     // master 0 silences every row (SC-002)

[<Fact>]
let ``setWeight changes only its row`` () =
    let mix = Music.empty |> Music.addRow "a" |> Music.addRow "b" |> Music.setWeight "a" 0.25f
    Assert.Equal(0.25f, (Music.rowOf "a" mix).Value.Weight)
    Assert.Equal(1.0f, (Music.rowOf "b" mix).Value.Weight)   // untouched

[<Fact>]
let ``setMaster leaves every row untouched`` () =
    let mix = Music.empty |> Music.addRow "a" |> Music.setMaster 0.3f
    Assert.Equal(0.3f, mix.Master)
    Assert.Equal(1.0f, (Music.rowOf "a" mix).Value.Weight)

// ══════ the authored mix is one step (US2) ══════

[<Fact>]
let ``adding a song is one step, and reconciles a rack with no decks`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do setMix shared &state (Music.addRow "a" shared.Mix)
    Assert.True(Music.contains "a" shared.Mix)
    Assert.Equal(1, List.length state.Editor.Undo.Applied)

[<Fact>]
let ``removing a song is one step`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    shared.Mix <- Music.empty |> Music.addRow "a"
    do setMix shared &state (Music.removeRow "a" shared.Mix)
    Assert.False(Music.contains "a" shared.Mix)
    Assert.Equal(1, List.length state.Editor.Undo.Applied)

[<Fact>]
let ``a weight edit is one step, and undo restores the prior mix`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let before = Music.empty |> Music.addRow "a"
    shared.Mix <- before
    do setMix shared &state (Music.setWeight "a" 0.2f before)
    Assert.Equal(0.2f, (Music.rowOf "a" shared.Mix).Value.Weight)
    Assert.Equal(1, List.length state.Editor.Undo.Applied)

    do Undo.undoHistory shared &state
    Assert.Equal(1.0f, (Music.rowOf "a" shared.Mix).Value.Weight)
    Assert.Equal(0, List.length state.Editor.Undo.Applied)

[<Fact>]
let ``a transport press records no step`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    shared.Mix <- Music.empty |> Music.addRow "a"
    // the audio is unavailable (an empty device-free manager), so each op is a quiet no-op — and never a step
    do Music.play shared.Assets.Audio shared.Mix shared.Rack "a" "nowhere.wav"
    do Music.pause shared.Rack "a"
    do Music.stop shared.Rack "a"
    Assert.Empty state.Editor.Undo.Applied

[<Fact>]
let ``the zone-level runner refuses a mix step, which is state-level`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    let step = SetMix (Music.empty, Music.addRow "a" Music.empty)
    Assert.True(raises (fun () -> Command.apply zone state.Physics shared.Assets step))
    Assert.True(raises (fun () -> Command.undo zone state.Physics shared.Assets step))

// ══════ loop (US3) ══════

[<Fact>]
let ``setLoop toggles only its row's flag`` () =
    let mix = Music.empty |> Music.addRow "a" |> Music.addRow "b" |> Music.setLoop "a" true
    Assert.True((Music.rowOf "a" mix).Value.Loop)
    Assert.False((Music.rowOf "b" mix).Value.Loop)   // untouched

[<Fact>]
let ``a loop edit is one step`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let before = Music.empty |> Music.addRow "a"
    shared.Mix <- before
    do setMix shared &state (Music.setLoop "a" true before)
    Assert.True((Music.rowOf "a" shared.Mix).Value.Loop)
    Assert.Equal(1, List.length state.Editor.Undo.Applied)
