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

## ScreenManager ([<Struct>])

Owns the active screen and its state; the single source of truth for "which
screen is active" (FR-007). Immutable — `update`/`draw` return a new value via
copy-and-update.

- `Active : ScreenKind`
- `Transition : ScreenTransition`
- `Shared : SharedState`
- `ShouldQuit : bool`
- `Gameplay : GameplayState`

`update`/`draw` dispatch on `Active`; a pending transition is applied at the end
of the frame via `applyTransition`, which activates the new screen on a switch
and sets `ShouldQuit` on `Quit` (the engine's loop condition checks it).

## Frame ([<Struct>])

Everything the engine surfaces to screens each frame, built once in `Engine`:
`Dt` (seconds) plus event-only input (currently `Wheel`). Pollable input is
read from SDL state by the screens themselves. New inputs extend this record,
never a signature.

## ScenePass ([<Struct>])

What a 3D screen needs to record its scene pass (`Cmd` + `ColorTargets`), so
GPU details cross one explicit seam instead of riding loose through the
manager.

## GameplayState ([<Struct>])

The gameplay screen's per-screen state. Grows in later phases; currently owns
the editor's data (`Camera`, `Editor`, `Editing : bool`) plus input
edge-detection state (`PrevMouseX`/`PrevMouseY`/`PrevBacktick`) and the
one-time `DockingInitialized` flag.

## SharedState (reference type)

Cross-screen state owned by the manager; a reference type so screens can write
shared values back. Currently holds the scanned `Assets`; later phases add
cues, cast, and the zone registry per `docs/ARCHITECTURE.md`.

## Relationships

- `ScreenManager` owns one active `ScreenKind` at a time and holds `GameplayState`
  plus `SharedState`.
- A screen's `update`/`draw` return a `ScreenTransition`; the manager stores it
  and applies it at the end of the frame (a draw request overrides an update
  request from the same frame via `Transition.orElse`).
