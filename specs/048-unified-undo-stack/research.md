# Phase 0 Research: One undo history on the gameplay state

Ten decisions. D1–D4 are the design; D5–D7 are the clarifications this feature was asked to settle; D8–D10 are
what the feature asserts and what it moves.

## Decision 1 — One history, on the gameplay state; the per-zone swap goes

- **Decision**: the editor keeps **one** history of scene edits, held as `EditorState.Undo` and reached from the
  gameplay state (`state.Editor.Undo`). `GameplayState.UndoByZone` is deleted, and `enterZone`/`swapZone` no longer
  park or load a stack. A zone switch does not touch the history at all.
- **Rationale**: this is the defect the feature exists to fix. `specs/024` Decision 5 introduced the side map
  because "commands name entity refs … valid only in the zone where they were recorded, so a global stack would
  apply an edit to the wrong zone" — a real hazard, but one whose cure (a history per zone) discards the session's
  order: an edit made a second ago is unreachable the moment another zone is looked at. Every later surface that
  is not a single zone has had to buy its way out of that cure (`specs/032` D-022, `specs/034` D-023, `specs/047`
  D-010). One history at the state level is where the order lives; the hazard is answered by Decision 2.
- **Reverses**: `specs/024` Decision 5, FR-010 ("Undo and redo MUST apply to the zone in which the edit was
  recorded") and FR-011 ("Adding, switching, and deleting zones … MUST NOT themselves be recorded as undoable
  scene edits"). Both are amended by this feature, not quietly contradicted.
- **Alternatives considered**: **keep the side map and add a project-scoped stack beside it** — rejected: two
  histories is the same defect one level up, and neither can order an edit that crosses between them. **Clear the
  history on a switch** — rejected by `specs/024` D-5 for the reason this feature restates: it discards work.

## Decision 2 — A zone move is a step, so a command carries no zone

- **Decision**: choosing a different zone to be active is recorded as `ZoneMove of Previous * Next` on the same
  history as an entity edit. Undo of a move switches back; redo switches forward. No command carries a zone name,
  and the existing `apply`/`undo` for an entity command continue to act on **the active zone**.
- **Rationale**: last-in-first-out does the work the zone name would have. If every change of the active zone is
  on the stack, then when undo reaches a step, the active zone is provably the zone that step was recorded in:

  ```
  edit in A   →  move A→B  →  edit in B
  undo: edit in B (active B) → move back to A → edit in A (active A)
  ```

  Correctness is then structural rather than a rule every future command must remember, and nothing about the
  existing per-kind command family changes (FR-010 of the spec).
- **Reverses**: the alternative `specs/024` Decision 5 rejected — "commands carrying their zone and undo
  activating it (rejected — undo would teleport the view across zones)". The reversal is deliberate: with one
  history the view *must* follow undo, or an undone edit happens in a zone the developer is not looking at. The
  objection's real content — that the view moves somewhere the developer did not ask for — is answered by the move
  being a **recorded step** rather than an implicit jump: undo never moves the view except by stepping over a move
  the developer made.
- **Alternatives considered**: **tag each command with its zone; undo activates that zone** — rejected: it puts a
  zone on every command and lets undo jump the view on its own, which is the behaviour D-5 objected to, now
  without the ordering that makes the jump predictable. **Keep the side map, no moves** — that is D-1's status quo.

## Decision 3 — A zone add or delete that changes the active zone is one step

- **Decision**: `ZoneAdd` and `ZoneDelete` carry the zone that was active before them and restore it on undo. The
  zone change they cause is part of that step, not a `ZoneMove` of its own; the editor records a `ZoneMove` only
  when the developer chooses a different zone.
- **Rationale**: "one user action is one undo" is the rule the whole history rests on. An add that took two undos
  would also make the acceptance criteria contradict each other, since a move step would exist for a change the
  developer never made.
- **Clarified**: the spec's Question 3 (2026-10-01), answered "one step".

## Decision 4 — A deleted zone is retained as its authored contents, and rebuilt through `occupy*`

- **Decision**: `ZoneDelete` carries a `ZoneContents`: the zone's name, its actors, props, waypoints and triggers
  (each as the `EntityRef` ↔ authored-state pair a `*Create`/`*Delete` command already carries), its loaded
  environment, its light and its player designation. Undoing the delete creates a zone of that name and puts the
  contents back through the same `Zone.occupy*` calls an entity create uses; redoing it removes the zone again.
- **Rationale**: authored state only is the rule the command family already follows — a payload "cannot express a
  physics attachment, a renderer, or a runtime flag" (`state/Command.fs`), and `Zone.occupy*` rebuilds exactly
  those. Capturing reuses the `Zone.capture*` readers that build the existing create payloads, so a delete adds no
  new vocabulary for what an actor or a trigger *is*. Restoring by ref keeps identity (FR-009): a restored actor is
  the actor it was, so a step recorded before the delete still resolves.
- **`ZoneTemplate` is not this type, and the distinction is worth stating**: `captureTemplate` (spec 034) records
  the environment, its parts and the zone's placed props for *registration* — a copy intended to outlive the zone
  and be instantiated elsewhere. It has no actors, waypoints, triggers or player, so it is not a zone's full
  contents; the two are allowed to look similar and are not merged.
- **Where it lives**: the record and its reader/writer (`captureZone`/`occupyZone`) are in `world/Zone.fs`, with the
  `capture*`/`occupy*` family they are built from — *not* beside the command that carries them. A payload type
  belongs where the thing it describes is (`ActorState`/`PropState`/`Environment` in `world/Entity.fs`,
  `LightSettings` in `render/Texture.fs`), and `ZoneTemplate` is the same kind of composite in `world/` too.
  `world/Zone.fs` is compiled before `state/Command.fs`, so the `ZoneDelete` case can name the record.
- **Reverses**: `specs/024` Decision 11's "making deletion undoable (rejected — it would have to snapshot every
  entity, out of scope and inconsistent with add/switch)" and FR-015's "and undo history". The inconsistency D-11
  named is now the other way round: add and switch are revertible, so an irreversible delete would be the odd one
  out.
- **Alternatives considered**: **hold the whole `Zone` value** — rejected: a `Zone` owns live physics bodies, GPU
  renderers and a batch, so retaining it means retaining resources the zone no longer holds, and restoring it
  would mean two owners of the same bodies. **Reuse the registration `ZoneTemplate`** — rejected above: it is not
  the whole zone. **A `Command list` of `*Create`s** — workable, but it spells the contents in a second language
  and has to be kept in the right order; a record plus one `occupyContents` routine says it once.

## Decision 5 — The command runners move behind `GameplayState`; the `Command` type does not

- **Decision**: `state/Command.fs` keeps the `Command` type, the per-command `apply`/`undo` that act *within* a
  zone, and all of `UndoStack` — `create`, `push`, and the zone-level `undo`/`redo`, which step a stack against one
  zone and need no gameplay state. A new module — `state/History.fs`, compiled **after**
  `state/GameplayState.fs` — holds the state-level dispatch (`apply`/`undo` that route a `ZoneMove`/`ZoneAdd`/
  `ZoneDelete` to the zone work) and the **state-level** steppers `undoHistory`/`redoHistory`.
- **Rationale**: adding or deleting a zone and setting the active zone are `GameplayState` operations, and
  `state/Command.fs` is compiled before `state/GameplayState.fs`. Moving *the type* below the state is the
  alternative `specs/024` D-5 rejected as cyclic — `EditorState` must name `UndoStack`, which holds `Command`s,
  and `GameplayState` names `EditorState`. Moving only the *runners* keeps the type where it is and repeats the
  one inversion the project already uses for the cue (`world/CueEngine.fs` after `state/GameplayState.fs`). The
  in-zone `apply`/`undo` do not move: they take a `Zone`, not the state, and nothing about them changes.
- **Alternatives considered**: **put the dispatch in `state/GameplayState.fs`** — rejected: the module is the
  state, and the history is the editor's behaviour over it; the file would carry command routing it does not
  otherwise know about. **Put it in `editor/`** — rejected: the history is not a panel, and `editor/` is interaction
  and panels.

## Decision 6 — Undoing a move restores the zone, not the camera or the selection

- **Decision**: undoing or redoing a `ZoneMove` re-runs the zone change: the camera re-frames on the zone it lands
  in (its player, else its origin), and the selection stays cleared. A move step carries no camera pose and no
  previous selection.
- **Rationale**: the camera is a viewport, not authored scene state, so making it undoable would make `Ctrl+Z` move
  the developer's view; and re-framing on arrival is the rule a manual switch already follows, so undo of a move
  behaves exactly like making that move by hand. A camera that was flown freely before a switch is not restored —
  accepted deliberately.
- **Clarified**: the spec's Question 4 (2026-10-01), answered "only the zone".

## Decision 7 — Moves do not coalesce

- **Decision**: each change of the active zone is its own step. The one merge rule stays as it is: consecutive
  `Transform`s of the same ref collapse into one step, and a zone change between two of them prevents the merge
  (they are different zones' refs).
- **Rationale**: the history is meant to be an honest account of the session; a run of navigation undone one move
  at a time is predictable, and per-move steps keep the "one action is one undo" rule literal. The alternative
  buys a shorter history at the cost of a step whose endpoints the developer never makes one action of.
- **Clarified**: the spec's Question 5 (2026-10-01), answered "every move is its own step".
- **Alternatives considered**: **merge consecutive moves like a transform gesture** — rejected above; it was the
  recommendation and the request chose against it, which is recorded here rather than left implicit.

## Decision 8 — The play/edit boundary is unchanged for zone edits, and the cue's seam is left open

- **Decision**: zone edits ride the one history across the play/edit toggle exactly as they do today — the history
  is not cleared or pruned when Play begins. The cue keeps its own history (`specs/047`) and is **not** moved onto
  this history by this feature.
- **Rationale**: a zone edit is authored state and does not go stale when the game plays, so nothing about it needs
  dropping. A cue *does* go stale — the cue advances while playing — which is why `specs/047` drops its history on
  Play; that rule is the next feature's to reconcile, and this feature leaves the boundary where it is rather than
  guessing at it. The one thing this feature does for that future is make the history a state-level object the cue
  can be added to as a command.
- **Alternatives considered**: **clear the whole history when Play begins, unifying the rule now** — rejected: it
  would throw away zone edits for no reason, and it would decide the cue's lifecycle before the cue is a step.

## Decision 9 — What the headless suite asserts

- **Decision**: `test/GameplayStateTests.fs` and the new history tests assert, with no device: an edit in A and an
  edit in B separated by a move are both undone, in order, with the active zone following (SC-001); an add and its
  undo leave the same zones and the same active zone (SC-002); a delete and its undo round-trip a zone with the
  same entities at the same refs, the same environment, light and player (SC-003); no step's undo touches a zone
  other than its own; and undo/redo through the whole history returns the project to its starting zones and
  entities (SC-004).
- **Rationale**: this is the feature's own requirement (FR-012 of `specs/046`'s lineage: exercisable with no
  graphical interface), and every scenario here is reachable headlessly — a zone, an entity and a command need no
  GPU, which is how `test/GameplayStateTests.fs` already builds them (`Unchecked.defaultof` for the drawers).
  Asserting the *state that changed* rather than a request for it is the house's stronger form.
- **Alternatives considered**: asserting the command list itself — rejected: it pins the representation rather
  than the behaviour, and the point is that the zone came back.

## Decision 10 — Terminology, where the superseded decisions are recorded, and which documents move

- **Decision**: **history** is the canonical term for the feature (one ordered timeline of an applied part and a
  redo part). `UndoStack` keeps its name as the type holding those two lists — a rename would touch every field
  and test for no behaviour change — and the prose stops saying "stack" for the timeline.
- **Rationale**: the clarify pass flagged "history" vs "stack" as the one loose term; picking the concept word for
  the concept and leaving the type name alone is the minimal fix.
- **Prior specs are historical artefacts and are left untouched.** A spec records the design at the moment it was
  written, and editing one afterwards is how the record stops being trustworthy — so the statements this feature
  supersedes are recorded **here**, and `specs/024`, `specs/030`, `specs/032`, `specs/043` and `specs/045` are not
  amended:

  | Superseded statement | Where it was written | What this feature does instead |
  |---|---|---|
  | "Undo and redo MUST apply to the zone in which the edit was recorded" | `specs/024` D-5, FR-010 | One history. The moves between zones are steps on it, so an undo still lands in the zone the edit was made in |
  | "Adding, switching, and deleting zones are container/view actions … MUST NOT themselves be recorded as undoable scene edits" | `specs/024` FR-011 | All three are undoable steps, one step per action (D3) |
  | "Deletion is not an undoable edit" / "its entities, environment, player designation, and undo history" | `specs/024` D-11, FR-015 | A delete carries the zone's authored contents and is undone like any other step (D4) |
  | "Undo history remains per zone" | `specs/030` edge case + assumption | One history; the ordering of edits *within* a zone is unchanged |
  | the binding is a direct write **because** the stacks are per zone | `specs/032` FR-011 rationale | Still a direct write, for its own reason; the per-zone reason is gone |
  | a light change rides "the active zone's own (per-zone) undo stack" | `specs/043` FR-006, SC-007 rationale | It rides the one history; the light is still the zone's own state, so the step still lands in that zone |
  | "the undo stacks are per zone, swapped on a zone switch" | `specs/045` plan, contracts | One history, no swap; the designation still lives on `Zone.Player` |

  **What is *not* superseded**: `specs/024`'s guards all stand — the last zone is protected, a delete releases
  bodies and GPU resources, deleting the active zone first makes another active — and `specs/043`'s and
  `specs/045`'s behaviour is unchanged. Only the sentences that said *why* are, and only the ones above.
- **The documents that move** (these are the living design, not a record): `docs/EDITOR_DESIGN.md` principle 4,
  D-010, D-011, D-022 and D-023; `docs/ARCHITECTURE.md`'s "the undo history [is] held per zone";
  `docs/PROJECT_STRUCTURE.md` (the new `state/History.fs`); and the headers of `editor/CastPanel.fs` and
  `editor/ZoneRegistryPanel.fs`, whose comments describe current behaviour.
