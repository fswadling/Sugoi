module CueEngine

// The cue engine (spec 046). `advance` is the whole of it — one cue in, the cue as it now stands out — and
// `tick` writes that answer back, so setting the cue *is* the beginning and there is no start call.
// `forkInto`, which runs an arriving cue beside the running one, lives beside the type in `world/Cue.fs` so the
// trigger system — compiled before this engine — can fork without waiting for it (spec 058).
//
// Compiled *after* `state/GameplayState.fs` — the project's one deliberate ordering inversion — because
// each op performs its effect on the gameplay state where the walk reaches it: an `Exposit` writes the
// screen's dialogue, a `SetZone` reuses `GameplayState.switchZone` (which is why the walk carries
// `shared` for the physics swap), the camera ops write the camera from a named waypoint, and the advent ops
// read and write `SharedState.Advents` (research D3).

open System.Numerics
open Prime
open Cue
open Entity
open Maths
open Advent
open Place

// ── The ops' effects, performed where the walk reaches them ──

let private exposit (state: byref<GameplayState.GameplayState>) (text: string) (kind: Dialogue.Kind) : unit =
    state.Dialogue.Shown <- true
    state.Dialogue.Kind <- kind
    state.Dialogue.Text <- text

// ── Camera ops (spec 054) ──

/// How far along a waypoint's facing the camera looks, in world units. The reference's fixed distance.
let private cameraLookDistance = 3.5f

/// The direction a waypoint faces: `-UnitZ` rotated by it — the convention the viewport's arrow draws
/// (`editor/Viewport.fs`) and `newWaypointAtCamera` authors (`editor/Outliner.fs`, via `CreateWorld`), so a
/// snap looks the way the arrow points and a waypoint placed at the camera round-trips.
let private facing (rotation: Quaternion) : Vector3 =
    Vector3.Transform(-Vector3.UnitZ, rotation)

/// Point the camera at a position, looking along a waypoint's facing.
let private setCamera (state: byref<GameplayState.GameplayState>) (position: Vector3) (rotation: Quaternion) : unit =
    state.Camera.Position <- position
    state.Camera.Target <- position + facing rotation * cameraLookDistance

/// The first live waypoint named `name` in the zone; raises when there is none (FR-009). `place` names the
/// zone for the diagnostic.
let private namedWaypoint (place: Place) (zone: Zone.Zone) (name: string) : EntityRef =
    match Zone.tryWaypointByName zone name with
    | Some r -> r
    | None -> invalidOp (sprintf "cue camera: no waypoint named '%s' in '%s'" name (Place.placeName place))

/// A waypoint's position and facing. Raises if the resolved waypoint is no longer alive.
let private poseAt (zone: Zone.Zone) (ref: EntityRef) (name: string) : Vector3 * Quaternion =
    match Zone.tryEntityAt zone ref with
    | Some e -> e.Transform.Position, e.Transform.Rotation
    | None -> invalidOp (sprintf "cue camera: waypoint '%s' is not alive" name)

/// Resolve a waypoint's pose for a snap, raising when the name does not resolve (FR-009).
let private snapPose (place: Place) (zone: Zone.Zone) (name: string) : Vector3 * Quaternion =
    poseAt zone (namedWaypoint place zone name) name

/// Resolve the chain starting at `name` into positions and facings. Raises for an unknown name or a
/// chain of fewer than two waypoints (FR-010).
let private resolvedPath (place: Place) (zone: Zone.Zone) (name: string) : Vector3[] * Quaternion[] =
    let start = namedWaypoint place zone name
    let chain = Zone.waypointChain zone start
    if List.length chain < 2 then
        invalidOp (sprintf "cue camera: path '%s' has fewer than two waypoints" name)
    else

    let points = Array.zeroCreate chain.Length
    let rotations = Array.zeroCreate chain.Length
    for i in 0 .. chain.Length - 1 do
        let (position, rotation) = poseAt zone chain.[i] name
        points.[i] <- position
        rotations.[i] <- rotation
    points, rotations

/// Begin a timed op. A duration of zero (or less) is over before it begins.
let private beginWait (now: float) (seconds: float) : Cue =
    let endsAt = now + seconds
    if now >= endsAt then Fin else WaitState(seconds, endsAt)

// ── Fade ops (spec 060) ──

/// Drive the screen's fade level toward `target` along `initial`..`target` spanning `duration`, ending at
/// `endsAt`: writes `state.Fade` and answers whether the fade is over, in which case the level is exactly
/// `target`. Shared by the two ops, which differ only in their endpoint (research D4).
let private stepFade (state: byref<GameplayState.GameplayState>) (initial: float32) (target: float32) (duration: float) (endsAt: float) (now: float) : bool =
    if now >= endsAt then
        state.Fade <- target
        true
    else
        let t = (now - (endsAt - duration)) / duration
        state.Fade <- initial + float32 t * (target - initial)
        false

// ── Actor ops (spec 064) ──

/// A waypoint's pose — position and rotation — or `None` when the name does not resolve in the zone. A set
/// op is a no-op without a waypoint to place the actor at (spec 064 FR-006).
let private waypointPose (zone: Zone.Zone) (name: string) : (Vector3 * Quaternion) option =
    match Zone.tryWaypointByName zone name with
    | Some ref ->
        match Zone.tryEntityAt zone ref with
        | Some e -> Some (e.Transform.Position, e.Transform.Rotation)
        | None -> None
    | None -> None

/// A placement transform from a waypoint's pose: position and facing, scale untouched.
let private poseTransform (position: Vector3) (rotation: Quaternion) : Transform =
    { transformIdentity with Position = position; Rotation = rotation }

/// Detach the actor body at `ref` when the member's place is not the active one, so a member built into an
/// inactive place does not simulate (research D5). Only the body just built is touched: `removeBody` is safe
/// for an attached body, which is what `spawnActor`/`setTransform` always leave behind.
let private detachIfInactive (world: Physics.World) (place: Place) (active: Place) (zone: Zone.Zone) (ref: EntityRef) : unit =
    if place <> active && zone.Actors.IsAlive(handleOf ref) then
        let actor = &zone.Actors.Get(handleOf ref)
        match actor.Physics with
        | Some ph when Physics.isAdded world ph.Body -> do Physics.removeBody world ph.Body
        | _ -> ()

/// Bring `character` into `place` and place her at `transform`: the unified set (spec 064 FR-002, research
/// D3). A member already in the place is moved; one elsewhere is moved across zones keeping her own art; one
/// nowhere is spawned from the cast binding for `look`.
let private setActor
    (shared: SharedState.SharedState)
    (state: byref<GameplayState.GameplayState>)
    (place: Place)
    (character: Roster.Character)
    (look: Casting.Look)
    (transform: Transform) : unit =
    let zone = state.Zones.[place]
    match GameplayState.tryCastActor state character with
    | Some (oldPlace, oldRef) when oldPlace <> place ->
        // Move her across zones: her art is her own, so the op's look is not consulted (spec 064 FR-004).
        let oldZone = state.Zones.[oldPlace]
        let actor = Zone.captureActor oldZone oldRef
        do Zone.removeActor oldZone shared.Physics oldRef
        GameplayState.removeCast &state character
        let entity = { actor.Base with Transform = transform }
        let ref = Zone.spawnActor zone shared.Physics shared.Assets entity actor.AssetId (Some character)
        do Zone.setShowCollider zone ref actor.ShowCollider
        do detachIfInactive shared.Physics place state.ActiveZone zone ref
        GameplayState.addCast &state character place ref
    | Some (_, ref) ->
        // Already in this place: the same actor, placed (spec 064 FR-002).
        do Zone.setTransform zone shared.Physics ref transform
        do detachIfInactive shared.Physics place state.ActiveZone zone ref
    | None ->
        let slot : Casting.Slot = { Character = character; Look = look }
        match Casting.tryAsset slot shared.Cast with
        | None -> () // no art for the look: nothing (spec 064 FR-004)
        | Some assetId ->
        let entity = baseEntity (Roster.characterName character) transform
        let ref = Zone.spawnActor zone shared.Physics shared.Assets entity assetId (Some character)
        do detachIfInactive shared.Physics place state.ActiveZone zone ref
        GameplayState.addCast &state character place ref

/// Take `character` out of the scene, wherever the cast index says she is (spec 064 FR-003). A character not
/// in the scene is untouched. No place is named: the index is global and one character is at most one actor,
/// so the place is already known.
let private removeActor (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (character: Roster.Character) : unit =
    match GameplayState.tryCastActor state character with
    | None -> ()
    | Some (memberPlace, ref) ->
    do Zone.removeActor state.Zones.[memberPlace] shared.Physics ref
    GameplayState.removeCast &state character


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
    | Exposit (text, kind) ->
        exposit &state text kind
        ValueSome Fin // no progress of its own: performed here and gone
    | SetZone name ->
        // a cue names where to go; a place that is not present is a no-op, not a fault (spec 062 FR-008)
        if state.Zones.ContainsKey name then GameplayState.switchZone shared &state name else ()
        ValueSome Fin

    | AddZone name ->
        // make a declared place present and stream its registered template in, without entering it (spec 062
        // FR-002): the active place is unchanged, so add/preload/set compose into a prepared transition.
        if not (state.Zones.ContainsKey name) then
            GameplayState.presentZone shared &state name (ZoneRegistry.tryTemplate name shared.ZoneRegistry)
        ValueSome Fin

    | RemoveZone name ->
        // an absent place, or the active one, is a no-op (spec 062 FR-003, FR-007)
        if state.Zones.ContainsKey name && name <> state.ActiveZone then GameplayState.deleteZone shared &state name else ()
        ValueSome Fin

    | PreloadZone name ->
        // start a present zone's environment loading ahead of the switch (spec 062 FR-004). A zone with no
        // environment of its own but a **registered** place has its template streamed in first — the state
        // the editor's add leaves behind — so a cue can make a bare room ready. Best-effort: an ordinary
        // request, and the active zone's environment is never what makes room for it.
        if state.Zones.ContainsKey name then
            let zone = state.Zones.[name]
            match zone.Environment with
            | Some environment -> do Assets.requestEnvironment shared.Assets environment.AssetId
            | None ->
            match ZoneRegistry.tryTemplate name shared.ZoneRegistry with
            | Some template -> GameplayState.populateZone shared &state name template
            | None -> ()
        ValueSome Fin

    | SetActor (place, character, look, waypoint) ->
        // a place that is not present, or a waypoint the place does not hold, is a no-op (spec 064 FR-006)
        if state.Zones.ContainsKey place then
            let zone = state.Zones.[place]
            match waypointPose zone waypoint with
            | Some (position, rotation) -> do setActor shared &state place character look (poseTransform position rotation)
            | None -> ()
        ValueSome Fin

    | RemoveActor character ->
        do removeActor shared &state character
        ValueSome Fin

    | Wait seconds -> ValueSome(beginWait now seconds)
    | WaitState (_, endsAt) ->
        if now >= endsAt then ValueSome Fin else ValueNone // not due yet: nothing to change

    | FadeOut duration ->
        // capture the level where the walk reaches the op, so a fade from part-way is correct (research D6)
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
    | SetCamera waypoint ->
        let zone = GameplayState.activeZone state
        let (position, rotation) = snapPose state.ActiveZone zone waypoint
        do setCamera &state position rotation
        ValueSome Fin // no progress of its own: performed here and gone

    | FlyCamera (path, speed) ->
        if speed <= 0.0 then invalidOp (sprintf "cue camera: speed must be positive (was %g)" speed) else

        let zone = GameplayState.activeZone state
        let (points, rotations) = resolvedPath state.ActiveZone zone path
        let duration = pathLength points 10 / speed
        if duration <= 0.0 then
            // over before it begins: land on the last waypoint and hold no state
            do setCamera &state points.[points.Length - 1] rotations.[rotations.Length - 1]
            ValueSome Fin
        else

        do setCamera &state points.[0] rotations.[0] // cut to the path's start as the flight begins
        ValueSome(FlyCameraState(path, points, rotations, duration, now + duration))

    | FlyCameraState (_, points, rotations, duration, endsAt) ->
        if now >= endsAt then
            do setCamera &state points.[points.Length - 1] rotations.[rotations.Length - 1]
            ValueSome Fin
        else
        // only the camera moves while the flight runs: no new cue to hand back, and no allocation
        let t = (now - (endsAt - duration)) / duration
        do setCamera &state (evalSpline points t) (evalRotation rotations t)
        ValueNone

    | AddAdvent advent ->
        shared.Advents <- Set.add advent shared.Advents
        ValueSome Fin // no progress of its own: performed here and gone

    | RemoveAdvent advent ->
        shared.Advents <- Set.remove advent shared.Advents
        ValueSome Fin

    | Await advent ->
        if Set.contains advent shared.Advents then ValueSome Fin // already satisfied: over before it begins
        else ValueSome(AwaitState advent) // begun: the walk and the panel can now see it is the running op

    | AwaitState advent ->
        if Set.contains advent shared.Advents then ValueSome Fin else ValueNone // still waiting: nothing changed

    | If (advent, thenCue, elseCue) ->
        // the flag is read where the op is reached, and the chosen branch runs in the `If`'s place
        advance shared &state now (if Set.contains advent shared.Advents then thenCue else elseCue)

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
