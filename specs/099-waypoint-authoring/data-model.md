# Data Model: Waypoint Authoring UX

**Feature**: `099-waypoint-authoring`  
**Date**: 2026-10-17

This feature adds no persisted field, format, or entity kind. It adds one editor-only
value used by two menus, and rules for how existing waypoint fields are written.
Automatic names ride the existing `Waypoint.Base.Name` field and are captured with the
waypoint like any other authored name.

## Existing entities used unchanged

### Waypoint

Owned by a zone's `Waypoints` slot map (`world/Entity.fs`, `world/Zone.fs`).

| Field | Type | Meaning |
|-------|------|---------|
| `Base.Name` | `string` | The label shown in lists and choosers. Not an identity; duplicates are legal. |
| `Base.Transform.Position` | `Vector3` | The waypoint's position (feet for a player-anchored waypoint). |
| `Base.Transform.Rotation` | `Quaternion` | The waypoint's facing, using the project's `-Z` forward convention. |
| `Base.Transform.Scale` | `Vector3` | Always unit for a newly created waypoint, regardless of anchor. |
| `Base.Enabled` / `Visible` / `Selectable` / `Draggable` | `bool` | The shared editor flags, unchanged. |
| `Next` | `EntityRef option` | The waypoint this chain continues at; `None` ends the chain. Zone-scoped. |

Validation rules (unchanged):

- A link is only between waypoints in the same zone.
- A waypoint cannot be its own next.
- A link to a dead waypoint does not resolve; it is not rewritten.

### Player designation

`Zone.Player : Handle voption` (unchanged, `specs/045`).

- The player anchor is available only when `Systems.isPlayerLive zone` is true,
  meaning the handle resolves to a live actor in that zone.
- The anchor reads the actor's current authored `State.Base.Transform.Position` and
  `Rotation`. This value is kept current by `Systems.syncActors` during play and by
  editor transform edits; a never-played actor still has one.

## New editor-only value

### WaypointAnchor

A closed, non-persisted union in `editor/Spawning.fs`:

| Case | Name prefix | Position / facing source | Availability |
|------|-------------|--------------------------|--------------|
| `Origin` | `Origin` | World origin, identity facing | Always |
| `Camera` | `Camera` | Editor camera position, camera forward as `-Z` | Always |
| `Player` | `Player` | Player actor's authored position and rotation | A live player is designated |

The ordered palette is `Origin`, `Camera`, `Player`. Both the creation menu and the
extend menu iterate this one palette. A future anchor adds a case, a prefix, and a
source; both menus gain the row automatically.

## Naming rule

A new waypoint is named `<Prefix> <n>`:

- `<Prefix>` is the anchor's name prefix (`Origin`, `Camera`, `Player`).
- `<n>` is the lowest positive integer such that no live waypoint in the active zone
  already has the name `<Prefix> <n>`.
- The suffix is parsed as a number, so a manually renamed `Camera 07` also reserves
  `Camera 7`. A name that does not match `<Prefix> <positive integer>` reserves
  nothing.
- Deleted or renamed waypoints free their number; the next creation at that anchor may
  reuse it.
- Names remain editable after creation. Manual renames may create duplicates, which are
  legal labels and do not affect chain identity.

## Chain insertion rule

Given a selected waypoint `A` with `A.Next = Some B`, extending `A` with a new
waypoint `N` produces:

```text
A -> N -> B
```

- `N.Next` becomes the previous value of `A.Next` (`B`).
- `A.Next` becomes `N`.
- When `A` is the chain end (`A.Next = None`), the same operation produces `A -> N`
  and `N.Next = None`.
- The selection becomes `N`.

Nothing outside the affected pair of links changes; merges and loops elsewhere remain
untouched.

## Commands and undo

No command type is added.

| Command | Existing payload | Use in this feature |
|---------|------------------|---------------------|
| `WaypointCreate` | `ZoneId`, `Ref`, captured `Waypoint` | Creation at an anchor. Undo removes; redo restores the captured value, name included. |
| `LinkSpawn` | `ZoneId`, `From`, `Before`, `Ref`, captured `Waypoint` | Link-and-create at an anchor. The captured waypoint carries `Next = Before`, so redo restores the insertion. Undo sets `From.Next` back to `Before` and removes `Ref`. |
| `Link` | `ZoneId`, `Ref`, `Before`, `After` | The `Next` picker's link, retarget, and end-chain edits. |

State transitions:

- **Create**: no waypoint -> one waypoint, selected, one history step.
- **End chain**: `A.Next = Some B` -> `A.Next = None`, one `Link` step.
- **Link / retarget**: `A.Next = none|B` -> `A.Next = Some C`, one `Link` step.
- **Insert**: `A.Next = none|Some B` -> `A.Next = Some N`, `N.Next = none|Some B`,
  selected, one `LinkSpawn` step.
- **Undo/redo**: the reverse of each transition, with the captured values.

## Capacity

The zone's waypoint slot map has a fixed capacity. Creation and insertion first check
for room, refuse without mutating anything, report through `Diagnostics`, and record
no history step. This is the existing `hasRoom` rule in `Spawning`.
