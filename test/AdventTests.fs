module AdventTests

// The adventure flags (spec 055; a string set since spec 082), asserted headlessly: the ordered string set,
// the panel's write path, and the history round trip. Nothing here opens a window, a GPU or a device — the
// set is plain data, so it is checkable with no device, and the panel's drawing is the quickstart's business.

open Xunit
open System
open System.Collections.Generic

// ── fixtures ──

/// A headless gameplay state, the way `test/CuePanelTests.fs` builds one.
let private newState () : GameplayState.GameplayState =
    GameplayState.create (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<Ui.Ui>) (Unchecked.defaultof<Skydome.Skydome>)

/// A headless `SharedState`: a device-free, empty asset manager and an empty advent set. Nothing here spawns
/// an entity, so no resource is touched; the physics world is the gameplay state's own.
let private sharedState () : SharedState.SharedState =
    { Gpu = Unchecked.defaultof<Gpu.Gpu>
      Assets = TestSupport.manager IntPtr.Zero (Dictionary()) (Dictionary()) (Dictionary())
      Advents = Advent.empty
      Mix = Music.empty
      Rack = Music.newRack ()
      Voices = Sound.newVoices Audio.unavailable }

let private raises (f: unit -> unit) : bool =
    try
        f ()
        false
    with _ ->
        true

// ══════ the ordered string set (FR-001, FR-005) ══════

[<Fact>]
let ``a fresh set is empty`` () =
    Assert.Equal<string list>([], Advent.names Advent.empty)

[<Fact>]
let ``names read back in the order they were added`` () =
    let set = Advent.empty |> Advent.add "TheOpeningIsOver" |> Advent.add "MetAiko" |> Advent.add "MetAkane"
    Assert.Equal<string list>([ "TheOpeningIsOver"; "MetAiko"; "MetAkane" ], Advent.names set)

[<Fact>]
let ``adding a name already present is a no-op`` () =
    let once = Advent.add "MetAiko" Advent.empty
    let twice = Advent.add "MetAiko" once
    Assert.Equal<string list>([ "MetAiko" ], Advent.names twice)

[<Fact>]
let ``a name is trimmed before it is stored, and matched exactly by case`` () =
    Assert.Equal("MetAiko", Advent.normalise " MetAiko ")
    let set = Advent.add (Advent.normalise " MetAiko ") Advent.empty
    Assert.Equal<string list>([ "MetAiko" ], Advent.names set)
    // the trimmed name and the typed name are the same advent; case still tells two names apart
    let other = Advent.add "metAiko" set
    Assert.Equal<string list>([ "MetAiko"; "metAiko" ], Advent.names other)

[<Fact>]
let ``removing a name drops it, and removing an absent name is a no-op`` () =
    let set = Advent.ofList [ "A"; "B" ]
    Assert.Equal<string list>([ "B" ], Advent.names (Advent.remove "A" set))
    Assert.Equal<string list>([ "A"; "B" ], Advent.names (Advent.remove "Z" set))

[<Fact>]
let ``the set is immutable: an add leaves the original untouched`` () =
    let empty = Advent.empty
    let one = Advent.add "MetAkane" empty
    Assert.True(Advent.contains "MetAkane" one)
    Assert.False(Advent.contains "MetAkane" empty)

// ══════ the write path (FR-003, FR-004, FR-008) ══════

[<Fact>]
let ``an accepted add records exactly one step`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    Assert.Equal<string list>([], Advent.names shared.Advents)

    do AdventPanel.commit shared &state (Advent.add "TheOpeningIsOver" shared.Advents)
    Assert.Equal<string list>([ "TheOpeningIsOver" ], Advent.names shared.Advents)
    Assert.Equal(1, List.length state.Editor.Undo.Applied)
    match state.Editor.Undo.Applied with
    | [ Command.Advent (previous, next) ] ->
        Assert.Equal<string list>([], Advent.names previous)
        Assert.Equal<string list>([ "TheOpeningIsOver" ], Advent.names next)
    | other -> Assert.Fail(sprintf "expected one Advent step, got %A" other)

[<Fact>]
let ``a remove records exactly one step`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    shared.Advents <- Advent.ofList [ "A"; "B" ]

    do AdventPanel.commit shared &state (Advent.remove "A" shared.Advents)
    Assert.Equal<string list>([ "B" ], Advent.names shared.Advents)
    Assert.Equal(1, List.length state.Editor.Undo.Applied)

// ══════ the history (FR-009) and the state-level rule (FR-008) ══════

[<Fact>]
let ``a flag survives a zone switch`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do AdventPanel.commit shared &state (Advent.add "TheOpeningIsOver" shared.Advents)

    do History.commit shared &state (Command.ZoneAdd (GameplayState.startZone, "Island"))
    Assert.Equal("Island", state.ActiveZone)
    Assert.Equal<string list>([ "TheOpeningIsOver" ], Advent.names shared.Advents)

[<Fact>]
let ``undo restores the prior set and redo re-applies the edit`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do AdventPanel.commit shared &state (Advent.add "TheOpeningIsOver" shared.Advents)
    Assert.Equal<string list>([ "TheOpeningIsOver" ], Advent.names shared.Advents)

    do History.undoHistory shared &state
    Assert.Equal<string list>([], Advent.names shared.Advents)
    Assert.Equal(0, List.length state.Editor.Undo.Applied)
    Assert.Equal(1, List.length state.Editor.Undo.Redo)

    do History.redoHistory shared &state
    Assert.Equal<string list>([ "TheOpeningIsOver" ], Advent.names shared.Advents)
    Assert.Equal(1, List.length state.Editor.Undo.Applied)
    Assert.Equal(0, List.length state.Editor.Undo.Redo)

[<Fact>]
let ``undo and redo restore the authored order`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    // added B before A, so alphabetical order would differ from the authored one
    do AdventPanel.commit shared &state (Advent.ofList [ "B"; "A" ])
    Assert.Equal<string list>([ "B"; "A" ], Advent.names shared.Advents)

    do History.undoHistory shared &state
    Assert.Equal<string list>([], Advent.names shared.Advents)

    do History.redoHistory shared &state
    Assert.Equal<string list>([ "B"; "A" ], Advent.names shared.Advents)

[<Fact>]
let ``a flag edit before a zone move is reached only after the move is undone`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do AdventPanel.commit shared &state (Advent.add "TheOpeningIsOver" shared.Advents)
    do History.commit shared &state (Command.ZoneAdd (GameplayState.startZone, "Island"))

    // The move reverses first, carrying the view back to main — and leaving the flag set, because a
    // move's undo touches no flag.
    do History.undoHistory shared &state
    Assert.Equal(GameplayState.startZone, state.ActiveZone)
    Assert.Equal<string list>([ "TheOpeningIsOver" ], Advent.names shared.Advents)

    // ...then the flag edit itself.
    do History.undoHistory shared &state
    Assert.Equal<string list>([], Advent.names shared.Advents)

[<Fact>]
let ``the zone-level runner refuses a flag step, which is state-level`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    let step = Command.Advent (Advent.empty, Advent.add "TheOpeningIsOver" Advent.empty)
    Assert.True(raises (fun () -> Command.apply zone state.Physics shared.Assets step))
    Assert.True(raises (fun () -> Command.undo zone state.Physics shared.Assets step))
