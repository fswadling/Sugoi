# Data Model: Actor Outline

The feature adds one derived per-vertex value and three uniforms. Nothing is authored, and no authored
format changes.

## The vertex grows: 40 → 52 bytes (attribute location 5)

The actor vertex buffer is interleaved; the outline needs one more `Float3` — the **smoothed normal**
(research D3, D4). Both the cel and outline pipelines declare all six attributes over the same buffer.
The contract's [vertex layout](./contracts/outline.md) is the authoritative copy; this table mirrors it.

| Offset | Location | Attribute | Format | Before | After |
|---|---|---|---|---|---|
| 0 | 0 | position | `Float3` | ✓ | ✓ |
| 12 | 1 | normal | `Float3` | ✓ | ✓ |
| 24 | 2 | uv | `Float2` | ✓ | ✓ |
| 32 | 3 | boneIds | `Ubyte4` | ✓ | ✓ |
| 36 | 4 | boneWeights | `Ubyte4Norm` | ✓ | ✓ |
| 40 | 5 | **smoothedNormal** | `Float3` | — | **NEW** |

Interleaved stride: **40 → 52 bytes**. `Mesh.interleave` writes the new field; `Mesh.create` gains a
`smoothNormals` argument; the pipelines declare the sixth attribute (offsets are `Float3`-clean:
40 + 12 = 52).

## The derivation — `Iqm.smoothNormals` (new, pure)

```fsharp
/// The outline's extrusion directions (spec 041): each vertex's normal replaced by the average of the
/// normals of every vertex sharing its position, normalized. Welds by a rounded position so the hull
/// does not gap at hard edges (this asset is largely hard-edged). Pure, so it is asserted headlessly.
val smoothNormals : positions: float32 array -> normals: float32 array -> float32 array
```

- **Input**: the IQM's flat `positions` (`xyz` triplets) and `normals` (`xyz` triplets), one per
  vertex (the parser already produces both).
- **Weld**: bucket vertices by their position rounded to a tolerance (`1e-4`); coincident vertices
  share a bucket.
- **Average**: for a bucket, sum the member normals and normalize; every member vertex takes that
  direction. A single-member bucket takes its own (normalized) normal.
- **Degenerate**: if a bucket's summed normal is ~zero (opposing normals), fall back to that vertex's
  own normal, normalized — never emit a zero direction (a zero extrusion direction would collapse the
  hint on the GPU).
- **Output**: one normal per vertex, same order and length as the input.

This is the whole feature's only non-obvious arithmetic, which is why it is the one thing tested
headlessly (research D10).

## The outline vertex uniform — slot 2, 16 bytes

The outline's width is a fixed world-space shell (research D5): one constant world distance, so the
line shrinks with distance exactly like the actor.

| Offset | Field | Meaning | Source |
|---|---|---|---|
| 0 | `vec4 uOutline` | x = world-space width (**0 = no offset**), yzw unused | `ActorRender.outlineWidth` |

`Mesh.draw` (the cel surface) pushes this with `x = 0`; `Mesh.drawOutline` pushes the real width. The
surface is therefore inert to the extrusion while sharing the same vertex shader.

## The outline fragment interface — `shaders/outline.frag`

| Resource | Set / binding | Count |
|---|---|---|
| `sampler2D uAlbedoMap` | `set = 2, binding = 0` | 1 sampler |
| uniform buffers | — | **0** |
| storage buffers | — | 0 |

Constants (FR-009): `OUTLINE_COLOR` (a dark near-black), `ALPHA_CUTOFF = 0.5`. The shader samples the
albedo alpha and discards below the cutoff (FR-006), so hair/eye cutouts are not boxed.

## Authored versus runtime boundary

| | Authored | Runtime |
|---|---|---|
| IQM positions and normals | yes — in the `.iqm` asset | no |
| Smoothed (extrusion) normals | no | derived once at load from the positions/normals; never persisted |
| Outline colour, width, cutoff | no | constants (shader / port) |

Nothing new is authored, and no authored format changes.

## Validation rules

- **`smoothNormals` output length equals the vertex count**, and every output is a unit vector (or the
  input normal where the bucket degenerates) — a zero direction is never emitted.
- **Coincident vertices share one extrusion direction**, so the hull does not gap at a hard edge
  (FR-005, SC-003).
- **The surface pass always pushes width 0** and the outline pass a positive width (FR-008); a missing
  push would be undefined, so both passes push slot 2.
- **Alpha cutouts are masked**: the outline samples the same albedo as the surface, so a transparent
  texel draws no outline (FR-006, SC-004).
- **Only actors** run the outline pass; the static env/prop path never binds the outline pipeline
  (FR-002).
