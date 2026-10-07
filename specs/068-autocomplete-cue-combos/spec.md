# Feature Specification: Autocomplete Cue Combos

**Feature Branch**: `068-autocomplete-cue-combos`

**Created**: 2026-10-04

**Status**: Draft

**Input**: User description: "in the cue editor it would be useful to have autocomplete — start typing the cue I want and it filters down."

## Origin

The cue panel (`editor/CueTree.fs` / `editor/CueEditor.fs`) offers every op's field as an ImGui combo: the
**kind** dropdown, the **character**, **place**, **advent** and **presentation** choosers, the **waypoint /
path** and **trigger** choosers, and the **clip / morph** name choosers (which can list every animation a
character's asset defines). Today a combo is a plain scroll list — the author scrolls and clicks. There is no way
to narrow it by typing.

ImGui offers this natively (a combo with a filter field, or a popup with a filtered list). The project's custom
`render/ImGui.fs` binding currently exposes only `beginCombo` / `selectableBool` / `inputText`, but the vendored
`native/libcimgui.so` already exports the two extra entry points the smoothest version needs
(`igSetKeyboardFocusHere`, `igCloseCurrentPopup`), so no native rebuild is required.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Type to filter a name combo (Priority: P1)

An author opens a cue field's dropdown (a clip, a morph, a waypoint, a trigger, a character, a place, an advent)
and starts typing; the list narrows to the entries whose name contains what was typed, and picking one — by
click or by Enter on the top match — sets the field as before.

**Why this priority**: it is the whole point — the long lists (clips/morphs in particular) are the ones that
hurt to scroll, and every field benefits.

**Independent Test**: open any cue combo, type a substring, and assert only matching options remain and that
choosing one commits it; with no input, every option remains.

**Acceptance Scenarios**:

1. **Given** an open combo listing several names, **When** the author types `jo`, **Then** only names containing
   `jo` remain (case-insensitively).
2. **Given** a filter is typed, **When** the author presses Enter, **Then** the top visible match is committed and
   the dropdown closes.
3. **Given** a filter is typed, **When** the author clicks a visible option, **Then** it is committed as today
   (one step on the one undo history).
4. **Given** a combo is open with no filter, **When** it draws, **Then** every option is offered and the filter
   field is focused so typing starts a filter without a click.
5. **Given** a filter matches nothing, **When** it draws, **Then** the list is empty and Enter does nothing.

---

### User Story 2 - Type to filter the kind dropdown (Priority: P2)

An author opening the op-kind dropdown can type an op name; while a filter is entered the grouped list is shown
**flat**, and with no filter the familiar grouped list (the separators) is unchanged.

**Why this priority**: the kind list is longer now (Structure / Timing / Dialogue / Zones / Actors / Animation /
Triggers / Camera / Advent / Debug) and is the first field a new node needs, so filtering it is valuable — but it
is a smaller win than the name fields and carries the flat-while-filtering wrinkle, so it rides second.

**Independent Test**: type an op name into the kind combo and assert only matching kinds remain (flat); clear the
filter and assert the groups and separators are back.

**Acceptance Scenarios**:

1. **Given** the kind dropdown with no filter, **When** it draws, **Then** the kinds are grouped as today.
2. **Given** a filter is typed, **When** it draws, **Then** only matching kinds are listed, without group
   separators.
3. **Given** a filtered list, **When** the author picks a kind, **Then** the node is replaced exactly as
   choosing it from the grouped list does.

---

### Edge Cases

- **A filter that matches nothing**: an empty list; Enter is a no-op; the field keeps its current value.
- **The current value not in the filter**: it still shows as the combo's preview; the list only offers matches.
- **Two entries sharing a name**: both remain filterable and choosing either writes the same name (the existing
  rule for waypoints/triggers), since the op resolves by name.
- **Switching between combos**: the filter does not carry over — a newly opened combo starts unfiltered.
- **While a node is executing**: the combo is not drawn (its fields are read-only), so nothing changes there.
- **A filter typed, then abandoned**: closing the dropdown discards the filter; reopening starts clean.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every dropdown in the cue panel MUST offer a filter: with text entered, only options whose name
  contains it (case-insensitively) are offered; with none, all options are offered.
- **FR-002**: The filter field MUST be focused when a dropdown opens, so an author can type a filter immediately
  without clicking the field first.
- **FR-003**: Enter MUST commit the top visible match and dismiss the dropdown; clicking an option MUST commit it
  as today; a filter matching nothing MUST leave the current value untouched.
- **FR-004**: The grouped **kind** dropdown MUST show its groups when unfiltered and a **flat** matching list
  while a filter is entered.
- **FR-005**: Choosing an option — filtered or not — MUST commit exactly as it does today: one accepted edit is
  one step on the one undo history (`specs/049`), and no op's value or meaning changes.
- **FR-006**: The match rule MUST be a pure, device-free function so it can be asserted without a window.
- **FR-007**: This feature MUST add no cue vocabulary, and MUST NOT change the panel's other behaviour (the
  read-only rule while executing, the text-field re-seed rules, the writing of fields).

### Key Entities *(include if data involved)*

- **Filter text**: the transient string an author types to narrow an open dropdown; belongs to the editor's UI
  state, not to the cue or to a zone.
- **Match rule**: the pure predicate deciding whether an option's name is offered for a given filter.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In every cue-panel dropdown, an author can type a substring and see only matching options.
- **SC-002**: Enter picks the top match and closes the dropdown; clicking picks the clicked option.
- **SC-003**: The grouped kind dropdown flattens to a filtered list while typing and restores its groups when the
  filter is cleared.
- **SC-004**: Choosing a filtered option is the same edit as choosing it unfiltered (one undo step; no value
  change).
- **SC-005**: The project builds with no warnings and the suite is green, with no device opened, and the match
  rule is covered by tests.

## Assumptions

- **Scope is the cue panel.** The change lives in the shared cue control (`editor/CueTree.fs`) and its panel
  (`editor/CueEditor.fs`), so a trigger's inline cue — drawn with the same control — gets it too. Other panels
  (the Outliner, the Inspector, the asset browser) are out of scope.
- **Matching is case-insensitive substring.** No fuzzy ranking, no highlighting; the top match is the first in the
  field's own order (the order the combo already offers), so the behaviour stays predictable.
- **Two small bindings are added.** `setKeyboardFocusHere` and `closeCurrentPopup` are already exported by the
  vendored `libcimgui.so`, so no native rebuild is needed.
- **One filter buffer.** Only one dropdown is open at a time, so a single reused buffer (owned by `EditorState`)
  suffices; it is cleared when a dropdown opens.
- **The fields themselves are unchanged.** This is a way to *find* an option, not a new kind of option: what a
  choice writes is exactly what the combo writes today.
