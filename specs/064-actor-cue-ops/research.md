# Research: Actor Cue Ops

## D1 — The ops name closed identity, and a waypoint by name

**Decision**: `Cue` gains two leaves:

```fsharp
| SetActor of place: Place * character: Roster.Character * look: Casting.Look * waypoint: string
| RemoveActor of character: Roster.Character
```

The set op names the actor by a `Roster.Character` (the closed roster), the location by a `Place` (the closed
place union) and the art on a first placement by a `Casting.Look`; only the waypoint is a string, and it is a
name in the place's zone, resolved by `Zone.tryWaypointByName` exactly as `SetCamera`/`FlyCamera` resolve
theirs. The **remove** op names the character alone: the index resolves her place and she is unique across all
zones, so a place would be redundant (D4).

**Rationale**: recorded in `specs/061`'s Assumptions — *"actor cue ops name a `(place, character)` pair and
resolve through that place's present zone"* — and in `specs/063`, which built the index that resolution needs.
Every odin placement op (`AddActor`, `WarpActor`) positions an actor at a **named waypoint** and faces her along
it, and the user chose that shape. `Casting.Look` is a closed union, so a cue cannot name a look the game does
not declare; it is compiled before `Cue` (`world/Casting.fs` precedes `world/Cue.fs`), so the op may name it.

**Alternatives considered**: a raw `Vector3` position + `Quaternion`/yaw (rejected — the user chose odin's
waypoint shape, and F# already carries waypoint names in the camera ops); a free-string actor name (rejected —
that is what `specs/063` replaced with the cast index); a separate spawn and warp op (rejected by the user:
*"combine spawnActor and warpActor into a single cue ⇒ set actor"*).

## D2 — Resolution is the cast index, not a scan

**Decision**: the set op looks the character up with `GameplayState.tryCastActor`; the remove op likewise. The
index answers `(Place, EntityRef) option`, so the op knows both whether the member exists and where.

**Rationale**: `specs/063` built the index precisely so actor ops do not scan zones; a character not in the
scene has no actor to find. The index is global, which is what lets a set see a member standing in **another**
place (D3).

**Alternatives considered**: scanning every present zone for the character's actor (rejected in `specs/063` D3 —
the index is maintained, not searched).

## D3 — A set is an upsert-and-place, in three cases

**Decision**: `SetActor (place, character, look, waypoint)`:

1. **Member nowhere** (`tryCastActor = None`): resolve the asset with
   `Casting.tryAsset { Character = character; Look = look } shared.Cast`; `None` ⇒ the op performs nothing.
   Otherwise build a base entity named `Roster.characterName character` at the waypoint's pose, spawn it into
   the place with `Cast = Some character`, and add the index entry.
2. **Member in the named place**: set the existing actor's transform to the waypoint's pose with
   `Zone.setTransform` — same actor, same art, index unchanged.
3. **Member in another place** (D2): move her — capture her authored `ActorState` from the old zone, remove her
   from it, spawn her into the named place at the waypoint with the **captured `AssetId`** (art kept; the op's
   `look` is not consulted), then move the index entry.

In all three the waypoint is resolved first; a place that is not present, or a waypoint the place's zone does
not hold, performs nothing (D7).

**Rationale**: this is the unification the user asked for (spawn and warp are one op), and it satisfies the two
clarifications: a set names a look but only uses it on a **first** placement (case 1), and a set on a member
already elsewhere **moves** her (case 3). It is also what keeps the one-character-across-all-zones invariant
(`specs/063` FR-003) true without a second actor.

**Alternatives considered**: a no-op with a diagnostic for case 3 (the editor's cross-zone rule, `specs/063`
FR-007) — rejected by the user, who wanted a cue to relocate a member in one op; re-resolving the asset from the
op's `look` on a move (rejected — it would re-skin her and could fail for an unbound look, against "keep her
art").

## D4 — A remove names the character alone, and drops her wherever she is

**Decision**: `RemoveActor character` resolves the index; when it answers `Some (place, ref)`, remove that
actor with `Zone.removeActor` (destroy body, release renderer) and drop the index entry. When the character is
not in the scene, it performs nothing. No place is named.

**Rationale**: the spec's FR-003/FR-005. The index is the authority on where the member is and a character is
unique across all zones, so the place is already known — naming one would only repeat it, and would make a
remove silently no-op when the member happens to be elsewhere (a foot-gun, not a guard). odin's `RemoveActor`
also names the actor alone.

**Alternatives considered**: keeping `(place, character)` and no-opping when she is elsewhere — rejected as
redundant and surprising; a "remove only if she is in place X" conditional — not a real case given uniqueness,
and it belongs behind an `If` predicate if ever wanted.

## D5 — A body built into an inactive place is detached

**Decision**: when the target place is **not** the active one, a body the op creates — a fresh spawn, a move's
rebuild, or a cross-zone spawn — is detached immediately with `Physics.removeBody`, guarded by
`Physics.isAdded`. `Zone.deactivatePhysics` is **not** used, because it removes every body in the zone
unconditionally and is not safe to re-run on an already-inactive zone.

**Rationale**: `Physics.createCapsuleBody` creates a body already joined to the simulation, and only the active
zone's bodies may simulate. `populateZone` deactivates a freshly built zone after building it, but it is the
zone's first build; an actor op touches a zone that already exists and may already be detached, so it detaches
only the body it just made.

**Alternatives considered**: temporarily making the place active to build (the `History` zone-delete-undo
trick) — rejected, it would disturb the active place, camera and selection mid-cue; deactivating the whole zone
after building — rejected, `Physics.removeBody` is unconditional and would wrongly touch already-detached bodies.

## D6 — A shared new-entity constructor (`Entity.baseEntity`)

**Decision**: add `Entity.baseEntity (name: string) (transform: Transform) : Entity` to `world/Entity.fs`, and
have `Spawning.baseEntity` and `Outliner.newEntity` delegate to it; `CueEngine` uses it to build the actor it
spawns.

**Rationale**: the new-entity literal (`Name`, `Transform`, and the four editor flags) currently exists twice
(`editor/Spawning.fs`'s `baseEntity`, `editor/Outliner.fs`'s `newEntity`); the actor op is the **third** call
site, which is exactly the rule of three (`AGENTS.md`). `Entity` is compiled before all three, so the move is
downward and cycle-free.

**Alternatives considered**: a third inline copy in `CueEngine` (rejected — a drift risk the rule of three
exists to prevent).

## D7 — The no-op rule, and its divergence from the camera ops

**Decision**: an actor op performs nothing (and the cue continues) when the place is not present, the character
has no art for a first placement, or the named waypoint does not resolve. It never raises on redundant or
contradictory state.

**Rationale**: the zone ops established this rule (`specs/062` FR-007/FR-008); odin's actor ops likewise no-op
(`if !found do return nil`). A cue is runtime data that may be re-run, and a waypoint removed after authoring
must not fault a re-run.

**Recorded divergence**: the camera ops (`SetCamera`/`FlyCamera`) **raise** on a waypoint name they cannot
resolve (`CueEngine.namedWaypoint`/`resolvedPath`). This feature deliberately does not copy that; the spec's edge
case was corrected to say so. Making the camera ops no-op is out of scope here.

## D8 — The player designation is left as an editor delete leaves it

**Decision**: the ops add **no** clear step for a zone's `Player` designation. A move within the same place
leaves it intact (the handle is unchanged); a remove or a cross-zone move leaves the old zone's designation
pointing at a handle that is no longer live, which the systems already treat as "no player"
(`Systems.isPlayerLive`).

**Rationale**: `specs/016`/`045` record this as deliberate — *"Bound actor deleted ⇒ binding unchanged;
`Actors.IsAlive` fails ⇒ systems inert (no clear step)"*. Adding a clear step would be a new behaviour the editor
delete does not have, and would be a second, weaker source of truth for "who is the player". The spec's FR-011
was corrected to state this.

## D9 — The editor offers closed sets and the named place's waypoints

**Decision**: `CueTree` gains the new kinds, labels, `defaultOfKind` values, and write helpers — `withPlace`
extended to both ops, a new `withCast (character, look)` for the set op, and `textOf`/`withText` extended to the
set op's waypoint. `CueEditor.drawNodeText` offers the set op's waypoint from the **named place's** live
waypoints (resolved from the gameplay state), falling back to a typed field when that place is not present.

**Rationale**: spec FR-008; the place, character and look are closed unions, so their choosers need no zone or
registry read (`specs/061`/`062`'s rule for the place chooser). The waypoint chooser must read the **named
place's** zone, not the active one — the difference from the camera ops, whose waypoints are always the active
zone's.

**Alternatives considered**: typing the waypoint name (rejected — FR-008 asks for a chooser, and the camera ops
already offer one); offering only the active zone's waypoints (rejected — the op may name another place).

## D10 — Verification is headless, through the real paths

**Decision**: assert the ops' effects and no-ops in `test/CueTests.fs` by driving `CueEngine.tick` against a
`GameplayState` whose asset manager holds a fabricated `ActorAsset` (defaulted GPU handles) — the approach
`specs/063`'s `test/CastIndexTests.fs` proved works with no device. Assert: a spawn places a member and indexes
her; a re-set moves the same actor (no second); a cross-zone set moves her and the index follows; a remove drops
her; each op no-ops on an absent place / unbound look / unknown waypoint. `test/CuePanelTests.fs` asserts the new
labels, kinds and write helpers purely; `test/CastIndexTests.fs` (or `CueTests`) asserts the index stays exact.
