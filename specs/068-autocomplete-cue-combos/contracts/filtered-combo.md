# Contract: Filtered Combo

The cue panel's dropdowns are searchable: open one, type, and the list narrows.

## The control — `editor/CueTree.fs`

```fsharp
val matches : filter: string -> name: string -> bool
/// true when `name` contains `filter` case-insensitively; an empty `filter` matches every name.

val filteredCombo
    : buffer: byte[]
    -> label: string
    -> preview: string
    -> names: string list
    -> current: string
    -> onPick: (string -> unit)
    -> unit
```

`filteredCombo` draws `beginCombo label preview`; inside it a filter `inputText` over `buffer`, then the
`selectableBool` rows for `names |> List.filter (matches (bufferText buffer))`, marking `current` selected.
On Enter it calls `onPick` with the top match and closes the popup. The buffer is cleared when the popup
appears, so a newly opened combo starts unfiltered.

## The kind combo

The **kind** combo does not use `filteredCombo` verbatim: while the filter is empty it draws its existing grouped
list (with separators); while a filter is entered it draws a flat `matches`-filtered list. The rows it draws are
the same `kindName` strings.

## Behaviour

| State | Shows |
|-------|-------|
| no filter | every option, in the field's own order (the kind combo: grouped) |
| filter entered | only options whose name contains it, case-insensitively (the kind combo: flat) |
| no matches | an empty list; Enter does nothing |
| Enter | the top match is picked and the dropdown closes |
| click | the clicked option is picked (the dropdown closes, as today) |

## Postconditions

- **One edit**: `onPick` commits exactly what the combo committed before — one step on the one undo history
  (`specs/049`); no op's value or meaning changes.
- **Focused on open**: typing filters immediately, without clicking the field first.
- **No carry-over**: a dropdown opened after another starts unfiltered.

## Explicitly out of scope

- Other panels' dropdowns (the Outliner, the Inspector, the Dialogue panel, the cast panel).
- Fuzzy matching, ranking or match highlighting.
- Any change to what a choice writes, or to the cue vocabulary.
