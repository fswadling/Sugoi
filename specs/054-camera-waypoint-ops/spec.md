# Feature Specification: Camera Cue Ops by Waypoint

**Feature Branch**: `054-camera-waypoint-ops`

**Created**: 2026-10-03

**Status**: Implemented

**Input**: User description: "First - lets remove movecamera - we want setcamera and flycamera that uses waypoint names."

## Origin

This follows `specs/046-cue-core`, whose first cut named `MoveCamera` as the leaf proving the shape *progress is more
than a deadline*. That leaf is corrected here: in this game a camera is placed by **waypoints** (`specs/026`,
`specs/028`), and the reference (`odin-game`) drives the camera through named waypoints — a snap to one
(`SetCamera`) and a flight along a chain (`FlyCamera`). `MoveCamera`, an authored pose the author drags, is removed:
it is not how this game's cameras are placed, and a waypoint name is what the editor can type without the control
reading the zone registry (`specs/047`, FR-006).

The fourth shape is not lost — `FlyCamera` still proves it, now with a `…State` case that carries the resolved path.
No persistence exists yet (phase 15), so nothing saved names `MoveCamera` and no migration is required.

## Clarifications

### Session 2026-10-03

- Q: When the camera flies along a waypoint chain, which way should it look at each point? → A: The waypoint's own facing at each point, interpolated between waypoints (the reference behaviour); the camera does not look along the direction of travel.
- Q: What path should the camera's position follow between waypoints during a flight? → A: A Catmull-Rom spline through the waypoint positions (the reference behaviour): a smooth curve passing through every waypoint.
- Q: When a camera op names a waypoint that does not resolve — a missing or empty name, a chain shorter than two waypoints, or a non-positive speed — what should happen? → A: Fail loudly (raise) for all of them, the rule an unknown zone already follows.
- Q: Should a cue's camera ops suppress the player's follow camera, which runs every play frame before the cue? → A: No. Camera ops only write the camera; enabling and disabling follow are separate ops (phase 10, the reference's own design).
- Q: When a flight begins, where is the camera at that instant — at the first waypoint of the path, or easing from wherever it currently is? → A: It cuts to the first waypoint of the path when the flight begins (the reference behaviour); it does not ease from the camera's current pose.
- Q: The panel authors a camera op by waypoint name; should the name be typed or chosen? → A: Chosen — the panel offers a combo of the **active zone's live waypoints**. The panel reads the zone (it already holds `GameplayState`), not the shared control, so `specs/047`'s FR-006 boundary (the control names no zone) still holds, and a chosen name always resolves.
- Q: Two forward conventions exist in the code — actors face `+UnitZ`, but the waypoint gizmo arrow faces `-UnitZ`. Which way does a camera op look? → A: The way the **waypoint's arrow points** (`-UnitZ` rotated by the waypoint's rotation, the convention `editor/Viewport.fs` draws and `newWaypointAtCamera` authors): a snap and a flight look the same way the arrow points, so a waypoint placed at the camera round-trips.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A cue points the camera from a named waypoint (Priority: P1)

A cue contains a "set camera" op naming a waypoint. When the cue reaches it, the camera is placed at that waypoint's
position and looks the way the waypoint faces. The op costs no time.

**Why this priority**: it is the simplest camera op, and it is the shape with no progress of its own. Every scripted
framing starts by looking from somewhere.

**Independent Test**: place a waypoint with a known transform in a zone, put a "set camera" op naming it in a cue,
step once, and assert the camera's position and look direction — no device, no GPU.

**Acceptance Scenarios**:

1. **Given** a zone with a waypoint at a known position and facing, **When** a cue reaches a "set camera" op naming
   it, **Then** the camera is at that position, looks along that facing, and the op costs no time (the cue is at the
   next op in the same step).
2. **Given** a cue whose "set camera" op names no waypoint in the active zone, **When** it is reached, **Then** it
   fails loudly rather than being skipped.
3. **Given** a waypoint that exists in a zone that is not active, **When** a "set camera" op in the active zone names
   it, **Then** it fails loudly — names resolve in the active zone.

---

### User Story 2 - A cue flies the camera along a waypoint chain (Priority: P2)

A cue contains a "fly camera" op naming the first waypoint of a chain and giving a speed. When the cue reaches it,
the camera flies from the first waypoint to the last over a time derived from the path's length and the speed, and
finishes on the last waypoint's pose.

**Why this priority**: it is the timed camera op — the shape whose progress is more than a deadline. It ranks below
the snap because a flight is composed of the same placement a snap performs.

**Independent Test**: build a chain of waypoints of known length, fly at a known speed, and assert the camera
arrives at the last waypoint's pose at the computed time and that the cue is then `Fin`.

**Acceptance Scenarios**:

1. **Given** a chain of waypoints a -> b -> c, **When** a "fly camera" op naming a at a given speed is reached and
   stepped, **Then** the camera travels the path and the cue is `Fin` at path-length divided by speed, ending at c's
   pose.
2. **Given** a "fly camera" op whose name resolves to no waypoint, or to a chain of fewer than two waypoints, or
   whose speed is not positive, **When** it is reached, **Then** it fails loudly.
3. **Given** a flight in progress, **When** it is stepped before its deadline, **Then** the camera is on the path
   between the first and last waypoint, and the step changes nothing in the cue value.
4. **Given** a cue that flies the camera and then switches zone, **When** it runs, **Then** the flight completes on
   the path it began with, and the switch still happens.
5. **Given** a flight in progress, **When** the cue is read, **Then** the node is the running one and is read-only in
   the panel.

---

### User Story 3 - The panel authors both camera ops by choosing a waypoint (Priority: P3)

The cue panel's kind list offers "set camera" and "fly camera" in place of "move camera": each names a waypoint
chosen from a combo of the active zone's live waypoints, and a flight also carries a speed. A flight that is running
reads as the op it came from and is read-only.

**Why this priority**: it is the authoring surface. The engine is useful before it is editable, so this ranks below
the two run stories.

**Independent Test**: assert the panel's kind list, the default each kind makes, the label of each row, which nodes
carry text and how it is written back, that an in-flight flight is locked, and that the combo it draws offers the
active zone's live waypoints.

**Acceptance Scenarios**:

1. **Given** the kind list, **When** a node is re-kinded, **Then** "set camera" makes a snap with an empty name and
   "fly camera" makes a flight with an empty name and a default speed, and "move camera" is absent.
2. **Given** a "set camera" or "fly camera" node, **When** its waypoint is chosen, **Then** exactly that node's name
   changes as one undo step; **Given** a flight, **Then** its speed is a number drag.
3. **Given** a flight in progress, **When** its row is drawn, **Then** it is labelled as a fly camera and is
   read-only while running.
4. **Given** a camera op and an active zone holding waypoints, **When** its combo is opened, **Then** it lists that
   zone's live waypoint names and choosing one writes it; the shared control still reads no zone.

---

### Edge Cases

- **A waypoint name that occurs more than once**: the first live waypoint in slot order resolves. Deterministic, not
  an error.
- **A waypoint chain that forms a cycle**: the walk must terminate rather than loop forever.
- **A "fly camera" name that resolves to a single waypoint**: not a path — fails loudly.
- **A zero-length path** (two waypoints at the same position): the duration is zero, so the flight is over before it
  begins, as any timed op with a passed deadline is (`specs/046`, edge cases).
- **An empty name**: reach-time resolution fails loudly, as an empty zone name does.
- **A name that only resolves later** (a waypoint added after the op was authored): resolution is at reach-time, so
  it resolves, and the combo offers the waypoint as soon as it exists.
- **A name whose waypoint has since been deleted, or one in a non-active zone**: the combo still shows the authored
  name (so it is not hidden or silently rewritten), but offers only the active zone's live waypoints; the op still
  fails loudly at reach time if it resolves to nothing (FR-009).
- **A waypoint edited or deleted while a flight is in progress**: the flight keeps the path it resolved when it
  began (a run is pinned to the values it started from), so it is not corrupted and still completes.
- **Flying into a finished cue**: a flight is an op like any other; nothing special happens at the end but `Fin`.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The cue vocabulary MUST remove `MoveCamera` and `MoveCameraState`. The `CameraPose` type MUST be
  removed unless another op uses it (none does).
- **FR-002**: A "set camera" op (`SetCamera`), carrying a waypoint name, MUST be added as an op with no progress of
  its own: performed where it is reached, holding no state record (`specs/046`, FR-007).
- **FR-003**: Reaching a "set camera" op MUST place the camera at the named waypoint's position and point it along
  that waypoint's facing — the direction the editor's waypoint arrow points (`-UnitZ` rotated by the waypoint's
  rotation, `editor/Viewport.fs`), not the opposite. The distance at which it looks is a named constant, not a
  literal.
- **FR-004**: A "fly camera" op (`FlyCamera`), carrying a path name and a speed, MUST be added, together with an
  in-flight case (`FlyCameraState`) carrying the resolved path and the timing the run needs.
- **FR-005**: A flight MUST follow the waypoint chain that starts at the named waypoint, through its links, to the
  end of the chain.
- **FR-006**: A flight MUST begin at the first waypoint of its path — the camera cuts there when the op begins — and
  MUST follow the path for its duration, then end exactly on the last waypoint's pose, after which the op is `Fin`.
  Its duration MUST be the path's length divided by its speed.
- **FR-007**: A flight's position MUST follow a smooth path through the waypoint positions, and its facing MUST
  interpolate through the waypoint facings (the reference's behaviour), under the same arrow convention as FR-003.
- **FR-008**: Both ops MUST resolve waypoint names in the **active zone** at the moment the op is reached.
- **FR-009**: A name that resolves to no waypoint MUST fail loudly, not be silently skipped — the rule an unknown
  zone already follows (`specs/046`, FR-014; Constitution II).
- **FR-010**: A "fly camera" whose path has fewer than two waypoints, or whose speed is not positive, MUST fail
  loudly.
- **FR-011**: The two ops MUST NOT change the shape of the run or the walk (`specs/046`, FR-017): a snap is a leaf
  with no in-flight case, a flight is a leaf with its own in-flight case, and neither alters the walk.
- **FR-012**: A flight's in-flight form MUST be pinned to the path it resolved when it began: it MUST NOT re-read the
  zone each step, and editing, deleting or switching away from the waypoints afterwards MUST NOT corrupt it
  (`docs/CUE_MODEL_DECISION.md`, invariant 5).
- **FR-013**: A step in which a flight is in progress and unchanged MUST allocate nothing; resolving a path MUST
  allocate only once, when the op begins, and that allocation MUST NOT grow with the cue's length (`specs/046`,
  FR-012; Constitution V).
- **FR-014**: The camera ops MUST remain assertable **headlessly** — the camera is plain data, and a test builds a
  zone with waypoints and steps the cue with no window, GPU or device (`specs/046`, FR-016).
- **FR-015**: The panel MUST offer "set camera" and "fly camera" in place of "move camera"; name a waypoint by
  choosing it from a combo of the **active zone's live waypoints**; edit a flight's speed as a number; label a
  flight's in-flight form as the op it came from; and treat an in-flight flight as read-only (`specs/047`).
- **FR-016**: A flight's chain walk MUST terminate even when the waypoints form a cycle (bounded by the zone's
  waypoint count).
- **FR-017**: `dotnet build` MUST be clean and `dotnet test` MUST pass, with new tests covering the stories and edge
  cases above as pure logic requiring no GPU.
- **FR-018**: A camera op MUST NOT change whether the player's follow camera runs. Writing the camera and toggling
  follow are separate concerns; enabling/disabling follow is a later op (phase 10), matching the reference. With a
  live player, follow runs before the cue each frame, so a snap holds only for the step it performs and a flight
  holds while it rewrites the camera each step.
- **FR-019**: The waypoint combo MUST be drawn by the **panel**, which reads the active zone, and not by the shared
  cue control: the control keeps naming no zone, no registry and no asset (`specs/047`, FR-006), so it stays reusable
  by a trigger's cue (phase 10) and a scenario's cue (phase 11). The combo MUST offer only the active zone's live
  waypoints; a camera op's existing name that no live waypoint matches MUST still be shown, so an authored value is
  never hidden.

### Key Entities *(include if feature involves data)*

- **Set camera op** (`world/Cue.fs`): the leaf naming a waypoint in the active zone; no in-flight case.
- **Fly camera op / its in-flight form** (`world/Cue.fs`): the authored op (path name + speed) and the value it
  becomes as it begins (the resolved positions and facings, and the timing).
- **Waypoint** (existing, `world/Entity.fs`): a name, a transform (position + facing), and a link to the next
  waypoint in its chain.
- **Waypoint chain** (existing, `world/Zone.fs`): the links a flight follows; walked by the engine now rather than
  only by the editor.
- **Active zone** (existing, `state/GameplayState.fs`): where a name resolves.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A "set camera" op frames exactly from its waypoint: 0 positional error and 0 direction error against
  the waypoint's arrow (the editor's own facing convention).
- **SC-002**: A flight arrives on time and on target: the cue is `Fin` at path-length divided by speed (*0* timing
  deviation) and the camera is at the last waypoint's pose (*0* pose error), with progress never going backwards.
- **SC-003**: A flight in progress costs the game loop nothing: 0 bytes per unchanged step, and the one allocation a
  path costs when it begins does not grow with the cue's length.
- **SC-004**: Every malformed case — an unknown or empty name, a chain shorter than two waypoints, and a
  non-positive speed — fails loudly and is covered by a test (0 silent skips). A cyclic chain is the one case that
  does **not** fail: it terminates (FR-016), and that is covered by a test too.
- **SC-005**: `MoveCamera`, `MoveCameraState` and `CameraPose` appear nowhere in the source — 0 references.
- **SC-006**: The change is demonstrable headlessly: `dotnet build` is clean and `dotnet test` passes.
- **SC-007**: The panel offers exactly the two camera kinds — 0 rows named "move camera".
- **SC-008**: The camera op's waypoint is chosen from the active zone and nothing else: the combo offers exactly that
  zone's live waypoints (0 names from any other zone), and the shared cue control still names no zone (0 references).

## Assumptions

- **Waypoints and chains already exist** and are authored in the editor (`specs/026`, `specs/028`); this feature only
  *names* them. It adds no waypoint authoring.
- **The cue control stays zone-agnostic; the panel draws the combo.** The control names no zone (`specs/047`,
  FR-006), but the panel already holds `GameplayState`, so it reads the active zone and draws the waypoint combo
  itself (FR-019). A camera op's name is therefore chosen rather than typed, and a name the combo offers always
  resolves.
- **A flight cuts to its path start** (decided 2026-10-03, see Clarifications): when a flight begins the camera is
  placed at the first waypoint and follows the path from there; it does not blend from wherever the camera was.
- **Look distance** (informed guess, vetoable): the reference points the camera a fixed 3.5 units along the
  waypoint's facing; this port adopts that as a named constant.
- **Path smoothing** (decided 2026-10-03, see Clarifications): the camera's position follows a Catmull-Rom
  spline through the waypoint positions and its facing interpolates through the waypoint facings, matching the
  reference. A flight's duration is the sampled arc length of that spline divided by the speed.
- **Facing** (decided 2026-10-03, see Clarifications): the camera looks along the *waypoint's* facing at each
  point, not along the direction of travel, matching the reference. The facing is the waypoint's arrow direction:
  `-UnitZ` rotated by the waypoint's rotation (`editor/Viewport.fs`), which is what `newWaypointAtCamera` authors and
  what the camera follows — the opposite sign to the actor forward convention.
- **Resolution is at reach-time**, and a flight is pinned to what it resolved (`docs/CUE_MODEL_DECISION.md`,
  invariant 5). A waypoint name that does not resolve fails loudly at the step the op is reached.
- **Camera ops do not suppress the follow camera** (decided 2026-10-03, see Clarifications): the play chain runs
  the player's follow camera before the cue each frame, and a cue camera op only writes the camera. A cutscene that
  must hold its framing with a live player needs the follow camera off, which is phase 10's enable/disable ops
  (`docs/ROADMAP.md`, phase 10), not this feature.
- **Names need not be unique**; the first live waypoint in slot order resolves. Duplicates are discouraged but are
  not an error.
- **No persistence** (phase 15): removing `MoveCamera` leaves no saved project to migrate, and the layout of a saved
  cue is deferred with the rest of persistence.
- **`MoveCamera`'s arbitrary authored pose is intentionally dropped.** If a raw pose is ever needed it returns as its
  own op, not by keeping this one.
- **Documentation moves with the feature**: any comment or document that names `MoveCamera` as the fourth shape
  (`world/Cue.fs`, `world/CueEngine.fs`, `docs/CUE_MODEL_DECISION.md`, `specs/046-cue-core`) is corrected to name the
  camera ops by waypoint.
