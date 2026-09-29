# Data Model: Cel-Shaded Actors

This feature adds almost no data. The one change is a boolean carried on the actor's submeshes; the
rest is a shader uniform and a pure function. The model's `Face` naming is *authored in the IQM asset*
and already read by the parser — this feature only surfaces it to the draw.

## The one change — `render/Mesh.fs`, `Submesh` gains `IsFace`

Today a submesh is geometry plus its material:

```fsharp
[<Struct>]
type Submesh =
    { FirstIndex : int
      IndexCount : int
      Material : Texture.Material }
```

After the feature it also records whether the mesh is a face mesh (a face mesh takes the idealized
front light, FR-007):

```fsharp
[<Struct>]
type Submesh =
    { FirstIndex : int
      IndexCount : int
      Material : Texture.Material
      IsFace : bool }       // the IQM mesh is named "Face" — lit by the idealized front light
```

It is set **once, at load**, from the parsed mesh name (`Assets.loadActors`), so the draw path reads a
struct field and never re-reads the model (FR-011). The fallback submesh (a model with no meshes)
sets it `false`.

The face rule is one pure predicate (in `render/Mesh.fs`, so it can be asserted headlessly and reused
by the loader):

```fsharp
/// The IQM mesh name that marks a face mesh — the reference's `iqm_mesh_indices(model_path, "Face")`,
/// whose match is exact.
let isFaceMeshName (name: string) : bool = name = "Face"
```

## The cel fragment uniform — `shaders/cel.frag`, 80 bytes

A lean block, one `std140` vec4 per line (FR-003, FR-004, FR-007). `Mesh.draw` writes it per submesh
into a reused byte array and pushes it at fragment uniform slot 0. Its field-by-field layout and
per-field sources are specified once in [contracts/cel-actor.md](./contracts/cel-actor.md) — the
contract is the single source of truth for the block.

The ramp parameters (`RAMP_THRESHOLD = 0.5`, `RAMP_SOFTNESS = 0.05`, `ALPHA_CUTOFF = 0.5`) are
shader constants, not data (research D4, FR-012).

The albedo is a single sampler (`set = 2, binding = 0`) bound to `Submesh.Material.Bindings.[0]`; the
other three material bindings are unused by cel (research D3).

## The idealized face light — a pure function, `world/ActorRender.fs`

The face light is not authored and not stored; it is derived from the actor's model matrix — the same
matrix `ActorRender.draw` already receives:

```fsharp
/// The idealized face light (FR-007): the actor's horizontal facing with a fixed elevation, matching
/// the reference's {sin yaw, 0.25, cos yaw}. The facing is the entity model matrix applied to local
/// +Z, flattened in Y and normalized (the project's own forward convention, `Systems.forwardOf`);
/// the elevation is added after, and the shader normalizes the result.
val faceLightDir : model:Matrix4x4 -> Vector3
```

- **Identity model** → `(0, 0.25, 1)` — level `+Z` with the elevation.
- **Quarter turn about `Y`** → `(±1, 0.25, 0)` — level `±X` with the elevation.
- The horizontal part is normalized **before** the elevation is added, so a scaled transform does not
  change the band's tilt (actors are unit-scaled today, but the formula does not depend on it).

`Systems.forwardOf`/`yawOf` (private) encode the identical convention, so this reproduces the
reference exactly without importing or widening them.

## Authored versus runtime boundary

| | Authored | Runtime |
|---|---|---|
| An IQM mesh's name (`"Face"`, `"Body"`, `"Hair"`) | yes — in the `.iqm` asset | no |
| `Submesh.IsFace` | no | yes — derived once at load from the mesh name; never persisted |
| The actor's face-light direction | no | yes — recomputed each frame from the transform |
| The cel constants (ramp threshold/softness, cutoff) | no | no — shader constants |

Nothing new is authored, and no authored format changes.

## Validation rules

- **A face mesh is a mesh named exactly `Face`** (FR-007). A model with no such mesh is ordinary:
  `IsFace` is `false` everywhere and the actor is lit by the scene sun throughout — no failure.
- **A model with several `Face` meshes** marks all of them; the reference keys off a set of indices,
  not one.
- **The face-light direction is well-formed**: a unit horizontal facing plus the elevation; a
  degenerate transform (zero horizontal) falls back to level `+Z` rather than producing a zero vector.
- **The face direction affects the band direction only** (FR-008): the same `uAmbient`/`uLightColor`
  colour the body, so a face still responds to the scene's light colour and ambient.
- **A non-face submesh uses the scene sun** (FR-009).
- **Cutouts are discarded by alpha test** (FR-006): the shared opaque-white placeholder means a
  factor-only material never discards, while a cutout texture does.
