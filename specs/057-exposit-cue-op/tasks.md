# Tasks: Exposit Cue Op

**Input**: Design documents from `/specs/057-exposit-cue-op/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md,
contracts/exposit-op.md, quickstart.md

**Tests**: Not TDD. The dialogue is plain data on `GameplayState`, so every scenario is exercised **headlessly** and the
acceptance tests are part of each story (spec FR-006, FR-009). No device, window or GPU is opened.

**Organization**: Tasks are grouped by user story. The vocabulary change (Phase 1) and the compile fixes it forces
(Phase 2) block every story. **US1** (P1) is the write; **US2** (P2) is the non-fatal miss; **US3** (P3) is the panel.
The build is red from T001 until T004 puts the exhaustive matches and the test `describe` functions right; after that
the app and tests are green with the op's plain-line behaviour unchanged, and each story adds to that.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US3)

## Path Conventions

Single-project F# repository. Paths are relative to the repository root (`fsharp-game/`): `core/`, `render/`, `world/`,
`state/`, `editor/`, `screen/`, `test/`, `specs/`, `docs/`.

---

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 In `world/Cue.fs`, change `Exposit of text: string` to `Exposit of text: string * kind: Dialogue.Kind`;
  widen the module header's dependency note (one more pure module, `Dialogue`, which names `Roster`) and the op's
  doc comment. (data-model.md; FR-001, FR-002.)

**Checkpoint**: the type compiles; every downstream `match` on `Cue` is now an error to fix in Phase 2.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: fix the exhaustive matches and the shared writes so the app and tests build again. No new behaviour.

- [X] T002 In `world/CueEngine.fs`, change `exposit` to take the kind and set `state.Dialogue.Kind <- kind`, and change
  the match arm to `| Exposit (text, kind) -> exposit &state text kind; ValueSome Fin`. (data-model.md; FR-003.)
- [X] T003 In `editor/CueTree.fs`, update the `Cue` matches for the new arity (`label` → `Exposit (text, _)`,
  `executing`/`children` wildcards unchanged), and make the shared writes field-aware: `defaultOfKind "Exposit" ->
  Exposit("", Dialogue.Kind.Dialogue)`, `textOf` → `Exposit (text, _) -> ValueSome text`, `withText` → `Exposit (_, kind)
  -> Exposit(text, kind)`. Add the public `withExpositKind (kind: Dialogue.Kind) (cue: Cue) : Cue` (raises for a
  non-`Exposit`, as `withText` does). No choosers yet. (data-model.md, contracts; FR-007.)
- [X] T004 In `test/CueTests.fs` and `test/CuePanelTests.fs`, update each `describe`'s `Exposit` pattern to
  `Exposit (t, _)` and migrate every `Exposit "…"` construction to `Exposit("…", Dialogue.Kind.Dialogue)`, keeping all
  existing assertions unchanged.

**Checkpoint**: `dotnet build` and `dotnet test` are green; the op's plain-line behaviour is unchanged.

---

## Phase 3: User Story 1 - A cue presents a line, however it is spoken (Priority: P1) 🎯 MVP

**Goal**: an `Exposit` writes the authored text and kind to the screen's dialogue, at no cost in time.

**Independent Test**: put an `Exposit` on the cue, tick once, and assert the dialogue's `Shown`/`Kind`/`Text`
(quickstart, "presents a line").

### Tests for User Story 1

- [X] T005 [US1] In `test/CueTests.fs`, add the write scenarios: `Exposit(t, Kind.Dialogue)` writes a plain line;
  `Kind.Thought` writes a thought; `Kind.PortraitDialogue(Akane, Happy)` writes that portrait kind including its speaker
  and expression; the op is at the next op in the same step; a second `Exposit` replaces the first. (FR-003, FR-004,
  FR-006; SC-001, SC-003.)

**Checkpoint**: a cue shows any of the three kinds headlessly.

---

## Phase 4: User Story 2 - An unresolved portrait is an expected miss (Priority: P2)

**Goal**: a portrait line whose speaker cannot resolve is written and advances the cue, without raising or blocking.

**Independent Test**: with no cast binding, a portrait `Exposit` sets the dialogue and the cue advances
(quickstart, "non-fatal miss").

### Tests for User Story 2

- [X] T006 [US2] In `test/CueTests.fs`, add the miss scenario: with an empty `Cast` binding, a portrait `Exposit` still
  sets `Dialogue.Kind` to the authored portrait line and leaves the cue `Fin` — no raise. Where `DialogueTests` can
  assert the view's miss from data alone, point to it (do not open a device). (FR-005; SC-002.)

**Checkpoint**: authoring a portrait line before its art is bound is safe.

---

## Phase 5: User Story 3 - The panel authors the op (Priority: P3)

**Goal**: the panel offers a presentation chooser and, for a portrait line, closed-list speaker and expression choosers.

**Independent Test**: assert the label, the default, `textOf`/`withText` preserving the kind, and `withExpositKind`
(quickstart, "panel authors it").

### Implementation for User Story 3

- [X] T007 [US3] In `editor/CueTree.fs`, in `drawEditor`, add for `Exposit (_, kind)`: a presentation combo
  (Dialogue / Thought / Portrait) that replaces the kind via `withAt`/`commit` without clobbering an existing pair when
  re-choosing the current entry; and, for `Dialogue.Kind.PortraitDialogue (character, emotion)`, a speaker combo over
  `Roster.characters` and an expression combo over `Dialogue.emotions`. Choosing Portrait when not already portrait makes
  `PortraitDialogue(List.head Roster.characters, Dialogue.Emotion.Neutral)`. (data-model.md; research D5; FR-007, FR-008.)
- [X] T008 [US3] In `test/CuePanelTests.fs`, add the control's pure-half assertions: the `Exposit` row's label is
  `"Exposit: x"` (already in the kind table); `textOf (Exposit("x", PortraitDialogue (…)))` is the text; `withText`
  replaces the text and preserves the kind; `withExpositKind` replaces the kind and preserves the text and raises for a
  non-`Exposit`. `kinds`/`defaultOfKind` stay private (as in `specs/056`), so the default is exercised by the
  walkthrough, not headlessly. (FR-007; SC-005, SC-006.)

**Checkpoint**: the op is authorable in the panel; every chooser is a closed list.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T009 [P] In `world/Dialogue.fs`, correct the `Emotion` note that tied `[<SymbolicExpansion>]` to the cue op:
  its trigger is persistence (roadmap phase 15), because nothing serializes a cue yet and `Advent` carries no such
  attribute either. (research D6.)
- [X] T010 Build `fsharp-game.fsproj` and `test/fsharp-game.Tests.fsproj` with **0 warnings** and run the full suite
  green; walk `quickstart.md` (the three kinds, the instant/replace case, the non-fatal miss, the panel tables).
  (FR-009; SC-006.)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 is the root.
- **Foundational (Phase 2)**: T002, T003, T004 all depend on T001; T003 fixes `CueTree` for the new arity, T004 fixes
  the tests. They can be done in any order across files, but nothing is green until all three land.
- **US1 (Phase 3)**: depends on Phase 2.
- **US2 (Phase 4)**: depends on Phase 2; independent of US1.
- **US3 (Phase 5)**: depends on Phase 2; T007 (control) and T008 (tests) are one file each.
- **Polish (Phase 6)**: depends on all of the above.

### Same-file constraint (sequential)

- `world/Cue.fs`: T001 only.
- `world/CueEngine.fs`: T002 only.
- `editor/CueTree.fs`: T003 → T007.
- `test/CueTests.fs`: T004 → T005 → T006.
- `test/CuePanelTests.fs`: T004 → T008.

### Parallel Opportunities

- T002 and T003 touch different files and can run together once T001 lands; T004 depends on T001 only and can run
  alongside them (it does not need the production fixes to be written, only the type change).
- T005 and T006 are one file, in order. T007 and T008 are different files after T003.

---

## Implementation Strategy

### MVP First (User Story 1)

1. T001 (vocabulary), then Phase 2 (T002–T004) to return the build to green.
2. US1 (T005) is the MVP: a cue presents a plain line, a thought, or a portrait line.
3. Then US2 (miss), US3 (panel), then polish.

---

## Notes

- Tests are part of each story; the dialogue is plain data, so everything is assertable headlessly.
- The build is red from T001 until T004; each checkpoint marks the state.
- No `Command`/`History`/fsproj change: a cue edit is still one `SetCue` step, and `world/Dialogue.fs` is already
  compiled before `world/Cue.fs`.
- Portrait resolution stays the view's, so the engine still needs no `AssetManager`.

### Implementation status (2026-10-03)

- **T001–T010: done.** Both projects build with **0 warnings** and `dotnet test` passes (452 tests, up from 448). The
  op carries `Dialogue.Kind`; the engine writes it; the panel offers the presentation/speaker/expression choosers over
  closed lists; the `Dialogue` note about `[<SymbolicExpansion>]` now names persistence. No `Command`/`History`/fsproj
  change (FR-008).
- `kinds`/`defaultOfKind` remain private, so the `Exposit` default (a plain line with empty text) is asserted by the
  record in `data-model.md` and seen in the walkthrough, not by a unit test — consistent with `specs/056`.
