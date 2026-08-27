# Contract: Screen

Every screen module exposes the same shape so `ScreenManager` can dispatch
uniformly. (Signatures are illustrative, not implementation.)

## Update

```fsharp
// Advance the screen; may request navigation.
update : ScreenState -> SharedState -> ScreenState * ScreenTransition
```

## Draw

```fsharp
// Render the screen's UI.
draw : ScreenState -> SharedState -> unit
```

## Rules

- `update` and `draw` take the screen's own state and the shared state; they must
  not capture global state.
- A screen requests navigation by returning a `ScreenTransition`; the manager
  applies it at the end of the frame.
- Exactly the active screen runs per frame (FR-004).
