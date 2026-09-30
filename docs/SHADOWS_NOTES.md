# Shadow Mapping — notes for a future attempt

**Status**: **parked.** A shadow-mapping feature was specified, planned and implemented (spec 043), then
**reverted** at the user's request after testing, and its **spec directory deleted** — the tree is back to
the post-MSAA (042) state. This file is the only record, kept so a retry does not rediscover what it cost.

## The design that was specified (the spec directory is now deleted)

- **A single-cascade sun shadow map**: a 2048² depth map rendered from the sun's orthographic view; env +
  props + actors **cast and receive**; a **per-zone on/off toggle**; slope-scaled bias + 3×3 PCF; the
  direct term only, at full strength. 21 tasks, none done.
- Reference sources (still present): `odin-game/src/shadow.odin`, `odin-game/docs/SHADOWS_PLAN.md`,
  `odin-game/assets/shaders/shadow/{shadow.vert,shadow.frag}`, and its receivers `env.frag` / `actor.frag`.

## What was built (and then reverted)

- `render/Shadow.fs` — pure `lightMatrix`, the depth-only pass (`beginPass`/`endPass`), the dirty key
  (`shouldRender`/`markRendered`), the `ShadowView` and the fixed tuning.
- `render/Gpu.fs` — the `D32Float` shadow map with `DepthStencilTarget ||| Sampler` usage, a clamp sampler,
  and two depth-only caster pipelines (the instanced static path and the posed actor path with an
  alpha-testing fragment). The two shared builders gained `(msaa)` and `(colourTarget)`.
- `shaders/shadow.frag` (empty) and `shaders/shadow_alpha.frag` (cutout `discard`); `static.frag` and
  `cel.frag` gained the sampling block.
- `render/StaticMesh.fs` / `render/Mesh.fs` — bigger fragment uniforms (+ a light-space `mat4` and a
  params `vec4`) and a `drawDepth` caster path each.
- `world/Zone.fs` (`ShadowsEnabled`, `ShadowGeneration`) + `editor/Outliner.fs` (toggle);
  `state/Command.fs` and `world/Systems.fs` bump the generation.
- `world/EnvironmentAsset.fs` (`StaticSubmesh.GeometryBounds`) + `world/EnvironmentBatch.fs`
  (`WorldBounds`).
- `test/ShadowTests.fs` — 4 pure `lightMatrix` cases.

## Traps that cost time (fix these first next time)

1. **The asset `Bounds` are LOCAL-space.** `Gltf.Model.Bounds` → `EnvironmentAsset.Bounds` is computed over
   the submeshes' *untransformed* positions (`Gltf.fs`: "bounds over all local-space positions"), and the
   node transforms (including the glTF root's scale) are applied only at draw time. Fitting the light
   frustum to them gives a ~1000-unit box centred far from the room → **no shadows at all**. Fit the
   frustum to the **casters' world-space bounds** instead (the batch's gathered instances plus the actors'
   world AABBs) — that is the `GeometryBounds` / `WorldBounds` / `transformBounds` machinery above.
2. **`open Screen` shadows `Option.None`** with `ScreenTransition.None`, so a bare `None` in
   `GameplayScreen` silently resolves to the wrong type. Qualify (`Option.None`, `Option.bind`) or avoid.
3. **Row-vector convention.** .NET composes `view * proj` (see `Camera.viewProjection`), and `writeMatrix`
   plus GLSL `M * v` reproduces `v * M`. A light matrix written `proj * view` is silently wrong.
4. **The port's projection maps z to `[0,1]`, not OpenGL's `[-1,1]`.** The reference's
   `proj.xyz * 0.5 + 0.5` is wrong here — remap only `xy`; `z` is already the `[0,1]` depth the map stores.
5. **A depth-only render pass has 0 colour targets.** `SDL.BeginGPURenderPass` with an empty colour span is
   fine; set the viewport to the map size explicitly.
6. **Actor casting changes the skip.** Because actors cast, any moving actor re-renders the map every frame
   — the FR-008 skip only helps a *static* editor. That is a consequence of the "actors cast" clarification.

## ArtRoom facts (needed to place the sun)

- The glTF **root node** has scale `0.01` and a **+90° rotation about X** (Blender Z-up → Y-up), so the raw
  glTF coordinates are ~100× the world size. `EnvironmentAsset.Orientation` is identity — the root node
  carries the conversion.
- World extents: `x ∈ [-11, 11]`, `y ∈ [0, 4]`, `z ∈ [-4, 4]` (art room `x ∈ [-11, 5]`, storage
  `x ∈ [5, 11]`).
- The `window01*` panes sit at `z ≈ ±4.07` with sills at `y ≈ 0.9`; the corridor/door wall
  (`door02_window`, `corridor_wall_*`) is on the opposite side. **Which `Z` sign gives light through the
  windows was the open question the reverted attempt ended on** — try `+Z` then `−Z` first.

## Test-suite note

Four helpers construct `EnvironmentAsset.StaticSubmesh` (`test/{Physics,Zone,Spawning,GameplayState}Tests.fs`);
any new field must be added there.

## Related, and shipped

The MSAA work (spec 042) needed the multisampled colour target's **store op** =
`SDL.GPUStoreOp.Resolve`. Setting `ResolveTexture` alone does nothing and the frame is black. That gotcha is
already recorded in `docs/RENDERING_NOTES.md` §15 and committed in 042.
