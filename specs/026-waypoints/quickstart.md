# Quickstart: Waypoints

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- For the editor scenarios: a display, `assets/` populated as for the existing
  editor, and a zone in the workspace.
- No waypoint scenario needs a physics asset: a waypoint has no renderer and no
  body.

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj   # includes EntityTests, ZoneTests, SpawningTests
```

The behaviour that can be asserted without a display is covered by the suite:
the `Waypoint` shape (`test/EntityTests.fs`), waypoint refs — spawn/resolve,
in-place transform, removal, slot reuse, and its own map (`test/ZoneTests.fs`),
and duplicate plus delete-and-undo (`test/SpawningTests.fs`).

## Validation scenarios

Automated (headless):

1. **A waypoint is a zone entity (FR-002)** — a spawned waypoint resolves to its
   base entity's transform, and `Zone.isAlive` reports its ref alive. Its kind is
   `Waypoint` and it occupies `zone.Waypoints`, not `zone.Props`.
2. **Stale refs never alias (FR-007, SC-004)** — remove a waypoint, spawn
   another, and confirm it reuses the freed index with a different generation:
   the stale ref resolves to neither the old nor the new waypoint.
3. **Transform in place (FR-005)** — `Zone.setTransform` on a waypoint ref moves
   and rotates it while the ref stays valid.
4. **Duplicate (FR-006, SC-007)** — duplicating a waypoint leaves the source
   untouched, spawns a distinct live ref, selects the copy, and pushes a
   reversing `Duplicate` command.
5. **Delete and undo (FR-006, FR-008)** — deleting a waypoint makes its ref
   stale; undo re-occupies the recorded slot with the recorded transform and
   rotation.
6. **Unsupported kinds still fail loudly (FR-009)** — duplicating `Environment`
   or `Trigger` still raises.

Interactive (editor; needs a display):

7. **Add at the origin (FR-003)** — press *Add Waypoint* in the Outliner's
   Waypoints section: a marker appears at the origin, a row named `Waypoint`
   appears in the list, and it becomes the selection.
8. **Add at the camera (FR-004, SC-003)** — frame the view somewhere distinctive
   looking in a distinctive direction and press *Add at Camera*: the new waypoint
   sits at the camera's position and its arrow points along the view direction,
   with no follow-up move or rotate needed.
9. **Place and orient (FR-005)** — drag the selected waypoint with the move
   gizmo and rotate it with the rotate gizmo: the marker follows, the inspector's
   position and rotation update, and one undo reverses the whole drag (FR-006).
10. **Facing is visible (FR-010, FR-011, SC-005)** — place two waypoints with
    different rotations: their arrows point different ways without either being
    selected, and the selected one is highlighted. Rotate one to face straight up
    and confirm its head is still readable.
11. **Selection surface (Edge Cases)** — clicking a waypoint's marker in the
    viewport does not select it; clicking its row in the Waypoints section does,
    after which the gizmo, nudge, inspector, *Del*, and *Dup* all apply.
12. **Waypoints are inert (FR-009)** — a waypoint draws no geometry, does not
    block the camera or the player, and appears in no physics query; nothing in
    `BodyToEntity` names it.
13. **Waypoints are zone data (FR-001, SC-002)** — add waypoints in one zone,
    switch to another (its list shows `(none)`, and the first zone's markers are
    not drawn), then switch back: the waypoints are exactly as left, including
    transforms.
14. **No accumulation (SC-006)** — add, duplicate, and delete waypoints
    repeatedly and watch the per-second stats line: body and draw counts return
    to the same values and the frame-rate overlay stays at the target 60 FPS.

## Expected outcome

- A zone holds waypoints as entities with a position and a facing, listed in the
  Outliner and drawn in the viewport with a direction arrow.
- Waypoints can be added at the origin or at the editor camera, selected,
  moved, rotated, duplicated, and deleted, with undo and redo covering each of
  those, and with refs that never alias a reused slot.
- A waypoint affects nothing else: no collider, no renderer, no asset, no
  simulation, and no effect on any other entity.
- `dotnet build` is clean and `dotnet test` passes (existing suite plus the
  waypoint cases in `EntityTests`, `ZoneTests`, and `SpawningTests`).
