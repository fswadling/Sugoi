# Feature Specification: Actor Sway Bones

**Feature Branch**: `091-actor-sway`

**Created**: 2026-10-15

**Status**: Draft

**Input**: User description: "return to the actor renderer — port odin-game's hair sway system: define sway bones in an actor's manifest and make the chains sway with motion." Wind is explicitly out of this port (the port has no zone wind yet).

## Origin

`odin-game` has a bone-chain sway system: `src/sway.odin` and the plan in
`docs/SWAY_BONES_PLAN.md`. It exists so hair — and, generically, any loose chain of bones
— behaves like a stiff elastic rope: anchored at the root, swaying with inertia and
gravity, and pulled back toward the animation pose by a per-chain `stiffness`. Chains are
declared in the actor manifest (`assets/actors/<id>/actor.json`, the `sway_chains` array —
Akane declares twelve `J_Sec_Hair*` chains), resolved by joint name against the skeleton,
and simulated **on the CPU**, rewriting the animated pose's per-joint rotations before
skinning:

> "All simulation runs on the CPU, modifying `Actor_Renderer.blended_pose_frame` after the
> animation system and before rendering, so the existing GPU skinning picks up sway
> automatically." — `odin-game/docs/SWAY_BONES_PLAN.md`

**This is not a straight port.** Three defects in the reference were isolated while
scoping this feature, by re-running the reference's own maths against the same `raylib`
binding it uses (`src/sway.odin`'s helpers plus the binding's column-vector
`Vector3Transform`):

1. **The parent frame is inverted.** The reference's `matrix_to_quat` returns the
   *conjugate* of the rotation its matrix encodes — verified
   `matrix_to_quat(QuaternionToMatrix(q)) == conj(q)` for the three axes and a composite.
   It is fed the parent bone's world matrix, so the local delta is conjugated by the wrong
   frame (`P · D · P⁻¹` where the formula means `P⁻¹ · D · P`). The visible symptom is
   exactly "the sway goes the wrong way": the bend mirrors whenever the parent bone's world
   rotation is not aligned with the sway plane — invisible while the head is upright, and
   wrong as soon as the character turns.
2. **Off by one joint.** Each bone's delta is derived from the segment *into* it
   (`points[i-1] → points[i]`), but a joint's rotation drives the segment to its *child*
   (`points[i] → points[i+1]`). Confirmed against the reference's own composition (`world
   = parent · T · R · S`): rotating joint *i* moves joint *i+1*, never *i*. The bend lands
   one joint too far down the chain, and the chain's first bone is never rotated, so the
   topmost segment never bends at all.
3. **Parents are not re-derived.** Every delta is computed from the rest-pose world
   matrices and then multiplied into a local rotation, so a child's delta ignores that its
   parent has already swayed; the lower segments over-rotate.

Measured on a 25°-per-segment bend with a parent rotation about a non-parallel axis, the
per-segment direction error reaches ~24° and the tip is off by ~0.16 world units. This
feature therefore ports the reference's *model* (manifest-declared chains, a Verlet rope
anchored to the animation pose) and not its arithmetic.

The port already has the pieces the reference relies on: IQM joint names and parent
indices (`core/Iqm.fs`), a blended per-joint pose built every frame (`world/Actor.fs`,
`Actor.blendPose`), and a bone palette uploaded per instance before the render pass
(`world/ActorRender.fs`, `ActorRender.upload`). Sway is a pass over that pose, between the
blend and the palette upload. The skinned draw itself — `shaders/mesh.vert`, the cel
surface and the outline hull (specs 040, 041) — is untouched.

## Clarifications

### Session 2026-10-15

- Q: When a declared chain names a joint the actor's skeleton does not have, is the *chain* dropped or the *actor* rejected at load? → A: The chain is **dropped and reported once at load**; the actor still spawns and renders, without that chain (FR-009). Chosen for reference parity while keeping the failure loud: a typo costs the effect and prints a diagnostic, never the character. The same policy covers a malformed chain — a missing or out-of-range stiffness, a chain with fewer than two joints, and a chain over a cap (FR-010, FR-012).
- Q: Is `stiffness = 0` a legal authoring value? → A: **No** — the range is `(0, 1]` (FR-010). At `0` the spring term vanishes, so nothing would pull the chain back to the animation pose and FR-004 could not hold — the failure mode would be a chain that quietly never settles. The reference allows `0..1`; this is the one place this port narrows it (Key Entities, Assumptions).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Hair sways with the character's motion (Priority: P1)

A character with a declared hair chain walks. As it moves, the hair trails behind the
direction of travel; when it stops, the hair settles back into the animation pose. Turning
on the spot swings it out and it settles again.

**Why this priority**: this is the capability. Until a chain visibly lags motion and
returns to rest, nothing else about the feature can be judged. It is also where the
reference's known defects would show, so getting the direction right is part of this story,
not a refinement of it.

**Independent Test**: play a walking/turning actor that declares a hair chain and confirm
the chain trails opposite the direction of travel and settles once the actor stops.

**Acceptance Scenarios**:

1. **Given** an actor with a declared chain standing still, **When** it starts to walk,
   **Then** the chain lags behind the travel direction as it is dragged along.
2. **Given** the same actor walking, **When** it stops, **Then** the chain settles back to
   its animated pose within a short time and then holds it (no persistent drift, no
   oscillation that never decays).
3. **Given** the actor turning in place, **When** it turns, **Then** the chain swings out
   and settles again once the turn stops.
4. **Given** two identical actors at yaws 180° apart moving in the *same world* direction,
   **When** both render, **Then** their chains bend the same way in the world (the
   reference's mirrored-frame defect must not be reproduced).
5. **Given** a chain of several joints, **When** it is displaced, **Then** the chain bends
   along its length — every segment turns, including the one leaving the first (hair-root)
   joint — rather than starting one joint down (the reference's one-joint offset must not be
   reproduced).

---

### User Story 2 - Chains are authored per actor, and undeclared actors are unchanged (Priority: P2)

An author declares sway chains in an actor's manifest: an ordered list of the actor's own
joint names, root to tip, plus how stiff the chain is. An actor that declares none renders
exactly as it does today, and adding sway to one actor cannot change another.

**Why this priority**: this is what makes the feature usable rather than a hard-coded
effect, and it is the boundary that keeps the change from leaking into every actor. It
ranks below Story 1 because a single hard-coded chain would still demonstrate the
capability.

**Independent Test**: declare a chain on one actor and none on another; confirm the first
sways and the second is unchanged, and that two instances of the same actor swaying in the
same scene are independent (one settling does not disturb the other).

**Acceptance Scenarios**:

1. **Given** an actor whose manifest declares no chain, **When** it animates and moves,
   **Then** it renders exactly as it did before this feature.
2. **Given** an actor whose manifest declares two chains, **When** it animates, **Then**
   both chains sway independently (each settles on its own; neither drives the other).
3. **Given** two instances of the same actor asset in the scene, **When** only one moves,
   **Then** only that instance's chains sway; the still instance's chains hold their pose.

---

### User Story 3 - The sway is stable, bounded and play-only (Priority: P3)

The sway is well-behaved: it never folds or inverts a chain, it cannot be driven into an
unstable state by a long frame or a bad authoring value, and it is an effect of *playing* —
an actor standing in the editor holds its animated pose.

**Why this priority**: this is what keeps the feature from being a source of visual bug
reports. It ranks last because the common case (a short chain, a steady frame rate) works
without it, but a fast-moving actor on a hitching frame is exactly where a rope simulation
misbehaves.

**Independent Test**: drive an actor with a declared chain through a long frame and a
teleport; confirm the chain neither explodes nor yanks, and that the editor's pose is
unaffected.

**Acceptance Scenarios**:

1. **Given** a chain of the maximum supported length, **When** it is displaced, **Then**
   the chain does not fold over itself or invert (no segment rotates past the bounded
   angle from its animated pose).
2. **Given** a large frame step, **When** the actor moves, **Then** the chain stays within
   its angle bound and settles afterwards (no runaway).
3. **Given** the actor is teleported (a zone switch, a spawn, or an authored move that
   changes its position in one frame by more than a short distance), **When** the next
   frame simulates, **Then** the chain re-seeds at its animated pose rather than taking a
   large inertia impulse.
4. **Given** the editor is open and the actor is not playing, **When** the actor is
   positioned or its transform is edited, **Then** its chains hold the animated pose with
   no sway.

---

### Edge Cases

- **An actor with no declared chains**: the whole feature is inert for it; the frame is
  unchanged and nothing is simulated.
- **A chain whose first joint is the looseness's attachment** (the scalp): the chain's
  first joint is anchored *in position* to the animation pose, but the chain is still free
  to bend along its whole length (see Story 1, scenario 5).
- **A chain naming a joint the actor's skeleton does not have** (a typo, or a chain copied
  from another actor): the chain is dropped and reported once at load; the actor still
  spawns and renders, without it (FR-009).
- **A malformed chain** (fewer than two named joints, which cannot form a segment, a stiffness
  of `0`, or a stiffness above `1`): dropped and reported on the same terms (FR-010).
- **A chain longer than the supported maximum**, or an actor declaring more chains than the
  supported maximum: a manifest must not be able to exhaust memory; the offending chain is
  dropped and reported on the same terms (FR-012).
- **A still frame (`dt = 0`)**: nothing advances; no divide-by-zero, no NaN in the pose.
- **A teleport**: handled explicitly by the re-seed requirement (Story 3, scenario 3),
  because the inertia term reads the change in the actor's position between frames.
- **The actor's animation changes mid-sway** (a clip switch, a blend, a morph): the chain
  keeps settling against the *new* pose; the sway never writes animation state.
- **An actor scaled or rotated arbitrarily in the editor**: the sway follows the actor's
  transform; it is applied in world space, not the actor's local frame.
- **An actor removed from the zone while swaying**: nothing is left simulating it.
- **Several chains on one actor** (Akane has twelve hair chains): each is independent; the
  cost is their sum.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An actor MUST be able to declare, in its own manifest, one or more **sway
  chains**. A chain is an ordered list of that actor's joint names, from the chain's root
  to its tip, together with a **stiffness**. This is authored data in the project's
  existing manifest form (the same form the actor's portraits and morphs already use).
- **FR-002**: An actor that declares no chain MUST render and behave exactly as it does
  today.
- **FR-003**: A declared chain MUST sway with the actor's motion: as the actor translates
  or turns, its response MUST trail the motion — the chain is dragged *opposite* the
  direction of travel, and does not ride ahead of the pose. A rope is a pendulum, so the
  swing then carries it forward and back within the bound of FR-007 before it settles; an
  inertia is a trailing response, not a chain that never moves ahead of the pose.
- **FR-004**: A chain MUST settle once the actor stops moving, within a short settling
  time: the oscillation decays to nothing and the chain then holds, hanging under its own
  weight at a small steady offset from the animated pose. It settles *toward* the pose, not
  exactly onto it — gravity is always on, so a chain sags by about `g·dt² / (stiffness ×
  SwaySpringScale)` per point rather than resting on the pose, and how much of that sag is
  visible is what the stiffness is tuned for.
- **FR-005**: The sway MUST be correct in world space, independent of the actor's facing: an
  actor at any yaw carrying a given chain and moving in a given world direction MUST bend
  in the same world direction as any other actor doing the same. The reference's
  mirrored-frame defect (Origin, defect 1) MUST NOT be reproduced.
- **FR-006**: Every **segment** of a declared chain MUST bend, and the chain MUST NOT start
  bending one joint down (Origin, defect 2). A chain of *n* joints has *n−1* segments, each
  driven by the joint it leaves — so every joint but the last is rotated, and the segment leaving
  the **first** (the scalp) joint turns like the rest. The last joint drives no segment of its
  own and is therefore never rotated; its world orientation still turns, carried by its parent.
  The first joint is anchored in *position* to the animation pose — it is the rotation, not the
  position, that is free.
- **FR-007**: The sway MUST be bounded: no chain joint may deviate from its animated pose
  by more than a fixed maximum angle (bounded so a long or fast-driven chain cannot fold
  over itself or invert), and a large frame step MUST NOT drive the chain outside that
  bound.
- **FR-008**: The sway MUST be a **play-mode** effect. While the editor is open and the
  actor is not playing, chains MUST hold the animated pose.
- **FR-009**: A chain that cannot be resolved against the actor's skeleton — a joint name
  the actor does not carry — MUST be **dropped and reported once at load**, naming the
  actor and the unresolved joint; the actor MUST still load and render, without that chain.
  This MUST NOT fail silently, and MUST NOT prevent the actor, or any other actor sharing
  the asset, from spawning.
- **FR-010**: A chain MUST declare a stiffness greater than `0` and at most `1`. A manifest
  that omits it, gives `0`, or gives a value above `1` MUST be reported and the chain dropped on
  the same terms as FR-009, rather than silently defaulted — `0` is refused because it leaves no
  force pulling the chain back to the animation pose (FR-004), and `1` is the most damped setting.
  A chain with fewer than two joints names no segment and MUST be dropped and reported the same
  way.
- **FR-011**: The sway MUST NOT write animation state: which clips an actor holds, their
  weights and their clocks are unchanged by it. It is a modification of the pose *after*
  blending, picked up by the existing skinning.
- **FR-012**: The feature MUST be bounded and MUST NOT allocate per frame. A manifest MUST
  NOT be able to exhaust memory: the number of chains per actor and the number of joints
  per chain are each capped, and a chain exceeding a cap is reported and dropped on the
  same terms as FR-009 rather than accepted.
- **FR-013**: A teleport — the actor's position changing by more than a short distance in a
  single frame (a zone switch, a spawn, an authored move) — MUST NOT yank the chain: the
  chain MUST re-seed at its animated pose instead of receiving a large inertia impulse.
- **FR-014**: The rendering path MUST be unchanged: no shader change, the same per-instance
  bone palette, and the same cel-shading (spec 040) and outline (spec 041) passes. The sway
  affects what the palette *contains*, not how it is drawn.
- **FR-015**: All existing behaviour MUST be unchanged: environment, prop, portrait, UI and
  editor rendering, gameplay and editor behaviour, and the existing test suite MUST pass with
  its assertions unmodified. (A required new field on a record a fixture builds is not an
  assertion change: the fixture's construction gains the field and every assertion stands.)
- **FR-016**: `dotnet build` MUST be clean and `dotnet test` MUST pass, including new tests
  for the pure sway maths (the rotation that maps an animated segment direction onto its
  swayed direction must be exact; a chain driven by no motion must converge).

### Key Entities *(include if feature involves data)*

- **Sway chain (authored)**: one entry in an actor's manifest — an ordered list of joint
  names (root to tip) and a stiffness. Authored per actor; human-written.
- **Sway chain definition (shared per asset)**: a chain resolved against the actor's
  skeleton — the joint indices the names resolve to, the stiffness, and whatever the
  stiffness derives. Shared by every instance of the asset, resolved once.
- **Sway chain runtime (per instance)**: the simulated state of one chain for one actor in
  the scene — the points it is made of (each bound to a joint, with the world position it
  rests at in the current pose, its current and previous simulated positions, and its rest
  distance to the previous point) plus the anchor position it was last seeded at, which is
  what a teleport is measured against. Not persisted and not authored.
- **Animated pose**: the blended per-joint transforms produced every frame for an actor
  (existing). The sway modifies these before they become the bone palette.
- **Stiffness**: the authored value in `(0, 1]` — how strongly a chain is pulled back to the
  animated pose and how quickly its oscillation decays. `0` is not a legal value: it leaves no
  return force at all, so the chain would never settle back to the pose (FR-004).

## Success Criteria *(mandatory)*

### Outcomes

- **SC-001**: Playing a walking, turning actor with a declared chain, the chain's response
  trails the motion — it is displaced opposite the direction of travel on the inertial
  response and sits behind the pose on average — and no joint exceeds the maximum deviation
  angle in a steady walk (0 clamped joints).
- **SC-002**: After the actor stops, every chain joint is within a small angular tolerance
  of its animated pose within the settling time, and stays there (0 joints drifting
  afterwards).
- **SC-003**: Across a full turn of the actor, chains bend in the correct world direction
  in 100% of sampled yaws (0 mirrored cases) — the reference's defect is absent.
- **SC-004**: Every chain **segment** turns: over a displacement, 100% of a chain's segments
  change their world direction (*n−1* of an *n*-joint chain — 0 segments that never move), the
  segment leaving the first joint included.
- **SC-005**: An actor with no declared chain is unchanged: its pose and rendered frame are
  identical to before this feature (0 difference).
- **SC-006**: 0 bytes allocated per frame by the sway; two instances of the same asset sway
  independently (0 cross-talk); `dotnet build` is clean and `dotnet test` passes with the
  existing assertions unmodified.
- **SC-007**: A chain driven by a single large frame step stays within the maximum deviation
  angle and settles afterwards (0 runaway), and a teleport produces 0 frames of yank.

## Assumptions

- **Wind is out of scope.** The port has no zone wind, so this feature carries no wind term
  and the manifest carries no wind field. The reference's `affected_by_wind` is *dropped*,
  not defaulted to false, so a later wind feature adds one field rather than changing the
  meaning of an existing one.
- **The reference's physics model is the model.** A Verlet rope of world-space points,
  anchored to the animated pose, pulled back by the stiffness, with distance constraints
  holding each segment's rest length, then applied to the pose as per-joint rotations. Its
  look is the starting point; its constants may be re-tuned, and its three defects are
  corrected rather than carried (Origin).
- **The stiffness maps to return-pull and damping**, as in the reference: a higher stiffness
  returns to the animated pose faster and oscillates less; a lower one is springier. The range is
  `(0, 1]` rather than the reference's `0..1`: at `0` the return force vanishes, so a chain could
  never settle back to the animated pose (FR-004).
- **The mechanism is generic; hair is the first consumer.** A "sway chain" is any chain of
  joints (hair, tail, coat); Akane's hair chains are authored as part of this feature, since
  otherwise there is nothing to see.
- **Only skinned IQM actors.** The world's other entities (props, environment parts) have no
  skeleton and are unaffected.
- **Sway is cosmetic.** It has no effect on collision, picking, capsule placement, camera
  follow or any gameplay system; the actor's collider and transform are unchanged.
- **Sway state is per instance and unpersisted.** It is regenerated on load, is not
  authored, and is not an undo step.
- **Play-only, matching the reference** (`SWAY_BONES_PLAN.md`: "Edit mode does not run sway
  — it's a play-only effect").
- **No blend/transparency change.** The sway moves existing geometry; it does not add a
  render pass or change material behaviour.
- **The per-frame budget is negligible.** Chains are short (Akane's longest is five joints)
  and few (twelve on Akane), so the simulation is a handful of vector operations per joint
  per frame.
