# Implementation Plan: Inspector Environment Readout

**Branch**: `051-inspector-environment-readout` | **Date**: 2026-10-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/051-inspector-environment-readout/spec.md`

## Summary

The Outliner's Zones section is followed by a read-only line, `Environment: <name>`, naming
the active zone's loaded environment (`editor/Outliner.fs`). It is the only zone-level
readout in a panel whose subject is the scene's structure — the places and entities — while
the zone's other settings already live in the Inspector: spec 043's **Lighting** section
draws for the active zone when no entity is selected, the Inspector's *fallback subject*.

**Move the line.** Delete the readout from the Outliner and draw it in the Inspector, in
the same no-selection branch, directly above the Lighting section. The value shown is
unchanged — the active zone's environment name, or `(none)` — so the cut is a relocation,
not a rewording: the environment and the light are both things that describe the zone, and
they read together. Nothing is added but one read-only text line; no new state, command,
undo step, panel, or dependency, and the Outliner's Zones list is otherwise untouched.

The accepted consequence, recorded in the spec's Assumptions: while an entity is selected
the environment name is no longer visible anywhere, because it joins the zone's settings
under the fallback model. That is the point of the change — a zone's settings are read in
one panel, and only while the zone is what is being inspected.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).

**Primary Dependencies**: **none new.** The readout is `ImGui.text`, already bound and
already used for exactly this string (`editor/Outliner.fs`). No new native binding.

**Storage**: **none.** The feature only chooses where an existing value is drawn; it writes
nothing and reads no authored file.

**Testing**: no headless test. The change is ImGui draw code — two panel functions that
cannot run without a GPU device and a frame — so the walkthrough in
[quickstart.md](./quickstart.md) is the validation, the same split every prior editor-panel
change uses (`specs/043`'s Inspector section, `specs/050`'s residency glyph). Nothing pure
is added to test.

**Target Platform**: Linux x64 desktop, SDL3 + SDL_gpu (Vulkan backend).

**Project Type**: single-project native desktop game with a docked ImGui editor.

**Performance Goals**: unchanged. The frame draws one text line in a different panel; no
per-frame allocation is added (the name is read from the zone, no formatting done today is
removed or added).

**Constraints**: display-only (FR-008); no `Environment:` line in the Outliner (FR-001);
the readout in the Inspector, above Lighting, when no entity is selected (FR-002, FR-003);
it names the active zone's environment, `(none)` when absent (FR-004); it follows the active
zone and instantiation without manual refresh (FR-005); read-only, no new panel or control
(FR-006); an entity selection is unchanged and shows no readout (FR-007).

**Scale/Scope**: two files. `editor/Outliner.fs` loses the readout block; `editor/Inspector.fs`
gains a small `drawEnvironment` and a call to it. No new module, no new binding, no new
state, no test, no persistence, no engine change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — the change is a deletion in one panel and one read-only line in another, placed with the zone settings already there. No new module, field, command, panel, or dependency. |
| II. Fail Loudly | Pass — no new failure path. `zone.Environment` is the same `option` the Outliner already matched; `None` draws `(none)`, which is expected, not an error. |
| III. Spec-First | Pass — `specs/051-inspector-environment-readout/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — no data change. The readout reads the existing `zone.Environment` record; no new type, no object graph. |
| V. Minimise GC Pressure | Pass — no per-frame allocation is added; the frame builds one format string in the Inspector where it built one in the Outliner. |
| N-V. Testing is the Authoring Tool | Not engaged — narrative subsystem, untouched. |

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

None. There is nothing to justify: the feature adds no structure, only relocates a display
line. The one design decision with a user-visible cost — the readout is absent while an
entity is selected — is the existing fallback model's, and is recorded in the spec's
Assumptions rather than paid for with new state (research D2).

## Project Structure

### Documentation (this feature)

```text
specs/051-inspector-environment-readout/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── environment-readout.md   # Phase 1 output — the surfaces that change
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
editor/
├── Outliner.fs          # CHANGED: delete the `Environment: <name>` block between the Zones
│                        #   section and the Actors section; `let zone = GameplayState.activeZone
│                        #   state` stays (the entity sections still use it).
└── Inspector.fs         # CHANGED: add `drawEnvironment zone` (read-only text, `(none)` when
                         #   the zone has no environment) and call it in the no-selection branch
                         #   just above `drawLighting`.

docs/
└── EDITOR_DESIGN.md     # CHANGED: the Inspector module-map entry names the environment
                         #   alongside lighting; the Lighting-section note already describes the
                         #   fallback subject the readout joins.

specs/
└── 011-environment-parts-props/quickstart.md   # CHANGED: scenario 8's "the Outliner's top
                                                #   line shows Environment:" becomes the Inspector
                                                #   readout above the Lighting section.
```

**Structure Decision**: the readout belongs beside the zone's other settings, so it is drawn
in the **Inspector**, in the branch that already takes the active zone as its subject when
nothing is selected — the same place spec 043 put the zone's light, and for the same reason.
The value stays on the **zone** (`zone.Environment`), where it already lives; this feature
adds no field. The Outliner's Zones section keeps only what it is about — the project's
zones and their controls — so the line is deleted there rather than duplicated.

## Phase 0: Research

See [research.md](./research.md). It settles where the readout lives and why (D1), why it is
absent while an entity is selected (D2), that it is read-only and adds no command (D3), that
it does not couple to residency (D4), that the value and its `(none)` fallback are unchanged
(D5), and what can and cannot be tested (D6).

## Phase 1: Design

- [data-model.md](./data-model.md) — no new data: the readout reads the existing zone
  environment, and the record types the feature must not change.
- [contracts/environment-readout.md](./contracts/environment-readout.md) — the two surfaces
  that change (`Outliner`, `Inspector`) and what explicitly does not (zone state, commands,
  the undo stack, residency, persistence).
- [quickstart.md](./quickstart.md) — runnable validation: the readout in the Inspector above
  Lighting, its absence from the Outliner, following the active zone, and the entity-selected
  case.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — a deletion and one read-only line; no new module, field, command, or dependency. |
| II. Fail Loudly | Pass — same `option` match, `None` reads `(none)`; no fallback path introduced. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — no data change; reads the existing `zone.Environment`. |
| V. Minimise GC Pressure | Pass — no new per-frame allocation. |
| N-V. Testing is the Authoring Tool | Not engaged. |
