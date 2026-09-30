# Phase 0 Research: Undoable Player Designation

The feature is small and the port already has every mechanism it needs, so this phase is mostly about
*not* inventing anything: each decision below is "reuse what spec 043 established for the light", the
one exception being the control that makes an undo observable.

## Decision 1 — The designation becomes a zone-level command, beside `SetEnvironment` and `SetLight`

- **Decision**: add one case to `state/Command.fs`'s union —
  `SetPlayer of Previous : Handle voption * Next : Handle voption` — applied as `zone.Player <- next` and
  undone as `zone.Player <- previous`.
- **Rationale**: this is the shape the union already uses for the other two pieces of state that belong
  to a *zone* rather than to an entity: `SetEnvironment (Previous, Next)` and `SetLight (Previous, Next)`
  (spec 043). Carrying both values is what makes it reversible without a capture step, and it is what
  FR-002 needs — undoing a re-designation must restore the *previous actor*, which only the stored
  `Previous` knows. The command may hold this value safely: the union's rule is that a payload is
  authored state and never a renderer or a physics attachment, and a designation is authored state.
- **Alternatives considered**: a command carrying the `EntityRef` and re-deriving the handle (an extra
  resolution step for no gain, and it would have to fail loudly for a ref that is no longer an actor);
  storing "no player" as a sentinel instead of a `voption` (the field is already a `voption`, and
  `ValueNone` is a real, required state — FR-002); a separate `SetPlayer`/`ClearPlayer` pair (the
  feature adds no clear action, FR-008).

## Decision 2 — The recording helper is `Selection.setPlayer`, shaped exactly like `Selection.setLight`

- **Decision**: a helper in `editor/Selection.fs`, next to `setLight`: read the zone's current
  designation, compute the next, **return without recording when the two are equal**, otherwise apply the
  command and push one step onto the active zone's stack.
- **Rationale**: FR-004 ("re-setting the actor who is already the player adds no step") is precisely the
  rule `setLight` already implements with `if after = before then () else`, and keeping the two helpers
  identical is what stops the editor growing two ways to record a zone's own setting. It is also the only
  place the *no-op* can be decided: `Command.apply` has no way to know whether the value changed.
- **Alternatives considered**: pushing from the Inspector directly (the button would then carry the
  no-op test and the stack plumbing, which is exactly the duplication `Selection` exists to prevent);
  letting `UndoStack.push` drop a no-op command (the stack cannot know which commands are no-ops, and
  coalescing is deliberately limited to consecutive `Transform`s).

## Decision 3 — No new payload type, and no change to `Zone` or `Systems`

- **Decision**: the command carries `SlotMap.Handle voption` — the very type of `Zone.Player` — and
  `world/Zone.fs` (`mutable Player : Handle voption`) and `world/Systems.fs`
  (`isPlayerLive`, which already treats a missing or stale designation as a no-op) are untouched.
- **Rationale**: the field and the stale-tolerance already exist and are not the problem; the problem is
  only that one write to the field bypasses the command system. Minimalism (principle I) says change the
  write, not the data. `Handle` is a `[<Struct>]` of two `uint32`s (`core/SlotMap.fs`), so the payload is
  two words and a tag — and its `Generation` is what keeps a *restored* designation from resolving to an
  actor that later reused the slot (Edge Cases).
- **Alternatives considered**: a dedicated `PlayerDesignation` wrapper type (a fourth spelling of the same
  two fields, which `ZoneTemplate`'s own doc comment warns against); a `bool voption`-style "is player"
  per actor (actors do not own this — the zone does, and per the current design exactly one actor per zone
  is the player).

## Decision 4 — The Inspector's control reads the designation, so the edit is observable

- **Decision**: the actor section's button becomes state-aware: it calls `Selection.setPlayer` and is
  drawn through `ImGui.beginDisabled`/`endDisabled` with a different label when the selected actor already
  is the active zone's player.
- **Rationale**: FR-005. Today **nothing** in the editor shows the designation, so after this feature an
  undo would have no visible effect in the editor — an undo you cannot see is not verifiable, and
  Story 3's acceptance scenarios could not be checked without entering play mode. The reference has the
  same idea: `odin-game`'s properties panel gives the actor a **"Player Locomotion" checkbox**
  (`src/editor_properties.odin:969`), which reads back whether that entity is the player. The bindings
  needed already exist (`render/ImGui.fs` binds `igBeginDisabled`/`igEndDisabled` alongside `igButton`),
  so this adds no native surface.
- **Alternatives considered**: marking the player in the **Outliner** (a second surface to keep in step,
  and the Outliner deliberately marks nothing today); a checkbox exactly as the reference has (it would
  add *clearing* the designation, which is scope the feature does not include — FR-008 — and the brief is
  to make the existing action reversible); leaving the control as-is and relying on play mode for
  observability (true, but it makes an editor edit unverifiable in the editor).

## Decision 5 — The design doc is amended, not just the code

- **Decision**: `docs/EDITOR_DESIGN.md` changes in three places: **principle 4** drops the player
  designation from its list of direct-write exceptions (leaving the collider overlay's visualisation
  preference and the cast binding), **D-022** stops offering the designation as a precedent for a direct
  write, and a new **D-025** records the reclassification — the designation is a *zone's own* setting, so
  it rides the zone's own stack, which is D-024's reasoning for the light applied to the other value of
  the same kind. **D-011** needs no change: it lists only the project-scoped surfaces, and never mentions
  the designation.
- **Rationale**: the feature exists because a documented decision was wrong, so the document that records
  it is part of the change (specification III and the project's spec-first rule). Principle 4's list and
  D-022's "like a zone's player designation" are where that decision is written down; leaving either
  saying the opposite would re-plant the mistake for the next reader — which is what happened between
  principle 4 and D-024.
- **Alternatives considered**: changing the code and leaving the doc (the documented reason *is* the
  defect); rewriting D-011 wholesale (it is still right about the project-scoped surfaces — the cast
  binding and the registry — which is what D-022/D-023 explain at length).

## Decision 6 — The reversible designation is asserted headlessly; only the control needs the walkthrough

- **Decision**: three headless cases, one per claim. The command's round trip in `test/ZoneTests.fs`,
  beside the `SetLight` case: a first designation undoes back to **no player** and redoes, and a
  re-designation undoes back to the **previous actor**. The per-zone history in
  `test/GameplayStateTests.fs`: an undo in the zone on screen takes back that zone's own edit and cannot
  reach the other zone's designation. And the no-op rule in `test/SelectionTests.fs`, where the module's
  own "records nothing" cases live: binding the actor who already is the player leaves the history alone.
  Only the control's state (Story 3) and play mode need the run walkthrough.
- **Rationale**: the test project cannot open a GPU device, so `Inspector.draw` is not reachable from it —
  the same boundary spec 043 hit, and the reason its `SetLight` case is a command test rather than a
  slider test. What is left is pure: the round trip is FR-001/FR-002, the two-zone case is FR-003, and the
  no-op is FR-004.
- **Alternatives considered**: asserting through `UndoStack` alone (what the `ZoneTests` case does, and
  the level the existing light case works at — kept, but on its own it cannot reach FR-003 or FR-004);
  leaving FR-004 to the walkthrough because `setLight` has no such test (rejected: `test/SelectionTests.fs`
  exists for exactly this shape — "the cases it must refuse or ignore" — so driving `Selection.setPlayer`
  there is the module's convention rather than a new one).
