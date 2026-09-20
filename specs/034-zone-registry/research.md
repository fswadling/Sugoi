# Research: The Zone Registry

Decisions derived from the feature spec and the code as it stands (`world/Zone.fs`,
`world/Entity.fs`, `world/EnvironmentAsset.fs`, `world/Assets.fs`, `world/Casting.fs`,
`state/Command.fs`, `state/SharedState.fs`, `editor/CastPanel.fs`, `editor/AssetBrowser.fs`,
`editor/Selection.fs`, `core/SlotMap.fs`, `render/ImGui.fs`, `native/cimgui_sdl3.cpp`, and the
headless fixture in `test/SpawningTests.fs`).

## Decision 1 — A zone's variants are its own union, and the case carries it

- **Decision**: the vocabulary is a union whose cases carry a further union each:
  `type Place = ArtRoom of Period | Island`, with `Period` declared beside it. A *place* is a value —
  `Place.ArtRoom Period.Morning` — and it is the registry's key: `Dictionary<Place, ZoneTemplate>`. A
  case with no fields is a place with no variants, and contributes a single row: `Island` is one place
  named for itself, so the shipped vocabulary exercises both shapes.
- **Rationale**: the spec's requirement is that each zone declares its own variants, unlike the
  cast's single shared `Look`. Carrying the variant union in the case makes the association
  structural: reflection reads a case, then that case's field type, and has exactly that zone's
  variants — no mapping table to keep in step, and two zones cannot accidentally share a set. It also
  keeps one typed key for the whole map, so a place is a value that cannot be misspelled and a
  definition's edit is one arm, which is the argument `specs/025-narrative-core` D28 makes for
  `Character` being a union rather than a `string`.
- **Alternatives considered**: a shared `Variant` union with `Slot = { Place : Place; Variant :
  Variant }`, which is the cast shape verbatim (`specs/032-cast-binding`) — rejected because
  `Morning` would then be a case of a type *every* zone shares, which is the thing the spec forbids,
  and because a zone's set would be describable only by the entries that happen to exist; a flat union
  (`ClassroomMorning | ClassroomEvening | …`) — no two-level tree to draw, and the grouping would have
  to be declared beside the union, which is the list-beside-the-vocabulary the spec's FR-002 forbids;
  a `string` path per place — reintroduces misspellable identity and loses exhaustiveness for no gain.

## Decision 2 — Names are derived from the walk, not matched by hand

- **Decision**: the panel's labels come from the same reflection walk that builds the tree:
  `ZoneRegistry.byZone` carries, per zone, its case name and a list of `{ Name; Place }` entries, and
  the per-place name is the zone's case name plus the variant's (`"ArtRoom Morning"`), or the zone's
  name alone for a fieldless case (`"Island"`). The walk runs once, at type initialisation.
- **Rationale**: this departs from `Casting.lookName`, which is a hand-written `match` over the cases
  with the deliberate property that adding a case fails the match and so cannot be forgotten. That
  works where the arms number a handful — one small union shared by everything. For places the arms
  are the *product* of the zones and their variants, and a second enumeration is precisely the list
  maintained beside the vocabulary that FR-002 forbids: it can lag the union, and `specs/032-cast-binding`
  is the project's own evidence of what a stale enumeration costs (sixteen removed arms compiled as
  variable patterns, unreachable rules after them, and no warning). Deriving the tree and the labels
  from one walk means the two cannot disagree.
- **Cost, stated plainly**: a label is always its case name — there is no way to word one
  differently without renaming the case — and adding a variant is name-complete automatically, so
  nothing is reported by the compiler. The compensation is that `test/ZoneRegistryTests.fs` asserts the
  enumeration is drawable (every group non-empty, every name non-empty and distinct), so a vocabulary
  the panel cannot render fails a test rather than drawing a blank row.
- **Alternatives considered**: a hand-written `placeName` matching every place (compiler-guarded for a
  *removed* case, but silent for a newly added variant, and it is the second enumeration this
  decision exists to avoid); formatting the case names per row per frame (the walk already computes
  them once, and per-frame reflection is work for nothing).

## Decision 3 — A record's parts are `PropState`

- **Decision**: `ZoneTemplate.Parts` is a `PropState array` — the type `Zone.capturePart` already
  answers and `PartCreate`/`PartDelete` already carry.
- **Rationale**: a part's authored value *is* those four fields. A new `PartState` would be a fourth
  spelling of them alongside `PropState`, `AssetBinding` and `EnvironmentAsset.EnvironmentPart`, kept
  in step by hand. Reusing the capture shape also makes FR-007 fall out rather than be implemented:
  the record is a copy of what the live environment holds, so a part the author moved or renamed is
  recorded as she left it, a part she deleted is simply absent, and one she duplicated is present
  twice.
- **Alternatives considered**: a dedicated `{ Base; SubmeshIndex; SubmeshCount }` (drops the
  meaningless-for-a-part `AssetId` field, at the cost of a type mirroring three of `PropState`'s four
  and converted both ways); recording each part's index in the asset's part list (cannot represent a
  part the author added, and breaks when the asset's ordering changes).

## Decision 4 — The record is taken from an `Environment`, and the panel writes the map directly

- **Decision**: `ZoneTemplate.ofEnvironment : Environment -> ZoneTemplate` is pure and takes a loaded
  environment, not a zone. `world/ZoneRegistry.fs` exposes `create` and `tryTemplate`, and the panel
  writes `shared.ZoneRegistry.Templates.[place] <- ZoneTemplate.ofEnvironment environment`.
- **Rationale**: the registry records an environment, so taking one is the honest dependency — and it
  leaves `world/ZoneRegistry.fs` needing neither `Zone` nor the editor, which is `world/Casting.fs`'s
  shape exactly: that module answers `tryAsset` and lets the panel write `Bindings` directly. The
  write being one line is what FR-009 asks for: there is no command to record and no stack to touch,
  so there is nothing to get wrong.
- **Alternatives considered**: `capture (zone) (place) (registry)`, which needs `Zone` for no gain —
  the panel already holds the active zone, and moving "which environment is being filed" into the
  registry would just be a second place that answers it; a setter returning `Result` (there is no
  failure mode: any loaded environment is a valid record).

## Decision 5 — One activation path, and the parts capacity follows from it

- **Decision**: `Zone.buildEnvironment` stops reading everything from the asset and takes
  `(assetId) (baseEntity) (orientation) (states: PropState array) (epoch)`; a new private
  `assetPartStates` produces an asset's own states (including the glTF node transform decomposition
  that is there today). `loadEnvironment` and `loadEnvironmentFrom` become two short callers. The
  parts map's capacity becomes `max 512 (uint32 states.Length)`.
- **Rationale**: a zone instantiated from a registration and one loaded from an asset must be built
  by the same code, or the second path drifts from the first in exactly the ways that matter —
  bodies, renderers, epoch stamping, part naming. The capacity change is a consequence of admitting a
  second source of parts: the map is also what `spawnPartBinding` adds to, so it needs headroom
  beyond what it starts with, and `max` keeps today's 512 while removing a latent failure (an asset
  with more than 512 mesh-bearing nodes currently cannot be loaded at all, because `SlotMap` is
  fixed-capacity and `Add` would run off the free list).
- **Alternatives considered**: a second builder for records (duplicates every construction detail the
  activation path depends on); making `partsMap`/`meshProp` public so the registry assembles the map
  itself (leaks `Zone`'s internals across a module boundary to save three lines there); keeping
  `buildEnvironment` as it is and giving `loadEnvironmentFrom` its own copy of the loop (the drift
  above, in the one place it is least visible).

## Decision 6 — Two controls per row, each disabled while its precondition is unmet

- **Decision**: a place's row draws the place's name, the registered asset's name (or `(none)`), a
  **Register** control enabled exactly when the active zone has an environment loaded, and an
  **Instantiate** control enabled exactly when the place has an entry.
- **Rationale**: both disabled states are the *absence of the thing the control needs*, drawn rather
  than hidden, which is the posture `editor/CastPanel.fs` established for an unassigned slot — the
  author sees where the action is and what is missing to enable it, instead of a row that appears to
  have no actions. The active zone's environment is read once into a local, so the enabled state and
  the action taken cannot disagree about whether there is one.
- **Alternatives considered**: hiding a control whose precondition is unmet (loses the affordance,
  and makes a registered place and an unregistered one differ by a widget appearing rather than by a
  state); one control whose label or behaviour changes with state (one widget with two meanings — and
  the two meanings here differ precisely in whether an undo step is recorded, which is worth two
  widgets on its own).

## Decision 7 — The instantiate swap is duplicated, not extracted

- **Decision**: the panel's instantiate path repeats the Asset Browser's environment swap: read
  `previous` off the zone, load, `Selection.dropStalePartSelection`, push `SetEnvironment`, assign the
  new stack.
- **Rationale**: this is forced by the compile order rather than chosen. The natural home is
  `editor/Spawning.fs` — "the single place the editor creates entities", and an environment swap
  creates the parts — but `Spawning.fs` is compiled *above* `editor/Selection.fs` and so cannot call
  the reconcile. The remaining candidates are worse: `state/GameplayState.fs` would host an
  editor-command pairing in a state module, and `state/Command.fs` cannot see `state.Editor`. This is
  the second call site, and `AGENTS.md`'s rule of three puts the function at the third, so the
  duplication is the rule working as intended rather than an oversight. **Recorded so the third
  extracts rather than copies**: what would be extracted is the four-step sequence with the builder
  passed in, since the two sites differ only in whether they build from an asset id or a record.
- **Alternatives considered**: moving `dropStalePartSelection` down into `Command.fs` so `Spawning`
  could host the swap (churns two modules' boundaries to save six lines); reimplementing the
  reconcile inside `Spawning` (two copies of a selection rule, in a module about entities); having
  `Register` reconcile the selection (wrong direction — registering is not a scene edit and must not
  touch the scene).
- **Superseded in part by `specs/035-zone-template-props`, and the trigger above did not fire.** That
  feature's instantiation replaces a zone's props as well as its environment, so it creates entities
  and cannot be a panel's work either; the swap therefore *moved* into `editor/Spawning.fs` (as
  `instantiateZone`) rather than being extracted at a third call site, and the reconcile became the
  panel's own step — the idiom `Editor.undoStep`/`redoStep` already use, which is why `Spawning` no
  longer needs `Selection`. There are still two environment-loading sites, and they still build
  different things: one loads an asset, the other installs what an entry recorded along with its props.
  What they share is the three-line tail, which is not worth a function with two callers; the trigger
  for extracting it is now a **third** environment-loading path, the scenario work being the likely one.

## Decision 8 — The vocabulary and the map share one file

- **Decision**: the `Place` union, its variant unions, the reflection walk and the sparse map all
  live in `world/ZoneRegistry.fs`.
- **Rationale**: `cast/Roster.fs` is a file of its own because *two* clusters name its roster — the
  engine and the game — and neither owns it, which is what made "the engine cannot reference the game"
  a compile error. Nothing outside the game names a place, so a separate file would buy a boundary
  nobody crosses, at the cost of a file and a compile-list line. The variant unions are the cases'
  payload, so the vocabulary and the walk over it are read together anyway.
- **Trigger for revisiting**: the moment a second cluster needs to name places — the scenario work is
  the likely one, since a scenario will want to say which place a cue happens in. Then the vocabulary
  moves to its own file compiled above everything that names it, exactly as `cast/Roster.fs` did, and
  this decision is the thing to overturn.
- **Alternatives considered**: `world/Places.fs` now, mirroring `cast/Roster.fs` (a boundary only this
  feature observes, and a file that exists to hold forty lines).

## Decision 9 — The panel pushes both names onto the ID stack

- **Decision**: before drawing a place's row, the panel pushes the zone's name and then the place's
  name.
- **Rationale**: `Register` and `Instantiate` are drawn on every row, so their labels repeat exactly
  as the cast panel's `Spawn` did — and `specs/033-cast-spawn` shipped that collision, because ImGui
  keys a widget by its label plus the ID stack. It was found in the running editor; no build and no
  test could see it, and the fixing commit's own message says so. Two pushes are that correction made
  before rather than after, and they are also what makes the row's other widgets (now and later)
  unique.
- **Alternatives considered**: `##`-suffixed labels per button (fixes these two labels, and leaves the
  next widget added to a row to rediscover the same problem).

## Decision 10 — The dock layout needs one native line and a rebuild

- **Decision**: add `DockBuilderDockWindow("Zone Registry", left)` to
  `void cimgui_setup_dock_layout()` in `native/cimgui_sdl3.cpp`, and re-run `native/build.sh`.
- **Rationale**: the dock layout is built in native code, and `docs/EDITOR_DESIGN.md`'s Workspace
  table already places this panel in the left node alongside the Outliner and Cast; the Cast panel's
  docking was added the same way in `specs/032-cast-binding`. `native/libcimgui.so` is gitignored, so
  the `.cpp` is the tracked half of this change and the library must be rebuilt for the panel to dock
  — a build step, not a source change.
- **Alternatives considered**: leaving the panel undocked (ImGui floats it in the middle of the
  workspace, the doc's Workspace table stops being true, and Ctrl+Tab reaches a window that is not
  where the table says it is).

## Decision 11 — `SharedState` gains the field, beside the cast binding

- **Decision**: `ZoneRegistry : ZoneRegistry.Registry` joins `Cast` on `SharedState`, and
  `ZoneRegistry.create ()` names the empty registry.
- **Rationale**: it is the same kind of thing as the cast binding — project state that belongs to no
  zone — and it must outlive every zone (FR-010), which is the reason the binding is there rather than
  on the gameplay state. `GameplayState` is the scene's: its zones, its camera, its selection, its
  per-zone undo history. `create ()` exists because the field appears in four record constructions
  (production plus three test fixtures), and a dictionary literal written four times invites the
  copies to drift while forcing each site to `open` a module whose name also reads as the field.
- **Alternatives considered**: the field on `GameplayState` (scene state holding project state, and
  the one place the cast binding deliberately is not); no `create` (four literals to remember, one of
  which a later field addition can leave behind).

## Decision 12 — The vocabulary, the map and the round trip are asserted headlessly

- **Decision**: the vocabulary is asserted in `test/ZoneRegistryTests.fs`, the map's contract in the
  same file, and the round trip in `test/ZoneTests.fs`: load a room asset, move and rename one part and
  delete another, capture the record, load it into a fresh zone, and compare the parts.
- **Rationale**: unlike `specs/033-cast-spawn`, the interesting half here is data rather than ImGui
  glue, and it is reachable without a display for the reason `test/SpawningTests.fs` demonstrates: a
  null `Gpu.Device` is never dereferenced by `EnvironmentRender.create`, so a hand-built environment
  asset drives `Zone`'s real load and activation path. That makes FR-012's claim — a registration
  reproduces the zone — a checked claim rather than a promise, and it is the check this feature would
  otherwise be missing on exactly the question it exists to answer (a record that silently records
  too little). The round trip went into `test/ZoneTests.fs` rather than the registry's own file
  because that file already owns a fixture that drives the real `loadEnvironment`/`setEnvironment`
  lifecycle headlessly: reusing it is why the round trip needed no fixture of its own, where the
  tasks first put it in `test/ZoneRegistryTests.fs` and would have had to hand-build a third copy.
- **What remains unasserted**: that the panel draws, and that the two controls appear enabled and
  disabled in the states `Decision 6` describes. That is six lines of ImGui glue and one native
  dock line, and `quickstart.md`'s interactive scenarios are where they are checked.
- **Alternatives considered**: mirroring 033's "no test: it is ImGui glue" — the argument does not
  transfer, because 033 could point at `test/SpawningTests.fs` covering the path it reused and here
  the new behaviour is the record itself; testing the panel's draw against a headless ImGui context
  (the project has no such harness, and building one is its own feature).

## Decision 13 — What is deferred, and where it will land

- **Decision**: an entry records the environment asset, the environment's settings and its parts, and
  nothing else. Placed props, waypoints, triggers, splat maps and persistence are out.
- **Rationale**: the spec's scope boundary. `docs/ARCHITECTURE.md`'s `Zone_Registry` entry names prop
  templates and splat maps as belonging to an entry too, so the deferral is recorded rather than left
  as a silently narrower shape: they join as siblings of the parts, which is why the record groups the
  environment's own settings apart from the parts instead of flattening everything into one field
  list.
- **Alternatives considered**: recording the whole zone (props, waypoints and triggers included) —
  each is a separate identity problem (a prop's asset and binding, a waypoint's chain link into other
  waypoints, a trigger's volume and prompt), and settling all of them here would settle them without a
  spec.

## Noticed and not taken

- **The base-entity literal exists twice, in two clusters.** `Spawning.baseEntity`
  (`editor/Spawning.fs`) and the literal `Zone.loadEnvironment` builds for an environment's base
  (`world/Zone.fs`) are the same seven lines. `world` cannot see `editor`, so one copy is only
  possible by moving it into `world/Entity.fs` — which is a tidy-up of existing code with its own
  third call site argument, not part of this feature. Left as-is; the compiler reports a new `Entity`
  field at both sites.
- **There is no Window menu.** `docs/EDITOR_DESIGN.md` describes the sub-editors being opened from
  one; none exists, and the Cast panel did not add one. This panel follows Cast, so the doc's Window
  menu stays a gap rather than becoming half-built here.
- **The panel offers no way to bind an asset to a place without registering a zone.** That is the
  spec's first Assumption, argued there; if pre-assigning turns out to be wanted it is additive, and
  the place to overturn is that Assumption rather than this plan.

## No unresolved clarifications

Every choice is settled by the spec, by the code, or by the inspection recorded above — including the
two that could only be settled by reading the compile order (Decisions 7 and 11) and the one that
could only be settled by reading the test fixture (Decision 12). No `NEEDS CLARIFICATION` remains for
Phase 1.
