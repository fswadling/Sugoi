module Viewport

// The 3D viewport: renders entity markers and the active transform gizmo via
// DebugDraw, and provides screen-ray picking for selection and gizmo handles
// (ported from ocaml-game's lib/editor/viewport.ml). The reference grid is
// drawn separately and always visible; markers/gizmo only while editing.

open System
open System.Numerics
open Entity
open EditorState
open Raycast

// ── Colors + constants (mirror ocaml-game) ──

let private white : DebugDraw.Color = { R = 1.0f; G = 1.0f; B = 1.0f; A = 1.0f }
let private selectedColor : DebugDraw.Color = { R = 1.0f; G = 0.8f; B = 0.0f; A = 1.0f }
let private xColor : DebugDraw.Color = { R = 1.0f; G = 0.2f; B = 0.2f; A = 1.0f }
let private yColor : DebugDraw.Color = { R = 0.2f; G = 1.0f; B = 0.2f; A = 1.0f }
let private zColor : DebugDraw.Color = { R = 0.3f; G = 0.4f; B = 1.0f; A = 1.0f }

let private halfExtent = Vector3(0.25f, 0.25f, 0.25f)
let private gizmoLen = 0.75f
let private gizmoRadius = 0.75f
let private hitThreshold = 0.1f
let private tipSize = 0.05f

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
    | Some ref -> Option.map (fun (e: Entity) -> e.Transform.Position) (Zone.entityAt state.Zone ref)
    | None -> None

// ── Reference grid (always visible) ──

let drawGrid (state: GameplayState.GameplayState) : GameplayState.GameplayState =
    let dd = DebugDraw.clear state.DebugDraw |> DebugDraw.drawGrid
    { state with DebugDraw = dd }

// ── Markers + gizmo (editor mode only; appends to the accumulator) ──

let buildLines (state: GameplayState.GameplayState) : GameplayState.GameplayState =
    let mutable dd = state.DebugDraw
    let zone = state.Zone

    SlotMap.iteriAlive zone.Actors (fun i a ->
        let isSelected = state.Editor.Selection = Some { Kind = EntityKind.Actor; Index = i }
        let c = if isSelected then selectedColor else white
        dd <- DebugDraw.wireframeBox dd a.Base.Transform.Position halfExtent c)

    SlotMap.iteriAlive zone.Environment.Props (fun i p ->
        let isSelected = state.Editor.Selection = Some { Kind = EntityKind.Prop; Index = i }
        let c = if isSelected then selectedColor else white
        dd <- DebugDraw.wireframeBox dd p.Base.Transform.Position halfExtent c)

    match gizmoCenter state with
    | Some center -> dd <- drawGizmo state dd center
    | None -> ()

    { state with DebugDraw = dd }

// ── Picking ──

let ray (state: GameplayState.GameplayState) (x: float32) (y: float32) : Ray =
    Raycast.rayFromScreen state.Camera state.WindowW state.WindowH x y

let pick (state: GameplayState.GameplayState) (x: float32) (y: float32) : EntityRef option =
    let r = ray state x y

    // Keep the nearest hit in a value-type accumulator: a struct tuple of
    // (distance, entity) inside a `voption`. No `Some`/tuple/`EntityRef` boxes,
    // so the fold allocates nothing; the lambda stays pure and inlinable.
    let consider (acc: struct (float32 * EntityRef) voption) (ref: EntityRef) (pos: Vector3) : struct (float32 * EntityRef) voption =
        match Raycast.intersectAabb r pos halfExtent with
        | None -> acc
        | Some t ->

        match acc with
        | ValueNone -> ValueSome (struct (t, ref))
        | ValueSome (struct (bt, _)) when t < bt -> ValueSome (struct (t, ref))
        | _ -> acc

    let bestActors =
        SlotMap.foldiAlive state.Zone.Actors ValueNone (fun acc i a -> consider acc { Kind = EntityKind.Actor; Index = i } a.Base.Transform.Position)

    let best =
        SlotMap.foldiAlive state.Zone.Environment.Props bestActors (fun acc i p -> consider acc { Kind = EntityKind.Prop; Index = i } p.Base.Transform.Position)

    match best with
    | ValueNone -> None
    | ValueSome (struct (_, ref)) -> Some ref

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
