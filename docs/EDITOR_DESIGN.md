# Editor Design

**Status**: Design. This document governs the editor's architecture, workspace,
interaction model, and undo model. It is a port of `ocaml-game`'s editor design
(`ocaml-game/docs/EDITOR_DESIGN.md` + the D-009…D-017/D-020/D-021 decisions) to
F#, adapted to the fsharp-game roadmap and data model. It is built incrementally
across the editor phases of `docs/ROADMAP.md`.

## Purpose

The editor is a **scene/scenario authoring tool** for a narrative game: place
actors and props, lay waypoint paths, place triggers and wire them to cue
scripts, and assemble scenarios from zones and cast members.

## Principles

1. **ImGui-native** — use ImGui's immediate-mode widgets (`InputText`,
   `DragFloat`, `InputFloat`, `Combo`, `ColorEdit`, `Checkbox`, `TreeNode`,
   list boxes, docking, context menus) instead of hand-rolled text buffers and
   pixel-magic layout. The reference's ~50 fixed byte-buffers + `editing` flags
   are the raylib limitation this removes.
2. **One docking workspace** — a single editor workspace with dockable panels,
   not the reference's fragmented `Shift+1..6` screens.
3. **Live-edit** — the editor and runtime share one live cue tree; backtick (`` ` ``)
   toggles between play and edit with no cloning.
4. **Undo everywhere** — every editor mutation is an undoable command.
5. **Data-oriented** — editor state is a small record (mode, selection, tool,
   camera, undo stack); no per-field text-buffer bloat.

## Workspace

A single editor workspace with a menu bar and dockable panels (ImGui docking):

| Panel | Dock | Purpose |
|-------|------|---------|
| **Viewport** | center | 3D scene, selection, gizmos, waypoint/trigger overlays |
| **Outliner** | left | hierarchical tree: Zone → Actors/Props/Waypoints/Triggers; tabs with Cast, Advent, Cue, Scenario, and Zone Registry |
| **Inspector** | right | the single editing surface: the selected item's properties (entity / cast member / scenario / zone entry) |
| **Asset Browser** | bottom | actors/environments/props; drag into the viewport to spawn |

The sub-editors are dockable windows/tabs opened from a **Window** menu, not
separate screens. Cast, Advent, Cue, Scenario, and Zone Registry dock as tabs in
the **left** panel (alongside the Outliner) and are shown by default:

- **Cast** — list of members (name → asset); selecting a member edits it in the Inspector.
- **Advent** — named flag set.
- **Cue** — recursive tree-list editor for the top-level cue.
- **Scenario** — scenario list; selecting one shows its Cue/Scene tabs in the Inspector.
- **Zone Registry** — zone list; selecting one shows its details in the Inspector.

The **Inspector** is the single editing surface (one selection → one Inspector):
list panels navigate/select/add/delete, and the Inspector shows the selected
item's settings. A trigger's cue is edited inline in the Inspector; a scenario's
cue/scene are Inspector tabs.

## Interaction model

- Backtick (`` ` ``) toggles edit mode; `Tab`/`Shift+Tab` move keyboard focus
  between widgets (`Enter`/`Space` activate). App shortcuts yield while a text
  field is being edited.
- Left-click selects (raycast); right-drag looks (pitch/yaw); right-drag + `WASD`
  flies (`Q`/`E` up/down); scroll zooms.
- Tools: `1` move, `2` rotate, `3` scale, `Q` snap toggle.
- Keyboard nudge: `Shift` + `W`/`A`/`S`/`D`/`Q`/`E` (camera-relative move/rotate/
  scale, held to repeat).
- `F` focus on selection, `Del` delete, `Ctrl+D` duplicate,
  `Ctrl+Z` / `Ctrl+Shift+Z` undo/redo.

## Ownership (D-020, D-021)

The **gameplay screen owns the scene and camera**; the editor is a togglable
layer (`editing : bool`) that reads and mutates the gameplay screen's
scene/camera without owning them.

- `GameplayScreen` owns `zones`/`camera`/`editor`/`editing`.
- `EditorState` holds only transient editor UI state (selection, tool, undo
  stack, drag, mouse).
- Editor functions take the gameplay state and read/write it without owning it —
  this avoids a `GameplayScreen ↔ Editor` module cycle by splitting the data
  (`GameplayState`) from the behaviour (`GameplayScreen` / `Editor`).
- `Engine.fs` stays screen-agnostic: it calls `ScreenManager.update`/`draw` and
  presents the returned scene; `GameplayScreen` calls into `Editor`.

## Command / undo model (D-011)

Every editor mutation goes through a command with `apply`/`undo`, pushed onto an
undo stack. Commands store **values** (not closures):

- Entity commands store an `EntityRef` (kind, index, generation, and the
  environment epoch for parts) so undo survives entity deletion and slot reuse.
  Restoring a deleted entity re-occupies its recorded slot via `Occupy`, which
  raises if the slot is taken. Each kind's entities live in their own slot map —
  actors, placed props, and the current environment's parts — and changing
  environments moves the `Environment` value (and its part map) rather than
  respawning, so a recorded slot is always free when undo runs. That rests on no
  untracked `Add` ever reusing a recorded slot, so entity creation is funnelled
  through `editor/Spawning.fs`, which pairs every `Zone.spawn*` with the command
  that reverses it; `AssetBrowser`/`Selection` must not call `Zone.spawn*`
  directly, and `Zone` checks the slot is free before building resources. A part
  ref's epoch ties it to its environment, so a part command applied while a
  different environment is loaded is a no-op (a delete or transform) or fails
  loudly (a restore, which the LIFO order makes unreachable) rather than acting
  on the wrong part. Swapping environments (a load, or undo/redo of the swap)
  drops a part selection whose epoch no longer matches, so the inspector never
  shows a dangling entity.
- Non-entity surfaces (cast, scenario, zone registry, advent flags, cue root)
  store **name-keyed before/after snapshots** of the immutable map/set entry.
- A generic `Field` command covers arbitrary entity property edits.
- Continuous edits (gizmo/slider drag) coalesce into a single undo step by
  merging consecutive commands with the same key.

`apply`/`undo` take the active zone plus `SharedState` (which hosts the cue,
cast, scenario library, zone registry, and advent flags), so `Command` depends on
`SharedState` rather than `GameplayState` — avoiding a `Command → GameplayState →
EditorState → Command` module cycle.

## Live-edit and the template/runtime split (D-010)

Cues are live data: the runtime executes a cue tree in place. To keep undo
well-defined while a cue is mid-play, the cue is split into an immutable
**template** (the authored structure — undoable) and a mutable **runtime**
(execution state — not undoable). Undo targets the template; the runtime
re-derives from the edited template.

This mirrors the `Cue_Op` (template) / `CueState` (runtime) split described in
`docs/ARCHITECTURE.md`.

## Cue editor (D-013)

The cue editor presents the recursive cue tree as a recursive **tree list**
(indented `TreeNode`s), not a node graph — a graph would misrepresent a
structure that is a recursive union with live `*_State` nodes during execution.
The interaction is select, indent, per-node properties, reorder (mirroring the
reference's recursive-tree editor).

## Selection and gizmos (D-012, D-015, D-016)

- M5/early editor uses a **minimal raycast** (screen-ray → sphere/box) for
  selection; accurate collider/BVH picking lands with physics.
- The viewport renders entities as **debug-draw markers** (spheres/boxes/
  wireframes) until full model/PBR rendering arrives; a `DebugDraw` line/wireframe
  primitive is shared by gizmos, waypoint arrows, and trigger overlays.
- Translate/rotate/scale gizmos are present from the start; axis picking reuses
  the raycast work.

## ImGui binding scope (D-017)

Bind **just enough** ImGui for the editor's needs — docking plus core widgets
(text, buttons, `TreeNode`, `CollapsingHeader`, `Combo`, `DragFloat`,
`InputText`, `Checkbox`, `ColorEdit`) — not the full surface. `render/ImGui.fs`
already provides the minimal `ig*` layer; grow it per milestone.

## Phasing (mapped to `docs/ROADMAP.md`)

- **Phase 2b** — workspace shell (docking, viewport, Outliner, Inspector, Asset
  Browser) plus the `DebugDraw` line primitive and editor camera.
- **Phase 2c** — selection, gizmo movement, spawn, delete, duplicate.
- **Phase 2d** — command/undo core.
- **Phase 6** — accurate physics raycast picking (replaces the minimal raycast).
- **Phase 9** — cue editor as a dockable window (recursive tree list) using the
  template/runtime split.
- **Phase 10** — waypoints/triggers/advent/cast as dockable panels; trigger→cue
  wiring inline in the Inspector.
- **Phase 11** — scenario editor (library + cue + scene tabs).
- **Phase 12** — zone registry editor.

## Module map

Per `docs/PROJECT_STRUCTURE.md`. `EditorState`/`Command` live in `state/`; the
rest are in `editor/`.

| Module | Responsibility |
|---|---|
| `Editor` | orchestration: docking workspace, keyboard shortcuts, camera/gizmo interaction |
| `Spawning` | single entity-creation surface; pairs each spawn with its reversing command |
| `Selection` | selection edits (delete / duplicate) + stale-part-selection cleanup |
| `EditorState` | selection + transient UI state |
| `Outliner` | hierarchical entity list |
| `Inspector` | single editing surface for the selection |
| `Viewport` | 3D viewport + transform gizmo |
| `CastPanel` / `AdventPanel` / `AssetBrowser` | docked list panels |
| `CueEditor` / `CueTree` | cue template editor + shared recursive tree renderer |
| `ScenarioEditor` / `ZoneEditor` | scenario / zone registry editors |
| `Command` | undoable editor commands |
| `Raycast` | screen-ray picking math |
