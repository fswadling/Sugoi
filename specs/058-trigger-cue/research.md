# Phase 0 Research: Trigger Cue

Decisions derived from the feature spec, the reference implementation (`odin-game/src/{entity,systems,gameplay_screen,cue_editor,editor_properties}.odin`
and `docs/TRIGGERS_CUE_PLAN.md`), and this project's own cue model (`specs/046`), cue control (`specs/047`), trigger
detection (`specs/029`) and command/undo model (`specs/048`, `docs/EDITOR_DESIGN.md` D-011).

## Decision 1 — The cue is a field on the trigger's authored state

- **Decision**: `TriggerState` gains `Cue : Cue.Cue`, defaulting to `Fin`. It is part of the trigger's authored value,
  captured by `Zone.captureTrigger`, carried by `TriggerCreate`/`TriggerDelete`/`TriggerSet`, and preserved across a
  zone switch and an instantiate.
- **Rationale**: this is the port's answer to the reference's `cue_root` on `Trigger`, expressed the way every other
  kind is: authored state lives on the `…State` record, so undo is the *same command* (`TriggerSet`) and no new command
  is added. It also makes duplicate and delete carry the cue for free (`specs/029` Decision 1/3).
- **Alternatives considered**: **a zone-level `Dictionary<EntityRef, Cue>`** (rejected — it duplicates capture,
  duplicate, delete and restore, and can hold entries for triggers that no longer exist); **a separate `SetTriggerCue`
  command** (rejected — `TriggerSet` already carries the whole record, so a per-field command would be a second way to
  say the same thing, `docs/EDITOR_DESIGN.md` D-011).

## Decision 2 — `world/Cue.fs` moves above `world/Entity.fs`

- **Decision**: relocate the `Cue.fs` compile entry to just before `Entity.fs` (still after `Advent.fs` and
  `Dialogue.fs`, which it already depends on).
- **Rationale**: F# compiles in list order, so a record that names `Cue` must follow `Cue.fs`. Nothing between the new
  and old positions (Entity, EnvironmentBatch, ZoneTemplate, Zone, ZoneRegistry) is depended on by `Cue.fs`, so the
  move is pure and keeps dependencies one-directional (Constitution IV).
- **Alternatives considered**: **boxing the cue as `obj`** (rejected — a type-unsafe field for a compile-order
  convenience); **storing the cue off `TriggerState`** (rejected in Decision 1).

## Decision 3 — `forkInto` moves into `world/Cue.fs`

- **Decision**: `forkInto (joined) (current)` moves from `world/CueEngine.fs` to `world/Cue.fs` (unchanged body).
- **Rationale**: it is a pure cue function with no state dependency, and the trigger system (`world/Systems.fs`) is
  compiled *before* the engine, so it could not otherwise call it. Relocating the function (rather than duplicating its
  three lines) keeps one definition, and `open Cue`-based tests are unaffected. `CueEngine`'s header note that
  "`forkInto` runs an arriving cue beside the running one" moves with it.
- **Alternatives considered**: **have `Systems` not fork and return a pending ref** (rejected — it splits the edge
  detection from the action it exists for, and the reference's trigger system forks directly); **keep `forkInto` in the
  engine and reorder the engine above `Systems`** (rejected — the engine must stay after `GameplayState`, which is
  after `Systems`; that inversion is deliberate, `specs/046`/`specs/048`).

## Decision 4 — The entry fire lives in `Systems.updateTriggers`

- **Decision**: `updateTriggers` gains a `cue : byref<Cue>` parameter. It already computes the rising edge (`inside &&
  not was`); on an edge for an enabled, non-interactable trigger it forks the trigger's cue into `cue`.
- **Rationale**: the edge is computed there and nowhere else, and `specs/029` Decision 5 explicitly pinned the edge to
  "the cue slice" — this is that slice. Taking the screen cue byref keeps the function device-free and physics-free, so
  the entry fire is asserted headlessly (`FR-006`). The idle path still allocates nothing: a fork allocates one node,
  and only on an entry.
- **Alternatives considered**: **fire from `GameplayScreen` after `updateTriggers`** (rejected — the rising edge would
  have to be re-derived outside, or exposed as a per-frame `entered` list, which is per-frame allocation for an event
  that only happens on entry); **a separate trigger-cue system** (rejected — it would rescan the triggers a second time
  per frame).

## Decision 5 — The interact fire lives in `screen/GameplayScreen.fs`

- **Decision**: on `Input.isPressed Key.E` while playing, fork the tracked trigger's cue. Both fire sites share one
  `Systems.forkTriggerCue (zone) (ref : EntityRef voption) (cue : byref<Cue>)` helper that resolves the ref, skips an
  empty cue, and forks.
- **Rationale**: input edge detection lives in the screen (the engine gathers it), and the tracked interactable ref is
  already on the state (`GameplayState.InteractableTrigger`). A shared helper keeps the "which cue, skip `Fin`" rule in
  one place for a headless test to exercise (`FR-004`). `isPressed` is edge, so a held key fires once.
- **Alternatives considered**: **reading the key inside `updateTriggers`** (rejected — the system deliberately takes no
  input and opens no device, `specs/029` Decision 4); **an `Interaction` command/op** (out of scope; there is no
  interaction vocabulary).

## Decision 6 — The inline editor reuses the control via a relocated `CueEditor`

- **Decision**: move `editor/CueTree.fs` and `editor/CueEditor.fs` above `editor/Inspector.fs` (after
  `editor/Selection.fs`). Parameterise `CueEditor`'s per-node text and waypoint-combo helpers over their transient
  field state and a staged commit, and add `CueEditor.drawTrigger (shared) (state) (ref)` that draws a trigger's cue
  with its own selection/text state and commits through `Selection.updateTriggerRef`.
- **Rationale**: the Inspector must call the cue editor, so the editor must be compiled before it, and the trigger
  editor must call `Selection.updateTriggerRef`, so the editor must be compiled after `Selection`. Parameterising the
  two helpers (rather than copying ~40 lines into the Inspector) keeps the same text/waypoint semantics for both
  editors (`FR-011`), and a distinct ImGui id prefix plus distinct `EditorState` fields keeps the two editors from
  colliding (`FR-007`).
- **Alternatives considered**: **duplicating the text/waypoint helpers in `Inspector.fs`** (rejected — two copies of
  the same field rules would drift); **putting the inline editor in `Inspector.fs` over `CueTree` directly** (rejected
  for the same duplication, plus it would move the cue-editing knowledge out of the cue module).

## Decision 7 — No new command; a trigger cue edit is a `TriggerSet` step

- **Decision**: the inline editor commits `{ trigger with Cue = next }` through the existing
  `Selection.updateTriggerRef`, i.e. one `TriggerSet` step per accepted edit.
- **Rationale**: `TriggerSet` already carries the whole `TriggerState`, so once the cue is a field the command needs no
  change (`docs/EDITOR_DESIGN.md` D-011, "a kind-specific edit is recorded by that kind's command"). Because the step
  is a zone step and not a `SetCue`, it is **not** pruned on entering play (`state/History.fs` `pruneCueSteps` drops
  only `SetCue`), so a trigger's cue stays undoable across a play session — which is correct: it is authored zone data
  that play does not consume.
- **Alternatives considered**: **`SetCue` for a trigger's cue** (rejected — `SetCue` writes the *screen's* cue, and it
  is pruned on play).

## Decision 8 — `TriggerState` equality over a cue is safe and only cold-path

- **Decision**: rely on `updateTriggerRef`'s `if after = before` comparison including the cue field.
- **Rationale**: `Prime.FDeque` answers structural equality (verified: two separately built equal deques compare
  equal), so the generated `TriggerState` equality is total and correct. Scalar edits (`{ t with Radius = … }`) keep the
  *same* cue reference, so the equality short-circuits by reference and costs nothing; only a cue edit compares the
  trees, and that is an editor-cold path. No bespoke comparison is introduced.
- **Alternatives considered**: **hand-written equality excluding the cue** (rejected — it is more code to keep in step
  and would let a real cue change go unrecorded).

## Decision 9 — The interact key is `E`, and there is no separate interaction flag

- **Decision**: bind `E` for interaction (recorded in the spec's Assumptions). Fire whenever playing; no
  `interactions_enabled` gate is added.
- **Rationale**: `E` is unused in play mode (it is an editor camera/nudge key only while editing; editing and playing
  are exclusive), so it is the free, conventional interact key. The reference's `interactions_enabled` flag is driven by
  a cue op this cut does not have, so adding the flag would be just-in-case machinery (Constitution I); play mode
  itself is the gate (`specs/046` FR-002).
- **Alternatives considered**: **`F`** (also mapped, but `E` is the established interact key convention and is what the
  reference uses); **no interact fire in this cut** (rejected — `specs/029` US3 already shows the prompt for exactly
  this action, and leaving it unwired would strand the interactable half).
