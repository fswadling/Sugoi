# Quickstart: Multiple Zones

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- `JoltPhysicsSharp` (with `JoltPhysics.Native`) restored; the Jolt native library
  copied next to the apphost by the build.
- `assets/` populated with at least one environment, one actor, and one prop, so a
  zone can be given visible, collidable content.

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj   # includes GameplayStateTests + PlayerTests
```

## Validation scenarios

1. **One zone at launch (FR-012, SC-007)** — launch the app: the workspace behaves
   as before with a single zone named `main` shown in the Outliner's Zones section.
2. **Add a zone (FR-002/FR-003)** — type a name (e.g. `classroom`) and press *Add
   Zone*: a second zone with that name appears, becomes active, and the workspace
   shows an empty world (grid only, no geometry). The first zone still exists in
   the list, unmarked. Typing an empty or already-used name leaves the add control
   unavailable, so nothing is created.
3. **Edit the active zone (FR-004)** — in `classroom`, load an environment and spawn
   a prop and an actor. They belong to `classroom` only; the Outliner lists them.
4. **Switch preserves contents (FR-005, SC-002)** — select `main`: the viewport
   shows `main`'s own contents (or the empty grid), never `classroom`'s. Switch back
   to `classroom`: its environment, props, and actors are exactly as left, including
   transforms.
5. **Delete a zone (FR-015/FR-016, SC-008)** — delete an inactive zone with its
   *Del* button: it and its contents disappear from the Zones section, and the
   active zone and camera are unchanged. Delete the active zone while another
   exists: another zone becomes active and the deleted zone is gone. With exactly
   one zone left, its *Del* control is unavailable, so it cannot be deleted.
6. **Isolation (FR-006, FR-007, SC-003)** — give both zones overlapping environments.
   Only the active zone's geometry renders and collides; the inactive zone's
   geometry neither blocks the camera/picking nor holds the player up.
7. **Player designation per zone (FR-008, SC-004)** — bind an actor as the player in
   `main` (*Set as Player*). Switch to `classroom` and enter play: nothing is driven
   (unless `classroom` has its own designation). Switch back to `main` and enter
   play: its designated actor is driven. A designation in one zone never drives a
   same-index actor in another.
8. **Camera re-frames (FR-009)** — switch zones: the camera pivots onto the incoming
   zone's designated player when one exists, otherwise the origin, preserving the
   current viewing offset.
9. **Undo is zone-scoped (FR-010, SC-005)** — make an edit in `main` (e.g. move a
   prop), switch to `classroom`, and make a different edit. Undo in `classroom`
   reverses only its edit; switch back to `main` and undo reverses only `main`'s.
   Add/select/delete themselves are not undoable (FR-011).
10. **No accumulation (SC-006, SC-009)** — switch zones and add/delete zones
    repeatedly and watch the per-second stats line: actors/props/parts counts return
    to the same values, draws/instances do not grow, and the frame-rate overlay
    stays at the target 60 fps.
11. **Clean shutdown (FR-014)** — with several zones, quit: shutdown releases every
    zone's static-scene batch and exits cleanly with no GPU validation errors.

## Expected outcome

- Several zones exist at runtime; exactly one is active and is the only world that
  renders, edits, and simulates. Zones can be added, selected, and deleted, with the
  last zone protected.
- Switching preserves each zone's entities, transforms, environment, and player
  designation, isolates physics to the active zone, swaps undo history, and
  re-frames the camera. Deleting a zone releases its bodies and GPU resources.
- Each zone is independently playable: it drives its own designated actor or
  nothing.
- `dotnet build` is clean and `dotnet test` passes (existing suite +
  `GameplayStateTests`, with `PlayerTests` updated for the zone-scoped
designation).
