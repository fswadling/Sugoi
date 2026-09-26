# Implementation Plan: Actor Animation Slots with Weights

**Branch**: `036-actor-animation-slots` | **Date**: 2026-09-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/036-actor-animation-slots/spec.md`

## Summary

The renderer holds one animation; this holds up to eight and blends them. `ActorRender`'s single `Animation : AnimationSlot` becomes `AnimationSlots : SlotMap<AnimationSlot>` (capacity 8, `Actor.MaxAnimationSlots`), and the per-frame pose becomes a weighted average of every contributing slot — joint translations and scales averaged linearly, rotations combined by an incremental slerp, and an actor with nothing contributing posed at its bind pose. A single slot at weight 1 reproduces today's pose exactly (FR-011), so the existing animation and skinning tests are the regression guard.

The blend is pure CPU math and lives in `world/Actor.fs`, beside the sampler it shares code with, so it is testable headlessly — the whole `ActorTests` file already drives it with no GPU: `blendPose` over the slot map's span, `advanceSlotsInPlace`, and the slot algebra `setSlotWeight` (find-or-add, clamped) and `removeSlot`. `samplePose` is refactored onto the same per-joint sampler so the channel-sampling math exists once; `023`'s golden tests and its no-allocation test guard that refactor.

The editor gains an **Animations** section in the actor Inspector: each held clip as a row with a weight drag and a remove button, and a button per unheld clip to add it. It writes straight to the runtime renderer through the same weight-setting call the gameplay caller uses (FR-020) — no command, no persisted field, and no new editor state — which is the authored/derived/runtime classification the command model already enforces (FR-019).

No new file, no `.fsproj` change: one field, four functions in `Actor`, three in `ActorRender`, and the Inspector's actor block. The idle↔walk cross-fade is not here (FR-013); `Systems.animate`'s hard switch keeps working through `playClip`, unchanged.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).

**Primary Dependencies**: none new. `System.Numerics` for the blend; the existing `core/SlotMap.fs` for storage; the existing ImGui binding (`dragFloat`, `smallButton`, `pushId`/`popId`) — **no new binding**, so `native/libcimgui.so` and `render/ImGui.fs` are untouched.

**Storage**: N/A. Slots are runtime state on the renderer; nothing is persisted, and no manifest or save field changes.

**Testing**: `test/ActorTests.fs` is extended — no new test file, no `.fsproj` change. It already builds synthetic `Iqm.Model` records and drives `Actor.samplePose`/`createSlot`/`advance` with no GPU, so the blend and the slot algebra are fully headless:

- `blendPose` of two slots at equal weight is the midpoint at every joint — translation and scale by value, rotation by `Quaternion.Slerp` — and of one slot at weight 1 equals `samplePose` for that clip and frame (FR-003, FR-004, FR-011).
- All-zero weights, and an empty slot map, pose the bind pose (FR-008).
- `advanceSlotsInPlace` advances enabled slots and skips disabled ones (FR-009).
- `blendPose` allocates nothing per frame, mirroring `023`'s measurement (FR-012).
- `setSlotWeight` finds-or-adds and clamps; `removeSlot` frees; a ninth add raises (FR-002, FR-006, FR-007).

What no test reaches is the renderer's name-resolving wrappers (`setClipWeight`/`removeClip` resolve a clip name before delegating) and the Inspector section itself — those are [quickstart.md](./quickstart.md), as `035` left its panel lines to its quickstart.

**Target Platform**: Linux x64 desktop, SDL3 + SDL_gpu.

**Project Type**: single-project native desktop game with a docked ImGui editor.

**Performance Goals**: hold 60 FPS. The blend is on the per-actor play-mode path (`GameplayScreen.upload`), so it must not allocate and must stay close to today's single-clip cost. With ≤8 slots it does more work than one clip, but the per-joint loop has the same shape and the extra slots add only weighted sums; the frame-window arithmetic (a modulo and a clamp per slot per joint) is the cheap part `023` already found not to dominate. The Inspector section is edit-only and off the play path.

**Constraints**: a single slot at weight 1 is the same pose as before the feature (FR-011, SC-002); no per-frame allocation (FR-012, SC-005); capacity 8 with a loud failure beyond it (FR-002, SC-004); the Inspector section is a direct runtime write — no command, no persisted field (FR-019, SC-009); everything else in the Inspector and editor is unchanged (FR-020, FR-006/SC-006).

**Scale/Scope**: one field on `ActorRender`; `world/Actor.fs` gains the blend and the slot algebra; `world/ActorRender.fs` rewires its four functions and gains two; `editor/Inspector.fs` gains the actor section; `test/ActorTests.fs` extends. No new file, no new type beyond the existing `SlotMap`, no `.fsproj` change, no command, no editor-state field.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one field, four pure functions, three renderer verbs, and an Inspector block. No new module, type, command, or editor-state field; the reference editor's frame-scrub, playback-rate, loop and Play/Stop controls are deliberately not built (spec assumptions). |
| II. Fail Loudly | Pass — an unknown clip name raises (FR-010); an out-of-range clip index raises; the ninth add raises through `SlotMap.Add`'s full-map `invalidOp` (FR-002). The Inspector *reports* the degenerate cases rather than raising, because there the condition is the author's to see, not a bug (FR-017). |
| III. Spec-First | Pass — `specs/036-actor-animation-slots/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — the slots are a `SlotMap` of `[<Struct>]` `AnimationSlot`s, scanned as a `Span`; the blend writes reused `Matrix4x4` arrays in place through per-joint locals, with no scratch allocation. |
| V. Minimise GC Pressure | Pass — this is the letter of the principle on the play path: the blend is allocation-free and the per-frame pose keeps its shape. The Inspector's per-frame `sprintf` row labels are editor-only, matching the transform readout already there. |
| N-V. Testing is the Authoring Tool | Not engaged — it governs the narrative subsystem, which is untouched. |

## Complexity Tracking

None. The only structural addition — a slot map on the renderer — is the thing asked for, and it reuses the existing `SlotMap` rather than introducing a parallel structure; the blend's per-joint shape is the reference engine's, chosen so it allocates nothing.

## Project Structure

### Documentation (this feature)

```text
specs/036-actor-animation-slots/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── animation-slots.md  # Phase 1 output
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
world/
├── Actor.fs            # CHANGED: `+ MaxAnimationSlots`; `+ blendPose`, `+ advanceSlotsInPlace`,
│                       #   `+ setSlotWeight`, `+ removeSlot`; `AnimationSlot.Weight` and
│                       #   `.Enabled` become `mutable`; `samplePose` refactored onto the shared
│                       #   per-joint sampler (`sampleJointTRS` + `composeJoint`) that `blendPose`
│                       #   now shares. Gains a `core/SlotMap.fs` dependency (compiled above it).
├── ActorRender.fs      # CHANGED: `Animation : AnimationSlot` → `AnimationSlots : SlotMap<AnimationSlot>`;
│                       #   `create` seeds one slot (Idle, else the first clip, else none);
│                       #   `advanceInPlace` advances all slots; `upload` blends; `+ setClipWeight`,
│                       #   `+ removeClip`; `playClip` re-expressed as "target at 1, others zeroed".
│                       #   Header comment updated (it describes the odin `Actor_Renderer` shape).
└── Systems.fs          # UNCHANGED: `animate` still calls `ActorRender.playClip` — FR-013 defers the
                        #   cross-fade, so the hard switch stands.
editor/
└── Inspector.fs        # CHANGED: the actor block gains an `Animations` collapsing header — a row per
                        #   held clip (name, weight `dragFloat`, remove `smallButton`) and a
                        #   `smallButton` per unheld clip to add; the three degenerate cases are text.
test/
└── ActorTests.fs       # EXTENDED: the blend, the slot algebra, the bind-pose fallback, and the
                        #   allocation check (see Testing above).
docs/
├── EDITOR_DESIGN.md    # CHANGED: D-011's direct-write list (cast binding, zone registry) gains the
│                       #   Inspector's animation section — the third surface that edits runtime state
│                       #   rather than recording a command. D-017 needs no change: no new widget.
└── ROADMAP.md          # CHANGED (small): Phase 3's `world/Actor.fs` line says "animation/morph state";
                        #   the port has no morphs and that state is now multi-slot weights.
```

**Structure Decision**: no new module and no new file, which is the point. The blend belongs in `world/Actor.fs` because it is pure CPU math with no GPU dependency, and that is the only module that can be driven headlessly (the existing tests prove it); putting it in `ActorRender` would make it untestable, since building one needs a GPU device. The slot map belongs on the renderer because that is the per-actor runtime resource that already owns the pose, the bone buffers and the asset pointer. The Inspector block belongs in `Inspector.fs` because the Inspector is the single editing surface for a selection, and an actor's slots are a property of the selected actor. `Systems.fs` does not move: the cross-fade it would drive is exactly what FR-013 defers.

## Phase 0: Research

See [research.md](./research.md). It settles why the slots are a `SlotMap` and not an array, why the blend lives in `Actor` while the name-resolving setter lives in `ActorRender`, why translations average linearly but rotations use an incremental slerp, why `samplePose` is refactored rather than duplicated, what weight an added clip takes, and why the Inspector section is a direct write with no command and no new editor state.

## Phase 1: Design

- [data-model.md](./data-model.md) — `AnimationSlot` (with its two new mutable fields), the actor's slot set, the blended pose and the bind-pose fallback, and the authored-versus-runtime boundary that keeps the slots out of every command payload.
- [contracts/animation-slots.md](./contracts/animation-slots.md) — the surfaces that change: `Actor`, `ActorRender`, and the Inspector section, plus what explicitly does not change (`Systems`, `Command`, `EditorState`).
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite, and the interactive Inspector walkthrough including the three degenerate cases.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — as checked: no new file, type, command, or editor-state field; the renderer's four functions are rewired rather than added to, plus two verbs. |
| II. Fail Loudly | Pass — name and index validation raise; over-capacity raises; the Inspector's degenerate cases are reports, not silent no-ops. |
| III. Spec-First | Pass. |
| IV. Data-Oriented | Pass — a contiguous `SlotMap` of structs scanned as a `Span`; per-joint locals, no scratch arrays. |
| V. Minimise GC | Pass — the blend allocates nothing; the only new per-frame strings are the Inspector's editor-only labels. |
