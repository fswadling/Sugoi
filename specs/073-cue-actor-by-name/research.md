# Research: Cue Actors Addressed by Name

## D1 — Actor ops carry a string, not a roster character

**Decision**: every actor op in `world/Cue.fs` takes `actor: string` (the actor's `Name`) in place of
`character: Roster.Character`: `AddActor`, `RemoveActor`, `MoveActor`, `RotateActor`, `Animate`, `CrossFade`,
`Morph`, `CrossMorph`, `EnablePlayer`, `DisablePlayer`, and the run-form cases (`MoveActorState`,
`RotateActorState`, `BlendState`). `world/Cue.fs` names no `Roster` or `Casting` value.

**Rationale**: the op was a closed-union value only for authoring convenience; at run time `specs/072` already
converted it to a name and scanned the scene. Making the op a string aligns authoring with resolution and lets
the editor be scene-driven (D4). It supersedes `specs/072` FR-004.

**Alternatives considered**: keeping `Roster.Character` and only changing the editor (rejected — the chooser
would still have to map a name back to a roster case, and the roster is the wrong key once the editor reads
the scene).

## D2 — `AddActor` names an asset and a name (no look, no binding)

**Decision**: `AddActor of place: Place * actor: string * asset: string * waypoint: string`. It drops the
`Casting.Look`, and resolves no binding: the new actor is built from the named **asset** (`Assets.createActorRender`)
and named `actor`.

**Rationale**: `AddActor` is the one op that *creates* an actor, so it cannot pick one from the scene; naming a
discovered asset is the reference lineage's shape and removes the binding from the cue vocabulary entirely
(spec FR-003). The editor offers discovered actor assets (as the Asset Browser does) and defaults the name to
the asset's.

**Alternatives considered**: dropping `AddActor` for an editor-only placement (loses "a cue brings a member
in"); keeping the look/binding for this one op (inconsistent, and the binding's key is a character the op no
longer carries).

## D3 — Resolution takes a string

**Decision**: `GameplayState.tryActorByName (state) (name: string) : (Place * Zone.Zone * EntityRef) option`
replaces the `Roster.Character` argument; `Zone.tryActorByName` already takes a string. `CueEngine`'s
`removeActor`, `tryResolveActor`, `beginMove`, `beginRotate`, the blend starters and the player toggles pass the
op's string through unchanged.

**Rationale**: the scan (`specs/072` FR-003) is unchanged; only the conversion (`Roster.characterName`) is
dropped. `noOp` takes the string directly rather than a character.

## D4 — The actor chooser moves to `CueEditor`, over the viewed place's actors

**Decision**: add `drawActorCombo` in `editor/CueEditor.fs`, mirroring `drawWaypointCombo`: it walks the
given zone's live actor slot map and offers their `Base.Name` through `CueTree.filteredCombo`, committing
`CueTree.withActor`. `CueTree.drawEditor` stops drawing the actor/look combos; for an existing-actor op the
actor combo is drawn by `drawNodeText` against the **viewed place's** zone (the active place, since these ops
name none).

**Rationale**: the chooser must read the scene, which only `CueEditor` (with the gameplay state) can do — the
same reason the waypoint and trigger choosers live there. Offering the place's live actors makes the editor
"the scene", which is the feature's point (spec FR-005, US2).

**Alternatives considered**: passing the actor-name list into `CueTree.drawEditor` (more plumbing, and the
combo still needs the filter buffer); keeping the roster combo (the bug being fixed).

## D5 — `AddActor`'s editor: asset combo + typed name + place + waypoint

**Decision**: `AddActor`'s controls split by source: the **place** combo stays in `CueTree.drawEditor` (a
closed set); `CueEditor.drawNodeText` adds a **drawAssetCombo** over the discovered actor assets (committing
`withAsset`) and a typed **name** field (`textOf`/`withText`, `textOf AddActor = actor name`), plus the
existing `drawWaypointCombo` against the named place's zone.

**Rationale**: `AddActor` creates an actor, so its name is not chosen from the scene — it is typed (defaulting
to the asset's), which is why it is the one actor control that is free text (spec Clarifications Q4, edge
case). The asset is a discovered asset, offered like the Asset Browser's actor list.

## D6 — Clip/morph names come from the placed actor's asset

**Decision**: `CueEditor`'s `assetNamesOf (actor: string)` resolves the actor in the **viewed place's** zone
(`Zone.tryActorByName` → `ActorRender.Asset` → `shared.Assets.Actors.[assetId]`) and returns that asset's
animation-clip and morph names; empty when the actor is not present. The cast-binding lookup is deleted.

**Rationale**: the field the cue will actually resolve at run time is the placed actor, not a binding (spec
FR-006, US3); populating the choosers from it is what makes them light up whenever she is in the scene.

**Alternatives considered**: the binding (the current, broken behaviour); unioning binding and scene (two
answers to one question).

## D7 — Write helpers: `withActor` and `withAsset`; `withLook` deleted

**Decision**: `CueTree` gains `withActor (name: string)` (replacing `withCharacter`, now string-based) and
`withAsset (id: string)` (for `AddActor`); `withLook` is deleted. `withPlace`, `withWaypoint`, `textOf`/
`withText`/`textOf2`/`withText2` are updated for the new field shapes.

**Rationale**: the write helpers are how the combos commit (`CueTree.withAt nodePath (write node value) cue`),
so each new control needs one; `withLook` has no field left to write (spec FR-003).

## D8 — The no-op rule is unchanged, with the asset in place of the look

**Decision**: `AddActor` performs nothing — and the cue continues — when the named place is not present, the
named waypoint does not resolve, or the named asset names no discovered actor asset. Removal and the acting
ops perform nothing when no actor of the name is present. All report through the existing `noOp` diagnostic.

**Rationale**: `specs/064` D7 / `specs/072` D7. The only change is the reason: an unresolved *asset* replaces
an unbound *look*.

## D9 — Verification is headless

**Decision**: retarget the actor-op tests in `test/CueTests.fs` to string names and the asset-naming `AddActor`;
update `test/CuePanelTests.fs` (labels/kinds/write helpers, `withActor`/`withAsset`, `textOf AddActor`); update
`test/ActorNameTests.fs` (`tryActorByName` takes `"Aiko"`). The editor's `drawEditor`/`drawNodeText` are drawn
through ImGui (device-bound), so the pure halves (`CueTree` write helpers, and `AssetNames`' pure asset lookup)
are asserted headlessly, as `test/CuePanelTests.fs` already does for the tree helpers. `test/DialogueTests.fs`
and `test/CastingTests.fs` remain unmodified — the regression proof that portraits are untouched (SC-004).

**Rationale**: `specs/063`/`specs/072` established the headless fixture; the dialogue/casting suites passing
unmodified is the proof the binding is untouched.
