# Research: Waypoint Authoring UX

**Feature**: `099-waypoint-authoring`  
**Date**: 2026-10-17

## Decision 1: Put chain editing in the Inspector

**Decision**: A selected waypoint's `Next` and `Add Next` controls are drawn in the
Inspector, beside its name and transform. The Outliner retains creation and listing
only.

**Rationale**: `docs/EDITOR_DESIGN.md` defines the Inspector as the single editing
surface for the selection. A link is a property of one waypoint, so editing it there
matches the existing model and avoids a second editing surface. The earlier
implementation put creation and chain actions together in the Outliner, which is what
produced the growing button row and the per-row `Link` control.

**Alternatives considered**:

- Keep creation and chain controls together in the Outliner. It keeps one panel, but
  the toolbar grows per anchor and the chain actions still need a selected waypoint.
- A dedicated waypoint-chain panel. The state is one property of the selection; a
  panel would duplicate the Inspector's subject and add docking state.

## Decision 2: Reuse `LinkSpawn`; implement insertion in the spawn helper

**Decision**: Keep the existing `WaypointCreate` and `LinkSpawn` commands. Change
`Spawning.spawnLinkedWaypoint` to insert: it writes the new waypoint's `Next` to the
predecessor's previous next, then points the predecessor at the new waypoint, and
captures the new waypoint (including that link) in the command.

**Rationale**: `LinkSpawn` already carries both sides of the operation: `Before` is
the predecessor's previous next and `Waypoint` is the new waypoint's captured value.
Its redo occupies the new waypoint (restoring its `Next`) and points the predecessor
at it; its undo restores the predecessor's `Before` and removes the new waypoint. Both
halves of an insertion are therefore already reversible, including the preserved tail.
No new command, payload, or undo path is needed.

**Alternatives considered**:

- A new `LinkInsert` command. It would duplicate `LinkSpawn`'s payload and runner for
  no extra state.
- Replace the old next (the original implementation). It detaches the tail and
  contradicts the clarified requirement.

## Decision 3: One anchor palette in `Spawning`

**Decision**: Add a small editor-only `WaypointAnchor` union (`Origin`, `Camera`,
`Player`) and a single ordered list in `Spawning`, plus dispatchers
`spawnWaypointAt` and `spawnNextWaypointAt` that both the Outliner and the Inspector
use. Availability is a function of the anchor and the active zone (`Player` requires
a live designation).

**Rationale**: Both menus are the same palette applied to two operations. One list
means a future anchor is added once, satisfying the requirement that adding an anchor
adds a menu row rather than a button. The union is editor-only and never persisted, so
it does not widen the authored data model.

**Alternatives considered**:

- Inline menus with explicit items in both panels. Fewer new types, but the palette is
  duplicated and adding an anchor requires edits in two places.
- Put the anchor type in `Entity`/`Zone`. The anchor is an authoring gesture, not
  authored data, so it does not belong in the world model.

## Decision 4: Anchor-informed automatic names

**Decision**: A new waypoint is named `<Anchor> <n>`, where `<Anchor>` is `Origin`,
`Camera`, or `Player` and `<n>` is the lowest positive integer not currently used as
the numeric suffix of a live waypoint with that prefix. The name is computed at
creation time in `Spawning`; rename remains unchanged.

**Rationale**: The next-chooser lists waypoints by name (the project's existing
name-addressing convention). Generic `Waypoint` names would make the chooser a list of
identical entries. Names are labels, not identities, so the counter is free to reuse a
number once no live waypoint uses it. The automatic name is an ordinary `Base.Name`
value, so it is captured and persisted with the waypoint exactly as a typed name is;
there is no schema change.

**Alternatives considered**:

- One zone-wide counter. It produces unrelated numbers per anchor (`Player 1`,
  `Camera 2`) and is harder to scan.
- Ask for a name at creation. It interrupts the repeated stamping gesture that makes
  the anchor useful in the first place.
- Keep `Waypoint`. It preserves the old behaviour but undermines the chooser.

## Decision 5: Read the player anchor from the actor's authored transform

**Decision**: `Player` is available only when `Systems.isPlayerLive zone` is true.
The waypoint copies the actor's current `State.Base.Transform.Position` and
`Rotation`, and uses unit scale.

**Rationale**: `syncActors` keeps an actor's authored transform current while playing,
and the editor's transform edits write the same value. The actor's transform is
therefore the one source of truth for both paths, and a never-played actor is still
valid. `isPlayerLive` already owns the live-designation rule, so the new code reuses
it instead of re-deriving the slot check.

**Alternatives considered**:

- Read the physics body directly. It would bypass the authored transform, fail for an
  actor without a body, and duplicate what `syncActors` already maintains.
- Require a play session. It would make a placed actor unusable as an anchor for no
  benefit.

## Decision 6: Action menus use real ImGui popups

**Decision**: Add the `igOpenPopup_Str`, `igBeginPopup`, and `igEndPopup` bindings to
`render/ImGui.fs`. The creation control and the extend control are buttons that open
a popup of `menuItem` rows. The next-chooser remains a filtered combo, reusing
`CueTree.filteredCombo` and `state.Editor.ComboFilter`.

**Rationale**: The project's ImGui binding is intentionally grown as widgets are
needed. A popup is the correct widget for an action menu; a combo would be a value
picker repurposed as a menu. The native library already exports all three entries, so
no native rebuild is required. The next-chooser is a value picker and already has a
filtered implementation, so it stays a combo.

**Alternatives considered**:

- Reuse `beginCombo` as an action menu. It works, but the preview/value semantics do
  not match an action and dismiss behaviour has to be managed by hand.
- Bind a full menu API (`BeginMenu` in a window). ImGui menus are intended for menu
  bars and popups; the popup API is the smaller addition.

## Decision 7: Keep the next picker a value combo over `(EntityRef option, name)`

**Decision**: The Inspector's `Next` combo lists `(None, "(end)")` followed by every
other live waypoint as `(Some ref, name)`. Picking writes through
`Selection.linkRef`; picking `(end)` writes `None`.

**Rationale**: `Selection.linkRef` already records exactly one `Link` step and
no-ops when the next is unchanged. Carrying the ref with the label means a duplicate
name cannot retarget the wrong waypoint, even though automatic naming makes duplicates
unlikely. `CueTree.filteredCombo` gives filtering and Enter-to-take-top-match without
new UI code.

**Alternatives considered**:

- A name-only combo resolved with `tryWaypointByName`. It would be simpler to build
  but would bind a duplicate-name pick to the first match rather than the row clicked.
- A context menu on each row. It is discoverable only by right-click and reintroduces
  per-row controls.

## Decision 8: Headless tests, manual UI validation

**Decision**: Cover anchor construction, naming, insertion, selection, capacity, and
undo/redo with xUnit tests over `Spawning`/`Zone`. Validate menu behaviour manually in
the running editor via `quickstart.md`; no headless ImGui harness is introduced.

**Rationale**: The existing `SpawningTests` already drives the real spawn/undo paths
without a GPU, and UI drawing has no headless test harness in this project. Adding a
harness is out of proportion to this feature.

**Alternatives considered**:

- An ImGui test harness. It would require a native context and input scripting, which
  no existing feature uses.
