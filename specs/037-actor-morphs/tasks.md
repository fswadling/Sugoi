---

description: "Task list for actor morph targets"
---

# Tasks: Actor Morph Targets

**Input**: Design documents from `/specs/037-actor-morphs/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: **No new test file** — `test/ActorTests.fs` is extended, which is `research.md` Decision 11. Everything this feature adds outside the shader is pure: the manifest **parse** (a string of s-expression text into records), the delta **packing** (the manifest plus a byte array into the dense per-vertex layout), and the **weight algebra** (over `SlotMap<MorphSlot>`). All three are driven headlessly, as the actor's existing pure functions are. The shader needs a GPU and the Inspector section needs a frame, so both are the manual half (`quickstart.md`), as `036` left its section and `035` its panel lines.

**Organization**: Tasks are grouped by user story. Phase 1 confirms the feature needs no new file, dependency, or build entry point, Phase 2 is the one indivisible change — the vertex stage, the asset's morph data, and the renderer that reads both, which cannot land apart — and Phases 3–5 are the three stories. Phase 6 is the fixture-independent validation, the docs, and the walkthrough.

**Why Phase 2 is foundational and not part of US1**: in F#, adding `Morphs` to `ActorAsset` breaks its constructor in `Assets.loadActors`, and adding the morph slot map to `ActorRender` breaks `create` and `draw` in the same compile; `draw` reads both `Actor.MorphSlot` and `r.Asset.Morphs`, and the shader must agree with the buffer `Assets` uploads. So the asset's morph data, the slot type, the renderer's wiring, and the shader are one build. What US1 then *is* is the authored data and the assertions — the verification the spec asks of it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- F# single project at the repository root; modules live in domain clusters (`world/`, `state/`, `editor/`, `render/`, `core/`).
- Compile order is defined by `fsharp-game.fsproj`. **No file is added and none moves**, so neither `.fsproj` changes and the compile order stands: `render/Gpu.fs` … `world/Actor.fs` … `world/ActorAsset.fs` … `world/Assets.fs` … `world/ActorRender.fs` … `editor/Inspector.fs`.
- `shaders/mesh.vert` is compiled to `.spv` with `glslc` (`glslc -fshader-stage=vert shaders/mesh.vert -o shaders/mesh.vert.spv`); the `.fsproj` `CopyShaders` target already copies it.
- Tests live in `test/`; this feature extends `test/ActorTests.fs` only.
- All tasks are unchecked (`- [ ]`); this list is generated before implementation.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the feature needs no new file, dependency, or build entry point.

- [X] T001 Verify that nothing is added to either project file and no managed dependency is added: `Prime` is already referenced and `[<SymbolicExpansion>]`/`scvalue` is how every other manifest is parsed — but confirm it parses an **array of records** (the manifest's `morphs` and each morph's `targets` need it), since no existing manifest nests a collection of records. Confirm the shader rebuild path (`glslc` → `shaders/mesh.vert.spv`) and that `CopyShaders` already copies `mesh.vert.spv`. Confirm `Frag`'s descriptor counts are untouched.

**Checkpoint**: nothing about the build, the native libraries, or the managed references changes; Prime's collection support is confirmed before the manifest shape is committed to.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The vertex stage can displace a vertex, the asset carries its morph data, and the renderer binds and weighs it. Every story rests on this.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 In `render/Gpu.fs`, add `let MorphsMax = 8` beside `BonesMax` — the vertex-stage morph bound, mirrored as `MORPHS_MAX` in `shaders/mesh.vert` and the size of every weights array (FR-006; research Decision 4) — and change `createMeshPipeline`'s vertex shader to declare **2** uniform buffers and **2** storage buffers (`1u 1u 0u` → `2u 2u 0u`), since it now also carries the `MorphWeights` uniform and the `Morphs` storage buffer. The fragment shader is unchanged (research Decision 3).

- [X] T003 In `shaders/mesh.vert`, add the morph path: a `Morphs` storage buffer at set 0, binding 1 (a `vec4` position and a `vec4` normal per (vertex, morph), indexed `(gl_VertexIndex * morphCount + m) * 2`), and a `MorphWeights` uniform at set 1, binding 1 (`vec4 weights[2]` + a `vec4 info` whose `x` is the morph count). In `main`, before the bone blend, run `position`/`normal` from `aPosition`/`aNormal` and add `Σ weights[m] · delta[m]` over `m < morphCount`; then skin the displaced vertex exactly as before (FR-004, FR-008; research Decisions 1 and 2). Recompile to `shaders/mesh.vert.spv`.

- [X] T004 In `world/ActorAsset.fs`, add the morph data and its pure builders: the manifest records under `[<SymbolicExpansion>]` (`MorphManifest` with `Version`/`Data`/`Morphs`, `MorphDefinition` with `Name`/`Targets`, `MorphTarget` with `FirstVertex`/`VertexCount`/`HasNormalDeltas`/`PositionOffset`/`NormalOffset`); the runtime `ActorMorphs` (`Names`, `Count`, `Buffer`); the pure `parseMorphManifest` (raising on an unknown version or a malformed document) and the pure `buildMorphDeltas` (resolving targets into the dense per-vertex layout, raising on a short data file or a target outside the vertex count); and `Morphs : ActorMorphs option` on `ActorAsset` (FR-001, FR-002, FR-003; research Decisions 7 and 8).

- [X] T005 In `world/Assets.fs`, point the actor load at the sidecar: the private actor `Manifest` gains `Morphs : string` and `readManifest` passes it through; `loadActors` reads the named morph manifest and its data file, calls T004's `parseMorphManifest` and `buildMorphDeltas`, uploads the result once with `Gpu.uploadBufferOnce`, and stores `Some { Names; Count; Buffer }` — or `None` when the actor manifest names none. Every malformed case raises here (FR-002, FR-003, SC-004; research Decisions 6 and 9). The morph manifest uses a distinct extension, so `scanDir`'s recursive `*.manifest` glob does not read it as an actor.

- [X] T006 In `world/Actor.fs`, add the slot and its lookup: `[<Struct>] type MorphSlot = { MorphIndex : int; mutable Weight : float32 }` (no `Enabled` — a weight of 0 already means "contributes nothing" and a morph has no clock to gate; research Decision 5), and `morphSlotIndexOf` (the alive slot holding a morph index, or -1), beside the animation slots' own lookup.

- [X] T007 In `world/ActorRender.fs`, wire the renderer: `MorphSlots : SlotMap<Actor.MorphSlot>` (created empty, capacity `Gpu.MorphsMax` — an actor holds no morphs until one is set, which is FR-011's "rebuilding yields the default"), `MorphWeightBytes : byte array` (48 bytes, reused); `create` builds both; and `draw` — before delegating to `Mesh.draw` — binds the asset's delta buffer at vertex **storage slot 1** (or the bone buffer when the asset has none, so binding 1 is always satisfied and never read at count 0), writes the `≤ MorphsMax` weights into `MorphWeightBytes` **by morph index** (0 for unheld morphs, so the sum is order-independent) with the count in `info.x`, and pushes it at vertex uniform slot 1 (FR-004, FR-008, FR-012; research Decisions 2 and 3).

**Checkpoint**: `dotnet build` and `dotnet test` are green with every existing assertion unmodified (FR-008, SC-002); the shader declares the buffers `Gpu` declares and the renderer binds; an actor with no morph data renders exactly as before.

---

## Phase 3: User Story 1 - An actor's shape responds to its morph weights (Priority: P1) 🎯 MVP

**Goal**: A real morph sidecar describes an actor's shapes, and what the loader makes of it is asserted.

**Independent Test**: select an actor whose asset carries the fixture sidecar; at weight 1 the morphed region is displaced by the morph's offsets, at 0.5 by half, at 0 by none; an actor with no morph data renders as before.

- [X] T008 [US1] Author the feature's fixture: one actor asset's morph sidecar — an s-expression manifest and its data file — checked in under `assets/actors/…`, with the `Morphs` field added to that actor's `.manifest`. Produce it however the mesh's vertex range allows (a hand-written small target, or a throwaway generator that writes the two files), and keep it small enough to check in. This is the feature's only source of real morph data — nothing in the repository can generate one — and it is what the in-app half of SC-001 and the walkthrough select (research Decision 12).

- [X] T009 [US1] Extend `test/ActorTests.fs` with the loader's two pure halves, over strings and byte arrays only: a well-formed manifest parses into its names and targets, an unknown version raises, and a malformed document raises (FR-002); `buildMorphDeltas` places each target's offsets at `v * morphCount + m` for every vertex in its range and leaves every other sample zero, a morph with no normal deltas packs a zero normal, a target whose range runs past the vertex count raises, and a data file too short for the offsets named raises (FR-001, FR-003, SC-004); and with no morphs held the weight payload the renderer would push is all zeros with a count of 0, so nothing is displaced (FR-008, SC-001).

**Checkpoint**: the format and the loader are proven against real bytes, and a morph-less actor is provably unchanged.

---

## Phase 4: User Story 2 - A caller sets each morph's weight (Priority: P2)

**Goal**: A caller names a morph and a weight; the morph joins the actor's set if it is not held, is clamped and validated, and is adjusted if it is.

**Independent Test**: set a morph's weight and confirm the shape changes; ease it to 0 and confirm the morph stays held but contributes nothing; request a weight above 1 or below 0 and confirm it clamps; name a morph the asset does not carry and confirm a loud failure; hold eight and confirm a ninth fails loudly.

- [X] T010 [US2] In `world/Actor.fs`, add the weight algebra beside the animation one: `setMorphWeight (slots) (morphCount) (morphIndex) (weight)` — find the slot for that morph index and set its weight (clamped 0..1), or add one when none matches; a `morphIndex` outside `morphCount` raises, and adding past `Gpu.MorphsMax` raises through `SlotMap.Add`. And `removeMorph (slots) (morphIndex)` — free the matching alive slot, a no-op when there is none. Pure, int-indexed, and GPU-free (FR-005, FR-006, FR-007; research Decision 5).

- [X] T011 [US2] In `world/ActorRender.fs`, add the name-resolving wrappers `setMorphWeight (r: byref<ActorRender>) (name: string) (weight: float32)` and `removeMorph (r: byref<ActorRender>) (name: string)`: resolve the name against `r.Asset.Morphs`' names (raising when the asset carries no such morph, FR-007) and delegate to T010. These are the one path the Inspector calls (research Decision 10).

- [X] T012 [US2] Extend `test/ActorTests.fs` with the algebra: a weight clamps to 0..1; a morph absent is added and one held is adjusted with no second slot; `removeMorph` frees and is a no-op when absent; a morph index outside the asset's morphs raises; an add past `Gpu.MorphsMax` raises (FR-005, FR-006, FR-007; SC-003).

**Checkpoint**: a caller drives an actor's shape from code, up to the eight-morph bound.

---

## Phase 5: User Story 3 - The author sets an actor's morphs in the Inspector (Priority: P3)

**Goal**: The actor Inspector lists the morphs the actor holds and lets the author add, remove and re-weight them live.

**Independent Test**: `quickstart.md` steps 1–8 — the section appears only for an actor, a morph can be added, weighted and removed, the degenerate cases report, and nothing is persisted or undone.

- [X] T013 [US3] In `editor/Inspector.fs`, add a `Morphs` collapsing header inside the existing `if ref.Kind = EntityKind.Actor` block, beside the Animations section and with the same shape: reach the runtime renderer as that section does; a row per held morph (name, a `dragFloat` weight 0..1 that calls `ActorRender.setMorphWeight`, a `smallButton` that calls `ActorRender.removeMorph`), row widgets kept distinct with `ImGui.pushId`/`popId`; a `smallButton` per morph the asset carries and the actor does not hold, joining at weight 0; and the degenerate states as text — "(asset has no morphs)", "(all morphs added)", "(maximum 8 morphs)", and "(holds no morphs)" when the last one is removed — rather than offering an add that cannot succeed. It records no command and stores nothing in `EditorState` (FR-009, FR-010, FR-011; research Decision 10).

**Checkpoint**: an author adds a morph, drags its weight, watches the shape change, and removes it — with no undo step and nothing persisted.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Whole-feature validation, the lines this feature makes false, and the properties it claims.

- [X] T014 Run `dotnet build --no-incremental` and `dotnet test test/fsharp-game.Tests.fsproj` and confirm the build is clean with no new warnings and the suite passes **with every assertion the existing animation, skinning and pose suites already made unmodified** (FR-008, SC-002, SC-005). `--no-incremental` is the build to run: an incremental build hides warnings on unchanged files. Record the counts.

- [X] T015 [P] Correct the lines this feature makes false in the repository docs. `docs/ROADMAP.md`: phase 3's note that morphs are deferred is now built. `docs/EDITOR_DESIGN.md`: D-011's list of direct writes (the cast binding, the zone registry, the actor animation section) gains the Inspector's Morphs section — a fourth surface that edits runtime state rather than recording a command. `docs/PROJECT_STRUCTURE.md` already describes `world/Actor.fs` as "IQM model + morphs" and needs no change.

- [X] T016 [P] Confirm the structural properties this feature claims, reading rather than assuming: `render/Mesh.fs` is unchanged (the vertex layout, the scratch, and `draw`'s signature); `world/Entity.fs`'s `ActorState` gains no morph field, so a payload still cannot hold a renderer or its slots; `state/Command.fs` gains no command and `state/EditorState.fs` no field (FR-011); `world/Systems.fs` gains no morph logic; and `render/ImGui.fs`/`native/` are untouched (no new widget). Record the reads.

- [X] T017 Rebuild the shader (`glslc -fshader-stage=vert shaders/mesh.vert -o shaders/mesh.vert.spv`) and walk `quickstart.md` steps 1–8 in the editor with the fixture actor selected: the section shows only for an actor; a morph can be added at weight 0, weighted (the region moves by the offsets, halves at 0.5, returns at 0), several add together, and one removes; the degenerate cases report; moving the actor then changing its morphs leaves the move as the one Ctrl+Z step and persists nothing; and an actor whose asset carries no morphs still plays its Idle clip. **Not run if the agent environment cannot be driven interactively** — and if it is not run, say so rather than implying the shader or the section was exercised.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks every user story**. T002 → T003 in step (the shader and the descriptor counts must agree); T004 → T005 (the field changes the actor constructor, so `Assets` must follow in the same compile); T006 → T007 (the renderer reads the slot type); and T004/T006 → T007 (the renderer reads both the asset's morphs and the slot type).
- **User Stories (Phases 3–5)**: all depend on Phase 2. US1 (T008–T009) is the fixture and the loader's assertions; US2 (T010–T012) is the algebra and its setters; US3 (T013) depends on US2's T011 (the section calls the wrappers).
- **Polish (Phase 6)**: depends on all of the above; T017 additionally depends on T008's fixture.

### Within each phase

- Phase 2: T002 → T003; T004 → T005; T006 → T007.
- Phase 4: T010 → T011 → T012.
- Phase 5: T013 after T011.
- Phase 6: T014 → T017; T015 ∥ T016.

### Parallel Opportunities

- **T009 ∥ T010**: US1's loader assertions (`test/ActorTests.fs`) and US2's slot algebra (`world/Actor.fs`) are different files and neither needs the other.
- **T013 ∥ T014**: once T011 lands, the Inspector section (`editor/Inspector.fs`) and the whole-suite check are different targets.
- **T015 ∥ T016** in the polish phase: docs and reads, no dependencies.

### Same-file constraint

**`test/ActorTests.fs`** is extended by T009 and T012, so they run in priority order (US1 → US2) rather than in parallel. **`world/Actor.fs`** is touched in both Phase 2 (T006) and Phase 4 (T010), and **`world/ActorRender.fs`** in both Phase 2 (T007) and Phase 4 (T011), so the US2 tasks follow Phase 2.

---

## Implementation Strategy

### MVP

**US1 is the MVP.** It is the capability — an actor's shape responds to a morph weight — and it covers the whole path from the sidecar to the displaced vertex; US2's algebra and US3's section add the handles to it.

1. Complete Phase 1 (no build or dependency change) and Phase 2 (the vertex stage, the asset's morph data, and the renderer that reads both).
2. Complete Phase 3 (US1) — the fixture and the loader's assertions.
3. **STOP and VALIDATE**: `dotnet test`, then confirm an actor with no morphs is visually unchanged.

### Incremental Delivery

1. Setup + Foundational → the vertex stage can displace, the loader can read, the renderer can weigh.
2. US1 → a real sidecar loads and its displacement is asserted (MVP).
3. US2 → a caller sets morph weights from code (`ActorRender.setMorphWeight`).
4. US3 → the author sets them in the Inspector.
5. Polish → the whole-suite check, the docs, the reads, and the shader-rebuild walkthrough.

---

## Notes

- [P] tasks touch different files and have no incomplete dependencies.
- **T002–T007 are one build, not six**: adding `Morphs` to `ActorAsset` breaks its only constructor (`Assets.loadActors`), adding `MorphSlot` breaks nothing until the renderer reads it, and `draw` reads the asset's morphs, the slot type, and the shader's bindings — so the tree is only green once all of it agrees. This is the same shape as 036's T002–T004.
- **The fixture (T008) is a gate, not a nicety**: nothing in the repository can generate a morph sidecar (IQM has no shape keys; the glTF loader serves environments and props), so without it the loader is only ever fed test-authored strings and the walkthrough has nothing to select. It is US1's, not Polish's, because it is part of what US1 delivers.
- **The cap lives in `Gpu.fs`, not `Actor.fs`**: `MorphsMax` is a shader bound (the weights array's length and the loop's ceiling), like `BonesMax`, and `render/` compiles before `world/Actor.fs`, so it cannot live where `MaxAnimationSlots` does. `Actor.fs`'s algebra takes the morph **count** as an argument instead, which keeps it free of both the asset and the GPU (research Decision 4).
- **The asserted detail most likely to be got wrong** is T009's packing index: `v * morphCount + m`, with the position sample immediately followed by the normal sample for the same pair, is what T003's shader indexes — a packing test that only counted values would not catch a transposed `v`/`m`.
- **The shader must be recompiled before the app shows anything**: the `.spv` is loaded from disk at startup, and the `.fsproj` copies it, so a stale `.spv` renders the old vertex path — T017 leads with the `glslc` step for that reason.
- Verify `dotnet test` passes after each phase; commit per task or logical group.

---

## Outcomes

Every task is complete. The tree builds warning-free and the suite is green; the walkthrough (T017) was **not** run, for the reason recorded below.

### Validation

- `dotnet build fsharp-game/fsharp-game.fsproj` — clean, 0 warnings, 0 errors.
- `dotnet build test/fsharp-game.Tests.fsproj --no-incremental` — clean, 0 warnings, 0 errors.
- `dotnet test test/fsharp-game.Tests.fsproj --no-build` — **285 passed, 0 failed, 0 skipped** (270 before this feature; **15 tests added**), with every pre-existing assertion unmodified.
- `glslc -fshader-stage=vert shaders/mesh.vert -o shaders/mesh.vert.spv` — recompiled (5,820 bytes); the vertex shader is where the feature's only GPU-visible behaviour lives.

### The one compile blocker

`world/ActorRender.fs`'s `removeClip` read `Actor.findAnimIndex`, which `036` renamed to `Actor.tryFindAnimIndex`; corrected. `removeClip`'s net diff against `036` is now nil, so this was a stale call in the working tree rather than a `036` defect.

### The fixture (T008)

`assets/actors/Akane/` gains `akane.morphs` (the s-expression manifest) and `akane.morphs.bin` (91,119 little-endian float32, 364,476 bytes); `akane.manifest` gains `[Morphs "actors/Akane/akane.morphs"]`.

The data is **real**, converted from `odin-game`'s `assets/actors/Akane/morphs.json` + `morphs.bin` by a throwaway generator that was deleted after running. The two `Akane.iqm` files are md5-identical (`9bbf1636…`), so the mesh index → combined-buffer mapping holds; odin's per-mesh packed deltas were repacked to this engine's absolute vertex ranges and sequential offsets. The fixture carries **exactly the eight-morph cap** — `Fcl_ALL_Neutral`, `Fcl_ALL_Joy`, `Fcl_ALL_Angry`, `Fcl_ALL_Sorrow`, `Fcl_ALL_Surprised`, `Fcl_ALL_Fun`, `Fcl_EYE_Close`, `Fcl_MTH_A` — all position-only (`HasNormalDeltas false`, physically correct for expression morphs), all targeting the Face submeshes (vertices 7,829–11,829).

Loading the checked-in fixture through the real loader was verified by driving `ActorAsset.parseMorphManifest` → `usableMorphs` → `buildMorphDeltas` against the real assembly: **8 morphs**, a dense buffer of **1,280,320 floats** (20,005 × 8 × 8), **50,992 nonzero samples**.

`T001` confirmed the s-expression shape ends up canonical: an **array of records is a wrapping list** — `[Morphs [[[Name …] [Targets […]]] […]]]` — which was found by round-tripping `scstring`/`scvalue` under `Prime 11.5.2` rather than guessed. Strings may be quoted (this fixture quotes them, as every other manifest does) and booleans may be lowercase.

### Deviations and findings recorded, not relitigated

- **`buildMorphDeltas`'s signature** differs from `contracts/actor-morphs.md`, which sketches `manifest -> data -> vertexCount -> morphCount`. The implementation is `(morphs: MorphDefinition array) -> data -> vertexCount`: the caller passes `usableMorphs manifest`, so the morph count is the array's length and the "no morphs describe nothing" edge case is decided once, in `usableMorphs`. Recorded here; the contract is not rewritten during implementation.
- **`docs/ROADMAP.md` needed no change, contrary to T015.** There is no morph-deferral note to correct: `036`'s commit `a4a9cd7` already replaced the only `morph` mention in the roadmap (`world/Actor.fs — IQM model + animation/morph state` → `… + animation slots + CPU skinning`), confirmed with `git log -S morph -- docs/ROADMAP.md`. The statement that morphs are deferred lives in `specs/009-animated-character/spec.md`, where it remains an accurate record of *that* spec's scope — this feature is the later phase it names. `docs/EDITOR_DESIGN.md` D-011 was updated: the Inspector's actor animation and morph sections (`specs/036`, `specs/037`) are the third direct-write surface. `docs/PROJECT_STRUCTURE.md`'s `world/Actor.fs — IQM model + morphs` was already correct.
- **Props and environments were not edited.** `scanDir`/`readManifest` are shared by actors, props and environments, so the new `Morphs : string` on the private `Manifest` is parsed for all three. `Prime`'s `scvalue` defaults an absent field (verified: an environment manifest without `Morphs` parses, `Morphs = ""`), so the three props and two environments manifests still load and `String.IsNullOrEmpty morphsPath` yields `None`. No `[Morphs ""]` filler was needed.
- **One degenerate Inspector state is unreachable with this fixture.** Because the asset carries exactly eight morphs, holding them all reaches "(maximum 8 morphs)" before "(all morphs added)"; the latter shows only for an asset carrying fewer than eight. Both texts exist and both are correct; only one is observable per asset. `036` had the same shape for clips.

### What is not verified by automation

- **T017 was not run.** The agent environment cannot be driven interactively, so the shader's on-screen displacement, the Inspector's Morphs section in a live frame, and walkthrough steps 1–8 are **not** exercised. The `.spv` compiles and the descriptor counts in `Gpu.fs` (`2u 2u 0u`) match what `mesh.vert` declares, but that is agreement in source, not a rendered frame.
- **SC-005 is structural, not asserted.** The delta buffer is uploaded once, in `Assets.loadActors`, through `Gpu.createStorageBuffer`; `Actor.packMorphWeights` clears and fills a reused 48-byte array via `Array.Clear` + `MemoryMarshal.Cast`, so the per-frame path allocates nothing — but no test drives `draw`, which needs a GPU. The existing zero-allocation assertions on `blendPose` and `samplePose` still pass.
- **FR-005's order-independence is asserted through the payload layout**, not by comparing two slot orders: `packMorphWeights` indexes by morph index, so a payload built in slot order would misplace the weights — which the `packMorphWeights places each weight at its morph index` test holds out of index order to catch.

### T016 structural reads

The working-tree diff touches only `render/Gpu.fs`, `world/Actor.fs`, `world/ActorAsset.fs`, `world/Assets.fs`, `world/ActorRender.fs`, `editor/Inspector.fs`, `shaders/mesh.vert` (+ `.spv`), `test/ActorTests.fs`, `docs/EDITOR_DESIGN.md`, and the Akane fixture. Unchanged, as claimed: `render/Mesh.fs` (vertex layout, scratch, `draw` signature), `world/Entity.fs` (no morph field on `ActorState`), `state/Command.fs` (no command), `state/EditorState.fs` (no field), `world/Systems.fs` (no morph logic), `render/ImGui.fs` and `native/` (no new widget or binding), and both `.fsproj` files (no file added or moved).
