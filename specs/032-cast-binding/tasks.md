---

description: "Task list for the Cast and Binding feature"
---

# Tasks: The Cast — a Roster Hoisted from the Engine and Bound to Actor Assets

**Input**: Design documents from `/specs/032-cast-binding/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — the plan and `quickstart.md` specify xUnit coverage: a new `test/RosterTests.fs` (the roster facts, moved out of `NarrativeVocabularyTests`) and a new `test/CastingTests.fs` (the binding), with the narrative suite's references updated. Everything is headless except the editor walk.

**Organization**: Tasks are grouped by user story. Phases 1 and 2 land the hoist in two steps — the new declaration is created first, then the old one is deleted so the compiler enumerates every site — which keeps the tree green at both checkpoints. Phase 3 **proves** US1's single declaration rather than adding it, Phase 4 adds the binding and the panel (US2), and Phase 5 verifies US3's claim that the engine still cannot reach the game.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- F# single project at the repository root; modules live in domain clusters (`cast/`, `narrative/`, `core/`, `physics/`, `render/`, `world/`, `state/`, `editor/`, `screen/`).
- Compile order is defined by `fsharp-game.fsproj`. This feature adds `cast/Roster.fs` (first, ahead of `narrative/`), `world/Casting.fs`, and `editor/CastPanel.fs`; `test/fsharp-game.Tests.fsproj` gains `RosterTests.fs` and `CastingTests.fs`.
- Tests live in `test/` (one `<Module>Tests.fs` per module).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the hoisted layer and put it first in the compile list, which is the one structural change everything else depends on.

- [X] T001 Create `cast/Roster.fs` holding `Character` (fieldless, closed, twenty cases), `characterName`, and `characters`, moved out of `narrative/Vocabulary.fs` verbatim in behaviour with the doc comment rewritten to say why it is here; register it as the **first** `<Compile>` entry in `fsharp-game.fsproj` with a comment stating the rule it exists to hold (the engine must see the roster and must not see the game). `narrative/Vocabulary.fs` still declares its own copies at this point, so the roster is transiently duplicated and the tree compiles; `cast/Roster.fs` is unreferenced until T002. **The layer rule**: `cast/Roster.fs` may not reference `Casting`, `Assets`, `Zone`, or any render or physics module (FR-001, FR-004, research Decisions 1 and 8).

**Checkpoint**: `dotnet build` is clean with the roster declared twice and consumed nowhere.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Delete the engine's copy and let the compiler enumerate every site that named it. Every user story reads the result of this phase.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 Delete `Character`, `characterName`, and `characters` from `narrative/Vocabulary.fs`; add `open Roster` to the narrative files the compiler reports as naming a character, and change the four `Vocabulary.characterName` sites in `narrative/Canonical.fs` to `Roster.characterName`. Leave `Trait`, `Event`, `Fact`, `FactKind`, `traitName`, `factParticipants`, `factKind`, and `sortParticipants` where they are, and leave `narrative/Units/*` unchanged. **No alias or re-export is left behind**: deleting the members is what makes the compiler name every site, so an alias would hide exactly the call sites this task exists to find (FR-001, FR-002, research Decision 8) (depends on T001).
- [X] T003 [P] Move the roster facts out of `test/NarrativeVocabularyTests.fs` into a new `test/RosterTests.fs`, and register it in `test/fsharp-game.Tests.fsproj`: the roster enumerates every case exactly once in declaration order, and `characterName` renders each case. `NarrativeVocabularyTests.fs` keeps only the vocabulary's own facts (traits, argument order, participant selection) (depends on T002).
- [X] T004 [P] Update the roster references the compiler reports in `test/NarrativeHarnessTests.fs` and `test/NarrativePerformanceTests.fs` (`Vocabulary.characters` → `Roster.characters`), leaving every assertion unmodified (depends on T002).

**Checkpoint**: `dotnet build` is clean, the roster is declared exactly once, and the narrative suite passes with **every assertion unmodified** — the engine behaves as though nothing moved.

---

## Phase 3: User Story 1 - A cast declared once, and consumed by the engine (Priority: P1) 🎯

**Goal**: Prove there is one declaration of the cast, that both the engine and the game read it, and that adding a member reports every place that must account for it.

**Independent Test**: read the solution and confirm exactly one file declares the member names; add a member and confirm the build reports what must account for it while the game's cast code needs no edit.

**Note**: Phases 1 and 2 move the roster, so this phase is where the single-declaration claim is *proved* and encoded rather than added.

- [X] T005 [US1] Verify the single-declaration claim (FR-001, SC-001): confirm `cast/Roster.fs` is the only file in the solution that names the cast members, that `narrative/Vocabulary.fs` declares none, and that no module carries an alias, a list, or a reflection table of them. Record the read in this task. **Outcome**: exactly one declaration, `cast/Roster.fs:29`; no `Roster.Character` alias or re-export anywhere in the app; and `cast/Roster.fs` is the only file naming a member. `narrative/Vocabulary.fs` still says `Character` 17 times, all of them uses in the event and fact constructs.
- [X] T006 [US1] Run the member-addition experiment (FR-003, SC-002): add a throwaway case to `Roster.Character` in `cast/Roster.fs`, confirm the build reports every match over the roster that must account for it — the roster's render match in `cast/Roster.fs`, and any other total match over the roster — while `world/Casting.fs`, `editor/CastPanel.fs`, and `Roster.characters` need no edit, record the exact outcome here, then remove the case. **Outcome**: exactly one report — the `characterName` match in `cast/Roster.fs`, as warning FS0025 (incomplete pattern matches) naming the added case. Nothing else moved: `Roster.characters` reflects the type, so the future panel tree and every test needed no edit, and no compile-list entry changed. Note *how* it reports: as a warning, so a case added and left unnamed still builds — the report is complete but not fatal. The case was then removed and the suite re-run green.
- [X] T007 [P] [US1] Confirm the engine's output is unchanged by the move (FR-012, SC-006): `test/Narrative{Commit,Projection,Derivation,RuleSet,Harness,Performance}Tests.fs` pass in full with every assertion unmodified, including the canonical-rendering guard in `test/NarrativePerformanceTests.fs` that keeps character rendering off the reflection path (research Decision 3). **Outcome**: `dotnet test test/fsharp-game.Tests.fsproj` passes 241/241 (240 before this feature). Every narrative assertion is unmodified apart from the roster fact, which moved to `test/RosterTests.fs` and became two facts there.

**Checkpoint**: US1 is proven — one declaration, consumed by the engine and available to the game, with the engine's behaviour untouched.

---

## Phase 4: User Story 2 - An author binds each character and look to an actor asset (Priority: P1)

**Goal**: The look vocabulary, the binding, and the Cast panel that edits it — the feature's authoring value.

**Independent Test**: assign two different assets to two slots of one member and one asset to a second member, confirm each slot resolves to what was assigned, then clear one and confirm it resolves to nothing.

- [X] T008 [US2] Add `world/Casting.fs` and register it in `fsharp-game.fsproj` directly after `world/Assets.fs` (research Decisions 3, 4, 9): `[<RequireQualifiedAccess>] type Look = SchoolUniform | Casual`; `lookName` as a match over the cases; `allLooks : Look list` enumerated from the type once, as `Roster.characters` is; `[<Struct>] type Slot = { Character : Roster.Character; Look : Look }`; `type Cast = { Bindings : Dictionary<Slot, string> }`; and `tryAsset` answering the absent case. No `create`, `assign`, or `clear` — each would have one call site.
- [X] T009 [US2] Add `Cast : Casting.Cast` to `state/SharedState.fs` beside `Assets`, constructed empty in `SharedState.create` (research Decision 5) (depends on T008).
- [X] T010 [P] [US2] Add `test/CastingTests.fs` and register it in `test/fsharp-game.Tests.fsproj`: a slot resolves to what was assigned; an unassigned slot answers `None`; clearing an assigned slot answers `None` again; a member may hold one look and not another and the unassigned slot leaves the assigned one alone; two members may share one asset and neither assignment is disturbed; and every `Roster.characters` × `Casting.allLooks` slot can be assigned, resolved, and cleared (FR-006, FR-007, FR-010, SC-003) (depends on T008).
- [X] T011 [US2] Add the five widget bindings to `render/ImGui.fs` — `igPushID_Str`, `igPopID`, `igBeginCombo`, `igEndCombo`, `igSelectable_Bool` — as `DllImport`s matching the `ig*` wrapper signatures, all of which are present in the shipped `native/libcimgui.so` (verified with `nm -D`, research Decision 6). Reuse the already-bound `igCollapsingHeader_TreeNodeFlags`, `igText`, `igSameLine`, and `ImVec2`; `igTreeNodeEx_Str`/`igTreePop` are available and deliberately not used.
- [X] T012 [US2] Run the look-addition experiment (FR-005, SC-002): add a throwaway case to `Casting.Look` in `world/Casting.fs`, confirm `lookName`'s match fails to compile until the case is named and that `allLooks` reports it with no other edit, record the outcome here, then remove the case (depends on T008).
- [X] T013 [US2] Create `editor/CastPanel.fs` (FR-008, FR-009, FR-010, FR-011): open a window named `Cast`; per member in `Roster.characters`, a `collapsingHeader` labelled with `Roster.characterName`, closed by default, with `pushId`/`popId` around its contents so the repeated look labels stay distinct; per look in `Casting.allLooks`, a row showing `Casting.lookName` and a combo of `shared.Assets.Actors` sorted by id plus a `(none)` item. The combo's preview is the assigned asset's `Name`, or `(none)` when `Casting.tryAsset` answers `None`; a pick writes `Bindings.[slot] <- id` and `(none)` removes the entry. It **assigns in place** rather than routing through the Inspector, which `docs/EDITOR_DESIGN.md` records as D-022 — a slot is not a selectable item in the one-selection model, and there are eight of them. It draws nothing else, mutates nothing outside `shared.Cast`, and pushes no `UndoStack` entry (depends on T009, T011).
- [X] T014 [US2] Register `editor/CastPanel.fs` in `fsharp-game.fsproj` immediately before `editor/Editor.fs`; add `"Cast"` to `Editor.panelNames` in `editor/Editor.fs` for Ctrl+Tab cycling, and call `do CastPanel.draw shared &state` after `AssetBrowser.draw` in `Editor.draw` (depends on T013).
- [X] T015 [US2] Add `ImGui::DockBuilderDockWindow("Cast", left);` to `cimgui_setup_dock_layout` in `native/cimgui_sdl3.cpp`, so the panel is a tab beside the Outliner exactly as `docs/EDITOR_DESIGN.md`'s Workspace table specifies — the shim already computes that node id for the Outliner, so following the document costs nothing extra — and rebuild `native/libcimgui.so` with `./native/build.sh` (research Decision 7). **This is the only native step and it needs network access and SDL3 development headers** — `native/cimgui` is not vendored in the tree. If the rebuild cannot be run here, record that and skip: the panel is complete and merely floats at a default position, because layout persistence is deliberately disabled, and FR-009 asks for the tree rather than for a dock slot.

**Checkpoint**: the cast is a tree in the editor, every slot can be assigned and cleared, and no assignment reaches an undo stack.

---

## Phase 5: User Story 3 - The engine keeps its independence and its headless tests (Priority: P2)

**Goal**: Confirm the hoist did not become the seam through which the engine grows a dependency on the game.

**Independent Test**: reference a game module from engine code and confirm the build fails; run the engine's suite and confirm it needs no window, device, or asset.

- [X] T016 [US3] Confirm the engine cannot reach the game (FR-004, SC-005): add a reference to `Assets`, `Casting`, `Zone`, or `Gpu` from a file under `narrative/`, confirm the build fails with the type not defined, record the error here, then remove it. In the same pass, confirm by reading that no file under `narrative/` names `Casting` at all, which is the structural form of "the binding is inert to the engine" (FR-012). **Outcome**: a probe naming `Casting.allLooks` in `narrative/Units/Cast.fs` failed to build — `error FS0039: The value, namespace, type or module 'Casting' is not defined` — because `narrative/` is compiled before `world/`. The probe was removed (`git diff` on that file is empty), and no file under `narrative/` names `Casting`, `Assets`, `Zone`, `Gpu`, or `Physics`.
- [X] T017 [P] [US3] Confirm the engine is still headless (FR-013, SC-006): the narrative suite and the new `test/RosterTests.fs` run with no window, no graphics device, and no assets, and `cast/Roster.fs` adds no graphics dependency to the engine. **Outcome**: the whole suite — 249 tests, `test/RosterTests.fs` included — runs with no window and no device, and `cast/Roster.fs` opens only `Microsoft.FSharp.Reflection`. Its only mention of `Assets`/`Zone` is the doc comment stating the layer rule.

**Checkpoint**: the engine's independence is a build property, and its tests need no display.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Whole-feature validation and the documents that stop being true.

- [X] T018 Run `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj` and confirm the whole suite passes, with the narrative assertions unmodified and the roster and binding tests passing. Record the counts against the suite's size before this feature. **Outcome**: `dotnet build` is clean — 0 errors, 2 warnings, both pre-existing in `Engine.fs` (FS0009/FS0051, native pointers, a file this feature does not touch) — and `dotnet test test/fsharp-game.Tests.fsproj` passes **249/249**, against 240 before the feature (241 after the hoist, +8 binding tests). Every narrative assertion is unmodified.
- [ ] T019 Walk interactive scenarios 13–17 in `specs/032-cast-binding/quickstart.md` in the editor (with a display): the cast tree, binding and clearing a slot, the shared-asset case, that only discovered assets are offered, Ctrl+Tab cycling, and the docked position if T015 landed. **Not run if the agent environment has no display.** — **Not run**: the panel draws only inside the editor, which needs a click to exit the main menu, so an unattended run cannot reach it. The failure this walk would catch — an `ig*` export that does not resolve — was checked instead against the rebuilt library, where `igPushID_Str`, `igPopID`, `igBeginCombo`, `igEndCombo` and `igSelectable_Bool` are each a defined symbol. The panel's draw path remains unexercised by the suite.
- [X] T020 [P] Correct the documents this feature makes false (research Decision 11): `docs/ARCHITECTURE.md`'s roster bullet in the narrative section and the paragraph explaining that `narrative/` is listed first; `docs/PROJECT_STRUCTURE.md`'s cluster list, abridged compile list, cluster descriptions, and dependency chain (adding `cast/` and its position); `docs/ROADMAP.md` phase 10's `world/Cast.fs` line; and `docs/EDITOR_DESIGN.md` — its Workspace table and the Cast panel's row and dock, Principle 4's undo scope, the non-entity-surfaces bullet, and a new D-022. Add an amendment to `specs/025-narrative-core/spec.md` recording that the roster moved and that `narrative/` is no longer compiled first, in the form that spec already uses for its two earlier amendments. **Outcome**: all five corrected — `ARCHITECTURE.md` (a `cast/Roster.fs` bullet and the compile-order paragraph), `PROJECT_STRUCTURE.md` (the cluster count, the abridged compile list, the layout tree with `cast/` and `world/Casting.fs`, the cluster descriptions, the dependency chain), `ROADMAP.md` (phase 10's cast line), `EDITOR_DESIGN.md` (the Workspace table, the Cast row, Principle 4, the non-entity-surfaces bullet, D-022), and a 2026-09-20 amendment in `specs/025-narrative-core/spec.md`.
- [X] T021 [P] Confirm no per-frame path gained work (SC-007): `Casting.allLooks` and `Roster.characters` are module-initialised values rather than per-frame reflections, `Casting` is read by the panel only, and the stats line's frame rate is unchanged with the panel open. **Outcome**: both enumerations are module-initialised `let` values, so drawing the panel reflects nothing; `grep` shows `Casting` read by `state/SharedState.fs` (the field) and `editor/CastPanel.fs` (the panel) and nowhere else; and the panel draws only in edit mode. It does allocate per frame while open — the sorted actor array, as `editor/AssetBrowser.fs` already does — which is editor UI rather than the play hot path.
- [X] T022 [P] Confirm the panel's two structural constraints by reading `editor/CastPanel.fs`: it contains no `UndoStack.push` (FR-011), and its combo iterates `Assets.Actors` only, so no id outside the discovery can be written (FR-008). **Outcome**: confirmed by reading — no `UndoStack` appears in the file, its only writes are `shared.Cast.Bindings.[slot] <- asset.Id` and `.Remove slot`, and the only asset source is `shared.Assets.Actors`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies. T001 is the whole phase, and it is the only task that changes the compile order.
- **Foundational (Phase 2)**: depends on T001; **blocks all user stories**. T002 is the move; T003 and T004 are free to run alongside each other once it lands.
- **User Stories (Phases 3–5)**: all depend on Phase 2. US1 and US2 are independent of each other; US3 reads the result of both but depends on neither's tasks.
- **Polish (Phase 6)**: depends on all three stories.

### Within each phase

- Phase 2: T002 → T003 ∥ T004.
- US1: T005 → T006 (T006 mutates `cast/Roster.fs`); T007 is independent.
- US2: T008 → T009, T010, T012; T011 is independent of T008; T013 needs T009 and T011; T014 needs T013; T015 is last and standalone.
- US3: T016 → T017.

### Parallel Opportunities

- T003 ∥ T004 (different test files), both ready as soon as T002 lands.
- T010 ∥ T011 (a test file and the binding module), both ready once T008 and the plan's confirmed signatures are in hand.
- T007 can run alongside US2's work, since it only reads the narrative suite.
- T020 ∥ T021 ∥ T022 in Polish (three different files).

---

## Parallel Example: after T008

```bash
# The look vocabulary and binding are in; these touch different files:
Task: "T010 binding tests in test/CastingTests.fs"
Task: "T011 the five widget bindings in render/ImGui.fs"
```

---

## Implementation Strategy

### MVP

US1 **and** US2 are both P1, and they are two halves of one deliverable: US1 alone is a
behaviour-preserving hoist with no authoring value, and US2 alone has no roster to bind. The
MVP is therefore Phases 1–4 together — the roster declared once, and the panel that binds it.

1. Complete Phase 1 (the hoisted declaration) and Phase 2 (the deletion that moves the engine
   onto it).
2. Complete Phase 3 (US1) — the proof that there is one declaration.
3. Complete Phase 4 (US2) — the binding and the panel.
4. **STOP and VALIDATE**: run `dotnet test` and walk `quickstart.md` scenarios 13–17.

### Incremental Delivery

1. Phases 1–2 → one roster, one consumer set, the app behaving exactly as before.
2. US1 → the single declaration is proven and the member-addition guarantee is demonstrated.
3. US2 → the cast is a tree in the editor and every slot resolves or is visibly unassigned.
4. US3 → the engine's independence and its headless suite are confirmed as build properties.
5. Polish → the suite green, the documents corrected, and the panel's costs checked.

---

## Notes

- [P] tasks touch different files and have no incomplete dependencies; tasks in the same file are ordered even when independent.
- **Why the hoist is two phases**: creating `cast/Roster.fs` before deleting the engine's copy keeps the tree green at both checkpoints. The duplication lasts exactly one task and is never committed as duplication — but it is a duplicate *declaration*, which is why T002 (not T001) is the task that satisfies FR-001, and why **T001 and T002 land as one commit**: a commit between them would commit two roster declarations, which FR-001 forbids and SC-001 counts.
- **Why US1's phase is mostly proof**: Phases 1–2 *are* the hoist, so US1's job is to demonstrate that one declaration exists and that adding a member reaches both sides. T006 is the experiment that would fail if the game kept a list of its own.
- **Identity and look are separate types** (research Decision 2): the roster case is fieldless, so this feature deliberately does not build the request's literal shape — a member carrying her variant options. The reason is recorded in `spec.md`'s Assumptions and research Decision 2, and T009's `Slot` is where the two are paired instead.
- **The map is sparse and the entry is the check** (research Decision 4): there is no availability list, so the panel offers the whole member × look cross product and an unassigned leaf means "no model here". T010 asserts this directly rather than treating it as a gap.
- **The native step is isolated** (research Decision 7): T015 is the only task that needs a rebuild of `native/libcimgui.so`, and nothing else in the feature depends on it. Every widget this feature needs is already exported by the shipped library.
- **Out of scope, deliberately**: persistence, and consuming the binding (placing a cast member, or recording which character a placed entity is). Both are cheap afterwards because a slot is a member and a look, and neither is asserted anywhere in these tasks.
- **A follow-on trim, after these tasks**: the cast was reduced from twenty members to the four the story's tests and harness actually name (`Aiko`, `Akane`, `Shinji`, `Yuki`), and the narrative performance suite's four cast-dependent tests — SC-005's rebuild and one-event latency, SC-009's sustained throughput, and the commit-cost guard — were deleted with it, leaving the canonical-render guard that never depended on cast size. `specs/025-narrative-core`'s 2026-09-20 amendment records the withdrawal of SC-005 and SC-009 and the restatement of FR-030; the numeric mentions in `plan.md`, `data-model.md`, `contracts/cast-binding.md` and `research.md` are corrected to four members and eight slots.
- **A stale case name in a match compiles.** Trimming the cast left `characterName`'s sixteen removed arms
  behind for a while, and the tree built: F# reads an identifier in a pattern that resolves to no case
  as an uppercase *variable* pattern (`FS0049`), which then makes every rule after it unreachable
  (`FS0026`) and makes the match total, so no incomplete-match warning fires either. The arm's `nameof`
  returns the variable's own name, so any member the surviving arms did not cover rendered as `"Ayumi"`.
  Nothing in the suite could see it — the four real cases were matched ahead of the catch-all, and
  `RosterTests`' distinctness assertion cannot tell a fifth member rendering an old name from a
  correctly rendered one. A rename leaves the same trap. `FS0025`, `FS0026` and `FS0049` as errors would
  make both directions a build failure (`FS0025` is the missing-case direction, a warning today, which is
  why `RosterTests` carries a runtime assertion instead); that is not taken here.
- Verify `dotnet test` passes after each phase; commit per task or logical group.
