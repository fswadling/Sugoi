---
description: "Task list for feature implementation"
---

# Tasks: Automatic Persistence of the Scenario Library

**Input**: Design documents from `/specs/095-scenario-persistence/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/project-file.md

**Tests**: Included — the project mandates headless verification (constitution N-V) and this feature's success
criteria are test-based (spec FR-016). A new `test/ProjectTests.fs` covers the round trip and the file handling;
`test/ScenarioTests.fs` gains the mid-run Register guard test.

**Organization**: This is a small, single-module feature. The **Foundational** phase builds `world/Project.fs` (the
serializable record tree, the codec, and the file read/write), which every story needs; the story phases then wire the
two production seams and verify one increment each — the round trip and startup load (US1), change-driven writes
(US2), and the damaged/missing file paths (US3); a final refinement (Phase 6) disables Register while the cue is
mid-run. T018–T022 were added after `/speckit-clarify` settled the mid-run cue refusal.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US3)
- Paths are repository-relative.

## Path Conventions

Single project, F# — flat clusters at the repository root: `world/`, `state/`, `screen/`, `test/`. Compile order is
fixed by `fsharp-game.fsproj`; this feature **adds** one file (`world/Project.fs`, directly after
`world/ScenarioRegistry.fs`) and deletes none, so exactly one `<Compile>` entry is inserted in the game project and one
in the test project.

---

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Add `<Compile Include="world/Project.fs" />` to `fsharp-game.fsproj` immediately after the `world/ScenarioRegistry.fs` line (it names that module's types and every `world/` type below it; nothing earlier depends on it); add `<Compile Include="ProjectTests.fs" />` last in `test/fsharp-game.Tests.fsproj`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the serializable record tree, the codec, and the file read/write — the whole mechanism, testable on its
own. **⚠️ No user-story work can begin until this is complete.**

- [X] T002 `world/Project.fs`: `open Prime`, `open System.IO`; `[<Literal>] projectPath = "project.manifest"` and `[<Literal>] projectVersion = 1`; the `[<SymbolicExpansion>]` record tree mirroring the authored types — `Vec3Dto`, `QuatDto`, `LightDto`, `SkydomeDto`, `EntityDto` (Transform flattened to `Position`/`Rotation`/`Scale`), `ActorDto`, `PropDto`, `TriggerDto` (reusing `Cue.Cue`), `WaypointDto`, `TemplateDto`, `StagingDto`, `ZoneDto`, `ContentsDto` (reusing `Map<string, ZoneDto>` and `Cue.Cue`), `EntryDto`, and the file root `Project` (`Version` + `Scenarios : EntryDto array`) (research D1/D2, data-model)
- [X] T003 `world/Project.fs`: the conversion pairs — `toVec3`/`ofVec3`, `toQuat`/`ofQuat`, `toLightDto`/`ofLightDto`, `toSkydomeDto`/`ofSkydomeDto`, `toEntityDto`/`ofEntityDto`, `toActorDto`/`ofActorDto`, `toPropDto`/`ofPropDto`, `toTriggerDto`/`ofTriggerDto`, `toWaypointDto`/`ofWaypointDto`, `toTemplateDto`/`ofTemplateDto`, `toStagingDto`/`ofStagingDto`, `toZoneDto`/`ofZoneDto`, `toContentsDto`/`ofContentsDto`, `toEntryDto`/`ofEntryDto`, and the library↔entries boundary `toProject` (`OMap.toSeq` → ordered entries) / `ofProject` (`OMap.ofSeq1`) (data-model; research D2)
- [X] T004 `world/Project.fs`: `encode : Registry -> string` (`valueToSymbol` / `PrettyPrinter.prettyPrintValue … PrettyPrinter.defaultPrinter`) and `decode : string -> Registry` (`scvalue<Project>` then `ofProject`), raising on malformed input (research D1)
- [X] T005 `world/Project.fs`: `loadFrom : string -> Registry` (absent → `ScenarioRegistry.create ()` with no report; present → `File.ReadAllText |> decode`), `saveTo : string -> Registry -> unit` (encode, write `path + ".tmp"`, then `File.Move(tmp, path, true)`; report and skip on an encode or write failure, leaving the last good file), the module store `let mutable private lastWritten : Registry = ScenarioRegistry.create ()`, and `load : unit -> Registry` (`loadFrom projectPath`, then remember it) (research D7/D8, data-model)
- [X] T006 `dotnet build fsharp-game.fsproj` is clean with no warnings (depends T002–T005)

**Checkpoint**: the tree, codec and file I/O exist and compile; the library can be read from and written to a path.

---

## Phase 3: User Story 1 - The author's scenarios are there again next time (Priority: P1) 🎯 MVP

**Goal**: the library is loaded once at startup, and a library the author builds reads back equal, scenario for
scenario, in the same order.

**Independent Test**: build a library headlessly, `encode |> decode` it, and assert it is equal (order, contents, the
named-but-unregistered case, a chain-ending waypoint, an empty cue); assert `loadFrom` on a nonexistent path yields
the empty library and writes nothing. No window, no GPU.

- [X] T007 [US1] `screen/ScreenManager.fs`: in `init`, load once and install — `let mutable gameplay = GameplayState.create …` then `gameplay.Scenarios <- Project.load ()` before the `ScreenManager` record is built (so the author's scenarios are present from the first frame; not in `GameplayState.create`, which the headless fixtures call) (spec FR-007, SC-001; research D6)
- [X] T008 [P] [US1] `test/ProjectTests.fs`: the round trip — a library built through `ScenarioRegistry.capture` (an actor, a chained waypoint pair, a registered trigger with an authored cue, and a scenario cue) satisfies `Project.decode (Project.encode r) = r`; a named-but-unregistered scenario (`setContents name None`), a `Next = None` chain end, and an empty (`Fin`) cue each survive; a multi-scenario library keeps authored order (spec FR-003, FR-004, FR-011, US1-AC1/AC2)
- [X] T009 [US1] `test/ProjectTests.fs`: `Project.loadFrom` a path under `Path.GetTempPath()` that does not exist returns `ScenarioRegistry.create ()`, reports nothing (`Diagnostics.errorCount` unchanged), and writes nothing (the file is still absent) (spec FR-008, US1-AC3)
- [X] T010 [US1] `dotnet test` — the round-trip and startup-load story is green with no device (depends T007–T009)

**Checkpoint**: a library round-trips through save and load, and the game loads it at startup.

---

## Phase 4: User Story 2 - Every change is captured, including undo (Priority: P2)

**Goal**: the library is written whenever its value changes — add, clear, register, delete, and undo/redo — and never
when it does not.

**Independent Test**: `Project.persist` the same instance twice and assert the file is unchanged (0 writes); persist
a new instance and assert the file changes; undo a delete (a prior instance) and assert the file reflects it again.

- [X] T011 [US2] `world/Project.fs`: `persist : Registry -> unit` — write `projectPath` **iff** the argument is not the same instance as `lastWritten` (`obj.ReferenceEquals`), then remember it; delegate the write (and its report-and-skip, T005) to `saveTo` (spec FR-005, FR-006, FR-015, SC-002, SC-003, SC-006, SC-008; research D5/D7/D10)
- [X] T012 [US2] `screen/GameplayScreen.fs`: end `update` with `do Project.persist state.Scenarios` (after the actor step; the production seam the headless suite never calls) (spec FR-001, FR-005; research D6)
- [X] T013 [US2] `test/ProjectTests.fs`: `persist` writes nothing when handed the same instance twice (the file's bytes and its mtime are unchanged); writes when handed a different instance; after a delete-then-undo (which restores a prior instance) the file holds the scenario again (spec FR-005, SC-002, US2-AC1..AC3); and a write touches **nothing but the library** — `Project.persist` and `Scenarios <- Project.loadFrom …` leave `state.Editor.Undo` and every zone unchanged, adding no history step (spec FR-012, FR-013, SC-007)
- [X] T014 [US2] `dotnet test` — the change-driven story is green with no device (depends T011–T013)

**Checkpoint**: every library change is captured, and a restored scenario is not lost on the next launch.

---

## Phase 5: User Story 3 - A missing or damaged project file is survivable (Priority: P3)

**Goal**: a damaged or unrecognised file is reported and harmless, and the write is atomic.

**Independent Test**: `loadFrom` a malformed file and a file with `Version = 99`; assert each reports, yields the
empty library, and leaves the file's bytes unchanged; assert `saveTo` leaves no `.tmp` and a readable target.

- [X] T015 [US3] `world/Project.fs`: the damaged path in `loadFrom` — catch a read/decode failure and an unrecognised `Version`, `Diagnostics.reportf` it, and return `ScenarioRegistry.create ()` while leaving the file untouched (spec FR-009, FR-010, US3-AC2/AC3; research D8/D9)
- [X] T016 [US3] `test/ProjectTests.fs`: a malformed file and an unknown-version file each report (`Diagnostics.errorCount` rises), return the empty library, and leave the file's bytes byte-for-byte unchanged; `saveTo` produces a complete target, leaves no `project.manifest.tmp`, and an existing good file is still readable afterwards; a failed write (an unwritable target) is reported and leaves the existing good file in place (spec FR-006, FR-009, FR-010, SC-005, SC-006, SC-008, US3-AC2..AC4)
- [X] T017 [US3] `dotnet test` — the survivability story is green with no device (depends T015, T016)

**Checkpoint**: a missing or damaged file never loses the session or corrupts what remains.

---

## Phase 6: The mid-run cue is refused at Register (US1 refinement)

**Goal**: a cue that is still mid-run cannot be filed, and the author is told why before attempting it.

**Independent Test**: with a headless state whose cue holds a `…State` op (tick a `Wait` so it becomes `WaitState`), assert the Register guard refuses (`canFile = false`); with an authored cue — including a non-`Fin` one that has never played — assert it allows filing.

- [X] T018 [P] `render/ImGui.fs`: add one tooltip extern — `[<DllImport("cimgui", EntryPoint = "igSetItemTooltip", CallingConvention = CallingConvention.Cdecl)>] extern void setItemTooltip(string text)` (bound like the existing variadic `igText`, with a literal format string). The native `libcimgui.so` (imgui 1.92.9b) already exports it, and `ImGuiHoveredFlags_AllowWhenDisabled` exists if the explicit `isItemHovered` + `setTooltip` path is preferred (research D13)
- [X] T019 [US1] `editor/ScenarioPanel.fs`: factor the capture guard into a pure predicate `canFile (cue: Cue) : bool = not (CueTree.executing cue)` (spec FR-018; research D13)
- [X] T020 [US1] `editor/ScenarioPanel.fs`: disable the **Register** control when `canFile` is false and attach the tooltip "The cue is still running — stop play first" (the panel already disables Add/Clear/Fork, so this is the same affordance) (spec FR-018)
- [X] T021 [US1] `test/ScenarioTests.fs`: assert `ScenarioPanel.canFile` refuses a mid-run cue (a `WaitState`) and allows an authored one — including a non-`Fin`, unplayed cue — and that a refusal files nothing (spec FR-018, SC-010)
- [X] T022 [US1] `dotnet test` — the Register-refusal story is green with no device (depends T019–T021)

**Checkpoint**: the library can only ever hold authored cues.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T023 [P] Update `docs/ARCHITECTURE.md` — the Persistence section: the s-syntax `project.manifest` at the working-directory root, the library-only scope, automatic write-on-change and load-at-startup, replacing the `Project_DTO`/JSON/`project_save`/`project_load` text
- [X] T024 [P] Update `docs/ROADMAP.md` — phase 15: the project-file deliverables (`world/Project.fs` in s-syntax, not `System.Text.Json`/`project.json`) and the "done when"
- [X] T025 [P] Note in `specs/083-scenario-strings/spec.md` that FR-016 ("the library MUST NOT be persisted") is superseded by `specs/095`
- [X] T026 [P] Sweep for the retired persistence sketch — grep `world/ state/ editor/ screen/` for `Project_DTO`, `project_save`, `project_load`, `project.json`; expect none in code (spec FR-014, FR-017)
- [X] T027 Run the `quickstart.md` validation: `dotnet build` warning-free and `dotnet test` green with no device; then the manual round trip (author, quit, relaunch) (spec FR-016, SC-009)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup — **BLOCKS all stories** (the codec and file I/O are the whole
  mechanism). T002 → T003 → T004 → T005 are sequential (one file, each building on the last).
- **User Stories (Phases 3–6)**: all depend on Foundational.
  - US1 (startup load + round trip) is the MVP and the base.
  - US2 (change-driven writes) adds `persist` and the frame seam; independent of US1's tests but its file overlaps
    (`ProjectTests.fs`).
  - US3 (damaged/missing + atomicity) refines `loadFrom` and verifies the file paths; independent of US2.
  - Phase 6 (the mid-run Register refusal) is a US1 refinement; it touches `editor/ScenarioPanel.fs` and
    `render/ImGui.fs` only, so it is independent of US2/US3.
- **Polish (Phase 7)**: after the desired stories.

### Within Each Story

- `world/` before `screen/` (compile order).
- The test tasks accompany the behaviour they cover; a story ends with `dotnet test` green.

### Parallel Opportunities

- T023, T024, T025, T026 (different files) in Polish; in the refinement phase, T018 (the binding) and T021 (the test) are separate files.
- T008 and T009 are the same file, so sequential in practice; across stories, US2's T013 and US3's T016 both extend
  `ProjectTests.fs` and should be serialized if done together.
- The story phases edit disjoint game files (`ScreenManager.fs` for US1; `GameplayScreen.fs` for US2) but share
  `world/Project.fs` and `test/ProjectTests.fs`, so they are best done in priority order by one worker.

---

## Parallel Example: Foundational

```bash
# The tree, conversions, codec and I/O are one file and must be sequential, but the build check is separable:
Task: "T006 dotnet build fsharp-game.fsproj is clean with no warnings"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup → Phase 2 Foundational (critical — blocks everything).
2. Phase 3 US1 → **STOP and VALIDATE** (`dotnet test`).
3. US1 alone delivers the core: a library the author builds is present, in order, on the next launch.

### Incremental Delivery

1. Setup + Foundational → the codec and file I/O exist; the build is green.
2. US1 → the startup load and the round trip are verified → MVP.
3. US2 → change-driven writes (including undo) are verified.
4. US3 → the missing/damaged/atomic paths are verified.
5. Phase 6 → the mid-run Register refusal is verified.
6. Polish → docs and quickstart.

---

## Notes

- [P] = different files, no dependencies.
- Phase 2 `world/Project.fs` is one file, so T002–T005 are sequential; it is the feature's only new module.
- The file is `project.manifest` at the working-directory root; the record tree is the only thing that flattens
  `Vector3`/`Quaternion` and the ordered map, both of which Prime 11.5.2 cannot encode (research D1).
- Loading is in `ScreenManager.init`, **not** `GameplayState.create` — every headless fixture calls `create`, so a
  disk read there would make the suite touch the filesystem (research D6).
- `persist` compares by **reference**, not by value — Prime's `OMap` equality is reference-based, and the library is
  immutable, so a change is always a new instance (research D5).
- A run-form cue (`…State` op) does not encode; **Register refuses it** with a disabled control and a tooltip
  (Phase 6, spec FR-018), and `persist` keeps a defensive report-and-skip rather than emitting a partial file
  (research D10/D13). The authored-cue invariant is the norm.
- No new package; `Prime` is already referenced.
- Commit after each task or logical group.

---

## Phase 8: Convergence

- [X] T028 Correct `docs/ROADMAP.md` — the phases table's row **15** still reads "save/load `project.json`, async loading, LRU cache"; update it to the s-syntax `project.manifest` so the summary row matches the corrected phase-15 detail (FR-017, partial)
- [X] T029 Correct `docs/PROJECT_STRUCTURE.md` — its `world/` compile-order list omits `world/Project.fs` (it belongs between `world/ScenarioRegistry.fs` and `world/Systems.fs`), and its target layout still says `Project.fs # DTO persistence (project.json) (planned)`; add the file to the compile list and describe it as the s-syntax project file (`project.manifest`) at the launch working directory, and add `test/ProjectTests.fs` to its test list if one is present (FR-017, partial)
