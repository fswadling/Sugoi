# Tasks: Interact Prompt

**Input**: Design documents from `/specs/039-interact-prompt/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: The feature spec asks for the pure layout to be covered headlessly (plan.md `Testing`), so one test task is included. The drawing, the font and the editor-overlay removal are the interactive walkthrough.

**Organization**: Tasks are grouped by user story. The feature is small and its stories share one pipeline, so the story phases are ordered by priority but overlap in the files they touch; the note in each phase says which acceptance it delivers.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Single-project F# repository. All paths are relative to the repository root (`fsharp-game/`):

- `render/` — the GPU/2D layer; `world/` — game-domain modules and views; `screen/` — the screens;
  `test/` — the xUnit project; `docs/` — reference docs.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the two new files and their build entries, so every later step builds green.

- [X] T001 [P] Create `world/PromptRender.fs` with its module header (`module PromptRender`) and add `<Compile Include="world/PromptRender.fs" />` to `fsharp-game.fsproj`, immediately after the `world/DialogueRender.fs` entry.
- [X] T002 [P] Create `test/PromptTests.fs` with its module header (`module PromptTests`) and the `open Xunit` it will need, and add `<Compile Include="test/PromptTests.fs" />` to `test/fsharp-game.Tests.fsproj`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: The prompt cannot be measured until the layer has a slot for it.

- [X] T003 In `render/Ui.fs`, raise `textSlots` from `2` to `3` and update its comment to name the three string kinds the layer draws (a dialogue's name plate, its line, the interact prompt). The prompt and a line are mutually exclusive today (FR-012), but the capacity is one slot per kind of string, not per string that happens to coexist. No other change to the layer.

**Checkpoint**: the layer can lay out three strings, one per kind; `dotnet build` and the suite are green (nothing uses the third slot yet).

---

## Phase 3: User Story 1 - The prompt appears when you enter a trigger (Priority: P1) 🎯 MVP

**Goal**: entering an interactable trigger draws its authored prompt over the scene; leaving removes it; the editor's ImGui read-out is gone.

**Independent Test**: place an interactable trigger with a prompt, play, walk into it (the text appears), walk out (it goes); with no trigger entered nothing is drawn.

### Implementation for User Story 1

- [X] T004 [US1] In `world/PromptRender.fs`, add the presentation constants — `bottomMargin : viewportH:float32 -> float32` and `maxWidth : viewportW:float32 -> float32`, each a fraction of the viewport (FR-007) — the `[<Struct>] PromptColours` palette (a light `Text`, a near-black `Shadow`) and the pure `layout : viewportW:float32 -> viewportH:float32 -> textW:float32 -> textH:float32 -> Ui.Rect` (centred horizontally, above the bottom margin).
- [X] T005 [US1] In `world/PromptRender.fs`, add `draw : prompt:string -> viewportW:float32 -> viewportH:float32 -> byref<Ui.Ui> -> unit`: a no-op when `prompt` is empty (FR-005, FR-009); measure the prompt through `Ui.setLine &ui 2 prompt (int (maxWidth viewportW))` (slot 2); `layout` it from the measured size; emit `Ui.text` at the text rect in `PromptColours.Text`. (Depends on T003, T004.)
- [X] T006 [P] [US1] In `test/PromptTests.fs`, cover `PromptRender.layout` (pure, no GPU): the block's horizontal centre is the viewport's centre; the block is fully on screen (left ≥ 0, right ≤ viewport width, bottom above the bottom margin); the placement scales with the viewport. (Depends on T004.)
- [X] T007 [US1] In `screen/GameplayScreen.fs`: delete `drawTriggerOverlay` and `anyInside` (the ImGui `Status` read-out, research Decision 7); in `draw`, while `not state.Editing`, resolve the prompt from `state.InteractableTrigger` with an explicit `match` over `Zone.tryTriggerAt zone` (no `ValueOption.bind`, no closure, no `Trim` — FR-002, FR-010) and call `PromptRender.draw` immediately after `DialogueRender.draw`. (Depends on T005.)

**Checkpoint**: User Story 1 is fully functional and testable on its own — a prompt appears and disappears with the trigger, and no debug window is drawn over the game.

---

## Phase 4: User Story 2 - It reads as game UI, and stays on screen (Priority: P2)

**Goal**: the prompt stays legible against any scene content and fully on screen at any size. (Its being game UI rather than an ImGui window, and its absence in edit mode, are already delivered by T007.)

**Independent Test**: resize the window (the prompt stays centred and fully on screen); stand over bright, then dark, scene content (it stays readable); give it a long prompt (it wraps and stays on screen).

### Implementation for User Story 2

- [X] T008 [US2] In `world/PromptRender.fs`, draw a shadow: the same measured geometry at the text rect offset by a small constant, in `PromptColours.Shadow`, emitted **before** the text, so the two share the glyph-atlas texture and sampler and coalesce into one draw run (FR-008). (Depends on T005.)
- [X] T009 [US2] In `world/PromptRender.fs` (and `test/PromptTests.fs`), handle a prompt wider than `maxWidth`: it wraps to that width, the block grows upward from its bottom anchor, and it stays fully on screen. Add a `PromptTests` case asserting a tall (multi-line) measured block stays on screen. (Depends on T004, T006.)

**Checkpoint**: the prompt is legible and stays on screen at any window size and prompt length.

---

## Phase 5: User Story 3 - The nearest wins; silence when there is nothing to say (Priority: P3)

**Goal**: a dialogue line takes the screen; when it goes, the prompt returns. (The *nearest* trigger and the *empty/non-interactable silence* are already delivered — by spec 029's `Systems.updateTriggers` and by `draw`'s empty early-out — and only need the walkthrough to confirm.)

**Independent Test**: with the player inside the trigger, show a dialogue line: the prompt is suppressed; dismiss the line: the prompt returns. Overlap two interactable triggers and confirm the nearer one's prompt is shown. Enter a non-interactable trigger, and one with an empty prompt: nothing is drawn.

### Implementation for User Story 3

- [X] T010 [US3] In `screen/GameplayScreen.fs`, gate the prompt on `not state.Dialogue.Shown` — while a dialogue line is up the line takes the bottom strip and the prompt is not drawn; it returns when the line is dismissed (FR-012). (Depends on T007.)

**Checkpoint**: all three stories behave; the prompt yields to dialogue and never competes with it.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T011 [P] Update `docs/PROJECT_STRUCTURE.md` (the new `world/PromptRender.fs`) and `docs/RENDERING_NOTES.md` §12 (the layer's second consumer and the third text slot).
- [X] T012 Run the [quickstart.md](./quickstart.md) walkthrough (steps 1–9) and the full build + suite; record the outcome (the drawing, the font and the shadow need a GPU and a person, so this is the manual pass).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies; T001 and T002 are independent (different `.fsproj` files).
- **Foundational (Phase 2)**: T003 depends on T001 (the file it precedes must exist in the build order).
- **US1 (Phase 3)**: T004 depends on T001; T005 on T003/T004; T006 on T004; T007 on T005. This is the MVP.
- **US2 (Phase 4)**: T008 depends on T005; T009 on T004/T006. Both are small additions to the same module.
- **US3 (Phase 5)**: T010 depends on T007.
- **Polish (Phase 6)**: T011 after the code is settled; T012 last.

### Within each story

- The view module before the wiring; the wiring before the precedence gate.
- T004 → T005 → T008 all edit `world/PromptRender.fs`, so they are sequential, not `[P]`.

### Parallel Opportunities

- T001 and T002 (different projects).
- T006 (`test/PromptTests.fs`) and T007 (`screen/GameplayScreen.fs`) touch different files, but T006 needs only T004 — so once T004 is done, T006 can proceed alongside T005/T007 if desired.
- T011 (docs) is independent of the code once the shapes are settled.

### Same-file constraint

- `world/PromptRender.fs` is edited by T004, T005, T008 and T009 — sequential.
- `screen/GameplayScreen.fs` is edited by T007 and T010 — sequential.
- `test/PromptTests.fs` is edited by T002, T006 and T009 — sequential.

---

## Implementation Strategy

### MVP

Phases 1–3 (T001–T007) are the MVP: the prompt appears when the player enters an interactable trigger
and goes when they leave, drawn by the game's own layer, with the editor's debug read-out removed. That
alone satisfies US1 and most of US2 (it is game UI, and it does not appear in edit mode).

### Incremental Delivery

1. **Setup + Foundational + US1** (T001–T007) — a working prompt; demonstrate.
2. **US2** (T008–T009) — contrast and long-prompt handling; demonstrate over a bright scene and at a
   small window size.
3. **US3** (T010) — the dialogue precedence; demonstrate a line and a prompt together.
4. **Polish** (T011–T012) — docs and the walkthrough.

---

## Notes

- **The data and the detection already exist.** The trigger's `Interactable` flag and `Prompt` (spec
  029) and the nearest-interactable resolution (`Systems.updateTriggers`) are untouched; this feature
  only draws their result (FR-002, FR-004). **FR-004 and SC-004 (the nearest wins) are verified, not
  built** — spec 029's `Systems.updateTriggers` already resolves it, and T012's walkthrough (step 6)
  confirms it.
- **One capacity bump, nothing else in the layer.** `textSlots` 2 → 3 is the only change to
  `render/Ui.fs`; no function, field or shader changes (plan.md `Structure Decision`).
- **No per-frame allocation (FR-010).** Measure through the layer's cached slot; read the prompt string
  as authored; resolve the trigger with the existing `Trigger voption`; do not `Trim`.
- **The shadow and the text coalesce** into a single draw run because they share the atlas texture and
  sampler (research Decision 3).
- **A line takes the screen (FR-012).** The dialogue panel and the prompt share the bottom strip; the
  prompt yields rather than stacking (research Decision 5).

---

## Validation notes (implementation)

- **Build and suite**: both projects build with `--no-incremental`, **0 warnings, 0 errors**; `dotnet
  test` passes **306** (up from 302), including the four new `PromptTests` — centred horizontally, fully
  on screen, a proportional bottom margin, and a tall wrapped block still on screen.
- **No new shader, pipeline, pass or native file.** The only change to the 2D layer is `textSlots`
  2 → 3; the prompt rides the existing layer and its `upload`/`draw`.
- **FR-010 holds structurally**: the prompt string is read as authored (no `Trim`), the trigger is
  resolved with the existing allocation-free `Trigger voption` via an explicit `match`, and the text is
  measured through the layer's cached slot, so the draw path allocates nothing per frame.
- **The editor's `Status` overlay is gone** — `drawTriggerOverlay` and `anyInside` are deleted, so no
  debug window appears over the game.
- **Not verified by automation**: **T012**, the interactive walkthrough. The drawing, the font, the
  shadow's legibility, the resize behaviour and the dialogue-precedence behaviour need a GPU, a window
  and a person, so T012 was run by hand and passed.

---

## Phase 7: Convergence

- [X] T013 Reconcile FR-009 and SC-002 with the feature's deliberate removal of the editor's ImGui read-out (`contradicts`). Both say that with nothing to show "the frame MUST be identical to the pre-feature frame", but the removal of the `Status` overlay (research Decision 7, the Assumption) changes the play-mode frame while the player is inside a non-interactable trigger: before 039 a window listed "inside X", now nothing is drawn. Scope the two statements to the prompt itself — nothing of the prompt is drawn and the frame is otherwise unchanged — and note the read-out removal as the intended change, so no reader concludes the old overlay must remain. `spec.md` is the only file that needs the edit (converge does not touch it; `/speckit-implement` does).

**T013 outcome.** FR-009 and SC-002 (and the plan Constraints and quickstart step 5) now scope the identical-frame claim to the prompt itself: with nothing to show, nothing of the prompt is drawn and the frame is otherwise unchanged, with the removed editor read-out named as the only intended difference. Docs only; no code changed, and the suite is unchanged.

**T012 outcome.** The interactive walkthrough was run by hand and passed: the prompt appears on entering an interactable trigger and goes on leaving, reads as game UI over the scene (no debug window), stays on screen when resized, is legible over bright and dark content, draws nothing for an empty or non-interactable trigger, shows only the nearest when triggers overlap, is suppressed while a dialogue line is up and returns when it is dismissed, and is absent in edit mode.
