# Phase 0 — Research: Actor Sway Bones

The reference (`odin-game/src/sway.odin`, `docs/SWAY_BONES_PLAN.md`) is the model. Its three
arithmetic defects are recorded in the spec's Origin and are corrected here. Everything below
was checked against this port's own maths, not assumed from the reference.

## D1 — Where the simulation runs, and how it gets the frame time

**Decision.** The actor's per-frame pose step (advance clocks → blend → sway) moves from the
render phase into the **update** phase, and `ActorRender.upload` shrinks to the bone-palette
upload it is named for.

**Rationale.** The sway needs the frame's `dt` and the actor's world transform. Today the pose
is built inside `ActorRender.upload`, which runs in the copy-pass phase from
`GameplayScreen.renderScene` — a function that receives a `ScenePass` (command buffer and
targets) and **no `dt`**, and whose loop over actors has no transform in scope. The player
systems in `GameplayScreen.update` have both `frame.Dt` and the actors, and `update` runs
before the render phase in the same frame (`Engine.fs`), so a pose built there is the pose
uploaded. The update phase is also where a sway pass belongs — it is a system, not a draw — but note
that this port runs it **last** in `update` rather than between animation and triggers, where the
reference's own loop (`odin-game`'s `play_mode_systems_update`) puts it. The next paragraph is why.

The pose must still be built in **edit** mode (an actor spawned in the editor has a zeroed
pose otherwise), so the new per-frame actor step runs in both modes: it advances clocks and
simulates sway only while playing, and always blends. Sway being play-only then falls out of
the same gate that already freezes the clock in the editor (FR-008).

**Its position within `update` matters as much as its phase.** The play block sets what the pose
depends on *late*: `Systems.syncActors` writes each actor's transform from physics,
`Systems.animate` sets the clip weights, and `CueEngine.tick` — last — runs cue ops that set
weights and move actors. The loop therefore runs **at the very end of `update`, after
`CueEngine.tick`**, so the blend uses this frame's weights and the simulation this frame's
positions. Left where the old clock-advance loop sat (the top of the play block) it would blend
one frame stale and read a pre-`syncActors` anchor — exactly the kind of quiet regression the
phase move must not introduce (FR-015).

**Alternatives considered.** *Add `dt` to `ScenePass`* — a shared type changed for one
feature, and the sim would still run in a phase that only draws. *Add `Dt` to
`GameplayState`* — a field existing only to smuggle time into the render phase; the update
phase already has it. *Run sway in `update` against the previous frame's pose* — works but
introduces a one-frame lag for no benefit, and splits the pose's ownership across two phases.

## D2 — Carrying per-joint rotations through a matrices-only pose

**Decision.** `PoseFrame` gains the **blended TRS** — `Translation`, `Rotation`, `Scale` — and
`WorldRotation`. `composeJoint` (which already receives `t`, `r`, `s`) writes them as it
composes; a new `composeAll` re-runs `composeJoint` over every joint from the stored TRS.

**Rationale.** Sway writes *rotations*; the pose stores *matrices*, and the sway cannot
recompose a joint without its translation and scale (they are the other two thirds of
`localMatrix t r s`). Recovering them from the composed matrix means factorising it
(row lengths, normalised rotation) — lossy under scale, and exactly the kind of hand-rolled
extraction that produced the reference's first defect. Storing what the blend already
computed is exact, free at blend time, and allocation-free (the arrays are allocated once in
`createPoseFrame`). `WorldRotation` is stored for the same reason `Rotation` is: the sway needs
the *animated* world rotation of a joint, and composing it alongside the matrix (`parent ⊗
local`) is exact where extracting it from a scaled matrix is not.

Recomposing the whole pose (rather than only the affected subtrees) is one extra O(joints)
pass over ~60 joints per actor — the same order as the blend that produced it, and the price
the reference pays too (`sway_bone_system` then `skin_actor_model`).

**Alternatives considered.** *Factorise the matrix* — lossy and error-prone. *Store only
`Rotation` and rotate the composed matrix* — pre-multiplying a rotation onto a row-vector
matrix moves its translation, so the joint would drift. *Recompose only the chain's subtrees* —
more bookkeeping for a saving measured in tens of multiplies.

## D3 — The arithmetic, and why it reproduces the simulated chain

**Decision.** For each chain, walk the joints root→tip in **closed form**, then recompose:

```text
restWorld[k]   = the animated world rotation of chain bone k          (pose.WorldRotation)
restDir[k]     = normalize(restPos[k+1] - restPos[k])                 (animated world positions)
targetDir[k]   = normalize(simPos[k+1]  - simPos[k])                  (the simulated chain)
d[k]           = quat_from_two_vectors(restDir[k], targetDir[k])       (world space, angle-clamped)
targetWorld[k] = d[k] ⊗ restWorld[k]                                   (apply the world delta)
parentWorld[k] = animated world rotation of chain[0]'s skeleton parent, when k = 0
                 targetWorld[k-1],                                     otherwise
pose.Rotation[chain[k]] = conj(parentWorld[k]) ⊗ targetWorld[k]
```

Above is the world-space shape of it. The implementation carries the same relations through the
pose's own frame — `world/Sway.fs` composes the actor's `bodyRot` on the **left** of
`pose.WorldRotation` and converts back the same way — and that side is where the sign can silently
invert. `contracts/sway.md` states the frame exactly, and a test walks the whole chain at several
yaws and asserts the local rotations come out identical.

**Why it is right.** The port is row-vector (`world/Entity.fs`: `TRS = S·R·T`; `world/Actor.fs`:
`world = local * parentWorld`, `skinning = inverseBind * world`). The quaternion relations that
follow were **verified against `System.Numerics`** (see "Verified facts" below), so the two
steps above are not guesses:

- The bone's world *orientation* returns to the joint's own world rotation — which is why the
  root needs no special anchoring: a joint's rotation never moves that joint, so the posed
  position of every chain bone already follows the animation. The reference had to force
  `points[0]` and skipped rotating bone 0 entirely; here the anchor is automatic and the whole
  chain still bends (FR-006).
- Each segment's world direction ends **exactly** on its simulated direction: the segment is
  `targetWorld[k]` applied to the local child offset, and `d[k]` was built to carry `restDir`
  onto `targetDir`.
- Because each segment also keeps its rest length (the translation is untouched), the chain's
  reconstructed positions equal the simulated ones up to the anchor — so the *visible* chain is
  the chain that was simulated, which is the outcome the reference's compounding never reached.

**The reference's three defects, and where each is answered.** Defect 1 (parent frame inverted
by a `matrix_to_quat` that returns the conjugate): not reproduced — `conj(parentWorld)` is taken
from a quaternion, and `parentWorld` is the *posed* one, so there is no extraction at all and no
inverted frame (FR-005). Defect 2 (delta derived from the segment *into* the bone): the delta is
derived from the segment the bone **drives** — `restDir[k]` spans `k → k+1` (FR-006). Defect 3
(parents not re-derived): `parentWorld[k]` is `targetWorld[k-1]`, the parent's *post-sway*
orientation, so the walk never composes a delta onto a stale frame.

**Verified facts (`System.Numerics`, this machine, .NET 10).**

| Relation | Result |
|---|---|
| `Vector3.Transform(v, T)` | `(11, 2, 3)` for `v = (1,2,3)`, `T` a `+10x` translation — row-vector (`v * M`) |
| `S * R * T` applied to `(1,0,0)` | `(1, 2, 0)` — scale, then rotate, then translate |
| `M(q1 * q2)` | `= M(q2) * M(q1)` — the matrix product order reverses |
| With `world = M(local) * M(parent)` | the world rotation is `parent ⊗ local` |
| Applying a world delta `d` | new world rotation is `d ⊗ rest` |
| `quat_from_two_vectors(src, tgt)` | `Transform(src, M(q)) = tgt` for axis-aligned and oblique pairs |

**Alternatives considered.** *Port the reference's arithmetic verbatim* — reproduces all three
defects; measured at ~24° per-segment error and a 0.16-unit tip error on a 25° bend. *Iterate
the chain, recomposing between joints* — converges, but needs the current segment direction per
step and so an intermediate recompose; the closed form needs neither.

## D4 — The rope

**Decision.** Per point: the joint index, the world position it rests at in the current pose,
its current and previous simulated positions, and its rest distance from the previous point.
Root anchored to its posed position; every other point Verlet-integrated:

```text
v            = (position - previous) * damping
previous     = position
position    += v
position.y  -= SWAY_GRAVITY * dt * dt
position    += (rest - position) * springK
```

then `SWAY_CONSTRAINT_ITERS` passes holding each `restLength` (both points corrected by half,
with the root re-snapped to its posed position after each pass so the anchor cannot drift), then
the D3 walk. `damping = lerp(0.98, 0.85, stiffness)`, `springK = stiffness * SwaySpringScale` — the
reference's constants, except that `SwaySpringScale` is tuned to 0.9 rather than its 0.3 (see
`world/Sway.fs`: the global pull is separate from the authored stiffness precisely so the rope can be
made stiffer without also making it wobble more).
reference's constants (spec Assumptions: its look is the starting point).

**Rationale.** The reference's model, minus the defects: its constraint pass skipped moving the
root and left the root pair unsatisfied; re-snapping is both simpler and correct. Gravity is
`-Y` in this port's world (the IQM's Z-up is already converted for rendering), where the
reference applied it in world space too.

**Seeding.** The reference detects "not yet seeded" by testing a position against `(0,0,0)` — a
sentinel that is also a legal position. Here each chain carries an explicit `Seeded` flag: it is
cleared while not playing, so the editor holds the animated pose (FR-008) and Play always starts
from rest.

## D5 — Where inertia comes from

**Decision.** From the anchored root alone. No movement term.

**Rationale.** Anchoring the root to a pose that is itself moving is what produces the lag: the
root is placed at its *new* posed position each frame, while the free points keep their world
positions (and their Verlet velocity through `previous`), so the chain is left behind by the
motion and the spring pulls it back. This is the mechanism `docs/SWAY_BONES_PLAN.md` describes,
and the same document lists actor acceleration feed-forward as a **non-goal**. The reference
nonetheless added `movement_force = -movement * 0.8`, added to position with no `dt` scaling —
frame-rate dependent, and it doubles a lag the anchoring already produces. It is dropped.

**Alternatives considered.** *Port the movement term* — a stated non-goal, frame-rate dependent,
and untested in the reference against the anchoring it duplicates. *Capture the actor's velocity
and scale it by `dt`* — a real feed-forward, but one the anchoring already supplies; adding it
would need its own justification and its own tuning.

## D6 — Bounds and caps

**Decision.** `SWAY_MAX_ANGLE` (45°) clamps the per-segment world delta `d[k]`; a chain is
refused beyond `MaxSwayJointsPerChain = 16` points, and an actor beyond
`MaxSwayChainsPerActor = 16` chains (Akane's longest chain is 5 and she declares 12).

**Rationale.** Clamping `d[k]` bounds the amount each *segment* turns in the world, which is
exactly the folding failure mode — a chain that over-turns folds through itself. The caps are
FR-012's requirement that a manifest cannot exhaust memory; 16 is comfortably above the only
asset that declares chains, and is a literal rather than a configurable. A frame step large
enough to overshoot is bounded by the same clamp, so no separate substep is needed (the
reference notes Verlet is unconditionally stable here).

**Teleport.** A chain whose anchored rest position moved by more than
`SwayTeleportDistance = 2.0` world units in one frame (a zone switch, a spawn, an authored
move) is re-seeded rather than handed a huge implicit velocity — the reference's guard against
`(0,0,0)` does not cover this, and the shell would otherwise whip.

## D7 — Resolution and the reported failure

**Decision.** `Sway.resolve : Iqm.Model -> SwayChain -> Result<SwayChainDef, string>` is pure,
and `Assets.loadActorAsset` calls it per chain, reports each `Error` through
`Diagnostics.reportf` naming the actor and the offending joint, keeps the `Ok` ones, and stores
them on the asset. Resolution happens **at load** (the IQM is loaded there; the spec requires
the report once, at load).

**Rationale.** FR-009's resolved policy (Clarifications, 2026-10-15) is drop-the-chain-and-
report, which must not fail silently and must not take the character out of the game. A
malformed chain is an *expected* failure — the constitution's own split puts that on
`Result`, not on an exception. Keeping `resolve` pure and returning the reason as a string
(rather than reporting inside it) keeps it testable without a device and keeps the actor id —
which only `Assets` knows — in the message. The validation is: a joint name the skeleton
lacks; a chain whose joints are not a parent→child **path** (each joint's skeleton parent is
the previous chain joint) — without which "the segment the bone drives" is not a real
segment; fewer than two joints; a stiffness outside `(0, 1]` (`0` is refused: it leaves no
return force at all, so FR-004 could not hold); and the caps.

**Alternatives considered.** *Drop silently* — forbidden by FR-009. *Reject the whole actor*
— the reference's robustness is right here and the constitution's Fail Loudly is satisfied by a
*loud* drop. *Report from inside `Sway`* — needs the actor id passed in and makes the pure
resolution untestable.

## D8 — Testing

**Decision.** A new `test/SwayTests.fs`, registered in `test/fsharp-game.Tests.fsproj`, built on
a fabricated `Iqm.Model` (the private `model` helper pattern in `test/ActorTests.fs`) so a chain
is simulated with no GPU and no window.

Covered: resolution accept/reject (each reason in D7); `quat_from_two_vectors` lands a segment
on its target; the applied local rotation is **invariant under a yaw of the whole actor** (the
same world motion bends the chain the same world way — SC-003 as a unit test); settling with no
motion (the chain converges to the animated pose — SC-002); the angle clamp under a large
displacement (FR-007); the teleport re-seed (FR-013); and allocation-free stepping, using the
warm-up-then-measure pattern `test/ActorTests.fs` already uses for `blendPose`.

The visible behaviour (a walking character's hair trailing) is the interactive walkthrough — see
[quickstart.md](./quickstart.md) — because it needs a GPU and a running frame loop.
