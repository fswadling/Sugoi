module Viewport

// The 3D viewport: renders the reference grid and the entity markers / active
// transform gizmo via DebugDraw, and provides screen-ray picking for selection and
// gizmo handles (ported from ocaml-game's lib/editor/viewport.ml). All of it is
// editor-only: the grid and the markers/gizmo are drawn while editing, and emptied
// while playing.

open System
open System.Numerics
open Entity
open EditorState
open Raycast

// ── Colors + constants (mirror ocaml-game) ──

let private white : DebugDraw.Color = { R = 1.0f; G = 1.0f; B = 1.0f; A = 1.0f }
let private selectedColor : DebugDraw.Color = { R = 1.0f; G = 0.8f; B = 0.0f; A = 1.0f }
let private colliderColor : DebugDraw.Color = { R = 0.2f; G = 1.0f; B = 0.6f; A = 1.0f }
let private triggerColor : DebugDraw.Color = { R = 0.2f; G = 0.6f; B = 1.0f; A = 1.0f }
let private xColor : DebugDraw.Color = { R = 1.0f; G = 0.2f; B = 0.2f; A = 1.0f }
let private yColor : DebugDraw.Color = { R = 0.2f; G = 1.0f; B = 0.2f; A = 1.0f }
let private zColor : DebugDraw.Color = { R = 0.3f; G = 0.4f; B = 1.0f; A = 1.0f }
// A chain link, distinct from the marker colours and from the gizmo axes.
let private linkColor : DebugDraw.Color = { R = 200.0f / 255.0f; G = 122.0f / 255.0f; B = 1.0f; A = 1.0f }

let private halfExtent = Vector3(0.25f, 0.25f, 0.25f)
let private gizmoLen = 0.75f
let private gizmoRadius = 0.75f
let private headLen = 0.25f
let private headRadius = 0.12f
let private hitThreshold = 0.1f
// Chain-link connector geometry: the segment midpoint is lifted so the line does not sit
// on the ground between two level waypoints, and the arrow head sits at that midpoint.
let private linkLift = 0.08f
let private linkBack = 0.15f
let private linkWing = 0.07f
// Below this, two waypoints count as coincident: there is no direction to draw, and
// normalising the delta would emit invalid vertices.
let private linkMinLength = 0.001f
let private tipSize = 0.05f
let private pickDistance = 1000.0f

// ── Gizmo axis helpers ──

let axisDir (axis: GizmoAxis) : Vector3 =
    match axis with
    | XAxis -> Vector3.UnitX
    | YAxis -> Vector3.UnitY
    | ZAxis -> Vector3.UnitZ

let private axisColor (axis: GizmoAxis) : DebugDraw.Color =
    match axis with
    | XAxis -> xColor
    | YAxis -> yColor
    | ZAxis -> zColor

// Two orthogonal vectors spanning the plane perpendicular to an axis (rotate rings).
let basis (axis: GizmoAxis) : Vector3 * Vector3 =
    match axis with
    | XAxis -> (Vector3.UnitY, Vector3.UnitZ)
    | YAxis -> (Vector3.UnitX, Vector3.UnitZ)
    | ZAxis -> (Vector3.UnitX, Vector3.UnitY)

// A circle in the plane spanned by (u, v), centred at `center`.
let private circle (dd: DebugDraw.DebugDraw) (center: Vector3) (radius: float32) (u: Vector3) (v: Vector3) (c: DebugDraw.Color) : DebugDraw.DebugDraw =
    let segments = 32
    let mutable dd = dd
    let mutable prev = None
    for i in 0 .. segments do
        let a = 2.0f * MathF.PI * float32 i / float32 segments
        let p = center + u * (radius * MathF.Cos a) + v * (radius * MathF.Sin a)
        match prev with
        | Some q -> dd <- DebugDraw.line dd q p c
        | None -> ()
        prev <- Some p
    dd

// ── Gizmo drawing ──

let drawGizmo (state: GameplayState.GameplayState) (dd: DebugDraw.DebugDraw) (center: Vector3) : DebugDraw.DebugDraw =
    let axes = [ XAxis; YAxis; ZAxis ]
    match state.Editor.Tool with
    | Move | Scale ->
        let mutable dd = dd
        for axis in axes do
            let d = axisDir axis
            let tip = center + d * gizmoLen
            dd <- DebugDraw.line dd center tip (axisColor axis)
            dd <-
                if state.Editor.Tool = Move then
                    DebugDraw.sphere dd tip tipSize (axisColor axis)
                else
                    DebugDraw.wireframeBox dd tip (Vector3(tipSize, tipSize, tipSize)) (axisColor axis)
        dd
    | Rotate ->
        let mutable dd = dd
        for axis in axes do
            let (u, v) = basis axis
            dd <- circle dd center gizmoRadius u v (axisColor axis)
        dd

// The selected entity's position (the gizmo center), or None.
let gizmoCenter (state: GameplayState.GameplayState) : Vector3 option =
    match state.Editor.Selection with
    | Some ref ->
    let entity = Zone.tryEntityAt (GameplayState.activeZone state) ref
    Option.map (fun e -> e.Transform.Position) entity
    | None -> None

// ── Reference grid (editor only) ──

// The grid is an editor-only aid (spec 093): rebuilt into its accumulator while editing, emptied while
// playing, so neither the grid nor the previous editing frame's vertices draw in play (FR-001, FR-002).
let drawGrid (state: byref<GameplayState.GameplayState>) : unit =
    state.DebugDraw <-
        if state.Editing then DebugDraw.clear state.DebugDraw |> DebugDraw.drawGrid
        else DebugDraw.clear state.DebugDraw

// ── Chain links ──

// One link: a line from its waypoint to the midpoint of the segment toward the next,
// plus a two-wing arrow head at that midpoint, so which way the chain runs is readable.
// Drawing only the first half is what distinguishes a link from a two-way relation.
let private drawLink (dd: DebugDraw.DebugDraw) (from: Vector3) (target: Vector3) : DebugDraw.DebugDraw =
    let delta = target - from
    let length = delta.Length()
    if length < linkMinLength then dd else

    let dir = delta / length
    let mid = from + delta * 0.5f + Vector3(0.0f, linkLift, 0.0f)
    let dd = DebugDraw.line dd from mid linkColor
    let side = Vector3.Cross(dir, Vector3.UnitY)
    let right = if side.Length() < linkMinLength then Vector3.UnitX else Vector3.Normalize side
    let back = mid - dir * linkBack
    let dd = DebugDraw.line dd mid (back + right * linkWing) linkColor
    DebugDraw.line dd mid (back - right * linkWing) linkColor

// ── Markers + gizmo (editor mode only; appends to the gizmo accumulator) ──

let buildLines (state: byref<GameplayState.GameplayState>) : unit =
    let mutable dd = DebugDraw.clear state.GizmoDraw
    let zone = GameplayState.activeZone state
    let selection = state.Editor.Selection

    zone.Actors.IteriAlive(fun i gen a ->
        let isSelected = selection = Some (actorRef i gen)
        let c = if isSelected then selectedColor else white
        dd <- DebugDraw.wireframeBox dd a.State.Base.Transform.Position halfExtent c
        if a.State.ShowCollider then
            match a.Physics with
            | Some ph -> dd <- DebugDraw.capsule dd (Zone.capsuleCenter a.State.Base.Transform ph.Collider) ph.Collider.HalfHeight ph.Collider.Radius colliderColor
            | None -> ())

    zone.Props.IteriAlive(fun i gen p ->
        let isSelected = selection = Some (propRef i gen)
        let c = if isSelected then selectedColor else white
        dd <- DebugDraw.wireframeBox dd p.State.Base.Transform.Position halfExtent c)

    match zone.Environment with
    | Some environment ->
        environment.Parts.IteriAlive(fun i gen p ->
            let isSelected = selection = Some (partRef environment.Epoch i gen)
            let c = if isSelected then selectedColor else white
            dd <- DebugDraw.wireframeBox dd p.State.Base.Transform.Position halfExtent c)
    | None -> ()

    zone.Waypoints.IteriAlive(fun i gen w ->
        let isSelected = selection = Some (waypointRef i gen)
        let c = if isSelected then selectedColor else white
        let rot = w.Base.Transform.Rotation
        let position = w.Base.Transform.Position
        dd <- DebugDraw.wireframeBox dd position halfExtent c
        // A shaft along the waypoint's facing (its rotation applied to -Z, the
        // camera-forward convention) so the orientation is visible and rotatable.
        // The head's barbs reuse the same rotation's X/Y axes, which stay exactly
        // perpendicular to the shaft (and avoid a degenerate cross product when
        // the waypoint faces straight up or down).
        let forward = Vector3.Transform(-Vector3.UnitZ, rot)
        let tip = position + forward * gizmoLen
        let basePoint = tip - forward * headLen
        let right = Vector3.Transform(Vector3.UnitX, rot) * headRadius
        let up = Vector3.Transform(Vector3.UnitY, rot) * headRadius
        dd <- DebugDraw.line dd position tip c
        dd <- DebugDraw.line dd tip (basePoint + right) c
        dd <- DebugDraw.line dd tip (basePoint - right) c
        dd <- DebugDraw.line dd tip (basePoint + up) c
        dd <- DebugDraw.line dd tip (basePoint - up) c

        // The link out of this waypoint, if it has one. Drawn per link, from the
        // waypoint that holds it, so a chain shows its order and its direction.
        match Zone.waypointNext zone (waypointRef i gen) with
        | Some next ->
            match Zone.tryEntityAt zone next with
            | Some target -> dd <- drawLink dd position target.Transform.Position
            | None -> ()
        | None -> ())

    zone.Triggers.IteriAlive(fun i gen t ->
        let isSelected = selection = Some (triggerRef i gen)
        let c = if isSelected then selectedColor else triggerColor
        let basePosition = t.State.Base.Transform.Position
        // The primitive takes a centre and a half-height; the volume's base is its position.
        let centre = basePosition + Vector3(0.0f, t.State.Height * 0.5f, 0.0f)
        dd <- DebugDraw.cylinder dd centre (t.State.Height * 0.5f) t.State.Radius c)

    match gizmoCenter state with
    | Some center -> dd <- drawGizmo state dd center
    | None -> ()

    state.GizmoDraw <- dd

// ── Picking ──

let ray (camera: Camera.Camera) (w: float32) (h: float32) (x: float32) (y: float32) : Ray =
    Raycast.rayFromScreen camera w h x y

let pick (physics: Physics.World) (state: GameplayState.GameplayState) (w: float32) (h: float32) (x: float32) (y: float32) : EntityRef option =
    let r = ray state.Camera w h x y

    // Raycast against the physics colliders and map the hit body back to its
    // entity via the zone's body→entity map.
    match Physics.raycast physics r.Origin (r.Dir * pickDistance) with
    | None -> None
    | Some (bodyId, _) ->

    match (GameplayState.activeZone state).BodyToEntity.TryGetValue(bodyId) with
    | true, ref -> Some ref
    | false, _ -> None

// Which gizmo axis (if any) the screen ray hits at the given center.
let gizmoAxisAt (state: GameplayState.GameplayState) (r: Ray) (center: Vector3) : GizmoAxis option =
    let axes = [ XAxis; YAxis; ZAxis ]

    let segHit (axis: GizmoAxis) =
        let tip = center + axisDir axis * gizmoLen
        Raycast.raySegmentDistance r center tip < hitThreshold

    let ringHit (axis: GizmoAxis) =
        let normal = axisDir axis
        match Raycast.planeIntersect r center normal with
        | None -> false
        | Some p ->

        let off = p - center
        abs (off.Length() - gizmoRadius) < hitThreshold

    let hit (axis: GizmoAxis) =
        if state.Editor.Tool = Rotate then ringHit axis else segHit axis

    List.tryFind hit axes
