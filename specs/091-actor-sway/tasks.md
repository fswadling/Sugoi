# Tasks: Actor Sway Bones

**Input**: Design documents from `/specs/091-actor-sway/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/sway.md](./contracts/sway.md), [quickstart.md](./quickstart.md)

**Tests**: Included, and required by the spec (FR-016). The arithmetic and the manifest rules are
pure, so they are asserted headlessly against a fabricated `Iqm.Model` (the private `model` helper
pattern in `test/ActorTests.fs`); the visible behaviour is the walkthrough in
[quickstart.md](./quickstart.md).

**Organization**: the mechanism here is **indivisible** — the pose can carry no rotations until
`PoseFrame` has them, nothing can be authored until the manifest field is parsed, and nothing
sways until the simulation exists and is called — so the implementation lives in Setup and
Foundational, one phase of it (US1) makes it visible, and US2/US3 carry the proofs for their
facets plus any gap they find. This mirrors how `specs/090-sound-cues/tasks.md` organised a
change that could not land one arm at a time. The checkpoints are still real increments: after
Foundational a chain is authored and resolved but nothing moves; after US1 hair sways.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1, US2, US3 (maps to spec.md)
- Exact file paths in every description

## Path Conventions

Single F# project at the repository root. This feature adds **one module** (`world/Sway.fs`) and
**one test file** (`test/SwayTests.fs`), each with one `.fsproj` entry, and edits
`world/Actor.fs`, `world/ActorAsset.fs`, `world/Assets.fs`, `world/ActorRender.fs`,
`screen/GameplayScreen.fs` and `assets/actors/Akane/akane.manifest`. No shader, no render file, no
editor file, no dependency.

---

## Phase 1: Setup

**Purpose**: The pure module — its shape and its validation — and its place in the compile order.

- [X] T001 Create `world/Sway.fs` with the module doc (what sway is, that it is a pose pass and not a shader, and that the reference's three defects are corrected here) and the five records from [data-model.md](./data-model.md): `SwayChain`, `SwayChainDef`, `SwayPoint`, `SwayChainState`, `SwayState`, plus the constants listed in [contracts/sway.md](./contracts/sway.md) (`MaxSwayChainsPerActor 16`, `MaxSwayJointsPerChain 16`, gravity, the damping pair, the spring scale, the constraint iterations, `SwayMaxAngle`, `SwayTeleportDistance`). No logic.
- [X] T002 In `world/Sway.fs`, add `resolve : Iqm.Model -> SwayChain -> Result<SwayChainDef, string>` implementing the six validation rules in order ([data-model.md](./data-model.md)) — at least two joints; every name found in the model; a parent→child path; stiffness in `(0, 1]` (`0` is refused: it leaves no return force, FR-004); the joint cap — each returning `Error` naming what failed; `createState : SwayChainDef array -> SwayState`; and the private helpers `quatFromTwoVectors` (the standard Hamilton `from→to` quaternion, with the near-parallel and near-opposite guards) and the angle clamp. (FR-009/FR-010/FR-012; research D6/D7; contract)
- [X] T003 In `fsharp-game.fsproj`, insert `<Compile Include="world/Sway.fs" />` between `world/Actor.fs` and `world/ActorAsset.fs` (the asset carries `SwayChainDef`; `Sway` names `PoseFrame`/`ActorModel`). `dotnet build` clean.

**Checkpoint**: the module compiles and is registered; nothing calls it yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The pose can carry rotations, and an actor can declare chains — without either, no
story can be built.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 In `world/Actor.fs`, add `Translation : Vector3 array`, `Rotation : Quaternion array`, `Scale : Vector3 array` and `WorldRotation : Quaternion array` to `PoseFrame`, allocate them in `createPoseFrame`, write them in `composeJoint` (it already receives `t`, `r`, `s`; `WorldRotation` is the parent's world rotation composed with the local one, or the local one at a root), and add `composeAll : ActorModel -> PoseFrame -> unit` that re-runs `composeJoint` for every joint from the stored TRS. (research D2; FR-011) — `dotnet test` with `test/ActorTests.fs` **unmodified** green (it asserts the composed matrices).
- [X] T005 In `world/ActorAsset.fs`, add `SwayChains : Sway.SwayChainDef array` to `ActorAsset` — resolved once, shared by every instance, `[||]` when the manifest declares none (the same treatment `Morphs`/`Portraits` already get). (FR-001/FR-002)
- [X] T006 In `world/Assets.fs`, add `Sway : Sway.SwayChain array` to the private `Manifest`; in `loadActorAsset`, resolve each entry through `Sway.resolve`, report each `Error` once with `Diagnostics.reportf` (naming the actor and the unresolved joint), drop that chain, keep the `Ok` ones, and apply `Sway.MaxSwayChainsPerActor` — the actor still loads and renders. (FR-009/FR-010/FR-012; research D7) — depends on T002, T005.
- [X] T007 [P] In `assets/actors/Akane/akane.manifest`, add the `Sway` array: Akane's hair chains, formed from `Akane.iqm`'s `J_Sec_Hair*` joints as parent→child paths, stiffness ≈ 0.15. (FR-001; spec Origin/Assumptions)
- [X] T008 Build clean (`dotnet build`), suite green (`dotnet test`), and the app runs: the manifest parses, chains resolve, and a misspelled joint reports and drops only that chain. Nothing sways yet.

**Checkpoint**: chains are authored, parsed, resolved and reported at load — the vocabulary exists, but nothing moves.

---

## Phase 3: User Story 1 - Hair sways with the character's motion (Priority: P1) 🎯 MVP

**Goal**: a declared chain trails the actor's motion and settles back into the animated pose; a
chain bends along its whole length and bends the same way in the world at any facing.

**Independent Test**: headlessly, a chain driven by a pose that moves lags the motion and, with no
motion, converges to the animated pose; on a device, walkthrough A (`quickstart.md` §2) and B (§3).

### Implementation for User Story 1

- [X] T009 [US1] In `world/Sway.fs`, implement the rope inside `apply`: read each point's posed world position from `pose.World[joint].Translation` transformed by `world`; anchor the first point; Verlet-step the rest (gravity, damping, spring to rest); run `SwayConstraintIterations` distance passes holding each point's rest length with the root re-snapped after each; and implement seeding — explicit `Seeded` (not a position sentinel), cleared while not playing, and re-seeded whenever the anchor's rest position moved more than `SwayTeleportDistance` from `SeedAnchor`. (FR-003/004/007/013; research D4/D5/D6)
- [X] T010 [US1] In `world/Sway.fs`, implement the closed-form application inside `apply`, exactly as [contracts/sway.md](./contracts/sway.md)'s arithmetic block: the per-segment world delta (clamped), `targetRot[k] = d[k] ⊗ restRot[k]`, the parent frame being the **posed** one, the segment being the one the bone **drives**, and the conversion back into the pose's own frame written to `pose.Rotation[joints[k]]`; then call `Actor.composeAll`. (FR-005/FR-006/FR-007; research D3)
- [X] T011 [US1] In `world/ActorRender.fs`, add `Sway : Sway.SwayState` to the `[<Struct>] ActorRender`, built in `create` from `asset.SwayChains`; add `simulate : byref<ActorRender> -> float32 -> bool -> unit` (advance the held clips' clocks while playing; always blend; while playing, step and apply sway with `upAxisFix * Entity.modelMatrix` and the matching `bodyRot`); and remove the `blendPose` call from `upload`, leaving the copy-pass palette upload. (FR-008/FR-011/FR-014; research D1; contract) — depends on T004, T010.
- [X] T012 [US1] In `screen/GameplayScreen.fs`, turn `advanceActorAnimations` into the per-frame actor loop that runs in **both** modes, calling `ActorRender.simulate &actor.Renderer frame.Dt (not state.Editing)` for every actor of the active zone — and place it at the **end of `update`, after `CueEngine.tick`**, *not* where the old clock-advance loop sat at the top of the play block, so the blend sees this frame's clip weights (`Systems.animate`), this frame's cue effects, and the post-`syncActors` positions. (FR-008/FR-015; research D1; contract) — depends on T011.

### Tests for User Story 1

- [X] T013 [US1] Create `test/SwayTests.fs` (register it after `ActorTests.fs` in `test/fsharp-game.Tests.fsproj`) with a fabricated chain model (the `model` helper pattern) and the maths proofs: `quatFromTwoVectors` lands `src` on `tgt` under `Vector3.Transform` for axis-aligned, oblique and near-opposite pairs; after `apply`, each segment's direction **equals** its simulated direction; and the applied local rotation is **unchanged by yawing the whole actor** while the world motion is held fixed. (FR-005/FR-006; SC-003/SC-004; research D3/D8) — depends on T010.
- [X] T014 [US1] Extend `test/SwayTests.fs`: drive a chain with a pose that translates between frames and assert the chain **lags** the motion (it trails the direction of travel); then hold the pose still and assert the chain converges back to the animated pose within the tolerance. (FR-003/FR-004; SC-001/SC-002) — depends on T013.
- [X] T015 [US1] `dotnet build` clean and full `dotnet test` green, then run walkthrough A and B (`quickstart.md` §2, §3) on a device: the hair trails a walking actor, settles when it stops, bends the same world way at any yaw, and every joint (the hairline included) contributes bend. (FR-003…FR-006; SC-001…SC-004) — depends on T012, T014. **— run and passed on a device (user, 2026-10-15), after the automated half came back green.**

**Checkpoint**: hair sways with motion — the feature's MVP, and the reason for the whole spec.

---

## Phase 4: User Story 2 - Chains are authored per actor, and undeclared actors are unchanged (Priority: P2)

**Goal**: a chain is authored on one actor's manifest and affects only that actor; a chain that
cannot resolve costs only itself.

**Independent Test**: headlessly, `resolve` accepts a good chain and refuses each bad one with a
message; `apply` on an empty state writes nothing; two chains and two instances are independent.

- [X] T016 [US2] Extend `test/SwayTests.fs`: `resolve` accepts a well-formed chain (joint indices in order); and refuses, with a message naming what failed, a joint name the model lacks, a chain whose joints are **not** a parent→child path, a chain with fewer than two joints, a stiffness of `0` and one above `1`, and a chain over `MaxSwayJointsPerChain`. (FR-001/FR-009/FR-010/FR-012) — depends on T002, T013.
- [X] T017 [US2] Extend `test/SwayTests.fs`: `apply` on an empty `SwayState` writes no rotation (an actor that declares nothing is unchanged, SC-005); two chains on one actor settle independently; and two `createState` calls sharing one `SwayChainDef array` do not share point state. (FR-002; SC-005/SC-006) — depends on T010, T013.
- [X] T018 [US2] Full `dotnet test` green, then run walkthrough C (`quickstart.md` §4): a misspelled joint and an out-of-range stiffness each report, drop only that chain, and leave the actor spawnable and rendered. (FR-009/FR-010) **— run and passed on a device (user, 2026-10-15), after the automated half came back green.**

**Checkpoint**: chains are per actor, and a bad chain costs its own effect and nothing else.

---

## Phase 5: User Story 3 - The sway is stable, bounded and play-only (Priority: P3)

**Goal**: the sway never folds or runs away, a teleport does not whip it, and the editor shows the
animated pose.

**Independent Test**: headlessly, a large displacement clamps; a large `dt` stays bounded and
settles; a teleport re-seeds; `dt = 0` and an empty state are inert; stepping allocates nothing.

- [X] T019 [US3] Extend `test/SwayTests.fs`: a displacement large enough to exceed the bound is clamped to `SwayMaxAngle` (no segment turns further, SC-007); a single very large `dt` step (0.25 s) stays within the bound and settles afterwards; and an anchor jump of more than `SwayTeleportDistance` re-seeds instead of stepping (the chain's deviation is ~0 immediately after, FR-013). (FR-007/FR-013; SC-007) — depends on T010, T013.
- [X] T020 [US3] Extend `test/SwayTests.fs`: `apply` with `dt <= 0` and on an empty state raises nothing and writes nothing (FR-012, FR-015); and stepping a chain allocates 0 bytes, using the warm-up-then-measure pattern `test/ActorTests.fs` uses for `blendPose`. (FR-012; SC-006) — depends on T010, T013.
- [X] T021 [US3] Verify the play gate and run walkthrough A's editor steps (`quickstart.md` §2): while editing, a chained actor holds the animated pose exactly (no drift, no swing), and pressing Play starts the chain from rest rather than a frozen mid-swing. (FR-008) — depends on T012. **— run and passed on a device (user, 2026-10-15), after the automated half came back green.**

**Checkpoint**: the sway is well-behaved and is an effect of playing only.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and end-to-end validation.

- [X] T022 [P] Update `docs/ROADMAP.md`: record bone-chain sway (motion only) as landed under phase 14, and that **wind** — and with it a wind field in the actor manifest — is the deferred remainder. (spec Assumptions)
- [X] T023 [P] Confirm nothing outside the documented surfaces moved: `git status` shows nothing under `shaders/`, `render/`, `editor/`, `world/Zone.fs`, `world/ZoneTemplate.fs`, `world/ScenarioZone.fs`, `world/Systems.fs`; and no `<PackageReference>`, pipeline, render target or uniform was added. (FR-014/FR-015; contract "Explicitly unchanged")
- [X] T024 Run [quickstart.md](./quickstart.md) end to end: the focused `SwayTests` filter, the whole suite unmodified, and walkthroughs A–D. (SC-001…SC-007) **— run and passed on a device (user, 2026-10-15), after the automated half came back green.**

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies. T002 depends on T001 (same file); T003 after T002.
- **Foundational (Phase 2)**: depends on Setup. T004 is the pose change every later task rests on;
  T005 → T006 (the asset field before the loader that fills it); T007 is independent; T008 closes
  the phase.
- **User Stories (Phase 3–5)**: all depend on Foundational.
  - **US1** is the implementation increment: T009 → T010 → T011 → T012, then its tests
    T013 → T014, then T015 verifies.
  - **US2** and **US3** carry proofs over the same mechanism; their tests may be written any time
    after T010 and T013 (the task that creates `test/SwayTests.fs`; T016 additionally after T002).
- **Polish (Phase 6)**: depends on the stories it documents and validates.

### User Story Dependencies

- **US1 (P1)**: after Foundational. No dependency on US2/US3 — but it is the one that makes the
  feature visible, so US2/US3 are only *observable* once US1 has landed.
- **US2 (P2)**: after Foundational (T002's validation, T010's apply). Independent of US1's tests.
- **US3 (P3)**: after Foundational (T010's clamp/re-seed, T011/T012's play gate). Independent of
  US1/US2.

### Within Each Phase

- The types before the logic (T001 → T002); the pose before the simulation (T004 → T009/T010);
  `apply` before `simulate` before the screen hook (T010 → T011 → T012); implementation before the
  tests that assert it.

### Parallel Opportunities

- Setup: T001 and T002 are the same file — sequential. T003 is a different file but needs T002.
- Foundational: after T004, **T005, T006 and T007 touch three different files** — T007 is [P].
- US1: T009/T010 are the same file (`world/Sway.fs`) — sequential; T011 (`world/ActorRender.fs`)
  and T012 (`screen/GameplayScreen.fs`) are separate files but stand in a chain.
- US2/US3: `test/SwayTests.fs` is one file grown across T013–T020 — **sequential**, not parallel.
- Polish: T022 (`docs/ROADMAP.md`) and T023 (a read-only check) are independent.

---

## Parallel Example: Foundational

```text
# After T004 (the pose change), three different files:
Task: "SwayChains on ActorAsset in world/ActorAsset.fs"        (T005)
Task: "Parse + resolve + report the Sway field in world/Assets.fs"  (T006 — after T005)
Task: "Author Akane's hair chains in assets/actors/Akane/akane.manifest"  (T007)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 Setup → 2. Phase 2 Foundational → 3. Phase 3 US1 → **STOP and VALIDATE**: hair sways
   with motion (the headless maths and settling tests, or walkthrough A on a device). This is the
   whole point of the spec; everything after it is assurance.

### Incremental Delivery

1. Setup + Foundational → chains authored and resolved; the render path unchanged.
2. US1 → hair sways, trails, settles (MVP).
3. US2 → per-actor authoring proven; a bad chain costs only itself.
4. US3 → bounded, teleport-safe, play-only.
5. Polish → docs and the walkthrough.

---

## Notes

- [P] = different files, no dependencies.
- **This feature's mechanism is indivisible**: the pose change (T004) and the simulation (T009/T010)
  cannot be split across stories, because nothing sways until the simulation exists *and* is
  called. US2 and US3 therefore carry proofs rather than independent implementations — the same
  shape `specs/090-sound-cues/tasks.md` used for a change that could not land one arm at a time.
- **The arithmetic is the risk.** T010 must follow [contracts/sway.md](./contracts/sway.md) exactly:
  the parent frame is the **posed** one and the segment is the one the bone **drives**. Those two
  facts are the whole difference from `odin-game/src/sway.odin`, and T013 asserts each (the exact
  segment direction, and yaw-invariance).
- No shader, no pipeline, no editor surface, no wind (FR-014; contract "Explicitly unchanged").
  If a task seems to need one, the plan is being departed from — stop and re-read the spec.
- Verify `dotnet test` stays green after every phase; T004 in particular must not move the pose
  the existing `ActorTests` assert (`composeJoint` produces the same matrices, and now also
  records the TRS it used).
- Sway is a runtime effect: no command, no undo step, nothing persisted (spec Assumptions) —
  verify in the Edit menu.

---

## Phase 7: Convergence

**Purpose**: Reconcile `tasks.md` with what shipped. The mechanism, the manifest, the plumbing and the
arithmetic are all in place and green (656 tests, 16 of them `SwayTests`); what follows is the gap
between the *artifacts* and the code, found by reading the shipped code against them. T025 and T026 are
artifact reconciliations — a spec decision, not an implement pass — and T027 is code and test work.

- [X] T025 Reconcile FR-003 and FR-004 with the rope's physics: a still chain settles to the animated pose **plus** a steady gravity sag (`g·dt²/springK`, ≈ 2 cm per point at 60 fps with the shipped `SwaySpringScale = 0.9` and Akane's `Stiffness 0.15`), and a dragged chain overshoots forward within a bounded swing before settling. There is no finite spring that removes the sag against constant gravity, so the remedy is a spec decision: reword the two requirements to say so, or deliberately change `world/Sway.fs` (e.g. bias out gravity's steady term) and accept the look it gives. per FR-003, FR-004 (partial) **— reworded, not re-engineered: FR-003 now says the chain's *response* trails (a rope is a pendulum and swings back and forth inside FR-007's bound), FR-004 says it settles *toward* the pose at a small steady gravity sag, and SC-001 and US1/AC1 were brought into line. `world/Sway.fs` is untouched.**
- [X] T026 Reconcile FR-015 with the field that had to be added: `ActorAsset.SwayChains` required `SwayChains = [||]` in six existing fixtures — `test/ActorTests.fs`, `test/ActorNameTests.fs`, `test/AssetNameIndexTests.fs`, `test/DialogueTests.fs`, `test/CueTests.fs`, `test/ScenarioForkTests.fs`. No assertion changed and all 656 tests pass, so either reword FR-015 to "passes with the existing assertions unmodified" or record the exception in the spec. per FR-015 (partial) **— FR-015 now reads "pass with its assertions unmodified" and records that a required new field on a record a fixture builds is not an assertion change; the six fixtures keep every assertion they had.**
- [X] T027 Bring the actor-level chain policy under test: `Assets.resolveSwayChains` (private, `world/Assets.fs`) owns the drop-and-report for a chain that will not resolve and the `MaxSwayChainsPerActor` truncation, and nothing in the suite exercises it — only the open walkthroughs. Move the policy beside `Sway.resolve` (or otherwise make it reachable) and assert that a failing chain is dropped with a message while its siblings survive, and that chains past the cap are ignored with one report. per FR-009, FR-012 (partial) **— landed as `Sway.resolveAll` in `world/Sway.fs` (pure: it returns a message per dropped chain, and `world/Assets.fs` reports them against the actor's id), so no private policy remains in `Assets.fs`; two tests added.**

---

## Phase 8: Convergence

**Purpose**: A second pass, after T025–T027 closed. The mechanism, the manifest, the plumbing, the
arithmetic and the manifest policy are all in place and green (658 tests, 18 of them `SwayTests`).
What is left is one stale justification in the design docs and one unasserted clause of a success
criterion.

- [X] T028 Correct the sway pass's recorded placement, which two feature docs state and the code does not do: `plan.md`'s D1 bullet and `research.md` D1 both say the pose step landed "exactly where `docs/ARCHITECTURE.md` already lists it (`… animation → sway → trigger …`)", but `screen/GameplayScreen.fs` runs `simulateActors` **last** in `update` — after `Systems.animate`, `Systems.syncActors` and `CueEngine.tick`. The placement is deliberate and correct (the blend must see this frame's clip weights, including the ones cue ops set, and the sway this frame's positions), so reword the two claims and update `docs/ARCHITECTURE.md:137`'s system chain to match. per plan: D1, research D1 (contradicts) **— the chain in `docs/ARCHITECTURE.md` turned out to be a faithful transcription of the reference's own loop (`odin-game/src/systems.odin`), so the doc was labelled as that and the port's one deviation recorded; `plan.md` and `research.md` D1 no longer claim the pass landed where that chain lists it.**
- [X] T029 Assert SC-001's second clause — "no joint exceeds the maximum deviation angle in a steady walk (0 clamped joints)" — which nothing checks today: the trailing test walks but only bounds the swing, and the clamp test displaces the rope by hand rather than walking. Extend the trailing test in `test/SwayTests.fs` to walk at the jog speed for enough frames to reach a steady state and assert that no segment's world turn ever reaches `Sway.SwayMaxAngle`. per SC-001 (partial) **— added to the trailing test: a 60-frame walk at 3.6 m/s tracks the worst desired segment turn and asserts it never reaches `SwayMaxAngle`, so SC-001's "0 clamped joints" is now measured rather than assumed (it holds).**
