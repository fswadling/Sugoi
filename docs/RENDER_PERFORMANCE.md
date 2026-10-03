# Render Performance: the ArtRoom draw-call cliff

**Status**: Open investigation, work stream separate from the Spec Kit feature
stream. Started after `specs/016-walk-around` was closed; deliberately **not**
tracked inside 016 (see "Provenance" below).

**Scope**: the scene render pass only — the cost of issuing one draw call per
glTF submesh for a large, static environment. UI/ImGui/gameplay-update costs are
out of scope (and measured as negligible).

**Goal**: get a frame with the ArtRoom loaded back under the 16.67 ms budget
(60 fps). Currently ~22–38 ms, almost all of it in command recording.

---

## 1. Problem statement

Loading `assets/environments/ArtRoom/ArtRoom.gltf` (463 environment parts, 521
submesh draws, 191 unique submeshes, 51 distinct materials) pushes `record` from
~0.4 ms to **~20–30 ms per frame**. An empty scene is fine.

Measured averages (`record` is the draw/recording phase; see §3):

```
empty scene:   record  0.4 ms
ArtRoom:       record 17–30 ms
update:        0.02–0.65 ms   (systems + physics — negligible)
acquire:       ~0.1 ms        (not GPU back-pressure)
```

The frame is **CPU-bound on draw submission**, not GPU-bound. The GPU has
plenty of spare time; the CPU spends tens of milliseconds recording 521 nearly
identical small draws.

This is not a JIT/Debug-configuration problem. Debug merely tips an already
over-budget frame over the edge (the software frame limiter only sleeps when the
frame finishes early). See §4.

---

## 2. Asset facts

| Quantity | Value |
|---|---|
| Environment parts | 463 |
| Submesh draws issued (one part can have >1 submesh) | **521** |
| Unique submeshes | 191 |
| Distinct materials | 51 |
| Meshes with >1 primitive | 1 (one mesh has 2; the other 164 have 1) |

Per-part transforms: the glTF stores height along local Z; the root node
`Art_room` (node 522) applies `rotation [0.7071, 0, 0, 0.7071]` and
`scale 0.01`, so world space is Y-up metres, floor at Y = 0 with normal +Y.

So: 521 draws collapse to **191 unique submeshes**, and every submesh has exactly
one material — grouping draws by submesh groups materials for free.

---

## 3. Instrumentation

Per-phase timing was added to `Engine.fs` (committed as part of `c7b3167`). It
prints once per second:

```
avg 25.15ms (last 16.81) | input 0.15  update 0.03  ui 7.40  imgui 0.02  acquire 0.47  record 17.15  submit 0.00 | phases 25.23 | actors 1 props 0 parts 463
```

| Phase | Covers |
|---|---|
| `input` | poll + gather + ImGui `NewFrame` |
| `update` | `ScreenManager.update`: systems, `Physics.step`, editor |
| `ui` | `ScreenManager.draw` + overlay |
| `imgui` | `ImGui.render` + get draw data |
| `acquire` | command buffer + swapchain acquire |
| `record` | draw-data prep, uploads, scene pass, ImGui pass (pure CPU recording) |
| `submit` | `SubmitGPUCommandBuffer` + `applyTransition` (blocks when GPU busy) |

`phases` is the sum; the gap to `avg` is idle sleep (headroom). Compare `avg` to
`phases`, not to `last`.

**Re-cut `record` / `submit` (uncommitted):** the lap boundary was moved so
`lap 5` sits *before* `SubmitGPUCommandBuffer`. Previously `record` wrongly
absorbed the blocking submit. Post-change, `record` is pure CPU recording and
`submit` shows the GPU block. **This re-cut has not yet been observed in a real
run.** (It is retained after the perf revert because it is an independent
measurement-correctness fix, not part of the failed attempts.)

The temporary `draws`/`culled` counters lived in `EnvironmentRender` and were
removed with the reverted culling work (§5a). If instance-level culling is
reintroduced, re-add equivalent counters.

---

## 4. What we ruled out

- **JIT / Debug configuration.** `DebuggableAttribute` is per-assembly; project-
  owned per-frame work measured ~1 ms. `sprintf` measured identical in Debug and
  Release. Debug is a symptom, not the cause.
- **Gameplay update / physics.** `update` is 0.02–0.65 ms.
- **GPU back-pressure.** `acquire` stays ~0.1 ms; `submit` was small (before the
  re-cut, it was hidden inside `record`).
- **Bind/state deduplication.** See §5.

---

## 5. What we tried (and the negative results)

These were implemented, measured, and **reverted** (see §9) — they produced zero
measurable change in `record`. That negative result is the key evidence for §6.
They are retained here as documented dead ends so nobody re-treads them.

### 5a. Frustum culling (reverted)

- `render/Camera.fs`: added `Frustum`, `Plane`, `frustum` (six world-space clip
  planes derived from `viewProjection`, projection mapped to z ∈ [0, 1]) and
  `intersectsBox` (conservative — transforms all 8 corners, only culls boxes
  entirely outside every plane).
- `EnvironmentRender` gained a `Bounds : Gltf.Bounds` field. `draw` composes the
  world matrix and early-returns when `intersectsBox` is false.
- `EnvironmentRender.create` gained a `bounds` parameter.
- `world/Assets.fs`: `createPartRender` looks up `part.Bounds` from
  `asset.Parts` (fails loudly with `failwithf` if no part matches the submesh
  index); `createPropRender` passes `asset.Bounds`.
- `screen/GameplayScreen.fs`: `renderScene` builds the frustum once and passes it
  down.
- `test/CameraTests.fs`: 6 new tests for the frustum math.

**Unquantified.** `culled 0` has never been observed; it may be near-zero if the
camera sees most of the room. If so, culling contributes little and instancing
does nearly all the work.

### 5b. Bind de-duplication (reverted)

- Pipeline bind hoisted out of `StaticMesh.draw` up into `renderScene`, bound
  once for the whole static block.
- Fragment-sampler rebind skipped when the material array reference is unchanged:
  `StaticMesh.resetSamplers()` clears a `boundSamplers` cache at block start.
- `StaticMesh.draw` no longer binds the pipeline.

Measured binds/frame: **521 pipeline binds → 1**; **521 sampler binds → 180**
with consecutive dedup; **51** if grouped by material. Eliminating ~860
binds/frame had **zero measurable effect**.

**Do not chase this further.** Grouping 180 → 51 is more of the same. The
negative result is the point.

---

## 6. Root cause hypothesis: per-draw CPU overhead in SDL_GPU

The evidence points at SDL_GPU's per-draw CPU cost being the dominant term, not
state changes. External corroboration:

| Source | Finding |
|---|---|
| [SDL #12757](https://github.com/libsdl-org/SDL/issues/12757) | 600 trivial `SDL_DrawGPUPrimitives` draws = 212 fps → ~**8 µs/draw**. Maintainer: "212 fps for issuing 600 redundant draw calls doesn't seem like poor performance"; asked the reporter whether they used a nested loop of individual draws or **instancing**. |
| [SDL #12674](https://github.com/libsdl-org/SDL/issues/12674) | slouken: "we don't have any standalone examples of large mesh workloads … there is **a more efficient way of rendering meshes**". |
| [SDL #11537](https://github.com/libsdl-org/SDL/issues/11537) | Instanced 1M sprites → 90 fps vs 180–200 fps raw DX11/Vulkan. |
| [SDL #12610](https://github.com/libsdl-org/SDL/issues/12610) | Per-draw CPU scan with ~760 draws → **30× slowdown**. |
| SDL wiki / DeepWiki | `MAX_FRAMES_IN_FLIGHT = 3`; "beginning a pass is relatively expensive"; `PushGPU*UniformData` is the *recommended* per-draw path (so pushes are not a bug, just per-draw cost). |

Back-of-envelope: 521 draws × ~8 µs ≈ 4 ms for trivial draws; real geometry and
the per-draw uniform pushes/pipeline lookup push that to ~20 ms. This matches the
observed `record` time and the fact that removing binds did nothing while draw
count stayed at 521.

**Conclusion: reduce the number of draw calls via instancing.**

---

## 7. Plan: instancing

**Prior art:** Nu solves this in its own ecosystem with instancing-by-default and
freeze-time pre-batching; see §12 for the code paths and the comparison.

### Confirmed: the geometry is already shared

This is smaller than it first looks. `core/Gltf.fs` already **deduplicates
instanced meshes** at parse time (`Gltf.load`, "Read every mesh's primitives once
(deduplicating instanced meshes)", ~L313-324): each glTF mesh's primitives become
one `Submesh`, and every node that references the mesh shares that range via
`Part.SubmeshIndex`/`SubmeshCount`. `loadStaticModel` then uploads each unique
`Submesh` exactly once as a `StaticMesh` (`world/Assets.fs`), so
`asset.Submeshes` is the 191 unique submeshes.

The 521 draws come solely from `EnvironmentRender.draw` looping a part's submesh
range and calling `StaticMesh.draw` per submesh (`world/EnvironmentRender.fs:34-36`),
each with its own full state block. Nothing needs re-uploading or re-merging —
but the grouping key is **not** a bare submesh index (see below).

### Grouping key: per-asset submesh identity, not a global index

`EnvironmentRender.SubmeshIndex` is relative to `r.Asset.Submeshes`, and `r.Asset`
depends on where the instance came from:

- `createPartRender` uses `manager.Environments.[environmentAssetId]`
  (`world/Assets.fs:255-257`).
- `createPropRender` uses `manager.Props.[assetId]` (`world/Assets.fs:249-251`).
- `draw` reads `r.Asset.Submeshes.[i]` (`world/EnvironmentRender.fs:35`).

So the same index can name **different geometry and material** in different
assets. A placed prop and an environment part can both have submesh index 0
while pointing at different vertex/index buffers; keying the batch on the bare
index would put them in one instanced draw and render one with the wrong
buffers/material. The key must be the shared submesh identity — `(asset,
submeshIndex)`, or directly the `StaticSubmesh.Mesh` + `Material` pair, since
that is what actually has to match for a single draw.

Consequently **191 is environment-only**. With props present the distinct-submesh
total is higher (one per prop asset, plus the environment's 191), still ≪ 521, so
the conclusion is unchanged. The measured run had `props 0 parts 463`, which is
why this has not bitten yet; placing a prop in the editor would trigger it.

### Confirmed invariant

The draw set is `Zone.Props` (placed props; each renders its whole asset) plus
`environment.Parts` (imported parts; each renders its submesh range). The
environment entity itself draws nothing, and environment parts are *not* in
`Zone.Props` (`world/Zone.fs`: "Placed props only — an environment's imported
parts live in its own map"). Both loops go through `EnvironmentRender`, so the
gather must cover both.

### Approach: one instanced draw per submesh

- Build a **per-frame instance buffer** — one `mat4 model` + `mat3 normal` per
  instance; 521 × 112 B ≈ 58 KB.
- Issue **one instanced draw per unique submesh identity** (asset + index) →
  **521 draws collapse to 191** for the ArtRoom environment alone; more once props
  are placed, still ≪ 521. Because each submesh has exactly one material, this
  groups materials for free (and makes the sampler dedup in §5b almost redundant).
- Move `model`/`normalMatrix` out of the pushed `PerDraw` uniform and into
  **per-instance vertex attributes** (`location 3..9` in `shaders/static.vert`).
  `viewProj` stays a pushed uniform (now the only slot it carries).
- `SDL.DrawGPUIndexedPrimitives` already takes `instance_count`; pass the number
  of instances for that submesh.

### Decision: instance *vertex* buffer, not a storage buffer

The original sketch used a vertex storage buffer indexed by `gl_InstanceIndex`
(Nu's approach, and the documented convention in `RENDERING_NOTES.md` §4). An
instance **vertex buffer** is preferred here:

- SDL_GPU has `SDL.GPUVertexInputRate.Instance` and `GPUBufferBinding.Offset`
  (in bytes), so per-submesh ranges bind directly. Note SDL_gpu asserts that
  `instance_step_rate` is **0** — `InputRate.Instance` alone advances one record
  per instance; a non-zero step rate fails pipeline creation with
  "instance_step_rate must be 0!".
- No `NumStorageBuffers` on the pipeline, no manual `gl_InstanceIndex` indexing,
  and no storage-buffer binding-slot arithmetic (`RENDERING_NOTES.md` §4).
- Same result and same shader data; only the binding mechanism differs.

Fall back to the storage-buffer form only if the instance vertex buffer hits an
SDL_GPU limit.

### Instance layout

One instance = 112 bytes, all `Float4` attributes (7 attributes, stride 112):

| Location | Contents | Offset |
|---|---|---|
| 3 | `model` row 0 (M11..M14) | 0 |
| 4 | `model` row 1 (M21..M24) | 16 |
| 5 | `model` row 2 (M31..M34) | 32 |
| 6 | `model` row 3 (M41..M44) | 48 |
| 7 | `normalMatrix` row 0 | 64 |
| 8 | `normalMatrix` row 1 | 80 |
| 9 | `normalMatrix` row 2 | 96 |

Rows are written in the same component order as the existing push, so the
row-major / no-transpose convention (`RENDERING_NOTES.md` §7) is preserved: the
shader builds the matrices from these as columns, exactly as the single pushed
`mat4` did. The normal matrix is stored (not derived via an in-shader `inverse()`)
to keep today's behavior for non-uniform scale.

### Gather: two-pass, allocation-free

Per frame, into a reusable CPU float/byte array sized to the instance high-water
mark (521), with a reusable count/cursor table keyed by submesh identity
(asset + index — see "Grouping key" above), not by a global submesh index:

1. **Count** — walk props + parts; for each submesh in a part's/prop's range,
   increment the count for its (asset, index) key (~521 increments).
2. **Prefix-sum** — turn counts into per-submesh start offsets.
3. **Fill** — reset cursors to the offsets, walk again composing
   `world = Entity.modelMatrix transform * r.OrientationMatrix` and writing the
   model + normal rows at the instance slot.
4. **Upload once** — map a transfer buffer, copy, `UploadToGPUBuffer` in a copy
   pass before the scene pass (same pattern as `DebugDraw.upload`,
   `render/DebugDraw.fs:314-332`).
5. **Draw** — for each submesh with count > 0: push `viewProj` + the fragment
   material/light uniform, bind vertex buffer slot 0, bind the instance buffer at
   slot 1 with byte offset `start * 112`, bind the index buffer, and
   `DrawGPUIndexedPrimitives(indexCount, count, 0, 0, 0)`.

Counts are recomputed each frame for correctness (parts/props can be spawned or
deleted in the editor); 521 increments is noise next to 20 ms. If profiling ever
shows the gather, cache the counts at environment load (the part layout is fixed
until an edit) — but do not build that up front.

### Files to touch (refined)

| File | Change |
|---|---|
| `shaders/static.vert` | Read `model`/`normalMatrix` from per-instance attributes; `PerDraw` shrinks to `viewProj`. |
| `shaders/static.vert.spv` | Recompile with `glslc` (same filename; `CopyShaders` target in the `.fsproj` is unchanged). |
| `render/Gpu.fs` (`createStaticPipeline`) | Add vertex buffer slot 1: pitch 112, `InputRate = Instance`, `InstanceStepRate = 0`; 7 `Float4` attributes at locations 3..9. |
| `render/StaticMesh.fs` | Instanced-draw variant: push `viewProj` + material, bind slot 0 + slot 1 (with offset), `DrawGPUIndexedPrimitives(count, instanceCount, …)`. |
| `render/InstanceBuffer.fs` (new) | Grow-only instance GPU buffer + transfer buffer + CPU staging; `ensureCapacity`/`upload`/`bind`. Pure GPU, no `world` dependency. |
| `world/EnvironmentBatch.fs` (new) | The gather + per-submesh draw. Keyed by `(asset, submeshIndex)` (see "Grouping key"), not a global index. Lives in `world/` because it walks `Zone`; `render/` may not depend on `world/` (see `PROJECT_STRUCTURE.md`). |
| `screen/GameplayScreen.fs` (`renderScene`, ~L99-107) | Replace the two `EnvironmentRender.draw` loops with one `EnvironmentBatch` call. |
| `test/EnvironmentBatchTests.fs` (new) | Unit-test the pure count/prefix-sum/fill planner (GPU paths can't run headless). |
| frustum cull | Still not required. If ever reintroduced it reduces **instances**, not call count — but §5a says don't. |

`EnvironmentRender.draw` stops issuing draws; `EnvironmentRender` becomes a
producer of instance data (or is retired in favour of `EnvironmentBatch` reading
its fields).

### Notes / hazards

- The current design shares one `PerDrawUniformBytes` / `MaterialUniformBytes`
  array per *mesh*, a latent data hazard once multiple instances draw from the
  same mesh in one frame. Instancing removes it (per-instance data comes from the
  instance buffer, not a shared scratch array).
- The vertex uniform push shrinks from 192 B (per draw × 521) to 64 B (per
  submesh × ≤191); the fragment material/light push stays per submesh.
- Don't chase the `Material` push further — ≤191 pushes, and §5b showed binds
  don't matter.
- Actors keep the existing `Mesh` pipeline (a handful of draws); no scope creep.

### Cheap confirmation (optional, before the refactor)

Add a temporary "draw only every Nth submesh" switch to show `record` scaling
**linearly with draw count** at constant geometry. The SDL issues in §6 are
already fairly conclusive, so only do this if certainty is wanted.

---

## 8. Secondary issue: missing mipmaps — FIXED

`render/Texture.fs` set `Sampler.MipmapMode <- Linear` but textures were created
with `NumLevels <- 1`, and `SDL3.Image.LoadGPUTexture` creates textures "with no
mip levels", so minification sampled the base level — aliasing plus a
texture-bandwidth cliff.

**Fixed in `dd7f8b1` (017-texture-mipmaps):** textures are now created with a full
mip chain and `SDL_GenerateMipmapsForGPUTexture` is called outside any render
pass. No further action.

---

## 9. Repository state

As of `dd7f8b1` (clean working tree):

- `0174343` *"Close out 016 walk around; document render performance findings"* —
  this doc, the `RENDERING_NOTES.md` pointer and the `record`/`submit` re-cut
  (see §3) are committed.
- `dd7f8b1` *"Add full mip chains to loaded textures (017-texture-mipmaps)"* —
  §8 is fixed.
- **No instancing code exists yet** (searched `render/`, `world/`, `screen/`,
  `shaders/`); §7 is unimplemented and there is no `018-*` spec yet.
- The §5 attempts remain reverted: `render/Camera.fs`, `render/StaticMesh.fs`,
  `world/EnvironmentRender.fs`, `world/Assets.fs`, `screen/GameplayScreen.fs`,
  `test/CameraTests.fs` are back to `c7b3167`; the `draws`/`culled` counters are
  still absent from the `Engine.fs` log.

**Instancing is now implemented (uncommitted, `specs/018-instanced-draws`).**
Per §7: `shaders/static.vert` takes per-instance transform attributes, the static
pipeline gains vertex slot 1 (112-byte instances), `render/InstanceBuffer.fs` and
`world/EnvironmentBatch.fs` are new, and `Engine.fs` logs `draws`/`instances`.

**Measured** with the ArtRoom loaded: **`draws 191 instances 521`** (from 521
draws), and `record` falls from ~20–30 ms to **~1.1–1.6 ms**. Frame `avg` ~16.8 ms
and `phases` ~4–5 ms — comfortably within the 16.67 ms budget (§1). The
`instance_step_rate` = 0 assertion was hit once and fixed in `render/Gpu.fs`.

**Measured with placed props** (SC-004): adding copies of one prop leaves `draws`
flat while `instances` grows — the first placement registered the prop asset's 2
distinct submeshes (`draws 191 → 193`), and 5 more copies added 0 draws
(`draws 193`, `instances 521 → 531`). With 6 props and a player, `record` is
~2.7–2.9 ms; with the player actor too it rises to ~5–7 ms (the unchanged actor
draw path, not the batch). **Those figures were Debug.** Re-measured with the
ArtRoom and no props (2026-09-13): one skinned actor adds **~0.1 ms** to `record`
in Release (~1.1 ms → ~1.2 ms) but **~3 ms** in Debug (~2.8 ms → ~6.1 ms) — a
~20–30× skew. So the per-actor cost is a Debug-JIT artifact of the CPU skinning
math (`Actor.samplePose`) plus the bone upload, not a production problem.
`023-actor-skinning-hot-path` removed a per-joint reference tuple (`sampleJoint`
now returns a struct tuple) and a redundant multiply in the local joint transform
(`localMatrix` writes the translation row directly). **Measured outcome: these did
not reduce the Debug per-actor `record` delta** (~3.2 ms after vs ~3.0 ms before —
unchanged within run-to-run noise). The per-actor cost is dominated by the
remaining managed work (channel sampling, `Quaternion.Slerp`, and the rest of the
joint matrix math), which would need profiling to localise. The 023 changes are
retained as allocation/multiply hygiene only.

**Measured with different prop assets** (FR-002): three different prop assets each
added their own 2 distinct submeshes (`draws` 193 → 195 → 197), while a duplicate
of an existing prop added 0 (`draws` stayed 197). This is the per-asset keying
working — a bare submesh index would have merged the different assets.

Visual identity (SC-003) still needs a look by eye (quickstart scenarios 3–4).

The last recorded full run was `dotnet build` clean and `dotnet test` → 96 passed
(the earlier 102 included the frustum `CameraTests` that §5a reverted); the 2
benign `Engine.fs` warnings (`FS0009`/`FS0051` from `NativePtr.toNativeInt (&&ev)`)
are noted in `RENDERING_NOTES.md` "Known follow-ups".

---

## 10. Next steps (in order)

1. ~~**Instrument first.**~~ — done: `Engine.fs` now logs `draws`/`instances`
   from `EnvironmentBatch` (`specs/018-instanced-draws` T012).
2. ~~**Implement instancing (§7)**~~ — done (uncommitted, `018-instanced-draws`);
   measured `record` ~1.2 ms and `draws 191 instances 521` with the ArtRoom (§9).
3. **Only if the gather/upload shows up**, cache per-submesh counts at
   environment load (Nu's freeze-time pre-batch, §12) — don't build it up front.
4. ~~Fix the missing mipmaps~~ — done in `dd7f8b1` (§8).
5. If instance-level culling is ever reintroduced, re-add `draws`/`culled`
   counters — but §5a says don't reintroduce culling.

---

## 11. Pitfalls / gotchas

- **Do not blame JIT/Debug for the draw-call cliff.** Measured and disproved
  (§4) — 521 draws cost in both configs. Debug *does* skew CPU-heavy struct code,
  though: see the skinned actor path in §9 (~20–30×), and measure actors in
  Release only.
- **Do not chase further bind/state deduplication.** Done; proven ineffective
  (§5b).
- **Don't report `culled` as working until it's been seen non-zero.**
- `ScreenTransition.None` shadows `Option.None` in files that `open Screen`
  (e.g. `Engine.fs`, `GameplayScreen.fs`) — use `| _ -> ()`.
- `SlotMap` is `Handle`-only (`Get` / `IsAlive` / `Occupy` / `Remove`); `TryGet`
  was deleted deliberately. Resolve by reference (`&…Get handle`) and mutate in
  place — never copy out and re-assign.
- `Zone.meshCollider` takes `EnvironmentAsset.StaticSubmesh`, not
  `Gltf.Submesh`.
- Headless limits: actors need a GPU device, so actor-mutating `Systems`
  functions and anything drawing can't be unit-tested. Pure helpers and the
  frustum math can be (that's how `CameraTests` covers culling).
- **Prop-placement hitches are not the batch.** Placing a prop in the editor
  intermittently stalls `update` for ~200–280 ms (and briefly raises `ui`),
  because spawning loads the prop asset and builds its Jolt mesh collider.
  `record` stays ~2–3 ms through it, so it is a separate editor/spawn cost, not
  instancing. **Dragging or nudging** a placed prop used to pay that collider
  rebuild every frame; `021-in-place-mesh-transform` makes a move/rotate update the
  existing body in place (only a scale change rebuilds), so the per-drag stall is
  gone. The spawn-time asset load is unchanged. A long debugger pause also leaves
  `acquire` elevated for a while (swapchain/present backlog), which then recovers.
- Don't run the game from here (no display) — ask for the log lines.

---

## 12. Prior art: Nu's instancing-by-default

`Repos/Nu` (the F# game engine that inspired this project) hits the same problem
from the other direction — its renderer is instancing-by-default, and it runs its
real, unoptimized Debug renderer in budget because it submits ~one draw per unique
surface instead of one per object. There is **no Debug/JIT-specific code path**:
`Nu/Nu/Nu/Nu.fsproj` Debug only sets `<Tailcalls>False</Tailcalls>` and
`DefineConstants=TRACE` (no `DEBUG` define), and nothing in the renderer is
`#if DEBUG`-gated. This reinforces §4: Debug is not the cause, draw count is.

How Nu does it (source-read of `Repos/Nu`, not measured):

- **Freeze-time grouping** (`Nu/Nu/Nu/World/WorldEntityHierarchy.fs`): a static
  hierarchy is *frozen*; `getFrozenArtifacts` groups every static-model surface by
  `(clipped, material, surface, depthTest, renderType)` (~L207-211, ~L266-270),
  producing `StaticModelSurfacePreBatch` entries whose `StaticModelSurfaces` array
  is the instance list. `RenderFrozenPreBatches` (~L414-432) culls the hierarchy
  once by its aggregate bounds and enqueues one `RenderStaticModelSurfacePreBatches`
  message.
- **Renderer batching** (`Nu/Nu/Nu/Render/Renderer3d.fs`):
  `RenderTasks.DeferredStatic : Dictionary<PhysicallyBasedSurface, … List>`
  (~L1076-1077); `categorizeStaticModelSurface` (~L2447-2458) files each instance
  under its surface; `renderGeometry` (~L3794-3806) iterates the dictionary, one
  instanced call per surface.
- **One instanced draw** (`Nu/Nu/Nu/OpenGL/OpenGL.PhysicallyBased.fs`): all
  instances are blitted into one reused `renderer.InstanceFields` array, uploaded
  once, then `Gl.DrawElementsInstanced (…, surfacesCount)` (~L3799/L3945) with
  `Hl.ReportDrawCall surfacesCount`. Draw calls ≈ unique surfaces, not objects.
- **Per-instance culling inside the batch** (~L3130-3138) reduces *instances*,
  never call count.
- **Amortisation**: the grouping is baked once at freeze; `RenderTasks.clear`
  (~L1108-1146) retains the dictionaries; `shadowUpToDate` (~L1148-1205) skips
  rebuilds when nothing changed; `InstanceFields` is grow-only. `BatchPhase`
  (Starting/Resuming/Stopping) hoists shader/VAO/state binds across the run.
- **Draw-call instrumentation**: `Nu/Nu/Nu/OpenGL/OpenGL.Hl.fs`
  (`ReportDrawCall` / `GetDrawCallCount` / `GetDrawInstanceCount`).

Mapping to fsharp-game:

| fsharp-game (now) | Nu |
|---|---|
| 1 instanced draw per unique submesh identity (521 → 191) | ~1 instanced draw per unique surface |
| grouping rebuilt per frame (v1) | baked once at freeze for static hierarchies |
| per-instance data in one vertex buffer; one shared `viewProj` push | per-instance data in one buffer |
| `Engine.fs` logs `draws`/`instances` | `Hl.ReportDrawCall` etc. |
| frustum cull per part (tried, reverted) | per-instance cull inside the batch |

Differences that matter: Nu renders through OpenGL, this project through
SDL_GPU/Vulkan (whose per-draw CPU cost is the §6 root cause), and Nu defers the
opaque bulk into a G-buffer while this project is forward per-submesh. Neither
changes the conclusion: collapse the draw calls. Nu's *freeze* concept is worth
copying only as the optional §7 step 3 cache; the v1 plan is a per-frame gather.

**Caveat:** this is a source-read of `Repos/Nu`, not a measured run — treat the
"in budget" claim as inference from the design, not a benchmark.

## 13. Asset upload batching (spec 052)

Loading an environment or prop used to submit **one command buffer per vertex/index buffer and
per texture**. Measured on ArtRoom: **469 submissions**, of which ~820 ms was pure submission
overhead (~0.67 s across 382 geometry-buffer uploads + ~0.55 s across 87 texture uploads), while
the geometry itself is only ~1.84 MB.

Spec 052 batches one asset's uploads. `Gpu.UploadBatch` packs every buffer/texture copy into a
staging transfer buffer and **flushes** it (one command buffer: one copy pass, mips outside the
pass, one submit) when it fills. The first buffer is sized to the workload (up to 4 MiB), growing
to `StagingCapacity` (256 MiB) only for a large asset. So **peak staging memory is bounded** (one
buffer, not the asset's size) and the submission count is a small handful, not one per resource.

Measured on ArtRoom (`FFS_LOAD_TIMING=1`, 2026-10-03):

| | Baseline | After |
|---|---|---|
| submissions | 469 | **7** |
| submit overhead | ~820 ms | **3.7 ms** (0.07% of the load) |
| peak staging | ~one resource | **≤ 256 MiB** (one buffer, 4 MiB for a small asset) |
| aggregate upload work | ~1.0 s | ~0.30 s |

Island: 1 submission, 11.9 ms. Props: 2 submissions each.

`uploadBufferOnce` (actor meshes, storage buffers) keeps a direct, data-sized one-shot upload, so
a small one-off buffer does not allocate a 256 MiB staging buffer. Texture decode (~4.3 s for
ArtRoom, ~89% of the load) is unchanged and out of scope — see `docs/ENVIRONMENT_LRU_PLAN.md`
and the async follow-on.

The per-asset instrumentation is opt-in (`FFS_LOAD_TIMING=1`) and reports
`parse | buf-create | tex-create | stage | memcpy | submit | decode`.
