# Contract: Screen

Screens are dispatched by `ScreenManager`. Each exposes `update` and `draw`,
which may return a `ScreenTransition`; the manager applies it at end of frame.
3D screens additionally expose `renderScene`. (Signatures are illustrative,
not implementation.)

## Stateful screens (e.g. Gameplay)

```fsharp
// Advance the screen; may request navigation. `Frame` carries dt plus
// event-only input (wheel); pollable input is read from SDL state directly.
update : SharedState -> ScreenState -> Frame -> struct (ScreenState * ScreenTransition)

// Render the screen's UI; may request navigation.
draw : SharedState -> ScreenState -> struct (ScreenState * ScreenTransition)

// Record the screen's 3D scene pass (3D screens only).
renderScene : ScreenState -> ScenePass -> unit

// Re-sync per-screen state on switch-in (input edge state, UI buffers).
activate : SharedState -> ScreenState -> ScreenState
```

Per-screen drift is allowed: parameters are taken only as needed (e.g. a
screen whose `draw` needs no `Frame` omits it).

## Stateless screens (e.g. MainMenu)

A screen with no per-frame state drops the `ScreenState` argument/result:

```fsharp
update : SharedState -> ScreenTransition
draw   : SharedState -> ScreenTransition
```

## Rules

- `update` and `draw` take the shared state, the screen's own state, and the
  frame (as needed); they must not capture global state — per-screen mutable
  data lives in `ScreenState`, re-synced by `activate` on switch-in.
- A screen requests navigation by returning a `ScreenTransition`; the manager
  applies it at the end of the frame (a draw request overrides an update
  request from the same frame).
- Exactly the active screen runs per frame (FR-004).
