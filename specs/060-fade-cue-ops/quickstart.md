# Quickstart: Fade Cue Ops

## Prerequisites

- .NET 10 SDK (`dotnet --version` reports `10.x`).
- Nothing else: **no window, no GPU, no device**. The fade is a `float32` on `GameplayState`, which the
  suite builds the way `test/CueTests.fs` already does.
- `dotnet build` clean (warnings are errors in this repo).

## Automated coverage (headless)

`dotnet test` runs the extended suites. What it asserts, by story:

| Story / requirement | Assertion |
|---|---|
| 1 — a fade out darkens to black | with the level at 0, `FadeOut 2.0` reached and stepped at 0, 1 and 2 leaves the level 0, 0.5 and 1.0 and the cue past the op. |
| 1 — a fade in clears it | with the level at 1, `FadeIn 2.0` gives 1, 0.5 and 0.0. |
| 1 — a part-way start | with the level at 0.4, a fade out runs 0.4 → 1.0 and a fade in 0.4 → 0.0; neither assumes an endpoint. |
| 1 — an immediate fade | `FadeOut 0.0` (and a negative duration) sets the endpoint and is `Fin` in the step it begins. |
| 1 — the endpoint is exact | at the deadline the level is exactly `1.0f` / `0.0f`, not an interpolation. |
| 1 — allocation | an in-flight fade allocates 0 bytes per step over many steps. |
| 2 — the panel authors it | both kinds are offered; `label` is `Fade out …`/`Fade in …` for the authored and in-flight forms; an in-flight fade is locked; retuning the duration is one `SetCue` step that undo restores. |
| FR-004/SC-005 — build/test | `dotnet build` clean, `dotnet test` green, no device. |

## Interactive walkthrough

The engine writes `GameplayState.Fade` (which the overlay from spec 059 renders), so the fastest way to
*see* a fade is a scratch tick using the headless fixture:

```fsharp
let mutable state = withCue (seqOf [ Wait 1.0; FadeOut 2.0; Wait 1.0; FadeIn 2.0 ])

CueEngine.tick shared &state 1.0   // the wait ends; the fade out begins where the level is (0)
CueEngine.tick shared &state 2.0   // half-way: 0.5
Assert.True(abs (state.Fade - 0.5f) < 0.001f)
CueEngine.tick shared &state 3.0   // the fade out is due: exactly black
Assert.Equal(1.0f, state.Fade)
CueEngine.tick shared &state 4.0   // the wait ends, the fade in begins from 1
CueEngine.tick shared &state 6.0   // exactly clear
Assert.Equal(0.0f, state.Fade)
```

Then the properties that are about *shape*:

- **Immediate** — `FadeOut 0.0` is `Fin` after one tick, with the level already 1.0.
- **Part-way** — set `state.Fade <- 0.4f` before a `FadeOut`, and the run reaches 1.0 from there.
- **Endpoint** — a fade that completes leaves the level exactly 1.0 or 0.0, so the overlay is fully on or
  fully off.
- **Two at once** — a `FadeOut` forked beside a `FadeIn` runs both; each lands its own endpoint.

In the editor, selecting a fade row shows a `Seconds` field; changing it is one undo step, and a fade that
is running is read-only and reads as `Fade out`/`Fade in`.

## Expected outcome

- `dotnet test` is green, including the fade timeline, the part-way start and the in-flight allocation.
- A cue can fade the gameplay view to black and back over chosen durations, from any starting level, and a
  completed fade lands exactly on its endpoint.
