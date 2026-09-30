# Feature Specification: Zone Light Settings

**Feature Branch**: `043-zone-light-settings`

**Created**: 2026-09-30

**Status**: Draft

**Input**: User description: "add the ability to edit a zone's light settings in the editor"

**Origin**: `odin-game` is the template. A zone there owns its own lighting
(`src/zone_registry.odin`):

- `light_pitch` — degrees above the horizon (0 = horizon, 90 = zenith),
- `light_yaw` — compass degrees (0–360),
- `light_color` — an RGB colour,
- `ambient` — a single intensity,

defaulting to pitch `70`, yaw `45`, colour `{255, 248, 240}` and ambient `0.85` (`.odin:45`).
Every renderer derives the sun direction from the two angles with the same spherical
convention — `{sin(yaw)·cos(pitch), sin(pitch), cos(yaw)·cos(pitch)}` — and passes the colour
and ambient straight to its shader (`src/environment_render.odin:240`, `src/ocean.odin:284`,
`src/shadow.odin:131`). The same four fields are what a zone's *recorded* settings carry:
`Zone_Settings` holds them, `zone_settings_capture` copies them off a zone and
`zone_settings_apply` copies them back (`.odin:78`, `.odin:109`), so a registered place keeps
the light it was authored under. The editor exposes them as a "Lighting" block in the
properties panel — "Sun Pitch" on a 10–90 slider, "Sun Yaw" on a 0–360 slider, "Light Color"
through a colour picker and "Ambient" on a 0.1–1.5 slider — written straight into the live
zone (`src/editor_properties.odin:714`).

This port already has the one directional sun plus an ambient term those shaders consume
(`Texture.Light`, `render/Texture.fs`), shared by the static/environment shader and the actor
cel shader alike — but it is **hard-coded** at the single site that builds it
(`screen/GameplayScreen.fs`):

```fsharp
{ Direction = Vector3.Normalize(Vector3(0.4f, 0.8f, 0.4f))
  Color = Vector3(1.0f, 1.0f, 1.0f)
  Ambient = Vector3(0.15f, 0.15f, 0.15f) }
```

A zone has no light of its own (`world/Zone.fs`), the registered-zone record has no field for
one (`world/ZoneTemplate.fs`), and no editor panel mentions lighting. This feature gives a
zone its own light settings and the editor a surface to change them, so the look is authored
per zone rather than compiled in.

**Scope note**: shadows are still later scope (spec 040's cel-shading work left them out and
the phase-14 attempt is parked, `docs/SHADOWS_NOTES.md`). This feature changes the sun that
already lights the scene; it adds no shadow behaviour. The idealised face light of spec 040 is
reused unchanged.

## Clarifications

### Session 2026-09-30

- Q: Should the light a zone was authored under be recorded with the registered place (register captures it, instantiate restores it), or be a live-only property of the running zone? → A: Recorded — the registered-zone record carries the light and instantiating restores it in the same single undo step, matching `odin-game`'s `Zone_Settings`/`zone_settings_capture`/`zone_settings_apply` (US3 and FR-007 stand as specified).
- Q: Should a light change in the editor be an undoable step or a direct live write? → A: Undoable — a committed change is one step on the active zone's own undo stack, and undo restores the previous light. This departs from `odin-game` (whose sliders write the live zone) in favour of the port's reversible-edit rule; consecutive changes do not coalesce (revises FR-006 and the Assumptions).
- Q: Now that the sun is authorable, should the `Face` mesh keep spec 040's idealised body-relative band direction, or be lit by the authored sun like every other mesh? → A: Keep it idealised — moving the sun re-tints the face's bands but does not rotate them (FR-010 stands; spec 040's face branch is kept).
- Q: Where should the lighting controls live, and what does "select a zone" mean? → A: In the **Inspector**, shown for the active zone when no entity is selected (the Inspector's fallback subject); a zone is brought up by clicking its row in the Outliner's Zones list, which also makes it the active zone. The entity and zone subjects are mutually exclusive by construction (revises FR-003 and the Assumptions).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A zone's light is authored, not compiled in (Priority: P1)

A zone carries its own sun — pitch, yaw, colour and ambient — and the editor shows controls
for them. Changing a control changes the scene's lighting immediately.

**Why this priority**: this is the capability. Until a zone holds a light and the editor can
change it, nothing else about the feature (independence between zones, the recorded light, the
face light's behaviour) can be judged.

**Independent Test**: select the zone in the Outliner's Zones list, then change the sun pitch and
yaw and the colour and ambient in the Inspector, and confirm the rendered scene's shading changes
accordingly for both static geometry and actors.

**Acceptance Scenarios**:

1. **Given** a zone, **When** it is selected in the Outliner's Zones list — whether or not it is
   already the active zone — **Then** the Inspector shows that zone's current sun pitch, sun yaw,
   light colour and ambient, and the scene renders under them.
2. **Given** a lighting control, **When** the author changes it, **Then** the rendered scene's
   lighting changes, for the static environment, placed props and actors alike.
3. **Given** a scene whose light has not been edited, **When** it renders, **Then** the
   lighting is the same as the hard-coded light this feature replaces.
4. **Given** the zone selected, **When** the author selects an entity instead, **Then** the
   Inspector shows the entity and the lighting controls are gone (the two selections are exclusive).

---

### User Story 2 - The light belongs to the zone and follows it (Priority: P2)

Each zone has its own light. Switching zones shows that zone's light and renders under it, so
two zones may look entirely different without recompiling.

**Why this priority**: it is what makes the setting *a zone's* rather than a global. It ranks
below Story 1 because a single editable light is still the capability, and above Story 3
because it is the more common authoring need.

**Independent Test**: give two zones different lights, switch between them, and confirm both
the controls and the rendered lighting follow the active zone.

**Acceptance Scenarios**:

1. **Given** two zones with different light settings, **When** the author switches between
   them, **Then** the editor shows and the scene renders each zone's own light.
2. **Given** the light changed in one zone, **When** the author switches to another zone,
   **Then** that zone's light is unchanged.
3. **Given** a zone with no environment loaded, **When** its light is shown and changed,
   **Then** the controls still work and the change applies (the zone's light is not tied to an
   environment).

---

### User Story 3 - A registered place keeps the light it was authored under (Priority: P3)

A zone's light settings are part of what the Zone Registry records for a place, and
instantiating a place restores them — so a place is the room *and* the light it was lit under.

**Why this priority**: it is the faithful port of `odin-game`'s recorded settings
(`Zone_Settings`) and rounds out "a zone's settings", but the feature is fully usable without
it. It ranks last because Story 1 and Story 2 deliver the authoring capability on their own.

**Independent Test**: author a zone's light, register it against a place, change the light,
then instantiate that place and confirm the light is restored to the recorded one.

**Acceptance Scenarios**:

1. **Given** a zone with an authored light, **When** it is registered against a place, **Then**
   the registration records that light.
2. **Given** a place whose registration holds a light, **When** it is instantiated, **Then**
   the active zone's light becomes the recorded one, alongside the environment and props.
3. **Given** instantiating replaces the environment and props in one undo step, **When** it
   also sets the light, **Then** that is still one step, and undo restores the previous light
   together with the previous environment and props.

---

### Edge Cases

- **No environment loaded**: a zone's light still applies and is still editable; the sun lights
  whatever is in the scene (actors included).
- **Extreme angles**: pitch at the horizon and at the zenith must yield a valid unit sun
  direction with no degenerate result; yaw wraps through a full turn.
- **Ambient floor/ceiling and colour extremes**: a black light or a black ambient colour must be
  representable without the scene becoming invalid (the shading simply goes dark).
- **The `Face` mesh**: spec 040's idealised face light means the face's *band direction* ignores
  the scene sun, so moving the sun changes the face's band *colours* but never rotates its
  shading. The feature must not disturb that.
- **The outline (spec 041)**: unlit, so it is unaffected by the sun; it must stay unchanged.
- **Two zones**: their lights are independent — no shared state, no leaking between them.
- **A zone never edited**: it renders under the default light, identical to the pre-feature look.
- **Undo after a light change**: a committed change is one step on the active zone's own stack, so
  an undo restores the light as it was before it; undoing in one zone cannot alter another's light.
- **Play mode**: rendering follows the zone's light there too; there is no in-game lighting UI.
- **A registered place recorded before this feature exists**: out of scope — there is no
  persistence, so there are no records on disk to migrate.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each zone MUST carry its own light settings: a sun pitch (an angle above the
  horizon), a sun yaw (a compass angle), a light colour and an ambient intensity — the four
  values `odin-game` exposes.
- **FR-002**: The scene MUST render under the active zone's light settings. The single
  directional sun plus ambient the static/environment renderer and the actor cel renderer
  already consume MUST be derived from those settings, replacing the light that is hard-coded
  today, for static geometry, props and actors alike.
- **FR-003**: The editor MUST present controls to change a zone's light settings — sun pitch,
  sun yaw, light colour and ambient — in the **Inspector**, for the active zone. The Inspector shows
  the selected entity when there is one and the active zone's lighting when there is not, so an
  entity and a zone are never shown together. A zone is brought into the Inspector by selecting it
  in the Outliner's Zones list, which also makes it the active zone — the selected zone and the
  active zone are the same zone. A change MUST be reflected in the rendered scene without a reload.
- **FR-004**: The default light settings of a zone MUST reproduce the light in use before this
  feature (sun direction `normalize(0.4, 0.8, 0.4)`, white light, ambient `0.15`) so that an
  unedited scene renders unchanged — to float precision.
- **FR-005**: Each zone's light settings MUST be independent: switching zones MUST change the
  light in effect and the controls shown, and editing one zone's light MUST NOT alter any
  other zone's.
- **FR-006**: A zone's light settings MUST be session state, never written to disk. A committed
  change in the editor MUST be recorded as **one** step on the active zone's own (per-zone) undo
  stack, with no coalescing of consecutive changes, so an undo restores the light as it was before
  that change — the same reversibility the instantiate that also sets the light already has
  (FR-007). A change is committed when its control is released or left, not once per frame while it
  is dragged.
- **FR-007**: The registered-zone record MUST carry a zone's light settings: registering a place
  MUST capture the active zone's light, and instantiating a place MUST restore the recorded
  light. Instantiating MUST remain a single undo step that also restores the previous light.
- **FR-008**: The sun direction MUST be derived from pitch and yaw with the same spherical
  convention `odin-game` uses (0 = horizon, 90 = zenith; yaw 0–360) and MUST be a unit vector.
- **FR-009**: The lighting controls MUST be bounded — sun pitch 10–90°, sun yaw 0–360°, ambient
  0.1–1.5, and the colour to 0–1 per channel — so the light cannot be authored into a degenerate
  or out-of-range state.
- **FR-010**: The idealised face light of spec 040 MUST be unchanged: moving the sun MUST NOT
  rotate the `Face` mesh's cel band direction; only the scene's band colours follow the sun.
- **FR-011**: The cel shading (spec 040) and the actor outline (spec 041) MUST otherwise be
  unchanged by this feature.
- **FR-012**: Shadows MUST remain out of scope: no shadow behaviour is added, removed or
  changed.
- **FR-013**: Rendering MUST NOT gain per-frame allocations; the active zone's light is resolved
  once per frame.
- **FR-014**: All existing behaviour MUST be unchanged: environment, prop, actor, portrait, UI
  and editor rendering, gameplay and editor behaviour, and the existing test assertions.
- **FR-015**: `dotnet build` MUST be clean and `dotnet test` MUST pass, including new tests for
  the pure logic of the feature — the pitch/yaw-to-direction derivation above all.

### Key Entities *(include if feature involves data)*

- **Zone light settings**: the four authored values a zone owns — sun pitch, sun yaw, light
  colour, ambient — the thing this feature adds and the editor edits.
- **Zone** (`world/Zone.fs`): the live zone; gains the settings.
- **ZoneTemplate** (`world/ZoneTemplate.fs`): the registered-zone record — "the zone as the
  author left it" — which gains the settings so a place keeps its light.
- **`Light`** (`render/Texture.fs`): the shader-facing value (a direction *toward* the sun, a
  colour, an ambient term) the settings resolve to; unchanged in shape and still shared by the
  static and cel shaders.
- **Lighting controls**: the editor surface for the active zone's settings.
- **Zone Registry** (`world/ZoneRegistry.fs`): the register/instantiate path that carries the
  settings to and from a place.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Each of the four controls changes the rendered scene's lighting when changed
  (100% of the four), for static geometry and actors alike.
- **SC-002**: With no light edited, the scene renders identically to the pre-feature hard-coded
  light (0 observable difference).
- **SC-003**: Two zones hold independent light settings: switching between them switches the
  light in effect and shown, with 0 cross-contamination.
- **SC-004**: A place registered from a zone and then instantiated restores that zone's light
  (100% of the four values), in the same single undo step as the environment and props.
- **SC-005**: Moving the sun leaves the `Face` mesh's cel band direction unchanged (0 change),
  and the cel shading and the outline are otherwise unchanged.
- **SC-006**: No per-frame allocation is added (0 bytes/frame); `dotnet build` is clean and
  `dotnet test` passes with the existing assertions unmodified and new tests covering the
  pitch/yaw-to-direction derivation.
- **SC-007**: A committed light change is one undo step: a single undo restores the light as it was
  before that change (100% of the four controls), and two consecutive changes take two undos.

## Assumptions

- **The four settings are `odin-game`'s.** Sun pitch, sun yaw, light colour and ambient, and
  nothing else — no rim, specular, per-actor parameters or shadow fields; those remain the
  reference's later work and this phase's out-of-scope (FR-012).
- **Angles, not a raw direction.** The light is authored as a pitch and a yaw rather than a
  direction vector, matching `odin-game` and giving bounded, readable controls; the renderer
  derives the direction in one place (FR-008).
- **Ambient is a single intensity**, as in `odin-game`, expanded by the renderer into the
  ambient term the shaders consume. Its default is the port's existing neutral grey.
- **Light edits are undoable, one step per committed change.** A control commits when it is
  released or left (as the Inspector's fields do), so tuning the sun does not put a step on the
  stack per frame, and consecutive changes do not coalesce (FR-006). This is a deliberate departure
  from `odin-game`, whose sliders write straight into the live zone, in favour of the port's rule
  that an authored change is reversible. Undo is per zone, so a light change is undone in the zone
  it was made in.
- **The light belongs to the zone, not the environment.** A zone lights its scene whether or
  not an environment is loaded, and actors are lit by it (Story 2's third scenario).
- **No persistence.** The project has no save/load yet, so the settings are session state and
  there are no recorded places on disk to migrate (Edge Cases).
- **Play mode renders under the zone's light**; there is no in-game lighting UI this phase.
- **The zone is the Inspector's fallback subject.** The Inspector shows the selected entity when
  there is one, and the active zone's lighting when there is not; the two are exclusive by
  construction, with no second selection to keep in step. A zone is reached by clicking its row in
  the Outliner's Zones list, which also switches the active zone to it (and already drops the entity
  selection). Selecting a zone is not itself undoable — the light *changes* made for it are
  (FR-006).
- **Story 3 is the recorded light.** Q1 confirmed it is in scope; Stories 1 and 2 still stand
  alone as the capability if it were ever deferred.
