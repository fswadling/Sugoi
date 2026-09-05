# Research: Animated Character on Screen

Decisions made while implementing phase 3. Retrospective — these are the
rationale behind the working-tree implementation.

## Decision 1 — Skin on the GPU, not the CPU

- **Decision**: the CPU samples per-joint skinning matrices (`Actor.samplePose`);
  the per-vertex blend runs in the vertex shader. The vertex buffer holds
  bind-pose position/normal/uv plus per-vertex blend indices/weights (uploaded
  once, static). Each frame the CPU only uploads the per-joint skinning matrices
  as a `mat4 bones[]` vertex storage buffer; the shader blends
  `Σ weight · bones[id] · position`.
- **Rationale**: this is what Nu does (a bone palette plus a 4-influence blend in
  the vertex shader). It removes the per-frame CPU vertex loop and the ~640 KB
  vertex re-upload; per-frame CPU cost drops to sampling 166 bones + one
  ~10.4 KiB storage-buffer upload.
- **Alternatives considered**: CPU skinning (the original phase-3 approach, which
  measured ~50 fps on Akane's 20,005 vertices). Rejected — it scales poorly with
  actor count and does avoidable per-frame CPU work.

## Decision 2 — Mesh vertex layout is position + normal + UV + blend data

- **Decision**: the GPU vertex buffer is `float3 position, float3 normal,
  float2 uv, ubyte4 boneIds, ubyte4Norm weights` (40-byte stride). The index
  buffer is `uint32` triangles.
- **Rationale**: IQM provides `BLENDINDEXES`/`BLENDWEIGHTS` as ubyte4, which map
  directly onto SDL_gpu's `Ubyte4`/`Ubyte4Norm` vertex formats (no conversion).
  Normals feed the placeholder Lambert shader; UVs are carried for phase 4/5.
- **Alternatives considered**: storing blend indices/weights as float4 (simpler,
  but 32 bytes/vertex extra); position-only (rejected — no shading).

## Decision 3 — One placeholder shader, upgraded in phase 5

- **Decision**: a single graphics pipeline + one shader (vertex + fragment) does
  a fixed directional Lambert (`N·L`) diffuse with a uniform base color. No
  textures, no material parameters.
- **Rationale**: the roadmap names phase 3's shader as the thing phase 5 upgrades
  to metallic-roughness GGX. A minimal directional shader gives visible 3D shape
  now and a clean seam to upgrade later.
- **Alternatives considered**: unlit flat color (rejected — loses the depth/
  lighting cue that proves depth testing works); full PBR now (rejected —
  violates minimalism and the roadmap's phase ordering).

## Decision 4 — Port the IQM parser, plus per-pose channel offsets

- **Decision**: `core/Iqm.fs` is a straight port of `ocaml-game/lib/core/iqm.ml`
  (header, meshes, vertex arrays, triangles, joints, poses, animations, frame
  channels), reading little-endian fields at absolute offsets. Geometry is
  `float32` arrays; blend indices/weights are raw `byte` arrays. The `Model` also
  records `PoseChannelOffsets` — the running count of set bits in each pose's
  `ChannelMask`, giving the sampler its base offset into a frame's packed channel
  group. It parses **data only**; sampling lives in `world/Actor.fs`.
- **Rationale**: ocaml-game already scoped the parser exactly this way and emits
  flat struct-of-arrays that feed SDL_gpu buffers directly. Precomputing the
  per-pose channel offsets once at load keeps the per-frame sampler a straight
  array read.
- **Alternatives considered**: a streaming/sectioned loader (rejected — files are
  small and one-shot); the `iqm/` C++ reference via a native shim (rejected — no
  native dependency for a format this simple).

## Decision 5 — IQM vertex arrays are a closed, loud-failing set

- **Decision**: support `POSITION` (float3), `TEXCOORD` (float2), `NORMAL`
  (float3), `TANGENT` (float4), `BLENDINDEXES` (ubyte4), `BLENDWEIGHTS` (ubyte4).
  Any other kind (notably `COLOR`) or format raises with the kind/format value.
- **Rationale**: matches ocaml-game and the IQM spec's "portable use"
  recommendation; fails loudly per constitution II rather than silently dropping
  data.
- **Alternatives considered**: accepting COLOR and ignoring it (rejected —
  silently changing the asset's appearance).

## Decision 6 — Sampling in `world/Actor.fs`, skinning in the shader

- **Decision**: `world/Actor.fs` owns the animation *sampling*: advance the clock
  (`frame += dt * fps`, loop/clamp) and sample a pose (per-joint local transform
  from `channel_offset + raw * channel_scale`, slerp/lerp between frame `N` and
  `N+1`) into per-joint skinning matrices (`inverseBind · world`). The per-vertex
  blend runs in `mesh.vert`.
- **Rationale**: matches Nu's split — CPU computes bone matrices, the GPU does the
  weighted blend. `Actor` stays pure math (no GPU), and `ActorRender` stays upload
  + draw.
- **Alternatives considered**: CPU per-vertex skinning (rejected — the perf
  problem); a UBO bone palette (rejected — see Decision 13).

## Decision 7 — The manifest gains a `Model` field, flat layout kept

- **Decision**: extend the s-syntax manifest with an optional `Model` string
  (`Id`/`Name`/`Model`). The model path is relative to the `assets/` root. The
  existing flat layout is kept: `assets/actors/akane.manifest` beside
  `assets/actors/Akane.iqm`.
- **Rationale**: `Assets.scan` already reads `assets/<kind>/*.manifest` flat, so
  the minimal change is one field. `Model` is actor-only this phase;
  props/environments carry a `ModelPath` field but are metadata-only until phase 4.
- **Alternatives considered**: a subdirectory-per-actor layout (rejected — churns
  the working flat manifest convention); a separate `ActorAsset` manifest type
  (rejected — one manifest shape serves all kinds).

## Decision 8 — One shared asset per actor, per-instance renderer

- **Decision**: the parsed model + uploaded mesh are loaded once per asset id into
  a shared `ActorAsset`; each spawned actor owns a per-instance `ActorRender`
  (`AnimationSlot` + `PoseFrame` + bone palette). `Entity.Actor` holds that
  renderer inline, so render state lives in the actor slot map.
- **Rationale**: matches odin-game's `Actor_Asset`/`Actor_Renderer` split and
  avoids re-parsing/re-uploading a shared IQM per instance. `ActorAsset` is split
  out of `Assets` so `ActorRender` can depend on it without a cycle.
- **Alternatives considered**: parse per actor entity (rejected — redundant I/O
  and memory); storing only `AssetId` and resolving the renderer via a global map
  each frame (rejected — an extra indirection; the renderer is per-instance data).

## Decision 9 — A fixed `BonesMax = 205` for the bone palette

- **Decision**: `Gpu.BonesMax = 205` (mirrored as `BONES_MAX` in `mesh.vert`).
  A model with more than 205 joints fails loudly at load. The pose/skinning arrays
  are sized to the model's actual `num_joints`; only the uploaded storage buffer is
  fixed at `BonesMax × 64` bytes, zero-padded.
- **Rationale**: the `mat4 bones[]` storage buffer needs a known capacity;
  205 × 64 B = 13,120 B. It matches Nu's 205-bone limit.
- **Alternatives considered**: an SSBO sized to the model (more flexible, but a
  second buffer type for no phase-3 benefit); a larger cap (rejected — over the
  comfort range for the palette).

## Decision 10 — Morph targets are deferred

- **Decision**: `morphs.json`/`morphs.bin` parsing and application are out of scope
  this phase.
- **Rationale**: the roadmap's phase-3 result is *skeletal* animation; facial
  morphs are a separate sidecar subsystem not needed to show an animated
  character.
- **Alternatives considered**: port `actor.ml` now (rejected — dead code with no
  UI/cue to drive it).

## Decision 11 — Rendering hooks into `GameplayScreen.renderScene`

- **Decision**: `world/ActorRender.fs` is invoked from `screen/GameplayScreen.fs`.
  `update` advances each actor's animation clock; `renderScene` uploads each
  actor's bone palette in the copy-pass phase, then draws the debug grid and each
  mesh inside the same scene render pass.
- **Rationale**: the scene pass already exists and clears color + depth; the grid
  and gizmos must stay visible (FR-007), so meshes join the same pass rather than
  replacing it.
- **Alternatives considered**: a separate render pass (rejected — needless
  complexity); replacing the grid (rejected — violates FR-007).

## Decision 12 — Command payloads carry the actor `AssetId`

- **Decision**: `state/Command.fs` `Spawn`/`Delete`/`Duplicate` cases carry an
  `AssetId : string` (empty for props). `Zone.setActorWithAsset` re-creates an
  actor with its id and an eagerly-created renderer; `apply`/`undo` use it so
  undo/redo preserves the model binding.
- **Rationale**: a spawned actor loses its model if undo stores only the base
  `Entity`. Carrying the id on the three structural cases keeps `Transform`
  untouched and props (`AssetId = ""`) uniform.
- **Alternatives considered**: store the full specialized `Actor` in the command
  (rejected — a wider refactor of `Command`/`Zone` CRUD); ignore it (rejected —
  undo/redo would silently drop the model).

## Decision 13 — Bone matrices upload as a vertex storage buffer

- **Decision**: a vertex-stage storage buffer (`set = 0`, slot 0) holds the
  `mat4 bones[]` palette; the per-draw `PerDraw` block stays in uniform slot 0
  (`set = 1`). `ActorRender.upload` writes sampled skinning matrices into a reused
  `BoneBytes` array and copies them to a `GraphicsStorageRead` buffer via a copy
  pass; `draw` binds it.
- **Rationale**: SDL_gpu's Vulkan backend binds each *pushed uniform* slot with a
  4 KiB range (`MAX_UBO_SECTION_SIZE`). A 205 × 64 B = 13,120 B uniform would read
  zeros past bone index 63, collapsing those vertices to a point. A storage buffer
  has no such cap. Matrix layout follows the existing `DebugDraw` row-vector↔GLSL
  transpose convention.
- **Alternatives considered**: a pushed uniform `mat4 bones[205]` (rejected — the
  4 KiB cap); splitting the palette across several 4 KiB uniform slots (rejected —
  more complex than one storage buffer).

## Decision 14 — Split the flat clusters into `world/` and `state/`

- **Decision**: the phase-3 work landed alongside a structural refactor. The old
  `core/{Assets,Entity,Zone}.fs` became `world/{Assets,Entity,Zone}.fs`;
  `core/Actor.fs` and `render/ActorRender.fs` became `world/Actor.fs` and
  `world/ActorRender.fs`; `screen/{Screen,SharedState,GameplayState}.fs` and
  `editor/{Command,EditorState}.fs` became `state/*.fs`. Two new clusters result:
  `world/` (entity/world + actor runtime data) and `state/` (per-screen/shared +
  editor command state).
- **Rationale**: `Entity.Actor` must own an `ActorRender`, so the world model now
  depends on the renderer (`world/` must follow `render/` in compile order).
  `Assets.AssetManager` must own the GPU device and shared loaded actors, so it
  belongs above `render/` on `SharedState`, not in `core/`. `state/` holds the
  manager-adjacent per-screen/shared state. This keeps dependency direction
  strictly `core → render → world → state → editor/screen`.
- **Alternatives considered**: keep everything in the old flat clusters (rejected —
  would make `core/` depend on `render/`, inverting the architecture); a single
  `world/` cluster for both world and state (rejected — mixes shared/screen state
  with the entity model).

## Decision 15 — Actor assets load eagerly at startup

- **Decision**: `Assets.scan` loads every actor asset eagerly — parse IQM, build
  `ActorModel`, upload the static mesh — into `AssetManager.Actors`. Props and
  environments are scanned as metadata-only (`PropAsset`/`EnvironmentAsset`).
- **Rationale**: matches odin-game's `Asset_Manager` and keeps the per-frame path
  free of I/O and parsing. One character this phase makes eager loading cheap and
  simple; an LRU/async loader is a later phase (roadmap 15).
- **Alternatives considered**: lazy load on first spawn (rejected — more state for
  no phase-3 benefit); parse per spawn (rejected — redundant I/O).

## Decision 16 — `ActorRender`/`AnimationSlot` are structs advanced by `byref`

- **Decision**: `ActorRender` and `AnimationSlot` are `[<Struct>]`; per-frame
  animation advance mutates the slot's `Frame` through a `byref` (no copy-and-update
  of the whole `Actor`/`ActorRender` records).
- **Rationale**: the renderer lives inline in the actor slot map; rebuilding the
  records each frame would copy the `PoseFrame` arrays and the bone bytes
  reference, and allocates on the hot path. Mutating in place keeps the frame cost
  to a float write.
- **Alternatives considered**: immutable functional `advance : slot -> slot`
  (kept as a convenience variant for tests); a class-backed renderer (rejected —
  breaks the inline struct layout).

## Decision 17 — Clip resolution: `Idle` by name, first clip, then bind pose

- **Decision**: `ActorRender.create` resolves the played clip by looking up the
  name `"Idle"`; if absent it falls back to the model's first animation clip; if
  the model has no clips it creates a disabled slot so the actor renders in bind
  pose.
- **Rationale**: the phase only needs the `Idle` clip, but the IQM carries two
  clips and the manifest does not name clips (they come from the model). The
  fallbacks keep a malformed/missing `Idle` from crashing.
- **Alternatives considered**: manifest-declared clip name (rejected — clip names
  belong to the IQM, and the manifest already carries the model path); hard-fail on
  a missing `Idle` (rejected — too strict for a placeholder shader phase).

## Decision 18 — IQM is Z-up; convert to the engine's Y-up at draw

- **Decision**: `ActorRender.draw` folds a fixed up-axis conversion into the model
  matrix: `upAxisFix * model`, where `upAxisFix = Matrix4x4.CreateRotationX(-π/2)`.
  This maps IQM's `+Z` (the character's height axis) to the engine's `+Y` and
  `+Y` to `-Z`.
- **Rationale**: IQM is authored Z-up (Akane's bounds span ~1.64 units along Z and
  ~0.5 along X/Y), while the engine world/camera is Y-up (`Camera.Up = UnitY`).
  Without the conversion the model lies on its side with its head toward the
  camera. The conversion is applied after skinning (in the model matrix), not to
  the stored vertices, because the bone palette is sampled every frame in IQM's
  Z-up space — rotating only the static vertices would leave the Z-up bones
  deforming an already-rotated mesh. One `Matrix4x4` multiply per actor per frame
  matches odin-game's per-frame `translation * rotation * orientation_matrix`
  (`orientation_degrees.x = -90`).
- **Alternatives considered**: baking the rotation into the vertex positions/normals
  and the inverse-bind matrices at load (rejected — the animated world matrices are
  still Z-up, so the per-frame cost would move from one multiply per actor to
  per-joint, with more code surface); converting the quantized animation channels
  to Y-up at load (rejected — invasive and lossy for a constant rotation).
