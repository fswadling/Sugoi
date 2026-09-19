# Quickstart: Waypoint Chains

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- For the interactive scenarios: a display, and a zone with at least one waypoint
  (add one with *Add Waypoint* or *Add at Camera*).

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj   # includes ZoneTests, SpawningTests, SelectionTests
```

The model and the commands are covered headlessly: link resolution and staleness
(`test/ZoneTests.fs`), the extend gesture as one command (`test/SpawningTests.fs`),
and link/unlink plus delete-and-undo (`test/SelectionTests.fs`). The section's
button gating and the connectors need an ImGui frame and are covered by the
scenarios below.

## Validation scenarios

Automated (headless):

1. **A link resolves and clears (FR-001, US2/AC1-2)** — `setWaypointNext` with a
   target makes `waypointNext` answer it; with `None` it answers nothing.
2. **A waypoint cannot be its own next (FR-004, US2/AC3)** — `setWaypointNext` to
   the same ref raises, and the link is unchanged.
3. **A chain reads in order (SC-001)** — linking A→B→C and following the links from
   A yields A, B, C in that order.
4. **A deleted target stops the chain (FR-002, SC-002)** — deleting the middle
   waypoint makes its predecessor's link unresolvable, and its own link is inert;
   nothing dangles.
5. **Undo restores the whole chain (FR-007, SC-002)** — the same delete, undone,
   makes both links resolve again and the chain reads A, B, C.
6. **A reused slot is never aliased (FR-002, SC-003)** — spawning a new waypoint
   into the deleted one's slot leaves both stale links unresolvable, and the
   newcomer has no link of its own.
7. **Chains are per zone (FR-010, FR-012)** — a link made in one zone does not
   resolve in another, and each zone keeps its own.
8. **One gesture is one step (FR-006, SC-004)** — the extend gesture pushes a
   single `LinkSpawn`; undo removes the waypoint and restores the predecessor's
   previous link; redo reapplies both.
9. **Linking and unlinking are one step each (US2/AC5)** — `linkRef` to a target
   pushes one `Link` carrying both sides; undo restores the previous next exactly,
   including when the previous next was another waypoint; `linkRef … None` breaks
   it in one step; linking to the current target pushes nothing.

Interactive (editor; needs a display):

10. **Extend a chain (US1/AC1-2, SC-001)** — select a waypoint, move the camera,
    press *Add Next at Camera*: a new waypoint appears at the camera facing the
    view direction, the first one now points at it, and the new one is selected.
    Repeat three times and confirm the connectors run in the order placed.
11. **Gating (US1/AC5, US2/AC3)** — with nothing selected, *Add Next at Camera* and
    *Unlink* are disabled, and every row's *Link* is disabled; with a waypoint
    selected, its own row's *Link* stays disabled while the others' are enabled.
12. **Link and unlink existing waypoints (US2, SC-001)** — with one waypoint
    selected, press *Link* on another's row: the connector appears. Press *Unlink*:
    it goes. Undo restores it.
13. **Chains follow their waypoints (US3/AC2, SC-005)** — move and rotate a linked
    waypoint: its connectors follow in the same frame, and the arrow head keeps
    showing the direction the chain runs.
14. **Coincident waypoints (US3)** — press *Add Next at Camera* twice without
    moving the camera: two waypoints coincide and no connector is drawn between
    them (no stray or malformed geometry).
15. **Delete and undo mid-chain (SC-002)** — delete the middle of a three-waypoint
    chain: the connectors show the chain stopping there. Undo: the whole chain
    returns, including the link that ran through the restored waypoint.
16. **Zone switching (FR-010)** — link waypoints in one zone, add a second zone and
    switch: the new zone draws no connectors; switching back restores the chain and
    its connectors.
17. **Nothing else changes (FR-011, SC-007)** — rename, move, duplicate, and delete
    waypoints as before, and confirm a linked waypoint still collides with nothing,
    renders no geometry, and does not affect the player or the frame rate (watch
    the stats line across a session of linking).

## Expected outcome

- Waypoints can be linked into chains: each waypoint has at most one next, chains
  are readable in the viewport with a direction arrow, and a chain may loop or
  share a successor without any code assuming otherwise.
- Extending, linking, and unlinking are each exactly one undo step, and deleting a
  mid-chain waypoint then undoing restores the chain in full.
- A link never resolves to a waypoint other than the one it was made to, and an
  unresolvable link neither dangles nor is silently rewritten.
- `dotnet build` is clean and `dotnet test` passes (existing suite plus the
  waypoint-link cases in `ZoneTests`, `SpawningTests`, and `SelectionTests`).
