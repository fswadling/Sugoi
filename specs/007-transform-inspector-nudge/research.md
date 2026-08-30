# Research: Transform Inspector + Keyboard Nudge

## Decision 1 — Rotation is displayed as ZYX Euler angles in degrees

- **Decision**: convert `Transform.Rotation` (a quaternion) to ZYX Euler angles and
  show them in degrees.
- **Rationale**: a raw quaternion is unreadable; the reference implementation
  (`ocaml-game/lib/editor/inspector.ml`) shows `to_rot3_zyx` Euler angles. Degrees
  were chosen over radians for readability (snap is 15° = π/12).
- **Alternatives considered**: show the quaternion (rejected — unreadable); show
  radians (rejected — user chose degrees).

## Decision 2 — The Euler helper lives in `core/Entity.fs`

- **Decision**: `eulerDegrees : Quaternion -> Vector3` as a top-level function in
  `core/Entity.fs`.
- **Rationale**: it is pure, generic transform math; placing it on the entity
  module makes it unit-testable and reusable (the Inspector uses it now, other
  surfaces can later).
- **Alternatives considered**: a private helper in `Inspector.fs` (rejected — not
  testable, less discoverable).

## Decision 3 — Nudge behaviour lives in `editor/Editor.fs`

- **Decision**: `nudgeTransform` and `updateNudge` go in `editor/Editor.fs`, next
  to `dragTransform`, which they mirror; `editor/EditorState.fs` stays data-only.
- **Rationale**: the nudge needs the camera basis and the selection read/write
  helpers (`selectedTransform`/`setSelectedTransform`) that already live in
  `Editor.fs`.
- **Alternatives considered**: `EditorState.fs` (rejected — would drag `Camera`
  into a module that is otherwise pure state).

## Decision 4 — Nudge steps are per-frame constants (reference parity)

- **Decision**: move 0.05 (snap 0.5), rotate 0.02 rad (snap π/12), scale 0.01
  (snap 0.25), applied once per frame while a key is held.
- **Rationale**: matches `ocaml-game/lib/editor/editor_state.ml` exactly, and the
  game runs under a 60 fps cap, so per-frame steps are consistent.
- **Alternatives considered**: delta-time scaling (rejected — diverges from the
  reference and makes the snap increments frame-rate dependent).

## Decision 5 — Shift reserves WASD/QE for nudge

- **Decision**: nudge fires on `Shift` + WASD/QE (no drag, no text input); camera
  fly fires on right-drag + WASD/QE with `not Shift`; the `Q` snap toggle fires on
  `Q` with `not Shift`.
- **Rationale**: this is the reference's key reservation — Shift = nudge, right-drag
  = fly — and it prevents a single key press from doing two things.
- **Alternatives considered**: distinguishing by whether the mouse is over the
  viewport (rejected — more complex and less predictable).

## Decision 6 — Undo coalescing is deferred to 2d

- **Decision**: this phase applies the nudge transform directly (like the gizmo
  drag already does); the `nudge_before` "one undo step per hold" state is deferred.
- **Rationale**: there is no undo stack yet (that is phase 2d); recording `before`
  now would be dead state.
- **Alternatives considered**: add `nudge_before` now (rejected — unused until the
  undo stack exists).
