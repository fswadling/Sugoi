# Research: Trigger Anchors

**Feature**: `100-trigger-anchors`  
**Date**: 2026-10-17

## Decision 1: Share one anchor palette across waypoints and triggers

**Decision**: Rename the editor-only `WaypointAnchor` union in
`editor/Spawning.fs` to `SpawnAnchor` and share `spawnAnchors`, `anchorName`, and
`anchorAvailable` between waypoint and trigger creation. Update the waypoint call sites
in `editor/Outliner.fs` and `editor/Inspector.fs` and the `SpawningTests` references.

**Rationale**: both kinds now use exactly the same three anchors (`Origin`, `Camera`,
`Player`) and the same availability rule. A second `TriggerAnchor` union would
duplicate the palette and let the two menus drift. The rename is mechanical: the
union and list live in one module, and the existing tests use the cases
(`Spawning.Player`) rather than the type name.

**Alternatives considered**:

- Keep `WaypointAnchor` and use it for triggers. It works but the name becomes
  misleading.
- Add a separate `TriggerAnchor`. It duplicates the palette and the availability
  rule, and a future anchor must be added twice.

## Decision 2: Camera anchor copies the camera's exact position

**Decision**: `Camera` supplies `camera.Position` as the trigger's base position. There
is no downward ray, ground projection, or collision query.

**Rationale**: this was the clarified user choice. It keeps the camera anchor
available in any zone, including empty ones and scenes whose only floor is the
editor grid, and avoids introducing a placement query for a gesture that is an
authoring starting point rather than a validated final position. A trigger created
above the floor may float; the author can adjust its position, radius, or height in
the Inspector.

**Alternatives considered**:

- First collidable surface below the camera. It would place walk-in volumes on the
  floor, but it was rejected in clarification.
- The camera target point. It would require a ray and is a different gesture from
  "at the camera".

## Decision 3: Player anchor drops 0.1 m below the feet

**Decision**: `Player` supplies the actor's authored feet position lowered by
`0.1f` on the Y axis. It is available only when `Systems.isPlayerLive zone` is true.
The actor's authored transform is the source, as it is for waypoints.

**Rationale**: the trigger's base is the bottom of its cylinder and containment is
inclusive at that boundary. Dropping the base slightly guarantees the player is
inside at the moment of creation even with small ground unevenness, without changing
the volume's useful reach. A fixed offset is predictable and easy to test.

**Alternatives considered**:

- Exactly at the feet. Correct at the boundary, but sensitive to ground variation.
- Centre the volume on the player. It extends the volume below the floor and changes
  what nearby positions count as inside.
- A percentage of the trigger height. It couples the offset to a value the author can
  change, so the same anchor would move when the height changes.

## Decision 4: Reuse `spawnTrigger` and `TriggerCreate`

**Decision**: Add `triggerAt` (build an entity at an anchor) and `spawnTriggerAt`
(check capacity, build, delegate to `spawnTrigger`) in `editor/Spawning.fs`. No
command type is added.

**Rationale**: `TriggerCreate` already carries the complete authored `TriggerState`,
and `spawnTrigger` already pairs the spawn with the reversing command and selects the
new trigger. The anchor only supplies `Base.Transform.Position`; the default radius
and height, identity rotation, and unit scale come from the existing trigger
construction.

**Alternatives considered**:

- A distinct create command. It would duplicate the existing payload and runner.
- Assign the position after spawning. That would need a second transform step and
  could leave a partial state if interrupted.

## Decision 5: Generalise the automatic-name helper

**Decision**: Extract the existing waypoint naming loop into a private generic helper
over a slot span and a name accessor, then use it for both waypoint and trigger names:
`<Anchor> <lowest free positive n>`. Trigger names therefore use the same
`Origin 1` / `Camera 1` / `Player 1` convention as waypoints.

**Rationale**: the algorithm (exact prefix match, numeric suffix parse, lowest free
number) is identical for both kinds. Duplicating it invites the two choosers to drift.
Names remain labels, and a rename or delete frees a number for both kinds.

**Alternatives considered**:

- A trigger-specific loop. Small, but a second copy of a subtle parsing rule.
- One kind-wide `Trigger n` counter. Simpler, but it loses the anchor context and
  does not match the clarified naming decision.

## Decision 6: One creation menu in the Outliner

**Decision**: Replace the trigger section's single `Add Trigger` button with one
button that opens a popup over `spawnAnchors`, exactly as the waypoint section does.
The existing ImGui popup bindings from feature 099 are reused; no render-layer change
is needed.

**Rationale**: it satisfies the one-control requirement, keeps triggers consistent
with waypoints, and makes a future anchor a menu row. Triggers have no chain or
extend gesture, so the Inspector needs no new section.

**Alternatives considered**:

- Separate `Add at Camera` / `Add at Player` buttons. That is the pattern this feature
  is replacing.
- A trigger-specific control in the Inspector. Creation belongs where triggers are
  listed; the Inspector remains the editing surface for the selected trigger.

## Decision 7: Headless tests plus the manual quickstart

**Decision**: Cover anchor construction, the 0.1 m player offset, camera positioning,
automatic names, capacity, selection, independence, and undo/redo through
`test/SpawningTests.fs`. Validate the menu and trigger volume in the editor with
`quickstart.md`.

**Rationale**: the existing tests already drive the real `Spawning`/`Zone`/undo paths
without a GPU, and the project has no headless ImGui harness. The menu is a thin
adapter over the tested spawn functions.

**Alternatives considered**:

- An ImGui test harness. It would add native context and input scripting for a small
  menu, which no existing feature uses.
