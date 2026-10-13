# Quickstart: Scenarios as a String-Keyed Ordered Library

## Prerequisites

- .NET 10 SDK; the vendored native libraries present (`native/`) for the editor.
- No asset setup needed for the library: a scenario name is a string with no vocabulary behind it. (A scenario's
  *contents* may reference assets, but registering a scene with none is valid.)

## Automated (headless)

```bash
dotnet build
dotnet test
```

Expected: a clean build with **no warnings** and the suite green, with no device opened.

- **the string-keyed library**: `ScenarioTests` adds names in an order and asserts `ScenarioRegistry.names` reads
  back in that order, that a repeated name is a no-op, that a name is trimmed, that a blank-after-trim add is refused,
  and that a deleted name leaves the library.
- **register and clear**: `ScenarioTests` registers the current scene under a name and asserts its contents; clears
  it and asserts the name remains with no contents; deletes it and asserts the row is gone.
- **one step per change**: `ScenarioTests` asserts add/delete/clear/register is exactly one `SetScenario` step, that
  undo restores the prior library and redo reapplies it, and that the zone-level runner refuses the step.
- **survives a zone switch**: `ScenarioTests` registers a scenario, switches zone, and asserts it is still registered.
- **the fork op over a string**: `ScenarioForkTests` forks `ForkScenario "Intro"` and asserts the scenario's entities
  are appended and its cue forks beside the run; an unregistered name is a diagnostic no-op; a full target zone
  refuses the whole fork.
- **the panel's pure halves**: `CuePanelTests` asserts the fork op's `""` default and that its label reads the raw
  name (omitted when empty); `CueTests` runs a fork op naming a string not in the library and asserts it is authored
  without error and runs to its documented no-op.

## Manual

1. **Name a scenario**: open the **Scenario** panel (right dock); type `Intro` and click **Add** — it appears in the
   list. Add a second name and note the list keeps the order you added them in.
2. **Refuse a bad add**: click **Add** with the field empty — nothing happens; try to add a name already listed — the
   Add control is disabled.
3. **Register**: with a scene built in the viewport, click **Register** on a row — it now reports `N zone(s)` (the
   present zones) and the screen's cue is filed with it.
4. **Fork**: click **Fork** on a registered row — the scenario's zones' entities appear in the scene and its cue runs
   beside the current one, as one undoable step. `Ctrl+Z` reverses it.
5. **Clear vs Delete**: **Clear** empties a registered row (it returns to `(none)` but stays listed); **Delete**
   removes the row entirely.
6. **Undo/redo**: `Ctrl+Z` reverses the last add/delete/clear/register (and `Ctrl+Y` reapplies it); the library is on
   the same history as a zone move.
7. **Cue with a string**: add a "Fork scenario" op in the Cue panel; its chooser offers the names you added and
   narrows as you type, and it also accepts a name you type that is not in the library (the library is unchanged by
   typing).
8. **No vocabulary anywhere**: there is no dropdown of fixed scenarios and no compile-time scenario type; a typo
   simply does not match (the fork performs nothing and reports why).

## References

- Contract: [contracts/scenario-library.md](./contracts/scenario-library.md)
- Data model: [data-model.md](./data-model.md)
- Research (the design decisions): [research.md](./research.md)
