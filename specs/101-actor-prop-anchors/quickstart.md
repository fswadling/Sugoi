# Quickstart Validation: Actor and Prop Spawn Anchors

**Feature**: `101-actor-prop-anchors`  
**Date**: 2026-10-17

## Prerequisites

- .NET 10 SDK.
- The native libraries are present (`native/libcimgui.so`, SDL3 and Jolt libraries).
- A scene with at least one discovered actor asset and one discovered prop asset.

## Build and automated tests

From the repository root:

```sh
dotnet build Sugoi.fsproj
dotnet test test/Sugoi.Tests.fsproj
```

Focused run for this feature:

```sh
dotnet test test/Sugoi.Tests.fsproj --filter "FullyQualifiedName~SpawningTests"
```

Expected: the build succeeds and all tests pass. The automated coverage is described in
`data-model.md`; the selector and asset buttons are validated manually below.

## Manual validation

Start the editor with `dotnet run` and load the gameplay screen.

### 1. One shared selector

1. Open the Asset Browser.

Expected: one `Spawn at` control above the Actors section, defaulting to `Origin`, with
`Origin`, `Camera`, and `Player` options. It is not repeated per asset or per section.

### 2. Spawn an actor at the camera

1. Fly the camera to a distinctive position and look in a distinctive horizontal
   direction.
2. Set `Spawn at` to `Camera` and click an actor asset.

Expected: the actor is selected, keeps its asset name, sits at the camera's exact
position, faces the horizontal view direction, and is upright (no camera pitch/roll).
It may float above the floor.

### 3. Spawn a prop at the player

1. Designate an actor as the player, play-walk her to a distinctive spot, and return to
   edit mode.
2. Set `Spawn at` to `Player` and click a prop asset.

Expected: the prop is selected, keeps its asset name, sits at the player's feet with her
rotation, uses unit scale, and overlaps her. It is not attached: move the player and
confirm the prop stays.

### 4. Origin remains the default

1. Set `Spawn at` back to `Origin` and spawn an actor and a prop.

Expected: both are at the origin with identity rotation, exactly as before this feature.

### 5. Unavailable and capacity states

1. Clear the player designation. If `Spawn at` was already set to `Player`, confirm the
   asset spawn buttons are now disabled; otherwise confirm `Player` cannot be selected.

Expected: `Player` is unavailable; asset spawning is disabled and creates nothing.

2. Fill a kind's capacity and confirm its asset buttons are disabled; no entity or
   history step is created.

### 6. Camera orientation edge case

1. Point the camera straight up or down, set `Spawn at` to `Camera`, and spawn an actor
   or prop.

Expected: the object uses identity yaw rather than an undefined facing.

### 7. Undo and environments

1. Spawn an actor and a prop at anchors, then undo and redo each.

Expected: each spawn is one history step; undo removes the entity and redo restores its
anchor transform, rotation, scale, name, and asset.

2. Click an environment asset.

Expected: it loads as before; the spawn anchor does not affect environment loading.

## Checklist

- [ ] One shared `Spawn at` selector governs Actors and Props.
- [ ] Origin is the default and reproduces the previous placement.
- [ ] Camera placement uses the exact camera position and horizontal yaw only.
- [ ] Player placement uses the player's feet and authored rotation, with unit scale.
- [ ] No live player makes Player unavailable and creates nothing.
- [ ] Actors and props keep their asset names.
- [ ] Each spawn is selected and is one undo step; undo/redo restores it.
- [ ] Capacity and unavailable anchors create no partial entity or history step.
- [ ] Changing the anchor is transient (not undoable, not persisted).
- [ ] Environments remain unaffected.
- [ ] The gameplay loop still holds the project's target frame rate.
