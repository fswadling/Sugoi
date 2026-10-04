# Research: Zones as Places

## D1 — Extract `Place` into its own vocabulary module

**Decision**: Move `Place` (and its `Period` variant union) out of `world/ZoneRegistry.fs` into a new
`world/Place.fs`, compiled early (before `world/Cue.fs`), and give it the vocabulary helpers the other closed
unions have: `placeName : Place -> string`, `all : Place list` (declaration order) and `start : Place`. The
registry (`world/ZoneRegistry.fs`) opens `Place` and keeps only the sparse `Registry` (place → template) and
the panel tree (`byZone`/`ZoneGroup`/`PlaceEntry`).

**Rationale**: This mirrors `Roster` (vocabulary, compiled first, consumed by engine and game) vs `Casting`
(the mapping), which is the codebase's existing split of identity from config. It also makes "the zone map
does not depend on the registry" structurally true: `GameplayState` depends on `Place`, not on the registry
module. `Place` must precede `Cue.fs` because the cue's zone op names a place.

**Alternatives considered**: keeping `Place` in `ZoneRegistry` and having `GameplayState`/`Cue` depend on
that module — rejected: it entangles the runtime with the config store.

## D2 — Runtime zones keyed by `Place`; the state starts on the start place

**Decision**: `GameplayState.Zones : Dictionary<Place, Zone>` and `ActiveZone : Place`. `GameplayState.create`
makes the **start place** present and active (only), exactly as it makes one `"main"` zone today.

**Rationale**: The authored identity becomes the storage key, so a zone that is not a declared place cannot
exist. Keeping `create` to one present place preserves today's boot shape and the "presence is managed"
decision.

**Alternatives considered**: populating every place at creation — rejected by the spec's presence decision
(`061` Clarifications) and because environments load lazily anyway.

## D3 — The start place is a designated constant

**Decision**: `Place.start` (a value in the vocabulary), used by `GameplayState.create`. No `create`
parameter is added.

**Rationale**: Spec Q2 = A: the starting location is stated, not implied by order. A constant avoids touching
every `GameplayState.create` call site (including tests).

## D4 — The zone steps carry a `Place`

**Decision**: `Command.ZoneMove of Previous: Place * Next: Place`, `ZoneAdd of Previous: Place * Name: Place`,
`ZoneDelete of Previous: Place * Name: Place * Contents: ZoneContents`. `History` applies/undoes them by
place; `GameplayState.switchZone/selectZone/addZone/deleteZone` take `Place`.

**Rationale**: Type-only change to the existing steps; the undo/redo machinery (which relies on the active
zone being the step's zone) is unchanged. Under the "active place cannot be made absent" rule the deleted
zone is always inactive, so `Previous` is simply the (unchanged) active place; the payload is retained to
keep the command shape and the undo's temporary re-activation as they are.

## D5 — Presence is managed in the Outliner; removal unloads the environment

**Decision**: The Zones section lists present places (declaration order) with their markers; **add** offers
the absent declared places (a chooser, not free text); **remove** is refused for the active place and, when
allowed, releases the zone and unloads its environment from the GPU (FR-010, via the asset manager's release).

**Rationale**: Spec Clarifications: presence is managed, the active place cannot be made absent, and a
removed zone's environment must not stay loaded. Sharing is respected: an environment a still-present place
uses is not unloaded.

**Alternatives considered**: auto-picking a successor on removing the active place — rejected (spec Q on
active-removal = C, which is simpler and needs no ordering rule).

## D6 — Drop `Zone.Name`; diagnostics name the active place

**Decision**: Remove `Zone`'s `Name : string` field (it was never set — always `""`, so `CueEngine`'s "in zone
''" message was a latent bug) and have diagnostics use `Place.placeName` of `GameplayState.ActiveZone`.

**Rationale**: A zone's identity is its place; a second string name is a redundant source of truth (FR-007).

## D7 — The registry panel acts on the place it names

**Decision**: **Register** captures the active place's zone as that place's template, and is offered for the
active place (a place's config is captured from its own zone, not filed under another). **Instantiate** makes
the named place present (if absent) and active and loads its template, as one command step — a `Batch` of the
presence/active change and the environment load — so one undo reverses the whole thing.

**Rationale**: Spec FR-009 and Clarifications (Instantiate = A). Loading builds bodies, which may only be
built in the active zone, so making the place active is part of the gesture.

## D8 — The cue's zone op names a place; the cue panel offers a place chooser

**Decision**: `Cue.SetZone` carries a `Place`. The cue panel draws a place chooser (the closed set, as the
advent and presentation choosers already do) instead of a text field; `CueTree.label`/`describe` render
`placeName`.

**Rationale**: FR-005. A place needs no zone or registry read to be offered, so the shared cue control stays
free of `GameplayState`, exactly as the advent combo does.

## D9 — Compile order

**Decision**: Add `world/Place.fs` immediately after `world/Advent.fs` and before `world/Cue.fs` in
`fsharp-game.fsproj`; `world/ZoneRegistry.fs` then opens `Place`.

**Rationale**: `Place` has no dependencies; `Cue.fs` and the state need it. F# compiles in list order, so
this is the only ordering constraint.
