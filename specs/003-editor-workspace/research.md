# Research: Editor Workspace Shell

Decisions resolving the open technical choices for this feature.

## Decision: render the 3D scene to the swapchain (no offscreen texture)

- **Decision**: the viewport's 3D scene (the grid) is rendered directly to the
  swapchain as the background; ImGui panels dock on top of it. No offscreen FBO.
- **Rationale**: simplest; the editor's offscreen `SceneTarget` FBO is a later
  phase (14). Docking already leaves the uncovered centre as the "viewport".
- **Alternatives considered**: render the scene to an offscreen texture and show
  it in an ImGui `Image` (deferred — needs FBO + sampler plumbing).

## Decision: pull a minimal line pipeline forward into `DebugDraw`

- **Decision**: `DebugDraw` owns a line-only GPU path (one vertex buffer + one
  line shader) to draw the grid. This is a small pull-forward of phase 3's
  `render/Gpu.fs`/`render/Mesh.fs` work, scoped to lines only.
- **Rationale**: the "done when" requires a visible grid, and lines are the
  cheapest primitive that satisfies it; a full mesh/pipeline abstraction is
  phase 3.
- **Alternatives considered**: defer the grid to phase 3 (rejected — the viewport
  needs a visible reference now); build `Gpu.fs`/`Mesh.fs` fully in 2b (rejected —
  YAGNI, phase 3 owns that).

## Decision: screen↔editor compile order via a data/behaviour split

- **Decision**: `GameplayState` (data) holds `Camera`, `EditorState`, and
  `Editing`; `Editor` (behaviour) lives in `editor/` and is called by
  `GameplayScreen` (behaviour). The `.fsproj` order becomes
  `core → render → screen/data → editor → screen/behaviour → Engine`.
- **Rationale**: matches `docs/EDITOR_DESIGN.md` D-020/D-021 (split data from
  behaviour) and avoids a `GameplayScreen ↔ Editor` module cycle.
- **Alternatives considered**: `Engine` calls `Editor` directly (rejected — breaks
  "Engine stays screen-agnostic"); a single combined `GameplayScreen` that
  contains the editor (rejected — conflates screen and editor, and breaks the
  `editor/` cluster).

## Decision: assets scanned from disk + JSON manifests, keyed by id

- **Decision**: `core/Assets.fs` scans `assets/actors/`, `assets/environments/`,
  `assets/props/` for JSON manifests and returns `Asset { Id; Name; Kind }` in
  scan order.
- **Rationale**: matches `docs/ARCHITECTURE.md`'s asset manager; the id-keyed
  manifest is what later phases (spawn in 2c, loading in 3/4) consume.
- **Alternatives considered**: hardcoded asset list (rejected — not data-driven);
  scan filenames without manifests (rejected — architecture specifies manifests).

## Decision: docking via `igDockSpaceOverViewport`

- **Decision**: the workspace uses ImGui's `igDockSpaceOverViewport` for the
  dockable layout, plus a main menu bar and per-panel `Begin`/`End` windows.
- **Rationale**: this is the standard Dear ImGui docking entry point and gives the
  drag-to-dock behaviour the editor design calls for.
- **Alternatives considered**: manual fixed layout (rejected — loses docking).

## Decision: look-at orbit camera (position + target), ported from ocaml-game

- **Decision**: `Camera { Position; Target; Up; Fov; Aspect; Near; Far }` — a
  look-at camera, not a spherical pitch/yaw/distance one. `forward`/`right`/`up`
  are derived, and `view`/`proj` build the matrices. Controls match
  `ocaml-game/lib/editor/editor.ml` and `docs/EDITOR_DESIGN.md`: right-drag orbits
  (yaw around up, pitch around right), scroll dollies (`0.9^wheel`, clamped), and
  right-drag + `WASD`/`Q`/`E` flies (moves `Position` + `Target` together).
- **Rationale**: one representation supports both orbit and fly; it is exactly
  what ocaml-game's editor uses.
- **Alternatives considered**: spherical pitch/yaw/distance camera (rejected —
  cannot express fly, and diverges from the ocaml-game reference).

## Decision: read viewport input at the SDL level, not through ImGui

- **Decision**: camera input is read at the SDL level rather than through
  ImGui's IO. Engine forwards only a per-frame `Frame` (dt + accumulated wheel
  — event-only input) to `ScreenManager.update`; the gameplay screen reads
  pollable input (mouse position/buttons, held keys) from SDL state itself.
- **Rationale**: the viewport is the dock space's passthru central node, not an
  ImGui window, so ImGui does not capture mouse/keyboard there. SDL input is also
  what the later play-mode systems (phase 7) will consume, so this establishes
  the input seam early.
- **Alternatives considered**: expand the ImGui binding to expose `io.MousePos`/
  `io.MouseDown`/`io.MouseWheel`/`io.KeysDown` (rejected — adds binding surface
  for something only the passthru viewport needs, and ImGui's `WantCapture*`
  gating would complicate camera drag vs. panel interaction).
