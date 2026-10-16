---
description: "Task list for feature implementation"
---

# Tasks: Live Reload of the Project File

**Input**: Design documents from `/specs/096-external-reload/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/live-reload.md

**Tests**: Included — the project mandates headless verification (constitution N-V) and this feature's success
criteria are test-based (spec FR-011). `test/ProjectTests.fs` gains the reload tests over a temp directory.

**Organization**: The **Foundational** phase builds the poll in `world/Project.fs` (the mechanism, testable on its
own); the story phases then wire it into the frame (US1), verify the non-destructive guarantees (US2), and add the
toggle (US3).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US3)
- Paths are repository-relative.

## Path Conventions

Single project, F# — flat clusters at the repository root: `world/`, `state/`, `editor/`, `screen/`, `test/`. This
feature **adds no file** and deletes none, so no `.fsproj` entry changes; every module it touches already exists.

---

## Phase 1: Foundational (Blocking Prerequisites)

**Purpose**: the content poll and the helpers the existing load/save share with it — the whole mechanism, testable on
its own. **⚠️ No user-story work can begin until this is complete.**

- [X] T001 `world/Project.fs`: factor the existing read/decode/write into `readText : string -> string option` (absent → `None`), `tryDecode : string -> Registry option` (illegal → `None`) and `writeAtomic : string -> string -> bool` (the encode-free `.tmp` + rename, reporting a failure); re-express `loadFrom` and `saveTo` over them **unchanged in behaviour** (research D3/D4)
- [X] T002 `world/Project.fs`: add the store values `[<Literal>] private pollSeconds = 0.25`, `mutable private lastText : string = ""` and `mutable private accumulator : float = 0.0`; make `load` set `lastText` to the file's text as read, and `persist` set it to the encoded text **only when `writeAtomic` reports success** (research D2/D3)
- [X] T003 `world/Project.fs`: `pollExternal (dt: float) : Registry option` — advance `accumulator`, return `None` until the interval elapses (then reset it), then `readText`: absent → `None`; text equal to `lastText` → `None`; decodes → set `lastText`/`lastWritten` and return `Some registry`; fails to decode → report and return `None` **without** updating `lastText` (so the next interval retries); and the pure gate `pollEnabled (editing: bool) (liveReload: bool) : bool = editing && liveReload` (spec FR-001, FR-004, FR-005, FR-007, FR-008; research D2/D4/D6/D7)
- [X] T004 `dotnet build fsharp-game.fsproj` is clean with no warnings (depends T001–T003)

**Checkpoint**: the poll exists and compiles; it is not yet drained by the frame.

---

## Phase 2: User Story 1 - An external edit appears without a restart (Priority: P1) 🎯 MVP

**Goal**: an external edit to `project.manifest` is installed into the running library within about a quarter of a second, while editing.

**Independent Test**: with the current directory at a temp dir holding a project file, rewrite the file externally,
advance the poll past the interval, and assert the reloaded library equals the new file's.

- [X] T005 [US1] `screen/GameplayScreen.fs`: end `update` with the drain, gated on the author editing — `if Project.pollEnabled state.Editing true then match Project.pollExternal frame.Dt with Some registry -> state.Scenarios <- registry | None -> ()` (beside the existing `persist`; the `true` is replaced by the toggle in T012) (spec FR-001, FR-003; research D6/D8)
- [X] T006 [P] [US1] `test/ProjectTests.fs`: add a temp-directory helper (create, set as current directory, run, restore, delete — the suite is serialized) and assert an **external rewrite** yields `Some` equal to the file's library; a **temp-file-then-rename** save (`File.WriteAllText(tmp)` then `File.Move(tmp, path, true)`) is picked up the same (FR-007, SC-004); an **identical rewrite** yields `None`; a write **by the game itself** (then a poll) yields `None`; and installing a reload leaves `state.Editor.Undo` and the zones unchanged (FR-006, FR-010, SC-005) (spec FR-002, FR-003, US1-AC1..AC3; research D9)
- [X] T007 [US1] `dotnet test` — the install story is green with no device (depends T005, T006)

**Checkpoint**: an external edit lands in the running game, and the game never reloads its own writes.

---

## Phase 3: User Story 2 - The reload never fights the game or empties a live library (Priority: P2)

**Goal**: a damaged, partial, or deleted file never alters the running library, and a transient partial write
self-heals.

**Independent Test**: write a malformed file and assert the poll reports it, returns `None`, and leaves the file
alone; then write a valid file and assert the poll now returns it; delete the file and assert `None` again.

- [X] T008 [US2] `test/ProjectTests.fs`: a **malformed / partial** external write yields `None`, raises the report tally, and leaves the file's bytes unchanged; a **subsequent valid** write is still picked up (the retry self-heals); a **deleted** file yields `None` and installs nothing (spec FR-004, FR-005, SC-003, US2-AC2/AC3)
- [X] T009 [US2] `dotnet test` — the non-destructive story is green with no device (depends T008)

**Checkpoint**: an agent's half-written or removed file never empties the author's library.

---

## Phase 4: User Story 3 - The author can suspend live reload (Priority: P3)

**Goal**: a **Live reload** toggle in the Miscellaneous panel suspends reloading without affecting saving.

**Independent Test**: assert `EditorState.create ()` has `LiveReload = true`, and that the interval gate suppresses a
second immediate poll.

- [X] T010 [US3] `state/EditorState.fs`: add `LiveReload : bool` (default `true`) beside `Snap`/`Tool`, initialised in `create` (spec FR-008; research D6)
- [X] T011 [US3] `editor/MiscPanel.fs`: draw a `Live reload` checkbox writing `state.Editor.LiveReload` in place (no `Command`, no `Undo` step) (spec FR-008)
- [X] T012 [US3] `screen/GameplayScreen.fs`: supply the toggle to the gate — `if Project.pollEnabled state.Editing state.Editor.LiveReload then …` (the poll is not called while playing, nor when the toggle is off) (spec FR-008; research D6/D8)
- [X] T013 [US3] `test/ProjectTests.fs`: assert `EditorState.create ()` has `LiveReload = true`; assert `Project.pollEnabled`'s truth table (true only when **both** editing and the toggle are on — the headless form of "the toggle suspends" and of the edit-mode gate, FR-008/FR-011); and assert the **interval gate** suppresses a second immediate poll (`dt` values summing below `pollSeconds` return `None` even when the file changed, and the poll past the interval then returns it) (spec FR-001, FR-008; research D6/D7/D9)
- [X] T014 [US3] `dotnet test` — the suspend story is green with no device (depends T010–T013)

**Checkpoint**: the author can hand-edit the file undisturbed, and turn live reload back on to pick up changes.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [X] T015 [P] Update `docs/ARCHITECTURE.md` — the Persistence section: note the runtime **content poll** (an external edit is picked up live; the game's own writes are recognised; a damaged/deleted file leaves the running library), alongside the write-on-change and read-at-startup of `specs/095` (spec FR-012)
- [X] T016 [P] Update `docs/EDITOR_DESIGN.md` — the Miscellaneous panel: the **Live reload** toggle (default on), transient editor state, not a step on the undo history (spec FR-012)
- [X] T017 Run the `quickstart.md` validation: `dotnet build` warning-free and `dotnet test` green with no device; then the manual check (external edit picked up live; the game's own saves do not churn) (spec FR-011, SC-007)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Foundational (Phase 1)**: no dependencies; T001 → T002 → T003 are sequential (one file, each building on the last).
- **User Stories (Phases 2–4)**: all depend on Foundational.
  - US1 (the drain + tests) is the MVP and the base.
  - US2 (the non-destructive guarantees) is `pollExternal`'s behaviour and is verified in tests; independent of US1's
    drain.
  - US3 (the toggle) edits `EditorState`/`MiscPanel` and gates the drain; independent of US2.
- **Polish (Phase 5)**: after the desired stories.

### Within Each Story

- `world/` before `screen/` (compile order).
- The test tasks accompany the behaviour they cover; a story ends with `dotnet test` green.

### Parallel Opportunities

- T015, T016 (different docs).
- T006 (the US1 tests) and T013 (the US3 tests) both extend `ProjectTests.fs`, so serialize them if done together.
- US3's T010 (`EditorState`) and T011 (`MiscPanel`) are separate files and can go together; T012 (the gate) follows
  T010.

---

## Parallel Example: Foundational

```bash
# The poll is one file and must be sequential; the build check is the separable step:
Task: "T004 dotnet build fsharp-game.fsproj is clean with no warnings"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Foundational (critical — blocks everything).
2. Phase 2 US1 → **STOP and VALIDATE** (`dotnet test`).
3. US1 alone delivers the core: an external edit is picked up live, and the game's own writes are ignored.

### Incremental Delivery

1. Foundational → the poll exists; the build is green.
2. US1 → the drain and the install are verified → MVP.
3. US2 → the damaged/partial/deleted guarantees are verified.
4. US3 → the toggle and the interval gate are verified.
5. Polish → docs and quickstart.

---

## Notes

- [P] = different files, no dependencies.
- Phase 1 `world/Project.fs` is one file, so T001–T003 are sequential; no new module, no `.fsproj` change, no package.
- Detection is by **content** (`readText <> lastText`), never a timestamp, so a temp-file-then-rename save and a
  coarse-timestamp filesystem are both handled (research D1/D2).
- The game's **own** write never reloads: `persist` maintains `lastText` on success (research D3).
- Runtime damage/deletion **keeps** the running library (research D4) — deliberately the opposite of the startup load,
  which yields an empty library for a damaged file (`specs/095`).
- The poll is drained **once per interval (~250 ms) and only while editing** — never during play and never per frame — with the gate `Project.pollEnabled editing liveReload` (a pure, tested predicate, research D6/D7/D8).
- FR-009 (no OS-specific API) needs no task: it is a "must not", satisfied by the absence of any watcher — `pollExternal` uses `System.IO` only (`specs/095`'s FR-016 precedent).
- Commit after each task or logical group.

---

## Phase 6: Convergence

- [X] T018 Align the design docs with the shipped `world/Project.fs` signatures — `research.md` D4 and `data-model.md` show `tryDecode : string -> Registry option`, but the code returns `Result<Registry, string>` so the decode failure's **message** is kept for the report; name the `decodeStartup` helper the code factors the startup policy into; and show `pollSeconds` as a plain `let private` (not `[<Literal>]`), matching the code (partial)
- [X] T019 Align `plan.md`'s source-tree `screen/GameplayScreen.fs` snippet with the shipped drain — the scrutinee is bound first and the empty option is matched with `| _ -> ()` rather than `| None -> ()`, because `open Screen` brings `ScreenTransition.None` into scope and shadows the option's `None` (partial)
