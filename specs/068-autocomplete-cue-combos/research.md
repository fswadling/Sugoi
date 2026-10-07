# Research: Autocomplete Cue Combos

## D1 — A filtered combo: the filter field inside `beginCombo`

**Decision**: keep `beginCombo`; inside it, draw a filter `inputText` as the first row, then only the
`selectableBool` rows whose name matches. Enter (`inputText`'s `enterReturnsTrue`) picks the top match and calls
`closeCurrentPopup`; a click on a row commits as today (ImGui closes the combo on a `selectableBool` click). The
filter field is focused on open via `isWindowAppearing` + `setKeyboardFocusHere(0)`.

**Rationale**: it needs no popup/listbox machinery and reuses the combo the whole panel already draws, so the
change is confined to the row loop. ImGui's more "native" route (`BeginPopup` + `BeginListBox` +
`ImGuiTextFilter`) needs more bindings and buys nothing here.

**Bindings**: the vendored `native/libcimgui.so` already exports `igIsWindowAppearing`,
`igSetKeyboardFocusHere` and `igCloseCurrentPopup` (verified in `native/cimgui/cimgui.h`), so each is a one-line
`DllImport` in `render/ImGui.fs` — no native rebuild.

**Alternatives considered**: an inline "search + list" control (an always-visible `inputText` with the filtered
list below, no popup) — rejected: it changes the panel's layout and the popup is the panel's established shape.
`ImGuiTextFilter` — not bound and unnecessary for a substring match.

## D2 — One shared helper and one pure predicate

**Decision**: a pure `CueTree.matches (filter: string) (name: string) : bool` (case-insensitive substring), and a
`CueTree.filteredCombo` that draws the filter and the matching rows over `beginCombo`. Every cue-panel combo —
the character, place, advent and presentation choosers in `CueTree`; the waypoint, trigger and clip/morph choosers
in `CueEditor` — calls the helper. The grouped **kind** combo keeps its own grouped drawing when the filter is
empty and uses `matches` to draw a flat filtered list otherwise.

**Rationale**: the combination of `beginCombo` + a loop is repeated at ~14 sites; one helper removes the
boilerplate and keeps the filter behaviour identical everywhere. The kind combo is the one that cannot use the
flat-only helper, because it must keep its separators while unfiltered.

**Case-insensitive substring, no ranking**: the top match is the first in the field's own order, so behaviour is
predictable and the rule stays a one-liner.

## D3 — One filter buffer, cleared when a dropdown opens

**Decision**: one reused buffer, `EditorState.ComboFilter : byte[]`, passed into `CueTree.drawEditor` (the control
names no editor state — `FR-006` of `specs/047`) and into the panel's own combos. It is cleared when the popup
appears (`isWindowAppearing`), so a newly opened combo starts unfiltered and a filter never carries between
combos.

**Rationale**: only one popup is open at a time across the whole UI, so one buffer is enough; clearing on appear
replaces the per-combo "owner id" bookkeeping the text fields need.

## D4 — The edit is exactly the choice the combo made before

**Decision**: `filteredCombo`'s `onPick` is the same `commit (withAt ...)` the combo called before, so a filtered
pick is one `SetCue` step on the one undo history and every op's value and meaning are unchanged.

**Rationale**: this is a way to *find* an option, not a new kind of option (spec Assumptions); the write path is
untouched.

## D5 — Verification is headless

**Decision**: assert `matches` directly (case-insensitivity, substring, empty filter matches all,
no-match is empty). The combos themselves are device-drawn, so they are not asserted — the same split the panel
already uses (its pure half is asserted; its drawing is the walkthrough's business).
