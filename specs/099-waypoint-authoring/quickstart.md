# Quickstart Validation: Waypoint Authoring UX

**Feature**: `099-waypoint-authoring`  
**Date**: 2026-10-17

## Prerequisites

- .NET 10 SDK (`dotnet --version`).
- The native libraries are present (`native/libcimgui.so`, SDL3 and Jolt libraries);
  see `native/README.md` and `native/build.sh`.
- A scene with at least one actor that can be designated as the player.

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

Expected: the build succeeds and all tests pass. The feature's headless coverage is
described in `data-model.md`; the editor menus themselves are validated manually
below.

## Manual validation

Start the editor with `dotnet run` and load the gameplay screen.

### 1. Create at the player after playing

1. Select an actor and choose **Set as Player** in the Inspector.
2. Press Backtick to play, walk or jog her to a distinctive spot, and press Backtick
   to return to edit mode.
3. In the Outliner's **Waypoints** section, open **Add Waypoint** and choose
   **At Player**.

Expected: one new waypoint exists at her current position and full rotation, is
selected, has unit scale, and is named `Player 1`.

### 2. Create at the other anchors

1. Open **Add Waypoint** and choose **At Camera** twice.
2. Choose **At Origin**.
3. Choose **At Player** again.

Expected names: `Camera 1`, `Camera 2`, `Origin 1`, `Player 2`. Any waypoint can be
renamed, and a freed number is reused by the next creation at that anchor.

### 3. No-player state

1. Select the player actor and clear her designation (or select another zone with no
   player).
2. Open **Add Waypoint**.

Expected: **At Player** is visible but unavailable; choosing it creates nothing. The
other anchors remain available.

### 4. Link and end a chain

1. Select a waypoint.
2. In the Inspector's **Next** chooser, choose another live waypoint.

Expected: the chain continues at that waypoint as one undoable step. Choose
`(end)` to break the link as one step.

### 5. Insert in the middle of a chain

1. Build a chain `A -> B` (link A to B).
2. Select A and open **Add Next**, choosing an anchor.

Expected: the chain becomes `A -> New -> B`; B is still reachable. Undo once restores
exactly `A -> B`, and redo restores the insertion.

### 6. Repeated extension

1. Select a waypoint and extend at the camera three times in a row.

Expected: each new waypoint becomes the selection and the next extension continues
from it; the chain order matches the order placed. Each extension is one history step.

### 7. Capacity

1. Fill a zone's waypoint capacity (the fixed slot-map capacity).
2. Open **Add Waypoint** and the Inspector's **Add Next**.

Expected: creation and extension are unavailable; no waypoint is created and no
history step is recorded.

## Checklist

- [ ] Player-anchored waypoint matches the player's position and full rotation, with unit scale.
- [ ] Player anchor is unavailable without a live designation.
- [ ] Creation anchors are reached from one creation menu.
- [ ] Automatic names are `Origin n`, `Camera n`, `Player n` with per-anchor numbers.
- [ ] Next chooser links, retargets, and ends a chain in one step each.
- [ ] Extending a middle waypoint inserts and preserves the tail.
- [ ] Undo/redo restores every creation, link, and insertion exactly.
- [ ] No creation or extension exceeds capacity or records a partial step.
- [ ] Existing waypoint rendering, transforms, rename, duplicate, delete, and scenario
      capture behave as before.
- [ ] The gameplay loop still holds the project's target frame rate with the new
      authoring controls present.
