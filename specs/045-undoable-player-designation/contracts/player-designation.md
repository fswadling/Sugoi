# Contract: Undoable Player Designation

The surfaces this feature touches, and what explicitly does not change. The designation is a zone's own
value gaining a reversal path; nothing about how a zone, an actor or the renderer works is altered.

## `state/Command.fs` — CHANGED

- The union gains **one** case, `SetPlayer of Previous : Handle voption * Next : Handle voption`, beside
  the two existing zone-level cases (`SetEnvironment`, `SetLight`). It carries no new payload type: the
  value is `Zone.Player`'s own type, `SlotMap.Handle voption` (`core/SlotMap.fs`), so `Command.fs` needs
  `SlotMap` in scope — an `open SlotMap` beside its existing `open Entity`/`open Zone`, which is the only
  import the change adds.
- `apply` writes `zone.Player <- next`; `undo` writes `zone.Player <- previous`. Both are total — the
  case cannot fail, because its payload is exactly what the field accepts.
- `Batch`, `UndoStack.push`/`undo`/`redo` and the `Transform`-only coalescing rule are unchanged: this
  case never coalesces (a designation is discrete, spec Assumptions).

## `editor/Selection.fs` — CHANGED

- A new `setPlayer (shared) (state) (ref : EntityRef)`, shaped like `setLight`: resolve the active zone,
  read the previous designation, **no-op when unchanged**, otherwise apply the command and push exactly
  one step onto the active zone's stack.
- It becomes the only writer of a player designation in the editor. Nothing else in `Selection` changes.

## `editor/Inspector.fs` — CHANGED

- The actor section's **"Set as Player"** control stops writing `zone.Player` directly and calls
  `Selection.setPlayer` instead.
- The control reads the active zone's designation so the edit is observable (FR-005): when the selected
  actor already is the player it is drawn disabled and relabelled, through the existing
  `ImGui.beginDisabled`/`endDisabled` and `ImGui.button` bindings — no new native surface.
- Every other Inspector section is unchanged.

## `docs/EDITOR_DESIGN.md` — CHANGED

- Principle 4 and D-022 stop listing the player designation as a direct write; a new **D-025** records the
  reclassification (see [data-model.md](../data-model.md)). D-011 needs no change — it never mentioned it.

## `test/ZoneTests.fs` — CHANGED

- A command round-trip case beside the existing `SetLight` one: a first designation undoes back to **no
  player** and redoes; a re-designation undoes back to the **previous actor**.

## Explicitly unchanged

- **`world/Zone.fs`** — `mutable Player : Handle voption` already exists; no field, no capture, no
  template member. A designation is still not part of a registered place (`ZoneTemplate`), so
  `ZoneRegistry` and `Spawning` are untouched (FR-007).
- **`world/Systems.fs`** — `playerHandleOf` (which already fails loudly for a non-actor) and
  `isPlayerLive` (which already treats a missing or stale designation as a no-op) are unchanged, as are
  `playerInput`, `cameraFollow`, `animate`, `updateTriggers` and `syncActors` (FR-006).
- **Play mode** — no in-game action designates or clears a player, and the play chain is untouched.
- **The other commands** — every entity create/delete/edit, `LinkSpawn`, `SetEnvironment`, `SetLight` and
  `Batch` are unchanged, along with the redo-clearing and transform-coalescing rules (FR-009).
- **The deliberately direct writes** — the cast binding (D-022), the zone registry's registrations
  (D-023), and the actor's animation and morph edits (specs 036/037) stay direct writes. They are
  project- or renderer-scoped; the player designation is neither.
- **The renderer** — no shader, pipeline, texture or per-frame buffer is touched; the frame's cost is
  unchanged (FR-009).
- **The Outliner** — no player marker is added; the designation is shown where the action is, in the
  Inspector's actor section.
- **`GameplayState`** — `UndoByZone`, `switchZone`/`enterZone` and their swap of the per-zone stacks are
  untouched; the feature relies on them as they are (FR-003).
- **Persistence** — there is none to change.
