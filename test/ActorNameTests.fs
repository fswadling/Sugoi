module ActorNameTests

// Actors are addressed by name (spec 072) and, since spec 075, resolved per zone: a cue op names its place and
// resolves the actor there with a `Zone`-local name scan (`Zone.tryActorByName`, the pattern
// `tryWaypointByName` / `tryTriggerByName` already use). There is no scan over the present places. Headless: an
// actor is spawned through the real `Spawning` / `Zone` path against a fabricated actor asset whose GPU handles
// are defaulted, so the scan is asserted with no device.

open System
open System.Collections.Generic
open Prime
open System.Numerics
open Xunit
open Gltf
open Entity
open TestSupport

let private headlessAssets () : Assets.AssetManager =
    managerWith [ "aiko", actorAsset "aiko" ] [] []

// Spawn an actor named `name` into the active zone, through the editor path — no cast argument: an actor
// carries no role, its name is its identity (spec 072 FR-001).
let private spawn (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (name: string) : EntityRef =
    do Spawning.spawnActor shared &state (Spawning.baseEntity name) "aiko"
    state.Editor.Selection.Value

// ── Zone.tryActorByName ──

[<Fact>]
let ``Zone.tryActorByName finds a named live actor by its name and misses an unknown one`` () =
    let shared = headlessSharedWith (headlessAssets ())
    let mutable state = headlessState ()
    let ref = spawn shared &state "Aiko"
    let zone = GameplayState.activeZone state

    // the actor's identity is its name; nothing else records who it is
    Assert.Equal("Aiko", zone.Actors.Get(handleOf ref).State.Base.Name)
    Assert.Equal(Some ref, Zone.tryActorByName zone "Aiko")
    Assert.Equal(None, Zone.tryActorByName zone "Akane")

[<Fact>]
let ``a duplicate name resolves to the first actor, silently`` () =
    let shared = headlessSharedWith (headlessAssets ())
    let mutable state = headlessState ()
    let first = spawn shared &state "Aiko"
    let second = spawn shared &state "Aiko"
    Assert.NotEqual(first, second)
    let zone = GameplayState.activeZone state
    Assert.Equal(Some first, Zone.tryActorByName zone "Aiko")
