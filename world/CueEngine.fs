module CueEngine

// The cue engine (spec 046). `advance` is the whole of it — one cue in, the cue as it now stands out — and
// `tick` writes that answer back, so setting the cue *is* the beginning and there is no start call.
// `forkInto`, which runs an arriving cue beside the running one, lives beside the type in `world/Cue.fs` so the
// trigger system — compiled before this engine — can fork without waiting for it (spec 058).
//
// Compiled *after* `state/GameplayState.fs` — the project's one deliberate ordering inversion — because
// each op performs its effect on the gameplay state where the walk reaches it: an `Exposit` writes the
// screen's dialogue, a `SetZone` reuses `GameplayState.switchZone` (which is why the walk carries
// `shared` for the switch's asset request), the camera ops write the camera from a named waypoint, and the advent ops
// read and write `SharedState.Advents` (research D3).

open System.Numerics
open Prime
open Cue
open Entity
open Maths
open SlotMap

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

/// Resolve the pose a camera op should snap to, or the reason it cannot: an absent place, a waypoint
/// name that does not resolve, or a waypoint no longer alive. A missing target is a no-op, not a fault
/// (spec 080, US2): the op performs nothing and reports the reason, as every other op does.
let private trySnapPose (state: byref<GameplayState.GameplayState>) (place: string) (waypoint: string) : Result<Vector3 * Quaternion, string> =
    if not (state.Zones.ContainsKey place) then Error (sprintf "place '%s' is not present" place) else

    let zone = state.Zones.[place]
    match Zone.tryWaypointByName zone waypoint with
    | None -> Error (sprintf "no waypoint named '%s' in '%s'" waypoint place)
    | Some ref ->
    match Zone.tryEntityAt zone ref with
    | Some e -> Ok (e.Transform.Position, e.Transform.Rotation)
    | None -> Error (sprintf "waypoint '%s' is not alive" waypoint)

/// Resolve a flight path into positions and facings, or the reason it cannot run: an absent place, an
/// unknown start waypoint, a chain of fewer than two waypoints, or a waypoint no longer alive.
let private tryResolvedPath (state: byref<GameplayState.GameplayState>) (place: string) (name: string) : Result<Vector3[] * Quaternion[], string> =
    if not (state.Zones.ContainsKey place) then Error (sprintf "place '%s' is not present" place) else

    let zone = state.Zones.[place]
    match Zone.tryWaypointByName zone name with
    | None -> Error (sprintf "no waypoint named '%s' in '%s'" name place)
    | Some start ->
    let chain = Zone.waypointChain zone start
    if List.length chain < 2 then Error (sprintf "path '%s' has fewer than two waypoints" name) else

    let points = Array.zeroCreate chain.Length
    let rotations = Array.zeroCreate chain.Length
    let mutable alive = true
    for i in 0 .. chain.Length - 1 do
        if alive then
            match Zone.tryEntityAt zone chain.[i] with
            | Some e ->
                points.[i] <- e.Transform.Position
                rotations.[i] <- e.Transform.Rotation
            | None -> alive <- false
    if alive then Ok (points, rotations) else Error (sprintf "a waypoint in path '%s' is not alive" name)

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

/// Say why a cue op performed nothing, on stderr (the `Spawning` warning convention). These ops are deliberately
/// forgiving — a no-op is the right outcome for state the op cannot act on — but a silent one cannot be told from
/// a working op in the editor, so the reason is named. Called once, where the op begins.
let private noOp (op: string) (actor: string) (reason: string) : unit =
    Diagnostics.reportf "cue: %s(%s) performs nothing: %s" op actor reason

/// The non-actor counterpart of `noOp` (spec 067): a trigger toggle names a place and a trigger, not an actor.
let private noOpTrigger (op: string) (place: string) (trigger: string) (reason: string) : unit =
    Diagnostics.reportf "cue: %s(%s/%s) performs nothing: %s" op (place) trigger reason

/// The place-targeted counterpart of `noOp` (spec 080): a camera op names a place and a waypoint or a path.
let private noOpPlace (op: string) (place: string) (reason: string) : unit =
    Diagnostics.reportf "cue: %s(%s) performs nothing: %s" op (place) reason

/// Detach the actor body at `ref` when the member's place is not the active one, so a member built into an
/// inactive place does not simulate (research D5). Only the body just built is touched: `removeBody` is safe
/// for an attached body, which is what `spawnActor`/`setTransform` always leave behind.
let private detachIfInactive (world: Physics.World) (place: string) (active: string) (zone: Zone.Zone) (ref: EntityRef) : unit =
    if place <> active && zone.Actors.IsAlive(handleOf ref) then
        let actor = &zone.Actors.Get(handleOf ref)
        match actor.Physics with
        | Some ph when Physics.isAdded world ph.Body -> do Physics.removeBody world ph.Body
        | _ -> ()

/// The live actor named `actor` in `place`'s zone: `None` when the place is not present or holds no such
/// actor. Every actor op names its zone explicitly now (spec 075), so there is no scan of the present places.
let private tryActorIn (state: byref<GameplayState.GameplayState>) (place: string) (actor: string) : (Zone.Zone * EntityRef) option =
    match state.Zones.TryGetValue place with
    | false, _ -> None
    | true, zone ->

    Zone.tryActorByName zone actor
    |> Option.map (fun ref -> zone, ref)


/// Bring an actor named `actor` into `place`, placed at `transform` and drawn from the discovered actor asset
/// `asset` (spec 073 FR-003). It never looks for an existing actor: a name is a label, so moving a member is a
/// removal followed by an add. An `asset` that names no discovered actor asset performs nothing.
let private addActor
    (shared: SharedState.SharedState)
    (state: byref<GameplayState.GameplayState>)
    (place: string)
    (actor: string)
    (asset: string)
    (transform: Transform) : unit =
    if not (shared.Assets.Actors.ContainsKey asset) then
        do noOp "AddActor" actor (sprintf "no actor asset '%s'" asset)
    else

    let zone = state.Zones.[place]
    if zone.Actors.Count >= zone.Actors.Capacity then
        do noOp "AddActor" actor (sprintf "the place '%s' is at capacity" place)
    else

    let entity = baseEntity actor transform
    let ref = Zone.spawnActor zone state.Physics shared.Assets entity asset
    do detachIfInactive state.Physics place state.ActiveZone zone ref

/// Take the actor named `actor` out of `place` (spec 075). An actor not in that place's zone is untouched.
let private removeActor (state: byref<GameplayState.GameplayState>) (place: string) (actor: string) : unit =
    match tryActorIn &state place actor with
    | None -> do noOp "RemoveActor" actor (sprintf "she is not in '%s'" (place))
    | Some (zone, ref) -> do Zone.removeActor zone state.Physics ref

/// Arm or disarm a named trigger in a named place (spec 067): resolve the place (absent is a no-op), the
/// trigger (`Zone.tryTriggerByName`; a miss is a no-op) and read it; a flag that already holds is a no-op,
/// else it is written. The trigger's derived runtime state is left to the next `Systems.updateTriggers`.
let private setTriggerEnabled (state: byref<GameplayState.GameplayState>) (place: string) (trigger: string) (enabled: bool) : unit =
    let op = if enabled then "EnableTrigger" else "DisableTrigger"
    if not (state.Zones.ContainsKey place) then
        do noOpTrigger op place trigger (sprintf "place '%s' is not present" (place))
    else

    let zone = state.Zones.[place]
    match Zone.tryTriggerByName zone trigger with
    | None -> do noOpTrigger op place trigger (sprintf "no trigger named '%s' in '%s'" trigger (place))
    | Some ref ->
    let trigger' = Zone.captureTrigger zone ref
    if trigger'.Base.Enabled = enabled then
        do noOpTrigger op place trigger (if enabled then "it is already enabled" else "it is already disabled")
    else
    do Zone.setTrigger zone ref { trigger' with Base = { trigger'.Base with Enabled = enabled } }

// ── Actor blends and motion (spec 066) ──

/// The zone a `…State` actor op's actor is in, or none when the op can no longer act: her place is gone, her slot
/// is dead or recycled, or the slot now holds a different name. O(1) — the place and handle were captured at
/// `begin*`, so a running move/turn/blend never scans the scene by name (`specs/074`).
let private tryLiveZone (state: byref<GameplayState.GameplayState>) (name: string) (place: string) (handle: Handle) : Zone.Zone voption =
    if not (state.Zones.ContainsKey place) then ValueNone
    else

    let zone = state.Zones.[place]
    if not (zone.Actors.IsAlive handle) then ValueNone
    else

    let live = &zone.Actors.Get handle
    if live.State.Base.Name <> name then ValueNone
    else ValueSome zone

/// The facing an actor takes to look where a waypoint's arrow points. An actor's forward is its local `+Z`
/// (`Systems.forwardOf`, and the model matrix `ActorRender` reads), while a waypoint's arrow is its own `-Z`
/// (`editor/Viewport.fs`), so the waypoint's rotation is turned about Y to match. Shared by every op that points
/// an actor at a waypoint (`AddActor`, `MoveActor`, `RotateActor`) so they agree.
let private actorFacing (waypointRotation: Quaternion) : Quaternion =
    let arrow = Vector3.Transform(-Vector3.UnitZ, waypointRotation)
    let yaw = System.MathF.Atan2(arrow.X, arrow.Z)
    Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw)



/// Write a blend's weights at `t`. `Animate`/`Morph` (`fromName = toName`) move one slot from `initialFrom`
/// toward `target`; a cross-fade moves `fromName` toward 0 while `toName` moves toward `target`. A slot the
/// actor does not yet hold is added at the written weight. Shared by every tick of every blend.
let private writeBlend (renderer: byref<ActorRender.ActorRender>) (morph: bool) (fromName: string) (toName: string) (initialFrom: float32) (initialTo: float32) (target: float32) (t: float32) : unit =
    if fromName = toName then
        let weight = initialFrom + (target - initialFrom) * t
        if morph then ActorRender.setMorphWeight &renderer fromName weight else ActorRender.setClipWeight &renderer fromName weight
    else
        let outgoing = initialFrom * (1.0f - t)
        let incoming = initialTo + (target - initialTo) * t
        if morph then
            do ActorRender.setMorphWeight &renderer fromName outgoing
            do ActorRender.setMorphWeight &renderer toName incoming
        else
            do ActorRender.setClipWeight &renderer fromName outgoing
            do ActorRender.setClipWeight &renderer toName incoming

/// Begin a blend: capture the starting weights of the slots the op names and hold a `BlendState`. `Fin` at once
/// when the actor is not in the scene, or her asset does not define a named clip/morph — a blend no-ops, so a
/// cue re-runs safely. A duration of zero (or less) is over before it begins, landing on the endpoints.
let private beginBlend (state: byref<GameplayState.GameplayState>) (place: string) (actor: string) (morph: bool) (fromName: string) (toName: string) (target: float32) (duration: float) (now: float) : Cue =
    let opName = if fromName = toName then (if morph then "Morph" else "Animate") else (if morph then "CrossMorph" else "CrossFade")
    let no reason =
        do noOp opName actor reason
        Fin
    match tryActorIn &state place actor with
    | None -> no (sprintf "she is not in '%s'" (place))
    | Some (zone, ref) ->
    let live = &zone.Actors.Get(handleOf ref)
    let initialFrom =
        if morph then ActorRender.tryMorphWeight &live.Renderer fromName
        else ActorRender.tryClipWeight &live.Renderer fromName
    let initialTo =
        if morph then ActorRender.tryMorphWeight &live.Renderer toName
        else ActorRender.tryClipWeight &live.Renderer toName
    match initialFrom, initialTo with
    | Some from, Some to' ->
        let endsAt = now + duration
        if now >= endsAt then
            do writeBlend &live.Renderer morph fromName toName from to' target 1.0f
            Fin
        else BlendState(actor, place, handleOf ref, morph, fromName, toName, from, to', target, duration, endsAt)
    | None, _ -> no (sprintf "her asset has no %s '%s'" (if morph then "morph" else "clip") fromName)
    | _, None -> no (sprintf "her asset has no %s '%s'" (if morph then "morph" else "clip") toName)

/// Step a blend at `now`, writing toward the target and answering whether it is over (exactly on the endpoints
/// at the deadline). An actor that has left the scene ends the blend.
let private stepBlend (state: byref<GameplayState.GameplayState>) (actor: string) (place: string) (handle: Handle) (morph: bool) (fromName: string) (toName: string) (initialFrom: float32) (initialTo: float32) (target: float32) (duration: float) (endsAt: float) (now: float) : bool =
    match tryLiveZone &state actor place handle with
    | ValueNone -> true
    | ValueSome zone ->
    let live = &zone.Actors.Get handle
    if now >= endsAt then
        do writeBlend &live.Renderer morph fromName toName initialFrom initialTo target 1.0f
        true
    else
        let t = float32 ((now - (endsAt - duration)) / duration)
        do writeBlend &live.Renderer morph fromName toName initialFrom initialTo target t
        false

/// Begin a move: resolve the actor (her zone must be the **active** one — motion is active-zone only), her body
/// and the named path (a single waypoint is a straight walk to it; positive speed). Build the spline from her
/// current position through the chain, preset the moving clip, and hold a `MoveActorState`. Anything unresolved
/// is `Fin`. The asset must define both clips, as an unknown clip name no-ops.
let private beginMove (state: byref<GameplayState.GameplayState>) (actor: string) (path: string) (speed: float) (movingClip: string) (idleClip: string) (now: float) : Cue =
    let no reason =
        do noOp "MoveActor" actor reason
        Fin
    let place = state.ActiveZone // motion is physics-driven, so it acts only in the active zone (spec 075)
    if speed <= 0.0 then no (sprintf "speed %g is not positive" speed) else
    match tryActorIn &state place actor with
    | None -> no "she is not in the active zone"
    | Some (zone, ref) ->
    let live = &zone.Actors.Get(handleOf ref)
    match live.Physics with
    | None -> no "she has no physics body"
    | Some _ ->
    let moving = ActorRender.tryClipWeight &live.Renderer movingClip
    let idle = ActorRender.tryClipWeight &live.Renderer idleClip
    if moving.IsNone then no (sprintf "her asset has no clip '%s'" movingClip) else
    if idle.IsNone then no (sprintf "her asset has no clip '%s'" idleClip) else
    match Zone.tryWaypointByName zone path with
    | None -> no (sprintf "no waypoint named '%s' in '%s'" path (place))
    | Some start ->
    // A path is the chain from the named waypoint; a single waypoint is a straight walk to it (her own position
    // is the spline's start), so there is no "fewer than two" rule — only a path with no length no-ops.
    let chain = Zone.waypointChain zone start
    let chainPositions = Array.zeroCreate chain.Length
    let mutable finalRotation = live.State.Base.Transform.Rotation
    let mutable resolved = true
    for i in 0 .. chain.Length - 1 do
        match Zone.tryEntityAt zone chain.[i] with
        | Some entity ->
            chainPositions.[i] <- entity.Transform.Position
            finalRotation <- actorFacing entity.Transform.Rotation
        | None -> resolved <- false
    if not resolved then no "a waypoint on the path is not alive" else
    let initialRotation = live.State.Base.Transform.Rotation
    let actorPosition = live.State.Base.Transform.Position
    // Walk from where she stands: prepend her position unless it is already the chain's start. A duplicated
    // point gives the spline a degenerate first segment (its endpoint tangent bends the curve backwards), so
    // the walk would stall; skipping it makes "already at the start" walk the chain itself.
    let points =
        if Vector3.DistanceSquared(actorPosition, chainPositions.[0]) < 1e-4f then chainPositions
        else Array.append [| actorPosition |] chainPositions
    if points.Length < 2 then no "the path has no length (she already stands at its only waypoint)" else
    let duration = pathLength points 10 / speed
    if duration <= 0.0 then no "the path has no length" else
    let blend = min 0.25 (duration * 0.3)
    // She slerps from her own facing to the path's end facing — the last waypoint's arrow read as an actor
    // facing — so a waypoint the author turned to face the path leaves her facing it.
    do ActorRender.setClipWeight &live.Renderer movingClip 1.0f
    do ActorRender.setClipWeight &live.Renderer idleClip 0.0f
    MoveActorState(actor, place, handleOf ref, points, speed, duration, now + duration, initialRotation, finalRotation, blend, movingClip, idleClip)

/// Step a move at `now`: physics-drive the body toward the spline point for this instant (horizontal velocity
/// at `speed`, vertical preserved so gravity still acts), slerp her facing from her own to the path's end facing,
/// and blend the moving clip down to idle over the final `blend` seconds. Answers whether it is over; at the
/// deadline she stops where she is — the last waypoint when unobstructed. An actor that has left the scene, or
/// whose zone is no longer active, ends it.
let private stepMove (state: byref<GameplayState.GameplayState>) (actor: string) (place: string) (handle: Handle) (points: Vector3[]) (speed: float) (duration: float) (endsAt: float) (initialRotation: Quaternion) (finalRotation: Quaternion) (blend: float) (movingClip: string) (idleClip: string) (now: float) : bool =
    match tryLiveZone &state actor place handle with
    | ValueNone -> true
    | ValueSome zone ->
    if place <> state.ActiveZone then true else
    let live = &zone.Actors.Get handle
    match live.Physics with
    | None -> true
    | Some attachment ->
    let body = attachment.Body
    let vertical = (Physics.getBodyVelocity state.Physics body).Y
    let offset = Vector3(0.0f, attachment.Collider.HalfHeight + attachment.Collider.Radius, 0.0f)
    if now >= endsAt then
        do Physics.setBodyVelocity state.Physics body (Vector3(0.0f, vertical, 0.0f))
        do Physics.setBodyRotation state.Physics body finalRotation
        do ActorRender.setClipWeight &live.Renderer movingClip 0.0f
        do ActorRender.setClipWeight &live.Renderer idleClip 1.0f
        true
    else
        let t = (now - (endsAt - duration)) / duration
        let struct (bodyPosition, _) = Physics.getBodyTransform state.Physics body
        let target = evalSpline points t + offset
        let toward = Vector3(target.X - bodyPosition.X, 0.0f, target.Z - bodyPosition.Z)
        let velocity =
            if toward.LengthSquared() < 1e-6f then Vector3(0.0f, vertical, 0.0f) else
            let v = Vector3.Normalize toward * float32 speed
            Vector3(v.X, vertical, v.Z)
        do Physics.setBodyVelocity state.Physics body velocity
        do Physics.setBodyRotation state.Physics body (Quaternion.Slerp(initialRotation, finalRotation, float32 t))
        let remaining = endsAt - now
        let arrival = if blend <= 0.0 then 0.0f else float32 (max 0.0 (1.0 - remaining / blend))
        do ActorRender.setClipWeight &live.Renderer movingClip (1.0f - arrival)
        do ActorRender.setClipWeight &live.Renderer idleClip arrival
        false

/// Begin an in-place turn: resolve the actor (her zone must be active) and the named waypoint, capture her
/// current facing and the waypoint's, and hold a `RotateActorState`. Anything unresolved is `Fin`.
let private beginRotate (state: byref<GameplayState.GameplayState>) (actor: string) (waypoint: string) (duration: float) (now: float) : Cue =
    let no reason =
        do noOp "RotateActor" actor reason
        Fin
    let place = state.ActiveZone // motion is physics-driven, so it acts only in the active zone (spec 075)
    match tryActorIn &state place actor with
    | None -> no "she is not in the active zone"
    | Some (zone, ref) ->
    let live = &zone.Actors.Get(handleOf ref)
    match live.Physics with
    | None -> no "she has no physics body"
    | Some attachment ->
    match waypointPose zone waypoint with
    | None -> no (sprintf "no waypoint named '%s' in '%s'" waypoint (place))
    | Some (_, targetRotation) ->
    let target = actorFacing targetRotation
    let initialRotation = live.State.Base.Transform.Rotation
    let endsAt = now + duration
    if now >= endsAt then
        do Physics.setBodyRotation state.Physics attachment.Body target
        Fin
    else RotateActorState(actor, place, handleOf ref, initialRotation, target, duration, endsAt)

/// Step an in-place turn at `now`, writing the body's rotation and answering whether it is over (exactly on the
/// waypoint's facing at the deadline). An actor that has left the scene, or whose zone is no longer active, ends
/// it.
let private stepRotate (state: byref<GameplayState.GameplayState>) (actor: string) (place: string) (handle: Handle) (initialRotation: Quaternion) (targetRotation: Quaternion) (duration: float) (endsAt: float) (now: float) : bool =
    match tryLiveZone &state actor place handle with
    | ValueNone -> true
    | ValueSome zone ->
    if place <> state.ActiveZone then true else
    let live = &zone.Actors.Get handle
    match live.Physics with
    | None -> true
    | Some attachment ->
    if now >= endsAt then
        do Physics.setBodyRotation state.Physics attachment.Body targetRotation
        true
    else
    let t = float32 ((now - (endsAt - duration)) / duration)
    do Physics.setBodyRotation state.Physics attachment.Body (Quaternion.Slerp(initialRotation, targetRotation, t))
    false


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
    | Print text ->
        printfn "cue: %s" text
        ValueSome Fin // no progress of its own: performed here and gone
    | SetZone name ->
        // a cue names where to go; a place that is not present is a no-op, not a fault (spec 062 FR-008)
        if state.Zones.ContainsKey name then GameplayState.switchZone shared &state name else ()
        ValueSome Fin

    | RemoveZone name ->
        // an absent zone, or the active one, is a no-op (spec 062 FR-003, FR-007)
        if state.Zones.ContainsKey name && name <> state.ActiveZone then GameplayState.deleteZone shared &state name else ()
        ValueSome Fin

    | ForkScenario scenario ->
        // materialise a registered scenario into the scene and run its cue beside the rest (spec 078). It is a
        // runtime effect, not a history step: the reversal `materialise` returns is discarded. An unregistered
        // scenario, or one whose target zone lacks room, performs nothing and reports why.
        match ScenarioRegistry.tryContents scenario state.Scenarios with
        | None ->
            do noOp "ForkScenario" scenario "it is not registered"
            ValueSome Fin
        | Some contents ->
        match ScenarioFork.materialise shared &state contents with
        | None -> ValueSome Fin // no capacity in a target zone: logged already, nothing forked (FR-010)
        | Some _ -> ValueSome (Fork contents.Cue)

    | AddActor (place, actor, asset, waypoint) ->
        // a place that is not present, or a waypoint the place does not hold, is a no-op (spec 073 FR-003)
        if not (state.Zones.ContainsKey place) then
            do noOp "AddActor" actor (sprintf "place '%s' is not present" (place))
            ValueSome Fin
        else
        let zone = state.Zones.[place]
        match waypointPose zone waypoint with
        | Some (position, rotation) -> do addActor shared &state place actor asset (poseTransform position (actorFacing rotation))
        | None -> do noOp "AddActor" actor (sprintf "no waypoint named '%s' in '%s'" waypoint (place))
        ValueSome Fin

    | RemoveActor (place, actor) ->
        do removeActor &state place actor
        ValueSome Fin

    | EnableTrigger (place, trigger) ->
        do setTriggerEnabled &state place trigger true
        ValueSome Fin

    | DisableTrigger (place, trigger) ->
        do setTriggerEnabled &state place trigger false
        ValueSome Fin

    | Animate (place, actor, clip, weight, duration) ->
        ValueSome(beginBlend &state place actor false clip clip weight duration now)

    | CrossFade (place, actor, fromClip, toClip, weight, duration) ->
        ValueSome(beginBlend &state place actor false fromClip toClip weight duration now)

    | Morph (place, actor, morph, weight, duration) ->
        ValueSome(beginBlend &state place actor true morph morph weight duration now)

    | CrossMorph (place, actor, fromMorph, toMorph, weight, duration) ->
        ValueSome(beginBlend &state place actor true fromMorph toMorph weight duration now)

    | BlendState (actor, place, handle, morph, fromName, toName, initialFrom, initialTo, target, duration, endsAt) ->
        if stepBlend &state actor place handle morph fromName toName initialFrom initialTo target duration endsAt now
        then ValueSome Fin
        else ValueNone

    | MoveActor (actor, path, speed, movingClip, idleClip) ->
        ValueSome(beginMove &state actor path speed movingClip idleClip now)

    | MoveActorState (actor, place, handle, points, speed, duration, endsAt, initialRotation, finalRotation, blend, movingClip, idleClip) ->
        if stepMove &state actor place handle points speed duration endsAt initialRotation finalRotation blend movingClip idleClip now
        then ValueSome Fin
        else ValueNone

    | RotateActor (actor, waypoint, duration) ->
        ValueSome(beginRotate &state actor waypoint duration now)

    | RotateActorState (actor, place, handle, initialRotation, targetRotation, duration, endsAt) ->
        if stepRotate &state actor place handle initialRotation targetRotation duration endsAt now
        then ValueSome Fin
        else ValueNone

    | EnablePlayer (place, actor) ->
        // the runtime counterpart of the editor's "Set as Player": bind her as the named place's player
        match tryActorIn &state place actor with
        | Some (zone, ref) -> zone.Player <- ValueSome (Systems.playerHandleOf ref)
        | None -> do noOp "EnablePlayer" actor (sprintf "she is not in '%s'" (place))
        ValueSome Fin

    | DisablePlayer (place, actor) ->
        match tryActorIn &state place actor with
        | Some (zone, ref) ->
            let handle = Systems.playerHandleOf ref
            match zone.Player with
            | ValueSome current when current = handle -> zone.Player <- ValueNone
            | _ -> do noOp "DisablePlayer" actor "she is not the player"
        | None -> do noOp "DisablePlayer" actor (sprintf "she is not in '%s'" (place))
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
    | SetCamera (place, waypoint) ->
        match trySnapPose &state place waypoint with
        | Ok (position, rotation) -> do setCamera &state position rotation
        | Error reason -> do noOpPlace "SetCamera" place reason
        ValueSome Fin // no progress of its own: performed here and gone

    | FlyCamera (place, path, speed) ->
        if speed <= 0.0 then
            do noOpPlace "FlyCamera" place (sprintf "speed must be positive (was %g)" speed)
            ValueSome Fin
        else
        match tryResolvedPath &state place path with
        | Error reason ->
            do noOpPlace "FlyCamera" place reason
            ValueSome Fin
        | Ok (points, rotations) ->

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
        shared.Advents <- Advent.add advent shared.Advents
        ValueSome Fin // no progress of its own: performed here and gone

    | RemoveAdvent advent ->
        shared.Advents <- Advent.remove advent shared.Advents
        ValueSome Fin

    | Await advent ->
        if Advent.contains advent shared.Advents then ValueSome Fin // already satisfied: over before it begins
        else ValueSome(AwaitState advent) // begun: the walk and the panel can now see it is the running op

    | AwaitState advent ->
        if Advent.contains advent shared.Advents then ValueSome Fin else ValueNone // still waiting: nothing changed

    | If (advent, thenCue, elseCue) ->
        // the flag is read where the op is reached, and the chosen branch runs in the `If`'s place
        advance shared &state now (if Advent.contains advent shared.Advents then thenCue else elseCue)

    | Fork inner ->
        // A `Fork` with nothing beside it runs its inner cue (spec 080, US2). The `ForkScenario` op returns
        // `Fork cue`; when it is not at a sequence front this is where that fork runs. It mirrors
        // `stepSequenceNext`'s own empty-remainder case.
        advance shared &state now inner
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
