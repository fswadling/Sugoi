# Quickstart: Cue Core

## Prerequisites

- .NET 10 SDK (`dotnet --version` reports `10.x`).
- Nothing else: **no window, no GPU, no device**. The vocabulary and the run (`world/Cue.fs`) depend on `Prime`
  (already referenced) and `System.Numerics` only, so it loads on its own; the engine (`world/CueEngine.fs`) also
  names the gameplay state, which the headless suite builds the way `test/GameplayStateTests.fs` does
  (`Unchecked.defaultof` for the GPU pieces).
- `dotnet build` clean (warnings are errors in this repo).

## Automated coverage (headless)

`dotnet test` runs the new suite alongside the existing ones. What it asserts, by story:

| Story / property | Assertion |
|---|---|
| 1 — runs as written | For a scripted clock, a three-step cue's ops happen at the step the cue asks for and it is `Fin` at the step after its last op (0 timing deviations); two branches reach `Fin` together; a detached branch runs beside the remainder and the cue is `Fin` only when both have; a cue of no-progress ops is `Fin` within one step; `Fin` stays `Fin`. |
| 1 — re-runnable | The same cue value run twice reaches the same state — the value was not consumed by running it. |
| 2 — fork | A `Fork` in a sequence detaches to a parallel of the remainder and the branch, both running; `forkInto` runs an arriving cue beside the running one **without disturbing it** (the running op's remaining time is unchanged, 0 ms); forking into `Fin` makes the arriving cue the cue; the same value can be forked in again elsewhere. |
| 3 — the screen's | A `SwitchZone` in a sequence switches at its place and everything after it still runs (0 dropped, 0 duplicated); a detached branch's switch while the sequence waits; a switch really changes `ActiveZone` against zones built the way `test/GameplayStateTests.fs` builds them, and an unknown zone raises; with the editor open the cue does not advance, and Play resumes it rather than restarting it. |
| FR-011 — bounded | Peak live nodes are the same for a 4-step and a 40-step cue; a cue of 200,000 no-progress ops is consumed by one tick without a stack overflow. |
| FR-012 — allocation | 2000 ticks with an op in flight allocate **0 bytes** (measured with `GC.GetAllocatedBytesForCurrentThread`, flat and several sequences deep), as do 2000 ticks on `Fin`; 2000 ticks each completing an op allocate a bounded amount per op that does not grow with the cue's length. |
| FR-015 — determinism | Two runs of the same cue against the same clock agree after every step. |
| FR-014 — fail loudly | A `Fork` outside a sequence's current op raises where it is reached. |

## Interactive walkthrough

The engine performs its ops' effects on the gameplay state, so it loads with the game rather than on its own
(research D3) — there is no two-module FSI script. The fastest way to *see* a cue is a scratch test in
`test/CueTests.fs`, building the same headless fixtures (`Unchecked.defaultof` for the GPU pieces, a device-free
asset manager) that `test/GameplayStateTests.fs` uses. This is the same walk the editor's panel will do, minus the
panel:

```fsharp
// a tiny view of a cue, for the printout
let rec describe (cue: Cue) : string =
    match cue with
    | Fin -> "Fin"
    | Sequence (current, rest) ->
        "Seq[" + String.concat ", " (seq { yield describe current; yield! FDeque.toSeq rest |> Seq.map describe }) + "]"
    | Parallel (l, r) -> sprintf "Par(%s, %s)" (describe l) (describe r)
    | Fork c -> sprintf "Fork(%s)" (describe c)
    | Wait s | WaitState (s, _) -> sprintf "Wait %g" s
    | Exposit t -> sprintf "Exposit(%s)" t
    | SwitchZone z -> sprintf "Zone(%s)" z
    | MoveCamera _ | MoveCameraState _ -> "MoveCamera"

let mutable state = newState ()                 // the headless GameplayState fixture
state.Cue <- Sequence(Wait 2.0, FDeque.ofList [ Exposit "hello"; Wait 1.0 ])

for step in 0 .. 3 do
    CueEngine.tick shared &state (float step)
    printfn "t=%d  %s" step (describe state.Cue)
```

Then, for the properties that are about *shape* rather than time:

- **Fork** — put `Fork(Wait 2.0)` in a sequence: at the step the front op finishes, the cue becomes
  `Par(Seq[…], Wait 2)` and both run. `CueEngine.forkInto (Exposit "arrived") state.Cue` does the same for a cue
  arriving from outside.
- **`Fin` rolls** — after the last op the cue is `Fin`; tick again and it is still `Fin`, for nothing.
- **Zone switch** — put `SwitchZone "atrium"` in a sequence: the cue keeps going and the active zone really
  changed, because the engine called `GameplayState.switchZone` where it reached it.

## Expected outcome

- `dotnet test` is green, including the allocation assertions — a waiting cue costs the game loop nothing.
- The scratch tick prints the cue shrinking as it runs — `Seq[Wait 2, Exposit(hello), Wait 1]`, then
  `Seq[Wait 1]`, then `Fin`, then `Fin` — with `Exposit "hello"` written to `Dialogue` exactly once, at t=2.
- In the game, setting a cue and pressing Play runs it; opening the editor freezes it (the play chain is what
  advances `PlayTime`), and pressing Play again resumes it rather than restarting it. Authoring a cue *in the
  editor* is the next feature; the panel reads `state.Cue`, which is the run.
