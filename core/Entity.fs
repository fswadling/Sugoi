module Entity

// Entity vocabulary: kinds, references, and the shared base composed by
// specialised entities. Math uses System.Numerics (value types, no GC).

open System.Numerics

[<Struct>]
type Transform =
    { Position : Vector3
      Rotation : Quaternion
      Scale : Vector3 }

// Editor-only handles. These aren't hot-path data (systems scan slot maps
// directly), so plain reference types are fine here.
type EntityKind =
    | Actor
    | Environment
    | Prop
    | Waypoint
    | Trigger

type EntityRef =
    { Kind : EntityKind
      Index : int }

// "No entity" sentinel: any EntityRef with a negative Index. The Kind field is
// meaningless for the sentinel.
let NO_ENTITY : EntityRef = { Kind = EntityKind.Actor; Index = -1 }

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
type Environment =
    { Base : Entity }

[<Struct>]
type Prop =
    { Base : Entity }

[<Struct>]
type Waypoint =
    { Position : Vector3 }

[<Struct>]
type Trigger =
    { Position : Vector3 }
