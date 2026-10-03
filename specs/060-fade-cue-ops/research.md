# Phase 0 Research: Fade Cue Ops

The reference already answers the behaviour: two ops, a captured start, a linear tween, an exact endpoint.
The decisions here are about the shape this port gives them and how the panel treats them.

## Decision 1 — Two ops, four cases: `FadeOut`/`FadeOutState` and `FadeIn`/`FadeInState`

- **Decision**: add `FadeOut of duration: float`, `FadeIn of duration: float`, and run states
  `FadeOutState of initial: float * duration: float * endsAt: float` and
  `FadeInState of initial: float * duration: float * endsAt: float`.
- **Rationale**: it matches the reference exactly (two authored ops, two state structs) and the port's own
  convention, where a leaf that spans time carries its numbers in its own `…State` case
  (`Wait`/`WaitState`, `FlyCamera`/`FlyCameraState`). It also keeps `label`, `kindName`, `executing` and
  `children` trivial: an in-flight fade reads as `Fade out`/`Fade in` by a case match, with no arithmetic.
- **Alternatives considered**: **one `FadeState` carrying `target`**, with both ops beginning it — rejected:
  the target is already encoded by *which* op ran, so the state would either duplicate it or force the
  panel to branch on a float to recover the op's name. **A single `Fade of target * duration` op** —
  rejected: it diverges from the reference's two-op vocabulary and makes "fade out" a value the author
  types rather than a kind they choose; `FadeOut`/`FadeIn` are also how the reference's editor lists them.

## Decision 2 — A linear tween that lands exactly on the endpoint

- **Decision**: while in flight, `Fade = initial + t * (target - initial)` with `t` from the deadline and
  duration; at the deadline, set `Fade` to exactly `target` (1.0 or 0.0).
- **Rationale**: it is the reference's arithmetic (`src/cue.odin:1249,1259`), and the explicit endpoint
  assignment (`:1244,1254`) is what makes a completed fade leave the view *exactly* black or *exactly*
  clear rather than a float-epsilon away. No easing is asked for and none exists in the reference.
- **Alternatives considered**: **an eased curve** — rejected: not in the reference, not requested
  (Constitution I). **Letting the last interpolation stand** — rejected: it can land at 0.9999999 and leave a
  faint overlay, which is a visible bug.

## Decision 3 — A non-positive duration is an immediate fade

- **Decision**: when the duration is zero or less the fade is over before it begins: the level is set to the
  endpoint in the step the op is reached and the op holds no state. The editor does not clamp the duration.
- **Rationale**: this is the port's existing rule for timed ops — `beginWait` returns `Fin` when
  `now >= endsAt` — so a fade behaves like a `Wait` of the same duration. The reference clamps the authored
  duration to 0 in its editor and then its endpoint check makes it immediate; clamping or not is therefore
  behaviourally identical here, and not clamping is one less special case (Constitution I).
- **Alternatives considered**: **clamping to 0 in the panel, as the reference does** — rejected: it would
  add an editor rule whose only effect is on a value the engine already handles, and `Wait` does not clamp.
  **Raising on a negative duration** — rejected: a negative `Wait` is legal and immediate; fades should
  read the same.

## Decision 4 — One private `stepFade` helper shared by both ops

- **Decision**: a private `stepFade (state) (initial) (target) (duration) (endsAt) (now) : bool` writes the
  level and answers whether the fade is over; each authored op begins by calling it once, and each `…State`
  arm calls it per tick.
- **Rationale**: the tween arithmetic would otherwise appear four times (begin/step × out/in), inviting the
  out and in copies to drift. It is one obvious orchestrator, not a speculative abstraction: both ops are
  the same curve with a different endpoint.
- **Alternatives considered**: **duplicating the arithmetic per op, as the reference duplicates its two
  branches** — rejected: the reference's branches are near-identical and the divergence risk is real; a
  helper costs one function and removes it.

## Decision 5 — The panel treats a fade as it treats a `Wait`

- **Decision**: `kinds` gains `"Fade out"` and `"Fade in"`; `kindName` maps both forms of each to its name;
  `label` renders `Fade out %g` / `Fade in %g`; `defaultOfKind` gives a one-second fade; `drawEditor` edits
  the duration with the existing `dragNumber`; `executing` and `children` list the four cases.
- **Rationale**: a fade is a timed leaf with one positive number — structurally a `Wait` toward an endpoint —
  so it reuses the number field, the lock rule and the label idiom rather than inventing a surface. The
  duration default of 1.0 matches `Wait`'s, so a newly placed op is a real step the author then retunes.
- **Alternatives considered**: **a slider or a bounded field** — rejected: a duration is unbounded like
  `Wait`'s, and `dragNumber` is already the shared field. **Showing the target level in the label** —
  rejected: the op's *name* already says out or in, and `Wait %g` sets the precedent of number-only.

## Decision 6 — The starting level is captured where the walk reaches the op

- **Decision**: the authored op reads `state.Fade` at the moment it is reached and stores it in the state
  case as `initial`; the tween runs from there.
- **Rationale**: this is the reference's `initial_fade = ctx.screen.fade` (`src/cue.odin:966,977`), and it is
  what makes a fade from part-way correct — a fade out that began from black would otherwise flash. It also
  means a fade interrupted by another fade does not jump, which is exactly the property a "capture the
  start" buys.
- **Alternatives considered**: **assuming the start is 0 (out) or 1 (in)** — rejected: wrong whenever the
  level is mid-fade or was set by the Misc slider, and the overlay makes those states reachable.

## Decision 7 — Two fades may run at once

- **Decision**: do not forbid a second fade while one is in flight (for example a fade forked beside a
  running fade). Both write the level in a frame; the walk's order decides which wins that frame, and each
  lands its own endpoint at its own deadline.
- **Rationale**: a cue is an immutable value composed with `Parallel`/`Fork`; forbidding overlap would be a
  new mechanism (an owner check) the reference does not have either. Forks are how cues compose
  (`specs/046`), and a later fade simply captures the level the earlier one left.
- **Alternatives considered**: **an "already fading" guard** — rejected: a new failure mode and a new
  mechanism for a case with a defined, benign behaviour. **Last-writer semantics documented only in code** —
  rejected: it belongs in the spec's edge cases, which is where it now is.
