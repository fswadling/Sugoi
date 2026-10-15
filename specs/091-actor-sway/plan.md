# Implementation Plan: Actor Sway Bones

**Branch**: `091-actor-sway` | **Date**: 2026-10-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/091-actor-sway/spec.md`

## Summary

Give manifest-declared bone chains — hair first — a motion-driven sway: a Verlet rope anchored
to the animated pose, applied as per-joint rotations after the pose is blended and before it
becomes the bone palette. Wind is out (the port has no zone wind). The reference's *model* is
ported; its three arithmetic defects are not (spec Origin; research D3).

Four decisions carry the feature:

- **It is a pose pass, not a shader.** The port already builds an actor's pose every frame and
  uploads it as the bone palette (specs 036/037/040/041), so sway changes *what the palette
  contains*. `shaders/mesh.vert`, the cel pass and the outline pass are untouched (FR-014) —
  the user's "apply sway in the shaders" is not how the reference does it and is not what is
  required here.
- **The actor's per-frame pose step moves from the render phase to the update phase**
  (research D1). Sway needs the frame's `dt` and the actor's world transform; the copy-pass
  loop that builds the pose today has neither. It lands at the *end* of `update`, not between
  animation and triggers where the reference's loop puts sway — the blend has to see the weights
  the cue has just set (research D1's second paragraph, and `contracts/sway.md`).
- **The applied rotation is closed-form per chain and reproduces the simulated chain exactly**
  (research D3). Because a joint's own rotation never moves that joint, anchoring is automatic;
  and because each segment's target direction is known up front, no intermediate
  recomposition is needed — the chain's reconstructed positions equal the simulation's.
- **Inertia comes from the anchored root alone** (research D5), not the reference's extra
  movement term (a stated non-goal there, frame-rate dependent, and it double-counts the lag
  the anchoring already produces).

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).

**Primary Dependencies**: **none new.** `System.Numerics` (`Quaternion`, `Matrix4x4`), the
existing IQM parser (`core/Iqm.fs`, which already reads joint **names** and **parents**), and
Prime's `scvalue` for the manifest (the same `[<SymbolicExpansion>]` array-of-records shape
spec 037's morph manifest already uses).

**Storage**: one new authored manifest field (`Sway`, an array of chains) on the actor
manifest. No new file, no new binary sidecar, nothing persisted.

**Testing**: a new `test/SwayTests.fs`, pure and device-free — chain resolution (a joint name
the skeleton lacks, a chain that is not a parent→child path, and a chain over a cap are each
refused with a message); the delta maths (a segment lands *exactly* on its target direction;
the applied local rotation is invariant under a yaw of the whole actor — this is SC-003 as a
unit test); settling (no motion ⇒ the chain converges to the animated pose); the angle bound;
the teleport re-seed. A fabricated `Iqm.Model` (the way `test/ActorTests.fs`'s private `model`
helper does) lets a chain be simulated with no GPU.

**Target Platform**: Linux x64 desktop, SDL3 + SDL_gpu.

**Project Type**: single-project native desktop game with a docked ImGui editor.

**Performance Goals**: hold 60 FPS. Per actor per frame the cost is
`chains × (points × a few vector ops)` for the simulation plus **one extra full pose compose**
(sway writes rotations, then the pose is recomposed) — the same O(joints) order as the blend
that already runs. Zero per-frame allocation (FR-012): chain points, pose arrays and the
palette buffers are all allocated at load/creation and mutated in place.

**Constraints**: manifest-declared chains only (FR-001); undeclared actors byte-identical
(FR-002); trails motion (FR-003) and settles (FR-004); world-space correct at any yaw (FR-005);
the whole chain bends, anchored in position only (FR-006); bounded deviation (FR-007);
play-only (FR-008); drop-and-report at load for a bad chain (FR-009/010/012); writes no
animation state (FR-011); no per-frame allocation and capped (FR-012); teleport re-seeds
(FR-013); rendering path unchanged (FR-014); everything else unchanged (FR-015); clean build
and green tests (FR-016).

**Scale/Scope**: one new `world/` module (`Sway.fs`); `world/Actor.fs` gains the blended TRS on
`PoseFrame` and a `composeAll`; `world/ActorAsset.fs` carries the resolved chains;
`world/Assets.fs` parses and resolves them; `world/ActorRender.fs` gains the per-instance sway
state and the `simulate` step, and its `upload` becomes palette-only; `screen/GameplayScreen.fs`
moves the pose step into the update chain; one actor manifest gains its hair chains;
`.fsproj` gains the new module and the test fsproj the new test file.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one new module, one new authored field, no new dependency, no new pass, no new target. Two existing surfaces move rather than grow: the pose step moves phase (research D1) and `upload` shrinks to what its name says. The chain count/point count are capped rather than unbounded (FR-012). |
| II. Fail Loudly | Pass — a chain that cannot resolve is an **expected** failure and uses `Result` (constitution's own split), reported at load and dropped; the actor still loads (FR-009, resolved in Clarifications). No silent default: a missing or out-of-range stiffness is a chain failure, not a default (FR-010). |
| III. Spec-First | Pass — `specs/091-actor-sway/spec.md` is written, clarified and validated, and drives this plan. |
| IV. Data-Oriented Architecture | Pass — `[<Struct>]` points and parallel arrays; the runtime state is arrays of structs mutated in place; no object graph, no per-frame record rebuild. The pose stays struct-of-arrays (three more parallel arrays, not a new object). |
| V. Minimise GC Pressure | Pass — every array is allocated at load or at renderer creation; the per-frame path mutates in place and allocates nothing (FR-012), which is asserted by the existing allocation-counting test style (`test/ActorTests.fs`). |
| N-V. Testing is the Authoring Tool | Not engaged — narrative subsystem, untouched. |

## Complexity Tracking

None. Nothing here is a constitution violation or a new dependency. The one structural change
(the pose step moving from the render phase to the update phase) removes a latent mismatch —
a simulation that needed frame time living in a phase that does not have it — rather than
adding one.

## Project Structure

### Documentation (this feature)

```text
specs/091-actor-sway/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── sway.md          # Phase 1 output — the surfaces that change
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
world/
├── Sway.fs              # NEW: the authored chain (`SwayChain`), the resolved def
│                        #   (`SwayChainDef`), the per-instance state (`SwayState`, `SwayPoint`),
│                        #   `Sway.resolve` (names → a parent→child path), and the simulation +
│                        #   apply (`Sway.apply`). Pure of GPU/screen/zone.
├── Actor.fs             # CHANGED: `PoseFrame` gains the blended TRS (Translation/Rotation/Scale)
│                        #   written by `composeJoint`; new `composeAll` recomposes the pose from it.
│                        #   (Also the correction that makes this port's arithmetic right: the parent
│                        #   world rotation is the *posed* one, so no matrix→quaternion helper exists.)
├── ActorAsset.fs        # CHANGED: `ActorAsset` carries the resolved `SwayChains`.
├── Assets.fs            # CHANGED: the actor manifest gains `Sway`; `loadActorAsset` resolves each
│                        #   chain, reports and drops a bad one, and stores the rest.
└── ActorRender.fs       # CHANGED: `[<Struct>] ActorRender` gains its `Sway` state, built in
                         #   `create`; new `simulate` (clocks + blend + sway, play-gated); `upload`
                         #   becomes the palette upload only.
screen/
└── GameplayScreen.fs    # CHANGED: the per-actor pose step runs at the end of `update` (edit and
                         #   play, after the play systems and `CueEngine.tick`), passing `frame.Dt`
                         #   and whether play is on; `renderScene` only uploads.
assets/actors/Akane/
└── akane.manifest       # CHANGED: the hair chains, resolved against Akane.iqm's
                         #   `J_Sec_Hair*` joints (53 exist — spec Origin).
fsharp-game.fsproj       # CHANGED: `world/Sway.fs` between `world/Actor.fs` and
                         #   `world/ActorAsset.fs` (the asset holds the callable def type).
test/
├── SwayTests.fs         # NEW: resolution, the delta maths, settling, the bound, the re-seed.
└── fsharp-game.Tests.fsproj  # CHANGED: register `SwayTests.fs` (after `ActorTests.fs`).
```

**Structure Decision**: the feature lives in one new core-of-the-world module, `world/Sway.fs`,
because the simulation is neither a screen nor a zone concern: it takes a model, a pose and a
world transform and rewrites the pose. `world/Actor.fs` owns the pose, so the one thing sway
needs from it — the per-joint rotation it may write — is added there (the blended TRS plus a
recompose), keeping one owner of the pose's shape. The manifest field rides the existing actor
manifest and the existing `[<SymbolicExpansion>]` parse path, so no new format is introduced.
The sway state is per instance on `ActorRender`, where the rest of an actor's per-instance
render state already lives, and is freed with it. Nothing in the environment/prop static path,
the shaders, or the editor's own panels changes.

## Phase 0: Research

See [research.md](./research.md). It settles where the simulation runs and how it gets the
frame time (D1); how per-joint rotations travel through a matrices-only pose (D2); the exact,
yaw-correct delta arithmetic that replaces the reference's three defects, and why it reproduces
the simulated chain (D3); the rope itself (D4); where inertia comes from and why the reference's
extra term is dropped (D5); the caps and the bound (D6); and where resolution/reporting lives so
the failure stays loud without making a character unspawnable (D7).

## Phase 1: Design

- [data-model.md](./data-model.md) — the authored chain, the resolved def, the runtime state and
  points, the pose's new TRS, and the validation rules.
- [contracts/sway.md](./contracts/sway.md) — every surface that changes, and what explicitly does
  not (the shaders above all).
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite and the walkthrough.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one module, one field, one moved step, one shrunken function; no new dependency/pass/target/shader. |
| II. Fail Loudly | Pass — `Result` for the expected manifest failure, reported and dropped; no silent default for a stiffness. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — `[<Struct>]` points, parallel arrays, in-place mutation, no per-frame record rebuild. |
| V. Minimise GC Pressure | Pass — load-time allocation only; the per-frame path is allocation-free. |
| N-V. Testing is the Authoring Tool | Not engaged. |
