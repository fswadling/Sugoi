# Contract: The Scenario Library — Scenarios as Strings

What the game says about a scenario after the closed union is retired (`specs/083`), and what the editor offers.
Supersedes the "closed union" contract of `specs/070`/`specs/078`.

## The vocabulary — formerly `world/Scenario.fs`

- **Removed.** There is no `Scenario` union, no `scenarioName`, no `all`, and no reflection. A scenario is a **plain
  string name**. No game code enumerates a scenario vocabulary (spec FR-012, SC-005).

## The library — `world/ScenarioRegistry.fs`

```fsharp
type Registry = OMap<string, ScenarioContents option>   // Prime's ordered persistent map: insertion order, keyed by name
```

- **Keyed by name**: the keys are the vocabulary. A key present with `None` is a scenario **named but not
  registered**; `Some contents` is a registered one.
- **Ordered**: iterates in the order names were added (`ScenarioRegistry.names`).
- **Persistent**: a change produces a new library, so a before/after pair is a complete undo snapshot and an overwrite
  keeps the key's position.
- **Operations**: `create` / `names` / `exists` / `tryContents` / `add` / `remove` / `setContents` / `normalise`
  (`add` is a no-op when the name is present, so a duplicate add never clears a registered scenario's contents);
  `capture` reads the live scene (unchanged).
- **Starts empty**: the former `Intro` case is not seeded.
- **Not persisted**: session state; nothing is written to disk (phase 15).

## `GameplayState.Scenarios : ScenarioRegistry.Registry`

- **Mutable**: `OMap` is a value, so a change stores a new library.
- **Gameplay-scoped**: it lives on the gameplay world, not `SharedState` — only the gameplay screen and its editor read it.
- **Zone-independent**: it survives a zone switch.

## The undo step — `Command.SetScenario`

- Payload: `Previous : ScenarioRegistry.Registry * Next : ScenarioRegistry.Registry` (the whole library, before and
  after; `OMap` is persistent, so a snapshot is a reference pair).
- **State-level**: `Command.placeOf` returns `None`; the zone-level runner refuses it; `History` carries it.
- One accepted add, delete, clear or register is exactly one step.

## The cue op — `world/Cue.fs` / `world/CueEngine.fs`

| Op | Payload | Behaviour (unchanged from `specs/078`/`specs/079`) |
|----|---------|----------------------------------------------------|
| `ForkScenario` | `string` | materialise a registered scenario into the scene (ensure + stage its zones, append its entities) and fork its cue beside the running one; ensure semantics and non-idempotence unchanged |

- A fork naming an **absent or unregistered** scenario performs nothing and reports why (a diagnostic no-op).
- A fork whose target zone **lacks room** refuses with a report and changes nothing at all.
- The runtime op adds no step to the history; the panel's Fork control is one undoable step (a `Batch`).

## The editor

- **Scenario panel**: a single-line text field + **Add**, and a **list** of the current scenarios, each row with
  **Register**, **Clear**, **Fork** and **Delete**. Add is disabled for a blank-after-trim name or one already
  present; adding clears the field. Register files the current scene, Clear empties a scenario's contents but keeps
  its name, Fork (enabled only when registered) materialises it, Delete removes the name and its contents. Each change
  is one `SetScenario` step.
- **Cue scenario chooser**: offers the **current** scenario names (filterable) and accepts a **typed** name matching
  none — the `specs/074` speaker / `specs/082` milestone rule. Typing a name **does not** add it to the library. A new
  fork op starts with an empty name.
- **Name identity**: trimmed of surrounding whitespace, then matched exactly (ordinal, case-sensitive).

## Postconditions

- **No union, no vocabulary**: no `Scenario` union, no `all`, no `scenarioName`; no game code enumerates scenarios;
  the cue op names a string (spec FR-008, FR-012, SC-005).
- **Ordered determinism**: the library reads in authored order across runs (spec FR-001, FR-011).
- **One step per change**: undo/redo restore the exact before/after library (spec FR-010, FR-011, SC-004).
- **A duplicate add is a no-op**: naming an already-present scenario leaves the library — and any registered contents
  — unchanged (spec FR-007).
- **Fork unchanged**: a registered scenario materialises and forks as before, and a fork that cannot fit changes
  nothing (spec FR-014, FR-015, SC-007).

## Explicitly unchanged / out of scope

- **Fork semantics** (`specs/078`/`specs/079`) — the same ensure/append/fork behaviour, non-idempotence and capacity
  refusal; only the payload is a string.
- **The one history** (`specs/048`) — the `SetScenario` step stays state-level; nothing about undo changes.
- **The dock layout** — the panel window keeps its name, so the native right-dock is untouched.
- **Persistence** — deferred to phase 15; a future save serialises an ordered list of name-and-contents records.
- **Spelling / cross-reference checking** — deliberately absent; the author owns correctness (spec FR-013).
