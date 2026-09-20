module Systems

// Play-mode player systems. Setting the player records only a `SlotMap.Handle` into
// `Zone.Actors`; play/edit simply includes or excludes these systems (there are no
// enter/leave callbacks). Each system resolves the player itself with `isPlayerLive`,
// so a missing or stale player is simply a no-op. The player's motion lives on the
// actor's existing dynamic capsule body, so there is no separate character or
// locomotion copy.

open System
open System.Numerics
open Entity
open SlotMap


// ── Tuning constants ──

let jogSpeed = 3.5f
let turnRate = 2.5f

// ── Camera-follow state ──

// The following view's parameters. `Yaw` mirrors the player's facing each frame;
// the rest are fixed for this phase.
[<Struct>]
type CameraFollow =
    { mutable Yaw : float32
      Pitch : float32    // elevation above the pivot, radians
      Distance : float32 // offset back from the player
      Height : float32 } // pivot above the player's feet

let createFollow () : CameraFollow =
    { Yaw = 0.0f; Pitch = 0.35f; Distance = 4.0f; Height = 1.7f }

// ── Helpers ──

// Facing for a yaw about world +Y (`UnitZ` rotated about `UnitY`).
let private forwardOf (yaw: float32) : Vector3 =
    Vector3(MathF.Sin yaw, 0.0f, MathF.Cos yaw)

// The yaw about world +Y of a rotation.
let private yawOf (q: Quaternion) : float32 =
    let f = Vector3.Transform(Vector3.UnitZ, q)
    MathF.Atan2(f.X, f.Z)

// An editor selection as a player handle; a non-actor is a bug, not a silent no-op.
let playerHandleOf (ref: EntityRef) : Handle =
    if ref.Kind <> EntityKind.Actor then
        failwithf "Set as Player: %A is not an actor" ref.Kind
    { Index = ref.Index; Generation = ref.Generation }

// Whether the bound player resolves to a live actor. Each system uses this to decide
// whether it has anything to do.
let isPlayerLive (zone: Zone.Zone) : bool =
    match zone.Player with
    | ValueSome handle -> zone.Actors.IsAlive handle
    | _ -> false

// ── Systems ──

// Whether the player is inside a trigger volume: within the radius horizontally, and
// between the base and the top vertically, inclusively at every boundary. The volume is
// radially symmetric, so the trigger's rotation and scale are not consulted.
let private contains (trigger: Trigger) (p: Vector3) : bool =
    let basePosition = trigger.Base.Transform.Position
    let dx = p.X - basePosition.X
    let dz = p.Z - basePosition.Z
    dx * dx + dz * dz <= trigger.Radius * trigger.Radius
    && p.Y >= basePosition.Y
    && p.Y <= basePosition.Y + trigger.Height

// Detect the player in the active zone's triggers: write each trigger's `Triggered` flag
// in place, and answer the nearest enabled interactable trigger the player is inside —
// what an interact prompt is shown for, and what an interaction will act on once a
// trigger can run a cue. A disabled trigger is never inside, and with no live designated
// player every trigger is cleared and the answer is ValueNone. Reads the player's entity
// position (already synced from its body this frame), so it needs no physics world and
// no device. Both the position and the answer are `voption`s: this runs every play frame,
// and `Some` on a struct would box. Reads and writes each payload through the slot's own
// byref, as `syncActors` does.
let updateTriggers (zone: Zone.Zone) : EntityRef voption =
    let playerPosition =
        match zone.Player with
        | ValueSome handle when zone.Actors.IsAlive handle ->
            let actor = &zone.Actors.Get handle
            ValueSome actor.Base.Transform.Position
        | _ -> ValueNone

    let mutable closest : EntityRef voption = ValueNone
    let mutable closestDistance = Single.MaxValue

    let slots = zone.Triggers.Slots
    for i in 0 .. slots.Length - 1 do
        let slot = &slots.[i]
        if not slot.Alive then () else
        let index = uint32 i
        let generation = slot.Generation
        let trigger = &slot.Value
        match playerPosition with
        | ValueSome p when trigger.Base.Enabled && contains trigger p ->
            trigger.Triggered <- true

            // The nearest interactable one wins; distance is measured to the volume's centre.
            if trigger.Interactable then
                let centre = trigger.Base.Transform.Position + Vector3(0.0f, trigger.Height * 0.5f, 0.0f)
                let distance = (p - centre).LengthSquared()
                if distance < closestDistance then
                    closestDistance <- distance
                    closest <- ValueSome (triggerRef index generation)
        | _ -> trigger.Triggered <- false

    closest

// Tank controls: A/D turn the facing and write the body's rotation; W/S write the
// body's horizontal velocity along the facing, preserving its vertical velocity so
// gravity still accumulates.
let playerInput (world: Physics.World) (zone: Zone.Zone) (input: Input.Input) (dt: float32) : unit =
    if not (isPlayerLive zone) then () else

    let actor = &zone.Actors.Get zone.Player.Value
    match actor.Physics with
    | None -> ()
    | Some attachment ->
    let turn =
        (if Input.isHeld Input.Key.A input then 1.0f else 0.0f)
        - (if Input.isHeld Input.Key.D input then 1.0f else 0.0f)
    let yaw = yawOf actor.Base.Transform.Rotation + turn * turnRate * dt
    let drive =
        (if Input.isHeld Input.Key.W input then 1.0f else 0.0f)
        - (if Input.isHeld Input.Key.S input then 1.0f else 0.0f)
    let forward = forwardOf yaw * (drive * jogSpeed)
    let vertical = (Physics.getBodyVelocity world attachment.Body).Y
    do Physics.setBodyVelocity world attachment.Body (Vector3(forward.X, vertical, forward.Z))
    do Physics.setBodyRotation world attachment.Body (Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw))


// Copy every actor's resolved body transform back onto its entity in place (the body
// is the source of truth while playing). The entity position is the feet, so the
// capsule centre is shifted down by the collider offset; only those two components are
// written, and the payload is reached through the slot's own byref rather than copied,
// so the hot path stays copy-free. The player is just one of these actors — a dynamic
// body that is not synced renders where it was placed while its collider moves
// elsewhere.
let syncActors (world: Physics.World) (zone: Zone.Zone) : unit =
    let slots = zone.Actors.Slots
    for i in 0 .. slots.Length - 1 do
        let slot = &slots.[i]
        if not slot.Alive then () else
        let actor = &slot.Value
        match actor.Physics with
        | None -> ()
        | Some attachment ->
        let offset = Vector3(0.0f, attachment.Collider.HalfHeight + attachment.Collider.Radius, 0.0f)
        let (position, rotation) = Physics.getBodyTransform world attachment.Body
        actor.Base.Transform.Position <- position - offset
        actor.Base.Transform.Rotation <- rotation


// Third-person camera position and target for a player position and facing. Pure.
let cameraPose (position: Vector3) (yaw: float32) (follow: CameraFollow) : Vector3 * Vector3 =
    let pivot = position + Vector3(0.0f, follow.Height, 0.0f)
    let forward = forwardOf yaw
    let cosPitch = MathF.Cos follow.Pitch
    let sinPitch = MathF.Sin follow.Pitch
    let offset =
        Vector3(-forward.X * follow.Distance * cosPitch, follow.Distance * sinPitch, -forward.Z * follow.Distance * cosPitch)
    pivot + offset, pivot

// Place the camera behind and above the player from the actor's facing.
let cameraFollow (zone: Zone.Zone) (follow: byref<CameraFollow>) (camera: byref<Camera.Camera>) : unit =
    if not (isPlayerLive zone) then () else

    let actor = &zone.Actors.Get zone.Player.Value
    let yaw = yawOf actor.Base.Transform.Rotation
    follow.Yaw <- yaw
    let (cameraPosition, target) = cameraPose actor.Base.Transform.Position yaw follow
    camera.Position <- cameraPosition
    camera.Target <- target

// The locomotion clip for the held movement keys: "Jog" while any of W/A/S/D is
// held, otherwise "Idle". Pure.
let locomotionClip (input: Input.Input) : string =
    if Input.isHeld Input.Key.W input || Input.isHeld Input.Key.A input
       || Input.isHeld Input.Key.S input || Input.isHeld Input.Key.D input then
        "Jog"
    else
        "Idle"

// Switch the player actor's animation to match the held movement keys.
let animate (zone: Zone.Zone) (input: Input.Input) : unit =
    if not (isPlayerLive zone) then () else

    let actor = &zone.Actors.Get zone.Player.Value
    do ActorRender.playClip &actor.Renderer (locomotionClip input)
