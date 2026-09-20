---

description: "Task list for the Zone Registry feature"
---

# Tasks: The Zone Registry

**Input**: Design documents from `/specs/034-zone-registry/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: **Two new test files are included**, and unlike `specs/033-cast-spawn` the argument for
skipping them does not apply (`research.md` Decision 12). The interesting half of this feature is data
rather than ImGui glue, and it is reachable headlessly for the reason `test/SpawningTests.fs`
demonstrates: a null GPU device is never dereferenced on the environment load path, so a hand-built
asset can drive the real `Zone` code. The round trip in T012 is what makes FR-012 a checked claim.
What no test reaches — that the panel draws, and that its two controls appear in the states T008 and
T011 describe — is `quickstart.md`'s interactive scenarios.

**Organization**: Tasks are grouped by user story. Phase 1–2 are the compile-order plumbing and the
two data types every story reads; Phase 3 draws the tree (US1), Phase 4 adds registering (US2), and
Phase 5 adds instantiating (US3). The instantiate half is deliberately *not* in the foundational
phase: it is the only thing that needs the `Zone` builder change, so it belongs to the story that
needs it and US1 and US2 stay independently deliverable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- F# single project at the repository root; modules live in domain clusters (`world/`, `state/`,
  `editor/`, `render/`).
- Compile order is defined by `fsharp-game.fsproj`, and in F# a file may only reference symbols
  defined in earlier files — so a new module's position in that list is part of creating it, not a
  follow-up. Tests are listed in `test/fsharp-game.Tests.fsproj` the same way.
- Tests live in `test/` (one `<Module>Tests.fs` per module).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The compile list, which in F# is the first thing that has to exist.

- [X] T001 Add `world/ZoneTemplate.fs` immediately before `world/Zone.fs` and `world/ZoneRegistry.fs`
  immediately after it in `fsharp-game.fsproj`, with each file created holding its `module` line and
  a header comment. The order is the design: `Zone.fs` must be able to name the record its loader
  takes (`ZoneTemplate`), and `ZoneRegistry` needs neither `Zone` nor the editor, so it sits below
  both and cannot grow a dependency on the live zone by accident (`plan.md`, Structure Decision).
  Then read `render/ImGui.fs` and confirm the panel's widgets are all bound already — `igText`,
  `igSameLine`, `igSmallButton`, `igBeginDisabled`/`igEndDisabled`, `igCollapsingHeader_TreeNodeFlags`,
  `igPushID_Str`/`igPopID` — so this feature adds no `DllImport`.

**Checkpoint**: `dotnet build` is clean with two new empty modules in the list.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The record and the map. Every user story reads one of them.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 Implement `world/ZoneTemplate.fs`: `type ZoneTemplate = { AssetId : string; Base : Entity;
  Orientation : Quaternion; Parts : PropState array }` and `ofEnvironment : environment:Environment ->
  ZoneTemplate`, which walks the live parts slot map (`SlotMap.IteriAlive`) and takes each part's
  authored value. `Parts` is a `PropState array` and not a new type, because `PropState` is what
  `Zone.capturePart` already answers and what `PartCreate`/`PartDelete` already carry — a fourth
  spelling of the same four fields would have to be kept in step by hand (research Decision 3). Taking
  the *slot map* is what makes FR-007 fall out: a part the author deleted is absent from the array and
  one she duplicated appears twice, with nothing to implement. Document that the record is a copy
  taken at a moment rather than a link to the zone it came from (FR-009, FR-010).

- [X] T003 [P] Implement `world/ZoneRegistry.fs`: the vocabulary — `type Period = Morning | Evening`
  and `type Light = Day | Night | Exam` as the examples, then `type Place = Classroom of Period |
  Corridor of Light` — with `PlaceEntry = { Name : string; Place : Place }` and `ZoneGroup = { Name :
  string; Places : PlaceEntry list }`; then `byZone : ZoneGroup list`, the reflection walk that
  derives the tree once at type initialisation (`FSharpType.GetUnionCases` over `Place`, then over
  each case's single union-typed field), naming a place with its zone's case name and the variant's,
  and the zone's name alone for a fieldless case (research Decision 2: names come from the same walk
  as the tree rather than a hand-written `match`, which is the departure from `Casting.lookName` and
  is argued there). A case that is neither fieldless nor carries exactly one union-typed field must
  `failwith` rather than be skipped: it has no tree, and skipping would hide a mistyped declaration.
  Then the map — `type Registry = { Templates : Dictionary<Place, ZoneTemplate> }`, `create ()`, and
  `tryTemplate : place:Place -> registry:Registry -> ZoneTemplate option`, sparse with an absent place
  answering `None` (FR-001, FR-002, FR-003; research Decisions 1, 2, 4 and 8).

- [X] T004 Add `ZoneRegistry : ZoneRegistry.Registry` to `state/SharedState.fs` beside `Cast`, and
  initialise it from `ZoneRegistry.create ()` in `SharedState.create`. It goes on the shared state and
  not on `GameplayState` because it is project state that belongs to no zone and must outlive every
  zone (FR-010), which is exactly why the cast binding is there (research Decision 11). The field then
  appears in four record constructions, so update the three test fixtures to match:
  `test/GameplayStateTests.fs`, `test/SelectionTests.fs` and `test/SpawningTests.fs` each gain
  `ZoneRegistry = ZoneRegistry.create ()`. This is the phase's last task because nothing compiles
  until all four sites agree.

**Checkpoint**: `dotnet build` and `dotnet test` are clean, with no behavioural change: the registry
exists, is empty, and nothing reads it yet.

---

## Phase 3: User Story 1 - The game's places, and what each one can look like (Priority: P1) 🎯

**Goal**: The vocabulary is drawable — a node per zone, a row per variant it declares.

**Independent Test**: open the panel and count nodes and rows against the union: every zone appears
once, each shows exactly the variants it declares, a zone with a smaller set shows fewer rows, and a
zone with none shows one. T007 asserts the same thing headlessly.

- [X] T005 [US1] Create `editor/ZoneRegistryPanel.fs` with `draw (shared) (state: byref<GameplayState>)`,
  drawing the tree from `ZoneRegistry.byZone`: a `ImGui.collapsingHeader` per zone, then one row per
  place showing `entry.Name` (`ImGui.text`) and the registered asset's name or `(none)` — read through
  `ZoneRegistry.tryTemplate entry.Place shared.ZoneRegistry` once per row. This task draws the tree
  only; the two controls arrive in T008 and T011. Push the zone's name and then the place's name onto
  ImGui's ID stack around each row from the start, because both buttons' labels will repeat on every
  row and `specs/033-cast-spawn` shipped exactly that collision (research Decision 9). Add the panel's
  `<Compile Include>` line to `fsharp-game.fsproj` after `editor/CastPanel.fs`, and wire it into
  `editor/Editor.fs`: add `"Zone Registry"` to `panelNames` (so Ctrl+Tab reaches it) and
  `do ZoneRegistryPanel.draw shared &state` in `draw` after `CastPanel.draw` (FR-001, FR-002).

- [X] T006 [P] [US1] Add `ImGui::DockBuilderDockWindow("Zone Registry", left);` beside the existing
  `"Cast"` line in `void cimgui_setup_dock_layout()` in `native/cimgui_sdl3.cpp`, and re-run
  `native/build.sh` so `native/libcimgui.so` carries it — the layout is built natively, and the
  gitignored library is the artifact that has to be rebuilt for the panel to dock where
  `docs/EDITOR_DESIGN.md`'s Workspace table puts it (research Decision 10). Independent of T005: the
  window's name is the only coupling, and until this lands the panel simply floats.

- [X] T007 [US1] Create `test/ZoneRegistryTests.fs` and add its `<Compile Include>` line to
  `test/fsharp-game.Tests.fsproj`. Start with the vocabulary assertions: `byZone` lists every zone in
  the union exactly once, every zone has at least one place, no place's name is empty, and no two
  places share a name. This is the check that fails if the reflection walk and the union stop
  agreeing, which is the exposure deriving the tree from the declaration buys (research Decision 2),
  and it is what stands in for the compiler's incomplete-match report that the cast's hand-written
  names get for free (SC-001).

**Checkpoint**: the panel shows the whole vocabulary in the editor, and the vocabulary's shape is
asserted headlessly.

---

## Phase 4: User Story 2 - Register the zone you have authored (Priority: P1)

**Goal**: The active zone's loaded environment can be filed against a place, as a copy.

**Independent Test**: load an environment, customise its parts, register it, then edit the live zone
further and confirm the entry did not change. T009 asserts the same headlessly.

- [X] T008 [US2] Add the **Register** control to each place's row in `editor/ZoneRegistryPanel.fs`:
  `ImGui.sameLine`, then `ImGui.beginDisabled (Option.isNone environment)` around an
  `ImGui.smallButton "Register"`, where `environment` is `(GameplayState.activeZone state).Environment`
  read once for the whole panel so the enabled state and the action cannot disagree (research
  Decision 6). Its body is one line —
  `shared.ZoneRegistry.Templates.[entry.Place] <- ZoneTemplate.ofEnvironment loaded` — with the `None`
  branch raising rather than no-opping, because a disabled widget cannot be activated and reaching it
  means the precondition broke (constitution II). Registering is a direct write: no command, no undo
  step, no scene change (FR-005, FR-006, FR-008, FR-009; research Decision 4).

- [X] T009 [US2] Extend `test/ZoneRegistryTests.fs` with the map's contract and the copy's direction:
  an unregistered place answers `None`; registering one answers it and registering again replaces it;
  writing one place changes no other (FR-003, FR-008, SC-007); and a record taken from an environment
  does not change when that environment is mutated afterwards, nor the other way round (FR-009,
  FR-010, SC-006). Build the environments by hand — a `SlotMap<Prop>` of `Prop` values with
  `Unchecked.defaultof<EnvironmentRender.EnvironmentRender>` renderers, as `test/SpawningTests.fs`
  builds its asset fixtures — so this needs no GPU and no zone.

**Checkpoint**: a place holds the zone the author registered, and nothing about the scene changed when
it was filed.

---

## Phase 5: User Story 3 - Instantiate a registered zone (Priority: P2)

**Goal**: A registered place can be put back into a zone, in one undoable step.

**Independent Test**: register a customised zone, change the live zone into something else entirely,
instantiate the registered place and confirm the environment comes back part for part; then undo and
confirm the intervening state returns. T012 asserts the reproduction headlessly.

- [X] T010 [US3] Change `world/Zone.fs` so that an asset's own part list and a registered record's
  take one path: `buildEnvironment` stops reading everything from the asset and becomes
  `(assetId) (baseEntity) (orientation) (states: PropState array) (epoch)`; a new private
  `assetPartStates` produces an asset's own states, keeping the glTF node-transform decomposition that
  is in `buildEnvironment` today; the parts map's capacity becomes
  `max 512 (uint32 states.Length)`, which keeps today's headroom for `spawnPartBinding`'s additions
  and removes the failure an asset with more than 512 mesh-bearing nodes would hit at `Add`. Then add
  `loadEnvironmentFrom : zone -> world -> assets -> template:ZoneTemplate -> Environment`, which
  mirrors `loadEnvironment` — fresh epoch, build, make it current, return the value so the caller's
  `SetEnvironment` command can retain it and undo can put the previous one back (research Decision 5;
  FR-012, FR-013).

- [X] T011 [US3] Add the **Instantiate** control to each place's row in `editor/ZoneRegistryPanel.fs`:
  `ImGui.sameLine`, then `ImGui.beginDisabled (Option.isNone registered)` around an
  `ImGui.smallButton "Instantiate"` whose body reads `previous` off the active zone, calls
  `Zone.loadEnvironmentFrom`, then `Selection.dropStalePartSelection &state`, then pushes
  `SetEnvironment (previous, Some next)` and assigns the new stack — the Asset Browser's swap, with
  the builder swapped. This is deliberately the *same five lines* rather than a shared function
  (research Decision 7): the swap's natural home, `editor/Spawning.fs`, compiles above
  `editor/Selection.fs` and cannot call the reconcile, and this is the second call site, so the rule
  of three puts the extraction at the third — quote that decision in the code comment so the third
  extracts rather than copies. The `None` branch raises for the reason T008's does (FR-011, FR-013,
  FR-014).

- [X] T012 [US3] Extend `test/ZoneRegistryTests.fs` with the round trip, which is the feature's
  central claim: build a room asset with two or three parts (declared locally here rather than shared
  with `test/SpawningTests.fs`, whose room has a single part — a fixture module arrives at the third
  user), load it into a zone with `Zone.loadEnvironment`, move one part, rename another and delete a
  third, take the record with `ZoneTemplate.ofEnvironment`, load it into a *fresh* zone with
  `Zone.loadEnvironmentFrom`, and assert the asset, the settings and the parts all match — with the
  deleted part still absent and the moved part where it was put. This is FR-012 and SC-002/SC-003, and
  it is the check that a record which silently captured too little would fail (research Decision 12).

**Checkpoint**: register → instantiate reproduces the zone exactly, in one undoable step, and that is
asserted rather than promised.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Whole-feature validation, and the lines this feature makes false.

- [X] T013 Run `dotnet build --no-incremental` and `dotnet test test/fsharp-game.Tests.fsproj` and
  confirm the build is clean with no new warnings and the suite passes **with every existing
  assertion unmodified** (FR-016, SC-008). `--no-incremental` is the build to run: an incremental
  build hides warnings on unchanged files. Record the counts.

- [X] T014 [P] Correct the lines this feature makes false, in the docs the plan lists:
  `docs/EDITOR_DESIGN.md` gains a D-023 for the panel (registering is a direct write since the
  registry outlives every zone; instantiating is an ordinary scene edit, and why the two differ), and
  its D-011 line that names the zone registry among the surfaces wanting a project-scoped stack is
  corrected to record that the answer is not to give it one, for D-022's reason;
  `docs/PROJECT_STRUCTURE.md` gains the two new files in the compile list and the `world/` layout;
  `docs/ROADMAP.md`'s phase 12 records what landed and what is deferred; `docs/ARCHITECTURE.md`'s
  `Zone_Registry` entry says what an entry holds now and that prop templates and splat maps are the
  deferred siblings.

- [X] T015 [P] Confirm the registry cannot reach the live zone or the editor, which is what its
  position in the compile list is for: read `world/ZoneRegistry.fs` and `world/ZoneTemplate.fs` and
  confirm neither names `Zone`, `GameplayState`, `Spawning` or any editor module, and that the only
  write to `Templates` is the panel's one line. Record the reads in this task. Also confirm no
  play-mode path gained work: `ZoneRegistry.byZone` is computed once at type initialisation, and the
  panel draws in edit mode only.

- [ ] T016 Walk interactive scenarios 6–14 in `specs/034-zone-registry/quickstart.md` in the editor
  (with a display): the panel docked in the left node and reachable by Ctrl+Tab, the tree matching the
  vocabulary, registering enabled and disabled in the right states, no undo step from a registration,
  instantiating reproducing the zone, one-step undo, two places staying independent, the registry
  surviving its zone's deletion, and no ImGui ID conflict in the log. **Not run if the agent
  environment cannot be driven interactively** — and if it is not run, say so rather than implying the
  panel's draw path was exercised.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks every user story**.
- **User Stories (Phases 3–5)**: US1 depends on Phase 2; US2 depends on US1 only because it adds a
  control to the panel US1 creates, not on anything it decides; US3 depends on Phase 2 and on the
  panel existing, for the same reason.
- **Polish (Phase 6)**: depends on all of the above.

### Within each phase

- Phase 2: T002 → T003 → T004, strictly, because the map's value type is the record and the state
  field's type is the map.
- Phase 3: T005 → T008 and T005 → T011 (both add controls to the panel T005 creates). T006 ∥ T005,
  T007 ∥ T005.
- Phase 5: T010 → T011 and T010 → T012.
- Phase 6: T013 → T016; T014 ∥ T015, and both can run alongside T013 since they only read and write
  docs.

### Parallel Opportunities

- **T003 ∥ nothing in Phase 2** (it needs T002), but **T006 ∥ T005 ∥ T007** once Phase 2 lands: the
  native dock line, the panel, and the vocabulary test touch three different files.
- **T014 ∥ T015**: docs versus a read.
- A second pair of hands could take US1's panel while US2's test (T009) is written, but T009 extends
  the file T007 creates, so the two test tasks are sequential.

---

## Implementation Strategy

### MVP

**US1 + US2 is the feature's authoring half, and US2 is where the value is.** US1 alone is the map of
where the game can happen; US2 is the act of filing a zone away, which is what the request asked for.
US3 is the same data read back, and it is in scope rather than deferred because it is the only thing
that proves a record holds enough — but it adds no state of its own, and US1 and US2 are independently
demonstrable without it.

1. Complete Phase 1 (the compile list) and Phase 2 (the record, the map, the state field).
2. Complete Phase 3 (US1) — the tree, the docking, and the vocabulary assertion.
3. Complete Phase 4 (US2) — registering, and the map's contract.
4. **STOP and VALIDATE**: `dotnet test`, then `quickstart.md` scenarios 6–10.
5. Complete Phase 5 (US3) — the builder change, the control, and the round trip.
6. **STOP and VALIDATE**: `quickstart.md` scenarios 11–14.

---

## Notes

- [P] tasks touch different files and have no incomplete dependencies.
- **T005 is one change with `editor/Editor.fs`**: a panel no call site draws is dead code, and the
  `panelNames` entry is what makes Ctrl+Tab reach it.
- **The `Zone` builder change is in US3, not in the foundational phase**, deliberately: registering
  needs no `Zone` change at all (`ofEnvironment` reads an `Environment`), so keeping the change with
  the story that needs it is what keeps US1 and US2 independently deliverable.
- **Two decisions here exist because a past feature got them wrong.** The two ID pushes (T005) are
  `specs/033-cast-spawn`'s collision, fixed before it happened rather than in a follow-up commit. The
  duplicated swap (T011) is the rule of three taken seriously rather than a shared function invented
  early, with the extract trigger written down where the third call site will see it.
- **One place the plan changes existing behaviour**, and it is recorded rather than slipped in:
  `buildEnvironment`'s capacity becomes `max 512 parts.Length` (T010). No reachable behaviour changes
  — it only affects an asset with more than 512 mesh-bearing nodes, which cannot be loaded today.
- Verify `dotnet test` passes after each phase; commit per task or logical group.

---

## Outcomes

**Validation (T013)**: `dotnet build --no-incremental` is clean — 0 errors, 0 warnings — and
`dotnet test test/fsharp-game.Tests.fsproj` passes **251/251**, against 242 before this feature: five
vocabulary tests (T007), three map tests (T009) and the round trip (T012). No existing assertion was
modified. `native/libcimgui.so` was rebuilt after T006's dock line; as ever, only
`native/cimgui_sdl3.cpp` is tracked, so the rebuild is a build step rather than a change to review.

**Three deviations from the plan, all recorded rather than absorbed**:

1. **The shipped vocabulary is `ArtRoom of Period | Island`**, where this plan and the spec's
examples said `Classroom of Period | Corridor of Light`. The assets that exist are `artroom` and
`island`, and making one case fieldless means both shapes the design has — a zone that declares
variants and a zone that declares none — are real in the shipped vocabulary and asserted by T007,
instead of one branch of `byZone` being a reflection path no code ever reaches. The *spec* keeps the
request's own words; the design artifacts (plan, research, data-model, contracts) now name what
shipped.
2. **T012's round trip lives in `test/ZoneTests.fs`, not in `test/ZoneRegistryTests.fs`** where the
task put it. That file already owns a headless fixture that drives the real
`loadEnvironment`/`setEnvironment` lifecycle, so the round trip needed no fixture of its own — where a
third hand-built copy of one is exactly what the rule of three refuses. The three-part `hall` asset it
needs (so one part can be moved, one renamed and one deleted) is built from that fixture's existing
primitives, and the test sits with the other environment-lifecycle tests. `research.md` Decision 12
and `quickstart.md` scenario 2 were corrected to match.
3. **One assertion was wrong, not the code**: the copy test first expected the mutated position on the
part that was *not* replaced. The fixture's second part sits at 1, and the assertion now says so.

**What is not verified (T016)**: the interactive walk did not run — the editor needs a person to click
through the main menu before any panel draws, and nothing here can drive it. So the panel's draw path,
the two controls' enabled and disabled states, and the native dock line remain unexercised by
automation. Everything those scenarios would check that is checkable without a frame was checked
instead: the vocabulary and the map (T007, T009), the round trip through the real load path (T012),
the structure — that neither new module can reach the live zone or the editor (T015) — and the suite
(T013).

---

## Phase 7: Convergence

Appended by `/speckit-converge` after the first implementation pass. Nothing here is new behaviour: two
are lines of documentation the plan's corrections list did not reach, and one is an assertion the spec
asks for and no test makes.

- [X] T017 Correct the Zone Registry entry in `docs/EDITOR_DESIGN.md`'s Workspace section, which still
  describes the surface as "zone list; selecting one shows its details in the Inspector". The shipped
  panel is a tree of the game's places that registers and instantiates **in place**, and it involves the
  Inspector in neither action — the same posture D-022 argues for the cast panel, and what D-023 records
  for this one. Read the section before editing: the neighbouring sub-editor lines describe panels that
  do not exist yet and are still accurate as plans. per `FR-005`, `FR-011` and the plan's docs list
  (contradicts).

- [X] T018 Correct the module map in `docs/EDITOR_DESIGN.md` (`Module map`): the row naming
  `ScenarioEditor` / `ZoneEditor` names a module that does not exist. The panel shipped as
  `ZoneRegistryPanel` (`editor/ZoneRegistryPanel.fs`), so replace that half of the row, keeping the
  scenario editor's planned status as it is. per `plan: Project Structure` (partial).

- [X] T019 Extend ``a registered zone loads back part for part, deletions included`` in
  `test/ZoneTests.fs` with the duplicate case `SC-003` asks for: after the rename and the delete,
  duplicate a part through the real path (`Spawning.duplicate` needs a `SharedState`, so the cheaper
  route is `Zone.spawnPartBinding` with the source part's binding, as `Spawning.duplicate` does for a
  part), then assert the record carries both copies — the source in its own state and the copy in the
  state it was given — and that the count is the live parts count. The behaviour is already correct
  (the record walks the live slot map); this is the assertion that says so rather than a change to
  `ofEnvironment`. per `SC-003` (partial).

### Outcomes (Phase 7)

All three landed. `dotnet build --no-incremental` is clean and `dotnet test` passes **251/251** — the
same count, because T019 extended an existing test rather than adding one.

- **T017**: the Workspace entry now describes the tree and the two controls, and D-023 is cited. Doing
  it surfaced **a fourth line the converge run missed, in the same paragraph**: the text still called
  Cast "the one surface that edits in place rather than through the Inspector", which this panel's
  shipping makes false. It now names both surfaces and keeps D-022's reason for why a slot is not a
  selectable item. Recorded here rather than as a new task, because it is the same finding in the same
  paragraph and T017 is the task that had to read it.
- **T018**: `ZoneEditor` in the module map is `ZoneRegistryPanel`.
- **T019**: the round trip now duplicates a part through the real part path as well as deleting one,
  and asserts the record carries both — the source where it was moved to, the copy where it was put and
  drawing the same piece of the asset. The test is renamed to `deletions and duplicates included`.
  Writing it turned up one thing worth recording: `open ZoneTemplate` in `test/ZoneTests.fs` makes the
  `Base` field ambiguous across `PropState`, `TriggerState`, `EnvironmentPart` and `Part` (FS3566), so
  the file keeps its opens as they were and the two bindings are annotated `: PropState` instead. The
  `open` was only ever needed in `test/ZoneRegistryTests.fs`, where a bare `ZoneTemplate` type
  annotation is written.
