# Feature Specification: Animated Character on Screen

**Feature Branch**: `009-animated-character`

**Created**: 2026-09-01

**Status**: Complete (retrospective)

**Input**: User description: "flesh out roadmap phase 3 — an animated actor (e.g.
Akane) visible and animating in the 3D viewport via the asset manifest"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A character mesh renders in the viewport (Priority: P1)

A developer spawns the Akane actor from the asset browser and sees the character
rendered as a real, three-dimensionally lit mesh in the viewport, replacing (or
adding to) the wireframe box placeholder. The model comes from the actor's IQM
file, referenced by its manifest.

**Why this priority**: it is the "hard plumbing" step the roadmap calls out —
SDL_gpu has no `LoadModel`/`DrawMesh` helper, so the mesh-draw path (vertex/index
buffers, one graphics pipeline, one shader) and the IQM binary parser must be
built from scratch. Nothing animates until a model is on screen; this is the
minimum viable result and de-risks everything phase 4 and 5 build on.

**Independent Test**: spawn the Akane actor; the viewport shows its geometry
(not a wireframe box), lit so its silhouette reads in 3D, positioned by its
entity transform under the editor camera.

**Acceptance Scenarios**:

1. **Given** the Akane asset has a valid IQM model referenced by its manifest,
   **When** the developer spawns that actor, **Then** its mesh is visible in the
   viewport at the spawned position.
2. **Given** a spawned actor, **When** the developer moves/rotates/scales it,
   **Then** the mesh follows the entity transform under the editor camera.
3. **Given** a spawned actor, **When** the developer orbits/zooms the camera,
   **Then** the mesh is correctly projected and depth-sorted (no self-draw order
   artifacts from missing depth testing).
4. **Given** the actor's IQM file is missing or malformed, **When** it is loaded,
   **Then** the failure is reported loudly (no silent empty mesh).

---

### User Story 2 - The character is skinned by its skeleton (Priority: P2)

The character is not a rigid blob: its vertices are deformed by its bone
hierarchy, so the mesh renders posed by its skeleton rather than in raw
bind-pose positions. This proves the joint/bind-pose/skinning math before any
time-varying animation is layered on.

**Why this priority**: skinning is the bridge between the parsed IQM data and a
correctly-posed mesh. It depends on the parser and mesh path from US1 and is the
prerequisite for animation (US3). It is independently testable by posing the
model with a single sampled animation frame.

**Independent Test**: render the actor posed by a single (non-time-varying)
sampled pose; the character's limbs/silhouette match the authored model rather
than a T-pose or degenerate vertex cloud, and the mesh deforms coherently as the
sample frame changes.

**Acceptance Scenarios**:

1. **Given** a skinned actor, **When** it renders, **Then** vertices follow the
   bone transforms (no vertices stuck at the origin or bind-pose degeneracy).
2. **Given** a skinned actor, **When** the sampled pose changes (different frame),
   **Then** the mesh re-poses accordingly without re-parsing the file.
3. **Given** a model whose joint count exceeds the fixed pose buffer, **When** it
   loads, **Then** the failure is reported loudly.

---

### User Story 3 - The character animates (Priority: P3)

The character plays a looping animation (its idle clip) over time: the
animation clock advances each frame, poses are sampled and interpolated between
frames, the mesh is re-skinned, and the result is visible as movement in the
viewport.

**Why this priority**: this is the phase's headline result — an *animated*
actor. It composes the parser (US1) and skinning (US2) with a per-frame
advance/sample/re-upload loop, and is the last incremental slice that delivers
the roadmap's "done when" state.

**Independent Test**: spawn the Akane actor and observe it loop its idle
animation continuously (no per-frame stutter from re-parsing; positions change
over time).

**Acceptance Scenarios**:

1. **Given** a spawned actor with a looping idle animation, **When** the
   viewport runs, **Then** the character visibly animates and the animation
   loops seamlessly.
2. **Given** a spawned actor, **When** it animates, **Then** the same model data
   is shared and only per-actor animation state (frame/time) differs between
   multiple spawned instances of the same asset.
3. **Given** an animation that reaches its end and is non-looping, **When** it
   advances past the last frame, **Then** it clamps to the final frame (no
   out-of-range sampling).
4. **Given** an actor with no animation clips, **When** it renders, **Then** it
   draws in its bind/sampled pose without crashing.

---

### Edge Cases

- Missing IQM file, wrong file extension, or unreadable file.
- IQM file with an unexpected magic string or unsupported version.
- IQM file with a missing POSITION vertex array, or with no meshes/triangles.
- IQM vertex array of an unsupported kind (e.g. COLOR) or format.
- A joint referenced by a vertex's blend indices that is out of range.
- An animation with zero frames, or a pose channel index beyond the frame data.
- A non-looping animation advancing past its final frame.
- Multiple actors sharing one asset/model — per-actor animation state must not
  cross-contaminate.
- An actor entity whose asset id no longer resolves to a loaded model.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST load an IQM v2 file and expose its geometry
  (positions, normals, UVs, triangles) and its skeleton/animation data (joints,
  poses, clips, frame channels).
- **FR-002**: The system MUST render a loaded model in the 3D viewport through a
  GPU mesh pipeline (vertex buffer + index buffer + one graphics pipeline + one
  shader), with depth testing so geometry occludes correctly.
- **FR-003**: The system MUST skin the model by its bone hierarchy (bind pose
  and sampled animation poses), deforming vertices by their blend weights.
- **FR-004**: The system MUST advance a named animation over time and loop it
  when the clip is looping, sampling and interpolating between frames.
- **FR-005**: An actor entity MUST be associated with its asset/model via the
  asset manifest, so spawning an actor renders the correct model.
- **FR-006**: The rendered actor MUST respect the entity's transform (position/
  rotation/scale) and be drawn through the editor camera.
- **FR-007**: The existing debug grid and editor markers/gizmos MUST continue to
  render in the same viewport alongside the mesh.
- **FR-008**: The system MUST fail loudly (exception/error, not a silent
  default) on a malformed or unsupported IQM file.

### Key Entities *(include if feature involves data)*

- **IQM model**: parsed binary data — meshes, vertex arrays (positions/normals/
  UVs/blend indices/blend weights), triangles, joints (bind pose), poses
  (channel masks/offsets/scales), animation clips, and frame channels.
- **Actor asset**: a manifest entry (id, display name, model path) plus its
  eagerly-loaded runtime data — the parsed IQM model and the uploaded GPU mesh.
  Animation clip names come from the model's own animation list, not the
  manifest.
- **Actor render state**: the per-instance renderer owned by a spawned actor —
  its shared asset reference, per-actor animation slot (current clip, frame,
  playback rate, loop, weight), and the sampled bone-palette buffers.
- **Skinned pose**: the per-frame bone transforms (local → world → skinning
  matrices) derived from a sampled animation clip.
- **Mesh**: the GPU vertex/index buffers holding a (re-skinned) model ready to
  draw.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The Akane actor is visible in the viewport as a lit 3D mesh, not a
  wireframe box.
- **SC-002**: The character's idle animation loops continuously and is clearly
  visible as motion at 60 fps.
- **SC-003**: Every malformed or unsupported IQM input fails loudly with a clear
  message (no crash-free silent failures).
- **SC-004**: The reference grid, entity markers, and transform gizmos still
  render correctly in the same viewport alongside the mesh.

## Assumptions

- Phase 3 renders the character untextured with a placeholder directional
  (Lambert) shader; textures and metallic-roughness (PBR) materials arrive in
  phase 5.
- Morph targets (facial morphs via `morphs.json`/`morphs.bin` sidecars) are out
  of scope for phase 3; only skeletal animation is needed to show an animated
  character. Morph parsing/application is deferred to a later phase.
- A single looping animation (the actor's `Idle` clip) is sufficient to satisfy
  this phase; the `run`/locomotion clip is consumed later (phase 7). If no clip
  is named `Idle`, the first clip is used; if the model has no clips, the actor
  renders in its bind pose.
- The Akane IQM model file lives under `assets/actors/Akane.iqm`, referenced by
  `assets/actors/akane.manifest`.
- The editor is the only consumer this phase: the character renders in the
  gameplay/editor viewport under the existing orbit camera; play-mode locomotion
  and camera follow arrive in phase 7.
- Skin weights are normalized per vertex by the exporter (standard IQM); no
  runtime re-normalization is required.
