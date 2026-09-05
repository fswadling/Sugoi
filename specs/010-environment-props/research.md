# Research: Environment + Props

Decisions made while planning phase 4, derived from `odin-game/src/{entity,assets,
environment_gltf,environment_render}.odin` and `ocaml-game/lib/core/gltf.ml`.

## Decision 1 — A static (unskinned) mesh path, not a reuse of the skinned path

- **Decision**: add a separate static pipeline + shader and a `render/StaticMesh.fs`
  buffer/draw primitive (24-byte stride `float3 position, float3 normal, float2 uv`).
  It reuses `render/Gpu.fs`'s buffer/transfer/upload helpers and the same
  `PerDraw` uniform layout (viewProjection + model + lightDir + baseColor), but has
  no blend attributes and no bone storage buffer.
- **Rationale**: static geometry must not pay the skinning cost (two extra vertex
  attributes, a per-instance bone palette, and a storage-buffer bind per draw).
  The phase-3 `Mesh.fs` interleaves blend data at a fixed 40-byte stride and
  `Gpu.createMeshPipeline` hardcodes it, so reusing it verbatim would either waste
  bandwidth or require the pipeline to know about a skeleton that isn't there.
- **Alternatives considered**: reuse the skinned pipeline with a degenerate
  identity palette (rejected — wasted bandwidth and a pointless bone buffer);
  parameterise `Mesh`/`createMeshPipeline` for both layouts (rejected — two vertex
  layouts in one module/pipeline is more complex than two small, explicit paths).

## Decision 2 — Port a scoped glTF parser, not a general one

- **Decision**: `core/Gltf.fs` parses exactly what static environments/props need:
  external `.bin` buffers, buffer views, accessors, `float32`
  `POSITION`/`NORMAL`/`TEXCOORD_0`, `u8`/`u16`/`u32` triangle indices, per-primitive
  `baseColorFactor`, and node transforms baked to world space. Everything else
  raises.
- **Rationale**: mirrors `ocaml-game/lib/core/gltf.ml`'s "scoped, loud-failing"
  scope and `odin-game`'s `environment_gltf.odin` (which parses a custom subset via
  `cgltf`, never the full spec). A general loader is a large surface for zero
  phase-4 value.
- **Alternatives considered**: a full glTF loader (rejected — violates minimalism);
  a native `cgltf`/`tinygltf` shim (rejected — no native dependency for a format
  whose needed subset is small and already specified by ocaml-game).

## Decision 3 — `System.Text.Json` for the JSON, `BinaryReader` for `.bin`

- **Decision**: parse the `.gltf` JSON with `System.Text.Json` (`JsonDocument`);
  read the external `.bin` with `System.IO.BinaryReader` at absolute
  `bufferView`/`accessor` offsets (little-endian).
- **Rationale**: glTF is JSON, and `System.Text.Json` ships in the BCL (no new
  dependency). This keeps the s-syntax/Prime convention for *manifests* while using
  the idiomatic JSON reader for the *asset format itself*, matching how `Iqm.fs`
  already uses `BinaryReader` for binary data.
- **Alternatives considered**: Prime `scvalue` for glTF (rejected — glTF is JSON,
  not s-syntax); a hand-rolled JSON scanner (rejected — reinventing a BCL type).

## Decision 4 — Bake node transforms to world space; no hierarchy preserved

- **Decision**: the parser resolves the scene's root nodes and recursively
  multiplies each node's local `T·R·S` up its parent chain, baking every
  primitive's positions/normals into world space (normals by the rotation only).
- **Rationale**: matches `ocaml-game`'s `bake_node`/`bake_scene` and odin-game's
  `gltf_cpu_bake_mesh_transform`. A single static environment renders correctly
  with one entity transform + the manifest orientation; there is no need to keep
  per-node transforms alive.
- **Alternatives considered**: preserve the node tree and render each node with its
  own transform (rejected — that is the "environment parts" feature, deferred out
  of phase 4; see spec Assumptions).

## Decision 5 — Emit flat struct-of-arrays, 32-bit indices

- **Decision**: the parser emits `float32[]` positions/normals/uvs and `int[]`
  indices (u32), plus per-submesh material/base-color metadata. `u8`/`u16` index
  accessors are widened to `int`.
- **Rationale**: the existing `Mesh.fs` path already uses 32-bit indices
  (`IndexElementSize32Bit`), so there is no >64K-vertex sub-mesh splitting
  (that was a raylib/GL `UNSIGNED_SHORT` constraint odin-game had to work around).
  Flat arrays feed SDL_gpu buffers directly, exactly like `Iqm.fs`.
- **Alternatives considered**: keep indices at their source width (rejected — the
  draw call already assumes u32); split >64K sub-meshes (rejected — unnecessary).

## Decision 6 — Materials are `baseColorFactor` only; textures/PBR are phase 5

- **Decision**: the parser records, per primitive, the material's
  `baseColorFactor` (default `(1,1,1,1)`); the static shader is the phase-3
  Lambert diffuse with a per-submesh base color. No textures, no
  metallic/roughness, no normal/emissive maps are parsed or sampled.
- **Rationale**: the roadmap names phase 5 as the PBR upgrade of this path. A flat
  Lambert base color gives visible 3D shape now and a clean seam for phase 5 to
  replace the shader and extend the material record.
- **Alternatives considered**: parse metallic/roughness/texture refs now for a
  later phase (rejected — dead code); full PBR now (rejected — violates minimalism
  and the phase ordering).

## Decision 7 — glTF is Y-up; no axis fix, orientation from the manifest

- **Decision**: no `upAxisFix` conversion (unlike the Z-up IQM actor). The
  environment's authored rotation is applied from the manifest's
  `OrientationDegrees` (Euler), folded into the environment entity's model matrix
  at draw time.
- **Rationale**: glTF is right-handed Y-up, matching the engine's world/camera
  (`Camera.Up = UnitY`). The manifest orientation mirrors odin-game's
  `orientation_degrees`, which authors use to align a Blender-exported model to
  the engine's axes.
- **Alternatives considered**: ignore orientation (rejected — the `island` model
  would sit in the wrong orientation); a global hard-coded rotation (rejected — it
  is per-asset authored data, not a format constant).

## Decision 8 — One static runtime type for both environments and props

- **Decision**: `world/EnvironmentAsset.fs` defines one `EnvironmentAsset`
  (`Id`/`Name`/`ModelPath`/`Orientation`/`Submeshes : StaticMesh array`) used by
  both environments and props; a prop asset is an `EnvironmentAsset` with
  `Orientation = Quaternion.Identity`. The manager keeps separate id-keyed
  `Environments`/`Props` dictionaries for the two kinds.
- **Rationale**: both are "a list of static GPU submeshes" and differ only in
  whether the manifest carries an orientation. One type means one renderer
  (`EnvironmentRender`) and one draw path serve both, minimising surface area.
- **Alternatives considered**: distinct `PropAsset`/`EnvironmentAsset` runtime
  types (rejected — identical shape, duplicated render code); a generic `StaticModel`
  plus two wrappers (rejected — indirection for no phase-4 benefit).

## Decision 9 — Shared asset / per-instance renderer split (mirrors Actor)

- **Decision**: `EnvironmentAsset` is loaded once per asset id (parse glTF, upload
  static submesh buffers); each spawned environment/prop owns an inline
  `EnvironmentRender` (`{ Asset; OrientationMatrix }`) that references the shared
  asset. The renderer's `draw` iterates the asset's submeshes and issues one
  indexed draw each.
- **Rationale**: matches odin-game's `Environment_Asset`/`Environment_Renderer`
  and the phase-3 `ActorAsset`/`ActorRender` split; multiple props of one asset
  share the same GPU buffers and differ only in transform.
- **Alternatives considered**: parse/upload per entity (rejected — redundant I/O
  and GPU memory); a per-frame id → asset dictionary lookup (rejected — an extra
  indirection on the hot path; the renderer holds the shared asset inline).

## Decision 10 — The environment is a single zone field, not a slot map entry

- **Decision**: spawning an environment sets `zone.Environment` (unloading any
  existing environment first), mirroring odin-game's `spawn_environment_asset_instance`.
  The environment is *not* addressable by `EntityRef` and is not in the props slot
  map.
- **Rationale**: a zone has exactly one environment; `Zone.Environment` is already
  a single field (with its own props slot map). This matches the target model in
  `docs/ARCHITECTURE.md`.
- **Alternatives considered**: make the environment a slot-mapped entity like
  actors/props (rejected — contorts the data model for a fixed singleton);
  multiple environments per zone (rejected — out of scope; zones arrive in phase 8).

## Decision 11 — Environment spawn is a zone-level load, not undoable this phase

- **Decision**: setting the environment is treated like loading the zone's world
  and is not routed through `state/Command.fs`/undo. Props, by contrast, carry an
  `AssetId` on `Spawn`/`Delete`/`Duplicate` and are fully undoable.
- **Rationale**: the command model operates on `EntityRef` (slot-map handles);
  the environment has no such handle. Making a singleton swap undoable would
  require a new command shape and storing the previous environment's asset id +
  orientation for a marginal phase-4 benefit; the roadmap's phase-4 result is
  "see the environment", not "edit it transactionally".
- **Alternatives considered**: an `Environment` command carrying the old/new asset
  id (rejected — scope creep; deferred to the zone/editor phase).

## Decision 12 — The environment is not selectable/gizmo-editable this phase

- **Decision**: the environment renders but is not selectable, draggable, or
  gizmo-transformed in phase 4; only props join the existing selection/gizmo flow
  (which already addresses props via the slot map).
- **Rationale**: environment transform/orientation is authored manifest data, not
  per-instance editor state, and the roadmap's phase-4 result is visual. Selecting
  an environment has no use until content authoring (zones/editor, phases 8/12).
- **Alternatives considered**: selecting the environment via a synthetic `EntityRef`
  (rejected — no slot-map index to point at; more machinery than phase 4 needs).

## Decision 13 — Assets load eagerly at startup (like actors)

- **Decision**: `Assets.scan` parses every environment/prop glTF and uploads its
  static submeshes eagerly, alongside the existing eager actor load. The manifest
  gains a `Model` field (and, for environments, `OrientationDegrees`).
- **Rationale**: keeps the per-frame path free of I/O and parsing, matching the
  phase-3 `loadActors` and odin-game's eager `Asset_Manager`. The LRU/async loader
  is a later phase (roadmap 15).
- **Alternatives considered**: lazy load on first spawn (rejected — more state for
  no phase-4 benefit); parse per spawn (rejected — redundant I/O).

## Decision 14 — A shared `PerDraw` uniform, reused without allocation

- **Decision**: `StaticMesh.draw` reuses the same 160-byte `PerDraw` block as
  `Mesh.draw` (viewProjection 64 + model 64 + lightDir 16 + baseColor 16) and
  pushes it to vertex-uniform slot 0; the base color is written per submesh from
  its parsed material. No per-frame allocation.
- **Rationale**: consistent with the phase-3 path and the data-oriented principle.
  The uniform bytes are pre-allocated on the `StaticMesh`; only the values change
  per draw.
- **Alternatives considered**: a separate per-material uniform block (rejected —
  phase 4 has a single shader and per-submesh base color is enough); recomputing
  the uniform buffer each frame (rejected — allocates on the hot path).

## Decision 15 — Fail loudly on unsupported glTF features

- **Decision**: the parser raises on: embedded/data-URI buffers, sparse accessors,
  `skins`/`animations`, a primitive with `mode != 4`, a missing `POSITION`
  attribute, a non-`float32` vertex attribute, an unsupported index component type,
  and a material/mesh index out of range. A manifest with an empty `Model` raises
  at scan time.
- **Rationale**: matches constitution II and the phase-3 IQM parser's behaviour —
  never silently drop or default a model's geometry. Assets in the wild that hit
  these paths should fail visibly so they are fixed at authoring time.
- **Alternatives considered**: skipping unsupported nodes/attributes with a
  warning (rejected — silently changes the asset's appearance).
