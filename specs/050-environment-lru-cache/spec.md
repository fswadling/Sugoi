# Feature Specification: Bounded environment residency

**Feature Branch**: `050-environment-lru-cache`

**Created**: 2026-10-02

**Status**: Implemented

**Input**: User description: "Only a subset of a project's zones should have their environment assets resident on the GPU at once. A large game holds many zones, and loading every zone's environment blows GPU memory (one environment here is ~1.7 GB of textures). Residency must be bounded by a memory budget: environments load on demand (when a zone is switched to, or an environment is instantiated) and, when the budget is exceeded, the least-recently-used environment is unloaded. Unloading frees GPU memory only — zone state, collision geometry, undo, and the registry are unaffected. Async/threaded loading and cue-driven preloading are out of scope for this feature."

## Clarifications

### Session 2026-10-02

- Q: When one environment is larger than the budget, load it anyway or refuse? → A: Load it anyway — the budget bounds what is *kept*; the single in-use environment may exceed it.
- Q: If loading an environment fails at switch/instantiation, what happens? → A: Cancel the action and surface the error — load first; on failure the switch/instantiation does not happen.
- Q: At startup, may model files be parsed (CPU) or only metadata read? → A: Metadata only — parsing and uploading are both deferred to first use.
- Q: If two different environment assets share a texture file, what happens on unload? → A: Unloading MUST NOT free a resource another resident environment still uses; free a shared resource only once no resident environment needs it.
- Q: How many environments must the bound hold for (validation target)? → A: 20.

### Session 2026-10-03

- Q: Should an eviction free only the GPU data, or the parsed geometry as well? → A: Free both — an environment's parsed geometry and its GPU data are one residency unit, and a reload re-parses the model. (This supersedes the "Unloading frees GPU memory only" phrasing in the Input.)
- Q: What does the budget bound — VRAM only, or RAM too? → A: The one budget bounds the whole loaded unit (parsed geometry plus GPU meshes and textures), so it caps RAM as well as VRAM.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A bounded environment footprint no matter how many zones exist (Priority: P1)

A developer builds a game with many zones, each with its own environment. They can
spawn or hold all of those zones at once, and the game keeps working: the total
memory used by environment geometry and textures stays under a configured budget,
independent of how many zones exist. When bringing a new environment in would exceed
the ceiling, the environment that has gone longest without being used is unloaded
first to make room.

**Why this priority**: this is the whole feature. Without a bounded residency, the
project's memory grows without limit as content is added, which is the problem
being solved.

**Independent Test**: place several environments that together far exceed the
ceiling, visit each in turn, and confirm the resident set never exceeds the ceiling
and the least-recently-used environment is the one dropped.

**Acceptance Scenarios**:

1. **Given** a ceiling smaller than the combined size of the environments in use,
   **When** the developer visits environments in turn, **Then** the resident set
   never exceeds the ceiling.
2. **Given** environments A and B resident and A used least recently, **When** a
   third environment is brought in and room is needed, **Then** A is unloaded before
   B.
3. **Given** the active zone's environment (the most recently used), **When** any
   other environment is brought in, **Then** the active environment is never the one
   unloaded.
4. **Given** a room already within the ceiling, **When** a small environment is
   brought in, **Then** nothing is unloaded.

---

### User Story 2 - Environments load on demand and render correctly (Priority: P1)

When a developer switches to a zone whose environment is not currently resident, or
instantiates an environment, it is brought onto the GPU at that moment, and the zone
renders and behaves exactly as if it had been loaded all along. A zone whose
environment was unloaded and later visited again loads again and looks identical.

**Why this priority**: without correct on-demand loading, the memory bound would be
achieved by simply not showing environments — which is not a game. Loading on switch
and on instantiation is what makes the bound invisible to the player.

**Independent Test**: switch to a zone, confirm its environment renders; switch away
far enough to evict it; switch back, confirm it renders again with no visible
difference.

**Acceptance Scenarios**:

1. **Given** a zone with an environment that is not resident, **When** the developer
   switches to it, **Then** its environment is loaded and renders that frame.
2. **Given** an environment already resident, **When** its zone is switched to,
   **Then** no reload occurs and it renders immediately.
3. **Given** an environment instantiated onto the active zone (from the asset
   browser or the registry), **When** it is instantiated, **Then** it is resident and
   renders.
4. **Given** an environment unloaded for memory, **When** its zone is visited again,
   **Then** it reloads and renders with no difference from before.

---

### User Story 3 - Unloading never changes what the game simulates (Priority: P2)

A zone's contents — its actors, props, waypoints, triggers, the environment's placed
parts, and their collision — are unaffected by whether its environment's GPU data is
resident. Physics, picking, the registry, and undo/redo all behave identically
whether or not an environment has been unloaded and reloaded.

**Why this priority**: eviction is a memory optimisation. If it changed simulation or
authoring state, it would corrupt the project. It is P2 because it is only observable
once the bound (US1) and on-demand loading (US2) exist.

**Independent Test**: with a low ceiling, alternate between zones and confirm prop
collision, picking, the registry's stored templates, and undo/redo return identical
results to a run with a ceiling large enough never to evict.

**Acceptance Scenarios**:

1. **Given** a zone whose environment was unloaded, **When** it becomes active and is
   simulated, **Then** its collision geometry and placed parts are exactly as before.
2. **Given** an eviction happened, **When** the developer looks at the registry, the
   part lists, or the outliner, **Then** each shows the same contents as before.
3. **Given** a zone deleted and undone, **When** the undo restores its environment,
   **Then** the environment is resident and renders, whether or not it had been
   unloaded in between.

---

### User Story 4 - Residency at a glance in the editor (Priority: P2)

While editing, a developer can see next to each zone in the outliner's zones list
whether that zone's environment is currently resident (loaded) — a minimal glyph,
not a new panel. It tells them which zones will switch in instantly and which will pay
a load when visited, and it updates on its own as zones are visited and environments
are unloaded, so it is always a truthful read-out.

**Why this priority**: the residency bound (US1) and on-demand loading (US2) change
what is resident over time; without a read-out, a developer cannot tell why a switch
was slow or which zones are resident. It is a small, self-contained editor affordance,
and it depends on US1/US2 existing to have anything to show.

**Independent Test**: with a budget small enough to force eviction, switch between
zones and watch the indicator beside each zone change between resident and
not-resident, matching which zones would render immediately.

**Acceptance Scenarios**:

1. **Given** a zones list, **When** the developer views it, **Then** each zone with an
   environment shows a minimal indicator of whether its environment is resident.
2. **Given** zone A resident and zone B not, **When** the developer switches to B,
   **Then** B's indicator reads resident and A's reads not-resident once evicted, with
   no manual refresh.
3. **Given** the active zone, **When** the developer views the list, **Then** its
   indicator reads resident (the active environment is never unloaded).
4. **Given** a zone with no environment, **When** the developer views the list,
   **Then** it is shown as having nothing to load, distinct from a zone whose
   environment is merely not resident.

---

### User Story 5 - Startup no longer reads or uploads every environment (Priority: P3)

Launching the game no longer reads or uploads every environment's geometry and
textures. The project's environments are still discovered and listed (names and
metadata are available everywhere they were before), but their model files are read
and their GPU data brought up only when first needed.

**Why this priority**: it is the same mechanism delivering a visible, independent
benefit — faster startup and lower memory before gameplay — but the memory bound
(US1) already delivers the core value.

**Independent Test**: launch with several environments present and confirm no
environment texture/geometry is uploaded before an environment is first used, while
the asset browser and registry still list every environment.

**Acceptance Scenarios**:

1. **Given** several environments on disk, **When** the application starts, **Then**
   none of their model files has been parsed and none of their GPU data has been
   uploaded.
2. **Given** the application has just started, **When** the developer opens the asset
   browser and the registry, **Then** every discovered environment is listed as
   before.

---

### Edge Cases

- A single environment larger than the budget: it still loads when used (the thing
  being used is never its own victim); the budget is a target, not a hard cap that
  prevents the active environment from loading.
- Two zones share the same environment asset: there is one residency entry, so visiting
  either touches it, both read the same indicator state, and it cannot be unloaded while
  either is active.
- An environment with no textures (geometry only): it costs almost nothing and rarely
  provokes an eviction.
- Switching to a zone with no environment: nothing is loaded or unloaded beyond the
  zone state; the viewport shows only the reference grid.
- Environment instantiated onto a non-active zone: no such path exists — every load is for
  the zone that is becoming (or already) active, so the asset being loaded *is* the active
  zone's environment and the active environment cannot be evicted as a side effect. If such a
  path is ever added, it must protect the active environment's id too.
- Repeatedly switching between two environments larger than the budget: each visit
  reloads the other (re-parse and re-upload); this works correctly though it pays the
  load cost each time.
- Shutdown with a mix of resident and unloaded environments: every resident
  environment's GPU resources are released exactly once.
- Loading an environment that fails to parse/upload (a missing or corrupt asset):
  the action is cancelled — the switch does not happen and the instantiation does
  not occur — and the failure is surfaced, so the project is never left half-changed
  (the active zone moved, or an environment replaced, without its geometry).
- Two *different* environments that reference the same texture file: unloading one
  leaves the shared texture resident while the other is resident, and both render
  correctly; the texture is freed only once neither needs it.
- An eviction drops an environment's parsed geometry as well as its GPU data; the
  zone's live colliders are unaffected (the physics world holds them), and a reload
  re-parses the model.
- A zone with no environment: its indicator reads as "nothing to load" rather than
  resident or not-resident, and it never provokes an eviction.
- A zone whose environment is resident but that is not active: it reads resident until
  an eviction for memory drops it.
- The indicator must not change the zone list's existing behaviour: clicking a zone to
  make it active, the active-zone marker, and the delete control all work as before.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The total memory held by environment assets — their parsed geometry and
  their GPU meshes and textures, as one loaded unit — MUST stay within a single
  configured budget (a byte ceiling), regardless of how many zones the project
  contains.
- **FR-002**: Activating a zone MUST ensure that zone's environment is resident: if it
  is not, it MUST be loaded at that moment; if it is, it MUST be used without
  reloading.
- **FR-003**: Instantiating an environment MUST ensure that environment is resident.
- **FR-004**: When making an environment resident would exceed the budget, the
  system MUST unload environments in least-recently-used order until the budget is
  met.
- **FR-005**: The environment currently in use (the active zone's) and any
  environment being loaded or instantiated MUST NOT be unloaded while it is in use,
  even if it alone exceeds the budget.
- **FR-006**: Unloading MUST free the environment's parsed geometry and its GPU data
  together — they are one residency unit. A zone's entities, placed parts, its live
  colliders, player designation, registry entries, and undo/redo history MUST be
  unaffected by whether its environment is resident: the colliders live in the physics
  world, and a reload re-parses the model.
- **FR-007**: Using an environment that was unloaded MUST reload it — re-parsing its
  model and re-uploading its data — so that it renders and behaves identically to
  before.
- **FR-008**: Environments backed by the same asset MUST share a single residency
  entry; their data MUST NOT be loaded more than once at a time.
- **FR-009**: The budget MUST be a single project-level constant (one knob), not a
  per-environment or per-zone setting.
- **FR-010**: Application startup MUST NOT upload any environment's GPU data and
  MUST NOT parse any environment's model file; discovery reads only lightweight
  manifest metadata. Every discovered environment MUST remain listed by name/metadata
  wherever environments are surfaced (asset browser, registry).
- **FR-011**: Rendering or simulating with an environment whose data is not resident
  MUST be treated as a defect and fail loudly, not read freed resources or silently
  draw nothing.
- **FR-012**: Shutdown MUST release every resident environment's parsed and GPU data
  exactly once, with no double-release and no leak, whether the environment is
  currently resident or not.
- **FR-013**: The editor MUST show, beside each listed zone, a minimal visual
  indicator (an icon/glyph) of whether that zone's environment is currently resident.
  It MUST be part of the existing zones list, not a new panel, dialog, or column.
- **FR-014**: The indicator MUST reflect current residency without any manual
  refresh: it MUST update when a zone is switched to, when an environment is
  instantiated, and when an environment is unloaded for memory.
- **FR-015**: A zone with no environment MUST be shown distinctly from a zone whose
  environment exists but is not resident.
- **FR-016**: While a zone is active, its indicator MUST read resident, consistent
  with the active environment never being unloaded.
- **FR-017**: If an environment cannot be loaded when it is required (a missing or
  corrupt asset), the switch or instantiation MUST be cancelled and the failure
  surfaced; the project MUST NOT be left half-changed — the active zone MUST NOT move
  and an environment MUST NOT be replaced without its geometry.
- **FR-018**: Unloading an environment MUST NOT free any GPU resource that another
  resident environment still uses; a resource shared between environments MUST be
  freed only once no resident environment needs it.

### Key Entities *(include if feature involves data)*

- **Environment asset**: a discoverable, named environment definition that is either
  fully loaded — its parsed geometry, parts and materials plus its GPU meshes and
  textures — or not loaded at all. Shared by every zone that uses it.
- **Zone environment**: the environment loaded into one zone, made of placed parts
  and their collision. Its existence is independent of whether the asset is resident.
- **Residency budget**: the single configured byte ceiling on an environment's loaded
  memory — its parsed geometry together with its GPU meshes and textures.
- **Residency set**: the environments currently resident (parsed geometry and GPU data
  together), ordered by recency of use; the least-recently-used is the eviction candidate.
- **Active zone**: the zone currently rendered and simulated; its environment is the
  most recently used and is exempt from eviction.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With at least 20 environments in the project (well beyond the budget), the
  memory held for environments (parsed geometry plus GPU data) never exceeds the
  budget, except for the single environment currently in use (100% of visits).
- **SC-002**: 100% of zone switches and environment instantiations show the correct
  environment geometry and textures in the same frame the zone becomes active.
- **SC-003**: Visiting an environment that was unloaded renders identically to its
  first visit (100% of revisits), including its collision and placed parts.
- **SC-004**: Physics, picking, the registry, and undo/redo produce identical results
  whether eviction occurs or not (100% of cases), for projects small enough to run
  both ways.
- **SC-005**: Startup performs zero environment GPU uploads and zero environment
  model-file parses, and the application reaches its first frame with the same
  environment metadata available as before.
- **SC-006**: Repeatedly visiting environments whose combined size exceeds the budget
  produces no growth in resident memory (parsed or GPU) and no leak at shutdown (steady
  state after the first cycle).
- **SC-007**: A developer can identify at a glance which listed zones are resident at
  any moment, and the indicator matches the actual resident set 100% of the time,
  including immediately after a switch or an eviction.
- **SC-008**: The residency indicator is a minimal glyph beside the zone name; it adds
  no new panel, dialog, or column to the editor.
- **SC-009**: A broken environment asset never leaves the project half-changed:
  100% of failed loads at a switch or instantiation are cancelled with the failure
  surfaced, and the previously active zone (or previous environment) remains in
  place.
- **SC-010**: Environments that share a texture file render correctly regardless of
  which of them is unloaded (100% of cases); no shared resource is freed while a
  resident environment still uses it.

## Assumptions

- Loading is synchronous: the first use of an unloaded environment may stall that
  frame. Asynchronous/threaded loading is out of scope for this feature.
- Cue-driven preloading (loading an environment ahead of a switch) is out of scope.
- Only the active zone is rendered at a time; there is no cross-zone crossfade that
  would require two environments resident simultaneously.
- A zone holds at most one environment at a time (unchanged from the current model).
- Actor assets and prop assets keep their current eager loading; only environment
  assets are brought under the budget in this feature.
- The budget is a single project-level constant chosen by the developer; a
  user-facing settings control is out of scope.
- Texture compression or resolution reduction (which would shrink the largest
  environment several-fold) is a possible future optimisation, not part of this
  feature.
- The editor residency indicator is a minimal glyph in the existing zones list; a
  dedicated residency panel, memory-usage meter, or per-zone byte read-out is out of
  scope.
- Startup reads only manifests/metadata for environments; model-file parsing and GPU
  upload are both deferred to an environment's first use, so launch cost does not grow
  with the number or size of environments.
- The existing behaviour that only the active zone is simulated continues to hold.
- The budget bounds a whole loaded environment — its parsed geometry together with its
  GPU meshes and textures — so it caps process RAM as well as VRAM. Parsed geometry is
  dropped on eviction, not retained.
- A reload re-parses the model as well as re-uploading it; measured here, an
  environment's glTF parse is ~7–20 ms and its retained parsed geometry ~2 MB, accepted
  in exchange for memory bounded by the budget.
