# Contract: Camera Cue Ops by Waypoint

The surfaces this feature adds or changes, and what explicitly does not change.

## `world/Cue.fs` — CHANGED

```fsharp
type Cue =
    | Fin
    | Sequence of current: Cue * rest: Cue FDeque
    | Parallel of left: Cue * right: Cue
    | Fork of inner: Cue
    | Wait of seconds: float
    | WaitState of seconds: float * endsAt: float
    | Exposit of text: string
    | SwitchZone of zone: string
    // camera, by named waypoint:
    | SetCamera of waypoint: string
    | FlyCamera of path: string * speed: float
    | FlyCameraState of path: string * points: Vector3[] * rotations: Quaternion[] * duration: float * endsAt: float
```

`MoveCamera`, `MoveCameraState` and `[<Struct>] CameraPose` are **removed**.

**Contract**: the cue is still the run — one type, and a step reports the cue as it now stands, or nothing to
change. The module still depends on `Prime` (for `FDeque`) and `System.Numerics` only; `SetCamera`'s name and
`FlyCamera`'s path/speed, and the in-flight arrays of `Vector3`/`Quaternion`, add no zone, screen, asset or device
type, so the module stays loadable on its own (FR-002, FR-004, FR-011).

## `world/Zone.fs` — CHANGED

```fsharp
val tryWaypointByName : zone: Zone -> name: string -> EntityRef option
val waypointChain     : zone: Zone -> start: EntityRef -> EntityRef list
```

**Contract**: `tryWaypointByName` answers the first live waypoint of that name in slot order (`None` if none);
`waypointChain` answers the `Next`-linked chain from `start`, bounded so a cycle terminates. Both sit beside
`waypointNext`/`setWaypointNext` and add no behaviour to anything else (FR-005, FR-016).

## `world/CueEngine.fs` — CHANGED

The two ops join the walk, with their effects performed where the walk reaches them:

```fsharp
| SetCamera waypoint ->
    let zone = GameplayState.activeZone state
    let (p, q) = ...resolve waypoint in zone, raising if absent...
    setCamera &state p q
    ValueSome Fin

| FlyCamera (path, speed) ->
    ...resolve the chain in the active zone, raising on < 2 waypoints or speed <= 0...
    ...cut to the first waypoint; if the path length is zero, land and Fin...
    ValueSome (FlyCameraState (path, points, rotations, length / speed, now + length / speed))

| FlyCameraState (_, points, rotations, duration, endsAt) ->
    if now >= endsAt then ...land on the last waypoint; ValueSome Fin...
    else ...write the camera at t; ValueNone...
```

**Contract**: `advance` is unchanged in kind — one cue in, the cue as it now stands out (or `ValueNone` while a
flight is in flight). A flight's begin allocates its resolved path; every in-flight step writes `state.Camera` and
allocates nothing (the answer is `ValueOption`, a struct). Names resolve in `GameplayState.activeZone`, and the
module is still compiled **after** `state/GameplayState.fs` for that reason. No new public function; the camera
helpers (`facing`, `setCamera`, `cameraLookDistance`) are private, and the spline geometry is `core/Maths.fs`
(below). `facing` follows the waypoint's arrow (`-UnitZ` rotated by the rotation, `editor/Viewport.fs`), so the camera
looks the way the arrow points and a waypoint placed at the camera round-trips (FR-003). The `CameraPose` helpers
(`applyPose`, `lerpPose`) are removed (FR-003, FR-006, FR-007, FR-008, FR-012, FR-013).

## `core/Maths.fs` — NEW

```fsharp
val catmullRom   : p0:Vector3 -> p1:Vector3 -> p2:Vector3 -> p3:Vector3 -> t:float32 -> Vector3
val evalSpline   : points:Vector3[] -> t:float -> Vector3
val evalRotation : rotations:Quaternion[] -> t:float -> Quaternion
val pathLength   : points:Vector3[] -> samplesPerSegment:int -> float
```

**Contract**: pure spline geometry over `Vector3`/`Quaternion`, naming no zone, screen, asset or device, so it is
exercisable on its own and reusable by any later flight (FR-006, FR-007).

## `editor/CueTree.fs` — CHANGED

| Surface | Change |
|---|---|
| `kinds` | `"Move camera"` is replaced by `"Set camera"` and `"Fly camera"` (FR-015, SC-007) |
| `kindName` | `SetCamera _ -> "Set camera"`; `FlyCamera _ \| FlyCameraState _ -> "Fly camera"` |
| `defaultOfKind` | `"Set camera" -> SetCamera ""`; `"Fly camera" -> FlyCamera("", 1.0)` |
| `label` | `SetCamera w -> "Camera: " + w`; `FlyCamera (p, _) \| FlyCameraState (p, _, _, _, _) -> "Fly: " + p` |
| `executing` | `FlyCameraState _` is executing (read-only); `SetCamera _`/`FlyCamera _` are not |
| `children` | both new leaves have none |
| `textOf` / `withText` | `SetCamera`'s waypoint and `FlyCamera`'s path carry text |
| `drawEditor` | `FlyCamera (path, speed)` gets a `"Speed"` number drag; the `MoveCamera` block is removed; `FlyCameraState _` joins the no-numbers branch |

**Contract**: the control still names `Cue`, `Prime` and `ImGui` and nothing else — no `GameplayState`, no zone, no
registry (FR-015, FR-019). A camera op's name reaches it through `textOf`/`withText`, but the widget that chooses the
name is the panel's (below), so the control gains no zone dependency.

## `editor/CueEditor.fs` — CHANGED

The panel draws a camera op's waypoint as a combo over the active zone's live waypoints:

```fsharp
// `SetCamera`/`FlyCamera` route to the combo; every other text-bearing node keeps the text field (FR-019).
| SetCamera current | FlyCamera (current, _) -> drawWaypointCombo &state idPrefix path node current
| _ -> ...drawText...

// The combo reads the active zone itself and writes through the control's own write path.
let private drawWaypointCombo state idPrefix path node current =
    ... beginCombo "Waypoint##<rowId>" ...
        for each live slot in (GameplayState.activeZone state).Waypoints.Slots do
            if selectableBool name ... then commit (CueTree.withAt path (CueTree.withText node name) state.Cue)
    ... endCombo ...
```

**Contract**: the panel reads `GameplayState.activeZone` and offers only that zone's live waypoint names, so a chosen
name resolves (FR-015, FR-019). It adds no state and no new write path: the choice is written with `CueTree.withText`
through the same `commit` the text field uses, so it is one `SetCue` step. An existing name that no live waypoint
matches is shown as the combo's preview and is left unchanged unless the author picks another. Keeping the combo in
the panel is what preserves the shared control's zone-agnosticism (`specs/047`, FR-006).

## `test/CueTests.fs`, `test/CuePanelTests.fs` — CHANGED

**Contract**: both files' `describe`/label tables gain the three new cases and drop `MoveCamera`. `CueTests` gains
the camera scenarios, asserted against `state.Camera` with a zone whose waypoints it places itself (FR-014); the
0-byte in-flight tick is measured as the other allocation properties are (FR-013). No test needs a device.

## `fsharp-game.fsproj` — CHANGED

One `Compile` entry: `core/Maths.fs`, after `core/Input.fs`. `world/Cue.fs`, `world/Zone.fs` and
`world/CueEngine.fs` keep their places.

## Explicitly unchanged

- **`state/GameplayState.fs`** — no new field; the ops write the existing `Camera`.
- **`world/Systems.fs` and `screen/GameplayScreen.fs`** — the follow camera and the play chain are untouched
  (FR-018).
- **`editor/Editor.fs`** — the panel's drawing and wiring are unchanged; `CueEditor.draw` still takes only the
  gameplay state.
- **`state/Command.fs`, `state/History.fs`, `EditorState.fs`** — a cue edit is still the whole-tree `SetCue` step;
  nothing about the ops reaches the undo model.
- **The render path, the physics world, `SharedState`, and the rest of the vocabulary** — untouched.
- **`docs/*.md`** — none names `MoveCamera`, so no doc changes. Only the code comments are corrected with the code:
  the headers of `world/Cue.fs` and `world/CueEngine.fs` (which name `MoveCameraState`/`MoveCamera` as the example
  `…State`/camera op), `editor/CueEditor.fs` (which said a name is typed), and `CueTree.executing`.
