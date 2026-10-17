# Feature Specification: Actor and Prop Spawn Anchors

**Feature Branch**: `101-actor-prop-anchors`

**Created**: 2026-10-17

**Status**: Draft

**Input**: User description: "Next up — we need the same spawning options for actors and props. Can you sketch that out?" The sketch proposed a shared spawn-anchor selector in the Asset Browser with Origin, Camera, and Player options.

**Scope**: this spec extends the asset-placement surface defined by
`specs/033-cast-spawn` and `specs/010-environment-props`. It adds placement anchors
for actors and placed props; it does not change actor/prop assets, naming, cue
addressing, or environment loading.

## Clarifications

### Session 2026-10-17

- Q: Should the spawn-anchor control be shared by the Actors and Props sections, or should each section have its own? → A: One shared control for both sections; actors and props use the same placement mode.
- Q: When spawning an actor or prop at the camera, should it use the camera's exact position, or be placed on the floor below the camera? → A: The camera's exact position; the object may float and the author can move it.
- Q: When spawning at the camera, should the actor or prop copy the camera's full rotation, or only its horizontal view direction? → A: Horizontal yaw only, applied to both; camera pitch and roll are not copied.
- Q: Should spawned actors and props keep the asset's name, or receive automatic anchor-informed names? → A: Keep the asset's name; automatic anchor naming is out of scope.
- Q: When spawning at the player, should the actor or prop use her exact feet position and rotation, or be offset slightly to reduce overlap? → A: Exact feet position and authored rotation; overlap is allowed and movable.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Spawn an actor at the player or camera (Priority: P1)

A developer chooses an anchor in the Asset Browser, then clicks an actor asset. The
actor is created at the anchor's position and facing, selected, and ready to edit.
Origin remains the default, so the existing click-to-spawn behaviour is unchanged
until the developer chooses another anchor.

**Why this priority**: this is the requested capability for actors. It removes the
"spawn at the origin, then drag the actor into place" step from a common authoring
flow and lets the editor place a second actor at the player or camera directly.

**Independent Test**: select each anchor in turn, click the same actor asset, and
confirm the actor's position, facing, selection, undo step, and asset binding. Confirm
that choosing Origin reproduces the previous behaviour.

**Acceptance Scenarios**:

1. **Given** a live designated player and the Player anchor selected, **When** the
   developer clicks an actor asset, **Then** the actor is created at the player's
   authored feet position with the player's authored rotation, is selected, and
   retains its asset.
2. **Given** the Camera anchor selected, **When** the developer clicks an actor asset,
   **Then** the actor is created at the camera's position, facing the camera's
   horizontal view direction, upright, and is selected.
3. **Given** the Origin anchor selected, **When** the developer clicks an actor asset,
   **Then** the actor is created at the origin with identity rotation, exactly as
   before this feature.
4. **Given** no live designated player, **When** the developer opens the anchor
   selector, **Then** Player is unavailable and cannot be selected.
5. **Given** an actor spawned at an anchor, **When** the developer moves the player or
   camera afterwards, **Then** the actor does not move: the anchor supplies a
   one-shot placement, not an attachment.
6. **Given** an actor spawned at an anchor, **When** the developer undoes, **Then** the
   actor is removed, and redo restores it at the recorded anchor transform and asset.
7. **Given** the actor after spawning, **Then** animation, collision, show-collider,
   rename, duplicate, delete, cue addressing, and scenario capture behave as they do
   for an actor spawned at the origin.

---

### User Story 2 - Spawn a prop at the player or camera (Priority: P1)

A developer chooses an anchor in the Asset Browser, then clicks a prop asset. The prop
is created at the anchor's position and facing, selected, and ready to edit. Origin
remains the default and keeps the previous behaviour.

**Why this priority**: props need the same placement convenience as actors. A prop
spawned at the player or camera is usually the initial placement the author wants, and
the asset binding and collision shape are unchanged.

**Independent Test**: select each anchor, click the same prop asset, and confirm its
position, orientation, selection, undo step, asset binding, and collision behaviour.
Confirm Origin reproduces the previous behaviour.

**Acceptance Scenarios**:

1. **Given** a live designated player and the Player anchor selected, **When** the
   developer clicks a prop asset, **Then** the prop is created at the player's authored
   feet position with the player's authored rotation, is selected, and retains its
   asset and collision shape.
2. **Given** the Camera anchor selected, **When** the developer clicks a prop asset,
   **Then** the prop is created at the camera's position with the camera's horizontal
   facing, is not tilted, and is selected.
3. **Given** the Origin anchor selected, **When** the developer clicks a prop asset,
   **Then** the prop is created at the origin with identity rotation, exactly as
   before this feature.
4. **Given** a prop spawned at an anchor, **When** the developer moves the player or
   camera afterwards, **Then** the prop does not move.
5. **Given** a prop spawned at an anchor, **When** the developer undoes, **Then** the
   prop is removed, and redo restores it at the recorded transform and asset.
6. **Given** the prop after spawning, **Then** its collider, renderer, rename,
   duplicate, delete, and scenario capture behave as they do for a prop spawned at the
   origin.
7. **Given** the player or prop list is at capacity, **When** the developer tries to
   spawn, **Then** spawning is unavailable and no entity or history step is created.

---

### User Story 3 - One shared anchor selector for both asset lists (Priority: P2)

A single `Spawn at` control in the Asset Browser governs both the Actors and Props
sections. Adding a future anchor adds one item to that control; it does not add a
button to every asset row.

**Why this priority**: actors and props are long asset lists. Per-asset anchor menus
would turn every one-click spawn into two clicks and duplicate the menu on every row.
One shared selector keeps spawning fast and bounds the number of controls.

**Independent Test**: open the Asset Browser and confirm there is one anchor selector
applying to both sections; change it and confirm the next actor and prop spawns use
the same anchor; confirm that each asset row still has one spawn button.

**Acceptance Scenarios**:

1. **Given** the Asset Browser, **When** the developer looks for placement options,
   **Then** there is one anchor control shared by the Actors and Props sections, not a
   control per asset.
2. **Given** the selector, **Then** Origin is the default and the offered options are
   Origin, Camera, and Player.
3. **Given** the selector is set to an anchor, **When** the developer spawns an actor
   and then a prop, **Then** both use that anchor.
4. **Given** an anchor is added in future, **Then** it joins the selector as one item
   and adds no per-asset button.
5. **Given** the selected anchor is unavailable (no player), **When** the developer
   views the asset rows, **Then** spawning is disabled and creates nothing.
6. **Given** the developer changes the anchor, **Then** the change is transient editor
   UI state: it is not undoable and is not persisted.

---

### Edge Cases

- No live player is designated: Player is unavailable in the selector; if Player was
  selected before the designation disappeared, spawning is disabled until another
  anchor is chosen.
- The camera points exactly vertically: there is no horizontal view direction, so a
  Camera-anchored actor or prop uses identity yaw rather than an undefined facing.
- The camera is high above the floor: the actor or prop is created at the camera's
  position and may float; this is accepted, and the author can move it.
- The player is moved after spawning: the spawned actor/prop stays where it was
  created.
- Spawning at the player overlaps her, and spawning multiple entities at the same
  anchor is allowed; names and positions are not required to be unique.
- The selected kind is at capacity: spawning is disabled and records nothing.
- The player actor is scaled: the spawned actor/prop uses unit scale; the anchor
  supplies position and rotation only.
- The camera is inside geometry: creation still succeeds at the camera's position.
- Actor and prop names remain the asset names. Duplicate names are legal; actor cue
  addressing and existing name semantics are unchanged.
- Environments are unaffected: they are loaded, not spawned, and the anchor does not
  apply to them.
- Every spawn remains one step on the one editor history and is redoable.
- Play mode remains non-authoring; the selector and spawn actions are edit-mode only.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Asset Browser MUST offer spawning actors and placed props at the
  world origin, the editor camera, or the live designated player.
- **FR-002**: Actor and prop spawning MUST share one anchor selector with Origin as
  the default; the selector MUST list Origin, Camera, and Player.
- **FR-003**: An Origin-anchored spawn MUST use the origin and identity rotation,
  exactly as before this feature.
- **FR-004**: A Camera-anchored spawn MUST use the editor camera's position and the
  camera's horizontal view direction as the facing. It MUST NOT apply camera pitch or
  roll. When the view has no horizontal direction, it MUST use identity yaw.
- **FR-005**: A Player-anchored spawn MUST use the live player actor's authored feet
  position and authored rotation. It MUST be unavailable when no live player is
  designated.
- **FR-006**: A spawned actor or prop MUST use unit scale, retain its asset binding and
  all kind-specific authored defaults, be selected afterwards, and record exactly one
  undoable create step.
- **FR-007**: Spawning MUST refuse at the selected kind's capacity without creating a
  partial entity or recording a step.
- **FR-008**: The anchor control MUST be one shared control for the Actors and Props
  sections. Adding an anchor MUST add one item to it, not a control per asset.
- **FR-009**: When the selected anchor is unavailable, the asset spawn actions MUST be
  disabled and MUST create nothing.
- **FR-010**: The feature MUST NOT change existing actor or prop semantics: asset
  resolution, renderers, collision, animation, show-collider, naming, duplicate,
  deletion, undo, cue addressing, and scenario capture MUST behave as before.
- **FR-011**: Environment loading MUST remain unchanged and unaffected by the anchor
  selector.
- **FR-012**: The anchor selection MUST be transient editor UI state: it MUST NOT be
  undoable and MUST NOT be persisted.
- **FR-013**: The editor's play mode MUST remain non-authoring; the anchor uses the
  player transform already recorded on the actor.
- **FR-014**: The feature MUST add no persisted field or format.

### Key Entities

- **Actor**: a spawned character entity built from a discovered actor asset, with its
  asset binding, renderer, physics attachment, and authored base state (unchanged).
- **Prop**: a placed prop entity built from a discovered prop asset, with its asset
  binding, collision shape, renderer, and authored base state (unchanged).
- **Spawn anchor**: an editor-only placement source — origin, editor camera (position
  and horizontal facing), or designated player (authored transform). Not authored data
  and not persisted.
- **Asset**: the discovered actor or prop entry whose button initiates the spawn; it
  supplies the asset binding and the default name.
- **Player designation**: the active zone's current player actor, whose authored
  transform is the player anchor (unchanged from
  `specs/045-undoable-player-designation`).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can spawn an actor at any available anchor in one click, with
  the expected position and facing and no follow-up placement adjustment (100% of such
  spawns).
- **SC-002**: A developer can spawn a prop at any available anchor in one click, with
  the expected position and facing and no follow-up placement adjustment (100% of such
  spawns).
- **SC-003**: The Asset Browser exposes exactly one anchor control for both sections,
  and adding an anchor adds zero per-asset controls (0 additional buttons per anchor).
- **SC-004**: Each actor/prop spawn, and the undo of each, is exactly one history step
  (100%).
- **SC-005**: An unavailable anchor and a full kind never create a partial entity or
  record a history step (0 violations).
- **SC-006**: Existing actor and prop behaviour is unchanged: the existing test suite
  passes, and asset resolution, animation, collision, naming, duplicate, delete, cue
  addressing, and scenario capture behave as before.
- **SC-007**: The gameplay loop keeps the project's target frame rate with the selector
  present.
- **SC-008**: A spawned actor or prop does not follow the player or camera after
  creation (0 attachments).
- **SC-009**: Actor and prop names remain their asset names; no automatic naming is
  introduced (0 name changes at spawn).

## Assumptions

- The anchor selector is shared by the Actors and Props sections, defaults to Origin,
  and is transient editor UI state.
- Camera placement uses the camera's exact position and horizontal yaw only, not pitch
  or roll. This keeps actors upright and props from being tilted, and may leave the
  object floating above the floor.
- Player placement uses the player's exact authored feet position and authored
  rotation. Overlap with the player is accepted and movable.
- Unit scale is used for both kinds; the anchor does not copy the player's scale.
- Actors and props keep the asset's name. Automatic anchor-informed naming is out of
  scope; duplicate names are already legal.
- Environments are not spawned and are outside this feature.
- Existing capacity, selection, undo, and redo paths are reused; no new command family
  is required.
- The editor's anchor palette from `specs/099-waypoint-authoring` and
  `specs/100-trigger-anchors` is the same set of options; this feature applies it to
  asset spawning.
- Play-mode stamping is out of scope; the selector and spawn actions are edit-mode
  only.
- The feature adds no persisted field or format.
