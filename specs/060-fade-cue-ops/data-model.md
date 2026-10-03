# Data Model: Fade Cue Ops

## `world/Cue.fs` — changed

```fsharp
// The screen fade (spec 060): drive `GameplayState.Fade` toward an endpoint over a duration. `…State`
// carries the level captured at begin, the duration and the deadline, as `WaitState`/`FlyCameraState` do.
| FadeOut of duration: float
| FadeIn of duration: float
| FadeOutState of initial: float32 * duration: float * endsAt: float
| FadeInState of initial: float32 * duration: float * endsAt: float
```

- **Two ops, one number each.** A fade carries only its duration, never the level, so the module still names
  no screen and no zone; the level is the engine's to read and write.
- **The state adds `initial`**, the level captured where the op was reached (research D6), plus the deadline
  the tween is measured against — the same three-scalar shape `FlyCameraState` has, without the resolved
  path.
- **A non-positive duration holds no state**: the authored op reports `Fin` and sets the endpoint (D3).

## `world/CueEngine.fs` — changed

```fsharp
/// Drive the level toward `target` along `initial`..`target` spanning `duration`, ending at `endsAt`.
/// Writes `state.Fade` and answers whether the fade is over (then exactly `target`).
let private stepFade (state: byref<GameplayState.GameplayState>) (initial: float32) (target: float32)
                     (duration: float) (endsAt: float) (now: float) : bool =
    if now >= endsAt then
        state.Fade <- target
        true
    else
        let t = (now - (endsAt - duration)) / duration
        state.Fade <- initial + float32 t * (target - initial)
        false

// in `advance`
| FadeOut duration ->
    let initial = state.Fade
    let endsAt = now + duration
    if stepFade &state initial 1.0f duration endsAt now then ValueSome Fin
    else ValueSome(FadeOutState(initial, duration, endsAt))

| FadeOutState (initial, duration, endsAt) ->
    if stepFade &state initial 1.0f duration endsAt now then ValueSome Fin else ValueNone

| FadeIn duration ->
    let initial = state.Fade
    let endsAt = now + duration
    if stepFade &state initial 0.0f duration endsAt now then ValueSome Fin
    else ValueSome(FadeInState(initial, duration, endsAt))

| FadeInState (initial, duration, endsAt) ->
    if stepFade &state initial 0.0f duration endsAt now then ValueSome Fin else ValueNone
```

- **One write per step**: an in-flight fade writes one `float32` and returns `ValueNone`, so nothing is
  rebuilt and nothing is allocated per tick (FR-006).
- **The endpoint is exact**: the deadline branch assigns `target`, not an interpolation (FR-005).
- **No dependency is added**: the engine already names `GameplayState` (research D4).

## `editor/CueTree.fs` — changed

| Surface | Change |
|---|---|
| `kinds` | adds `"Fade out"`, `"Fade in"` |
| `kindName` | `FadeOut _ | FadeOutState _ -> "Fade out"`; `FadeIn _ | FadeInState _ -> "Fade in"` |
| `defaultOfKind` | `"Fade out" -> FadeOut 1.0`; `"Fade in" -> FadeIn 1.0` (a one-second fade, as `Wait` defaults to 1.0) |
| `label` | `FadeOut d | FadeOutState (d, _, _) -> sprintf "Fade out %g" d`; the same for a fade in |
| `executing` | `FadeOutState _ | FadeInState _ -> true`; both authored forms `false` |
| `children` | the four cases have no children |
| `drawEditor` | `FadeOut d | FadeOutState (d, _, _)` and the in one edit `dragNumber idBase "Seconds"` and commit `FadeOut(float v)` / `FadeIn(float v)` |
| `textOf` / `withText` | unchanged — a fade carries no text |
| `withAdvent` / `withExpositKind` | unchanged |

## Validation rules

| Rule | Where it is enforced |
|---|---|
| A fade has an endpoint (out→1, in→0) | the op — `FadeOut` vs `FadeIn` |
| A completed fade is exactly black or clear | `stepFade`'s deadline branch assigns `target` |
| A non-positive duration is immediate | `stepFade`'s deadline branch at the begin step |
| The level stays in 0..1 | the start is in 0..1 (spec 059) and the endpoint is 0 or 1; a linear tween between them cannot leave the range |
| An in-flight fade is read-only in the panel | `executing` reports `true` for the two state cases |
| A fade is a leaf | `children` returns `[]` |

## Deliberately not modelled

- **Easing / a curve** — the reference is linear; nothing asks for more (research D2).
- **A fade colour** — the overlay is black (spec 059); a fade is a level, not a tint.
- **A clamp on a negative duration** — a non-positive duration is defined as immediate (D3).
- **Forbidding two fades at once** — documented as benign; the walk order decides the frame (D7).
