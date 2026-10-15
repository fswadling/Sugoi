# Implementation Plan: Zone Skydome

**Branch**: `092-zone-skydome` | **Date**: 2026-10-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/092-zone-skydome/spec.md`

## Summary

Bring `odin-game`'s zone-level **skydome** into the port: a large full sphere drawn behind the scene,
shaded by a horizon→zenith gradient with a simple sun that follows the zone's authored light. The sky
is an **authored, zone-level setting** (an on/off flag and two colours) sitting beside the zone's light;
because a zone's authored settings are its **staging**, the sky is captured by a scenario and restored by
a fork (`specs/079-scenario-owned-zones`), exactly as `specs/043-zone-light-settings` made the light.
Scope is trimmed per the Clarifications: gradient + a simple sun, **no** clouds and **no** animation.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: SDL3 + SDL_gpu (`SDL3-CS`), Dear ImGui (`ImGui.fs`/`ImGuiSdl3.fs`), Prime
(`scvalue`/`scstring`). No new dependency.

**Storage**: N/A — the sky is part of a zone's in-memory staging; a scenario carries it (no `project.json`
in this feature).

**Testing**: `dotnet test` (xUnit, one `<Module>Tests.fs` per module, headless).

**Target Platform**: Linux desktop (SDL3 + SDL_gpu / Vulkan).

**Project Type**: desktop game (single project, `fsharp-game.fsproj`).

**Performance Goals**: one extra draw call per frame (one sphere, ~≤1k triangles); no per-frame
allocation (the uniform buffer is reused).

**Constraints**: shaders are GLSL compiled to SPIR-V with `glslc`, checked in as `.spv` and copied by the
`CopyShaders` target. SDL_gpu resource-set convention: vertex uniforms `set = 1`, fragment uniforms
`set = 3` (`docs/RENDERING_NOTES.md` §4).

**Scale/Scope**: one new render module (`render/Skydome.fs`), one pipeline, two shaders, a zone field, a
command, three Inspector controls, staging wiring, tests, doc updates.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. One record type, one pipeline, one mesh, one command; no speculative
  settings (cloud density, sun colour are constants, not authored fields). Clouds/animation are
  deliberately deferred rather than half-built.
- **II. Fail Loudly** — PASS. The pipeline failing to build raises (the `Ui`/`Gpu` precedent); no silent
  fallback.
- **III. Spec-First** — PASS. This plan follows `specs/092-zone-skydome/spec.md`.
- **IV. Data-Oriented Architecture** — PASS. `Skydome.Settings` is a `[<Struct>]` record (like
  `Texture.LightSettings`); the sphere is a plain vertex/index buffer.
- **V. Minimise GC Pressure** — PASS. The sphere is uploaded once; the draw reuses a fixed uniform byte
  buffer and allocates nothing per frame.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/092-zone-skydome/
├── plan.md              # this file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── skydome-shader.md  # the F# ↔ GLSL uniform contract
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
render/
├── Gpu.fs               # + createSkydomePipeline + Gpu.SkydomePipeline field/create/destroy
└── Skydome.fs           # NEW: Settings + sphere mesh + pipeline owner + create/draw/release
world/
├── Zone.fs              # + mutable Skydome : Skydome.Settings; +Zone.create default; captureTemplate
├── ZoneTemplate.fs      # + Skydome on ZoneTemplate and ZoneStaging (+ default/empty/ofTemplate/ofEnvironment)
└── ScenarioZone.fs      # captureStaging carries the zone's Skydome
state/
├── Command.fs           # + SetSkydome; placeOf; apply/undo
├── EditorState.fs       # + Skydome colour buffers (zenith/horizon) + editing flags
└── GameplayState.fs     # applyStaging sets zone.Skydome
editor/
├── Selection.fs         # + setSkydome (mirrors setLight)
└── Inspector.fs         # + drawSkydome in the no-selection zone fallback
screen/
└── GameplayScreen.fs    # renderScene draws the sky first in the scene pass
Engine.fs                # create the Skydome resource; pass to ScreenManager.init
screen/ScreenManager.fs  # thread the Skydome resource through init/SharedState or GameplayState
shaders/
├── skydome.vert         # NEW (position-only, set=1 mat4 VP)
├── skydome.frag         # NEW (gradient + simple sun, set=3 uniforms)
└── skydome.{vert,frag}.spv  # NEW, glslc-compiled
test/
├── SkydomeTests.fs      # NEW: Settings defaults, staging round-trip, editor step
├── ScenarioForkTests.fs # extend: fork/restage carry the sky
└── SpawningTests.fs     # extend: captureTemplate captures the sky (if not covered by the new file)
fsharp-game.fsproj       # compile order + CopyShaders for the new .spv
docs/
├── ARCHITECTURE.md      # "Zone = world container": name the sky among the zone's settings
├── PROJECT_STRUCTURE.md # Skydome.fs is no longer "(planned)"
└── ROADMAP.md           # phase 14: sky landed (trimmed)
```

**Structure Decision**: single project, files placed in their existing domain cluster (`render/`,
`world/`, `state/`, `editor/`, `screen/`), inserted at the correct point in the `.fsproj` compile list.
The shared GPU resource (pipeline + sphere) is a device-lifetime object created in `Engine.fs` and read
by the gameplay scene, mirroring how `Ui` and `DebugDraw` are created and threaded. `render/Skydome.fs`
owns its own pipeline + mesh (the `Ui.fs` precedent) and calls a `Gpu.createSkydomePipeline` helper so the
pinned-array pipeline construction lives with the other pipelines.

## Complexity Tracking

No constitution violations; nothing to justify.
