# Shadow Mapping — notes for two parked attempts

**Status**: **parked.** A sun-shadow feature has been built and reverted **twice**; each time the tree went
back to the post-MSAA (042) state and the spec directory was deleted. This file is the record, so a retry
does not rediscover what the attempts cost.

| | spec | outcome |
|---|---|---|
| Attempt 1 | 043 — **that number is now the zone light settings** | specified → implemented → reverted after testing. **Cast nothing at all** (trap 1). |
| Attempt 2 | 044 | specified → implemented → **worked** → reverted for the look. |

- Attempt 2's entire tree — implementation, shaders, tests and its `specs/044-sun-shadows/` artefacts
  (spec, plan, tasks, research, data-model, contracts, quickstart) — is preserved locally in
  `git stash@{0}`; `git stash list` names it. Nothing else points at it.
- The reference is still present: `odin-game/src/shadow.odin`, `odin-game/docs/SHADOWS_PLAN.md`,
  `odin-game/assets/shaders/shadow/{shadow.vert,shadow.frag}`, and its receivers
  `odin-game/assets/shaders/{environment/env.frag,actor/actor.frag}`.

## What attempt 2 got to

It **rendered correctly**, which is the important part for a retry:

- a zone's sun casting real shadows from the room's own geometry and its props, onto the floor, the walls
  and each other, moving with the sun's pitch/yaw in the same frame;
- actors **receiving** (the cel band dims, floored so the two-tone never dies) and casting **nothing**;
- a per-zone on/off toggle in the Inspector's lighting section;
- the map cached on (zone, sun pitch, sun yaw, static revision);
- 341 tests green (331 baseline + 10 new).

**It was reverted anyway** — the user judged the shadows "not adding much at the moment". The reason is the
edge problem in "The look" below: with a fixed tap kernel you cannot be both crisp (the cel look) and free
of the map's texel staircase, and a low sun through the ArtRoom's windows is the worst case for it.

## The design that works

The reference's technique, ported. Every convention below was wrong at least once.

- **The map.** A fixed 2048² `D32Float` texture, `DepthStencilTarget ||| Sampler`, **single-sample** (the
  scene pass stays 4×), sampled through a **nearest, clamp-to-edge** sampler. Fixed-size and created with
  the device, so it is not part of the resize path.
- **The caster pass.** A depth-only render pass — **0 colour targets** — begun with an empty colour span and
  the map as the depth target, `StoreOp.Store`, clear depth 1. It draws the **same instance buffer** the
  scene pass draws, from the sun's orthographic view (the light's view-projection is pushed into the
  vertex-stage `viewProj` slot `static.vert` already uses). One transform source, so a caster and its
  shadow cannot disagree.
- **The fit.** `WorldBounds` = the union, over every gathered instance, of
  `transformBounds world sub.GeometryBounds`; then
  `eye = centre + dir·2r`, `up = +Y` (swapped to `+Z` when `|dir.Y| > 0.99`), radius clamped at `1e-3`,
  `view * proj` with `CreateOrthographic(2r, 2r, 0.1, 4r)`. `r` is the **half-diagonal** of that box, which
  is enough to cover any projection of it.
- **The receiver.** `p = lightSpace * vec4(worldPos, 1)`; then

  ```glsl
  vec2 uv = vec2(p.x * 0.5 + 0.5, 0.5 - p.y * 0.5);   // y MIRRORED — see trap 2
  if (p.z > 1.0 || any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) return 0.0; // lit
  float bias = max(slopeBias * (1.0 - NdotL), depthBias);
  ```

  a box PCF over `uv ± texel·k`, and `shadowed / taps`. Constants, the reference's own:
  `depthBias = 0.001`, `slopeBias = 0.005`, `strength = 1.0`, cel visibility floor `0.2`.
- **Who is shaded.** Static: the **direct** term only, `direct *= 1 - shadow`; ambient untouched, so a
  shadowed surface is ambient-lit rather than black. Cel: `visibility = max(1 - shadow*strength, 0.2)`
  multiplies the lit band. The actor's **`Face` mesh takes no cast shadow at all** (visibility `1.0`) — a
  deliberate departure from the reference, below.
- **The cache.** Key = (zone **name**, sun pitch, sun yaw, `Zone.StaticRevision`); the pass runs only when
  the key changes. The key lives on `SharedState`, **not** `Gpu`, because `Gpu` is compiled before
  `render/Shadow.fs` and could not name `Shadow.Key`. `Zone.StaticRevision` is bumped by the seven *static*
  funnels only — actors never bump it, which is what makes the cache effective.

## Traps, in the order they cost time

1. **The asset `Bounds` are LOCAL-space.** `Gltf.Model.Bounds` → `EnvironmentAsset.Bounds` is computed over
   the submeshes' *untransformed* positions, and the node transforms (including the glTF root's scale) are
   applied only at draw time. Fitting the frustum to them gives a ~1000-unit box centred far from the room
   → **no shadows at all** (attempt 1's whole failure). Fit to the **casters' world extent** instead. The
   per-submesh box is `submeshRangeBounds model.Submeshes i 1`, transformed per instance.
2. **SDL_gpu's NDC `+y` is up, but its texture `v` is down — a receiver must MIRROR y.** This was attempt
   2's last bug and the most expensive one. `SDL_gpu.h` §"Coordinate System" is explicit: NDC's lower-left
   is `(-1, -1)` and its upper-right is `(1, 1)`; texture coordinates' **top**-left is `(0, 0)`. The
   reference is raylib/OpenGL, where *both* point up, so its `p.xy * 0.5 + 0.5` is right there and wrong
   here:

   ```glsl
   vec2 uv = vec2(p.x * 0.5 + 0.5, 0.5 - p.y * 0.5);
   ```

   The header's "SDL will automatically convert the coordinate system behind the scenes" covers *drawing*,
   not reading back a texture a previous pass wrote. The port already agrees with both halves and proves the
   convention without a GPU: **glTF UVs are top-left origin** (per spec) and sample correctly, and the
   **UI's ortho** maps pixel `y = 0` to NDC `+1`. Sampling y unmirrored reads the map upside-down and paints
   light-dependent blobs that sweep with the sun instead of the room's shadows.
3. **Row-vector convention.** .NET composes `view * proj` (as `Camera.viewProjection` does); `writeMatrix`
   plus GLSL `M * v` reproduces `v * M`. A light matrix written `proj * view` is silently wrong.
4. **The port's projection maps z to `[0,1]`**, unlike OpenGL's `[-1,1]`, so a receiver must **not** remap
   z. (`.NET CreateOrthographic` maps view-space `z ∈ [-far, -near]` → `[0,1]` under the right-handed
   `CreateLookAt` — verified; it is *not* the `[-1,1]` a GL mental model expects.) The reference's
   `proj.xyz * 0.5 + 0.5` is wrong here.
5. **A pipeline's declared fragment uniform/sampler counts must match its shader's.** `cel.frag` grew to
   2 uniform blocks and 2 samplers, and the pipeline still declared `1u 1u`, so
   `PushGPUFragmentUniformData(cmd, 1u, …)` **aliased slot 0** and overwrote the cel uniforms — the actor
   rendered solid black. Same class of bug as any "my push went nowhere" report.
6. **A depth-only pass has 0 colour targets — and SDL_gpu defaults the viewport and scissor for it.**
   `SDL.BeginGPURenderPass` with an empty colour span is fine. `VULKAN_BeginRenderPass` sizes the default
   viewport **and** scissor from the smallest attachment, which for a depth-only pass is the depth target,
   and `VULKAN_INTERNAL_SetCurrentViewport` flips y (`y = h, height = -h`) to match D3D/Metal. So setting
   the viewport explicitly is belt-and-braces, and **do not chase a leftover scissor** for a caster pass
   that renders nothing — it is not the cause.
7. **A narrow PCF kernel reads as blocks on a grazed surface** — see "The look".
8. **`open Screen` shadows `Option.None`** with `ScreenTransition.None`, so a bare `None` in
   `GameplayScreen` silently resolves to the wrong type. Qualify it (`Option.None`, `Option.bind`).
9. **Whichever actor decision you take, it drives the cache.** Attempt 1 had actors cast, so any moving
   actor re-rendered the map every frame and the skip only helped a *static* editor. Attempt 2 ships actors
   as receivers only, which is what makes the cache worth having (`test/ZoneTests.fs` asserts that no actor
   mutation bumps `Zone.StaticRevision`).
10. **A function call cannot be addressed** in F# (for `&byref` arguments) — bind it to a `let mutable`
    first. And `Gpu` is a `[<Struct>]`, so reassign it (`shared.Gpu <- …`), never mutate a copy.

## The look: why it was reverted

Both attempts found the same wall, and it is worth understanding before retrying.

- A map texel covers **`1/sin(angle the surface makes with the sun)`** of the receiving surface. On a
  surface the sun *grazes* — the floor under a low sun, which is exactly the case for light through the
  ArtRoom's windows — one texel is a large square. A receiver's PCF output is then constant over each of
  those squares, and with a handful of taps it takes a handful of brightness levels: the user saw a
  **"clear boxy step in brightness"**, i.e. the map's texel grid drawn as a staircase.
- Widening the kernel fixes the blockiness: radius 3 texels (49 taps) smooths the staircase into a ramp.
  But the user then asked for the shadows to be **sharper** — and a fixed box kernel has no middle ground:
  narrow is blocky, wide is soft, and soft fights a cel look that wants crisp shapes.
- **Nearest sampling is not incidental**: depth formats generally do not support linear filtering in
  Vulkan, so this cannot be fixed by turning on bilinear on the map. The two ways out are (a) filter the
  depth by hand (4 taps per sample) so a narrow kernel is still continuous, or (b) **PCSS /
  contact-hardening** — size the kernel from the blocker's distance, so an edge is crisp at contact and
  softens with distance, which is what "light through a window" actually looks like. (b) is the principled
  fix and the first thing to try next time; both attempts' specs listed it as out of scope.
- **This zone is the worst case for the problem**, so it is a harsh test of any filter: a big room, a large
  radius fit (~24 world units across 2048 texels ≈ 1.2 cm/texel), a low sun, and a lot of grazing floor.

Judgement at the time: for a stylised game, the payoff did not justify shipping until the edge quality is
solved. Any retry should start from the filter, not from the plumbing — the plumbing above works.

## Other things attempt 2 learned

- **The sun had to become authorable first.** Attempt 1 ended on "*which way must the sun face to come
  through the ArtRoom's windows*", which a compile-time constant could not answer. Spec 043 (a per-zone
  light, edited in the Inspector) is what made attempt 2 possible; even then the direction had to be
  nudged by hand before anything was visible.
- **The reference dims a cast shadow across a face; the user does not want that.** `actor.frag` keeps only
  the face's band *direction* idealised and lets scene occlusion dim it. The port exempts the `Face` mesh
  from cast shadows entirely, on the grounds that a shadow crossing a face is the same artefact the
  idealised face light (spec 040) exists to avoid. Worth keeping.
- **Cost.** 49 taps per receiver pixel (radius 3) is real fragment work; radius 2 (25 taps) or radius 1
  (9 taps) are the cheaper dials. The port is CPU-bound on draw submission (`docs/RENDER_PERFORMANCE.md`),
  so the GPU absorbed it, but measure before shipping a wide kernel.

## Code inventory, so a retry can rebuild it quickly

Attempt 2's shape (all of it in `git stash@{0}`):

- `render/Shadow.fs` — **new, pure**: `transformBounds`, `unionBounds` (both over `Gltf.Bounds`),
  `lightMatrix`, `[<Struct>] Key { ZoneName; Pitch; Yaw; Revision }`, `key`, `shouldRender`,
  `[<Struct>] Binding { Map; Sampler; LightSpace; Enabled }`, `writeUniform`, `bindMap`, and the constants.
  Compiled after `render/Texture.fs`.
- `render/Gpu.fs` — `ShadowMapSize`, the map + sampler, `ShadowPipeline`, a private
  `createStaticPipelineFrom (fragmentPath) (fragmentUniformBuffers) (fragmentSamplers) (colorFormat option)
  (depthFormat) (sampleCount)` with `createStaticPipeline` and `createShadowPipeline` as its two wirings,
  plus `shadowTargetInfo` and `shadowViewport`.
- `shaders/shadow.frag` (empty body) + `.spv`; a `shadowFactor` block added to `static.frag` (uniform
  `set = 3, binding = 1`; map `set = 2, binding = 4`) and `cel.frag` (map `set = 2, binding = 1`).
  `static.vert` is reused **unchanged** by the caster pipeline.
- `world/assets/EnvironmentAsset.fs` — `StaticSubmesh.GeometryBounds`. **Not** named `Bounds`: that label already
  exists on two other records in the module and made record literals ambiguous (FS0656).
- `world/assets/Assets.fs` — fills `GeometryBounds` per submesh.
- `world/render/EnvironmentBatch.fs` — `mutable WorldBounds : Gltf.Bounds option` accumulated in `fillInstance`;
  `renderDepth` (binds the pipeline, pushes the light view-projection at vertex slot 0, one draw per group);
  `render`/`drawInstanced` take the `Shadow.Binding`.
- `render/StaticMesh.fs` / `render/Mesh.fs` — an 80-byte shadow block pushed at fragment slot 1 and the map
  bound (slot 4 / slot 1), plus a depth-only draw path.
- `world/Zone.fs` — `ShadowsEnabled` (default `true`), `StaticRevision`, `touchStatic`, wired into the
  **seven** static funnels: `setMeshTransform`, `removeMeshEntity`, `spawnPropBinding`, `occupyProp`,
  `occupyPart`, `attachPart`, `setEnvironment` (a spawn does *not* route through `occupyProp` — hence seven).
- `state/SharedState.fs` — `mutable ShadowKey : Shadow.Key option`.
- `screen/GameplayScreen.fs` — derives `light`, `lightMatrix`, `shadowKey`; runs the depth-only pass before
  the scene pass; passes the binding to the static and actor draws.
- `editor/Inspector.fs` — the "Shadows" checkbox (a direct write, not an undo step).
- `test/ShadowTests.fs` (10 cases: the fit, the bounds transform, the key, and the real ArtRoom's world
  extent) + the revision invariants in `test/ZoneTests.fs`.
- `docs/RENDERING_NOTES.md` §16 documents the pass.

## ArtRoom facts (needed to place the sun)

- The glTF **root node** has scale `0.01` and a **+90° rotation about X** (Blender Z-up → Y-up), so the raw
  glTF coordinates are ~100× the world size. `EnvironmentAsset.Orientation` is identity — the root node
  carries the conversion.
- World extents: `x ∈ [-11, 11]`, `y ∈ [0, 4]`, `z ∈ [-4, 4]` (art room `x ∈ [-11, 5]`, storage
  `x ∈ [5, 11]`).
- The `window01*` panes sit at `z ≈ ±4.07` with sills at `y ≈ 0.9`; the corridor/door wall
  (`door02_window`, `corridor_wall_*`) is on the opposite side. **Which `Z` sign gives light through the
  windows was the open question attempt 1 ended on** — try `+Z` then `−Z` first.
- The loaded ArtRoom is ~463 parts / ~521 submeshes; `RENDER_PERFORMANCE.md` covers why that is the frame's
  CPU cost.

## Test-suite note

The GPU cannot be opened from the test project, so only the pure half is unit-tested — the pass, the map and
the receivers need the run walkthrough. Four helpers construct `EnvironmentAsset.StaticSubmesh`
(`test/{Physics,Zone,Spawning,GameplayState}Tests.fs`) and three construct a `SharedState`
(`GameplayStateTests`, `SelectionTests`, `SpawningTests`); any new field on either must be added there too.
The baseline suite is **331 tests** with the feature reverted.

## Related, and shipped

The MSAA work (spec 042) needed the multisampled colour target's **store op** =
`SDL.GPUStoreOp.Resolve`. Setting `ResolveTexture` alone does nothing and the frame is black. That gotcha is
already recorded in `docs/RENDERING_NOTES.md` §15 and committed in 042.
