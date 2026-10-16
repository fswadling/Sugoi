module Entity

// Entity vocabulary: kinds, references, and the shared base composed by
// specialised entities. Math uses System.Numerics (value types, no GC).

open System
open System.Numerics
open SlotMap

[<Struct>]
type Transform =
    { mutable Position : Vector3
      mutable Rotation : Quaternion
      mutable Scale : Vector3 }

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
    | Part
    | Waypoint
    | Trigger

// A lightweight handle to an entity: kind, slot-map index, and the slot's
// generation. The generation makes a delete-then-reuse fail a lookup instead of
// aliasing the new occupant. `Epoch` scopes a Part ref to the environment that
// produced it (0 for other kinds), so a same-index part in another environment
// can't resolve. Absence is `EntityRef option`; a value type keeps the per-frame
// selection check allocation-free.
[<Struct>]
type EntityRef =
    { Kind : EntityKind
      Index : uint32
      Generation : uint32
      Epoch : uint32 }

// Constructors for the common ref kinds, so call sites can't forget the epoch.
let actorRef (index: uint32) (generation: uint32) : EntityRef =
    { Kind = EntityKind.Actor; Index = index; Generation = generation; Epoch = 0u }

let propRef (index: uint32) (generation: uint32) : EntityRef =
    { Kind = EntityKind.Prop; Index = index; Generation = generation; Epoch = 0u }

let partRef (epoch: uint32) (index: uint32) (generation: uint32) : EntityRef =
    { Kind = EntityKind.Part; Index = index; Generation = generation; Epoch = epoch }

let waypointRef (index: uint32) (generation: uint32) : EntityRef =
    { Kind = EntityKind.Waypoint; Index = index; Generation = generation; Epoch = 0u }

let triggerRef (index: uint32) (generation: uint32) : EntityRef =
    { Kind = EntityKind.Trigger; Index = index; Generation = generation; Epoch = 0u }

// The slot-map handle a ref names. The ref's kind/epoch select which map it resolves
// against; the handle itself is only the index + generation.
let handleOf (ref: EntityRef) : Handle =
    { Index = ref.Index; Generation = ref.Generation }

[<Struct>]
type Entity =
    { Name : string
      mutable Transform : Transform
      Enabled : bool
      Visible : bool
      Selectable : bool
      Draggable : bool }

// The base entity every spawned entity starts from: a name, a transform, and the four editor flags the
// panels author. `Spawning.baseEntity`, `Outliner.newEntity` and the cue's actor ops all build from here,
// so the literal exists once (spec 064).
let baseEntity (name: string) (transform: Transform) : Entity =
    { Name = name
      Transform = transform
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

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

// What the editor authors for an actor: everything a command must carry, and nothing an
// engine rebuilds.
[<Struct>]
type ActorState =
    { Base : Entity
      AssetId : string
      ShowCollider : bool }

// An actor is its authored state plus the resources derived from it. They are different
// types, so nothing recorded can hold a body or a renderer, and which half a site is
// touching is visible in the spelling: `State` is what a command records, and the two
// mutable fields beside it are what a restore rebuilds.
[<Struct>]
type Actor =
    { State : ActorState
      mutable Physics : PhysicsAttachment<CapsuleCollider> option
      mutable Renderer : ActorRender.ActorRender }

module ActorEntity =
    // Release an actor's renderer GPU buffers.
    let releaseRenderer (a: Actor) : unit = ActorRender.release a.Renderer

// A placed prop asset (`AssetId = Some id`, whole model) or an environment part
// (`AssetId = None`, the part's submesh range in the zone's environment).
[<Struct>]
type PropState =
    { Base : Entity
      AssetId : string option
      SubmeshIndex : int
      SubmeshCount : int }

// A prop is its authored state plus the resources derived from it, the same split an
// actor has: a command carries the state, a restore rebuilds the rest.
[<Struct>]
type Prop =
    { State : PropState
      mutable Physics : PhysicsAttachment<MeshCollider> option
      mutable Renderer : EnvironmentRender.EnvironmentRender }

// The zone's loaded environment (optional on the zone). It owns its parts slot
// map (one prop per mesh-bearing glTF node), so environment churn never touches
// the zone's placed-prop or actor maps. `Epoch` identifies this environment so a
// part ref can be checked against the current one.
//
// `PartsSource` (spec 053) records where the parts came from: the parse is off-thread, so an
// asset-authored environment starts with an empty parts map that is materialised when the asset
// lands, while a registered template or an undo/redo payload is already final. It lives here (not on
// the zone) so it rides the `SetEnvironment`/`ZoneContents` payload.
type PartsSource =
    | FromAsset    // the asset's parsed parts; materialise them when it lands
    | Authored     // final (a registered template, or an undo/redo payload)

[<Struct>]
type Environment =
    { Base : Entity
      AssetId : string
      Orientation : Quaternion
      Epoch : uint32
      Parts : SlotMap<Prop>
      PartsSource : PartsSource }

module Prop =
    // Release a prop's renderer GPU buffers.
    let releaseRenderer (p: Prop) : unit = EnvironmentRender.release p.Renderer

// A point of interest placed in a zone: where to stand (the base transform's
// position) and which way to face (its rotation). Composes the shared base entity
// — name, transform, and editor flags — like actors and props, so the editor can
// select, drag, and rotate it, but carries no renderer, collider, or asset binding.
// `Next` is the waypoint this one's chain continues at, in this zone: authored state,
// so it is carried by whichever operation records the waypoint and dies with it.
// `None` ends a chain.
[<Struct>]
type Waypoint =
    { Base : Entity
      Next : EntityRef option }

// A cylindrical volume that detects the player. The base transform's position is the
// centre of the cylinder's base, `Height` extends up from it, and `Radius` is its
// cross-section; the rotation exists so the volume can be aimed like any entity, but
// containment is radially symmetric and the scale is not the volume's size. Composes
// the shared base entity like actors, props and waypoints, and carries no renderer,
// collider, or asset binding. `Base.Enabled` is the trigger's disabled flag. `Cue` is
// the cue forked into the screen's running cue when the trigger fires (spec 058):
// authored state like the rest of the record, `Fin` when the trigger does nothing.
[<Struct>]
type TriggerState =
    { Base : Entity
      Radius : float32
      Height : float32
      Interactable : bool
      Prompt : string
      Cue : Cue.Cue }

// A trigger is its authored shape plus its runtime flag, which is neither authored nor
// derived: it is recomputed in place by the trigger system and never recorded.
[<Struct>]
type Trigger =
    { State : TriggerState
      mutable Triggered : bool }
