# Data Model: Command / Undo

## Zone (mutable) — `core/Zone.fs`

`Zone` becomes a mutable reference type so edits happen in place:

```fsharp
[<Struct>]
type Environment =
    { Base : Entity
      mutable Props : SlotMap<Prop> }

type Zone =
    { Name : string
      mutable Actors : SlotMap<Actor>
      mutable Environment : Environment }
```

Entity CRUD mutates in place and returns `unit` (spawn returns the new ref):

```fsharp
entityAt : Zone -> EntityRef -> Entity option      // read (unchanged)
setEntity : Zone -> EntityRef -> Entity -> unit    // write at ref (in place)
removeEntity : Zone -> EntityRef -> unit           // free at ref (in place)
spawnActor : Zone -> Entity -> EntityRef           // add + return ref
spawnProp : Zone -> Entity -> EntityRef            // add + return ref
```

- `setEntity`/`removeEntity` dispatch on `ref.Kind` and mutate `zone.Actors` or
  `zone.Environment.Props` via `SlotMap.setByIndex`/`removeByIndex` (which already
  mutate the shared slot array in place).
- `spawnActor`/`spawnProp` use `SlotMap.add`, write the result back to the zone, and
  return `{ Kind; Index }`.

## Command — `editor/Command.fs`

```fsharp
type Command =
    | Spawn    of { Ref : EntityRef; Entity : Entity }
    | Delete   of { Ref : EntityRef; Entity : Entity }
    | Duplicate of { Ref : EntityRef; Entity : Entity }
    | Transform of { Ref : EntityRef; Before : Transform; After : Transform }

apply : Zone -> Command -> unit
undo  : Zone -> Command -> unit
```

| Command | apply | undo |
|---|---|---|
| `Spawn` / `Duplicate` | `setEntity ref entity` | `removeEntity ref` |
| `Delete` | `removeEntity ref` | `setEntity ref entity` |
| `Transform` | set transform to `After` | set transform to `Before` |

`Transform` no-ops when `entityAt zone ref = None` (a command targeting a
since-deleted entity degrades safely); the structural commands use the LIFO
symmetry of the stack so `removeEntity`/`setEntity` always hit the expected
live-or-free slot.

## Undo stack — `editor/Command.fs`

```fsharp
type UndoStack =
    { Applied : Command list
      Redo : Command list }

module UndoStack =
    val create : unit -> UndoStack
    val push   : UndoStack -> Command -> UndoStack
    val undo   : UndoStack -> Zone -> UndoStack
    val redo   : UndoStack -> Zone -> UndoStack
```

- `push` merges a `Transform` onto the top of `Applied` when both target the same
  `EntityRef` (same `Kind` + `Index`), keeping the first `Before` and latest
  `After`; a non-merge push clears `Redo`.
- `push`/`undo`/`redo` return a new `UndoStack` (the lists are immutable). `undo`
  pops from `Applied`, calls `undo`, and pushes onto `Redo`; `redo` is the mirror.

## EditorState — `editor/EditorState.fs`

```fsharp
type EditorState =
    { Selection : EntityRef option
      PendingFocus : string option
      Tool : Tool
      Snap : bool
      Drag : Drag option
      DockingInitialized : bool
      Undo : Command.UndoStack            // NEW — undo history
      NudgeBefore : Transform option }    // NEW — nudge coalescing "before"
```

`create()` builds a fresh `UndoStack` and `NudgeBefore = None`. `Drag.Start` already
holds the pre-drag transform (the gizmo `Transform` command's `Before`).

## Relationships

- `GameplayState` (struct) holds the shared `Zone` reference; the editor mutates the
  zone in place through `Command.apply`/`undo` and returns the struct only for its
  own transient `EditorState` changes.
- `Command` depends on `Zone` + `Entity`; `EditorState` depends on `Command`.
  `GameplayState` depends on `EditorState`; `Editor`/`Selection`/`AssetBrowser`
  depend on `Command` + `GameplayState`. No cycle.
