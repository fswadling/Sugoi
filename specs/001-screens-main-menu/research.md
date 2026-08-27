# Research: Screens + Main Menu

Decisions resolving the open technical choices for this feature.

## Decision: screen model = discriminated union + small state records

- **Decision**: `Screen_Kind` (MainMenu | Gameplay | Settings) discriminates the
  active screen; each screen carries a small state record (`GameplayState`,
  empty for now). `Screen_Manager` holds the active kind + its state and a
  `Screen_Transition` for navigation.
- **Rationale**: matches the data-oriented architecture (`docs/ARCHITECTURE.md`)
  and ocaml-game's `Screen_Manager`/`Gameplay_state` split. No class hierarchy,
  no per-screen closures.
- **Alternatives considered**: interface + object model (rejected — heavier, GC
  churn, fights the data-oriented principle); a single flat `Game_State` record
  with a `mode` enum (rejected — conflates screens with game state).

## Decision: engine loop is a thin, screen-agnostic dispatcher

- **Decision**: `Engine.run` only calls `Screen_Manager.update`/`draw` and
  presents the result; it knows nothing about specific screens. `Program.fs`
  becomes `[<EntryPoint>] main → Engine.run`.
- **Rationale**: mirrors odin-game's `main.odin` and ocaml-game D-021. Keeps the
  loop stable as screens are added.
- **Alternatives considered**: screens reach up into the loop (rejected — would
  entangle the loop with screen internals and create module cycles).

## Decision: direct (instant) screen transition, no animation

- **Decision**: `Screen_Transition` is a simple `Next Screen_Kind | Quit`, applied
  immediately at the end of the current frame.
- **Rationale**: transitions/fade are not in scope for the shell; a simple switch
  satisfies FR-003 with the least code.
- **Alternatives considered**: fade/transition animation (deferred — YAGNI for
  this milestone).

## Decision: minimal `SharedState` stub now, filled later

- **Decision**: introduce `SharedState` as an empty record owned by the screen
  manager, threaded through `update`/`draw`, to be populated in later phases
  (assets, cues, cast, zone registry).
- **Rationale**: establishes the `SharedState` seam now (per `ARCHITECTURE.md`)
  without speculative contents; avoids a later signature churn across every
  screen.
- **Alternatives considered**: omit `SharedState` entirely (rejected — the seam is
  a known future requirement and re-adding it would touch every screen).

## Decision: reuse the existing ImGui + SDL_gpu loop unchanged

- **Decision**: no new rendering/input infrastructure; screens draw via the
  existing `ImGui.fs` binding and the loop already present in `Program.fs`.
- **Rationale**: phase 1 is structural only; rendering work is phases 2+.
