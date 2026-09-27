# Implementation Plan: Interact Prompt

**Branch**: `039-interact-prompt` | **Date**: 2026-09-27 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/039-interact-prompt/spec.md`

## Summary

When the player is inside an interactable trigger, draw that trigger's prompt as a single centred line near the bottom of the screen, using the game's own 2D layer (spec 038) — the **second consumer** of that layer, after the dialogue panel. The data and the selection already exist (spec 029): a trigger carries an `Interactable` flag and a `Prompt`, and the play-mode trigger pass (`Systems.updateTriggers`) already resolves the **nearest** interactable trigger the player is inside, exactly as the `odin-game` reference does. This feature is therefore presentation only: a small `world/PromptRender.fs` view that lays a line out bottom-centre and draws it (with a shadow for contrast), the editor's ImGui `Status` read-out removed, and one more text slot in the layer so a prompt can be measured alongside a dialogue's name plate and line.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).

**Primary Dependencies**: **none new.** SDL3-CS (SDL_gpu) and the vendored SDL_ttf the 2D layer already uses. No new package, no new native library, no new shader.

**Storage**: none. The prompt is authored text already stored on the trigger (spec 029); nothing new is persisted and no authored format changes.

**Testing**: `test/PromptTests.fs` (new) covers the **pure layout** — the prompt rect is centred, fully on screen, clear of the bottom edge, at a range of viewport sizes and measured sizes. The drawing, the font and the editor-overlay removal are the interactive walkthrough (a GPU, a window and a person).

**Target Platform**: Linux x64 desktop, SDL3 + SDL_gpu.

**Project Type**: single-project native desktop game with a docked ImGui editor.

**Performance Goals**: hold 60 FPS. The prompt is one extra measured string and two text draws (text + shadow) that **coalesce into a single run** because they share the glyph atlas texture and sampler; the draw path must add no per-frame allocation (FR-010, SC-005).

**Constraints**: play mode only, and only when no dialogue line is shown (FR-001, FR-012); the text is the authored prompt alone (FR-002); the nearest interactable trigger wins (FR-004); it must stay on screen at any window size and be legible against any scene content (FR-007, FR-008); with nothing to show the prompt draws nothing and the frame is otherwise unchanged (FR-009; the editor's play-mode read-out is removed, research Decision 7); no per-frame allocation (FR-010); existing behaviour unchanged (FR-011).

**Scale/Scope**: one new view module (`world/PromptRender.fs`), one capacity bump in `render/Ui.fs` (`textSlots` 2 → 3), the prompt wired into `screen/GameplayScreen.fs` with the editor's `Status` overlay removed, one new test file, and the two `.fsproj` entries. No authored format, no new shader, pipeline or pass, and no new editor surface.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one small view module and one test file; the data, the detection and the drawing layer all already exist. No new dependency, shader, pipeline or pass. The one editor change (removing a debug overlay) *reduces* surface. |
| II. Fail Loudly | Pass — no new failure path. An absent or empty prompt is an expected state that draws nothing; the drawing reuses the layer's existing loud failures (font open, measurement). |
| III. Spec-First | Pass — `specs/039-interact-prompt/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — the prompt is a plain string read from a `[<Struct>]` trigger; the layout is a pure function of the viewport and the measured size; the drawing writes into the layer's reused batch arrays. |
| V. Minimise GC Pressure | Pass — the draw path measures through the layer's cached text slots and copies geometry into the reused batch; the prompt string is read, not rebuilt (no per-frame `Trim`), and the trigger lookup uses the existing allocation-free `Trigger voption`. |
| N-V. Testing is the Authoring Tool | Not engaged — it governs the narrative subsystem, which is untouched. |

## Complexity Tracking

None. Nothing here is a constitution violation or a new dependency.

## Project Structure

### Documentation (this feature)

```text
specs/039-interact-prompt/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── prompt-display.md    # Phase 1 output
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
render/
└── Ui.fs                # CHANGED: `textSlots` 2 -> 3 — one slot per kind of string the layer
                         #   draws (a dialogue's name plate, its line, the interact prompt).
world/
└── PromptRender.fs      # NEW: the prompt view — a pure `layout` (viewport + measured size ->
                         #   `Ui.Rect`) and `draw` (measure, then shadow and text). Knows only
                         #   `Ui` and the viewport; nothing of triggers, dialogue or the editor.
screen/
└── GameplayScreen.fs    # CHANGED: resolve the prompt from `state.InteractableTrigger` (nearest
                         #   interactable, playing, no line shown) and draw it after the dialogue;
                         #   remove the ImGui `Status` read-out (`drawTriggerOverlay` + `anyInside`).
fsharp-game.fsproj       # CHANGED: the new `world/PromptRender.fs` compile entry.
test/
├── PromptTests.fs       # NEW: the pure layout across viewport and measured sizes.
└── fsharp-game.Tests.fsproj  # CHANGED: the new test entry.
docs/
├── PROJECT_STRUCTURE.md # CHANGED: the new module.
└── RENDERING_NOTES.md   # CHANGED: the layer's second consumer and the extra text slot.
```

**Structure Decision**: the prompt is a **view**, so it sits beside `world/DialogueRender.fs` and is likewise blind to where its text came from — `PromptRender.draw` takes a `string`, the viewport and the layer, and knows nothing of triggers, dialogue or the editor. The layer change is a **capacity bump, not a new facility**: its rule is one text slot per kind of string it may draw, so `textSlots` grows from two to three — a name plate, a line, and the prompt (the prompt and a line are mutually exclusive today, FR-012, but the capacity must not depend on that); nothing else in the layer changes. The **screen** is where the two consumers meet: `GameplayScreen.draw` decides *which* text to show — the nearest interactable trigger's prompt, while playing and with no dialogue line up — and calls the view, exactly as it already calls `DialogueRender.draw`. Nothing is added to `Gpu.fs`, `Mesh.fs`, the static path, or any shader, and no new pass or pipeline is created.

## Phase 0: Research

See [research.md](./research.md). It settles why the prompt is a view rather than inline or ImGui-drawn; why it is a bare bottom-centre line rather than a panel; how it stays legible without a panel; why the layer needs a third text slot; why the dialogue takes precedence over the prompt; and why the editor's read-out is removed rather than kept.

## Phase 1: Design

- [data-model.md](./data-model.md) — the trigger's authored `Interactable`/`Prompt` (already present, spec 029), the prompt's resolved form (a string), and the pure `PromptLayout` the view computes.
- [contracts/prompt-display.md](./contracts/prompt-display.md) — the surfaces that change: `render/Ui.fs`, the new `world/PromptRender.fs`, `screen/GameplayScreen.fs`, and what explicitly does not change.
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite and the interactive walkthrough.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — as checked: one state read, one tiny view module, one capacity bump, one removal of a debug overlay. |
| II. Fail Loudly | Pass — no new failure path; the layer's existing failures (font, measure) are unchanged. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — a plain string from a struct trigger, a pure layout, a reused batch. |
| V. Minimise GC Pressure | Pass — nothing allocates per frame; the string is read, not rebuilt, and measured through cached slots. |
| N-V. Testing is the Authoring Tool | Not engaged. |
