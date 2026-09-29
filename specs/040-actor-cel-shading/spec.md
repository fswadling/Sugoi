# Feature Specification: Cel-Shaded Actors

**Feature Branch**: `040-actor-cel-shading`

**Created**: 2026-09-29

**Status**: Draft

**Input**: User description: "add the stylised cel shading for actors that odin-game already has"

**Origin**: `odin-game` is the template. Actors draw through a dedicated program
(`src/actor_render.odin`, planned in `docs/CEL_SHADING_PLAN.md`) instead of raylib's
default material pipeline, with `assets/shaders/actor/actor.{vert,frag}` producing a
**two-band cel look with a soft ramp**: the wrapped half-Lambert
(`dot(N, L) * 0.5 + 0.5`) is stepped by `smoothstep(threshold - softness, threshold +
softness, …)` into a shadow band and a lit band, combined additively as
`albedo * (ambient + sunColour * band)` (the reference's shipped form; its earlier plan
described a plain mix instead). Two further details shape the look:

- **The face is lit by an idealized light.** The actor's meshes whose IQM name is
  `Face` (`Actor_Renderer.face_meshes = iqm_mesh_indices(model_path, "Face")`) do not use
  the scene sun for the band direction. Instead they use a fixed direction in front of
  the actor — `{sin(yaw), 0.25, cos(yaw)}` (normalized in the shader), i.e. the actor's
  facing yaw with a slight elevation — so the face keeps constant, readable banding no
  matter which way the actor turns or where the sun is. The band *colours* still come from
  the scene sun and ambient; only the band *direction* is idealized.
- **The shipped shader also gates the lit band by a shadow-map PCF** and floors visibility
  so the two-tone never dies in shade — but that, plus outlines, rim, specular and
  per-actor parameters, is explicitly the reference's *later* work.

This port already has what the initial phase needs: actors are skinned and morphed and
drawn as one mesh per actor with per-submesh albedo materials (`world/ActorRender.fs`,
`render/Mesh.fs`); the mesh fragment shader already alpha-tests cutout texels; the scene
supplies one directional sun plus an ambient term (`Texture.Light`); and the IQM parser
already reads each mesh's **name** (`core/Iqm.fs`), so the same `Face` identification the
reference uses is available. What is missing is the look: actors currently share the PBR
pipeline, so they shade with the full metallic-roughness BRDF and carry none of the flat
two-tone banding that reads as the anime style. This feature gives actors their own cel
pipeline while leaving environments and props on PBR.

## Clarifications

### Session 2026-09-29

- Q: Should the sun's contribution be added to the ambient term (like the environment's `direct + ambient`) or replace it (a `mix` of shadow and lit)? → A: Additive — `albedo * (ambient + sunColour * band)`, matching the environment's lighting model and the reference's shipped shader.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An actor renders with the stylised two-tone cel look (Priority: P1)

An actor in the scene is shaded in two flat bands — a shadow tone and a lit tone with a
soft edge between them — rather than the smooth gradient PBR produces elsewhere. The
flat, high-contrast banding is what makes the character read as cel-shaded anime.

**Why this priority**: this is the capability. Until an actor draws through a cel
pipeline, none of the feature's other work (band response to light, face light, cutout
handling, regression safety) has anything to stand on.

**Independent Test**: spawn an actor next to an environment; the actor shows a clean
two-tone split while the environment still shades with PBR.

**Acceptance Scenarios**:

1. **Given** an actor with an albedo texture, **When** it renders, **Then** its surface
   is separated into a shadow band and a lit band by a soft step, not a smooth PBR
   gradient.
2. **Given** the same actor and an environment side by side, **When** both render,
   **Then** only the actor is cel-shaded; the environment and props keep their PBR look.
3. **Given** an actor that animates, **When** it renders, **Then** the cel banding follows
   the skinned and morphed normals, and the deformation is unchanged from today.

---

### User Story 2 - The bands follow the scene light (Priority: P2)

The boundary between the two bands sits where the scene's sun does: turning the sun moves
the terminator across the actor, and raising the ambient lifts the shadow band so the actor
stays readable when the sun is dim. The cel look is stylised, not unlit.

**Why this priority**: a two-tone actor that ignores the scene light would not sit in the
world. It ranks below Story 1 because a fixed but light-consistent split is already the
headline look, and above the face story because it applies to the whole body.

**Independent Test**: rotate the scene sun and confirm the band boundary sweeps across the
actor's body; raise and lower the ambient and confirm the shadow band lightens and darkens
accordingly.

**Acceptance Scenarios**:

1. **Given** the sun points one way, **When** it is turned, **Then** the boundary between
   the shadow and lit bands moves across the actor's body accordingly.
2. **Given** the ambient term is raised, **Then** the shadow band is lifted (the actor is
   never fully black in shade).
3. **Given** the sun's colour changes, **Then** the lit band takes that colour while the
   shadow band takes the ambient tone.

---

### User Story 3 - The face keeps a constant, front-lit banding (Priority: P3)

The actor's face is the one part of the character that must not go dark or get a ragged
terminator as the actor turns: it is lit as if a soft light always sits in front of the
actor, slightly above eye level. The face therefore keeps a clean two-tone split — the skin
reading correctly and the features staying legible — whatever the actor's heading and
whatever the scene sun is doing. The rest of the body still follows the scene sun.

**Why this priority**: anime faces are the reason the reference special-cases the face mesh
at all; without it, a turning head washes out or muddies. It ranks below the body look
because the body is most of the character, and above the cutout story because it is a
visible artistic behaviour rather than a guard.

**Independent Test**: turn the actor through a full circle and move the scene sun around;
confirm the face's band boundary stays fixed relative to the face while the body's boundary
follows the sun.

**Acceptance Scenarios**:

1. **Given** an actor, **When** the scene sun is turned, **Then** the face's band boundary
   does **not** move (the body's does).
2. **Given** the actor is turned to face away from the camera, **Then** the face's front-lit
   banding is unchanged from when it faced the camera.
3. **Given** the scene sun's colour or ambient changes, **Then** the face's tones take that
   colour/ambient (only the band direction is idealized, not the colours).

---

### User Story 4 - Cutouts stay clean and nothing else changes (Priority: P4)

Anime hair and eye textures are alpha cutouts: their transparent texels stay discarded, so
no fringe or sorting artefact appears and no blend pass becomes necessary. Everything that
is not an actor — environments, props, portraits, the UI and the editor — renders exactly as
it does today.

**Why this priority**: it is the guard against the feature leaking: the cel pipeline must be
actor-only, and cutouts are the one material behaviour a naive dedicated shader could
regress. It ranks last because the common actor renders correctly without it, but it is what
makes the change safe to ship.

**Independent Test**: render an actor whose albedo has fully transparent texels (hair
cards / eye overlays) and confirm they are absent, not fringed or blended; run the suite
and confirm the environment, prop, UI and editor paths are unchanged.

**Acceptance Scenarios**:

1. **Given** an actor material whose albedo alpha falls below the cutoff, **When** it
   renders, **Then** those texels are discarded (no blend pass, no sorting artefact).
2. **Given** a material with no albedo texture, **When** it renders, **Then** it shades
   from its base colour factor through the same cel bands.
3. **Given** the feature is present, **When** environments, props, portraits, the UI and
   the editor render, **Then** they are unchanged.

---

### Edge Cases

- **An actor asset with no mesh named `Face`**: every mesh is lit by the scene sun — the
  face light simply never applies, and nothing fails.
- **Several meshes named `Face`**: all of them are treated as face meshes (the reference
  keys off a set of indices, not one).
- **A face mesh whose name contains `Face` but is not the face** (e.g. an unrelated mesh):
  the reference matches the name exactly, so identification follows the asset's own naming.
- **A factor-only actor material** (no albedo texture): it shades from its base colour
  factor through the same two bands rather than failing or rendering unlit.
- **Cutout texels at the band boundary**: a discarded texel must not leave a fringe or
  a one-pixel outline where the shadow band meets the cutout.
- **Very hard or very soft ramp**: the softness is small; a material must not show a
  visible stair-step or a fully blurred, PBR-like gradient.
- **An actor with several submeshes**, each with its own material: every submesh is
  cel-shaded, each with its own albedo/base colour, and only the `Face` ones take the
  idealized light.
- **An actor facing away from the sun**: its body shows the shadow band across the whole
  surface, still lifted by ambient (never pure black), while the face keeps its front-lit
  split.
- **Morphs and multi-clip blending** (specs 036/037): the cel path sees the same deformed
  vertices and normals as the PBR path, so animation is unaffected.
- **No actor in the scene**: nothing of the feature is exercised and the frame is
  unchanged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Actors MUST render with cel shading instead of the metallic-roughness PBR
  shading they use today. Environments and props MUST keep rendering with PBR.
- **FR-002**: An actor's surface MUST be separated into exactly two flat bands — a shadow
  band and a lit band — with a soft ramp at the boundary, driven by the wrapped half-Lambert
  `dot(N, L) * 0.5 + 0.5` (so the terminator wraps past the geometric horizon as it does in
  the reference), stepped by a threshold of `0.5` and a softness of `0.05`.
- **FR-003**: The colour MUST be composed additively — `albedo * (ambient + sunColour *
  band)` — so a surface in the shadow band takes the albedo times the ambient term and a
  surface in the lit band takes the albedo times the ambient **plus** the sun's colour. This
  matches the scene's own model (the environment shades as `direct + ambient`), so a lit actor
  sits at the same brightness as the lit environment beside it. Because ambient is always
  present, the shadow band MUST never fall to black.
- **FR-004**: For a non-face mesh, the band direction MUST be the scene sun: changing the
  sun's direction moves the terminator across the body, and changing the sun's colour or the
  ambient changes the corresponding band. A non-face mesh MUST NOT be lit by a hard-coded,
  scene-independent light.
- **FR-005**: The albedo MUST come from the mesh's material albedo texture where present
  (sRGB-decoded, multiplied by the base-colour factor), and from the base-colour factor
  alone where it is absent.
- **FR-006**: Cutout texels MUST be discarded by alpha test — a texel whose albedo alpha
  (times the base-colour alpha) falls below the cutoff is dropped — so hair/eye cutouts stay
  clean without a blend pass or sorting.
- **FR-007**: A mesh the asset names `Face` MUST take its band direction from a fixed,
  idealized light in front of the actor (the actor's facing direction, elevated by a fixed
  `0.25` — FR-012), not from the scene sun, so its band boundary is constant across the
  actor's heading and the sun's position. The direction MUST be normalized before use, as
  the reference does.
- **FR-008**: Only the band *direction* on a face mesh is idealized: the band *colours* MUST
  still come from the scene sun's colour and the ambient term, so a face still responds to
  the scene's lighting mood.
- **FR-009**: The face behaviour (FR-007, FR-008) MUST apply only to `Face` meshes; every
  other mesh uses the scene sun (FR-004).
- **FR-010**: Skinning and morphing MUST be unchanged: the cel path MUST consume the same
  deformed vertices and normals the PBR path does, so poses, blends and morph weights are
  unaffected.
- **FR-011**: The actor draw path MUST NOT introduce per-frame allocations.
- **FR-012**: Cel parameters (ramp threshold, ramp softness, alpha cutoff, face-light
  elevation) MUST be fixed constants this phase; there MUST be no authoring surface for them.
- **FR-013**: All existing behaviour MUST be unchanged: environment, prop, portrait, UI and
  editor rendering, gameplay and editor behaviour, and the test suite MUST pass unmodified.
- **FR-014**: `dotnet build` MUST be clean and `dotnet test` MUST pass, including any new
  tests for the pure cel logic (e.g. face-mesh identification).

### Key Entities *(include if feature involves data)*

- **Actor**: the skinned, morphed character entity and its `ActorRender` instance state
  (unchanged); the thing whose surface gains the cel look.
- **Actor mesh material**: the existing per-submesh albedo/`baseColorFactor` material
  (unchanged); the source of the cel band colours.
- **Face mesh**: a mesh of the actor's model whose authored name is `Face`; the meshes that
  take the idealized front light instead of the scene sun. Identified from the model's mesh
  names, which the actor loader already parses.
- **Light**: the existing scene light (direction toward the sun, colour, ambient); the input
  the body's bands follow, and the source of the face's band colours.
- **Cel pipeline**: the actor mesh pipeline with its fragment stage replaced by the cel
  shader; it consumes the same (skinned, morphed) mesh vertex data as before and differs
  only in the fragment stage.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An actor renders with a clean two-tone split while a co-located environment
  renders with PBR (2 of 2 pipelines behaving as specified).
- **SC-002**: Turning the scene sun moves the body's band boundary across the actor, and
  raising the ambient lifts the shadow band (100% of the sampled sun directions and ambient
  levels).
- **SC-003**: Over a full turn of the actor and a sweep of the sun, the face's band boundary
  stays fixed relative to the face while the body's follows the sun (100% of sampled
  headings and sun directions), and the face still takes the scene's light colour/ambient
  (100%).
- **SC-004**: An actor whose albedo has transparent texels shows those texels discarded,
  with no fringe and no blend artefact (0 visible cutout artefacts).
- **SC-005**: An actor's pose is identical before and after the feature under the same
  animation and morph weights (100%, since the vertex path is unchanged); no per-frame
  allocation is added to the draw path (0 bytes/frame).
- **SC-006**: `dotnet build` is clean and `dotnet test` passes, with the existing rendering,
  gameplay and editor assertions unmodified.

## Assumptions

- **Actors only.** This is a character style; environments and props keep the PBR pipeline.
  The reference applies cel shading only to actors too.
- **No shadow receiving yet.** The reference's shipped shader gates the lit band with a
  shadow-map PCF, but this port has no shadow system (`render/Shadow.fs` is still planned,
  and the roadmap sequences shadows before cel-shading in phase 14). The band therefore
  combines albedo×ambient with albedo×sun additively, with no shadow factor, matching the
  reference's initial phase; shadow receiving is deferred to when the shadow map exists. This
  is a deliberate scope decision: shadows are later scope.
- **Face identification is by the authored IQM mesh name `Face`.** This is exactly the
  reference's rule (`iqm_mesh_indices(model_path, "Face")`), and the same Akane asset is in
  use here, so the same meshes are found. The port's IQM parser already reads mesh names;
  the actor loader must carry that flag through to the draw so each submesh can be marked.
- **The face is idealized in direction only.** Matching the reference, a face mesh still
  multiplies the same ambient and sun colour terms; only the band direction is fixed. The
  elevation (`0.25`) and the facing-yaw derivation match the reference.
- **Alpha test only, no blend pass.** Cutouts are handled by discard, as the existing mesh
  shader already does; a transparent/blended actor pass is out of scope.
- **Parameters are constants.** Ramp threshold, softness, alpha cutoff and face-light
  elevation are fixed for this phase, matching the reference's initial phase; per-actor or
  zone authoring is deferred.
- **Non-goals (later phases, matching the reference's plan):** shadow-map receiving,
  inverted-hull outlines, rim light, specular/matcap bands, ramp-texture authoring, and
  per-actor cel parameters.
- **Reuses the existing scene light.** No new lighting authoring is added; the body's bands
  follow the existing `Light` (as the environment and props already do).
