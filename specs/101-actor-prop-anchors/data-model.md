# Data Model: Actor and Prop Spawn Anchors

**Feature**: `101-actor-prop-anchors`  
**Date**: 2026-10-17

This feature adds no persisted field, format, or entity kind. It moves one editor-only
union into `EditorState`, adds one transient editor-state field, and defines how an
existing entity transform is written when an asset is spawned.

## Existing entities used unchanged

### Actor

Owned by a zone's `Actors` slot map (`world/Entity.fs`, `world/Zone.fs`).

| Field | Meaning |
|-------|---------|
| `State.Base.Name` | The actor's name; a new spawn keeps the asset's name. |
| `State.Base.Transform` | Position, rotation, and scale. |
| `State.AssetId` | The discovered actor asset the spawn was built from. |
| `State.ShowCollider` | The editor overlay flag. |
| `Physics` / `Renderer` | Runtime resources derived from the asset; unchanged. |

### Prop

Owned by a zone's `Props` slot map.

| Field | Meaning |
|-------|---------|
| `State.Base.Name` | The prop's name; a new spawn keeps the asset's name. |
| `State.Base.Transform` | Position, rotation, and scale. |
| `State.AssetId` / `SubmeshIndex` / `SubmeshCount` | The placed-prop asset binding. |
| `Physics` / `Renderer` | Runtime resources derived from the asset; unchanged. |

### Player designation

`Zone.Player : Handle voption` (unchanged, `specs/045`).

- The player anchor is available only when `Systems.isPlayerLive zone` is true.
- The source position and rotation are the actor's current authored
  `State.Base.Transform`.

## Editor-only values

### SpawnAnchor

A closed, non-persisted union moved to `state/EditorState.fs` so the selected value can
live in `EditorState`. It is shared with waypoint and trigger creation.

| Case | Name prefix | Position | Rotation |
|------|-------------|----------|----------|
| `Origin` | `Origin` | `Vector3.Zero` | identity |
| `Camera` | `Camera` | the editor camera's exact `Position` | yaw about +Y by `atan2(forward.X, forward.Z)`; no pitch/roll; zero horizontal component yields identity yaw |
| `Player` | `Player` | the live player's authored feet position | the player's authored rotation |

`anchorAvailable` stays in `editor/Spawning.fs` and returns false for `Player` without
a live designation; `Origin` and `Camera` are always available.

### EditorState.SpawnAt

A `SpawnAnchor` field on the transient editor state, defaulting to `Origin`.

- It is the selected placement mode for both Actors and Props.
- It is not undoable and not persisted.
- It survives until the editor session ends or the value is changed.
- If the selected anchor becomes unavailable (for example, the player is deleted),
  spawning is disabled until another anchor is selected.

## Placement rules

A spawn applies the anchor to the entity built from the asset name:

```text
entity.Transform.Position = anchor position
entity.Transform.Rotation = anchor rotation
entity.Transform.Scale    = Vector3.One
```

- The asset name, asset binding, and kind-specific authored defaults are unchanged.
- The entity is selected and one `ActorCreate` or `PropCreate` step is recorded.
- No attachment exists: the entity copies the transform once and does not follow the
  player or camera.
- Environments are not spawned and ignore the selector.

## Commands and undo

No command type is added.

| Command | Existing payload | Use in this feature |
|---------|------------------|---------------------|
| `ActorCreate` | `ZoneId`, `Ref`, captured `ActorState` | An actor spawn at an anchor; undo removes, redo restores the captured state (position, rotation, scale, name, asset). |
| `PropCreate` | `ZoneId`, `Ref`, captured `PropState` | A prop spawn at an anchor; undo/redo as above. |

## Capacity

Each kind's fixed slot-map capacity is checked by the existing `spawnActor`/`spawnProp`
path. The Asset Browser disables the asset buttons when the selected anchor is
unavailable or the relevant count is at capacity; a direct call still refuses through
`hasRoom` and records no step.

## Validation rules

- The selected anchor must be available; otherwise no entity is created.
- The player anchor requires a live designation in the active zone.
- Camera placement never queries collision and may produce a floating entity.
- Unit scale is always used; the player's or camera's scale is not copied.
- Names are the asset names; duplicates remain legal.
- Only the active zone is affected.
