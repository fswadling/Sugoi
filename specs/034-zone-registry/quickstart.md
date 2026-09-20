# Quickstart: The Zone Registry

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- A display, for the interactive scenarios only. The feature's central claim is asserted headlessly
  (scenario 2 below), so unlike `specs/033-cast-spawn` there is something to validate without one;
  the panel's drawing is the part that needs a screen.

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj
```

`dotnet build --no-incremental` is the build to run: it reports warnings on unchanged files, which an
incremental build hides.

## Validation scenarios

Automated (headless):

1. **The vocabulary is drawable (FR-001, FR-002, SC-001)** — `test/ZoneRegistryTests.fs` walks
   `ZoneRegistry.byZone`: every zone in the union appears once, every zone has at least one place,
   and no two places share a name. This is the check that fails if the reflection walk and the union
   stop agreeing, which is the failure mode deriving the tree from the declaration is exposed to.
2. **A registration reproduces the zone (FR-012, SC-002, SC-003)** — the round trip
   (`test/ZoneTests.fs`), and the one to read if only one is read: load a room asset into a zone, move
   one part, rename another and delete a third, take `ZoneTemplate.ofEnvironment`, load the record
   into a *fresh* zone with `Zone.loadEnvironmentFrom`, and compare — the same asset, the same
   settings, the same parts, with the deleted part still absent and the moved part still where it was
   put. Nothing here needs a display: a null GPU device is never dereferenced on this path.
3. **The map is sparse and its places are independent (FR-003, FR-008, SC-007)** — an unregistered
   place answers `None`; registering one answers it; registering again replaces it; and writing one
   place changes no other.
4. **A registration is a copy, not a link (FR-009, FR-010, SC-006)** — mutating the environment after
   it is recorded does not change the record, and the record does not change the environment.
5. **No existing behaviour changes (FR-016, SC-008)** — the whole existing suite passes unmodified,
   in particular `test/SpawningTests.fs`'s part and environment paths, which share the builder this
   feature parameterised.

Interactive (editor; needs a display):

6. **The panel is where the docs say (FR-002)** — the Zone Registry appears as a tab in the left
   node, beside the Outliner and Cast, and Ctrl+Tab cycles to it. If it floats instead, the native
   library was not rebuilt (`research.md` Decision 10).
7. **The tree is the vocabulary (FR-001)** — expand a zone and see one row per variant it declares;
   a zone with a larger variant set shows more rows and nothing forces two zones to agree.
8. **Register a zone (FR-005, FR-006, SC-001)** — with an environment loaded in the active zone,
   **Register** is enabled against every place; activate it and the row's second column changes from
   `(none)` to that asset's name. With no environment loaded, every **Register** is greyed out.
9. **Registering is not a scene edit (FR-009, SC-005)** — Ctrl+Z after a registration does not remove
   it and does not undo anything in the scene.
10. **Instantiate a zone (FR-011, FR-012, SC-002)** — load something else into the zone, then
    **Instantiate** the place registered in 8: the registered environment comes back part for part.
    A place with no registration draws its **Instantiate** greyed out.
11. **Instantiating is one step (FR-013, SC-004)** — Ctrl+Z restores exactly what the zone held
    before the instantiation, and nothing else moves.
12. **Two places are independent (FR-008, SC-007)** — register two variants of one zone from two
    different arrangements, then instantiate each: each reproduces its own, and re-registering one
    leaves the other as it was.
13. **The registry outlives its zone (FR-010)** — register a place, delete the zone the registration
    came from, and confirm the entry and its **Instantiate** are untouched.
14. **No ImGui ID collision (research.md Decision 9)** — expand every zone and watch the log: with
    both buttons drawn on every row, a conflict would be reported as *N visible items with
    conflicting ID*. `specs/033-cast-spawn` shipped exactly that and only the running editor could
    see it.

## Expected outcome

- The game's places are one union, each zone declaring its own variants, and the panel shows exactly
  what that declaration says — adding a zone or a variant needs no panel change.
- A place holds the asset it was registered with, the environment's settings, and its parts as they
  stood, deletions included; the same record loaded back reproduces them.
- Registering is a direct write with no undo step and no effect on the scene; instantiating is one
  undoable scene edit. The two sit on one row and behave differently on purpose.
- Nothing about props, waypoints or triggers is recorded, and the record's shape leaves room for them
  as siblings of the parts.
- `dotnet build --no-incremental` is clean and `dotnet test` passes, with every existing assertion
  unmodified.
