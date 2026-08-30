module Raycast

// Minimal screen-ray math for viewport picking (ported from ocaml-game's
// lib/editor/raycast.ml). Pure and headless-testable: a ray is built from the
// camera and a mouse position, then tested against axis-aligned boxes, planes,
// and segments. Uses System.Numerics (value types, no GC).

open System
open System.Numerics

[<Struct>]
type Ray =
    { Origin : Vector3
      Dir : Vector3 }

// Build a world-space ray through the pixel (x, y) in a w x h window. The aspect
// is taken from the camera (kept in sync with w/h each frame by GameplayScreen).
let rayFromScreen (camera: Camera.Camera) (w: float32) (h: float32) (x: float32) (y: float32) : Ray =
    let ndcX = (2.0f * x / w) - 1.0f
    let ndcY = 1.0f - (2.0f * y / h)
    let f = Camera.forward camera
    let r = Camera.right camera
    let u = Camera.up camera
    let tanHalf = MathF.Tan(camera.Fov / 2.0f)
    let dir =
        f + (r * (ndcX * tanHalf * camera.Aspect)) + (u * (ndcY * tanHalf))
    { Origin = camera.Position
      Dir = Vector3.Normalize(dir) }

// Slab test: the nearest t along the ray that enters the box, or None.
let intersectAabb (ray: Ray) (center: Vector3) (half: Vector3) : float32 option =
    let minB = center - half
    let maxB = center + half
    let mutable tmin = Single.NegativeInfinity
    let mutable tmax = Single.PositiveInfinity
    let mutable ok = true

    let axis (o: float32) (d: float32) (lo: float32) (hi: float32) =
        if abs d < 1e-8f then
            if o < lo || o > hi then ok <- false
        else

        let t1 = (lo - o) / d
        let t2 = (hi - o) / d
        tmin <- max tmin (min t1 t2)
        tmax <- min tmax (max t1 t2)
        if tmin > tmax then ok <- false

    axis ray.Origin.X ray.Dir.X minB.X maxB.X
    axis ray.Origin.Y ray.Dir.Y minB.Y maxB.Y
    axis ray.Origin.Z ray.Dir.Z minB.Z maxB.Z
    if ok && tmax >= 0.0f then Some (max 0.0f tmin) else None

// Intersect the ray with the plane through `point` with `normal`; returns the
// hit point, or None if parallel or behind the ray origin.
let planeIntersect (ray: Ray) (point: Vector3) (normal: Vector3) : Vector3 option =
    let denom = Vector3.Dot(ray.Dir, normal)
    if abs denom < 1e-8f then None else
    let t = Vector3.Dot(point - ray.Origin, normal) / denom
    if t < 0.0f then None
    else Some (ray.Origin + ray.Dir * t)

// Distance from the ray to the segment [a; b] (for gizmo-handle picking).
let raySegmentDistance (ray: Ray) (a: Vector3) (b: Vector3) : float32 =
    let w0 = ray.Origin - a
    let seg = b - a
    let dir = ray.Dir
    let a2 = Vector3.Dot(seg, seg)
    let b2 = Vector3.Dot(dir, dir)
    let c = Vector3.Dot(seg, dir)
    let d = Vector3.Dot(seg, w0)
    let e = Vector3.Dot(dir, w0)
    let denom = (a2 * b2) - (c * c)

    let segPoint (t: float32) = a + seg * t
    let dist (p: Vector3) (q: Vector3) = (p - q).Length()

    let originToSegment () =
        let t = if a2 < 1e-8f then 0.0f else max 0.0f (min 1.0f (d / a2))
        dist ray.Origin (segPoint t)

    if abs denom < 1e-8f then
        originToSegment ()
    else

    let t = (d * b2 - e * c) / denom
    let t = max 0.0f (min 1.0f t)
    let s = (t * c - e) / b2
    if s < 0.0f then
        originToSegment ()
    else
        dist (ray.Origin + dir * s) (segPoint t)
