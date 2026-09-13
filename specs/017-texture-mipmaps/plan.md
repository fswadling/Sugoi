# Implementation Plan: Texture Mipmaps

**Branch**: `017-texture-mipmaps` | **Date**: 2026-09-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/017-texture-mipmaps/spec.md`

## Summary

Give every decoded texture a complete mip chain, generated once at load. Today
`render/Texture.fs` decodes straight into an SDL_gpu texture with a single level
while the shared sampler is configured to sample mip levels, so minified
fragments alias and hit a sampling-bandwidth cliff. The fix decodes the image
into a software surface, normalises it to a 32-bit RGBA layout, creates the GPU
texture with a full chain (`Sampler | ColorTarget` usage) sized to its largest
dimension, uploads level 0, and lets SDL generate the remaining levels in the
same command buffer. The placeholder stays 1×1 with a single level. Nothing on
the per-frame path changes.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`).

**Primary Dependencies**: `SDL3-CS` 3.4.14.1 (SDL_gpu + SDL_image). Level
generation needs SDL ≥ 3.2; the host ships 3.4.12. No new managed or native
dependency.

**Storage**: N/A — textures live in the asset loader's path-keyed cache for the
process lifetime.

**Testing**: xUnit. The decode/upload/generate path needs a GPU device, so it is
validated through the app (quickstart) rather than unit-tested; the only pure,
headless-testable part is the level-count arithmetic for a given width/height.

**Target Platform**: Linux x64 desktop (native), Vulkan backend.

**Project Type**: native desktop game.

**Performance Goals**: no regression — preparation is a one-time load cost and
the per-frame render path gains no work (SC-004).

**Constraints**: F# compile order is the dependency order; `render/Texture.fs`
owns decode/upload and is the only file that needs to change. The static and mesh
pipelines, their vertex layouts, and the shaders are untouched. The shared
material sampler already samples mip levels and is unchanged.

**Scale/Scope**: every texture the asset loader decodes (glTF environment/prop
maps and actor maps), cached by absolute path so a shared map is prepared once;
one shared 1×1 placeholder.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: one loader function is rewritten. No new module, no
  per-asset authoring, no per-material level policy, and no fallback chain when a
  backend cannot generate levels.
- **II. Fail Loudly** — PASS: a decode, format-normalisation, or texture-creation
  failure raises with the source path; it never silently falls back to a
  single-level texture (SC-005).
- **III. Spec-First** — PASS: this plan follows `spec.md`. (The code was written
  first and the spec/plan/tasks are being recorded retrospectively, by explicit
  request; the workflow ordering is inverted only for this one feature.)
- **IV. Data-Oriented Architecture** — PASS: no new state or object graph; a
  texture remains a value handle plus its sampler, cached by path.
- **V. Minimise GC Pressure** — PASS: the staging byte array and surface are
  transient load-time allocations; the per-frame path allocates nothing new.

## Project Structure

### Documentation (this feature)

```text
specs/017-texture-mipmaps/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 validation guide
├── contracts/
│   └── texture-upload.md # Loader contract
├── checklists/
│   └── requirements.md  # /speckit-specify quality gate
└── tasks.md             # /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
render/Texture.fs           # EXTENDED: load decodes to a surface, normalises,
                            #           creates a mip-chained texture, uploads
                            #           level 0, generates the remaining levels
render/Gpu.fs               # unchanged (transfer-buffer/channel helpers reused)
world/Assets.fs             # unchanged (path-keyed texture cache already dedups)
shaders/static.*, mesh.*    # unchanged
test/TextureTests.fs        # NEW (optional): the pure level-count arithmetic
```

**Structure Decision**: the change lives entirely in `render/Texture.fs`'s
`load`. `world/Assets.fs` already caches decoded textures by absolute path, so
sharing and load-once (FR-008) fall out of the existing structure. Because the
sampler is shared and already configured for mip sampling, no pipeline or shader
change is needed — which is why this is confined to one function.

## Complexity Tracking

No constitution violations to justify.
