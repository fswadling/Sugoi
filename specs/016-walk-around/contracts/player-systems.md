# Contract: Player Systems

Public surface added or changed by this feature. Signatures are illustrative F#,
matching the data model. Actor capsules become dynamic (rotation locked), and the
player systems drive that existing body — no character object is created.

## Player systems — `world/Systems.fs`

```fsharp
module Systems

[<Struct>]
type CameraFollow =
    { mutable Yaw : float32
      Pitch : float32
      Distance : float32
      Height : float32 }

val createFollow : unit -> CameraFollow   // Yaw 0, Pitch 0.35, Distance 4.0, Height 1.7

// An editor selection as a player handle; raises for a non-actor kind.
val playerHandleOf : ref:EntityRef -> SlotMap.Handle

// Whether the bound player resolves to a live actor. Each player system resolves the
// player itself with this; it does not gate the world step (that runs for the whole of
// play mode).
val isPlayerLive : zone:Zone -> player:SlotMap.Handle option -> bool

// W/S set the body's horizontal velocity along its facing; A/D turn the facing and
// write the body's rotation. The body's vertical velocity is left to gravity.
val playerInput : world:Physics.World -> zone:Zone -> player:SlotMap.Handle option -> input:Input -> dt:float32 -> unit

// Copy every actor's resolved position/rotation back onto its entity in place. Not
// player-gated: all actors are dynamic, so an unsynced one would render where it was
// placed while its collider moved.
val syncActors : world:Physics.World -> zone:Zone -> unit

// The camera position and target for a player position and facing. Pure.
val cameraPose : position:Vector3 -> yaw:float32 -> follow:CameraFollow -> Vector3 * Vector3

// Place the camera behind and above the player from the actor's facing.
val cameraFollow : zone:Zone -> player:SlotMap.Handle option -> follow:byref<CameraFollow> -> camera:byref<Camera.Camera> -> unit

// The locomotion clip for the held movement keys: "Jog" while any of W/A/S/D is
// held, otherwise "Idle". Pure.
val locomotionClip : input:Input -> string

// Switch the player actor's animation to match the held movement keys.
val animate : zone:Zone -> player:SlotMap.Handle option -> input:Input -> unit
```

## Physics adapter — `physics/Physics.fs`

```fsharp
// Changed: the actor capsule is now dynamic (gravity + collision) with rotation
// locked (AllowedDOFs = TranslationX|Y|Z) and sleeping disabled, so it stays upright.
val createCapsuleBody : world:World -> position:Vector3 -> halfHeight:float32 -> radius:float32 -> BodyId

// Added: the actor body that locomotion drives.
val getBodyVelocity : world:World -> BodyId -> Vector3
val setBodyVelocity : world:World -> BodyId -> velocity:Vector3 -> unit
val setBodyRotation : world:World -> BodyId -> rotation:Quaternion -> unit
val getBodyTransform : world:World -> BodyId -> Vector3 * Quaternion
```

- `createMeshBody`/`step`/`destroyBody`/`setBodyTransform`/`raycast` are unchanged.
- `World` retains the collision filters and `PhysicsSystemSettings` it was built from
  (as record fields): Jolt's `PhysicsSystem` holds raw pointers to them, so if the GC
  finalises the managed wrappers, collision silently stops and bodies fall through all
  geometry. `test/PhysicsTests.fs` forces a collection to guard this.
- No `CharacterVirtual` wrapper: setting the player must not create physics.

## ActorRender — `world/ActorRender.fs`

```fsharp
// Switch this instance's animation to the named clip, failing loudly if the asset
// does not define it. Used to select Idle/Jog from the held movement keys.
val playClip : r:byref<ActorRender> -> name:string -> unit
```

## Editor actions — `editor/Inspector.fs`

```fsharp
// Set as Player stores the actor's slot handle in `state.Player` directly (not a
// Command). It does not touch physics. No clear action is needed: a stale handle
// goes inert.
val Inspector.draw : state:byref<GameplayState.GameplayState> -> unit
```

## Gameplay screen — `screen/GameplayScreen.fs`

```fsharp
// While not editing: playerInput → Physics.step → syncActors → cameraFollow → animate.
// The world is stepped for all of play mode; the player systems no-op without a live
// bound player, while `syncActors` runs regardless. No enter/leave callbacks.
val update : shared:SharedState.SharedState -> frame:Screen.Frame -> state:byref<GameplayState.GameplayState> -> Screen.ScreenTransition
```

## Rules

- Each player system resolves the bound player with `isPlayerLive` and is a no-op
  when it is absent or the handle is stale. `syncActors` is not a player system: it
  runs for every actor whenever the world steps.
- The binding is an index+generation `SlotMap.Handle` into `Zone.Actors`, not an
  `EntityRef`; a deleted actor or recycled slot fails resolution, so no explicit
  clearing step exists.
- `Set as Player` and the play/edit toggle create, reset, and destroy nothing.
- `Physics.step` runs for the whole of play mode, player or not (and never while
  editing): a play session with no player still simulates the world.
- `syncActors` never writes a body; `physics/Physics.fs` remains a leaf (no
  `Entity`, `Zone`, or GPU dependency).
- Player binding is not part of the undo stack.
