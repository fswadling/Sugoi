# Research: Actor and Prop Spawn Anchors

**Feature**: `101-actor-prop-anchors`  
**Date**: 2026-10-17

## Decision 1: Store the shared anchor choice in `EditorState`

**Decision**: Move the editor-only `SpawnAnchor` union, the ordered `spawnAnchors`
palette, and `anchorName` from `editor/Spawning.fs` into `state/EditorState.fs`, and add
a `SpawnAt : SpawnAnchor` field defaulting to `Origin`. `anchorAvailable` stays in
`Spawning` because it reads the zone's live player designation.

**Rationale**: the Asset Browser needs a persistent selected anchor, and `EditorState`
`EditorState` is the project's home for transient editor UI state. `EditorState` is compiled before
`editor/Spawning.fs`, so the type must live at or below `EditorState` for the field to
name it. Moving the small vocabulary is a mechanical refactor; the alternative is hidden
module-level mutable state in the Asset Browser. In `Spawning`, use qualified cases
(`EditorState.Origin`, `EditorState.Camera`, `EditorState.Player`) rather than
`open EditorState`, because the `Camera` case would shadow the `Camera` module.

**Alternatives considered**:

- A module-level mutable in `editor/AssetBrowser.fs`. It avoids the move but hides
  editor state outside `EditorState`, where the rest of the panel state lives.
- A per-asset anchor popup. It needs no persistent selector state, but the clarified UX
  is one shared control, and a popup per asset would turn one-click spawning into two
  clicks for every row.
- Store a string or integer in `EditorState`. It would avoid the move but lose the
  closed union and the compiler's exhaustiveness checks.

## Decision 2: Camera placement uses the exact camera position and horizontal yaw

**Decision**: A Camera-anchored actor or prop uses `camera.Position` and a rotation
about world +Y by `atan2(forward.X, forward.Z)`, where `forward` is the camera's view
direction. Camera pitch and roll are not copied. If the horizontal component is zero
(the camera looks exactly up or down), the yaw is zero.

**Rationale**: actors and props are upright world objects. Copying the camera's full
rotation would tilt props and can face actors backwards because actors use local +Z as
forward while the camera's look-at basis maps -Z to the view direction. Horizontal yaw
makes actors face the view direction and leaves props un-tilted. The exact position was
the clarified choice; objects may float, and the author can move them.

**Alternatives considered**:

- Full camera rotation. It is compact but tilts props and has the wrong forward
  convention for actors.
- Ground projection. It would place objects on the floor but was rejected in
  clarification and requires a collision query.

## Decision 3: Player placement copies the authored transform

**Decision**: A Player-anchored actor or prop uses the live player actor's authored
feet position and authored rotation. The anchor is unavailable without a live
designation. Overlap is allowed.

**Rationale**: it matches the waypoint and trigger player anchors (the actor's authored
transform is the source of truth) and makes "at the player" mean exactly that. The
object is a one-shot placement, not an attachment, so it does not follow her later.

**Alternatives considered**:

- A fixed offset to reduce overlap. It introduces an arbitrary direction/distance and
  makes the anchor less predictable.
- A ground query. It is unnecessary: the player's feet are already the authored position.

## Decision 4: Reuse the existing actor/prop spawn paths

**Decision**: Add `spawnActorAt` and `spawnPropAt` in `editor/Spawning.fs`. Each applies
the anchor transform to the entity passed by the Asset Browser, then delegates to the
existing `spawnActor`/`spawnProp`, which already check capacity, select the new entity,
and record `ActorCreate`/`PropCreate`.

**Rationale**: the asset binding, default authored state, renderer/body creation, undo,
and selection already live in those functions. The anchor adapters only replace
`Position`/`Rotation`; no new command or resource path is needed. Keeping the existing
`spawnActor`/`spawnProp` public also preserves their use in tests and any future
non-anchored caller.

**Alternatives considered**:

- A new command or a post-spawn transform step. Both duplicate or split an operation
  that is already one atomic create.
- Assign the transform after spawning. It would visibly move the entity and record a
  second history step.

## Decision 5: Keep asset names; add no automatic naming

**Decision**: A spawned actor or prop keeps the asset's name, exactly as today. The
anchor changes position and rotation only.

**Rationale**: actor names are used by cue operations, and props use the asset label.
Automatic anchor naming would change existing cue addressing and is a separate UX
decision. The clarified scope is placement.

**Alternatives considered**:

- Anchor-informed automatic names. They would help distinguish repeated spawns but
  change actor naming and cue references, which is out of scope.

## Decision 6: One shared selector at the top of the Asset Browser

**Decision**: Draw one `Spawn at` combo before the Actors and Props sections. It lists
`Origin`, `Camera`, and `Player`, defaults to Origin, and applies to both sections.
`Player` is disabled when unavailable; the asset spawn buttons are disabled when the
selected anchor is unavailable or the kind is full. Environments ignore the selector.

**Rationale**: it keeps the fast one-click asset spawn, gives a single visible placement
mode, and ensures adding an anchor adds one combo item rather than a control per asset.
The selector value is transient editor state: not undoable, not persisted.

**Alternatives considered**:

- One selector per section. It allows different anchors per section but adds a second
  control and mode; the clarified choice is one shared selector.
- Per-asset menus. They avoid a mode but turn every spawn into two clicks.

## Decision 7: Headless spawn tests plus the manual quickstart

**Decision**: Cover actor and prop anchor placement, camera yaw (including the vertical
view), player availability, unit scale, asset-name preservation, capacity, selection,
and undo/redo with `test/SpawningTests.fs`. Validate the selector and the asset buttons
manually with `quickstart.md`.

**Rationale**: the existing tests already drive the real `Spawning`/`Zone`/undo paths
without a GPU, and the project has no headless ImGui harness. The Asset Browser wiring is
a thin adapter over the tested spawn functions.

**Alternatives considered**:

- An ImGui harness. It would add native context and input scripting for one combo and a
  few buttons, which no existing feature uses.
