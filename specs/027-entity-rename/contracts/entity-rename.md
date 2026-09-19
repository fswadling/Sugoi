# Contract: Entity Rename

Public surface added or changed by this feature. Signatures are illustrative F#.
A rename is one undoable command; the name is written through the zone (the only
module that touches entity storage) and recorded by the selection module (the
module that owns ref-addressed edits). The Inspector is a UI surface over those.

## Undo command — `state/Command.fs`

```fsharp
type Command =
    | ...
    // + new: one committed name change, with both sides recorded
    | Rename of Ref : EntityRef * Before : string * After : string
```

- `apply` writes `After`; `undo` writes `Before`; both via `Zone.setName`.
- `UndoStack.push` merges only consecutive `Transform` commands, so a rename is
  always exactly one undo step and is never merged into another edit.

## Zone — `world/Zone.fs`

```fsharp
// Change an existing entity's name at the ref (Actor/Prop/Part/Waypoint). No-op for
// other kinds or a stale/absent ref. A name is a label, not an identity, so the same
// name may be given to more than one entity.
val setName : zone:Zone -> ref:EntityRef -> name:string -> unit
```

- Writes the base entity's name in place for the addressed kind; touches no
  physics, renderer, binding, or `BodyToEntity` entry.
- `Actor`, `Prop`, `Waypoint`, and `Part` are supported; `Environment` and
  `Trigger` refs do nothing (there is no base entity to name).
- A stale or absent ref does nothing, matching `setTransform`.

## Selection — `editor/Selection.fs`

```fsharp
// Which entity a finished name-field edit should rename, and to what. `owner` is the
// entity the edited text was typed for (not necessarily the current selection), and
// `finished` is the field's commit signal. None means "commit nothing": nothing was
// being edited, the edit is not finished, or the text is empty.
val renameTarget : owner:EntityRef option -> finished:bool -> text:string -> (EntityRef * string) option

// Rename a specific entity as one undoable step: resolve it, build the command from
// its current name and `name`, apply it, and push it. No-op when the ref no longer
// resolves or the name is unchanged. Raises when `name` is empty or whitespace-only
// (the editor never commits one).
val renameRef : shared:SharedState.SharedState -> state:byref<GameplayState.GameplayState> -> ref:EntityRef -> name:string -> unit
```

- `renameTarget` is pure (no ImGui, no zone) so the attribution rule — that an edit
  goes to the entity it was typed for — is testable without a frame.
- `renameRef` leaves the selection, drag, and nudge state untouched.
- Does not call `Spawning` or create anything: a rename only ever changes a label
  on an entity that already exists.

## Editor state — `state/EditorState.fs`

```fsharp
// The Inspector's name field. Sized for UTF-8's worst case so seeding never needs a
// mid-character clamp, and a character-based limit so truncation never splits a
// character.
[<Literal>] val entityNameBufferSize : int = 256
[<Literal>] val entityNameMaxChars : int = 63

// Added fields, both set up in EditorState.create:
//   NameBuffer  : byte[]              the field's text (re-seeded every frame)
//   NameEditRef : EntityRef option    the entity the buffer's text belongs to
```

## Binding — `render/ImGui.fs`

```fsharp
// Already exported by the vendored libcimgui.so; no native rebuild.
[DllImport("cimgui", EntryPoint = "igIsItemDeactivatedAfterEdit", ...)]
extern bool isItemDeactivatedAfterEdit()

// ImGuiInputTextFlags values used by the name field (from cimgui.h):
let enterReturnsTrue = 1 <<< 6   // submitting the field reports true (Enter)
let autoSelectAll = 1 <<< 12     // opening the field selects the existing name
```

`inputText` is reused unchanged.

## Inspector — `editor/Inspector.fs`

```fsharp
// `shared` is new: the panel needs it to apply the rename command.
val draw : shared:SharedState.SharedState -> state:byref<GameplayState.GameplayState> -> unit
```

- The name row becomes an `inputText` field instead of read-only text; position,
  rotation, and scale stay read-only, and the actor-only collider toggle and *Set
  as Player* are unchanged.
- Per frame it seeds the buffer from the selected entity's name (recording that
  entity in `NameEditRef`), draws the field, and commits when the field is
  submitted (Enter) or left after an edit — never mid-typing.
- An empty or unchanged name is not committed. The text is applied to the entity
  the edit was typed for, which is not always the currently selected one.
- A rename is applied as one step, so one undo reverses one rename.
