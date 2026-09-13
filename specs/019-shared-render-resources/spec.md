# Feature Specification: Shared Render Resources

**Feature Branch**: `019-shared-render-resources`

**Created**: 2026-09-13

**Status**: Draft

**Input**: User description: "Move device-level render resources (the GPU context: mesh/static pipelines, swapchain and depth formats, depth target) out of GameplayState and into SharedState, so per-screen state models the screen's entities and view rather than hardware handles. Keep per-screen render buffers (debug lines, gizmos, the static-scene batch) on the screen's state. Screens read device resources from shared state instead of being handed copies at construction. Rendering behaviour must not change."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Screen state describes the screen, not the hardware (Priority: P1)

A developer opens a screen's state and reads only what that screen is about: the
entities it holds, its camera, its editor/play state, and the render buffers it
owns. There are no raw hardware handles mixed in, so the file's purpose is
immediately clear and a screen's state can be reasoned about (and changed)
without knowing how graphics resources are created.

**Why this priority**: this is the point of the change — screen state that leaks
device resources is confusing and invites the wrong ownership assumptions. It is
independently valuable even before the resources are given a single home.

**Independent Test**: inspect the screen's state type and confirm every field
describes the screen (entities, view, buffers, editor state); no field is a bare
device resource.

**Acceptance Scenarios**:

1. **Given** the gameplay screen's state type, **When** a developer reads it,
   **Then** its fields are the zone, the camera, the per-screen render buffers,
   and editor/play state — no pipelines or device handles.
2. **Given** the gameplay screen's state is constructed, **When** it is created,
   **Then** it does not receive hardware handles as constructor arguments.

---

### User Story 2 - Device resources have a single home (Priority: P2)

Graphics resources created from the device — the render pipelines, the swapchain
and depth formats, and the depth target — live in one shared place for the whole
app, created once and reachable by any screen that needs them. There is exactly
one owner; nothing is copied into a screen's state.

**Why this priority**: a single owner removes the current duplication (the same
pipeline also lives in the GPU context) and makes lifetime obvious — created
after the device, released before it.

**Independent Test**: locate the shared device-resource state, confirm each
resource appears exactly once, and confirm screens read it from there rather
than holding their own copies.

**Acceptance Scenarios**:

1. **Given** the app has started, **When** a screen needs a render pipeline,
   **Then** it reads it from shared state rather than from a field on the screen.
2. **Given** the shared device-resource state, **When** a developer searches for
   a pipeline, **Then** it is defined in exactly one place across the app.
3. **Given** a window resize, **When** the depth target must be recreated,
   **Then** the recreation goes through the single shared owner.

---

### User Story 3 - Nothing about rendering changes (Priority: P1)

The refactor is about where things live, not what they do. The same scene draws
with the same pipelines and the same draw counts, the app starts and shuts down
as before, and the tests are unaffected.

**Why this priority**: co-equal with the ownership goal — a "pure move" that
changes behaviour is a regression, and this refactor touches the render path
that was just optimised.

**Independent Test**: run the app, confirm the same scene and draw counts as
before, and confirm the existing test suite still passes.

**Acceptance Scenarios**:

1. **Given** the same scene as before the change, **When** it is rendered,
   **Then** the frame's draw counts and appearance match the pre-change build.
2. **Given** the app runs to completion, **When** it shuts down, **Then** device
   resources are released in the same order relative to the device as before.

---

### Edge Cases

- The depth target is recreated on resize; it must remain owned by the shared
  device-resource state, not re-created per screen.
- A screen with no 3D scene (the menu) must be unaffected — it neither needs nor
  receives the device resources.
- Device resources are created only after the device exists and released before
  the device is destroyed; the ordering must be unchanged.
- The per-screen render buffers (debug lines, gizmos, the static-scene batch)
  still need the device to create their own buffers — they get it from shared
  state rather than as a threaded argument.
- Compile order: shared state is defined before screen state, so screen state may
  read it; the reverse must not be introduced.
- No resource ends up owned by two places at once during or after the move.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Device-level render resources — the render pipelines, the
  swapchain and depth formats, and the depth target — MUST live in shared app
  state, created once, not on a per-screen state type.
- **FR-002**: Per-screen state MUST contain only that screen's entities, its
  view, its own render buffers, and its editor/play state.
- **FR-003**: Screens MUST obtain device-level resources from shared state, not
  from fields on the screen's own state and not as hardware handles passed into
  the screen's constructor.
- **FR-004**: Each device-level resource MUST have exactly one owner; no
  resource may be duplicated across shared and screen state.
- **FR-005**: Recreating the depth target on resize MUST go through the single
  shared owner.
- **FR-006**: Per-screen render buffers (debug lines, gizmos, the static-scene
  batch) MUST remain per-screen state and MUST still be able to create their own
  device buffers, obtaining the device from shared state.
- **FR-007**: Rendering behaviour MUST be unchanged: the same pipelines draw the
  same scene with the same draw counts and the same appearance.
- **FR-008**: Resource creation and release ordering relative to the device MUST
  be unchanged (created after the device, released before it).
- **FR-009**: The change MUST NOT alter the entity model, the physics model, or
  the editor command/undo model.
- **FR-010**: The refactor MUST NOT add per-frame work or allocations to the
  render path.

### Key Entities *(include if feature involves data)*

- **Shared app state**: the state that outlives screens — the device and its
  derived render resources, the asset manager, and the physics world.
- **Screen state**: the state for one screen — its entities, view, per-screen
  render buffers, and editor/play state.
- **Device-level render resources**: pipelines, swapchain format, depth format,
  and depth target, all created from the device.
- **Per-screen render buffer**: a GPU buffer owned by a screen and tied to its
  view (debug lines, gizmos, the static-scene instance batch).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The gameplay screen's state type contains zero device-level render
  resource fields (pipelines, formats, depth target).
- **SC-002**: Every device-level render resource is reachable from shared state,
  and each is defined in exactly one place (100%, no duplicates).
- **SC-003**: The same scene renders with identical draw counts and appearance to
  the pre-change build (no visual or performance regression).
- **SC-004**: The app starts and shuts down cleanly with no change in resource
  creation/release ordering, and `dotnet build` + the existing test suite pass.
- **SC-005**: Per-screen render buffers remain on the screen that owns them
  (100% of them), and none is moved to shared state.

## Assumptions

- The device is already owned by shared state (through the asset manager), so
  adding the device's derived resources there is consistent rather than novel.
- The GPU context that currently holds the pipelines is created in the app's run
  loop; the shared state will hold that context (or the equivalent resources)
  once the device exists.
- "Per-screen render buffer" is a deliberate category: buffers tied to a screen's
  view stay on that screen, even though they are GPU memory.
- The camera is view state, not hardware, and stays on the screen's state.
- No new feature is added; this is an ownership/location change only.
