# Contract — the surfaces that change

Everything here is internal to the one project. The surfaces below are the ones that move; the
section at the end lists what must *not* move, because this feature's whole risk is leaking into
the render path.

## New — `world/Sway.fs`

The simulation, pure of GPU, screen and zone. Compiled between `world/Actor.fs` (it names
`PoseFrame` and `ActorModel`) and `world/ActorAsset.fs` (which carries the resolved def).

```fsharp
// ── authored (the manifest) ─────────────────────────────────────────────
[<SymbolicExpansion>]
type SwayChain =
    { Bones : string array; Stiffness : float32 }

// ── resolved (shared per asset) ─────────────────────────────────────────
type SwayChainDef =
    { Joints : int array; Stiffness : float32 }

// ── runtime (per instance) ──────────────────────────────────────────────
[<Struct>]
type SwayPoint = { Joint : int; mutable Rest : Vector3; mutable Position : Vector3; mutable Previous : Vector3; mutable RestLength : float32 }

[<Struct>]
type SwayChainState = { Def : SwayChainDef; Points : SwayPoint array; mutable SeedAnchor : Vector3; mutable Seeded : bool }

type SwayState = { Chains : SwayChainState array }

// ── constants (this phase's tuning) ─────────────────────────────────────
val MaxSwayChainsPerActor : int        // 16
val MaxSwayJointsPerChain : int        // 16
val SwayGravity : float32              // 9.81
val SwayDampingMax : float32           // 0.98  (at stiffness 0)
val SwayDampingMin : float32           // 0.85  (at stiffness 1)
val SwaySpringScale : float32          // 0.9, the global pull (the reference uses 0.3)
val SwayConstraintIterations : int     // 3
val SwayMaxAngle : float32             // 45°, in radians
val SwayTeleportDistance : float32     // 2.0 world units

// ── the surface ─────────────────────────────────────────────────────────
/// Resolve one authored chain against a model. Pure. `Error` is an expected manifest
/// failure (the spec's drop-and-report policy), never a crash.
val resolve : Iqm.Model -> SwayChain -> Result<SwayChainDef, string>

/// Build one instance's runtime chains from an asset's resolved defs. An empty def array
/// yields an empty state, so every entry point is inert for an actor that declares nothing.
val createState : SwayChainDef array -> SwayState

/// Step the ropes, then write the chain joints' rotations into `pose` and recompose it. The blend
/// must have run already. `world` is the actor's world matrix (`upAxisFix * modelMatrix transform`,
/// the same one `draw` uses) and `bodyRot` its world orientation (`transform.Rotation * upAxisFix`,
/// in that order). A no-op when the state is empty or `dt <= 0`; the caller does not call it while
/// not playing. `SwayState` is a reference record, so it is passed by value and its arrays are
/// mutated in place — no `byref` is needed, and a `byref` to a field of a `byref` receiver is not
/// even expressible in F#.
val apply : ActorModel -> PoseFrame -> SwayState -> float32 -> Matrix4x4 -> Quaternion -> unit

/// Forget every chain's simulated state, so the next simulation re-seeds at the animated pose.
/// Called while the editor is open (FR-008): the pose is the blend's either way, but this is what
/// stops a resumed Play from starting mid-swing.
val clear : SwayState -> unit
```

Only `resolveAll`, `resolve`, `createState`, `clear` and `apply` are called from outside the module; the
point maths is private. `resolveAll` is the whole-actor policy — it resolves each authored chain, keeps
the usable defs and returns a message per dropped chain (the caller reports them against the actor's
id) — so the drop-and-report is testable without a device.

### The arithmetic `apply` must implement (research D3)

Read the **animated** pose before writing anything, then walk `k = 0 … n-2` (the last chain joint
drives no segment, so it is never rotated):

```text
restPos[k]   = Vector3.Transform(pose.World[joints[k]].Translation, world)   // world space
restRot[k]   = bodyRot ⊗ pose.WorldRotation[joints[k]]                      // world orientation
restDir[k]   = normalize(restPos[k+1] - restPos[k])
targetDir[k] = normalize(simPos[k+1]  - simPos[k])
d[k]         = quat_from_two_vectors(restDir[k], targetDir[k]), clamped to SwayMaxAngle
targetRot[k] = d[k] ⊗ restRot[k]
targetPose[k]= conj(bodyRot) ⊗ targetRot[k]                                  // back into the pose's own frame
parentPose[k]= pose.WorldRotation[parent of joints[0]]  when k = 0   // outside the chain: unswayed
               targetPose[k-1]                          otherwise    // the parent, post-sway
pose.Rotation[joints[k]] = conj(parentPose[k]) ⊗ targetPose[k]
```

`⊗` is `Quaternion.Multiply` (left operand first). Then `Actor.composeAll` rebuilds the pose.

**`bodyRot` multiplies from the *left*** of the pose's world rotation, and the conversion back
multiplies `conj bodyRot` from the left too. The joint's world orientation is `bodyRot ⊗ poseRot`
because the composites are `pose.World[j] * world` and System.Numerics reverses the quaternion
order — the same reversal the relation table below records. Getting this side wrong is the
reference's own failure mode in a new dress, so a test walks the whole chain at several yaws and
asserts the local rotations come out identical.

The relations this rests on were verified against `System.Numerics` on this machine — row-vector
`Vector3.Transform`, `q(local * parent) = parent ⊗ local`, "a world delta pre-multiplies a world
orientation", and the `from→to` quaternion landing a vector on its target — see research D3. **The
parent frame is the *posed* one, and the segment is the one the bone *drives*.** Those two facts
are the whole difference from the reference, and a test asserts each.

## Changed — `world/Actor.fs`

`PoseFrame` gains `Translation : Vector3 array`, `Rotation : Quaternion array`,
`Scale : Vector3 array` and `WorldRotation : Quaternion array`, allocated in `createPoseFrame`.
`composeJoint` writes them alongside the matrices it already writes. New:

```fsharp
/// Recompose every joint's Local/World/Skinning/WorldRotation from the pose's stored TRS.
/// Called after a pass that rewrote some joints' `Rotation`.
val composeAll : ActorModel -> PoseFrame -> unit
```

`blendPose`, `samplePose` and the slot algebra are otherwise unchanged; the existing tests (which
assert the composed matrices) must still pass.

## Changed — `world/ActorAsset.fs`

`ActorAsset` gains `SwayChains : Sway.SwayChainDef array` — resolved once, shared by every
instance (the treatment `Morphs` and `Portraits` already get). An actor declaring no chains
carries `[||]`.

## Changed — `world/Assets.fs`

The private `Manifest` gains `Sway : Sway.SwayChain array`. `loadActorAsset` resolves each entry
through `Sway.resolve`, reports each `Error` once via `Diagnostics.reportf` (naming the actor and
the offending joint), and stores the successes. An actor whose chains all fail still loads.

## Changed — `world/ActorRender.fs`

- `[<Struct>] ActorRender` gains `Sway : Sway.SwayState`, built in `create` from
  `asset.SwayChains`.
- New `simulate` — the actor's per-frame pose step, replacing the blend that used to live inside
  `upload`:

```fsharp
/// Every frame, edit and play alike: advance the held clips' clocks while playing, blend the pose
/// and — while playing — step the actor's sway and apply it. The blend always runs, so an actor
/// spawned in the editor is posed; the clock and the sway are gated on `playing` (FR-008).
///
/// `model` is the entity's model matrix and `rotation` its rotation, passed in rather than derived
/// because `world/Entity.fs` is compiled below this file.
val simulate : byref<ActorRender> -> float32 -> bool -> Matrix4x4 -> Quaternion -> unit
```

- `upload` loses its `blendPose` call and becomes what its name says: the bone-palette upload
  (copy pass). Its signature is unchanged.

`upAxisFix` and the `bodyRot` it implies stay private; `simulate` computes
`upAxisFix * Entity.modelMatrix a.State.Base.Transform` and passes it with `Sway.apply`.

## Changed — `screen/GameplayScreen.fs`

`advanceActorAnimations` — today the play-only loop that advances each actor's clock — becomes
the per-frame actor loop that runs in **both** modes, calling
`ActorRender.simulate &actor.Renderer frame.Dt (not state.Editing)` for every actor of the active
zone. **It moves to the end of `update`, after `CueEngine.tick`** — its position is the substance,
not an incidental detail: `Systems.syncActors` writes each actor's transform from physics,
`Systems.animate` sets the clip weights, and `CueEngine.tick` runs cue ops that set weights and
move actors, so a loop left at the top of the play block (where `advanceActorAnimations` sits
today) would blend from the previous frame's weights and read a pre-`syncActors` position — a
behaviour change against FR-015, and a one-frame-stale anchor for the sway. Placing it last makes
the frame's weights, cue effects and positions all visible to the blend and the simulation. The
rest of the play block's ordering is unchanged; `renderScene` keeps calling `ActorRender.upload`
where it does today and passes nothing new.

## Changed — data and project files

- `assets/actors/Akane/akane.manifest` — a `Sway` array: Akane's hair chains, checked against
  `Akane.iqm`'s joint names (`J_Sec_Hair*`, 53 of them) and a parent→child path.
- `fsharp-game.fsproj` — `<Compile Include="world/Sway.fs" />` between `world/Actor.fs` and
  `world/ActorAsset.fs`.
- `test/fsharp-game.Tests.fsproj` — `<Compile Include="SwayTests.fs" />` after `ActorTests.fs`.
- `test/SwayTests.fs` — new; see [quickstart.md](./quickstart.md).

## Explicitly unchanged

- **`shaders/` — all of it.** `mesh.vert`, `cel.frag`, `outline.frag` and their `.spv` files are
  untouched: the sway changes what the bone palette *contains*, not how it is drawn (FR-014).
  There is no sway in a shader, in this port or in the reference.
- **`render/`** — `Gpu`, `Mesh`, `Texture`, `StaticMesh`, `InstanceBuffer`, `Ui`, `DebugDraw`: no
  new pipeline, target, binding or uniform.
- **`world/Actor.fs`'s animation surface** — `blendPose`'s weighting, `AnimationSlot`,
  `setSlotWeight`/`removeSlot`, `MorphSlot`: sway writes no animation state (FR-011).
- **`core/Iqm.fs`** — joint names and parents are already parsed; nothing new is read.
- **The editor** — no panel, no Inspector row, no gizmo, no command, no undo step. Sway is not
  authored per instance and is not persisted.
- **`world/Zone.fs`, `ZoneTemplate`, `ScenarioZone`, `Systems`** — no wind field, no zone setting,
  no capture: wind is out of this phase (spec Assumptions).
- **The physics/gameplay surface** — colliders, capsules, picking, camera follow. Sway is
  cosmetic and is never written back to the entity (spec Assumptions).
