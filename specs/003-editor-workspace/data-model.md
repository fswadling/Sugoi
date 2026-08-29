# Data Model: Editor Workspace Shell

## Camera (`render/Camera.fs`)

A look-at orbit camera, ported from `ocaml-game/lib/camera.ml`: a `Position` +
`Target` + `Up`, from which a forward/right/up basis is derived.

- `Camera` (`[<Struct>]`):
  - `Position : Vector3` — the camera eye.
  - `Target : Vector3` — the point being looked at/orbited.
  - `Up : Vector3` — world up (usually `(0, 1, 0)`).
  - `Fov : float32` — vertical field of view.
  - `Aspect : float32` — viewport width/height.
  - `Near : float32`, `Far : float32` — clip planes.
- Derived basis:
  - `forward = normalize(Target - Position)`
  - `right = normalize(cross(forward, Up))`
  - `up = cross(right, forward)`
- Matrices: `view` (look-at), `proj` (perspective), `viewProjection = view * proj`.

### Camera control

The viewport maps input to three camera ops (matching `ocaml-game` and
`docs/EDITOR_DESIGN.md`):

- **Orbit** — right-drag: yaw around the up axis and pitch around the right axis,
  rotating `forward` about the target; the distance is preserved
  (`Target = Position + rotatedForward`).
- **Dolly (zoom)** — scroll: scale the position↔target distance by `0.9^wheel`
  (clamped to a minimum distance).
- **Fly (pan)** — right-drag + `W`/`A`/`S`/`D`/`Q`/`E`: translate `Position` and
  `Target` together by a camera-relative direction (`W`/`S` forward/back,
  `A`/`D` left/right, `Q`/`E` up/down).

## Asset (`core/Assets.fs`)

- `Asset` = `{ Id : string; Name : string; Kind : EntityKind }` — reuses
  `EntityKind` from `core/Entity.fs` (no separate enum); assets are only ever
  `Actor`/`Environment`/`Prop`.
- `scan : string -> Asset array` — scan a root directory for
  `actors/`/`environments/`/`props/` subdirectories, parse each JSON manifest,
  and return the assets keyed by `Id`.

## Editor state (`editor/EditorState.fs` + `screen/GameplayState.fs`)

- `EditorState` (immutable record) — transient editor UI state:
  - `Selection : EntityRef option` (always `None` this milestone).
- `GameplayState` (a `[<Struct>]`) owns the editor's data:
  - `Camera : Camera`
  - `DebugDraw : DebugDraw` — the viewport's line accumulator + GPU line pipeline
    (created once, reused every frame; gameplay-screen specific).
  - `Editor : EditorState`
  - `Editing : bool` (always `true` this milestone).
  - `DockingInitialized : bool` — the dock layout is built once, remembered here.
  - `PrevMouseX`/`PrevMouseY`/`PrevBacktick` — input edge-detection state for
    mouse deltas and backtick presses (re-synced by `activate` on switch-in).

## Relationships

- `GameplayState` owns the `Camera` and `EditorState` (data); `GameplayScreen`
  (behaviour) invokes `Editor.draw`, which returns the updated state and a
  `ScreenTransition` (File > Quit).
- `Viewport` reads the `Camera` and draws through `DebugDraw`.
- `AssetBrowser` reads the `Asset array` produced by `Assets.scan`.
- `Engine` stays screen-agnostic: it builds the per-frame `Frame` and calls
  `ScreenManager.update`/`draw`/`renderScene`/`applyTransition`.
