# Quickstart Validation: Trigger Anchors

**Feature**: `100-trigger-anchors`  
**Date**: 2026-10-17

## Prerequisites

- .NET 10 SDK.
- The native libraries are present (`native/libcimgui.so`, SDL3 and Jolt libraries).
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

Expected: the build succeeds and all tests pass. The automated coverage is described
in `data-model.md`; the editor menu and volume are validated manually below.

## Manual validation

Start the editor with `dotnet run` and load the gameplay screen.

### 1. Create a trigger at the player

1. Select an actor and choose **Set as Player** in the Inspector.
2. Press Backtick to play, walk or jog her to a distinctive spot, and press Backtick
   to return to edit mode.
3. In the Outliner's **Triggers** section, open **Add Trigger** and choose **Player**.

Expected: a new trigger is selected, named `Player 1`, with its base **0.1 m below**
her feet, the default radius `1.0` and height `2.0`, identity rotation, and unit
scale. The player is inside the volume at that moment.

### 2. Create a trigger at the camera

1. Fly the camera to a distinctive position (not the player's spot).
2. Open **Add Trigger** and choose **Camera**.

Expected: a new trigger is selected, named `Camera 1`, with its base at the camera's
exact position. If the camera is above the floor, the volume floats; this is expected.

### 3. Names and reuse

1. Create a second trigger at the camera and one at the origin.

Expected names: `Camera 2` and `Origin 1`. Rename `Camera 1`, create at the camera
again, and confirm `Camera 1` is reused. Delete a named trigger and confirm the next
creation at that anchor reuses its number.

### 4. No-player and capacity states

1. Clear the player designation (or select a zone with no player) and open
   **Add Trigger**.

Expected: **Player** is visible but unavailable; choosing it creates nothing.

2. Fill a zone's trigger capacity and open the creation menu.

Expected: creation is unavailable, no trigger is added, and no history step is
recorded.

### 5. Independence

1. Create a trigger at the player.
2. Move the player in edit mode.

Expected: the trigger stays where it was created; it does not follow the player.

### 6. Existing trigger behavior

1. Select the new trigger, change its radius, height, prompt, interactable flag, and
   authored cue.
2. Duplicate it, delete it, and undo each.

Expected: every existing trigger path behaves as before, including cue wiring and
scenario capture.

## Checklist

- [ ] Trigger at the player uses her feet minus 0.1 m and is selected.
- [ ] Trigger at the camera uses the camera's exact position and is selected.
- [ ] No live player makes the Player anchor unavailable.
- [ ] Creation anchors are reached from one creation menu.
- [ ] Automatic names are `Origin n`, `Camera n`, `Player n` with per-anchor numbers.
- [ ] Every creation is one undo step and redo restores position, shape, and name.
- [ ] No creation exceeds capacity or records a partial step.
- [ ] A trigger created at an anchor does not follow the player or camera.
- [ ] Existing trigger detection, cue wiring, prompt, duplicate, delete, and scenario
      capture behave as before.
- [ ] The gameplay loop still holds the project's target frame rate with the new
      authoring controls present.
