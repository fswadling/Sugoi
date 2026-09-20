# Research: Entity Authored State

Decisions derived from the feature spec, the review of `specs/030-entity-command-families`,
and the code as 030 left it (`world/Entity.fs`, `world/Zone.fs`, `state/Command.fs`).

## Decision 1 — The authored value is a record nested in the entity

- **Decision**: `Actor`, `Prop`, and `Trigger` gain a `State` field holding a new
  authored record — `ActorState`, `PropState`, `TriggerState` — and keep their derived
  fields as siblings (`Physics`, `Renderer`, `Triggered`). A command that creates,
  deletes, or copies such an entity carries the authored record.
- **Rationale**: the payload type then *is* the authored value, so FR-002 holds by
  construction rather than by a clearing step. It is also the only shape that keeps a
  single payload per entity: the derived resources stay inline in the same slot, so the
  slot maps keep one contiguous payload per kind.
- **Alternative considered and rejected**: leaving the records flat and holding the
  derived resources beside each kind's store in the zone. It would have touched roughly
  half as many read sites (~99 against the ~84 this design ended up qualifying), but the
  resources would be addressed by an index carrying no generation, so every access would
  have to prove liveness against the authored store first — a per-site obligation of
  exactly the kind the project's reference handles exist to remove. That safety argument,
  not the churn, is what decided it.

## Decision 2 — Reads are spelled through `State`

- **Decision**: there are no read-through accessors. An authored property is read as
  `x.State.…` (`a.State.Base.Transform.Position`, `t.State.Prompt`), and an authored write
  is a whole-record copy-and-update on an authored path
  (`a <- { a with State.Base.Transform = … }`). `State` is an immutable field, so an
  in-place write through it does not compile and the compiler finds every write site.
- **Rationale**: `State` is the entity's authored half, so spelling it at each read puts
  the boundary in the code and not only in the types. The accessors were the one part of
  this design that was not necessary, and they put a struct-returning member call on every
  authored read — about half the app's read sites are in per-frame code
  (`Systems.animate`/`syncActors`/`updateTriggers`, `Viewport.buildLines`,
  `GameplayScreen.renderScene`, `EnvironmentBatch`).
- **This reversed the plan before the feature was committed.** `plan`, `contracts`, and
  `data-model` were first written with accessors so that the existing reads compiled
  untouched; they are corrected to say what is true rather than what was planned, as
  030's research was.
- **Counted, not assumed**: 84 authored reads are now spelled through `State` — 47 in the
  app across 8 files, 37 across 7 test modules; 25 of the app's are in per-frame paths
  (`Systems` 12, `Viewport` 8, `GameplayScreen` 3, `EnvironmentBatch` 2). The compiler named
  them: deleting the members turned each into an error, and a site already reading `State`
  simply did not error, so none could be missed.
- **Alternatives considered**: keeping the accessors (the original plan) — rejected: a
  member call per read in code that runs every frame, for a boundary that showed in the
  type rather than at the site. A mutable accessor — impossible: a member cannot be an
  assignment target.

## Decision 3 — Only the kinds that carry something derived or runtime are split

- **Decision**: `Actor`, `Prop` (and therefore environment parts, which share the
  record), and `Trigger` are split. `Entity` and `Waypoint` are unchanged.
- **Rationale**: a waypoint's record is already pure authored state — a base entity and
  the reference of the waypoint its chain continues at — so splitting it would add a
  field (and a `.State` on every read) to express nothing. The feature's guarantee is about the
  kinds that *can* be recorded unsafely, and those are the three that carry a body, a
  renderer, or a runtime flag.
- **Alternatives considered**: splitting uniformly for symmetry (rejected — a `State`
  wrapper on a record that is already the state is noise, and the codebase already has
  kinds of deliberately different shapes).

## Decision 4 — `capture*` becomes resolve-and-read, and the clearing step is deleted

- **Decision**: `Zone.captureActor`/`captureProp`/`capturePart`/`captureTrigger` keep
  their names and roles — resolve a ref to the value a command carries, raising for a
  stale or wrong-kind ref — but their bodies become a resolve plus a read of `State`.
  `captureWaypoint` is unchanged. There is no blanking anywhere.
- **Rationale**: FR-005. The functions are still the one place the editor reads an
  entity's authored value, which is worth keeping: the resolve-and-raise precondition
  is real behaviour, and `Selection`/`Spawning` build commands from it. What goes is the
  *clearing*, which existed only to make a live record safe to store.
- **Alternatives considered**: deleting `capture*` and letting the editor read
  `zone.Actors.Get h).State` directly (rejected — it puts a slot-map handle and its
  precondition into the editor, which is what the function exists to hide); renaming them
  to something like `authoredActor` (rejected — needless churn across 030's tests and
  docs for a name that still describes the job).

## Decision 5 — The derived fields are still forced, by the constructions

- **Decision**: `occupy*` keep constructing the entity field by field and now take the
  authored value whole (`{ State = state; Physics = attachment; Renderer = … }`); the
  `spawn*` builders keep the parameters they had and assemble that value inside, which is
  where a new authored field's initial value is decided.
- **Rationale**: FR-006's other half. Nesting makes the authored side automatic (a new
  authored field is carried because the whole state passes through), and the explicit
  construction of the *entity* is what still fails the build when a derived field is
  added. Neither direction can be done silently, which is the whole point of the shape.
- **Alternatives considered**: a record-update over a partially built entity (rejected —
  it would drop a newly added derived field silently, exactly the failure 030 removed).

## Decision 6 — The environment lifecycle keeps its teardown

- **Decision**: `deactivateEnvironment` still clears a part's `Physics` and `Renderer`
  (now a record-update that leaves `State` alone), and `attachPart` still rebuilds them.
  No change.
- **Rationale**: FR-011, and the distinction the spec records: that clearing happens
  because the environment is unloaded, not to make something recordable. It is a
  lifecycle behaviour with an observable consequence (an inactive zone's parts have no
  bodies and hold no GPU resources), and the nested shape makes it read correctly —
  the update touches the resource fields and provably cannot disturb the authored state.
- **Alternatives considered**: unifying it with the (now deleted) recording clear under
  one helper (rejected — they have different reasons and different lifetimes; a shared
  helper would imply the recording path still needs clearing).

## Decision 7 — Runtime state stays a sibling field

- **Decision**: `Trigger.Triggered` remains a mutable field beside `State`, not folded
  into the authored record or into a "resources" record.
- **Rationale**: FR-010. It is recomputed in place every update, it is not produced from
  the authored value, and it has no construction step — so it is neither authored nor
  derived, and the three-way distinction the spec draws survives in the types. A separate
  `TriggerResources` record holding one `Triggered` flag would be a wrapper for nothing.
- **Alternatives considered**: grouping `Triggered` with the derived fields under a
  `Resources` record for symmetry with the other kinds (rejected — `Actor`/`Prop` group
  their resources because there are two of them and they are built together; a lone flag
  is clearer as a field).

## No unresolved clarifications

Every choice is settled by the spec, by 030's decisions, or by the measurements recorded
in Decision 2. No `NEEDS CLARIFICATION` remains for Phase 1.
