module MusicCueTests

// The music cue ops (spec 088), asserted headlessly: each op's effect on the authored `Mix` and the runtime
// `Rack`, the `…State` transitions against a `now` clock, the no-op rules, and the property that a cue's
// music op records **no** step on the one history. The SDL decks themselves are heard in quickstart.md.

open System
open System.Collections.Generic
open Prime
open Xunit
open Command
open Cue
open CueEngine
open Music

// ── Headless fixtures ──

/// A headless gameplay state, the way `test/CueTests.fs` builds one.
let private newState () : GameplayState.GameplayState =
    GameplayState.create (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<Ui.Ui>)

/// A headless `SharedState`: a device-free, empty asset manager (whose audio is `unavailable` and whose
/// `AudioAssets` is empty, so every song resolves to *unavailable*), an empty mix and a fresh rack.
let private sharedState () : SharedState.SharedState =
    { Gpu = Unchecked.defaultof<Gpu.Gpu>
      Assets = TestSupport.manager IntPtr.Zero (Dictionary()) (Dictionary()) (Dictionary())
      Advents = Advent.empty
      Mix = Music.empty
      Rack = Music.newRack () }

/// A shared state with the named songs already palette rows.
let private withPalette (songs: string list) : SharedState.SharedState =
    let shared = sharedState ()
    shared.Mix <- List.fold (fun mix song -> Music.addRow song mix) shared.Mix songs
    shared

/// Authoring sugar: a list of ops as the cue they denote (the `CueTests` helper).
let private seqOf (cues: Cue list) : Cue =
    match cues with
    | [] -> Fin
    | [ only ] -> only
    | head :: tail -> Sequence(head, FDeque.ofList tail)

let private raises (f: unit -> unit) : bool =
    try
        f ()
        false
    with _ ->
        true

// ══════ US1 — the palette and transport (FR-001..FR-005) ══════

[<Fact>]
let ``an add op brings a song into the palette, and re-adding does not make a second row`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    state.Cue <- seqOf [ AddMusic "casual1"; AddMusic "casual1" ]
    tick shared &state 0.0
    Assert.True(Music.contains "casual1" shared.Mix)
    Assert.Equal(1, List.length shared.Mix.Rows) // one row per song (FR-001)
    Assert.Equal(Fin, state.Cue)                 // a no-progress leaf: consumed in the tick it is reached

[<Fact>]
let ``a remove op drops the row, and removing an absent song is a no-op`` () =
    let shared = withPalette [ "casual1"; "dark1" ]
    let mutable state = newState ()
    state.Cue <- RemoveMusic "casual1"
    tick shared &state 0.0
    Assert.False(Music.contains "casual1" shared.Mix)
    Assert.True(Music.contains "dark1" shared.Mix) // the other row is untouched
    state.Cue <- RemoveMusic "nope"                // an absent song: no change, no raise
    tick shared &state 0.0
    Assert.Equal(1, List.length shared.Mix.Rows)

[<Fact>]
let ``play, stop and the bed transport reach Fin on a device-free state without raising`` () =
    let shared = withPalette [ "casual1"; "dark1" ]
    let mutable state = newState ()
    // the audio is unavailable and the assets are empty, so every transport op is a quiet no-op — and the
    // cue still finishes (FR-003..FR-005, FR-010)
    state.Cue <- seqOf [ PlayMusic "casual1"; StopMusic "casual1"; PlayBed; StopBed ]
    tick shared &state 0.0
    Assert.Equal(Fin, state.Cue)

[<Fact>]
let ``a transport op on a song that is not a palette row is a no-op`` () =
    let shared = withPalette []
    let mutable state = newState ()
    state.Cue <- seqOf [ PlayMusic "casual1"; StopMusic "casual1" ]
    tick shared &state 0.0
    Assert.Empty shared.Mix.Rows
    Assert.Equal(Fin, state.Cue)

[<Fact>]
let ``a music op is a runtime effect, not a step on the undo history`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    state.Cue <- seqOf [ AddMusic "casual1"; PlayMusic "casual1"; StopMusic "casual1"; RemoveMusic "casual1" ]
    tick shared &state 0.0
    Assert.Empty state.Editor.Undo.Applied // FR-009: 0 steps, while an editor `SetMix` is one

// ══════ the editor's authoring surface for US1 (FR-013) ══════

[<Fact>]
let ``the music ops render, and the song chooser's write replaces their song`` () =
    let labelOf (cue: Cue) : string = (List.head (CueTree.rows cue)).Label
    Assert.Equal("Add music: casual1", labelOf (AddMusic "casual1"))
    Assert.Equal("Remove music: casual1", labelOf (RemoveMusic "casual1"))
    Assert.Equal("Play music: casual1", labelOf (PlayMusic "casual1"))
    Assert.Equal("Stop music: casual1", labelOf (StopMusic "casual1"))
    Assert.Equal("Play bed", labelOf PlayBed)
    Assert.Equal("Stop bed", labelOf StopBed)
    // an unnamed op drops the colon (a song need not be chosen yet)
    Assert.Equal("Add music", labelOf (AddMusic ""))

    // the chooser's write replaces the song, keeping the op; it refuses any other node
    Assert.Equal(AddMusic "dark1", CueTree.withSong "dark1" (AddMusic "casual1"))
    Assert.Equal(PlayMusic "dark1", CueTree.withSong "dark1" (PlayMusic "casual1"))
    Assert.True(raises (fun () -> CueTree.withSong "x" (Wait 1.0) |> ignore))

[<Fact>]
let ``the music kinds default to a song-bearing op, and no music op names a place`` () =
    for kind in [ "Add music"; "Remove music"; "Play music"; "Stop music" ] do
        match CueTree.defaultOfKind "Island" kind with
        | AddMusic s | RemoveMusic s | PlayMusic s | StopMusic s -> Assert.Equal("", s)
        | other -> Assert.Fail(sprintf "kind '%s' should default a song-bearing op, got %A" kind other)
    Assert.Equal(PlayBed, CueTree.defaultOfKind "Island" "Play bed")
    Assert.Equal(StopBed, CueTree.defaultOfKind "Island" "Stop bed")
    // music is global: no music op names a place (spec 088)
    for kind in [ "Add music"; "Play music"; "Play bed"; "Stop bed" ] do
        Assert.True((CueTree.placeOf (CueTree.defaultOfKind "Island" kind)).IsNone)

// ══════ US2 — a level and the master, over time or at once (FR-006/FR-007) ══════

[<Fact>]
let ``a level op with a duration ramps the row to its target and lands exactly at the deadline`` () =
    let shared = withPalette [ "casual1" ]
    let mutable state = newState ()
    state.Cue <- SetMusicLevel("casual1", 0.0f, 2.0)
    tick shared &state 0.0
    Assert.Equal(1.0f, (Music.rowOf "casual1" shared.Mix).Value.Weight) // captured, not yet moved
    tick shared &state 1.0
    Assert.Equal(0.5f, (Music.rowOf "casual1" shared.Mix).Value.Weight) // half way
    tick shared &state 2.0
    Assert.Equal(0.0f, (Music.rowOf "casual1" shared.Mix).Value.Weight) // exactly the target (SC-002)
    Assert.Equal(Fin, state.Cue)

[<Fact>]
let ``a level op with a zero duration lands at once`` () =
    let shared = withPalette [ "casual1" ]
    let mutable state = newState ()
    state.Cue <- SetMusicLevel("casual1", 0.25f, 0.0)
    tick shared &state 0.0
    Assert.Equal(0.25f, (Music.rowOf "casual1" shared.Mix).Value.Weight)
    Assert.Equal(Fin, state.Cue)

[<Fact>]
let ``a master op ramps the palette master`` () =
    let shared = withPalette [ "casual1" ]
    let mutable state = newState ()
    state.Cue <- SetMusicMaster(0.0f, 2.0)
    tick shared &state 0.0
    Assert.Equal(1.0f, shared.Mix.Master)
    tick shared &state 1.0
    Assert.Equal(0.5f, shared.Mix.Master)
    tick shared &state 2.0
    Assert.Equal(0.0f, shared.Mix.Master)
    Assert.Equal(Fin, state.Cue)

[<Fact>]
let ``a retarget reached mid-ramp captures the current level, so it continues without a jump`` () =
    let shared = withPalette [ "casual1" ]
    let mutable state = newState ()
    state.Cue <- SetMusicLevel("casual1", 0.0f, 2.0)
    tick shared &state 0.0
    tick shared &state 1.0                                                    // half way: 0.5
    state.Cue <- SetMusicLevel("casual1", 1.0f, 4.0)                          // retarget while 0.5 is in flight
    tick shared &state 1.0
    Assert.Equal(MusicLevelState("casual1", 0.5f, 1.0f, 4.0, 5.0), state.Cue) // captured 0.5, not 1.0 (FR-012)
    tick shared &state 3.0
    Assert.Equal(0.75f, (Music.rowOf "casual1" shared.Mix).Value.Weight)      // 0.5 -> 1.0, half way

[<Fact>]
let ``a level op on a song that is not a palette row is a no-op`` () =
    let shared = withPalette []
    let mutable state = newState ()
    state.Cue <- SetMusicLevel("casual1", 0.5f, 1.0)
    tick shared &state 0.0
    Assert.Empty shared.Mix.Rows
    Assert.Equal(Fin, state.Cue)

[<Fact>]
let ``a level op on a row that is not playing still writes the mix`` () =
    let shared = withPalette [ "casual1" ] // no deck: nothing has played
    let mutable state = newState ()
    state.Cue <- SetMusicLevel("casual1", 0.25f, 0.0)
    tick shared &state 0.0
    Assert.Equal(0.25f, (Music.rowOf "casual1" shared.Mix).Value.Weight) // FR-011: applied when it next plays

[<Fact>]
let ``a level or master op records no step on the history`` () =
    let shared = withPalette [ "casual1" ]
    let mutable state = newState ()
    state.Cue <- seqOf [ SetMusicLevel("casual1", 0.0f, 0.0); SetMusicMaster(0.5f, 0.0) ]
    tick shared &state 0.0
    Assert.Empty state.Editor.Undo.Applied

[<Fact>]
let ``a music level and master render, and an in-flight ramp reads as its op`` () =
    let labelOf (cue: Cue) : string = (List.head (CueTree.rows cue)).Label
    Assert.Equal("Music level: casual1", labelOf (SetMusicLevel("casual1", 0.0f, 2.0)))
    Assert.Equal("Music level: casual1", labelOf (MusicLevelState("casual1", 1.0f, 0.0f, 2.0, 2.0)))
    Assert.Equal("Music master", labelOf (SetMusicMaster(0.0f, 2.0)))
    Assert.Equal("Music master", labelOf (MusicMasterState(1.0f, 0.0f, 2.0, 2.0)))
    // the chooser's write reaches a level op
    Assert.Equal(SetMusicLevel("dark1", 0.0f, 2.0), CueTree.withSong "dark1" (SetMusicLevel("casual1", 0.0f, 2.0)))
    // a ramp in flight is executing; the authored ops are not
    Assert.True(CueTree.executing (MusicLevelState("casual1", 1.0f, 0.0f, 2.0, 2.0)))
    Assert.True(CueTree.executing (MusicMasterState(1.0f, 0.0f, 2.0, 2.0)))
    Assert.False(CueTree.executing (SetMusicLevel("casual1", 0.0f, 2.0)))
    // the two kinds default to a full level over one second
    Assert.Equal(SetMusicLevel("", 1.0f, 1.0), CueTree.defaultOfKind "Island" "Music level")
    Assert.Equal(SetMusicMaster(1.0f, 1.0), CueTree.defaultOfKind "Island" "Music master")
    // no level op names a place
    Assert.True((CueTree.placeOf (SetMusicLevel("casual1", 0.0f, 2.0))).IsNone)
    Assert.True((CueTree.placeOf (SetMusicMaster(0.0f, 2.0))).IsNone)

// ══════ US3 — the cross-fade (FR-008) ══════

[<Fact>]
let ``a cross-fade lowers the outgoing and raises the incoming together over its duration`` () =
    let shared = withPalette [ "casual1"; "dark1" ]
    shared.Mix <- Music.setWeight "dark1" 0.0f shared.Mix
    let mutable state = newState ()
    state.Cue <- CrossFadeMusic("casual1", "dark1", 2.0)
    tick shared &state 0.0
    Assert.Equal(1.0f, (Music.rowOf "casual1" shared.Mix).Value.Weight)
    Assert.Equal(0.0f, (Music.rowOf "dark1" shared.Mix).Value.Weight)
    tick shared &state 1.0
    Assert.Equal(0.5f, (Music.rowOf "casual1" shared.Mix).Value.Weight)
    Assert.Equal(0.5f, (Music.rowOf "dark1" shared.Mix).Value.Weight)
    tick shared &state 2.0
    Assert.Equal(0.0f, (Music.rowOf "casual1" shared.Mix).Value.Weight) // the outgoing falls to silence
    Assert.Equal(1.0f, (Music.rowOf "dark1" shared.Mix).Value.Weight)   // the incoming rises to full
    Assert.Equal(Fin, state.Cue)

[<Fact>]
let ``a cross-fade with a zero duration swaps at once`` () =
    let shared = withPalette [ "casual1"; "dark1" ]
    shared.Mix <- Music.setWeight "dark1" 0.0f shared.Mix
    let mutable state = newState ()
    state.Cue <- CrossFadeMusic("casual1", "dark1", 0.0)
    tick shared &state 0.0
    Assert.Equal(0.0f, (Music.rowOf "casual1" shared.Mix).Value.Weight)
    Assert.Equal(1.0f, (Music.rowOf "dark1" shared.Mix).Value.Weight)
    Assert.Equal(Fin, state.Cue)

[<Fact>]
let ``a cross-fade with a missing side reports it and the other side still moves`` () =
    let shared = withPalette [ "dark1" ]
    shared.Mix <- Music.setWeight "dark1" 0.0f shared.Mix
    let mutable state = newState ()
    state.Cue <- CrossFadeMusic("ghost", "dark1", 2.0)
    tick shared &state 0.0
    Assert.Equal(CrossFadeMusicState("ghost", "dark1", 0.0f, 0.0f, 2.0, 2.0), state.Cue) // reported, still held
    tick shared &state 1.0
    Assert.Equal(0.5f, (Music.rowOf "dark1" shared.Mix).Value.Weight) // the present side still rises
    Assert.False(Music.contains "ghost" shared.Mix)                    // the absent side is untouched

[<Fact>]
let ``a cross-fade with both sides absent is a no-op`` () =
    let shared = withPalette []
    let mutable state = newState ()
    state.Cue <- CrossFadeMusic("a", "b", 2.0)
    tick shared &state 0.0
    Assert.Empty shared.Mix.Rows
    Assert.Equal(Fin, state.Cue)

[<Fact>]
let ``a cross-fade records no step on the history`` () =
    let shared = withPalette [ "casual1"; "dark1" ]
    let mutable state = newState ()
    state.Cue <- CrossFadeMusic("casual1", "dark1", 0.0)
    tick shared &state 0.0
    Assert.Empty state.Editor.Undo.Applied

[<Fact>]
let ``the cross-fade renders, and its two choosers write the two songs`` () =
    let labelOf (cue: Cue) : string = (List.head (CueTree.rows cue)).Label
    Assert.Equal("Cross fade music: casual1 -> dark1", labelOf (CrossFadeMusic("casual1", "dark1", 2.0)))
    Assert.Equal("Cross fade music: casual1 -> dark1", labelOf (CrossFadeMusicState("casual1", "dark1", 1.0f, 0.0f, 2.0, 2.0)))
    // the two song choosers are separate writes; each keeps the rest
    Assert.Equal(CrossFadeMusic("dark2", "dark1", 2.0), CueTree.withSong "dark2" (CrossFadeMusic("casual1", "dark1", 2.0)))
    Assert.Equal(CrossFadeMusic("casual1", "dark2", 2.0), CueTree.withSong2 "dark2" (CrossFadeMusic("casual1", "dark1", 2.0)))
    Assert.True(raises (fun () -> CueTree.withSong2 "x" (Wait 1.0) |> ignore))
    // a cross-fade in flight is executing; the authored op is not
    Assert.True(CueTree.executing (CrossFadeMusicState("casual1", "dark1", 1.0f, 0.0f, 2.0, 2.0)))
    Assert.False(CueTree.executing (CrossFadeMusic("casual1", "dark1", 2.0)))
    Assert.Equal(CrossFadeMusic("", "", 1.0), CueTree.defaultOfKind "Island" "Cross fade music")
    Assert.True((CueTree.placeOf (CrossFadeMusic("casual1", "dark1", 2.0))).IsNone)
