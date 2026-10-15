# Research: Editor-Only Reference Grid

Phase 0 decisions resolving the feature's design unknowns. The value being changed is a view rule, so
the decisions are about *where* the gate lives and *how* it is proven with no window.

## D1 — Where the gate lives

**Decision**: inside `Viewport.drawGrid`, by reading `state.Editing` — the function keeps its current
signature `(state: byref<GameplayState.GameplayState>) : unit` and its existing call site in
`GameplayScreen.draw` (`do Viewport.drawGrid &state`) is unchanged:

```fsharp
// ── Reference grid (editor only, spec 093) ──

// The grid is an editor-only aid: rebuilt into its accumulator while editing, emptied while playing, so
// neither the grid nor the previous editing frame's vertices draw in play (FR-001, FR-002).
let drawGrid (state: byref<GameplayState.GameplayState>) : unit =
    state.DebugDraw <-
        if state.Editing then DebugDraw.clear state.DebugDraw |> DebugDraw.drawGrid
        else DebugDraw.clear state.DebugDraw
```

**Rationale**: `GameplayState.DebugDraw` is the **grid's own accumulator** — `Viewport.drawGrid` is its
only writer (`screen/GameplayScreen.fs` only ever *reads* it, to upload and draw it) — so the gate
belongs with the thing that fills it, and the rule becomes one function that a test can call directly.
It needs no signature change, no new parameter threaded from the screen, and no second place where a
caller could re-introduce the bug by forgetting the gate.

**Alternatives considered**:

- *Gate at the call site in `GameplayScreen.draw`, mirroring the gizmo's `if state.Editing` branch* —
  rejected: it leaves the grid's visibility expressed as a bare call in the screen, so the rule is not a
  single callable unit (FR-007), and the edit touches two files where one suffices.
- *A separate `gridVisible : bool -> bool` predicate* — rejected: it is `editing` restated, an
  unnecessary function (Constitution I, the rule of three).
- *A user preference / view toggle* — rejected: the spec fixes "editor active" as the existing edit/play
  toggle (Assumptions); a second switch would be a feature the user did not ask for.

## D2 — How the grid is hidden (clear, don't just skip)

**Decision**: while playing, **empty** the accumulator (`DebugDraw.clear`) rather than merely not
rebuilding it; while editing, rebuild it exactly as today (`DebugDraw.clear |> DebugDraw.drawGrid`).

**Rationale**: `renderScene` uploads and draws `state.DebugDraw` **unconditionally**, so simply skipping
the rebuild would leave the previous editing frame's vertices in the buffer and they would draw again in
play. Emptying is what actually hides the grid, and it is precisely what the markers/gizmo branch
already does for `GizmoDraw` (`screen/GameplayScreen.fs`), so the two editor overlays are hidden the same
way. Because `DebugDraw.clear` is a struct copy (`{ dd with Count = 0 }`), this allocates nothing.

**Alternatives considered**: *add an `if state.Editing` around the `DebugDraw.upload`/`draw` in
`renderScene`* — rejected: it changes the render pass for a non-render rule and leaves a stale buffer to
reason about; clearing at the source is smaller and keeps `renderScene` uniform.

## D3 — Proving it headlessly

**Decision**: a new `test/ViewportTests.fs` calls `Viewport.drawGrid` against a `GameplayState` built
with `GameplayState.create (Unchecked.defaultof<DebugDraw.DebugDraw>) …` (the `test/FadeTests.fs`
fixture) and asserts `state.DebugDraw.Count`:

- `Editing = false` → the accumulator is **empty** (`Count = 0`), which works on the `defaultof`
  accumulator because `clear` only writes `Count`.
- `Editing = true` → the accumulator is **non-empty**; this needs a `DebugDraw` whose CPU `Data` array
  exists, so the test builds a device-free one locally (positions/colours are the only fields the
  accumulator touches; the GPU-handle fields are defaulted and never dereferenced — the
  `test/TestSupport.fs` "device-free fixture" precedent).

**Rationale**: `DebugDraw`'s CPU accumulator (`Data`, `Count`) is independent of its GPU resources, so
the frame's *decision* — grid or no grid — is observable with no device. Asserting `Count` proves the
spec's P1 (US1) directly: empty while playing, non-empty while editing.

**Alternatives considered**: *assert on a GPU upload* — rejected (needs a device); *assert only the
predicate `Editing`* — rejected (D1: no such function; the observable is the accumulator's contents).

## D4 — The switch frame

**Decision**: no extra work. `GameplayScreen.draw` runs before `renderScene` in the same frame, and
`drawGrid` empties the accumulator when playing, so the frame the author enters play on is already free
of grid vertices — the same guarantee the markers get (FR-002).

**Rationale**: the ordering is the existing engine contract `GameplayScreen.draw` already relies on for
its gizmo clear; the grid rides it for free.

**Alternatives considered**: none; a cross-frame deferral would be a bug, not a design.
