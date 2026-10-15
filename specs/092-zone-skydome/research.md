# Research: Zone Skydome

Phase 0 decisions resolving the feature's design unknowns.

## D1 — Where the sky value lives

**Decision**: a `[<Struct>]` record `Skydome.Settings = { Enabled : bool; Zenith : Vector3; Horizon : Vector3 }`
in `render/Skydome.fs`, held as a mutable field `Zone.Skydome` — exactly how `Texture.LightSettings` is held
as `Zone.Light`.

**Rationale**: `odin-game` stores the three sky fields on the zone (`src/zone_registry.odin`), and the port
already models a zone-authored, shader-facing visual setting (the light) as a render-cluster struct on the
zone. Keeping the type in `render/` lets `world/Zone.fs` reference it (render is compiled below world) with
no new dependency direction. `ZoneTemplate`/`ZoneStaging` (in `world/ZoneTemplate.fs`) then carry the same
type.

**Alternatives considered**: a field on `Environment` (rejected — a sky must exist with no environment, FR-008);
a global setting on `SharedState` (rejected — the sky is per-zone, FR-006).

## D2 — Who owns the pipeline and the sphere mesh

**Decision**: `render/Skydome.fs` owns a `Skydome` value holding the pipeline, the sphere vertex/index
buffers and a reused uniform byte buffer, created by `Skydome.create device swapchainFormat depthFormat` and
freed by `Skydome.release`. The pipeline itself is built by a new `Gpu.createSkydomePipeline` (in `Gpu.fs`,
beside `createStaticPipeline`) and stored as `Gpu.SkydomePipeline`.

**Rationale**: pipelines live in `Gpu` with the pinned-array construction; the mesh + draw live in the
render module that uses them, as `Ui.fs` owns its layer's pipeline + buffers. The resource is device-lifetime,
so it is created in `Engine.fs` and threaded like `Ui`/`DebugDraw`.

**Alternatives considered**: a lazily-created global handle (`odin-game`'s `g_skydome_renderer`, `InstanceBuffer`'s
lazy `ensure`) — rejected in favour of the port's explicit create/destroy at the device seam, matching `Ui`.

## D3 — The shader: gradient + a simple sun, SDL_gpu resource sets

**Decision**: two new GLSL shaders compiled with `glslc` to `.spv`:

- `skydome.vert` — a position-only vertex (`layout(location = 0) in vec3 aPosition`), one vertex uniform
  `layout(set = 1, binding = 0) uniform PerDraw { mat4 viewProj; }`, passing the local position out as
  `vLocalPos`. (Odin's vertex shader verbatim, adjusted to the port's set convention.)
- `skydome.frag` — one fragment uniform `layout(set = 3, binding = 0) uniform SkyUniform { vec4 uZenith; vec4
  uHorizon; vec4 uLightDir; }`. The fragment normalises `vLocalPos` to a direction `dir`, uses `dir.y` to
  blend `uHorizon`→`uZenith` (`smoothstep(0, 0.4, dir.y)` above the horizon, flat horizon colour below), and
  adds a **simple sun**: `smoothstep` on `dot(dir, normalize(uLightDir.xyz))`, gated to `dir.y > 0`.

**Rationale**: the resource-set numbers are SDL_gpu's fixed convention (`docs/RENDERING_NOTES.md` §4). The
colour blend reproduces `odin-game`'s gradient; the sun is the trimmed single-disc version the Clarifications
chose. No `uTime` (clouds deferred), so no clock plumbing.

**Alternatives considered**: porting odin's fbm clouds + layered sun rings verbatim (deferred by the spec's
Clarifications); a time uniform (deferred with the clouds).

## D4 — Colour space

**Decision**: author the colours as **linear** RGB (the same space `Texture.LightSettings.Color` already
uses), and gamma-encode at the shader's output (`pow(color, vec3(1.0/2.2))`), matching `shaders/static.frag`.
The default colours convert `odin-game`'s sRGB `zenith (89,140,217)` / `horizon (140,179,217)` to linear.

**Rationale**: the scene pass writes to a non-sRGB swapchain format and every shader does its own gamma
(`static.frag`), so linear-authored colours + an end-of-shader encode reproduce `odin-game`'s appearance.

**Alternatives considered**: storing sRGB and skipping the encode (rejected — inconsistent with every other
shader, and the ImGui picker would then need a conversion).

## D5 — How the sky is drawn (behind the scene, camera-centred)

**Decision**: draw the sky **first** in the scene pass, before any geometry. The vertex shader is fed a
**sky-only** view-projection: a view from the origin looking along the camera's `forward` (so the dome is
always centred on the camera and never clips), and a projection whose far plane is just past the sphere
radius. The pipeline has depth test and depth write **disabled** and culling **disabled**.

**Rationale**: `odin-game` draws the sky first inside `BeginMode3D` with `DEPTH_TEST`/`CULL_FACE` disabled,
then draws geometry over it (`src/gameplay_screen.odin`); drawing it first with no depth write means geometry
overwrites it where geometry exists and nothing else is affected. Drawing it first (not last) is what makes
"depth test off" safe — a later draw would overwrite the geometry. The dedicated VP is what keeps the dome
behind everything without depending on the camera's far plane.

**Alternatives considered**: drawing it last with depth test off (rejected — overwrites the scene); drawing
it in its own pass before the scene (rejected — an extra pass for no benefit, and it would clear/load the
colour target).

## D6 — The command and the staging round-trip

**Decision**:
- A new `SetSkydome of ZoneId : string * Previous : Skydome.Settings * Next : Skydome.Settings` command,
  exactly the shape of `SetLight`; it names its zone (`placeOf`), and `apply`/`undo` assign the field.
- `ZoneTemplate` and `ZoneStaging` gain a `Skydome : Skydome.Settings` field; `ZoneTemplate.ofEnvironment`
  takes the sky, `Zone.captureTemplate` reads `zone.Skydome`, `ZoneStaging.empty`/`ofTemplate` carry it, and
  `GameplayState.applyStaging` sets `zone.Skydome <- staging.Skydome` beside `zone.Light <- staging.Light`.
- `EditorState` gains two colour buffers (zenith, horizon) mirroring `LightColorBuffer`/`LightColorEditing`.

**Rationale**: this is the exact path the light already takes (`SetLight`, `ZoneTemplate.Light`,
`ZoneStaging.Light`, `applyStaging`), so "saved into a scenario" is satisfied with no new mechanism
(spec FR-007/FR-010).

**Alternatives considered**: a `Batch` of primitive ops (rejected — `SetLight` set the precedent of one
before/after zone-valued command); storing the sky only on the runtime `Zone` and skipping staging
(rejected — that would not persist into a scenario).
