# Research: Zone Cue Ops

## D1 — The op set: `AddZone`, `RemoveZone`, `PreloadZone` (plus a no-op `SetZone`)

**Decision**: The cue gains three leaf ops, each carrying a `place: Place`, alongside the existing `SetZone`:
`AddZone`, `RemoveZone` and `PreloadZone`. All four are "no progress of their own" leaves — reached, performed
and gone (`ValueSome Fin`), like `Exposit` and the advent ops. `SetZone` keeps its shape but its behaviour on a
place that is not present changes from *raise* to *no-op* (spec FR-008).

**Rationale**: The names mirror the existing vocabulary (`AddAdvent`/`RemoveAdvent`, `SetZone`) and the
reference: `docs/ENVIRONMENT_LRU_PLAN.md` names a `PreloadZone` cue op as the deferred follow-up of the LRU
work (spec 050), and odin carries the same. "Add" and "remove" are the two presence directions; "preload" is
the residency direction.

**Alternatives considered**: `DeleteZone` instead of `RemoveZone` (mirrors the editor command `ZoneDelete`, but
the cue vocabulary uses `Add`/`Remove` for its paired ops); separate ops for the editor's add-and-enter versus a
streaming add (rejected: `SetZone` already enters, so the ops compose).

## D2 — The no-op rule lives at the cue layer; the editor functions keep failing loudly

**Decision**: The redundant-state and contradictory-state checks are **guards in the cue ops**
(`world/CueEngine.fs`), not changes to `GameplayState`'s functions. `GameplayState.addZone`/`switchZone`/
`deleteZone` keep raising exactly as they do for the editor; the cue arm checks the precondition and performs
nothing when it does not hold.

**Rationale**: Principle II (Fail Loudly) is right for the editor, where a refused action is a control that
should be disabled and a bug when it is not. A cue is different: it is runtime data that may legitimately run
twice, or reach an op whose effect a previous op already produced, so a redundant op is the ordinary case, not
a bug (spec FR-007, Q3 = B). Putting the guard at the cue layer keeps the editor's loud behaviour and confines
the softening to the one caller that asked for it.

**Alternatives considered**: making the `GameplayState` functions no-op and having the editor check
preconditions to disable controls — rejected: it would move "this is a bug" out of the functions and scatter it
into every editor call site.

## D3 — `AddZone` streams the registered template without entering the zone

**Decision**: An add op makes the place **present** and populates it from its registered template (environment,
light, placed props) if it has one, then **detaches the new zone's bodies** so the inactive zone does not
simulate. The active place, the camera and the selection are untouched. The presence primitive is a new
`GameplayState.addPresentZone` (editor `addZone`, which also activates, is unchanged).

**Rationale**: Spec FR-002 and the Q1 = A decision: add *prepares*, `SetZone` *enters*. The body-building
constraint is what makes this subtle: `Physics.createBody` both creates and **adds** the body
(`physics/Physics.fs:68-77`), and `Zone.loadEnvironmentFrom`/`Zone.spawnPropBinding` therefore leave every body
they build joined to the simulation. A new zone is inactive, so its bodies must be removed — `Zone.deactivatePhysics`
already does exactly that (it is the switch path's detach), and calling it after populating leaves the zone in
the same "bodies alive but detached" state a zone is in when you switch away from it.

**Alternatives considered**: a temporary switch into the new place to build its bodies and then back out (the
shape `History.undo` uses for a zone delete). Rejected: `swapZone` runs `enterZone`, which would re-frame the
camera and drop the selection — a visible side effect for an op whose whole contract is "do not enter". Because
`createBody` is create-and-add, the detach alone suffices and no switch is needed.

## D4 — The template lookup happens at the cue layer, keeping the runtime registry-free

**Decision**: `world/CueEngine.fs` opens `ZoneRegistry` and resolves the place's template with
`ZoneRegistry.tryTemplate place shared.ZoneRegistry`, passing it to `GameplayState.addPresentZone`.
`GameplayState` and `world/Zone.fs` do not consult the registry.

**Rationale**: `specs/061` drew the line that the registry is **config, not identity**, and that the runtime
keys its zones by place *without depending on the registry*. Keeping the lookup in the cue engine preserves
that: presence is a registry-free primitive that takes a template as data; only the game's cue vocabulary knows
where templates come from. Compile order already permits it — `world/ZoneRegistry.fs` is listed before
`world/CueEngine.fs` (which is itself after `state/GameplayState.fs`).

**Alternatives considered**: resolving the template inside `GameplayState` — rejected: it re-entangles the
runtime with the config store, reversing 061.

## D5 — `PreloadZone` is a bare `Assets.requestEnvironment`, best-effort

**Decision**: A preload op resolves the present zone's environment asset id and calls
`Assets.requestEnvironment shared.Assets assetId` — the existing asynchronous, de-duplicated, non-blocking
request. No new state, no pin change: the preloaded environment follows normal least-recently-used recency, and
making room for it evicts only non-active environments (the pin set is already the active zone's environment,
refreshed each frame by `screen/GameplayScreen.fs`).

**Rationale**: Spec Q2 = A. `requestEnvironment` already de-duplicates against a loading or resident
environment (`world/Assets.fs:500-513`) and never loads on the calling thread, so the op is a one-liner. The
hard guarantee the spec keeps — a preload never unloads the active zone's environment — is already provided:
`Assets.settle`'s eviction excludes `PinnedIds`, and the gameplay layer narrows those to the active environment
every frame.

**Alternatives considered**: a *strong* reservation (a "wanted" set that pins the preload until it is used).
Rejected per Q2: it needs new gameplay state and a wider pin computation, and a preload being evicted under
real budget pressure is acceptable; the switch re-requests anyway.

## D6 — `RemoveZone` guards, then reuses `GameplayState.deleteZone`

**Decision**: A remove op performs nothing when the place is absent or is the active place; otherwise it calls
`GameplayState.deleteZone`, which releases the zone's bodies and GPU resources and unloads its environment
unless a still-present place shares it (`specs/061` FR-010).

**Rationale**: The release and the shared-asset rule already exist and are tested; the cue needs only the guard
(D2). The active place being a no-op is spec Q3 = B; the editor's `deleteZone` still raises for it.

**Alternatives considered**: none material.

## D7 — Authoring: one place chooser for the whole zone-op family

**Decision**: `editor/CueTree.fs` treats `SetZone`/`AddZone`/`RemoveZone`/`PreloadZone` as one family: the kind
list and `kindName` gain `"Add zone"`, `"Remove zone"`, `"Preload zone"`; `defaultOfKind` gives each
`Place.start`; `label` reads `"Add zone: Island"` etc.; `withPlace` is extended from `SetZone` to all four; and
the existing place-chooser arm in `drawEditor` widens to all four. `textOf` already answers `ValueNone` for
non-text ops, so no text field is offered.

**Rationale**: The four ops differ only in what they do at run time; their authoring is identical (pick a
place), so a shared arm keeps the control small and the ops consistent — exactly how the advent ops share a
chooser.

**Alternatives considered**: a separate control per op — rejected as redundant; they name the same closed set.

## D8 — No new module and no compile-order change

**Decision**: The op cases go in `world/Cue.fs`; the effects and guards in `world/CueEngine.fs`; the presence
primitive in `state/GameplayState.fs`; the (small) template-population helper in `world/Zone.fs`. No file is
added to `fsharp-game.fsproj`.

**Rationale**: Every module the feature touches already sits in the right order relative to its dependencies:
`Place` before `Cue`; `Zone`, `Assets` and `ZoneRegistry` before `GameplayState` and `CueEngine`. The only new
edge is `CueEngine → ZoneRegistry`, which the existing order permits (D4).

**Alternatives considered**: a new `world/ZoneStreaming.fs` — rejected: the behaviour is three ops and one
primitive, not a subsystem (Minimalism).

## D9 — Verification is headless

**Decision**: The new behaviour is asserted in `test/CueTests.fs` (the ops' effects and no-ops through
`CueEngine.tick`), `test/CuePanelTests.fs` (labels, kinds, `withPlace`) and `test/GameplayStateTests.fs`
(`addPresentZone` makes a place present with its template and leaves the active place alone), using the
existing device-free fixtures (`TestSupport.manager`, fake `Set`/`ZoneTemplate`). No window, no GPU.

**Rationale**: The project's whole test envelope runs with no device (constitution; every spec since 050 does
this). Zone presence, streaming and the residency request are all data operations that need no GPU — the
environment asset is a hand-built record, and `requestEnvironment` only enqueues.

## D10 — A preload resolves a bare registered place from the registry

**Decision**: When a preload names a present zone that has no environment of its own, `CueEngine` resolves
`ZoneRegistry.tryTemplate place` and, if there is a template, populates the zone from it (via
`GameplayState.populateZone` — which loads the template's environment) before the usual request. A present
place with neither an environment nor a registered template is the only no-op.

**Rationale**: The editor's add makes a place present **empty** (`specs/061` FR-003), so a registered place
can be present with no environment; the cue vocabulary then had no way to give it its content (`AddZone`
no-ops because the place is present). Resolving the registry at the cue layer — the same layering D4 chose —
makes `PreloadZone` mean "make this present place ready to enter" without touching the editor's add semantics.

**Alternatives considered**: making the Outliner's add stream the template (rejected: it changes `specs/061`
FR-003 and duplicates Instantiate); leaving it a no-op (rejected: it left a present-but-empty registered place
unreachable from a cue — the gap this closes).
