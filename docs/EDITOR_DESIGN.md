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
4. **Undo everywhere** — every editor mutation of *scene* state is an undoable command.
   Session- and project-scoped authoring state that outlives a zone — a zone's player
   designation, the collider overlay's visualisation preference, and the cast binding — is a
   direct write instead, because the undo stacks are per zone (D-011, D-022).
5. **Data-oriented** — editor state is a small record (mode, selection, tool,
   camera, undo stack); no per-field text-buffer bloat.

## Workspace

A single editor workspace with a menu bar and dockable panels (ImGui docking):

| Panel | Dock | Purpose |
|-------|------|---------|
| **Viewport** | center | 3D scene, selection, gizmos, waypoint/trigger overlays |
| **Outliner** | left | hierarchical tree: Zone → Actors/Props/Waypoints/Triggers; tabs with Cast, Advent, Cue, Scenario, and Zone Registry |
| **Inspector** | right | the single editing surface: the selected item's properties (entity / scenario / zone entry) |
| **Asset Browser** | bottom | actors/environments/props; drag into the viewport to spawn |

The sub-editors are dockable windows/tabs opened from a **Window** menu, not
separate screens. Cast, Advent, Cue, Scenario, and Zone Registry dock as tabs in
the **left** panel (alongside the Outliner) and are shown by default:

- **Cast** — the cast as a tree: each member a collapsible node, and under her one row per
  look with the actor asset bound to that slot (D-022).
- **Advent** — named flag set.
- **Cue** — recursive tree-list editor for the top-level cue.
- **Scenario** — scenario list; selecting one shows its Cue/Scene tabs in the Inspector.
- **Zone Registry** — the game's places as a tree: a zone per node and one row per variant it
  declares, each row registering the active zone's environment against that place or instantiating
  one into the active zone (D-023).

The **Inspector** is the single editing surface (one selection → one Inspector):
list panels navigate/select/add/delete, and the Inspector shows the selected
item's settings. A trigger's cue is edited inline in the Inspector; a scenario's
cue/scene are Inspector tabs. Cast and the Zone Registry are the two surfaces that
edit in place rather than through the Inspector (D-022, D-023), because what they
edit — a slot, a place — is not a selectable item: there is one for every look of
every member, and one for every variant of every zone.

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
  actors, placed props, the current environment's parts, waypoints, and triggers —
  and changing environments moves the `Environment` value (and its part map) rather
  than respawning, so a recorded slot is always free when undo runs. That rests on no
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
- A create or delete carries the kind's **own authored value**: a record of its
  own holding the authored state and nothing the engine rebuilds, so no step has to
  clear a derived body or renderer to make it storable — a payload cannot hold one.
  There is deliberately no kind-agnostic create/delete payload, so undo restores
  every authored property of a kind rather than only the ones all kinds share, and a
  field added to a kind is either carried automatically or fails the build until it
  is classified as authored, derived, or runtime. Recording an entity is reading
  that value, and producing a live entity from it happens in one place per kind
  (`Zone.occupy*`/`spawn*`, which rebuild the body and renderer).
- A command may be **several mutations that were one gesture** (`Batch`): applied forwards,
  reversed backwards, so one command stays one undo step for a gesture that has to replace a
  zone's environment and every prop standing in it at once (`specs/035-zone-template-props`).
  It is not a transaction: the commands inside carry their own payloads and are commands that
  already exist, so a batch says nothing a sequence of commands could not — only that they
  were one action.
- Non-entity surfaces (scenario, advent flags, cue root) store
  **name-keyed before/after snapshots** of the immutable map/set entry. Note that none of
  them is zone-scoped, so none has a stack today: only zone-scoped state is undoable, and
  each of these needs a project-scoped stack when it lands. Two surfaces that were expected
  to need one are not among them: the cast binding (D-022) and the zone registry (D-023)
  are both direct writes, because neither is a scene edit and re-making an entry by hand
  costs less than the stack would.
- Property edits are per-kind rather than generic: `Transform` and `Rename` cover
  the properties every kind shares, and a kind with an editable property of its own
  has its own command for it (`TriggerSet` for a trigger's volume and prompt, `Link`
  for a waypoint's link). A kind-specific edit is recorded by that kind's command, so
  a new kind brings its own rather than widening a shared one.
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

## Cast panel (D-022)

The cast panel is a tree that assigns **in place**: each member is a collapsible node, and
under her one row per look, each row naming the look and holding the actor asset bound to
that slot. An earlier shape — a list of members, each selected and then edited in the
Inspector — is rejected for two reasons: a *slot* (a member and a look) is not a selectable
item in the one-selection model, so the Inspector would need a second notion of what is
being edited; and binding every slot through a two-step round trip each is worse than
doing it in the list, which is where a member's other looks are visible for comparison.

This is why the binding is neither undoable nor routed through the command system at all.
The undo stacks are per zone (`GameplayState.UndoByZone`), swapped on a zone switch so an
edit's undo always applies to the zone it was recorded in — and the cast binding outlives
every zone, so an assignment parked in one zone's stack would be unreachable from every
other, and would be discarded outright when that zone is deleted. Assigning to it is a
direct write, like a zone's player designation and the collider overlay's visualisation
preference, both of which the code already treats as binding actions rather than scene
edits. A project-scoped stack is a real gap, but it is a gap for the *editable* surfaces
that do not exist yet (scenario, advent flags, cue root), not for this one: an assignment
is two clicks in a list, and re-making it by hand costs less than undoing it. The zone
registry, which since `specs/034-zone-registry` does exist, answered the same way (D-023).

Assigning is not the panel's only act. A slot with an asset bound can also **place** it: the panel's
Spawn control creates an actor in the active zone, drawn with that asset and named for the member,
through `editor/Spawning.fs` — the one place the editor creates entities, which is what pairs the
create with the command that reverses it and selects what it made (`specs/033-cast-spawn`). So the
panel assigns directly and places undoably, side by side in one panel, and the difference between
them is exactly the difference between a binding and a scene edit.

## Zone registry panel (D-023)

The zone registry panel shows the game's **places** as a tree — one node per zone, one row per
variant that zone declares — and each row carries two controls: one that files the active zone's
loaded environment against that place, and one that puts a registered zone back into the active
zone. The tree is derived from the zone union (`world/ZoneRegistry.fs`) rather than maintained
beside it, so a new zone or a new variant needs no panel change, and two zones are free to declare
entirely different variant sets — unlike the cast, where one `Look` union is shared by every member.

The two controls behave differently on purpose, for the reason D-022 gives. **Registering** is a
direct write: the registry outlives every zone, the undo stacks are per zone, so an entry recorded
in one zone's stack would be unreachable from the others and discarded when that zone is deleted —
and a registration is one click to make again by hand. **Instantiating** is a scene edit, and is
undoable through the command that already carries a whole environment (`SetEnvironment`), so putting
a registered zone into the scene is one step and one Ctrl+Z like anything else placed there.

An entry holds the environment asset, the environment's own settings, the environment's parts and the
zone's placed props, each as the author left them — including a part or a prop she deleted and one she
duplicated, because the record is a copy of the live maps rather than a reference to the asset's own
list. Instantiating puts both back, and does it in **one** undo step however much it replaced
(`specs/035-zone-template-props`). It does not yet hold waypoints, triggers or splat maps: those are
the rest of `docs/ARCHITECTURE.md`'s `Zone_Registry` entry, and they join as siblings of the parts.

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
| `Selection` | selection edits (delete / duplicate / rename) + stale-part-selection cleanup |
| `EditorState` | selection + transient UI state |
| `Outliner` | hierarchical entity list |
| `Inspector` | single editing surface for the selection |
| `Viewport` | 3D viewport + transform gizmo |
| `CastPanel` / `AdventPanel` / `AssetBrowser` | docked list panels |
| `CueEditor` / `CueTree` | cue template editor + shared recursive tree renderer |
| `ScenarioEditor` / `ZoneRegistryPanel` | scenario / zone registry editors |
| `Command` | undoable editor commands |
| `Raycast` | screen-ray picking math |
