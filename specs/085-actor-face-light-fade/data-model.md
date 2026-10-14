# Phase 1 Data Model: Face Light Fade

**No data changes.** This feature adds no record, no field, no uniform member, no authored format
and no persisted state. It reads three values that already exist and derives one local scalar in the
shader. This file records them so the "no new data" claim (spec FR-006/FR-007) is checkable.

## Inputs the fade reads (all pre-existing)

| Value | Where it comes from | Meaning |
|---|---|---|
| `cel.uFace.xyz` | the idealized face direction, computed by `ActorRender.faceLightDir` and written into the cel uniform by `Mesh.draw` (`specs/040`) | the actor's horizontal facing (unit) plus a fixed `0.25` elevation |
| `cel.uFace.w` | same (`specs/040`) | `1` for a `Face` submesh, `0` otherwise — the gate for the fade |
| `cel.uLightDir.xyz` | the scene sun's direction, `Light.Direction` (`specs/043`) | points **toward** the sun (unit) |

## The cel fragment uniform block (unchanged)

The block is untouched — the fade adds no member. Its std140 layout, as declared in
`shaders/cel.frag`, remains:

| Offset (bytes) | Member | Contents |
|---|---|---|
| 0 | `vec4 uBaseColor` | rgb + alpha |
| 16 | `vec4 uLightDir` | xyz toward the scene sun |
| 32 | `vec4 uLightColor` | xyz |
| 48 | `vec4 uAmbient` | xyz |
| 64 | `vec4 uFace` | xyz = idealized face direction; w = isFace (1/0) |

Total 80 bytes; pushed once per submesh by `Mesh.draw` (unchanged).

## Derived value (shader-local, not stored)

| Value | Derived from | Definition |
|---|---|---|
| `faceH` | `cel.uFace.xz` | the facing's horizontal part, `vec3(uFace.x, 0, uFace.z)` |
| `sunH` | `cel.uLightDir.xz` | the sun's horizontal part, `vec3(uLightDir.x, 0, uLightDir.z)` |
| `toward` | `faceH`, `sunH` | `dot(faceH, sunH) / (|faceH| · |sunH|)`, or `1` when either length is ~0 |
| fade factor | `toward` | `smoothstep(-FACE_FADE_SOFTNESS, FACE_FADE_SOFTNESS, toward)`, in `[0, 1]`; `1` pointing toward the sun, `0` pointing away (a two-tone switch at the 90° crossing) |

These are fragment-local temporaries; nothing is stored, and no inter-stage or host value is added.

## Entities (unchanged; from the spec's Key Entities)

- **Face mesh** — the actor mesh named `Face` (`specs/040`); the only thing the fade applies to.
- **Actor facing** — the actor's horizontal heading, already derived for the idealized face light.
- **Scene light** — the existing `Light` (direction toward the sun, colour, ambient); no field added.
