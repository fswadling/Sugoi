module Maths

// Spline geometry (spec 054): a curve through points, and a turn through rotations, sampled at a global
// parameter. Pure and device-free — positions and rotations only — so it names no zone, screen, asset or
// device and is exercisable on its own. The cue's camera flight is its first caller (`state/CueEngine.fs`).

open System.Numerics

/// A Catmull-Rom segment through `p1`..`p2` (with `p0`/`p3` the neighbours), at `t` in [0, 1].
let catmullRom (p0: Vector3) (p1: Vector3) (p2: Vector3) (p3: Vector3) (t: float32) : Vector3 =
    let t2 = t * t
    let t3 = t2 * t
    let a = 2.0f * p1
    let b = (p2 - p0) * t
    let c = (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2
    let d = (3.0f * p1 - p0 - 3.0f * p2 + p3) * t3
    0.5f * (a + b + c + d)

/// The spline through `points` at global `t` in [0, 1], with the endpoints duplicated as neighbours.
let evalSpline (points: Vector3[]) (t: float) : Vector3 =
    let n = points.Length
    let tc = float32 (max 0.0 (min 1.0 t))
    let segCount = n - 1
    let f = tc * float32 segCount
    let mutable seg = int f
    if seg >= segCount then seg <- segCount - 1
    let localT = f - float32 seg
    let p1 = points.[seg]
    let p2 = points.[seg + 1]
    let p0 = if seg = 0 then p1 else points.[seg - 1]
    let p3 = if seg + 2 >= n then p2 else points.[seg + 2]
    catmullRom p0 p1 p2 p3 localT

/// The rotation midway through `rotations` at global `t` in [0, 1], slerped within its segment.
let evalRotation (rotations: Quaternion[]) (t: float) : Quaternion =
    let n = rotations.Length
    let tc = max 0.0 (min 1.0 t)
    let scaled = tc * float (n - 1)
    let mutable seg = int scaled
    if seg >= n - 1 then seg <- n - 2
    let localT = float32 (scaled - float seg)
    Quaternion.Slerp(rotations.[seg], rotations.[seg + 1], localT)

/// The sampled arc length of the spline through `points`, at `samplesPerSegment` samples a segment.
let pathLength (points: Vector3[]) (samplesPerSegment: int) : float =
    let total = (points.Length - 1) * samplesPerSegment
    let mutable prev = evalSpline points 0.0
    let mutable acc = 0.0
    for i in 1 .. total do
        let p = evalSpline points (float i / float total)
        acc <- acc + float (Vector3.Distance(prev, p))
        prev <- p
    acc
