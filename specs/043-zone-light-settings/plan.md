# Implementation Plan: Zone Light Settings

**Branch**: `043-zone-light-settings` | **Date**: 2026-09-30 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/043-zone-light-settings/spec.md`

## Summary

The scene's sun is a constant compiled into the one place that builds it
(`screen/GameplayScreen.fs`): `Direction = normalize(0.4, 0.8, 0.4)`, colour white, ambient
`0.15`. It lights static geometry and the cel-shaded actors alike and nothing can change it.

Give **each zone its own light settings** — the four `odin-game` exposes: a sun **pitch**, a sun
**yaw**, a light **colour** and an **ambient** intensity — and an editor surface to author them.
The renderer derives the shader-facing `Light` from the active zone's settings (the same
spherical convention the reference uses), so the scene lights under the zone it is in rather
than under a constant. The editor's **Inspector** gains a "Lighting" section, shown for the
active zone when no entity is selected (the Inspector's fallback subject, so no new selection
state), and a committed change is one `SetLight` undo step — a change commits when its control is
released or left. The registered-zone record (`ZoneTemplate`) carries the settings
too, so **registering** captures the light and **instantiating** restores it in the same single
undo step that already swaps the environment and its props.

A zone with untouched settings must render exactly as today (FR-004): the default pitch/yaw is
chosen so the derived direction is `normalize(0.4, 0.8, 0.4)` and the default ambient is the
port's existing neutral `0.15`. The idealised face light of spec 040 is *not* touched — the
`Face` mesh keeps its own direction; only the band *colours* follow the scene sun (research D8).

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).

**Primary Dependencies**: **none new.** The one new native binding is `igColorEdit3`, which is
already in the vendored cimgui (`native/cimgui/cimgui.h`:
`bool igColorEdit3(const char* label, float col[3], ImGuiColorEditFlags flags)`) and in the
committed `native/libcimgui.so`; `render/ImGui.fs` P/Invokes it like the widgets already bound.
`igDragFloat` (the three scalar controls), `SDL`/`SDL_gpu` and everything else are already used.

**Storage**: **none.** The settings are session state — a zone's field, rebuilt from the default
on every run. Nothing is written to disk (the project has no persistence yet, ROADMAP phase 15).
No authored format changes.

**Testing**: the derivations are pure and are covered headlessly (a new `test/LightTests.fs` for
the pitch/yaw → direction mapping, and the template carry in `test/ZoneTests.fs` /
`test/SpawningTests.fs`). Everything GPU-visible — the render following the settings, the
Inspector's committed edit, the colour picker, the light restored on instantiate — is the
quickstart walkthrough, the same split every prior rendering spec uses (tests cannot open a GPU
device).

**Target Platform**: Linux x64 desktop, SDL3 + SDL_gpu (Vulkan backend).

**Project Type**: single-project native desktop game with a docked ImGui editor.

**Performance Goals**: hold the port's 60 FPS cadence. The light is resolved **once per frame**
from the active zone and passed to the existing draws; no per-frame allocation is added (FR-013),
and the colour picker's scratch buffer is reused (research D9). No explicit frame-rate budget.

**Constraints**: four authored settings per zone (FR-001); the active zone's settings drive the
scene's one sun for static geometry and actors (FR-002); the controls in the Inspector for the
active zone, a change reflected immediately (FR-003); defaults identical to today's light
(FR-004); zones independent (FR-005); session state, one undo step per committed change, never
persisted (FR-006); the record carries the settings and instantiate
restores them in one step (FR-007); the reference's spherical convention, unit direction (FR-008);
bounded controls (FR-009); the idealised face light unchanged (FR-010); cel shading and outline
otherwise unchanged (FR-011); shadows untouched (FR-012); no per-frame allocation (FR-013);
existing behaviour and tests unchanged (FR-014, FR-015).

**Scale/Scope**: `render/Texture.fs` gains the `LightSettings` record, its default and the
`toLight` derivation beside `Light`; `world/Zone.fs` gains a `mutable Light` field and threads it
through `captureTemplate`; `world/ZoneTemplate.fs` gains a `Light` field and an `ofEnvironment`
parameter; `state/Command.fs` gains `SetLight`; `editor/Spawning.fs` sets the light inside the
instantiate `Batch`; `editor/Inspector.fs` gains the "Lighting" section (the active zone when no
`editor/Inspector.fs` gains the "Lighting" section (the active zone when no
entity is selected); `editor/Outliner.fs` changes one call site so a zone row brings its zone up
(spec 043's "selecting it" includes the zone already active); `state/GameplayState.fs` gains
`selectZone`; `render/ImGui.fs` gains
`colorEdit3`; `state/EditorState.fs` gains the reused colour buffer; `screen/GameplayScreen.fs`
derives the light from the zone; `docs` gain a short section. No new module, no new pass, no
shader, no dependency.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — a four-field record, a pure derivation, a `mutable` field on two records, one command variant, one new ImGui binding and one `GameplayState.selectZone`. No new module, pass, shader, target or dependency. The new `SetLight` command serves both routes an author can change the light (research D7, D8) rather than introducing a second mechanism. |
| II. Fail Loudly | Pass — no new failure path: the ranges are bounded by the controls (FR-009) and a pitch/yaw always yields a unit direction. Nothing silently falls back. |
| III. Spec-First | Pass — `specs/043-zone-light-settings/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — a `[<Struct>]` record of four plain values on the existing records; mutation is the `mutable` field `Zone` already uses for `Environment`/`NextEnvironmentEpoch`. No object graph. |
| V. Minimise GC Pressure | Pass — the per-frame path is unchanged except that the `Light` struct is built from the zone instead of a literal (FR-013); the editor's colour array is reused (research D9). |
| N-V. Testing is the Authoring Tool | Not engaged — narrative subsystem, untouched. |

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

None. `SetLight` is the feature's one new command, and it is needed by both routes an author can
change the light — the Inspector's committed edits (FR-006) and an instantiate that restores the
recorded light (FR-007). `GameplayState.selectZone` is two lines over the existing `switchZone`, and
the Outliner's change is a single call site. No new module, pass, shader, target or dependency.

## Project Structure

### Documentation (this feature)

```text
specs/043-zone-light-settings/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── zone-light.md    # Phase 1 output — the surfaces that change
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
render/
├── Texture.fs           # CHANGED: add `LightSettings` (pitch, yaw, colour, ambient), its
│                        #   default and `toLight : LightSettings -> Light`, beside `Light`.
└── ImGui.fs             # CHANGED: bind `igColorEdit3` (the only new widget).

world/
├── Zone.fs              # CHANGED: `mutable Light : LightSettings` on `Zone`; `create` seeds the
│                        #   default; `captureTemplate` records it.
└── ZoneTemplate.fs      # CHANGED: `Light : LightSettings` field; `ofEnvironment` takes it.

state/
├── GameplayState.fs      # CHANGED: `selectZone` — bring a zone up in the editor, the zone already
│                        #   active included (a plain `switchZone` no-ops there).
├── Command.fs           # CHANGED: `SetLight` (previous/next settings), applied and undone; used
│                        #   by the Inspector's committed edits and by the instantiate batch.
├── EditorState.fs       # CHANGED: a reused `float32` colour buffer for the picker.
editor/
├── Inspector.fs         # CHANGED: the active zone's "Lighting" section (four controls), shown
│                        #   when no entity is selected; one `SetLight` step per committed change.
├── Outliner.fs          # CHANGED: a zone row calls `selectZone`, so the click always brings the
│                        #   zone into the Inspector.
└── Spawning.fs          # CHANGED: instantiate sets the light in its `Batch`.

screen/
└── GameplayScreen.fs    # CHANGED: build `Light` from the active zone's settings.

test/
├── LightTests.fs        # NEW: the pitch/yaw → direction derivation and the defaults.
├── ZoneTests.fs         # CHANGED: `captureTemplate` carries the light.
├── SpawningTests.fs     # CHANGED: instantiate restores the light; undo restores the previous.
└── fsharp-game.Tests.fsproj  # CHANGED: add `LightTests.fs` to the compile list.

docs/
└── EDITOR_DESIGN.md     # CHANGED: a note on the Lighting section and the live-write decision.
```

**Structure Decision**: the authored settings and their derivation live with **`Light`** in
`render/Texture.fs` — the shader-facing type and its authoring source belong together, and the
world modules already consume `Texture` types (`Light`, `Material`). The live value lives on the
the **`Zone`** (the container that already owns zone-level state and is already mutable), the recorded
copy on **`ZoneTemplate`** (whose doc comment anticipates exactly this), and the editor surface in
the **Inspector**, as its fallback subject when no entity is selected. No new module, no new
panel, no screen or shader change.

## Phase 0: Research

See [research.md](./research.md). It settles the four settings and their authoring form (D1), where
the type lives and why (D2), the derivation and the appearance-preserving defaults (D3, D4), the
ambient expansion (D5), the editor surface and its control kinds (D6), the commit-and-undo model
(D7), the recorded light and the one-step instantiate (D8), the reused colour buffer (D9) and what
is tested headlessly (D10).

## Phase 1: Design

- [data-model.md](./data-model.md) — the `LightSettings` record, the `Zone`/`ZoneTemplate` fields,
  the `SetLight` command, the derivation and its ranges, the authored-vs-runtime boundary and the
  validation rules.
- [contracts/zone-light.md](./contracts/zone-light.md) — the surfaces that change
  (`Texture`, `Zone`, `ZoneTemplate`, `Command`, `Spawning`, `Inspector`, `ImGui`, `EditorState`,
  `GameplayScreen`) and what explicitly does not (shaders, the cel/outline pipelines, the static
  path, the face light, the Outliner).
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite and the run
  walkthrough (the four controls, zone independence, the unchanged default, register/instantiate).

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one record, one derivation, two fields, one command variant, one binding; no new module/pass/shader/dependency. |
| II. Fail Loudly | Pass — bounded controls, a total derivation; no fallback path. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — a struct of four values on existing records; mutation via the `mutable` field already used. |
| V. Minimise GC Pressure | Pass — the frame builds one more small struct from the zone; the editor reuses its colour array. |
| N-V. Testing is the Authoring Tool | Not engaged. |
