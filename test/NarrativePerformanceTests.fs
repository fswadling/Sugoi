module NarrativePerformanceTests

// The guard for the cost research D30 found, and the one figure in this file that never depended on
// the cast's size: encoding a value as canonical text must not go back through the type. Written out
// as a match it costs a fraction of a microsecond; asking `GetUnionFields` cost ~17 us and was nearly
// all of a commit's time. Five microseconds is ten times what the encoder measures and a third of
// what reflection measured, so it is a smoke alarm rather than a speed record — and if a contended
// run ever trips it, the bound is the thing to raise, not the encoder.
//
// This was the narrative suite's performance suite: SC-005's rebuild and one-event latency, and
// SC-009's sustained throughput, each measured at a cast of twenty, with its own 10,000-event world.
// Those criteria and their worlds went with the cast when it shrank to the four the story has
// (`specs/032-cast-binding`); `specs/025-narrative-core` records the withdrawal. What is left is the
// render guard, which measures a cost per value rather than a rate over a cast.

open System.Diagnostics
open Xunit
open Narrative
open Roster

[<Fact>]
let ``a canonical text render does not go through reflection`` () =
    let cast = characters
    let size = List.length cast

    let facts =
        [ for i in 0 .. 9_999 ->
            let a = cast[i % size]
            let b = cast[(i * 7 + 3) % size]

            match i % 7 with
            | 0 -> HasTrait(a, if i % 2 = 0 then Shy else HotHeaded)
            | 1 -> Friends(a, b)
            | 2 -> AttractedTo(a, b)
            | 3 -> InRelationship(a, b)
            | 4 -> JealousOf(a, b)
            | 5 -> FriendshipThreatened(a, b)
            | _ -> BottledUpFeelings(a, b) ]

    // Warm first: a first pass pays for the JIT of the encoder, and the claim is about the encoder.
    facts |> List.iter (fun fact -> ignore (Canonical.factText fact))

    let watch = Stopwatch.StartNew()
    let mutable length = 0

    for _ in 1 .. 10 do
        for fact in facts do
            length <- length + (Canonical.factText fact).Length

    watch.Stop()

    let each = watch.Elapsed.TotalMicroseconds / float (10 * List.length facts)

    printfn "a canonical text render costs %.2f us a value (%d characters)" each length
    Assert.True((each < 5.0), sprintf "rendering canonical text cost %.2f us a value" each)
