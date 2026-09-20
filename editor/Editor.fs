module Editor

// Editor orchestration: the docking workspace (menu bar + panels), keyboard
// shortcuts, and the viewport selection/gizmo interaction (ported from
// ocaml-game's lib/editor/editor.ml). The editor reads the gameplay state's
// camera/editor data without owning it; transient editor state lives on
// `EditorState`, not in module-level singletons.

open System
open System.Numerics
open Entity
open EditorState
open Raycast
open Command

// ── Docked panel cycling (Ctrl+Tab / Ctrl+Shift+Tab) ──

// The docked panels in focus order. The viewport is a passthru node, not an
// ImGui window, so it is intentionally absent from this list.
let private panelNames = [| "Outliner"; "Inspector"; "Asset Browser"; "Cast" |]

// Compute the index the focus should move to (forward/backward, wrapping).
let private nextPanelIndex (forward: bool) : int =
    let n = panelNames.Length
    let current = Array.tryFindIndex (fun (p: string) -> ImGui.isWindowActive p) panelNames
    match current with
    | None -> if forward then 0 else n - 1
    | Some i -> if forward then (i + 1) % n else (i - 1 + n) % n

// Apply a queued panel-focus request (valid only after ImGui.NewFrame), then
// clear it. Returns the updated state.
let private applyPendingFocus (state: byref<GameplayState.GameplayState>) : unit =
    match state.Editor.PendingFocus with
    | None -> ()
    | Some name ->
        ImGui.setWindowFocus name
        state.Editor <- { state.Editor with PendingFocus = None }

// ── Selection edits ──

// The selected entity's position + full transform, or None.
let private selectedTransform (state: GameplayState.GameplayState) : (Vector3 * Transform) option =
    match state.Editor.Selection with
    | None -> None
    | Some ref ->
    let entity = Zone.tryEntityAt (GameplayState.activeZone state) ref
    Option.map (fun e -> (e.Transform.Position, e.Transform)) entity

// Write a transform back to the selected entity.
let private setSelectedTransform (physics: Physics.World) (t: Transform) (state: byref<GameplayState.GameplayState>) : unit =
    match state.Editor.Selection with
    | None -> ()
    | Some ref ->
    Zone.setTransform (GameplayState.activeZone state) physics ref t


// Focus the camera on the selection (re-centre the target, preserve the offset).
let private focus (state: byref<GameplayState.GameplayState>) : unit =
    match state.Editor.Selection with
    | None -> ()
    | Some ref ->

    let entity = Zone.tryEntityAt (GameplayState.activeZone state) ref
    match entity with
    | None -> ()
    | Some e ->

    let offset = state.Camera.Position - state.Camera.Target
    state.Camera <- { state.Camera with Target = e.Transform.Position; Position = e.Transform.Position + offset }

// ── Gizmo drag ──

// Derive the new transform for a drag, or None if the mouse ray doesn't
// intersect the drag plane. Move/scale project onto the camera-facing plane
// along the grabbed axis; rotate measures the angle around the axis.
let private dragTransform (t: Transform) (r: Ray) (d: Drag) (a: Vector3) (state: GameplayState.GameplayState) : Transform option =
    match state.Editor.Tool with
    | Move | Scale ->
        let normal = Camera.forward state.Camera
        let planeHit = planeIntersect r t.Position normal
        match planeHit with
        | None -> None
        | Some hit ->
        let proj = Vector3.Dot(hit - d.Grab, a)
        let proj = if state.Editor.Snap then MathF.Round(proj / 0.5f) * 0.5f else proj
        if state.Editor.Tool = Move then
            Some { d.Start with Position = d.Start.Position + a * proj }
        else
            Some { d.Start with Scale = d.Start.Scale + a * proj }
    | Rotate ->
        let (u, v) = Viewport.basis d.Axis
        let planeHit = planeIntersect r t.Position a
        match planeHit with
        | None -> None
        | Some hit ->
        let ang (p: Vector3) =
            let off = p - t.Position
            MathF.Atan2(Vector3.Dot(off, v), Vector3.Dot(off, u))
        let delta = ang hit - ang d.Grab
        let delta = if state.Editor.Snap then MathF.Round(delta / (MathF.PI / 12.0f)) * (MathF.PI / 12.0f) else delta
        let rotQ = Quaternion.CreateFromAxisAngle(a, delta)
        Some { d.Start with Rotation = Quaternion.Concatenate(rotQ, d.Start.Rotation) }

// Apply one frame of an in-progress gizmo drag.
let private applyDrag (physics: Physics.World) (viewportW: float32) (viewportH: float32) (x: float32) (y: float32) (state: byref<GameplayState.GameplayState>) : unit =
    match state.Editor.Selection, state.Editor.Drag with
    | Some _, Some d ->
        let sel = selectedTransform state
        match sel with
        | None -> ()
        | Some (_, t) ->
        let r = Viewport.ray state.Camera viewportW viewportH x y
        let a = Viewport.axisDir d.Axis
        let result = dragTransform t r d a state
        match result with
        | None -> ()
        | Some newT -> setSelectedTransform physics newT &state
    | _ -> ()

// End a drag: push one Transform command for the whole gesture (coalesced by the
// undo stack), using the pre-drag transform as `Before`.
let private finishDrag (state: byref<GameplayState.GameplayState>) : unit =
    let undo =
        match state.Editor.Selection, state.Editor.Drag with
        | Some ref, Some d ->
            let entity = Zone.tryEntityAt (GameplayState.activeZone state) ref
            match entity with
            | Some e -> UndoStack.push state.Editor.Undo (Transform (ref, d.Start, e.Transform))
            | None -> state.Editor.Undo
        | _ -> state.Editor.Undo
    state.Editor <- { state.Editor with Drag = None; Undo = undo }

// ── Keyboard nudge (Shift + WASD/QE) ──

// A camera-relative nudge direction mapped from a held key.
type private NudgeDir = Forward | Back | Left | Right | Up | Down

let private nudgeDirOfKey (key: Input.Key) : NudgeDir option =
    match key with
    | Input.Key.W -> Some Forward
    | Input.Key.S -> Some Back
    | Input.Key.A -> Some Left
    | Input.Key.D -> Some Right
    | Input.Key.Q -> Some Down
    | Input.Key.E -> Some Up
    | _ -> None

// Per-frame nudge steps (snap in parentheses), mirroring ocaml-game.
let private moveStep = 0.05f
let private rotateStep = 0.02f
let private scaleStep = 0.01f
let private moveSnap = 0.5f
let private rotateSnap = MathF.PI / 12.0f
let private scaleSnap = 0.25f

// Apply one frame of camera-relative nudge to a transform.
let private nudgeTransform (camera: Camera.Camera) (tool: Tool) (snap: bool) (t: Transform) (dirs: NudgeDir list) : Transform =
    let f = Camera.forward camera
    let r = Camera.right camera
    let u = Camera.up camera

    let dirVec (d: NudgeDir) : Vector3 =
        match d with
        | Forward -> f
        | Back -> -f
        | Right -> r
        | Left -> -r
        | Up -> u
        | Down -> -u

    match tool with
    | Move ->
        let step = if snap then moveSnap else moveStep
        let delta = dirs |> List.fold (fun acc d -> acc + dirVec d * step) Vector3.Zero
        { t with Position = t.Position + delta }
    | Rotate ->
        let step = if snap then rotateSnap else rotateStep
        dirs |> List.fold (fun (acc: Transform) d ->
            let axis, sign =
                match d with
                | Forward -> r, 1.0f
                | Back -> r, -1.0f
                | Left -> f, -1.0f
                | Right -> f, 1.0f
                | Down -> u, -1.0f
                | Up -> u, 1.0f
            let rotQ = Quaternion.CreateFromAxisAngle(axis, sign * step)
            { acc with Rotation = Quaternion.Concatenate(rotQ, acc.Rotation) }) t
    | Scale ->
        let step = if snap then scaleSnap else scaleStep
        let factor =
            dirs |> List.fold (fun acc d ->
                match d with
                | Forward -> acc * (1.0f + step)
                | Back -> acc / (1.0f + step)
                | _ -> acc) 1.0f
        { t with Scale = t.Scale * factor }

// Apply one frame of keyboard nudge to the selection (Shift + WASD/QE) and, on
// release, commit the whole held nudge as a single undo step. A no-op with no
// selection, a drag in progress, or text input focused.
let updateNudge (physics: Physics.World) (input: Input.Input) (state: byref<GameplayState.GameplayState>) : unit =
    let dirs =
        if Input.isShift input && not (ImGui.wantTextInput()) && state.Editor.Drag = None then
            List.choose (fun k -> if Input.isHeld k input then nudgeDirOfKey k else None)
                [ Input.Key.W; Input.Key.A; Input.Key.S; Input.Key.D; Input.Key.Q; Input.Key.E ]
        else []

    match dirs with
    | _ :: _ ->
        // First frame of a hold: capture the pre-nudge transform as the "before".
        match state.Editor.NudgeBefore with
        | Some _ -> ()
        | None ->
        match selectedTransform state with
        | None -> ()
        | Some (_, t) ->
        state.Editor <- { state.Editor with NudgeBefore = Some t }

        match selectedTransform state with
        | None -> ()
        | Some (_, t) ->

        let newT = nudgeTransform state.Camera state.Editor.Tool state.Editor.Snap t dirs
        setSelectedTransform physics newT &state
    | [] ->
        // No nudge key held: push one Transform command for the whole hold.
        match state.Editor.NudgeBefore with
        | None -> ()
        | Some before ->

        let undo =
            match state.Editor.Selection with
            | None -> state.Editor.Undo
            | Some ref ->

            let entity = Zone.tryEntityAt (GameplayState.activeZone state) ref
            match entity with
            | Some e -> UndoStack.push state.Editor.Undo (Transform (ref, before, e.Transform))
            | None -> state.Editor.Undo

        state.Editor <- { state.Editor with NudgeBefore = None; Undo = undo }

// ── Undo / redo ──

// Apply one undo/redo step and reconcile the selection: a swapped environment
// dangles a Part selection. Both the shortcut and the Edit menu route through here
// so the reconcile can't be missed.
let private undoStep (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    state.Editor <- { state.Editor with Undo = UndoStack.undo state.Editor.Undo (GameplayState.activeZone state) shared.Physics shared.Assets }
    do Selection.dropStalePartSelection &state

// Redo re-applies the most recently undone command to the zone it came from.
let private redoStep (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    state.Editor <- { state.Editor with Undo = UndoStack.redo state.Editor.Undo (GameplayState.activeZone state) shared.Physics shared.Assets }
    do Selection.dropStalePartSelection &state

// ── App-level shortcuts ──

// Apply one frame of app-level shortcuts. No-ops while a text field has focus.
// Returns the updated state and any navigation request (Escape → menu).
let handleShortcuts (shared: SharedState.SharedState) (input: Input.Input) (state: byref<GameplayState.GameplayState>) : Screen.ScreenTransition =
    if ImGui.wantTextInput() then
        Screen.ScreenTransition.None
    else

    let mutable transition = Screen.ScreenTransition.None

    // editor-only shortcuts (tool/snap/focus/delete/duplicate)
    if state.Editing then
        // tool switch: 1/2/3
        if Input.isPressed (Input.Key.Digit 1) input then state.Editor <- { state.Editor with Tool = Move }
        if Input.isPressed (Input.Key.Digit 2) input then state.Editor <- { state.Editor with Tool = Rotate }
        if Input.isPressed (Input.Key.Digit 3) input then state.Editor <- { state.Editor with Tool = Scale }

        // snap toggle: Q (not while right-dragging — reserves Q for camera fly —
        // or while Shift is held — reserves Q for keyboard nudge)
        if Input.isPressed Input.Key.Q input && not input.RightDown && not (Input.isShift input) then
            state.Editor <- { state.Editor with Snap = not state.Editor.Snap }

        // focus / delete / duplicate
        if Input.isPressed Input.Key.F input then focus &state
        if Input.isPressed Input.Key.Delete input then Selection.delete shared &state
        if Input.isCtrl input && Input.isPressed Input.Key.D input then Selection.duplicate shared &state

        // undo / redo (blocked mid-drag so an in-progress gesture isn't corrupted)
        if state.Editor.Drag = None then
            if Input.isCtrl input && Input.isPressed Input.Key.Z input && not (Input.isShift input) then
                do undoStep shared &state
            if Input.isCtrl input && Input.isPressed Input.Key.Y input then
                do redoStep shared &state

    // Escape returns to the main menu
    if Input.isPressed Input.Key.Escape input then
        transition <- Screen.ScreenTransition.Next Screen.ScreenKind.MainMenu

    // Ctrl+Tab / Ctrl+Shift+Tab cycle docked panel focus
    if Input.isCtrl input && Input.isPressed Input.Key.Tab input then
        let next = nextPanelIndex (not (Input.isShift input))
        state.Editor <- { state.Editor with PendingFocus = Some panelNames.[next] }

    transition

// ── Viewport selection + gizmo interaction ──

// On a left press (not ImGui-captured): begin a gizmo drag when the cursor hits
// a handle on the selection, otherwise pick an entity (a miss clears the selection).
let private beginDragOrPick (physics: Physics.World) (input: Input.Input) (viewportW: float32) (viewportH: float32) (state: byref<GameplayState.GameplayState>) : unit =
    let sel = selectedTransform state
    match sel with
    | None -> state.Editor <- { state.Editor with Selection = Viewport.pick physics state viewportW viewportH input.MouseX input.MouseY }
    | Some (center, transform) ->

    let r = Viewport.ray state.Camera viewportW viewportH input.MouseX input.MouseY
    match Viewport.gizmoAxisAt state r center with
    | None -> state.Editor <- { state.Editor with Selection = Viewport.pick physics state viewportW viewportH input.MouseX input.MouseY }
    | Some axis ->

    let normal = if state.Editor.Tool = Rotate then Viewport.axisDir axis else Camera.forward state.Camera
    let grab = match planeIntersect r center normal with Some p -> p | None -> center
    state.Editor <- { state.Editor with Drag = Some { Axis = axis; Start = transform; Grab = grab } }

// Handle one frame of left-button interaction: begin a drag/pick on press, apply
// the drag while held, and finish on release.
let private interact (physics: Physics.World) (input: Input.Input) (viewportW: float32) (viewportH: float32) (state: byref<GameplayState.GameplayState>) : unit =
    if input.LeftPressed && not (ImGui.wantCaptureMouse()) then
        do beginDragOrPick physics input viewportW viewportH &state
    else if state.Editor.Drag <> None && input.LeftDown then
        do applyDrag physics viewportW viewportH input.MouseX input.MouseY &state
    else if state.Editor.Drag <> None && input.LeftReleased then
        do finishDrag &state

// ── Camera controls (edit mode) ──

// Apply orbit/dolly/fly to the camera from this frame's input. Fly speed is
// per second (dt-scaled); orbit and dolly are event-driven and unscaled.
let private updateCamera (input: Input.Input) (dt: float32) (state: byref<GameplayState.GameplayState>) : unit =
    if input.RightDown then
        Camera.orbit &state.Camera (-input.MouseDX * 0.01f) (-input.MouseDY * 0.01f)

        let moving =
            not (Input.isShift input)
            && (Input.isHeld Input.Key.W input || Input.isHeld Input.Key.A input || Input.isHeld Input.Key.S input
                || Input.isHeld Input.Key.D input || Input.isHeld Input.Key.Q input || Input.isHeld Input.Key.E input)

        if moving then
            let f = Camera.forward state.Camera
            let r = Camera.right state.Camera
            let up = Vector3.UnitY
            let speed = 6.0f * dt
            let mutable delta = Vector3.Zero
            if Input.isHeld Input.Key.W input then delta <- delta + f * speed
            if Input.isHeld Input.Key.S input then delta <- delta - f * speed
            if Input.isHeld Input.Key.D input then delta <- delta + r * speed
            if Input.isHeld Input.Key.A input then delta <- delta - r * speed
            if Input.isHeld Input.Key.E input then delta <- delta + up * speed
            if Input.isHeld Input.Key.Q input then delta <- delta - up * speed
            do Camera.pan &state.Camera delta

    if input.Wheel <> 0.0f then
        Camera.dolly &state.Camera (MathF.Pow(0.9f, input.Wheel))

// ── Per-frame update ──

// Advance the editor for one frame: the edit/play toggle, shortcuts, nudge,
// viewport interaction, and the camera. Called by GameplayScreen each frame.
let update (shared: SharedState.SharedState) (frame: Screen.Frame) (state: byref<GameplayState.GameplayState>) : Screen.ScreenTransition =
    // Backtick toggles edit/play, unless the user is typing in a text field.
    if Input.isPressed Input.Key.Backtick frame.Input && not (ImGui.wantTextInput()) then
        state.Editing <- not state.Editing

    if not state.Editing then Screen.ScreenTransition.None else

    let transition = handleShortcuts shared frame.Input &state

    // keyboard nudge (editor mode only)
    do updateNudge shared.Physics frame.Input &state
    // viewport selection + gizmo drag (editor mode only)
    do interact shared.Physics frame.Input frame.WindowW frame.WindowH &state
    do updateCamera frame.Input frame.Dt &state

    transition

// ── Docking workspace draw ──

// Draw the workspace. Returns the updated state (the dock layout is built once
// and remembered in the state) and any navigation request (File > Quit).
let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : Screen.ScreenTransition =
    if not state.Editing then Screen.ScreenTransition.None else
    // Apply any panel-focus request queued by update (valid only after NewFrame).
    do applyPendingFocus &state

    // Dockspace + (once) the default dock layout. The viewport is the central
    // passthru node; Outliner/Inspector/Asset Browser are docked around it.
    let dockspaceId = ImGui.dockSpaceOverViewport(0u, IntPtr.Zero, ImGui.passthruCentralNode, IntPtr.Zero)

    if not state.Editor.DockingInitialized then
        do ImGui.setupDockLayout(dockspaceId)
        state.Editor <- { state.Editor with DockingInitialized = true }

    let mutable transition = Screen.ScreenTransition.None

    if ImGui.beginMainMenuBar() then
        if ImGui.beginMenu("File", true) then
            if ImGui.menuItem("Quit", null, false, true) then
                transition <- Screen.ScreenTransition.Quit
            do ImGui.endMenu()

        // Edit menu: undo/redo mirror the keyboard shortcuts and are gated on the
        // same conditions (not mid-drag, not typing, and a non-empty stack).
        if ImGui.beginMenu("Edit", true) then
            let idle = state.Editor.Drag = None && not (ImGui.wantTextInput())
            if ImGui.menuItem("Undo", "Ctrl+Z", false, idle && not (List.isEmpty state.Editor.Undo.Applied)) then
                do undoStep shared &state
            if ImGui.menuItem("Redo", "Ctrl+Y", false, idle && not (List.isEmpty state.Editor.Undo.Redo)) then
                do redoStep shared &state
            do ImGui.endMenu()

        do ImGui.endMainMenuBar()

    do Outliner.draw shared &state
    do Inspector.draw shared &state
    do AssetBrowser.draw shared &state
    do CastPanel.draw shared

    transition
