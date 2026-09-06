# Feature Specification: Environment Parts as Props

**Feature Branch**: `011-environment-parts-props`

**Created**: 2026-09-05

**Status**: Draft

**Input**: User description: "importing an environment adds its furniture (desks, chairs,
windows, etc.) as individual props, matching odin-game's parts-as-props behaviour"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Importing an environment spawns its parts as props (Priority: P1)

A developer spawns a multi-node environment (e.g. `ArtRoom`) and sees each
mesh-bearing node in its glTF file become a separate, selectable prop in the
zone's props list — desks, chairs, windows, doors, blackboards, etc. — positioned
by that node's transform in the file. The environment itself is a container
(its orientation and shared model), not a single solid mesh.

**Why this priority**: it is the headline behaviour this feature exists for, and
the structural change the rest of the feature hangs on. Until the environment is
decomposed into parts, there is no "import props" behaviour at all.

**Independent Test**: import `ArtRoom`; the outliner shows many props (not one
environment mesh), each at its authored position, and no geometry is baked into
the environment entity itself.

**Acceptance Scenarios**:

1. **Given** an environment glTF whose scene contains multiple nodes with meshes,
   **When** the environment is spawned, **Then** the zone's props slot map contains
   one prop per mesh-bearing node, named from the node (or its hierarchy path).
2. **Given** a mesh-bearing node with a non-identity transform, **When** the
   environment is spawned, **Then** that part renders at the node's world
   transform (position, rotation, and scale).
3. **Given** a node with no mesh (a transform-only group), **When** the
   environment is spawned, **Then** no prop is created for it, but its transform
   is still applied to its descendants' props.
4. **Given** a mesh with multiple primitives (e.g. bark + leaves), **When** the
   environment is spawned, **Then** the corresponding prop draws all of that
   mesh's submeshes as one prop.

---

### User Story 2 - Imported parts are independently editable (Priority: P2)

An imported part behaves like any other prop: it renders with correct depth,
can be selected, moved/rotated/scaled with the gizmo, and is undoable/redoable.
Moving one desk does not move the rest of the room.

**Why this priority**: importing props is only useful if they are first-class
entities the editor can manipulate. This proves the part's transform is truly
per-entity (not baked into shared geometry) and wired into the existing
selection/gizmo/command flow.

**Independent Test**: import `ArtRoom`, select one desk, move it; only that desk
moves, and undo restores it.

**Acceptance Scenarios**:

1. **Given** an imported part, **When** it is selected, **Then** the selection
   marker and gizmo appear at the part's position.
2. **Given** an imported part, **When** it is moved with the gizmo, **Then** only
   that part's mesh moves; other parts stay put.
3. **Given** an imported part, **When** it is moved and then undone, **Then** it
   returns to its original authored transform.
4. **Given** an imported part, **When** the camera orbits, **Then** the part
   depth-sorts correctly against the environment's other parts and any actors.

---

### User Story 3 - Composition with placed props and actors (Priority: P3)

A zone with an imported environment, manually placed prop assets (`beech`/`oak`),
and the animated actor renders all three together with correct depth ordering, and
the existing overlays (grid, markers, gizmos) keep working. Placed prop assets are
unchanged by the environment-part mechanism.

**Why this priority**: it is the integration slice proving the parts-as-props
change composes with the phase-3 actor and the phase-4 placed-prop path without
regressing either.

**Independent Test**: import an environment, spawn a `beech` prop, and spawn Akane;
all render together, the `beech` prop is still a single entity, and grid/gizmos
remain visible.

**Acceptance Scenarios**:

1. **Given** an environment with imported parts, a placed prop, and an actor,
   **When** the viewport renders, **Then** all three are visible with correct
   depth ordering.
2. **Given** a placed prop asset, **When** the environment-part mechanism is
   active, **Then** the placed prop still renders as one entity at its transform.
3. **Given** an imported environment, **When** it is re-spawned (or replaced with
   another environment), **Then** the previous environment's parts are removed and
   the new environment's parts are spawned.

---

### Edge Cases

- Environment glTF with no nodes, no meshes, or an empty scene.
- A node with a mesh but zero primitives.
- A node whose mesh has multiple primitives (submesh range spans them).
- A mesh referenced by multiple nodes (instancing) — its geometry is uploaded once
  and shared by every part that references it.
- Transform-only parent nodes (no prop, but their transform affects children).
- Non-uniform node scale (normals are approximate; see Assumptions).
- A node with a `matrix` transform (currently unsupported — must fail loudly).
- Re-spawning/replacing an environment removes the old parts and keeps placed
  props; undoing an environment spawn restores the previous environment (or none).
- Imported parts must not collide with placed props in the slot map (both live in
  the same `Zone.Props` map).
- A part name that collides with another part (names are display-only; no key).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The glTF parser MUST emit submesh geometry in *local* space and, in
  addition, a **part** per mesh-bearing node (name, hierarchy path, world
  transform, and a contiguous submesh range), rather than baking node transforms
  into world-space positions.
- **FR-002**: An environment asset MUST carry its parts alongside its shared
  submeshes; the environment entity MUST be a container (orientation + shared
  model) that draws no geometry directly.
- **FR-003**: Spawning an environment MUST create one prop in the zone's props
  slot map per part, with the part's world transform as the prop's entity
  transform and the part's submesh range as the prop's draw range.
- **FR-004**: A prop MUST distinguish a placed prop asset (`AssetId = Some id`,
  draws the whole model) from an environment part (`AssetId = None`, draws a
  submesh range of the environment's shared model).
- **FR-005**: The environment's authored orientation MUST apply to every imported
  part when it is drawn.
- **FR-006**: Placed prop assets MUST continue to render as single entities with
  their entity transform; the environment-part change MUST NOT alter their
  behaviour.
- **FR-007**: Imported parts MUST be selectable, transformable, and undoable
  through the existing `EntityRef`/`Zone`/`Command` flow.
- **FR-008**: Re-spawning or replacing an environment MUST remove the previous
  environment's parts (props with `AssetId = None`) before spawning the new ones,
  and MUST preserve placed props (`AssetId = Some id`).
- **FR-009**: The system MUST fail loudly on a missing, malformed, or unsupported
  glTF or manifest (including `matrix` nodes, sparse accessors, skins/animations,
  and non-triangle primitives).
- **FR-010**: Spawning or replacing an environment MUST be an undoable command
  that restores the previous environment (or an empty environment) on undo.

### Key Entities *(include if feature involves data)*

- **Part** (parsed): one glTF node with a mesh — `Name`, `HierarchyPath`, a world
  `Transform`, and a `SubmeshIndex`/`SubmeshCount` into the shared model.
- **Environment asset**: a manifest entry plus its loaded runtime data — the
  shared static submeshes and the `Parts` list. Mirrors odin-game's
  `Environment_Asset.parts`.
- **Environment entity**: the zone's single world container — base `Entity`, its
  asset reference, and orientation. Renders nothing directly; it is optional
  (`Zone.Environment : Environment option`) and its imported parts live in the
  zone's `Props` slot map.
- **Prop entity**: a placed prop — either a prop asset (`AssetId = Some id`,
  whole model) or an environment part (`AssetId = None`, submesh range of the
  environment's shared model).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Importing `ArtRoom` spawns one prop per mesh-bearing node (many
  props), not a single environment mesh.
- **SC-002**: Each imported part renders at its authored world transform and is
  independently movable; moving one part does not move any other part.
- **SC-003**: A placed `beech` prop and the Akane actor still render correctly in
  the same viewport alongside imported parts.
- **SC-004**: Re-spawning an environment replaces the previous parts (no stale
  props left behind) and keeps placed props.
- **SC-005**: Every missing/malformed/unsupported glTF or manifest input fails
  loudly (no silent empty scene).
- **SC-006**: Spawning an environment is undoable: undo restores the previous
  environment (or clears it), and redo re-applies it.

## Assumptions

- **Local-space geometry + per-part transforms.** glTF submeshes are kept in node
  local space; each part carries its accumulated world transform, which becomes
  the prop's entity transform at spawn. This replaces the phase-4 "bake to world
  space" behaviour so parts are genuinely movable (a baked mesh cannot be
  re-transformed by an absolute gizmo position without double-applying its
  original placement).
- **One submesh per primitive, deduplicated by mesh.** With 32-bit indices there
  is no >64K u16 split, so one primitive maps to one submesh (a mesh's primitive
  count is its submesh count). A mesh referenced by multiple nodes is uploaded once
  and shared by every part that references it, so parts reference mesh-index-ordered
  ranges rather than re-reading shared geometry.
- **Prop assets are single-root with identity transforms.** `beech`/`oak`/`oak2`
  each have one root node with no transform, so a placed prop renders its whole
  model under its entity transform unchanged. Multi-root prop assets are out of
  scope this phase.
- **Non-uniform node scale approximates normals.** The static shader transforms
  normals by `mat3(model)` (normalised), which is exact for uniform scale and
  approximate for non-uniform scale; the proper inverse-transpose normal matrix
  is deferred to the phase-5 PBR/shader upgrade.
- **Environment orientation is identity for current assets.** The phase-4
  `OrientationDegrees` manifest field was dropped; `EnvironmentAsset.Orientation`
  remains `Quaternion.Identity`, but the parts renderer still composes it so a
  future orientation just works.
- **Collision/BVH, textures/PBR, and persistence remain deferred** (phases 6, 5,
  and 15 respectively). The prop entity gains no collision data this phase.
