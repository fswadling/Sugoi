# Research: Entity Command Families

Decisions derived from the feature spec, the three losses recorded in it, and the
code as `specs/028-waypoint-chains` and `specs/029-triggers` left it
(`world/{Entity,Zone}.fs`, `state/Command.fs`, `editor/{Spawning,Selection,Inspector}.fs`).

## Decision 1 — One create and one delete per kind, with the shared record removed

- **Decision**: `Command`'s `Spawn`/`Delete`/`Duplicate` — all three carrying the
  same `(Entity, AssetBinding)` pair — are replaced by `ActorCreate`/`ActorDelete`,
  `PropCreate`/`PropDelete`, `PartCreate`/`PartDelete`, `WaypointCreate`/
  `WaypointDelete`, and `TriggerCreate`/`TriggerDelete`, each carrying that kind's
  own record. The shared pair, and the kind-agnostic restore path
  (`Zone.setActorWithAsset`) it fed, are deleted.
- **Rationale**: the shared payload is the part every kind has *in common*, so
  anything a kind carries beyond it has nowhere to be recorded — which is the
  origin of all three losses this feature exists to fix. Per-kind records make each
  kind's authored state recordable by construction (FR-001, FR-002). The arms are
  not copies: a restore genuinely differs per kind — an actor rebuilds a capsule
  body and an actor renderer, a prop a mesh body and an environment renderer, a part
  additionally resolves its environment by epoch, a waypoint occupies a bare slot,
  and a trigger takes its authored shape.
- **The one cost, accepted deliberately**: the command type grows from 11 to 16
  cases, and `apply`/`undo` from 10 arms each to 16. That is the price of a faithful
  payload, and the owner chose it over a single family with a kind-tagged payload
  (Decision 1's alternative), on the grounds that the entity model already
  keeps one storage per kind and never discriminates on the hot path (FR-010), so
  per-kind handling matches how the entities are built.
- **Alternatives considered**: keeping the shared pair and widening it with
  per-kind extras (rejected — a grab bag every kind pays for); keeping the shared
  pair for the kinds it already fits, i.e. props and parts, and giving only actor,
  waypoint, and trigger their own (rejected — a new kind could then be added against
  a path that records less than its state, which is what FR-006 forbids; and a
  prop's fit today is a coincidence of its current fields, not a property of props).

## Decision 2 — The payload is the kind's own record, captured with derived and runtime fields cleared

- **Decision**: a command carries the concrete record (`Actor`, `Prop`,
  `Waypoint`, `Trigger`), captured through one function per kind
  (`Zone.captureActor`, `captureProp`, `capturePart`, `captureWaypoint`,
  `captureTrigger`) which returns the live record with its **derived** fields
  (`Physics`, `Renderer`) and its **runtime** fields (`Trigger.Triggered`) blanked —
  written as a record-update, `{ a with Physics = None; Renderer = ... }`.
- **Rationale**: three properties fall out of one decision.
  1. **FR-006 and SC-004 hold for a kind and for a property, by two mechanisms.**
     The capture is a record-update, so it carries *every* field, including one added
     to the kind later — nothing is dropped silently, which is the failure a
     hand-written conversion into a separate "authored state" type would have (it
     would compile while dropping the new field, recreating the exact bug this
     feature removes). The restore is the mirror: `occupy*` constructs the record
     field by field rather than updating the payload, so a field added to a kind
     breaks the build until its author classifies it as authored (copied from the
     payload), derived (rebuilt), or runtime (left blank). Together they mean a new
     field is neither lost silently nor added without a decision.
  2. **FR-007 and FR-008 hold by construction.** A payload holds no released
     renderer and no stale Jolt body id, because the capture blanks them; and the
     runtime flag cannot be restored because it is blank when recorded.
  3. **FR-003 needs no other change.** The collider overlay is an ordinary field of
     `Actor`, so the capture carries it with no edit to `editor/Inspector.fs` — the
     overlay keeps writing the slot directly (it is not an undo step) and the delete
     payload simply returns its value on undo.
- **Blanked, not absent**: the restore never reads the derived fields — it rebuilds
  them from the record's asset id and submesh range. Blanking is therefore hygiene
  (no retained GPU object, no stale body id) rather than a correctness dependency,
  which is why it is safe to express as a record-update.
- **Alternatives considered**: a separate authored-state type per kind
  (`ActorState`, `TriggerState`, …) holding only the authored fields (rejected —
  the conversion is hand-written and silently drops a newly added field, defeating
  FR-006, and it duplicates fields that already exist); nesting the authored fields
  in a sub-record so the payload *is* that sub-record (`Actor = { State : ActorState;
  Physics; Renderer }`) (rejected — it makes derived state unrepresentable, which is
  the same guarantee, at the cost of a mechanical rewrite of every read of
  `a.Base`/`a.AssetId`/`t.Radius`/`p.Base` across `Zone`, render, editor, and six test
  modules, for no gain over blanking); recording the live record unblanked (rejected —
  the undo stack would retain released renderers, and a stale body id in a payload is
  a footgun nothing needs).

## Decision 3 — `Spawn` and `Duplicate` merge into one create per kind

- **Decision**: each kind has one `*Create`, used both by "spawn a new entity" and
  by "duplicate an existing one". `Spawn`, `Duplicate`, and the trigger's own
  `TriggerDuplicate` — a third case whose apply and undo were the same again — are
  all gone.
- **Rationale**: their `apply` and `undo` were already byte-identical — all three restore
  through the same path and undo by removing the entity — so the distinction
  was never behavioural, only a note about where the command came from, which lives
  in the caller that built it (`Spawning.spawn*` versus `Spawning.duplicate`).
  Merging removes those cases and the arms that mirrored them. Nothing observable
  changes; a duplicate is simply a create whose payload came from another entity —
  which is precisely how FR-013, "a copy carries every property of the original",
  becomes one line of code.
- **The one care point this creates**: `Zone.spawn*` builds a live entity from a base
  entity plus that kind's defaults, so a duplicate must write the captured record's
  kind-specific fields into the copy afterwards (`setShowCollider`, `setWaypointNext`,
  `setTrigger`) or the live copy would not match the command that reverses it. A plain spawn has no such gap, because `Spawning` captures *after*
  spawning, so its payload is read from the entity it describes.
- **Alternatives considered**: keeping both names (rejected — one operation under
  two names, and 5 more arms to keep in step); reintroducing provenance as a field
  (rejected — no consumer wants it, and a caller-side note is where it already
  lives).

## Decision 4 — The link lives on the waypoint, and the zone's table is deleted

- **Decision**: `Waypoint` becomes `{ Base : Entity; Next : EntityRef option }` and
  `Zone.Links` is removed. `Zone.waypointNext` keeps its signature and now reads the
  record; `setWaypointNext` keeps its signature and now writes it. Capture and
  restore are ordinary per-kind payload work.
- **Rationale**: FR-014. It also makes two other requirements structural rather than
  remembered. SC-008 (no residual link state) — a deleted waypoint takes its link
  with it, so nothing can be orphaned. FR-013 (a copy carries the link) — the create
  payload *is* the record, so copying one copies its link with no extra code, which
  is also why `028`'s "a duplicate starts its own chain" carve-out is no longer
  needed. And the delete payload already carries the link, so FR-005's mid-chain
  restore is the same mechanism as every other kind's.
- **Purity and staleness are preserved from `028`**: resolution still requires the
  *source* to be a live waypoint and the *target* to resolve, so a link whose target
  is gone answers "no next" and is left exactly as it is; a read never mutates the
  zone. The reference project instead clears a stale link in place, from its render
  path, which `028` rejected and this decision keeps rejected.
- **Alternatives considered**: keeping the zone table with the link mirrored on the
  record (rejected — two sources of truth for one value); clearing a link when its
  target is deleted (rejected — the clear would have to be captured by the delete
  for undo to round-trip, and a stale reference is already inert, so the write buys
  nothing a read does not); clearing on read, as the reference does (rejected in
  `028`, on the grounds that drawing the viewport must not rewrite zone data).

## Decision 5 — What "the kind's own family" covers

- **Decision**: create and delete exist for every kind. A property-edit command
  exists only for the kinds that have an undoable kind-specific property: `TriggerSet`
  (unchanged shape, already the trigger's) and `Link` (the waypoint's link edit,
  unchanged shape). The extend gesture keeps its own case, `LinkSpawn`, because it
  changes two entities — it creates a waypoint and sets its predecessor's link — and
  one gesture must be one undo step. `Transform` and `Rename` stay shared, and
  `SetEnvironment` stays zone-level.
- **Rationale**: FR-015. A kind-specific property edit belongs to that kind's
  family; an edit of the shared base belongs to no single kind, so sharing it is
  correct rather than a compromise. Keeping `Link` focused (the next before and
  after) rather than generalising it to a whole-record `WaypointSet` means the
  payload holds exactly the field the edit touches, matching `Transform` and
  `Rename`; `TriggerSet` carries the whole record only because its inspector edits
  four fields together and always has.
- **The actor family gets no `Set`**, and that is the point rather than an omission:
  an actor's only kind-specific property is the collider overlay, which FR-003 says
  is not an undo step, so there is no edit for an `ActorSet` to carry. A command with
  no caller is exactly what the Minimalism principle rules out.
- **Alternatives considered**: a whole-record `WaypointSet` for uniformity with
  `TriggerSet` (rejected — it would record the waypoint's base, which the link edit
  does not touch); splitting `LinkSpawn` into a create plus a link (rejected — two
  undo steps for one gesture, the same reason `028` gave); a general "group of
  commands as one step" mechanism to express the extend gesture (rejected — machinery
  for one gesture).

## Decision 6 — The `Zone` surface that results

- **Decision**: per kind, three functions — `capture*` (read the record as a command
  records it), `occupy*` (restore a record at a recorded ref, rebuilding resources),
  and `remove*` (free the slot, releasing resources) — alongside the existing
  `spawn*`, which stays the "add a new entity and return its ref" path used by
  `Spawning`. `setActorWithAsset` and `removeEntity` are deleted; `occupyTrigger`
  keeps its name and gains `occupyActor`/`occupyProp`/`occupyPart`/`occupyWaypoint`
  as siblings sharing its shape. `tryTriggerAt` stays, and a `capture*` by the same
  shape is added where a reader is needed. The
  generic readers (`isAlive`, `tryEntityAt`, `setTransform`, `setName`) are
  unchanged.
- **Rationale**: `capture*` is where the blanking of Decision 2 lives, in one place
  per kind, next to the storage it reads — so an unblanked record cannot reach a
  command. `occupy*` is the per-kind rebuild that `setActorWithAsset` was faking with
  a kind switch and a `| _ -> ()`. `remove*` is the per-kind teardown `removeEntity`
  was doing the same way. Every `EntityKind` match on a create, delete, or restore
  path becomes exhaustive, so the `| _ -> ()` arms go: this is the mechanism behind
  FR-006 and FR-007's "a kind cannot be added without its round-trip".
- **Retired when their last caller goes**: `bindingAt` and `bindingNone` existed to
  hand a command the asset half of the shared payload; with each kind's record
  captured whole, nothing needs to reconstruct it. Their callers in `Selection` and
  `Spawning` disappear with the shared cases, and the tests that exercised them move
  onto the per-kind paths. Whether they are removed outright or keep a caller is
  settled during implementation rather than assumed here.
- **What does not change**: `Actor`/`Prop`/`Trigger` keep their present fields, so
  every read in `Viewport`, `Inspector`, `Systems`, and `GameplayScreen` is
  untouched — in particular the collider overlay stays a direct slot write and the
  connector still calls `Zone.waypointNext`.
- **Alternatives considered**: one kind-dispatching `occupy`/`remove` taking a
  payload union (rejected — a payload union is the shared record under another name,
  and the owner's decision was one family per kind); keeping `removeEntity` as a
  dispatcher for its non-command callers (rejected — it has none: the environment
  teardown and `Zone.release` walk their slot maps directly).

## Decision 7 — FR-006 is enforced by removing the catch-alls

- **Decision**: the `| _ -> ()` arms on the create, delete, and restore paths are
  removed, and the `EntityKind` matches that remain on those paths are written out
  over all six kinds (`Actor`, `Environment`, `Prop`, `Part`, `Waypoint`, `Trigger`);
  and `occupy*` constructs its record field by field, so a field added to a kind is a
  compile error until it is classified rather than a silent pass-through.
- **The kind half is a compiler-reported case, not a build failure.** F# reports a
  non-exhaustive match as a *warning* (FS0025), and the project deliberately does not
  promote that warning to an error: removing a catch-all makes the compiler name the
  unhandled case, which is the loud signal FR-006 asks for, without a project-wide flag
  that would turn every future non-exhaustive match into a hard failure. The field half
  needs no such flag — record construction is checked exactly (FS0764), which is why
  adding `Next` to `Waypoint` failed the build at `occupyWaypoint` until it was
  classified.
- **Rationale**: F# reports a non-exhaustive match only when there is no wildcard, so
  a wildcard is the difference between "the compiler names the unhandled kind"
  (FR-006) and "adding a kind silently does nothing". F# also checks record
  construction exactly, which is what makes the restore half of the same guarantee
  work: the field case is a hard error, the kind case a compiler-reported one. This is
  the smallest mechanical change that
  makes the requirement true rather than aspirational, and it is why the spec states
  FR-006 as a compiler outcome.
- **Alternatives considered**: a runtime check that raises for an unhandled kind
  (rejected — the compiler already knows, and Fail Loudly prefers the earlier
  failure); leaving the wildcards and relying on review (rejected — that is how the
  three losses arrived).

## No unresolved clarifications

Every choice is settled by the spec — whose clarifications fix the collider
overlay's undo behaviour, the family shape and the removal of the shared record,
where a waypoint's link lives, what a copy carries, and which family a kind-specific
property edit belongs to — by the owner's stated design
decisions (one storage per kind, one family per kind, separate handling rather than
a discriminator), or by the code's existing shape. No `NEEDS CLARIFICATION` remains
for Phase 1.
