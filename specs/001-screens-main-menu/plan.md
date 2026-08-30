# Implementation Plan: Screens + Main Menu

**Branch**: `001-screens-main-menu` | **Date**: 2026-08-27 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-screens-main-menu/spec.md`

## Summary

Build the app shell: a **screen manager** that owns the active screen and
dispatches per-frame update/draw to it, a **main menu** screen, and a **UI-only
gameplay** screen, navigable menu ↔ gameplay and cleanly quittable. This is the
foundation every later phase builds on. The engine loop becomes a thin,
screen-agnostic dispatcher; screens own their update/draw.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`, `Exe`)

**Primary Dependencies**: SDL3-CS (windowing/GPU), custom Dear ImGui binding
(`ImGui.fs` + native `libcimgui.so`), Box3D (referenced, unused this phase)

**Storage**: N/A (no persistence this phase)

**Testing**: Manual via `dotnet build` + `dotnet run`; no test framework yet

**Target Platform**: Linux x64 desktop (native)

**Project Type**: native desktop game

**Performance Goals**: 60 fps (already capped in the loop)

**Constraints**: No per-frame allocations on the screen-dispatch hot path

**Scale/Scope**: 2 screens (Main Menu, Gameplay) growing later

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: only the screen kinds needed (Main Menu, Gameplay),
  no speculative screen types.
- **II. Fail Loudly** — PASS: an unknown/invalid screen kind or failed transition
  raises rather than silently no-oping.
- **III. Spec-First** — PASS: this plan derives from `spec.md`.
- **IV. Data-Oriented** — PASS: `Screen_Manager` is a small struct-like record;
  screens are discriminated cases over small state records, not class hierarchies.
- **V. Minimise GC Pressure** — PASS: the hot path is a single dispatch on the
  active screen; no per-frame allocation.

## Project Structure

### Documentation (this feature)

```text
specs/001-screens-main-menu/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── screen.md
└── tasks.md            # /speckit-tasks (not created here)
```

### Source Code (repository root)

```text
screen/
├── Screen.fs           # Screen_Kind, Screen_Transition
├── ScreenManager.fs    # active screen + dispatch
├── MainMenuScreen.fs   # main menu
├── GameplayScreen.fs   # UI-only gameplay placeholder
├── GameplayState.fs    # (empty) gameplay screen state
└── SharedState.fs      # minimal shared state (empty this phase)

Engine.fs               # extracted run loop (thin, screen-agnostic)
Program.fs              # [<EntryPoint>] main → Engine.run
render/ImGui.fs         # moved from root (existing)
render/ImGuiSdl3.fs     # moved from root (existing)
```

**Structure Decision**: Follow `docs/PROJECT_STRUCTURE.md`. The `screen/` cluster
lands under `screen/`, `Engine.fs` is split out of `Program.fs`, and the existing
`ImGui.fs`/`ImGuiSdl3.fs` move to `render/` (pure moves, module names unchanged).

## Complexity Tracking

No constitution violations to justify.
