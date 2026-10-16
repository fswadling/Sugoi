# Tasks: Project Structure Consolidation

**Input**: Design documents from `specs/094-structure-consolidation/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Retrospective**: This task list records work already landed (see spec.md
Assumptions); every task is marked complete as of 2026-10-16. The verification
steps (build, test, scans) were run at each stage, not only at the end.

**Tests**: No new test tasks — a restructuring with zero behavior change; the
existing 666-test suite is the regression gate (FR-009).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup

- [x] T001 Scan the dependency graph: parse `fsharp-game.fsproj` for compile order, map every cross-file module reference across the 82 `.fs` files, and report order violations, upward cluster edges, and per-file topological levels (script in `specs/094-structure-consolidation/quickstart.md` §2)
- [x] T002 Verify the `state/History.fs` claim from the scan: grep `state/History.fs` for `ScenarioFork`/`CueEngine` references (none found — highest dependency is `GameplayState`)
- [x] T003 [P] Enumerate all `History.` references game-wide and classify global undo-history vs `Narrative.History` (no file both opens `Narrative` and references the undo history)
- [x] T004 [P] Enumerate call sites of the nested `Entity.Actor`/`Entity.Prop` companion modules (only `world/Zone.fs`, 2 + 4 sites) and confirm no file that `open Entity` references the global `Actor` module bare

## Phase 2: Foundational

(No foundational tasks — the restructuring needs no shared infrastructure; each story is independently buildable and verified by `dotnet build`.)

## Phase 3: User Story 1 — One clean compile-order sweep (P1)

**Goal**: `state/` forms one contiguous block in the compile list; the stale "second deliberate ordering inversion" is gone.

**Independent test**: `dotnet build fsharp-game.fsproj` green after the one-line move, with zero other changes.

- [x] T005 [US1] Move `<Compile Include="state/History.fs" />` in `fsharp-game.fsproj` to immediately after `<Compile Include="state/GameplayState.fs" />` (before `world/ScenarioFork.fs`)
- [x] T006 [US1] Rewrite the fsproj comments for the undo history, scenario fork and cue engine entries in `fsharp-game.fsproj` to state the current placement reasons (no "inversion" for the undo history)
- [x] T007 [US1] Rewrite the placement comment in `state/History.fs` (lines 12–15) to match: after `state/GameplayState.fs`, and nothing below it
- [x] T008 [US1] Verify: `dotnet build fsharp-game.fsproj` — 0 warnings, 0 errors

## Phase 4: User Story 2 — Folders match compile layers (P2)

**Goal**: No `world/` file compiles after any `state/` file; the debug overlay lives in `render/`; `world/` is grouped into `assets/` and `render/` subfolders.

**Independent test**: `dotnet build fsharp-game.fsproj` green after each move (path-only changes in the fsproj).

- [x] T009 [US2] Move `world/ScenarioFork.fs` → `state/ScenarioFork.fs` and `world/CueEngine.fs` → `state/CueEngine.fs`; update paths and comments in `fsharp-game.fsproj` (the cue engine is no longer "the only `world/` file below `state/`")
- [x] T010 [P] [US2] Move `screen/DebugOverlay.fs` → `render/DebugOverlay.fs`; in `fsharp-game.fsproj` list it at the end of the `render/` block (needs only `Diagnostics` + `ImGui`) and remove it from the `screen/` block
- [x] T011 [US2] Verify: `dotnet build fsharp-game.fsproj` green
- [x] T012 [P] [US2] Create `world/assets/` and move `world/AssetLoad.fs`, `world/Assets.fs`, `world/ActorAsset.fs`, `world/EnvironmentAsset.fs` into it; update the four paths in `fsharp-game.fsproj` (order unchanged)
- [x] T013 [P] [US2] Create `world/render/` and move `world/ActorRender.fs`, `world/EnvironmentRender.fs`, `world/EnvironmentBatch.fs`, `world/DialogueRender.fs`, `world/PromptRender.fs` into it; update the five paths in `fsharp-game.fsproj` (order unchanged)
- [x] T014 [US2] Verify: `dotnet build fsharp-game.fsproj` green

## Phase 5: User Story 3 — One name, one module (P3)

**Goal**: No global module shares a name with a `Narrative.*` module; no nested module shadows a global one.

**Independent test**: `dotnet build` (game + tests) green after each rename — the compiler enumerates any missed call site; `dotnet test` 666/666.

- [x] T015 [US3] Rename `state/History.fs` → `state/Undo.fs`, module `History` → `Undo`; update the fsproj path and the `"History.%s"` report prefix in `state/Undo.fs`
- [x] T016 [US3] Update `History.` → `Undo.` at all call sites: `editor/AdventPanel.fs`, `editor/CueEditor.fs`, `editor/Editor.fs`, `editor/Outliner.fs`, `editor/ScenarioPanel.fs`, `editor/Selection.fs`, `editor/SongPanel.fs`
- [x] T017 [P] [US3] Update `History.` → `Undo.` in tests: `test/AdventTests.fs`, `test/CuePanelTests.fs`, `test/GameplayStateTests.fs`, `test/MusicTests.fs`, `test/ScenarioForkTests.fs`, `test/ScenarioTests.fs`, `test/SkydomeTests.fs` (~120 sites; none open `Narrative`)
- [x] T018 [P] [US3] Update backtick-`` `History`` mentions in comments: `state/Command.fs` (6), `world/Zone.fs` doc comment, `audio/Audio.fs`, `editor/MiscPanel.fs`, `editor/Editor.fs`, `test/CuePanelTests.fs`
- [x] T019 [US3] Rename `Engine.fs` → `Shell.fs`, module `Engine` → `Shell`; update `<Compile Include>` in `fsharp-game.fsproj` and `Program.fs` (`Shell.run`, header comment)
- [x] T020 [US3] Rename nested `module Actor` → `module ActorEntity` in `world/Entity.fs:158`; update the two call sites in `world/Zone.fs` (`Actor.releaseRenderer` → `ActorEntity.releaseRenderer`); leave `Entity.Prop` (no global `Prop` namesake)
- [x] T021 [US3] Verify: `dotnet build fsharp-game.fsproj` and `dotnet build test/fsharp-game.Tests.fsproj` green; `dotnet test` — 666/666 passed

## Final Phase: Polish & Cross-Cutting

- [x] T022 [P] Fix stale path references in code comments across ~15 `.fs` files (`world/CueEngine.fs` → `state/CueEngine.fs`, `world/Assets.fs` → `world/assets/Assets.fs`, etc.) and the two moved files' own headers (`state/ScenarioFork.fs`, `state/CueEngine.fs`)
- [x] T023 Rewrite `docs/PROJECT_STRUCTURE.md` for the new layout: layout tree (also fixes stale `ZoneRegistry.fs`/`ZoneRegistryPanel.fs` entries; adds missing `Diagnostics`/`Lru`/`Maths`/`InstanceBuffer`), abridged compile listing, cluster definitions, principle 1 (subfolder allowance), migration status
- [x] T024 [P] Fix path references in the remaining docs: `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/EDITOR_DESIGN.md`, `docs/CUE_MODEL_DECISION.md`, `docs/ENVIRONMENT_LRU_PLAN.md`, `docs/RENDERING_NOTES.md`, `docs/RENDER_PERFORMANCE.md`, `docs/SHADOWS_NOTES.md` (incl. two stale line-number suffixes re-anchored: `Assets.scan` at `world/assets/Assets.fs:777`, zone release at `Shell.fs:274`)
- [x] T025 Final verification: `dotnet build` 0/0; `dotnet test` 666/666; quickstart scans §2–§4 all expected (`specs/094-structure-consolidation/quickstart.md`)

## Dependencies

- US1 (T005–T008) → US2 (T009–T014): the reorder and the moves touch the same fsproj region; sequenced to keep each build's diff small and attributable.
- US2 → US3 (T015–T021): renames touch files that moved (`state/History.fs` → `state/Undo.fs`); done after the paths settled.
- T015 → T016/T017/T018: call sites follow the module rename (compiler-driven).
- US3 → T022–T025: comment/doc sweeps reference final paths.

## Parallel execution

- T003 and T004 run in parallel (different scans, independent findings).
- T010 parallel with T009 (different files: `render/`+`screen/` blocks vs `state/` block) — executed sequentially in practice to keep builds attributable.
- T012 and T013 run in parallel (different subfolders).
- T016/T017/T018 run in parallel (editor vs test vs comment call sites).
- T022 and T024 run in parallel (code comments vs docs).

## Implementation strategy

MVP = US1 alone: one fsproj line + comment fixes, provable by a single green
build. Each subsequent story is an independent increment gated by its own green
build; renames were done strictly after moves so every build failure could be
attributed to exactly one change. No test logic was modified at any point.
