# Feature Specification: Multisample Anti-Aliasing

**Feature Branch**: `042-multisample-antialiasing`

**Created**: 2026-09-29

**Status**: Draft

**Input**: User description: "add MSAA to the renderer so edges — especially the actor outline — are anti-aliased"

**Origin**: The actor outline (spec 041) reads as a jagged, stair-stepped line on its boundaries.
The cause is not the outline: **the renderer draws with no multisampling anywhere.** Every graphics
pipeline and render target is single-sampled — the actor, static and outline pipelines and the depth
texture (`render/Gpu.fs`), the 2D layer (`render/Ui.fs`), `DebugDraw` (`render/DebugDraw.fs`) and the
texture upload paths (`render/Texture.fs`) are all `SampleCount1` — and the scene pass renders
**directly into the acquired swapchain texture** (`Engine.fs`), so there is no multisampled target and
no resolve. Every edge in the frame is therefore aliased: the outline, the character silhouette, prop
and environment silhouettes, and `DebugDraw` lines alike.

A near-black outline is the most visible case because it is a hard, high-contrast line. Its
*inner* boundary (a dark line against the brightly lit cel character) reads as a defect more than its
outer boundary (a dark line against the scene) — the same staircase on both sides, noticed first on the
inside.

The established fix for geometry-edge aliasing is **multisample anti-aliasing (MSAA)**: render the
scene into multisampled colour and depth targets (each pixel keeps several depth/coverage samples),
then **resolve** the multisampled colour into the ordinary single-sample swapchain image. It smooths
polygon edges — including depth-tested edges between overlapping surfaces, and the alpha-cutout
`discard` edges of the hair/eye cards — without the blur or cost of a full-screen post-process filter.

**Scope note**: this is a **renderer-wide** change, not an outline tweak. It must preserve the
appearance of spec 040 (cel shading) and spec 041 (outline) apart from the smoothing itself.

## Clarifications

### Session 2026-09-29

- Q: How many samples per pixel should the anti-aliasing use? → A: 4× — the standard quality/cost point; it smooths the outline and geometry edges at moderate multisample memory and fill cost.
- Q: Should the game's own 2D layer and the `DebugDraw`/gizmo lines be anti-aliased along with the 3D scene, or stay single-sample? → A: Anti-aliased too — they already share the scene pass, so their edges smooth for free; no second target or pass.
- Q: If the GPU cannot provide 4× multisampling, should the renderer fail loudly or fall back? → A: Fail loudly — an unsupported sample count is a clear initialisation error; no fallback path (per `AGENTS.md`).
- Q: Should the spec include a measurable frame-rate budget for the anti-aliasing? → A: No — the checks are a correct image (SC-001–SC-004) and an unchanged per-frame allocation (SC-005); no explicit frame-rate target this phase.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The scene is drawn with smooth edges (Priority: P1)

The frame is rendered with multisample anti-aliasing, so the actor outline (spec 041) no longer shows a
staircase on either boundary, and geometry silhouettes across the scene read as smooth edges rather than
steps.

**Why this priority**: this is the capability itself and the entire reason for the request. Until a
multisampled scene renders at all, nothing else (correct depth, correct overlays, resize behaviour) can
be judged.

**Independent Test**: view an actor's outline (and a hard-edged prop beside it) and confirm the
boundaries are smooth gradients rather than single-pixel steps.

**Acceptance Scenarios**:

1. **Given** an actor with the outline (spec 041), **When** the frame renders, **Then** both the inner
   and outer boundaries of the line are anti-aliased — no visible staircase.
2. **Given** any geometry silhouette against the background or against another surface, **When** the
   frame renders, **Then** that edge is anti-aliased.
3. **Given** the anti-aliasing is present, **When** the frame renders, **Then** the scene is otherwise
   the same image as before (same colours, same cel bands, same outline), only with smooth edges.

---

### User Story 2 - The anti-aliasing is correct across the whole frame (Priority: P2)

Multisampling is only correct if it covers the depth-tested scene as a whole and composites cleanly: edges
where two surfaces overlap (not just silhouettes against the background) are smoothed, the alpha-cutout
masks stay correct, the outline's depth-bias behaviour is unchanged, and the 2D/editor overlays sit on
top of the resolved image.

**Why this priority**: a multisampled colour target with a single-sample depth buffer would leave
overlapping-surface edges and the cutout masks ragged, and a resolve placed wrongly would blur or drop
the overlays. This is what makes the feature usable rather than a partial fix. It ranks below Story 1
because the outline's own boundary is smoothed even by a minimal version.

**Independent Test**: turn the actor so a limb crosses the torso and confirm the limb's edge against the
body is smooth and the outline does not leak; open the editor overlay and confirm it and the game's 2D UI
are unaffected.

**Acceptance Scenarios**:

1. **Given** two overlapping surfaces (an arm against the torso), **When** the frame renders, **Then** the
   nearer surface's edge against the further one is anti-aliased.
2. **Given** the hair/eye alpha-cutout meshes, **When** they render, **Then** their cut edges are
   anti-aliased and there is no cel-shading or outline regression, and the cut shape is unchanged.
3. **Given** the editor overlay (ImGui) and the game's own 2D UI layer, **When** the frame renders,
   **Then** they draw on top of the resolved scene correctly — no blurring, no double-resolve, no
   re-sampling of their text and lines.
4. **Given** the outline's depth bias (spec 041, FR-007), **When** the frame renders with anti-aliasing,
   **Then** there is no new bleed-through on thin/concave parts and the line is not thinned.

---

### User Story 3 - Bounded cost, honest quality point, clean lifecycle (Priority: P3)

The anti-aliasing runs at a fixed sample count chosen as this port's quality/cost point; the render
targets are created once and recreated only when the render extent changes (never per frame); and if the
device cannot provide the sample count the engine fails loudly rather than silently rendering without it.

**Why this priority**: this governs cost and robustness. It ranks last because the feature delivers its
value without it, but the port's conventions (`AGENTS.md`: fail loudly, no just-in-case fallbacks; no
per-frame allocation) make it a requirement of a change that touches every frame.

**Independent Test**: resize the window repeatedly and confirm the frame stays correct and the targets
are not recreated every frame; confirm that an unavailable sample count is a loud failure, not a silent
degradation.

**Acceptance Scenarios**:

1. **Given** a window resize or a display-DPI change, **When** the render extent changes, **Then** the
   multisampled targets are recreated once at the new size and the frame is correct.
2. **Given** a steady window, **When** frames render, **Then** the multisampled targets are reused and
   the frame allocates no new GPU or managed memory.
3. **Given** a device that cannot provide the required sample count, **When** the renderer initialises,
   **Then** it fails loudly instead of silently falling back to no anti-aliasing.

---

### Edge Cases

- **Window resize / DPI change**: the multisampled colour and depth targets follow the render extent; the
  existing single-sample depth-recreate path is the precedent.
- **Minimised / zero-size window**: the frame is skipped as today (no valid swapchain texture); no
  multisampled target is created for a zero extent.
- **Screens with no 3D scene** (the main menu): the scene pass still clears and must still resolve, so the
  cleared colour reaches the swapchain exactly as before.
- **The 2D layer and `DebugDraw` share the scene pass**: they render into the same target, so their geometry
  edges also become anti-aliased; their ordering and alpha blending must be preserved.
- **The editor overlay (ImGui)** composites in its own pass after the scene: it must load the *resolved*
  swapchain image and must not itself be multisampled.
- **Alpha-cutout `discard`** (hair/eye cards): under MSAA, coverage is per-sample; the cutout must still
  produce the same visible shape, with anti-aliased cut edges.
- **Several submeshes/materials**: each submesh's outline and cel draws go through the same multisampled
  target; the per-submesh sampler binding is unaffected.
- **The sample count does not vary**: it is the fixed 4× of FR-001/FR-010, so the memory/fill cost is
  fixed too — there is no runtime choice of sample count to exceed a budget with.
- **No actor in the scene**: nothing of the feature is exercised beyond the scene being anti-aliased.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The 3D scene MUST be rendered with multisample anti-aliasing at a fixed sample count of
  **4×**, applied to **both** the colour and the depth targets.
- **FR-002**: The multisampled colour MUST be resolved into the acquired swapchain texture at the end of
  the scene pass, so the swapchain holds the resolved single-sample image.
- **FR-003**: Every pass that renders into the scene — the actor cel surface and outline (specs 040/041),
  the static environment/props, `DebugDraw`, the gizmo and the game's 2D UI layer — MUST render through the
  multisampled targets.
- **FR-004**: The depth buffer MUST be multisampled, so edges between **overlapping surfaces** are
  anti-aliased, not only silhouettes against the background.
- **FR-005**: Alpha-cutout `discard` (spec 041 outline, spec 040 cel) MUST keep per-sample coverage: cutout
  edges MUST be anti-aliased and the visible cut shape MUST be unchanged.
- **FR-006**: The outline's depth-bias behaviour (spec 041, FR-007) MUST be preserved — no new
  bleed-through on thin/concave parts and no thinning of the line.
- **FR-007**: The editor overlay (ImGui) MUST draw over the **resolved** image and MUST NOT be
  multisampled or resolved; the game's 2D UI layer MUST composite correctly — its interior, colours and
  blending unchanged, with only its geometry edges coverage-smoothed by the shared pass (Clarifications Q2).
- **FR-008**: The multisampled targets MUST be created once and recreated only when the render extent
  changes; the feature MUST NOT add per-frame allocations (spec 041, FR-011).
- **FR-009**: If the device cannot provide the required sample count, initialisation MUST fail loudly
  rather than silently fall back to no anti-aliasing; a multisampled target that fails to create (a null
  handle) MUST also fail loudly, not render without it.
- **FR-010**: The sample count MUST be a fixed constant this phase; there MUST be no authoring surface for
  it.
- **FR-011**: Specs 040/041 appearance MUST be preserved apart from the smoothing itself (the cel bands,
  light response, face light, outline width/colour, cutouts), and environment, prop, portrait rendering and
  gameplay/editor behaviour MUST be unchanged.
- **FR-012**: `dotnet build` MUST be clean and `dotnet test` MUST pass with the existing assertions
  unmodified, including any new tests for pure logic this feature introduces.
- **FR-013**: The scene MUST still be cleared each frame, and a screen that draws no 3D scene MUST resolve
  that cleared colour to the swapchain as before.

### Key Entities *(include if feature involves data)*

- **Multisampled scene target**: the multisampled colour texture (and matching multisampled depth texture)
  the scene pass renders into; several coverage samples per pixel.
- **Resolve**: writing the multisampled colour down to a single-sample image (the acquired swapchain
  texture) at the end of the scene pass.
- **Sample count**: the fixed number of samples per pixel; a constant this phase (FR-010).
- **Scene pass**: the single pass that already draws the actor, the environment/props, `DebugDraw`, the
  gizmo and the game's 2D UI into the colour + depth targets.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The actor outline's inner and outer boundaries are anti-aliased across a full turn of the
  actor and across the walkthrough's camera range — 0 visible stair-steps at the walkthrough's view.
- **SC-002**: Edges between overlapping surfaces (a limb against the torso) are anti-aliased (qualitatively
  smooth), with no outline bleed-through introduced.
- **SC-003**: Hair/eye cutout edges are anti-aliased with 0 change to the cut shape and 0 new cel/outline
  artefacts.
- **SC-004**: The editor overlay is unaffected — 0 blurring and 0 double-resolve in its text and lines;
  the 2D UI layer's interior, colours and blending are unchanged, with only its geometry edges
  coverage-smoothed by the shared pass (FR-007).
- **SC-005**: The per-frame allocation is unchanged (0 bytes/frame); the multisampled targets are recreated
  only when the render extent changes.
- **SC-006**: `dotnet build` is clean and `dotnet test` passes with the existing assertions unmodified.

## Assumptions

- **MSAA is the chosen method, at a fixed 4×.** Geometry-edge aliasing (the observed artefact) is exactly
  what MSAA targets; 4× is the standard quality/cost point and is near-universally supported. Post-process
  filters (FXAA) and temporal methods (TAA) are out of scope — they blur or need history buffers this port
  does not have.
- **The change is confined to the 3D scene pass.** The editor overlay keeps its own rendering and composites
  after the resolve; it is not multisampled.
- **The sample count is a constant.** No settings UI, no per-screen choice this phase (matching the
  parameter policy of specs 040/041).
- **Fail loudly, no fallback.** Consistent with `AGENTS.md`, an unsupported sample count is an error, not a
  silent downgrade to single-sample rendering.
- **Target device.** The port targets a desktop SDL_gpu device that reports multisample support; the change
  does not add a software fallback.
- **The 2D layer and `DebugDraw` are in the scene pass today** and therefore acquire anti-aliased edges as a
  side effect; this is acceptable and must not change their ordering or blending.
- **No explicit frame-rate budget this phase.** Multisampling adds fill cost, but acceptance rests on the
  correct image (SC-001–SC-004) and the unchanged per-frame allocation (SC-005); no frame-rate target is set.
- **Deferred items stay deferred.** Shadows, rim light, specular/matcap, interior crease lines and the other
  non-goals of specs 040/041 remain out of scope.
