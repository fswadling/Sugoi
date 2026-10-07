# Implementation Plan: Autocomplete Cue Combos

**Branch**: `068-autocomplete-cue-combos` | **Date**: 2026-10-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/068-autocomplete-cue-combos/spec.md`

## Summary

Make every cue-panel dropdown filterable: open it, type, and the list narrows to names containing the text
(case-insensitively); Enter takes the top match and closes it, a click picks as today. A shared helper
(`CueTree.filteredCombo`) draws the filter field and the filtered rows over the existing `beginCombo` /
`selectableBool`; a pure `matches` predicate is the whole rule. The grouped **kind** dropdown keeps its groups
when unfiltered and goes flat while a filter is entered. Three tiny bindings are added to `render/ImGui.fs`
(`isWindowAppearing`, `setKeyboardFocusHere`, `closeCurrentPopup`) — all already exported by the vendored
`native/libcimgui.so`, so no native rebuild. One reused filter buffer lives in `EditorState` and is cleared when a
dropdown opens.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: the ImGui binding (`render/ImGui.fs`), the cue control (`editor/CueTree.fs`) and its
panel (`editor/CueEditor.fs`), the transient editor state (`state/EditorState.fs`), and the existing text-buffer
helpers (`seedText` / `bufferText`). All present; three binding additions.

**Storage**: N/A

**Testing**: xUnit, headless (`dotnet test`); the pure `matches` rule is asserted directly (the combos
themselves need a device, as the panel's drawing already does).

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor)

**Performance Goals**: editor-only (drawn while the play chain is stopped); the filter rebuilds a short
`string list` per open combo per frame — a cold path, no per-frame game cost.

**Constraints**: no cue value or write changes (a pick is the same edit as today, one undo step); no new cue
vocabulary; the build is warning-free; the suite is green; no device.

**Scale/Scope**: 4 source files (`render/ImGui.fs`, `state/EditorState.fs`, `editor/CueTree.fs`,
`editor/CueEditor.fs`); 1 test file (`test/CuePanelTests.fs`); no `.fsproj` change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. One helper, one pure predicate, one reused buffer, three one-line bindings. The helper
  replaces the repeated `beginCombo` + loop boilerplate rather than adding to it.
- **II. Fail Loudly** — PASS. No new failure paths: a filter that matches nothing simply shows an empty list;
  nothing raises.
- **III. Spec-First** — PASS. This feature is `specs/068-autocomplete-cue-combos`.
- **IV. Data-Oriented Architecture** — PASS. The filter is a transient `byte[]` on the editor state; the rule is
  a pure function over strings; no object graph.
- **V. Minimise GC Pressure** — PASS. Editor-only cold path; the play loop is untouched.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/068-autocomplete-cue-combos/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── filtered-combo.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
render/ImGui.fs           # + isWindowAppearing / setKeyboardFocusHere / closeCurrentPopup (EntryPoints already in libcimgui.so)
state/EditorState.fs      # + one reused ComboFilter buffer
editor/CueTree.fs         # + matches (pure), filteredCombo (the control); the character/place/advent/presentation
                          #   combos and the kind combo (flat while filtering) use them; drawEditor takes the filter buffer
editor/CueEditor.fs       # drawName (clip/morph), drawWaypointCombo and drawTriggerCombo use filteredCombo
test/CuePanelTests.fs     # the match rule, asserted directly
```

**Structure Decision**: No module moves and no compile-order change. The helper lives in `CueTree` (the shared cue
control, which already owns every combo's drawing and names no editor state — the filter buffer is passed in), so
`CueEditor`'s combos call it too. The bindings sit with the rest in `ImGui.fs`.

## Complexity Tracking

> No Constitution Check violations.
