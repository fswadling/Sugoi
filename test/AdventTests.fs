module AdventTests

// The adventure flags (spec 055), asserted headlessly: the milestone union, the flag set, the panel's
// write path, and the history round trip. Nothing here opens a window, a GPU or a device — the
// panel's drawing is the quickstart walkthrough's business, and the union and the write path are
// plain data, so they are checkable with no device.

open Xunit
open System
open System.Collections.Generic
open Advent

// ── fixtures ──

/// A headless gameplay state, the way `test/CuePanelTests.fs` builds one.
let private newState () : GameplayState.GameplayState =
    GameplayState.create (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<Ui.Ui>)

/// A headless `SharedState`: a device-free, empty asset manager and an empty flag set. Nothing here
/// spawns an entity, so no resource is touched; the physics world is the gameplay state's own.
let private sharedState () : SharedState.SharedState =
    { Gpu = Unchecked.defaultof<Gpu.Gpu>
      Assets = TestSupport.manager IntPtr.Zero (Dictionary()) (Dictionary()) (Dictionary())
      Advents = Set.empty }

/// The set milestones as their names, in declaration order — the set as a person reads it.
let private setNames (flags: Set<Advent>) : string list =
    all |> List.filter (fun advent -> Set.contains advent flags) |> List.map name

let private raises (f: unit -> unit) : bool =
    try
        f ()
        false
    with _ ->
        true

// ══════ the milestone union (FR-001) ══════

[<Fact>]
let ``every milestone is a case, enumerated in declaration order`` () =
    Assert.Equal<string list>(
        [ "OpeningComplete"; "MetAiko"; "MetAkane"; "MetShinji"; "MetYuki" ],
        all |> List.map name)

// ══════ the flag set (FR-001) ══════

[<Fact>]
let ``a fresh set is empty`` () =
    for advent in all do
        Assert.False(Set.contains advent Set.empty)
    Assert.Equal<string list>([], setNames Set.empty)

[<Fact>]
let ``setting and clearing a milestone are membership, and the original set is untouched`` () =
    let empty : Set<Advent> = Set.empty
    let one = Set.add MetAkane empty
    Assert.True(Set.contains MetAkane one)
    Assert.False(Set.contains MetAkane empty) // immutable: the empty set is a different value
    Assert.Equal<string list>([], setNames (Set.remove MetAkane one))

// ══════ the write path (FR-004, FR-005, FR-006) ══════

[<Fact>]
let ``an accepted set records exactly one step`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    Assert.Equal<string list>([], setNames shared.Advents)

    do AdventPanel.commit shared &state (Set.add OpeningComplete shared.Advents)
    Assert.Equal<string list>([ "OpeningComplete" ], setNames shared.Advents)
    Assert.Equal(1, List.length state.Editor.Undo.Applied)
    match state.Editor.Undo.Applied with
    | [ Command.Advent (previous, next) ] ->
        Assert.Equal<string list>([], setNames previous)
        Assert.Equal<string list>([ "OpeningComplete" ], setNames next)
    | other -> Assert.Fail(sprintf "expected one Advent step, got %A" other)

[<Fact>]
let ``a milestone is cleared by clearing it`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    shared.Advents <- Set.add MetAkane (Set.add OpeningComplete Set.empty)

    do AdventPanel.commit shared &state (Set.remove OpeningComplete shared.Advents)
    Assert.Equal<string list>([ "MetAkane" ], setNames shared.Advents)

[<Fact>]
let ``a flag survives a zone switch`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do AdventPanel.commit shared &state (Set.add OpeningComplete shared.Advents)

    do History.commit shared &state (Command.ZoneAdd (GameplayState.startZone, "Island"))
    Assert.Equal("Island", state.ActiveZone)
    Assert.Equal<string list>([ "OpeningComplete" ], setNames shared.Advents)

// ══════ the history (FR-007, FR-009) ══════

[<Fact>]
let ``undo restores the prior set and redo re-applies the edit`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do AdventPanel.commit shared &state (Set.add OpeningComplete shared.Advents)
    Assert.Equal<string list>([ "OpeningComplete" ], setNames shared.Advents)

    do History.undoHistory shared &state
    Assert.Equal<string list>([], setNames shared.Advents)
    Assert.Equal(0, List.length state.Editor.Undo.Applied)
    Assert.Equal(1, List.length state.Editor.Undo.Redo)

    do History.redoHistory shared &state
    Assert.Equal<string list>([ "OpeningComplete" ], setNames shared.Advents)
    Assert.Equal(1, List.length state.Editor.Undo.Applied)
    Assert.Equal(0, List.length state.Editor.Undo.Redo)

[<Fact>]
let ``a flag edit before a zone move is reached only after the move is undone`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do AdventPanel.commit shared &state (Set.add OpeningComplete shared.Advents)
    do History.commit shared &state (Command.ZoneAdd (GameplayState.startZone, "Island"))

    // The move reverses first, carrying the view back to main — and leaving the flag set, because a
    // move's undo touches no flag.
    do History.undoHistory shared &state
    Assert.Equal(GameplayState.startZone, state.ActiveZone)
    Assert.Equal<string list>([ "OpeningComplete" ], setNames shared.Advents)

    // ...then the flag edit itself.
    do History.undoHistory shared &state
    Assert.Equal<string list>([], setNames shared.Advents)

[<Fact>]
let ``the zone-level runner refuses a flag step, which is state-level`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    let step = Command.Advent (Set.empty, Set.add OpeningComplete Set.empty)
    Assert.True(raises (fun () -> Command.apply zone state.Physics shared.Assets step))
    Assert.True(raises (fun () -> Command.undo zone state.Physics shared.Assets step))
