# Research: An Explicit Zone on Every Entity Cue Op

## D1 — The zone is a `Place`, carried in the op

**Decision**: each of `RemoveActor`, `Animate`, `CrossFade`, `Morph`, `CrossMorph`, `EnablePlayer`,
`DisablePlayer`, `SetCamera`, `FlyCamera` gains a `place: Place` argument, positioned **first** (the shape
`AddActor` and the trigger toggles already use). `Place` is the closed union (`world/Place.fs`), so a zone
that is not declared is unrepresentable.

**Rationale**: the op already names its entity by a string; the zone is the other half of the address, and
making it a value of the same closed vocabulary keeps `world/Cue.fs` device-free and dependency-light
(`specs/061`). First position matches the existing place-carrying ops and reads as "where, then what".

**Alternatives considered**: a string zone name (rejected — the union exists precisely so a misspelled
zone is a compile error); a `(place, name)` pair type (rejected — more ceremony for one field, and no other
op uses it).

## D2 — Actor ops resolve in the named place; the global scan is deleted

**Decision**: `RemoveActor`, the four blends, `EnablePlayer` and `DisablePlayer` resolve their actor via the
op's place — `state.Zones.TryGetValue place`, then `Zone.tryActorByName zone actor` — and perform nothing
when the place is absent or holds no such actor. `CueEngine.tryResolveActor` is re-based on a
`(state) (place) (actor)` helper; `GameplayState.tryActorByName` (the `Place.all`-order scan over the
present places) loses its last caller and is **removed**.

**Rationale**: the op now names the zone, so a scan of every present place would contradict the value it
carries, and "first match anywhere" is exactly the ambiguity the feature removes. With the place in hand the
lookup is O(1) plus the one zone's slot scan — cheaper than the scan it replaces.

**Alternatives considered**: scan the named place *and* every other present place (rejected — the op names a
zone; a name resolving elsewhere would be a surprise); keep the global scan as a fallback (rejected — two
answers to one question, and it hides a misplaced name instead of no-oping).

## D3 — `MoveActor`/`RotateActor` stay zone-implicit

**Decision**: `MoveActor` and `RotateActor` take **no** zone. They resolve their actor in the **active
zone** (`state.Zones.[state.ActiveZone]` + the slot scan) and keep their physics requirement. The
`place <> state.ActiveZone` guard in `beginMove`/`beginRotate` (and the matching check in `stepMove`/
`stepRotate`) is dropped: with the actor resolved in the active zone, it is always true.

**Rationale**: physics is attached per active zone, so these ops can only ever run in the active zone; a
zone argument would offer a choice that cannot take effect. Resolving directly in the active zone also
removes a latent oddity: the old global scan could find an actor of that name in a *non-active* zone and
no-op, even when a same-named actor stood in the active zone (spec Clarifications Q5).

**Alternatives considered**: give them a zone and no-op unless it is the active zone (the earlier answer,
superseded — a selector that only sometimes works is worse than none); build physics for non-active zones
(rejected — a simulation-model change, out of scope).

## D4 — Camera ops read the named place and keep their raise convention

**Decision**: `SetCamera (place, waypoint)` and `FlyCamera (place, path, speed)` resolve their waypoint in
`state.Zones.[place]`. They keep the existing convention (`specs/054` FR-009): an **unresolved name raises**
(`namedWaypoint`/`resolvedPath`). A place that is **not present** also raises (there is no zone to read),
which is the camera ops' counterpart of the actor ops' no-op rule.

**Rationale**: the camera is the screen's, not a zone's, so the op only *reads* a pose; the existing
"raise, do not silently do nothing" contract for camera ops is deliberate and unchanged. An explicit place
just replaces `activeZone` as the source of the pose.

**Alternatives considered**: make an absent place a no-op for camera ops too (rejected — inconsistent with
camera ops raising on an unknown waypoint in a present zone; a camera op that silently does nothing is the
failure mode `specs/054` set out to avoid). This is the one point where the spec's FR-003 ("a named place
that is not present … a no-op") is read as scoped to the actor ops; the camera ops keep raising.

## D5 — The in-flight `…State` cases are unchanged

**Decision**: no `…State` case changes shape. `BlendState` already carries `place` + `handle`;
`MoveActorState`/`RotateActorState` already carry `place` + `handle` (the active zone captured at `begin*`);
`FlyCameraState` captures its resolved points and needs no place. Only the **authored** ops gain the place.

**Rationale**: the `…State` cases are the run form, resolved once at `begin*` so a running op never re-scans
(`specs/074`). The authored form gains the place; the run form already had it where it needs it.

## D6 — The selector is the existing place combo, over the closed place set

**Decision**: the per-op zone selector is the **same control** `CueTree.drawEditor` already draws for the
zone ops, `AddActor` and the trigger toggles — a `filteredCombo` over `Place.all`, writing `withPlace`.
Extend its `match` arms to the nine ops. It offers the **closed** place set (`Place.all`), not only the
present places.

**Rationale**: one control, one convention — every place-carrying op in the panel already offers `Place.all`,
and a cue may legitimately name a place it `AddZone`s first, so offering the whole closed vocabulary is
consistent and more expressive. `CueTree` names no zone registry (`specs/047` FR-006), so it *cannot* know
which places are present without new plumbing.

**Spec alignment**: FR-005 names the closed place set, matching this control; the entity choosers still
populate from the chosen place's zone **when it is present**, and are not drawn when it is not (D7).

## D7 — Entity choosers read the op's place

**Decision**: in `CueEditor.drawNodeText`, resolve each node's own zone: an actor/camera op reads
`zones.TryGetValue place`; `MoveActor`/`RotateActor` read the active zone. The actor chooser, the waypoint
chooser and the clip/morph (`assetNamesOf`) choosers read that zone. When the op's place is **absent** there is
no zone to scan, so the actor/waypoint chooser is **not drawn** — the name it holds is preserved and shown in
the row label — and the clip/morph fields fall back to typing because no asset resolves. `AddActor`'s waypoint
and the trigger toggles keep their existing typed fallback, because their field slots are free; a blend's two
slots are taken by its clips, so it does not gain one.

**Rationale**: the chooser must read the scene the op targets — the whole point of the feature. The typed
fallback is the established pattern for "the place that names the entities is not present" and keeps an op
authored ahead of its zone usable.

## D8 — A new op defaults its place to the active zone

**Decision**: `CueTree.defaultOfKind` takes a `defaultPlace: Place` and uses it for every op's place
(`RemoveActor`, the blends, the player toggles, the camera ops, and the existing zone/`AddActor`/trigger
ops). `drawEditor` takes that place too (from `CueEditor`, which passes `state.ActiveZone`) and threads it
through both the kind change and `withAppendedChild`.

**Rationale**: `defaultOfKind` currently hard-codes `Place.start`; the feature's "default to the current
zone" is exactly that a newly-chosen op's place is the active zone. `CueTree` may name `Place`; it needs no
registry to receive the default, so `specs/047` FR-006 still holds.

## D9 — Write helpers preserve the place

**Decision**: `CueTree.withPlace` gains arms for the nine ops; `withActor`, `withText`, `withText2` and
`withWaypoint` gain the new `place` argument in their constructor patterns and pass it through untouched.
`textOf`/`textOf2` are unchanged in what they report (the place is not a text field).

**Rationale**: the combos commit through these helpers (`withAt path (write node value) cue`); each must
carry the fields it does not edit. This is the same discipline `withActor` already applies to the asset and
waypoint.

## D10 — Labels show the zone

**Decision**: an entity op's row label names its place, as the trigger toggles' labels already do — e.g.
`Remove actor: Aiko in Island`, `Animate: Jog in Island`, `Camera: balcony in Island`. The in-flight forms
(`BlendState`, `MoveActorState`, `RotateActorState`, `FlyCameraState`) keep their current labels.

**Rationale**: once ops carry zones, a list of actor ops that only shows the actor name cannot tell two
same-named actors in different zones apart; showing the place is what makes the tree readable. It is the
deferred UX point from clarify, resolved here.

## D11 — Verification is headless

**Decision**: assert the vocabulary and resolution through the pure paths — `test/CuePanelTests.fs` for the
control (`describe`, labels, kinds, `withPlace`/`withActor`/`withText` writes, `defaultOfKind`'s place) and
`test/CueTests.fs` through `CueEngine.tick` with a fabricated `ActorAsset` (an op naming a place acts on the
actor there; two same-named actors in two zones resolve to the op's zone; an absent place and a name not in
the place no-op; the camera ops read the named place and still raise on a miss). `test/PlayerTests.fs` is
retargeted; the global-`tryActorByName` tests retire with the function.

**Rationale**: `specs/063`/`specs/073` established the headless fixture; the drawing (`drawEditor`/
`drawNodeText`) is device-bound, so the pure halves are asserted directly, as the panel suite already does.
