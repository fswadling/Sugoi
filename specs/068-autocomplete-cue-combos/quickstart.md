# Quickstart: Autocomplete Cue Combos

## Prerequisites

- .NET 10 SDK; the vendored native libraries present (`native/`).
- A cue with a node selected (a placed actor/zone helps populate the choosers, but the match rule needs none).

## Automated (headless)

```bash
dotnet build
dotnet test
```

Expected: a clean build with no warnings and the suite green, no device opened. `test/CuePanelTests.fs` asserts
the pure match rule:

- an empty filter offers every name;
- a substring matches case-insensitively (`jo` matches `Jog`);
- a filter that matches nothing yields an empty list.

The combos themselves are device-drawn (as the panel's drawing already is) and are exercised by the walkthrough.

## Manual

1. **Name combo**: select a cue node with a clip field, open the clip dropdown, and type — the list narrows;
   press Enter to take the top match or click one. The node updates and the edit is one undo step.
2. **Kind combo**: open the op-kind dropdown, type an op name (e.g. `trigger`) — the grouped list flattens to the
   matches; clear the filter and the groups return.

## References

- Contract: [contracts/filtered-combo.md](./contracts/filtered-combo.md)
- Data model: [data-model.md](./data-model.md)
- Research (the design decisions): [research.md](./research.md)
