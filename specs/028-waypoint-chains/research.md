# Research: Waypoint Chains

Decisions derived from the feature spec, the reference project's waypoint paths
(`ocaml-game/lib/core/{entity,zone}.ml`, `lib/editor/{outliner,command,viewport}.ml`,
`specs/014-waypoints-triggers-cast`), and this project's own entity/command/undo
model as `specs/026-waypoints` left it.

## Decision 1 — The link is zone data: a reference-keyed map, not a field on the waypoint

- **Decision**: `Zone` gains `Links : Dictionary<EntityRef, EntityRef>`, keyed by
  the *source* waypoint's ref with the *target* waypoint's ref as the value. At
  most one entry per source, so "a waypoint has at most one next" is structural.
  The `Waypoint` record is unchanged (`{ Base : Entity }`).
- **Rationale**: our `Waypoint` composes the shared `Entity` base and travels
  through the *shared* `Spawn`/`Delete`/`Duplicate` commands, whose payload is an
  `Entity` plus an `AssetBinding`, and whose restore path rebuilds the payload
  (`world/Zone.fs` `setActorWithAsset`: `Occupy(handleOf ref, { Base = entity })`).
  A link held on the record therefore has nowhere to be carried across a delete
  and undo: `Selection.deleteRef` captures `Zone.tryEntityAt`, which returns the
  base entity only, so the restore can only produce a link-less waypoint. Putting
  the link on the record means also changing the command model (a waypoint-specific
  delete carrying the whole record, or a link-bearing payload), which is exactly
  the unification `specs/026-waypoints` Decision 1 traded for.
- **What the map buys instead**: a link is a *reference*, and references in this
  project are generation-checked, so a link that outlives its waypoint is inert
  and a link whose target's slot was reused does not resolve. Deleting a mid-chain
  waypoint and undoing it therefore restores the whole chain with **no command
  change at all**: the entry was never destroyed, and the restored waypoint
  re-occupies its slot with the same generation. The reference cannot do this —
  it clears a stale link destructively, and that clearing is not recorded, so its
  undo of a mid-chain delete leaves the chain broken.
- **Precedent in this codebase**: `Zone.BodyToEntity` is already a ref-keyed,
  zone-level relationship map that tolerates staleness, so relationships living on
  the zone rather than inside an entity payload is the established shape here.
- **Alternatives considered**: a field on the waypoint
  (`Next : EntityRef option`, the reference's shape) — rejected for the command
  coupling above; the same field *plus* a waypoint-specific delete command that
  carries the whole record (what the reference does) — technically workable and
  fully faithful, but it splits the command model and adds a second delete path to
  buy only what the map gets for free; a `Handle option` instead of an `EntityRef`
  — rejected, since `EntityRef` already carries the kind and generation the
  resolution rule needs, and the map's keys and values are then the same type the
  rest of the editor uses; a `Dictionary<EntityRef, Waypoint>` copy — rejected,
  the target's *live* state must be read, not snapshotted.

## Decision 2 — Resolution is pure, and a stale link is left in place

- **Decision**: `Zone.waypointNext zone ref` answers `Some target` only when the
  source is a live waypoint, the map has an entry for it, and the target resolves
  as a live waypoint; otherwise `None`. It **does not** clear the entry.
- **Rationale**: the reference's `waypoint_next` clears a stale link in place,
  which makes a query mutate the zone — and it is called from its *render* path, so
  drawing the viewport rewrote zone data outside the undo system. That is the
  mechanism behind its broken mid-chain undo. Here, reads are pure (`tryEntityAt`,
  `isAlive` set the precedent), and an unresolvable entry is simply inert: it can
  never resolve to the wrong waypoint (the key is generation-checked) and it
  becomes live again the moment its waypoint is restored by undo.
- **Note on the source check**: requiring the *source* to be alive keeps the read
  honest — asking a deleted waypoint for its next answers "none", not "the next it
  had when it existed".
- **Alternatives considered**: clearing on read, as the reference does (rejected —
  a mutating query, and it destroys exactly the state undo needs); clearing on
  delete (rejected — pruning would have to be captured by the delete command, i.e.
  it reintroduces the very coupling Decision 1 avoids); a separate "prune pass"
  (rejected — machinery for a state that is already inert).

## Decision 3 — One write covers linking and unlinking

- **Decision**: `Zone.setWaypointNext zone ref (next: EntityRef option)` writes
  (`Some target`) or clears (`None`) the entry. It refuses a self-link
  (`invalidOp`), and refuses a target that is not a waypoint. It does **not**
  require the target to be alive.
- **Rationale**: linking and unlinking are the same write with a different value,
  and both the UI actions and the `Link` command's apply/undo go through it, so
  the invariant (never self-linked) has exactly one home. A target-alive
  precondition is deliberately *not* imposed: the command's apply/undo may
  legitimately restore a recorded link while the target is momentarily dead (the
  undo stack is LIFO, so a link is undone after the delete that hid it, but the
  ordering is a property of the stack rather than of this function), and such a
  link is inert rather than wrong. The editor gates the actions, so a self-link or
  a non-waypoint target reaching here is a bug, and fails loudly.
- **Alternatives considered**: separate `linkWaypoints`/`unlinkWaypoint` functions
  (rejected — one worker with two wrappers for a boolean difference); requiring
  the target to be alive (rejected above); treating a non-waypoint source as "no
  link" rather than raising (rejected — a read may miss, but a *write* to the
  wrong kind would silently create a bogus entry).

## Decision 4 — Orphan entries are retained, and that is the point

- **Decision**: a link entry whose source or target waypoint has been deleted is
  left in the map. Nothing prunes it.
- **Rationale**: retention is what makes undo restore a chain, and the entry is
  provably inert while its waypoint is gone: resolution requires the key to match
  exactly (generation included) and the target to be alive, so an orphan can
  neither resolve nor be mistaken for a link from a waypoint that reused the slot.
  The cost is bounded by a zone's waypoint capacity in lifetime terms and by
  editor activity in count: a few dead entries after a long delete-heavy session,
  each a struct pair, none reachable.
- **Alternatives considered**: pruning on delete (rejected — see Decision 2);
  pruning on zone load or on a waypoint-capacity sweep (rejected — machinery for
  nothing observable).

## Decision 5 — Two commands: `Link` for link edits, `LinkSpawn` for the extend gesture

- **Decision**: add
  `Link of Ref : EntityRef * Before : EntityRef option * After : EntityRef option`
  (027's `Rename` shape) for linking and unlinking, and
  `LinkSpawn of From : EntityRef * Before : EntityRef option * Ref : EntityRef * Entity : Entity`
  for "add next at camera", whose apply creates the waypoint and sets the link and
  whose undo clears the link and removes the waypoint.
- **Rationale**: a link change is a before/after edit of one value, so it maps onto
  the existing command shape exactly, and it cannot merge with anything (only
  consecutive `Transform`s merge in `UndoStack.push`). The extend gesture changes
  *two* things — it creates a waypoint and it links it — and one gesture must be
  one undo step, so it needs a composite case; the reference reaches the same
  conclusion with its `Waypoint_spawn_next`, which likewise carries the
  predecessor's state before the link.
- **Alternatives considered**: pushing `Spawn` followed by `Link` (rejected — two
  undo steps for one gesture); making `Link` optionally carry a spawn (rejected —
  one case doing two jobs, and the payload becomes conditional); having the undo
  stack treat adjacent command pairs as one step (rejected — a general merge
  mechanism for one gesture, and it would obscure the step boundaries everywhere).

## Decision 6 — The extend gesture is a `Spawning` entry point

- **Decision**: `Spawning.spawnLinkedWaypoint (state) (from: EntityRef) (entity: Entity)`
  reads the predecessor's current next (the `Before`), spawns the waypoint,
  links it, and commits the `LinkSpawn` command (which selects the new waypoint).
- **Rationale**: `Spawning` is this project's single entity-creation surface and
  the module that pairs every spawn with its reversing command immediately — the
  invariant that keeps a recorded ref from naming a slot an untracked `Add` later
  reuses. The extend gesture creates an entity, so it belongs there, and it needs
  no `SharedState` (waypoints have no body or asset), exactly like `spawnWaypoint`.
- **Alternatives considered**: doing it inline in the Outliner (rejected — it would
  bypass the spawn/command pairing rule); a general "spawn N entities" helper
  (rejected — one gesture, not a mechanism).

## Decision 7 — Connectors port the reference's geometry, plus a guard it lacks

- **Decision**: for each live waypoint with a resolvable next, draw a line from the
  waypoint to the midpoint of the segment toward its next, lifted `0.08` in Y, and
  a two-wing arrow head at that midpoint (wings `0.07` either side of the
  direction's perpendicular, base `0.15` back), in a dedicated link colour
  (200, 122, 255). Coincident source and target draw nothing.
- **Rationale**: drawing only the first half of the segment plus an arrow head is
  how the reference makes chain *direction* readable, and the lift keeps the line
  off the ground plane between two waypoints at the same height. The coincidence
  guard is added because the reference normalises a possibly-zero delta: pressing
  its "add next at camera" twice without moving the camera creates two coincident
  waypoints, which yields a zero direction, and `NaN` vertices are a real
  rendering fault rather than a cosmetic one. Skipping the connector is honest —
  two waypoints in the same place have no direction to show, and their markers
  already overlap.
- **Alternatives considered**: an unbroken line plus a midpoint arrow (rejected —
  the halves are what distinguish a link from a two-way relation); a colour per
  chain (rejected — a chain has no identity in the model, and per-link colouring
  has no meaning); drawing connectors for invisible waypoints — moot, since
  waypoint markers do not consult visibility today either.

## Decision 8 — The actions live in the Outliner and are disabled, not hidden

- **Decision**: the Waypoints section gains *Add Next at Camera* and *Unlink*
  beside its existing add actions, each disabled when the selection cannot use it
  (not a waypoint; or, for *Unlink*, no next), and each waypoint row gains a
  `Link` button that appears after the shared row actions and is disabled unless a
  *different* waypoint is selected.
- **Rationale**: the section is where waypoints are added, listed, and already
  deleted/duplicated, so it is where they are ordered; nothing else in the editor
  knows how to address a waypoint (they remain un-clickable in the viewport, as
  `specs/026-waypoints` records). Disabling rather than hiding follows this
  project's own precedent — the zones section disables *Add Zone* and *Del* for
  invalid states — and keeps the buttons from moving under the pointer; the
  reference hides the extend button instead, which is the one place this port
  diverges on presentation.
- **Alternatives considered**: a `Next` field in the Inspector (rejected — it would
  need a target picker for a value whose only real picker is the list); a
  viewport click on a waypoint marker to link to it (rejected — it needs waypoint
  picking, which `specs/026-waypoints` deferred); a modal or "linking mode"
  (rejected — state to hold and to get wrong, for an action that is one click from
  a list).

## Decision 9 — No chain walk in production

- **Decision**: nothing in this feature follows a chain. The model exposes link
  resolution (`waypointNext`); the tests walk links themselves to assert chain
  order.
- **Rationale**: with no consumer, a `Zone.waypointChain` returning a list would be
  a function with no caller — just-in-case machinery, which the constitution's
  Minimalism principle rules out — and it would also have to take a position on
  loops (bounded walk vs. returning the cycle) that only a real consumer can
  settle. The spec's original "following a chain must terminate" requirement was
  therefore amended away: this feature owes a future walker the resolution rule,
  not a walker. That rule is what makes a loop detectable — every step either
  resolves to a waypoint that exists or ends the chain.
- **Alternatives considered**: a bounded `waypointChain` for the tests and the
  future consumer (rejected — no caller, and it would likely be replaced by the
  consumer's own traversal); forbidding cycles at link time (rejected — a loop is a
  legitimate patrol route, and forbidding it would need a traversal to detect it,
  i.e. the walk this decision declines to write).

## Decision 10 — Nothing else changes

- **Decision**: no change to `Waypoint`, `Zone.spawnWaypoint`, `Zone.removeEntity`,
  `Zone.setTransform`, `Zone.setName`, or the shared `Spawn`/`Delete`/`Duplicate`
  commands and their restore path.
- **Rationale**: this is the payoff of Decision 1 and worth stating as a decision,
  because it is what makes the feature small: a new waypoint starts a chain (no
  entry), a copy starts its own chain (no entry — matching the spec's assumption
  that a duplicate does not inherit the original's link), a field edit preserves
  the link (it is not in the payload), and delete/undo round-trips it (Decision 4).
  Every one of those would have needed a decision, and most of them code, if the
  link lived on the record.
- **Alternatives considered**: none — this is the consequence being recorded.

## No unresolved clarifications

Every choice is settled by the spec, the reference project, or this codebase's own
model. The link-storage decision (Decision 1) is the one the reference does not
settle for us, because its waypoint is not an `Entity` and carries its own command
family; the alternatives and the corrected reasoning are recorded above rather
than left implicit.
