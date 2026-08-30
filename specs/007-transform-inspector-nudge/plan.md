# Implementation Plan: Transform Inspector + Keyboard Nudge

**Branch**: `007-transform-inspector-nudge` | **Date**: 2026-08-30 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/007-transform-inspector-nudge/spec.md`

## Summary

Add the two missing editor-feedback pieces that round out phase 2c: (1) show the
full transform in the Inspector — rotation as Euler degrees and scale — so the
rotate/scale gizmos have observable output, and (2) port ocaml-game's keyboard
nudge (`Shift`+`W/A/S/D/Q/E`) with the two Shift-gates that stop the camera-fly
and snap-toggle from colliding with it.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`)

**Primary Dependencies**: `System.Numerics` (quaternion/Euler math), SDL3-CS (input),
the existing `ImGui`/`Input` bindings. No new packages.

**Storage**: in-memory only (the `Zone` on `GameplayState`).

**Testing**: xUnit for the pure quaternion→Euler helper (`test/EntityTests.fs`); the
UI interaction is validated manually via `quickstart.md`.

**Target Platform**: Linux x64 desktop (native).

**Project Type**: native desktop game.

**Performance Goals**: 60 fps; no per-frame allocation in the nudge path (reads the
`Input` bitmask, writes the struct transform in place).

**Constraints**: `GameplayState` is a `[<Struct>]`; the editor stays a layer that
reads/mutates gameplay state without owning it; nudge yields to ImGui text input and
to an in-progress gizmo drag.

**Scale/Scope**: one active zone, one selection, one tool.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: one Euler helper, one nudge function, two Shift-gates.
  No new modules; the oriented-box marker was explicitly deferred (real meshes
  arrive later).
- **II. Fail Loudly** — PASS: nudge with no selection is a guarded no-op; the Euler
  conversion is pure.
- **III. Spec-First** — PASS: derives from `spec.md`.
- **IV. Data-Oriented** — PASS: nudge direction is a small value type; no objects.
- **V. Minimise GC Pressure** — PASS: nudge reads the `KeyMask` bitmask and writes
  the struct transform; no allocation.

## Project Structure

### Documentation (this feature)

```text
specs/007-transform-inspector-nudge/
├── plan.md              # This file
├── research.md          # Phase 0 decisions
├── data-model.md        # Phase 1 data/behaviour model
├── quickstart.md        # Phase 1 validation guide
├── contracts/
│   └── transform-inspector-nudge.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
core/Entity.fs           # + eulerDegrees : Quaternion -> Vector3 (ZYX, degrees)
editor/Inspector.fs      # + rotation (degrees) + scale display
editor/Editor.fs         # + nudgeTransform + updateNudge; Q snap gated on not-Shift
screen/GameplayScreen.fs # camera fly gated on not-Shift; call Editor.updateNudge
test/EntityTests.fs      # + eulerDegrees tests
```

**Structure Decision**: `eulerDegrees` lives in `core/Entity.fs` (reusable, pure,
unit-testable) rather than inline in `Inspector.fs`. The nudge behaviour lives in
`editor/Editor.fs` alongside `dragTransform`, keeping `editor/EditorState.fs`
data-only.

## Complexity Tracking

No constitution violations to justify.
