# Data Model: Screens + Main Menu

## ScreenKind (discriminated union)

Discriminates the active screen.

- `MainMenu` — the launch screen.
- `Gameplay` — the UI-only gameplay placeholder.

(Extensions: `Settings` and future screens are added as new cases.)

## ScreenTransition (discriminated union)

A pending navigation request produced by a screen and consumed by the manager.

- `None` — no transition.
- `Next of ScreenKind` — switch to the given screen.
- `Quit` — exit the application.

## ScreenManager (record)

Owns the active screen and its state; the single source of truth for "which
screen is active" (FR-007).

- `active : ScreenKind`
- `transition : ScreenTransition`
- `shared : SharedState`
- `gameplay : GameplayState`

`update`/`draw` dispatch on `active`; a transition is applied at the end of the
frame.

## GameplayState (record)

The gameplay screen's per-screen state. Empty in this milestone (no 3D world).

## SharedState (record)

Cross-screen state owned by the manager. Empty in this milestone; populated by
later phases (assets, cues, cast, zone registry) per `docs/ARCHITECTURE.md`.

## Relationships

- `ScreenManager` owns one active `ScreenKind` at a time and holds `GameplayState`
  plus `SharedState`.
- A screen's `update`/`draw` may set the manager's `transition` field; the manager
  applies it at the end of the frame.
