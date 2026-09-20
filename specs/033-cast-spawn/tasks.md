---

description: "Task list for the Cast Spawn feature"
---

# Tasks: Spawning a Cast Member from the Cast Panel

**Input**: Design documents from `/specs/033-cast-spawn/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: **No new test file, deliberately** — see `research.md` Decision 5. What the control decides is `Casting.tryAsset` and `Roster.characterName`, both already asserted, and the spawn it calls is `Zone.spawnActor` + `ActorCreate`, whose undo `test/SpawningTests.fs` already covers. What remains is ImGui glue no headless test can reach, so the validation is `quickstart.md`'s interactive walk plus the existing suite passing unmodified (SC-006).

**Organization**: Tasks are grouped by user story. Phase 2 moves the one literal both spawn paths need, Phase 3 adds the control (US1), and Phase 4 verifies the actor is ordinary and the two paths cannot drift (US2).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)
- Include exact file paths in descriptions

## Path Conventions

- F# single project at the repository root; modules live in domain clusters (`editor/`, `world/`, `state/`, `render/`).
- Compile order is defined by `fsharp-game.fsproj`. **No file is added or removed**, so neither `.fsproj` changes.
- Tests live in `test/` (one `<Module>Tests.fs` per module).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the feature needs no build change.

- [X] T001 Verify that nothing is added to either project file: this feature adds no module (the control lives in `editor/CastPanel.fs`, the moved literal in `editor/Spawning.fs`), and that `render/ImGui.fs` already binds every widget the control needs — `igSameLine`, `igSmallButton`, `igBeginDisabled`, `igEndDisabled` — so there is no new `DllImport` and no native rebuild.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: One entity literal, used by every spawn path. Both user stories rest on it.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 Move the private `baseEntity` from `editor/AssetBrowser.fs` to `editor/Spawning.fs` as a public `baseEntity : name:string -> Entity`, and point `AssetBrowser`'s two call sites (`spawnActor`, `spawnProp`) at it. It is the entity a spawned asset becomes — identity transform and the same flags — so it belongs in the module whose comment already claims to be the single place the editor creates entities. Its third call site arrives in T003, which is what puts it past the rule of three. No behaviour change: the literal is unchanged and both of `AssetBrowser`'s spawns must produce byte-identical entities.

**Checkpoint**: `dotnet build` is clean and the Asset Browser spawns exactly what it spawned before.

---

## Phase 3: User Story 1 - Put a cast member's model into the scene (Priority: P1) 🎯

**Goal**: A bound slot places its actor, named for the member, in one undoable step.

**Independent Test**: bind an asset to a member's look, activate the control, and confirm an actor appears with that asset and the member's case name; undo and confirm it is gone and the binding is untouched.

- [X] T003 [US1] Add the spawn control to each slot row in `editor/CastPanel.fs`, and take the gameplay state in `draw`: after the row's combo, `ImGui.sameLine` then `ImGui.beginDisabled (Option.isNone bound)` around an `ImGui.smallButton "Spawn"` — so an unassigned slot draws the control inert rather than hiding it (FR-005) — whose activation calls `Spawning.spawnActor shared &state (Spawning.baseEntity (Roster.characterName character)) assetId`. Read `Casting.tryAsset` once per row so the preview and the control cannot disagree, and use the button's result rather than a second binding read. The member's name is already pushed onto ImGui's ID stack around the row, which is what keeps several `Spawn` buttons distinct (FR-001 to FR-004, FR-006; research Decisions 1, 2 and 4) (depends on T002).
- [X] T004 [US1] Pass `&state` to `CastPanel.draw` in `editor/Editor.fs` (research Decision 2) (depends on T003). — T003 and T004 land as one change: the signature change does not compile until the call site moves with it.

**Checkpoint**: a bound slot spawns its actor, named for the member, selected, and undoable in one step.

---

## Phase 4: User Story 2 - The new actor is an ordinary actor (Priority: P2)

**Goal**: What is placed is what the Asset Browser places, with a name from the cast and nothing else.

**Independent Test**: spawn the same asset from the Asset Browser and from the Cast panel and compare the two actors: same kind, asset and placement, differing only in name.

- [X] T005 [US2] Confirm the created actor carries no cast identity and that the two paths cannot drift (FR-007, FR-009, SC-005): read `state/Command.fs` and `world/Entity.fs` and confirm no field, flag or command was added for the cast, and read `editor/AssetBrowser.fs` and `editor/CastPanel.fs` and confirm both build their entity from `Spawning.baseEntity` — the single copy. Record both reads in this task. **Outcome**: `grep` finds no `Character` and no `Casting` in `state/Command.fs` or `world/Entity.fs`, so the actor learns nothing about the cast and no command was added. `baseEntity` has exactly one definition (`editor/Spawning.fs:17`) and three call sites (`AssetBrowser`'s actor and prop, `CastPanel`'s actor), so the two spawn paths cannot drift. One correction to this feature's own artifacts, found while doing it: 032's *contract* already specified the panel's `state` parameter and it was 032's *implementation* that took `shared` alone, so the signature needed no correction — the false line was the contract's "mutates nothing outside `shared.Cast`, and touches no undo stack", now superseded there.

**Checkpoint**: no data was added anywhere, and one literal is the only thing both spawn paths share.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Whole-feature validation and the lines this feature makes false.

- [X] T006 Run `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj` and confirm the build is clean and the suite passes **with every existing assertion unmodified** (FR-008, SC-006). **Outcome**: `dotnet build --no-incremental` is clean — 0 errors, no new warnings, and the same two pre-existing `Engine.fs` native-pointer ones — and `dotnet test test/fsharp-game.Tests.fsproj` passes **242/242**, unchanged in count and in assertions, because this feature adds no command and no data.
- [ ] T007 Walk interactive scenarios 3–10 in `specs/033-cast-spawn/quickstart.md` in the editor (with a display): a bound slot spawning its model, the member's name on the actor, the inert control on an unassigned slot, one-step undo, the binding unchanged in both directions, two looks giving two controls, and the two spawn entry points agreeing. **Not run if the agent environment cannot be driven interactively.** — **Not run**: the editor needs a person to click through the main menu before any panel draws, and nothing here can drive it. Everything the walk would check that is checkable without a frame has been checked instead — the suite (T006), the single literal (T005), and the widget names resolving (`igSameLine`, `igSmallButton`, `igBeginDisabled`/`igEndDisabled` are bound and unchanged, which is why no `DllImport` was added). The control's own draw path stays unexercised until someone walks it.
- [X] T008 [P] Correct the two lines this feature makes false (contracts §"Corrections to `specs/032-cast-binding`"): `specs/032-cast-binding/contracts/cast-binding.md`'s `CastPanel` `val draw` and its "takes `shared` alone" note, and `specs/032-cast-binding/data-model.md`'s "The panel's data, and what it does not hold" — keeping the assignment reasoning, which is what those paragraphs are about, and stating the new signature. Add a sentence to `docs/EDITOR_DESIGN.md` D-022 recording the panel's second job and why placing is undoable while assigning is not.
- [X] T009 [P] Confirm no play-mode path gained work (SC-002's cost side) and that the control added no binding: the panel draws in edit mode only, the control adds one `tryAsset` lookup and one button per slot row per frame, and `render/ImGui.fs` gained no `DllImport`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks both user stories**.
- **User Stories (Phases 3–4)**: US1 depends on Phase 2; US2 reads the result of Phase 3 rather than depending on it, since what it verifies is that nothing was added.
- **Polish (Phase 5)**: depends on all of the above.

### Within each phase

- Phase 3: T003 → T004, necessarily together.
- Phase 5: T008 ∥ T009; T006 → T007.

### Parallel Opportunities

- T008 ∥ T009 (different files).
- T005 can run alongside T006, since it only reads.

---

## Implementation Strategy

### MVP

US1 alone is the whole feature: a bound slot places its actor. US2 adds no code — it is the
verification that the actor is ordinary and that `baseEntity` is the only shared thing between the
two spawn paths, which is exactly the property a future change would break silently.

1. Complete Phase 1 (no build change) and Phase 2 (one literal, three call sites).
2. Complete Phase 3 (US1) — the control and its wiring.
3. Complete Phase 4 (US2) — the reads that say nothing was added.
4. **STOP and VALIDATE**: run the suite and walk `quickstart.md` scenarios 3–10.

---

## Notes

- [P] tasks touch different files and have no incomplete dependencies.
- **T003 and T004 are one change**: taking the gameplay state in `draw` does not compile until
  `Editor.draw` passes it, so they land together rather than at a green checkpoint in between.
- **No new test, and the reason is recorded rather than assumed** (research Decision 5): the two
  facts the control decides are already asserted, the spawn it calls is already covered including
  its undo, and the remainder is ImGui glue. Inventing a seam so a test could reach it is the
  just-in-case structure the constitution's minimalism principle refuses.
- **What this feature retires**: `specs/032-cast-binding` chose a panel signature that needed only
  project state, and reasoned from it that the panel never touches the scene. T008 corrects that
  record; the *assignment* half of the reasoning is still exactly right.
- **Placement is inherited, not decided** (research Decision 7): the actor lands where the Asset
  Browser's lands. Where a spawned actor *should* go is a separate question, and answering it in one
  panel is how the two spawn paths would start to differ.
- Verify `dotnet test` passes after each phase; commit per task or logical group.
