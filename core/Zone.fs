module Zone

// The world container for one playable area: actors plus the environment (which
// owns props). Waypoints and triggers land in a later phase. Heap-allocated (not
// a struct); its entity collections are fixed-size inline slot maps.

open Entity
open SlotMap

type Zone =
    { Name : string
      Actors : SlotMap<Actor>
      Environment : Environment }

let create () : Zone =
    let environmentBase : Entity =
        { Name = ""
          Transform = transformIdentity
          Enabled = true
          Visible = true
          Selectable = true
          Draggable = true }
    { Name = ""
      Actors = SlotMap.create<Actor> 64u
      Environment = { Base = environmentBase; Props = SlotMap.create<Prop> 512u } }
