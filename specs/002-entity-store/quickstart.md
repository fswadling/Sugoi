# Quickstart: Entity Store

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK (installed).

## Build & test

```sh
dotnet build
dotnet test
```

## Validation scenarios

1. **Slot map correctness** — `dotnet test` runs `SlotMapTests` proving add/get/
   free, free-list reuse, and stale-handle rejection.
2. **Entity model** — `dotnet test` runs `EntityTests` proving each entity kind
   composes the shared base.
3. **Game still runs** — `dotnet run` still launches the main menu (the new
   `core/` types compile but are not yet used by the loop).

## Expected outcome

- `dotnet build` is clean (2 expected native-pointer warnings in `Engine.fs`).
- `dotnet test` is green (all slot-map and entity tests pass).
- `dotnet run` still launches into the main menu.
