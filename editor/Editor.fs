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

// ── Docked panel cycling (Ctrl+Tab / Ctrl+Shift+Tab) ──

// The docked panels in focus order. The viewport is a passthru node, not an
// ImGui window, so it is intentionally absent from this list.
let private panelNames = [| "Outliner"; "Inspector"; "Asset Browser" |]

// Compute the index the focus should move to (forward/backward, wrapping).
let private nextPanelIndex (forward: bool) : int =
    let n = panelNames.Length
    let current = Array.tryFindIndex (fun (p: string) -> ImGui.isWindowActive p) panelNames
    match current with
    | Option.None -> if forward then 0 else n - 1
    | Option.Some i -> if forward then (i + 1) % n else (i - 1 + n) % n

// Apply a queued panel-focus request (valid only after ImGui.NewFrame), then
// clear it. Returns the updated state.
let private applyPendingFocus (state: GameplayState.GameplayState) : GameplayState.GameplayState =
    match state.Editor.PendingFocus with
    | None -> state
    | Some name ->
    do ImGui.setWindowFocus(name)
    { state with Editor = { state.Editor with PendingFocus = None } }

// ── Selection edits ──

// The selected entity's position + full transform, or None.
let private selectedTransform (state: GameplayState.GameplayState) : (Vector3 * Transform) option =
    match state.Editor.Selection with
    | None -> None
    | Some ref ->
    let entity = Zone.entityAt state.Zone ref
    Option.map (fun (e: Entity) -> (e.Transform.Position, e.Transform)) entity

// Write a transform back to the selected entity.
let private setSelectedTransform (state: GameplayState.GameplayState) (t: Transform) : GameplayState.GameplayState =
    match state.Editor.Selection with
    | None -> state
    | Some ref ->

    let entity = Zone.entityAt state.Zone ref
    match entity with
    | Some e -> { state with Zone = Zone.setEntity state.Zone ref { e with Transform = t } }
    | None -> state

// Focus the camera on the selection (re-centre the target, preserve the offset).
let private focus (state: GameplayState.GameplayState) : GameplayState.GameplayState =
    match state.Editor.Selection with
    | None -> state
    | Some ref ->

    let entity = Zone.entityAt state.Zone ref
    match entity with
    | None -> state
    | Some e ->

    let offset = state.Camera.Position - state.Camera.Target
    { state with Camera = { state.Camera with Target = e.Transform.Position; Position = e.Transform.Position + offset } }

// ── Gizmo drag ──

// Derive the new transform for a drag, or None if the mouse ray doesn't
// intersect the drag plane. Move/scale project onto the camera-facing plane
// along the grabbed axis; rotate measures the angle around the axis.
let private dragTransform (state: GameplayState.GameplayState) (t: Transform) (r: Ray) (d: Drag) (a: Vector3) : Transform option =
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
let private applyDrag (state: GameplayState.GameplayState) (x: float32) (y: float32) : GameplayState.GameplayState =
    match (state.Editor.Selection, state.Editor.Drag) with
    | Some _, Some d ->
        let sel = selectedTransform state
        match sel with
        | None -> state
        | Some (_, t) ->
        let r = Viewport.ray state x y
        let a = Viewport.axisDir d.Axis
        let result = dragTransform state t r d a
        match result with
        | None -> state
        | Some newT -> setSelectedTransform state newT
    | _ -> state

// End a drag (2d will also push an undo command here using `Drag.Start`).
let private finishDrag (state: GameplayState.GameplayState) : GameplayState.GameplayState =
    { state with Editor = { state.Editor with Drag = None } }

// ── Keyboard nudge (Shift + WASD/QE) ──

// A camera-relative nudge direction mapped from a held key.
type private NudgeDir = Forward | Back | Left | Right | Up | Down

let private nudgeDirOfKey (key: Screen.Key) : NudgeDir option =
    match key with
    | Screen.Key.W -> Some Forward
    | Screen.Key.S -> Some Back
    | Screen.Key.A -> Some Left
    | Screen.Key.D -> Some Right
    | Screen.Key.Q -> Some Down
    | Screen.Key.E -> Some Up
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

// Apply one frame of keyboard nudge to the selection (Shift + WASD/QE); a no-op
// with no selection, a drag in progress, or text input focused.
let updateNudge (state: GameplayState.GameplayState) (input: Screen.Input) : GameplayState.GameplayState =
    let dirs =
        if Screen.Input.isShift input && not (ImGui.wantTextInput()) && state.Editor.Drag = None then
            List.choose (fun k -> if Screen.Input.isHeld k input then nudgeDirOfKey k else None)
                [ Screen.Key.W; Screen.Key.A; Screen.Key.S; Screen.Key.D; Screen.Key.Q; Screen.Key.E ]
        else []

    match dirs with
    | [] -> state
    | _ ->

    let sel = selectedTransform state
    match sel with
    | None -> state
    | Some (_, t) ->

    let newT = nudgeTransform state.Camera state.Editor.Tool state.Editor.Snap t dirs
    setSelectedTransform state newT

// ── App-level shortcuts ──

// Apply one frame of app-level shortcuts. No-ops while a text field has focus.
// Returns the updated state and any navigation request (Escape → menu).
let handleShortcuts (state: GameplayState.GameplayState) (input: Screen.Input) : struct (GameplayState.GameplayState * Screen.ScreenTransition) =
    if ImGui.wantTextInput() then
        struct (state, Screen.ScreenTransition.None)
    else

    let mutable state = state
    let mutable transition = Screen.ScreenTransition.None

    // editor-only shortcuts (tool/snap/focus/delete/duplicate)
    if state.Editing then
        // tool switch: 1/2/3
        if Screen.Input.isPressed (Screen.Key.Digit 1) input then state <- { state with Editor = { state.Editor with Tool = Move } }
        if Screen.Input.isPressed (Screen.Key.Digit 2) input then state <- { state with Editor = { state.Editor with Tool = Rotate } }
        if Screen.Input.isPressed (Screen.Key.Digit 3) input then state <- { state with Editor = { state.Editor with Tool = Scale } }

        // snap toggle: Q (not while right-dragging — reserves Q for camera fly —
        // or while Shift is held — reserves Q for keyboard nudge)
        if Screen.Input.isPressed Screen.Key.Q input && not input.RightDown && not (Screen.Input.isShift input) then
            state <- { state with Editor = { state.Editor with Snap = not state.Editor.Snap } }

        // focus / delete / duplicate
        if Screen.Input.isPressed Screen.Key.F input then state <- focus state
        if Screen.Input.isPressed Screen.Key.Delete input then state <- Selection.delete state
        if Screen.Input.isCtrl input && Screen.Input.isPressed Screen.Key.D input then state <- Selection.duplicate state

    // Escape returns to the main menu
    if Screen.Input.isPressed Screen.Key.Escape input then
        transition <- Screen.ScreenTransition.Next Screen.ScreenKind.MainMenu

    // Ctrl+Tab / Ctrl+Shift+Tab cycle docked panel focus
    if Screen.Input.isCtrl input && Screen.Input.isPressed Screen.Key.Tab input then
        let next = nextPanelIndex (not (Screen.Input.isShift input))
        state <- { state with Editor = { state.Editor with PendingFocus = Some panelNames.[next] } }

    struct (state, transition)

// ── Viewport selection + gizmo interaction ──

// On a left press (not ImGui-captured): begin a gizmo drag when the cursor hits
// a handle on the selection, otherwise pick an entity (a miss clears the selection).
let private beginDragOrPick (state: GameplayState.GameplayState) (input: Screen.Input) : GameplayState.GameplayState =
    let sel = selectedTransform state
    match sel with
    | None -> { state with Editor.Selection = Viewport.pick state input.MouseX input.MouseY }
    | Some (center, transform) ->

    let r = Viewport.ray state input.MouseX input.MouseY
    match Viewport.gizmoAxisAt state r center with
    | None -> { state with Editor.Selection = Viewport.pick state input.MouseX input.MouseY }
    | Some axis ->

    let normal = if state.Editor.Tool = Rotate then Viewport.axisDir axis else Camera.forward state.Camera
    let grab = match planeIntersect r center normal with Some p -> p | None -> center
    { state with Editor = { state.Editor with Drag = Some { Axis = axis; Start = transform; Grab = grab } } }

// Handle one frame of left-button interaction: begin a drag/pick on press, apply
// the drag while held, and finish on release.
let interact (state: GameplayState.GameplayState) (input: Screen.Input) : GameplayState.GameplayState =
    if input.LeftPressed && not (ImGui.wantCaptureMouse()) then
        beginDragOrPick state input
    else if state.Editor.Drag <> None && input.LeftDown then
        applyDrag state input.MouseX input.MouseY
    else if state.Editor.Drag <> None && input.LeftReleased then
        finishDrag state
    else
        state

// ── Camera controls (edit mode) ──

// Apply orbit/dolly/fly to the camera from this frame's input. Fly speed is
// per second (dt-scaled); orbit and dolly are event-driven and unscaled.
let private updateCamera (state: GameplayState.GameplayState) (input: Screen.Input) (dt: float32) : GameplayState.GameplayState =
    let camera =
        if not input.RightDown then state.Camera else
        let c = Camera.orbit state.Camera (-input.MouseDX * 0.01f) (-input.MouseDY * 0.01f)
        let moving =
            not (Screen.Input.isShift input)
            && (Screen.Input.isHeld Screen.Key.W input || Screen.Input.isHeld Screen.Key.A input || Screen.Input.isHeld Screen.Key.S input
                || Screen.Input.isHeld Screen.Key.D input || Screen.Input.isHeld Screen.Key.Q input || Screen.Input.isHeld Screen.Key.E input)
        if not moving then c else
        let f = Camera.forward c
        let r = Camera.right c
        let up = Vector3.UnitY
        let speed = 6.0f * dt
        let mutable delta = Vector3.Zero
        if Screen.Input.isHeld Screen.Key.W input then delta <- delta + f * speed
        if Screen.Input.isHeld Screen.Key.S input then delta <- delta - f * speed
        if Screen.Input.isHeld Screen.Key.D input then delta <- delta + r * speed
        if Screen.Input.isHeld Screen.Key.A input then delta <- delta - r * speed
        if Screen.Input.isHeld Screen.Key.E input then delta <- delta + up * speed
        if Screen.Input.isHeld Screen.Key.Q input then delta <- delta - up * speed
        Camera.pan c delta

    let camera =
        if input.Wheel = 0.0f then camera else
        Camera.dolly camera (MathF.Pow(0.9f, input.Wheel))

    { state with Camera = camera }

// ── Per-frame update ──

// Advance the editor for one frame: the edit/play toggle, shortcuts, nudge,
// viewport interaction, and the camera. Called by GameplayScreen each frame.
let update (state: GameplayState.GameplayState) (input: Screen.Input) (dt: float32) : struct (GameplayState.GameplayState * Screen.ScreenTransition) =
    // Backtick toggles edit/play, unless the user is typing in a text field.
    let state =
        if Screen.Input.isPressed Screen.Key.Backtick input && not (ImGui.wantTextInput()) then
            { state with Editing = not state.Editing }
        else state

    let struct (state, transition) = handleShortcuts state input

    let state =
        if not state.Editing then state else
        // keyboard nudge (editor mode only)
        let state = updateNudge state input
        // viewport selection + gizmo drag (editor mode only)
        let state = interact state input
        let state = updateCamera state input dt
        state

    struct (state, transition)

// ── Docking workspace draw ──

// Draw the workspace. Returns the updated state (the dock layout is built once
// and remembered in the state) and any navigation request (File > Quit).
let draw (state: GameplayState.GameplayState) (shared: SharedState.SharedState) : struct (GameplayState.GameplayState * Screen.ScreenTransition) =
    // Apply any panel-focus request queued by update (valid only after NewFrame).
    let state = applyPendingFocus state

    // Dockspace + (once) the default dock layout. The viewport is the central
    // passthru node; Outliner/Inspector/Asset Browser are docked around it.
    let dockspaceId = ImGui.dockSpaceOverViewport(0u, IntPtr.Zero, ImGui.passthruCentralNode, IntPtr.Zero)

    let state =
        if state.DockingInitialized then state else
        do ImGui.setupDockLayout(dockspaceId)
        { state with DockingInitialized = true }

    let mutable transition = Screen.ScreenTransition.None

    if ImGui.beginMainMenuBar() then
        if ImGui.beginMenu("File", true) then
            if ImGui.menuItem("Quit", null, false, true) then
                transition <- Screen.ScreenTransition.Quit
            do ImGui.endMenu()
        do ImGui.endMainMenuBar()

    let state = Outliner.draw state
    do Inspector.draw state
    let state = AssetBrowser.draw state shared

    struct (state, transition)
