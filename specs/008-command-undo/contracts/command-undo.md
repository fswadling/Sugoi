# Contract: Command / Undo

Public surfaces added by this feature. Signatures are illustrative F#, matching
the data model.

## Zone CRUD (in place) — `core/Zone.fs`

```fsharp
entityAt : Zone -> EntityRef -> Entity option
setEntity : Zone -> EntityRef -> Entity -> unit
removeEntity : Zone -> EntityRef -> unit
spawnActor : Zone -> Entity -> EntityRef
spawnProp : Zone -> Entity -> EntityRef
```

## Command — `editor/Command.fs`

```fsharp
type Command =
    | Spawn    of { Ref : EntityRef; Entity : Entity }
    | Delete   of { Ref : EntityRef; Entity : Entity }
    | Duplicate of { Ref : EntityRef; Entity : Entity }
    | Transform of { Ref : EntityRef; Before : Transform; After : Transform }

apply : Zone -> Command -> unit
undo  : Zone -> Command -> unit

module UndoStack =
    val create : unit -> UndoStack
    val push   : UndoStack -> Command -> UndoStack
    val undo   : UndoStack -> Zone -> UndoStack
    val redo   : UndoStack -> Zone -> UndoStack
```

## Rules

- `apply`/`undo` mutate the zone in place; they never return a copy.
- `UndoStack.push`/`undo`/`redo` return a new `UndoStack` (they do not mutate the
  stack in place).
- `Transform.apply`/`undo` no-op when the entity is absent; `Spawn`/`Duplicate`/`Delete`
  rely on the stack's LIFO symmetry to always target a live-or-free slot.
- `UndoStack.push` merges a `Transform` onto the top of the stack only when both
  target the same `EntityRef` (`Kind` + `Index`), keeping the first `Before` and
  latest `After`; any other push clears the redo stack.
- `UndoStack.undo`/`redo` are no-ops when the relevant stack is empty.
- Undo/redo entry points (`Ctrl+Z` / `Ctrl+Y` and the Edit menu, editor-only) must
  not fire while a gizmo drag is in progress or an ImGui text field has focus.
