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

// Compose a model matrix from a transform. System.Numerics is row-vector
// (`v * M`), so TRS is `S * R * T` (scale, rotate, translate).
let modelMatrix (t: Transform) : Matrix4x4 =
    Matrix4x4.CreateScale(t.Scale) * Matrix4x4.CreateFromQuaternion(t.Rotation) * Matrix4x4.CreateTranslation(t.Position)

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

// Collider shape data kept on an entity so a transform change can rebuild the
// body without re-reading the asset. Props/environment parts always collide as
// mesh geometry and actors always as a capsule, so each entity stores only its
// own shape, optionally (None = no collider).

// Prop/environment-part geometry: node-local flat vertex positions (xyz
// triplets) and triangle indices, unscaled; the body build bakes the entity
// scale into the vertices.
[<Struct>]
type MeshCollider =
    { Positions : float32 array
      Indices : int array }

// Actor capsule: horizontal radius and vertical half-height.
[<Struct>]
type CapsuleCollider =
    { Radius : float32
      HalfHeight : float32 }

// An entity's physics attachment: the collider shape plus the body built from
// it, held together so a body can never exist without its collider. `None`
// means no collider.
[<Struct>]
type PhysicsAttachment<'Collider> =
    { Collider : 'Collider
      Body : Physics.BodyId }

// Specialised entities compose the base via a nested field (no struct
// inheritance in F#). Actor/Environment/Prop share the base; Waypoint/Trigger
// are standalone.

// The asset binding carried by undo commands: which model to (re)create — an
// actor id, a prop id, or `None` for an environment part — and, for parts, the
// submesh range to draw.
[<Struct>]
type AssetBinding =
    { AssetId : string option
      SubmeshIndex : int
      SubmeshCount : int }

[<Struct>]
type Actor =
    { Base : Entity
      AssetId : string
      Physics : PhysicsAttachment<CapsuleCollider> option
      ShowCollider : bool
      mutable Renderer : ActorRender.ActorRender }

module Actor =
    // Release an actor's renderer GPU buffers.
    let releaseRenderer (a: Actor) : unit = ActorRender.release a.Renderer

// A placed prop asset (`AssetId = Some id`, whole model) or an environment part
// (`AssetId = None`, the part's submesh range in the zone's environment).
[<Struct>]
type Prop =
    { Base : Entity
      AssetId : string option
      SubmeshIndex : int
      SubmeshCount : int
      Physics : PhysicsAttachment<MeshCollider> option
      mutable Renderer : EnvironmentRender.EnvironmentRender }

// The zone's loaded environment asset. The environment is optional on the zone
// (`Environment option`); when present it always has an asset id.
[<Struct>]
type Environment =
    { Base : Entity
      AssetId : string
      Orientation : Quaternion }

module Prop =
    // Release a prop's renderer GPU buffers.
    let releaseRenderer (p: Prop) : unit = EnvironmentRender.release p.Renderer

[<Struct>]
type Waypoint =
    { Position : Vector3 }

[<Struct>]
type Trigger =
    { Position : Vector3 }
