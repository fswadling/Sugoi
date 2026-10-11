# Research: Actors Addressed by Name

## D1 — An actor's identity is its `Name`; the cast role is removed

**Decision**: `ActorState.Cast : Roster.Character option` (`world/Entity.fs`) is deleted. An actor's
addressability is its `Base.Name` alone, exactly as a waypoint's or a trigger's is.

**Rationale**: the role was a **second identity** that existed only to key the index (D2); every other
addressable entity has one identity and is found by a scan of its name. Keeping both meant two identities
that had to agree by convention. `specs/063` introduced the role for the index, so withdrawing the index
withdraws its reason to exist.

**Alternatives considered**: keeping the role and scanning zones by it (the "option B" middle path) — it
still leaves a second identity beside the name, which is the confusion this feature removes.

## D2 — The runtime cast index and its maintenance sites are removed

**Decision**: delete `GameplayState.Cast : Dictionary<Roster.Character, Place * EntityRef>` and its four
accessors (`tryCastActor`, `addCast`, `removeCast`, `removeCastZone`), and the sites that kept it exact:
`Spawning.spawnActor` (the add), `History.apply`/`undo` for `ActorCreate`/`ActorDelete` (the matching
change), `GameplayState.deleteZone` (`removeCastZone`), and the zone-delete-undo restore loop in
`History.undo`.

**Rationale**: the index was a lookup **maintained by hand** wherever an actor came into or left existence —
five or six places that had to agree, where the rest of the codebase has none. `specs/063` noted its own gap
(a direct slot mutation bypasses it). With resolution by scan (D3), the index has no consumer.

**Alternatives considered**: keeping the index but deriving it lazily — rejected; a scan is already cheap at
this scale and a derived index is the same structure with extra invalidation.

## D3 — Resolution is a name scan, in `Place.all` order

**Decision**: add `Zone.tryActorByName (zone) (name) : EntityRef option` beside `tryWaypointByName` /
`tryTriggerByName` — an index-ordered scan of `zone.Actors.Slots` for the first live slot whose
`State.Base.Name = name`. Add a `GameplayState` resolver that walks the **present** places in `Place.all`
order and returns the first `(Place, Zone, EntityRef)` whose `tryActorByName` hits.

**Rationale**: it is the established pattern for a named entity (`specs/064` D1 named only the waypoint by
string; this extends that to the actor). `Place.all` gives a **deterministic** order (declaration order),
where iterating the zones `Dictionary` would not; the constitution's determinism principle is narrative-
scoped, but a stable order costs nothing and the spec's "first match in slot order, then place order" (FR-008)
requires it. The `Place` union and the actor count are both small, so the scan is cheap and allocates nothing.

**Alternatives considered**: iterating `state.Zones` directly (rejected — `Dictionary` order is not
guaranteed); scanning only the active place (rejected — a member may be in an inactive place, and the ops
name no place).

## D4 — `AddActor` is a pure spawn (no upsert)

**Decision**: `SetActor` is renamed `AddActor` and **never looks for an existing actor**. It resolves the
place (absent ⇒ no-op), the waypoint (unresolved ⇒ no-op) and the art (`Casting.tryAsset { Character; Look }`
⇒ `None` ⇒ no-op), then spawns an actor named `Roster.characterName character` into the place at the
waypoint's pose. Two runs place two actors of the same name.

**Rationale**: the user withdrew the uniqueness constraint (spec FR-008), so an op that "reconciles" with an
existing actor is exactly the machinery being removed. This reverses `specs/064` D3's three-case upsert;
moving a member becomes a `RemoveActor` then `AddActor` (spec US3 scenario 3, FR-006).

**Alternatives considered**: keeping the upsert (064's spawn-or-move) — rejected by the user: it exists to
uphold the uniqueness invariant this feature removes; a separate `WarpActor` (odin's pair) — deferred; this
feature adds no op (FR-006, Assumptions).

## D5 — Removal and the acting ops resolve by name across present places

**Decision**: `RemoveActor`, and the ops that act on an existing actor (`MoveActor`, `RotateActor`,
`Animate`/`CrossFade`/`Morph`/`CrossMorph`, `EnablePlayer`/`DisablePlayer`), resolve through the D3 resolver.
Their argument shapes are unchanged — none gains a place.

**Rationale**: previously they resolved through the global index (`CueEngine.tryResolveActor` and
`tryCastActor`). The D3 scan is global in the same sense (it walks every present place) and needs no index, so
the ops keep their arity (spec FR-007). `tryResolveActor` already returns `(Place * Zone * EntityRef) option`,
which the scan can supply directly.

**Alternatives considered**: giving these ops a `Place` argument — rejected; it would be a vocabulary change
the spec excludes (FR-007), and the scan already answers "wherever she is".

## D6 — A spawn into an inactive place is still detached

**Decision**: keep `specs/064` D5. A body `Zone.spawnActor` builds joins the simulation; when the target place
is not the active one, the new body is detached with `Physics.removeBody`, guarded by `Physics.isAdded`.

**Rationale**: unchanged from `specs/064`; an `AddActor` may name an inactive place (a cue makes a place
present and populates it before switching), and only the active place's bodies may simulate.

## D7 — The no-op rule is unchanged

**Decision**: an actor op performs nothing — and the cue continues — when the place is not present, the named
look resolves no art, the named waypoint does not resolve, or (for an acting op) no actor of the name exists.
The `AddActor` diagnostic is re-worded `noOp "AddActor"`.

**Rationale**: `specs/064` D7 and the zone ops' rule (`specs/062` FR-007). A cue is runtime data that may be
re-run; a re-run must not fault on state an earlier run produced.

## D8 — The portrait and binding paths are untouched

**Decision**: `world/Casting.fs`, `world/Dialogue.fs` (`Kind.PortraitDialogue of Roster.Character * Emotion`)
and `world/DialogueRender.fs` (`tryPortrait`, `speaker`, `missReason`) are **not modified**.

**Rationale**: portraits resolve `(character, look) → asset → asset.Portraits` through the **binding**, which
never consulted `ActorState.Cast` or the index. Keeping the roster and the binding keeps the whole chain
identical (spec FR-005, US2, SC-003). This is the reason the change is contained.

## D9 — The editor's clip/morph chooser reads the binding alone

**Decision**: `CueEditor.assetNames` drops its `runtimeCast` parameter and its placed-actor branch; it reads
the animation-clip and morph names from the character's asset via the **binding** only.

**Rationale**: the placed-actor branch existed solely to read the runtime index (spec Clarifications,
2026-10-09). The binding fallback already exists and works whether or not the character is placed; an unbound
character offers no suggestions and the field falls back to typing (spec FR-013).

**Alternatives considered**: a name scan for a placed actor, then the binding (preserves today's "reflect the
placed actor" nuance) — rejected by the user in favour of the binding alone.

## D10 — The role/index readouts are removed, not re-pointed

**Decision**: delete `Outliner.castTags` (and its use on the zone rows), `ScenarioPanel.actorTags`, and the
Inspector's actor "Character" readout.

**Rationale**: each read the removed role or index; re-pointing them at "the actors here" would invent a new
affordance rather than preserve one (spec FR-011, Assumptions; Constitution I).

## D11 — The op is renamed `SetActor` → `AddActor` throughout

**Decision**: rename the cue case, the `CueEngine.setActor` helper, the panel label/kind (`"Set actor"` →
`"Add actor"`), `kindName`, `defaultOfKind`, and the comments — across `world/Cue.fs`, `world/CueEngine.fs`,
`editor/CueTree.fs`, `editor/CueEditor.fs` and the docs.

**Rationale**: spec FR-006; the reference lineage's name for a placement op. No other op is added or removed.

## D12 — Verification is headless, through the real paths

**Decision**: retire `test/CastIndexTests.fs` (its subject, the index, no longer exists) and replace it with a
name-resolution suite: `Zone.tryActorByName` finds a named actor and misses an unknown name; the gameplay
resolver finds an actor across places in `Place.all` order and takes the first of a duplicate. Retarget
`test/CueTests.fs` through `CueEngine.tick` at a `GameplayState` with a fabricated `ActorAsset`: an `AddActor`
places a member and a second `AddActor` places a second (no index to update); a removal and each acting op
find the member by name; each op no-ops on an absent place / unbound look / unknown waypoint / absent member.
`test/CuePanelTests.fs` asserts the renamed labels, kinds and write helpers purely; `test/ScenarioTests.fs`
and `test/GameplayStateTests.fs` drop their cast-index assertions; `test/SpawningTests.fs` drops the cast
argument. `test/RosterTests.fs`, `test/CastingTests.fs` and `test/DialogueTests.fs` are unchanged.

**Rationale**: `specs/063`'s fabricated-`ActorAsset` fixture already runs the real spawn/remove paths with no
device; the same fixture proves the scan-based resolution. The dialogue suite passing unmodified is the
regression proof for D8 (SC-003).
