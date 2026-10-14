# Implementation Plan: Face Light Fades as the Actor Turns Away from the Sun

**Branch**: `085-actor-face-light-fade` | **Date**: 2026-10-14 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/085-actor-face-light-fade/spec.md`

## Summary

Layer a brightness response onto `specs/040`'s idealized face light. The actor's `Face` mesh keeps
its fixed band *direction* (the band boundary stays put, as `specs/040` requires), but its lit band
now fades toward the shadow band as the actor turns its face away from the scene sun, and back to
the full front light as it turns toward it — so a face no longer reads as fully lit while the body
and scene around it are in shadow.

The key structural fact is that **this needs no new inputs and no new code paths**. The cel
fragment shader already receives both vectors the fade needs — the idealized face direction
(`cel.uFace.xyz`, from `specs/040` FR-007) and the scene sun's direction (`cel.uLightDir.xyz`, from
`specs/040` FR-004). The whole change is a few lines in the shader *body*: derive an azimuthal
`toward` factor from those two existing vectors and multiply it into the band. The 80-byte cel
uniform, the F# draw path (`render/Mesh.fs`, `world/ActorRender.fs`), `shaders/mesh.vert`, the
pipelines and the environment/prop path are all untouched.

## Technical Context

**Language/Version**: GLSL `#version 450` (compiled to SPIR-V with `glslc`). The host is F# on
.NET 10, but **no F# source changes** are needed.

**Primary Dependencies**: **none new.** `glslc` (already used to compile every committed `.spv`) for
the one recompiled fragment shader.

**Storage**: none. No authored format, no persisted state, no new uniform field.

**Testing**: the change is in the shader body, which needs a GPU and cannot run headless; the
existing suite (`test/CelTests.fs` for the pure face rules, and the rest) must pass unmodified
(FR-008, SC-004). The fade itself is validated by the interactive walkthrough in
[quickstart.md](./quickstart.md).

**Target Platform**: Linux x64 desktop, SDL3 + SDL_gpu.

**Project Type**: single-project native desktop game with a docked ImGui editor; the change is
confined to the actor fragment shading.

**Performance Goals**: hold 60 FPS. The added work is a handful of scalar ops per **face** fragment
only (two `vec3` constructions with `Y` zeroed, two `length`s, a `dot`, and a multiply), inside the
existing `isFace` branch. The non-face path is untouched, and no uniform, sampler, buffer or draw
call is added (FR-007).

**Constraints**: face meshes only (FR-005); the band *direction* stays idealized (FR-001); the fade
follows the sun's azimuth with the actor's facing aligned = bright (FR-002, clarifications); azimuth
only, with a degenerate-input fallback (FR-003); band colours and the additive composition unchanged
(FR-004); a fixed constant with no authoring surface (FR-006); no per-frame allocation (FR-007);
build clean and suite pass (FR-008).

**Scale/Scope**: one shader body edited (`shaders/cel.frag`), recompiled to `shaders/cel.frag.spv`,
one doc section updated (`docs/RENDERING_NOTES.md` §13). No F# changes, no new module, no
`.fsproj` change, no uniform/layout change, no pipeline change, no test change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — a few lines in an existing shader body. No new function, uniform, module, dependency, or parameter. The fade is derived from data already pushed. |
| II. Fail Loudly | Pass — no new failure path. The degenerate cases (a sun with no horizontal direction, a facing with no horizontal direction) are handled by the specified fallback to the full front light, which is the feature's defined behaviour, not a silent default swapped in for a bug. |
| III. Spec-First | Pass — `specs/085-actor-face-light-fade/spec.md` is written, clarified, and drives this plan. |
| IV. Data-Oriented Architecture | Pass — no data shape changes; the shader computes a local scalar from its existing uniform. |
| V. Minimise GC Pressure | Pass — nothing is added on the CPU side at all; the GPU work is per-fragment scalars. |
| N-V. Testing is the Authoring Tool | Not engaged — it governs the narrative subsystem, which is untouched. |

## Complexity Tracking

None. Nothing here is a constitution violation or a new dependency. The feature adds a few lines to
one shader and no code at all on the host side.

## Project Structure

### Documentation (this feature)

```text
specs/085-actor-face-light-fade/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── cel-face-fade.md # Phase 1 output — the shader contract change and what does not change
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
shaders/
├── cel.frag             # CHANGED: inside the body, an `if (isFace)` block derives the azimuthal
│                        #   `toward` factor from cel.uFace.xz and cel.uLightDir.xz and multiplies a
│                        #   small `smoothstep` of it into `band`, switching the lit band to the
│                        #   shadow band on the away side. Uniform block, ramp constants and the
│                        #   non-face path are unchanged.
└── cel.frag.spv         # RECOMPILED with glslc (committed, same as every other .spv).

docs/
└── RENDERING_NOTES.md   # CHANGED: §13 gains the fade bullet (it is the source of truth for the
                         #   actor render path).

# Explicitly unchanged:
render/Mesh.fs            # the cel uniform block and the draw are untouched — no new field.
world/ActorRender.fs      # faceLightDir and the draw are untouched.
shaders/mesh.vert(.spv)   # the vertex stage is untouched.
render/Gpu.fs             # pipelines unchanged; the same cel pipeline serves the edited shader.
screen/GameplayScreen.fs  # the actor draw is unchanged.
fsharp-game.fsproj        # CopyShaders already lists cel.frag.spv — no change.
test/                     # no new pure logic to test; the suite must pass unmodified.
specs/040-actor-cel-shading/  # NOT edited — this spec references it, it does not rewrite it.
```

**Structure Decision**: the change is confined to the actor's fragment shading. Because the shader
already receives both the face direction and the sun direction, the fade is computed *where those
values already are* rather than threading a new scalar through the CPU draw path — which is what
keeps the uniform block, `Mesh.draw`, `ActorRender.draw` and the `.fsproj` untouched. The band
direction (FR-001) is not touched, so the `specs/040` band contract holds; only the band's *amount*
is scaled.

## Phase 0: Research

See [research.md](./research.md). It settles why the fade lives in the shader body rather than on
the CPU draw path; why the comparison is azimuth-only with a degenerate fallback; the sign (facing
the sun = bright); the fade's shape (a two-tone switch at the 90° crossing, eased by a small
`smoothstep`); and how multiplying the band switches the lit band to the shadow tone. No
`NEEDS CLARIFICATION` remain — the spec's open points were resolved in `/speckit-clarify`.

## Phase 1: Design

- [data-model.md](./data-model.md) — no data change; records the existing cel uniform block (byte
  layout, unchanged) and the three existing inputs the fade reads, so the "no new data" claim is
  checkable.
- [contracts/cel-face-fade.md](./contracts/cel-face-fade.md) — the shader contract change
  (`shaders/cel.frag` shading body) and the explicit "unchanged" list (uniform, F# draw path,
  vertex stage, pipelines, `.fsproj`).
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite and the interactive
  walkthrough (turn the actor under a fixed sun; confirm the fade saturates at both ends and the
  band boundary stays fixed).

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — a few lines in one shader body; no new artifact. |
| II. Fail Loudly | Pass — the fallback is specified behaviour, not a hidden default. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — no data shape change. |
| V. Minimise GC Pressure | Pass — no CPU-side change at all. |
| N-V. Testing is the Authoring Tool | Not engaged. |
