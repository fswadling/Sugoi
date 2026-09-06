# fsharp-game Constitution

## Core Principles

### I. Minimalism
Write only what is needed. Prefer small, focused modules over large,
speculative abstractions. No just-in-case fallbacks unless they are part of an
expected use case.

### II. Fail Loudly
Unexpected state is a bug. Prefer exceptions/asserts over silently returning
defaults. Use `Result`/`Option` for *expected* failure paths and
`failwith`/`invalidOp`/`assert` where an error is unrecoverable.

### III. Spec-First
Every feature starts as a spec under `specs/<NNN>-<name>/spec.md`, then moves
through plan, tasks, implement, and converge. Code is written to satisfy a
spec, not before one exists.

### IV. Data-Oriented Architecture
Prefer records and discriminated unions over inheritance and heavy object
graphs. F# supports structs, which enables the same generational-slot-map and
struct-of-arrays patterns used in `odin-game`. Use `[<Struct>]` records and
struct tuples for hot-path data created and consumed every frame, keep hot-path
data contiguous (`array`, `Span`, `Memory`), and make mutation explicit and
localized.

### V. Minimise GC Pressure
The game loop runs every frame. Prefer structs, value options, and reused
buffers in hot paths to avoid per-frame allocations and GC pauses. Do not
contort code to avoid allocation in cold paths — clarity first, then measure.

## Technology Stack

- Language: F#
- Build: .NET (`dotnet`)
- Windowing/Graphics: SDL3 (via the `SDL3-CS` .NET bindings)
- Editor UI: Dear ImGui (via a custom cimgui binding + SDL_gpu backend, `ImGui.fs`/`ImGuiSdl3.fs` + native `libcimgui.so`)
- Physics: Jolt Physics (via the `JoltPhysicsSharp` .NET bindings)
- Spec workflow: Spec Kit ("speckit")

## Development Workflow

- Feature specs live in `specs/`; the active feature is recorded in
  `.specify/feature.json` (override with `SPECIFY_FEATURE_DIRECTORY`).
- Run `/speckit-specify` to create a spec, `/speckit-plan` for design,
  `/speckit-tasks` for the task list, `/speckit-implement` to build, and
  `/speckit-converge` to verify completeness.
- Build with `dotnet build`, run with `dotnet run`, test with `dotnet test`.

## Governance

- This constitution supersedes other ad-hoc practices.
- Amendments require a documented rationale and a migration plan where the
  change affects existing specs or code.
- Complexity must be justified against the Minimalism principle.

## Amendments

### 2026-09-06 — Physics backend: Box3D → Jolt Physics

- **Rationale**: `Box3D` was referenced but never used in code. The physics +
  picking phase (`specs/013-physics-picking`) and later locomotion need a
  full-featured rigid-body engine; Jolt Physics provides a BVH broadphase,
  capsule/box/hull/mesh/heightfield shapes, ray casts, and a character
  controller. The maintained .NET binding is `JoltPhysicsSharp`.
- **Migration plan**: Replace the unused `Box3D` NuGet reference with
  `JoltPhysicsSharp` (+ `JoltPhysics.Native`). No code depends on `Box3D`, so
  there is no source migration.

**Version**: 1.1.0 | **Ratified**: 2026-08-27 | **Last Amended**: 2026-09-06
