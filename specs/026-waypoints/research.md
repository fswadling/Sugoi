# Research: Waypoints

Decisions derived from `docs/ROADMAP.md` phase 10 ("Story primitives"),
`docs/ARCHITECTURE.md` (the zone's entity collections and the entity/ref model),
the feature spec, and the existing zone/editor/undo code.

## Decision 1 — Waypoints live in their own slot map on the zone

- **Decision**: `Zone` gains `Waypoints : SlotMap<Waypoint>` (capacity 128),
  sized in `Zone.create` like `Actors` (64) and `Props` (32). `EntityKind.Waypoint`
  becomes a stored kind: `waypointRef index generation` builds a ref with the
  waypoint kind and `Epoch = 0u`, and `Zone.isAlive`, `tryEntityAt`,
  `setTransform`, `removeEntity`, and `setActorWithAsset` all handle it. The
  spec's "waypoint" is therefore a slot-map entity, not a parallel list.
- **Rationale**: a waypoint is addressed by an `EntityRef` — it is selected,
  duplicated, deleted, restored, and transformed — and every one of those paths in
  this codebase is written against a slot map with a generation. Reusing the
  generational handle is what gives the feature its guarantees for free: a ref
  survives unrelated spawns (slots are stable), a deleted waypoint's ref goes
  stale (generation), and a reused slot cannot be addressed by the old ref
  (spec FR-007). A separate container would have to reimplement all of that, and
  would need a second vocabulary for "which entity is selected".
- **Alternatives considered**: a plain `ResizeArray<Waypoint>` or array with an
  index ref (rejected — index refs alias after a removal, which is exactly the
  bug the ref model exists to prevent, and the editor's command/undo path is
  written for `EntityRef`); reusing the `Props` slot map with a flag (rejected —
  props carry renderers, bodies, and asset bindings that a waypoint must not
  have, so every prop path would need a branch); a `Dictionary<string, Waypoint>`
  keyed by name (rejected — the name is decoration here, not identity; the
  scenario/registry phases key by name where a name *is* the identity, which is
  why zones do, but a waypoint is referenced by handle).

## Decision 2 — A waypoint composes the shared `Entity` base, not a bare position

- **Decision**: `Waypoint` changes from `{ Position : Vector3 }` to
  `{ Base : Entity }`, matching `Actor`, `Prop`, and environment parts.
- **Rationale**: the editor's selection, gizmo, inspector, nudge, duplicate, and
  outliner-row code all operate on an `Entity` obtained from
  `Zone.tryEntityAt`. Composing the base means a waypoint is not a special case
  anywhere in that code: `Zone.tryEntityAt` returns its `Base`, so
  `Editor.gizmoCenter`, `Editor.focus`, the inspector, drag/nudge, and
  `Selection.duplicateRef` all work with no waypoint branches. It also gives the
  waypoint the shared flags (`Enabled`, `Visible`, `Selectable`, `Draggable`) and
  a name, so a later rename UI or a "hide waypoints" toggle needs no new field.
  The struct stays small and allocation-free.
- **Alternatives considered**: keeping `{ Position : Vector3 }` and adding a
  separate rotation field (rejected — it re-solves orientation outside the
  project's `Transform`, and still leaves the waypoint invisible to the shared
  editor paths); `{ Position; Rotation; Name }` without the base (rejected —
  duplicates three of the base's fields and diverges from the other kinds);
  giving waypoints a renderer-bearing entity variant (rejected — Decision 3).

## Decision 3 — A waypoint carries no body, renderer, or asset binding

- **Decision**: a waypoint has no `Physics` attachment, no renderer, and no
  asset. `Zone.bindingAt` returns a new `bindingNone`
  (`{ AssetId = None; SubmeshIndex = 0; SubmeshCount = 0 }`) for the waypoint
  kind instead of falling into the catch-all `failwithf`, and the command path
  that re-occupies a slot (`setActorWithAsset`) stores `{ Base = entity }` for
  the kind, ignoring the binding. `Zone.spawnWaypoint` needs neither the physics
  world nor the asset manager, and neither does `Spawning.spawnWaypoint`.
- **Rationale**: a waypoint marks intent — a place and a facing — and nothing
  about it should affect the world. This keeps waypoints out of the broadphase
  and out of the render batches, so they cost nothing per frame beyond their
  markers, and it keeps `Delete`/`Spawn`/`Duplicate` commands uniform (the
  `Command` type stores a binding for every kind; `bindingNone` is the honest
  value for a kind that has none, and the restore path already ignores it for
  spawning because the kind branch rebuilds from the entity). It also makes
  deleting a waypoint trivially leak-free: there is nothing to destroy or
  release.
- **Alternatives considered**: raising from `bindingAt` for waypoints (rejected —
  `Selection.deleteRef` calls `bindingAt` before recording the `Delete` command,
  so every waypoint deletion would fail; the project's rule is that a catch-all
  raise means "this kind cannot exist here", and a waypoint can); giving a
  waypoint a static body so it can be click-picked (rejected — an invisible
  collider in the world is a real cost and a real hazard, and viewport picking
  was not needed for the feature; see Decision 9); a nullable binding field
  (rejected — every consumer would branch on it, and `Command` already models
  "no asset" as `AssetId = None`).

## Decision 4 — A waypoint's facing is the rotation applied to −Z, seeded from the camera

- **Decision**: a waypoint's facing direction is
  `Vector3.Transform(-Vector3.UnitZ, transform.Rotation)` — the project's
  existing camera-forward convention, used by the gameplay camera and stated in
  the viewport comment. The *Add at Camera* action builds that rotation with
  `Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(camera.Position,
  Camera.forward camera, camera.Up))`, and takes the position from
  `camera.Position`; *Add Waypoint* uses `transformIdentity`.
- **Rationale**: the purpose of a waypoint's orientation is to say which way
  somebody faces when they get there, and the thing that will consume it is a
  camera or an actor's heading, both of which already mean "−Z rotated by the
  rotation". Adopting the convention rather than inventing a yaw angle keeps one
  definition. `Matrix4x4.CreateWorld` builds exactly that basis (its local +Z is
  the negated forward, so local −Z lands on the view direction), and
  `CreateFromRotationMatrix` converts it to the `Quaternion` the `Transform`
  stores. Verified against the level, steeply-down, and steeply-up camera cases
  (`dot(facing, camera.forward) = 1.0` in all three), which is why the spec can
  claim the placement needs no follow-up adjustment (SC-003).
- **Alternatives considered**: storing a yaw `float32` (rejected — a second
  rotation representation to keep in step with the `Transform`, and it loses the
  pitch/roll a waypoint may later want); composing yaw + pitch from
  `Camera.forward` by hand with `atan2` and `asin` (rejected — more code, gimbal
  cases to reason about, and it must reproduce what `CreateWorld` already
  guarantees); a "look at the selection" add action (rejected — not the gesture
  the authoring flow needs, and it needs a selection to exist).
- **Note**: `CreateWorld`'s `up` reference is the camera's stored `Up` (world
  up). If the camera points exactly along ±Y its basis is already degenerate for
  `CreateLookAt` too, so a camera-seeded waypoint there inherits a pre-existing
  camera property rather than adding a new failure mode (spec Edge Cases).

## Decision 5 — The arrow head is four line barbs built from the waypoint's own axes

- **Decision**: a waypoint marker is a wireframe box at its position, a shaft
  from the position to `position + forward * 0.75`, and a head of four barbs from
  the shaft's tip back to a point 0.25 along the shaft, offset 0.12 along the
  waypoint's rotated local X and Y axes. The marker uses the selection colour
  when selected and white otherwise. The head is composed inline in
  `Viewport.buildLines`; no `DebugDraw` primitive is added.
- **Rationale**: `render/DebugDraw.fs` is a line-only pipeline (its rasterizer
  state draws `GPUPrimitiveType.LineList`), so a solid or cone head is not
  available without changing the pipeline. The waypoint's rotation supplies two
  axes exactly perpendicular to its facing, so the barbs are perpendicular by
  construction: no cross product against an arbitrary reference, and no
  degenerate case when the waypoint faces straight up or down (spec FR-011). Four
  barbs rather than two mean the head reads from any camera angle, including from
  directly above, where a two-barb chevron in one plane degenerates into the
  shaft's own line.
- **Alternatives considered**: a two-segment chevron in the local X/Y plane
  (rejected — invisible as an arrow from above/below and ambiguous when the
  camera is edge-on to its plane); a smaller wireframe box as the head (rejected —
  reads as a second marker, not a direction); a `DebugDraw.cone`-style helper
  that samples a circle and fans lines to the tip (rejected — many more segments
  for a shape that is only ever this size, and a brand-new primitive for a single
  call site); extending the pipeline with a fill primitive (rejected — a
  pipeline, shader, and blend change for a marker).
- **Note**: `headLen` (0.25) and `headRadius` (0.12) sit beside the existing
  `gizmoLen` (0.75) as module-level constants in `editor/Viewport.fs`, which is
  where the other marker sizes already live.

## Decision 6 — The Waypoints section lives in the Outliner, after the Triggers header

- **Decision**: `editor/Outliner.fs` gains a **Waypoints** `collapsingHeader` at
  the end of the entity sections, after the existing **Triggers** header, listing
  one row per waypoint (select on click, with the shared inline *Del* and *Dup*
  buttons), preceded by the two add buttons.
- **Rationale**: the Outliner is the entity-hierarchy panel — zones, actors,
  props, parts — and a waypoint is a zone entity, so it belongs in that hierarchy
  next to the kinds it sits beside in the zone. A collapsing header keeps the
  list tucked away when unused and, unlike a new docked panel, needs no change to
  the native dock layout compiled into `libcimgui.so`. It is drawn last, after
  the placeholder Triggers section, because that is where the two
  authoring-only sections land: Actors, Props, and Parts are the kinds whose
  contents put geometry on screen and into the broadphase, while a waypoint (like
  a trigger) is a tag on the world rather than a part of it.
- **Alternatives considered**: a dedicated left-dock panel tabbed with the
  Outliner (considered and dropped — it would need a native dock-layout rebuild
  for a list that is a handful of rows); a section directly above the Triggers
  header (considered and dropped — the reading order of the sections is cosmetic,
  nothing reads it, and either position is equivalent, so the section was appended
  where it keeps the existing list untouched); a viewport overlay list
  (rejected — the Outliner is already the selection surface and the viewport has
  no list UI).

## Decision 7 — Two explicit add actions, and spawning goes through `Spawning`

- **Decision**: the section offers *Add Waypoint* and *Add at Camera* as two
  buttons on one line, rather than one button plus a mode or a modifier.
  `editor/Spawning.fs` gains `spawnWaypoint`, which calls `Zone.spawnWaypoint`
  and immediately pushes the reversing `Spawn` command and selects the new
  waypoint; `Spawning.duplicate` gains a `Waypoint` case. The Outliner calls only
  `Spawning`, never `Zone.spawn*`.
- **Rationale**: the two placements are the two gestures an author wants, and
  making them separate buttons keeps each one a single, labelled action — no
  hidden mode, and the origin-versus-camera choice is visible before clicking.
  Routing creation through `Spawning` is the module's stated contract: every
  spawn is paired with the command that reverses it *immediately*, because a
  recorded command names a slot and an untracked `Add` could let that slot be
  reused, which would make a later undo hit a taken slot. It also means the new
  waypoint is selected (and thus immediately draggable) for free.
- **Alternatives considered**: one button that uses the camera when a modifier
  is held (rejected — undiscoverable, and the two actions are not variants of one
  idea); adding at the camera by default (rejected — the origin placement is the
  predictable one, and it is what the section had before); a "new waypoint
  follows the camera until placed" mode (rejected — a persistent mode with no
  home on `EditorState` yet, and it makes an accidental click place a waypoint);
  calling `Zone.spawnWaypoint` directly from the Outliner (rejected — it breaks
  the `Spawning` invariant above and would create a waypoint with no undo step).

## Decision 8 — Undo and redo reuse the existing command paths unchanged

- **Decision**: no change to `state/Command.fs` or `UndoStack`. `Spawn`,
  `Duplicate`, and `Delete` are stored as before, with the waypoint's
  `bindingNone`; apply/undo reach it through the kind dispatch in
  `Zone.setActorWithAsset` (occupy) and `Zone.removeEntity` (free). Transforms
  coalesce through the existing consecutive-`Transform` merge, so a gizmo drag or
  held nudge on a waypoint is one undo step.
- **Rationale**: the commands already carry everything needed — the ref, the
  entity, and a binding (which a waypoint does not use) — and they already
  dispatch on the ref's kind. Adding a waypoint case to the two dispatch sites is
  the whole cost, and the undo semantics the spec asks for (restore transform and
  identity; a delete's undo re-occupies the recorded slot) follow from the
  existing behaviour rather than being implemented again. This is the payoff of
  Decision 1: the ref, generation, and slot-freeing rules that make restore
  correct are the ones already in place.
- **Alternatives considered**: dedicated waypoint commands (rejected —
  duplication would be total, and `UndoStack` would need a second merge rule);
  recording waypoint edits as a single "set waypoints" snapshot (rejected — it
  breaks the per-entity step granularity the editor has everywhere else).

## Decision 9 — Selection is Outliner-driven; viewport picking is out of scope

- **Decision**: a waypoint is selected by clicking its row in the Waypoints
  section. Clicking its marker in the viewport does not select it: viewport
  picking (`Viewport.pick`) is a physics raycast against colliders, and a
  waypoint has none. The gizmo then applies to whatever is selected, including a
  waypoint, because `Editor.gizmoCenter` resolves the selection through
  `Zone.tryEntityAt`.
- **Rationale**: the feature's authoring loop is satisfied by the list — add,
  click the row, drag — and the alternative (a collider per waypoint, or a
  ray-vs-marker test added to the pick path) buys a convenience at the cost of
  either putting invisible bodies in the world (contradicting FR-009) or a second
  picking mechanism with its own marker-size tolerances. Leaving it out keeps the
  change small and honest; the gap is recorded in the spec's Assumptions so it is
  a decision rather than an oversight.
- **Alternatives considered**: a marker-only ray/sphere test added to
  `Viewport.pick` before the physics raycast (rejected for this feature — a
  second pick path and its tuning, for a list that already sits in the panel
  beside the viewport; it is the natural follow-up if authors ask for it);
  giving each waypoint a sensor body (rejected — see Decision 3).

## Decision 10 — Scope: no name editing, no triggers, nothing consumes a waypoint

- **Decision**: every new waypoint is named `Waypoint` and the name is
  display-only (the Inspector already shows the name of the selected entity, and
  nothing renames it). `Trigger` keeps its placeholder `{ Position : Vector3 }`
  and the Outliner's Triggers section keeps showing "(none)". Nothing reads a
  waypoint yet.
- **Rationale**: the phase's consumers are the cue engine and the story systems,
  which arrive with their own specs; inventing a waypoint consumer here would
  pre-empt them. Rename needs an interaction design (inline field versus a
  dialog) and, unlike zones, a waypoint's name is not its identity — it is
  addressed by handle — so deferring it costs nothing and cannot cause a re-key.
- **Alternatives considered**: a rename field in the Inspector or the row
  (rejected — a UI design decision with no consumer yet, and it would need a
  recorded command to be undoable); auto-numbered names (`Waypoint 2`) (rejected —
  a counter with no purpose while names are unused, and it invites the
  duplicate-name question that handle-addressing avoids); implementing `Trigger`
  together with waypoints (rejected — a trigger needs the conditions and firing
  rules that phase 10 specifies separately, and bundling them would make this
  feature's exit criteria unclear).

## No unresolved clarifications

Every technology choice and behaviour here is determined by the spec, the
architecture doc, the roadmap phase, and the existing code; there are no
`NEEDS CLARIFICATION` items to carry into design. The spec was written
retrospectively, after the behaviour was demonstrated, so the questions that
would have been raised were instead settled against the working feature and
recorded above.
