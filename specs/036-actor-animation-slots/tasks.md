---

description: "Task list for actor animation slots with weights"
---

# Tasks: Actor Animation Slots with Weights

**Input**: Design documents from `/specs/036-actor-animation-slots/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: **No new test file** — `test/ActorTests.fs` is extended, which is `research.md` Decision 13. It already builds synthetic `Iqm.Model` records and drives `Actor.samplePose`/`createSlot`/`advance` headlessly, so the blend, the slot algebra and the per-slot clocks are all reachable without a GPU. The renderer's name-resolving wrappers and the Inspector section need a device and a running ImGui frame, so they are the manual half (`quickstart.md`), as `035` left its panel lines to its own quickstart.

**Organization**: Tasks are grouped by user story. Phase 1 confirms the feature needs no build change, Phase 2 changes the renderer's storage — the field, the blend, and the four functions that read it, which cannot land apart — and Phases 3–6 are the four stories' mechanisms and assertions. Phase 7 is validation and the docs.

**Why Phase 2 is foundational and not part of US1**: in F#, replacing `ActorRender.Animation` with an `AnimationSlots` slot map breaks `create`, `advanceInPlace`, `upload` and `playClip` in the same compile — every one of them reads the field. So the storage swap and the functions that read it (the blend, the advance, the re-expressed switch) are one indivisible change both stories rest on: US1 is the blend, US2's setter writes into the same map, US3 draws it and US4 advances it. It lands first; what US1 then *is* is the verification the spec asks of it, exactly as `035` split its record from its assertions.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- Include exact file paths in descriptions

## Path Conventions

- F# single project at the repository root; modules live in domain clusters (`world/`, `state/`, `editor/`, `render/`, `core/`).
- Compile order is defined by `fsharp-game.fsproj`. **No file is added and none moves**, so neither `.fsproj` changes and the compile order stands: `core/SlotMap.fs` … `world/Actor.fs` … `world/ActorRender.fs` … `world/Zone.fs` … `editor/Inspector.fs`.
- Tests live in `test/` (one `<Module>Tests.fs` per module); this feature extends `test/ActorTests.fs` only.
- Completed tasks are marked `- [X]`. T014, the interactive walk, is the only one left unchecked — this environment cannot drive the editor (it was run by hand).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the feature needs no build or binding change.

- [X] T001 Verify that nothing is added to either project file and that `render/ImGui.fs` gains no binding: the Inspector section uses only widgets already bound (`collapsingHeader`, `text`, `sameLine`, `dragFloat`, `smallButton`, `pushId`/`popId`) — so there is no new `DllImport`, no native change, and `native/libcimgui.so` is untouched (`docs/EDITOR_DESIGN.md` D-017; research Decisions 8 and 9). Confirmed: neither `.fsproj` changed and `render/ImGui.fs` is untouched.

**Checkpoint**: nothing about the build or the native library changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The renderer holds a slot set, the pose is the weighted blend of it, and the four functions that read the old single slot are rewired. Every story rests on this.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 In `world/Actor.fs`, give the slot its state and a shared sampler: add `[<Literal>] let MaxAnimationSlots = 8`; make `AnimationSlot.Weight` and `AnimationSlot.Enabled` `mutable` so a setter can write in place through `&slots.[i].Value` the way `Frame` already is; and extract the per-joint sampler — `sampleJointTRS` (one joint's interpolated `(translation, rotation, scale)` for one clip at a frame window) and `composeJoint` (the local/world/skinning triple from a TRS) — rewriting `samplePose` on them so the channel math exists once. This is a pure refactor of `samplePose`: same inputs, same outputs, no allocation (FR-011; research Decisions 2 and 6). `023`'s golden tests and its no-allocation test are the guard.

- [X] T003 In `world/Actor.fs`, add the blend and the advance over the slot map's `Span`: `blendPose (m) (slots: Span<Slot<AnimationSlot>>) (pose)` — per joint, sum `translation * weight` and `scale * weight` and divide by the total, combine rotations by an incremental slerp (`r ← Slerp(r, rᵢ, wᵢ / Σw)`), and fall back to the joint's bind-pose local when no slot contributed, then compose `Local`/`World`/`Skinning` exactly as `samplePose` does; and `advanceSlotsInPlace (m) (slots: Span<Slot<AnimationSlot>>) (dt)` — advance every alive, enabled slot on its own clock. Accumulate in per-joint locals (no scratch arrays), so the blend allocates nothing (FR-003, FR-004, FR-008, FR-009, FR-012; research Decisions 3, 4 and 5). Takes `Span`, not `ReadOnlySpan`: a `ReadOnlySpan` parameter routes `Slots` through `Span.op_Implicit`, which F# flags with warning FS3391 (research Decision 4). Depends on T002.

- [X] T004 In `world/ActorRender.fs`, swap the storage and read it: replace `mutable Animation : Actor.AnimationSlot` with `AnimationSlots : SlotMap<Actor.AnimationSlot>`; in `create`, build the map at `Actor.MaxAnimationSlots` and seed **one** slot at weight 1 ("Idle" if the asset defines it, else the first clip, else none — an empty map that blends to the bind pose); point `advanceInPlace` at `Actor.advanceSlotsInPlace`; point `upload` at `Actor.blendPose` (the byte copy and copy-pass are untouched); and re-express `playClip` over the slots — bring the named clip's slot to weight 1 and zero every other slot's weight, leaving them held, so `Systems.animate`'s hard switch keeps working byte-for-byte. Update the header comment, which still describes the odin `Actor_Renderer` single-slot shape (FR-001, FR-008, FR-011; research Decisions 1, 11 and 12). Depends on T002, T003.

**Checkpoint**: `dotnet build` and `dotnet test` are green, every existing `ActorTests` and animation/skinning assertion is unmodified and passing (FR-011, SC-002, SC-006), and an actor still plays its Idle clip exactly as before.

---

## Phase 3: User Story 1 - An actor poses as a blend of several animations (Priority: P1) 🎯 MVP

**Goal**: What the weighted average produces is asserted at every joint, over the arrangements the spec names.

**Independent Test**: drive a synthetic actor with two clips at equal weight and compare against each clip sampled alone — translations and scales at the midpoint, rotation at the rotational midpoint — then drive it with one clip at weight 1 and confirm it equals `samplePose` for that clip and frame.

- [X] T005 [P] [US1] Extend `test/ActorTests.fs` with the blend, over the fixtures it already has (`model`, `translationModel`, `createPoseFrame`, `createSlot` and the new `SlotMap<AnimationSlot>`): two slots at equal weight blend to the per-joint midpoint — the translation half exact on `translationModel`, the rotation half equal to `Quaternion.Slerp` — and weights 0.5/0.5 equal 1.0/1.0 (FR-003, FR-004; SC-001); `blendPose` with one slot at weight 1 equals `samplePose` for the same clip and frame at every joint, which is the regression guard for T002's refactor and FR-011 (SC-002); an empty slot map, and a map whose every weight is 0, pose the bind pose, not a zeroed pose (FR-008, SC-003); and `blendPose` allocates 0 bytes over a run of frames, measured as the existing `samplePose allocates nothing` test measures it (FR-012, SC-005).

**Checkpoint**: US1 works on its own — an actor blends several clips, and a single clip is provably unchanged.

---

## Phase 4: User Story 2 - A caller sets each clip's weight (Priority: P2)

**Goal**: A caller names a clip and a weight; the clip joins the set if it is not held, is adjusted if it is, and is clamped and validated.

**Independent Test**: on an actor holding one clip, set a second clip's weight and confirm both contribute; ease the first to 0 and confirm its contribution is gone; request a weight above 1 or below 0 and confirm it clamps; name a clip the asset does not define and confirm a loud failure.

- [X] T006 [US2] In `world/Actor.fs`, add the slot algebra: `setSlotWeight (m) (slots: SlotMap<AnimationSlot>) (animIndex: int) (weight: float32)` — find the alive slot whose `AnimIndex` matches and set its weight (clamped 0..1) and `Enabled = true`, or add a slot for that clip seeded at that weight when none matches; adding past `MaxAnimationSlots` raises through `SlotMap.Add`, and an out-of-range index raises. And `removeSlot (slots: SlotMap<AnimationSlot>) (animIndex: int)` — free the matching alive slot, a no-op when there is none. Both are pure and headless (FR-002, FR-005, FR-006, FR-007; research Decisions 3 and 4). Depends on Phase 2.

- [X] T007 [US2] In `world/ActorRender.fs`, add the name-resolving wrappers `setClipWeight (r: byref<ActorRender>) (name: string) (weight: float32)` and `removeClip (r: byref<ActorRender>) (name: string)`, resolving the name through `Actor.findAnimIndex` and raising when the asset does not define it (FR-005, FR-010; research Decision 3). These are the one path the Inspector calls (FR-020). Depends on T006.

- [X] T008 [US2] Extend `test/ActorTests.fs` with the slot algebra: `setSlotWeight` on a clip already held adjusts it and adds no second slot for the same clip (FR-007); it clamps a weight outside 0..1 (FR-006); a weight of 0 removes the clip's contribution while the slot stays held (spec edge case); `removeSlot` frees the slot; an `animIndex` outside the asset's clips raises; an add past `MaxAnimationSlots` raises (FR-002, SC-004). Depends on T006, T007.

**Checkpoint**: a caller drives the blend from code — the reference engine's `actor_set_slot_weight`, and the mechanism the Inspector will call.

---

## Phase 5: User Story 3 - The author sets an actor's animation slots in the Inspector (Priority: P3)

**Goal**: The actor Inspector lists the clips the actor holds and lets the author add, remove and re-weight them live.

**Independent Test**: `quickstart.md` steps 1–8 with a display — the section appears only for an actor, a clip can be added, weighted and removed (removing the last reports the actor holds none), the add-control degenerate cases report, nothing is persisted, and undoing a deletion returns the actor to its default slot.

- [X] T009 [US3] In `editor/Inspector.fs`, add an `Animations` section inside the existing `if ref.Kind = EntityKind.Actor` block, after Show Collider / Set as Player: reach the runtime renderer the way `Systems.animate` does — `let actor = &zone.Actors.Get(handleOf ref)` then `&actor.Renderer` (no new `Zone` accessor; research Decision 9) — draw a row per held clip (its name, a `dragFloat` weight 0..1 that calls `ActorRender.setClipWeight`, and a `smallButton` that calls `ActorRender.removeClip`), keep the row widgets distinct with `ImGui.pushId`/`popId`, add a `smallButton` per clip the asset defines and the actor does not hold that calls `ActorRender.setClipWeight` at weight 0 (research Decision 7), and report the degenerate states as text — "(asset has no clips)", "(all clips added)", "(maximum 8 clips)", and "(holds no clips)" once the actor's last slot is removed — rather than offering an add that cannot succeed or leaving an empty control (FR-018). It records no command and stores nothing in `EditorState` (FR-014 to FR-020; research Decisions 8 and 10). Depends on T007 (the section calls `setClipWeight`/`removeClip`).

**Checkpoint**: an author adds a second clip, balances the two, watches the pose change, and removes it — with no undo step and nothing persisted.

---

## Phase 6: User Story 4 - Each clip keeps its own clock (Priority: P4)

**Goal**: Every held clip advances independently, at its own rate and loop, and a zero weight does not stop its clock.

**Independent Test**: hold two clips of different frame rates, advance one step, and confirm each reached its own frame; hold a looping and a non-looping clip together and confirm one wraps while the other holds.

- [X] T010 [US4] Extend `test/ActorTests.fs` with the per-slot clocks, over `advanceSlotsInPlace` from T003: two enabled slots with different `PlaybackFps` each advance by their own rate in one step and neither affects the other; a disabled slot does not advance; a slot whose weight is 0 still advances (a zero weight removes its contribution, not its playback — spec edge case); and a looping slot wraps at its frame count while a non-looping one holds its last frame (FR-009). Note: this task edits the same file as T005, so it follows T005 rather than running beside it. Depends on Phase 2.

**Checkpoint**: clips that do not share a timeline blend without either being forced to the other's timing.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Whole-feature validation, the lines this feature makes false, and the properties it claims.

- [X] T011 Run `dotnet build --no-incremental` and `dotnet test test/fsharp-game.Tests.fsproj` and confirm the build is clean with no new warnings and the suite passes **with every assertion `specs/009-animated-character` and `specs/023-actor-skinning-hot-path` already made unmodified** (FR-011, SC-002, SC-006). `--no-incremental` is the build to run: an incremental build hides warnings on unchanged files. Record the counts. Done — see Outcomes.

- [X] T012 [P] Correct the lines this feature makes false in the repository docs. `docs/EDITOR_DESIGN.md`: D-011's list of direct writes (the cast binding, the zone registry) gains the Inspector's animation section — the third surface that edits runtime state rather than recording a command. `docs/ROADMAP.md`: Phase 3's `world/Actor.fs` line says "animation/morph state"; the port has no morphs and that state is now multi-slot weights. `docs/EDITOR_DESIGN.md` D-017 needs no change: the section adds no widget.

- [X] T013 [P] Confirm the structural properties this feature claims, reading rather than assuming: `state/Command.fs` gains no command and its `UndoStack`/`Applied : Command list` are untouched (FR-019); `state/EditorState.fs` gains no field (research Decision 8); `world/Entity.fs`'s `ActorState` gains no animation field, so a payload still cannot hold a renderer or its slots (FR-019); and `render/ImGui.fs` is unchanged (T001). Record the reads. Done — see Outcomes.

- [ ] T014 Walk `quickstart.md` steps 1–8 in the editor (with a display): the section shows only for an actor, a clip can be added at weight 0, weighted, and removed — removing the last reports "(holds no clips)" and the actor stands in its rest pose (FR-018); the add-control degenerate cases report; moving the actor then changing its slots leaves the move as the one Ctrl+Z step and persists nothing; undoing a deletion returns the actor holding one default slot (FR-019); and an untouched actor still plays its Idle clip. **Not run if the agent environment cannot be driven interactively** — and if it is not run, say so rather than implying the panel's draw path was exercised. Run by hand: the user confirmed the section works.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks every user story**. T002 → T003 → T004, necessarily: T003's blend uses T002's `sampleJointTRS`/`composeJoint`, and T004's renderer reads both and cannot compile against the old `Animation` field.
- **User Stories (Phases 3–6)**: all depend on Phase 2. US1 (T005) and US2 (T006) have no dependency on each other. US3 (T009) depends on US2's T007 (the section calls `setClipWeight`/`removeClip`). US4 (T010) depends on Phase 2 only.
- **Polish (Phase 7)**: depends on all of the above.
- **Convergence (Phase 8)**: T015 depends on Phase 2; T016 is documentation.

### Within each phase

- Phase 2: T002 → T003 → T004 (see above).
- Phase 4: T006 → T007 → T008.
- Phase 5: T009 after T007.
- Phase 7: T011 → T014; T012 ∥ T013, and both can run alongside T011 since they only read and write docs.
- Phase 8: T015 ∥ T016.

### Parallel Opportunities

- **T005 ∥ T006**: US1's assertions (`test/ActorTests.fs`) and US2's slot algebra (`world/Actor.fs`) are different files and neither needs the other.
- **T009 ∥ T010**: once T007 lands, the Inspector section (`editor/Inspector.fs`) and US4's clock assertions (`test/ActorTests.fs`) are different files.
- **T012 ∥ T013** in the polish phase: different targets, no dependencies.
- **T015 ∥ T016** in the convergence phase: a test file and a contract doc.

### Same-file constraint

Three user-story tasks extend **`test/ActorTests.fs`** — T005, T008, T010. They cannot be edited in parallel; run them in priority order (US1 → US2 → US4), which is also the order their mechanisms land. `world/Actor.fs` is likewise touched in both Phase 2 (T002, T003) and Phase 4 (T006), so T006 follows Phase 2.

---

## Implementation Strategy

### MVP

**US1 is the MVP.** It is the capability — an actor blends several clips, and a single clip is provably unchanged — and it covers the whole renderer half; US2's setter, US3's section and US4's clocks add the handles to it.

1. Complete Phase 1 (no build change) and Phase 2 (the renderer holds the set and blends it; existing tests green).
2. Complete Phase 3 (US1) — the blend, asserted.
3. **STOP and VALIDATE**: `dotnet test`, then confirm an actor with one clip is visually unchanged.

### Incremental Delivery

1. Setup + Foundational → an actor blends its slots; nothing yet sets them.
2. US1 → the blend is asserted (MVP).
3. US2 → a caller sets weights from code (`ActorRender.setClipWeight`).
4. US3 → the author sets them in the Inspector.
5. US4 → clips of different rates/loops blend correctly, asserted.
6. Polish → docs, structural reads, the interactive walk, and the whole-suite regression check.
7. Convergence → the scale/rotation assertions (T015) and the contract note (T016).

---

## Notes

- [P] tasks touch different files and have no incomplete dependencies.
- **T002/T003/T004 are one build, not three**: the storage swap breaks `create`, `advanceInPlace`, `upload` and `playClip` at once, and the tree is only green once all four read the new field. This is the same shape as 035's T002/T003 and 033's T003/T004.
- **The blend lands in the foundational phase, not in US1**, for the reason 035 put its record before its assertions: `upload` must call something in the same compile, so `blendPose` cannot arrive later than the field it reads. US1 is then the verification the spec asks of it, not the mechanism.
- **The asserted detail most likely to be got wrong** is T005's `blendPose`-equals-`samplePose` case: the refactor of `samplePose` onto the shared sampler (T002) is exactly where a subtle change would hide, so that assertion — not a visual check — is what proves FR-011.
- **The second most likely** is the rotation midpoint: a linear quaternion average passes a translation-only test and fails it, which is why the spec calls rotations out as an edge case. T015 supplies that assertion.
- **`Systems.fs` is deliberately untouched**: the idle↔walk cross-fade is FR-013's deferral, and `playClip`'s hard switch keeps working through the new slots.
- Verify `dotnet test` passes after each phase; commit per task or logical group.

---

## Outcomes

**Validation (T011)**: `dotnet build test/fsharp-game.Tests.fsproj --no-incremental` is clean — 0 errors, **0 warnings** (both projects) — and `dotnet test test/fsharp-game.Tests.fsproj` passes **270/270**, against 255 before this feature: 15 new `ActorTests` (5 for the blend, 5 for the slot algebra, 3 for the per-slot clocks, 2 for the scale/rotation midpoints from T015). No existing assertion was modified, so FR-011 / SC-002 / SC-006 hold: the animation and skinning suites pass with every assertion they already made.

**One deviation from the plan, taken and recorded**: `blendPose` takes `Span<Slot<AnimationSlot>>` rather than `ReadOnlySpan` (T003). `SlotMap.Slots` yields a `Span`, and a `ReadOnlySpan` parameter routes it through `Span.op_Implicit`, which F# flags with **warning FS3391** — the project builds warning-free, so the fallback research Decision 4 already named was taken. `research.md` D4 and `contracts/animation-slots.md` record it.

**Structural reads (T013)**: `state/Command.fs` has no animation reference (no command), `state/EditorState.fs` has no animation field, `world/Entity.fs`'s `ActorState` is untouched, and `render/ImGui.fs` gained no binding — read, not assumed.

**What is not verified by automation (T014)**: the interactive walk — the editor needs a person to click through the main menu before any panel draws, and nothing here can drive it. The user ran it by hand and confirmed the section works, so US3's presentation is confirmed manually but not by a test. The Inspector section's draw path and `ActorRender.setClipWeight`/`removeClip` (which need a GPU device to build an `ActorRender`) remain unexercised by tests.

---

## Phase 8: Convergence

Appended by `/speckit-converge`, which assessed the code against `spec.md`, `plan.md` and `tasks.md`. Existing tasks are untouched. One `partial` gap and one `unrequested` addition were found; everything else the artifacts call for is implemented and validated.

T014's interactive walk was not runnable by automation, but the user has since run it by hand and confirmed the section works, so US3's presentation is confirmed manually, not by a test.

- [X] T015 Add a scale- and rotation-midpoint assertion to `test/ActorTests.fs`: a fixture whose clip animates a scale channel and a rotation channel, blended at equal weight, must land on the scale midpoint and on `Quaternion.Slerp`'s rotational midpoint. The current fixtures animate `translate.x` only, so `blendPose`'s rotation path runs as an identity slerp and neither its scale nor its rotation result is asserted, leaving SC-001's scale and rotation clauses and FR-003's "joint rotations combined into a valid rotation that is the weighted midway" unproven per SC-001, FR-003 (partial). Depends on Phase 2.

- [X] T016 Record `ActorRender.animationSlotCapacity` in `contracts/animation-slots.md`'s `ActorRender` surface — or inline it at the one Inspector call site — so the contract names every member the feature adds. The re-export is an addition neither the plan nor the contract mentions, and it exists because the Inspector opens `Entity`, which also declares a module `Actor`, making the capacity ambiguous there per plan: ActorRender surface (unrequested).

**Convergence outcome**: T015 and T016 land with `dotnet build test/fsharp-game.Tests.fsproj --no-incremental` clean (0 warnings) and `dotnet test --no-build` at **270/270** (the two new assertions: `blendPose` of two clips at equal weight is the scale midpoint / the rotation midpoint). SC-001 is now asserted in full, and the contract names `animationSlotCapacity`. A follow-up `/speckit-converge` should report `converged`.

**Checkpoint**: after T015, SC-001 is asserted in full; after T016, the contract matches the code's public surface.
