# Quickstart: Selection + Transform Foundations

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK (installed).

## Build & test

```sh
dotnet build
dotnet test
```

## Validation scenarios

1. **Entity storage lifecycle** — `dotnet test` runs `SlotMapTests.fs`, which
   proves add → read → remove → reuse, index-based read/write/remove, restore at
   a freed index, and the fail-loudly guards (remove-dead and out-of-range).
2. **Entity composition** — `EntityTests.fs` proves actors/props compose the base
   entity and the environment now owns a props collection.
3. **Editor state defaults** — building `EditorState.create ()` yields "move tool,
   no snap, no selection, no drag" (verified by inspection / future gizmo tests).
4. **Debug-draw shapes** — running `dotnet run` and opening the editor still
   renders the grid; the new `wireframeBox`/`sphere` primitives compile and are
   exercised by the next phase's markers/gizmos.

## Expected outcome

- `dotnet build` is clean (2 expected native-pointer warnings).
- `dotnet test` passes (27 tests at time of writing).
- No per-frame allocation was introduced in the debug-draw accumulator path.
