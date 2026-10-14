# Feature Specification: Face Light Fades as the Actor Turns Away from the Sun

**Feature Branch**: `085-actor-face-light-fade`

**Created**: 2026-10-14

**Status**: Draft

**Input**: User description: "Improve the character renderer. The face light is fixed in front of the face so faces keep consistent shadows, but it looks weird that a face stays fully illuminated when the actor faces away from the light source. Can the lit band's brightness be lerped to the shadow band's as the actor's heading turns away from the light — full brightness when the face points at the light, fully lerped to the shadow band when it points the same way the light travels?"

## Origin

`specs/040` gives the actor's `Face` mesh an **idealized front light**: the band *direction*
comes from a fixed light in front of the actor (its horizontal facing, elevated slightly)
instead of the scene sun, so the face keeps a clean, constant two-tone split as the actor turns
(`specs/040` FR-007). Only the band *direction* is idealized; the band *colours* still come
from the scene sun's colour and the ambient term (`specs/040` FR-008).

That is what produces the artefact this feature removes. Because the face's *direction* never
changes and its *brightness* only ever follows the ambient and sun **colour**, a face reads as
fully lit even when the sun is behind the actor and every other surface — the body, the
environment, the props — has fallen into shadow. The front light fixed the turning head ("the
face must not go dark or ragged"), but it over-corrected: it also throws away the signal that
the face has turned out of the light. The result is a face that looks pasted on: its shading is
constant, and its brightness does not agree with the body it sits on.

The intended behaviour is a middle ground. The band *direction* stays idealized — the split
stays clean and fixed relative to the face — but the *amount* of the lit band now follows where
the face is pointing relative to the sun. Facing the sun, nothing changes from `specs/040`:
the full front light. Facing directly away, the lit band drops to the shadow band, so the whole
face sits in the ambient-only shade that the body beside it already shows. The face therefore
reads as a **two-tone switch** — the crisp lit/shade split the rest of the character has — rather
than a gradual gradient: which side of the light it is on decides it, with only a small smoothstep
through the 90° crossing so the switch does not strobe.

This is a **port extension**, not a behaviour of the `odin-game` reference: the reference keeps
the face at full brightness regardless of heading. The change reuses everything
`specs/040` already provides — the `Face` identification, the facing derivation, the scene
`Light` (whose direction is *toward* the sun) and the lean cel uniform — so no new asset data,
no new uniform field and no authoring surface are introduced.

## Clarifications

### Session 2026-10-14

- Q: Should the face be brightest when it points toward the scene sun or when it points away from it? → A: Toward the sun — the fade is brightest when the actor's facing is aligned with the scene light's direction (toward the sun) and darkest when anti-aligned (facing away). The sign follows the actor's own shading: bright in sunlight, dark in shade.
- Q: Should the fade compare the face's heading against the sun in azimuth only, or in full three dimensions? → A: Azimuth only — drop the vertical component of both the facing and the sun direction, so the fade's bright-to-dark swing is the same at any sun elevation; a sun with no horizontal direction (straight overhead) falls back to the full front light.
- Q: Should the lit band fade gradually across the heading, or switch? → A: Switch — a two-tone step at the ~90° crossing (full front light on the toward side, the shadow band on the away side), with a small smoothstep so it does not strobe. A gradual lerp read as a soft gradient against the crisp two-tone of the body and environment.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The face dims as the actor turns away from the sun (Priority: P1)

As the director turns an actor, the character's face should respond to where the sun is: bright
and front-lit when the actor faces the sun, and falling into shade as the actor turns its face
away from it, so the face's brightness agrees with the body and the scene around it.

**Why this priority**: this is the whole feature. Without it the face is a permanent full-bright
sticker; with it the character reads as lit by the scene it stands in.

**Independent Test**: place an actor under a fixed sun that is offset from vertical, turn the
actor through a full circle, and confirm the face's brightness peaks when the actor faces the
sun and falls to the ambient-only shade when it faces directly away, changing smoothly and
monotonically in between.

**Acceptance Scenarios**:

1. **Given** an actor whose face points at the scene sun, **When** the actor is turned so its
   face points directly away from the sun, **Then** the face switches at the ~90° crossing — its
   lit band drops to the shadow band's brightness and the whole face reads at the ambient-only
   shade.
2. **Given** the actor's face is turned to point at neither toward nor away from the sun (a
   quarter turn off), **Then** the face sits at the switch's midpoint — part way between the full
   front light and the shadow band.
3. **Given** the actor's face pointing at the sun, **Then** the face's brightness is unchanged
   from `specs/040` (the full front light).
4. **Given** the actor is turned back toward the sun after facing away, **Then** the lit band
   switches back to the full front light.

---

### User Story 2 - The split stays clean and nothing else changes (Priority: P2)

Turning the actor must not move, smear or ragged the face's band boundary, must not change the
face's band *colours* (they still take the scene's sun colour and ambient), and must leave every
mesh that is not the face — the body, environments, props, portraits, the UI and the editor —
rendering exactly as it does today.

**Why this priority**: it is the guard that keeps the feature a brightness change rather than a
second, competing shading model. It ranks below the fade because the fade is the value; this
is what makes the fade safe to ship.

**Independent Test**: sweep the scene sun around a stationary actor and turn the actor under a
fixed sun; confirm the face's band boundary does not move relative to the face, the face still
takes the scene's colour/ambient, and a frame with no actor in view is visually unchanged.

**Acceptance Scenarios**:

1. **Given** a face mesh, **When** the scene sun's direction is swept, **Then** the face's band
   boundary stays fixed relative to the face (as in `specs/040`), while its brightness follows
   the sun's azimuth.
2. **Given** the scene sun's colour or ambient changes, **Then** the face's tones still take
   that colour/ambient (`specs/040` FR-008).
3. **Given** a non-face mesh, an environment, a prop, a portrait, the UI or the editor,
   **Then** it renders exactly as it does today.

---

### Edge Cases

- **A scene sun at the zenith** (no horizontal direction to compare): the face keeps its full
  front light rather than dividing by zero or snapping to an arbitrary azimuth.
- **An actor whose facing is degenerate** (no horizontal component): likewise, the face keeps
  its full front light.
- **An actor asset with no mesh named `Face`**: nothing is modulated; the feature never applies
  and nothing fails (as `specs/040`).
- **A face pointing exactly at, or exactly away from, the sun**: the fade saturates cleanly to
  full front light or to the shadow band respectively, with no flicker at the extreme.
- **The ambient term is non-zero**: even at the darkest extreme the face sits at the shadow
  band's ambient-only tone and never falls to pure black (`specs/040` FR-003).
- **A non-face actor mesh, or several `Face` meshes**: unaffected / all modulated alike, as
  `specs/040` already treats them.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A `Face` mesh MUST keep its idealized band *direction* (`specs/040` FR-007): the
  band boundary stays fixed relative to the face whatever the actor's heading and whatever the
  scene sun is doing.
- **FR-002**: A `Face` mesh's lit band MUST switch with the sun's azimuth relative to the face:
  the full front light while the face points more toward the sun than away from it, and the
  shadow band's (ambient-only) brightness once it points more away than toward. The switch MUST
  be a two-tone step at the ~90° crossing (the face is not gradually lerped), eased by a small
  smoothstep so it does not strobe (FR-006).
- **FR-003**: The modulation MUST be computed from the horizontal (azimuthal) components of the
  facing and the sun's direction only, so the sun's elevation does not bias it. If either has no
  horizontal component, the face MUST keep its full front light (no division by zero).
- **FR-004**: Only the lit band's *amount* changes. The band *colours* MUST still come from the
  scene sun's colour and the ambient term, and the additive composition
  `albedo * (ambient + sunColour * band)` MUST be unchanged (`specs/040` FR-003, FR-008).
- **FR-005**: The fade MUST apply only to `Face` meshes. Every other mesh — and the environment,
  prop, portrait, UI and editor paths — MUST be unchanged (`specs/040` FR-009, FR-013).
- **FR-006**: The fade MUST be a fixed constant this phase; there MUST be no authoring surface
  for its strength or shape (`specs/040` FR-012).
- **FR-007**: The actor draw path MUST NOT introduce per-frame allocations; the change MUST reuse
  the existing cel uniform rather than grow it (`specs/040` FR-011).
- **FR-008**: `dotnet build` MUST be clean and `dotnet test` MUST pass, with the existing suite
  unmodified.

### Key Entities *(include if feature involves data)*

- **Face mesh**: the actor mesh whose authored name is `Face` (`specs/040`); the only thing the
  fade applies to. Its band direction stays idealized; only its lit-band amount is modulated.
- **Actor facing**: the actor's horizontal heading (its local forward under the model), already
  derived for the idealized face light (`specs/040`); the input the fade compares against the sun.
- **Scene light**: the existing `Light` whose direction points *toward* the sun (`specs/040`,
  `specs/043`); the reference the fade measures the facing against. No new field is added.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With a fixed, off-vertical sun and the actor turned through a full circle, the
  face's band boundary stays fixed relative to the face (100% of sampled headings) while its
  lit-band brightness passes through full front light when facing the sun, an intermediate tone
  at a quarter turn, and the shadow band's brightness when facing directly away (100% of the
  sampled headings, monotonic between the extremes).
- **SC-002**: At its darkest the face equals the shadow band (albedo × ambient) and never falls
  below it, for every sampled heading (0 faces below ambient).
- **SC-003**: The face's band colours and boundary, and every non-face mesh, environment, prop,
  portrait, UI and editor surface, are unchanged from `specs/040` for a fixed scene (0 unintended
  visual differences, confirmed by the walkthrough — the repo has no headless or pixel-diff harness
  for GPU paths).
- **SC-004**: `dotnet build` is clean and `dotnet test` passes, with the existing rendering,
  gameplay and editor assertions unmodified.

## Assumptions

- **The face light is idealized in direction only, as before.** `specs/040` FR-007/FR-008 stand;
  this feature layers a brightness response on top, and does not touch the band direction, the
  ramp, the alpha test or the additive composition.
- **The fade is a port extension.** The `odin-game` reference keeps the face at full brightness
  regardless of heading; this feature deliberately departs from it to remove the "fully lit while
  in shadow" artefact. There is no reference behaviour to match, so the fade's shape is a free
  choice. It is implemented as a two-tone switch at the 90° crossing — the full front light on the
  toward side, the shadow band on the away side — with a small smoothstep through the crossing;
  retuning that softness is a shader edit.
- **The sun's direction points toward the sun.** The scene `Light.Direction` is documented as
  "toward the sun" (`specs/043`), so the fade is brightest when the actor's facing is *aligned*
  with that direction and darkest when it is *anti-aligned*. The user's own phrasing ("facing
  the light" / "facing the way the light travels") reads the same way once the light's travel
  direction — the opposite of `Light.Direction` — is accounted for; the implementation follows
  the physical, "facing the light = bright" sense.
- **No new data, uniform or parameter.** The `Face` flag, the facing derivation and the scene
  light all already exist; the change is confined to how the face's existing inputs are combined.
- **Non-goals (matching `specs/040`):** shadow-map receiving, outlines, rim light,
  specular/matcap, ramp textures and per-actor cel parameters.
