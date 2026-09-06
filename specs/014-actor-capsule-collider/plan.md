# Implementation Plan: Actor Capsule Collider

**Branch**: `014-actor-capsule-collider` | **Date**: 2026-09-06 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/014-actor-capsule-collider/spec.md`

## Summary

Add a per-actor editor toggle that renders the actor's capsule collider as a
wireframe overlay, and correct the collider so it matches the rendered actor:
a fixed torso radius and the actor's full height, standing on the actor's feet.
This mirrors `odin-game`'s actor capsule setup (`DEFAULT_ACTOR_COLLIDER_RADIUS =
0.35`, feet-at-position, full-height capsule).

## Technical Context

**Language/Version**: F# (.NET 10)

**Primary Dependencies**: SDL3-CS, Dear ImGui (custom `cimgui` binding + SDL_gpu backend), JoltPhysicsSharp

**Storage**: N/A (in-memory editor/zone state)

**Testing**: xUnit (`fsharp-game.Tests`)

**Target Platform**: Linux desktop

**Project Type**: Desktop game + editor

**Performance Goals**: 60 fps interactive editor; collider wireframe is editor-only (not the gameplay hot path)

**Constraints**: Per-frame code should minimise allocations; the editor overlay is allowed small per-frame work

**Scale/Scope**: One active zone, a small number of actors per scene

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Minimalism** — PASS. The change adds one field, one ImGui binding, one debug-draw function, and a small collider-derivation change; no new abstractions.
- **Fail Loudly** — PASS. No silent fallbacks introduced; actor without an asset id still fails as before.
- **Spec-First** — PASS (documented retroactively). The spec captures the already-implemented behavior.
- **Data-Oriented Architecture** — PASS. `ShowCollider` is a value field on the `[<Struct>] Actor` record; collider derivation stays a pure function of bounds.
- **Minimise GC Pressure** — PASS. Debug draw accumulates into the pre-allocated line buffer; the fixed-radius collider is computed once at spawn/transform time, not per frame.

## Project Structure

### Documentation (this feature)

```text
specs/014-actor-capsule-collider/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
render/
├── ImGui.fs             # + igCheckbox P/Invoke binding
├── DebugDraw.fs         # + capsule wireframe primitive
world/
├── Entity.fs            # + Actor.ShowCollider field
├── Zone.fs              # capsule collider derivation + placement
editor/
├── Inspector.fs         # + Show Collider checkbox
├── Viewport.fs          # + draw capsule when enabled
test/
├── EntityTests.fs       # Actor record construction
├── PhysicsTests.fs      # capsuleCollider / capsuleCenter assertions
```

**Structure Decision**: No new directories; changes are additive in the existing
`render`/`world`/`editor`/`test` clusters per `docs/PROJECT_STRUCTURE.md`.

## Complexity Tracking

> No Constitution Check violations to justify.
