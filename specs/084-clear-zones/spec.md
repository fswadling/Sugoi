# Feature Specification: Clear the World Back to a Fresh Start

**Feature Branch**: `084-clear-zones`

**Created**: 2026-10-13

**Status**: Draft

**Input**: User description: "Add a 'clear all zones' operation, reachable from the Edit menu with a Ctrl+C
shortcut, that removes every zone and returns the world to a fresh start. It must be one undoable step on the one
history."

## Origin

The world is a set of **present zones** keyed by name, with exactly one **active** zone
(`GameplayState.Zones`, `ActiveZone`). Zones are managed one at a time from the Outliner: adding, deleting and
switching are the `ZoneAdd`, `ZoneDelete` and `ZoneMove` steps on the one history (`specs/048`). The active place
cannot be made absent (`specs/061` FR-011), so there is no existing way to reduce the world to nothing — and no way
to wipe it in a single action.

The scenario panel's **Clear** (`specs/083`) is a different operation: it empties a *scenario's recorded contents*
but keeps the scenario. This feature removes the **zones themselves**, resetting the live world.

Retiring a zone already has a primitive: `GameplayState.deleteZone` releases a zone's runtime resources
(`Zone.release` — its Jolt bodies, its renderers and its static-scene batch) and unloads its environment when no
remaining zone uses it. This feature reuses that teardown, but must apply it to a zone that is the **active** one,
which `deleteZone` refuses by design.

The editor's Edit menu already mirrors keyboard shortcuts (`Undo`/`Redo`, `Ctrl+Z`/`Ctrl+Y`), and
`Editor.handleShortcuts` is the one app-level shortcut surface. The reference projects' cue vocabulary also carries
a `ClearScenario` op; a world-level **Clear** is its editor-side analogue.

## Clarifications

### Session 2026-10-13

- Q: What does clearing do — empty each zone in place, or remove the zones? → **Remove every zone** and return the
  world to a fresh start (a single empty start zone, active). It is a **world reset**, not an empty-in-place.
- Q: Where does the action live? → The **Edit menu**, with a keyboard shortcut, matching the existing Undo/Redo
  pairing.
- Q: Which shortcut? → **Ctrl+Shift+C**. It keeps the "C for clear" mnemonic without shadowing the conventional
  Copy binding; like every app-level shortcut it is suppressed while a text field has focus and only fires while
  editing.
- Q: Is it one undoable step? → **Yes** — one step on the one history; a single undo restores the entire previous
  world.
- Q: What happens to the active zone, which normally cannot be removed? → The reset removes it too; the fresh start
  zone becomes active. The rule that the active place cannot be made absent guards an ordinary *delete*, not a
  whole-world reset.
- Q: Does it affect the running cue or the scenario library? → The **running cue is reset** to its starting value
  (`Fin`, an empty run — the value the game begins with), so a clear leaves a playable clean slate; the **scenario
  library is untouched** (authored project state, `specs/070`).
- Q: Does it reset the camera or other screen runtime state? → **No.** The camera is left where it is (a clear is not
  a switch), and the fade, dialogue and follow are untouched. Only the zones and the running cue return to their
  starting state.
- Q: What is the action called? → **Clear World**. It stresses that the whole live world is reset, and "World"
  distinguishes it from the Scenario panel's per-row **Clear** (which empties a scenario but keeps it, `specs/083`).
- Q: What is the "fresh start"? → A single zone at the designated **start place** (`GameplayState.startZone`),
  empty: no entities, no environment, the default light, and no player — the state the world is created in.
- Q: Is there a confirmation prompt? → **No.** Undo is the safety net; a modal confirmation would be new UI and is
  out of scope.
- Q: Is the action offered when there is nothing to lose? → It is offered whenever the editor is editing (there is
  always at least the start zone). Clearing an already-fresh world is permitted and, like any command, records one
  step.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The author clears the world to a fresh start (Priority: P1)

The author has authored several zones. They choose **Edit → Clear World** (or press its shortcut). Every zone is
removed and the world returns to a single empty **start** zone, which becomes active: the Outliner's Zones section
lists just the start place, the viewport shows an empty scene, and the Inspector shows no selection. The running cue
is reset to empty; the scenario library and the camera are untouched.

**Why this priority**: it is the whole feature. Reducing the live world to a clean slate in one action is the value;
nothing else here delivers that.

**Independent Test**: build a headless gameplay state, add and author several zones, run the clear, and assert the
world holds exactly one zone — at the start place, empty, and active. No window, no GPU.

**Acceptance Scenarios**:

1. **Given** a world with several present zones, **When** the author clears, **Then** the world holds exactly one
   zone, at the designated start place, and it is active.
2. **Given** the world after a clear, **Then** the remaining start zone is empty — no actors, props, parts,
   waypoints or triggers; no environment; the default light; no player.
3. **Given** a world whose only zone is the active start zone, **When** the author clears, **Then** the world is a
   fresh start zone and nothing is lost.
4. **Given** the author clears, **Then** the running cue is reset to empty and the scenario library is unchanged.

---

### User Story 2 - The clear is one undoable step, and undo restores the world exactly (Priority: P2)

After clearing, the author presses `Ctrl+Z` (or **Edit → Undo**). Every removed zone returns with its authored
contents, and the previously active place is active again — the world is exactly as it was before the clear.
Redoing clears it again.

**Why this priority**: the clear is destructive; its usability rests on a single, exact reversal. It is still a
viable feature without it (the clear works), so it ranks below the core action.

**Independent Test**: from a authored multi-zone world, clear, then undo, and assert every zone and its contents
and the active place match the pre-clear world; redo and assert the fresh start returns.

**Acceptance Scenarios**:

1. **Given** a world cleared as one step, **When** the author undoes it, **Then** every removed zone is present
   again with its authored contents, and the previously active place is active.
2. **Given** an undone clear, **When** the author redoes it, **Then** the world is the fresh start zone again.
3. **Given** a clear with nothing else on the history, **Then** a single undo suffices to restore the whole world.
4. **Given** a clear followed by other edits, **When** those are undone in turn (LIFO), **Then** the clear's undo
   then restores the world it replaced.
5. **Given** a clear that reset a running cue, **When** the author undoes it, **Then** the previous running cue is
   restored along with the world.

---

### User Story 3 - The clear is reachable by keyboard as well as by menu (Priority: P3)

The author can invoke the clear with `Ctrl+Shift+C`, with the same effect as the menu item. The shortcut does not
fire while a text field has focus (so a field keeps its own `Ctrl+C`), and neither the shortcut nor the menu item is
offered while the game is playing rather than editing.

**Why this priority**: it is parity and convenience. The menu item alone already delivers the feature.

**Independent Test**: assert headlessly that the new `C` key maps to its own bit, so the binding cannot collide with
an existing one; inspect the binding's placement to confirm it is suppressed while a text field has focus, mid-drag,
and outside edit mode — the handler reads live `ImGui` state, so the gating itself is not drivable without a device.

**Acceptance Scenarios**:

1. **Given** the editor is editing and no field has focus, **When** `Ctrl+Shift+C` is pressed, **Then** the world
   clears exactly as the menu item does.
2. **Given** a text field has focus, **When** `Ctrl+Shift+C` is pressed, **Then** the world is unchanged (the press
   is left to the field).
3. **Given** the game is playing (not editing), **Then** neither the menu item nor the shortcut is available.

---

### Edge Cases

- **An already-fresh world**: permitted; the result is a fresh start zone, recorded as one step like any other.
- **A clear with a cue running**: the running cue is reset to empty (`Fin`) along with the world; when the cue is
  already empty the gesture records no cue step, and a single undo restores the previous run.
- **A zone whose environment is shared with another removed zone**: the environment is unloaded **once**, when no
  remaining zone uses it — the existing delete-time rule (`specs/061` FR-010).
- **The previously active place**: undo restores *that* place as active, not an arbitrary one.
- **A selected entity in a removed zone**: the selection is dropped (and any in-progress drag or snap baseline with
  it), so the editor shows no dangling selection after the clear.
- **`Ctrl+Shift+C` with a text field focused**: does not clear; the press is left to the field.
- **A clear attempted mid-drag**: not carried out, matching the Undo/Redo gating that avoids corrupting a gesture.
- **Zones added after a clear**: they are later steps; undoing them first leaves the clear's undo free to restore
  the world it replaced.
- **Redo after an intervening edit**: the intervening step clears the redo side, as with any command, so a cleared
  world is not re-applied out of order.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The editor MUST offer a **Clear World** action from the Edit menu.
- **FR-002**: The action MUST remove **every** present zone and bring the world to a single **fresh start zone** at
  the designated start place, which MUST become active; the world MUST hold exactly one zone afterwards.
- **FR-003**: The fresh start zone MUST be empty — no actors, props, parts, waypoints or triggers; no environment;
  the default light; no player.
- **FR-004**: The action MUST be exactly **one step** on the one undo history. Undo MUST restore every removed zone
  with its authored contents, MUST restore the previously active place, **and MUST restore the previous running cue**;
  redo MUST clear again (and re-reset the cue to empty).
- **FR-005**: The action MUST be reachable by a **keyboard shortcut** (`Ctrl+Shift+C`) whose effect and gating
  match the menu item.
- **FR-006**: The shortcut MUST NOT fire while a text field has focus; neither the shortcut nor the menu item MUST
  be available outside edit mode (while playing).
- **FR-007**: Retiring the zones MUST release the runtime resources they held — their physics bodies destroyed,
  their renderers and static-scene batches freed, and each environment unloaded from the GPU once no remaining zone
  uses it. The removed zones MUST NOT leak.
- **FR-008**: The action MUST reset the **running cue** to its starting value (an empty run, `Fin`) and MUST NOT
  change the **scenario library** (which is authored project state, not a zone).
- **FR-009**: After the action, a selection (or in-progress drag/snap baseline) of a removed entity MUST be dropped,
  so the editor shows no dangling selection.
- **FR-010**: The clear and its undo/redo MUST leave the camera where it is (a clear is not a switch — the camera is
  re-derived from the active place by a switch, and is never stored in a step). Undo/redo MUST otherwise leave the
  world consistent: the restored active place is the active place, and no selection dangles on a removed entity.

### Key Entities *(include if data involved)*

- **World (the present zones + active place)**: the set of present zones and which is active — what the clear
  replaces and what its undo restores.
- **Fresh start zone**: the single empty zone at the designated start place that the world is reset to.
- **Clear step**: the one undoable gesture; it carries the **whole previous world** — each zone's name and authored
  contents, and which was active — together with the **previous running cue** (two paired mutations, the world and the
  cue, applied forwards and reversed backwards as one step), so undo rebuilds both exactly.
- **Edit menu and shortcut surface**: the existing Edit menu (Undo/Redo) and the app-level shortcut handler the new
  action joins.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author can reduce any world to a single empty start zone in **one action** from the Edit menu.
- **SC-002**: A **single undo** restores the entire previous world — every zone, its contents, and the active
  place — exactly; redo re-clears it.
- **SC-003**: After a clear, **no zone resources leak**: the removed zones' bodies and renderers are released and
  their environments unloaded.
- **SC-004**: Typing is never interrupted by the clear: with a text field focused the shortcut does nothing, and
  `Ctrl+C` keeps its ordinary meaning.
- **SC-005**: The project builds with no warnings and the suite is green, with no window or device opened (the write
  path is drivable headlessly).
- **SC-006**: A clear resets the running cue to empty, and undo restores the previous run; the scenario library and
  the camera are untouched.

## Assumptions

- **The world and its running cue.** The clear resets the zones **and** the running cue to their starting state; the
  scenario library, the camera, the fade, the dialogue and the follow are left as they are. A future feature could
  reset more, but not this one.
- **The designated start place** is the existing `GameplayState.startZone` — the world the game is created in.
- **The shortcut is `Ctrl+Shift+C`, not `Ctrl+C`.** Reserved deliberately so the conventional Copy binding stays
  free both for a future Copy feature and for the user's habits; the binding fires only in edit mode and never while
  a text field has focus.
- **No confirmation dialog.** Undo is the safety net; a modal would be new UI and is out of scope.
- **No persistence.** The world is not saved (phase 15), so a clear has no persisted effect.
- **Reused teardown.** Retiring a zone reuses the same release path the delete step already uses; no new runtime
  teardown is introduced.
- **Slot-exact restore.** Undo of a clear re-occupies each removed entity's original slot, exactly as a delete's undo
  does (LIFO ordering), so a restored world is identical to the one replaced.
