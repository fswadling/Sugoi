module PlayerTests

// Player-system helpers: the pure functions (handle conversion, player resolution,
// clip choice, camera placement) and the body→entity sync (which needs a physics body
// but no GPU). The systems that drive an actor body take it by reference and need a
// GPU-backed actor, so they are covered by the quickstart scenarios.

open Xunit
open System
open System.Numerics
open Entity
open SlotMap
open Systems

// A frame's input with only the held-key mask set.
let private input (held: Input.KeyMask) : Input.Input =
    { MouseX = 0.0f
      MouseY = 0.0f
      MouseDX = 0.0f
      MouseDY = 0.0f
      LeftDown = false
      LeftPressed = false
      LeftReleased = false
      RightDown = false
      Held = held
      Pressed = Input.KeyMask.zero }

[<Fact>]
let ``playerHandleOf returns the actor's slot handle`` () =
    let handle = playerHandleOf (actorRef 3u 7u)
    Assert.Equal(3u, handle.Index)
    Assert.Equal(7u, handle.Generation)

[<Fact>]
let ``playerHandleOf rejects a non-actor`` () =
    Assert.Throws<Exception>(fun () -> playerHandleOf (propRef 0u 0u) |> ignore) |> ignore

[<Fact>]
let ``locomotionClip jogs while a movement key is held`` () =
    for key in [ Input.Key.W; Input.Key.A; Input.Key.S; Input.Key.D ] do
        Assert.Equal("Jog", locomotionClip (input (Input.Key.mask key)))

[<Fact>]
let ``locomotionClip is idle with no movement key held`` () =
    Assert.Equal("Idle", locomotionClip (input Input.KeyMask.zero))

[<Fact>]
let ``cameraPose sits behind and above the player`` () =
    let follow = createFollow ()
    let (cameraPosition, target) = cameraPose Vector3.Zero 0.0f follow
    // Yaw 0 faces +Z, so the camera is behind (-Z) and above (+Y).
    Assert.True(cameraPosition.Z < 0.0f, sprintf "expected camera behind, got %A" cameraPosition)
    Assert.True(cameraPosition.Y > 0.0f, sprintf "expected camera above, got %A" cameraPosition)
    Assert.Equal(Vector3(0.0f, follow.Height, 0.0f), target)

// A minimal actor value for liveness tests (its renderer is never touched).
let private actor (name: string) : Actor =
    { State =
        { Base =
            { Name = name
              Transform = transformIdentity
              Enabled = true
              Visible = true
              Selectable = true
              Draggable = true }
          AssetId = ""
          ShowCollider = false }
      Physics = None
      Renderer = Unchecked.defaultof<ActorRender.ActorRender> }

[<Fact>]
let ``isPlayerLive is true only for a live designated actor`` () =
    let zone = Zone.create ()
    Assert.False(isPlayerLive zone)

    let stale : Handle = { Index = 0u; Generation = 99u }
    zone.Player <- ValueSome stale
    Assert.False(isPlayerLive zone)

    let handle = zone.Actors.Add(actor "player")
    zone.Player <- ValueSome handle
    Assert.True(isPlayerLive zone)

    // Freeing the actor bumps the generation, so the handle goes stale.
    zone.Actors.Free(handle)
    Assert.False(isPlayerLive zone)

[<Fact>]
let ``systems are inert with no player designated`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let frame = input (Input.Key.mask Input.Key.W)
    let mutable follow = createFollow ()
    let mutable camera = Camera.create Vector3.Zero Vector3.Zero
    do playerInput world zone frame 0.016f
    do cameraFollow zone &follow &camera
    do animate zone frame
    Assert.Equal(Vector3.Zero, camera.Position)

[<Fact>]
let ``systems are inert with a stale designation`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let stale : Handle = { Index = 0u; Generation = 99u }
    zone.Player <- ValueSome stale
    let frame = input (Input.Key.mask Input.Key.W)
    let mutable follow = createFollow ()
    let mutable camera = Camera.create (Vector3(1.0f, 1.0f, 1.0f)) Vector3.Zero
    do playerInput world zone frame 0.016f
    do cameraFollow zone &follow &camera
    do animate zone frame
    Assert.Equal(Vector3(1.0f, 1.0f, 1.0f), camera.Position)

// Every actor's body is written back, not just a bound player's. A dynamic actor that
// is not synced renders where it was placed while its collider moves elsewhere.
[<Fact>]
let ``syncActors writes back every actor body`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let capsule : CapsuleCollider = { Radius = 0.35f; HalfHeight = 0.5f }
    let addActor (name: string) (position: Vector3) : Handle =
        let body = Physics.createCapsuleBody world position capsule.HalfHeight capsule.Radius
        zone.Actors.Add({ actor name with Physics = Some { Collider = capsule; Body = body } })

    let first = addActor "one" (Vector3(1.0f, 0.0f, 0.0f))
    let second = addActor "two" (Vector3(2.0f, 0.0f, 0.0f))
    do syncActors world zone

    // The entity position is the feet: the capsule centre shifted down by its offset.
    let feetOf (handle: Handle) : Vector3 =
        let a = &zone.Actors.Get handle
        a.State.Base.Transform.Position
    Assert.True(abs (feetOf(first).Y + 0.85f) < 1e-4f, sprintf "expected feet at y=-0.85, got %f" (feetOf(first).Y))
    Assert.True(abs (feetOf(second).Y + 0.85f) < 1e-4f, sprintf "expected feet at y=-0.85, got %f" (feetOf(second).Y))
    Assert.True(abs (feetOf(first).X - 1.0f) < 1e-4f)
    Assert.True(abs (feetOf(second).X - 2.0f) < 1e-4f)
