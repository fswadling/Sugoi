module Entity

// Entity vocabulary: kinds, references, and the shared base composed by
// specialised entities. Math uses System.Numerics (value types, no GC).

open System
open System.Numerics
open SlotMap

[<Struct>]
type Transform =
    { Position : Vector3
      Rotation : Quaternion
      Scale : Vector3 }

// Identity transform: at the origin, no rotation, unit scale.
let transformIdentity : Transform =
    { Position = Vector3.Zero
      Rotation = Quaternion.Identity
      Scale = Vector3.One }

// Convert a rotation quaternion to ZYX Euler angles in degrees (for display).
let eulerDegrees (q: Quaternion) : Vector3 =
    let x, y, z, w = q.X, q.Y, q.Z, q.W
    let roll = MathF.Atan2(2.0f * (w * x + y * z), 1.0f - 2.0f * (x * x + y * y))
    let pitch = MathF.Asin(Math.Clamp(2.0f * (w * y - z * x), -1.0f, 1.0f))
    let yaw = MathF.Atan2(2.0f * (w * z + x * y), 1.0f - 2.0f * (y * y + z * z))
    let deg = 180.0f / MathF.PI
    Vector3(roll * deg, pitch * deg, yaw * deg)

// Editor-only handles. These aren't hot-path data (systems scan slot maps
// directly), so plain reference types are fine here.
type EntityKind =
    | Actor
    | Environment
    | Prop
    | Waypoint
    | Trigger

// A lightweight handle to an entity: its kind plus its slot-map index. Absence
// ("no entity") is represented with `EntityRef option`, so there is no sentinel
// value — the odin reference needed one because it has no option type. A value
// type, so the per-frame selection check in `Viewport.buildLines` and the pick
// fold stay allocation-free.
[<Struct>]
type EntityRef =
    { Kind : EntityKind
      Index : uint32 }

[<Struct>]
type Entity =
    { Name : string
      Transform : Transform
      Enabled : bool
      Visible : bool
      Selectable : bool
      Draggable : bool }

// Specialised entities compose the base via a nested field (no struct
// inheritance in F#). Actor/Environment/Prop share the base; Waypoint/Trigger
// are standalone.
[<Struct>]
type Actor =
    { Base : Entity }

[<Struct>]
type Prop =
    { Base : Entity }

[<Struct>]
type Environment =
    { Base : Entity
      Props : SlotMap<Prop> }

[<Struct>]
type Waypoint =
    { Position : Vector3 }

[<Struct>]
type Trigger =
    { Position : Vector3 }
