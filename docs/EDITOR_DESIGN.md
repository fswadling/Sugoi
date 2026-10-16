# Editor Design

**Status**: Design. This document governs the editor's architecture, workspace,
interaction model, and undo model. It is a port of `ocaml-game`'s editor design
(`ocaml-game/docs/EDITOR_DESIGN.md` + the D-009…D-017/D-020/D-021 decisions) to
F#, adapted to the fsharp-game roadmap and data model. It is built incrementally
across the editor phases of `docs/ROADMAP.md`.

## Purpose

The editor is a **scene/scenario authoring tool** for a narrative game: place
actors and props, lay waypoint paths, place triggers and wire them to cue
scripts, and assemble scenarios from zones and characters.

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
4. **Undo everywhere** — every editor mutation of *scene* state is an undoable command, on **one**
   history for the gameplay state (`specs/048-unified-undo-stack`). A zone move, an add and a delete are
   steps on it too, which is what keeps an edit undoable after the developer has moved away from the zone
   it was made in. Session-scoped authoring state that is not a scene edit — the collider overlay's
   visualisation preference — is a direct write, and the zone registry (D-023)
   still is, though the per-zone reason it was given no longer applies.
5. **Data-oriented** — editor state is a small record (mode, selection, tool,
   camera, undo stack); no per-field text-buffer bloat.

## Workspace

A single editor workspace with a menu bar and dockable panels (ImGui docking):

| Panel | Dock | Purpose |
|-------|------|---------|
| **Viewport** | center | 3D scene, selection, gizmos, waypoint/trigger overlays |
| **Outliner** | left | hierarchical tree: Zone → Actors/Props/Waypoints/Triggers; tabs with Scenario and Zone Registry |
| **Inspector** | right | the single editing surface: the selected item's properties (entity / scenario / zone entry); tabs with Cue, Advent and Miscellaneous |
| **Asset Browser** | bottom | actors/environments/props; drag into the viewport to spawn |
| **Dialogue** | bottom | the dialogue state put on screen by hand — shown, kind, speaker, expression, text (spec 038) |
| **Songs** | bottom | the global music bed — add music assets, a row each with transport, a scrub, a weight and a loop toggle over one master (spec 087) |

The sub-editors are dockable windows/tabs opened from a **Window** menu, not
separate screens. Scenario and Zone Registry dock as tabs in
the **left** panel (alongside the Outliner) and are shown by default:

- **Advent** — the story milestones as an ordered **string set** (spec 082), edited by naming one and adding it or removing it (spec 055), docked on the **right**.
- **Scenario** — the game's scenarios, one row each, named by hand: a text field + **Add** names one, **Delete**
  removes it, **Register** files the current scene's zones (each with its staging and its narrative entities) and the
  cue under it, **Clear** empties it but keeps the name, and **Fork** runs it — ensuring and restaging the
  scenario's zones, appending its entities and forking its cue, as one step on the one history (specs 070, 078, 079;
  a scenario is a plain string and the library is an ordered map since `specs/083-scenario-strings`).
- **Zone Registry** — **retired** (`specs/079-scenario-owned-zones`): a scenario owns its zones' staging, zones are
  addressed by **name**, and the store and its panel are gone. The active zone's environment is set in the Asset
  Browser (a `SetEnvironment` step) and its light in the Inspector.

The **Cue** panel (spec 047) and the **Advent** panel (spec 055) are the exceptions among the
sub-editors: they dock as tabs beside the **Inspector** in the **right** column rather than in the
left, because what the Cue edits is a cue rather than a place in the scene and what the Advent edits
is a set of story facts rather than a place, and the right column is where the editing surfaces are.
The Cue presents the cue as a recursive tree list (D-013): nothing that is executing can be retuned,
replaced or removed, everything ahead of it can be edited, an addition is always taken, and its edits
are steps on the one undo history (D-010, `specs/049`). The Advent presents the milestones as a text field, an
**Add** and a list with a **Remove** per row, and an add or a removal is likewise a step on the one history
(D-011, `specs/055`, `specs/082`).

The **Miscellaneous** panel (spec 059) docks on the **right** too: it is the home for **game-wide runtime
tuning** — values that belong to no zone and no selection, so they cannot live in the Inspector. Its first
control is the screen **fade level**, a slider over 0..1 for the black overlay the gameplay screen draws
over the rendered game view. Unlike the Cue and Advent panels it writes the value **in place**, not as a
step on the history, because it is live screen state rather than an authored edit (the same class as the
Cast binding and the collider overlay, D-022).

The **Dialogue** panel (spec 038) docks by exception as a tab beside the **Asset Browser** at the
**bottom** rather than in the left column, because it is a line of text and wants the dock's width —
the 20 % left column would wrap it. Like Cast and the Zone Registry it writes the state it edits
directly rather than through the Inspector (D-011), since the dialogue state is runtime screen state
rather than a selection.

The **Songs** panel (spec 087) docks at the **bottom** too, a tab beside the Asset Browser and the
Dialogue: it is a wide, list-shaped surface — a picker and a row per song — rather than one selection,
so the right column (and certainly the 20 % left column) would wrap it. Its weight, master, loop and
row-set edits are `SetMix` steps on the one history (D-011); its transport and scrub are runtime and
record nothing.

The **Inspector** is the single editing surface (one selection → one Inspector):
list panels navigate/select/add/delete, and the Inspector shows the selected
item's settings. A trigger's cue is edited inline in the Inspector.
Cast, the Zone Registry and the Scenario panel are the three surfaces that
edit in place rather than through the Inspector (D-022, D-023, spec 070), because what they
edit — a slot, a place, a scenario — is not a selectable item: there is one for every look of
every member, one for every variant of every zone, and one for every declared scenario.

## Interaction model

- Backtick (`` ` ``) toggles edit mode; `Tab`/`Shift+Tab` move keyboard focus
  between widgets (`Enter`/`Space` activate). App shortcuts yield while a text
  field is being edited.
- Left-click selects (raycast); right-drag looks (pitch/yaw); right-drag + `WASD`
  flies (`Q`/`E` up/down). There is no scroll zoom: the wheel collides with
  scrolling in the editor's panels, so the camera is fly-only.
- Tools: `1` move, `2` rotate, `3` scale, `Q` snap toggle.
- Keyboard nudge: `Shift` + `W`/`A`/`S`/`D`/`Q`/`E` (camera-relative move/rotate/
  scale, held to repeat).
- `F` focus on selection, `Del` delete, `Ctrl+D` duplicate,
  `Ctrl+Z` / `Ctrl+Shift+Z` undo/redo, `Ctrl+Shift+C` clear the world (**Edit → Clear World**).

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
- `Shell.fs` stays screen-agnostic: it calls `ScreenManager.update`/`draw` and
  presents the returned scene; `GameplayScreen` calls into `Editor`.

## Command / undo model (D-011)

Every editor mutation goes through a command with `apply`/`undo`, pushed onto an
undo stack. Commands store **values** (not closures):

- Every content command names the **place** it acts on (`specs/077-commands-carry-zone`), so a step acts on its own
  zone rather than whichever place is active, and a `Batch` may span places. `ZoneMove` remains a step because
  switching the active place is itself an edit the author can undo, not because undo depends on it.
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
  (`Zone.occupy*`/`spawn*`, which rebuild the body and renderer). A payload's **type** is defined where the
  thing it describes is — `ActorState`/`PropState`/`Environment` in `world/Entity.fs`, `LightSettings` in
  `render/Texture.fs`, the whole of a zone's contents (`ZoneContents`, with `captureZone`/`occupyZone`) in
  `world/Zone.fs` — so the `Command` union names them rather than owning them.
- A command may be **several mutations that were one gesture** (`Batch`): applied forwards,
  reversed backwards, so one command stays one undo step for a gesture that has to replace a
  zone's environment and every prop standing in it at once (`specs/035-zone-template-props`).
  It is not a transaction: the commands inside carry their own payloads and are commands that
  already exist, so a batch says nothing a sequence of commands could not — only that they
  were one action.
- One step can span the whole world: **Clear World** (`specs/084`) retires every zone and installs a fresh **start**
  place, and resets the running cue, as one `Batch` — a state-level `ZonesClear` (carrying each zone's name and
  `ZoneContents`, and the active place) paired with a `SetCue` to empty. Like a zone add/delete or a cue edit it is
  carried by `Undo`, not the zone-level runner, and it reuses the delete-time teardown (`Zone.release` plus the
  environment unload). The camera and the scenario library are untouched.
- Non-entity surfaces (scenario, advent flags, cue root) store
  **name-keyed before/after snapshots** of the immutable map/set entry. The cue's has landed
  (`specs/049-cue-on-one-history`): its edits are `SetCue` steps — before/after cue values — on the one history,
  pruned on the way into play (D-010). The adventure flags' has landed too (`specs/055-advent-flags`): its edits are
  `Advent` steps — the set before and after the edit — on the same history, carried by `state/Undo.fs` because the
  set is not a zone's. The scenario is the one surface a project-scoped stack is still owed to, and it is a gap for a
  surface that does not exist yet rather than for this one.
  The surfaces expected
  to need one are not among them: the zone registry (D-023) is a direct write, because it is not a scene edit and re-making an entry by hand
  costs less than the stack would. The Inspector's actor animation and morph
  sections (`specs/036`, `specs/037`) are a third: an actor's animation slots and
  its morph weights are runtime state on its renderer, which a command payload may
  not hold, so their add/remove/weight edits are direct writes too — and, like the
  rest of the runtime, are not persisted. The Dialogue panel (`specs/038`) is a
  fourth: the dialogue state is runtime screen state, not a scene edit and never
  persisted, so its shown flag, kind, speaker, expression and text are written
  directly — the panel records no command and stores nothing beyond the state.
- Property edits are per-kind rather than generic: `Transform` and `Rename` cover
  the properties every kind shares, and a kind with an editable property of its own
  has its own command for it (`TriggerSet` for a trigger's volume and prompt, `Link`
  for a waypoint's link). A kind-specific edit is recorded by that kind's command, so
  a new kind brings its own rather than widening a shared one.
- Continuous edits (gizmo/slider drag) coalesce into a single undo step by
  merging consecutive commands with the same key.

`Command.apply`/`undo` take one zone plus the physics world and the asset manager as **values**, so
`Command` depends on neither state container — avoiding a `Command → GameplayState → EditorState →
Command` module cycle. The state-level runner resolves a content command's named place to its zone
(`Command.placeOf`) before delegating, so `Command` still names no state container. (The advent flags live on `SharedState`; the zone registry
and the physics world live on `GameplayState`; `Command` reads none of them directly.) The
**history** is one, on the gameplay state:
`state/Undo.fs`, compiled after `GameplayState`, carries the state-level runners and the steppers,
because a step may be a zone move, an add, a delete or a cue edit, which only the state can carry out
(`specs/048-unified-undo-stack`, `specs/049-cue-on-one-history`). The `Command` *type* stays where it is — moving it would be that cyclic
edge — and only the runners moved.

## Live-edit and the cue's undo (D-010)

A cue is one immutable **value** (`world/Cue.fs`) that *is* its own run: advancing it
returns the next value, and the cue on the screen shrinks as it runs, down to `Fin`. So
"live-edit" is not about keeping a mutable runtime in step with a template — there is no
mutable runtime to fall out of step with, and no template beside it.

Undo targets the **cue** (`specs/046`); there is no separate run to keep off a stack. The cue's edits ride the
one history on the gameplay state (`specs/048-unified-undo-stack`) as `SetCue` steps, each carrying the cue
before the edit and after it — the same before/after value snapshot every zone-level command already uses, and a
snapshot is one reference because a cue is immutable. The panel needed a history of its own only while the stacks
were per zone and a cue spans zones; with one history that reason is gone, so the second history, its field and
its two Undo/Redo controls went with it (`specs/049-cue-on-one-history`).

On the way into play the cue's **steps** are pruned from both sides of the history — the cue advances while
playing, so a step recorded against it could no longer be applied cleanly — while every zone step stays. That is
what keeps `specs/048`'s property that a scene edit made before a play is still undoable after it. `Ctrl+Z`,
`Ctrl+Y` and the Edit menu are the entry points (`specs/049`); the panel draws no control of its own.

The earlier template/runtime framing — an immutable `Cue_Op` template, a mutable
`CueState` runtime, and a `state_map` — was measured and retired; the note is
`docs/CUE_MODEL_DECISION.md` and the engine is `specs/046`.

## Cue editor (D-013)

The cue editor presents the recursive cue tree as a recursive **tree list**
(indented `TreeNode`s), not a node graph — a graph would misrepresent a
structure that is a recursive union — and, while it runs, a cue value that
shrinks, rather than a mutable node per op (`specs/046`).
The interaction has landed (`specs/047`) as select, per-node properties, add and remove: a
row per container with its children indented beneath it, and nothing that is executing retuned,
replaced or removed, so an edit never disturbs what is running — save that a `Sequence`
always takes a child on its queue, since a queue's tail lies ahead of the running op.
**Reorder is not in that cut**: the reference names it, and it is the obvious
addition to make next.

Every dropdown in the cue editor — the op **kind**, the character, place, advent, waypoint, trigger and
clip/morph choosers — is **filterable** (`specs/068-autocomplete-cue-combos`): open it and type, and the list
narrows to names containing the text (case-insensitively); **Enter** takes the top match and dismisses the
dropdown. The advent chooser (`specs/082`) also accepts a name that matches no current milestone, so a cue may
name an advent before it is added; typing it does not add it to the set. The grouped kind list shows **flat**
while a filter is entered and its groups when it is cleared. A
pick is the same edit as before — one step on the one history — so this changes how an option is *found*, not
what choosing it writes.

## Cast panel (D-022, retired)

D-022 once described the **Cast panel**: a tree that assigned an actor asset to each member-and-look
*slot* in place, with a Spawn control that placed a bound slot's actor. `specs/074-retire-cast` retired it.
The look vocabulary, the slot → actor asset binding, the panel and the roster all left the game: the closed
`Character` union is now the narrative engine's (`narrative/Roster.fs`), and a portrait line's speaker — like
a cue's actor — is a plain **string**, resolved to her actor asset by name (the asset manager's name index).
Placing an actor is the Asset Browser's job, which already named an asset directly, so the panel's
"assign directly, place undoably" split is gone with it. The reasons recorded below are kept for history.

The cast panel was a tree that assigns **in place**: each member is a collapsible node, and
under her one row per look, each row naming the look and holding the actor asset bound to
that slot. An earlier shape — a list of members, each selected and then edited in the
Inspector — is rejected for two reasons: a *slot* (a member and a look) is not a selectable
item in the one-selection model, so the Inspector would need a second notion of what is
being edited; and binding every slot through a two-step round trip each is worse than
doing it in the list, which is where a member's other looks are visible for comparison.

This was why the binding is neither undoable nor routed through the command system at all. The reason given
was that the undo stacks were per zone (`GameplayState.UndoByZone`), swapped on a zone switch so an
edit's undo always applied to the zone it was recorded in — and the cast binding outlives
every zone, so an assignment parked in one zone's stack was unreachable from every
other, and was discarded outright when that zone was deleted. **That reason is gone**
(`specs/048-unified-undo-stack`: one history, no swap), and the binding has since been retired outright.
Assigning to it was a
direct write, like the collider overlay's visualisation preference, both of which the code
treats as binding actions rather than scene edits. (A zone's player designation used to be
cited here as a third; it is neither project-scoped nor, since
`specs/045-undoable-player-designation`, a direct write — D-025.) A project-scoped stack is a
real gap, but it is a gap for the *editable* surfaces that do not yet ride the one history (the
scenario, and the cue root); the adventure flags, which used to be named here, have landed as an
`Advent` step on that history (`specs/055-advent-flags`). The zone registry, which since `specs/034-zone-registry` does
exist, answered the same way (D-023).

Assigning was not the panel's only act. A slot with an asset bound could also **place** it: the panel's
Spawn control created an actor in the active zone, drawn with that asset and named for the member,
through `editor/Spawning.fs` — the one place the editor creates entities, which is what paired the
create with the command that reverses it and selects what it made (`specs/033-cast-spawn`). So the
panel assigned directly and placed undoably, side by side in one panel, and the difference between
them was exactly the difference between a binding and a scene edit.

## Zone registry panel (D-023, retired)

The zone registry panel shows the game's **places** as a tree — one node per zone, one row per
variant that zone declares — and each row carries two controls: one that files the active zone's
loaded environment against that place, and one that puts a registered zone back into the active
zone. The tree is derived from the zone union (`world/ZoneRegistry.fs`) rather than maintained
beside it, so a new zone or a new variant needs no panel change, and two zones are free to declare
entirely different variant sets.

The two controls behave differently on purpose, for the reason D-022 gives. **Registering** is a
direct write: a registration is one click to make again by hand, and when the undo stacks were per zone an
entry recorded in one zone's stack was unreachable from the others and discarded when that zone was deleted
(`specs/048-unified-undo-stack` has since made the history one). **Instantiating** is a scene edit, and is
undoable through the command that already carries a whole environment (`SetEnvironment`), so putting
a registered zone into the scene is one step and one Ctrl+Z like anything else placed there.

An entry holds the environment asset, the environment's own settings, the environment's parts and the
zone's placed props, each as the author left them — including a part or a prop she deleted and one she
duplicated, because the record is a copy of the live maps rather than a reference to the asset's own
list. Instantiating puts both back, and does it in **one** undo step however much it replaced
(`specs/035-zone-template-props`). It does not yet hold waypoints, triggers or splat maps: those are
the rest of `docs/ARCHITECTURE.md`'s `Zone_Registry` entry, and they join as siblings of the parts.

## Lighting section (D-024)

A zone's sun — pitch, yaw, colour and ambient — is edited in the **Inspector**, not in a panel of
its own (`specs/043-zone-light-settings`). The Inspector's subject is the selection; a zone is not a
selectable entity in that model, so rather than add a second notion of what is being edited, the
active zone is the Inspector's **fallback subject**: it shows the selected entity when there is one,
and the zone's lighting when there is not. The two can therefore never be shown together, and no
state has to be kept in step. A zone is brought up by clicking its row in the Outliner's Zones list:
the click goes through `GameplayState.selectZone`, so it switches when it is another zone and drops
the entity selection when it is the zone already in use — the second case being what a plain switch,
which no-ops on the active zone, would leave showing.

A light change is **undoable, one step per committed change** — a deliberate departure from
`odin-game`, whose sliders write the live zone directly. This follows the rule that an authored
change is reversible; the controls commit when released or left (`isItemDeactivatedAfterEdit`), as
the Inspector's trigger fields do, so one drag is one step and two changes are two steps. The edit
is pushed through `Selection.setLight`, which records a `SetLight` command against the zone.

Unlike the zone registry (D-023), which is deliberately a direct write,
this is a *zone's own* setting rather than project-scoped state, so it lives on the zone and its undo
rides the zone's own stack — which is why it is a command at all. The same command is what an
**instantiate** uses: a registration records the light with the environment and the props, and
putting a registered zone back sets its light inside the one `Batch` that already replaces the
environment, so one Ctrl+Z restores the previous light along with everything else (spec 035 FR-005).

## Player designation (D-025)

Which of a zone's actors is the avatar is a **zone's own** setting, held as `Zone.Player` — and
`docs/ARCHITECTURE.md` already says so ("the player designation and the undo history are held per
It was nonetheless listed among the direct writes that *outlive a zone* (principle 4),
and that grouping was wrong: those are project-scoped surfaces, which have no zone to be undone in,
where this one lives on the zone and has one.

So it is a command like the zone's light (D-024): `Selection.setPlayer` records one `SetPlayer` step on
the active zone's own stack, so a designation is one Ctrl+Z, and undoing a re-designation restores the
*previous actor* rather than no player at all. Binding the actor who already is the player records
nothing, so the control can be pressed twice without leaving an empty step behind.

The Inspector's actor section also reads the designation back — the action is drawn disabled and
labelled when the selected actor already is the player. Nothing in the editor showed it before, which
would have left an undo with no visible effect; the reference's properties panel reads its own
**"Player Locomotion"** checkbox back the same way. The action is still only *setting*: the reference's
checkbox can also clear the designation, and this one does not (spec 045 FR-008).

## Trigger cue, inline (D-026)

A trigger **owns an authored cue**, edited **inline in the Inspector** and drawn through the same control as the Cue
panel (`editor/CueTree.fs`). The field lives on the trigger's authored state, so an edit is one `TriggerSet` step on
the one history and needs no command of its own (D-011); it carries no resource, so it rides the trigger's own
create/delete/duplicate too. The inline editor keeps **its own** node selection and text field, distinct from the Cue
panel's, and re-points them when the selected trigger changes, so a path or line typed for one trigger is never
applied to another.

When the trigger fires, the cue is **forked** into the screen's running cue — it runs beside it rather than replacing
it (`docs/ARCHITECTURE.md`, `specs/046`). The authored value is never consumed, so the same trigger fires again on
re-entry. A non-interactable, enabled trigger fires on the rising edge of "player inside"; an interactable one fires
on the interact key while it is the tracked trigger, not on entry (`specs/058-trigger-cue`).

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
- **Phase 9** — the cue engine (`specs/046`, done: one type that is the vocabulary and
  the run, shrinking to `Fin`), then the cue editor as a dockable window (recursive tree
  list); its edits are steps on the one undo history (`specs/049`).
- **Phase 10** — waypoints/triggers/advent as dockable panels; trigger→cue
  wiring inline in the Inspector.
- **Phase 11** — scenario editor (library + cue + scene tabs).
- **Phase 12** — zone registry editor. *(retired `specs/079-scenario-owned-zones`: a zone is named and its
  staging rides with the scenario that authors it, so the registry and its editor are gone)*

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
| `Inspector` | single editing surface for the selection (and the active zone's environment and lighting when nothing is selected) |
| `Viewport` | 3D viewport + transform gizmo |
| `AdventPanel` / `AssetBrowser` | docked list panels |
| `MiscPanel` | the right-docked Miscellaneous panel: game-wide runtime tuning (the fade level is its first control) |
| `CueEditor` / `CueTree` | the cue panel (right column, beside the Inspector) + the shared recursive cue-tree control |
| `ScenarioPanel` | the scenario panel (add/delete · register / clear / fork) |
| `Command` | undoable editor commands |
| `Raycast` | screen-ray picking math |
