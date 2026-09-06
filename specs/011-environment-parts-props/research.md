# Research: Environment Parts as Props

Decisions derived from `odin-game/src/{entity,assets,environment_hierarchy}.odin`
and the phase-4 `core/Gltf.fs`.

## Decision 1 — Local-space geometry + per-part world transforms (not baking)

- **Decision**: `Gltf` keeps each primitive's positions/normals in node-local
  space and emits a `Part` per mesh-bearing node carrying its accumulated world
  transform (`Matrix4x4`) plus a submesh range. Node transforms are no longer
  baked into world-space positions.
- **Rationale**: a baked mesh cannot be re-transformed by an absolute gizmo
  position without re-applying its original placement. Keeping geometry local and
  setting the prop's entity transform to the part's world transform makes each
  part genuinely movable (and selectable at the right position), matching odin's
  `spawn_environment_part_instance` → `create_prop_entity(part.local_transform)`.
- **Alternatives considered**: keep phase-4 baking and store part transforms
  alongside (rejected — double transform / broken gizmo); odin's bake +
  `mesh_origin_inverse` compensation (rejected — that exists to undo raylib's own
  `rl.LoadModel` baking, which our hand-rolled parser does not do).

## Decision 2 — Store the part world transform as a `Matrix4x4`; decompose at spawn

- **Decision**: `Gltf.Part.Transform` is a `System.Numerics.Matrix4x4` (the
  accumulated `world` matrix already computed in the node walk). `Zone` decomposes
  it into `Entity.Transform` (`Position`/`Rotation`/`Scale`) via
  `Matrix4x4.Decompose` when spawning the prop.
- **Rationale**: `Gltf` is in `core/` and cannot reference `Entity.Transform`
  (compile order); a `Matrix4x4` is the exact accumulated result and Decompose
  recovers `Scale * Rotation * Translation` (System.Numerics' row-vector order).
  The accumulated world matrix is composed as `local * parentWorld` (row-vector
  `v * M`, so the node's transform applies before its ancestors') — the phase-4
  `parentWorld * local` order was wrong for multi-node hierarchies and scattered
  `ArtRoom`'s furniture.
- **Alternatives considered**: store Euler degrees + scale and compose like odin's
  `transform_combine` (rejected — reimplements composition approximately when
  `Matrix4x4` already does it exactly); store a `Quaternion` + scale + translation
  tuple in `Gltf` (rejected — no gain over `Matrix4x4`).

## Decision 3 — Submeshes are deduplicated per mesh; parts reference ranges

- **Decision**: with 32-bit indices there is no >64K u16 split, so one glTF
  primitive maps to exactly one submesh. Submeshes are emitted in **mesh-index
  order** (each unique mesh read once), so a mesh referenced by several nodes is
  uploaded once and shared by every part that references it; a part's
  `SubmeshCount` is its mesh's primitive count (usually 1, but 2 for a bark+leaves
  mesh) and `SubmeshIndex` is that mesh's range.
- **Rationale**: preserves the phase-4 decision that the engine uses 32-bit
  indices, removes the need for odin's `gltf_submesh_count_table`, and avoids
  duplicating shared geometry when one mesh is instanced by many nodes (common in
  `ArtRoom`, e.g. `door02_door1`).
- **Alternatives considered**: one submesh per node (rejected — loses per-material
  base color for multi-material meshes and duplicates instanced meshes);
  DFS-order ranges recorded during the node walk (rejected — re-reads shared
  meshes once per referencing node).

## Decision 4 — The environment renders via its parts; the entity is a container

- **Decision**: `Zone.loadEnvironment` spawns one prop per part (`AssetId = None`,
  submesh range, part transform) and the environment entity itself draws nothing.
  The environment keeps `AssetId` + `Orientation` so the orientation can be
  composed into every part's draw; props live on `Zone.Props` and the environment
  is optional (`Zone.Environment : Environment option`).
- **Rationale**: this is exactly odin's `render_full_model = false` path. It makes
  "imported furniture" a set of first-class props instead of one opaque mesh.
- **Alternatives considered**: keep a `render_full_model` flag and support both
  whole-model and parts rendering (rejected — two render paths for no phase-011
  benefit; odin always uses parts-as-props for environments).

## Decision 5 — A prop distinguishes a placed asset from an environment part by asset id

- **Decision**: a prop with `AssetId = Some id` is a placed prop asset (draws the
  whole model); a prop with `AssetId = None` is an environment part (draws a
  submesh range of the environment's shared model). `Prop` gains
  `SubmeshIndex`/`SubmeshCount` used only by parts (a placed asset uses
  `0`/`asset.Submeshes.Length`).
- **Rationale**: mirrors odin's `prop_asset_id` ("non-empty = placed prop asset,
  empty = environment part"). One `Prop` shape keeps the slot map, editor, and
  command flow uniform.
- **Alternatives considered**: a separate `EnvironmentPart` entity kind (rejected —
  a new kind would need new `EntityRef`/selection/undo branches for a value that is
  behaviourally a prop).

## Decision 6 — One `EnvironmentRender` with a submesh range, not two renderers

- **Decision**: `EnvironmentRender` gains `SubmeshIndex`/`SubmeshCount` and
  `draw` iterates only that range; `OrientationMatrix` is set per instance (identity
  for placed props, the environment orientation for parts). The renderer still
  references the shared `EnvironmentAsset`.
- **Rationale**: both a placed prop and an imported part are "draw a contiguous
  range of a shared static asset with a transform"; the only differences are the
  range and the orientation, so they fold into two fields rather than a second
  renderer type.
- **Alternatives considered**: a `StaticSubmesh`-range draw helper separate from
  `EnvironmentRender` (rejected — duplicated draw/push logic); a second
  `PartRender` type (rejected — more types for two fields).

## Decision 7 — Non-uniform scale normals stay approximate

- **Decision**: the static shader's `mat3(model) * aNormal` (then normalise) is
  retained. For the new local-space parts this is exact under uniform scale and
  approximate under non-uniform scale.
- **Rationale**: this is the same approximation the phase-4 path already had
  (baked scale, unit normals); a correct inverse-transpose normal matrix belongs
  with the phase-5 PBR/shader work, not here.
- **Alternatives considered**: add an inverse-transpose normal matrix now
  (rejected — scope creep; phase 5 owns the shader upgrade).

## Decision 8 — Re-spawning an environment replaces parts, preserves placed props

- **Decision**: `loadEnvironment` drops the previous environment's parts (props
  with `AssetId = None`) and keeps placed props (`Some` `AssetId`), then
  spawns the new environment's parts. `clearEnvironment` removes the environment
  (parts removed, placed props kept).
- **Rationale**: an environment swap must not leave the previous environment's
  furniture in the zone, but placed props are first-class entities that live in
  the same props slot map and must survive a swap (spec FR-006/FR-008). Using
  `AssetId = None` as the part marker makes the split trivial.
- **Alternatives considered**: wiping the whole props map like phase 4 (rejected —
  destroys placed props and breaks their undo stack); diffing old/new parts
  (rejected — no stable part identity across different assets).

## Decision 9 — Loading an environment is an undoable command

- **Decision**: `state/Command.fs` gains `SetEnvironment (PreviousAssetId,
  NewAssetId)`; the Asset Browser pushes it after calling `loadEnvironment`/
  `clearEnvironment`. Undo re-runs the previous id (or clears when `None`).
- **Rationale**: loading an environment now spawns many props, so it must be a
  first-class, undoable edit rather than a bare zone-level mutation (phase 4's
  `Decision 11` deferred this; phase 11 makes it necessary). Because parts are
  spawned deterministically from the asset, re-running `loadEnvironment` restores
  the previous environment's parts on undo.
- **Alternatives considered**: a full props-map snapshot for undo (rejected —
  copying the slot map is heavy and re-spawning is deterministic); leaving it
  non-undoable (rejected — inconsistent with the prop command flow).
