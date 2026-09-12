# Quickstart: Entity Reference Integrity

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- The game/editor runs via `dotnet run` (see the repository `README.md`).

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj
```

Automated coverage added by this feature:

- `test/SlotMapTests.fs` — class API, generation reporting, `IsAlive`/
  `IsOccupied`/`TryGet`/`Occupy` (including restore-after-recycle and
  raise-on-occupied).
- `test/ZoneTests.fs` — per-kind ref resolution, stale-ref non-aliasing, epoch
  scoping across environments, and the real `loadEnvironment`/`setEnvironment`
  lifecycle (bodies created/destroyed, slots retained, refs revived).
- `test/SpawningTests.fs` — duplicate pairing (copy selected + reversing command),
  loud failure for non-entity kinds, and the occupied-slot conflict on restore.

## Validation scenarios

1. **Slot reuse** — delete an actor, spawn a new one that reuses the freed slot,
   then transform/delete through the old reference; the new actor must be unchanged
   and the old reference must resolve to nothing.
2. **Environment scoping** — load an environment, note its parts under the
   outliner's Parts section, load a different environment; placed props and actors
   are unchanged and the first environment's part references resolve to nothing.
3. **Selection reconcile** — select a part, swap environments; the selection is
   dropped and the Inspector shows no entity (not a dangling name).
4. **Undo a delete** — delete an entity, undo; it reappears and the same reference
   resolves to it again; redo deletes it.
5. **Undo an environment swap** — load a second environment, undo; the first
   environment returns with its part references live; redo swaps again.
6. **Restore conflict** — attempt to restore a deleted entity into a slot now taken
   by a different live entity; an error is reported and the occupant is intact.
7. **Undo-paired creation** — spawn/duplicate from the asset browser; a single undo
   removes the new entity, a single redo re-creates it with the same identity.

## Expected outcome

- `dotnet build` is clean; `dotnet test` passes with the new suite (89 tests as of
  this feature).
- Stale references never act on a different entity; environment swaps never disturb
  placed content; every editor creation is reversible in one step.
