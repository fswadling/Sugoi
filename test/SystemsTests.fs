module SystemsTests

// Trigger detection: the containment rule (a cylinder around the player) and the nearest
// interactable tracking. The update reads the player's *entity* position, which the
// play-mode body sync has already written, so it needs no physics world and no graphics
// device — a zone, an actor, and the player designation are enough.

open Xunit
open System.Numerics
open Entity
open Systems
open Cue
open TestSupport

// A zone whose only actor is the designated player, standing at `position`.
let private zoneWithPlayer (position: Vector3) : Zone.Zone =
    let zone = Zone.create ()
    let hero : Actor =
        { State =
            { Base = { entity "hero" with Transform = { transformIdentity with Position = position } }
              AssetId = "character"
              ShowCollider = false
              Physics = dynamicPhysics }
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

// The nearest interactable trigger the player is inside, after an update — the out-parameter
// `updateTriggers` writes into.
let private tracked (zone: Zone.Zone) : EntityRef voption =
    let mutable closest = ValueNone
    let mutable cue = Fin
    do updateTriggers zone &closest &cue
    closest

// Whether the trigger reports the player inside, after an update.
let private inside (zone: Zone.Zone) (ref: EntityRef) : bool =
    let mutable closest = ValueNone
    let mutable cue = Fin
    do updateTriggers zone &closest &cue
    (Zone.tryTriggerAt zone ref).Value.Triggered

// Move the designated player to `position`, writing through the slot as the play-mode body sync does.
let private movePlayer (zone: Zone.Zone) (position: Vector3) : unit =
    let handle = zone.Player.Value
    let actor = &zone.Actors.Get handle
    let baseEntity = { actor.State.Base with Transform = { actor.State.Base.Transform with Position = position } }
    actor <- { actor with State = { actor.State with Base = baseEntity } }

// Give a trigger an authored cue.
let private setCue (zone: Zone.Zone) (ref: EntityRef) (cue: Cue) : unit =
    let payload = (Zone.tryTriggerAt zone ref).Value
    do Zone.setTrigger zone ref { payload.State with Cue = cue }

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
    Assert.True(ValueOption.isNone (tracked zone))
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
    Assert.True(ValueOption.isNone (tracked zone))

// ── Overlap and interactable tracking ──

[<Fact>]
let ``overlapping triggers each report the player inside`` () =
    let zone = zoneWithPlayer (Vector3(0.0f, 1.0f, 0.0f))
    let first = add zone Vector3.Zero 2.0f 3.0f false true ""
    let second = add zone (Vector3(1.0f, 0.0f, 0.0f)) 2.0f 3.0f false true ""

    do tracked zone |> ignore

    Assert.True((Zone.tryTriggerAt zone first).Value.Triggered)
    Assert.True((Zone.tryTriggerAt zone second).Value.Triggered)

[<Fact>]
let ``the nearest interactable trigger the player is inside is the tracked one`` () =
    let zone = zoneWithPlayer (Vector3(0.0f, 1.0f, 0.0f))
    let far = add zone (Vector3(6.0f, 0.0f, 0.0f)) 8.0f 3.0f true true "Far"
    let near = add zone (Vector3(2.0f, 0.0f, 0.0f)) 8.0f 3.0f true true "Near"

    // Both contain the player; the nearer one is answered.
    match tracked zone with
    | ValueSome tracked -> Assert.Equal<EntityRef>(near, tracked)
    | ValueNone -> Assert.Fail "expected the nearer interactable trigger"

    // The prompt is what an overlay shows, and it comes from the tracked trigger.
    Assert.Equal("Near", (Zone.tryTriggerAt zone near).Value.State.Prompt)
    Assert.True((Zone.tryTriggerAt zone far).Value.Triggered)

[<Fact>]
let ``a non-interactable trigger is never the tracked one`` () =
    let zone = zoneWithPlayer (Vector3(0.0f, 1.0f, 0.0f))
    let plain = add zone Vector3.Zero 4.0f 3.0f false true ""
    Assert.True(ValueOption.isNone (tracked zone))
    Assert.True((Zone.tryTriggerAt zone plain).Value.Triggered)

[<Fact>]
let ``leaving clears the inside state and the tracked trigger`` () =
    let zone = zoneWithPlayer (Vector3(0.0f, 1.0f, 0.0f))
    let ref = add zone Vector3.Zero 2.0f 3.0f true true "Look"
    Assert.Equal(ValueSome ref, (tracked zone))

    // Move the player well outside the volume.
    let handle = zone.Player.Value
    let actor = &zone.Actors.Get handle
    actor <- { actor with State.Base.Transform = { actor.State.Base.Transform with Position = Vector3(20.0f, 1.0f, 0.0f) } }

    Assert.True(ValueOption.isNone (tracked zone))
    Assert.False((Zone.tryTriggerAt zone ref).Value.Triggered)

// ── Firing the authored cue (spec 058) ──

// Run one trigger update against `cue` and return the cue as it now stands and the tracked trigger.
let private step (zone: Zone.Zone) (cue: Cue) : Cue * EntityRef voption =
    let mutable c = cue
    let mutable closest = ValueNone
    do updateTriggers zone &closest &c
    c, closest

[<Fact>]
let ``entering a trigger forks its authored cue beside the running cue`` () =
    let zone = zoneWithPlayer (Vector3(20.0f, 1.0f, 0.0f)) // outside to begin
    let ref = add zone Vector3.Zero 2.0f 3.0f false true ""
    do setCue zone ref (Wait 1.0)

    // Outside: nothing fires.
    let (unfired, _) = step zone Fin
    match unfired with
    | Fin -> ()
    | _ -> Assert.Fail "nothing should fire while the player is outside"

    // Step inside: the trigger's cue is forked in beside the running one, undisturbed.
    do movePlayer zone (Vector3(0.0f, 1.0f, 0.0f))
    let (fired, _) = step zone (Wait 5.0)
    match fired with
    | Parallel (Wait running, Wait arrived) ->
        Assert.Equal(5.0, running)
        Assert.Equal(1.0, arrived)
    | other -> Assert.Fail(sprintf "expected the forked cue beside the running one, got %A" other)
    Assert.True((Zone.tryTriggerAt zone ref).Value.Triggered)

[<Fact>]
let ``remaining inside does not fire the trigger again`` () =
    let zone = zoneWithPlayer (Vector3(0.0f, 1.0f, 0.0f))
    let ref = add zone Vector3.Zero 2.0f 3.0f false true ""
    do setCue zone ref (Wait 1.0)

    // The first update is the entry: it fires.
    let (entered, _) = step zone Fin
    match entered with
    | Wait seconds -> Assert.Equal(1.0, seconds)
    | other -> Assert.Fail(sprintf "expected the trigger's cue to be forked, got %A" other)

    // The second, still inside, is not an entry: nothing fires.
    let (again, _) = step zone Fin
    match again with
    | Fin -> ()
    | _ -> Assert.Fail "the trigger must not fire again while the player remains inside"

[<Fact>]
let ``leaving and re-entering fires the trigger again`` () =
    let zone = zoneWithPlayer (Vector3(0.0f, 1.0f, 0.0f))
    let ref = add zone Vector3.Zero 2.0f 3.0f false true ""
    do setCue zone ref (Wait 1.0)
    let (_, _) = step zone Fin // enter: fires

    do movePlayer zone (Vector3(20.0f, 1.0f, 0.0f))
    let (left, _) = step zone Fin
    match left with
    | Fin -> ()
    | _ -> Assert.Fail "leaving must not fire"

    do movePlayer zone (Vector3(0.0f, 1.0f, 0.0f))
    let (refired, _) = step zone Fin
    match refired with
    | Wait seconds -> Assert.Equal(1.0, seconds)
    | other -> Assert.Fail(sprintf "expected a re-fire on re-entry, got %A" other)

[<Fact>]
let ``an empty cue, a disabled trigger and no live player fire nothing`` () =
    let zone = zoneWithPlayer (Vector3(0.0f, 1.0f, 0.0f))
    let empty = add zone Vector3.Zero 2.0f 3.0f false true ""

    let (withEmpty, _) = step zone Fin
    match withEmpty with
    | Fin -> ()
    | _ -> Assert.Fail "an empty cue must fire nothing"
    Assert.True((Zone.tryTriggerAt zone empty).Value.Triggered)

    let disabled = add zone (Vector3(4.0f, 0.0f, 0.0f)) 8.0f 3.0f false false ""
    do setCue zone disabled (Wait 1.0)
    let (withDisabled, _) = step zone Fin
    match withDisabled with
    | Fin -> ()
    | _ -> Assert.Fail "a disabled trigger must fire nothing"

    let bare = Zone.create ()
    let plain = add bare Vector3.Zero 2.0f 3.0f false true ""
    do setCue bare plain (Wait 1.0)
    let (withNoPlayer, _) = step bare Fin
    match withNoPlayer with
    | Fin -> ()
    | _ -> Assert.Fail "with no live player nothing fires"

[<Fact>]
let ``an interactable trigger is not fired on entry`` () =
    let zone = zoneWithPlayer (Vector3(0.0f, 1.0f, 0.0f))
    let ref = add zone Vector3.Zero 2.0f 3.0f true true "Look"
    do setCue zone ref (Wait 1.0)

    let (cue, closest) = step zone Fin
    match cue with
    | Fin -> ()
    | _ -> Assert.Fail "an interactable trigger must not fire on entry"
    Assert.Equal(ValueSome ref, closest)

[<Fact>]
let ``forkTriggerCue forks the tracked trigger's cue and nothing else`` () =
    // FR-004, FR-005: the one rule the entry fire and the interact key share.
    let zone = zoneWithPlayer (Vector3(0.0f, 1.0f, 0.0f))
    let ref = add zone Vector3.Zero 2.0f 3.0f true true "Look"
    do setCue zone ref (Wait 1.0)

    let mutable cue = Fin
    do forkTriggerCue zone (ValueSome ref) &cue
    match cue with
    | Wait seconds -> Assert.Equal(1.0, seconds)
    | other -> Assert.Fail(sprintf "expected the trigger's cue to be forked, got %A" other)

    // No tracked trigger fires nothing.
    let mutable none = Fin
    do forkTriggerCue zone ValueNone &none
    match none with
    | Fin -> ()
    | _ -> Assert.Fail "no tracked trigger must fire nothing"

    // A trigger whose cue is empty fires nothing.
    let empty = add zone (Vector3(6.0f, 0.0f, 0.0f)) 2.0f 3.0f false true ""
    let mutable untouched = Fin
    do forkTriggerCue zone (ValueSome empty) &untouched
    match untouched with
    | Fin -> ()
    | _ -> Assert.Fail "an empty cue must fire nothing"

    // A stale ref (wrong generation) fires nothing.
    let stale = { ref with Generation = ref.Generation + 1u }
    let mutable withStale = Fin
    do forkTriggerCue zone (ValueSome stale) &withStale
    match withStale with
    | Fin -> ()
    | _ -> Assert.Fail "a stale ref must fire nothing"
