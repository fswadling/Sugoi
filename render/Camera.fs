module Camera

// Look-at orbit camera (ported from ocaml-game/lib/camera.ml). A position +
// target + up, with a derived forward/right/up basis and view/projection
// matrices built from System.Numerics.

open System
open System.Numerics

[<Struct>]
type Camera =
    { mutable Position : Vector3
      mutable Target : Vector3
      Up : Vector3
      Fov : float32
      mutable Aspect : float32
      Near : float32
      Far : float32 }

let create (position: Vector3) (target: Vector3) : Camera =
    { Position = position
      Target = target
      Up = Vector3.UnitY
      Fov = MathF.PI / 3.0f
      Aspect = 16.0f / 9.0f
      Near = 0.1f
      Far = 1000.0f }

let forward (c: Camera) : Vector3 = Vector3.Normalize(c.Target - c.Position)
let right (c: Camera) : Vector3 = Vector3.Normalize(Vector3.Cross(forward c, c.Up))
let up (c: Camera) : Vector3 = Vector3.Cross(right c, forward c)

let view (c: Camera) : Matrix4x4 = Matrix4x4.CreateLookAt(c.Position, c.Target, c.Up)
let proj (c: Camera) : Matrix4x4 = Matrix4x4.CreatePerspectiveFieldOfView(c.Fov, c.Aspect, c.Near, c.Far)
let viewProjection (c: Camera) : Matrix4x4 = view c * proj c

// Orbit: yaw around the up axis + pitch around the right axis, rotating the
// position<->target offset about the target (distance preserved). Mutates the
// camera in place.
let orbit (c: byref<Camera>) (yaw: float32) (pitch: float32) : unit =
    let offset = c.Target - c.Position
    let r = right c
    let u = up c
    let yawQ = Quaternion.CreateFromAxisAngle(u, yaw)
    let pitchQ = Quaternion.CreateFromAxisAngle(r, pitch)
    let q = Quaternion.Concatenate(yawQ, pitchQ)
    let offset' = Vector3.Transform(offset, q)
    c.Target <- c.Position + offset'

// Dolly: scale the position<->target distance by factor (clamped to a minimum).
// Mutates the camera in place.
let dolly (c: byref<Camera>) (factor: float32) : unit =
    let offset = c.Position - c.Target
    let dist = offset.Length()
    let newDist = max 0.1f (dist * factor)
    let dir = offset / dist
    c.Position <- c.Target + dir * newDist

// Pan/fly: move position and target together by a world-space delta. Mutates the
// camera in place.
let pan (c: byref<Camera>) (delta: Vector3) : unit =
    c.Position <- c.Position + delta
    c.Target <- c.Target + delta
