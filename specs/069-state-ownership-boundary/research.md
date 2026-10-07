# Research: State Ownership Boundary

## D1 — The deciding test is readership, not authorship or persistence

**Decision**: A state member's home is decided by **who may read it**. `SharedState` holds state
the engine itself, or more than one screen, may read; `GameplayState` holds state whose only
readers are the gameplay screen and its editor layer.

**Rationale**: The existing written rule — *"the undo stacks are per zone, so anything that must
outlive every zone lives here (spec 034)"* (`state/SharedState.fs:18`) — is dead:
`specs/048-unified-undo-stack` replaced the per-zone stacks with one history on the gameplay state,
and `docs/EDITOR_DESIGN.md` D-022 records that *"that reason is gone"*. Persistence cannot be the
test either: the zones — the largest authored payload, and the one a save writes — already live on
`GameplayState`. Readership is the one test that is checkable from the call sites.

**Alternatives considered**: "authored project data is shared" — rejected, because `GameplayState`
already owns the authored scene (the zones). "persisted data is shared" — rejected for the same
reason. "cross-screen" alone — rejected as too narrow: with one real screen (`MainMenu` reads
nothing) it fails to place `Physics`, which is shared across **zones**, not screens.

## D2 — The zone registry moves to the gameplay state

**Decision**: Move `ZoneRegistry.Registry` from `SharedState` to `GameplayState`, beside `Zones`.

**Rationale**: Every reader is the gameplay screen or its editor layer: the Zone Registry panel
(`editor/ZoneRegistryPanel.fs:35`, `:57`) and the cue's zone ops through
`GameplayState.presentZone`/`populateZone`, reading `ZoneRegistry.tryTemplate … shared.ZoneRegistry`
in `world/CueEngine.fs` (`AddZone`, `PreloadZone`). The registry is the recipe table for the zones,
which are the gameplay state's, so it belongs next to them.

**Alternatives considered**: keep it shared for consistency with `Cast`/`Advents` — rejected: those
two are kept for a different reason (D4). Leave it where it is and only fix the comment — rejected
by the spec (FR-002).

## D3 — The physics world moves to the gameplay state

**Decision**: Move `Physics.World` from `SharedState` to `GameplayState`, beside `Zones`.

**Rationale**: Nothing outside the gameplay path reads it — the play chain
(`screen/GameplayScreen.fs:65-77`), the editor's nudge/pick/drag (`editor/Editor.fs:377-385`),
`editor/Selection.fs`, `editor/Spawning.fs`, `editor/AssetBrowser.fs`, every `Zone.*` operation
through `state/GameplayState.fs`/`state/History.fs`/`world/CueEngine.fs`, and the shutdown sweep
(`Engine.fs:257`, which touches it only to release the gameplay zones). It is shared across **zones**,
and `GameplayState` owns every zone, so the single-world invariant is preserved. It is the same
finding as the registry; it is recorded separately because its "engine service" appearance most
plausibly resists the move.

**Alternatives considered**: keep it as a service alongside `Gpu`/`Assets` — rejected: `Gpu` is read
by the engine's own render loop and `Assets` by any presenting screen, while `Physics` has no
non-world reader. The "service" label is descriptive, not a placement rule; readership is.

## D4 — The cast binding and the advent flags stay shared

**Decision**: Leave `Cast` (the slot → actor-asset binding) and `Advents` (the flags) on
`SharedState`, with their rationale stated as an anticipated non-gameplay reader (a journal or
inventory screen), not as zone survival.

**Rationale**: Both are the kind of game data a screen other than the world would present — a
binding resolves character → look → actor asset → portrait, and the flags are progress a UI may
gate on. Today only the gameplay screen reads them, which is why they were candidates; the
anticipated reader is what separates them from the registry and physics.

**Alternatives considered**: move them too, for uniform "authored/common" treatment — rejected per
the spec (FR-004) and the D1 test.

## D5 — No compile-order or signature change

**Decision**: Do not touch `fsharp-game.fsproj`; do not change any `Zone.*`, `Command.apply`/`undo`
signature. `GameplayState.create` constructs the registry and the physics world itself.

**Rationale**: `physics/Physics.fs` (line 44) and `world/ZoneRegistry.fs` (line 99) both compile
before `state/GameplayState.fs` (line 105), so the state may reference them. `Zone.*` takes physics
as a parameter (a zone never holds the world), and `Command.apply`/`undo` take `(zone, physics,
assets)` as values (`docs/CUE_MODEL_DECISION.md`), so nothing forms a `Command → GameplayState`
cycle. `CueEngine.fs` and `History.fs` compile *after* `GameplayState` and already receive `state`.

**Alternatives considered**: pass the moved values in through `create` parameters — rejected: it
adds parameters for no gain; `create` already builds the state's own containers.

## D6 — Test fixtures stop hand-building the moved members

**Decision**: The hand-built `SharedState` fixtures in `test/*.fs` drop `ZoneRegistry = …` and
`Physics = …`; a test that needs a seeded template sets it on the gameplay state's registry, and a
test that reads the world reads `state.Physics`.

**Rationale**: `SharedState` no longer has the fields, and the fixtures construct both records by
hand (`test/GameplayStateTests.fs`, `AdventTests.fs`, `CastIndexTests.fs`, `CuePanelTests.fs`,
`CueTests.fs`). `CueTests.registerTemplate` seeds a template and becomes a state-seeding helper.

**Alternatives considered**: a shared test helper that builds both — deferred; the fixtures are
small and per-file, and consolidating them is out of this feature's scope.

## D7 — The docs stop citing the dead rationale

**Decision**: Correct `docs/ARCHITECTURE.md` §State management, `docs/PROJECT_STRUCTURE.md`
(whose `SharedState.fs` one-liner still says *"GPU context, assets, physics"*), and the
`docs/EDITOR_DESIGN.md` D-011 note (which says `SharedState` hosts the zone registry). Check
`docs/ENVIRONMENT_LRU_PLAN.md` and `docs/ROADMAP.md` for `Shared.Physics`/registry references.

**Rationale**: The spec (FR-010) requires the code and docs to agree on the readership rule and to
drop the per-zone-undo reason. `docs/CUE_MODEL_DECISION.md` already states that `Command.apply`
takes `(zone, physics, assets)`, so it needs no change and is the citation that the move is safe.
