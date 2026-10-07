# Data Model: Autocomplete Cue Combos

## `render/ImGui.fs` — three bindings

```fsharp
// All three are already exported by the vendored libcimgui.so (native/cimgui/cimgui.h):
//   igIsWindowAppearing, igSetKeyboardFocusHere, igCloseCurrentPopup
val isWindowAppearing : unit -> bool          // true the frame a popup/child window first appears
val setKeyboardFocusHere : offset:int -> unit // focus the *next* widget
val closeCurrentPopup : unit -> unit
```

## `state/EditorState.fs` — one reused buffer

```fsharp
ComboFilter : byte[]   // the open dropdown's filter text; cleared when the popup appears
```

## `editor/CueTree.fs` — the control

```fsharp
/// Whether `name` is offered for `filter`: a case-insensitive substring match (an empty filter offers all).
/// Pure, so it is asserted headlessly.
val matches : filter: string -> name: string -> bool

/// A combo with a filter field: type to narrow `names` (case-insensitive substring); Enter takes the top
/// match and closes the dropdown; a click picks as today. The buffer belongs to the caller; it is cleared
/// when the dropdown appears.
val filteredCombo
    : buffer: byte[]
    -> label: string
    -> preview: string
    -> names: string list
    -> current: string
    -> onPick: (string -> unit)
    -> unit
```

`drawEditor` gains the filter buffer as a parameter. Its character, place, advent, presentation (speaker /
expression) and look combos call `filteredCombo`. The **kind** combo keeps its grouped drawing while the filter
is empty and draws a flat `matches`-filtered list otherwise.

## `editor/CueEditor.fs` — the panel's name combos

`drawName` (the clip/morph chooser — combo when the asset resolves names, typed field otherwise),
`drawWaypointCombo` and `drawTriggerCombo` call `CueTree.filteredCombo` with the shared buffer. The typed
fallbacks are unchanged.

## Validation rules

- **Empty filter offers all**: the default state of every dropdown is its full list.
- **Case-insensitive substring**: `matches "jo" "Jog"` is true; the order is the field's own.
- **No match ⇒ empty list**: Enter is a no-op and the current value is untouched.
- **Same edit**: a pick calls the same `commit (withAt …)` as before — one undo step, no op changes.
- **No carry-over**: the buffer is cleared when a dropdown appears.
