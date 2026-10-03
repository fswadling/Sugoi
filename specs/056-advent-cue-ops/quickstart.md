# Quickstart: Advent Cue Ops

## Prerequisites

- .NET 10 SDK (`dotnet --version` reports `10.x`).
- Nothing else: **no window, no GPU, no device**. The flag set is plain data on `SharedState`, which the suite builds
  the way `test/CueTests.fs` and `test/AdventTests.fs` already do.
- `dotnet build` clean (warnings are errors in this repo).

## Automated coverage (headless)

`dotnet test` runs the extended suites. What it asserts, by story:

| Story / requirement | Assertion |
|---|---|
| 1 — writes | `AddAdvent m` puts `m` in `SharedState.Advents` and costs no time (the cue is at the next op in the same step); `RemoveAdvent m` clears it; adding a set flag / removing a clear one is a no-op, not an error. |
| 2 — awaits | `Await m` with `m` clear stays on the op and holds up the queue; setting `m` finishes it and runs what follows; `Await m` with `m` already set costs no time; an `AddAdvent m` earlier in the same cue satisfies a later `Await m` in that step; clearing `m` while waiting leaves it waiting; 2000 waiting steps allocate **0 bytes** (`GC.GetAllocatedBytesForCurrentThread`). |
| 3 — branches | `If m` runs the then-branch when `m` is set and the else-branch otherwise; the untaken branch never runs; a `Fin` branch costs no time; a timed branch begins in the `If`'s place; an `If` nests inside a `Sequence`/`Parallel`/`Fork`. |
| 4 — panel | `kinds` offers the four and nothing spurious; each kind's default (the `If` with two `Fin` branches); the labels (`Add: …`, `Remove: …`, `Await: …`, `If: …`); an `If`'s two children each headed by a `then`/`else` label row; a running await is locked and reads as `Await m`; `textOf` is `ValueNone` for all four. |
| FR-011 — not undoable | No `Command` is produced by the ops; the existing history tests are unchanged. |
| FR-012 — build/test | `dotnet build` clean, `dotnet test` green, no device. |

## Interactive walkthrough

The engine loads with the game rather than on its own (it writes `SharedState`), so the fastest way to *see* the ops is
a scratch test in `test/CueTests.fs` using the same headless fixtures:

```fsharp
let mutable state = newState ()
// start from an empty flag set (the fixture's `shared` has `Advents = Set.empty`)

// ── set, then branch ──
state.Cue <- seqOf [ AddAdvent MetAiko; If (MetAiko, Exposit "hello", Exposit "stranger") ]
CueEngine.tick shared &state 0.0
Assert.Equal("hello", state.Dialogue.Text)          // the flag gate ran the then-branch
Assert.True(Set.contains MetAiko shared.Advents)

// ── wait, then continue ──
state.Cue <- seqOf [ Await MetAkane; Exposit "after" ]
CueEngine.tick shared &state 1.0
Assert.Equal("Await(MetAkane)", describe state.Cue) // still waiting, the exposition has not run
shared.Advents <- Set.add MetAkane shared.Advents
CueEngine.tick shared &state 2.0
Assert.Equal("Fin", describe state.Cue)
Assert.Equal("after", state.Dialogue.Text)
```

Then the properties that are about *shape*:

- **Idempotent** — `do AddAdvent MetAiko` twice in a row leaves the set unchanged and does not raise.
- **0 bytes** — tick an in-flight `AwaitState` 2000 times and measure `GC.GetAllocatedBytesForCurrentThread`: 0.
- **Re-read each step** — clear the flag while waiting and the cue keeps waiting (membership is not remembered).

## Expected outcome

- `dotnet test` is green, including the write assertions and the 0-byte waiting step.
- The scratch tick shows a cue setting a milestone, taking the matching branch, and resuming once a milestone lands.
- In the editor, the cue panel's kind list offers the four ops; each milestone is chosen from the closed list, an `If`
  shows its two branch rows under `then`/`else` labels, and a running await is read-only. Unticking the milestone in the
  Advent panel while a cue waits leaves it waiting.
