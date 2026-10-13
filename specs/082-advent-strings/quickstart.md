# Quickstart: Advents as an Ordered String Set

## Prerequisites

- .NET 10 SDK; the vendored native libraries present (`native/`) for the editor.
- No asset setup needed: an advent is a string with no vocabulary and no asset behind it.

## Automated (headless)

```bash
dotnet build
dotnet test
```

Expected: a clean build with **no warnings** and the suite green, with no device opened.

- **the ordered set**: `AdventTests` adds names in an order and asserts the set reads back in that order, that a
  repeated name is a no-op, that a name is trimmed, and that a blank-after-trim add is refused.
- **one step per change**: `AdventTests` asserts an add is exactly one `Advent` step (empty before, one-name after),
  that undo restores the prior set and redo reapplies it, and that the zone-level runner refuses the step.
- **survives a zone switch**: `AdventTests` sets a name, switches zone, and asserts it is still set.
- **the ops over strings**: `CueTests` runs `AddAdvent`/`RemoveAdvent`/`Await`/`If` with string milestones and keeps
  the **allocation guard** — an in-flight `Await` adds **0 bytes per step** (proves `OSet.contains`).
- **the panel's pure halves**: `CuePanelTests` asserts the advent kinds, the `""` default for a new op, the labels,
  and the `If` children — all over strings.

## Manual

1. **Name an advent**: open the **Advent** panel (right dock); type `TheOpeningIsOver` and click **Add** — it appears
   in the list. Add a second name and note the list keeps the order you added them in.
2. **Refuse a bad add**: click **Add** with the field empty — nothing happens; try to add a name already listed — the
   Add control is disabled.
3. **Remove one**: click **Remove** on a row — it leaves the set and the list.
4. **Undo/redo**: `Ctrl+Z` removes the last addition (and `Ctrl+Y` reapplies it); the set is on the same history as a
   zone move.
5. **Cue with a string**: add an `Await` op in the Cue panel; its chooser offers the names you added and narrows as
   you type, and it also accepts a name you type that is not in the set (the set is unchanged by typing).
6. **No vocabulary anywhere**: there is no dropdown of fixed milestones and no compile-time milestone type; a typo
   simply does not match (an `Await` waits, an `If` takes its else branch).

## References

- Contract: [contracts/advent-set.md](./contracts/advent-set.md)
- Data model: [data-model.md](./data-model.md)
- Research (the design decisions): [research.md](./research.md)
