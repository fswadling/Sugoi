# Phase 0 Research: Cel-Shaded Actors

The feature is a port of `odin-game`'s actor cel shader (its plan is `odin-game/docs/CEL_SHADING_PLAN.md`;
its shipped form is `odin-game/assets/shaders/actor/actor.frag`). These decisions settle the parts the
reference does not translate literally — this port's pipeline topology, uniform plumbing and player
conventions differ from the reference's raylib program — and record the scope the spec fixed: the
idealized face light is in, shadows are not.

## Decision 1 — Retarget the actor pipeline; do not add a second one

- **Decision**: the actor mesh pipeline *becomes* the cel pipeline. Replace `shaders/mesh.frag` with
  `shaders/cel.frag`, rename `Gpu.createMeshPipeline`/`Gpu.MeshPipeline` to
  `createCelPipeline`/`CelPipeline`, and reuse `shaders/mesh.vert` unchanged.
- **Rationale**: the skinned-mesh pipeline is **used by actors only** — environments and props draw
  through the separate instanced path (`shaders/static.vert`/`static.frag`, `render/StaticMesh.fs`,
  `world/EnvironmentBatch.fs`). Its fragment shader is therefore already actor-only, so adding a
  parallel "cel pipeline" beside the PBR one would leave a PBR actor shader that nothing draws
  (dead code — Minimalism I). The cel shader is also strictly simpler than the PBR one (no GGX, no
  tangent frame, no emissive), so this is a reduction. `mesh.vert` stays: it is the skinning + morph
  vertex stage (specs 023, 037) and the cel shader needs exactly its outputs (`vWorldNormal`, `vUV`).
- **Alternatives considered**: **add `cel.frag` + a second pipeline, keep `mesh.frag`** (FR-001 says
  "dedicated pipeline", but the existing one is already dedicated; this leaves an unused PBR actor
  shader and a second pipeline object for no gain); **keep the filename `mesh.frag`** (works, but a
  file named "mesh" that does cel shading misleads — the swap is the point of the feature, so name it
  after what it does).

## Decision 2 — A lean cel fragment uniform (5 × vec4 = 80 bytes)

- **Decision**: the cel fragment shader declares its own uniform block — `uBaseColor`, `uLightDir`,
  `uLightColor`, `uAmbient`, `uFace` (`xyz` = face light dir, `w` = isFace) — and `Mesh.draw` pushes
  80 bytes per submesh.
- **Rationale**: the PBR `MaterialUniform` carries normal/metallic-roughness/emissive factors, a
  `has*` flags vec4 and a camera position, none of which cel uses. Actor materials are built with
  albedo only (`Assets.loadActors` calls `Texture.makeMaterial … albedo None None None`), so normal,
  metallic-roughness and emissive maps are never present on an actor; the reference's cel shader
  ignores them too. Pushing the fields cel actually reads is smaller and honest.
- **Alternatives considered**: **keep the 128-byte `MaterialUniform` and add a 9th vec4** (less churn
  in `Mesh.draw`, but leaves three unused vec4s in the block and a `has*` semantics cel does not
  honour); **a second fragment uniform slot for the cel extras** (two pushes per submesh instead of
  one).

## Decision 3 — One sampler (albedo)

- **Decision**: `cel.frag` declares a single sampler (`uAlbedoMap`, `set = 2, binding = 0`) and the
  cel pipeline is created with `samplers = 1u`; `Mesh.draw` binds one sampler.
- **Rationale**: cel reads only the albedo map. The PBR shader declared four samplers and the mesh
  pipeline was created with four; declaring four and reading one risks the SPIR-V compiler stripping
  the unused ones and the reflection count then not matching `NumSamplers`. Declaring just the one
  the shader reads keeps the count exact. `Texture.Material.Bindings` is unchanged (still four
  entries in fixed order), and `Bindings.[0]` is the albedo binding, so binding one is `Bindings.[0]`.
- **Alternatives considered**: **declare four samplers, use one** (relies on reflection behaviour
  that is not guaranteed for unused bindings); **change `Material` to carry fewer bindings** (touches
  the environment/prop path, which still needs all four).

## Decision 4 — Ramp parameters and cutoff are shader constants

- **Decision**: `RAMP_THRESHOLD = 0.5`, `RAMP_SOFTNESS = 0.05`, `ALPHA_CUTOFF = 0.5` are `const`s in
  `cel.frag`, not uniforms.
- **Rationale**: FR-012 fixes them for this phase. The reference passes them as uniforms but never
  varies them; baking them removes three uniforms and three `Uniform1f` writes per program with no
  lost capability. When per-actor or per-zone authoring is wanted (the reference's later phase), they
  become uniforms again.
- **Alternatives considered**: **push them as uniforms** (unused configurability now); **put them in
  the cel uniform block** (same, plus it grows the block).

## Decision 5 — Face identification by IQM mesh name `Face`, carried as a submesh flag

- **Decision**: a submesh is a face mesh when its IQM mesh's authored name is `Face` (exact match).
  `Assets.loadActors` sets `Mesh.Submesh.IsFace = (mesh.Name = "Face")` while it builds the submeshes;
  `Mesh.draw` reads the flag per submesh.
- **Rationale**: this is exactly the reference's rule —
  `Actor_Renderer.face_meshes = iqm_mesh_indices(model_path, "Face")`, whose match is `name ==
  mesh_name` — and it is the **same Akane asset** here: `Akane.iqm` carries meshes named `Body`,
  `Face` and `Hair` (verified). The port's IQM parser already reads each mesh's name
  (`core/Iqm.fs`, `Mesh.Name`), so the same rule needs no new parsing. Setting it once at load keeps
  the draw path allocation-free (FR-011); re-reading the IQM at draw time would not.
- **Alternatives considered**: **re-read the IQM in the renderer** (per-frame file I/O — absurd);
  **an actor-manifest flag** (new authored format and authoring surface for a rule the asset already
  encodes in its mesh names); **case-insensitive / contains matching** (the reference is exact, and
  the asset's names are exact).

## Decision 6 — The idealized face-light direction, derived from the actor's model matrix

- **Decision**: for the `Face` meshes, the band direction is an idealized light in front of the actor:
  take the actor's horizontal facing — the entity model matrix applied to local `+Z`, its `Y` zeroed
  and normalized — and add a fixed elevation of `0.25`; the shader normalizes the result. This is a
  pure helper (`ActorRender.faceLightDir`), computed from the same `model` matrix `ActorRender.draw`
  already receives.
- **Rationale**: it is the reference's formula — `face_dir = {sin(yaw), 0.25, cos(yaw)}` — and the
  port's rotation convention already matches it: `Systems.forwardOf yaw = (sin yaw, 0, cos yaw)` and
  `Systems.yawOf q = atan2(Transform(UnitZ, q).X, .Z)`, i.e. **local `+Z` at yaw 0**, the same as the
  reference. Deriving it from the model matrix means no new field on the actor/entity and no extra
  argument; it is correct for any heading and for a scaled transform (the horizontal part is
  normalized before the elevation is added). Directions transform with the upper 3×3, so
  `Vector3.TransformNormal(Vector3.UnitZ, model)` is the facing (translation-independent). The
  elevation is added **after** flattening/normalizing, exactly as the reference adds a constant
  `yaw`-independent height.
- **Alternatives considered**: **pass the actor yaw explicitly** (more plumbing for the same value);
  **derive from the camera** (wrong — the reference's face light is fixed to the actor, not the
  camera; a camera-relative light would move the band as the player orbits); **use the vertex
  normal's own facing** (defeats the purpose — the point is a *fixed* band on the face).

## Decision 7 — Alpha test with the shared white placeholder (drop the `hasAlbedo` gate)

- **Decision**: `cel.frag` samples `uAlbedoMap` unconditionally and discards texels whose
  `albedo.a * uBaseColor.a` is below the cutoff.
- **Rationale**: absent maps are substituted with the shared opaque-white placeholder
  (`Texture.solid device 255 255 255 255`, FR-005's factor-only fallback), so a factor-only actor
  samples `(1,1,1,1)` → `albedo = baseColor` and never discards, while a hair/eye cutout texture does.
  This is behaviour-equivalent to the existing PBR shader's `hasAlbedo`-gated discard, with one fewer
  uniform. The reference's own discard is ungated in the same way (it substitutes `vec4(1.0)` when
  there is no map).
- **Alternatives considered**: **keep a `hasAlbedo` flag and gate the discard** (the PBR shader's
  shape, but redundant given the placeholder); **sample only when present** (same, plus a branch).

## Decision 8 — Scope: face light in, shadow receiving out (fixed by the spec)

- **Decision**: this phase implements the two bands, the additive colour, the idealized face light
  and the cutout — and **not** the reference's shadow-map receiving, outlines, rim, specular/matcap,
  ramp textures or per-actor parameters.
- **Rationale**: the spec's Assumptions fix this, and the user confirmed it during clarification:
  shadows are later scope (the roadmap sequences `render/Shadow.fs` *before* cel-shading), so there is
  no shadow map to receive from; the reference's own initial phase is exactly the two bands + ambient
  + sun. The later items are the reference's explicitly deferred work.
- **Alternatives considered**: **land shadows first** (the roadmap order, but the user chose to keep
  shadows as later scope and port the cel look now); **port the reference's shadow gating with a
  pass-through visibility of 1** (dead uniform plumbing with no shadow map behind it).

## Decision 9 — Test the two pure parts; the rest is the walkthrough

- **Decision**: add headless tests for (a) the face-mesh name rule (`"Face"` is a face mesh,
  anything else is not) and (b) the face-light direction helper (identity → level `+Z` with the
  elevation; a quarter turn about `Y` → level `+X` with the elevation; the vector is non-degenerate).
  The shading, the sun response and the cutouts are the interactive walkthrough.
- **Rationale**: GPU draws cannot run headless in this project (no device in the test host), which is
  why every prior rendering spec tests only the pure parts (e.g. 039's layout, 018's planner). The
  face rule and the face-light direction are the two things that can be asserted without a device and
  that carry the feature's non-obvious logic.
- **Alternatives considered**: **golden-image tests** (the host has no GPU; out of the project's
  testing model); **test nothing** (leaves the face rule and the face-light formula — the parts most
  likely to regress silently — uncovered).
