module DiagnosticsTests

// The error tally (spec 081): the count, the de-duplication, the ordering of the detail, the formatter,
// the "loud" threshold, and the no-cost property of a cleared read. The tally is process-global, so each
// test clears it first; the runner serializes collections (`xunit.runner.json`), so no other class reports
// between a clear and an assert.

open System
open Xunit
open Diagnostics

[<Fact>]
let ``a fresh tally reports nothing`` () =
    Diagnostics.clear ()
    Assert.Equal(0, errorCount ())
    Assert.Equal("", lastMessage ())
    Assert.True((recent ()).IsEmpty)

[<Fact>]
let ``a report is counted and remembered as the most recent`` () =
    Diagnostics.clear ()
    do Diagnostics.report "boom"
    Assert.Equal(1, errorCount ())
    Assert.Equal("boom", lastMessage ())

[<Fact>]
let ``repeats de-duplicate into one entry with a count`` () =
    Diagnostics.clear ()
    for _ in 1 .. 5 do Diagnostics.report "repeat"
    Assert.Equal(5, errorCount ())
    match recent () with
    | [ r ] -> Assert.Equal(("repeat", 5), (r.Message, r.Count))
    | other -> Assert.Fail(sprintf "expected exactly one entry, got %A" other)

[<Fact>]
let ``distinct reports are separate, most recent first`` () =
    Diagnostics.clear ()
    do Diagnostics.report "first"
    do Diagnostics.report "second"
    Assert.Equal<string list>([ "second"; "first" ], recent () |> List.map (fun r -> r.Message))
    Assert.Equal(2, errorCount ())
    // and each carries its own count
    Assert.Equal<int list>([ 1; 1 ], recent () |> List.map (fun r -> r.Count))

[<Fact>]
let ``reportf formats like the eprintfn it replaces`` () =
    Diagnostics.clear ()
    do Diagnostics.reportf "cue: %s(%s) performs nothing: %s" "SetCamera" "main" "no waypoint"
    Assert.Equal("cue: SetCamera(main) performs nothing: no waypoint", lastMessage ())

[<Fact>]
let ``isLoud crosses at the threshold`` () =
    Diagnostics.clear ()
    for _ in 1 .. loudThreshold - 1 do Diagnostics.report "x"
    Assert.False(isLoud ())
    do Diagnostics.report "x"
    Assert.True(isLoud ())

[<Fact>]
let ``reading a cleared tally allocates nothing`` () =
    // The display reads the tally every frame; with nothing reported that read must cost nothing (FR-007).
    Diagnostics.clear ()
    let mutable acc = 0
    TestSupport.assertAllocatesNothing 2000 10_000 (fun () ->
        acc <- acc + errorCount () + (if isLoud () then 1 else 0) + (lastMessage ()).Length)
    Assert.Equal(0, acc)
