# Contract: The Advent Set — Milestones as Strings

What the game says about a story milestone after the closed union is retired (`specs/082`), and what the editor
offers. Supersedes the "closed union" contract of `specs/055`/`specs/056`.

## The vocabulary — `world/Advent.fs`

```fsharp
type Set = OSet<string>          // Prime's ordered persistent set: insertion order, de-duplicated
```

- A milestone is a **plain string**. There is no union, no enumeration, no display-name mapping.
- `add`/`remove`/`contains`/`names`/`empty`/`normalise` (trim) are the whole surface.

## The set — `SharedState.Advents : Advent.Set`

- **Ordered**: iterates in the order names were added (`Advent.names`).
- **Starts empty**: the former union cases are not seeded.
- **Zone-independent**: it outlives a zone switch (`specs/055` FR-002).
- **Not persisted**: session state; nothing is written to disk.

## The undo step — `Command.Advent`

- Payload: `Previous: Advent.Set * Next: Advent.Set` (the set is persistent, so a snapshot is a reference pair).
- **State-level**: `Command.placeOf` returns `None`; the zone-level runner refuses it; `History` carries it.
- One accepted add or remove is exactly one step.

## The cue ops — `world/Cue.fs` / `world/CueEngine.fs`

| Op | Payload | Behaviour (unchanged from `specs/056`) |
|----|---------|----------------------------------------|
| `AddAdvent` | `string` | add the name where reached; no progress; no-op when present |
| `RemoveAdvent` | `string` | remove the name; no progress; no-op when absent |
| `Await` / `AwaitState` | `string` | complete when the name is in the set, re-checked each step; begun form is `AwaitState` |
| `If` | `string * Cue * Cue` | run the then-branch when the name is in the set, else the else-branch |

The per-step membership check is `OSet.contains` — allocation-free.

## The editor

- **Advent panel**: a single-line text field + **Add**, and a **list** of the current advents, each with **Remove**.
  Add is disabled for a blank-after-trim name or one already present; adding clears the field. Each change is one
  `Advent` step.
- **Cue milestone chooser**: offers the **current** advent names (filterable) and accepts a **typed** name matching
  none — the `specs/074` speaker-chooser rule. Typing a name **does not** add it to the set. A new advent op starts
  with an empty name.
- **Name identity**: trimmed of surrounding whitespace, then matched exactly (ordinal, case-sensitive).

## Postconditions

- **No union, no vocabulary**: no `Advent` union, no `all`, no `name`; no game code enumerates milestones; the cue
  ops name a string (spec FR-006, FR-011, SC-005).
- **Ordered determinism**: the set reads in authored order across runs (spec FR-001, FR-009).
- **One step per change**: undo/redo restore the exact before/after set (spec FR-008, FR-009, SC-004).
- **Allocation-free await**: the in-flight `Await` adds 0 bytes per step (project Principle V).

## Explicitly unchanged / out of scope

- **Cue-op behaviour** (`specs/056`) — the same no-op, wait and branch semantics; only the payload is a string.
- **The one history** (`specs/048`) — the `Advent` step stays state-level; nothing about undo changes.
- **The dock layout** — the panel window keeps its name, so the native right-dock is untouched.
- **Persistence** — deferred to phase 15; a future save serialises the names as an ordered string list.
- **Spelling / cross-reference checking** — deliberately absent; the author owns correctness (spec FR-012).
