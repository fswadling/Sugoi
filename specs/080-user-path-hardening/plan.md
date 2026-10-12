# Implementation Plan: Graceful Failure on User-Facing Paths

**Branch**: `080-user-path-hardening` | **Date**: 2026-10-12 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/080-user-path-hardening/spec.md`

## Summary

Convert the audit's **user-reachable** fatal sites into the no-effect-and-report (or skip-and-report)
pattern the cue engine already uses, and add a top-level boundary so a genuinely unexpected failure is
reported and the run ends with a controlled teardown. Fail-fast stays for programming bugs: the change
is a line drawn between *programmer error* (still fatal) and *user-reachable state* (handled), not a
weakening of `Fail Loudly`.

Eight small, mostly independent changes: the locomotion-clip fallback; the camera cue ops and a bare
scenario-fork op; the cue editor's conditional branch; the history runner's skip-and-report; creation
capacity pre-checks; scenario-capture tolerance of dangling links; report-and-skip asset loading; and
the frame-loop boundary.

## Technical Context

**Language/Version**: F# 8 / .NET `net10.0`

**Primary Dependencies**: `SDL3-CS` (window/GPU/ttf), `JoltPhysicsSharp` (+ native `joltc`), `Prime`
(composition + symbolic expressions). No new dependency is introduced.

**Storage**: In-memory runtime state; asset files and manifests on disk. Nothing is persisted by this
feature.

**Testing**: xUnit, headless (`test/fsharp-game.Tests.fsproj`). The suite already builds a
`GameplayState` with `Unchecked.defaultof` GPU pieces and a device-free asset manager
(`test/TestSupport.fs`), so the cue-engine and history behaviours below are unit-testable without a
device; GPU-backed renderer paths are covered by the quickstart scenarios.

**Target Platform**: Desktop (Linux is the dev platform); single-process app.

**Project Type**: Desktop application — a data-oriented game engine plus an ImGui editor.

**Performance Goals**: No change to the 60 fps frame budget. No new per-frame allocation (SC-006):
diagnostics are emitted only when an event happens, never per frame.

**Constraints**: Preserve existing fail-fast for programming bugs (FR-012); no new UI surface; keep the
`.fsproj` compile order (all touched files already exist and stay in place).

**Scale/Scope**: ~25 kLOC across 8 clusters; this feature touches ~10 modules and adds focused coverage.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. Each change is a small guard at an existing site; no new module, type, or
  abstraction is introduced. Diagnostics reuse the existing `eprintfn` convention.
- **II. Fail Loudly** — PASS (with the feature's whole point). Fail-fast is *narrowed*, not removed:
  every state reachable from a user action or an authored asset is handled; states only a programming
  error can produce (stale slot handles, double-free, `Occupy` on a taken slot, broken invariants) keep
  their `failwith`/`invalidOp` (FR-012). No violation to justify.
- **III. Spec-First** — PASS. This plan and its artifacts are produced before implementation.
- **IV. Data-Oriented Architecture** — PASS. No struct, record, or container shape changes; no hot-path
  layout is affected.
- **V. Minimise GC Pressure** — PASS. The guards run on events, not per frame; the diagnostics allocate
  only when a failure occurs. The frame path is otherwise untouched.

No violations — **Complexity Tracking is empty**.

## Project Structure

### Documentation (this feature)

```text
specs/080-user-path-hardening/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/
│   └── user-path-hardening.md   # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command — NOT created here)
```

### Source Code (repository root)

Single project, existing domain clusters. No files are added or moved; the touched files keep their
place in `fsharp-game.fsproj`.

```text
fsharp-game/
├── Engine.fs                    # frame loop: add the top-level boundary (US8)
├── Program.fs                   # entry: wrap Engine.run for a startup failure (US7/US8)
├── core/
│   └── SlotMap.fs               # read-only use of Capacity/Count by the capacity guards (US5)
├── world/
│   ├── ActorRender.fs           # playClip: fall back when the clip is absent (US1)
│   ├── CueEngine.fs             # camera ops no-op; bare Fork runs its inner cue (US2)
│   ├── ScenarioZone.fs          # capture links through the tolerant accessor (US6)
│   └── Assets.fs                # scan/load: report-and-skip a bad asset (US7)
├── state/
│   └── History.fs               # runner skips-and-reports a stale step (US4)
├── editor/
│   ├── CueTree.fs               # withChildren handles the conditional shape (US3)
│   ├── Spawning.fs              # creation refused at capacity (US5)
│   ├── Outliner.fs              # create controls gated on room (US5)
│   └── AssetBrowser.fs          # create controls gated on room (US5)
└── test/                        # focused additions per module (flat, one file per module)
```

**Structure Decision**: Extend the existing clusters in place. Every changed file already exists and
already sits at the correct point in the compile order, so no `.fsproj` edit is required. New test cases
go into the existing `test/<Module>Tests.fs` for their subject.

## Complexity Tracking

> No Constitution Check violations — this section is intentionally empty.
