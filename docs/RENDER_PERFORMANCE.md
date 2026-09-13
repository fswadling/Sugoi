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

### Sketch

- Build a **per-frame instance buffer** of model matrices: 521 × 64 B ≈ 33 KB.
- Issue **one instanced draw per unique submesh** → **521 draws collapse to 191**
  calls. Because each submesh has exactly one material, this groups materials
  for free (and makes the sampler dedup in §5b almost redundant).
- Move the model matrix out of the pushed vertex uniform and into a **vertex
  storage buffer** indexed by `gl_InstanceIndex` in `shaders/static.vert`.
  `viewProj` stays a pushed uniform (or moves into the same storage buffer).
- `SDL.DrawGPUIndexedPrimitives` already takes `instance_count`; pass the number
  of instances for that submesh.

### Files to touch

| File | Change |
|---|---|
| `shaders/static.vert` | Read `model` from a vertex storage buffer / instance attribute instead of the `PerDraw` uniform. |
| `render/StaticMesh.fs` | Add the instance-buffer usage + vertex binding/attribute layout; take an instance count on draw. |
| pipeline creation (static pipeline) | Add the instance vertex buffer's binding/attribute description. |
| `world/EnvironmentRender.fs` | Stop drawing its own submeshes; become a producer of instance data. |
| `screen/GameplayScreen.fs` | `renderScene`: gather visible instances per submesh, upload the instance buffer, issue one instanced draw per submesh. |
| frustum cull | Now reduces **instances**, not draw-call count. Keeps its value: fewer instances written/processed, but call count is already collapsed. |

### Notes / hazards

- The current design shares one `PerDrawUniformBytes` / `MaterialUniformBytes`
  array per *mesh*, which is a latent data hazard once multiple instances draw
  from the same mesh in one frame. Instancing removes it (per-instance data
  comes from the instance buffer, not a shared scratch array).
- Per-draw uniform pushes remain for `viewProj` + fragment material/light; these
  can be hoisted to once per submesh draw (191) instead of once per draw (521).
- Consider whether `Material` push can be batched too — 51 materials, one draw
  per submesh already means ≤ 191 pushes, may not be worth more.

### Cheap confirmation (optional, before the refactor)

Add a temporary "draw only every Nth instance" switch to show `record` scaling
**linearly with draw count** at constant geometry. The SDL issues in §6 are
already fairly conclusive, so only do this if certainty is wanted.

---

## 8. Secondary issue: missing mipmaps

`render/Texture.fs` sets `Sampler.MipmapMode <- Linear` but textures are created
with `NumLevels <- 1`, and `SDL3.Image.LoadGPUTexture` creates textures "with no
mip levels". Minification therefore samples the base level → aliasing plus a
texture-bandwidth cliff.

Fix: decode → `Surface` → create the texture with a full mip chain →
`SDL_GenerateMipmapsForGPUTexture` (SDL 3.2+; must be called outside any render
pass; requires >1 level).

**Lower priority than draw count** on current evidence. Revisit after instancing
lands.

---

## 9. Repository state (as of writing)

Committed:

- `c7b3167` *"Implement 016 walk around…"* — includes the Engine frame timing,
  the Jolt lifetime fix, `world/Systems.fs`, `test/PlayerTests.fs`,
  `specs/016-walk-around/`, `.zed/debug.json`. `dotnet build` clean (2 benign
  `Engine.fs` warnings from `NativePtr.toNativeInt (&&ev)`), `dotnet test` → 102
  passed.

Uncommitted:

```
?? docs/RENDER_PERFORMANCE.md          (this doc)
 M docs/RENDERING_NOTES.md            (perf results + pointer)
 M docs/ROADMAP.md                    (perf section revert)
 M specs/016-walk-around/tasks.md     (perf section revert)
 M Engine.fs                          (record/submit re-cut only)
```

Reverted this session (the perf attempts that had no effect — see §5):
`render/Camera.fs`, `render/StaticMesh.fs`, `world/EnvironmentRender.fs`,
`world/Assets.fs`, `screen/GameplayScreen.fs`, `test/CameraTests.fs` are back to
`c7b3167`, and the `draws`/`culled` counter args were dropped from the `Engine.fs`
log. The `record`/`submit` re-cut was kept (it is an independent measurement fix).

**Drift to fix when committing:**

- The `tasks.md` / `ROADMAP.md` edits are *reverts* removing the perf section
  from 016 — intended.
- `c7b3167`'s commit message still references "follow-up tasks T023–T025" which
  no longer exist (in 016). Consider an amend.

---

## 10. Next steps (in order)

1. Implement instancing (§7) and re-measure `record` via the per-phase log.
   Target < ~5 ms.
2. Fix the missing mipmaps (§8) and re-check quality/bandwidth.
3. Commit the docs and the `record`/`submit` re-cut (separate from 016); consider
   amending `c7b3167`'s stale T023–T025 reference.
4. If culling is reintroduced at instance level, re-add `draws`/`culled` counters.

---

## 11. Pitfalls / gotchas

- **Do not claim the JIT or Debug configuration is the cause.** Measured and
  disproved (§4).
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
- Don't run the game from here (no display) — ask for the log lines.
