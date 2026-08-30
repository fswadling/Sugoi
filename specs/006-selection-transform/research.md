# Research: Selection + Transform

Decisions for the 2c viewport interaction, ported from `ocaml-game`'s
`lib/editor/{raycast,viewport,editor}.ml`.

## Decision 1 — Window size comes from `io.DisplaySize`

- **Decision**: add a context-safe `cimgui_display_size()` shim returning the
  main viewport size, bind it as `ImGui.displaySize`, and read it each frame in
  `GameplayScreen.gatherInput` (storing `WindowW`/`WindowH` on `GameplayState`).
- **Rationale**: ImGui already tracks the window size and keeps it correct on
  resize (resize events already flow through the backend); no need to thread the
  SDL window handle into state. It also lets us fix the hard-coded
  `Camera.Aspect` by setting `aspect = w/h`.
- **Alternatives considered**: `SDL.GetWindowSize` (rejected — needs the window
  handle exposed from `Engine`); hard-coding `1280x720` (rejected — breaks on
  resize).

## Decision 2 — Left-button edge detection is `PrevLeftDown : bool`

- **Decision**: add `LeftDown` to `Input` and `PrevLeftDown : bool` to
  `GameplayState`; pressed/released are derived from the two.
- **Rationale**: only the left button needs edge detection (selection + gizmo
  drag). A single bool is minimal and mirrors the existing `PrevHeld` pattern.
- **Alternatives considered**: a full `PrevButtons : int` bitmask (rejected —
  over-general for now; promote later if a second button needs edge detection).

## Decision 3 — Entity kind-dispatch lives in `Zone` (temporary)

- **Decision**: add `entityAt`/`setEntity`/`removeEntity` and `spawnActor`/
  `spawnProp` to `core/Zone.fs`, dispatching on `EntityRef.Kind`, with a banner
  comment marking them temporary (they move to `editor/Command.fs` in 2d).
- **Rationale**: 2c needs read/write by `EntityRef` without an undo stack; 2d will
  wrap these exact helpers in commands. Keeping them on `Zone` now avoids
  re-plumbing.
- **Alternatives considered**: seed `editor/Command.fs` early (rejected — pulls
  the undo model forward); inline dispatch at each call site (rejected — five
  call sites, one concept).

## Decision 4 — Raycast is a pure port of `ocaml-game`'s `raycast.ml`

- **Decision**: `Ray { Origin; Dir }`, `rayFromScreen`, `intersectAabb` (slab
  test → nearest `t ≥ 0`), `planeIntersect`, `raySegmentDistance`, all
  `System.Numerics` + `float32`. No scene/GPU dependencies (unit-testable).
- **Rationale**: this is exactly the math ocaml uses; keeping it pure makes it
  independently testable.
- **Alternatives considered**: inline the math in `Viewport` (rejected — not
  testable in isolation).

## Decision 5 — Gizmo picking reuses the raycast

- **Decision**: move/scale axis hit = `raySegmentDistance < 0.1`; rotate ring hit
  = `|distance(planeIntersect, center) − radius| < 0.1`. The drag plane for
  move/scale is the camera-facing plane through the entity; rotate measures the
  angle around the axis.
- **Rationale**: matches ocaml's `gizmo_axis_at` and `drag_transform`.
- **Alternatives considered**: separate handle geometry (rejected — overkill for a
  minimal gizmo).

## Decision 6 — Constants mirror ocaml-game

- **Decision**: marker box half-extent `0.25`; gizmo length/radius `0.75`; pick
  threshold `0.1`; move tip sphere radius and scale tip box half `0.05`; snap
  steps move `0.5`, rotate `π/12`, scale `0.25`.
- **Rationale**: proven values from the reference; no reason to diverge.
