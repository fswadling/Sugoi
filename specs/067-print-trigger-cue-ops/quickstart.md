# Quickstart: Print & Trigger Cue Ops

## Prerequisites

- .NET 10 SDK; the vendored native libraries present (`native/`).
- A trigger placed in a zone (any place) for the toggle scenarios; no asset is needed (a trigger carries no
  renderer or body).

## Automated (headless)

```bash
dotnet build
dotnet test
```

Expected: a clean build with no warnings and the suite green, no device opened. The tests drive
`CueEngine.tick` against a `GameplayState`, so the real paths run headlessly:

- `DisableTrigger (place, name)` flips the trigger's `Enabled` to false, and the next `Systems.updateTriggers`
  leaves it neither firing nor tracking the player; `EnableTrigger` restores it;
- a redundant toggle, a name the place holds no trigger for, and a place that is not present all perform nothing
  (and write their reason to stderr);
- `Print` is a no-progress leaf — it is done the tick it is reached and leaves no state;
- none of the three records a step on `state.Editor.Undo`;
- `Zone.tryTriggerByName` returns the first live trigger with a matching name.

`test/CuePanelTests.fs` asserts the panel's pure half — the new labels, kinds and writes.

## Manual

1. **Toggle**: place a trigger (e.g. an area), author `DisableTrigger` naming its place and name, and Play —
   the trigger stops detecting the player; `EnableTrigger` starts it again.
2. **Print**: author `Print "chapter 1"` in a sequence and Play — the line appears on the console at the point
   the walk reaches it.

## References

- Contract: [contracts/cue-print-trigger-ops.md](./contracts/cue-print-trigger-ops.md)
- Data model: [data-model.md](./data-model.md)
- Research (the design decisions): [research.md](./research.md)
