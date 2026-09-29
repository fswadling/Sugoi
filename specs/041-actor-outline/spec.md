# Feature Specification: Actor Outline

**Feature Branch**: `041-actor-outline`

**Created**: 2026-09-29

**Status**: Draft

**Input**: User description: "add an outline to the actors" (continuing the cel-shading work of spec 040)

**Origin**: `odin-game`'s cel-shading plan lists outlines as the first deferred enhancement:
*"Outlines — inverted-hull second pass (scaled back-face shells in the actor program)."* The
`odin-game` shader does not ship one yet, so this is not a straight port; it is the documented next
step of the same renderer.

The established way to outline a cel-shaded character in real time is the **inverted hull**
(back-face extrusion): draw the mesh a second time with vertices pushed out along their normals,
cull *front* faces, draw it flat in the outline colour, and let the depth buffer hide everything
except the ring that pokes past the silhouette. It is the "wireframe method" described in
[Wikipedia's cel-shading article](https://en.wikipedia.org/wiki/Cel_shading), it is Godot's **Grow**
feature ("*Add a second material pass, make it black and unshaded, reverse culling (Cull Front), and
add some grow*", [Godot — Standard Material 3D](https://docs.godotengine.org/en/stable/tutorials/3d/standard_material_3d.html)),
and it is what this port's engine can already support: SDL_gpu exposes front-face culling and depth
bias ([`SDL_GPUCullMode`](https://wiki.libsdl.org/SDL3/SDL_GPUCullMode),
[`SDL_GPURasterizerState`](https://wiki.libsdl.org/SDL3/SDL_GPURasterizerState)).

The other family — **post-process edge detection**, a Sobel/derivative filter over a depth buffer and
a world-normal buffer ([Lettier — Outlining](https://lettier.github.io/3d-game-shaders-for-beginners/outlining.html))
— gives interior crease lines as well, but needs offscreen depth and normal targets and a full-screen
pass. This port has neither (its scene renders straight to the swapchain with a depth texture, and
`render/SceneTarget.fs` is still planned), so that family is out of scope.

**The one hard problem is the mesh's normals.** Godot warns of the inverted hull: *"For Grow to work
as expected, the mesh must have connected faces with shared vertices, or 'smooth shading'. If the
mesh has disconnected faces with unique vertices, or 'flat shading', the mesh will appear to have
gaps."* Akane's mesh is overwhelmingly hard-edged: of its coincident vertices **79% have normals
diverging by more than 35°**, and the **Hair mesh is almost entirely hard-edged (2 694 of 2 696
coincident groups)**. Extruding along the authored normal would therefore tear the outline open at
every crease — worst on the hair, where it shows most. The outline must extrude along a direction
that is averaged across the coincident vertices, not the authored per-face normal.

Today actors render with cel shading (spec 040): `shaders/mesh.vert` (skinning + morphs) and
`shaders/cel.frag`, through `Gpu.CelPipeline`. This feature adds a second, outline pass over the same
posed geometry.

## Clarifications

### Session 2026-09-29

- Q: Should the outline cover the actor's alpha-cutout meshes (hair, eyes) or only the fully opaque meshes (body, face)? → A: Outline every mesh, hair and eyes included, with the outline masked by the same alpha cutout as the surface (FR-006, as specified; confirmed rather than changed).
- Q: Should the outline hold a constant on-screen width, or scale with distance? → A: A **fixed world-space shell** — the line is offset along the smoothed normal by one constant world distance, so it shrinks with distance exactly like the geometry (FR-008). Revised several times after seeing it run: a constant on-screen width did not recede; a shell clamped on screen, and then a clamped shell with a distance fade, still read as wrong; a plain world-space width is the final model.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An actor carries a drawn outline (Priority: P1)

An actor in the scene is drawn with an outline — a dark edge that follows the character's silhouette,
on top of the flat cel shading. The character reads as drawn rather than modelled.

**Why this priority**: this is the capability. Until an actor shows an outline at all, nothing else
about the feature (following the pose, staying even, closing at creases) can be judged.

**Independent Test**: spawn an actor and confirm a dark edge traces its silhouette while the
environment and props remain un-outlined.

**Acceptance Scenarios**:

1. **Given** an actor, **When** it renders, **Then** a continuous line of a single colour traces its
   outline against whatever is behind it.
2. **Given** an actor beside an environment and props, **When** both render, **Then** only the actor
   is outlined.
3. **Given** the outline and the cel shading together, **When** the actor renders, **Then** the
   outline sits at the actor's silhouette and does not cover the shaded interior.

---

### User Story 2 - The outline follows the actor as a physical line (Priority: P2)

The outline is part of the actor, not a static decal: it deforms with the skinned, morphed pose (it
bends with the limbs and moves with the character), and its width is even around the whole silhouette
rather than thick on one side and gone on another.

**Why this priority**: an outline that ignored the animation, or that thinned to nothing on a turned
side, would read as a bug. It ranks below Story 1 because a static-ish outline is still an outline,
and above Story 3 because it is the difference between "drawn" and "broken".

**Independent Test**: animate the actor (jog/idle and a morph) and confirm the outline tracks the
limbs; zoom the camera and confirm the line shrinks with distance exactly as the actor's apparent size
does.

**Acceptance Scenarios**:

1. **Given** an animating actor, **When** it plays a clip or blends a morph, **Then** its outline
   follows the deformed silhouette.
2. **Given** the actor at different distances from the camera, **When** it renders, **Then** the
   outline shrinks with distance exactly as the actor's apparent size does (a fixed world-space width).
3. **Given** the actor turned to face away, **When** it renders, **Then** the outline is still an even
   edge around the silhouette.

---

### User Story 3 - The outline closes at creases and does not leak or ghost (Priority: P3)

The outline is clean: unbroken where the mesh has hard creases, following the alpha cutouts of the
hair and eyes rather than boxing them, and free of the artefacts an outline pass can introduce —
bleeding through thin or concave parts, or doubling the shading.

**Why this priority**: this is what separates a usable outline from one that has to be turned off.
It ranks last because the outline works without it in the common case, but on *this* asset —
hard-edged hair especially — a broken outline is the likely failure.

**Independent Test**: outline Akane; confirm no gaps at the hair and body creases, no outline around
transparent hair/eye texels, no dark bleed through thin parts, and no change to the cel shading itself.

**Acceptance Scenarios**:

1. **Given** the actor's hard-edged meshes (body creases, hair), **When** it renders, **Then** the
   outline is continuous — no gaps between faces that meet at a sharp angle.
2. **Given** a hair or eye texture with transparent cutout texels, **When** it renders, **Then** the
   outline follows the visible (opaque) shape and there is no outline around the transparent regions.
3. **Given** a thin or concave part (a limb against the torso), **When** it renders, **Then** the
   outline does not bleed through the shaded surface it is in front of.
4. **Given** the feature is present, **When** environments, props, portraits, the UI and the editor
   render, **Then** they are unchanged and un-outlined.

---

### Edge Cases

- **Hard-edged / split normals** (the dominant case on this asset): the extrusion direction must be
  averaged across coincident vertices, or the outline tears open at each crease. See the Origin data.
- **Alpha-cutout meshes** (hair/eye cards): the outline must be masked by the same alpha cutout as the
  surface, so no outline is drawn around transparent texels.
- **Thin or concave geometry**: the extruded hull can poke through the front of the actor on a thin
  part; the outline must not draw over the actor's own shaded surface.
- **Distance**: the outline is a fixed world-space shell, so it shrinks with distance exactly like the
  actor; it must stay visible across the walkthrough's camera range.
- **The `Face` mesh**: it is outlined like any other mesh (the idealized face light of spec 040 does
  not affect the outline, which is unlit).
- **Several submeshes/materials**: every submesh is outlined; the outline colour is uniform regardless
  of the submesh's albedo.
- **An actor scaled or rotated in the editor**: the outline follows the transform.
- **No actor in the scene**: nothing of the feature is exercised and the frame is unchanged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Actors MUST render with a drawn outline: a single-colour edge that traces the actor's
  silhouette, on top of the cel shading (spec 040).
- **FR-002**: Only actors MUST be outlined. Environments, props, portraits, the UI and the editor MUST
  be unchanged and un-outlined.
- **FR-003**: The outline MUST be produced within the existing scene pass, without new offscreen
  render targets (no world-normal buffer, no full-screen colour target, no dedicated post-process
  pass): the port has no scene G-buffer and this feature MUST NOT require one.
- **FR-004**: The outline MUST follow the actor's animated silhouette — the same skinned, morphed pose
  the cel shading uses — so it deforms with clips and morph weights.
- **FR-005**: The outline MUST be closed at creases: on a mesh whose coincident vertices carry
  differing (hard-edged) normals, the outline MUST NOT open into gaps. The masking artefact that
  Godot documents for this technique MUST NOT appear on the port's own actor asset.
- **FR-006**: A mesh using an alpha cutout (hair, eyes) MUST have its outline masked by the same
  cutout: transparent texels MUST draw no outline, and the outline MUST follow the visible shape.
- **FR-007**: The outline MUST NOT draw over the actor's own shaded surface (no bleed-through on thin
  or concave parts) and MUST be correctly occluded by scene geometry in front of the actor.
- **FR-008**: The outline MUST be a fixed world-space shell: a constant world-space width, offset along
  the extrusion direction, so it shrinks with distance exactly as the actor's own size does. There MUST
  be no on-screen cap and no distance fade.
- **FR-009**: The outline's colour and width MUST be fixed constants this phase; there MUST be no
  authoring surface for them.
- **FR-010**: The cel shading (spec 040) MUST be unchanged by the outline pass: the same bands, light
  response, face light and cutouts, with no double-shading or darkening.
- **FR-011**: The outline MUST NOT introduce per-frame allocations, and skinning/morphing MUST remain
  unchanged (the same posed geometry as the surface).
- **FR-012**: All existing behaviour MUST be unchanged: environment, prop, portrait, UI and editor
  rendering, gameplay and editor behaviour, and the test suite MUST pass unmodified.
- **FR-013**: `dotnet build` MUST be clean and `dotnet test` MUST pass, including any new tests for
  the pure outline logic (e.g. the extrusion-direction computation).

### Key Entities *(include if feature involves data)*

- **Actor**: the skinned, morphed character entity (unchanged); the thing that gains the outline.
- **Outline pass / pipeline**: a second draw of the actor's posed mesh, flat-shaded in the outline
  colour, that produces the silhouette edge. Distinct from the cel pipeline of spec 040.
- **Extrusion direction** (the plan's "smoothed normal"; the code's `Iqm.smoothNormals` /
  vertex attribute `aSmoothNormal`): the direction a vertex is pushed along for the outline. It is
  *not* the authored per-face normal — it MUST be a per-vertex direction averaged across coincident
  vertices, derived once from the model (see the Origin data).
- **Outline parameters**: colour, and the width as a fixed world-space shell; fixed constants this
  phase.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An actor renders with a continuous single-colour silhouette edge while a co-located
  environment and prop render un-outlined (100% of actors outlined; 0 non-actors outlined).
- **SC-002**: Across an animation clip and a morph blend, the outline tracks the deformed silhouette
  (100% of sampled frames); the cel-shaded interior is unchanged (0 double-shading).
- **SC-003**: On Akane's hard-edged meshes — the hair above all — the outline is continuous, with 0
  observable gaps at coincident hard-edged vertices across a full turn of the actor.
- **SC-004**: On a hair/eye cutout mesh, 0 outline texels are drawn over transparent regions; the
  outline follows the visible shape.
- **SC-005**: Across the walkthrough's camera range, the outline shrinks with distance exactly as the
  actor does (a fixed world-space width), and no bleed-through appears on the thinnest silhouette.
- **SC-006**: The actor's pose is unchanged (the outline pass shares the posed geometry); no per-frame
  allocation is added (0 bytes/frame); `dotnet build` is clean and `dotnet test` passes with the
  existing assertions unmodified.

## Assumptions

- **Silhouette only.** This phase outlines the actor's profile (and the cutout shapes). Interior
  *crease* lines — the folds within the silhouette — are the post-process family's job and are out of
  scope here, because the port has no normal G-buffer (FR-003). This matches the reference's stated
  "inverted hull" plan.
- **The inverted hull is the chosen method.** It is what the reference names, what Godot documents as
  "Grow", and what the engine can support today (front-face culling + depth bias). The alternative
  stencil-buffer outline Godot added in 4.5 would need a stencil-capable depth format, which this
  engine does not currently create.
- **The extrusion direction is averaged across coincident vertices.** This is required, not optional,
  by the port's own asset (Origin data: Akane is ~79% hard-edged, hair ~100%), because the authored
  normals are per-face at creases. The average is derived once from the model at load (positions +
  normals are already parsed); it is not authored and not a new asset file if it can be derived.
- **Width is a fixed world-space shell.** Each vertex is offset along its smoothed normal by a single
  constant world distance, so the line is a physical ribbon: it shrinks with distance exactly as the
  actor does, and is thickest when the actor is closest. There is no on-screen cap and no distance
  fade. The width is a constant this phase (FR-008).
- **Colour and width are constants.** No per-actor or per-material outline authoring this phase
  (matching spec 040's parameter policy); a fixed dark tone is assumed.
- **No blend pass.** The outline is opaque; the cutout masking (FR-006) is an alpha test, as in spec
  040.
- **Shadows are still later scope.** As in spec 040, shadow receiving is not part of this feature;
  the outline is unlit and unaffected by the scene sun.
- **Skips nothing that spec 040 already fixed.** The cel pipeline, the `Face` idealization and the
  cutout behaviour are assumed correct and are only *reused* here.
