# Feature Specification: Environment + Props

**Feature Branch**: `010-environment-props`

**Created**: 2026-09-05

**Status**: Draft

**Input**: User description: "sketch out roadmap phase 4 — a static environment + prop
assets you can see, using odin-game as the reference"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A static environment renders in the viewport (Priority: P1)

A developer spawns an environment asset (e.g. `island`) and sees its static
geometry in the 3D viewport, positioned by the environment's orientation from its
manifest. The geometry comes from the asset's glTF file; it is drawn through the
phase-3 mesh infrastructure but as *static* (unskinned) geometry, since an
environment has no skeleton.

**Why this priority**: it is the phase's headline result and the hard part — the
project has no glTF loader, and SDL_gpu has no model loader. Until an environment
model is on screen, neither props nor any later content-authoring phase has a
world to sit in. This slice delivers the glTF parser plus the static mesh-draw
path (a static variant of the phase-3 skinned pipeline) and de-risks phase 5's
PBR upgrade of that path.

**Independent Test**: spawn the `island` environment; the viewport shows its
static meshes (not a wireframe box), correctly projected and depth-sorted under
the editor camera, at the orientation declared in its manifest.

**Acceptance Scenarios**:

1. **Given** the `island` asset has a valid glTF model referenced by its
   manifest, **When** the developer spawns that environment, **Then** its
   geometry is visible in the viewport.
2. **Given** a spawned environment, **When** the developer orbits/zooms the
   camera, **Then** the geometry is correctly projected and depth-sorted (no
   self draw-order artifacts).
3. **Given** an environment manifest that declares an `OrientationDegrees`
   (Euler rotation), **When** it is spawned, **Then** the model is rendered with
   that orientation applied.
4. **Given** the environment's glTF file is missing or malformed, **When** it is
   loaded, **Then** the failure is reported loudly (no silent empty mesh).

---

### User Story 2 - Prop assets spawn and render (Priority: P2)

A developer spawns a prop asset (e.g. `beech`) from the asset browser and sees it
render as an individually-positioned static mesh inside the zone's `props` slot
map, following its entity transform. Multiple props of the same asset share one
loaded model, differing only in transform.

**Why this priority**: props are the second half of the phase's "environment +
props" result and the first *multiple-instance* static-rendering test — several
props sharing one GPU mesh, each drawn with its own transform. It exercises the
prop entity shape (`Prop_Entity` with an asset id + renderer) that later phases
hang collision (6), story primitives (10), and persistence (15) on.

**Independent Test**: spawn two `beech` props at different positions; the
viewport shows two copies of the same mesh, each at its own transform, both
correctly depth-sorted against the environment and each other.

**Acceptance Scenarios**:

1. **Given** a prop asset with a valid glTF model, **When** the developer spawns
   it, **Then** its mesh renders in the viewport at the spawned transform.
2. **Given** a spawned prop, **When** the developer moves/rotates/scales it,
   **Then** the mesh follows the entity transform under the editor camera.
3. **Given** two spawned props of the same asset, **When** they render, **Then**
   they share the same GPU mesh/model and differ only in per-instance transform.
4. **Given** a prop asset whose glTF is missing or malformed, **When** it is
   loaded, **Then** the failure is reported loudly.

---

### User Story 3 - Environment, props, and actors render together (Priority: P3)

In a single viewport, a spawned environment, several props, and the animated
actor from phase 3 all draw in one scene pass with correct depth ordering and no
draw-order artifacts, and the existing editor overlays (reference grid, selection
markers, transform gizmos) continue to work — including selecting/transforming a
prop.

**Why this priority**: it is the integration slice that makes the phase a usable
editor result rather than three disjoint renders. It proves the static path
composes with the skinned path and the debug-draw overlays, and that props are
fully wired into the existing selection/gizmo flow (which already addressed
props via the slot map in phase 2c).

**Independent Test**: spawn an environment + a prop + the Akane actor, then
select and move the prop; all three render together, depth-sort correctly, the
prop's gizmo moves it, and the grid remains visible.

**Acceptance Scenarios**:

1. **Given** an environment, a prop, and an actor in one zone, **When** the
   viewport renders, **Then** all three are visible together with correct depth
   ordering.
2. **Given** a spawned prop, **When** it is selected and moved with the gizmo,
   **Then** the mesh follows the transform in the same viewport as the rest of
   the scene.
3. **Given** the phase-3 reference grid and gizmos, **When** the static geometry
   renders, **Then** the overlays still draw in the same viewport (no regressions
   from adding a second pipeline).

---

### Edge Cases

- glTF with an external `.bin` buffer that is missing or unreadable.
- glTF with embedded data-URI buffers (unsupported — must fail loudly).
- glTF with sparse accessors (unsupported — must fail loudly).
- glTF with skins or animations (unsupported for static environments/props — must
  fail loudly, not silently ignore).
- A primitive with a missing `POSITION` attribute.
- A vertex attribute of an unsupported kind (e.g. `COLOR_0`) or non-`float32`
  format.
- Indices of an unsupported component type; `u8`/`u16`/`u32` indices must all be
  widened to the engine's 32-bit index format.
- A primitive that is not triangle-mode (`mode != 4`).
- A node with no mesh, or an empty scene (no meshes).
- An environment or prop asset whose manifest `Model` path is empty or missing.
- Multiple prop instances sharing one asset — per-instance transform must not
  cross-contaminate.
- A prop asset id that no longer resolves to a loaded model.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST parse a scoped subset of glTF 2.0 for static
  environments/props: external `.bin` buffers, buffer views, accessors,
  `float32` `POSITION`/`NORMAL`/`TEXCOORD_0`, `u8`/`u16`/`u32` triangle indices,
  materials (per-submesh `baseColorFactor`), and node transforms baked to world
  space.
- **FR-002**: The parser MUST emit flat struct-of-arrays data (`float32[]`
  positions/normals/uvs, `int[]` indices, per-submesh material indices and base
  colors) that feeds GPU buffers directly, mirroring `core/Iqm.fs`.
- **FR-003**: The system MUST render static (unskinned) geometry through a static
  mesh path — vertex/index buffers + a static pipeline/shader — reusing the
  phase-3 `render/Gpu.fs`/`render/Mesh.fs` buffer/upload machinery without the
  skinning cost.
- **FR-004**: An environment entity MUST be associated with its environment asset
  via the asset manifest, render its meshes, and apply the manifest's declared
  orientation.
- **FR-005**: A prop entity MUST be associated with its prop asset via the asset
  manifest, render its mesh, and follow its entity transform.
- **FR-006**: The zone's environment MUST own a `props` slot map of prop entities
  (already present from phase 2a); multiple props of one asset MUST share one
  loaded GPU mesh/model and differ only in per-instance transform.
- **FR-007**: The system MUST fail loudly (exception/error, not a silent default)
  on a missing, malformed, or unsupported glTF or manifest.
- **FR-008**: Environment, prop, and actor rendering MUST compose in one scene
  pass with correct depth ordering, and the existing debug grid/gizmos MUST
  continue to render alongside them.
- **FR-009**: The editor MUST be able to spawn an environment and props from the
  asset browser, and to select/transform/remove props through the existing
  `Zone` entity-dispatch helpers.

### Key Entities *(include if feature involves data)*

- **glTF model** (parsed): meshes → primitives (positions/normals/uvs, indices,
  material index), materials (base color factor), and baked world-space node
  transforms — the static analogue of the IQM model.
- **Environment asset**: a manifest entry (id, display name, model path,
  orientation) plus its loaded runtime data — the parsed glTF model and the
  uploaded GPU mesh(es). Mirrors odin-game's `Environment_Asset`.
- **Prop asset**: a manifest entry (id, display name, model path) plus its
  loaded runtime data — the parsed glTF model and the uploaded GPU mesh. Mirrors
  odin-game's `Prop_Asset`.
- **Environment entity**: the zone's single world container — base `Entity`,
  the `props` slot map, its asset reference/renderer, and the orientation from
  its asset. Mirrors odin-game's `Environment_Entity`.
- **Prop entity**: a placed prop — base `Entity`, its prop asset reference, and
  its per-instance renderer/transform. Mirrors odin-game's `Prop_Entity`
  (`prop_asset_id`).
- **Static mesh**: the GPU vertex/index buffers (position/normal/uv) for one
  submesh, ready to draw through the static pipeline.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The `island` environment is visible in the viewport as a static 3D
  mesh, not a wireframe box.
- **SC-002**: At least two props of the same asset render simultaneously at
  distinct transforms, sharing one loaded model.
- **SC-003**: Every missing/malformed/unsupported glTF or manifest input fails
  loudly with a clear message (no crash-free silent failures).
- **SC-004**: The reference grid, entity markers, and transform gizmos still
  render correctly in the same viewport alongside the static geometry.
- **SC-005**: The animated actor from phase 3 still renders correctly in the
  same viewport (no regression from adding the static path).

## Assumptions

- **Static, untextured geometry this phase.** Environments/props render with a
  placeholder flat/Lambert shader using each submesh's `baseColorFactor` (default
  gray). Textures, metallic-roughness (PBR), and normal/emissive maps arrive in
  phase 5; the glTF parser may record material indices and base color now but
  must not need texture upload.
- **No collision/BVH this phase.** The odin-game `Prop_Collision_Data`/BVH and
  `environment.odin` collision helpers are phase 6 (physics + picking). The prop
  entity does not carry collision data yet.
- **No environment hierarchy/parts this phase.** The glTF node tree is *baked to
  world space* and the whole model renders as one environment entity; the
  odin-game `Environment_Part` concept (named mesh ranges spawnable as props via
  `environment_hierarchy.odin`) is out of scope. `mesh_index`/`mesh_count` and
  `mesh_origin_inverse` on the prop entity are deferred until then.
- **No waypoints/triggers/zones this phase.** `Waypoint`/`Trigger`/`Zone_Map`
  land in phases 8/10; the `Zone` container is unchanged apart from the
  environment/prop asset wiring.
- **glTF is Y-up, right-handed**, matching the engine's world/camera. Unlike the
  Z-up IQM actor, no axis fix is required; the environment's authored rotation is
  applied from the manifest's `OrientationDegrees`.
- **32-bit indices throughout.** The existing `render/Mesh.fs` path uses 32-bit
  indices, so the glTF parser widens `u8`/`u16` indices to `int` and does not
  need odin-game's >64K-vertex u16 sub-mesh splitting.
- **A static pipeline is added rather than reused verbatim.** The phase-3
  pipeline is skinned (position/normal/uv/blend + bone storage buffer); static
  geometry uses a separate position/normal/uv pipeline + shader so environments
  and props do not pay skinning cost.
- **The environment/prop manifests gain a `Model` field.** Today they are
  `[[Id ...] [Name ...]]` metadata-only; phase 4 adds `Model` (path to the glTF)
  and, for environments, `OrientationDegrees`. The actual glTF/bin files are
  copied from odin-game's `assets/environments`/`assets/props`.
- **Editor-only consumer.** Environments/props render in the gameplay/editor
  viewport under the existing orbit camera; play-mode navigation and camera
  follow arrive in phase 7.
