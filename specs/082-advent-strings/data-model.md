# Data Model: Advents as an Ordered String Set

## `world/Advent.fs` — the set, not a union

```fsharp
// before (spec 055)
type Advent =
    | OpeningComplete | MetAiko | MetAkane | MetShinji | MetYuki
let all : Advent list = ...        // reflection enumeration
let name (advent: Advent) : string = ...  // the case name

// after
type Set = OSet<string>            // Prime's ordered persistent set — insertion order, de-duplicated

let empty    : Set                    = OSet.ofSeq1 []
let contains (name: string) (set: Set) : bool = OSet.contains name set
let add      (name: string) (set: Set) : Set  = OSet.add name set
let remove   (name: string) (set: Set) : Set  = OSet.remove name set
let names    (set: Set)               : string list = OSet.toSeq set |> List.ofSeq
let normalise (name: string)          : string = name.Trim()
```

No union, no `all`, no `name`, no reflection. `open Prime`.

## `state/SharedState.fs`

```fsharp
// before
{ mutable Gpu : Gpu.Gpu
  Assets : Assets.AssetManager
  mutable Advents : Set<Advent> }

// after
{ mutable Gpu : Gpu.Gpu
  Assets : Assets.AssetManager
  mutable Advents : Advent.Set }   // starts Advent.empty (no seeding)
```

## `state/Command.fs` — the step's payload

```fsharp
// before
| Advent of Previous : Set<Advent> * Next : Set<Advent>

// after (same shape; OSet is persistent, so a reference snapshot is complete)
| Advent of Previous : Advent.Set * Next : Advent.Set
```

`Command.placeOf` still returns `None` for it; `Command.apply`/`undo` still refuse it as a zone-level step.
`state/History.fs` assigns `next`/`previous` unchanged.

## `world/Cue.fs` — the four ops carry a string

```fsharp
// before (spec 056)
| AddAdvent of advent: Advent
| RemoveAdvent of advent: Advent
| Await of advent: Advent
| AwaitState of advent: Advent
| If of advent: Advent * thenCue: Cue * elseCue: Cue

// after
| AddAdvent of advent: string
| RemoveAdvent of advent: string
| Await of advent: string
| AwaitState of advent: string
| If of advent: string * thenCue: Cue * elseCue: Cue
```

`world/Cue.fs` no longer `open Advent`.

## `world/CueEngine.fs` — the run

```text
AddAdvent n     -> shared.Advents <- Advent.add n shared.Advents        // no progress, cold path
RemoveAdvent n  -> shared.Advents <- Advent.remove n shared.Advents     // no progress, cold path
Await n         -> if Advent.contains n shared.Advents then ValueSome Fin else ValueSome(AwaitState n)
AwaitState n    -> if Advent.contains n shared.Advents then ValueSome Fin else ValueNone   // per tick, 0 bytes
If (n,t,e)      -> advance … (if Advent.contains n shared.Advents then t else e)
```

Behaviour is unchanged from `specs/056`; only the payload type and the membership primitive change.

## Editor

### `state/EditorState.fs` — a new buffer

```fsharp
AdventNameBuffer : byte[]    // NEW — the Advent panel's text field, beside ZoneNameBuffer; sized entityNameBufferSize
```

### `editor/AdventPanel.fs` — rewritten

```text
[ text field ]  [ Add ]        // Add disabled when blank-after-trim or already present; clears the field on add
───────────────────────────
OpeningComplete      [ Remove ]
TheOpeningIsOver     [ Remove ]
(no rows -> "(none)")
```

`commit shared state (next: Advent.Set)` keeps its shape: read `before <- shared.Advents`, then
`History.commit shared state (Command.Advent (before, next))`. Each Add and each Remove is one step.

### `editor/CueTree.fs` — labels, defaults, chooser

| Concern | Before | After |
|---------|--------|-------|
| `label` | `"Add: " + Advent.name advent` | `"Add: " + name`, suffix omitted when `name = ""` |
| `defaultOfKind` | a new advent op defaults to `List.head Advent.all` | defaults to `""` |
| milestone control | `filteredCombo … Advent.all Advent.name` | `filteredNameCombo … advents` (pick **or** type) |
| `withAdvent` | takes an `Advent` | takes a `string` |
| `drawEditor` params | `(…, names: string list, …)` — actor names | gains `advents: string list` |

`editor/CueEditor.fs` passes `Advent.names shared.Advents` into `CueTree.drawEditor` (both `draw` and `drawTrigger`).

## Validation rules

- **Ordered, de-duplicated**: the set reads in the order names were added; adding a present name changes nothing
  (member of the set).
- **Trimmed, then exact**: a typed name is trimmed of surrounding whitespace, then matched exactly (ordinal,
  case-sensitive). A blank-after-trim add is refused.
- **One step per change**: every accepted add and remove is a single `Advent` step (before/after sets) on the one
  history; undo restores the exact before set, redo the exact after, including order.
- **No vocabulary, no cross-check**: a cue's milestone string is never validated against the set; typing a name in a
  cue does not add it; removing a name does not rewrite or warn about cues.
- **Nothing persisted**: the set is session state; nothing is written to disk.
- **Hot path untouched**: the per-frame membership check is `OSet.contains` (0 bytes).
