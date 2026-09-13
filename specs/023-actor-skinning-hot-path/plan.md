# Implementation Plan: Actor Skinning Hot Path

**Branch**: `023-actor-skinning-hot-path` | **Date**: 2026-09-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/023-actor-skinning-hot-path/spec.md`

## Summary

The Akane character has 166 joints and 13 submeshes. In a Debug build one actor
adds ~3 ms to `record`; in Release ~0.1 ms — the gap is unoptimized managed work in
`Actor.samplePose` (`world/Actor.fs`). Two changes reduce that work without
altering the sampled pose:

1. `sampleJoint` returns a **reference tuple** (`Vector3 * Quaternion * Vector3`),
   allocated twice per joint per frame in Debug (measured 56 B/call → ~18.6 KB/frame
   for 166 joints). Return a **struct tuple** instead.
2. `localMatrix` composes `Scale * Rotation * Translation` with two full matrix
   multiplies; because translation only sets the last row, build `Scale * Rotation`
   and write the translation directly — one multiply saved per joint.

The 10-way `match` in `sampleJoint` was considered and deliberately left alone:
at ~3 300 iterations/frame it is a small share of the cost, and unrolling it would
hurt readability for little gain.

Plus golden tests over a model with active channels, so the refactor is proven
output-identical.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`).

**Primary Dependencies**: `System.Numerics` (matrix/vector math). No new dependency.

**Storage**: N/A.

**Testing**: xUnit. `test/ActorTests.fs` already builds synthetic IQM models
headlessly and calls `Actor.samplePose`, so the sampler is fully testable without a
GPU. Add golden tests that populate `FrameChannels`/`ChannelMask` and assert the
expected `Local`/`World`/`Skinning` matrices (FR-004). The allocation removal
(FR-001) is verified by measurement/inspection; the frame-time targets (SC-002/003)
are measured in-game by the user.

**Target Platform**: Linux x64 desktop.

**Project Type**: native desktop game.

**Performance Goals**: at least 25% lower per-actor `record` in Debug (from ~3 ms);
no Release regression (~0.1 ms).

**Constraints**: Debug builds run with optimizations off (the fsproj sets no
`Optimize`), so value-type returns and elision matter; the change must be a pure
refactor of the sampler, with no signature/API change to `Actor`/`ActorRender` and
no change to the upload/draw path.

**Scale/Scope**: one module (`world/Actor.fs`) plus tests; no schema/interface
change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: three local refactors plus tests; no new abstraction.
- **II. Fail Loudly** — PASS: unaffected; the sampler's no-op guards are unchanged.
- **III. Spec-First** — PASS: `spec.md` precedes this plan and the tasks.
- **IV. Data-Oriented Architecture** — PASS: this is the letter of the principle —
  value types and struct tuples for hot-path data, mutation in place.
- **V. Minimise GC Pressure** — PASS: this is the change; it removes the sample
  path's per-frame allocations. AGENTS.md explicitly calls for struct tuples on hot
  paths.

Re-checked after Phase 1 design: no new obligations; still PASS.

## Project Structure

### Documentation (this feature)

```text
specs/023-actor-skinning-hot-path/
├── plan.md                  # This file (/speckit-plan output)
├── research.md              # Phase 0 output
├── data-model.md            # Phase 1 output
├── quickstart.md            # Phase 1 validation guide
├── contracts/
│   └── sampler-contract.md   # Behavioural contract for `Actor.samplePose`
├── checklists/
│   └── requirements.md      # /speckit-specify quality gate
└── tasks.md                 # /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
world/Actor.fs        # CHANGED: sampleJoint returns a struct tuple; localMatrix
                      #          writes the translation row directly
                      #          (channel sampling left unchanged)
test/ActorTests.fs    # CHANGED: golden tests over a model with active channels
                      #          (extend the model builder with frame channels)
```

**Structure Decision**: the change is confined to `world/Actor.fs` — the sampler is
pure CPU math with no GPU or Zone dependency. `ActorRender.upload`/`draw` are already
allocation-free (022) and their signatures are unchanged. Tests live with the
existing `ActorTests`.

## Complexity Tracking

No constitution violations to justify.
