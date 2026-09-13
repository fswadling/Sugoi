# Research: Multiple Zones

Decisions derived from `docs/ROADMAP.md` phase 8 ("Zones"), `docs/ARCHITECTURE.md`
(the `Zone_Map` / `Zone_Registry` split and the shared physics world), the feature
spec, and the existing zone/undo/editor code.

## Decision 1 — Zone container: a plain name-keyed dictionary on the gameplay state

- **Decision**: there is no `ZoneMap` type or module. `GameplayState` holds
  `Zones : Dictionary<string, Zone>` keyed by each zone's unique name, plus
  `mutable ActiveZone : string`; `GameplayState.activeZone` resolves the active zone
  from the dictionary. Adding and switching live in `GameplayState`, since they need
  the physics world and assets to move bodies. The zone does not store its name.
- **Rationale**: the container is a name→zone lookup plus the active name — a
  dictionary and a string. Wrapping them in a record or module buys nothing: zones
  are never batch-processed on the hot path, they are not slot-map entities (no
  contiguous-scan or generation-reuse requirement), and the operations that matter
  (add/switch) already need `GameplayState`'s other fields (the undo map, camera,
  and editor state). A dictionary also matches how `AssetManager` keys content by
  string id, so zones read the same way as every other asset. Resolving the active
  zone is one lookup per frame with no allocation, which is negligible.
- **Alternatives considered**: `SlotMap<ZoneEntry>` (rejected — buys nothing here,
  needs a struct wrapper around a heap payload, and caps capacity); a `ZoneMap`
  record holding the dictionary + active name (rejected — a second place to look
  for "which zones exist" when `GameplayState` already owns every other
  gameplay-scope field); an integer `ZoneId` surrogate (rejected — opaque in
  scenario/registry/saved data and forces a separate, equally-unique display name);
  a cached `Active : Zone` reference (rejected — removes one lookup per frame for no
  measurable gain, at the cost of a second field to keep in step).
- **Note**: dictionary enumeration order is unspecified, so the zones list sorts by
  name; shutdown iteration does not care about order.

## Decision 2 — The active zone is resolved from the dictionary

- **Decision**: call sites resolve the active zone with
  `GameplayState.activeZone state` (`state.Zones.[state.ActiveZone]`); no field
  caches a `Zone` reference.
- **Rationale**: the active zone is looked up once per frame (then threaded to the
  systems and the renderer) — a single string-keyed lookup with no allocation, which
  is negligible at 60 fps. A cached reference would remove it, but adds a second
  field to keep in step with `ActiveZone` for no measurable gain.
- **Alternatives considered**: a cached `Active : Zone` field (rejected —
  negligible saving, an extra invariant); a `member this.Zone` returning the active
  zone (rejected — taking `byref` into the map through a member is fragile in F#,
  and it would still do the lookup).

## Decision 3 — One physics world; zones are detached/attached, not rebuilt

- **Decision**: keep the single shared `Physics.World`. Switching deactivates the
  outgoing zone with `BodyInterface.RemoveBody` on each of its bodies and activates
  the incoming zone with `BodyInterface.AddBody`, then calls
  `PhysicsSystem.OptimizeBroadPhase`. No body or shape is destroyed or rebuilt, and
  renderers, `Physics` attachments, and `BodyToEntity` are left untouched.
- **Rationale**: Jolt's Architecture doc (Bodies) states "You can call AddBody,
  RemoveBody, AddBody, RemoveBody to temporarily remove and later reinsert a body
  into the simulation", and that a removed body stays alive with its shape (only
  `DestroyBody` frees it). This is Jolt's intended load/unload pattern
  ("sections of the simulation can be loaded / unloaded in the background"). It
  makes a switch cheap and, crucially, avoids rebuilding each environment part's
  `MeshShape` BVH. The doc warns that adding many bodies one at a time degrades the
  broadphase, so activation finishes with `OptimizeBroadPhase`; the binding exposes
  `RemoveBody`, `AddBody`, `IsAdded`, and `OptimizeBroadPhase`.
- **Single world vs per zone**: Jolt explicitly supports multiple `PhysicsSystem`s
  (Architecture doc, "Working With Multiple Physics Systems"), but bodies and
  constraints cannot be shared between them (only `Shape` can), each system
  pre-allocates its own `MaxBodies`/`MaxBodyPairs`/`MaxContactConstraints` pools,
  and `Physics.create` here builds a `JobSystemThreadPool` per world. Per-zone
  worlds would buy an O(1) switch — a cold editor action we do not need — at the
  cost of N systems, a shared-job-system refactor, and multiplied memory. One
  system with detach/attach matches the doc's streaming guidance and the existing
  ownership (`SharedState.Physics`; systems take the world).
- **Alternatives considered**: destroy + rebuild bodies on switch (rejected —
  rebuilds mesh-shape BVHs and couples the switch to renderer ownership); a
  `PhysicsSystem` per zone (rejected — see above; revisit if zones ever need to
  simulate simultaneously or independently); Jolt collision layers per zone
  (rejected — inactive dynamic bodies would still integrate and fall, and layer
  churn is more complex than detach/attach).

## Decision 4 — Player designation lives on the zone

- **Decision**: `Zone.Player : Handle voption` designates which of the zone's
  actors is the avatar. `Systems` read it from the active zone; the `player`
  parameter and `GameplayState.Player` are removed. Only the active zone's
  designation is ever live.
- **Rationale**: the player is an actor, and actors live in a zone's slot map, so a
  handle is only meaningful against its own zone's map; storing it per zone is the
  only model that cannot alias a same-index actor in another zone. Inactive zones
  hold a dormant designation exactly as they already hold dormant actors, props,
  and environment, so there is still one live player. This supersedes phase 7's
  top-level binding (`specs/016-walk-around` FR-002/FR-003), which assumed a single
  zone.
- **Alternatives considered**: a screen-level handle carrying its zone
  (`{ Zone; Actor }`) (rejected — the player entity is already zone data, so this
  stores the same fact outside its home and couples "which zone am I viewing" with
  "where is the character"); clearing the binding on every switch (rejected — it
  loses an authored designation and makes each zone not independently playable);
  a single global player entity outside the zone maps (rejected — contradicts the
  player being an actor that collides with zone geometry).

## Decision 5 — Per-zone undo history, swapped on switch

- **Decision**: `GameplayState` keeps `UndoByZone : Dictionary<string, UndoStack>`.
  On a switch, the current `Editor.Undo` is stored under the outgoing zone's name
  (`state.ActiveZone`) and the incoming zone's stack (or a fresh one) is loaded.
  Selection, drag, and nudge state are cleared on the switch.
- **Rationale**: commands name entity refs (and, for `SetEnvironment`, an
  `Environment`) valid only in the zone where they were recorded, so a global stack
  would apply an edit to the wrong zone. `Command`/`UndoStack` compile after
  `Zone`, so the history cannot live on the zone; a side map keyed by the zone
  name keeps each zone's timeline. Clearing the selection/drag is required
  because those refs are likewise zone-scoped.
- **Alternatives considered**: clearing history on switch (rejected — the spec
  requires each zone to retain its history); commands carrying their zone and undo
  activating it (rejected — undo would teleport the view across zones); moving
  `Command` below `Zone` (rejected — `SetEnvironment` holds an `Environment`, so it
  would be cyclic).

## Decision 6 — Zones are a collapsible section at the top of the Outliner

- **Decision**: `editor/Outliner.fs` gains a `"Zones"` `collapsingHeader` at the
  top, above the Environment/Actors/Props sections: one select row per zone (the
  active one marked, with a *Del* button), a name text field, and an *Add Zone*
  button. Selecting a row calls `GameplayState.switchZone`; *Del* calls
  `GameplayState.deleteZone`; *Add Zone* calls `GameplayState.addZone` with the
  typed name.
- **Rationale**: the Outliner is already the scene/world hierarchy panel, so zones
  (the top of that hierarchy) belong there, and a collapsible header keeps the
  entity lists uncluttered. It also needs no change to the native dock layout
  (`cimgui_setup_dock_layout`), which is compiled into `libcimgui.so`.
- **Alternatives considered**: a dedicated `editor/ZonePanel.fs` docked as a tab
  alongside the Outliner (rejected — needs a one-line native change and a
  `native/build.sh` rebuild for a small list); a floating `"Zones"` window
  (rejected — undocked and obscures the workspace); a *Zone* menu in the menu bar
  (rejected — poor for a list with per-zone delete actions).

## Decision 7 — Names are developer-authored and are the map key

- **Decision**: a zone's name is chosen by the developer when it is added (the
  Zones section's text field) and is the map key. The app seeds a single zone named
  `"main"`. `GameplayState.addZone` rejects an empty or duplicate name (`invalidOp`),
  and the editor does not offer the add in that case. The `Zone` does not store its
  name, and `Zone.create ()` is unchanged.
- **Rationale**: the name is the zone's identity and its label, so the person
  creating the zone should choose it — a generated `"Zone 3"` would have to be
  renamed later, and a rename re-keys. Keeping the name only as the dictionary key
  avoids a second copy that could desync, and leaves the zone record and the
  existing tests that build a bare zone untouched. An empty name is meaningless and
  a duplicate would alias an existing zone, so both fail loudly.
- **Alternatives considered**: auto-generated names with a counter (rejected — the
  developer would then rename, which re-keys); a `Name` field on `Zone` (rejected —
  a redundant second copy of the key); allowing duplicates (rejected — the key would
  be ambiguous).

## Decision 8 — Camera re-frame on switch

- **Decision**: on a switch, pivot the camera on the incoming zone — the live
  designated player's feet plus `CameraFollow.Height` when one exists, otherwise the
  world origin — preserving the current camera offset (`Position - Target`).
- **Rationale**: zones share world coordinates, so the incoming zone may be far from
  the outgoing camera. Pivot-and-preserve-offset reuses the editor's existing
  focus behaviour (spec FR-009).
- **Alternatives considered**: leave the camera untouched (rejected — it strands the
  view in the previous zone); snap to a fixed pose (rejected — loses the author's
  viewing angle).

## Decision 9 — Releasing zones at shutdown

- **Decision**: shutdown iterates `GameplayState.Zones.Values`, releasing every
  zone's `EnvironmentBatch` (not only the active zone's), in addition to the
  existing debug-draw/gizmo releases.
- **Rationale**: each zone owns a grow-only instance buffer allocated on first
  gather; the current shutdown releases only the single zone's batch. Iterating the
  map releases all of them before the device is destroyed (spec FR-014).
- **Alternatives considered**: releasing on deactivate (rejected — the buffer would
  be reallocated on every return, defeating the grow-only design).

## Decision 10 — Add `InputText` to the ImGui binding for naming

- **Decision**: add one P/Invoke, `igInputText`, to `render/ImGui.fs` (already
  exported by the vendored `libcimgui.so`; verified with `nm`, so no native
  rebuild), and keep the pending name in a small reusable byte buffer on
  `EditorState` (`NewZoneName`). The Zones section decodes the buffer to a string
  (up to the first NUL) and passes it to `GameplayState.addZone`, clearing the
  buffer on a successful add.
- **Rationale**: naming needs a text field, and the binding is deliberately grown
  as widgets are required (its header says so). One reused buffer on `EditorState`
  avoids per-frame allocation, and the symbol is a standard cimgui export already
  present in the shared object.
- **Alternatives considered**: a modal/popup prompt (rejected — the same
  `InputText` need plus extra plumbing); a counter-generated name (rejected —
  Decision 7); a native shim (rejected — the symbol already exists).

## Decision 11 — Deleting a zone releases it; the last zone is protected

- **Decision**: `GameplayState.deleteZone` rejects deletion when only one zone
  remains (`invalidOp`). To delete the active zone it first switches to another
  remaining zone (reusing `switchZone`, which detaches the outgoing bodies and
  re-frames the camera), then releases the deleted zone; to delete an inactive zone
  it releases it directly, leaving the active zone and camera untouched. Releasing
  (`Zone.release`) destroys the zone's bodies, releases its actors'/props'/parts'
  renderers and its `EnvironmentBatch`, and clears `BodyToEntity`; then the entry is
  removed from `Zones` and `UndoByZone`. Deletion is not an undoable edit.
- **Rationale**: the workspace, systems, and viewport all assume an active zone, so
  the last zone is protected. Routing the active case through `switchZone` keeps a
  single activate/deactivate path and gets the camera and undo-swap behaviour for
  free. Releasing bodies and GPU resources is required so add/delete cycles do not
  leak; a switch (detach) is deliberately not a release, and a delete always
  follows a detach (the zone is inactive by the time it is released).
- **Alternatives considered**: allowing zero zones (rejected — nothing defines an
  empty active zone); making deletion undoable (rejected — it would have to snapshot
  every entity, out of scope and inconsistent with add/switch); leaving a deleted
  zone's GPU resources for shutdown to free (rejected — leaks during a session).

## Decision 12 — Invalid zone actions are prevented in the UI; guards are preconditions

- **Decision**: the Zones section disables its controls for invalid actions —
  *Add Zone* while the typed name is empty or already used, *Del* when only one zone
  remains — so those calls are unreachable through the editor. `addZone`,
  `deleteZone`, and `switchZone` still raise (`invalidOp`) on an invalid argument,
  as precondition checks for a programming error. No catch-all is added around the
  engine's frame loop.
- **Rationale**: the constitution separates expected failure paths (`Result`/
  `Option`) from programming bugs (`raise`). A duplicate name or deleting the last
  zone is an expected *user* mistake, so the fix is to not offer the action; the
  check and the click happen in the same single-threaded frame, so the UI gate is
  reliable. Once the UI prevents the expected case, an invalid call is a bug and
  raising is correct. A blanket try/catch would hide bugs and continue from an
  unknown state, which is worse than a loud failure at this phase.
- **Alternatives considered**: returning `Result` from the container functions
  (rejected — every caller would re-handle a case the UI already prevents, and the
  precondition framing is clearer); a catch-all around the frame in `Engine.run`
  (rejected — hides real bugs and resumes from corrupted state; if shipping
  robustness is wanted later, use a scoped handler at the screen-update boundary
  that logs and takes a defined safe action); silently ignoring invalid adds/deletes
  (rejected — violates fail-loudly and can mask a stale-UI bug).

## No unresolved clarifications

All technology choices and behaviours are determined by the spec, the architecture
doc, and the existing code; there are no `NEEDS CLARIFICATION` items to carry into
design.
