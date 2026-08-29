# Contract: Screen

Screens are dispatched by `ScreenManager`. Each exposes `update` and `draw`,
which return a `ScreenTransition`; the manager applies it at end of frame.
(Signatures are illustrative, not implementation.)

## Stateful screens (e.g. Gameplay)

```fsharp
// Advance the screen; may request navigation.
update : ScreenState -> SharedState -> ScreenState * ScreenTransition

// Render the screen's UI; may request navigation.
draw : ScreenState -> SharedState -> ScreenState * ScreenTransition
```

## Stateless screens (e.g. MainMenu)

A screen with no per-frame state drops the `ScreenState` argument/result:

```fsharp
update : SharedState -> ScreenTransition
draw   : SharedState -> ScreenTransition
```

## Rules

- `update` and `draw` take the screen's own state and the shared state (when the
  screen has state); they must not capture global state.
- A screen requests navigation by returning a `ScreenTransition`; the manager
  applies it at the end of the frame.
- Exactly the active screen runs per frame (FR-004).
