# Research: Actor Animation Slots with Weights

Decisions derived from the feature spec and the code as it stands (`world/Actor.fs`,
`world/ActorRender.fs`, `core/SlotMap.fs`, `world/Entity.fs`, `world/Assets.fs`,
`state/EditorState.fs`, `state/Command.fs`, `editor/Inspector.fs`,
`screen/GameplayScreen.fs`, and the fixtures in `test/ActorTests.fs`), with `odin-game`'s
`src/actor.odin` and `src/editor_properties.odin` as the reference.

## Decision 1 — The slots are a `SlotMap`, not an array

- **Decision**: `ActorRender.AnimationSlots : SlotMap<AnimationSlot>`, capacity `Actor.MaxAnimationSlots`
  (8), replacing the single `mutable Animation : AnimationSlot`.
- **Rationale**: the spec asks for a slot map by name, and the project already has one — a fixed capacity,
  O(1) add/free, and a `Slots : Span<Slot<'T>>` accessor that systems scan directly, which is exactly what
  the blend and the advance need. `odin-game`'s `Actor_Renderer` uses `Slot_Map(Actor_Animation_Slot, …)`
  for the same reason, and reusing the existing generic keeps the two in step. A free-list also means a
  removed clip's slot is reused rather than the map growing.
- **Alternatives considered**: a `AnimationSlot array` plus a count — no free list and no generation, so it
  re-implements half of `SlotMap` beside it; an `AnimationSlot list` — allocates on every add, and the
  blend would walk a linked structure; a plain `AnimationSlot[8]` with `Enabled` doubling as presence —
  loses the "held but contributing nothing" state a zero weight needs (spec edge case).

## Decision 2 — `Weight` and `Enabled` become `mutable`

- **Decision**: `AnimationSlot.Weight` and `.Enabled` change from immutable to `mutable`; `Frame` stays
  mutable as it already is.
- **Rationale**: the setters write in place through `&slots.[i].Value`, the same idiom the existing
  `advanceInPlace` uses for `Frame`. Making the two fields mutable keeps one mutation idiom for the struct
  rather than two, and the write is a single field store.
- **Alternatives considered**: reassigning the whole slot (`slots.[i].Value <- { slot with Weight = w }`) —
  works and allocates nothing (a struct copy), but leaves the struct with a mix of in-place-mutable and
  copy-and-update fields; a separate weights array parallel to the slots — splits the weight from the clip
  it belongs to, and every scan must index two arrays.

## Decision 3 — The blend lives in `Actor.fs`; `ActorRender` stays the wrapper

- **Decision**: `blendPose`, `advanceSlotsInPlace`, `setSlotWeight` and `removeSlot` are pure functions in
  `world/Actor.fs`; `ActorRender` holds `setClipWeight`/`removeClip`, which resolve a clip *name* to an
  index and delegate.
- **Rationale**: `world/Actor.fs` is the pure data/math module with no GPU or Zone dependency, and it is
  the only module that can be driven headlessly — every existing `ActorTests` test builds synthetic
  `Iqm.Model` records and calls into it. `ActorRender.create` needs a `Gpu.Device`, so a blend or slot
  operation living there could not be unit-tested at all. Splitting the name resolution out keeps the
  tested half pure. `core/SlotMap.fs` is neither GPU nor Zone, so it does not breach `Actor.fs`'s charter —
  the header only claims "no GPU, no Zone dependency".
- **Alternatives considered**: all of it in `ActorRender` (untestable without a device); a new module
  (`AnimationSlots.fs`) — a new file and a `.fsproj` entry for four functions that belong beside the pose
  they produce; a callback so `Actor` never sees `SlotMap` — over-engineering for one type.

## Decision 4 — The blend reads a `Span`; the advance writes one

- **Decision**: `blendPose (m) (slots: Span<Slot<AnimationSlot>>) (pose)`,
  `advanceSlotsInPlace (m) (slots: Span<Slot<AnimationSlot>>) (dt)`.
- **Rationale**: `SlotMap.Slots` already returns `Span<Slot<'T>>`, and the project's documented idiom
  (`ARCHITECTURE.md`: "`Slots` exposes the backing array as a `Span` so systems scan directly") is to scan
  it. Passing the span keeps the blend allocation-free and avoids a `SlotMap` method call per slot. The
  blend only reads, but it takes a `Span` rather than a `ReadOnlySpan`: `Slots` yields a `Span`, and a
  `ReadOnlySpan` parameter routes it through `Span.op_Implicit`, which F# flags with **warning FS3391** —
  and this project builds warning-free. `Span` is the same view at no cost, and the read-only intent lives
  in the function's comment. `Slot.Alive` and `Slot.Value` are read by copy, which is a struct copy and
  costs nothing to allocate.
- **Alternatives considered**: a `SlotMap` method (`IteriAlive`) — it passes the payload **by copy**, so a
  weight written inside the lambda would be discarded; a `byref` callback — fine, but a span scan is the
  idiom `GameplayScreen.advanceActorAnimations` already uses for exactly this.

## Decision 5 — Per joint: linear translation/scale, incremental slerp rotation

- **Decision**: joints-outer, slots-inner. For each joint, sum `translation * weight` and `scale * weight`
  and divide by `Σw`; combine rotations as an incremental running average,
  `r ← Slerp(r, rᵢ, wᵢ / (w_so_far + wᵢ))`; if no slot contributed, use the joint's bind-pose local
  (`joint.Translate/Rotate/Scale`). Then compose `World = Local * parentWorld` (or `Local` for a root) and
  `Skinning = InverseBind * World`, as `samplePose` does.
- **Rationale**: this is the reference engine's per-bone blend, and it answers the spec's rotation edge case
  — the four components of two rotations cannot be averaged linearly (that is not generally a rotation), so
  a slerp running average is used, and two clips at equal weight give the rotational midpoint. Per-joint
  *locals* mean no scratch arrays: everything accumulates in `mutable` values in the joint loop, so the
  blend allocates nothing. Weights are normalised by `Σw`, so they are relative (FR-004, spec assumption),
  and when `Σw = 0` the bind-pose fallback is exactly the state `createModel` composed (FR-008).
- **Alternatives considered**: linearly averaging quaternion components then normalising (an nlerp) —
  cheaper but biased at large angles and not what the reference does, and it can flip sign between the two
  contributing rotations; a per-joint weighted *matrix* blend — matrix blending shears under rotation and
  would not reproduce a single clip (FR-011); precomputing one blend result per clip then combining — the
  slot *is* the clip-plus-clock, so there is nothing coarser to precompute.

## Decision 6 — `samplePose` is refactored onto the shared per-joint sampler

- **Decision**: extract `sampleJointTRS` (one joint's interpolated `(translation, rotation, scale)` for one
  clip at a frame window) and `composeJoint` (the local/world/skinning triple from a joint's TRS); rewrite
  `samplePose` as a loop over joints calling both, and write `blendPose` as a loop over joints calling
  `sampleJointTRS` per contributing slot and `composeJoint` once.
- **Rationale**: the channel-sampling math (`sampleJoint`'s mask loop and `match`) is the part worth having
  once. Refactoring `samplePose` rather than duplicating it keeps the single-clip path and the blend path
  provably in step, and `023`'s golden tests plus its no-allocation test are the guard that the refactor
  changes nothing. `blendPose` recomputes the frame window per joint per slot (a modulo and a clamp); with
  ≤8 slots that is at most a few hundred extra scalar operations per actor per frame, and `023` already
  found the cost of this path is the matrix math, not the window arithmetic.
- **Alternatives considered**: leaving `samplePose` untouched and giving `blendPose` its own copy of the
  channel loop — two copies of the trickiest code in the module, drifting apart; hoisting the frame window
  per slot into a fixed-size scratch array — eight structs, and the allocation or the stack juggling is not
  worth arithmetic the compiler folds into the loop.

## Decision 7 — An added clip joins at weight 0, and a weight drag raises it

- **Decision**: the Inspector's add sets the new clip's weight to 0 — the clip joins the set and appears
  in the list, contributing nothing until the author raises its weight.
- **Rationale**: this matches the reference editor (`actor_add_animation_slot` adds at weight 0 / disabled)
  and avoids a visible jump the instant a clip is added: with the existing clip at weight 1, adding a
  second at weight 1 would suddenly halve the actor. The spec's Story 3 says the author adds a clip and
  *then* balances the weights, so a zero-weight add is the state the next step acts on. A zero weight is
  already a first-class state (FR-006, and the "zero-weight clip stays in the set" edge case).
- **Alternatives considered**: adding at weight 1 (a pose jump on every add); adding and *removing* the
  previous clip (destroys the other slot the author was balancing against).

## Decision 8 — No new editor state; the keys are ImGui-pushed, not stored

- **Decision**: the section draws a row per held clip (name, weight `dragFloat`, remove `smallButton`) and
  a `smallButton` per unheld clip to add. Nothing is remembered between frames; row widget IDs are kept
  distinct with `ImGui.pushId (clip name)` / `popId`, as `CastPanel` does for its repeated labels.
- **Rationale**: the spec's "pending add choice" entity exists only if the UI needs one; a button per
  unheld clip makes the choice immediately, so there is no half-made selection to store, and FR-018's
  "a pending add choice must not be carried from one actor to another" holds structurally — there is
  nothing to carry. This keeps `EditorState` unchanged and avoids the seed/re-point dance the name and
  prompt buffers need. It is the shape `AssetBrowser` already uses (a button per asset).
- **Alternatives considered**: a combo plus an Add button (the reference editor's shape) — needs a stored
  selection index in `EditorState` and its own re-seed rules, for no behaviour the buttons do not already
  give; a `dragFloat` per clip with `##`-suffixed labels instead of `pushId` — the same effect with string
  building on every label.

## Decision 9 — The Inspector reaches the renderer through the slot map, with no new accessor

- **Decision**: the section resolves its actor as `let actor = &zone.Actors.Get(handleOf ref)` and passes
  `&actor.Renderer` to the `ActorRender` verbs; `Entity.handleOf` is already public, and `Actor.Renderer`
  is already public and mutable.
- **Rationale**: this is exactly the idiom `Systems.animate` uses
  (`let actor = &zone.Actors.Get zone.Player.Value` then `&actor.Renderer`), so it is proven and needs no
  new surface. The Outliner already reads `zone.Actors.Slots` directly, so a panel touching the actor slot
  map is not a new coupling. The section only runs for a live actor selection, which the Inspector has
  already established with `Zone.tryEntityAt`, so `Get` cannot be stale here.
- **Alternatives considered**: a `Zone.actorRenderer : Zone -> EntityRef -> byref<ActorRender>` — a
  byref-returning function with exactly one caller, tangled in F#'s byref lifetime rules; a
  `Zone.withActorRenderer` callback — more machinery for the same one call site.

## Decision 10 — A direct write: no command, no persisted field

- **Decision**: the Inspector's slot edits record no `Command`, add no `EditorState` field, and add no
  field to `ActorState`. `ActorRender.create` seeds one slot, and `Zone.occupyActor`/`spawnActor` (which
  call it) therefore rebuild an actor with the default single slot.
- **Rationale**: the command model carries an actor's **authored value** — `ActorState`, which
  deliberately holds no runtime state (`world/Zone.fs`: "a payload is a different type from the entity it
  came from, so it cannot hold a body, a renderer, or a runtime flag") — and the slots live with the
  renderer, which a restore rebuilds. There is nothing a command is allowed to carry, so a command is not
  available. This is FR-019, and the same stance as the cast binding (D-022) and zone registry (D-023),
  the two other direct writes. It also means slot edits survive neither save (animation is not persisted at
  all today) nor an undo that rebuilds the actor.
- **Alternatives considered**: adding `Slots : …` to `ActorState` so the set is authored and undoable —
  that is a materially larger feature (a serialized field, a kind-specific command, an `occupyActor` that
  restores it, and a persistence decision), and the spec explicitly puts persistence out of scope; a
  command carrying the runtime renderer state — forbidden by the classification above.

## Decision 11 — `playClip` survives as a hard switch over the slots

- **Decision**: `playClip (r) (name)` becomes "add or find the named clip's slot, set it to weight 1,
  enable it, and zero every other slot's weight" (leaving the other slots held). `Systems.animate` is
  unchanged.
- **Rationale**: FR-013 defers the idle↔walk cross-fade, so the existing hard switch must keep working
  exactly as it does today; expressing it over the slots keeps one storage model and one entry point.
  Leaving the other slots held (rather than removed) means a later cross-fade finds them ready, and
  matches the reference engine, which zeroes weights rather than freeing slots.
- **Alternatives considered**: deleting `playClip` and having `Systems.animate` set weights directly —
  that is the cross-fade this feature defers; removing the losing slots on switch — churn for no benefit
  and it loses a clip the author set up in the Inspector.

## Decision 12 — `create` seeds one slot at weight 1, or none

- **Decision**: `ActorRender.create` builds `SlotMap<AnimationSlot>(uint MaxAnimationSlots)` and adds one
  slot — "Idle" if the asset defines it, else the first clip, else nothing — at weight 1.
- **Rationale**: preserves the current single-clip behaviour exactly (FR-011, SC-002): an actor spawned
  today plays Idle at full weight, and after this change it holds one slot at full weight, which the blend
  reproduces identically. When the asset defines no clips, an empty map poses the bind pose (FR-008),
  which is better than the current code's zeroed joint transforms.
- **Alternatives considered**: seeding every clip at weight 0 except Idle — puts clips in the Inspector's
  list the author did not add, which reads as "these are blended" when they are not; seeding none and
  letting callers add — breaks every existing actor's idle-on-spawn.

## Decision 13 — Tests extend `ActorTests`; the panel and the wrappers are quickstart

- **Decision**: extend `test/ActorTests.fs` with the blend, the slot algebra, the bind-pose fallback and
  the allocation check. Do not add a test file, and do not unit-test `ActorRender.setClipWeight`/
  `removeClip` or the Inspector section.
- **Rationale**: `ActorTests` already has the headless fixtures (`model`, `translationModel`,
  `createPoseFrame`, `createSlot`) and drives the pure functions directly; the logic this feature adds is
  there, so it is all reachable without a GPU. The renderer wrappers need a `Gpu.Device` to build an
  `ActorRender`, and the panel needs a running ImGui frame; both are the manual half, as `035` recorded for
  its panel lines. The no-allocation check mirrors the existing `samplePose allocates nothing` test so
  FR-012 is measured, not assumed.
- **Alternatives considered**: a `test/ActorRenderTests.fs` with a device fixture — no such fixture exists
  and building one for two name-resolving wrappers is not worth it; asserting the Inspector through a
  headless ImGui — the binding has no such harness.
