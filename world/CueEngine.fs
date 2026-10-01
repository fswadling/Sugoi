module CueEngine

// The cue engine (spec 046). `advance` is the whole of it — one cue in, the cue as it now stands out — and
// `tick` writes that answer back, so setting the cue *is* the beginning and there is no start call.
// `forkInto` runs an arriving cue beside the running one.
//
// Compiled *after* `state/GameplayState.fs` — the project's one deliberate ordering inversion — because
// each op performs its effect on the gameplay state where the walk reaches it: an `Exposit` writes the
// screen's dialogue, a `SwitchZone` reuses `GameplayState.switchZone` (which is why the walk carries
// `shared` for the physics swap), and a `MoveCamera` writes the camera (research D3).

open System.Numerics
open Prime
open Cue

// ── The ops' effects, performed where the walk reaches them ──

let private exposit (state: byref<GameplayState.GameplayState>) (text: string) : unit =
    state.Dialogue.Shown <- true
    state.Dialogue.Kind <- Dialogue.Kind.Dialogue
    state.Dialogue.Text <- text

let private applyPose (state: byref<GameplayState.GameplayState>) (p: CameraPose) : unit =
    state.Camera.Position <- p.Position
    state.Camera.Target <- p.Target

let private lerpPose (a: CameraPose) (b: CameraPose) (t: float) : CameraPose =
    let t32 = float32 t
    { Position = Vector3.Lerp(a.Position, b.Position, t32)
      Target = Vector3.Lerp(a.Target, b.Target, t32) }

/// Begin a timed op. A duration of zero (or less) is over before it begins.
let private beginWait (now: float) (seconds: float) : Cue =
    let endsAt = now + seconds
    if now >= endsAt then Fin else WaitState(seconds, endsAt)

// ── The engine — a cue in, the cue as it now stands out ──

/// Advance a cue one tick, performing each op's effect where the walk reaches it.
///
/// The answer says whether there is a cue to hand back. `ValueSome` is the cue as it now stands — which may
/// be `Fin`, a *value* rather than an absence — and `ValueNone` says the cue is still working on what it was
/// given, so the caller keeps that. "Nothing moved" is therefore a fact the answer carries, rather than an
/// invariant about reference identity that every caller has to know; and `ValueOption` is a struct, so
/// saying it costs the idle path nothing. The suite measures that: an idle tick and a tick on `Fin` both
/// allocate 0 bytes.
let rec advance
    (shared: SharedState.SharedState)
    (state: byref<GameplayState.GameplayState>)
    (now: float)
    (cue: Cue) : Cue voption =
    match cue with
    | Fin -> ValueSome Fin // nothing left, and it stays nothing left — the fixed point, and still a *value*
    | Exposit text ->
        exposit &state text
        ValueSome Fin // no progress of its own: performed here and gone
    | SwitchZone name ->
        GameplayState.switchZone shared &state name
        ValueSome Fin
    | Wait seconds -> ValueSome(beginWait now seconds)
    | WaitState (_, endsAt) ->
        if now >= endsAt then ValueSome Fin else ValueNone // not due yet: nothing to change
    | MoveCamera (target, duration) ->
        if duration <= 0.0 then
            do applyPose &state target
            ValueSome Fin
        else
            let from : CameraPose = { Position = state.Camera.Position; Target = state.Camera.Target }
            ValueSome(MoveCameraState(from, target, duration, now + duration))
    | MoveCameraState (from, target, duration, endsAt) ->
        if now >= endsAt then
            do applyPose &state target
            ValueSome Fin
        else
            // only the camera moves while the move runs, so there is no new cue to hand back — and, since
            // the cue value is untouched, no allocation either
            do applyPose &state (lerpPose from target ((now - (endsAt - duration)) / duration))
            ValueNone
    | Fork _ -> invalidOp "a Fork is only meaningful at a Sequence's current op"
    | Parallel (left, right) -> stepParallel shared &state now left right
    | Sequence (current, rest) -> stepSequence shared &state now current rest

/// Both sides run; a side that is `Fin` is done and drops out, and the point is over when both are. Neither
/// side moving — the common idle case — is `ValueNone`, so an idle point needs no comparison with the value
/// it was given.
and stepParallel
    (shared: SharedState.SharedState)
    (state: byref<GameplayState.GameplayState>)
    (now: float)
    (left: Cue)
    (right: Cue) : Cue voption =
    let l = advance shared &state now left
    let r = advance shared &state now right

    if l.IsNone && r.IsNone then ValueNone // neither moved: nothing to rebuild
    else

    // a side that reported nothing is the side it already was
    let l' = match l with ValueSome c -> c | ValueNone -> left
    let r' = match r with ValueSome c -> c | ValueNone -> right
    match l', r' with
    | Fin, Fin -> ValueSome Fin
    | Fin, x | x, Fin -> ValueSome x
    | x, y -> ValueSome(Parallel(x, y))

/// Step a sequence. When the front finishes, the next op is pulled from `rest` and the walk carries on **in
/// the same step** — so a run of instant ops is consumed by one tick — with the pull in **tail position**, so a
/// run of 200,000 no-progress ops does not grow the stack (FR-011). That position is load-bearing: post-processing
/// the pull's answer (a `match` that rewrites `ValueNone` to a rebuilt sequence) would put the frames back on the
/// stack. A front still in flight is the one way this answers `ValueNone`, and the idle path never touches `rest`.
and stepSequence
    (shared: SharedState.SharedState)
    (state: byref<GameplayState.GameplayState>)
    (now: float)
    (current: Cue)
    (rest: Cue FDeque) : Cue voption =
    stepSequenceNext shared &state now current rest false

/// `stepSequence`'s walker. `moved` says whether it has already pulled past the queue's front: the front is
/// data, so *stepping onto* a new one changes the sequence even if that op then reports nothing to change. It is
/// a parameter precisely so the pull below is the direct return value — the tail call that keeps the stack flat.
and stepSequenceNext
    (shared: SharedState.SharedState)
    (state: byref<GameplayState.GameplayState>)
    (now: float)
    (current: Cue)
    (rest: Cue FDeque)
    (moved: bool) : Cue voption =
    match current with
    | Fork inner ->
        // "run beside the rest of my sequence": a parallel of the remainder and the branch, both
        // advanced in this same step (the remainder on the left, as the reference has it)
        if rest.IsEmpty then advance shared &state now inner else
        let (next, rest') = FDeque.uncons rest
        advance shared &state now (Parallel(Sequence(next, rest'), inner))

    | _ ->

    match advance shared &state now current with
    | ValueNone ->
        // in flight, unmoved — unless the walk already stepped onto this front, in which case the
        // sequence has still changed shape and the caller must be handed the moved-up queue
        if moved then ValueSome(Sequence(current, rest)) else ValueNone

    | ValueSome current' ->
    // A `Fin` front is a *hole* — nothing to run — so the walk carries past it. `advance Fin`
    // answers `ValueSome Fin` rather than `ValueNone` precisely so that this lands here, rather
    // than being read as "in flight, unmoved".
    if current'.IsFin then
        if rest.IsEmpty then ValueSome Fin
        else
            let (next, rest') = FDeque.uncons rest
            stepSequenceNext shared &state now next rest' true // the pull: tail position, stack-flat (FR-011)
    else ValueSome(Sequence(current', rest)) // began or moved: rebuild the front only

/// Advance the state's cue one tick, in place. `tick` owns the cue, so the play chain never has to lift it
/// or write it back, and setting the cue is the only way it begins. A step that reports nothing to change
/// leaves the state's cue exactly where it is — not even written back.
let tick
    (shared: SharedState.SharedState)
    (state: byref<GameplayState.GameplayState>)
    (now: float) : unit =
    let current = state.Cue
    match advance shared &state now current with
    | ValueSome next -> state.Cue <- next
    | ValueNone -> ()

/// Run an arriving cue beside whatever is running: `Fin` becomes the cue, anything else becomes a
/// parallel with it. The running cue is not rebuilt, so nothing in flight is disturbed (FR-009). This
/// is what a trigger will call; `Fork` is the authored form of the same thing.
let forkInto (joined: Cue) (current: Cue) : Cue =
    match current with
    | Fin -> joined
    | _ -> Parallel(current, joined)
