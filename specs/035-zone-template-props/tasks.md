---

description: "Task list for props in a zone's registered contents"
---

# Tasks: A Registered Zone's Placed Props

**Input**: Design documents from `/specs/035-zone-template-props/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: **No new test file** — the two existing files whose fixtures already drive these paths are
extended, which is `research.md` Decision 10. `test/ZoneTests.fs` covers capture (it already drives
`loadEnvironment`/`setEnvironment` headlessly with a hand-built asset) and `test/SpawningTests.fs`
covers the replacement (it is the only file with a `SharedState`/`GameplayState` fixture, which the
editor path needs). `Command.Batch` is exercised through the instantiation that needs it rather than in
isolation, the way `LinkSpawn` is exercised through the extend gesture.

**Organization**: Tasks are grouped by user story. Phase 1 confirms the feature needs no build change,
Phase 2 makes the record whole — the field, the capture and the call site that reads them, which cannot
land apart — Phase 3 is US1's assertions, Phase 4 is US2 (the compound command, the replacement, its
assertions) and Phase 5 is validation and the docs.

**Why Phase 2 is foundational and not part of US1**: the record's new field, the function that fills it
and the panel line that reads it are one change in F# — adding the field changes the constructor's
signature, which the call site must follow in the same compile. That is infrastructure both stories
rest on (`captureProps` is US1's recorder *and* US2's way of reading what it is about to replace), so
it lands first; what US1 then *is* is the verification the spec asks of it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)
- Include exact file paths in descriptions

## Path Conventions

- F# single project at the repository root; modules live in domain clusters (`world/`, `state/`,
  `editor/`, `render/`).
- Compile order is defined by `fsharp-game.fsproj`. **No file is added and none moves**, so neither
  `.fsproj` changes and `world/ZoneTemplate.fs` keeps its place above `world/Zone.fs`.
- Tests live in `test/` (one `<Module>Tests.fs` per module).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the feature needs no build or binding change.

- [X] T001 Verify that nothing is added to either project file and that `render/ImGui.fs` gains no
  binding: this feature changes what the panel's existing Instantiate control *does*, adds a field to a
  record that already exists, adds one command case, and adds no module — so there is no new
  `DllImport`, no native change, and `native/libcimgui.so` is untouched.

**Checkpoint**: nothing about the build or the native library changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The record gains the props, the zone can be asked for them, and the call site that reads
the record keeps compiling. Both stories rest on this.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 Add `Props : PropState array` to `ZoneTemplate` in `world/ZoneTemplate.fs`, beside `Parts`,
  and make `ofEnvironment` take the props as its second argument. The constructor stays pure and
  zone-free — it cannot read a zone, because this file is compiled above `world/Zone.fs` and the loader
  takes a record as its argument — and keeping it that way is what lets the registry's own tests build
  a record without a scene (`research.md` Decision 3). Then add to `world/Zone.fs`:
  `captureProps : Zone -> (EntityRef * PropState) array`, the zone's live props each with the ref it
  stands at, read through the slot map's `IteriAlive` as the parts are; and
  `captureTemplate : Zone -> ZoneTemplate option`, which pairs the loaded environment with those props
  and answers `None` when there is none — FR-007 expressed in the return type, and the same condition
  that disables the panel's Register control. Both take their place beside `captureActor`/
  `captureProp`/`capturePart`, which is the family they belong to (FR-001, FR-002; research Decisions 3
  and 4).

- [X] T003 Point the call sites the new field breaks at the capture, so the tree is green and
  registration records the props rather than nothing: `editor/ZoneRegistryPanel.fs`'s Register control
  captures the whole entry — `match Zone.captureTemplate (GameplayState.activeZone state) with Some
  template -> shared.ZoneRegistry.Templates.[entry.Place] <- template` — and keeps its enabled state on
  the cheap question, so nothing is captured per frame (`research.md` Decision 11); and the two test
  call sites that construct a record (`test/ZoneTests.fs`'s round trip and `test/ZoneRegistryTests.fs`'s
  map tests) pass the props they mean, an empty array where the test is about the map. Registering
  stays a direct write with no command and no scene change (FR-008) (depends on T002).

**Checkpoint**: `dotnet build` and `dotnet test` are green, and a place registered now records the
zone's props — with nothing yet reading them.

---

## Phase 3: User Story 1 - A registered zone carries its props (Priority: P1) 🎯

**Goal**: What a registration takes from a zone's props is asserted, over the arrangements the spec
names.

**Independent Test**: place several props, move one, rename another, delete a third and duplicate a
fourth, capture the entry, and inspect it: every prop left standing is there with its asset, name and
placement, the deleted one is absent and the duplicated one appears twice.

- [X] T004 [US1] Extend `test/ZoneTests.fs` with what a record takes from a zone's props, placed beside
  its existing environment round trip: a zone with props placed from `propAssets ()`'s whole-model prop
  asset, one moved, one renamed, one deleted through `PartDelete`'s sibling path for a prop
  (`PropDelete` through `apply`) and one duplicated through `Zone.spawnPropBinding`; assert the entry
  holds exactly the props left standing, each naming the asset it was placed from, with the moved one at
  its new placement and the renamed one under its new name. Also assert the copy direction — capturing
  does not disturb the zone, and editing the zone afterwards does not reach the entry (FR-008) — and
  that an entry's props all name an asset while its parts all name a piece of the environment, so
  instantiating cannot draw one as the other (FR-003). A zone with no props records none, which is a
  complete entry rather than a partial one (SC-001).

**Checkpoint**: a registration's props are asserted to be the zone's props, in every arrangement the
spec names.

---

## Phase 4: User Story 2 - Instantiating a zone replaces its props too (Priority: P1)

**Goal**: An instantiation replaces the zone's props with the entry's, and one reversal puts back
everything it replaced.

**Independent Test**: register a zone with props, change the zone into a different environment with
different props, instantiate the place, and confirm the props are exactly the registered ones; then undo
once and confirm both the environment and the props that were there come back — at the refs those props
had.

- [X] T005 [US2] Add `| Batch of Commands : Command list` to `state/Command.fs`, with `apply` walking
  the list forwards and `undo` walking it backwards, over commands that already exist. One command is
  still one entry on the stack and `UndoStack`'s shape does not move. This is the whole mechanism by
  which one gesture with `1 + N + M` mutations becomes one step (FR-005; research Decision 1): a
  purpose-built payload would be a `Batch` that cannot be used for anything else, and grouping on the
  stack would change the type every consumer touches.

- [X] T006 [US2] Add `instantiateZone` to `editor/Spawning.fs` — the module whose header claims the
  single place the editor creates entities, which is why an instantiation that creates props is not a
  panel's work (research Decision 5). In order: capture the props being replaced with `Zone.captureProps`
  and remove them by *applying the very `PropDelete` commands it will record*; load the environment from
  the entry's environment half with `Zone.loadEnvironmentFrom`; place the entry's props with
  `Zone.spawnPropBinding`, keeping each new ref; then push **one** `Batch` carrying the deletes, the
  `SetEnvironment` and the creates, in that order. Require each recorded prop to name an asset and raise
  otherwise — a props array holding something shaped like a part is a broken registration, and the
  `None` shape would silently build an environment-part renderer instead (FR-003; research Decision 8).
  It selects nothing, unlike every other function here, because an instantiation may place none, one or
  twenty props (research Decision 7). Then point the panel's Instantiate branch at it in
  `editor/ZoneRegistryPanel.fs`, followed by `Selection.dropStalePartSelection &state` — the reconcile
  the swap no longer does itself, and the same step `Editor.undoStep`/`redoStep` already take (FR-004,
  FR-005, FR-006) (depends on T002, T005).

- [X] T007 [US2] Extend `test/SpawningTests.fs` with the replacement, through `Spawning.instantiateZone`
  over its `headlessAssets` fixture: register a zone's contents, change the zone into a different
  environment with different props, instantiate, and assert the zone's props are exactly the entry's —
  same count, assets, names and placements, and none of the ones it held before — including the case
  where the entry records no props at all, which clears the zone's rather than leaving them (FR-004,
  SC-002, SC-003). Then assert the step: **one** undo restores the previous environment *and* the
  previous props **at the refs those props had before**, not merely with equal values, because a
  reversal that placed them as copies at fresh slots would pass a values-only comparison and still break
  every ref the author held; do it with more than one prop so a step-per-prop implementation cannot pass
  (FR-005, SC-004). And assert the instantiation leaves the entry, the second place, and the zone's
  actors, waypoints and triggers alone (FR-006) (depends on T006).

**Checkpoint**: an instantiation puts the registered room back, props included, and one Ctrl+Z restores
what it replaced.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Whole-feature validation, and the lines this feature makes false.

- [X] T008 Run `dotnet build --no-incremental` and `dotnet test test/fsharp-game.Tests.fsproj` and
  confirm the build is clean with no new warnings and the suite passes **with every assertion
  `specs/034-zone-registry` already made unmodified** (FR-009, SC-006). `--no-incremental` is the build
  to run: an incremental build hides warnings on unchanged files. Record the counts.

- [X] T009 [P] Correct the lines this feature makes false in the repository docs.
  `docs/EDITOR_DESIGN.md`: D-023's "it does not yet hold placed props, waypoints, triggers or splat
  maps" now holds props, so it names what is still deferred; and D-011's command-model section gains the
  compound command, because "one gesture, several mutations" is a shape the model did not previously
  have a name for and a reader of that section would otherwise not find it.
  `docs/ARCHITECTURE.md`: the `Zone_Registry` entry's `prop_template` is no longer deferred.
  `docs/ROADMAP.md`: phase 12's note listing props among the deferrals.

- [X] T010 [P] Supersede the two `specs/034-zone-registry` lines this feature falsifies, rather than
  leaving them claiming something that did not happen: its `contracts/zone-registry.md`'s Instantiate
  block, which showed the environment swap inline in the panel with a comment about the compile order,
  and its `research.md` Decision 7, which recorded that the second call site would extract at the third
  — it moved instead, and the two remaining sites build different environments (research Decision 6,
  which also restates the trigger).

- [X] T011 [P] Confirm the structural properties this feature claims, reading rather than assuming:
  `editor/ZoneRegistryPanel.fs` captures only inside the Register control's activation and never per
  frame; `editor/AssetBrowser.fs` is unchanged in behaviour and shape (its environment load is a
  different question from installing a record); and `state/Command.fs`'s `UndoStack` and its `Applied :
  Command list` are untouched, so `Batch` is one entry like everything else. Record the reads.

- [ ] T012 Walk interactive scenarios 9–13 in `specs/035-zone-template-props/quickstart.md` in the
  editor (with a display): registering a room takes its props, instantiating puts the room back with
  them, one Ctrl+Z restores the previous environment and props whatever the furniture count, a prop
  that was selected resolves again after the undo, and an actor, a waypoint and a trigger in the zone
  are untouched. **Not run if the agent environment cannot be driven interactively** — and if it is not
  run, say so rather than implying the panel's draw path was exercised.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks every user story**.
- **User Stories (Phases 3–4)**: US1 depends on Phase 2 and nothing else; US2 depends on Phase 2, and
  its three tasks are strictly ordered (the command, then the replacement, then its assertions).
- **Polish (Phase 5)**: depends on all of the above.

### Within each phase

- Phase 2: T002 → T003, necessarily: the field changes the constructor's signature, and the call site
  has to follow in the same compile.
- Phase 4: T005 → T006 → T007.
- Phase 5: T008 → T012; T009 ∥ T010 ∥ T011, and all three can run alongside T008 since they only read
  and write docs.

### Parallel Opportunities

- **T009 ∥ T010 ∥ T011** in the polish phase: three different files, no dependencies.
- US1 (T004) can be written while US2's `Batch` (T005) is added — different files, and T004 does not
  read the command at all.
- T005 ∥ nothing else inside US2: T006 needs it.

---

## Implementation Strategy

### MVP

**Both stories are the feature.** US1 is what an entry records and US2 is what reading it does; the
first without the second is a larger file, and the second without the first has nothing to read. They
are split because each has an independent test the spec names, not because either can ship alone.

1. Complete Phase 1 (no build change) and Phase 2 (the record is whole and registration reads it).
2. Complete Phase 3 (US1) — the capture, asserted.
3. Complete Phase 4 (US2) — the compound command, the replacement, and the assertions that one step
   restores what it replaced, identities included.
4. **STOP and VALIDATE**: `dotnet test`, then `quickstart.md` scenarios 9–13.

---

## Notes

- [P] tasks touch different files and have no incomplete dependencies.
- **T002 and T003 are one build, not two**: the field changes the constructor, and the call site must
  move with it or nothing compiles — the same shape as 033's T003/T004.
- **The compound command lands in US2, not in the foundational phase**, for the reason 034 put its
  `Zone` builder change in US3: nothing in US1 needs it, so keeping it with the story that does is what
  keeps the phases independently checkable.
- **The asserted detail most likely to be got wrong** is T007's: the previous props must come back at
  the refs they had, not as copies at fresh slots. Both pass a values-only comparison, and only the
  refs are what an author's selection, drag or recorded command depends on. That is why the task says to
  assert the refs.
- **The panel gets shorter**, which is worth noticing in review: five lines of environment swap leave
  it for `editor/Spawning.fs`, and what stays is a call and a reconcile.
- Verify `dotnet test` passes after each phase; commit per task or logical group.

---

## Outcomes

**Validation (T008)**: `dotnet build --no-incremental` is clean — 0 errors, 0 warnings — and
`dotnet test test/fsharp-game.Tests.fsproj` passes **255/255**, against 251 before this feature: two
capture assertions (T004) and two replacement assertions (T007). No existing assertion was modified.

**Two assertions were wrong while the code was right**, and both are worth recording because a
values-only test would not have caught either:

1. **The props come back in a different order than they were captured in.** The first version of T007's
test asserted the whole record equal after an instantiation and failed on the props array order: the
setup had freed two slots, and the free list is LIFO, so the entry's first prop landed in the second
one's old slot. The spec already says the order props come back in is not part of the record, so the
assertion was too strong rather than the code being wrong — it now compares the whole record with the
props sorted by name (`ignoringOrder`), which is what "the zone records what it recorded" actually
means. **Left as-is deliberately**: `captureProps` reads slot order, and sorting it would need a
tie-break for props that share a name, which props may.
2. **The props a *setup* removed are not an instantiation's to restore.** The first version asserted the
original `lectern` and `books` refs resolve again after undo. They cannot: the setup removed them by
hand, outside any command, so nothing recorded their removal. What undo restores is the prop that stood
there when the instantiation ran — the `desk` — and the test now asserts that, at its own ref, after
renaming it so the assertion is about identity as well as presence.

**What is not verified (T012)**: the interactive walk did not run — the editor needs a person to click
through the main menu before any panel draws, and nothing here can drive it. The panel's two lines
(registering the whole entry, and instantiating it followed by the reconcile) remain unexercised by
automation. Everything checkable without a frame was: the capture, the replacement, the one-step undo,
and the structural reads in T011 — the capture happens only inside the Register control's activation,
`editor/AssetBrowser.fs` is untouched, and `state/Command.fs`'s undo stack is unmodified.
