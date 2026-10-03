# Feature Specification: Inspector Environment Readout

**Feature Branch**: `051-inspector-environment-readout`

**Created**: 2026-10-03

**Status**: Implemented

**Input**: User description: "Clean up a minor UI inconsistency — the Outliner has a Zones section, and under it a label reading `Environment:` followed by the name of the environment asset. This does not belong in the Outliner; it should go in the Inspector panel for that zone, above the light settings."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A zone's settings are read in one panel (Priority: P1)

A developer working in the editor opens the Inspector with nothing selected to inspect
the active zone. They see everything that describes that zone in one place: the name of
its loaded environment, then its Lighting section beneath it. They no longer have to
look to the Outliner — which lists places and entities in the scene — for a zone-level
setting.

**Why this priority**: this is the whole feature. It removes a zone-level readout from a
panel whose subject is scene structure, and puts it with the other zone-level settings so
the two are read together.

**Independent Test**: load an environment into a zone, select nothing, and confirm the
Inspector shows `Environment: <name>` directly above the Lighting section while the
Outliner's Zones section shows no such line.

**Acceptance Scenarios**:

1. **Given** the active zone has an environment loaded and no entity is selected,
   **When** the developer looks at the Inspector, **Then** it shows `Environment: <name>`
   above the Lighting section.
2. **Given** the same state, **When** the developer looks at the Outliner, **Then** the
   Zones section lists the project's zones with no `Environment:` line between the Zones
   section and the entity sections.
3. **Given** an entity is selected, **When** the developer looks at the Inspector,
   **Then** the entity's properties are shown as today, with no zone environment readout
   (the zone's settings are the Inspector's fallback subject, shown only when nothing is
   selected).

---

### User Story 2 - The readout follows the active zone (Priority: P2)

When the developer switches to another zone, or instantiates a different environment into
the active zone, the readout names the environment now in use; a zone with no environment
reads as `(none)`.

**Why this priority**: the readout is only meaningful if it names the environment of the
zone currently being inspected. It is P2 because it is the existing behaviour of the
value, relocated, rather than new capability.

**Independent Test**: switch between a zone with an environment and a zone without one
and confirm the readout changes to match each; instantiate a different environment into a
zone and confirm the readout names the new one.

**Acceptance Scenarios**:

1. **Given** two zones, one with an environment loaded and one without, **When** the
   developer switches between them with nothing selected, **Then** the readout names the
   first zone's environment in one case and `(none)` in the other.
2. **Given** the active zone with no environment, **When** an environment is instantiated
   into it, **Then** the readout names that environment.
3. **Given** the active zone whose environment was unloaded for memory, **When** the
   developer looks at the Inspector, **Then** the readout still names the zone's
   environment (the readout names the zone's environment, not its residency).

---

### Edge Cases

- A zone with no environment loaded: the readout reads `(none)`.
- No entity selected and the active zone has no environment: the readout reads `(none)`,
  and the Lighting section still appears beneath it.
- An entity is selected: neither the environment readout nor the Lighting section appears
  — the Inspector shows the entity, as it does today.
- The readout is informational only: it offers no control to change or unload the
  environment. Assigning an environment remains the Asset Browser's or the Zone
  Registry's action.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Outliner's Zones section MUST NOT display the active zone's environment
  name (or any `Environment:` line) between the Zones section and the entity sections.
- **FR-002**: The Inspector MUST display the active zone's environment name, read-only, as
  `Environment: <name>`, when no entity is selected.
- **FR-003**: The environment readout MUST be shown above the zone's Lighting section.
- **FR-004**: The readout MUST name the environment currently loaded in the active zone,
  and read `(none)` when the active zone has no environment.
- **FR-005**: The readout MUST reflect the active zone without any manual refresh: it MUST
  update when the active zone changes and when an environment is instantiated into the
  active zone.
- **FR-006**: The readout MUST remain informational: it MUST NOT add a control to change
  or unload the environment, and MUST NOT introduce a new panel, dialog, or column.
- **FR-007**: When an entity is selected, the Inspector MUST show the entity as today and
  MUST NOT show the zone environment readout (the zone remains the Inspector's fallback
  subject).
- **FR-008**: The change MUST be display-only — it introduces no new state, no command,
  and no undo step, and it MUST NOT alter what the editor simulates or what the Outliner
  lists.

### Key Entities *(include if feature involves data)*

- **Zone environment**: the environment loaded into a zone, presented by name. Already
  exists on the zone; this feature adds no field and no storage, only a new place it is
  displayed.
- **Active zone**: the zone currently being edited; the Inspector's fallback subject when
  no entity is selected, which supplies the environment name the readout shows.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With no entity selected and an environment loaded, the environment's name is
  visible in the Inspector in 100% of cases, and is absent from the Outliner in 100% of
  cases.
- **SC-002**: The readout names the environment actually loaded in the active zone in 100%
  of checks, including immediately after a zone switch or an environment instantiation.
- **SC-003**: The readout adds no new panel, dialog, or column, and offers no new control;
  it is a read-only line (100% of cases).
- **SC-004**: With an entity selected, the Inspector's contents are unchanged from before
  this feature (100% of selections).

## Assumptions

- A zone holds at most one environment at a time (unchanged from the current model), so a
  single readout line suffices.
- The Inspector's existing fallback model — the active zone is its subject when no entity
  is selected — is the right home for a zone-level readout, so no second selection state
  is introduced.
- Consequence of that model, accepted: while an entity is selected the environment name is
  not shown. The Outliner listed it unconditionally before; the feature deliberately
  accepts losing that always-visible behaviour in exchange for keeping all zone settings
  together in one panel.
- Editing or assigning an environment is out of scope; this feature only relocates an
  existing read-only display.
- No persistence format, authored asset, or engine behaviour changes.
