module SystemsTests

// Trigger detection: the containment rule (a cylinder around the player) and the nearest
// interactable tracking. The update reads the player's *entity* position, which the
// play-mode body sync has already written, so it needs no physics world and no graphics
// device — a zone, an actor, and the player designation are enough.

open Xunit
open System.Numerics
open Entity
open Systems

let private entity (name: string) : Entity =
    { Name = name
      Transform = transformIdentity
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

// A zone whose only actor is the designated player, standing at `position`.
let private zoneWithPlayer (position: Vector3) : Zone.Zone =
    let zone = Zone.create ()
    let hero : Actor =
        { State =
            { Base = { entity "hero" with Transform = { transformIdentity with Position = position } }
              AssetId = "character"
              ShowCollider = false }
          Physics = None
          Renderer = Unchecked.defaultof<ActorRender.ActorRender> }
    zone.Player <- ValueSome (zone.Actors.Add hero)
    zone

// A trigger at `position`, with the given shape and settings.
let private add (zone: Zone.Zone) (position: Vector3) (radius: float32) (height: float32) (interactable: bool) (enabled: bool) (prompt: string) : EntityRef =
    let ref = Zone.spawnTrigger zone { entity "area" with Transform = { transformIdentity with Position = position } }
    let payload = Zone.captureTrigger zone ref
    do Zone.setTrigger
        zone
        ref
        { payload with
            Radius = radius
            Height = height
            Interactable = interactable
            Prompt = prompt
            Base = { payload.Base with Enabled = enabled } }
    ref

// Whether the trigger reports the player inside, after an update.
let private inside (zone: Zone.Zone) (ref: EntityRef) : bool =
    do updateTriggers zone |> ignore
    (Zone.tryTriggerAt zone ref).Value.Triggered

// ── Containment ──

[<Fact>]
let ``containment includes the radius and the base and top boundaries`` () =
    let cases =
        [ "inside the volume", Vector3(0.0f, 1.0f, 0.0f), true
          "on the radius", Vector3(2.0f, 1.0f, 0.0f), true
          "on the base", Vector3(0.0f, 0.0f, 0.0f), true
          "on the top", Vector3(0.0f, 3.0f, 0.0f), true
          "just beyond the radius", Vector3(2.001f, 1.0f, 0.0f), false
          "just above the top", Vector3(0.0f, 3.001f, 0.0f), false
          "just below the base", Vector3(0.0f, -0.001f, 0.0f), false
          "far away horizontally", Vector3(10.0f, 1.0f, 0.0f), false ]

    for (label, playerPosition, expected) in cases do
        let zone = zoneWithPlayer playerPosition
        let ref = add zone Vector3.Zero 2.0f 3.0f false true ""
        Assert.True((expected = inside zone ref), sprintf "expected %b for a player %s" expected label)

[<Fact>]
let ``the volume is not affected by the trigger's rotation or scale`` () =
    let zone = zoneWithPlayer (Vector3(0.0f, 1.0f, 0.0f))
    let ref = add zone Vector3.Zero 2.0f 3.0f false true ""
    let payload = (Zone.tryTriggerAt zone ref).Value

    do Zone.setTransform
        zone
        (Physics.create ())
        ref
        { transformIdentity with
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.2f)
            Scale = Vector3(8.0f, 8.0f, 8.0f) }

    Assert.True(inside zone ref)
    Assert.Equal(2.0f, (Zone.tryTriggerAt zone ref).Value.State.Radius)
    Assert.Equal(payload.State.Height, (Zone.tryTriggerAt zone ref).Value.State.Height)

[<Fact>]
let ``standing inside stays inside across updates`` () =
    let zone = zoneWithPlayer (Vector3(0.5f, 1.0f, 0.5f))
    let ref = add zone Vector3.Zero 2.0f 3.0f false true ""

    // Remaining inside is a state, not an event: repeated updates keep reporting it.
    for _ in 1 .. 5 do
        Assert.True(inside zone ref)

// ── Disabled, and no player ──

[<Fact>]
let ``a disabled trigger never reports the player inside`` () =
    let zone = zoneWithPlayer (Vector3(0.0f, 1.0f, 0.0f))
    let ref = add zone Vector3.Zero 2.0f 3.0f false false ""
    Assert.False(inside zone ref)

    // It is also never the tracked interactable, and re-enabling detects again.
    let interactable = add zone Vector3.Zero 2.0f 3.0f true false "Look"
    Assert.True(ValueOption.isNone (updateTriggers zone))
    Assert.False(inside zone interactable)

[<Fact>]
let ``with no live player nothing is inside`` () =
    let noneDesignated = Zone.create ()
    let ref = add noneDesignated Vector3.Zero 2.0f 3.0f false true ""
    Assert.False(inside noneDesignated ref)

    // A designation whose actor no longer resolves is the same as no designation.
    let zone = zoneWithPlayer (Vector3(0.0f, 1.0f, 0.0f))
    let area = add zone Vector3.Zero 2.0f 3.0f false true ""
    let handle = zone.Player.Value
    zone.Player <- ValueSome { handle with Generation = handle.Generation + 1u }

    Assert.False(inside zone area)
    Assert.True(ValueOption.isNone (updateTriggers zone))

// ── Overlap and interactable tracking ──

[<Fact>]
let ``overlapping triggers each report the player inside`` () =
    let zone = zoneWithPlayer (Vector3(0.0f, 1.0f, 0.0f))
    let first = add zone Vector3.Zero 2.0f 3.0f false true ""
    let second = add zone (Vector3(1.0f, 0.0f, 0.0f)) 2.0f 3.0f false true ""

    do updateTriggers zone |> ignore

    Assert.True((Zone.tryTriggerAt zone first).Value.Triggered)
    Assert.True((Zone.tryTriggerAt zone second).Value.Triggered)

[<Fact>]
let ``the nearest interactable trigger the player is inside is the tracked one`` () =
    let zone = zoneWithPlayer (Vector3(0.0f, 1.0f, 0.0f))
    let far = add zone (Vector3(6.0f, 0.0f, 0.0f)) 8.0f 3.0f true true "Far"
    let near = add zone (Vector3(2.0f, 0.0f, 0.0f)) 8.0f 3.0f true true "Near"

    // Both contain the player; the nearer one is answered.
    match updateTriggers zone with
    | ValueSome tracked -> Assert.Equal<EntityRef>(near, tracked)
    | ValueNone -> Assert.Fail "expected the nearer interactable trigger"

    // The prompt is what an overlay shows, and it comes from the tracked trigger.
    Assert.Equal("Near", (Zone.tryTriggerAt zone near).Value.State.Prompt)
    Assert.True((Zone.tryTriggerAt zone far).Value.Triggered)

[<Fact>]
let ``a non-interactable trigger is never the tracked one`` () =
    let zone = zoneWithPlayer (Vector3(0.0f, 1.0f, 0.0f))
    let plain = add zone Vector3.Zero 4.0f 3.0f false true ""
    Assert.True(ValueOption.isNone (updateTriggers zone))
    Assert.True((Zone.tryTriggerAt zone plain).Value.Triggered)

[<Fact>]
let ``leaving clears the inside state and the tracked trigger`` () =
    let zone = zoneWithPlayer (Vector3(0.0f, 1.0f, 0.0f))
    let ref = add zone Vector3.Zero 2.0f 3.0f true true "Look"
    Assert.Equal(ValueSome ref, (updateTriggers zone))

    // Move the player well outside the volume.
    let handle = zone.Player.Value
    let actor = &zone.Actors.Get handle
    actor <- { actor with State.Base.Transform = { actor.State.Base.Transform with Position = Vector3(20.0f, 1.0f, 0.0f) } }

    Assert.True(ValueOption.isNone (updateTriggers zone))
    Assert.False((Zone.tryTriggerAt zone ref).Value.Triggered)
