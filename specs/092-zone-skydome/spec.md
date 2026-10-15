# Feature Specification: Zone Skydome

**Feature Branch**: `092-zone-skydome`

**Created**: 2026-10-15

**Status**: Draft

**Input**: User description: "Bring in the zone-level skysphere used in the neighbouring odin-game project. The sky sphere should be an authored property of a zone that gets saved into a scenario."

## Context

`odin-game` renders a **skydome** — a large full sphere drawn behind the scene, shaded by a
horizontal gradient (a horizon colour below the equator blending to a zenith colour above), procedural
drifting clouds, and a sun whose position follows the zone's authored light. It is a **zone-level**
setting: `enabled`, a zenith colour and a horizon colour live on the zone beside the light
(`odin-game/src/skydome.odin`, `src/zone_registry.odin`). In this port the zone's authored settings are
its **staging**, and a scenario carries its zones' staging (`specs/079-scenario-owned-zones`), so making
the sky an authored zone property makes it ride with a scenario exactly as the light of
`specs/043-zone-light-settings` does. The port has **no** sky today: a zone's scene clears to a fixed
colour and nothing is drawn behind the geometry. This specification ports the dome's **gradient and a
simple sun**; `odin-game`'s procedural clouds (and the time-driven animation they need) are **deliberately
deferred** (see Clarifications).

## Clarifications

### Session 2026-10-15

- Q: Should a zone show a sky by default? → A: **Off by default** — nothing is drawn until the author
  enables it, so an unedited zone is visually unchanged (the promise spec 043 made for the light).
- Q: How faithfully should the sky be ported? → A: **Gradient + a simple sun**, no clouds and no animation.
  The drifting-cloud layer and its time uniform are deferred; the sun is a single soft disc, not the
  reference's layered rings and glare.
- Q: What does "saved into a scenario" mean here? → A: **Scenario staging only.** The sky is part of a
  zone's staging, so a registered scenario carries it and a fork restores it; on-disk `project.json`
  persistence is out of scope (roadmap phase 15).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A zone draws an authored sky behind its scene (Priority: P1)

An author stands in a zone and gives it a sky: they turn it on and choose a zenith colour and a horizon
colour. The scene is then drawn against that sky — a smooth gradient from horizon to zenith, with a simple
sun whose position matches the light the author already set — instead of the flat clear colour. Turning it
off returns the frame to the flat clear colour.

**Why this priority**: it is the feature's reason to exist and the smallest complete slice — a zone that
shows an authored sky. Everything else (persistence, the editor controls) is how the value is set and
carried.

**Independent Test**: build a zone, turn its sky on with distinct zenith and horizon colours, and assert
the sky renders behind the scene with those colours; turn it off and assert the frame is the flat clear
colour again. The value itself (`Zone.skydome`) can be asserted with no window.

**Acceptance Scenarios**:

1. **Given** a zone whose sky is off, **When** the scene is drawn, **Then** nothing is drawn behind the
   geometry and the frame clears to the same colour as before this feature.
2. **Given** a zone whose sky is on, **When** the scene is drawn, **Then** a full sphere is drawn behind
   every piece of geometry, showing a gradient from the horizon colour (at and below the horizon) to the
   zenith colour (overhead), with a sun.
3. **Given** a zone whose sky is on and a light set to a given pitch and yaw, **When** the sky is drawn,
   **Then** the sun appears where that light points.
4. **Given** a zone whose sky is on, **When** the camera moves or turns, **Then** the sky stays centred on
   the camera (it never recedes or clips), so it reads as infinitely distant.

---

### User Story 2 - The sky belongs to the zone and rides with a scenario (Priority: P2)

An author authors a zone's sky, registers the scene as a scenario, then later forks that scenario into a
world without the zone. The zone comes back with the sky it was authored under — the same colours and the
same on/off state — because the sky is part of the zone's staging. A zone restaged by a fork takes the
scenario's sky, as it already takes its environment and light.

**Why this priority**: it is what the user asked for — the sky **saved into a scenario** — and it is what
makes the sky an authored property rather than a transient view setting.

**Independent Test**: set a zone's sky, register it as a scenario, fork into a fresh world, and assert the
resulting zone carries the authored sky; restage a present zone and assert its sky becomes the scenario's.

**Acceptance Scenarios**:

1. **Given** a zone with an authored sky, **When** the scene is registered as a scenario and later forked
   into a world without that zone, **Then** the created zone's sky equals the authored one.
2. **Given** a present zone and a scenario naming it with a different sky, **When** the scenario is forked,
   **Then** the zone's sky becomes the scenario's and the zone is not duplicated.
3. **Given** a zone that has no environment, **When** it is authored with a sky and round-tripped through
   register and fork, **Then** the sky survives (a sky does not depend on an environment being loaded).

---

### User Story 3 - Editing the sky is reversible from the Inspector (Priority: P3)

An author selects nothing (so the Inspector shows the active zone's zone-level settings) and edits the
sky: an on/off checkbox and two colour pickers for the zenith and horizon. Each committed edit is one step
on the one undo history, so tuning the sky is reversible like the light beside it.

**Why this priority**: it is the surface through which the setting is authored in the editor, and the
reversibility guarantee the rest of the editor already keeps. The value is complete and correct without
it, but the feature is not usable from the editor without it.

**Independent Test**: with nothing selected, toggle the sky and change each colour, and assert each is one
undo step that round-trips; assert a commit that changes nothing records nothing.

**Acceptance Scenarios**:

1. **Given** the Inspector with no entity selected, **When** the author toggles the sky or commits a colour
   change, **Then** the active zone's sky changes and exactly one undo step is recorded.
2. **Given** a sky step on the history, **When** it is undone and redone, **Then** the zone's sky returns to
   exactly its previous and then its changed value.
3. **Given** a control the author drags but returns to its starting value, **When** it is released, **Then**
   nothing is recorded (a no-op commit is not a step).

---

### Edge Cases

- **The sky is off**: nothing is drawn and the frame clears to the flat colour, exactly as before this
  feature — a zone that is never authored is visually unchanged.
- **A zone with no environment**: still has a sky; the sky is drawn regardless of whether an environment is
  loaded or resident.
- **The camera is inside a large environment**: the sky is drawn behind the geometry (depth-less), so
  geometry occludes it and it never occludes geometry, regardless of the camera's position.
- **The sun behind the horizon** (light pitch at or below the horizon): the sun is not drawn; the gradient
  remains.
- **The zone is inactive**: an inactive zone's sky is not drawn (the active zone's is).
- **The sky is on but the scene has no geometry**: the frame is the sky alone.
- **An edit made in one zone while another is active**: the step names its zone, so it acts on its own zone
  (the same rule every zone-level step already keeps).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A zone MUST carry, as an authored setting, a **skydome**: an on/off flag, a **zenith colour**
  and a **horizon colour**. The colours MUST be authored in the same colour space the existing light colour
  is (a linear RGB triple as the editor's picker produces), and the flag MUST default to **off** so a zone
  that is never authored renders exactly as it does today.
- **FR-002**: When a zone's sky is **on**, the scene MUST be drawn against it: a full sphere centred on the
  camera showing a horizontal gradient — the horizon colour at and below the horizon, blending to the zenith
  colour overhead — with a simple sun disc.
- **FR-003**: The sphere MUST be drawn **behind** the scene: it MUST NOT occlude any geometry, MUST NOT be
  occluded by it (no depth test or write), and MUST NOT be culled, so it reads as infinitely distant and
  never clips as the camera moves.
- **FR-004**: The sun's direction MUST follow the zone's authored **light** (spec 043) — the same pitch and
  yaw that light the scene — and MUST NOT be drawn when it is at or below the horizon.
- **FR-005**: When a zone's sky is **off**, nothing MUST be drawn behind the scene and the frame MUST clear
  exactly as it did before this feature (FR-009 of spec 043's precedent: an unedited zone is unchanged).
- **FR-006**: The sky MUST be a zone's setting, not a global one: the **active** zone's sky is the one
  drawn, and an inactive zone's is not.
- **FR-007**: A zone's sky MUST be part of the zone's **staging**, so registering a scenario captures it and
  forking that scenario restores it: a created zone takes the scenario's sky, and a present zone restaged by
  a fork **replaces** its sky with the scenario's. This is the "saved into a scenario" requirement.
- **FR-008**: An **environment-less** zone MUST be able to carry a sky, so the sky round-trips through
  register and fork whether or not the zone has an environment.
- **FR-009**: The Inspector MUST expose the sky — an on/off control and a colour picker for each of the
  zenith and horizon colours — as part of the active zone's zone-level settings (the fallback shown when no
  entity is selected), beside the lighting of spec 043.
- **FR-010**: Every committed sky edit MUST be **one reversible step** on the one undo history, acting on
  the zone it names, and a commit that changes nothing MUST record nothing. A new command variant carries
  the before/after sky so a change is undoable like the light.
- **FR-011**: The tests MUST cover, headlessly: the sky as a value (defaults, off renders nothing), the
  staging round-trip (capture and fork restore the sky, environment-less included), the present-zone restage
  replacing the sky, and the editor step as a single reversible command that records nothing on a no-op.

### Key Entities

- **Skydome settings**: a zone's authored sky — an on/off flag, a zenith colour and a horizon colour. It
  sits beside the zone's light on the zone, is part of the zone's staging, and is captured with a scenario.
- **Zone staging**: a zone's environment (asset, placement, parts), its light, its placed props, and now
  its sky (`specs/079`).
- **Scenario**: the authored unit that carries each of its zones' staging, including the sky, so a fork
  brings a zone up with the sky it was authored under.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A zone authored with a sky draws that sky behind its scene; a zone left unauthored draws no
  sky and clears to exactly the colour it cleared to before this feature.
- **SC-002**: Registering a scene with an authored sky and forking it into a world without its zones brings
  each zone up with its authored sky (on/off and both colours), including a zone with no environment.
- **SC-003**: Forking a scenario naming a present zone replaces that zone's sky with the scenario's and does
  not duplicate the zone.
- **SC-004**: In the editor, each committed sky edit is a single undo step that round-trips exactly, and a
  no-op commit records no step.
- **SC-005**: The sky value, its staging round-trip and the editor step can be exercised and asserted with
  no window opened; the project builds with no warnings and the suite is green.

## Assumptions

- **The sky is an authored zone property, like the light.** It sits on the zone, is captured in the zone's
  staging, and rides with a scenario (`specs/079-scenario-owned-zones`). Project-file persistence
  (`project.json`, roadmap phase 15) is **not** in this feature's scope; "saved into a scenario" means the
  scenario carries it, which is the port's existing authored-unit model.
- **Off by default.** Unlike `odin-game` (whose default zone has the sky on), the port defaults the sky
  **off** so every zone that is never authored renders exactly as it does today — the same promise spec 043
  made for the light. The author turns it on explicitly.
- **The colours match `odin-game`'s zone fields.** The sky is exactly `enabled`, a zenith colour and a
  horizon colour — the three fields `odin-game` stores on a zone — and nothing more; no sun colour, cloud
  density or scale is authored (those stay shader constants, as they are a fixed look).
- **The look is a trimmed port.** The gradient and a simple sun disc are carried over from `odin-game`'s
  skydome shader, including the sun's follow-the-light behaviour. Its procedural drifting clouds and the
  layered sun rings/glare are **deferred**: no cloud layer, no animation and no time uniform in this feature.
- **The default colours are `odin-game`'s.** A zone's sky, though off by default, carries the reference's
  zenith and horizon colours (`89,140,217` and `140,179,217` sRGB, converted to the port's linear space) so
  that enabling it shows a sensible sky without the author first choosing colours.
- **The sun is the zone's light.** The sky does not introduce a second sun; it reads the pitch and yaw of
  the zone's authored light (spec 043).
- **Drawn last, depth-less.** The sky is drawn after the scene's geometry, with depth test and culling
  disabled, so it neither occludes nor is occluded — the `odin-game` approach.
