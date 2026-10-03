# Contract: Fade Cue Ops

The surfaces this feature adds or changes, and what explicitly does not change.

## `world/Cue.fs` — CHANGED

```fsharp
| FadeOut of duration: float
| FadeIn of duration: float
| FadeOutState of initial: float32 * duration: float * endsAt: float
| FadeInState of initial: float32 * duration: float * endsAt: float
```

**Contract**: two ordinary leaf ops with no children and no text, and their run states. The authored ops
carry only a duration, so the module still names no zone, screen, asset or device — the level is captured in
the engine. An in-flight fade reads as the op it came from (`Fade out`/`Fade in`) and is locked in the
panel (FR-001, FR-006, FR-007, FR-008).

## `world/CueEngine.fs` — CHANGED

```fsharp
let private stepFade (state: byref<GameplayState.GameplayState>) (initial: float32) (target: float32)
                     (duration: float) (endsAt: float) (now: float) : bool =
    if now >= endsAt then
        state.Fade <- target
        true
    else
        let t = (now - (endsAt - duration)) / duration
        state.Fade <- initial + float32 t * (target - initial)
        false

| FadeOut duration ->
    let initial = state.Fade
    let endsAt = now + duration
    if stepFade &state initial 1.0f duration endsAt now then ValueSome Fin
    else ValueSome(FadeOutState(initial, duration, endsAt))
| FadeOutState (initial, duration, endsAt) ->
    if stepFade &state initial 1.0f duration endsAt now then ValueSome Fin else ValueNone
// ... and the same pair with target 0.0f for FadeIn/FadeInState
```

**Contract**: the engine captures `state.Fade` where the walk reaches the op, tweens it linearly toward the
endpoint, and sets the endpoint exactly at the deadline. A non-positive duration writes the endpoint and
returns `ValueSome Fin` in the begin step (FR-002, FR-003, FR-004, FR-005). An in-flight fade returns
`ValueNone` — the cue is unmoved — and writes one `float32` with no allocation (FR-006).

## `editor/CueTree.fs` — CHANGED

Both kinds join `kinds`; `kindName`, `label`, `defaultOfKind`, `executing`, `children` and `drawEditor`
handle them. The duration is edited with the existing `dragNumber` and committed through `withAt`, so an
accepted edit is one `SetCue` step (spec 049) as every other number is (FR-007).

**Contract**: the control still reads no game state for a fade — a duration needs no zone, registry or
asset — so the whole surface stays in the shared control.

## `test/CueTests.fs`, `test/CuePanelTests.fs` — CHANGED

Both `describe` helpers gain the four cases (they are exhaustive over `Cue`). `CueTests` gains the timeline
(0/½/1), the part-way start, the exact endpoint, the immediate case, and the in-flight allocation property;
`CuePanelTests` gains the labels (authored and in-flight), the in-flight lock, and the retune-one-step.
Neither opens a device (FR-006, FR-007, FR-008).

## Explicitly unchanged

- **`state/GameplayState.fs`** — `Fade` already exists (spec 059); these ops only move it.
- **`screen/GameplayScreen.fs`** — the overlay already reads `Fade`; it needs no change to show a fade.
- **`world/Dialogue.fs`, `cast/Roster.fs`, `world/Advent.fs`** — untouched by these ops.
- **`state/Command.fs`, `state/History.fs`** — a cue edit is still one `SetCue` step; no command is added.
- **`fsharp-game.fsproj`, `test/fsharp-game.Tests.fsproj`** — no new module or file, so no compile change.
- **The native library and the dock layout** — untouched.
- **The remaining op families** — actor movement, animation blends, trigger toggles and preloading stay
  deferred.
