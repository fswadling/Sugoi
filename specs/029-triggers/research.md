# Research: Triggers

Decisions derived from the feature spec, the two reference implementations
(`ocaml-game/lib/core/{entity,zone,systems}.ml`, `lib/editor/{viewport,inspector}.ml`,
`lib/render/debug_draw.ml`, its `specs/014-waypoints-triggers-cast`;
`odin-game/src/{entity,systems}.odin`, `docs/TRIGGERS_CUE_PLAN.md`), and this
project's own entity/command/editor model after `specs/026-waypoints`,
`specs/027-entity-rename`, and `specs/028-waypoint-chains`.

## Decision 1 — `Trigger` composes the shared `Entity` base

- **Decision**: `Trigger = { Base : Entity; Radius : float32; Height : float32;
  Interactable : bool; Prompt : string; mutable Triggered : bool }`, replacing the
  placeholder `{ Position : Vector3 }`.
- **Rationale**: this is the decision `specs/026-waypoints` already made for
  waypoints, and it buys the same things here: the editor's selection, gizmo,
  Inspector, focus, nudge, rename, duplicate, delete and undo all work on an
  entity obtained from `Zone.tryEntityAt`, with no trigger branches. It also lets
  the trigger travel through the *shared* `Spawn`/`Delete`/`Duplicate` commands,
  which carry an `Entity` — the references cannot do this because their waypoint
  and trigger records are flat (no embedded entity) and therefore need their own
  command families.
- **Alternatives considered**: keeping a flat record as both references do
  (rejected — it would mean a parallel command family, a second
  selection/inspection path, and a second ref vocabulary, for no gain); putting
  radius/height on the transform's scale (rejected — a trigger's size is not its
  scale, and FR-014 requires them independent).

## Decision 2 — `Triggered` is mutable, runtime, and never authored

- **Decision**: `mutable Triggered : bool`, written in place through a `byref`
  slot read each update. It is never stored by a command, never edited by the
  Inspector, and never displayed there.
- **Rationale**: it is written every frame for every trigger, so an in-place write
  is the cheap shape — the same reasoning that makes `Transform`'s fields mutable
  while every other payload field is assigned by record copy. It is runtime state,
  not authored data, so recording it in undo would be wrong. Displaying it would
  be worse than useless: the editor's panels are hidden while playing, so it could
  only ever be shown stale from a previous play session; the play-mode overlay
  (Decision 10) is where inside-ness is observed.
- **Alternatives considered**: keeping the flag off the payload in a zone-level
  set of inside refs (rejected — it introduces entries for triggers that no longer
  exist, i.e. stale state to clean up, where the field dies with its trigger);
  not storing it at all and recomputing on demand (rejected — the overlay draws it
  and the interaction will act on it, and containment would be recomputed per
  reader).

## Decision 3 — Trigger storage mirrors waypoints; no trigger command family

- **Decision**: `Zone.Triggers : SlotMap<Trigger>`, `triggerRef`, and a trigger
  case in `isAlive`, `tryEntityAt`, `setTransform`, `removeEntity`,
  `setActorWithAsset`, and `bindingAt` (answering the existing `bindingNone`).
- **Rationale**: exactly the shape `specs/026-waypoints` established, for the same
  reason — a trigger is an entity addressed by a generation-checked ref, so the
  shared commands and the editor need no special case. Answering `bindingNone` is
  what lets the shared `Delete` command record a trigger and undo restore it.
- **Alternatives considered**: a trigger-specific command family as both
  references have (rejected — it exists there only because their trigger is not an
  entity; here it would duplicate the shared command machinery).

## Decision 4 — Detection is one function, and the player position comes from the entity

- **Decision**: `Systems.updateTriggers zone &closest` updates every trigger's flag from
  the player's position and writes the closest interactable one into `closest` (or
  `ValueNone`). It derives the player's position itself: the designated
  actor's entity position, which `syncActors` has already written from its body
  earlier in the same frame. With no live designation it clears every trigger and
  answers `ValueNone`.
- **Rationale**: one pass over the triggers answers both questions (who is inside,
  and which interactable is nearest), so splitting a pure scan from a mutating
  system — which the reference does only because its system also forks cues —
  would be two functions where one suffices. Reading the *entity* position rather
  than the body keeps the whole function free of the physics world and the GPU, so
  it is unit-testable over a zone and a player actor (FR-012), which is how a
  headless suite can cover the containment rule exhaustively. The derived position
  and the answer are both `voption`s, so the every-frame path allocates nothing —
  `Some` over a struct boxes. The flag write and the nearest-candidate choice share
  one `match` on the position, and the loop walks `Slots` to read and write the
  payload through `Get`'s byref rather than copying it, as `syncActors` does:
  `IteriAlive` can only pass it by copy, because an F# lambda parameter cannot be a
  `byref` (FS3300, "not permitted by the rules of Common IL").
- **Alternatives considered**: injecting the position as a parameter, as the
  reference's pure scan does (rejected — the only caller would pass the value this
  function can derive, and the player lookup has exactly one home); reading the
  player's physics body position (rejected — it would drag the physics world in
  and force the tests to build bodies, for a value the same frame has already
  copied onto the entity).

## Decision 5 — No rising-edge output in this slice

- **Decision**: the update reports inside-ness per trigger, and does not return or
  store "entered this frame".
- **Rationale**: with no cue to run, an `entered` value would have no consumer —
  just-in-case machinery the constitution rules out — while inside-ness is what
  makes an edge derivable at all (the reference's `inside && not was` is one line
  over this state). The spec therefore requires that remaining inside be
  *distinguishable* from entering, and pins the edge itself to the cue slice
  (FR-010).
- **Alternatives considered**: returning an `entered` list now (rejected — dead
  code, and it would allocate a list per frame for nothing); dropping the
  requirement to distinguish entering (rejected — it is what a future one-per-entry
  action depends on, so it is stated now).

## Decision 6 — The tracked interactable lives on the gameplay state

- **Decision**: `GameplayState.InteractableTrigger : EntityRef voption`, assigned
  each play frame from the update's answer.
- **Rationale**: both of its consumers are per-frame — the prompt drawn this frame,
  and the interaction that will act on it — and the reference keeps it in the same
  place (its gameplay state). The update clears it whenever nothing qualifies, so
  a stale value cannot survive a frame in which detection ran. Edit mode never
  reads it, and zone switching only happens while editing. It is a `voption` for
  the same reason `Zone.Player` is: it is reassigned every play frame, so a plain
  `option` would box a struct each time.
- **Alternatives considered**: recomputing the nearest interactable in the draw
  (rejected — duplicated traversal, and the interaction would have to agree with
  it separately); storing it on the zone (rejected — it is per-frame screen state,
  not authored zone data, and a zone held it would have to be cleared on every
  switch).

## Decision 7 — One command edits a trigger's properties

- **Decision**: `TriggerSet of Ref : EntityRef * Before : Trigger * After : Trigger`
  (whole record), applied and undone through `Zone.setTrigger`. The Inspector
  builds `After` from the *live* payload, so the runtime flag rides along rather
  than being authored.
- **Rationale**: the property edits (radius, height, interactable, prompt,
  enabled) are all whole-record writes, and one case serves all of them — the same
  shape both references use (`Trigger_set`). A per-field command family would be
  four cases for four scalars of one record, and each would still carry the record
  to be able to restore it. Transform edits stay with the shared `Transform`
  command, so a `TriggerSet` never carries a move.
- **Alternatives considered**: field-specific commands (rejected above); reusing
  `Rename` for the prompt string (rejected — the prompt is not the entity's name,
  and the record write covers it); excluding `Triggered` from the command by
  snapshotting a separate authoring record (rejected — a second record to keep in
  step, for a field the update rewrites every frame anyway).

## Decision 8 — A new `DebugDraw.cylinder` primitive

- **Decision**: `cylinder (dd) (center) (halfHeight) (radius) (c)` — two 16-segment
  rings at the ends and a vertical line at each segment angle, matching the
  reference's geometry, with the call site passing the volume's centre.
- **Rationale**: the render module is where primitives live (`line`,
  `wireframeBox`, `sphere`, `capsule`), and a trigger volume is a genuine cylinder.
  Reusing `capsule` would draw hemispherical caps the volume does not have — a
  wireframe that lies about containment, which is the one thing it must not do.
  The signature matches the family's centre/half-extent form, so the call site
  computes the centre once from the base and height.
- **Alternatives considered**: composing the cylinder at the call site from
  `line`s (rejected — the segment loop and the trig belong with the other
  primitives, and the capsule's private circle helper shows the cost of
  re-deriving it); a `base`/`top` signature as the reference has (rejected —
  inconsistent with `capsule`/`sphere`, which take a centre and an extent).

## Decision 9 — Radius and height are edited with a drag widget

- **Decision**: add one P/Invoke, `igDragFloat(label, float32& v, speed, min, max,
  format, flags)`, and commit an edit when the item is deactivated after editing
  (`isItemDeactivatedAfterEdit`, already bound for the name field in
  `specs/027-entity-rename`), so a drag is one undo step rather than one per frame.
- **Rationale**: the vendored library already exports `igDragFloat` (verified), so
  this is a binding addition with no native rebuild — the same situation as the
  two bindings the earlier specs added. Committing on release rather than per
  frame is the same rule the name field uses for typing.
- **Alternatives considered**: `igInputFloat` (exported too, but a drag is the
  better control for a size); dragging the volume with a gizmo handle (rejected —
  it needs a new handle kind and picking path, and the reference edits the numbers);
  editing sizes from the transform's scale (rejected by FR-014).

## Decision 10 — A play-mode overlay is the observable

- **Decision**: while playing, draw the tracked interactable trigger's prompt, and
  the names of the triggers the player is currently inside.
- **Rationale**: firing is deferred, and the editor's panels — including the
  Inspector — are hidden during play, so without this the author has no way to see
  that detection works. The names come from the same per-frame state the detection
  writes, so the overlay adds no new state and no new traversal beyond reading the
  active zone's triggers once per drawn frame. The prompt is authored content and
  is what the reference shows; the name line is this port's stand-in for the cue
  that would otherwise be the visible effect.
- **Alternatives considered**: the prompt alone (rejected — non-interactable
  triggers would then be invisible in play, and they are the type that will fire on
  entry); drawing trigger markers during play (rejected — the editor's markers are
  deliberately edit-only, and it would change what play mode looks like); a debug
  log (rejected — the author is looking at the game, not the console).
