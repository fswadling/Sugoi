# Data Model: Trigger Anchors

**Feature**: `100-trigger-anchors`  
**Date**: 2026-10-17

This feature adds no persisted field, format, or entity kind. It adds/reuses one
editor-only value and rules for how an existing trigger field is written. Automatic
names ride the existing trigger name and are captured with the trigger like any other
authored name.

## Existing entities used unchanged

### Trigger

Owned by a zone's `Triggers` slot map (`world/Entity.fs`, `world/Zone.fs`).

| Field | Type | Meaning |
|-------|------|---------|
| `Base.Name` | `string` | The label used by trigger choosers and cue ops. Not an identity; duplicates are legal. |
| `Base.Transform.Position` | `Vector3` | The centre of the cylinder's base. |
| `Base.Transform.Rotation` | `Quaternion` | Authored rotation; containment is radially symmetric, so it does not affect detection. |
| `Base.Transform.Scale` | `Vector3` | Not the volume's size; new triggers use unit scale. |
| `Radius` | `float32` | Cylinder radius; default `1.0`. |
| `Height` | `float32` | Cylinder height above the base; default `2.0`. |
| `Interactable` / `Prompt` / `Cue` | authored | Unchanged from `specs/029-triggers` and `specs/058-trigger-cue`. |
| `Base.Enabled` | `bool` | Whether the trigger detects the player. |

Validation rules (unchanged):

- Containment is `within Radius horizontally` and `baseY <= playerY <= baseY + Height`.
- The trigger volume does not use rotation or scale.
- A trigger is zone data and does not attach to any actor or camera.

### Player designation

`Zone.Player : Handle voption` (unchanged, `specs/045`).

- The player anchor is available only when `Systems.isPlayerLive zone` is true.
- The source position is the actor's current authored feet
  `State.Base.Transform.Position`, lowered by **0.1** on Y.

## Editor-only value

### SpawnAnchor

A closed, non-persisted union in `editor/Spawning.fs`, shared with waypoint
authoring. It is renamed from `WaypointAnchor` and keeps the same cases.

| Case | Name prefix | Trigger base position | Availability |
|------|-------------|-----------------------|--------------|
| `Origin` | `Origin` | `Vector3.Zero` | Always |
| `Camera` | `Camera` | The editor camera's exact `Position` | Always in edit mode |
| `Player` | `Player` | Player feet minus `Vector3(0, 0.1, 0)` | A live player is designated |

The ordered palette is `Origin`, `Camera`, `Player`. It is used by the trigger
creation menu and the existing waypoint creation/extend menus. A future anchor adds a
case, a prefix, and a position source, and both kinds gain the row.

## Naming rule

A new trigger is named `<Prefix> <n>`:

- `<Prefix>` is the anchor's prefix (`Origin`, `Camera`, `Player`).
- `<n>` is the lowest positive integer such that no live trigger in the active zone
  already has the name `<Prefix> <n>`.
- The suffix is parsed as a number, so a renamed `Camera 07` reserves `Camera 7`.
- A name that does not match `<Prefix> <positive integer>` reserves nothing.
- Deleted or renamed triggers free their number; the next creation at that anchor may
  reuse it.
- Names remain editable. Manual renames may create duplicates, which are legal labels.
- The rule is identical to the waypoint naming rule and shares one helper.

## Placement rules

### At Origin

Base at `Vector3.Zero`; identity rotation; unit scale.

### At Camera

Base at the editor camera's exact `Position`; identity rotation; unit scale. No
ground projection or collision query. The resulting volume may float above the floor;
the author can resize or move it.

### At Player

Base at the player actor's authored feet position minus `0.1` on Y; identity rotation;
unit scale. Available only with a live designation. With the default height of `2.0`,
a standing player is inside the volume immediately (the base is below her feet and the
height covers her). The trigger does not attach to the player or follow her.

## Commands and undo

No command type is added.

| Command | Existing payload | Use in this feature |
|---------|------------------|---------------------|
| `TriggerCreate` | `ZoneId`, `Ref`, captured `TriggerState` | Creation at an anchor. Undo removes; redo restores the captured state, position and name included. |

State transitions:

- **Create**: no trigger -> one trigger at the anchor position, selected, one history
  step.
- **Undo/redo**: the reverse of the create, with the captured authored value.

## Capacity

The zone's trigger slot map has a fixed capacity. Creation first checks for room,
refuses without mutating anything, reports through `Diagnostics`, and records no
history step. This is the existing `hasRoom` rule in `Spawning`.
