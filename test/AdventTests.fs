module AdventTests

// The adventure flags (spec 055; a string set since spec 082), asserted headlessly: the panel's write path,
// and the history round trip. The set itself is `Prime`'s ordered `OSet<string>`; these tests reach it through
// the panel and the undo history rather than exercising its operations directly. Nothing here opens a window,
// a GPU or a device — the set is plain data, so it is checkable with no device, and the panel's drawing is the
// quickstart's business.

open Xunit
open System
open System.Collections.Generic
open Prime

// ── fixtures ──

/// A headless gameplay state, the way `test/CuePanelTests.fs` builds one.
let private newState () : GameplayState.GameplayState =
    GameplayState.create (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<Ui.Ui>) (Unchecked.defaultof<Skydome.Skydome>)

/// A headless `SharedState`: a device-free, empty asset manager and an empty advent set. Nothing here spawns
/// an entity, so no resource is touched; the physics world is the gameplay state's own.
let private sharedState () : SharedState.SharedState =
    { Gpu = Unchecked.defaultof<Gpu.Gpu>
      Assets = TestSupport.manager IntPtr.Zero (Dictionary()) (Dictionary()) (Dictionary())
      Advents = OSet.ofSeq1 []
      Mix = Music.empty
      Rack = Music.newRack ()
      Voices = Sound.newVoices Audio.unavailable }

let private raises (f: unit -> unit) : bool =
    try
        f ()
        false
    with _ ->
        true

/// The recorded names, in the order they were added — what the panel lists.
let private names (set: OSet<string>) : string list = OSet.toSeq set |> List.ofSeq

// ══════ the write path (FR-003, FR-004, FR-008) ══════

[<Fact>]
let ``an accepted add records exactly one step`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    Assert.Equal<string list>([], names shared.Advents)

    do AdventPanel.commit shared &state (OSet.add "TheOpeningIsOver" shared.Advents)
    Assert.Equal<string list>([ "TheOpeningIsOver" ], names shared.Advents)
    Assert.Equal(1, List.length state.Editor.Undo.Applied)
    match state.Editor.Undo.Applied with
    | [ Command.Advent (previous, next) ] ->
        Assert.Equal<string list>([], names previous)
        Assert.Equal<string list>([ "TheOpeningIsOver" ], names next)
    | other -> Assert.Fail(sprintf "expected one Advent step, got %A" other)

[<Fact>]
let ``a remove records exactly one step`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    shared.Advents <- OSet.ofSeq1 [ "A"; "B" ]

    do AdventPanel.commit shared &state (OSet.remove "A" shared.Advents)
    Assert.Equal<string list>([ "B" ], names shared.Advents)
    Assert.Equal(1, List.length state.Editor.Undo.Applied)

// ══════ the history (FR-009) and the state-level rule (FR-008) ══════

[<Fact>]
let ``a flag survives a zone switch`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do AdventPanel.commit shared &state (OSet.add "TheOpeningIsOver" shared.Advents)

    do Undo.commit shared &state (Command.ZoneAdd (GameplayState.startZone, "Island"))
    Assert.Equal("Island", state.ActiveZone)
    Assert.Equal<string list>([ "TheOpeningIsOver" ], names shared.Advents)

[<Fact>]
let ``undo restores the prior set and redo re-applies the edit`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do AdventPanel.commit shared &state (OSet.add "TheOpeningIsOver" shared.Advents)
    Assert.Equal<string list>([ "TheOpeningIsOver" ], names shared.Advents)

    do Undo.undoHistory shared &state
    Assert.Equal<string list>([], names shared.Advents)
    Assert.Equal(0, List.length state.Editor.Undo.Applied)
    Assert.Equal(1, List.length state.Editor.Undo.Redo)

    do Undo.redoHistory shared &state
    Assert.Equal<string list>([ "TheOpeningIsOver" ], names shared.Advents)
    Assert.Equal(1, List.length state.Editor.Undo.Applied)
    Assert.Equal(0, List.length state.Editor.Undo.Redo)

[<Fact>]
let ``undo and redo restore the authored order`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    // added B before A, so alphabetical order would differ from the authored one
    do AdventPanel.commit shared &state (OSet.ofSeq1 [ "B"; "A" ])
    Assert.Equal<string list>([ "B"; "A" ], names shared.Advents)

    do Undo.undoHistory shared &state
    Assert.Equal<string list>([], names shared.Advents)

    do Undo.redoHistory shared &state
    Assert.Equal<string list>([ "B"; "A" ], names shared.Advents)

[<Fact>]
let ``a flag edit before a zone move is reached only after the move is undone`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do AdventPanel.commit shared &state (OSet.add "TheOpeningIsOver" shared.Advents)
    do Undo.commit shared &state (Command.ZoneAdd (GameplayState.startZone, "Island"))

    // The move reverses first, carrying the view back to main — and leaving the flag set, because a
    // move's undo touches no flag.
    do Undo.undoHistory shared &state
    Assert.Equal(GameplayState.startZone, state.ActiveZone)
    Assert.Equal<string list>([ "TheOpeningIsOver" ], names shared.Advents)

    // ...then the flag edit itself.
    do Undo.undoHistory shared &state
    Assert.Equal<string list>([], names shared.Advents)

[<Fact>]
let ``the zone-level runner refuses a flag step, which is state-level`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    let step = Command.Advent (OSet.ofSeq1 [], OSet.add "TheOpeningIsOver" (OSet.ofSeq1 []))
    Assert.True(raises (fun () -> Command.apply zone state.Physics shared.Assets step))
    Assert.True(raises (fun () -> Command.undo zone state.Physics shared.Assets step))
