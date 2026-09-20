# Quickstart: Entity Command Families

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- For the interactive scenarios: a display, and a zone with a few entities of
  different kinds (an actor and a prop from the asset browser, a waypoint, a trigger).

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj   # includes EntityTests, ZoneTests, SpawningTests, SelectionTests
```

Everything that matters here is headless: the per-kind capture/occupy/remove
round-trip and the link on the waypoint (`test/ZoneTests.fs`), the create and
extend gestures (`test/SpawningTests.fs`), and the editor's delete and link paths
(`test/SelectionTests.fs`). Only the editor's own behaviour needs the scenarios
below.

**Three existing tests assert the behaviour this feature changes and must be
rewritten, not merely adjusted:**

- `SpawningTests` "a duplicated waypoint carries the link to the same successor" —
  inverted by FR-013; it previously asserted the copy had no link.
- `ZoneTests` "chains live with their zone" — asserts `second.Links.Count`, which no
  longer exists; the property it checks (a link in one zone does not resolve in
  another) is asserted through the waypoint's own state instead.
- Any test asserting a `Spawn`/`Delete`/`Duplicate` command now asserts the matching
  `*Create`/`*Delete`.

- **An actor's delete and undo cannot be covered headlessly**: building *and* releasing
  its renderer need a GPU device (`ActorRender.release` dereferences the asset). The
  headless suite covers that a delete *records* the collider overlay; undoing one is
  interactive scenario 12 below.

## Validation scenarios

Automated (headless):

1. **Every kind round-trips exactly (FR-001, FR-002, SC-001, SC-002)** — for prop,
   part, waypoint, and trigger: set every property that kind exposes to a non-default
   value, delete it, undo, and assert every property is unchanged. Repeat
   delete → undo → redo → undo 50 times and assert the properties are identical at
   every step where it is present. The actor's delete is covered as far as the
   headless suite can reach it (its recorded payload, scenario 5, plus interactive
   scenario 12).
2. **The overlay survives delete and undo (FR-003, US1/AC1)** — toggle an actor's
   collider overlay, delete the actor, undo: the overlay is still on. And the toggle
   itself pushes no undo step, so an undo immediately after it reverses the preceding
   edit instead.
3. **Trigger authored state is preserved (FR-004, US1/AC3)** — set a trigger's shape,
   prompt, interactable flag, and enabled state, then delete and undo: all four are
   unchanged, and the recorded payload has `Triggered = false`.
4. **A link travels with its waypoint (FR-005, FR-014, SC-007, SC-008)** — build
   A→B→C; delete B and assert the chain stops at A; undo and assert both links resolve
   again. Assert no link state is left behind by the deletion (condition 5 below).
5. **No stale link survives (SC-008)** — after deleting a linked waypoint, nothing in
   the zone holds a reference to it: the freed slot reused by a new waypoint leaves
   no link resolving to the newcomer, and no other waypoint answers with an unexpected
   next.
6. **A copy carries the link (FR-013, US2/AC2)** — duplicate a linked waypoint; the
   copy answers with the same next. Delete that next and the copy's link stops
   resolving rather than pointing at anything else.
7. **One gesture is one step (FR-009, SC-004, SC-005)** — a create pushes exactly one
   `*Create` and its undo removes the entity; the extend gesture pushes exactly one
   `LinkSpawn` and its undo removes the waypoint *and* restores the predecessor's
   previous link; redo reapplies both. A link edit pushes exactly one `Link`. Linking
   to the current next pushes nothing.
8. **A payload carries no live resource (FR-007)** — the record a `*Delete` holds has
   `Physics = None` and a defaulted `Renderer`; a restore rebuilds them.
9. **Repeated cycles do not accumulate resources (SC-003)** — delete and undo an
   actor 50 times; live body and renderer counts return to baseline.
10. **A reused slot is refused (validation)** — occupying a ref whose slot is taken
    reports the reused slot rather than clobbering the occupant.
11. **Existing behaviour is unchanged (FR-009, FR-012)** — `Transform`/`Rename` still
    work, a transform drag still coalesces into one step, the environment swap and a
    part's epoch behaviour are unchanged, and the physics/rendering test suites pass
    unmodified.

Interactive (editor; needs a display):

12. **Exactness by hand (US1)** — set an actor's name and transform and toggle its
    overlay; delete it; undo: all three return. Redo and undo again: still exact.
13. **Trigger editing (FR-004)** — drag a trigger's radius, type a prompt, toggle
    interactable and enabled; each is one undo step; delete and undo: the shape,
    prompt, and flags all return.
14. **Author a chain and delete mid-chain (US2, SC-007)** — place three waypoints,
    link them from the list, delete the middle one: the connectors show the chain
    stopping there. Undo: the whole chain, and its connectors, return.
15. **Copy a linked waypoint (FR-013)** — duplicate a linked waypoint: a connector
    appears from the copy to the same successor. Delete that successor: both the
    original's and the copy's connectors disappear.
16. **Nothing else changes (FR-011, FR-012, SC-006)** — rename, move, duplicate, and
    delete entities as before; switch zones and confirm each keeps its own contents
    and its own undo history; confirm play mode is unaffected and the stats line shows
    the frame rate unchanged.

## Expected outcome

- Deleting and undoing any entity restores it exactly, with no authored property
  silently reset — including an actor's collider overlay, a trigger's authored shape,
  and a waypoint's link.
- Every kind is created, destroyed, and copied through its own handling; there is no
  path that records less than a kind's full authored state. A property added without
  handling fails the build; a kind added without a path is named by the compiler as an
  unhandled case.
- A waypoint's link is part of the waypoint: it survives deletion and undo, is carried
  by a copy, never resolves to a waypoint it was not made to, and dies with its
  waypoint.
- Nothing about selection, transform, rename, environment swap, zone switch, physics,
  rendering, or gameplay behaviour changes.
- `dotnet build` is clean and `dotnet test` passes (the existing suite with the three
  tests above rewritten).
